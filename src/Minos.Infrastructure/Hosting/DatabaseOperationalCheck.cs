using Microsoft.EntityFrameworkCore;
using Minos.Infrastructure.Persistence;
using Npgsql;

namespace Minos.Infrastructure.Hosting;

public enum DatabaseActor { Api, Worker, Migrator }

// Infrastructure-specific host readiness, not a business use case.
public sealed class DatabaseOperationalCheck(MinosDbContext database)
{
    public async Task ValidateAsync(DatabaseActor actor, CancellationToken cancellationToken)
    {
        var connection = database.Database.GetDbConnection();
        await database.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT current_user = @expected
                    AND NOT (rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls)
                    AND NOT EXISTS (SELECT 1 FROM pg_auth_members WHERE member = r.oid)
                    AND NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = current_database() AND datdba = r.oid)
                FROM pg_roles r WHERE rolname = current_user
                """;
            command.Parameters.Add(new NpgsqlParameter("expected", actor switch
            {
                DatabaseActor.Api => "minos_api", DatabaseActor.Worker => "minos_worker", _ => "minos_migrator"
            }));
            if (await command.ExecuteScalarAsync(cancellationToken) is not true)
                throw new InvalidOperationException("PostgreSQL operational identity is missing or excessively privileged.");
            if (actor == DatabaseActor.Migrator) return;

            command.Parameters.Clear();
            command.CommandText = """
                SELECT NOT has_database_privilege(current_database(), 'CREATE,TEMP')
                    AND NOT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname NOT LIKE 'pg_temp_%'
                        AND nspname NOT LIKE 'pg_toast_temp_%' AND has_schema_privilege(oid, 'CREATE'))
                    AND has_table_privilege('problems.problem_drafts', 'SELECT')
                    AND has_table_privilege('problems.problem_drafts', 'INSERT') = @can_insert
                    AND NOT has_table_privilege('problems.problem_drafts', 'UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                    AND has_table_privilege('public."__EFMigrationsHistory"', 'SELECT')
                    AND NOT has_table_privilege('public."__EFMigrationsHistory"', 'INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                """;
            command.Parameters.Add(new NpgsqlParameter("can_insert", actor == DatabaseActor.Api));
            if (await command.ExecuteScalarAsync(cancellationToken) is not true)
                throw new InvalidOperationException("PostgreSQL runtime grants do not match the M2 least-privilege baseline.");
            if ((await database.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
                throw new InvalidOperationException("Apply database migrations before starting Minos.");
        }
        finally { await database.Database.CloseConnectionAsync(); }
    }
}
