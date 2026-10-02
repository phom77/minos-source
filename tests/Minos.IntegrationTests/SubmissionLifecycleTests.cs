using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Minos.Api.Accounts;
using Minos.Api.Submissions;
using Minos.Application.Evaluation;
using Minos.Application.Problems;
using Minos.Domain.Accounts;
using Minos.Domain.Evaluation;
using Minos.Domain.Problems;
using Minos.Infrastructure;
using Minos.Infrastructure.Persistence;
using Npgsql;
using Xunit;

namespace Minos.IntegrationTests;

public sealed class SubmissionLifecycleTests
{
    [Fact]
    public async Task Owner_can_retrieve_pinned_submission_after_restart_but_other_account_cannot_read_it()
    {
        var token = TestContext.Current.CancellationToken;
        var email = AccountTestHost.Email();
        Guid submissionId;
        Guid firstVersionId;

        await using (var factory = AccountTestHost.Factory())
        using (var owner = AccountTestHost.Client(factory))
        using (var other = AccountTestHost.Client(factory))
        {
            var registration = await AccountTestHost.RegisterAsync(owner, email);
            var account = (await registration.Content.ReadFromJsonAsync<AccountResponse>(token))!;
            var seeded = await SeedPublishedAsync(factory, account.Id, token);
            firstVersionId = seeded.Version.Id;
            await AccountTestHost.CsrfAsync(owner);

            var create = await owner.PostAsJsonAsync($"/api/problems/{seeded.Problem.Id}/submissions",
                new CreateSubmissionRequest(SupportedSubmission.Language, "int main() { return 0; }"), token);
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            var submission = (await create.Content.ReadFromJsonAsync<SubmissionResponse>(token))!;
            submissionId = submission.Id;
            Assert.Equal(firstVersionId, submission.ProblemVersionId);
            Assert.Equal(SupportedSubmission.ToolchainId, submission.ToolchainId);

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var repository = scope.ServiceProvider.GetRequiredService<IProblemRepository>();
                var problem = (await repository.FindForAuthoringAsync(seeded.Problem.Id, token))!;
                problem.EditContent("Revised title", "Revised statement", DateTimeOffset.UtcNow);
                var second = problem.Publish(account.Id, new(true, true, true), DateTimeOffset.UtcNow);
                await repository.PublishAsync(problem, second, token);
                Assert.NotEqual(firstVersionId, second.Id);
            }

            var unchanged = await owner.GetFromJsonAsync<SubmissionResponse>($"/api/submissions/{submissionId}", token);
            Assert.Equal(firstVersionId, unchanged!.ProblemVersionId);
            Assert.Equal("int main() { return 0; }", unchanged.Source);

            var historyResponse = await owner.GetAsync("/api/me/submissions?page=1&pageSize=20", token);
            Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
            var historyJson = await historyResponse.Content.ReadAsStringAsync(token);
            Assert.DoesNotContain("\"source\"", historyJson, StringComparison.OrdinalIgnoreCase);

            Assert.Equal(HttpStatusCode.Created,
                (await AccountTestHost.RegisterAsync(other, AccountTestHost.Email())).StatusCode);
            var denied = await other.GetAsync($"/api/submissions/{submissionId}", token);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.DoesNotContain("int main", await denied.Content.ReadAsStringAsync(token), StringComparison.Ordinal);
        }

