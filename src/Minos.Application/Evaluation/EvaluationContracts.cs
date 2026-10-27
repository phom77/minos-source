using Minos.Domain.Evaluation;

namespace Minos.Application.Evaluation;

public static class SupportedSubmission
{
    public const string Language = "cpp20";
    public const string ToolchainId = "gcc-14.3.0-cpp20-o2-pipe@sha256:5e927c284bf55a7dc796262e311a0703344f62f41f5621eb56843111b1d37e15";
    public const int MaximumActivePerAccount = 5;
}

public sealed record CreateSubmission(string? Language, string? Source, Guid? RequestKey = null);

public sealed record SubmissionSummary(
    Guid Id,
    Guid ProblemId,
    Guid ProblemVersionId,
    SubmissionStatus Status,
    SubmissionVerdict? Verdict,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record SubmissionAttemptDetails(
    Guid Id,
    int Number,
    JudgingAttemptStatus Status,
    InfrastructureFailureKind? FailureKind,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc);

public sealed record SubmissionDetails(
    Guid Id,
    Guid ProblemId,
    Guid ProblemVersionId,
    string Language,
    string ToolchainId,
    string Source,
    SubmissionStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<SubmissionAttemptDetails> Attempts,
    SubmissionResultDetails? Result,
    SubmissionJudgingConfiguration? Configuration);

public sealed record SubmissionJudgingConfiguration(
    int TimeLimitMilliseconds,
    int MemoryLimitMegabytes,
    string Checker,
    int TestCount);

public sealed record SubmissionTestResultDetails(
    int Order,
    TestCaseResultStatus Status,
    long? CpuTimeMilliseconds,
    long? WallTimeMilliseconds,
    long? MemoryPeakBytes);

public sealed record SubmissionResultDetails(
    Guid AttemptId,
    SubmissionVerdict? Verdict,
    string CompilerDiagnostics,
    long? MaximumCpuTimeMilliseconds,
    long? MaximumWallTimeMilliseconds,
    long? MaximumMemoryBytes,
    DateTimeOffset CompletedAtUtc,
    IReadOnlyList<SubmissionTestResultDetails> Tests,
    SubmissionTestSummary? TestSummary,
    bool ComparisonEligible = false);

public sealed record SubmissionTestSummary(int TotalTests, int ExecutedTests, int PassedTests, int FailedTests);

public sealed record SubmissionPage(IReadOnlyList<SubmissionSummary> Items, int Page, int PageSize, int Total);

public sealed record ClaimedSubmissionWork(
    Guid JobId,
    Guid SubmissionId,
    Guid AttemptId,
    Guid LeaseToken,
    string WorkerId,
    DateTimeOffset LeaseExpiresAtUtc,
    Guid ProblemVersionId,
    string Language,
    string ToolchainId,
    string Source);

public enum LeaseMutationResult { Accepted, Stale }
public enum AttemptFailureResult { RetryScheduled, SystemFailed, Stale }
public enum SubmissionExecutionStatus { Completed, InfrastructureFailed }

public sealed record SubmissionExecutionResult(
    SubmissionExecutionStatus Status,
    JudgingOutcome? Outcome = null,
    InfrastructureFailureKind? FailureKind = null)
{
    public static SubmissionExecutionResult Completed(JudgingOutcome outcome) =>
        new(SubmissionExecutionStatus.Completed, outcome);

    public static SubmissionExecutionResult InfrastructureFailed(InfrastructureFailureKind kind = InfrastructureFailureKind.ExecutorFailure) =>
        new(SubmissionExecutionStatus.InfrastructureFailed, null, kind);
}
