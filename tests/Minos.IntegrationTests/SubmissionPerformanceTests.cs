using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Minos.Api.Accounts;
using Minos.Api.Submissions;
using Minos.Application.Evaluation;
using Minos.Domain.Accounts;
using Minos.Domain.Evaluation;
using Minos.Domain.Problems;
using Minos.Infrastructure.Persistence;
using Npgsql;
using Xunit;

namespace Minos.IntegrationTests;

public sealed class SubmissionPerformanceTests
{
    private const string Profile = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Comparison_is_owner_only_and_uses_latest_distinct_peer_accounts_in_the_exact_cohort()
    {
        var token = TestContext.Current.CancellationToken;
        var capture = new PerformanceCapture();
        await using var factory = AccountTestHost.Factory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddDbContext<MinosDbContext>(options => options.AddInterceptors(capture))));
        using var client = AccountTestHost.Client(factory);
        using var other = AccountTestHost.Client(factory);
        using var guest = AccountTestHost.Client(factory);
        var owner = (await (await AccountTestHost.RegisterAsync(client, AccountTestHost.Email())).Content.ReadFromJsonAsync<AccountResponse>(token))!;
        var problem = await SubmissionLifecycleTests.SeedPublishedAsync(factory, owner.Id, token);
        Guid selected, legacy, wrong;
        await using (var database = FixtureDatabase())
        {
            Assert.False(database.Database.HasPendingModelChanges());
            selected = await AddResult(database, owner.Id, problem.Version, 50, 100, Profile, Now);
            legacy = await AddResult(database, owner.Id, problem.Version, 50, 100, null, Now.AddSeconds(1));
            wrong = await AddResult(database, owner.Id, problem.Version, 50, 100, Profile, Now.AddSeconds(2), accepted: false);
            // Own repeat submissions never change the reference population.
            await AddResult(database, owner.Id, problem.Version, 9999, 9999, Profile, Now.AddSeconds(3));
            var peers = Enumerable.Range(0, 20).Select(index => Account.Create("Peer " + index, Now)).ToArray();
            database.Accounts.AddRange(peers); await database.SaveChangesAsync(token);
            var otherProblem = await SubmissionLifecycleTests.SeedPublishedAsync(factory, owner.Id, token);
            var editable = await database.Problems.Include(value => value.Samples).Include(value => value.TestCases)
                .SingleAsync(value => value.Id == problem.Problem.Id, token);
            editable.EditContent("New version", "Updated statement", Now.AddDays(1));
            var nextVersion = editable.Publish(owner.Id, new(true, true, true), Now.AddDays(1));
            database.PublishedProblemVersions.Add(nextVersion); await database.SaveChangesAsync(token);
            for (var i = 0; i < 20; i++)
            {
                var cpu = i < 5 ? 10 : i < 10 ? 50 : 100;
                var memory = i < 5 ? 50 : i < 10 ? 100 : 200;
                await AddResult(database, peers[i].Id, problem.Version, 1000, 1000, Profile, Now.AddSeconds(-1));
                if (i < 19) await AddResult(database, peers[i].Id, problem.Version, cpu, memory, Profile, Now);
                // Newer ineligible measurements must not replace the latest eligible Accepted.
                await AddResult(database, peers[i].Id, problem.Version, 500, 500, new string('b', 64), Now.AddSeconds(1));
                await AddResult(database, peers[i].Id, problem.Version, 500, 500, null, Now.AddSeconds(2));
                await AddResult(database, peers[i].Id, problem.Version, 500, 500, Profile, Now.AddSeconds(3), accepted: false);
                await AddResult(database, peers[i].Id, otherProblem.Version, 500, 500, Profile, Now.AddSeconds(4));
                await AddResult(database, peers[i].Id, nextVersion, 500, 500, Profile, Now.AddSeconds(5));
            }
            // The 20th peer already has an old eligible result. Remove its eligibility to test the threshold below.
            await database.Database.ExecuteSqlInterpolatedAsync($"UPDATE evaluation.submission_results r SET measurement_profile_id = NULL FROM evaluation.submissions s WHERE r.submission_id = s.id AND s.owner_account_id = {peers[19].Id} AND s.problem_version_id = {problem.Version.Id}", token);
            var insufficient = (await client.GetFromJsonAsync<SubmissionPerformanceResponse>($"/api/submissions/{selected}/performance", token))!;
            Assert.Equal("InsufficientSamples", insufficient.Status); Assert.Equal(19, insufficient.SampleCount);
            Assert.Null(insufficient.Runtime!.BeatsPercentage); Assert.Empty(insufficient.Runtime.Buckets);
            await AddResult(database, peers[19].Id, problem.Version, 100, 200, Profile, Now.AddSeconds(10));
        }
        capture.Commands.Clear();
        var response = await client.GetAsync($"/api/submissions/{selected}/performance", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        var result = (await response.Content.ReadFromJsonAsync<SubmissionPerformanceResponse>(token))!;
        Assert.Equal("Ready", result.Status); Assert.Equal(20, result.SampleCount);
        foreach (var metric in new[] { result.Runtime!, result.Memory! })
        {
            Assert.Equal(50m, metric.BeatsPercentage); Assert.Equal(5, metric.EqualCount);
            Assert.Equal(20, metric.Buckets.Count); Assert.Equal(20, metric.Buckets.Sum(bucket => bucket.Count));
            Assert.True(metric.RangeMaximum > metric.Value);
        }
        var json = await response.Content.ReadAsStringAsync(token);
        foreach (var forbidden in new[] { "source", "ownerAccountId", "measurementProfileId", "artifact", "toolchainId", "attemptId" })
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(capture.Commands, command => command.Contains("test_case_results", StringComparison.Ordinal));
        var detail = (await client.GetFromJsonAsync<SubmissionResponse>($"/api/submissions/{selected}", token))!;
        Assert.True(detail.Result!.ComparisonEligible);
        var aggregateReads = capture.AggregateReads;
        Assert.Equal("Unprofiled", (await client.GetFromJsonAsync<SubmissionPerformanceResponse>($"/api/submissions/{legacy}/performance", token))!.Status);
        Assert.Equal("NotAccepted", (await client.GetFromJsonAsync<SubmissionPerformanceResponse>($"/api/submissions/{wrong}/performance", token))!.Status);
        Assert.Equal(aggregateReads, capture.AggregateReads);
        await AccountTestHost.RegisterAsync(other, AccountTestHost.Email());
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync($"/api/submissions/{selected}/performance", token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync($"/api/submissions/{selected}/performance", token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/submissions/{Guid.NewGuid()}/performance", token)).StatusCode);
        Assert.Equal(aggregateReads, capture.AggregateReads);
        // Explain the actual parameterized aggregate: bounded output and no source/test materialization.
        await using var connection = new NpgsqlConnection(PostgresPrivilegeTests.Connection("ConnectionStrings__Minos"));
        await connection.OpenAsync(token);
        await using var explain = new NpgsqlCommand("EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + capture.AggregateSql, connection);
        foreach (var parameter in capture.Parameters) explain.Parameters.AddWithValue(parameter.Key, parameter.Value);
        using var plan = JsonDocument.Parse((string)(await explain.ExecuteScalarAsync(token))!);
        Assert.Equal(40m, plan.RootElement[0].GetProperty("Plan").GetProperty("Actual Rows").GetDecimal());
        TestContext.Current.TestOutputHelper!.WriteLine("Performance aggregate: 40 output rows; PostgreSQL execution {0} ms.", plan.RootElement[0].GetProperty("Execution Time").GetDecimal());
    }

    [Fact]
    public async Task Empty_zero_and_equal_samples_do_not_invent_percentiles_and_migration_keeps_legacy_rows_nullable()
    {
        var token = TestContext.Current.CancellationToken;
        await using var factory = AccountTestHost.Factory();
        using var client = AccountTestHost.Client(factory);
        var owner = (await (await AccountTestHost.RegisterAsync(client, AccountTestHost.Email())).Content.ReadFromJsonAsync<AccountResponse>(token))!;
        var problem = await SubmissionLifecycleTests.SeedPublishedAsync(factory, owner.Id, token);
        await using var database = FixtureDatabase();
        var selected = await AddResult(database, owner.Id, problem.Version, 0, 0, Profile, Now);
        var empty = (await client.GetFromJsonAsync<SubmissionPerformanceResponse>($"/api/submissions/{selected}/performance", token))!;
        Assert.Equal(0, empty.SampleCount); Assert.Null(empty.Runtime!.BeatsPercentage); Assert.Empty(empty.Runtime.Buckets);
        for (var i = 0; i < 20; i++)
        {
            var peer = Account.Create("Equal", Now); database.Accounts.Add(peer); await database.SaveChangesAsync(token);
            await AddResult(database, peer.Id, problem.Version, 0, 0, Profile, Now);
        }
        var equal = (await client.GetFromJsonAsync<SubmissionPerformanceResponse>($"/api/submissions/{selected}/performance", token))!;
        Assert.Equal(0m, equal.Runtime!.BeatsPercentage); Assert.Equal(20, equal.Runtime.EqualCount);
        Assert.Equal(20, equal.Runtime.Buckets[0].Count); Assert.Equal(1m, equal.Runtime.RangeMaximum);
        await using var connection = new NpgsqlConnection(PostgresPrivilegeTests.Connection("MINOS_TEST_MIGRATION_CONNECTION"));
        await connection.OpenAsync(token);
        await using var invalid = new NpgsqlCommand("UPDATE evaluation.submission_results SET measurement_profile_id = 'invalid' WHERE submission_id = @id", connection);
        invalid.Parameters.AddWithValue("id", selected);
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await Assert.ThrowsAsync<PostgresException>(() => invalid.ExecuteNonQueryAsync(token))).SqlState);
    }

    private static MinosDbContext FixtureDatabase() => new(new DbContextOptionsBuilder<MinosDbContext>()
        .UseNpgsql(PostgresPrivilegeTests.Connection("MINOS_TEST_MIGRATION_CONNECTION")).Options);

    private static async Task<Guid> AddResult(MinosDbContext database, Guid owner, PublishedProblemVersion version,
        long cpu, long memory, string? profile, DateTimeOffset time, bool accepted = true)
    {
        var token = TestContext.Current.CancellationToken;
        var submission = Submission.Create(owner, version.ProblemId, version.Id, "cpp20", SupportedSubmission.ToolchainId, "// private fixture", time);
        var job = JudgeJob.Create(submission.Id, time); database.Submissions.Add(submission); database.JudgeJobs.Add(job);
        await database.SaveChangesAsync(token);
        var attempt = job.Claim("performance-fixture", time); submission.StartAttempt(attempt.Id);
        database.JudgingAttempts.Add(attempt);
        var outcome = JudgingOutcome.Create(accepted ? SubmissionVerdict.Accepted : SubmissionVerdict.WrongAnswer, "",
            [new(1, accepted ? TestCaseResultStatus.Passed : TestCaseResultStatus.WrongAnswer, cpu, cpu, memory, 0)], profile);
        database.SubmissionResults.Add(submission.Complete(attempt.Id, outcome, time.AddMilliseconds(1)));
        job.Complete(attempt.Id, attempt.LeaseToken, "performance-fixture", time.AddMilliseconds(1)); attempt.Succeed(time.AddMilliseconds(1));
        await database.SaveChangesAsync(token); return submission.Id;
    }

    private sealed class PerformanceCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public string AggregateSql { get; private set; } = "";
        public Dictionary<string, object> Parameters { get; private set; } = [];
        public int AggregateReads { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            if (command.CommandText.Contains("WITH peers AS MATERIALIZED", StringComparison.Ordinal))
            {
                AggregateReads++; AggregateSql = command.CommandText;
                Parameters = command.Parameters.Cast<DbParameter>().ToDictionary(parameter => parameter.ParameterName, parameter => parameter.Value!);
            }
            return ValueTask.FromResult(result);
        }
    }
}
