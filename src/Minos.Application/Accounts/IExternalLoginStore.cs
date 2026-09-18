using Minos.Domain.Accounts;

namespace Minos.Application.Accounts;

// Identity evidence is supplied only after the host's provider middleware authenticates it.
public sealed record ExternalIdentity(string Provider, string Subject, string? Email, bool EmailVerified, string? DisplayName);

public interface IExternalLoginStore
{
    Task<Guid?> FindAsync(string provider, string subject, CancellationToken token);
    Task RegisterAsync(Account account, ExternalIdentity identity, CancellationToken token);
    Task LinkAsync(Guid accountId, ExternalIdentity identity, CancellationToken token);
}
