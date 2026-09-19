using Minos.Domain.Problems;

namespace Minos.Application.Problems;

public sealed class ProblemAuthoringUseCases(
    IProblemRepository repository,
    IProblemArtifactStore artifacts,
    ProblemAccess access,
    TimeProvider clock)
{
    public async Task<ProblemDraftDetails> CreateAsync(CreateProblem request, CancellationToken token)
    {
        var author = await access.RequireAuthorAsync(token);
        var problem = Problem.Create(author.Id, request.Title, request.Statement, clock.GetUtcNow());
        await repository.AddAsync(problem, token);
        return MapDraft(problem);
    }

    public async Task<ProblemDraftDetails> FindDraftAsync(Guid id, CancellationToken token)
    {
        var (problem, _) = await OwnedAsync(id, token);
        return MapDraft(problem);
    }

    public async Task<ProblemDraftDetails> UpdateDraftAsync(Guid id, UpdateProblemDraft request, CancellationToken token)
    {
        var (problem, _) = await OwnedAsync(id, token);
        var now = clock.GetUtcNow();
        problem.EditContent(request.Title, request.Statement, now);
        problem.ConfigureExecution(request.TimeLimitMilliseconds, request.MemoryLimitMegabytes, request.Checker, now);
        problem.ReplaceSamples(request.Samples, now);
        await repository.SaveAsync(problem, token);
        return MapDraft(problem);
    }

    public async Task<ProblemDraftDetails> AddTestCaseAsync(Guid id, Stream package, CancellationToken token)
    {
        var (problem, _) = await OwnedAsync(id, token);
        var stored = await artifacts.StorePackageAsync(package, token);
        try
        {
            problem.AddTestCase(stored.Input, stored.Output, clock.GetUtcNow());
            await repository.SaveAsync(problem, token);
            return MapDraft(problem);
        }
        catch
        {
            await DeleteBestEffortAsync([stored.Input.Key, stored.Output.Key]);
            throw;
        }
    }

    public async Task<ProblemDraftDetails> RemoveTestCaseAsync(Guid id, Guid testCaseId, CancellationToken token)
    {
        var (problem, _) = await OwnedAsync(id, token);
        if (!problem.RemoveTestCase(testCaseId, clock.GetUtcNow())) throw new ResourceNotFoundException();
        await repository.SaveAsync(problem, token);
        return MapDraft(problem);
    }

    public async Task<PublishedProblemDetails> PublishAsync(Guid id, PublishProblem request, CancellationToken token)
    {
        var (problem, author) = await OwnedAsync(id, token);
        await artifacts.VerifyAsync(problem.TestCases.SelectMany(test => new[] { test.InputArtifact, test.OutputArtifact }), token);
        var checklist = new PublicationChecklist(request.StatementReviewed, request.SamplesVerified, request.ReferenceSolutionChecked);
        var version = problem.Publish(author.Id, checklist, clock.GetUtcNow());
        await repository.PublishAsync(problem, version, token);
        return MapPublished(version);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken token)
    {
        var (problem, _) = await OwnedAsync(id, token);
        problem.Archive(clock.GetUtcNow());
        await repository.SaveAsync(problem, token);
    }

    private async Task<(Problem Problem, Minos.Domain.Accounts.Account Author)> OwnedAsync(Guid id, CancellationToken token)
    {
        var author = await access.RequireAuthorAsync(token);
        var problem = await repository.FindForAuthoringAsync(id, token) ?? throw new ResourceNotFoundException();
        ProblemAccess.EnsureOwner(problem, author);
        return (problem, author);
    }

    private async Task DeleteBestEffortAsync(IEnumerable<string> keys)
    {
        try { await artifacts.DeleteAsync(keys, CancellationToken.None); }
        catch { }
    }

    internal static ProblemDraftDetails MapDraft(Problem problem) => new(
        problem.Id,
        problem.Title,
        problem.Statement,
        problem.TimeLimitMilliseconds,
        problem.MemoryLimitMegabytes,
        problem.Checker,
        problem.LatestPublishedVersion,
        problem.IsArchived,
        problem.Revision,
        problem.CreatedAtUtc,
        problem.UpdatedAtUtc,
        problem.Samples.OrderBy(sample => sample.Order).Select(sample => new ProblemSampleContent(sample.Input, sample.Output)).ToArray(),
        problem.TestCases.OrderBy(test => test.Order).Select(test =>
            new ProblemTestCaseDetails(test.Id, test.Order, test.InputArtifact.SizeBytes, test.OutputArtifact.SizeBytes)).ToArray());

    internal static PublishedProblemDetails MapPublished(PublishedProblemVersion version) => new(
        version.ProblemId,
        version.Id,
        version.VersionNumber,
        version.Title,
        version.Statement,
        version.TimeLimitMilliseconds,
        version.MemoryLimitMegabytes,
        version.Checker,
        version.PrivateTestCount,
        version.PublishedAtUtc,
        version.Samples.OrderBy(sample => sample.Order).Select(sample => new ProblemSampleContent(sample.Input, sample.Output)).ToArray());
}
