using Microsoft.EntityFrameworkCore;
using Minos.Infrastructure.Hosting;
using Minos.Infrastructure.Persistence;
using Npgsql;
using Xunit;

namespace Minos.IntegrationTests;

public sealed class PostgresPrivilegeTests
{
    internal static string Connection(string variable) => Environment.GetEnvironmentVariable(variable)
        ?? throw new InvalidOperationException($"Run scripts/check-foundation.sh; {variable} is required.");

    [Theory]
    [InlineData("ConnectionStrings__Minos", DatabaseActor.Api)]
    [InlineData("MINOS_TEST_WORKER_CONNECTION", DatabaseActor.Worker)]
    [InlineData("MINOS_TEST_MIGRATION_CONNECTION", DatabaseActor.Migrator)]
    public async Task Operational_roles_have_expected_identity_and_schema_is_current(string variable, DatabaseActor actor)
    {
        await using var database = Database(Connection(variable));
        var token = TestContext.Current.CancellationToken;
        await new DatabaseOperationalCheck(database).ValidateAsync(actor, token);
        Assert.Empty(await database.Database.GetPendingMigrationsAsync(token));
        Assert.Equal(database.Database.GetMigrations(), await database.Database.GetAppliedMigrationsAsync(token));
        if (actor != DatabaseActor.Migrator) Assert.True(await database.Problems.CountAsync(token) >= 0);
    }

