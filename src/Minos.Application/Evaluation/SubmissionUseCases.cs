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
        var identity = SubmissionRequestIdentity.Create(request, problemId);
        if (identity is not null && await submissions.FindRequestAsync(account.Id, identity, token) is { } replay)
            return await FindAsync(replay, token);
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
        if (identity is not null)
        {
            var admitted = await submissions.AdmitOnceAsync(submission, job, SupportedSubmission.MaximumActivePerAccount, identity, token)
                ?? throw new AdmissionLimitExceededException();
            if (admitted != submission.Id) return await FindAsync(admitted, token);
        }
        else if (!await submissions.AdmitAsync(submission, job, SupportedSubmission.MaximumActivePerAccount, token))
            throw new AdmissionLimitExceededException();
        return Map(submission, [], configuration: Configuration(version));
    }

    public async Task<SubmissionDetails> FindAsync(Guid id, CancellationToken token)
    {
        var account = await accounts.RequireActiveAsync(token);
        var found = await submissions.FindAsync(id, token) ?? throw new ResourceNotFoundException();
        if (found.Submission.OwnerAccountId != account.Id) throw new AccessDeniedException();
        var version = await problems.FindPublishedVersionAsync(found.Submission.ProblemVersionId, token)
            ?? throw new InvalidOperationException("Pinned problem version is missing.");
        return Map(found.Submission, found.Attempts, found.Result, Configuration(version));
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
        return new(items.Select(item => MapSummary(item.Submission, item.Verdict)).ToArray(), page, pageSize, total);
    }

    internal static SubmissionDetails Map(
        Submission submission,
        IReadOnlyList<JudgingAttempt> attempts,
        SubmissionResult? result = null,
        SubmissionJudgingConfiguration? configuration = null) =>
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
                attempt.FinishedAtUtc)).ToArray(),
            result is null ? null : new SubmissionResultDetails(
                result.AttemptId,
                result.Verdict,
                result.CompilerDiagnostics,
                result.MaximumCpuTimeMilliseconds,
                result.MaximumWallTimeMilliseconds,
                result.MaximumMemoryBytes,
                result.CompletedAtUtc,
                result.TestCaseResults.OrderBy(test => test.Order).Select(test => new SubmissionTestResultDetails(
                    test.Order,
                    test.Status,
                    test.CpuTimeMilliseconds,
                    test.WallTimeMilliseconds,
                    test.MemoryPeakBytes)).ToArray(), SummarizeTests(result)), configuration);

    private static SubmissionTestSummary? SummarizeTests(SubmissionResult result)
    {
        if (result.Verdict is null) return null;
        var executed = result.TestCaseResults.Count(test => test.Status != TestCaseResultStatus.NotExecuted);
        var passed = result.TestCaseResults.Count(test => test.Status == TestCaseResultStatus.Passed);
        return new(result.TestCaseResults.Count, executed, passed, executed - passed);
    }

    private static SubmissionJudgingConfiguration Configuration(Minos.Domain.Problems.PublishedProblemVersion version) =>
        new(version.TimeLimitMilliseconds, version.MemoryLimitMegabytes, version.Checker.ToString(), version.PrivateTestCount);

    private static SubmissionSummary MapSummary(Submission submission, SubmissionVerdict? verdict) =>
        new(submission.Id, submission.ProblemId, submission.ProblemVersionId, submission.Status, verdict,
            submission.CreatedAtUtc, submission.CompletedAtUtc);
}
