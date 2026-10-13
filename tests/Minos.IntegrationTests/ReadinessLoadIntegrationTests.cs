using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Minos.Api.Accounts;
using Minos.Api.Submissions;
using Minos.Application.Evaluation;
using Minos.Domain.Contests;
using Minos.Infrastructure.Evaluation;
using Minos.Infrastructure.Persistence;
using Minos.JudgeWorker;
using Xunit;

namespace Minos.IntegrationTests;

// Opt-in timings are observations, not machine-dependent assertions in correctness CI.
public sealed class ReadinessLoadIntegrationTests
{
    public static bool Enabled => DockerExecutionTests.SandboxEnabled && !SandboxTestProfile.Native &&
        Environment.GetEnvironmentVariable("MINOS_TEST_READINESS") == "1";

    [Theory(Skip = "Opt-in isolated Docker readiness workload.", SkipUnless = nameof(Enabled))]
    [InlineData(1, 3)]
    [InlineData(2, 3)]
    [InlineData(4, 3)]
    [InlineData(4, 100)]
    public async Task Measure_shared_admission_and_execution(int concurrency, int count)
    {
        var token = TestContext.Current.CancellationToken;
        var slots = Math.Min(concurrency >= 4 ? 4 : 2, Math.Max(1, Environment.ProcessorCount - 1));
        var quota = Math.Min(concurrency >= 4 ? 2 : 1, slots);
        var budget = new ExecutionBudgetOptions(slots, concurrency >= 4 ? 2048 : 1152, 256 + 64 * concurrency, quota, quota);
        await using var workers = OfficialJudgingIntegrationTests.Workers(budget: budget);
        await OfficialJudgingIntegrationTests.DrainAsync(workers, token);
        var initialization = Stopwatch.StartNew();
        await workers.GetRequiredService<Minos.Infrastructure.Hosting.JudgeSandboxLifecycle>().InitializeAsync(token);
        initialization.Stop();
        await using var factory = AccountTestHost.Factory();
        using var author = AccountTestHost.Client(factory);
        var organizer = (await (await AccountTestHost.RegisterAsync(author, AccountTestHost.Email())).Content.ReadFromJsonAsync<AccountResponse>(token))!;
        using var first = AccountTestHost.Client(factory); using var second = AccountTestHost.Client(factory);
        var firstAccount = (await (await AccountTestHost.RegisterAsync(first, AccountTestHost.Email())).Content.ReadFromJsonAsync<AccountResponse>(token))!;
        var secondAccount = (await (await AccountTestHost.RegisterAsync(second, AccountTestHost.Email())).Content.ReadFromJsonAsync<AccountResponse>(token))!;
        var seeded = await OfficialJudgingIntegrationTests.SeedAsync(factory.Services, organizer.Id, token, count);
        // Trusted fixture setup pins a real published contest; HTTP submission still enforces authorization/time.
        var now = DateTimeOffset.UtcNow;
        var earlier = now.AddMinutes(-2);
        var contest = Contest.Create(organizer.Id, "Readiness fixture", "", now.AddMinutes(-1), now.AddHours(1), earlier);
        contest.Edit(contest.Title, "", contest.StartsAtUtc, contest.EndsAtUtc,
            [new(seeded.Problem.Id, seeded.Version.Id)], earlier);
        contest.Publish(earlier);
        await using (var setup = factory.Services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<MinosDbContext>();
            db.Contests.Add(contest);
            db.ContestRegistrations.AddRange(ContestRegistration.Create(contest, firstAccount.Id, now), ContestRegistration.Create(contest, secondAccount.Id, now));
            await db.SaveChangesAsync(token);
        }
        await AccountTestHost.CsrfAsync(first); await AccountTestHost.CsrfAsync(second);
        var practicePath = $"/api/problems/{seeded.Problem.Id}/submissions";
        var contestPath = $"/api/contests/{contest.Id}/problems/{contest.Problems.Single().Id}/submissions";
        var modes = count == 100 ? new[] { "accepted" } : new[] { "accepted", "wrong", "runtime", "compile", "cpu", "compile-heavy", "memory", "output", "accepted" };
        var phases = new ConcurrentDictionary<string, ConcurrentQueue<double>>();
        var balances = new ConcurrentDictionary<string, long>(); var peaks = new ConcurrentDictionary<string, long>();
        var counters = new ConcurrentDictionary<string, long>();
        var unsafeTags = new ConcurrentQueue<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name is "Minos.Judging" or "Minos.JudgeWorker" or "Minos.Sandbox") l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
        {
            var stage = ""; var phase = "";
            foreach (var tag in tags)
            {
                if (tag.Key == "stage") stage = tag.Value?.ToString();
                else if (tag.Key == "phase") phase = tag.Value?.ToString();
                else unsafeTags.Enqueue(tag.Key);
            }
            var key = instrument.Name == "minos.judging.phase_duration" ? $"{stage}/{phase}" : instrument.Name;
            phases.GetOrAdd(key, _ => new()).Enqueue(value);
        });
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var stage = "";
            foreach (var tag in tags)
                if (tag.Key == "stage" && tag.Value is "compile" or "run") stage = "/" + tag.Value;
                else unsafeTags.Enqueue(tag.Key);
            var key = instrument.Name + stage;
            if (instrument is UpDownCounter<long>)
            {
                var current = balances.AddOrUpdate(key, value, (_, prior) => prior + value);
                peaks.AddOrUpdate(key, current, (_, prior) => Math.Max(current, prior));
            }
            else if (instrument is Counter<long>) counters.AddOrUpdate(key, value, (_, prior) => prior + value);
        });
        listener.Start();
        var admission = new List<double>();
        var admitted = new List<(Guid Id, string Mode, HttpClient Owner, long Started)>();
        for (var i = 0; i < modes.Length; i++)
        {
            var client = i % 2 == 0 ? first : second;
            var path = i % 2 == 0 ? practicePath : contestPath;
            var started = Stopwatch.GetTimestamp();
            using var response = await client.PostAsJsonAsync(path, new CreateSubmissionRequest("cpp20", Source(modes[i]), Guid.NewGuid()), token);
            admission.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(token);
            admitted.Add((json.GetProperty("id").GetGuid(), modes[i], client, Stopwatch.GetTimestamp()));
        }
        var rejected = 0;
        if (count == 3)
        {
            using var rejection = await first.PostAsJsonAsync(contestPath, new CreateSubmissionRequest("cpp20", Source("accepted"), Guid.NewGuid()), token);
            Assert.Equal(HttpStatusCode.TooManyRequests, rejection.StatusCode);
            Assert.Equal("5", Assert.Single(rejection.Headers.GetValues("Retry-After")));
            rejected++;
        }
        var logs = new CapturedWorkerLog();
        using var worker = new SubmissionJobWorker(workers.GetRequiredService<IServiceScopeFactory>(), new(concurrency), logs);
        var completion = new Dictionary<Guid, double>(); var queue = new List<double>();
        var api = new List<double>(); var results = new List<object>();
        var workload = Stopwatch.StartNew();
        await worker.StartAsync(token);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromMinutes(8));
            while (completion.Count != admitted.Count)
            {
                var probe = Stopwatch.GetTimestamp();
                using var catalog = await first.GetAsync("/api/problems?page=1&pageSize=20", deadline.Token);
                api.Add(Stopwatch.GetElapsedTime(probe).TotalMilliseconds);
                Assert.Equal(HttpStatusCode.OK, catalog.StatusCode);
                foreach (var item in admitted)
                {
                    if (completion.ContainsKey(item.Id)) continue;
                    probe = Stopwatch.GetTimestamp();
                    var result = (await item.Owner.GetFromJsonAsync<SubmissionResponse>($"/api/submissions/{item.Id}", deadline.Token))!;
                    api.Add(Stopwatch.GetElapsedTime(probe).TotalMilliseconds);
                    Assert.NotEqual("SystemFailed", result.Status);
                    if (result.Status != "Completed") continue;
                    completion[item.Id] = Stopwatch.GetElapsedTime(item.Started).TotalSeconds;
                    queue.Add((result.Attempts[0].StartedAtUtc - result.CreatedAtUtc).TotalSeconds);
                    var passed = item.Mode == "wrong" ? count - (count + 1) / 3 : item.Mode is "accepted" or "cpu" or "compile-heavy" ? count : 0;
                    var executed = item.Mode == "compile" ? 0 : count;
                    if (result.Result!.Verdict != Verdict(item.Mode))
                        TestContext.Current.TestOutputHelper!.WriteLine("READINESS_FAILURE_JSON=" + JsonSerializer.Serialize(new
                        { scenario = item.Mode, result.Id, result.Attempts, result.Result }));
                    Assert.Equal(new SubmissionTestSummaryResponse(count, executed, passed, executed - passed), result.Result!.TestSummary);
                    Assert.Equal(Verdict(item.Mode), result.Result.Verdict);
                    Assert.Single(result.Attempts);
                    Assert.Equal(Enumerable.Range(1, count), result.Result.Tests.Select(t => t.Order));
                    if (executed != 0) Assert.All(result.Result.Tests, t => Assert.NotNull(t.MemoryPeakBytes));
                    results.Add(new { scenario = item.Mode, submissionId = item.Id, attemptId = result.Result.AttemptId, verdict = result.Result.Verdict,
                        result.Result.TestSummary, queueSeconds = queue[^1], completionSeconds = completion[item.Id] });
                }
                if (completion.Count != admitted.Count) await Task.Delay(100, deadline.Token);
            }
        }
        finally { await worker.StopAsync(CancellationToken.None); }
        workload.Stop();
        Assert.Empty(unsafeTags); Assert.All(balances.Values, balance => Assert.Equal(0, balance));
        Assert.Equal(admitted.Count, counters.GetValueOrDefault("minos.worker.claims"));
        foreach (var name in new[] { "minos.worker.lost_leases", "minos.worker.iteration_failures", "minos.sandbox.cleanup_failures" })
            Assert.Equal(0, counters.GetValueOrDefault(name));
        Assert.InRange(peaks["minos.judging.active_sandboxes"], 1, slots);
        Assert.InRange(peaks["minos.judging.reserved_memory"], 1, budget.MemoryMegabytes * 1024L * 1024);
        Assert.All(admitted, item => Assert.Contains(logs.Messages, line => line.Contains(item.Id.ToString()) && line.Contains("Processed submission job")));
        Assert.DoesNotContain(logs.Messages, line => modes.Any(mode => line.Contains(Source(mode), StringComparison.Ordinal)));
        foreach (var phase in new[] { "persistence/practice-admission", "persistence/claim", "persistence/complete", "submission/artifact-preflight", "submission/artifact-read", "run/cleanup" })
            Assert.True(phases.ContainsKey(phase), $"Missing measurement {phase}");
        if (count == 3) Assert.True(phases.ContainsKey("persistence/contest-admission"));
        await using (var evidence = factory.Services.CreateAsyncScope())
        {
            var db = evidence.ServiceProvider.GetRequiredService<MinosDbContext>();
            var ids = admitted.Select(a => a.Id).ToArray();
            var expected = await db.Submissions.Where(s => ids.Contains(s.Id)).OrderBy(s => s.CreatedAtUtc).ThenBy(s => s.Id).Select(s => s.Id).ToArrayAsync(token);
            var actual = await db.JudgingAttempts.Where(a => ids.Contains(a.SubmissionId)).OrderBy(a => a.StartedAtUtc).Select(a => a.SubmissionId).ToArrayAsync(token);
            Assert.Equal(expected, actual);
            Assert.Equal(count == 3 ? 4 : 0, await db.ContestSubmissions.CountAsync(s => ids.Contains(s.SubmissionId), token));
        }
        TestContext.Current.TestOutputHelper!.WriteLine("READINESS_JSON=" + JsonSerializer.Serialize(new
        {
            schemaVersion = 1, measuredAtUtc = DateTimeOffset.UtcNow, workload = count == 100 ? "many-short-tests" : "mixed",
            activeJobs = concurrency, testsPerJob = count, jobs = admitted.Count, logicalCpus = Environment.ProcessorCount,
            initializationSeconds = initialization.Elapsed.TotalSeconds, elapsedSeconds = workload.Elapsed.TotalSeconds,
            throughputJobsPerSecond = admitted.Count / workload.Elapsed.TotalSeconds,
            queueSeconds = Distribution(queue), completionSeconds = Distribution(completion.Values),
            admissionMilliseconds = Distribution(admission), apiMilliseconds = Distribution(api),
            phases = phases.OrderBy(p => p.Key).ToDictionary(p => p.Key, p => Distribution(p.Value)), peaks, counters,
            systemFailed = 0, retries = 0, accountRejections = rejected, fifoClaims = true, results,
            scope = "real PostgreSQL + in-process HTTP + actual single-host Worker/Docker; fixed labels; no external network/TLS; cached images"
        }));
    }

    private static object Distribution(IEnumerable<double> values)
    {
        var samples = values.Order().ToArray();
        return new { n = samples.Length, p50 = samples[(int)Math.Ceiling(samples.Length * .5) - 1],
            p95 = samples[(int)Math.Ceiling(samples.Length * .95) - 1], max = samples[^1], total = samples.Sum() };
    }
    private static string Verdict(string mode) => mode switch
    {
        "wrong" => "WrongAnswer", "runtime" => "RuntimeError", "compile" => "CompilationError",
        "memory" => "MemoryLimitExceeded", "output" => "OutputLimitExceeded", _ => "Accepted"
    };
    private static string Source(string mode) => mode switch
    {
        "runtime" => "int main(){return 7;}", "compile" => "int main(){compile_fail;}",
        "memory" => "#include <cstdlib>\nint main(){volatile char* p=(char*)malloc(256*1024*1024);for(int i=0;i<256*1024*1024;i+=4096)p[i]=1;while(true){}}",
        "output" => "#include <cstdio>\nint main(){while(true)puts(\"xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\");}",
        "compile-heavy" => "#include <bits/stdc++.h>\nint main(){int a,b;std::cin>>a>>b;std::cout<<a+b<<'\\n';}",
        "cpu" => "#include <iostream>\nint main(){volatile unsigned x=1;for(unsigned i=0;i<100000000;i++)x=x*1664525+1013904223;int a,b;std::cin>>a>>b;std::cout<<a+b<<'\\n';}",
        "wrong" => "#include <iostream>\nint main(){int a,b;std::cin>>a>>b;std::cout<<(a==19?5:a+b)<<'\\n';}",
        _ => "#include <iostream>\nint main(){int a,b;std::cin>>a>>b;std::cout<<a+b<<'\\n';}"
    };
    private sealed class CapturedWorkerLog : ILogger<SubmissionJobWorker>
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter) => Messages.Enqueue(formatter(state, error));
    }
}
