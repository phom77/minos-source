using Minos.Application.Accounts;
using Minos.Domain.Accounts;
using Minos.Domain.Problems;

namespace Minos.Application.Problems;

public sealed class ProblemAccess(AccountUseCases accounts)
{
    public async Task<Account> RequireAuthorAsync(CancellationToken token)
    {
        var account = await accounts.RequireActiveAsync(token);
        if (!account.CanAuthorProblems) throw new AccessDeniedException();
        return account;
    }

    public static void EnsureOwner(Problem problem, Account account)
    {
        if (!problem.IsOwnedBy(account.Id)) throw new AccessDeniedException();
    }
}
