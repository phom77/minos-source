using Minos.Application;
using Minos.Application.Accounts;
using Minos.Application.Evaluation;
using Minos.Application.Problems;
using Minos.Domain.Accounts;
using Minos.Domain.Evaluation;
using Minos.Domain.Problems;
using Xunit;

namespace Minos.IntegrationTests;

public sealed class SubmissionUseCaseTests
{
    [Fact]
    public async Task Admission_derives_owner_and_pins_latest_version_and_toolchain()
    {
        var account = Account.Create("Solver", DateTimeOffset.UtcNow);
        var version = Published(account.Id);
        var submissions = new RecordingSubmissionRepository();
        var useCases = Cases(account, version, submissions);

        var created = await useCases.CreateAsync(version.ProblemId,
            new(SupportedSubmission.Language, "int main(){}"), TestContext.Current.CancellationToken);

        Assert.Equal(account.Id, submissions.Submission!.OwnerAccountId);
        Assert.Equal(version.Id, created.ProblemVersionId);
        Assert.Equal(SupportedSubmission.ToolchainId, created.ToolchainId);
        Assert.NotNull(submissions.Job);
    }

    [Fact]
    public async Task Admission_rejects_unsupported_language_and_limit_before_acknowledgement()
    {
        var account = Account.Create("Solver", DateTimeOffset.UtcNow);
        var version = Published(account.Id);
        var submissions = new RecordingSubmissionRepository { Accept = false };
        var useCases = Cases(account, version, submissions);
        var token = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<Minos.Domain.DomainValidationException>(() =>
            useCases.CreateAsync(version.ProblemId, new("python", "print(1)"), token));
        await Assert.ThrowsAsync<AdmissionLimitExceededException>(() =>
            useCases.CreateAsync(version.ProblemId, new(SupportedSubmission.Language, "int main(){}"), token));
    }

    [Fact]
    public async Task Submission_source_and_attempts_are_owner_only_in_application()
    {
        var owner = Account.Create("Owner", DateTimeOffset.UtcNow);
        var other = Account.Create("Other", DateTimeOffset.UtcNow);
        var version = Published(owner.Id);
        var submission = Submission.Create(owner.Id, version.ProblemId, version.Id,
            SupportedSubmission.Language, SupportedSubmission.ToolchainId, "private source", DateTimeOffset.UtcNow);
        var submissions = new RecordingSubmissionRepository { Submission = submission };
        var useCases = Cases(other, version, submissions);

        await Assert.ThrowsAsync<AccessDeniedException>(() =>
            useCases.FindAsync(submission.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task History_rejects_an_offset_that_cannot_be_represented()
    {
        var account = Account.Create("Solver", DateTimeOffset.UtcNow);
        var version = Published(account.Id);
        var useCases = Cases(account, version, new RecordingSubmissionRepository());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            useCases.ListMineAsync(int.MaxValue, 100, TestContext.Current.CancellationToken));
    }

    private static SubmissionUseCases Cases(
        Account account,
        PublishedProblemVersion version,
        RecordingSubmissionRepository submissions) =>
        new(submissions, new ProblemRepositoryStub(version),
            new(new AppAccountStore(account), new TestCurrentAccount(account.Id), TimeProvider.System), TimeProvider.System);

    private static PublishedProblemVersion Published(Guid owner)
    {
        var now = DateTimeOffset.UtcNow;
        var problem = Problem.Create(owner, "Title", "Statement", now);
        problem.ConfigureExecution(1000, 64, BuiltInChecker.Exact, now);
        problem.ReplaceSamples([new("1", "1")], now);
        problem.AddTestCase(
            ProblemArtifactReference.Create($"tests/{Guid.NewGuid():N}/input.bin", new string('a', 64), 1),
            ProblemArtifactReference.Create($"tests/{Guid.NewGuid():N}/output.bin", new string('b', 64), 1), now);
        return problem.Publish(owner, new(true, true, true), now);
    }

    private sealed class RecordingSubmissionRepository : ISubmissionRepository
    {
        public bool Accept { get; init; } = true;
        public Submission? Submission { get; set; }
        public JudgeJob? Job { get; private set; }
        public Task<bool> AdmitAsync(Submission submission, JudgeJob job, int maximumActive, CancellationToken token)
        {
            Submission = submission;
            Job = job;
            return Task.FromResult(Accept);
        }
        public Task<(Submission Submission, IReadOnlyList<JudgingAttempt> Attempts, SubmissionResult? Result)?> FindAsync(Guid id, CancellationToken token) =>
            Task.FromResult<(Submission, IReadOnlyList<JudgingAttempt>, SubmissionResult?)?>(Submission is null ? null : (Submission, [], null));
        public Task<(IReadOnlyList<(Submission Submission, SubmissionVerdict? Verdict)> Items, int Total)> ListByOwnerAsync(
            Guid ownerAccountId, int offset, int limit, CancellationToken token) =>
            Task.FromResult(((IReadOnlyList<(Submission, SubmissionVerdict?)>)(Submission is null ? [] : [(Submission, null)]), Submission is null ? 0 : 1));
    }

    private sealed class ProblemRepositoryStub(PublishedProblemVersion version) : IProblemRepository
    {
        public Task<PublishedProblemVersion?> FindLatestPublishedAsync(Guid problemId, CancellationToken token) =>
            Task.FromResult<PublishedProblemVersion?>(problemId == version.ProblemId ? version : null);
        public Task AddAsync(Problem problem, CancellationToken token) => throw new NotSupportedException();
        public Task<Problem?> FindForAuthoringAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task SaveAsync(Problem problem, CancellationToken token) => throw new NotSupportedException();
        public Task PublishAsync(Problem problem, PublishedProblemVersion published, CancellationToken token) => throw new NotSupportedException();
        public Task<(IReadOnlyList<PublishedProblemSummary> Items, int Total)> ListPublishedAsync(int offset, int limit, CancellationToken token) => throw new NotSupportedException();
        public Task<PublishedProblemVersion?> FindPublishedVersionAsync(Guid versionId, CancellationToken token) =>
            Task.FromResult<PublishedProblemVersion?>(version.Id == versionId ? version : null);
        public Task<IReadOnlySet<string>> ReferencedArtifactKeysAsync(CancellationToken token) => throw new NotSupportedException();
    }
}
