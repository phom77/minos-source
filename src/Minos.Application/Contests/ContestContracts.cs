using Minos.Application.Evaluation;
using Minos.Domain.Contests;
using Minos.Domain.Evaluation;

namespace Minos.Application.Contests;

public sealed record SaveContest(string? Title, string? Description, DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc, IReadOnlyList<Guid> VersionIds, long? Revision = null);
public sealed record ContestSummary(Guid Id, string Title, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc,
    ContestState State, int ProblemCount);
public sealed record ContestPage(IReadOnlyList<ContestSummary> Items, int Page, int PageSize, int Total);
public sealed record ContestProblemDetails(Guid Id, Guid ProblemId, Guid VersionId, int Order, string Title);
public sealed record ContestDetails(Guid Id, string Title, string Description, DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc, ContestState State, long Revision, bool CanManage, bool IsRegistered,
    bool CanRegister, bool CanSubmit, int ProblemCount, DateTimeOffset ServerTimeUtc, IReadOnlyList<ContestProblemDetails> Problems);
public sealed record ContestSubmissionItem(Guid Id, Guid ContestProblemId, SubmissionStatus Status,
    SubmissionVerdict? Verdict, DateTimeOffset CreatedAtUtc);
public sealed record ContestSubmissionPage(IReadOnlyList<ContestSubmissionItem> Items, int Page, int PageSize, int Total);

public interface IContestRepository
{
    Task AddAsync(Contest contest, CancellationToken token);
    Task<Contest?> FindAsync(Guid id, CancellationToken token);
    Task SaveAsync(Contest contest, CancellationToken token);
    Task<(IReadOnlyList<Contest> Items, int Total)> ListAsync(Guid? ownerId, int offset, int limit, CancellationToken token);
    Task<ContestRegistration?> FindRegistrationAsync(Guid contestId, Guid accountId, CancellationToken token);
    Task RegisterAsync(ContestRegistration registration, CancellationToken token);
    Task<bool> AdmitAsync(Contest contest, ContestSubmission association, Submission submission, JudgeJob job, CancellationToken token);
    Task<Guid?> AdmitOnceAsync(Contest contest, ContestRegistration registration, Guid contestProblemId, Submission submission, JudgeJob job,
        SubmissionRequestIdentity request, CancellationToken token);
    Task<(IReadOnlyList<ContestSubmissionItem> Items, int Total)> ListSubmissionsAsync(Guid contestId, Guid ownerId,
        int offset, int limit, CancellationToken token);
}