        await using var restarted = AccountTestHost.Factory();
        using var reader = AccountTestHost.Client(restarted);
        await AccountTestHost.LoginAsync(reader, email);
        var persisted = await reader.GetFromJsonAsync<SubmissionResponse>($"/api/submissions/{submissionId}", token);
        Assert.Equal(firstVersionId, persisted!.ProblemVersionId);
        Assert.Equal("Pending", persisted.Status);
    }

    [Fact]
    public async Task Concurrent_admission_is_bounded_per_account()
    {
        var token = TestContext.Current.CancellationToken;
        await using var factory = AccountTestHost.Factory();
        using var client = AccountTestHost.Client(factory);
        var registration = await AccountTestHost.RegisterAsync(client, AccountTestHost.Email());
        var owner = (await registration.Content.ReadFromJsonAsync<AccountResponse>(token))!;
        var seeded = await SeedPublishedAsync(factory, owner.Id, token);

        var admissions = Enumerable.Range(0, SupportedSubmission.MaximumActivePerAccount + 1).Select(async index =>
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<ISubmissionRepository>();
            var submission = Submission.Create(owner.Id, seeded.Problem.Id, seeded.Version.Id,
                SupportedSubmission.Language, SupportedSubmission.ToolchainId, $"// {index}\nint main(){{}}", DateTimeOffset.UtcNow);
            return await repository.AdmitAsync(submission, JudgeJob.Create(submission.Id, submission.CreatedAtUtc),
                SupportedSubmission.MaximumActivePerAccount, token);
        });

        var results = await Task.WhenAll(admissions);
        Assert.Equal(SupportedSubmission.MaximumActivePerAccount, results.Count(accepted => accepted));
        Assert.Single(results, accepted => !accepted);
    }

    [Fact]
    public async Task Failed_job_insert_rolls_back_submission_and_returns_safe_error()
    {
        var token = TestContext.Current.CancellationToken;
        await using var factory = AccountTestHost.Factory();
        using var client = AccountTestHost.Client(factory);
        var registration = await AccountTestHost.RegisterAsync(client, AccountTestHost.Email());
        var owner = (await registration.Content.ReadFromJsonAsync<AccountResponse>(token))!;
        var seeded = await SeedPublishedAsync(factory, owner.Id, token);
        await AccountTestHost.CsrfAsync(client);
        var suffix = Guid.NewGuid().ToString("N");
        var function = $"fail_job_insert_{suffix}";
        var trigger = $"fail_job_insert_{suffix}";

        await using var connection = new NpgsqlConnection(PostgresPrivilegeTests.Connection("MINOS_TEST_MIGRATION_CONNECTION"));
        await connection.OpenAsync(token);
        await using (var install = new NpgsqlCommand($$"""
            CREATE FUNCTION evaluation.{{function}}() RETURNS trigger LANGUAGE plpgsql AS
            'BEGIN RAISE EXCEPTION ''m5-rollback-sentinel''; END';
            CREATE TRIGGER {{trigger}} BEFORE INSERT ON evaluation.judge_jobs
            FOR EACH ROW EXECUTE FUNCTION evaluation.{{function}}();
            """, connection))
            await install.ExecuteNonQueryAsync(token);

        try
        {
            var response = await client.PostAsJsonAsync($"/api/problems/{seeded.Problem.Id}/submissions",
                new CreateSubmissionRequest(SupportedSubmission.Language, "// rollback-sentinel\nint main(){}"), token);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(token);
            Assert.DoesNotContain("m5-rollback-sentinel", body, StringComparison.Ordinal);

            await using var verify = new NpgsqlCommand("""
                SELECT (SELECT count(*) FROM evaluation.submissions WHERE source LIKE '%rollback-sentinel%'),
                       (SELECT count(*) FROM evaluation.judge_jobs job
                        JOIN evaluation.submissions submission ON submission.id = job.submission_id
                        WHERE submission.source LIKE '%rollback-sentinel%')
                """, connection);
            await using var reader = await verify.ExecuteReaderAsync(token);
            Assert.True(await reader.ReadAsync(token));
            Assert.Equal(0L, reader.GetInt64(0));
            Assert.Equal(0L, reader.GetInt64(1));
        }
        finally
        {
            await using var remove = new NpgsqlCommand($$"""
                DROP TRIGGER IF EXISTS {{trigger}} ON evaluation.judge_jobs;
                DROP FUNCTION IF EXISTS evaluation.{{function}}();
                """, connection);
            await remove.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Concurrent_claim_has_one_winner_and_completion_is_idempotent()
    {
        var token = TestContext.Current.CancellationToken;
        await using var workers = WorkerServices();
        await DrainJobsAsync(workers, token);
        var submissionId = await CreatePendingSubmissionAsync(token);
        var now = DateTimeOffset.UtcNow.AddMinutes(1);

        await using var firstScope = workers.CreateAsyncScope();
        await using var secondScope = workers.CreateAsyncScope();
        var first = firstScope.ServiceProvider.GetRequiredService<IJudgeJobRepository>();
        var second = secondScope.ServiceProvider.GetRequiredService<IJudgeJobRepository>();
        var claims = await Task.WhenAll(
            first.ClaimNextAsync("worker-a", now, token),
            second.ClaimNextAsync("worker-b", now, token));
        var work = Assert.Single(claims, claim => claim is not null)!;
        Assert.Single(claims, claim => claim is null);
        Assert.Equal(submissionId, work.SubmissionId);

        await using (var processScope = workers.CreateAsyncScope())
        {
            var useCases = processScope.ServiceProvider.GetRequiredService<JudgeJobUseCases>();
            Assert.Equal(LeaseMutationResult.Accepted, await useCases.ProcessAsync(work, token));
        }
        await using (var duplicateScope = workers.CreateAsyncScope())
        {
            var repository = duplicateScope.ServiceProvider.GetRequiredService<IJudgeJobRepository>();
            Assert.Equal(LeaseMutationResult.Accepted,
                await repository.CompleteAsync(work.AttemptId, work.LeaseToken, work.WorkerId, AcceptedOutcome(), now.AddSeconds(1), token));
        }

        await using var database = PostgresPrivilegeTests.Database(PostgresPrivilegeTests.Connection("MINOS_TEST_MIGRATION_CONNECTION"));
        Assert.Equal(1, await database.SubmissionResults.CountAsync(result => result.SubmissionId == submissionId, token));
        Assert.Equal(SubmissionStatus.Completed,
            (await database.Submissions.SingleAsync(submission => submission.Id == submissionId, token)).Status);
    }

    [Fact]
    public async Task Renewed_lease_can_be_reclaimed_after_expiry_and_stale_completion_is_rejected()
    {
        var token = TestContext.Current.CancellationToken;
        await using var workers = WorkerServices();
        await DrainJobsAsync(workers, token);
        var submissionId = await CreatePendingSubmissionAsync(token);
        var now = DateTimeOffset.UtcNow.AddMinutes(1);
        ClaimedSubmissionWork first;
        await using (var scope = workers.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IJudgeJobRepository>();
            first = (await repository.ClaimNextAsync("worker-old", now, token))!;
            Assert.Equal(LeaseMutationResult.Accepted,
                await repository.RenewAsync(first.AttemptId, first.LeaseToken, first.WorkerId, now.AddSeconds(1), token));
        }

        ClaimedSubmissionWork replacement;
        await using (var scope = workers.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IJudgeJobRepository>();
            replacement = (await repository.ClaimNextAsync("worker-new", now.AddSeconds(32), token))!;
        }
        Assert.Equal(submissionId, replacement.SubmissionId);
        Assert.NotEqual(first.AttemptId, replacement.AttemptId);

        await using (var scope = workers.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IJudgeJobRepository>();
            Assert.Equal(LeaseMutationResult.Stale,
                await repository.CompleteAsync(first.AttemptId, first.LeaseToken, first.WorkerId, AcceptedOutcome(), now.AddSeconds(33), token));
            Assert.Equal(LeaseMutationResult.Accepted,
                await repository.CompleteAsync(replacement.AttemptId, replacement.LeaseToken, replacement.WorkerId,
                    AcceptedOutcome(), now.AddSeconds(33), token));
        }

        await using var database = PostgresPrivilegeTests.Database(PostgresPrivilegeTests.Connection("MINOS_TEST_MIGRATION_CONNECTION"));
        var attempts = await database.JudgingAttempts.Where(attempt => attempt.SubmissionId == submissionId)
            .OrderBy(attempt => attempt.AttemptNumber).ToArrayAsync(token);
        Assert.Equal([JudgingAttemptStatus.LeaseExpired, JudgingAttemptStatus.Succeeded], attempts.Select(x => x.Status));
        Assert.Single(await database.SubmissionResults.Where(result => result.SubmissionId == submissionId).ToArrayAsync(token));
    }

    [Fact]
    public async Task Infrastructure_retries_are_bounded_and_exhaust_as_system_failure()
    {
        var token = TestContext.Current.CancellationToken;
        await using var workers = WorkerServices();
        await DrainJobsAsync(workers, token);
        var submissionId = await CreatePendingSubmissionAsync(token);
        var now = DateTimeOffset.UtcNow.AddMinutes(1);

        for (var number = 1; number <= JudgeJob.MaximumAttempts; number++)
        {
            await using var scope = workers.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IJudgeJobRepository>();
            var work = (await repository.ClaimNextAsync($"worker-{number}", now, token))!;
            Assert.Equal(number, await AttemptNumberAsync(work.AttemptId, token));
            var result = await repository.FailAsync(work.AttemptId, work.LeaseToken, work.WorkerId,
                InfrastructureFailureKind.ExecutorFailure, now.AddMilliseconds(100), token);
            Assert.Equal(number == JudgeJob.MaximumAttempts
                ? AttemptFailureResult.SystemFailed
                : AttemptFailureResult.RetryScheduled, result);
            now = now.AddSeconds(number == 1 ? 2 : 6);
        }

        await using var database = PostgresPrivilegeTests.Database(PostgresPrivilegeTests.Connection("MINOS_TEST_MIGRATION_CONNECTION"));
        var submission = await database.Submissions.SingleAsync(candidate => candidate.Id == submissionId, token);
        Assert.Equal(SubmissionStatus.SystemFailed, submission.Status);
        Assert.Equal(JudgeJob.MaximumAttempts,
            await database.JudgingAttempts.CountAsync(attempt => attempt.SubmissionId == submissionId, token));
        Assert.False(await database.SubmissionResults.AnyAsync(result => result.SubmissionId == submissionId, token));
    }

    [Fact]
    public async Task M6_upgrade_preserves_published_problem_and_truthfully_retains_M5_lifecycle_result()
    {
        var token = TestContext.Current.CancellationToken;
        var name = $"minos_m5_upgrade_{Guid.NewGuid():N}";
        await using var bootstrap = new NpgsqlConnection(PostgresPrivilegeTests.Connection("MINOS_TEST_BOOTSTRAP_CONNECTION"));
        await bootstrap.OpenAsync(token);
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {name}", bootstrap))
            await create.ExecuteNonQueryAsync(token);
        try
        {
            var bootstrapConfig = new NpgsqlConnectionStringBuilder(PostgresPrivilegeTests.Connection("MINOS_TEST_BOOTSTRAP_CONNECTION"))
                { Database = name, Pooling = false };
            await using (var setup = new NpgsqlConnection(bootstrapConfig.ConnectionString))
            {
                await setup.OpenAsync(token);
                await using var grant = new NpgsqlCommand($"REVOKE ALL ON DATABASE {name} FROM PUBLIC; GRANT CONNECT,CREATE ON DATABASE {name} TO minos_migrator; REVOKE ALL ON SCHEMA public FROM PUBLIC; GRANT USAGE,CREATE ON SCHEMA public TO minos_migrator", setup);
                await grant.ExecuteNonQueryAsync(token);
            }
            var config = new NpgsqlConnectionStringBuilder(PostgresPrivilegeTests.Connection("MINOS_TEST_MIGRATION_CONNECTION"))
                { Database = name, Pooling = false };
            await using var database = PostgresPrivilegeTests.Database(config.ConnectionString);
            await database.GetService<IMigrator>().MigrateAsync("20260918163404_M4ProblemPublishing", token);
            var account = Account.Create("M4 Owner", DateTimeOffset.UtcNow);
            var problem = PublishedProblem(account.Id);
            database.Accounts.Add(account);
            database.Problems.Add(problem.Problem);
            database.PublishedProblemVersions.Add(problem.Version);
            await database.SaveChangesAsync(token);

            await database.GetService<IMigrator>().MigrateAsync("20260924174220_M5SubmissionJobs", token);
            var submissionId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            var attemptId = Guid.NewGuid();
            var leaseToken = Guid.NewGuid();
            var completed = DateTimeOffset.UtcNow;
            await database.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO evaluation.submissions
                    (id,owner_account_id,problem_id,problem_version_id,language,toolchain_id,source,status,created_at_utc)
                VALUES ({{submissionId}},{{account.Id}},{{problem.Problem.Id}},{{problem.Version.Id}},'cpp20',{{SupportedSubmission.ToolchainId}},'int main(){}',0,{{completed}});
                INSERT INTO evaluation.judge_jobs
                    (id,submission_id,status,attempt_count,available_at_utc,created_at_utc,updated_at_utc)
                VALUES ({{jobId}},{{submissionId}},0,1,{{completed}},{{completed}},{{completed}});
                INSERT INTO evaluation.judging_attempts
                    (id,judge_job_id,submission_id,attempt_number,lease_token,worker_id,status,started_at_utc,lease_expires_at_utc,finished_at_utc)
                VALUES ({{attemptId}},{{jobId}},{{submissionId}},1,{{leaseToken}},'m5-worker',1,{{completed.AddSeconds(-1)}},{{completed.AddSeconds(29)}},{{completed}});
                UPDATE evaluation.judge_jobs SET status=2,completed_attempt_id={{attemptId}} WHERE id={{jobId}};
                UPDATE evaluation.submissions SET status=2,completed_attempt_id={{attemptId}},completed_at_utc={{completed}} WHERE id={{submissionId}};
                INSERT INTO evaluation.submission_results (submission_id,attempt_id,completed_at_utc)
                VALUES ({{submissionId}},{{attemptId}},{{completed}});
                """, token);

            await database.Database.MigrateAsync(token);
            database.ChangeTracker.Clear();
            Assert.Equal("M5 problem", (await database.PublishedProblemVersions
                .SingleAsync(version => version.Id == problem.Version.Id, token)).Title);
            Assert.Equal(1, await database.Submissions.CountAsync(token));
            var legacy = await database.SubmissionResults.SingleAsync(result => result.SubmissionId == submissionId, token);
            Assert.Null(legacy.Verdict);
            Assert.Empty(legacy.CompilerDiagnostics);
            Assert.Empty(await database.TestCaseResults.Where(result => result.SubmissionId == submissionId).ToArrayAsync(token));
            Assert.Empty(await database.Database.GetPendingMigrationsAsync(token));
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", bootstrap);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task PostgreSQL_rejects_submission_source_that_bypasses_domain_validation()
    {
        var token = TestContext.Current.CancellationToken;
        await using var factory = AccountTestHost.Factory();
        using var client = AccountTestHost.Client(factory);
        var registration = await AccountTestHost.RegisterAsync(client, AccountTestHost.Email());
        var owner = (await registration.Content.ReadFromJsonAsync<AccountResponse>(token))!;
        var seeded = await SeedPublishedAsync(factory, owner.Id, token);

        await using var connection = new NpgsqlConnection(PostgresPrivilegeTests.Connection("ConnectionStrings__Minos"));
        await connection.OpenAsync(token);
        await using var command = new NpgsqlCommand("""
            INSERT INTO evaluation.submissions
                (id,owner_account_id,problem_id,problem_version_id,language,toolchain_id,source,status,created_at_utc)
            VALUES (@id,@owner,@problem,@version,'cpp20',@toolchain,@source,0,now())
            """, connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("owner", owner.Id);
        command.Parameters.AddWithValue("problem", seeded.Problem.Id);
        command.Parameters.AddWithValue("version", seeded.Version.Id);
        command.Parameters.AddWithValue("toolchain", SupportedSubmission.ToolchainId);
        command.Parameters.AddWithValue("source", "\u00a0\u2003");
        var failure = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
        Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
    }

    private static async Task<(Problem Problem, PublishedProblemVersion Version)> SeedPublishedAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory,
        Guid ownerId,
        CancellationToken token)
    {
        var published = PublishedProblem(ownerId);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MinosDbContext>();
        database.Problems.Add(published.Problem);
        database.PublishedProblemVersions.Add(published.Version);
        await database.SaveChangesAsync(token);
        return published;
    }

    private static (Problem Problem, PublishedProblemVersion Version) PublishedProblem(Guid ownerId)
    {
        var now = DateTimeOffset.UtcNow;
        var problem = Problem.Create(ownerId, "M5 problem", "Statement", now);
        problem.ConfigureExecution(1000, 64, BuiltInChecker.Exact, now);
        problem.ReplaceSamples([new("1", "1")], now);
        problem.AddTestCase(
            ProblemArtifactReference.Create($"tests/{Guid.NewGuid():N}/input.bin", new string('a', 64), 1),
            ProblemArtifactReference.Create($"tests/{Guid.NewGuid():N}/output.bin", new string('b', 64), 1), now);
        return (problem, problem.Publish(ownerId, new(true, true, true), now));
    }

    private static async Task<Guid> CreatePendingSubmissionAsync(CancellationToken token)
    {
        await using var factory = AccountTestHost.Factory();
        using var client = AccountTestHost.Client(factory);
        var registration = await AccountTestHost.RegisterAsync(client, AccountTestHost.Email());
        var owner = (await registration.Content.ReadFromJsonAsync<AccountResponse>(token))!;
        var seeded = await SeedPublishedAsync(factory, owner.Id, token);
        await AccountTestHost.CsrfAsync(client);
        var response = await client.PostAsJsonAsync($"/api/problems/{seeded.Problem.Id}/submissions",
            new CreateSubmissionRequest(SupportedSubmission.Language, "int main(){}"), token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SubmissionResponse>(token))!.Id;
    }

    private static ServiceProvider WorkerServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMinosInfrastructure(PostgresPrivilegeTests.Connection("MINOS_TEST_WORKER_CONNECTION"));
        services.AddScoped<ISubmissionExecutor, SuccessfulExecutor>();
        return services.BuildServiceProvider();
    }

    private static async Task DrainJobsAsync(ServiceProvider workers, CancellationToken token)
    {
        await using var scope = workers.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IJudgeJobRepository>();
        var now = DateTimeOffset.UtcNow.AddDays(1);
        for (var count = 0; count < 100; count++)
        {
            var work = await repository.ClaimNextAsync("drain-worker", now, token);
            if (work is null) return;
            Assert.Equal(LeaseMutationResult.Accepted,
                await repository.CompleteAsync(work.AttemptId, work.LeaseToken, work.WorkerId, AcceptedOutcome(), now.AddMilliseconds(1), token));
            now = now.AddMilliseconds(2);
        }
        throw new InvalidOperationException("Unexpected number of pending M5 test jobs.");
    }

    private static async Task<int> AttemptNumberAsync(Guid attemptId, CancellationToken token)
    {
        await using var database = PostgresPrivilegeTests.Database(PostgresPrivilegeTests.Connection("MINOS_TEST_MIGRATION_CONNECTION"));
        return await database.JudgingAttempts.Where(attempt => attempt.Id == attemptId)
            .Select(attempt => attempt.AttemptNumber).SingleAsync(token);
    }

    private static JudgingOutcome AcceptedOutcome() => JudgingOutcome.Create(
        SubmissionVerdict.Accepted,
        "",
        [new(1, TestCaseResultStatus.Passed, 1, 1, 1024, 0)]);

    private sealed class SuccessfulExecutor : ISubmissionExecutor
    {
        public Task<SubmissionExecutionResult> ExecuteAsync(ClaimedSubmissionWork work, CancellationToken token) =>
            Task.FromResult(SubmissionExecutionResult.Completed(AcceptedOutcome()));
    }
}
