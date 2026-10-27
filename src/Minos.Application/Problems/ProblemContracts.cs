using Minos.Domain.Problems;

namespace Minos.Application.Problems;

public sealed record CreateProblem(string? Title, string? Statement);
public sealed record UpdateProblemDraft(
    string? Title,
    string? Statement,
    int TimeLimitMilliseconds,
    int MemoryLimitMegabytes,
    BuiltInChecker Checker,
    IReadOnlyList<ProblemSampleContent> Samples);
public sealed record PublishProblem(bool StatementReviewed, bool SamplesVerified, bool ReferenceSolutionChecked);
public sealed record ProblemTestCaseDetails(Guid Id, int Order, long InputSizeBytes, long OutputSizeBytes);
public sealed record ProblemDraftDetails(
    Guid Id,
    string Title,
    string Statement,
    int? TimeLimitMilliseconds,
    int? MemoryLimitMegabytes,
    BuiltInChecker Checker,
    int LatestPublishedVersion,
    bool IsArchived,
    long Revision,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<ProblemSampleContent> Samples,
    IReadOnlyList<ProblemTestCaseDetails> TestCases);
public sealed record PublishedProblemSummary(Guid ProblemId, Guid VersionId, int VersionNumber, string Title, DateTimeOffset PublishedAtUtc)
{
    public ProblemClassificationSummary? Classification { get; init; }
    public int PublicNumber { get; init; }
    public ProblemSolveStatus? SolveStatus { get; init; }
}
public sealed record PublishedProblemDetails(
    Guid ProblemId,
    Guid VersionId,
    int VersionNumber,
    string Title,
    string Statement,
    int TimeLimitMilliseconds,
    int MemoryLimitMegabytes,
    BuiltInChecker Checker,
    int PrivateTestCount,
    DateTimeOffset PublishedAtUtc,
    IReadOnlyList<ProblemSampleContent> Samples);
public sealed record PublishedProblemPage(IReadOnlyList<PublishedProblemSummary> Items, int Page, int PageSize, int Total)
{
    public ProblemProgressSummary? Progress { get; init; }
}
