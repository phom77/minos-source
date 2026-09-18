using System.Security.Claims;
using Minos.Application.Accounts;

namespace Minos.Api.Accounts;

internal sealed class HttpCurrentAccount(IHttpContextAccessor accessor) : ICurrentAccount
{
    public Guid? AccountId => accessor.HttpContext?.User is { Identity.IsAuthenticated: true } principal
        && Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
