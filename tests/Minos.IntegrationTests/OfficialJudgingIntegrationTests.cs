using System.IO.Compression;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Minos.Api.Accounts;
using Minos.Api.Submissions;
using Minos.Application.Evaluation;
using Minos.Application.Problems;
using Minos.Domain.Evaluation;
using Minos.Domain.Problems;
using Minos.Infrastructure;
using Minos.Infrastructure.Hosting;
using Minos.Infrastructure.Persistence;
using Npgsql;
using Xunit;

namespace Minos.IntegrationTests;

public sealed class OfficialJudgingIntegrationTests
{
    [Fact]
    public async Task First_failure_is_committed_with_original_version_and_private_evidence_after_restart()
    {
        var token = TestContext.Current.CancellationToken;
        var backend = new ScriptedBackend(
            Complete(artifact: [1]), Complete(stdout: "3\n"u8.ToArray()), Complete(stdout: "wrong\n"u8.ToArray()));
        await using var workers = Workers(backend);
        await DrainAsync(workers, token);
        var email = AccountTestHost.Email();
        Guid id;
        Guid versionId;
        await using (var factory = AccountTestHost.Factory())
        using (var owner = AccountTestHost.Client(factory))
        using (var other = AccountTestHost.Client(factory))
        {
            var account = (await (await AccountTestHost.RegisterAsync(owner, email))
                .Content.ReadFromJsonAsync<AccountResponse>(token))!;
            var seeded = await SeedAsync(factory.Services, account.Id, token);
            versionId = seeded.Version.Id;
            await AccountTestHost.CsrfAsync(owner);
            var admission = await owner.PostAsJsonAsync($"/api/problems/{seeded.Problem.Id}/submissions",
                new CreateSubmissionRequest("cpp20", "int main(){}"), token);
            Assert.Equal(HttpStatusCode.Created, admission.StatusCode);
            id = (await admission.Content.ReadFromJsonAsync<SubmissionResponse>(token))!.Id;

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var repository = scope.ServiceProvider.GetRequiredService<IProblemRepository>();
                var problem = (await repository.FindForAuthoringAsync(seeded.Problem.Id, token))!;
                foreach (var test in problem.TestCases.ToArray()) problem.RemoveTestCase(test.Id, DateTimeOffset.UtcNow);
                var stored = await StoreAsync(scope.ServiceProvider.GetRequiredService<IProblemArtifactStore>(), "99 99\n", "198\n", token);
                problem.AddTestCase(stored.Input, stored.Output, DateTimeOffset.UtcNow);
                problem.ConfigureExecution(2000, 128, BuiltInChecker.Exact, DateTimeOffset.UtcNow);
                var second = problem.Publish(account.Id, new(true, true, true), DateTimeOffset.UtcNow);
                await repository.PublishAsync(problem, second, token);
            }

            await using var processing = workers.CreateAsyncScope();
            var cases = processing.ServiceProvider.GetRequiredService<JudgeJobUseCases>();
            var work = (await cases.ClaimAsync("m6-test-worker", token))!;
            Assert.Equal(id, work.SubmissionId);
            Assert.Equal(versionId, work.ProblemVersionId);
            Assert.Equal(LeaseMutationResult.Accepted, await cases.ProcessAsync(work, token));
            Assert.Equal(3, backend.Requests.Count); // One compilation, then exactly two tests.
            Assert.Equal("19 23\n"u8.ToArray(), backend.Requests[2].Input);
            Assert.Equal(64*1024*1024, backend.Requests[2].Limits.MemoryBytes);
            Assert.Equal(TimeSpan.FromSeconds(1), backend.Requests[2].Limits.CpuTime);

            await AccountTestHost.RegisterAsync(other, AccountTestHost.Email());
            Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync($"/api/submissions/{id}", token)).StatusCode);
        }

        await using var restarted = AccountTestHost.Factory();
        using var reader = AccountTestHost.Client(restarted);
        await AccountTestHost.LoginAsync(reader, email);
        var result = (await reader.GetFromJsonAsync<SubmissionResponse>($"/api/submissions/{id}", token))!;
        Assert.Equal(versionId, result.ProblemVersionId);
        Assert.Equal(1000, result.Configuration!.TimeLimitMilliseconds);
        Assert.Equal(64, result.Configuration.MemoryLimitMegabytes);
        Assert.Equal(3, result.Configuration.TestCount);
        Assert.Equal("WrongAnswer", result.Result!.Verdict);
        Assert.Equal(["Passed", "WrongAnswer", "NotExecuted"], result.Result.Tests.Select(test => test.Status));
        Assert.Null(result.Result.Tests[2].MemoryPeakBytes);
        var json = await reader.GetStringAsync($"/api/submissions/{id}", token);
        foreach (var forbidden in new[] { "19 23", "wrong\\n", "stdout", "stderr", "artifactKey" })
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
        var history = await reader.GetFromJsonAsync<SubmissionPageResponse>("/api/me/submissions?page=1&pageSize=20", token);
        Assert.Equal("WrongAnswer", Assert.Single(history!.Items).Verdict);
    }

    [Fact]
    public async Task Poll_is_one_committed_snapshot_when_a_worker_finishes_between_queries()
    {
        var token = TestContext.Current.CancellationToken;
        await using var workers = Workers(new ScriptedBackend());
        await DrainAsync(workers, token);
        var gate = new SubmissionReadGate();
        await using var factory = AccountTestHost.Factory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddDbContext<MinosDbContext>(options => options.AddInterceptors(gate))));
        using var client = AccountTestHost.Client(factory);
        var owner = (await (await AccountTestHost.RegisterAsync(client, AccountTestHost.Email()))
            .Content.ReadFromJsonAsync<AccountResponse>(token))!;
        var seeded = await SeedAsync(factory.Services, owner.Id, token);
        await AccountTestHost.CsrfAsync(client);
        var created = await client.PostAsJsonAsync($"/api/problems/{seeded.Problem.Id}/submissions",
            new CreateSubmissionRequest("cpp20", "int main(){}"), token);
        var id = (await created.Content.ReadFromJsonAsync<SubmissionResponse>(token))!.Id;
        gate.Armed = true;
        var polling = client.GetFromJsonAsync<SubmissionResponse>($"/api/submissions/{id}", token);
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            await using var scope = workers.CreateAsyncScope();
            var jobs = scope.ServiceProvider.GetRequiredService<IJudgeJobRepository>();
            var now = DateTimeOffset.UtcNow;
            var work = (await jobs.ClaimNextAsync("m6-snapshot-worker", now, token))!;
            Assert.Equal(id, work.SubmissionId);
            var outcome = JudgingOutcome.Create(SubmissionVerdict.Accepted, "",
                Enumerable.Range(1, 3).Select(order => new JudgingTestOutcome(order, TestCaseResultStatus.Passed, 1, 1, 1024, 0)).ToArray());
            Assert.Equal(LeaseMutationResult.Accepted,
                await jobs.CompleteAsync(work.AttemptId, work.LeaseToken, work.WorkerId, outcome, now.AddMilliseconds(1), token));
        }
        finally { gate.Release.TrySetResult(); }
        var before = (await polling)!;
        Assert.Equal("Pending", before.Status);
        Assert.Empty(before.Attempts);
        Assert.Null(before.Result);
        var after = (await client.GetFromJsonAsync<SubmissionResponse>($"/api/submissions/{id}", token))!;
        Assert.Equal("Completed", after.Status);
        Assert.Equal("Accepted", after.Result!.Verdict);
        Assert.Equal("Succeeded", Assert.Single(after.Attempts).Status);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("corrupt")]
    [InlineData("runner")]
    [InlineData("snapshot")]
    public async Task Untrustworthy_judging_retries_and_exhausts_without_a_contestant_verdict(string failure)
    {
        var token = TestContext.Current.CancellationToken;
        var backend = new ScriptedBackend(failure == "snapshot"
            ? Complete() with { ResourceSnapshotComplete = false }
            : Complete() with { InfrastructureFailure = "FixtureRunnerFault" });
        await using var workers = Workers(backend);
        await DrainAsync(workers, token);
        await using var factory = AccountTestHost.Factory();
        using var client = AccountTestHost.Client(factory);
        var owner = (await (await AccountTestHost.RegisterAsync(client, AccountTestHost.Email()))
            .Content.ReadFromJsonAsync<AccountResponse>(token))!;
        var seeded = await SeedAsync(factory.Services, owner.Id, token);
        if (failure is "missing" or "corrupt")
        {
            var key = seeded.Version.TestCases[2].OutputArtifact.Key;
            var path = Path.Combine(Environment.GetEnvironmentVariable("Minos__Artifacts__Root")!, "problem-artifacts", key);
            if (failure == "missing") File.Delete(path);
            else await File.WriteAllTextAsync(path, "bad", token);
        }
        await AccountTestHost.CsrfAsync(client);
        var admitted = await client.PostAsJsonAsync($"/api/problems/{seeded.Problem.Id}/submissions",
            new CreateSubmissionRequest("cpp20", "int main(){}"), token);
        var id = (await admitted.Content.ReadFromJsonAsync<SubmissionResponse>(token))!.Id;
        for (var number = 1; number <= 3; number++)
        {
            await using var scope = workers.CreateAsyncScope();
            var jobs = scope.ServiceProvider.GetRequiredService<IJudgeJobRepository>();
            var work = (await jobs.ClaimNextAsync("m6-fault-worker", DateTimeOffset.UtcNow.AddMinutes(number), token))!;
            Assert.Equal(id, work.SubmissionId);
            await scope.ServiceProvider.GetRequiredService<JudgeJobUseCases>().ProcessAsync(work, token);
        }
        var result = (await client.GetFromJsonAsync<SubmissionResponse>($"/api/submissions/{id}", token))!;
        Assert.Equal("SystemFailed", result.Status);
        Assert.Null(result.Result);
        Assert.Equal(3, result.Attempts.Count);
        Assert.All(result.Attempts, attempt => Assert.Equal("InfrastructureFailed", attempt.Status));
        if (failure is "missing" or "corrupt") Assert.Empty(backend.Requests);
    }

    [Fact]
    public async Task Claim_scope_observes_external_renewal_and_rejects_incomplete_or_cross_attempt_results()
    {
        var token = TestContext.Current.CancellationToken;
        await using var workers = Workers(new ScriptedBackend());
        await DrainAsync(workers, token);
        await using var factory = AccountTestHost.Factory();
        using var client = AccountTestHost.Client(factory);
        var owner = (await (await AccountTestHost.RegisterAsync(client, AccountTestHost.Email()))
            .Content.ReadFromJsonAsync<AccountResponse>(token))!;
        var seeded = await SeedAsync(factory.Services, owner.Id, token);
        await AccountTestHost.CsrfAsync(client);
        var admitted = await client.PostAsJsonAsync($"/api/problems/{seeded.Problem.Id}/submissions",
            new CreateSubmissionRequest("cpp20", "int main(){}"), token);
        var id = (await admitted.Content.ReadFromJsonAsync<SubmissionResponse>(token))!.Id;
        await using var claimScope = workers.CreateAsyncScope();
        var jobs = claimScope.ServiceProvider.GetRequiredService<IJudgeJobRepository>();
        var now = DateTimeOffset.UtcNow;
        var work = (await jobs.ClaimNextAsync("m6-renew-worker", now, token))!;
        Assert.Equal(id, work.SubmissionId);
        await using (var renewalScope = workers.CreateAsyncScope())
            Assert.Equal(LeaseMutationResult.Accepted,
                await renewalScope.ServiceProvider.GetRequiredService<IJudgeJobRepository>()
                    .RenewAsync(work.AttemptId, work.LeaseToken, work.WorkerId, now.AddSeconds(20), token));
        var incomplete = JudgingOutcome.Create(SubmissionVerdict.Accepted, "", [new(1, TestCaseResultStatus.Passed, 1, 1, 1024, 0)]);
        await Assert.ThrowsAsync<Minos.Domain.DomainValidationException>(() =>
            jobs.CompleteAsync(work.AttemptId, work.LeaseToken, work.WorkerId, incomplete, now.AddSeconds(35), token));
        var complete = JudgingOutcome.Create(SubmissionVerdict.Accepted, "",
            Enumerable.Range(1, 3).Select(order => new JudgingTestOutcome(order, TestCaseResultStatus.Passed, 1, 2, 1024, 0)).ToArray());
        Assert.Equal(LeaseMutationResult.Accepted,
            await jobs.CompleteAsync(work.AttemptId, work.LeaseToken, work.WorkerId, complete, now.AddSeconds(35), token));

        await using var connection = new NpgsqlConnection(PostgresPrivilegeTests.Connection("MINOS_TEST_MIGRATION_CONNECTION"));
        await connection.OpenAsync(token);
        var sql = "INSERT INTO evaluation.test_case_results (id,submission_id,attempt_id,test_order,status,wall_timeout,cpu_limit,memory_failure,output_limit) VALUES (@id,@submission,@attempt,4,1,false,false,false,false)";
        await using var invalid = new NpgsqlCommand(sql, connection);
        invalid.Parameters.AddWithValue("id", Guid.NewGuid());
        invalid.Parameters.AddWithValue("submission", id);
        invalid.Parameters.AddWithValue("attempt", work.AttemptId);
        Assert.Equal(PostgresErrorCodes.CheckViolation,
            (await Assert.ThrowsAsync<PostgresException>(() => invalid.ExecuteNonQueryAsync(token))).SqlState);
        invalid.CommandText = "INSERT INTO evaluation.test_case_results (id,submission_id,attempt_id,test_order,status,cpu_time_ms,wall_time_ms,memory_peak_bytes,exit_code,wall_timeout,cpu_limit,memory_failure,output_limit) VALUES (@id,@submission,@attempt,4,1,1,1,1,0,false,false,false,false)";
        invalid.Parameters["attempt"].Value = Guid.NewGuid();
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation,
            (await Assert.ThrowsAsync<PostgresException>(() => invalid.ExecuteNonQueryAsync(token))).SqlState);
    }

    private static ServiceProvider Workers(IExecutionBackend backend)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMinosInfrastructure(PostgresPrivilegeTests.Connection("MINOS_TEST_WORKER_CONNECTION"));
        services.AddSingleton(new PrivateArtifactDirectory(Environment.GetEnvironmentVariable("Minos__Artifacts__Root"), ArchitectureTests.Repository));
        services.AddSingleton(backend);
        return services.BuildServiceProvider();
    }

    private static async Task DrainAsync(ServiceProvider workers, CancellationToken token)
    {
        for (var count = 0; count < 500; count++)
        {
            await using var scope = workers.CreateAsyncScope();
            var jobs = scope.ServiceProvider.GetRequiredService<IJudgeJobRepository>();
            var now = DateTimeOffset.UtcNow.AddDays(count + 1);
            var work = await jobs.ClaimNextAsync("m6-drain", now, token);
            if (work is null) return;
            await jobs.FailAsync(work.AttemptId, work.LeaseToken, work.WorkerId,
                InfrastructureFailureKind.ExecutorFailure, now.AddMilliseconds(1), token);
        }
        throw new InvalidOperationException("Unexpected fixture job count.");
    }

    private static async Task<(Problem Problem, PublishedProblemVersion Version)> SeedAsync(IServiceProvider services, Guid owner, CancellationToken token)
    {
        await using var scope = services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IProblemArtifactStore>();
        var problem = Problem.Create(owner, "M6 fixture", "Print A + B.", DateTimeOffset.UtcNow);
        problem.ConfigureExecution(1000, 64, BuiltInChecker.Exact, DateTimeOffset.UtcNow);
        problem.ReplaceSamples([new("1 2\n", "3\n")], DateTimeOffset.UtcNow);
        foreach (var (input, output) in new[] { ("1 2\n", "3\n"), ("19 23\n", "42\n"), ("-7 10\n", "3\n") })
        {
            var stored = await StoreAsync(store, input, output, token);
            problem.AddTestCase(stored.Input, stored.Output, DateTimeOffset.UtcNow);
        }
        var version = problem.Publish(owner, new(true, true, true), DateTimeOffset.UtcNow);
        var database = scope.ServiceProvider.GetRequiredService<MinosDbContext>();
        database.Problems.Add(problem);
        database.PublishedProblemVersions.Add(version);
        await database.SaveChangesAsync(token);
        return (problem, version);
    }

    private static async Task<StoredProblemTestArtifacts> StoreAsync(IProblemArtifactStore store, string input, string output, CancellationToken token)
    {
        using var package = new MemoryStream();
        using (var zip = new ZipArchive(package, ZipArchiveMode.Create, true))
            foreach (var (name, contents) in new[] { ("input.txt", input), ("output.txt", output) })
            {
                await using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                await writer.WriteAsync(contents.AsMemory(), token);
            }
        package.Position = 0;
        return await store.StorePackageAsync(package, token);
    }

    private static ExecutionEvidence Complete(byte[]? stdout = null, byte[]? artifact = null) => new(
        0, false, false, false, false, false, TimeSpan.FromMilliseconds(2), TimeSpan.FromMilliseconds(1),
        1024, stdout ?? [], [], artifact, true, true, null);

    private sealed class ScriptedBackend(params ExecutionEvidence[] evidence) : IExecutionBackend
    {
        public List<ExecutionRequest> Requests { get; } = [];
        public Task<ExecutionEvidence> ExecuteAsync(ExecutionRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult(evidence[Math.Min(Requests.Count - 1, evidence.Length - 1)]);
        }
    }

    private sealed class SubmissionReadGate : DbCommandInterceptor
    {
        private int intercepted;
        public bool Armed { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Armed && command.CommandText.Contains("FROM evaluation.submissions", StringComparison.Ordinal) &&
                Interlocked.CompareExchange(ref intercepted, 1, 0) == 0)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
