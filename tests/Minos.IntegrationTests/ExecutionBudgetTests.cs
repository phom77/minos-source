using Minos.Application.Evaluation;
using Minos.Infrastructure.Evaluation;
using Minos.JudgeWorker;
using Xunit;

namespace Minos.IntegrationTests;

public sealed class ExecutionBudgetTests
{
    private static ExecutionRequest Stage(int memory) => new(ExecutionStage.Run, [1], [],
        new(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), memory * 1024L * 1024, 8 * 1024 * 1024, 32, 1024, 1024));

    [Theory]
    [InlineData(0, 2048, 512)]
    [InlineData(17, 2048, 512)]
    [InlineData(2, 1024, 512)]
    [InlineData(2, 2048, 128)]
    public void Invalid_execution_budgets_fail_before_startup(int slots, int memory, int reserve) =>
        Assert.Throws<InvalidOperationException>(() => new ExecutionBudget(new(slots, memory, reserve)));

    [Fact]
    public void Host_validation_reserves_cpu_and_management_memory()
    {
        var options = new ExecutionBudgetOptions(2, 2048, 512);
        Assert.Throws<InvalidOperationException>(() => options.ValidateHost(2, 4L * 1024 * 1024 * 1024));
        Assert.Throws<InvalidOperationException>(() => new ExecutionBudgetOptions(1).ValidateHost(1, 4L * 1024 * 1024 * 1024));
        Assert.Throws<InvalidOperationException>(() => options.ValidateHost(4, 2L * 1024 * 1024 * 1024));
        options.ValidateHost(4, 4L * 1024 * 1024 * 1024);
        Assert.Throws<InvalidOperationException>(() => new JudgeWorkerOptions(17).Validate());
    }

    [Fact]
    public async Task Weighted_budget_preserves_fifo_and_cancellation_releases_only_its_waiter()
    {
        var token = TestContext.Current.CancellationToken;
        var budget = new ExecutionBudget(new(3, 1152));
        using var first = await budget.EnterAsync(Stage(1024), token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var large = budget.EnterAsync(Stage(256), cancellation.Token);
        var small = budget.EnterAsync(Stage(16), token);
        Assert.False(large.IsCompleted);
        Assert.False(small.IsCompleted); // No bypassing the older memory-heavy stage.
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => large);
        using var second = await small.WaitAsync(TimeSpan.FromSeconds(3), token);
        var stillTooLarge = budget.EnterAsync(Stage(256), token);
        Assert.False(stillTooLarge.IsCompleted);
        first.Dispose();
        using var third = await stillTooLarge.WaitAsync(TimeSpan.FromSeconds(3), token);
        first.Dispose(); // Double-dispose must not release another stage's reservation.
    }

    [Fact]
    public async Task Slot_is_held_until_cleanup_and_cleanup_failure_blocks_further_work()
    {
        var token = TestContext.Current.CancellationToken;
        var inner = new CleanupBackend();
        var budget = new ExecutionBudget(new(1, 1152));
        var backend = new BudgetedExecutionBackend(inner, budget);
        var first = backend.ExecuteAsync(Stage(64), token);
        await inner.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3), token);
        var second = backend.ExecuteAsync(Stage(64), token);
        Assert.False(second.IsCompleted);
        inner.Release.SetResult();
        Assert.False((await first).CleanupComplete);
        await Assert.ThrowsAsync<IOException>(() => second);
        await Assert.ThrowsAsync<IOException>(() => backend.ExecuteAsync(Stage(64), token));
        await Assert.ThrowsAsync<IOException>(() => new BudgetedJudgeJobDelivery(new NeverReceive(), budget).ReceiveAsync(token));
        Assert.Equal(1, inner.Calls);
    }

    [Theory]
    [InlineData(ExecutionStage.Compile)]
    [InlineData(ExecutionStage.Run)]
    public async Task A_full_stage_quota_does_not_block_the_other_stage_and_preserves_its_fifo(ExecutionStage stage)
    {
        var token = TestContext.Current.CancellationToken;
        var budget = new ExecutionBudget(new(3, 2048, CompileSlots: 1, RuntimeSlots: 1));
        var same = Stage(64) with { Stage = stage };
        var other = Stage(64) with { Stage = stage == ExecutionStage.Compile ? ExecutionStage.Run : ExecutionStage.Compile };
        using var active = await budget.EnterAsync(same, token);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
        var oldest = budget.EnterAsync(same, cancel.Token);
        var next = budget.EnterAsync(same, token);
        using var concurrent = await budget.EnterAsync(other, token).WaitAsync(TimeSpan.FromSeconds(3), token);
        Assert.False(oldest.IsCompleted); Assert.False(next.IsCompleted);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => oldest);
        Assert.False(next.IsCompleted);
        active.Dispose();
        using var resumed = await next.WaitAsync(TimeSpan.FromSeconds(3), token);
        var stillBlocked = budget.EnterAsync(same, token);
        Assert.False(stillBlocked.IsCompleted);
        active.Dispose();
        Assert.False(stillBlocked.IsCompleted);
        resumed.Dispose();
        using var final = await stillBlocked.WaitAsync(TimeSpan.FromSeconds(3), token);
    }

    [Fact]
    public async Task Separate_stage_quotas_still_respect_total_slots_and_memory()
    {
        var token = TestContext.Current.CancellationToken;
        var budget = new ExecutionBudget(new(2, 1152, CompileSlots: 2, RuntimeSlots: 2));
        using var compiler = await budget.EnterAsync(Stage(512) with { Stage = ExecutionStage.Compile }, token);
        var large = budget.EnterAsync(Stage(1024), token);
        var small = budget.EnterAsync(Stage(16), token);
        Assert.False(large.IsCompleted); Assert.False(small.IsCompleted);
        compiler.Dispose();
        using var runtime = await large.WaitAsync(TimeSpan.FromSeconds(3), token);
        using var second = await small.WaitAsync(TimeSpan.FromSeconds(3), token);
        var third = budget.EnterAsync(Stage(16) with { Stage = ExecutionStage.Compile }, token);
        Assert.False(third.IsCompleted);
        second.Dispose();
        using var granted = await third.WaitAsync(TimeSpan.FromSeconds(3), token);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(3, 1)]
    public void Invalid_stage_quotas_fail_before_execution(int compile, int run) =>
        Assert.Throws<InvalidOperationException>(() => new ExecutionBudget(new(2, CompileSlots: compile, RuntimeSlots: run)));

    private sealed class NeverReceive : IJudgeJobDelivery
    {
        public Task<Guid?> ReceiveAsync(CancellationToken token) => throw new InvalidOperationException("Unhealthy execution must not receive another job.");
    }

    private sealed class CleanupBackend : IExecutionBackend
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Calls;
        public async Task<ExecutionEvidence> ExecuteAsync(ExecutionRequest request, CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            Entered.TrySetResult();
            await Release.Task.WaitAsync(token);
            return new(null, false, false, false, false, false, TimeSpan.Zero, null, null, [], [], null, false, false, "FixtureCleanupFault");
        }
    }
}
