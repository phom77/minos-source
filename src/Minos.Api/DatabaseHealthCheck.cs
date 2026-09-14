using Microsoft.Extensions.Diagnostics.HealthChecks;
using Minos.Infrastructure.Hosting;
using System.Data.Common;

namespace Minos.Api;

internal sealed class DatabaseHealthCheck(IServiceScopeFactory scopes, PrivateArtifactDirectory artifacts) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await artifacts.CheckWritableAsync(cancellationToken);
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<DatabaseOperationalCheck>().ValidateAsync(DatabaseActor.Api, cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception error) when (error is DbException or InvalidOperationException or IOException or UnauthorizedAccessException)
        { return HealthCheckResult.Unhealthy("Minos database or private storage is unavailable or misconfigured."); }
    }
}
