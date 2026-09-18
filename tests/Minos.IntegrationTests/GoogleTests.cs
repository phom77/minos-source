using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Minos.Api.Accounts;
using Minos.Application.Accounts;
using Minos.Infrastructure.Persistence;
using Xunit;

namespace Minos.IntegrationTests;

public sealed class GoogleTests
{
    [Fact]
    public async Task Framework_google_flow_creates_account_and_stable_subject_survives_changed_email_and_restart()
    {
        Guid id;
        var subject = $"google-{Guid.NewGuid():N}";
        await using (var host = new GoogleHost(AccountTestHost.Email(), subject))
        {
            using var client = AccountTestHost.Client(host.Factory);
            var completed = await host.SignInAsync(client);
            Assert.Equal(HttpStatusCode.Redirect, completed.StatusCode);
            Assert.Equal("/accounts/me", completed.Headers.Location!.OriginalString);
            var account = await client.GetFromJsonAsync<AccountResponse>("/accounts/me", TestContext.Current.CancellationToken);
            id = account!.Id;
            await using var scope = host.Factory.Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<MinosDbContext>();
            var user = await database.Users.SingleAsync(x => x.Id == id, TestContext.Current.CancellationToken);
            Assert.Null(user.PasswordHash);
            Assert.True(user.EmailConfirmed);
            Assert.Equal(id, (await database.UserLogins.SingleAsync(x => x.ProviderKey == subject, TestContext.Current.CancellationToken)).UserId);
        }
        await using var restarted = new GoogleHost(AccountTestHost.Email(), subject);
        using var restartedClient = AccountTestHost.Client(restarted.Factory);
        Assert.Equal(HttpStatusCode.Redirect, (await restarted.SignInAsync(restartedClient)).StatusCode);
        Assert.Equal(id, (await restartedClient.GetFromJsonAsync<AccountResponse>("/accounts/me", TestContext.Current.CancellationToken))!.Id);
    }

    [Fact]
    public async Task Matching_email_never_merges_and_explicit_password_and_provider_proof_link_the_existing_account()
    {
        var email = AccountTestHost.Email();
        await using var host = new GoogleHost(email);
        using var local = AccountTestHost.Client(host.Factory);
        using var google = AccountTestHost.Client(host.Factory);
        var token = TestContext.Current.CancellationToken;
        var created = await AccountTestHost.RegisterAsync(local, email);
        var account = (await created.Content.ReadFromJsonAsync<AccountResponse>(token))!;
        await using var scope = host.Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MinosDbContext>();
        var before = await database.Accounts.CountAsync(token);
        Assert.Equal(HttpStatusCode.Conflict, (await host.SignInAsync(google)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await google.GetAsync("/accounts/me", token)).StatusCode);
        Assert.Equal(before, await database.Accounts.CountAsync(token));
        Assert.False((await database.Users.SingleAsync(x => x.Id == account.Id, token)).EmailConfirmed);
        await AccountTestHost.CsrfAsync(local);
        var wrong = await local.PostAsJsonAsync("/accounts/google/link", new { password = "incorrect password here" }, token);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        var start = await local.PostAsJsonAsync("/accounts/google/link", new { password = AccountTestHost.Password }, token);
        Assert.Equal(HttpStatusCode.Redirect, (await host.CompleteAsync(local, start)).StatusCode);
        Assert.Equal(account.Id, (await local.GetFromJsonAsync<AccountResponse>("/accounts/me", token))!.Id);
        using var linkedLogin = AccountTestHost.Client(host.Factory);
        Assert.Equal(HttpStatusCode.Redirect, (await host.SignInAsync(linkedLogin)).StatusCode);
        Assert.Equal(account.Id, (await linkedLogin.GetFromJsonAsync<AccountResponse>("/accounts/me", token))!.Id);
        Assert.Equal(before, await database.Accounts.CountAsync(token));
    }

