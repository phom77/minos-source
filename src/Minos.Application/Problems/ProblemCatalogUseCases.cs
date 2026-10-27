using Minos.Application.Accounts;

namespace Minos.Application.Problems;

public sealed class ProblemCatalogUseCases(IProblemRepository repository, ICurrentAccount current, AccountUseCases accounts)
{
    public Task<IReadOnlyList<ProblemTopicCountSummary>> ListTopicCountsAsync(CancellationToken token) => repository.ListTopicCountsAsync(token);
    public Task<PublishedProblemPage> ListAsync(int page, int pageSize, CancellationToken token) => ListAsync(new(page, pageSize), token);

    public async Task<PublishedProblemPage> ListAsync(ProblemCatalogQuery request, CancellationToken token)
    {
        request = request.Validate();
        Guid? owner = null;
        if (current.AccountId is not null || request.Status is not null) owner = (await accounts.RequireActiveAsync(token)).Id;
        var result = await repository.ListPublishedAsync(request, owner, token);
        return new(result.Items, request.Page, request.PageSize, result.Total) { Progress = result.Progress };
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
