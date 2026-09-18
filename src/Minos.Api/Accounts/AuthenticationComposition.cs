using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using System.Threading.RateLimiting;
using Minos.Api.Accounts;
using Minos.Application.Accounts;
using Minos.Infrastructure;
using Minos.Infrastructure.Accounts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.Google;
using Minos.Infrastructure.Hosting;

namespace Minos.Api;

// Identity persistence types are confined to this DI/authentication hosting composition.
internal static class AuthenticationComposition
{
    public static void AddAccountAuthentication(this WebApplicationBuilder builder)
    {
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentAccount, HttpCurrentAccount>();
        builder.Services.AddMinosAccountPersistence().AddSignInManager();
        // DataProtection can initialize before hosted startup checks. Create/validate the private
        // directory first, without relying on the developer shell's umask.
        var keyRoot = Path.Combine(builder.Configuration["Minos:Artifacts:Root"]!, "data-protection");
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(keyRoot);
        else Directory.CreateDirectory(keyRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        _ = new PrivateArtifactDirectory(keyRoot, builder.Environment.ContentRootPath);
        var protection = builder.Services.AddDataProtection().SetApplicationName("Minos")
            .PersistKeysToFileSystem(new DirectoryInfo(keyRoot));
        if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
            options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
            options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
        }).AddIdentityCookies();
        builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "__Host-Minos.Session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = false;
            options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
            options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
            options.Events.OnValidatePrincipal = async context =>
            {
                context.HttpContext.RequestServices.GetRequiredService<AccountUserManager>().OperationCancellationToken = context.HttpContext.RequestAborted;
                await SecurityStampValidator.ValidatePrincipalAsync(context);
                // Preserve the absolute expiration; stamp checks must not extend the session.
                context.ShouldRenew = false;
                if (context.Principal is null) return;
                var id = context.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
                var store = context.HttpContext.RequestServices.GetRequiredService<IAccountStore>();
                var account = Guid.TryParse(id, out var parsed) ? await store.FindAsync(parsed, context.HttpContext.RequestAborted) : null;
                if (account is null || account.IsDisabled)
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                }
            };
        });
        builder.Services.AddScoped<AccountCookieSession>(services => new(async (id, token) =>
        {
            token.ThrowIfCancellationRequested();
            services.GetRequiredService<AccountUserManager>().OperationCancellationToken = token;
            var user = await services.GetRequiredService<UserManager<CredentialUser>>().FindByIdAsync(id.ToString())
                ?? throw new AccountAuthenticationException();
            return await services.GetRequiredService<IUserClaimsPrincipalFactory<CredentialUser>>().CreateAsync(user);
        }));
        builder.Services.Configure<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(IdentityConstants.ExternalScheme, options =>
        {
            options.Cookie.Name = "__Host-Minos.External";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
        });
        if (builder.Configuration.GetValue<bool>("Authentication:Google:Enabled"))
        {
            var clientId = builder.Configuration["Authentication:Google:ClientId"];
            var clientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
                throw new InvalidOperationException("Enabled Google authentication requires Authentication:Google:ClientId and ClientSecret through user secrets or environment variables.");
            builder.Services.AddAuthentication().AddGoogle(options =>
            {
                options.ClientId = clientId;
                options.ClientSecret = clientSecret;
                options.SignInScheme = IdentityConstants.ExternalScheme;
                options.SaveTokens = false;
                options.BackchannelTimeout = TimeSpan.FromSeconds(15);
                options.RemoteAuthenticationTimeout = TimeSpan.FromMinutes(5);
                options.ClaimActions.MapJsonKey("minos:email-verified", "verified_email");
                options.Events.OnRemoteFailure = context =>
                {
                    context.HandleResponse();
                    context.Response.Redirect("/accounts/google/error");
                    return Task.CompletedTask;
                };
            });
            builder.Services.AddScoped<ExternalCookieFlow>(services =>
            {
                var signIn = services.GetRequiredService<SignInManager<CredentialUser>>();
                return new(id =>
                {
                    var properties = signIn.ConfigureExternalAuthenticationProperties(GoogleDefaults.AuthenticationScheme, "/accounts/google/complete", id);
                    if (id is not null) properties.Items["minos:link-account"] = id;
                    return properties;
                }, id => signIn.GetExternalLoginInfoAsync(id));
            });
            builder.Logging.AddFilter("Microsoft.AspNetCore.Authentication.Google", LogLevel.None);
        }
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "__Host-Minos.Csrf";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.AddPolicy("authentication", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        // Identity may include submitted email in validation logs. Keep credential diagnostics private.
        builder.Logging.AddFilter("Microsoft.AspNetCore.Identity", LogLevel.None);
        // Hosting request logs otherwise include OAuth codes/state from callback query strings.
        builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
    }

    public static void UseAccountRequestProtection(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/accounts"))
            {
                if (!context.Request.IsHttps) throw new BadHttpRequestException("HTTPS is required.", 400);
                context.Response.Headers.CacheControl = "no-store";
                var limit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = 16 * 1024;
                // Also bound chunked bodies and test/hosting implementations without Kestrel's feature.
                if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
                {
                    if (context.Request.ContentLength > 16 * 1024) throw new BadHttpRequestException("Request too large.", 413);
                    context.Request.EnableBuffering(bufferThreshold: 16 * 1024, bufferLimit: 16 * 1024);
                    try { await context.Request.Body.CopyToAsync(Stream.Null, context.RequestAborted); }
                    catch (IOException) { throw new BadHttpRequestException("Request too large.", 413); }
                    context.Request.Body.Position = 0;
                }
            }
            // Only endpoints that actually exist require a token; status contracts remain 404 for absent routes.
            if (context.GetEndpoint() is not null && !HttpMethods.IsGet(context.Request.Method)
                && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method))
            {
                try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
                catch (AntiforgeryValidationException) { throw new BadHttpRequestException("Invalid request.", 400); }
            }
            await next(context);
        });
    }
}
