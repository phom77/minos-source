using System.Diagnostics;
using System.Diagnostics.Metrics;
using Minos.Application.Evaluation;

namespace Minos.Infrastructure.Evaluation;

public sealed record ExecutionBudgetOptions(int SandboxSlots = 2, int MemoryMegabytes = 2048, int ControllerReserveMegabytes = 512,
    int? CompileSlots = null, int? RuntimeSlots = null)
{
    internal int MaximumCompilers => CompileSlots ?? SandboxSlots;
    internal int MaximumRuntimes => RuntimeSlots ?? SandboxSlots;
    internal void Validate()
    {
        if (SandboxSlots is < 1 or > 16 || MemoryMegabytes is < 1152 or > 65536 || ControllerReserveMegabytes is < 256 or > 16384)
            throw new InvalidOperationException("Execution budget requires 1–16 sandbox slots, 1152–65536 MiB sandbox memory and 256–16384 MiB controller reserve.");
        if (MaximumCompilers < 1 || MaximumCompilers > SandboxSlots || MaximumRuntimes < 1 || MaximumRuntimes > SandboxSlots)
            throw new InvalidOperationException("Compile/runtime slots must be between one and the total sandbox slots.");
    }

    internal void ValidateHost(int cpus, long memoryBytes)
    {
        Validate();
        if (cpus < 2 || SandboxSlots > cpus - 1 || (MemoryMegabytes + (long)ControllerReserveMegabytes) * 1024 * 1024 > memoryBytes)
            throw new InvalidOperationException("The execution budget exceeds host CPU/memory capacity after management reserve.");
    }
}

// FIFO within each stage, oldest eligible stage first under shared CPU/memory bounds.
// A stage at its own concurrency cap cannot block the other stage. Cleanup retains the permit.
internal sealed class ExecutionBudget
{
    private static readonly Meter Meter = new("Minos.Judging");
    private static readonly UpDownCounter<long> Active = Meter.CreateUpDownCounter<long>("minos.judging.active_sandboxes");
    private static readonly UpDownCounter<long> ActiveStages = Meter.CreateUpDownCounter<long>("minos.judging.active_stages");
    private static readonly UpDownCounter<long> Reserved = Meter.CreateUpDownCounter<long>("minos.judging.reserved_memory", "By");
    private readonly object sync = new();
    private readonly LinkedList<Waiter> waiting = [];
    private readonly int slots;
    private readonly int compileSlots;
    private readonly int runtimeSlots;
    private readonly long memory;
    private int usedSlots;
    private int usedCompilers;
    private int usedRuntimes;
    private long usedMemory;
    private bool cleanupFault;

    internal ExecutionBudget(ExecutionBudgetOptions options)
    {
        options.Validate();
        slots = options.SandboxSlots;
        compileSlots = options.MaximumCompilers;
        runtimeSlots = options.MaximumRuntimes;
        memory = options.MemoryMegabytes * 1024L * 1024;
    }

    internal async Task<IDisposable> EnterAsync(ExecutionRequest request, CancellationToken token)
    {
        // Scratch is charged conservatively in addition to the sandbox's memory limit.
        var bytes = checked(request.Limits.MemoryBytes + request.Limits.ScratchBytes);
        if (bytes > memory) throw new InvalidOperationException("An execution stage exceeds the configured memory budget.");
        token.ThrowIfCancellationRequested();
        var waiter = new Waiter(bytes, request.Stage);
        lock (sync)
        {
            if (cleanupFault) throw new IOException("Sandbox cleanup failed; restart after host recovery before further execution.");
            waiter.Node = waiting.AddLast(waiter);
            Drain();
        }
        using var cancellation = token.Register(() =>
        {
            lock (sync)
            {
                if (waiter.Node?.List is null) return;
                waiting.Remove(waiter.Node);
                waiter.Ready.TrySetCanceled(token);
                Drain();
            }
        });
        var permit = await waiter.Ready.Task;
        if (!token.IsCancellationRequested) return permit;
        permit.Dispose();
        token.ThrowIfCancellationRequested();
        throw new UnreachableException();
    }

