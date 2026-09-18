using Minos.Application.Accounts;
using Minos.Domain.Accounts;

namespace Minos.IntegrationTests;

internal sealed class TestCurrentAccount(Guid? id) : ICurrentAccount { public Guid? AccountId { get; set; } = id; }
internal sealed class AppAccountStore(Account account) : IAccountStore
{
    public Task<Account?> FindAsync(Guid id, CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult<Account?>(id == account.Id ? account : null); }
    public Task RegisterLocalAsync(Account created, string email, string password, CancellationToken token) => Task.CompletedTask;
    public Task<Guid?> AuthenticateLocalAsync(string email, string password, CancellationToken token) => Task.FromResult<Guid?>(account.Id);
    public Task<bool> ConfirmPasswordAsync(Guid id, string password, CancellationToken token) => Task.FromResult(true);
    public Task ChangePasswordAsync(Guid id, string currentPassword, string newPassword, CancellationToken token) => Task.CompletedTask;
    public Task SaveAsync(Account value, bool revokeSessions, CancellationToken token) => Task.CompletedTask;
    public Task RevokeSessionsAsync(Guid id, CancellationToken token) => Task.CompletedTask;
}
