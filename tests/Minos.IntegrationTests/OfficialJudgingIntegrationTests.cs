using System.IO.Compression;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Collections.Concurrent;
using System.Text.Json;
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
using Minos.Infrastructure.Evaluation;
using Minos.Infrastructure.Hosting;
using Minos.Infrastructure.Persistence;
using Npgsql;
using Xunit;

namespace Minos.IntegrationTests;

public sealed class OfficialJudgingIntegrationTests
{
    public static bool SandboxEnabled => DockerExecutionTests.SandboxEnabled;
    public static bool NativeBenchmarkEnabled => SandboxTestProfile.Native && DockerExecutionTests.BenchmarkEnabled;

    [Fact(Skip = "Opt-in official 100-test benchmark in the trusted native Linux profile.", SkipUnless = nameof(NativeBenchmarkEnabled))]
    public async Task Native_official_100_tests_preserve_every_result_and_owner_counts()
    {
        var token = TestContext.Current.CancellationToken;
        await using var workers = Workers();
        await DrainAsync(workers, token);
        var timer = Stopwatch.StartNew();
        await workers.GetRequiredService<JudgeSandboxLifecycle>().InitializeAsync(token);
        var initializeSeconds = timer.Elapsed.TotalSeconds;
        await using var factory = AccountTestHost.Factory();
        using var client = AccountTestHost.Client(factory);
        var owner = (await (await AccountTestHost.RegisterAsync(client, AccountTestHost.Email())).Content.ReadFromJsonAsync<AccountResponse>(token))!;
        var seeded = await SeedAsync(factory.Services, owner.Id, token, testCount: 100);
        foreach (var wrong in new[] { false, true })
        {
            var measurements = new ConcurrentBag<(string Stage, string Phase, double Seconds)>();
            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == "Minos.Judging") meterListener.EnableMeasurementEvents(instrument);
            };
            listener.SetMeasurementEventCallback<double>((_, seconds, tags, _) => measurements.Add(((string)tags[0].Value!, (string)tags[1].Value!, seconds)));
            listener.Start();
            await AccountTestHost.CsrfAsync(client);
            var source = wrong
                ? "#include <iostream>\nint main(){int a,b;std::cin>>a>>b;std::cout<<(a==19?5:a+b)<<'\\n';}"
                : "#include <iostream>\nint main(){int a,b;std::cin>>a>>b;std::cout<<a+b<<'\\n';}";
            timer.Restart();
            var created = await client.PostAsJsonAsync($"/api/problems/{seeded.Problem.Id}/submissions", new CreateSubmissionRequest("cpp20", source), token);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var id = (await created.Content.ReadFromJsonAsync<SubmissionResponse>(token))!.Id;
            var admissionSeconds = timer.Elapsed.TotalSeconds;
            await using (var scope = workers.CreateAsyncScope())
            {
                var cases = scope.ServiceProvider.GetRequiredService<JudgeJobUseCases>();
                var work = (await cases.ClaimAsync("native-100-benchmark", token))!;
                Assert.Equal(id, work.SubmissionId);
                Assert.Equal(LeaseMutationResult.Accepted, await cases.ProcessAsync(work, token));
            }
            var response = (await client.GetFromJsonAsync<SubmissionResponse>($"/api/submissions/{id}", token))!;
            var elapsed = timer.Elapsed.TotalSeconds;
            Assert.Equal("Completed", response.Status);
            Assert.Equal(wrong ? "WrongAnswer" : "Accepted", response.Result!.Verdict);
            Assert.Equal(new SubmissionTestSummaryResponse(100, 100, wrong ? 67 : 100, wrong ? 33 : 0), response.Result.TestSummary);
            Assert.Equal(Enumerable.Range(1, 100), response.Result.Tests.Select(test => test.Order));
            Assert.All(response.Result.Tests, test => { Assert.NotNull(test.CpuTimeMilliseconds); Assert.NotNull(test.MemoryPeakBytes); });
            if (wrong) Assert.Equal("Passed", response.Result.Tests[99].Status);
            Assert.Equal(100, measurements.Count(sample => sample.Stage == "submission" && sample.Phase == "check"));
            var phases = measurements.GroupBy(sample => (sample.Stage, sample.Phase)).Select(group => new
            { stage = group.Key.Stage, phase = group.Key.Phase, count = group.Count(), seconds = group.Sum(sample => sample.Seconds) }).ToArray();
            TestContext.Current.TestOutputHelper!.WriteLine("OFFICIAL_NATIVE_BENCHMARK_JSON=" + JsonSerializer.Serialize(new
            {
                wrong, initializeSeconds, admissionSeconds, admissionThroughOwnerResultSeconds = elapsed,
                scope = "real HTTP admission/read + PostgreSQL claim/compile/100 verified reads/checks/result commit; immediate claim, excludes queue polling wait and startup/fixture setup",
                testSummary = response.Result.TestSummary, phases
            }));
        }
    }

    [Theory(Skip = "Requires the approved Linux Docker sandbox profile.", SkipUnless = nameof(SandboxEnabled))]
    [InlineData("accepted", "Accepted", 3, 0)]
    [InlineData("bits", "Accepted", 3, 0)]
    [InlineData("wrong", "WrongAnswer", 2, 1)]
    [InlineData("compile", "CompilationError", 0, 0)]
    [InlineData("runtime", "RuntimeError", 0, 3)]
    public async Task Real_sandbox_persists_complete_test_counts_through_postgres_and_owner_http(
        string scenario, string verdict, int passed, int failed)
    {
        var token = TestContext.Current.CancellationToken;
        var source = scenario switch
        {
            "accepted" => "#include <iostream>\nint main(){int a,b;std::cin>>a>>b;std::cout<<a+b<<'\\n';}",
            "bits" => "#include <bits/stdc++.h>\nint main(){int a,b;std::cin>>a>>b;std::cout<<a+b<<'\\n';}",
            "wrong" => "#include <iostream>\nint main(){int a,b;std::cin>>a>>b;std::cout<<(a==19?5:a+b)<<'\\n';}",
            "compile" => "int main(){ this is not C++; }",
            _ => "int main(){return 7;}"
        };
        await using var workers = Workers();
        await DrainAsync(workers, token);
        await using var factory = AccountTestHost.Factory();
        using var client = AccountTestHost.Client(factory);
        var owner = (await (await AccountTestHost.RegisterAsync(client, AccountTestHost.Email()))
            .Content.ReadFromJsonAsync<AccountResponse>(token))!;
        var seeded = await SeedAsync(factory.Services, owner.Id, token);
        await AccountTestHost.CsrfAsync(client);
        var created = await client.PostAsJsonAsync($"/api/problems/{seeded.Problem.Id}/submissions",
            new CreateSubmissionRequest("cpp20", source), token);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<SubmissionResponse>(token))!.Id;
        await using (var scope = workers.CreateAsyncScope())
        {
            var cases = scope.ServiceProvider.GetRequiredService<JudgeJobUseCases>();
            var work = (await cases.ClaimAsync("phase2-native-worker", token))!;
            Assert.Equal(id, work.SubmissionId);
            Assert.Equal(LeaseMutationResult.Accepted, await cases.ProcessAsync(work, token));
        }
        var result = (await client.GetFromJsonAsync<SubmissionResponse>($"/api/submissions/{id}", token))!;
        Assert.Equal("Completed", result.Status);
        Assert.Equal(verdict, result.Result!.Verdict);
        Assert.Equal(new SubmissionTestSummaryResponse(3, passed + failed, passed, failed), result.Result.TestSummary);
        if (scenario == "wrong") Assert.Equal(new[] { "Passed", "WrongAnswer", "Passed" }, result.Result.Tests.Select(test => test.Status));
        if (scenario == "compile") Assert.All(result.Result.Tests, test => Assert.Equal("NotExecuted", test.Status));
        else Assert.All(result.Result.Tests, test => Assert.NotNull(test.MemoryPeakBytes));
    }

    [Fact]
    public async Task All_tests_including_later_passes_are_committed_with_pinned_version_and_counts_after_restart()
    {
        var token = TestContext.Current.CancellationToken;
        var backend = new ScriptedBackend(
            Complete(artifact: [1]), Complete(stdout: "3\n"u8.ToArray()), Complete(stdout: "wrong\n"u8.ToArray()),
            Complete(stdout: "3\n"u8.ToArray()));
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
            Assert.Equal(4, backend.Requests.Count); // One compilation and all three pinned tests.
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
        Assert.Equal(["Passed", "WrongAnswer", "Passed"], result.Result.Tests.Select(test => test.Status));
        Assert.NotNull(result.Result.Tests[2].MemoryPeakBytes);
        Assert.Equal(new SubmissionTestSummaryResponse(3, 3, 2, 1), result.Result.TestSummary);
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
        Assert.Equal(new SubmissionTestSummaryResponse(3, 3, 3, 0), after.Result.TestSummary);
        Assert.Equal("Succeeded", Assert.Single(after.Attempts).Status);
    }

    [Theory]
    [InlineData("mixed", 3, 1, 2, "RuntimeError")]
    [InlineData("compile", 3, 0, 0, "CompilationError")]
    [InlineData("hundred", 100, 37, 63, "WrongAnswer")]
    [InlineData("maximum", 1500, 1437, 63, "WrongAnswer")]
    [InlineData("compile", 1500, 0, 0, "CompilationError")]
    [InlineData("historical", 3, 1, 1, "WrongAnswer")]
    public async Task Owner_counts_cover_all_tests_mixed_failures_compilation_and_historical_evidence(
        string scenario, int testCount, int passed, int failed, string verdict)
    {
        var token = TestContext.Current.CancellationToken;
        var expected = new[] { "3\n", "42\n", "3\n" };
        ExecutionEvidence[] evidence = scenario switch
        {
            "compile" => [Complete() with { ExitCode = 1, Stderr = "main.cpp: error"u8.ToArray() }],
            "mixed" => [Complete(artifact: [1]), Complete() with { ExitCode = 9 },
                Complete(stdout: "42\n"u8.ToArray()), Complete(stdout: "wrong\n"u8.ToArray())],
            "historical" => [Complete(artifact: [1]), Complete(stdout: "3\n"u8.ToArray()),
                Complete(stdout: "wrong\n"u8.ToArray()), Complete(stdout: "3\n"u8.ToArray())],
            _ => new[] { Complete(artifact: [1]) }.Concat(Enumerable.Range(0, testCount)
                .Select(index => Complete(stdout: System.Text.Encoding.UTF8.GetBytes(index < 63 ? "wrong\n" : expected[index % 3])))).ToArray()
        };
        var backend = new ScriptedBackend(evidence);
        await using var workers = Workers(backend);
        await DrainAsync(workers, token);
        await using var factory = AccountTestHost.Factory();
        using var client = AccountTestHost.Client(factory);
        var owner = (await (await AccountTestHost.RegisterAsync(client, AccountTestHost.Email()))
            .Content.ReadFromJsonAsync<AccountResponse>(token))!;
        var seeded = await SeedAsync(factory.Services, owner.Id, token, testCount);
        await AccountTestHost.CsrfAsync(client);
        var created = await client.PostAsJsonAsync($"/api/problems/{seeded.Problem.Id}/submissions",
            new CreateSubmissionRequest("cpp20", "int main(){}"), token);
        var id = (await created.Content.ReadFromJsonAsync<SubmissionResponse>(token))!.Id;
        await using (var scope = workers.CreateAsyncScope())
        {
            var cases = scope.ServiceProvider.GetRequiredService<JudgeJobUseCases>();
            var work = (await cases.ClaimAsync("complete-test-worker", token))!;
            Assert.Equal(id, work.SubmissionId);
            Assert.Equal(LeaseMutationResult.Accepted, await cases.ProcessAsync(work, token));
        }
        Assert.Equal(scenario == "compile" ? 1 : testCount + 1, backend.Requests.Count);
        if (scenario == "historical")
        {
            // Reproduce durable pre-change M6 evidence; a read must not upgrade or relabel it.
            await using var migration = PostgresPrivilegeTests.Database(PostgresPrivilegeTests.Connection("MINOS_TEST_MIGRATION_CONNECTION"));
            Assert.Equal(1, await migration.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE evaluation.test_case_results SET status=0,cpu_time_ms=NULL,wall_time_ms=NULL,
                    memory_peak_bytes=NULL,exit_code=NULL,wall_timeout=false,cpu_limit=false,
                    memory_failure=false,output_limit=false
                WHERE submission_id={id} AND test_order=3
                """, token));
        }
        var response = (await client.GetFromJsonAsync<SubmissionResponse>($"/api/submissions/{id}", token))!;
        Assert.Equal("Completed", response.Status);
        Assert.Equal(verdict, response.Result!.Verdict);
        Assert.Equal(new SubmissionTestSummaryResponse(testCount, passed + failed, passed, failed), response.Result.TestSummary);
        Assert.Equal(testCount, response.Result.Tests.Count);
        Assert.Equal(testCount, response.Configuration!.TestCount);
        Assert.Equal(Enumerable.Range(1, testCount), response.Result.Tests.Select(test => test.Order));
        Assert.Equal(passed, response.Result.Tests.Count(test => test.Status == "Passed"));
        Assert.Equal(testCount - passed - failed, response.Result.Tests.Count(test => test.Status == "NotExecuted"));
        if (scenario == "historical") Assert.Equal("NotExecuted", response.Result.Tests[2].Status);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("corrupt")]
    [InlineData("runner")]
    [InlineData("snapshot")]
    [InlineData("late-runner")]
    [InlineData("late-snapshot")]
    [InlineData("late-cleanup")]
    public async Task Untrustworthy_judging_retries_and_exhausts_without_a_contestant_verdict(string failure)
    {
        var token = TestContext.Current.CancellationToken;
        var fault = failure switch
        {
            "snapshot" or "late-snapshot" => Complete() with { ResourceSnapshotComplete = false },
            "late-cleanup" => Complete() with { CleanupComplete = false },
            _ => Complete() with { InfrastructureFailure = "FixtureRunnerFault" }
        };
        var backend = failure.StartsWith("late-", StringComparison.Ordinal)
            ? new ScriptedBackend(Complete(artifact: [1]), Complete(stdout: "wrong\n"u8.ToArray()), fault)
            : new ScriptedBackend(fault);
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
            if (number == 1 && failure.StartsWith("late-", StringComparison.Ordinal))
                Assert.Equal(3, backend.Requests.Count); // Compile, contestant failure, then infrastructure failure.
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

    internal static ServiceProvider Workers(IExecutionBackend? backend = null, ExecutionBudgetOptions? budget = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        SandboxTestProfile.AddInfrastructure(services, PostgresPrivilegeTests.Connection("MINOS_TEST_WORKER_CONNECTION"), budget);
        services.AddSingleton(new PrivateArtifactDirectory(Environment.GetEnvironmentVariable("Minos__Artifacts__Root"), ArchitectureTests.Repository));
        if (backend is not null) services.AddSingleton<IExecutionBackend>(provider => budget is null ? backend :
            new BudgetedExecutionBackend(backend, provider.GetRequiredService<ExecutionBudget>()));
        return services.BuildServiceProvider();
    }

    internal static async Task DrainAsync(ServiceProvider workers, CancellationToken token)
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

    internal static async Task<(Problem Problem, PublishedProblemVersion Version)> SeedAsync(IServiceProvider services, Guid owner, CancellationToken token, int testCount = 3)
    {
        await using var scope = services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IProblemArtifactStore>();
        var problem = Problem.Create(owner, "M6 fixture", "Print A + B.", DateTimeOffset.UtcNow);
        problem.ConfigureExecution(1000, 64, BuiltInChecker.Exact, DateTimeOffset.UtcNow);
        problem.ReplaceSamples([new("1 2\n", "3\n")], DateTimeOffset.UtcNow);
        var fixtures = new[] { ("1 2\n", "3\n"), ("19 23\n", "42\n"), ("-7 10\n", "3\n") };
        foreach (var (input, output) in Enumerable.Range(0, testCount).Select(index => fixtures[index % fixtures.Length]))
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
