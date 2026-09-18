using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;
using Minos.Application.Accounts;

namespace Minos.Api.Accounts;

// The two delegates bridge standard SignInManager composition without leaking persistence types.
internal sealed class ExternalCookieFlow(Func<string?, AuthenticationProperties> createProperties,
    Func<string?, Task<ExternalLoginInfo?>> readIdentity)
{
    public AuthenticationProperties Start(string? accountId) => createProperties(accountId);
    public Task<ExternalLoginInfo?> ReadAsync(string? accountId) => readIdentity(accountId);
}

internal static class GoogleEndpoints
{
    public static void MapGoogleEndpoints(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("Authentication:Google:Enabled")) return;
        app.MapGet("/accounts/google/login", async (HttpContext context, ExternalCookieFlow flow) =>
        {
            await context.SignOutAsync(IdentityConstants.ExternalScheme);
            return Results.Challenge(flow.Start(null), [GoogleDefaults.AuthenticationScheme]);
        }).RequireRateLimiting("authentication");
        app.MapPost("/accounts/google/link", async (ConfirmPasswordRequest request, ExternalLoginUseCases useCases, HttpContext context, ExternalCookieFlow flow, CancellationToken token) =>
        {
            var id = await useCases.BeginLinkAsync(request.Password, token);
            await context.SignOutAsync(IdentityConstants.ExternalScheme);
            return Results.Challenge(flow.Start(id.ToString()), [GoogleDefaults.AuthenticationScheme]);
        }).RequireRateLimiting("authentication");
        app.MapGet("/accounts/google/complete", async (HttpContext context, ExternalCookieFlow flow, ExternalLoginUseCases useCases, AccountCookieSession session, CancellationToken token) =>
        {
            try
            {
                var external = await context.AuthenticateAsync(IdentityConstants.ExternalScheme);
                if (!external.Succeeded || external.Properties is null) throw new AccountAuthenticationException();
                external.Properties.Items.TryGetValue("minos:link-account", out var link);
                var info = await flow.ReadAsync(link) ?? throw new AccountAuthenticationException();
                if (info.LoginProvider != GoogleDefaults.AuthenticationScheme) throw new AccountAuthenticationException();
                var identity = new ExternalIdentity(info.LoginProvider, info.ProviderKey, info.Principal.FindFirstValue(ClaimTypes.Email),
                    bool.TryParse(info.Principal.FindFirstValue("minos:email-verified"), out var verified) && verified,
                    info.Principal.FindFirstValue(ClaimTypes.Name));
                var account = link is null ? await useCases.SignInAsync(identity, token)
                    : await useCases.CompleteLinkAsync(Guid.Parse(link), identity, token);
                await session.IssueAsync(context, account.Id, token);
                // Fixed local destination avoids open redirects.
                return Results.Redirect("/accounts/me");
            }
            finally { await context.SignOutAsync(IdentityConstants.ExternalScheme); }
        }).RequireRateLimiting("authentication");
        app.MapGet("/accounts/google/error", () => Results.Problem(statusCode: 400, title: "External authentication failed."));
    }
}
