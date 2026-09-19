namespace Minos.Application.Problems;

public sealed class ProblemCatalogUseCases(IProblemRepository repository)
{
    public async Task<PublishedProblemPage> ListAsync(int page, int pageSize, CancellationToken token)
    {
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page));
        if (pageSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(pageSize));
        var (items, total) = await repository.ListPublishedAsync(checked((page - 1) * pageSize), pageSize, token);
        return new(items, page, pageSize, total);
    }

    public async Task<PublishedProblemDetails> FindLatestAsync(Guid problemId, CancellationToken token)
    {
        var version = await repository.FindLatestPublishedAsync(problemId, token) ?? throw new ResourceNotFoundException();
        return ProblemAuthoringUseCases.MapPublished(version);
    }

    public async Task<PublishedProblemDetails> FindVersionAsync(Guid versionId, CancellationToken token)
    {
        var version = await repository.FindPublishedVersionAsync(versionId, token) ?? throw new ResourceNotFoundException();
        return ProblemAuthoringUseCases.MapPublished(version);
    }
}
