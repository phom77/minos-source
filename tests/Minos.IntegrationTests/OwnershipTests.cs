using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Minos.Api;
using Minos.Infrastructure.Persistence;
using Npgsql;
using Xunit;

namespace Minos.IntegrationTests;

public sealed class OwnershipTests
{
    [Fact]
    public async Task Anonymous_cannot_read_or_create_and_another_account_cannot_read_a_private_draft()
    {
        await using var factory = AccountTestHost.Factory();
        using var owner = AccountTestHost.Client(factory);
        using var other = AccountTestHost.Client(factory);
        using var anonymous = AccountTestHost.Client(factory);
        var token = TestContext.Current.CancellationToken;
        await AccountTestHost.CsrfAsync(anonymous);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/_development/problem-drafts/", new CreateProblemDraftRequest("Title", "Private"), token)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await AccountTestHost.RegisterAsync(owner, AccountTestHost.Email())).StatusCode);
        await AccountTestHost.CsrfAsync(owner);
        var created = await owner.PostAsJsonAsync("/_development/problem-drafts/", new CreateProblemDraftRequest("Title", "Private"), token);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<ProblemDraftResponse>(token))!.Id;
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/_development/problem-drafts/{id}", token)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await AccountTestHost.RegisterAsync(other, AccountTestHost.Email())).StatusCode);
        var denied = await other.GetAsync($"/_development/problem-drafts/{id}", token);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.DoesNotContain("Private", await denied.Content.ReadAsStringAsync(token), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/_development/problem-drafts/{id}", token)).StatusCode);
    }

    [Fact]
    public async Task Ownership_upgrade_fails_without_changing_legacy_drafts()
    {
        var token = TestContext.Current.CancellationToken;
        var name = $"minos_upgrade_{Guid.NewGuid():N}";
        await using var bootstrap = new NpgsqlConnection(PostgresPrivilegeTests.Connection("MINOS_TEST_BOOTSTRAP_CONNECTION"));
        await bootstrap.OpenAsync(token);
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {name}", bootstrap)) await create.ExecuteNonQueryAsync(token);
        try
        {
            var bootstrapConfig = new NpgsqlConnectionStringBuilder(PostgresPrivilegeTests.Connection("MINOS_TEST_BOOTSTRAP_CONNECTION")) { Database = name, Pooling = false };
            await using (var setup = new NpgsqlConnection(bootstrapConfig.ConnectionString))
            {
                await setup.OpenAsync(token);
                await using var grant = new NpgsqlCommand($"REVOKE ALL ON DATABASE {name} FROM PUBLIC; GRANT CONNECT,CREATE ON DATABASE {name} TO minos_migrator; REVOKE ALL ON SCHEMA public FROM PUBLIC; GRANT USAGE,CREATE ON SCHEMA public TO minos_migrator", setup);
                await grant.ExecuteNonQueryAsync(token);
            }
            var config = new NpgsqlConnectionStringBuilder(PostgresPrivilegeTests.Connection("MINOS_TEST_MIGRATION_CONNECTION")) { Database = name, Pooling = false };
            await using var database = PostgresPrivilegeTests.Database(config.ConnectionString);
            await database.GetService<IMigrator>().MigrateAsync("20260914142551_AccountFoundation", token);
            await database.Database.ExecuteSqlRawAsync("INSERT INTO problems.problem_drafts VALUES (gen_random_uuid(),'Legacy','Preserve me',now())", token);
            var failure = await Assert.ThrowsAsync<PostgresException>(() => database.Database.MigrateAsync(token));
            Assert.Contains("explicit ownership migration", failure.MessageText, StringComparison.Ordinal);
            await database.Database.OpenConnectionAsync(token);
            await using var read = database.Database.GetDbConnection().CreateCommand();
            read.CommandText = "SELECT statement FROM problems.problem_drafts";
            Assert.Equal("Preserve me", await read.ExecuteScalarAsync(token));
        }
        finally
        {
            // The name is generated locally and contains only a fixed prefix plus GUID hex.
            await using var drop = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", bootstrap);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