    private void Drain()
    {
        if (cleanupFault) return;
        while (usedSlots < slots)
        {
            var node = waiting.First;
            while (node is not null && (node.Value.Stage == ExecutionStage.Compile ? usedCompilers >= compileSlots : usedRuntimes >= runtimeSlots))
                node = node.Next;
            // Preserve memory fairness for the oldest stage eligible for its class quota.
            if (node is null || usedMemory + node.Value.Bytes > memory) break;
            waiting.Remove(node);
            usedSlots++; usedMemory += node.Value.Bytes;
            if (node.Value.Stage == ExecutionStage.Compile) usedCompilers++; else usedRuntimes++;
            Active.Add(1); Reserved.Add(node.Value.Bytes);
            ActiveStages.Add(1, new KeyValuePair<string, object?>("stage", node.Value.Stage == ExecutionStage.Compile ? "compile" : "run"));
            node.Value.Ready.SetResult(new Permit(this, node.Value.Bytes, node.Value.Stage));
        }
    }

    internal void StopAfterCleanupFailure()
    {
        lock (sync)
        {
            cleanupFault = true;
            foreach (var waiter in waiting)
                waiter.Ready.TrySetException(new IOException("Sandbox cleanup failed; further execution is blocked until host recovery."));
            waiting.Clear();
        }
    }

    internal void EnsureHealthy()
    {
        lock (sync)
            if (cleanupFault) throw new IOException("Sandbox cleanup failed; new claims are blocked until host recovery.");
    }

    private void Release(long bytes, ExecutionStage stage)
    {
        lock (sync)
        {
            usedSlots--; usedMemory -= bytes;
            if (stage == ExecutionStage.Compile) usedCompilers--; else usedRuntimes--;
            Active.Add(-1); Reserved.Add(-bytes);
            ActiveStages.Add(-1, new KeyValuePair<string, object?>("stage", stage == ExecutionStage.Compile ? "compile" : "run"));
            Drain();
        }
    }

    private sealed class Waiter(long bytes, ExecutionStage stage)
    {
        internal long Bytes { get; } = bytes;
        internal ExecutionStage Stage { get; } = stage;
        internal LinkedListNode<Waiter>? Node { get; set; }
        internal TaskCompletionSource<IDisposable> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class Permit(ExecutionBudget owner, long bytes, ExecutionStage stage) : IDisposable
    {
        private int disposed;
        public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) owner.Release(bytes, stage); }
    }
}

internal sealed class BudgetedJudgeJobDelivery(IJudgeJobDelivery delivery, ExecutionBudget budget) : IJudgeJobDelivery
{
    public Task<Guid?> ReceiveAsync(CancellationToken token)
    {
        budget.EnsureHealthy();
        return delivery.ReceiveAsync(token);
    }
}

internal sealed class BudgetedExecutionBackend(IExecutionBackend inner, ExecutionBudget budget) : IExecutionBackend
{
    public async Task<ExecutionEvidence> ExecuteAsync(ExecutionRequest request, CancellationToken token)
    {
        DockerExecutionBackend.Validate(request);
        IDisposable permit;
        using (JudgingMetrics.Measure(request.Stage == ExecutionStage.Compile ? "compile" : "run", "budget-wait"))
            permit = await budget.EnterAsync(request, token);
        using (permit)
        {
            ExecutionEvidence evidence;
            try { evidence = await inner.ExecuteAsync(request, token); }
            // Both adapters throw cancellation before creating a sandbox; started stages return cleanup evidence.
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch { budget.StopAfterCleanupFailure(); throw; }
            if (!evidence.CleanupComplete) budget.StopAfterCleanupFailure();
            return evidence;
        }
    }
}
