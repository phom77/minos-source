using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Minos.Api.Accounts;

internal sealed class AccountCookieSession(Func<Guid, CancellationToken, Task<ClaimsPrincipal>> createPrincipal)
{
    public async Task IssueAsync(HttpContext context, Guid accountId, CancellationToken token) =>
        await context.SignInAsync(IdentityConstants.ApplicationScheme, await createPrincipal(accountId, token),
            new AuthenticationProperties { IsPersistent = false, AllowRefresh = false });

    public static async Task ClearAsync(HttpContext context)
    {
        await context.SignOutAsync(IdentityConstants.ApplicationScheme);
        await context.SignOutAsync(IdentityConstants.ExternalScheme);
    }
}
