using Minos.Domain.Problems;

namespace Minos.Application.Problems;

public static class ProblemArtifactLimits
{
    public const long MaximumPackageBytes = 16 * 1024 * 1024;
}

public sealed record StoredProblemTestArtifacts(ProblemArtifactReference Input, ProblemArtifactReference Output);

public interface IProblemArtifactStore
{
    Task<StoredProblemTestArtifacts> StorePackageAsync(Stream package, CancellationToken token);
    Task VerifyAsync(IEnumerable<ProblemArtifactReference> artifacts, CancellationToken token);
    Task DeleteAsync(IEnumerable<string> keys, CancellationToken token);
    Task CleanupAbandonedAsync(IReadOnlySet<string> referencedKeys, DateTimeOffset olderThanUtc, CancellationToken token);
}
