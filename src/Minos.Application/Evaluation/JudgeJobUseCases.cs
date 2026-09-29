using Minos.Domain.Evaluation;

namespace Minos.Application.Evaluation;

public sealed class JudgeJobUseCases(
    IJudgeJobRepository jobs,
    ISubmissionExecutor executor,
    TimeProvider clock)
{
    public Task<ClaimedSubmissionWork?> ClaimAsync(string workerId, CancellationToken token)
    {
        ValidateWorker(workerId);
        return jobs.ClaimNextAsync(workerId, clock.GetUtcNow(), token);
    }

    public Task<LeaseMutationResult> RenewAsync(ClaimedSubmissionWork work, CancellationToken token) =>
        jobs.RenewAsync(work.AttemptId, work.LeaseToken, work.WorkerId, clock.GetUtcNow(), token);

    public async Task<LeaseMutationResult> ProcessAsync(ClaimedSubmissionWork work, CancellationToken token)
    {
        DeterministicExecutionResult result;
        try { result = await executor.ExecuteAsync(work, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch
        {
            var disposition = await jobs.FailAsync(work.AttemptId, work.LeaseToken, work.WorkerId,
                InfrastructureFailureKind.WorkerFailure, clock.GetUtcNow(), CancellationToken.None);
            return disposition == AttemptFailureResult.Stale ? LeaseMutationResult.Stale : LeaseMutationResult.Accepted;
        }

        if (result.Outcome == DeterministicExecutionOutcome.Succeeded)
            return await jobs.CompleteAsync(work.AttemptId, work.LeaseToken, work.WorkerId, clock.GetUtcNow(), token);

        var failure = await jobs.FailAsync(work.AttemptId, work.LeaseToken, work.WorkerId,
            result.FailureKind ?? InfrastructureFailureKind.ExecutorFailure, clock.GetUtcNow(), token);
        return failure == AttemptFailureResult.Stale ? LeaseMutationResult.Stale : LeaseMutationResult.Accepted;
    }

    private static void ValidateWorker(string? workerId)
    {
        if (string.IsNullOrWhiteSpace(workerId) || workerId.Length > 128)
            throw new ArgumentOutOfRangeException(nameof(workerId));
    }
}