    [Theory]
    [InlineData("ConnectionStrings__Minos", "CREATE SCHEMA forbidden_schema")]
    [InlineData("ConnectionStrings__Minos", "CREATE TEMP TABLE forbidden_table (id integer)")]
    [InlineData("MINOS_TEST_WORKER_CONNECTION", "CREATE SCHEMA forbidden_schema")]
    [InlineData("MINOS_TEST_WORKER_CONNECTION", "CREATE TEMP TABLE forbidden_table (id integer)")]
    [InlineData("ConnectionStrings__Minos", "CREATE TABLE public.forbidden_table (id integer)")]
    [InlineData("MINOS_TEST_WORKER_CONNECTION", "CREATE TABLE problems.forbidden_table (id integer)")]
    [InlineData("ConnectionStrings__Minos", "CREATE ROLE forbidden_role")]
    [InlineData("MINOS_TEST_WORKER_CONNECTION", "CREATE ROLE forbidden_role")]
    [InlineData("ConnectionStrings__Minos", "DELETE FROM problems.problems")]
    [InlineData("ConnectionStrings__Minos", "UPDATE problems.published_problem_versions SET title = 'Changed'")]
    [InlineData("ConnectionStrings__Minos", "DELETE FROM problems.published_problem_versions")]
    [InlineData("MINOS_TEST_WORKER_CONNECTION", "UPDATE problems.problems SET title = 'Changed'")]
    [InlineData("MINOS_TEST_WORKER_CONNECTION", "SELECT * FROM problems.problem_test_cases")]
    [InlineData("MINOS_TEST_WORKER_CONNECTION", "SELECT * FROM problems.published_problem_samples")]
    [InlineData("ConnectionStrings__Minos", "TRUNCATE problems.problems")]
    [InlineData("MINOS_TEST_WORKER_CONNECTION", "INSERT INTO problems.problems (id,owner_account_id,title,statement,created_at_utc,updated_at_utc) VALUES (gen_random_uuid(),gen_random_uuid(),'Title','Statement',now(),now())")]
    [InlineData("ConnectionStrings__Minos", "DELETE FROM public.\"__EFMigrationsHistory\"")]
    [InlineData("MINOS_TEST_WORKER_CONNECTION", "DELETE FROM public.\"__EFMigrationsHistory\"")]
    [InlineData("MINOS_TEST_MIGRATION_CONNECTION", "CREATE ROLE forbidden_role")]
    [InlineData("MINOS_TEST_WORKER_CONNECTION", "SELECT * FROM accounts.credentials")]
    [InlineData("MINOS_TEST_WORKER_CONNECTION", "SELECT * FROM accounts.accounts")]
    [InlineData("MINOS_TEST_WORKER_CONNECTION", "SELECT * FROM accounts.external_logins")]
    [InlineData("ConnectionStrings__Minos", "SELECT * FROM accounts.tokens")]
    [InlineData("ConnectionStrings__Minos", "DELETE FROM accounts.credentials")]
    [InlineData("ConnectionStrings__Minos", "UPDATE evaluation.judge_jobs SET status = 2")]
    [InlineData("ConnectionStrings__Minos", "INSERT INTO evaluation.submission_results (submission_id) VALUES (gen_random_uuid())")]
    [InlineData("ConnectionStrings__Minos", "UPDATE evaluation.submissions SET status = 3")]
    [InlineData("MINOS_TEST_WORKER_CONNECTION", "INSERT INTO evaluation.submissions (id) VALUES (gen_random_uuid())")]
    [InlineData("MINOS_TEST_WORKER_CONNECTION", "DELETE FROM evaluation.judging_attempts")]
    public async Task PostgreSQL_denies_operations_outside_each_actors_responsibility(string variable, string sql)
    {
        await using var connection = new NpgsqlConnection(Connection(variable));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        var failure = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, failure.SqlState);
    }

    [Theory]
    [InlineData(" ", "Statement")]
    [InlineData("\t\n", "Statement")]
    [InlineData("\u00a0\u2003", "Statement")]
    [InlineData(" Title ", "Statement")]
    [InlineData("Title", "\t\n")]
    [InlineData("Title", "\u0085\u3000")]
    public async Task PostgreSQL_rejects_invalid_text_even_when_domain_is_bypassed(string title, string statement)
    {
        await using var connection = new NpgsqlConnection(Connection("ConnectionStrings__Minos"));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        var owner = await SeedOwnerAsync(connection, transaction);
        await using var command = new NpgsqlCommand("INSERT INTO problems.problems (id,title,statement,created_at_utc,updated_at_utc,owner_account_id) VALUES (@id,@title,@statement,@created,@created,@owner)", connection, transaction);
        command.Parameters.AddWithValue("owner", owner);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("title", title);
        command.Parameters.AddWithValue("statement", statement);
        command.Parameters.AddWithValue("created", DateTimeOffset.UtcNow);
        var failure = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
    }

    [Fact]
    public async Task Runtime_validation_rejects_bootstrap_identity_and_detects_grant_drift()
    {
        var token = TestContext.Current.CancellationToken;
        await using var bootstrap = Database(Connection("MINOS_TEST_BOOTSTRAP_CONNECTION"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new DatabaseOperationalCheck(bootstrap).ValidateAsync(DatabaseActor.Api, token));
        await using var runtime = Database(Connection("ConnectionStrings__Minos"));
        await bootstrap.Database.ExecuteSqlRawAsync("GRANT DELETE ON problems.problems TO minos_api", token);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => new DatabaseOperationalCheck(runtime).ValidateAsync(DatabaseActor.Api, token));
        }
        finally { await bootstrap.Database.ExecuteSqlRawAsync("REVOKE DELETE ON problems.problems FROM minos_api", CancellationToken.None); }
        await new DatabaseOperationalCheck(runtime).ValidateAsync(DatabaseActor.Api, token);
    }

    internal static MinosDbContext Database(string connection) => new(new DbContextOptionsBuilder<MinosDbContext>().UseNpgsql(connection).Options);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PostgreSQL_matches_domain_utf16_limits_for_non_bmp_text(bool titleLimit)
    {
        var token = TestContext.Current.CancellationToken;
        await using var connection = new NpgsqlConnection(Connection("ConnectionStrings__Minos"));
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        var owner = await SeedOwnerAsync(connection, transaction);
        await using var command = new NpgsqlCommand("INSERT INTO problems.problems (id,title,statement,created_at_utc,updated_at_utc,owner_account_id) VALUES (@id,@title,@statement,@created,@created,@owner)", connection, transaction);
        command.Parameters.AddWithValue("owner", owner);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("title", titleLimit ? string.Concat(Enumerable.Repeat("🧩", 60)) : "Title");
        command.Parameters.AddWithValue("statement", titleLimit ? "Statement" : string.Concat(Enumerable.Repeat("🧩", 32768)));
        command.Parameters.AddWithValue("created", DateTimeOffset.UtcNow);
        Assert.Equal(1, await command.ExecuteNonQueryAsync(token));
        command.Parameters["id"].Value = Guid.NewGuid();
        command.Parameters[titleLimit ? "title" : "statement"].Value += "🧩";
        var failure = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
        Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
        await transaction.RollbackAsync(token);
    }

    private static async Task<Guid> SeedOwnerAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        var id = Guid.NewGuid();
        await using var command = new NpgsqlCommand("INSERT INTO accounts.accounts (id,display_name,created_at_utc,is_disabled,role) VALUES (@id,'Owner',now(),false,0)", connection, transaction);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return id;
    }
}
