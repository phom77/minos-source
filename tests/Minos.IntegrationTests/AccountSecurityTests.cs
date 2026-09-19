using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Minos.Api.Accounts;
using Minos.Application;
using Minos.Application.Accounts;
using Minos.Domain.Accounts;
using Npgsql;
using Xunit;

namespace Minos.IntegrationTests;

public sealed class AccountSecurityTests
{
    [Fact]
    public async Task Concurrent_disabling_prevents_an_in_flight_profile_edit_from_committing()
    {
        await using var factory = AccountTestHost.Factory();
        using var client = AccountTestHost.Client(factory);
        var token = TestContext.Current.CancellationToken;
        var response = await AccountTestHost.RegisterAsync(client, AccountTestHost.Email());
        var id = (await response.Content.ReadFromJsonAsync<AccountResponse>(token))!.Id;
        await using var editing = factory.Services.CreateAsyncScope();
        await using var disabling = factory.Services.CreateAsyncScope();
        var editor = editing.ServiceProvider.GetRequiredService<IAccountStore>();
        var disabler = disabling.ServiceProvider.GetRequiredService<IAccountStore>();
        var original = (await editor.FindAsync(id, token))!;
        var changed = (await disabler.FindAsync(id, token))!;
        original.Rename("Late edit");
        changed.Disable();
        await disabler.SaveAsync(changed, true, token);
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => editor.SaveAsync(original, false, token));
    }
    [Fact]
    public async Task Session_has_absolute_expiration_even_with_per_request_stamp_validation()
    {
        var clock = new MutableClock();
        await using var factory = AccountTestHost.Factory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.Configure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options => options.TimeProvider = clock)));
        using var client = AccountTestHost.Client(factory);
        var token = TestContext.Current.CancellationToken;
        Assert.Equal(HttpStatusCode.Created, (await AccountTestHost.RegisterAsync(client, AccountTestHost.Email())).StatusCode);
        clock.Advance(TimeSpan.FromHours(7));
        var active = await client.GetAsync("/accounts/me", token);
        Assert.Equal(HttpStatusCode.OK, active.StatusCode);
        Assert.False(active.Headers.TryGetValues("Set-Cookie", out _));
        clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/accounts/me", token)).StatusCode);
    }

    [Fact]
    public async Task Account_application_mutations_require_an_active_actor_without_http()
    {
        var token = TestContext.Current.CancellationToken;
        var account = Account.Create("Name", DateTimeOffset.UtcNow);
        var actor = new TestCurrentAccount(null);
        var store = new AppAccountStore(account);
        var cases = new AccountUseCases(store, actor, TimeProvider.System);
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => cases.RenameAsync("Changed", token));
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => cases.LogoutAsync(token));
        actor.AccountId = account.Id;
        Assert.Equal("Changed", (await cases.RenameAsync("Changed", token)).DisplayName);
        var external = new ExternalLoginUseCases(new RejectExternalStore(), store, cases, TimeProvider.System);
        await Assert.ThrowsAsync<AccessDeniedException>(() => external.CompleteLinkAsync(Guid.NewGuid(), new("Google", "subject", "name@example.test", true, "Name"), token));
        account.Disable();
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => cases.CurrentAsync(token));
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => cases.RenameAsync("Changed again", token));
    }

    [Fact]
    public async Task Account_http_requires_https_and_disabled_google_configuration_exposes_no_provider_routes()
    {
        await using var factory = AccountTestHost.Factory();
        using var insecure = factory.CreateClient();
        var denied = await insecure.GetAsync("/accounts/csrf", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        using var secure = AccountTestHost.Client(factory);
        Assert.Equal(HttpStatusCode.NotFound, (await secure.GetAsync("/accounts/google/login", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Theory]
    [InlineData("\u2003")]
    [InlineData("\u3000Name\u3000")]
    [InlineData("Name\u0085control")]
    public async Task PostgreSQL_rejects_invalid_profiles_without_domain_validation(string name)
    {
        await using var connection = new NpgsqlConnection(PostgresPrivilegeTests.Connection("ConnectionStrings__Minos"));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using var insert = new NpgsqlCommand("INSERT INTO accounts.accounts (id,display_name,created_at_utc,is_disabled,role) VALUES (@id,@name,now(),false,0)", connection, transaction);
        insert.Parameters.AddWithValue("id", Guid.NewGuid());
        insert.Parameters.AddWithValue("name", name);
        var error = await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Fact]
    public async Task PostgreSQL_protects_profile_utf16_boundaries_and_unknown_draft_owners()
    {
        var token = TestContext.Current.CancellationToken;
        await using var connection = new NpgsqlConnection(PostgresPrivilegeTests.Connection("ConnectionStrings__Minos"));
        await connection.OpenAsync(token);
        await using (var transaction = await connection.BeginTransactionAsync(token))
        {
            await using var insert = new NpgsqlCommand("INSERT INTO accounts.accounts (id,display_name,created_at_utc,is_disabled,role) VALUES (@id,@name,now(),false,0)", connection, transaction);
            insert.Parameters.AddWithValue("id", Guid.NewGuid());
            insert.Parameters.AddWithValue("name", string.Concat(Enumerable.Repeat("🧩", 40)));
            Assert.Equal(1, await insert.ExecuteNonQueryAsync(token));
            insert.Parameters["id"].Value = Guid.NewGuid();
            insert.Parameters["name"].Value += "🧩";
            Assert.Equal(PostgresErrorCodes.CheckViolation, (await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync(token))).SqlState);
        }
        await using var ownerInsert = new NpgsqlCommand("INSERT INTO problems.problems (id,title,statement,created_at_utc,updated_at_utc,owner_account_id) VALUES (gen_random_uuid(),'Title','Statement',now(),now(),gen_random_uuid())", connection);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, (await Assert.ThrowsAsync<PostgresException>(() => ownerInsert.ExecuteNonQueryAsync(token))).SqlState);
    }

    private sealed class MutableClock : TimeProvider
    {
        private DateTimeOffset instant = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => instant;
        public void Advance(TimeSpan duration) => instant += duration;
    }
    private sealed class RejectExternalStore : IExternalLoginStore
    {
        public Task<Guid?> FindAsync(string provider, string subject, CancellationToken token) => throw new InvalidOperationException("Authorization must run first.");
        public Task RegisterAsync(Account account, ExternalIdentity identity, CancellationToken token) => throw new InvalidOperationException("Authorization must run first.");
        public Task LinkAsync(Guid accountId, ExternalIdentity identity, CancellationToken token) => throw new InvalidOperationException("Authorization must run first.");
    }
}
