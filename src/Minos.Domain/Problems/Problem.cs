namespace Minos.Domain.Problems;

public enum BuiltInChecker { Exact }

public sealed class Problem
{
    public const int MaximumTitleLength = 120;
    public const int MaximumStatementLength = 65536;
    public const int MinimumTimeLimitMilliseconds = 100;
    public const int MaximumTimeLimitMilliseconds = 10000;
    public const int MinimumMemoryLimitMegabytes = 16;
    public const int MaximumMemoryLimitMegabytes = 1024;
    public const int MaximumSamples = 20;
    public const int MaximumTestCases = 100;

    private readonly List<ProblemSample> samples = [];
    private readonly List<ProblemTestCase> testCases = [];
    private Problem() { }

    public Guid Id { get; private set; }
    public Guid OwnerAccountId { get; private set; }
    public string Title { get; private set; } = "";
    public string Statement { get; private set; } = "";
    public int? TimeLimitMilliseconds { get; private set; }
    public int? MemoryLimitMegabytes { get; private set; }
    public BuiltInChecker Checker { get; private set; } = BuiltInChecker.Exact;
    public int LatestPublishedVersion { get; private set; }
    public bool IsArchived { get; private set; }
    public long Revision { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public IReadOnlyList<ProblemSample> Samples => samples;
    public IReadOnlyList<ProblemTestCase> TestCases => testCases;

    public static Problem Create(Guid ownerAccountId, string? title, string? statement, DateTimeOffset createdAtUtc)
    {
        if (ownerAccountId == Guid.Empty)
            throw new DomainValidationException(nameof(OwnerAccountId), "A problem must have an account owner.");
        EnsureUtc(createdAtUtc, nameof(CreatedAtUtc));
        var problem = new Problem
        {
            Id = Guid.NewGuid(),
            OwnerAccountId = ownerAccountId,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc
        };
        problem.SetContent(title, statement, createdAtUtc, false);
        return problem;
    }

    public bool IsOwnedBy(Guid accountId) => accountId != Guid.Empty && OwnerAccountId == accountId;

    public void EditContent(string? title, string? statement, DateTimeOffset updatedAtUtc) =>
        SetContent(title, statement, updatedAtUtc, true);

    public void ConfigureExecution(int timeLimitMilliseconds, int memoryLimitMegabytes, BuiltInChecker checker, DateTimeOffset updatedAtUtc)
    {
        EnsureEditable();
        EnsureUtc(updatedAtUtc, nameof(UpdatedAtUtc));
        if (timeLimitMilliseconds is < MinimumTimeLimitMilliseconds or > MaximumTimeLimitMilliseconds)
            throw new DomainValidationException(nameof(TimeLimitMilliseconds), $"Time limit must be {MinimumTimeLimitMilliseconds}–{MaximumTimeLimitMilliseconds} milliseconds.");
        if (memoryLimitMegabytes is < MinimumMemoryLimitMegabytes or > MaximumMemoryLimitMegabytes)
            throw new DomainValidationException(nameof(MemoryLimitMegabytes), $"Memory limit must be {MinimumMemoryLimitMegabytes}–{MaximumMemoryLimitMegabytes} MiB.");
        if (!Enum.IsDefined(checker))
            throw new DomainValidationException(nameof(Checker), "Checker is not supported.");
        TimeLimitMilliseconds = timeLimitMilliseconds;
        MemoryLimitMegabytes = memoryLimitMegabytes;
        Checker = checker;
        Touch(updatedAtUtc);
    }

    public void ReplaceSamples(IEnumerable<ProblemSampleContent> replacements, DateTimeOffset updatedAtUtc)
    {
        EnsureEditable();
        EnsureUtc(updatedAtUtc, nameof(UpdatedAtUtc));
        var contents = replacements.ToArray();
        if (contents.Length > MaximumSamples)
            throw new DomainValidationException(nameof(Samples), $"A problem can contain at most {MaximumSamples} samples.");
        var newSamples = contents.Select((content, index) =>
            ProblemSample.Create(Guid.NewGuid(), index + 1, content.Input, content.Output)).ToArray();
        samples.Clear();
        samples.AddRange(newSamples);
        Touch(updatedAtUtc);
    }

    public ProblemTestCase AddTestCase(ProblemArtifactReference input, ProblemArtifactReference output, DateTimeOffset updatedAtUtc)
    {
        EnsureEditable();
        EnsureUtc(updatedAtUtc, nameof(UpdatedAtUtc));
        if (testCases.Count >= MaximumTestCases)
            throw new DomainValidationException(nameof(TestCases), $"A problem can contain at most {MaximumTestCases} test cases.");
        var test = ProblemTestCase.Create(Guid.NewGuid(), testCases.Count + 1, input, output);
        testCases.Add(test);
        Touch(updatedAtUtc);
        return test;
    }

    public bool RemoveTestCase(Guid testCaseId, DateTimeOffset updatedAtUtc)
    {
        EnsureEditable();
        EnsureUtc(updatedAtUtc, nameof(UpdatedAtUtc));
        var test = testCases.SingleOrDefault(candidate => candidate.Id == testCaseId);
        if (test is null) return false;
        testCases.Remove(test);
        for (var index = 0; index < testCases.Count; index++) testCases[index].SetOrder(index + 1);
        Touch(updatedAtUtc);
        return true;
    }

    public PublishedProblemVersion Publish(Guid publishedByAccountId, PublicationChecklist checklist, DateTimeOffset publishedAtUtc)
    {
        EnsureEditable();
        EnsureUtc(publishedAtUtc, nameof(PublishedProblemVersion.PublishedAtUtc));
        if (publishedByAccountId == Guid.Empty)
            throw new DomainValidationException(nameof(PublishedProblemVersion.PublishedByAccountId), "Publication requires an account.");
        if (TimeLimitMilliseconds is null || MemoryLimitMegabytes is null)
            throw new DomainValidationException(nameof(TimeLimitMilliseconds), "Execution limits must be configured before publication.");
        if (samples.Count == 0)
            throw new DomainValidationException(nameof(Samples), "At least one sample is required before publication.");
        if (testCases.Count == 0)
            throw new DomainValidationException(nameof(TestCases), "At least one private test case is required before publication.");
        checklist.EnsureComplete();

        LatestPublishedVersion = checked(LatestPublishedVersion + 1);
        Touch(publishedAtUtc);
        return PublishedProblemVersion.Create(this, Guid.NewGuid(), LatestPublishedVersion, publishedByAccountId, checklist, publishedAtUtc);
    }

    public void Archive(DateTimeOffset archivedAtUtc)
    {
        EnsureEditable();
        EnsureUtc(archivedAtUtc, nameof(UpdatedAtUtc));
        if (LatestPublishedVersion == 0)
            throw new DomainValidationException(nameof(LatestPublishedVersion), "Only a published problem can be archived.");
        IsArchived = true;
        Touch(archivedAtUtc);
    }

    private void SetContent(string? title, string? statement, DateTimeOffset updatedAtUtc, bool touch)
    {
        if (touch) EnsureEditable();
        EnsureUtc(updatedAtUtc, nameof(UpdatedAtUtc));
        var normalizedTitle = title?.Trim() ?? "";
        if (normalizedTitle.Length is 0 or > MaximumTitleLength)
            throw new DomainValidationException(nameof(Title), $"Title must contain 1–{MaximumTitleLength} characters after trimming.");
        if (string.IsNullOrWhiteSpace(statement) || statement.Length > MaximumStatementLength)
            throw new DomainValidationException(nameof(Statement), $"Statement must contain text and at most {MaximumStatementLength} characters.");
        Title = normalizedTitle;
        Statement = statement;
        if (touch) Touch(updatedAtUtc);
    }

    private void EnsureEditable()
    {
        if (IsArchived) throw new DomainValidationException(nameof(IsArchived), "An archived problem cannot be changed.");
    }

    private void Touch(DateTimeOffset instant)
    {
        UpdatedAtUtc = instant;
        Revision = checked(Revision + 1);
    }

    internal static void EnsureUtc(DateTimeOffset instant, string property)
    {
        if (instant.Offset != TimeSpan.Zero)
            throw new DomainValidationException(property, "Time must use UTC.");
    }
}

public sealed record ProblemSampleContent(string? Input, string? Output);

public sealed record PublicationChecklist(bool StatementReviewed, bool SamplesVerified, bool ReferenceSolutionChecked)
{
    internal void EnsureComplete()
    {
        if (!StatementReviewed || !SamplesVerified || !ReferenceSolutionChecked)
            throw new DomainValidationException(nameof(PublicationChecklist), "The publication checklist must be completed.");
    }
}
