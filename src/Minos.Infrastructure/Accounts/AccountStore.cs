using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Minos.Application;
using Minos.Application.Accounts;
using Minos.Domain.Accounts;
using Minos.Infrastructure.Persistence;
using Npgsql;

namespace Minos.Infrastructure.Accounts;

public sealed class AccountStore(MinosDbContext database, AccountUserManager users) : IAccountStore
{
    private static readonly CredentialUser UnknownUser = new();
    private static readonly string UnknownPasswordHash = new PasswordHasher<CredentialUser>()
        .HashPassword(UnknownUser, "This is a dummy hash, never a stored account credential.");
    public Task<Account?> FindAsync(Guid id, CancellationToken token) => database.Accounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token);

    public async Task RegisterLocalAsync(Account account, string email, string password, CancellationToken token)
    {
        users.OperationCancellationToken = token;
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        try
        {
            database.Accounts.Add(account);
            await database.SaveChangesAsync(token);
            var result = await users.CreateAsync(new CredentialUser { Id = account.Id, UserName = email, Email = email }, password);
            if (!result.Succeeded) throw new AccountRegistrationException();
            await transaction.CommitAsync(token);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new AccountRegistrationException(); }
    }

    public async Task<Guid?> AuthenticateLocalAsync(string email, string password, CancellationToken token)
    {
        users.OperationCancellationToken = token;
        token.ThrowIfCancellationRequested();
        var user = await users.FindByEmailAsync(email);
        if (user is null || user.PasswordHash is null)
        {
            VerifyUnknownPassword(password);
            return null;
        }
        if (!await VerifyAsync(user, password)) return null;
        token.ThrowIfCancellationRequested();
        return user.Id;
    }

    public async Task<bool> ConfirmPasswordAsync(Guid id, string password, CancellationToken token)
    {
        users.OperationCancellationToken = token;
        token.ThrowIfCancellationRequested();
        var user = await users.FindByIdAsync(id.ToString());
        return user is not null && await VerifyAsync(user, password);
    }

    private async Task<bool> VerifyAsync(CredentialUser user, string password)
    {
        if (await users.IsLockedOutAsync(user)) { VerifyUnknownPassword(password); return false; }
        if (!await users.CheckPasswordAsync(user, password))
        {
            await users.AccessFailedAsync(user);
            return false;
        }
        return (await users.ResetAccessFailedCountAsync(user)).Succeeded;
    }

    private void VerifyUnknownPassword(string password) => users.PasswordHasher.VerifyHashedPassword(UnknownUser, UnknownPasswordHash, password);

    public async Task ChangePasswordAsync(Guid id, string currentPassword, string newPassword, CancellationToken token)
    {
        users.OperationCancellationToken = token;
        token.ThrowIfCancellationRequested();
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null || !await VerifyAsync(user, currentPassword)) throw new AccountAuthenticationException();
        if (!(await users.ChangePasswordAsync(user, currentPassword, newPassword)).Succeeded) throw new AccountAuthenticationException();
        token.ThrowIfCancellationRequested();
    }

    public async Task RevokeSessionsAsync(Guid id, CancellationToken token)
    {
        users.OperationCancellationToken = token;
        token.ThrowIfCancellationRequested();
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null || !(await users.UpdateSecurityStampAsync(user)).Succeeded) throw new AccountAuthenticationException();
        token.ThrowIfCancellationRequested();
    }

    public async Task SaveAsync(Account account, bool revokeSessions, CancellationToken token)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        // Only mutable profile/state fields are written. The predicate prevents a stale profile
        // edit from reactivating or changing an account disabled by another request.
        var affected = await database.Accounts.Where(x => x.Id == account.Id && !x.IsDisabled)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.DisplayName, account.DisplayName)
                .SetProperty(x => x.IsDisabled, account.IsDisabled), token);
        if (affected != 1) throw new AccountAuthenticationException();
        if (revokeSessions) await RevokeSessionsAsync(account.Id, token);
        await transaction.CommitAsync(token);
    }

    public async Task SaveRoleAsync(Guid id, AccountRole role, CancellationToken token)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        var affected = await database.Accounts.Where(account => account.Id == id && !account.IsDisabled)
            .ExecuteUpdateAsync(update => update.SetProperty(account => account.Role, role), token);
        if (affected != 1) throw new ResourceNotFoundException();
        await RevokeSessionsAsync(id, token);
        await transaction.CommitAsync(token);
    }
}
