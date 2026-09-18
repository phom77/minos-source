using Minos.Application.Accounts;
using Minos.Domain.Problems;

namespace Minos.Application.Problems;

public sealed class ProblemDraftAccess(AccountUseCases accounts) : IProblemDraftAccess
{
    public async Task<Guid> RequireActiveAccountAsync(CancellationToken token) => (await accounts.RequireActiveAsync(token)).Id;
    public void EnsureCanRead(ProblemDraft draft, Guid accountId)
    {
        if (!draft.IsOwnedBy(accountId)) throw new AccessDeniedException();
    }
}
