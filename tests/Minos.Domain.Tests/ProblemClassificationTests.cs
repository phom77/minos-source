using Minos.Domain;
using Minos.Domain.Problems;
using Xunit;

namespace Minos.Domain.Tests;

public sealed class ProblemClassificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);
    private static ClassificationEvidence Evidence() => new("cf-1-a", 1200, ["math"], new('a', 40), new('b', 64), "codeforces-v1", new('c', 64));

    [Fact]
    public void Metadata_changes_do_not_mutate_or_publish_the_problem_snapshot()
    {
        var problem = Problem.Create(Guid.NewGuid(), "Original", "Statement", Now);
        problem.ConfigureExecution(1000, 64, BuiltInChecker.Exact, Now);
        problem.ReplaceSamples([new("1", "1")], Now);
        problem.AddTestCase(ProblemArtifactReference.Create($"tests/{Guid.NewGuid():N}/input.bin", new('a', 64), 1),
            ProblemArtifactReference.Create($"tests/{Guid.NewGuid():N}/output.bin", new('a', 64), 1), Now);
        var version = problem.Publish(problem.OwnerAccountId, new(true, true, true), Now);
        var revision = problem.Revision;
        var value = ProblemClassification.Create(problem.Id, version.Id, ProblemDifficulty.Easy, DifficultyOrigin.CodeforcesRating,
            Evidence(), [new("math", TopicOrigin.Upstream)], Now);
        value.Revise(version.Id, ProblemDifficulty.Medium, DifficultyOrigin.Manual, Evidence(),
            [new("math", TopicOrigin.Manual)], Now.AddMinutes(1));
        Assert.Equal(2, value.Revision);
        Assert.Equal(1, problem.LatestPublishedVersion);
        Assert.Equal(revision, problem.Revision);
        Assert.Equal("Original", version.Title);
        Assert.Equal(1000, version.TimeLimitMilliseconds);
        var tags = value.SourceTags;
        tags[0] = "Changed";
        Assert.Equal("math", Assert.Single(value.SourceTags));
    }

    [Theory]
    [InlineData("Math")]
    [InlineData("math,dp")]
    [InlineData(" math ")]
    [InlineData("")]
    public void Invalid_topic_slugs_are_rejected(string slug) => Assert.Throws<DomainValidationException>(() =>
        ProblemClassification.Create(Guid.NewGuid(), Guid.NewGuid(), null, DifficultyOrigin.Manual, null, [new(slug, TopicOrigin.Manual)], Now));

    [Fact]
    public void Invalid_replacement_is_atomic_and_inference_requires_bounded_evidence()
    {
        var value = ProblemClassification.Create(Guid.NewGuid(), Guid.NewGuid(), null, DifficultyOrigin.Manual, null,
            [new("math", TopicOrigin.Manual)], Now);
        foreach (var topics in new IReadOnlyList<TopicClassification>[] {
            [new("math", TopicOrigin.Inferred)],
            [new("math", TopicOrigin.Manual, ClassificationConfidence.High)],
            [new("math", TopicOrigin.Inferred, ClassificationConfidence.High, new string('x', 1001))],
            [new("math", TopicOrigin.Manual), new("math", TopicOrigin.Upstream)],
            Enumerable.Range(0, 33).Select(_ => new TopicClassification("math", TopicOrigin.Manual)).ToArray()
        })
            Assert.Throws<DomainValidationException>(() => value.Revise(Guid.NewGuid(), ProblemDifficulty.Hard, DifficultyOrigin.Manual, null, topics, Now));
        Assert.Equal(1, value.Revision);
        Assert.Null(value.Difficulty);
        Assert.Equal(TopicOrigin.Manual, Assert.Single(value.Topics).Origin);
        value.Revise(value.BasisVersionId, null, DifficultyOrigin.Manual, null,
            [new("math", TopicOrigin.Inferred, ClassificationConfidence.Medium, "Reference uses integer arithmetic.")], Now);
        Assert.Equal(ClassificationConfidence.Medium, Assert.Single(value.Topics).Confidence);
    }

    [Fact]
    public void Rating_classification_requires_complete_source_and_policy_evidence()
    {
        foreach (var evidence in new ClassificationEvidence?[] { null, Evidence() with { SourceRating = null },
            Evidence() with { PolicySha256 = "bad" }, Evidence() with { DatasetRevision = "bad" },
            Evidence() with { SourceTags = [new string('x', 65)] } })
            Assert.Throws<DomainValidationException>(() => ProblemClassification.Create(Guid.NewGuid(), Guid.NewGuid(), ProblemDifficulty.Easy,
                DifficultyOrigin.CodeforcesRating, evidence, [], Now));
        Assert.Throws<DomainValidationException>(() => ProblemClassification.Create(Guid.NewGuid(), Guid.Empty, null, DifficultyOrigin.Manual, null, [], Now));
        Assert.Throws<DomainValidationException>(() => ProblemClassification.Create(Guid.NewGuid(), Guid.NewGuid(), (ProblemDifficulty)99, DifficultyOrigin.Manual, null, [], Now));
        Assert.Throws<DomainValidationException>(() => ProblemClassification.Create(Guid.NewGuid(), Guid.NewGuid(), null, DifficultyOrigin.Manual, null, [], Now.ToOffset(TimeSpan.FromHours(7))));
    }
}