    [Fact]
    public async Task Link_is_bound_to_the_current_account_and_cannot_take_another_accounts_provider_subject()
    {
        await using var host = new GoogleHost(AccountTestHost.Email());
        using var first = AccountTestHost.Client(host.Factory);
        using var second = AccountTestHost.Client(host.Factory);
        var token = TestContext.Current.CancellationToken;
        var secondEmail = AccountTestHost.Email();
        Assert.Equal(HttpStatusCode.Created, (await AccountTestHost.RegisterAsync(first, AccountTestHost.Email())).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await AccountTestHost.RegisterAsync(second, secondEmail)).StatusCode);
        await AccountTestHost.CsrfAsync(first);
        var start = await first.PostAsJsonAsync("/accounts/google/link", new { password = AccountTestHost.Password }, token);
        await AccountTestHost.LoginAsync(first, secondEmail);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.CompleteAsync(first, start)).StatusCode);
        using var google = AccountTestHost.Client(host.Factory);
        Assert.Equal(HttpStatusCode.Redirect, (await host.SignInAsync(google)).StatusCode);
        await AccountTestHost.CsrfAsync(second);
        var duplicate = await second.PostAsJsonAsync("/accounts/google/link", new { password = AccountTestHost.Password }, token);
        Assert.Equal(HttpStatusCode.Conflict, (await host.CompleteAsync(second, duplicate)).StatusCode);
    }

    [Fact]
    public async Task Forged_state_missing_correlation_and_unverified_signup_fail_safely()
    {
        await using var host = new GoogleHost(AccountTestHost.Email()) { Verified = false };
        using var client = AccountTestHost.Client(host.Factory);
        var token = TestContext.Current.CancellationToken;
        var forged = await client.GetAsync("/signin-google?code=test-code&state=invalid", token);
        Assert.Equal("/accounts/google/error", forged.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(forged.Headers.Location, token)).StatusCode);
        var start = await client.GetAsync("/accounts/google/login", token);
        using var withoutCorrelation = AccountTestHost.Client(host.Factory);
        var query = QueryHelpers.ParseQuery(start.Headers.Location!.Query);
        var missing = await withoutCorrelation.GetAsync(QueryHelpers.AddQueryString("/signin-google", new Dictionary<string, string?>
        { ["code"] = "test-code", ["state"] = query["state"].ToString() }), token);
        Assert.Equal("/accounts/google/error", missing.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Conflict, (await host.SignInAsync(client)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/accounts/me", token)).StatusCode);
    }

    [Theory]
    [InlineData(null, "test-only-sentinel-secret")]
    [InlineData("test-only-client", null)]
    public async Task Enabled_google_requires_configuration_without_echoing_secrets(string? clientId, string? secret)
    {
        var variables = new Dictionary<string, string?>
        { ["Authentication__Google__Enabled"] = "true", ["Authentication__Google__ClientId"] = clientId ?? "", ["Authentication__Google__ClientSecret"] = secret ?? "" };
        var originals = variables.ToDictionary(x => x.Key, x => Environment.GetEnvironmentVariable(x.Key));
        foreach (var variable in variables) Environment.SetEnvironmentVariable(variable.Key, variable.Value);
        try
        {
            await using var factory = AccountTestHost.Factory();
            var error = Assert.Throws<InvalidOperationException>(() => AccountTestHost.Client(factory));
            Assert.Contains("Enabled Google authentication requires", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("sentinel-secret", error.Message, StringComparison.Ordinal);
        }
        finally { foreach (var original in originals) Environment.SetEnvironmentVariable(original.Key, original.Value); }
    }

    private sealed class GoogleHost : IAsyncDisposable
    {
        private readonly Dictionary<string, string?> originals;
        internal WebApplicationFactory<Program> Factory { get; }
        internal string Email { get; }
        internal string Subject { get; }
        internal bool Verified { get; init; } = true;
        internal GoogleHost(string email, string? subject = null)
        {
            Email = email;
            Subject = subject ?? $"google-{Guid.NewGuid():N}";
            var variables = new Dictionary<string, string?>
            { ["Authentication__Google__Enabled"] = "true", ["Authentication__Google__ClientId"] = "test-only-client", ["Authentication__Google__ClientSecret"] = "test-only-secret" };
            originals = variables.ToDictionary(x => x.Key, x => Environment.GetEnvironmentVariable(x.Key));
            foreach (var variable in variables) Environment.SetEnvironmentVariable(variable.Key, variable.Value);
            Factory = AccountTestHost.Factory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
                services.Configure<GoogleOptions>(GoogleDefaults.AuthenticationScheme, options => options.Backchannel = new HttpClient(new Backchannel(this)))));
        }
        internal async Task<HttpResponseMessage> SignInAsync(HttpClient client) => await CompleteAsync(client,
            await client.GetAsync("/accounts/google/login", TestContext.Current.CancellationToken));
        internal async Task<HttpResponseMessage> CompleteAsync(HttpClient client, HttpResponseMessage start)
        {
            Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
            var location = start.Headers.Location!;
            Assert.Equal("accounts.google.com", location.Host);
            var query = QueryHelpers.ParseQuery(location.Query);
            Assert.Equal("S256", query["code_challenge_method"].ToString());
            Assert.NotEmpty(query["code_challenge"].ToString());
            var callback = await client.GetAsync(QueryHelpers.AddQueryString("/signin-google", new Dictionary<string, string?>
            { ["code"] = "test-code", ["state"] = query["state"].ToString() }), TestContext.Current.CancellationToken);
            Assert.Equal("/accounts/google/complete", callback.Headers.Location!.OriginalString);
            return await client.GetAsync(callback.Headers.Location, TestContext.Current.CancellationToken);
        }
        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            foreach (var original in originals) Environment.SetEnvironmentVariable(original.Key, original.Value);
        }
        private sealed class Backchannel(GoogleHost host) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                if (request.Method == HttpMethod.Post)
                {
                    Assert.Equal(GoogleDefaults.TokenEndpoint, request.RequestUri!.AbsoluteUri);
                    var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(token));
                    Assert.NotEmpty(form["code_verifier"].ToString());
                    Assert.Equal("test-only-client", form["client_id"].ToString());
                }
                else
                {
                    Assert.Equal(GoogleDefaults.UserInformationEndpoint, request.RequestUri!.AbsoluteUri);
                    Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
                    Assert.Equal("test-only-token", request.Headers.Authorization.Parameter);
                }
                object payload = request.Method == HttpMethod.Post
                    ? new { access_token = "test-only-token", token_type = "Bearer", expires_in = 3600 }
                    : new { id = host.Subject, email = host.Email, verified_email = host.Verified, name = "Google developer" };
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };
            }
        }
    }
}
