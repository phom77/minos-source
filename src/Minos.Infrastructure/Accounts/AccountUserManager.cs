using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Minos.Infrastructure.Accounts;

// UserManager's public operations lack CancellationToken arguments. Its supported override
// propagates the current use-case token into the framework's EF store, without HTTP dependencies.
public sealed class AccountUserManager(IUserStore<CredentialUser> store, IOptions<IdentityOptions> options,
    IPasswordHasher<CredentialUser> hasher, IEnumerable<IUserValidator<CredentialUser>> userValidators,
    IEnumerable<IPasswordValidator<CredentialUser>> passwordValidators, ILookupNormalizer normalizer,
    IdentityErrorDescriber errors, IServiceProvider services, ILogger<UserManager<CredentialUser>> logger)
    : UserManager<CredentialUser>(store, options, hasher, userValidators, passwordValidators, normalizer, errors, services, logger)
{
    public CancellationToken OperationCancellationToken { get; set; }
    protected override CancellationToken CancellationToken => OperationCancellationToken;
}
