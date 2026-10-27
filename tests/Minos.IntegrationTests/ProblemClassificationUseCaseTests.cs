using Minos.Application;
using Minos.Application.Accounts;
using Minos.Application.Problems;
using Minos.Domain;
using Minos.Domain.Accounts;
using Minos.Domain.Problems;
using Xunit;

namespace Minos.IntegrationTests;

public sealed class ProblemClassificationUseCaseTests
{
    [Fact]
    public async Task Direct_calls_require_active_author_and_immutable_problem_ownership()
    {
        var account = Account.Create("Owner", DateTimeOffset.UtcNow);
        var repository = new Repository(account.Id);
        var cases = Cases(account, repository);
        var token = TestContext.Current.CancellationToken;
        var request = new UpdateProblemClassification(0, repository.Version, ProblemDifficulty.Easy, DifficultyOrigin.Manual, null, []);
        await Assert.ThrowsAsync<AccessDeniedException>(() => cases.UpdateAsync(repository.Problem, request, token));
        account.AssignRole(AccountRole.Setter);
        repository.Owner = Guid.NewGuid();
        await Assert.ThrowsAsync<AccessDeniedException>(() => cases.UpdateAsync(repository.Problem, request, token));
        await Assert.ThrowsAsync<AccessDeniedException>(() => cases.FindOwnedAsync(repository.Problem, token));
        repository.Owner = account.Id;
        account.Disable();
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => cases.UpdateAsync(repository.Problem, request, token));
        Assert.Equal(0, repository.Writes);
    }

    [Fact]
    public async Task Invalid_basis_unknown_topics_archival_and_stale_revisions_do_not_write()
    {
        var account = Account.Create("Owner", DateTimeOffset.UtcNow);
        account.AssignRole(AccountRole.Setter);
        var repository = new Repository(account.Id);
        var cases = Cases(account, repository);
        var token = TestContext.Current.CancellationToken;
        var request = new UpdateProblemClassification(0, repository.Version, ProblemDifficulty.Medium, DifficultyOrigin.Manual, null, [new("math", TopicOrigin.Manual)]);
        await Assert.ThrowsAsync<DomainValidationException>(() => cases.UpdateAsync(repository.Problem, request with { BasisVersionId = Guid.NewGuid() }, token));
        await Assert.ThrowsAsync<DomainValidationException>(() => cases.UpdateAsync(repository.Problem, request with { Topics = [new("unknown", TopicOrigin.Manual)] }, token));
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => cases.UpdateAsync(repository.Problem, request with { ExpectedRevision = 1 }, token));
        Assert.Equal(0, repository.Writes);
        var created = await cases.UpdateAsync(repository.Problem, request, token);
        Assert.Equal(1, created.Revision);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => cases.UpdateAsync(repository.Problem, request, token));
        repository.Archived = true;
        await Assert.ThrowsAsync<DomainValidationException>(() => cases.UpdateAsync(repository.Problem, request with { ExpectedRevision = 1 }, token));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => cases.FindPublicAsync(repository.Problem, token));
        Assert.Equal(1, repository.Writes);
    }

    [Fact]
    public async Task Public_projection_keeps_current_labels_and_flags_a_new_content_version()
    {
        var account = Account.Create("Owner", DateTimeOffset.UtcNow);
        account.AssignRole(AccountRole.Setter);
        var repository = new Repository(account.Id);
        var cases = Cases(account, repository);
        var token = TestContext.Current.CancellationToken;
        await cases.UpdateAsync(repository.Problem, new(0, repository.Version, ProblemDifficulty.Hard, DifficultyOrigin.Manual,
            null, [new("math", TopicOrigin.Inferred, ClassificationConfidence.Medium, "Private author rationale.")]), token);
        repository.Version = Guid.NewGuid();
        var visible = await cases.FindPublicAsync(repository.Problem, token);
        Assert.True(visible.NeedsReassessment);
        Assert.Equal(ProblemDifficulty.Hard, visible.Difficulty);
        Assert.Equal("Math", Assert.Single(visible.Topics).DisplayName);
        var serialized = System.Text.Json.JsonSerializer.Serialize(visible);
        Assert.DoesNotContain("Private author rationale", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("SourceRating", serialized, StringComparison.Ordinal);
        Assert.True((await cases.FindOwnedAsync(repository.Problem, token)).NeedsReassessment);
    }

    private static ProblemClassificationUseCases Cases(Account account, Repository repository) =>
        new(repository, new(new(new AppAccountStore(account), new TestCurrentAccount(account.Id), TimeProvider.System)), TimeProvider.System);

    private sealed class Repository(Guid owner) : IProblemClassificationRepository
    {
        internal Guid Owner { get; set; } = owner;
        internal Guid Problem { get; } = Guid.NewGuid();
        internal Guid Version { get; set; } = Guid.NewGuid();
        internal bool Archived { get; set; }
        internal int Writes { get; private set; }
        private ProblemClassification? value;
        public Task<ProblemClassificationTarget?> FindTargetAsync(Guid problemId, CancellationToken token) =>
            Task.FromResult<ProblemClassificationTarget?>(problemId == Problem ? new(Owner, Archived, Version) : null);
        public Task<bool> VersionBelongsToProblemAsync(Guid problemId, Guid versionId, CancellationToken token) =>
            Task.FromResult(problemId == Problem && versionId == Version);
        public Task<ProblemClassification?> FindAsync(Guid problemId, CancellationToken token) => Task.FromResult(value);
        public Task<IReadOnlyList<ProblemTopicSummary>> ListTopicsAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyList<ProblemTopicSummary>>([new("math", "Math")]);
        public Task SaveAsync(ProblemClassification classification, CancellationToken token) { value = classification; Writes++; return Task.CompletedTask; }
    }
}
