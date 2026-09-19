using Minos.Domain.Problems;

namespace Minos.Application.Problems;

public interface IProblemRepository
{
    Task AddAsync(Problem problem, CancellationToken token);
    Task<Problem?> FindForAuthoringAsync(Guid id, CancellationToken token);
    Task SaveAsync(Problem problem, CancellationToken token);
    Task PublishAsync(Problem problem, PublishedProblemVersion version, CancellationToken token);
    Task<(IReadOnlyList<PublishedProblemSummary> Items, int Total)> ListPublishedAsync(int offset, int limit, CancellationToken token);
    Task<PublishedProblemVersion?> FindLatestPublishedAsync(Guid problemId, CancellationToken token);
    Task<PublishedProblemVersion?> FindPublishedVersionAsync(Guid versionId, CancellationToken token);
    Task<IReadOnlySet<string>> ReferencedArtifactKeysAsync(CancellationToken token);
}
