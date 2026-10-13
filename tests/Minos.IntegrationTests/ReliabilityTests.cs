using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Minos.Api.Accounts;
using Minos.Application.Evaluation;
using Minos.Domain.Accounts;
using Minos.Domain.Evaluation;
using Minos.Infrastructure;
using Minos.Infrastructure.Persistence;
using Xunit;

namespace Minos.IntegrationTests;

public sealed class ReliabilityTests
{
    [Fact]
    public async Task Operations_track_stuck_queue_expired_lease_exhaustion_and_a_fresh_recovery_submission()
    {
        var token = TestContext.Current.CancellationToken;
        await using var workers = Workers();
        await DrainAsync(workers, token);
        await using var factory = AccountTestHost.Factory();
        using var client = AccountTestHost.Client(factory);
        var owner = (await (await AccountTestHost.RegisterAsync(client, AccountTestHost.Email())).Content.ReadFromJsonAsync<AccountResponse>(token))!;
        var seeded = await SubmissionLifecycleTests.SeedPublishedAsync(factory, owner.Id, token);
        var now = DateTimeOffset.UtcNow;
        var submission = Submission.Create(owner.Id, seeded.Problem.Id, seeded.Version.Id, "cpp20", SupportedSubmission.ToolchainId, "int main(){}", now);
        await using (var admission = factory.Services.CreateAsyncScope())
            Assert.True(await admission.ServiceProvider.GetRequiredService<ISubmissionRepository>().AdmitAsync(submission, JudgeJob.Create(submission.Id, now), 5, token));
        await using var observer = factory.Services.CreateAsyncScope();
        var query = observer.ServiceProvider.GetRequiredService<IJudgingOperationsQuery>();
        var queued = await query.ReadAsync(now.AddSeconds(130), token);
        Assert.Equal(1, queued.Pending); Assert.Equal(0, queued.Leased);
        Assert.Equal(130, queued.OldestPendingSeconds);
        async Task<ClaimedSubmissionWork> Claim(DateTimeOffset at)
        {
            await using var scope = workers.CreateAsyncScope();
            return (await scope.ServiceProvider.GetRequiredService<IJudgeJobRepository>().ClaimNextAsync("readiness-recovery", at, token))!;
        }
        await Claim(now.AddSeconds(130));
        var expired = await query.ReadAsync(now.AddSeconds(161), token);
        Assert.Equal(0, expired.Pending); Assert.Equal(1, expired.Leased); Assert.Equal(1, expired.ExpiredLeases);
        var second = await Claim(now.AddSeconds(162));
        await using (var failure = workers.CreateAsyncScope())
            Assert.Equal(AttemptFailureResult.RetryScheduled, await failure.ServiceProvider.GetRequiredService<IJudgeJobRepository>().FailAsync(
                second.AttemptId, second.LeaseToken, second.WorkerId, InfrastructureFailureKind.ExecutorFailure, now.AddSeconds(162), token));
        var retrying = await query.ReadAsync(now.AddSeconds(163), token);
        Assert.Equal(1, retrying.Pending); Assert.Equal(0, retrying.ExpiredLeases);
        Assert.Equal(queued.FailedAttemptsLastHour + 2, retrying.FailedAttemptsLastHour);
        var third = await Claim(now.AddSeconds(168));
        await using (var failure = workers.CreateAsyncScope())
            Assert.Equal(AttemptFailureResult.SystemFailed, await failure.ServiceProvider.GetRequiredService<IJudgeJobRepository>().FailAsync(
                third.AttemptId, third.LeaseToken, third.WorkerId, InfrastructureFailureKind.ExecutorFailure, now.AddSeconds(168), token));
        var failed = await query.ReadAsync(now.AddSeconds(169), token);
        Assert.Equal(0, failed.Pending); Assert.Equal(0, failed.Leased);
        Assert.Equal(queued.SystemFailedLastHour + 1, failed.SystemFailedLastHour);
        Assert.Equal(queued.FailedAttemptsLastHour + 3, failed.FailedAttemptsLastHour);
        Assert.Equal(queued.ExecutorFailuresLastHour + 2, failed.ExecutorFailuresLastHour);
        // Recovery validates a new submission; it never rewrites the exhausted one.
        var fresh = Submission.Create(owner.Id, seeded.Problem.Id, seeded.Version.Id, "cpp20", SupportedSubmission.ToolchainId, "int main(){}", now.AddSeconds(170));
        await using (var admission = factory.Services.CreateAsyncScope())
            Assert.True(await admission.ServiceProvider.GetRequiredService<ISubmissionRepository>().AdmitAsync(fresh, JudgeJob.Create(fresh.Id, fresh.CreatedAtUtc), 5, token));
        var recovered = await Claim(now.AddSeconds(171));
        var outcome = JudgingOutcome.Create(SubmissionVerdict.Accepted, "", [new(1, TestCaseResultStatus.Passed, 1, 1, 1024, 0)]);
        await using (var completion = workers.CreateAsyncScope())
            Assert.Equal(LeaseMutationResult.Accepted, await completion.ServiceProvider.GetRequiredService<IJudgeJobRepository>().CompleteAsync(
                recovered.AttemptId, recovered.LeaseToken, recovered.WorkerId, outcome, now.AddSeconds(171), token));
        var final = await query.ReadAsync(now.AddSeconds(172), token);
        Assert.Equal(0, final.Pending); Assert.Equal(0, final.ExpiredLeases);
        Assert.Equal(failed.SystemFailedLastHour, final.SystemFailedLastHour);
        Assert.Equal(queued.CompletedLastHour + 1, final.CompletedLastHour);
    }

