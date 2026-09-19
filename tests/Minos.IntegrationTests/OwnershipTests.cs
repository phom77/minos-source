using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Minos.Api.Accounts;
using Minos.Api.Problems;
using Minos.Domain.Accounts;
using Minos.Infrastructure.Persistence;
using Npgsql;
using Xunit;

namespace Minos.IntegrationTests;

public sealed class OwnershipTests
{
    [Fact]
    public async Task Anonymous_solver_and_another_setter_cannot_modify_an_owned_problem()
    {
        await using var factory = AccountTestHost.Factory();
        using var owner = AccountTestHost.Client(factory);
        using var other = AccountTestHost.Client(factory);
        using var anonymous = AccountTestHost.Client(factory);
        var token = TestContext.Current.CancellationToken;
        await AccountTestHost.CsrfAsync(anonymous);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/authoring/problems/", new CreateProblemRequest("Title", "Private"), token)).StatusCode);

        var ownerRegistration = await AccountTestHost.RegisterAsync(owner, AccountTestHost.Email());
        var ownerAccount = (await ownerRegistration.Content.ReadFromJsonAsync<AccountResponse>(token))!;
        await AccountTestHost.SetRoleAsync(factory, ownerAccount.Id, AccountRole.Setter);
        await AccountTestHost.CsrfAsync(owner);
        var created = await owner.PostAsJsonAsync("/api/authoring/problems/", new CreateProblemRequest("Title", "Private"), token);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<ProblemDraftResponse>(token))!.Id;

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync($"/api/authoring/problems/{id}/draft", token)).StatusCode);
        var otherRegistration = await AccountTestHost.RegisterAsync(other, AccountTestHost.Email());
        var otherAccount = (await otherRegistration.Content.ReadFromJsonAsync<AccountResponse>(token))!;
        await AccountTestHost.SetRoleAsync(factory, otherAccount.Id, AccountRole.Setter);
        var denied = await other.GetAsync($"/api/authoring/problems/{id}/draft", token);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.DoesNotContain("Private", await denied.Content.ReadAsStringAsync(token), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/authoring/problems/{id}/draft", token)).StatusCode);
    }

    [Fact]
    public async Task M4_upgrade_preserves_existing_M3_problem_data()
    {
        var token = TestContext.Current.CancellationToken;
        var name = $"minos_m4_upgrade_{Guid.NewGuid():N}";
        await using var bootstrap = new NpgsqlConnection(PostgresPrivilegeTests.Connection("MINOS_TEST_BOOTSTRAP_CONNECTION"));
        await bootstrap.OpenAsync(token);
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {name}", bootstrap)) await create.ExecuteNonQueryAsync(token);
        try
        {
            var bootstrapConfig = new NpgsqlConnectionStringBuilder(PostgresPrivilegeTests.Connection("MINOS_TEST_BOOTSTRAP_CONNECTION"))
                { Database = name, Pooling = false };
            await using (var setup = new NpgsqlConnection(bootstrapConfig.ConnectionString))
            {
                await setup.OpenAsync(token);
                await using var grant = new NpgsqlCommand($"REVOKE ALL ON DATABASE {name} FROM PUBLIC; GRANT CONNECT,CREATE ON DATABASE {name} TO minos_migrator; REVOKE ALL ON SCHEMA public FROM PUBLIC; GRANT USAGE,CREATE ON SCHEMA public TO minos_migrator", setup);
                await grant.ExecuteNonQueryAsync(token);
            }
            var config = new NpgsqlConnectionStringBuilder(PostgresPrivilegeTests.Connection("MINOS_TEST_MIGRATION_CONNECTION"))
                { Database = name, Pooling = false };
            await using var database = PostgresPrivilegeTests.Database(config.ConnectionString);
            await database.GetService<IMigrator>().MigrateAsync("20260914143837_ProtectAccountAndExternalIdentities", token);
            var ownerId = Guid.NewGuid();
            var problemId = Guid.NewGuid();
            await database.Database.ExecuteSqlRawAsync(
                "INSERT INTO accounts.accounts (id,display_name,created_at_utc,is_disabled) VALUES ({0},'Owner',now(),false)", [ownerId], token);
            await database.Database.ExecuteSqlRawAsync(
                "INSERT INTO problems.problem_drafts (id,title,statement,created_at_utc,owner_account_id) VALUES ({0},'Legacy','Preserve me',now(),{1})",
                [problemId, ownerId], token);
            await database.Database.MigrateAsync(token);
            await database.Database.OpenConnectionAsync(token);
            await using var read = database.Database.GetDbConnection().CreateCommand();
            read.CommandText = "SELECT title || ':' || statement FROM problems.problems WHERE id = @id";
            var parameter = read.CreateParameter();
            parameter.ParameterName = "id";
            parameter.Value = problemId;
            read.Parameters.Add(parameter);
            Assert.Equal("Legacy:Preserve me", await read.ExecuteScalarAsync(token));
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", bootstrap);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
