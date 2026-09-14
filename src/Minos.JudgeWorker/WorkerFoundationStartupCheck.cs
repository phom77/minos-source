using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Minos.Infrastructure.Hosting;

namespace Minos.JudgeWorker;

internal sealed class WorkerFoundationStartupCheck(IServiceScopeFactory scopes, PrivateArtifactDirectory artifacts) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await artifacts.CheckWritableAsync(cancellationToken);
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseOperationalCheck>().ValidateAsync(DatabaseActor.Worker, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
