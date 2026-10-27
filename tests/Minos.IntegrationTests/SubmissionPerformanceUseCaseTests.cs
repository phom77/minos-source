using Minos.Application;
using Minos.Application.Accounts;
using Minos.Application.Evaluation;
using Minos.Domain.Accounts;
using Minos.Domain.Evaluation;
using Xunit;

namespace Minos.IntegrationTests;

public sealed class SubmissionPerformanceUseCaseTests
{
    [Fact]
    public async Task Comparison_enforces_active_account_and_owner_before_aggregation()
    {
        var owner = Account.Create("Owner", DateTimeOffset.UtcNow);
        var query = new RecordingQuery { Target = Target(owner.Id) };
        var other = Account.Create("Other", DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<AccessDeniedException>(() => Cases(other, query).ReadAsync(query.Target.Id, TestContext.Current.CancellationToken));
        other.Disable();
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => Cases(other, query).ReadAsync(query.Target.Id, TestContext.Current.CancellationToken));
        query.Target = null;
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Cases(owner, query).ReadAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));
        Assert.Equal(0, query.Aggregations);
    }

    [Fact]
    public async Task Legacy_and_unsuccessful_results_never_enter_the_comparison_query()
    {
        var owner = Account.Create("Owner", DateTimeOffset.UtcNow);
        var query = new RecordingQuery { Target = Target(owner.Id) with { MeasurementProfileId = null } };
        var result = await Cases(owner, query).ReadAsync(query.Target.Id, TestContext.Current.CancellationToken);
        Assert.Equal("Unprofiled", result.Status);
        Assert.Equal(10, result.Runtime!.Value);
        query.Target = Target(owner.Id) with { Verdict = SubmissionVerdict.WrongAnswer };
        Assert.Equal("NotAccepted", (await Cases(owner, query).ReadAsync(query.Target.Id, TestContext.Current.CancellationToken)).Status);
        Assert.Equal(0, query.Aggregations);
    }

    private static SubmissionPerformanceUseCases Cases(Account account, RecordingQuery query) => new(query,
        new(new AppAccountStore(account), new TestCurrentAccount(account.Id), TimeProvider.System));
    private static SubmissionPerformanceTarget Target(Guid owner) => new(Guid.NewGuid(), owner, Guid.NewGuid(), "cpp20",
        SupportedSubmission.ToolchainId, SubmissionStatus.Completed, SubmissionVerdict.Accepted, 10, 4096, new string('a', 64));
    private sealed class RecordingQuery : ISubmissionPerformanceQuery
    {
        public SubmissionPerformanceTarget? Target { get; set; }
        public int Aggregations { get; private set; }
        public Task<SubmissionPerformanceTarget?> FindTargetAsync(Guid id, CancellationToken token) => Task.FromResult(Target);
        public Task<SubmissionPerformanceDistribution> ReadDistributionAsync(SubmissionPerformanceTarget target, CancellationToken token)
        {
            Aggregations++;
            throw new InvalidOperationException("Unauthorized aggregation");
        }
    }
}
