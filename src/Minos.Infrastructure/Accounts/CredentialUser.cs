using Microsoft.AspNetCore.Identity;

namespace Minos.Infrastructure.Accounts;

// Authentication persistence, deliberately separate from the Domain account/profile.
public sealed class CredentialUser : IdentityUser<Guid>;
