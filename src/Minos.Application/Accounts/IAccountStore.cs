using Minos.Domain.Accounts;

namespace Minos.Application.Accounts;

// Focused account operations. Registration and disable/revocation are atomic persistence boundaries.
public interface IAccountStore
{
    Task<Account?> FindAsync(Guid id, CancellationToken token);
    Task RegisterLocalAsync(Account account, string email, string password, CancellationToken token);
    Task<Guid?> AuthenticateLocalAsync(string email, string password, CancellationToken token);
    Task<bool> ConfirmPasswordAsync(Guid id, string password, CancellationToken token);
    Task ChangePasswordAsync(Guid id, string currentPassword, string newPassword, CancellationToken token);
    Task SaveAsync(Account account, bool revokeSessions, CancellationToken token);
    Task SaveRoleAsync(Guid id, AccountRole role, CancellationToken token);
    Task RevokeSessionsAsync(Guid id, CancellationToken token);
}
