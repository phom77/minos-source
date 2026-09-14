using Minos.Domain.Problems;

namespace Minos.Application.Problems;

public sealed record CreateProblemDraft(string? Title, string? Statement);
public sealed record ProblemDraftDetails(Guid Id, string Title, string Statement, DateTimeOffset CreatedAtUtc);

public sealed class ProblemDraftUseCases(IProblemDraftRepository repository, IProblemDraftAccess access, TimeProvider clock)
{
    public async Task<ProblemDraftDetails> CreateAsync(CreateProblemDraft request, CancellationToken cancellationToken)
    {
        await access.EnsureCanCreateAsync(cancellationToken);
        var draft = ProblemDraft.Create(request.Title, request.Statement, clock.GetUtcNow());
        await repository.AddAsync(draft, cancellationToken);
        return Map(draft);
    }

    public async Task<ProblemDraftDetails?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        await access.EnsureCanReadAsync(id, cancellationToken);
        var draft = await repository.FindAsync(id, cancellationToken);
        return draft is null ? null : Map(draft);
    }

    private static ProblemDraftDetails Map(ProblemDraft draft) => new(draft.Id, draft.Title, draft.Statement, draft.CreatedAtUtc);
}
