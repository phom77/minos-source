using System.Net;
using System.Text;
using Minos.Infrastructure.Evaluation;
using Xunit;

namespace Minos.IntegrationTests;

public sealed class DockerExecStateTests
{
    [Fact]
    public async Task Attachment_headers_before_start_do_not_fabricate_a_completed_exit()
    {
        using var handler = new ExecHandler(["{\"Running\":false,\"ExitCode\":null,\"Pid\":0}",
            "{\"Running\":true,\"ExitCode\":null,\"Pid\":42}", "{\"Running\":false,\"ExitCode\":7,\"Pid\":0}"]);
        using var client = Client(handler);
        using var session = await DockerExecutionSession.StartAsync(client, "fixture", "1000:1000", ["/input/main"], TestContext.Current.CancellationToken);
        var running = await session.InspectAsync(TestContext.Current.CancellationToken);
        Assert.True(running.Running); Assert.Equal(42, running.ProcessId); Assert.Equal(2, handler.Inspections);
        var completed = await session.InspectAsync(TestContext.Current.CancellationToken);
        Assert.False(completed.Running); Assert.Equal(7, completed.ExitCode);
    }

    [Fact]
    public async Task Missing_exit_evidence_is_bounded_and_remains_an_infrastructure_fault()
    {
        using var handler = new ExecHandler(["{\"Running\":false,\"ExitCode\":null,\"Pid\":0}"]);
        using var client = Client(handler);
        using var session = await DockerExecutionSession.StartAsync(client, "fixture", "1000:1000", ["/input/main"], TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<IOException>(() => session.InspectAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.True(handler.Inspections > 1);
    }

    [Fact]
    public async Task Cancellation_interrupts_transitional_state_waiting()
    {
        using var handler = new ExecHandler(["{\"Running\":false,\"ExitCode\":null,\"Pid\":0}"]);
        using var client = Client(handler);
        using var session = await DockerExecutionSession.StartAsync(client, "fixture", "1000:1000", ["/input/main"], TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.InspectAsync(cancellation.Token));
    }

    private static HttpClient Client(ExecHandler handler) => new(handler) { BaseAddress = new("http://docker/v1.51/") };
    private sealed class ExecHandler(string[] states) : HttpMessageHandler
    {
        public int Inspections { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var json = request.RequestUri!.AbsolutePath.EndsWith("/json", StringComparison.Ordinal)
                ? states[Math.Min(Inspections++, states.Length - 1)] : "{\"Id\":\"fixture-exec\"}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
