namespace Minos.Api.Submissions;

public sealed record CreateSubmissionRequest(string? Language, string? Source);
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
    IReadOnlyList<SubmissionAttemptResponse> Attempts);
public sealed record SubmissionSummaryResponse(
    Guid Id,
    Guid ProblemId,
    Guid ProblemVersionId,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc);
public sealed record SubmissionPageResponse(
    IReadOnlyList<SubmissionSummaryResponse> Items,
    int Page,
    int PageSize,
    int Total);
