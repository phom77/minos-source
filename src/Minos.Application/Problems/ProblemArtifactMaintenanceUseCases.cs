namespace Minos.Application.Problems;

public sealed class ProblemArtifactMaintenanceUseCases(
    IProblemRepository repository,
    IProblemArtifactStore artifacts,
    TimeProvider clock)
{
    public async Task CleanupAbandonedAsync(CancellationToken token)
    {
        var referenced = await repository.ReferencedArtifactKeysAsync(token);
        await artifacts.CleanupAbandonedAsync(referenced, clock.GetUtcNow().AddHours(-24), token);
    }
}
