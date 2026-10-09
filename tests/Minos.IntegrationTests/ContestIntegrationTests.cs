using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Minos.Api.Accounts;
using Minos.Api.Contests;
using Minos.Api.Problems;
using Minos.Api.Submissions;
using Minos.Application;
using Minos.Application.Accounts;
using Minos.Application.Contests;
using Minos.Application.Problems;
using Minos.Domain.Accounts;
using Minos.Domain.Problems;
using Minos.Infrastructure.Persistence;
using Npgsql;
using Xunit;

namespace Minos.IntegrationTests;

public sealed class ContestIntegrationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    [Fact]
    public async Task Draft_access_roles_csrf_revision_and_publication_are_enforced()
    {
        var clock = new ContestClock(); await using var factory = Factory(clock);
        using var author = AccountTestHost.Client(factory); var owner = await AccountAsync(factory, author, AccountRole.Setter);
        using var other = AccountTestHost.Client(factory); await AccountAsync(factory, other, AccountRole.Setter);
        using var solver = AccountTestHost.Client(factory); await AccountAsync(factory, solver, AccountRole.Solver);
        using var guest = AccountTestHost.Client(factory);
        var version = await SeedAsync(factory, owner.Id);
        var request = Request(clock, version.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await solver.PostAsJsonAsync("/api/contests", request, Token)).StatusCode);
        var c = await ReadAsync<ContestResponse>(await author.PostAsJsonAsync("/api/contests", request, Token), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/contests/{c.Id}", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/contests/{c.Id}", Token)).StatusCode);
        var publicList = await guest.GetFromJsonAsync<ContestPageResponse>("/api/contests", Token);
        Assert.DoesNotContain(publicList!.Items, item => item.Id == c.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PutAsJsonAsync($"/api/contests/{c.Id}", request with { Revision = c.Revision }, Token)).StatusCode);
        var updated = await ReadAsync<ContestResponse>(await author.PutAsJsonAsync($"/api/contests/{c.Id}", request with { Title = "Updated", Revision = c.Revision }, Token));
        Assert.Equal(HttpStatusCode.Conflict, (await author.PutAsJsonAsync($"/api/contests/{c.Id}", request with { Revision = c.Revision }, Token)).StatusCode);
        author.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PostAsJsonAsync($"/api/contests/{c.Id}/publish", new PublishContestRequest(updated.Revision), Token)).StatusCode);
        await AccountTestHost.CsrfAsync(author);
        var published = await ReadAsync<ContestResponse>(await author.PostAsJsonAsync($"/api/contests/{c.Id}/publish", new PublishContestRequest(updated.Revision), Token));
        Assert.Equal("Scheduled", published.State);
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PutAsJsonAsync($"/api/contests/{c.Id}", request with { Revision = published.Revision }, Token)).StatusCode);
        var visible = await guest.GetFromJsonAsync<ContestResponse>($"/api/contests/{c.Id}", Token);
        Assert.Empty(visible!.Problems); Assert.False(visible.CanManage);
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PutAsync($"/api/contests/{c.Id}/registration", null, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.GetAsync("/api/contests?page=2147483647&pageSize=100", Token)).StatusCode);
    }

    [Fact]
    public async Task Registration_is_idempotent_and_submission_pins_version_atomically_with_membership_and_time_checks()
    {
        var clock = new ContestClock(); await using var factory = Factory(clock);
        using var author = AccountTestHost.Client(factory); var owner = await AccountAsync(factory, author, AccountRole.Setter);
        using var participant = AccountTestHost.Client(factory); var user = await AccountAsync(factory, participant, AccountRole.Solver);
        using var outsider = AccountTestHost.Client(factory); await AccountAsync(factory, outsider, AccountRole.Solver);
        var version = await SeedAsync(factory, owner.Id);
        var c = await CreatePublishedAsync(author, clock, version.Id); var problem = c.Problems.Single();
        var path = $"/api/contests/{c.Id}/problems/{problem.Id}";
        var registrations = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => participant.PutAsync($"/api/contests/{c.Id}/registration", null, Token)));
        Assert.All(registrations, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(HttpStatusCode.Forbidden, (await participant.GetAsync(path, Token)).StatusCode);
        var source = new ContestSubmissionRequest("cpp20", "int main(){}");
        Assert.Equal(HttpStatusCode.BadRequest, (await participant.PostAsJsonAsync(path + "/submissions", source, Token)).StatusCode);
        clock.Now = c.StartsAtUtc;
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.PostAsJsonAsync(path + "/submissions", source, Token)).StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IProblemRepository>();
            var draft = (await repository.FindForAuthoringAsync(version.ProblemId, Token))!;
            draft.EditContent("Changed after contest", "Different statement", clock.Now);
            await repository.PublishAsync(draft, draft.Publish(owner.Id, new(true, true, true), clock.Now), Token);
            draft.Archive(clock.Now); await repository.SaveAsync(draft, Token);
        }
        var pinned = await participant.GetFromJsonAsync<PublishedProblemResponse>(path, Token);
        Assert.Equal(version.Id, pinned!.VersionId);
        var created = await ReadAsync<CreatedContestSubmissionResponse>(await participant.PostAsJsonAsync(path + "/submissions", source, Token), HttpStatusCode.Created);
        var submission = await participant.GetFromJsonAsync<SubmissionResponse>($"/api/submissions/{created.Id}", Token);
        Assert.Equal(version.Id, submission!.ProblemVersionId);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MinosDbContext>();
            Assert.Equal(1, await db.ContestRegistrations.CountAsync(r => r.ContestId == c.Id && r.AccountId == user.Id, Token));
            Assert.True(await db.ContestSubmissions.AnyAsync(s => s.SubmissionId == created.Id && s.AccountId == user.Id, Token));
            Assert.True(await db.JudgeJobs.AnyAsync(j => j.SubmissionId == created.Id, Token));
        }
        var history = await participant.GetFromJsonAsync<ContestSubmissionPageResponse>($"/api/contests/{c.Id}/submissions/mine", Token);
        Assert.Single(history!.Items);
        Assert.Empty((await outsider.GetFromJsonAsync<ContestSubmissionPageResponse>($"/api/contests/{c.Id}/submissions/mine", Token))!.Items);
        clock.Now = c.EndsAtUtc;
        Assert.Equal(HttpStatusCode.BadRequest, (await participant.PostAsJsonAsync(path + "/submissions", source, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await outsider.PutAsync($"/api/contests/{c.Id}/registration", null, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await participant.PutAsync($"/api/contests/{c.Id}/registration", null, Token)).StatusCode);
    }

    [Fact]
    public async Task Use_cases_enforce_roles_and_active_accounts_without_HTTP()
    {
        var clock = new ContestClock(); await using var factory = Factory(clock);
        using var client = AccountTestHost.Client(factory); var user = await AccountAsync(factory, client, AccountRole.Solver);
        await using var scope = factory.Services.CreateAsyncScope(); var provider = scope.ServiceProvider;
        var accountCases = new AccountUseCases(provider.GetRequiredService<IAccountStore>(), new TestCurrentAccount(user.Id), clock);
        var cases = new ContestUseCases(provider.GetRequiredService<IContestRepository>(), provider.GetRequiredService<IProblemRepository>(), accountCases, new TestCurrentAccount(user.Id), clock);
        var command = new SaveContest("Title", "", clock.Now.AddHours(1), clock.Now.AddHours(2), []);
        await Assert.ThrowsAsync<AccessDeniedException>(() => cases.CreateAsync(command, Token));
        var db = provider.GetRequiredService<MinosDbContext>();
        await db.Accounts.Where(a => a.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.IsDisabled, true), Token);
        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => cases.CreateAsync(command, Token));
    }
    [Fact]
    public async Task Contest_and_practice_share_capacity_and_rejected_admissions_leave_no_links_or_jobs()
    {
        var clock = new ContestClock(); await using var factory = Factory(clock);
        using var author = AccountTestHost.Client(factory); var owner = await AccountAsync(factory, author, AccountRole.Setter);
        using var participant = AccountTestHost.Client(factory); var user = await AccountAsync(factory, participant, AccountRole.Solver);
        var version = await SeedAsync(factory, owner.Id); var c = await CreatePublishedAsync(author, clock, version.Id);
        await ReadAsync<ContestResponse>(await participant.PutAsync($"/api/contests/{c.Id}/registration", null, Token));
        clock.Now = c.StartsAtUtc;
        var request = new ContestSubmissionRequest("cpp20", "int main(){}");
        Assert.Equal(HttpStatusCode.Created, (await participant.PostAsJsonAsync($"/api/problems/{version.ProblemId}/submissions", request, Token)).StatusCode);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => participant.PostAsJsonAsync(
            $"/api/contests/{c.Id}/problems/{c.Problems.Single().Id}/submissions", request, Token)));
        Assert.Equal(4, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(2, results.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests));
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<MinosDbContext>();
        Assert.Equal(4, await db.ContestSubmissions.CountAsync(s => s.ContestId == c.Id, Token));
        Assert.Equal(5, await db.Submissions.CountAsync(s => s.OwnerAccountId == user.Id, Token));
        Assert.Equal(5, await (from job in db.JudgeJobs join submission in db.Submissions on job.SubmissionId equals submission.Id
            where submission.OwnerAccountId == user.Id select job).CountAsync(Token));
    }

    [Fact]
    public async Task Admission_rechecks_end_time_after_waiting_for_capacity_lock()
    {
        var clock = new ContestClock(); await using var factory = Factory(clock);
        using var author = AccountTestHost.Client(factory); var owner = await AccountAsync(factory, author, AccountRole.Setter);
        using var participant = AccountTestHost.Client(factory); await AccountAsync(factory, participant, AccountRole.Solver);
        var version = await SeedAsync(factory, owner.Id); var c = await CreatePublishedAsync(author, clock, version.Id);
        await ReadAsync<ContestResponse>(await participant.PutAsync($"/api/contests/{c.Id}/registration", null, Token));
        clock.Now = c.StartsAtUtc;
        await using var connection = new NpgsqlConnection(PostgresPrivilegeTests.Connection("ConnectionStrings__Minos"));
        await connection.OpenAsync(Token); await using var transaction = await connection.BeginTransactionAsync(Token);
        await using (var hold = new NpgsqlCommand("SELECT pg_advisory_xact_lock(21754185638891831)", connection, transaction))
            await hold.ExecuteNonQueryAsync(Token);
        var submission = participant.PostAsJsonAsync($"/api/contests/{c.Id}/problems/{c.Problems.Single().Id}/submissions",
            new ContestSubmissionRequest("cpp20", "int main(){}"), Token);
        var waiting = false;
        for (var i = 0; i < 100 && !waiting; i++)
        {
            await using var inspect = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_locks WHERE locktype = 'advisory' AND NOT granted AND classid::bigint = (21754185638891831::bigint >> 32) AND objid::bigint = (21754185638891831::bigint & 4294967295))", connection, transaction);
            waiting = (bool)(await inspect.ExecuteScalarAsync(Token))!;
            if (!waiting) await Task.Delay(20, Token);
        }
        Assert.True(waiting, "The request must reach the admission lock before advancing time.");
        clock.Now = c.EndsAtUtc;
        await transaction.CommitAsync(Token);
        Assert.Equal(HttpStatusCode.BadRequest, (await submission).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<MinosDbContext>().ContestSubmissions.AnyAsync(s => s.ContestId == c.Id, Token));
    }

    private static WebApplicationFactory<Program> Factory(ContestClock clock) => AccountTestHost.Factory().WithWebHostBuilder(b =>
        b.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock))));
    private static async Task<AccountResponse> AccountAsync(WebApplicationFactory<Program> factory, HttpClient client, AccountRole role)
    {
        var account = await ReadAsync<AccountResponse>(await AccountTestHost.RegisterAsync(client, AccountTestHost.Email()), HttpStatusCode.Created);
        if (role != AccountRole.Solver) await AccountTestHost.SetRoleAsync(factory, account.Id, role);
        await AccountTestHost.CsrfAsync(client); return account;
    }
    private static SaveContestRequest Request(ContestClock clock, Guid version) => new("Weekly practice", "Solve these problems.", clock.Now.AddHours(1), clock.Now.AddHours(2), [version], null);
    private static async Task<ContestResponse> CreatePublishedAsync(HttpClient author, ContestClock clock, Guid version)
    {
        var draft = await ReadAsync<ContestResponse>(await author.PostAsJsonAsync("/api/contests", Request(clock, version), Token), HttpStatusCode.Created);
        return await ReadAsync<ContestResponse>(await author.PostAsJsonAsync($"/api/contests/{draft.Id}/publish", new PublishContestRequest(draft.Revision), Token));
    }
    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        Assert.True(response.StatusCode == expected, $"Expected {expected}; got {response.StatusCode}: {await response.Content.ReadAsStringAsync(Token)}");
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }
    private static async Task<PublishedProblemVersion> SeedAsync(WebApplicationFactory<Program> factory, Guid owner)
    {
        await using var scope = factory.Services.CreateAsyncScope(); var repository = scope.ServiceProvider.GetRequiredService<IProblemRepository>();
        var now = DateTimeOffset.UtcNow; var p = Problem.Create(owner, "Contest sum", "Print the sum.", now);
        p.ConfigureExecution(1000, 64, BuiltInChecker.Exact, now); p.ReplaceSamples([new("1 2", "3")], now);
        p.AddTestCase(ProblemArtifactReference.Create($"tests/{Guid.NewGuid():N}/input.bin", new string('a', 64), 1),
            ProblemArtifactReference.Create($"tests/{Guid.NewGuid():N}/output.bin", new string('b', 64), 1), now);
        await repository.AddAsync(p, Token); var version = p.Publish(owner, new(true, true, true), now); await repository.PublishAsync(p, version, Token); return version;
    }
    private sealed class ContestClock : TimeProvider { public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
}
