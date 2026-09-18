using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Minos.Api.Accounts;
using Minos.Infrastructure.Persistence;
using Xunit;

namespace Minos.IntegrationTests;

public sealed class AccountTests
{
    [Fact]
    public async Task Self_service_registration_issues_secure_cookie_and_persists_hashed_credentials()
    {
        await using var factory = AccountTestHost.Factory();
        using var client = AccountTestHost.Client(factory);
        var token = TestContext.Current.CancellationToken;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/accounts/me", token)).StatusCode);
        var registered = await AccountTestHost.RegisterAsync(client, AccountTestHost.Email());
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var account = await registered.Content.ReadFromJsonAsync<AccountResponse>(token);
        Assert.NotNull(account);
        var cookie = registered.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("__Host-Minos.Session=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(account.Id, (await client.GetFromJsonAsync<AccountResponse>("/accounts/me", token))!.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MinosDbContext>();
        var persisted = await database.Users.SingleAsync(x => x.Id == account.Id, token);
        Assert.NotNull(persisted.PasswordHash);
        Assert.NotEqual(AccountTestHost.Password, persisted.PasswordHash);
        Assert.False(persisted.EmailConfirmed);
        var json = await registered.Content.ReadAsStringAsync(token);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stamp", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Duplicate_and_invalid_registration_are_generic_and_atomic_under_concurrency()
    {
        await using var factory = AccountTestHost.Factory();
        var email = AccountTestHost.Email();
        var token = TestContext.Current.CancellationToken;
        using var first = AccountTestHost.Client(factory);
        using var second = AccountTestHost.Client(factory);
        await AccountTestHost.CsrfAsync(first);
        await AccountTestHost.CsrfAsync(second);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MinosDbContext>();
        var before = await database.Accounts.CountAsync(token);
        var results = await Task.WhenAll(AccountTestHost.RegisterAsync(first, email), AccountTestHost.RegisterAsync(second, email.ToUpperInvariant()));
        Assert.Single(results, result => result.StatusCode == HttpStatusCode.Created);
        var duplicate = results.Single(result => result.StatusCode == HttpStatusCode.BadRequest);
        await AccountTestHost.CsrfAsync(first);
        var invalid = await first.PostAsJsonAsync("/accounts/register", new { email = "invalid", password = "tiny", displayName = "Name" }, token);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(Title(await duplicate.Content.ReadAsStringAsync(token)), Title(await invalid.Content.ReadAsStringAsync(token)));
        Assert.Equal(before + 1, await database.Accounts.CountAsync(token));
        Assert.DoesNotContain(email, await duplicate.Content.ReadAsStringAsync(token), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Local_login_survives_restart_and_logout_revokes_replayed_and_other_session_cookies()
    {
        var email = AccountTestHost.Email();
        var token = TestContext.Current.CancellationToken;
        string cookie;
        Guid id;
        await using (var factory = AccountTestHost.Factory())
        {
            using var client = AccountTestHost.Client(factory);
            var registered = await AccountTestHost.RegisterAsync(client, email);
            Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
            id = (await registered.Content.ReadFromJsonAsync<AccountResponse>(token))!.Id;
            cookie = AccountTestHost.Cookie(registered);
        }
        await using var restarted = AccountTestHost.Factory();
        using var replay = AccountTestHost.Client(restarted, false);
        replay.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.Equal(id, (await replay.GetFromJsonAsync<AccountResponse>("/accounts/me", token))!.Id);
        using var other = AccountTestHost.Client(restarted);
        await AccountTestHost.LoginAsync(other, email);
        Assert.Equal(HttpStatusCode.NoContent, (await other.PostAsync("/accounts/logout", null, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.GetAsync("/accounts/me", token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync("/accounts/me", token)).StatusCode);
    }

    [Fact]
    public async Task Bad_password_unknown_email_and_locked_account_have_same_safe_contract()
    {
        await using var factory = AccountTestHost.Factory();
        using var client = AccountTestHost.Client(factory);
        var email = AccountTestHost.Email();
        var token = TestContext.Current.CancellationToken;
        Assert.Equal(HttpStatusCode.Created, (await AccountTestHost.RegisterAsync(client, email)).StatusCode);
        await AccountTestHost.CsrfAsync(client);
        var unknown = await client.PostAsJsonAsync("/accounts/login", new { email = AccountTestHost.Email(), password = AccountTestHost.Password }, token);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var failed = await client.PostAsJsonAsync("/accounts/login", new { email, password = "incorrect password here" }, token);
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
            Assert.Equal(Title(await unknown.Content.ReadAsStringAsync(token)), Title(await failed.Content.ReadAsStringAsync(token)));
        }
        var locked = await client.PostAsJsonAsync("/accounts/login", new { email, password = AccountTestHost.Password }, token);
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        var text = await locked.Content.ReadAsStringAsync(token);
        Assert.DoesNotContain(email, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password here", text, StringComparison.Ordinal);
        Assert.DoesNotContain("stack", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Csrf_bounded_requests_and_authentication_rate_limit_are_enforced()
    {
        await using var factory = AccountTestHost.Factory();
        using var client = AccountTestHost.Client(factory);
        var token = TestContext.Current.CancellationToken;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/accounts/login", new { }, token)).StatusCode);
        await AccountTestHost.CsrfAsync(client);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge,
            (await client.PostAsJsonAsync("/accounts/login", new { email = new string('x', 17000) }, token)).StatusCode);
        HttpResponseMessage? last = null;
        for (var i = 0; i < 21; i++)
            last = await client.PostAsJsonAsync("/accounts/login", new { email = "invalid", password = "invalid" }, token);
        Assert.Equal(HttpStatusCode.TooManyRequests, last!.StatusCode);
        Assert.Equal("application/problem+json", last.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Profile_update_cannot_change_identity_or_grant_permissions_and_password_change_and_disable_revoke_sessions()
    {
        await using var factory = AccountTestHost.Factory();
        using var client = AccountTestHost.Client(factory);
        var token = TestContext.Current.CancellationToken;
        var email = AccountTestHost.Email();
        var registered = await AccountTestHost.RegisterAsync(client, email);
        var account = (await registered.Content.ReadFromJsonAsync<AccountResponse>(token))!;
        await AccountTestHost.CsrfAsync(client);
        var renamed = await client.PatchAsJsonAsync("/accounts/me", new { displayName = "New name", id = Guid.NewGuid(), isDisabled = true, roles = new[] { "Admin" } }, token);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(account.Id, (await renamed.Content.ReadFromJsonAsync<AccountResponse>(token))!.Id);
        using var replay = AccountTestHost.Client(factory, false);
        replay.DefaultRequestHeaders.Add("Cookie", AccountTestHost.Cookie(registered));
        const string replacement = "a different secure password";
        var changed = await client.PostAsJsonAsync("/accounts/password", new { currentPassword = AccountTestHost.Password, newPassword = replacement }, token);
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.GetAsync("/accounts/me", token)).StatusCode);
        await AccountTestHost.LoginAsync(client, email, replacement);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/accounts/disable", new { password = replacement }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/accounts/me", token)).StatusCode);
        await AccountTestHost.CsrfAsync(client);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/accounts/login", new { email, password = replacement }, token)).StatusCode);
    }

    private static string? Title(string json) => JsonDocument.Parse(json).RootElement.GetProperty("title").GetString();
}
