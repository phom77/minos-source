using Minos.Application.Accounts;
using Minos.Application.Problems;
using Minos.Domain.Evaluation;

namespace Minos.Application.Evaluation;

public sealed class SubmissionUseCases(
    ISubmissionRepository submissions,
    IProblemRepository problems,
    AccountUseCases accounts,
    TimeProvider clock)
{
    public async Task<SubmissionDetails> CreateAsync(Guid problemId, CreateSubmission request, CancellationToken token)
    {
        var account = await accounts.RequireActiveAsync(token);
        if (!string.Equals(request.Language, SupportedSubmission.Language, StringComparison.Ordinal))
            throw new Minos.Domain.DomainValidationException(nameof(request.Language), "Only cpp20 is supported.");
        var version = await problems.FindLatestPublishedAsync(problemId, token) ?? throw new ResourceNotFoundException();
        var submission = Submission.Create(
            account.Id,
            version.ProblemId,
            version.Id,
            SupportedSubmission.Language,
            SupportedSubmission.ToolchainId,
            request.Source,
            clock.GetUtcNow());
        var job = JudgeJob.Create(submission.Id, submission.CreatedAtUtc);
        if (!await submissions.AdmitAsync(submission, job, SupportedSubmission.MaximumActivePerAccount, token))
            throw new AdmissionLimitExceededException();
        return Map(submission, []);
    }

    public async Task<SubmissionDetails> FindAsync(Guid id, CancellationToken token)
    {
        var account = await accounts.RequireActiveAsync(token);
        var found = await submissions.FindAsync(id, token) ?? throw new ResourceNotFoundException();
        if (found.Submission.OwnerAccountId != account.Id) throw new AccessDeniedException();
        return Map(found.Submission, found.Attempts);
    }

    public async Task<SubmissionPage> ListMineAsync(int page, int pageSize, CancellationToken token)
    {
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page));
        if (pageSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(pageSize));
        var offset = (long)(page - 1) * pageSize;
        if (offset > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(page));
        var account = await accounts.RequireActiveAsync(token);
        var (items, total) = await submissions.ListByOwnerAsync(
            account.Id, (int)offset, pageSize, token);
        return new(items.Select(MapSummary).ToArray(), page, pageSize, total);
    }

    internal static SubmissionDetails Map(Submission submission, IReadOnlyList<JudgingAttempt> attempts) =>
        new(
            submission.Id,
            submission.ProblemId,
            submission.ProblemVersionId,
            submission.Language,
            submission.ToolchainId,
            submission.Source,
            submission.Status,
            submission.CreatedAtUtc,
            submission.CompletedAtUtc,
            attempts.OrderBy(attempt => attempt.AttemptNumber).Select(attempt => new SubmissionAttemptDetails(
                attempt.Id,
                attempt.AttemptNumber,
                attempt.Status,
                attempt.FailureKind,
                attempt.StartedAtUtc,
                attempt.FinishedAtUtc)).ToArray());

    private static SubmissionSummary MapSummary(Submission submission) =>
        new(submission.Id, submission.ProblemId, submission.ProblemVersionId, submission.Status,
            submission.CreatedAtUtc, submission.CompletedAtUtc);
}
