using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Minos.Application.Evaluation;
using Minos.Domain.Evaluation;
using Minos.JudgeWorker;
using Xunit;

namespace Minos.IntegrationTests;

public sealed class WorkerSchedulingTests
{
    [Fact]
    public async Task Duplicate_delivery_and_bounded_jobs_use_independent_scopes_without_duplicate_processing()
    {
        var state = new State(4);
        state.Deliveries.Enqueue(state.Ids[0]);
        foreach (var id in state.Ids) state.Deliveries.Enqueue(id);
        await using var services = Services(state);
        using var worker = Worker(services, 2);
        await worker.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await Until(() => state.Starts.Count == 2);
            Assert.Equal(2, state.Claimed.Count);
            Assert.Equal(2, state.MaximumActive);
            state.Release.SetResult();
            await Until(() => state.Completed.Count == 4);
        }
        finally { await worker.StopAsync(CancellationToken.None); }
        Assert.Equal(4, state.Starts.Count);
        Assert.Equal(4, state.Starts.Values.Select(marker => marker.Id).Distinct().Count());
        Assert.All(state.Starts.Values, marker => Assert.True(marker.Disposed));
        Assert.True(state.MaximumActive <= 2);
    }

    [Fact]
    public async Task Shutdown_waits_for_cancelled_job_cleanup_before_disposing_its_scope()
    {
        var state = new State(1, blockCleanup: true);
        state.Deliveries.Enqueue(state.Ids[0]);
        await using var services = Services(state);
        using var worker = Worker(services, 1);
        await worker.StartAsync(TestContext.Current.CancellationToken);
        await Until(() => state.Starts.Count == 1);
        var stopped = worker.StopAsync(CancellationToken.None);
        await state.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        try
        {
            Assert.False(stopped.IsCompleted);
            Assert.False(state.Starts.Single().Value.Disposed);
            Assert.Empty(state.Completed);
        }
        finally { state.Cleanup.SetResult(); await stopped; }
        Assert.True(state.Starts.Single().Value.Disposed);
    }

    [Fact]
    public async Task Lost_lease_cancels_only_that_attempt_and_awaits_cleanup()
    {
        var state = new State(2) { StaleId = 0 };
        foreach (var id in state.Ids) state.Deliveries.Enqueue(id);
        await using var services = Services(state);
        using var worker = Worker(services, 2);
        await worker.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await Until(() => state.Starts.Count == 2);
            await state.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            Assert.True(state.Renewals > 0);
            Assert.False(state.Starts[state.Ids[1]].Disposed);
            state.Release.SetResult();
            await Until(() => state.Completed.Count == 1);
            Assert.Contains(state.Ids[1], state.Completed.Keys);
            Assert.DoesNotContain(state.Ids[0], state.Completed.Keys);
        }
        finally { state.Release.TrySetResult(); await worker.StopAsync(CancellationToken.None); }
    }

    private static SubmissionJobWorker Worker(ServiceProvider services, int jobs) =>
        new(services.GetRequiredService<IServiceScopeFactory>(), new(jobs), NullLogger<SubmissionJobWorker>.Instance);

    private static ServiceProvider Services(State state)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IJudgeJobRepository>(state);
        services.AddSingleton<IJudgeJobDelivery>(state);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ScopeMarker>();
        services.AddScoped<ISubmissionExecutor>(provider => new Executor(state, provider.GetRequiredService<ScopeMarker>()));
        services.AddScoped<JudgeJobUseCases>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static async Task Until(Func<bool> condition)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, deadline.Token);
    }

    private sealed class ScopeMarker : IDisposable
    {
        internal Guid Id { get; } = Guid.NewGuid();
        internal bool Disposed;
        public void Dispose() => Disposed = true;
    }

    private sealed class Executor(State state, ScopeMarker marker) : ISubmissionExecutor
    {
        public async Task<SubmissionExecutionResult> ExecuteAsync(ClaimedSubmissionWork work, CancellationToken token)
        {
            state.Starts.TryAdd(work.JobId, marker);
            var active = Interlocked.Increment(ref state.Active);
            lock (state) state.MaximumActive = Math.Max(active, state.MaximumActive);
            try
            {
                await state.Release.Task.WaitAsync(token);
                Assert.False(marker.Disposed);
                return SubmissionExecutionResult.Completed(JudgingOutcome.Create(SubmissionVerdict.Accepted, "",
                    [new(1, TestCaseResultStatus.Passed, 1, 1, 1024, 0)]));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { state.Cancelled.TrySetResult(); throw; }
            finally
            {
                await state.Cleanup.Task;
                Assert.False(marker.Disposed);
                Interlocked.Decrement(ref state.Active);
            }
        }
    }

    private sealed class State : IJudgeJobRepository, IJudgeJobDelivery
    {
        internal State(int count, bool blockCleanup = false)
        {
            Ids = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();
            if (!blockCleanup) Cleanup.SetResult();
        }
        internal Guid[] Ids;
        internal int StaleId = -1, Active, MaximumActive, Renewals;
        internal ConcurrentQueue<Guid> Deliveries { get; } = new();
        internal ConcurrentDictionary<Guid, ClaimedSubmissionWork> Claimed { get; } = new();
        internal ConcurrentDictionary<Guid, ScopeMarker> Starts { get; } = new();
        internal ConcurrentDictionary<Guid, bool> Completed { get; } = new();
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Cleanup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<Guid?> ReceiveAsync(CancellationToken token) => Task.FromResult<Guid?>(Deliveries.TryDequeue(out var id) ? id : null);
        public Task<ClaimedSubmissionWork?> ClaimNextAsync(string workerId, DateTimeOffset nowUtc, CancellationToken token) => throw new NotSupportedException();
        public Task<ClaimedSubmissionWork?> ClaimAsync(Guid id, string workerId, DateTimeOffset nowUtc, CancellationToken token)
        {
            var work = new ClaimedSubmissionWork(id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), workerId,
                nowUtc.AddSeconds(30), Guid.NewGuid(), SupportedSubmission.Language, SupportedSubmission.ToolchainId, "fixture");
            return Task.FromResult<ClaimedSubmissionWork?>(Claimed.TryAdd(id, work) ? work : null);
        }
        public Task<LeaseMutationResult> RenewAsync(Guid attemptId, Guid leaseToken, string workerId, DateTimeOffset nowUtc, CancellationToken token)
        {
            Interlocked.Increment(ref Renewals);
            var stale = StaleId >= 0 && Claimed[Ids[StaleId]].AttemptId == attemptId;
            return Task.FromResult(stale ? LeaseMutationResult.Stale : LeaseMutationResult.Accepted);
        }
        public Task<LeaseMutationResult> CompleteAsync(Guid attemptId, Guid leaseToken, string workerId, JudgingOutcome outcome, DateTimeOffset nowUtc, CancellationToken token)
        {
            var work = Claimed.Values.Single(work => work.AttemptId == attemptId);
            Completed.TryAdd(work.JobId, true);
            return Task.FromResult(LeaseMutationResult.Accepted);
        }
        public Task<AttemptFailureResult> FailAsync(Guid attemptId, Guid leaseToken, string workerId, InfrastructureFailureKind failure, DateTimeOffset nowUtc, CancellationToken token) => throw new NotSupportedException();
    }
}
