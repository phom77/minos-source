using System.Net.Mail;
using Minos.Domain.Accounts;

namespace Minos.Application.Accounts;

public sealed record AccountDetails(Guid Id, string DisplayName, DateTimeOffset CreatedAtUtc);

public sealed class AccountUseCases(IAccountStore store, ICurrentAccount current, TimeProvider clock)
{
    public async Task<AccountDetails> RegisterAsync(string? email, string? password, string? displayName, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!ValidEmail(email) || !ValidPassword(password)) throw new AccountRegistrationException();
        var account = Account.Create(displayName, clock.GetUtcNow());
        await store.RegisterLocalAsync(account, email!.Trim(), password!, token);
        return Map(account);
    }

    public async Task<AccountDetails> LoginAsync(string? email, string? password, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!ValidEmail(email) || !ValidPassword(password)) throw new AccountAuthenticationException();
        var id = await store.AuthenticateLocalAsync(email!.Trim(), password!, token);
        var account = id is null ? null : await store.FindAsync(id.Value, token);
        if (account is null || account.IsDisabled) throw new AccountAuthenticationException();
        return Map(account);
    }

    public async Task<Account> RequireActiveAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (current.AccountId is not Guid id || id == Guid.Empty) throw new AccountAuthenticationException();
        var account = await store.FindAsync(id, token);
        if (account is null || account.IsDisabled) throw new AccountAuthenticationException();
        return account;
    }

    public async Task<AccountDetails> CurrentAsync(CancellationToken token) => Map(await RequireActiveAsync(token));

    public async Task<AccountDetails> RenameAsync(string? displayName, CancellationToken token)
    {
        var account = await RequireActiveAsync(token);
        account.Rename(displayName);
        await store.SaveAsync(account, false, token);
        return Map(account);
    }

    public async Task LogoutAsync(CancellationToken token)
    {
        var account = await RequireActiveAsync(token);
        await store.RevokeSessionsAsync(account.Id, token);
    }

    public async Task ChangePasswordAsync(string? password, string? replacement, CancellationToken token)
    {
        var account = await RequireActiveAsync(token);
        if (!ValidPassword(password) || !ValidPassword(replacement)) throw new AccountAuthenticationException();
        await store.ChangePasswordAsync(account.Id, password!, replacement!, token);
    }

    public async Task DisableAsync(string? password, CancellationToken token)
    {
        var account = await RequireActiveAsync(token);
        if (!ValidPassword(password) || !await store.ConfirmPasswordAsync(account.Id, password!, token))
            throw new AccountAuthenticationException();
        account.Disable();
        await store.SaveAsync(account, true, token);
    }

    internal static bool ValidEmail(string? email) => email is { Length: > 0 and <= 256 }
        && MailAddress.TryCreate(email.Trim(), out var parsed) && parsed.Address == email.Trim();
    internal static bool ValidPassword(string? password) => password is { Length: >= 12 and <= 256 };
    internal static AccountDetails Map(Account account) => new(account.Id, account.DisplayName, account.CreatedAtUtc);
}
