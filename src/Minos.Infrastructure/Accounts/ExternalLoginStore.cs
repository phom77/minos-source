using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Minos.Application.Accounts;
using Minos.Domain.Accounts;
using Minos.Infrastructure.Persistence;
using Npgsql;

namespace Minos.Infrastructure.Accounts;

public sealed class ExternalLoginStore(MinosDbContext database, AccountUserManager users) : IExternalLoginStore
{
    public async Task<Guid?> FindAsync(string provider, string subject, CancellationToken token)
    {
        users.OperationCancellationToken = token;
        token.ThrowIfCancellationRequested();
        return (await users.FindByLoginAsync(provider, subject))?.Id;
    }

    public async Task RegisterAsync(Account account, ExternalIdentity identity, CancellationToken token)
    {
        users.OperationCancellationToken = token;
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        try
        {
            // No email-based merge. Framework email validation plus the unique index protect races.
            database.Accounts.Add(account);
            await database.SaveChangesAsync(token);
            var user = new CredentialUser { Id = account.Id, UserName = identity.Email, Email = identity.Email, EmailConfirmed = true };
            if (!(await users.CreateAsync(user)).Succeeded) throw new ExternalLoginException();
            if (!(await users.AddLoginAsync(user, Login(identity))).Succeeded) throw new ExternalLoginException();
            await transaction.CommitAsync(token);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new ExternalLoginException(); }
    }

    public async Task LinkAsync(Guid accountId, ExternalIdentity identity, CancellationToken token)
    {
        users.OperationCancellationToken = token;
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        try
        {
            var user = await users.FindByIdAsync(accountId.ToString()) ?? throw new AccountAuthenticationException();
            if (!(await users.AddLoginAsync(user, Login(identity))).Succeeded) throw new ExternalLoginException();
            if (!(await users.UpdateSecurityStampAsync(user)).Succeeded) throw new AccountAuthenticationException();
            await transaction.CommitAsync(token);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new ExternalLoginException(); }
    }

    private static UserLoginInfo Login(ExternalIdentity identity) => new(identity.Provider, identity.Subject, identity.Provider);
}
