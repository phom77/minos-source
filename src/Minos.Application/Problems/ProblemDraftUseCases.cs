using Minos.Domain.Problems;

namespace Minos.Application.Problems;

public sealed record CreateProblemDraft(string? Title, string? Statement);
public sealed record ProblemDraftDetails(Guid Id, string Title, string Statement, DateTimeOffset CreatedAtUtc);

public sealed class ProblemDraftUseCases(IProblemDraftRepository repository, IProblemDraftAccess access, TimeProvider clock)
{
    public async Task<ProblemDraftDetails> CreateAsync(CreateProblemDraft request, CancellationToken cancellationToken)
    {
        var ownerId = await access.RequireActiveAccountAsync(cancellationToken);
        var draft = ProblemDraft.Create(ownerId, request.Title, request.Statement, clock.GetUtcNow());
        await repository.AddAsync(draft, cancellationToken);
        return Map(draft);
    }

    public async Task<ProblemDraftDetails?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var accountId = await access.RequireActiveAccountAsync(cancellationToken);
        var draft = await repository.FindAsync(id, cancellationToken);
        if (draft is not null) access.EnsureCanRead(draft, accountId);
        return draft is null ? null : Map(draft);
    }

    private static ProblemDraftDetails Map(ProblemDraft draft) => new(draft.Id, draft.Title, draft.Statement, draft.CreatedAtUtc);
}
