using Microsoft.AspNetCore.Antiforgery;
using Minos.Application.Accounts;

namespace Minos.Api.Accounts;

internal static class AccountEndpoints
{
    public static void MapAccountEndpoints(this WebApplication app)
    {
        var accounts = app.MapGroup("/accounts");
        accounts.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new CsrfResponse(antiforgery.GetAndStoreTokens(context).RequestToken!));
        });
        accounts.MapPost("/register", async (RegisterAccountRequest request, AccountUseCases useCases, AccountCookieSession session, HttpContext context, CancellationToken token) =>
        {
            var account = await useCases.RegisterAsync(request.Email, request.Password, request.DisplayName, token);
            await session.IssueAsync(context, account.Id, token);
            return Results.Created("/accounts/me", Map(account));
        }).RequireRateLimiting("authentication");
        accounts.MapPost("/login", async (LoginAccountRequest request, AccountUseCases useCases, AccountCookieSession session, HttpContext context, CancellationToken token) =>
        {
            var account = await useCases.LoginAsync(request.Email, request.Password, token);
            await session.IssueAsync(context, account.Id, token);
            return Results.Ok(Map(account));
        }).RequireRateLimiting("authentication");
        accounts.MapPost("/logout", async (AccountUseCases useCases, HttpContext context, CancellationToken token) =>
        {
            await useCases.LogoutAsync(token);
            await AccountCookieSession.ClearAsync(context);
            return Results.NoContent();
        });
        accounts.MapGet("/me", async (AccountUseCases useCases, CancellationToken token) => Results.Ok(Map(await useCases.CurrentAsync(token))));
        accounts.MapPatch("/me", async (RenameAccountRequest request, AccountUseCases useCases, CancellationToken token) => Results.Ok(Map(await useCases.RenameAsync(request.DisplayName, token))));
        accounts.MapPost("/password", async (ChangePasswordRequest request, AccountUseCases useCases, HttpContext context, CancellationToken token) =>
        {
            await useCases.ChangePasswordAsync(request.CurrentPassword, request.NewPassword, token);
            await AccountCookieSession.ClearAsync(context);
            return Results.NoContent();
        }).RequireRateLimiting("authentication");
        accounts.MapPost("/disable", async (ConfirmPasswordRequest request, AccountUseCases useCases, HttpContext context, CancellationToken token) =>
        {
            await useCases.DisableAsync(request.Password, token);
            await AccountCookieSession.ClearAsync(context);
            return Results.NoContent();
        }).RequireRateLimiting("authentication");
    }

    internal static AccountResponse Map(AccountDetails account) => new(account.Id, account.DisplayName, account.CreatedAtUtc);
}
