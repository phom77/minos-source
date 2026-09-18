using Minos.Infrastructure.Hosting;

namespace Minos.Api;

// Composition/startup concern: runtime hosts never apply migrations.
internal sealed class FoundationStartupCheck(IServiceScopeFactory scopes, PrivateArtifactDirectory artifacts, IConfiguration configuration, IHostEnvironment environment) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await artifacts.CheckWritableAsync(cancellationToken);
        var keyRoot = Path.Combine(configuration["Minos:Artifacts:Root"]!, "data-protection");
        await new PrivateArtifactDirectory(keyRoot, environment.ContentRootPath).CheckWritableAsync(cancellationToken);
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseOperationalCheck>().ValidateAsync(DatabaseActor.Api, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
