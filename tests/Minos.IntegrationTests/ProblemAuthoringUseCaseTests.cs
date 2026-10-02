using Minos.Application;
using Minos.Application.Accounts;
using Minos.Application.Problems;
using Minos.Domain.Accounts;
using Minos.Domain.Problems;
using Xunit;

namespace Minos.IntegrationTests;

public sealed class ProblemAuthoringUseCaseTests
{
    [Fact]
    public async Task Setter_creation_derives_owner_and_solver_is_rejected_before_persistence()
    {
        var account = Account.Create("Author", DateTimeOffset.UtcNow);
        var repository = new RecordingProblemRepository();
        var cases = Cases(account, repository, new RecordingArtifacts());
        var token = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<AccessDeniedException>(() => cases.CreateAsync(new("Title", "Statement"), token));
        Assert.Equal(0, repository.Writes);

        account.AssignRole(AccountRole.Setter);
        var created = await cases.CreateAsync(new("  Title  ", "Statement"), token);
        Assert.Equal(account.Id, repository.Problem!.OwnerAccountId);
        Assert.Equal("Title", created.Title);
        Assert.Equal(1, repository.Writes);
    }

    [Fact]
    public async Task Cross_owner_changes_are_denied_and_disabled_accounts_are_rejected()
    {
        var account = Account.Create("Author", DateTimeOffset.UtcNow);
        account.AssignRole(AccountRole.Setter);
        var repository = new RecordingProblemRepository
        {
            Problem = Problem.Create(Guid.NewGuid(), "Other", "Private", DateTimeOffset.UtcNow)
        };
        var cases = Cases(account, repository, new RecordingArtifacts());
        var token = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<AccessDeniedException>(() => cases.FindDraftAsync(repository.Problem.Id, token));
        Assert.Equal(0, repository.Writes);
        account.Disable();
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => cases.CreateAsync(new("Title", "Statement"), token));
    }

    [Fact]
    public async Task Failed_test_metadata_commit_removes_new_artifacts()
    {
        var account = Account.Create("Author", DateTimeOffset.UtcNow);
        account.AssignRole(AccountRole.Setter);
        var repository = new RecordingProblemRepository
        {
            Problem = Problem.Create(account.Id, "Title", "Statement", DateTimeOffset.UtcNow),
            FailSave = true
        };
        var artifacts = new RecordingArtifacts();
        var cases = Cases(account, repository, artifacts);
        await Assert.ThrowsAsync<IOException>(() => cases.AddTestCaseAsync(repository.Problem.Id, Stream.Null, TestContext.Current.CancellationToken));
        Assert.Equal(2, artifacts.Deleted.Count);
    }

    [Fact]
    public async Task Publication_verifies_artifacts_and_commits_one_immutable_version()
    {
        var account = Account.Create("Author", DateTimeOffset.UtcNow);
        account.AssignRole(AccountRole.Setter);
        var problem = Problem.Create(account.Id, "Title", "Statement", DateTimeOffset.UtcNow);
        problem.ConfigureExecution(1000, 64, BuiltInChecker.Exact, DateTimeOffset.UtcNow);
        problem.ReplaceSamples([new("1", "1")], DateTimeOffset.UtcNow);
        problem.AddTestCase(Artifact("input.bin"), Artifact("output.bin"), DateTimeOffset.UtcNow);
        var repository = new RecordingProblemRepository { Problem = problem };
        var artifacts = new RecordingArtifacts();
        var published = await Cases(account, repository, artifacts).PublishAsync(problem.Id, new(true, true, true), TestContext.Current.CancellationToken);
        Assert.Equal(2, artifacts.Verified);
        Assert.Single(repository.Published);
        Assert.Equal(repository.Published.Single().Id, published.VersionId);
    }

    [Fact]
    public async Task Failed_artifact_verification_creates_no_published_version()
    {
        var account = Account.Create("Author", DateTimeOffset.UtcNow);
        account.AssignRole(AccountRole.Setter);
        var problem = Problem.Create(account.Id, "Title", "Statement", DateTimeOffset.UtcNow);
        problem.ConfigureExecution(1000, 64, BuiltInChecker.Exact, DateTimeOffset.UtcNow);
        problem.ReplaceSamples([new("1", "1")], DateTimeOffset.UtcNow);
        problem.AddTestCase(Artifact("input.bin"), Artifact("output.bin"), DateTimeOffset.UtcNow);
        var repository = new RecordingProblemRepository { Problem = problem };
        var artifacts = new RecordingArtifacts { FailVerification = true };

        await Assert.ThrowsAsync<ArtifactValidationException>(() =>
            Cases(account, repository, artifacts).PublishAsync(problem.Id, new(true, true, true), TestContext.Current.CancellationToken));

        Assert.Empty(repository.Published);
        Assert.Equal(0, problem.LatestPublishedVersion);
    }

    private static ProblemAuthoringUseCases Cases(
        Account account,
        RecordingProblemRepository repository,
        RecordingArtifacts artifacts) =>
        new(repository, artifacts, new(new(new AppAccountStore(account), new TestCurrentAccount(account.Id), TimeProvider.System)), TimeProvider.System);

    private static ProblemArtifactReference Artifact(string name) =>
        ProblemArtifactReference.Create($"tests/{Guid.NewGuid():N}/{name}", new string('a', 64), 1);

    private sealed class RecordingArtifacts : IProblemArtifactStore
    {
        public List<string> Deleted { get; } = [];
        public int Verified { get; private set; }
        public bool FailVerification { get; init; }
        public Task<StoredProblemTestArtifacts> StorePackageAsync(Stream package, CancellationToken token) =>
            Task.FromResult(new StoredProblemTestArtifacts(Artifact("input.bin"), Artifact("output.bin")));
        public Task VerifyAsync(IEnumerable<ProblemArtifactReference> artifacts, CancellationToken token)
        {
            Verified += artifacts.Count();
            if (FailVerification) throw new ArtifactValidationException("invalid");
            return Task.CompletedTask;
        }
        public Task<byte[]> ReadVerifiedAsync(ProblemArtifactReference artifact, CancellationToken token) =>
            Task.FromResult(Array.Empty<byte>());
        public Task DeleteAsync(IEnumerable<string> keys, CancellationToken token)
        {
            Deleted.AddRange(keys);
            return Task.CompletedTask;
        }
        public Task CleanupAbandonedAsync(IReadOnlySet<string> referencedKeys, DateTimeOffset olderThanUtc, CancellationToken token) => Task.CompletedTask;
    }

    private sealed class RecordingProblemRepository : IProblemRepository
    {
        public Problem? Problem { get; set; }
        public int Writes { get; private set; }
        public bool FailSave { get; set; }
        public List<PublishedProblemVersion> Published { get; } = [];
        public Task AddAsync(Problem problem, CancellationToken token) { Writes++; Problem = problem; return Task.CompletedTask; }
        public Task<Problem?> FindForAuthoringAsync(Guid id, CancellationToken token) => Task.FromResult(Problem?.Id == id ? Problem : null);
        public Task SaveAsync(Problem problem, CancellationToken token)
        {
            if (FailSave) throw new IOException("failed");
            Writes++;
            return Task.CompletedTask;
        }
        public Task PublishAsync(Problem problem, PublishedProblemVersion version, CancellationToken token)
        {
            Published.Add(version);
            return Task.CompletedTask;
        }
        public Task<(IReadOnlyList<PublishedProblemSummary> Items, int Total)> ListPublishedAsync(int offset, int limit, CancellationToken token) =>
            Task.FromResult(((IReadOnlyList<PublishedProblemSummary>)Array.Empty<PublishedProblemSummary>(), 0));
        public Task<PublishedProblemVersion?> FindLatestPublishedAsync(Guid problemId, CancellationToken token) => Task.FromResult<PublishedProblemVersion?>(null);
        public Task<PublishedProblemVersion?> FindPublishedVersionAsync(Guid versionId, CancellationToken token) => Task.FromResult<PublishedProblemVersion?>(null);
        public Task<IReadOnlySet<string>> ReferencedArtifactKeysAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
    }
}