    [Fact]
    public async Task Operations_authorization_applies_to_direct_use_cases_and_disabled_administrators()
    {
        var token = TestContext.Current.CancellationToken;
        var account = Account.Create("Operator", DateTimeOffset.UtcNow);
        var accounts = new Minos.Application.Accounts.AccountUseCases(new AppAccountStore(account), new TestCurrentAccount(account.Id), TimeProvider.System);
        var cases = new JudgingOperationsUseCases(accounts, new UnexpectedOperationsQuery(), TimeProvider.System);
        await Assert.ThrowsAsync<Minos.Application.AccessDeniedException>(() => cases.ReadAsync(token));
        account.AssignRole(AccountRole.Setter);
        await Assert.ThrowsAsync<Minos.Application.AccessDeniedException>(() => cases.ReadAsync(token));
        account.AssignRole(AccountRole.Administrator);
        account.Disable();
        await Assert.ThrowsAsync<Minos.Application.Accounts.AccountAuthenticationException>(() => cases.ReadAsync(token));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public void Invalid_global_capacity_fails_configuration(int capacity) =>
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddMinosInfrastructure(
            "Host=localhost;Database=unused;Username=unused", maximumActiveSubmissions: capacity));

    [Fact]
    public async Task Concurrent_admission_across_accounts_obeys_global_limit_and_releases_capacity()
    {
        var token = TestContext.Current.CancellationToken;
        await using var workers = Workers();
        await DrainAsync(workers, token);
        await using var factory = AccountTestHost.Factory();
        var candidates = new List<Submission>();
        for (var index = 0; index < 8; index++)
        {
            using var client = AccountTestHost.Client(factory);
            var owner = (await (await AccountTestHost.RegisterAsync(client, AccountTestHost.Email()))
                .Content.ReadFromJsonAsync<AccountResponse>(token))!;
            var seeded = await SubmissionLifecycleTests.SeedPublishedAsync(factory, owner.Id, token);
            candidates.Add(Submission.Create(owner.Id, seeded.Problem.Id, seeded.Version.Id,
                "cpp20", SupportedSubmission.ToolchainId, "int main(){}", DateTimeOffset.UtcNow));
        }
        var services = new ServiceCollection();
        services.AddMinosInfrastructure(PostgresPrivilegeTests.Connection("ConnectionStrings__Minos"), maximumActiveSubmissions: 3);
        await using var admissions = services.BuildServiceProvider();
        var accepted = await Task.WhenAll(candidates.Select(async submission =>
        {
            await using var scope = admissions.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ISubmissionRepository>().AdmitAsync(
                submission, JudgeJob.Create(submission.Id, submission.CreatedAtUtc), 5, token);
        }));
        Assert.Equal(3, accepted.Count(result => result));
        await using (var database = PostgresPrivilegeTests.Database(PostgresPrivilegeTests.Connection("MINOS_TEST_MIGRATION_CONNECTION")))
        {
            var ids = candidates.Select(candidate => candidate.Id).ToArray();
            Assert.Equal(3, await database.Submissions.CountAsync(submission => ids.Contains(submission.Id), token));
            Assert.Equal(3, await database.JudgeJobs.CountAsync(job => ids.Contains(job.SubmissionId), token));
        }
        await DrainAsync(workers, token);
        var rejected = candidates[Array.FindIndex(accepted, result => !result)];
        await using var retryScope = admissions.CreateAsyncScope();
        Assert.True(await retryScope.ServiceProvider.GetRequiredService<ISubmissionRepository>().AdmitAsync(
            rejected, JudgeJob.Create(rejected.Id, rejected.CreatedAtUtc), 5, token));
        await DrainAsync(workers, token);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_around_result_commit_is_atomic_and_retry_is_idempotent(bool afterCommit)
    {
        var token = TestContext.Current.CancellationToken;
        var gate = new CommitFault(afterCommit);
        await using var workers = Workers(gate);
        await DrainAsync(workers, token);
        await using var factory = AccountTestHost.Factory();
        using var client = AccountTestHost.Client(factory);
        var owner = (await (await AccountTestHost.RegisterAsync(client, AccountTestHost.Email()))
            .Content.ReadFromJsonAsync<AccountResponse>(token))!;
        var seeded = await SubmissionLifecycleTests.SeedPublishedAsync(factory, owner.Id, token);
        var submission = Submission.Create(owner.Id, seeded.Problem.Id, seeded.Version.Id,
            "cpp20", SupportedSubmission.ToolchainId, "int main(){}", DateTimeOffset.UtcNow);
        await using (var scope = factory.Services.CreateAsyncScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<ISubmissionRepository>().AdmitAsync(
                submission, JudgeJob.Create(submission.Id, submission.CreatedAtUtc), 5, token));
        ClaimedSubmissionWork work;
        await using (var scope = workers.CreateAsyncScope())
            work = (await scope.ServiceProvider.GetRequiredService<IJudgeJobRepository>()
                .ClaimNextAsync("m7-commit", DateTimeOffset.UtcNow, token))!;
        var outcome = JudgingOutcome.Create(SubmissionVerdict.Accepted, "", [new(1, TestCaseResultStatus.Passed, 1, 1, 1024, 0)]);
        gate.Armed = true;
        await using (var scope = workers.CreateAsyncScope())
            await Assert.ThrowsAsync<IOException>(() => scope.ServiceProvider.GetRequiredService<IJudgeJobRepository>()
                .CompleteAsync(work.AttemptId, work.LeaseToken, work.WorkerId, outcome, DateTimeOffset.UtcNow, token));
        await using (var database = PostgresPrivilegeTests.Database(PostgresPrivilegeTests.Connection("MINOS_TEST_MIGRATION_CONNECTION")))
        {
            Assert.Equal(afterCommit ? SubmissionStatus.Completed : SubmissionStatus.Judging,
                (await database.Submissions.SingleAsync(candidate => candidate.Id == submission.Id, token)).Status);
            Assert.Equal(afterCommit ? 1 : 0, await database.SubmissionResults.CountAsync(result => result.SubmissionId == submission.Id, token));
            Assert.Equal(afterCommit ? 1 : 0, await database.TestCaseResults.CountAsync(result => result.SubmissionId == submission.Id, token));
        }
        gate.Armed = false;
        await using var completion = workers.CreateAsyncScope();
        Assert.Equal(LeaseMutationResult.Accepted, await completion.ServiceProvider.GetRequiredService<IJudgeJobRepository>()
            .CompleteAsync(work.AttemptId, work.LeaseToken, work.WorkerId, outcome, DateTimeOffset.UtcNow, token));
        await using var final = PostgresPrivilegeTests.Database(PostgresPrivilegeTests.Connection("MINOS_TEST_MIGRATION_CONNECTION"));
        Assert.Equal(1, await final.SubmissionResults.CountAsync(result => result.SubmissionId == submission.Id, token));
        Assert.Equal(1, await final.TestCaseResults.CountAsync(result => result.SubmissionId == submission.Id, token));
    }

    [Fact]
    public async Task Operations_are_administrator_only_at_http_and_application_boundaries()
    {
        var token = TestContext.Current.CancellationToken;
        await using var factory = AccountTestHost.Factory();
        using var anonymous = AccountTestHost.Client(factory);
        using var client = AccountTestHost.Client(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/operations/judging", token)).StatusCode);
        var owner = (await (await AccountTestHost.RegisterAsync(client, AccountTestHost.Email()))
            .Content.ReadFromJsonAsync<AccountResponse>(token))!;
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/operations/judging", token)).StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
            await Assert.ThrowsAsync<Minos.Application.Accounts.AccountAuthenticationException>(() =>
                scope.ServiceProvider.GetRequiredService<JudgingOperationsUseCases>().ReadAsync(token));
        await AccountTestHost.SetRoleAsync(factory, owner.Id, AccountRole.Administrator);
        var response = await client.GetAsync("/api/operations/judging", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        var json = await response.Content.ReadAsStringAsync(token);
        foreach (var secret in new[] { "source", "leaseToken", "artifact", "compilerDiagnostics", "workerId" })
            Assert.DoesNotContain(secret, json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expiredLeases", json, StringComparison.Ordinal);
    }

    private static ServiceProvider Workers(CommitFault? fault = null)
    {
        var services = new ServiceCollection();
        services.AddMinosInfrastructure(PostgresPrivilegeTests.Connection("MINOS_TEST_WORKER_CONNECTION"));
        if (fault is not null) services.AddDbContext<MinosDbContext>(options => options.AddInterceptors(fault));
        return services.BuildServiceProvider();
    }

    private static async Task DrainAsync(ServiceProvider workers, CancellationToken token)
    {
        for (var count = 0; count < 500; count++)
        {
            await using var scope = workers.CreateAsyncScope();
            var jobs = scope.ServiceProvider.GetRequiredService<IJudgeJobRepository>();
            var now = DateTimeOffset.UtcNow.AddDays(count + 1);
            var work = await jobs.ClaimNextAsync("m7-drain", now, token);
            if (work is null) return;
            await jobs.FailAsync(work.AttemptId, work.LeaseToken, work.WorkerId,
                InfrastructureFailureKind.ExecutorFailure, now.AddMilliseconds(1), token);
        }
        throw new InvalidOperationException("Unexpected fixture queue growth.");
    }

    private sealed class CommitFault(bool afterCommit) : DbTransactionInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Armed && !afterCommit) throw new IOException("M7 failure before commit");
            return ValueTask.FromResult(result);
        }
        public override Task TransactionCommittedAsync(DbTransaction transaction,
            TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (Armed && afterCommit) throw new IOException("M7 lost commit acknowledgement");
            return Task.CompletedTask;
        }
    }

    private sealed class UnexpectedOperationsQuery : IJudgingOperationsQuery
    {
        public Task<JudgingOperationsSnapshot> ReadAsync(DateTimeOffset nowUtc, CancellationToken token) =>
            throw new InvalidOperationException("Unauthorized callers must never query operational data.");
    }
}
