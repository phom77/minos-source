using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Minos.Api.Accounts;
using Minos.Api.Contests;
using Minos.Api.Submissions;
using Minos.Application.Accounts;
using Minos.Application.Contests;
using Minos.Application.Evaluation;
using Minos.Application.Problems;
using Minos.Domain.Accounts;
using Minos.Domain.Problems;
using Minos.Infrastructure.Persistence;
using Minos.Infrastructure;
using Npgsql;
using Xunit;

namespace Minos.IntegrationTests;

public sealed class SubmissionIdempotencyTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Concurrent_practice_retries_commit_one_submission_job_and_key_and_conflict_safely()
    {
        await using var factory = AccountTestHost.Factory();
        using var client = AccountTestHost.Client(factory);
        var account = await RegisterAsync(client);
        var version = await SeedAsync(factory, account.Id);
        var path = $"/api/problems/{version.ProblemId}/submissions";
        var request = new CreateSubmissionRequest("cpp20", "int main(){}", Guid.NewGuid());
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.PostAsJsonAsync(path, request, Token)));
        var ids = new List<Guid>();
        foreach (var response in responses) ids.Add((await ReadAsync<SubmissionResponse>(response)).Id);
        Assert.Single(ids.Distinct());
        var id = ids[0];
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(path, request with { Source = "int main(){return 1;}" }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(path, request with { Language = "cpp17" }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/problems/{Guid.NewGuid()}/submissions", request, Token)).StatusCode);
        await using var db = Database();
        Assert.Equal(1, await db.Submissions.CountAsync(s => s.OwnerAccountId == account.Id, Token));
        Assert.Equal(1, await db.JudgeJobs.CountAsync(j => j.SubmissionId == id, Token));
        Assert.Equal(1L, await RequestCountAsync(db, account.Id));
    }

    [Fact]
    public async Task Lost_response_can_be_replayed_after_restart_republication_and_archive_without_changing_the_pin()
    {
        var email = AccountTestHost.Email();
        var request = new CreateSubmissionRequest("cpp20", "int main(){}", Guid.NewGuid());
        Guid id, problemId, versionId;
        await using (var factory = AccountTestHost.Factory())
        using (var client = AccountTestHost.Client(factory))
        {
            var account = await RegisterAsync(client, email);
            var version = await SeedAsync(factory, account.Id); problemId = version.ProblemId; versionId = version.Id;
            // Discard the first response body, as a disconnected caller would.
            using var response = await client.PostAsJsonAsync($"/api/problems/{problemId}/submissions", request, Token);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            id = Guid.Parse(response.Headers.Location!.OriginalString.Split('/').Last());
            await using var scope = factory.Services.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IProblemRepository>();
            var problem = (await repository.FindForAuthoringAsync(problemId, Token))!;
            problem.EditContent("Revised", "Revised statement", DateTimeOffset.UtcNow);
            var next = problem.Publish(account.Id, new(true, true, true), DateTimeOffset.UtcNow);
            await repository.PublishAsync(problem, next, Token);
            problem.Archive(DateTimeOffset.UtcNow);
            await repository.SaveAsync(problem, Token);
        }
        await using var restarted = AccountTestHost.Factory();
        using var owner = AccountTestHost.Client(restarted);
        await AccountTestHost.LoginAsync(owner, email);
        var replay = await ReadAsync<SubmissionResponse>(await owner.PostAsJsonAsync($"/api/problems/{problemId}/submissions", request, Token));
        Assert.Equal(id, replay.Id); Assert.Equal(versionId, replay.ProblemVersionId);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsJsonAsync($"/api/problems/{problemId}/submissions", request with { RequestKey = Guid.NewGuid() }, Token)).StatusCode);
    }

    [Fact]
    public async Task Replays_bypass_capacity_but_rejected_new_admission_rolls_back_key_and_allows_later_retry()
    {
        await using var factory = AccountTestHost.Factory();
        using var client = AccountTestHost.Client(factory);
        var account = await RegisterAsync(client); var version = await SeedAsync(factory, account.Id);
        var path = $"/api/problems/{version.ProblemId}/submissions";
        var first = new CreateSubmissionRequest("cpp20", "int main(){}", Guid.NewGuid());
        var accepted = await ReadAsync<SubmissionResponse>(await client.PostAsJsonAsync(path, first, Token));
        for (var i = 1; i < SupportedSubmission.MaximumActivePerAccount; i++)
            await ReadAsync<SubmissionResponse>(await client.PostAsJsonAsync(path, first with { RequestKey = Guid.NewGuid() }, Token));
        Assert.Equal(accepted.Id, (await ReadAsync<SubmissionResponse>(await client.PostAsJsonAsync(path, first, Token))).Id);
        var rejected = first with { RequestKey = Guid.NewGuid() };
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync(path, rejected, Token)).StatusCode);
        await using var db = Database();
        Assert.Equal(5L, await RequestCountAsync(db, account.Id));
        Assert.Equal(5, await db.Submissions.CountAsync(s => s.OwnerAccountId == account.Id, Token));
        // Release one slot through the real lease/guarded completion path, never by overwriting a submission.
        await using (var workers = new ServiceCollection().AddLogging().AddMinosInfrastructure(PostgresPrivilegeTests.Connection("MINOS_TEST_WORKER_CONNECTION")).BuildServiceProvider())
        await using (var scope = workers.CreateAsyncScope())
        {
            var jobs = scope.ServiceProvider.GetRequiredService<IJudgeJobRepository>();
            var job = await db.JudgeJobs.SingleAsync(j => j.SubmissionId == accepted.Id, Token);
            var work = (await jobs.ClaimAsync(job.Id, "phase1-fixture", DateTimeOffset.UtcNow, Token))!;
            var outcome = Minos.Domain.Evaluation.JudgingOutcome.Create(Minos.Domain.Evaluation.SubmissionVerdict.Accepted, "",
                [new(1, Minos.Domain.Evaluation.TestCaseResultStatus.Passed, 1, 1, 1024, 0)]);
            Assert.Equal(LeaseMutationResult.Accepted, await jobs.CompleteAsync(work.AttemptId, work.LeaseToken, work.WorkerId, outcome, DateTimeOffset.UtcNow, Token));
        }
        await ReadAsync<SubmissionResponse>(await client.PostAsJsonAsync(path, rejected, Token));
        Assert.Equal(6L, await RequestCountAsync(db, account.Id));
    }

    [Fact]
    public async Task Keys_are_account_scoped_and_disabled_accounts_cannot_replay_even_without_HTTP()
    {
        await using var factory = AccountTestHost.Factory();
        using var owner = AccountTestHost.Client(factory); using var other = AccountTestHost.Client(factory);
        var account = await RegisterAsync(owner); var outsider = await RegisterAsync(other);
        var version = await SeedAsync(factory, account.Id);
        var request = new CreateSubmissionRequest("cpp20", "int main(){}", Guid.NewGuid());
        var path = $"/api/problems/{version.ProblemId}/submissions";
        var first = await ReadAsync<SubmissionResponse>(await owner.PostAsJsonAsync(path, request, Token));
        var second = await ReadAsync<SubmissionResponse>(await other.PostAsJsonAsync(path, request, Token));
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync($"/api/submissions/{first.Id}", Token)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MinosDbContext>();
        await db.Accounts.Where(a => a.Id == account.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.IsDisabled, true), Token);
        db.ChangeTracker.Clear();
        var current = new TestCurrentAccount(account.Id);
        var accounts = new AccountUseCases(scope.ServiceProvider.GetRequiredService<IAccountStore>(), current, TimeProvider.System);
        var cases = new SubmissionUseCases(scope.ServiceProvider.GetRequiredService<ISubmissionRepository>(), scope.ServiceProvider.GetRequiredService<IProblemRepository>(), accounts, TimeProvider.System);
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => cases.CreateAsync(version.ProblemId, new("cpp20", request.Source, request.RequestKey), Token));
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.PostAsJsonAsync(path, request, Token)).StatusCode);
    }

    [Fact]
    public async Task Contest_retry_after_end_returns_same_work_but_new_keys_and_cross_route_keys_cannot_create_work()
    {
        var clock = new Clock();
        await using var factory = AccountTestHost.Factory().WithWebHostBuilder(b => b.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock))));
        using var author = AccountTestHost.Client(factory); using var participant = AccountTestHost.Client(factory); using var other = AccountTestHost.Client(factory);
        var owner = await RegisterAsync(author); var user = await RegisterAsync(participant); await RegisterAsync(other);
        await AccountTestHost.SetRoleAsync(factory, owner.Id, AccountRole.Setter); await AccountTestHost.CsrfAsync(author);
        var version = await SeedAsync(factory, owner.Id);
        var draft = await ReadAsync<ContestResponse>(await author.PostAsJsonAsync("/api/contests", new SaveContestRequest("Phase 1", "", clock.Now.AddHours(1), clock.Now.AddHours(2), [version.Id], null), Token));
        var contest = await ReadAsync<ContestResponse>(await author.PostAsJsonAsync($"/api/contests/{draft.Id}/publish", new PublishContestRequest(draft.Revision), Token), HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.OK, (await participant.PutAsync($"/api/contests/{contest.Id}/registration", null, Token)).StatusCode);
        clock.Now = contest.StartsAtUtc;
        var path = $"/api/contests/{contest.Id}/problems/{contest.Problems.Single().Id}/submissions";
        var request = new ContestSubmissionRequest("cpp20", "int main(){}", Guid.NewGuid());
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => participant.PostAsJsonAsync(path, request, Token)));
        var ids = new List<Guid>();
        foreach (var response in responses) ids.Add((await ReadAsync<CreatedContestSubmissionResponse>(response)).Id);
        Assert.Single(ids.Distinct());
        clock.Now = contest.EndsAtUtc;
        Assert.Equal(ids[0], (await ReadAsync<CreatedContestSubmissionResponse>(await participant.PostAsJsonAsync(path, request, Token))).Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await participant.PostAsJsonAsync(path, request with { RequestKey = Guid.NewGuid() }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await participant.PostAsJsonAsync(path, request with { Source = "int main(){return 1;}" }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await participant.PostAsJsonAsync($"/api/problems/{version.ProblemId}/submissions", new CreateSubmissionRequest("cpp20", request.Source, request.RequestKey), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsJsonAsync(path, request, Token)).StatusCode);
        await using var db = Database();
        Assert.Equal(1, await db.ContestSubmissions.CountAsync(s => s.ContestId == contest.Id, Token));
        Assert.Equal(1L, await RequestCountAsync(db, user.Id));
        await using var scope = factory.Services.CreateAsyncScope();
        var current = new TestCurrentAccount(user.Id);
        var accounts = new AccountUseCases(scope.ServiceProvider.GetRequiredService<IAccountStore>(), current, clock);
        var cases = new ContestUseCases(scope.ServiceProvider.GetRequiredService<IContestRepository>(), scope.ServiceProvider.GetRequiredService<IProblemRepository>(), accounts, current, clock, scope.ServiceProvider.GetRequiredService<ISubmissionRepository>());
        Assert.Equal(ids[0], await cases.SubmitAsync(contest.Id, contest.Problems.Single().Id, new("cpp20", request.Source, request.RequestKey), Token));
        await db.Accounts.Where(a => a.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.IsDisabled, true), Token);
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => cases.SubmitAsync(contest.Id, contest.Problems.Single().Id, new("cpp20", request.Source, request.RequestKey), Token));
    }

    [Fact]
    public async Task PostgreSQL_rejects_a_key_linked_to_another_accounts_submission()
    {
        await using var factory = AccountTestHost.Factory(); using var owner = AccountTestHost.Client(factory); using var other = AccountTestHost.Client(factory);
        var account = await RegisterAsync(owner); var outsider = await RegisterAsync(other); var version = await SeedAsync(factory, account.Id);
        var submission = await ReadAsync<SubmissionResponse>(await owner.PostAsJsonAsync($"/api/problems/{version.ProblemId}/submissions", new CreateSubmissionRequest("cpp20", "int main(){}"), Token));
        await using var db = Database(); var key = Guid.NewGuid(); var fingerprint = new string('a', 64);
        var failure = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO evaluation.submission_requests (account_id,request_key,fingerprint,submission_id) VALUES ({outsider.Id},{key},{fingerprint},{submission.Id})", Token));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, failure.SqlState);
        Assert.Equal(0L, await RequestCountAsync(db, outsider.Id));
    }

    [Fact]
    public async Task Empty_or_malformed_request_keys_are_safe_validation_errors()
    {
        await using var factory = AccountTestHost.Factory(); using var client = AccountTestHost.Client(factory);
        var account = await RegisterAsync(client); var version = await SeedAsync(factory, account.Id);
        foreach (var key in new[] { Guid.Empty.ToString(), "not-a-guid" })
        {
            var response = await client.PostAsJsonAsync($"/api/problems/{version.ProblemId}/submissions", new { language = "cpp20", source = "int main(){}", requestKey = key }, Token);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.DoesNotContain("stack", await response.Content.ReadAsStringAsync(Token), StringComparison.OrdinalIgnoreCase);
        }
    }

    private static MinosDbContext Database() => PostgresPrivilegeTests.Database(PostgresPrivilegeTests.Connection("MINOS_TEST_MIGRATION_CONNECTION"));
    private static async Task<long> RequestCountAsync(MinosDbContext db, Guid accountId) =>
        await db.Database.SqlQuery<long>($"SELECT count(*) AS \"Value\" FROM evaluation.submission_requests WHERE account_id = {accountId}").SingleAsync(Token);
    private static async Task<AccountResponse> RegisterAsync(HttpClient client, string? email = null)
    {
        var account = await ReadAsync<AccountResponse>(await AccountTestHost.RegisterAsync(client, email ?? AccountTestHost.Email()));
        await AccountTestHost.CsrfAsync(client); return account;
    }
    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.Created)
    {
        Assert.Equal(expected, response.StatusCode); return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }
    private static async Task<PublishedProblemVersion> SeedAsync(WebApplicationFactory<Program> factory, Guid owner)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IProblemRepository>(); var now = DateTimeOffset.UtcNow;
        var p = Problem.Create(owner, "Phase 1 fixture", "Print the sum.", now);
        p.ConfigureExecution(1000, 64, BuiltInChecker.Exact, now); p.ReplaceSamples([new("1 2", "3")], now);
        p.AddTestCase(ProblemArtifactReference.Create($"tests/{Guid.NewGuid():N}/input.bin", new string('a', 64), 1),
            ProblemArtifactReference.Create($"tests/{Guid.NewGuid():N}/output.bin", new string('b', 64), 1), now);
        await repository.AddAsync(p, Token); var version = p.Publish(owner, new(true, true, true), now);
        await repository.PublishAsync(p, version, Token); return version;
    }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
}
