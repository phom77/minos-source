using Minos.Domain;
using Minos.Domain.Problems;
using Xunit;

namespace Minos.Domain.Tests;

public sealed class ProblemTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Problem_requires_owner_valid_text_and_utc_time()
    {
        Assert.Throws<DomainValidationException>(() => Problem.Create(Guid.Empty, "Title", "Statement", Now));
        Assert.Throws<DomainValidationException>(() => Problem.Create(Guid.NewGuid(), " ", "Statement", Now));
        Assert.Throws<DomainValidationException>(() => Problem.Create(Guid.NewGuid(), "Title", " ", Now));
        Assert.Throws<DomainValidationException>(() => Problem.Create(Guid.NewGuid(), "Title", "Statement", Now.ToOffset(TimeSpan.FromHours(7))));
        var owner = Guid.NewGuid();
        var problem = Problem.Create(owner, "  A + B  ", "  Keep formatting\n", Now);
        Assert.True(problem.IsOwnedBy(owner));
        Assert.Equal("A + B", problem.Title);
        Assert.Equal("  Keep formatting\n", problem.Statement);
        Assert.Equal(problem.CreatedAtUtc, problem.UpdatedAtUtc);
    }

    [Fact]
    public void Authoring_enforces_limit_sample_test_and_artifact_invariants()
    {
        var problem = Problem.Create(Guid.NewGuid(), "Title", "Statement", Now);
        Assert.Throws<DomainValidationException>(() => problem.ConfigureExecution(99, 64, BuiltInChecker.Exact, Now));
        Assert.Throws<DomainValidationException>(() => problem.ConfigureExecution(1000, 15, BuiltInChecker.Exact, Now));
        problem.ConfigureExecution(1000, 64, BuiltInChecker.Exact, Now);
        problem.ReplaceSamples([new("", "0"), new("1", "1")], Now);
        Assert.Equal([1, 2], problem.Samples.Select(sample => sample.Order));
        var test = problem.AddTestCase(Artifact("input.bin", 2), Artifact("output.bin", 3), Now);
        Assert.Equal(1, test.Order);
        Assert.Throws<DomainValidationException>(() => ProblemArtifactReference.Create("../input", new string('a', 64), 1));
        Assert.Throws<DomainValidationException>(() => ProblemArtifactReference.Create($"tests/{Guid.NewGuid():N}/input.bin", new string('A', 64), 1));
    }

    [Fact]
    public void Publication_requires_complete_configuration_and_checklist()
    {
        var problem = Problem.Create(Guid.NewGuid(), "Title", "Statement", Now);
        var complete = new PublicationChecklist(true, true, true);
        Assert.Throws<DomainValidationException>(() => problem.Publish(problem.OwnerAccountId, complete, Now));
        problem.ConfigureExecution(1000, 64, BuiltInChecker.Exact, Now);
        Assert.Throws<DomainValidationException>(() => problem.Publish(problem.OwnerAccountId, complete, Now));
        problem.ReplaceSamples([new("1", "1")], Now);
        Assert.Throws<DomainValidationException>(() => problem.Publish(problem.OwnerAccountId, complete, Now));
        problem.AddTestCase(Artifact("input.bin", 1), Artifact("output.bin", 1), Now);
        Assert.Throws<DomainValidationException>(() => problem.Publish(problem.OwnerAccountId, new(true, false, true), Now));
        Assert.Equal(0, problem.LatestPublishedVersion);
    }

    [Fact]
    public void Published_versions_are_snapshots_and_later_edits_create_a_new_version()
    {
        var owner = Guid.NewGuid();
        var problem = Complete(owner, "Version one");
        var first = problem.Publish(owner, new(true, true, true), Now);
        var firstTestKey = first.TestCases.Single().InputArtifact.Key;

        problem.EditContent("Version two", "Changed statement", Now.AddMinutes(1));
        problem.ConfigureExecution(2000, 128, BuiltInChecker.Exact, Now.AddMinutes(1));
        problem.ReplaceSamples([new("2", "2")], Now.AddMinutes(1));
        var originalTest = problem.TestCases.Single();
        Assert.True(problem.RemoveTestCase(originalTest.Id, Now.AddMinutes(1)));
        problem.AddTestCase(Artifact("input.bin", 4), Artifact("output.bin", 5), Now.AddMinutes(1));
        var second = problem.Publish(owner, new(true, true, true), Now.AddMinutes(2));

        Assert.Equal(problem.Id, first.ProblemId);
        Assert.Equal(problem.Id, second.ProblemId);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(1, first.VersionNumber);
        Assert.Equal(2, second.VersionNumber);
        Assert.Equal("Version one", first.Title);
        Assert.Equal(1000, first.TimeLimitMilliseconds);
        Assert.Equal("1", first.Samples.Single().Input);
        Assert.Equal(firstTestKey, first.TestCases.Single().InputArtifact.Key);
        Assert.Equal("Version two", second.Title);
        Assert.Equal(2000, second.TimeLimitMilliseconds);
    }

    [Fact]
    public void Archive_preserves_versions_and_blocks_further_authoring()
    {
        var problem = Complete(Guid.NewGuid(), "Title");
        var version = problem.Publish(problem.OwnerAccountId, new(true, true, true), Now);
        problem.Archive(Now.AddMinutes(1));
        Assert.True(problem.IsArchived);
        Assert.Equal(1, version.VersionNumber);
        Assert.Throws<DomainValidationException>(() => problem.EditContent("Changed", "Statement", Now.AddMinutes(2)));
        Assert.Throws<DomainValidationException>(() => problem.Publish(problem.OwnerAccountId, new(true, true, true), Now.AddMinutes(2)));
    }

    [Fact]
    public void Publication_preserves_1500_tests_and_rejects_the_1501st_without_mutation()
    {
        var problem = Complete(Guid.NewGuid(), "Large test suite");
        for (var order = 2; order <= 1500; order++)
            problem.AddTestCase(Artifact("input.bin", 1), Artifact("output.bin", 1), Now);

        var revision = problem.Revision;
        Assert.Throws<DomainValidationException>(() =>
            problem.AddTestCase(Artifact("input.bin", 1), Artifact("output.bin", 1), Now));
        Assert.Equal(revision, problem.Revision);
        Assert.Equal(1500, problem.TestCases.Count);

        var version = problem.Publish(problem.OwnerAccountId, new(true, true, true), Now);
        Assert.Equal(1500, version.PrivateTestCount);
        Assert.Equal(Enumerable.Range(1, 1500), version.TestCases.Select(test => test.Order));

        Assert.True(problem.RemoveTestCase(problem.TestCases[749].Id, Now));
        problem.AddTestCase(Artifact("input.bin", 1), Artifact("output.bin", 1), Now);
        Assert.Equal(Enumerable.Range(1, 1500), problem.TestCases.Select(test => test.Order));
        Assert.Equal(1500, version.TestCases.Count);
    }

    private static Problem Complete(Guid owner, string title)
    {
        var problem = Problem.Create(owner, title, "Statement", Now);
        problem.ConfigureExecution(1000, 64, BuiltInChecker.Exact, Now);
        problem.ReplaceSamples([new("1", "1")], Now);
        problem.AddTestCase(Artifact("input.bin", 1), Artifact("output.bin", 1), Now);
        return problem;
    }

    private static ProblemArtifactReference Artifact(string name, long size) =>
        ProblemArtifactReference.Create($"tests/{Guid.NewGuid():N}/{name}", new string('a', 64), size);
}
