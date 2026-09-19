namespace Minos.Domain.Problems;

public sealed class PublishedProblemVersion
{
    private readonly List<PublishedProblemSample> samples = [];
    private readonly List<PublishedProblemTestCase> testCases = [];
    private PublishedProblemVersion() { }

    public Guid Id { get; private set; }
    public Guid ProblemId { get; private set; }
    public int VersionNumber { get; private set; }
    public string Title { get; private set; } = "";
    public string Statement { get; private set; } = "";
    public int TimeLimitMilliseconds { get; private set; }
    public int MemoryLimitMegabytes { get; private set; }
    public BuiltInChecker Checker { get; private set; }
    public int PrivateTestCount { get; private set; }
    public Guid PublishedByAccountId { get; private set; }
    public DateTimeOffset PublishedAtUtc { get; private set; }
    public bool StatementReviewed { get; private set; }
    public bool SamplesVerified { get; private set; }
    public bool ReferenceSolutionChecked { get; private set; }
    public IReadOnlyList<PublishedProblemSample> Samples => samples;
    public IReadOnlyList<PublishedProblemTestCase> TestCases => testCases;

    internal static PublishedProblemVersion Create(
        Problem problem,
        Guid id,
        int versionNumber,
        Guid publishedByAccountId,
        PublicationChecklist checklist,
        DateTimeOffset publishedAtUtc)
    {
        var version = new PublishedProblemVersion
        {
            Id = id,
            ProblemId = problem.Id,
            VersionNumber = versionNumber,
            Title = problem.Title,
            Statement = problem.Statement,
            TimeLimitMilliseconds = problem.TimeLimitMilliseconds!.Value,
            MemoryLimitMegabytes = problem.MemoryLimitMegabytes!.Value,
            Checker = problem.Checker,
            PrivateTestCount = problem.TestCases.Count,
            PublishedByAccountId = publishedByAccountId,
            PublishedAtUtc = publishedAtUtc,
            StatementReviewed = checklist.StatementReviewed,
            SamplesVerified = checklist.SamplesVerified,
            ReferenceSolutionChecked = checklist.ReferenceSolutionChecked
        };
        version.samples.AddRange(problem.Samples.Select(sample =>
            PublishedProblemSample.Create(Guid.NewGuid(), version.Id, sample.Order, sample.Input, sample.Output)));
        version.testCases.AddRange(problem.TestCases.Select(test =>
            PublishedProblemTestCase.Create(Guid.NewGuid(), version.Id, test.Order, test.InputArtifact, test.OutputArtifact)));
        return version;
    }
}

public sealed class PublishedProblemSample
{
    private PublishedProblemSample() { }
    public Guid Id { get; private set; }
    public Guid ProblemVersionId { get; private set; }
    public int Order { get; private set; }
    public string Input { get; private set; } = "";
    public string Output { get; private set; } = "";

    internal static PublishedProblemSample Create(Guid id, Guid versionId, int order, string input, string output) =>
        new() { Id = id, ProblemVersionId = versionId, Order = order, Input = input, Output = output };
}

public sealed class PublishedProblemTestCase
{
    private PublishedProblemTestCase() { }
    public Guid Id { get; private set; }
    public Guid ProblemVersionId { get; private set; }
    public int Order { get; private set; }
    public ProblemArtifactReference InputArtifact { get; private set; } = null!;
    public ProblemArtifactReference OutputArtifact { get; private set; } = null!;

    internal static PublishedProblemTestCase Create(
        Guid id,
        Guid versionId,
        int order,
        ProblemArtifactReference input,
        ProblemArtifactReference output) =>
        new()
        {
            Id = id,
            ProblemVersionId = versionId,
            Order = order,
            InputArtifact = input.Copy(),
            OutputArtifact = output.Copy()
        };
}
