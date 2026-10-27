namespace Minos.Api.Submissions;

public sealed record CreateSubmissionRequest(string? Language, string? Source, Guid? RequestKey = null);
public sealed record SubmissionAttemptResponse(
    Guid Id,
    int Number,
    string Status,
    string? FailureKind,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc);
public sealed record SubmissionResponse(
    Guid Id,
    Guid ProblemId,
    Guid ProblemVersionId,
    string Language,
    string ToolchainId,
    string Source,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<SubmissionAttemptResponse> Attempts,
    SubmissionResultResponse? Result,
    SubmissionJudgingConfigurationResponse? Configuration);
public sealed record SubmissionJudgingConfigurationResponse(
    int TimeLimitMilliseconds,
    int MemoryLimitMegabytes,
    string Checker,
    int TestCount);
public sealed record SubmissionTestResultResponse(
    int Order,
    string Status,
    long? CpuTimeMilliseconds,
    long? WallTimeMilliseconds,
    long? MemoryPeakBytes);
public sealed record SubmissionResultResponse(
    Guid AttemptId,
    string? Verdict,
    string CompilerDiagnostics,
    long? MaximumCpuTimeMilliseconds,
    long? MaximumWallTimeMilliseconds,
    long? MaximumMemoryBytes,
    DateTimeOffset CompletedAtUtc,
    IReadOnlyList<SubmissionTestResultResponse> Tests,
    SubmissionTestSummaryResponse? TestSummary,
    bool ComparisonEligible = false);
public sealed record SubmissionTestSummaryResponse(int TotalTests, int ExecutedTests, int PassedTests, int FailedTests);
public sealed record SubmissionSummaryResponse(
    Guid Id,
    Guid ProblemId,
    Guid ProblemVersionId,
    string Status,
    string? Verdict,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc);
public sealed record SubmissionPageResponse(
    IReadOnlyList<SubmissionSummaryResponse> Items,
    int Page,
    int PageSize,
    int Total);

public sealed record SubmissionPerformanceResponse(Guid SubmissionId, string Status, long SampleCount, int MinimumSampleCount,
    PerformanceMetricResponse? Runtime, PerformanceMetricResponse? Memory);
public sealed record PerformanceMetricResponse(long Value, decimal? BeatsPercentage, long? EqualCount, decimal RangeMaximum,
    IReadOnlyList<PerformanceBucketResponse> Buckets);
public sealed record PerformanceBucketResponse(int Index, long Count);
