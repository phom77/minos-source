using Minos.Domain.Accounts;

namespace Minos.Application.Accounts;

public sealed class ExternalLoginUseCases(IExternalLoginStore logins, IAccountStore accounts, AccountUseCases access, TimeProvider clock)
{
    public async Task<AccountDetails> SignInAsync(ExternalIdentity identity, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Validate(identity);
        var id = await logins.FindAsync(identity.Provider, identity.Subject, token);
        Account? account;
        if (id is Guid existing)
        {
            // The stable provider subject owns the mapping; changed email never moves ownership.
            account = await accounts.FindAsync(existing, token);
        }
        else
        {
            if (!identity.EmailVerified || !AccountUseCases.ValidEmail(identity.Email)) throw new ExternalLoginException();
            account = Account.Create(string.IsNullOrWhiteSpace(identity.DisplayName) ? "Minos user" : identity.DisplayName, clock.GetUtcNow());
            await logins.RegisterAsync(account, identity, token);
        }
        if (account is null || account.IsDisabled) throw new AccountAuthenticationException();
        return AccountUseCases.Map(account);
    }

    public async Task<Guid> BeginLinkAsync(string? password, CancellationToken token)
    {
        var account = await access.RequireActiveAsync(token);
        if (!AccountUseCases.ValidPassword(password) || !await accounts.ConfirmPasswordAsync(account.Id, password!, token))
            throw new AccountAuthenticationException();
        return account.Id;
    }

    public async Task<AccountDetails> CompleteLinkAsync(Guid expectedAccountId, ExternalIdentity identity, CancellationToken token)
    {
        var account = await access.RequireActiveAsync(token);
        if (account.Id != expectedAccountId) throw new AccessDeniedException();
        Validate(identity);
        await logins.LinkAsync(account.Id, identity, token);
        return AccountUseCases.Map(account);
    }

    private static void Validate(ExternalIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(identity.Provider) || identity.Provider.Length > 32 || identity.Provider.Trim() != identity.Provider || identity.Provider.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(identity.Subject) || identity.Subject.Length > 256 || identity.Subject.Any(char.IsControl))
            throw new ExternalLoginException();
    }
}
