using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Minos.Api.Accounts;
using Minos.Api.Submissions;
using Minos.Application.Evaluation;
using Minos.Infrastructure.Evaluation;
using Minos.Infrastructure.Persistence;
using Minos.JudgeWorker;
using Xunit;

namespace Minos.IntegrationTests;

public sealed class JudgingConcurrencyIntegrationTests
{
    [Fact]
    public async Task Delivery_is_only_an_id_and_duplicate_or_expired_ids_keep_database_attempt_authority()
    {
        var token = TestContext.Current.CancellationToken;
        await using var workers = OfficialJudgingIntegrationTests.Workers(new FixtureBackend());
        await OfficialJudgingIntegrationTests.DrainAsync(workers, token);
        await using var factory = AccountTestHost.Factory();
        using var client = AccountTestHost.Client(factory);
        var account = (await (await AccountTestHost.RegisterAsync(client, AccountTestHost.Email())).Content.ReadFromJsonAsync<AccountResponse>(token))!;
        var problem = await OfficialJudgingIntegrationTests.SeedAsync(factory.Services, account.Id, token);
        var id = await Admit(client, problem.Problem.Id, "accepted", token);
        Guid jobId;
        await using (var scope = workers.CreateAsyncScope())
        {
            var delivery = scope.ServiceProvider.GetRequiredService<IJudgeJobDelivery>();
            jobId = (await delivery.ReceiveAsync(token))!.Value;
            Assert.Equal(jobId, await delivery.ReceiveAsync(token)); // Receiving doesn't claim or consume durable work.
        }
        await using var first = workers.CreateAsyncScope();
        var cases = first.ServiceProvider.GetRequiredService<JudgeJobUseCases>();
        var work = (await cases.ClaimAsync(jobId, "id-worker", token))!;
        Assert.Equal(id, work.SubmissionId);
        await using (var duplicate = workers.CreateAsyncScope())
        {
            Assert.Null(await duplicate.ServiceProvider.GetRequiredService<JudgeJobUseCases>().ClaimAsync(jobId, "duplicate-worker", token));
            Assert.Null(await duplicate.ServiceProvider.GetRequiredService<IJudgeJobDelivery>().ReceiveAsync(token));
        }
        Assert.Equal(LeaseMutationResult.Accepted, await cases.ProcessAsync(work, token));
        Assert.Null(await cases.ClaimAsync(jobId, "completed-delivery", token));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => cases.ClaimAsync(Guid.Empty, "id-worker", token));

        await Admit(client, problem.Problem.Id, "accepted", token);
        Guid retryId;
        await using (var scope = workers.CreateAsyncScope())
            retryId = (await scope.ServiceProvider.GetRequiredService<IJudgeJobDelivery>().ReceiveAsync(token))!.Value;
        await using var attemptScope = workers.CreateAsyncScope();
        var repository = attemptScope.ServiceProvider.GetRequiredService<IJudgeJobRepository>();
        var now = DateTimeOffset.UtcNow;
        var expired = (await repository.ClaimAsync(retryId, "expired-worker", now.AddMinutes(-1), token));
        // A Pending job cannot be claimed before admission time.
        Assert.Null(expired);
        var current = (await repository.ClaimAsync(retryId, "expired-worker", now, token))!;
        await using var reclaimedScope = workers.CreateAsyncScope();
        var reclaimedRepository = reclaimedScope.ServiceProvider.GetRequiredService<IJudgeJobRepository>();
        var reclaimed = (await reclaimedRepository.ClaimAsync(retryId, "reclaim-worker", now.AddSeconds(31), token))!;
        Assert.Equal(LeaseMutationResult.Stale, await repository.RenewAsync(current.AttemptId, current.LeaseToken, current.WorkerId, now.AddSeconds(32), token));
        Assert.NotEqual(current.LeaseToken, reclaimed.LeaseToken);
        var complete = Minos.Domain.Evaluation.JudgingOutcome.Create(Minos.Domain.Evaluation.SubmissionVerdict.Accepted, "",
            Enumerable.Range(1, 3).Select(order => new Minos.Domain.Evaluation.JudgingTestOutcome(order, Minos.Domain.Evaluation.TestCaseResultStatus.Passed, 1, 1, 1024, 0)).ToArray());
        Assert.Equal(LeaseMutationResult.Stale, await repository.CompleteAsync(current.AttemptId, current.LeaseToken, current.WorkerId, complete, now.AddSeconds(32), token));
        await using (var thirdScope = workers.CreateAsyncScope())
            Assert.NotNull(await thirdScope.ServiceProvider.GetRequiredService<IJudgeJobRepository>().ClaimAsync(retryId, "third-worker", now.AddSeconds(62), token));
        await using (var exhaustedScope = workers.CreateAsyncScope())
            Assert.Null(await exhaustedScope.ServiceProvider.GetRequiredService<IJudgeJobRepository>().ClaimAsync(retryId, "exhausted-worker", now.AddSeconds(93), token));
        await using var evidenceScope = workers.CreateAsyncScope();
        var database = evidenceScope.ServiceProvider.GetRequiredService<MinosDbContext>();
        Assert.Equal(Minos.Domain.Evaluation.JudgeJobStatus.SystemFailed, (await database.JudgeJobs.AsNoTracking().SingleAsync(job => job.Id == retryId, token)).Status);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public Task Bounded_worker_load_preserves_all_test_counts_queue_fairness_and_owner_access(int concurrency) => LoadAsync(real: false, concurrency);

