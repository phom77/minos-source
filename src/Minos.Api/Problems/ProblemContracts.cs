namespace Minos.Api.Problems;

public sealed record CreateProblemRequest(string? Title, string? Statement);
public sealed record ProblemSampleRequest(string? Input, string? Output);
public sealed record UpdateProblemDraftRequest(
    string? Title,
    string? Statement,
    int TimeLimitMilliseconds,
    int MemoryLimitMegabytes,
    string? Checker,
    IReadOnlyList<ProblemSampleRequest>? Samples);
public sealed record PublishProblemRequest(bool StatementReviewed, bool SamplesVerified, bool ReferenceSolutionChecked);
public sealed record ProblemSampleResponse(string Input, string Output);
public sealed record ProblemTestCaseResponse(Guid Id, int Order, long InputSizeBytes, long OutputSizeBytes);
public sealed record ProblemDraftResponse(
    Guid Id,
    string Title,
    string Statement,
    int? TimeLimitMilliseconds,
    int? MemoryLimitMegabytes,
    string Checker,
    int LatestPublishedVersion,
    bool IsArchived,
    long Revision,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<ProblemSampleResponse> Samples,
    IReadOnlyList<ProblemTestCaseResponse> TestCases);
public sealed record PublishedProblemSummaryResponse(
    Guid ProblemId,
    Guid VersionId,
    int VersionNumber,
    string Title,
    DateTimeOffset PublishedAtUtc);
public sealed record PublishedProblemResponse(
    Guid ProblemId,
    Guid VersionId,
    int VersionNumber,
    string Title,
    string Statement,
    int TimeLimitMilliseconds,
    int MemoryLimitMegabytes,
    string Checker,
    int PrivateTestCount,
    DateTimeOffset PublishedAtUtc,
    IReadOnlyList<ProblemSampleResponse> Samples);
public sealed record PublishedProblemPageResponse(
    IReadOnlyList<PublishedProblemSummaryResponse> Items,
    int Page,
    int PageSize,
    int Total);
