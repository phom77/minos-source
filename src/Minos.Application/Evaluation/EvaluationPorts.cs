using Minos.Domain.Evaluation;

namespace Minos.Application.Evaluation;

public interface ISubmissionRepository
{
    Task<bool> AdmitAsync(Submission submission, JudgeJob job, int maximumActive, CancellationToken token);
    Task<(Submission Submission, IReadOnlyList<JudgingAttempt> Attempts)?> FindAsync(Guid id, CancellationToken token);
    Task<(IReadOnlyList<Submission> Items, int Total)> ListByOwnerAsync(Guid ownerAccountId, int offset, int limit, CancellationToken token);
}

public interface IJudgeJobRepository
{
    Task<ClaimedSubmissionWork?> ClaimNextAsync(string workerId, DateTimeOffset nowUtc, CancellationToken token);
    Task<LeaseMutationResult> RenewAsync(
        Guid attemptId, Guid leaseToken, string workerId, DateTimeOffset nowUtc, CancellationToken token);
    Task<LeaseMutationResult> CompleteAsync(
        Guid attemptId, Guid leaseToken, string workerId, DateTimeOffset nowUtc, CancellationToken token);
    Task<AttemptFailureResult> FailAsync(
        Guid attemptId,
        Guid leaseToken,
        string workerId,
        InfrastructureFailureKind failureKind,
        DateTimeOffset nowUtc,
        CancellationToken token);
}

public interface ISubmissionExecutor
{
    Task<DeterministicExecutionResult> ExecuteAsync(ClaimedSubmissionWork work, CancellationToken token);
}
