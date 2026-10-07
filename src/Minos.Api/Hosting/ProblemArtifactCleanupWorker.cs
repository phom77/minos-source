using Minos.Application.Problems;

namespace Minos.Api;

internal sealed class ProblemArtifactCleanupWorker(IServiceScopeFactory scopes, ILogger<ProblemArtifactCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ProblemArtifactMaintenanceUseCases>().CleanupAbandonedAsync(stoppingToken);
                logger.LogInformation("Abandoned artifact cleanup completed");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError("Abandoned artifact cleanup failed with {ErrorType}", error.GetType().Name); }
        }
    }
}
