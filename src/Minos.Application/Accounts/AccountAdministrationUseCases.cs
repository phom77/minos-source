using Minos.Domain.Accounts;

namespace Minos.Application.Accounts;

public sealed class AccountAdministrationUseCases(IAccountStore store, AccountUseCases accounts)
{
    public async Task AssignRoleAsync(Guid accountId, AccountRole role, CancellationToken token)
    {
        var administrator = await accounts.RequireActiveAsync(token);
        if (!administrator.CanManageAccountRoles) throw new AccessDeniedException();
        await AssignAsync(accountId, role, token);
    }

    private async Task AssignAsync(Guid accountId, AccountRole role, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (accountId == Guid.Empty) throw new ResourceNotFoundException();
        var account = await store.FindAsync(accountId, token) ?? throw new ResourceNotFoundException();
        account.AssignRole(role);
        await store.SaveRoleAsync(account.Id, role, token);
    }
}

// Deliberately has no HTTP endpoint. The API host exposes it only through its maintenance command.
public sealed class TrustedAccountProvisioningUseCases(IAccountStore store)
{
    public async Task ProvisionAdministratorAsync(Guid accountId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (accountId == Guid.Empty) throw new ResourceNotFoundException();
        var account = await store.FindAsync(accountId, token) ?? throw new ResourceNotFoundException();
        account.AssignRole(AccountRole.Administrator);
        await store.SaveRoleAsync(account.Id, AccountRole.Administrator, token);
    }
}
