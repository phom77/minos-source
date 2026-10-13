using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Minos.Api.Accounts;
using Minos.Api.Submissions;
using Minos.Application.Evaluation;
using Minos.Domain.Evaluation;
using Xunit;

namespace Minos.IntegrationTests;

public sealed class JudgingObservabilityIntegrationTests
{
    [Fact]
    public async Task Admission_failure_retry_and_commit_are_correlated_without_sensitive_labels_or_logs()
    {
        var token = TestContext.Current.CancellationToken;
        using var logs = new CapturedLogs();
        await using var workers = OfficialJudgingIntegrationTests.Workers(new BrokenBackend());
        workers.GetRequiredService<ILoggerFactory>().AddProvider(logs);
        await OfficialJudgingIntegrationTests.DrainAsync(workers, token);
        await using var factory = AccountTestHost.Factory().WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(logs)));
        using var owner = AccountTestHost.Client(factory);
        var account = (await (await AccountTestHost.RegisterAsync(owner, AccountTestHost.Email())).Content.ReadFromJsonAsync<AccountResponse>(token))!;
        var seeded = await OfficialJudgingIntegrationTests.SeedAsync(factory.Services, account.Id, token);
        var measurements = new ConcurrentQueue<(string Stage, string Phase)>();
        var unsafeTags = new ConcurrentQueue<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Minos.Judging" && instrument.Name == "minos.judging.phase_duration") l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
        {
            Assert.True(double.IsFinite(value) && value >= 0);
            var stage = ""; var phase = "";
            foreach (var tag in tags)
            {
                if (tag.Key == "stage") stage = tag.Value!.ToString()!;
                else if (tag.Key == "phase") phase = tag.Value!.ToString()!;
                else unsafeTags.Enqueue(tag.Key);
            }
            measurements.Enqueue((stage, phase));
        });
        listener.Start();
        var requestKey = Guid.NewGuid();
        const string source = "int main(){/* private observability sentinel */return 0;}";
        await AccountTestHost.CsrfAsync(owner);
        var admission = await owner.PostAsJsonAsync($"/api/problems/{seeded.Problem.Id}/submissions", new CreateSubmissionRequest("cpp20", source, requestKey), token);
        var submission = (await admission.Content.ReadFromJsonAsync<SubmissionResponse>(token))!;
        ClaimedSubmissionWork work;
        await using (var first = workers.CreateAsyncScope())
        {
            var cases = first.ServiceProvider.GetRequiredService<JudgeJobUseCases>();
            work = (await cases.ClaimAsync("observability", token))!;
            Assert.Equal(LeaseMutationResult.Accepted, await cases.RenewAsync(work, token));
            Assert.Equal(LeaseMutationResult.Accepted, await cases.ProcessAsync(work, token));
        }
        await using (var second = workers.CreateAsyncScope())
        {
            var repository = second.ServiceProvider.GetRequiredService<IJudgeJobRepository>();
            var now = DateTimeOffset.UtcNow.AddSeconds(2);
            var retry = (await repository.ClaimAsync(work.JobId, "observability", now, token))!;
            Assert.NotEqual(work.AttemptId, retry.AttemptId);
            var outcome = JudgingOutcome.Create(SubmissionVerdict.Accepted, "", Enumerable.Range(1, 3).Select(i =>
                new JudgingTestOutcome(i, TestCaseResultStatus.Passed, 1, 1, 1024, 0)).ToArray());
            Assert.Equal(LeaseMutationResult.Accepted, await repository.CompleteAsync(retry.AttemptId, retry.LeaseToken, retry.WorkerId, outcome, now, token));
        }
        var result = (await owner.GetFromJsonAsync<SubmissionResponse>($"/api/submissions/{submission.Id}", token))!;
        Assert.Equal("Accepted", result.Result!.Verdict); Assert.Equal(2, result.Attempts.Count);
        Assert.Contains(logs.Messages, line => line.Contains("Submission request resolved") && line.Contains(submission.Id.ToString()) && line.Contains("TraceId"));
        Assert.Contains(logs.Messages, line => line.Contains("Official judging failed") && line.Contains(work.JobId.ToString()) && line.Contains(work.SubmissionId.ToString()) && line.Contains(work.AttemptId.ToString()));
        foreach (var secret in new[] { source, requestKey.ToString(), work.LeaseToken.ToString(), BrokenBackend.Secret })
            Assert.DoesNotContain(logs.Messages, line => line.Contains(secret, StringComparison.Ordinal));
        Assert.Empty(unsafeTags);
        foreach (var phase in new[] { "practice-admission", "claim", "renew", "fail", "complete" })
            Assert.Contains(("persistence", phase), measurements);
    }

    private sealed class BrokenBackend : IExecutionBackend
    {
        internal const string Secret = "hidden-runner-exception-details";
        public Task<ExecutionEvidence> ExecuteAsync(ExecutionRequest request, CancellationToken token) => throw new IOException(Secret);
    }
    private sealed class CapturedLogs : ILoggerProvider
    {
        internal ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string category) => new Capture(Messages);
        public void Dispose() { }
        private sealed class Capture(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> format) => messages.Enqueue(format(state, error));
        }
    }
}