    public static bool SandboxEnabled => DockerExecutionTests.SandboxEnabled;
    [Theory(Skip = "Requires the approved Linux sandbox profile.", SkipUnless = nameof(SandboxEnabled))]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public Task Real_sandboxes_execute_mixed_jobs_with_the_shared_budget(int concurrency) => LoadAsync(real: true, concurrency);

    public static bool BurstEnabled => SandboxTestProfile.Native && SandboxTestProfile.BoxCount >= 2 && Environment.GetEnvironmentVariable("MINOS_TEST_BURST") == "1";
    [Theory(Skip = "Opt-in twenty-job trusted native burst benchmark.", SkipUnless = nameof(BurstEnabled))]
    [InlineData(2)]
    [InlineData(8)]
    public Task Twenty_submissions_preserve_counts_under_compile_runtime_quotas(int concurrency) => LoadAsync(true, concurrency, burst: true);

    private static async Task LoadAsync(bool real, int concurrency, bool burst = false)
    {
        var token = TestContext.Current.CancellationToken;
        var fixture = real ? null : new FixtureBackend();
        var slots = concurrency >= 4 ? 4 : 2;
        var compilers = concurrency >= 4 ? 2 : 1;
        var runtimes = real && SandboxTestProfile.Native ? Math.Min(SandboxTestProfile.BoxCount, concurrency >= 4 ? 2 : 1) : concurrency >= 4 ? 2 : 1;
        var memory = concurrency >= 4 ? 2048 : 1152;
        await using var workers = OfficialJudgingIntegrationTests.Workers(fixture, new(slots, memory, 256 + 64 * concurrency, compilers, runtimes));
        await OfficialJudgingIntegrationTests.DrainAsync(workers, token);
        if (real) await workers.GetRequiredService<Minos.Infrastructure.Hosting.JudgeSandboxLifecycle>().InitializeAsync(token);
        await using var factory = AccountTestHost.Factory();
        using var owner = AccountTestHost.Client(factory);
        using var other = AccountTestHost.Client(factory);
        using var third = AccountTestHost.Client(factory);
        using var fourth = AccountTestHost.Client(factory);
        var account = (await (await AccountTestHost.RegisterAsync(owner, AccountTestHost.Email())).Content.ReadFromJsonAsync<AccountResponse>(token))!;
        await AccountTestHost.RegisterAsync(other, AccountTestHost.Email());
        if (burst) { await AccountTestHost.RegisterAsync(third, AccountTestHost.Email()); await AccountTestHost.RegisterAsync(fourth, AccountTestHost.Email()); }
        var count = real && !SandboxTestProfile.Native ? 3 : 100;
        var problem = await OfficialJudgingIntegrationTests.SeedAsync(factory.Services, account.Id, token, count);
        var admitted = new List<(Guid Id, string Mode, long Start)>();
        var owners = new Dictionary<Guid, HttpClient>();
        var maxActiveJobs = 0L; var activeJobs = 0L; var maxSandboxes = 0L; var activeSandboxes = 0L; var reserved = 0L; var maxReserved = 0L;
        var startedJobs = new List<Guid>();
        var activeCompile = 0L; var activeRuntime = 0L; var maxCompile = 0L; var maxRuntime = 0L;
        var compilerMemory = new List<long>();
        var queue = new ConcurrentDictionary<Guid, double>();
        var completion = new ConcurrentDictionary<Guid, double>();
        var timer = Stopwatch.StartNew();
        foreach (var client in burst ? new[] { owner, other, third, fourth } : new[] { owner, other })
            foreach (var mode in burst ? new[] { "accepted", "wrong", "runtime", "compile", "accepted" } : new[] { "accepted", "wrong", "runtime", "compile" })
            {
                var id = await Admit(client, problem.Problem.Id, mode, token);
                admitted.Add((id, mode, timer.ElapsedTicks));
                owners.Add(id, client);
            }
        var admissionSeconds = timer.Elapsed.TotalSeconds;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name is "Minos.JudgeWorker" or "Minos.Judging") meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            lock (startedJobs)
            {
                if (instrument.Name == "minos.worker.active_jobs") { activeJobs += value; maxActiveJobs = Math.Max(maxActiveJobs, activeJobs); }
                if (instrument.Name == "minos.judging.active_sandboxes") { activeSandboxes += value; maxSandboxes = Math.Max(maxSandboxes, activeSandboxes); }
                if (instrument.Name == "minos.judging.reserved_memory") { reserved += value; maxReserved = Math.Max(maxReserved, reserved); }
                if (instrument.Name == "minos.judging.compiler_memory_peak") compilerMemory.Add(value);
                if (instrument.Name == "minos.judging.active_stages")
                {
                    var compileStage = false;
                    foreach (var tag in tags) if (tag.Key == "stage" && Equals(tag.Value, "compile")) compileStage = true;
                    if (compileStage) { activeCompile += value; maxCompile = Math.Max(maxCompile, activeCompile); }
                    else { activeRuntime += value; maxRuntime = Math.Max(maxRuntime, activeRuntime); }
                }
            }
        });
        listener.Start();
        using var worker = new SubmissionJobWorker(workers.GetRequiredService<IServiceScopeFactory>(), new(concurrency), NullLogger<SubmissionJobWorker>.Instance);
        await worker.StartAsync(token);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromMinutes(3));
            while (completion.Count != admitted.Count)
            {
                foreach (var item in admitted)
                {
                    if (completion.ContainsKey(item.Id)) continue;
                    var result = (await owners[item.Id].GetFromJsonAsync<SubmissionResponse>($"/api/submissions/{item.Id}", deadline.Token))!;
                    var elapsed = (timer.ElapsedTicks - item.Start) / (double)Stopwatch.Frequency;
                    if (result.Attempts.Count != 0) queue.TryAdd(item.Id, (result.Attempts[0].StartedAtUtc - result.CreatedAtUtc).TotalSeconds);
                    if (result.Status != "Completed") continue;
                    completion.TryAdd(item.Id, elapsed);
                    var expectedPassed = item.Mode switch { "accepted" => count, "wrong" => count - (count + 1) / 3, _ => 0 };
                    var executed = item.Mode == "compile" ? 0 : count;
                    Assert.Equal(new SubmissionTestSummaryResponse(count, executed, expectedPassed, executed - expectedPassed), result.Result!.TestSummary);
                    Assert.Equal(item.Mode switch { "accepted" => "Accepted", "wrong" => "WrongAnswer", "runtime" => "RuntimeError", _ => "CompilationError" }, result.Result.Verdict);
                    Assert.Equal(Enumerable.Range(1, count), result.Result.Tests.Select(test => test.Order));
                    if (executed != 0) Assert.All(result.Result.Tests, test => Assert.NotNull(test.MemoryPeakBytes));
                    Assert.Single(result.Attempts);
                }
                if (completion.Count != admitted.Count) await Task.Delay(20, deadline.Token);
            }
        }
        finally { await worker.StopAsync(CancellationToken.None); }
        Assert.Equal(0, activeJobs); Assert.Equal(0, activeSandboxes); Assert.Equal(0, reserved);
        Assert.Equal(concurrency, maxActiveJobs);
        Assert.InRange(maxSandboxes, 1, slots);
        Assert.InRange(maxCompile, 1, compilers); Assert.InRange(maxRuntime, 1, runtimes);
        Assert.Equal(0, activeCompile); Assert.Equal(0, activeRuntime);
        Assert.Equal(admitted.Count, compilerMemory.Count);
        Assert.InRange(maxReserved, 1, memory * 1024L * 1024);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync($"/api/submissions/{admitted[0].Id}", token)).StatusCode);
        await using (var scope = workers.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<MinosDbContext>();
            var ids = admitted.Select(item => item.Id).ToArray();
            var starts = await database.JudgingAttempts.Where(attempt => ids.Contains(attempt.SubmissionId)).OrderBy(attempt => attempt.StartedAtUtc).Select(attempt => attempt.SubmissionId).ToArrayAsync(token);
            Assert.Equal(ids, starts); // Older ready work is claimed before newer work; completion can differ.
        }
        static double P95(IEnumerable<double> values) { var samples = values.Order().ToArray(); return samples[(int)Math.Ceiling(samples.Length * .95) - 1]; }
        TestContext.Current.TestOutputHelper!.WriteLine("JUDGING_CONCURRENCY_JSON=" + JsonSerializer.Serialize(new
        {
            measuredAtUtc = DateTimeOffset.UtcNow, runner = real ? (SandboxTestProfile.Native ? "isolate" : "docker") : "controlled-fixture",
            jobs = admitted.Count, testsPerJob = count, maximumConcurrentJobs = maxActiveJobs, maximumSandboxes = maxSandboxes,
            maximumReservedBytes = maxReserved, compileSlots = compilers, runtimeSlots = runtimes,
            isolateBoxes = real && SandboxTestProfile.Native ? SandboxTestProfile.BoxCount : 0,
            maximumCompilers = maxCompile, maximumRuntimes = maxRuntime, compilerMemoryPeakBytes = compilerMemory.Max(), admissionSeconds,
            queueP95Seconds = P95(queue.Values), completionP95Seconds = P95(completion.Values),
            elapsedSeconds = timer.Elapsed.TotalSeconds,
            results = admitted.Select(item => new { scenario = item.Mode, passed = item.Mode == "accepted" ? count : item.Mode == "wrong" ? count - (count + 1) / 3 : 0,
                executed = item.Mode == "compile" ? 0 : count }),
            scope = "HTTP/PostgreSQL + actual bounded Worker; queue from persisted timestamps; completion admission-response to owner read via 20-ms polling; known mixed fixtures, not public capacity/SLA"
        }));
    }

    private static async Task<Guid> Admit(HttpClient client, Guid problem, string mode, CancellationToken token)
    {
        await AccountTestHost.CsrfAsync(client);
        var source = mode switch
        {
            "accepted" => "#include <iostream>\nint main(){int a,b;std::cin>>a>>b;std::cout<<a+b<<'\\n';}",
            "wrong" => "#include <iostream>\nint main(){int a,b;std::cin>>a>>b;std::cout<<(a==19?5:a+b)<<'\\n';}",
            "runtime" => "int main(){return 7;}", _ => "int main(){compile_fail;}"
        };
        var response = await client.PostAsJsonAsync($"/api/problems/{problem}/submissions", new CreateSubmissionRequest("cpp20", source), token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SubmissionResponse>(token))!.Id;
    }

    private sealed class FixtureBackend : IExecutionBackend
    {
        public async Task<ExecutionEvidence> ExecuteAsync(ExecutionRequest request, CancellationToken token)
        {
            await Task.Delay(20, token);
            var code = Encoding.UTF8.GetString(request.Payload);
            var exit = 0; byte[]? artifact = null; byte[] stdout = [];
            if (request.Stage == ExecutionStage.Compile)
            {
                if (code.Contains("compile_fail", StringComparison.Ordinal)) exit = 1;
                else artifact = [(byte)(code.Contains("return 7", StringComparison.Ordinal) ? 2 : code.Contains("a==19", StringComparison.Ordinal) ? 1 : 0)];
            }
            else if (request.Payload[0] == 2) exit = 7;
            else
            {
                var numbers = Encoding.UTF8.GetString(request.Input).Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
                stdout = Encoding.UTF8.GetBytes((request.Payload[0] == 1 && numbers[0] == 19 ? 5 : numbers.Sum()) + "\n");
            }
            return new(exit, false, false, false, false, false, TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(1), 1024,
                stdout, [], artifact, true, true, null);
        }
    }
}
