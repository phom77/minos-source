using System.Text.RegularExpressions;

namespace Minos.Domain.Problems;

public enum ProblemDifficulty { Easy = 1, Medium = 2, Hard = 3 }
public enum DifficultyOrigin { Manual, CodeforcesRating }
public enum TopicOrigin { Upstream, Inferred, Manual }
public enum ClassificationConfidence { Low, Medium, High }

public sealed record ClassificationEvidence(
    string SourceProblemKey, int? SourceRating, IReadOnlyList<string> SourceTags,
    string DatasetRevision, string ManifestSha256, string PolicyVersion, string PolicySha256);
public sealed record TopicClassification(
    string Slug, TopicOrigin Origin, ClassificationConfidence? Confidence = null, string? Rationale = null);

// Mutable library metadata for a stable problem. Judging snapshots never depend on it.
public sealed class ProblemClassification
{
    public const int MaximumTopics = 32;
    private readonly List<ProblemTopicAssignment> topics = [];
    private string[] sourceTags = [];
    private ProblemClassification() { }
    public Guid ProblemId { get; private set; }
    public Guid BasisVersionId { get; private set; }
    public ProblemDifficulty? Difficulty { get; private set; }
    public DifficultyOrigin DifficultyOrigin { get; private set; }
    public string? SourceProblemKey { get; private set; }
    public int? SourceRating { get; private set; }
    public string[] SourceTags { get => sourceTags.ToArray(); private set => sourceTags = value; }
    public string? DatasetRevision { get; private set; }
    public string? ManifestSha256 { get; private set; }
    public string? PolicyVersion { get; private set; }
    public string? PolicySha256 { get; private set; }
    public long Revision { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public IReadOnlyList<ProblemTopicAssignment> Topics => topics;

    public static ProblemClassification Create(Guid problemId, Guid basisVersionId, ProblemDifficulty? difficulty,
        DifficultyOrigin origin, ClassificationEvidence? evidence, IReadOnlyList<TopicClassification> topics, DateTimeOffset now)
    {
        if (problemId == Guid.Empty) throw new DomainValidationException(nameof(ProblemId), "A problem is required.");
        var result = new ProblemClassification { ProblemId = problemId };
        result.Revise(basisVersionId, difficulty, origin, evidence, topics, now);
        return result;
    }

    public void Revise(Guid basisVersionId, ProblemDifficulty? difficulty, DifficultyOrigin origin,
        ClassificationEvidence? evidence, IReadOnlyList<TopicClassification> replacements, DateTimeOffset now)
    {
        if (basisVersionId == Guid.Empty) throw new DomainValidationException(nameof(BasisVersionId), "A published basis version is required.");
        Problem.EnsureUtc(now, nameof(UpdatedAtUtc));
        if ((difficulty is not null && !Enum.IsDefined(difficulty.Value)) || !Enum.IsDefined(origin))
            throw new DomainValidationException(nameof(Difficulty), "Invalid difficulty or origin.");
        if (origin == DifficultyOrigin.CodeforcesRating && (difficulty is null || evidence?.SourceRating is null))
            throw new DomainValidationException(nameof(SourceRating), "Rating-based difficulty requires source evidence and a rating.");
        if (evidence is not null)
        {
            if (evidence.SourceProblemKey is null || !Regex.IsMatch(evidence.SourceProblemKey, @"\Acf-[1-9][0-9]{0,8}-[a-z]{1,2}[0-9]?\z"))
                throw new DomainValidationException(nameof(SourceProblemKey), "Invalid Codeforces problem key.");
            if (evidence.SourceRating is <= 0 or > 10000 || evidence.SourceTags is null || evidence.SourceTags.Count > MaximumTopics ||
                evidence.SourceTags.Any(tag => string.IsNullOrWhiteSpace(tag) || tag.Length > 64))
                throw new DomainValidationException(nameof(SourceTags), "Source rating or tags are invalid.");
            if (!Hex(evidence.ManifestSha256, 64) || !Hex(evidence.PolicySha256, 64) || !Hex(evidence.DatasetRevision, 40) ||
                string.IsNullOrWhiteSpace(evidence.PolicyVersion) || evidence.PolicyVersion.Length > 64)
                throw new DomainValidationException(nameof(ManifestSha256), "Source and policy evidence must be bounded and checksum-pinned.");
        }
        if (replacements is null || replacements.Count > MaximumTopics)
            throw new DomainValidationException(nameof(Topics), $"A problem supports at most {MaximumTopics} topics.");
        var validated = replacements.Select(item => item is null
            ? throw new DomainValidationException(nameof(Topics), "A topic assignment is required.")
            : ProblemTopicAssignment.Create(ProblemId, item)).ToArray();
        if (validated.Select(item => item.TopicSlug).Distinct(StringComparer.Ordinal).Count() != validated.Length)
            throw new DomainValidationException(nameof(Topics), "Topics must be unique.");
        var nextRevision = checked(Revision + 1);
        BasisVersionId = basisVersionId;
        Difficulty = difficulty;
        DifficultyOrigin = origin;
        SourceProblemKey = evidence?.SourceProblemKey;
        SourceRating = evidence?.SourceRating;
        SourceTags = evidence?.SourceTags.ToArray() ?? [];
        DatasetRevision = evidence?.DatasetRevision;
        ManifestSha256 = evidence?.ManifestSha256;
        PolicyVersion = evidence?.PolicyVersion;
        PolicySha256 = evidence?.PolicySha256;
        topics.RemoveAll(existing => validated.All(item => item.TopicSlug != existing.TopicSlug));
        foreach (var item in validated)
        {
            var existing = topics.SingleOrDefault(candidate => candidate.TopicSlug == item.TopicSlug);
            if (existing is null) topics.Add(item);
            else existing.ReplaceEvidence(item);
        }
        Revision = nextRevision;
        UpdatedAtUtc = now;
    }

    private static bool Hex(string? value, int length) => value?.Length == length &&
        value.All(character => char.IsAsciiHexDigit(character) && !char.IsUpper(character));
}

public sealed class ProblemTopic
{
    private ProblemTopic() { }
    public string Slug { get; private set; } = "";
    public string DisplayName { get; private set; } = "";
    public static bool ValidSlug(string? slug) => slug is { Length: > 0 and <= 48 } &&
        Regex.IsMatch(slug, @"\A[a-z]+(?:-[a-z]+)*\z");
    public static ProblemTopic Create(string slug, string displayName)
    {
        if (!ValidSlug(slug) || string.IsNullOrWhiteSpace(displayName) || displayName.Length > 80 || displayName != displayName.Trim())
            throw new DomainValidationException(nameof(Slug), "A topic requires a valid slug and a name of at most 80 characters.");
        return new() { Slug = slug, DisplayName = displayName };
    }
}

public sealed class ProblemTopicAssignment
{
    private ProblemTopicAssignment() { }
    public Guid ProblemId { get; private set; }
    public string TopicSlug { get; private set; } = "";
    public TopicOrigin Origin { get; private set; }
    public ClassificationConfidence? Confidence { get; private set; }
    public string? Rationale { get; private set; }
    internal static ProblemTopicAssignment Create(Guid problemId, TopicClassification item)
    {
        if (!ProblemTopic.ValidSlug(item.Slug) || !Enum.IsDefined(item.Origin) ||
            (item.Confidence is not null && !Enum.IsDefined(item.Confidence.Value)) ||
            (item.Origin == TopicOrigin.Inferred) != (item.Confidence is not null) ||
            (item.Origin == TopicOrigin.Inferred && string.IsNullOrWhiteSpace(item.Rationale)) ||
            item.Rationale?.Length > 1000)
            throw new DomainValidationException(nameof(ProblemClassification.Topics), "Invalid topic assignment or inference evidence.");
        return new() { ProblemId = problemId, TopicSlug = item.Slug, Origin = item.Origin,
            Confidence = item.Confidence, Rationale = item.Rationale };
    }
    internal void ReplaceEvidence(ProblemTopicAssignment item) => (Origin, Confidence, Rationale) = (item.Origin, item.Confidence, item.Rationale);
}
