using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Minos.Infrastructure.Hosting;

namespace Minos.JudgeWorker;

internal sealed class WorkerFoundationStartupCheck(IServiceScopeFactory scopes, PrivateArtifactDirectory artifacts, JudgeSandboxLifecycle sandbox) : IHostedService
{
    private const string ReadinessFile = "/tmp/minos-judge-execution/.worker-ready";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await artifacts.CheckWritableAsync(cancellationToken);
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseOperationalCheck>().ValidateAsync(DatabaseActor.Worker, cancellationToken);
        await sandbox.InitializeAsync(cancellationToken);
        // Health probes inspect this process; they must never acquire the runner's lifetime lock.
        if (new FileInfo(ReadinessFile).LinkTarget is not null)
            throw new IOException("Worker readiness marker is unsafe.");
        await File.WriteAllTextAsync(ReadinessFile,
            Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        File.Delete(ReadinessFile);
        return Task.CompletedTask;
    }
}
