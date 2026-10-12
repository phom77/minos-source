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
                WITH account_tables AS (
                    SELECT c.oid, c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = 'accounts' AND c.relname IN ('accounts','credentials','external_logins','claims','tokens')
                ), problem_tables AS (
                    SELECT c.oid, c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = 'problems' AND c.relname IN (
                        'problems','problem_samples','problem_test_cases','published_problem_versions',
                        'published_problem_samples','published_problem_test_cases')
                ), contest_tables AS (
                    SELECT c.oid, c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = 'contests' AND c.relname IN ('contests','contest_problems','registrations','submissions')
                ), evaluation_tables AS (
                    SELECT c.oid, c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = 'evaluation' AND c.relname IN (
                        'submissions','judge_jobs','judging_attempts','submission_results','test_case_results','submission_requests')
                )
                SELECT NOT has_database_privilege(current_database(), 'CREATE,TEMP')
                    AND NOT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname NOT LIKE 'pg_temp_%'
                        AND nspname NOT LIKE 'pg_toast_temp_%' AND has_schema_privilege(oid, 'CREATE'))
                    AND (SELECT count(*) = 6 AND bool_and(
                        has_table_privilege(oid, 'SELECT') = (@is_api OR relname IN ('problems','published_problem_versions','published_problem_test_cases'))
                        AND has_table_privilege(oid, 'INSERT') = @is_api
                        AND has_table_privilege(oid, 'UPDATE') = (@is_api AND relname IN ('problems','problem_samples','problem_test_cases'))
                        AND has_table_privilege(oid, 'DELETE') = (@is_api AND relname IN ('problem_samples','problem_test_cases'))
                        AND NOT has_table_privilege(oid, 'TRUNCATE,REFERENCES,TRIGGER')
                    ) FROM problem_tables)
                    AND has_schema_privilege('contests', 'USAGE') = @is_api
                    AND NOT has_schema_privilege('contests', 'CREATE')
                    AND (SELECT count(*) = 4 AND bool_and(
                        has_table_privilege(oid, 'SELECT') = @is_api
                        AND has_table_privilege(oid, 'INSERT') = @is_api
                        AND has_table_privilege(oid, 'UPDATE') = (@is_api AND relname IN ('contests','contest_problems'))
                        AND has_table_privilege(oid, 'DELETE') = (@is_api AND relname = 'contest_problems')
                        AND NOT has_table_privilege(oid, 'TRUNCATE,REFERENCES,TRIGGER')
                    ) FROM contest_tables)
                    AND has_schema_privilege('evaluation', 'USAGE')
                    AND NOT has_schema_privilege('evaluation', 'CREATE')
                    AND (SELECT count(*) = 6 AND bool_and(
                        has_table_privilege(oid, 'SELECT') = CASE WHEN @is_api
                            THEN relname IN ('submissions','judge_jobs','judging_attempts','submission_results','test_case_results','submission_requests')
                            ELSE relname IN ('submissions','judge_jobs','judging_attempts') END
                        AND has_table_privilege(oid, 'INSERT') = CASE WHEN @is_api
                            THEN relname IN ('submissions','judge_jobs','submission_requests')
                            ELSE relname IN ('judging_attempts','submission_results','test_case_results') END
                        AND has_table_privilege(oid, 'UPDATE') = (NOT @is_api AND relname IN ('submissions','judge_jobs','judging_attempts'))
                        AND NOT has_table_privilege(oid, 'DELETE,TRUNCATE,REFERENCES,TRIGGER')
                    ) FROM evaluation_tables)
                    AND has_table_privilege('public."__EFMigrationsHistory"', 'SELECT')
                    AND NOT has_table_privilege('public."__EFMigrationsHistory"', 'INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                    AND has_schema_privilege('accounts', 'USAGE') = @is_api
                    AND NOT has_schema_privilege('accounts', 'CREATE')
                    AND (SELECT count(*) = 5 AND bool_and(
                        has_table_privilege(oid, 'SELECT') = (@is_api AND relname <> 'tokens')
                        AND has_table_privilege(oid, 'INSERT') = (@is_api AND relname IN ('accounts','credentials','external_logins'))
                        AND has_table_privilege(oid, 'UPDATE') = (@is_api AND relname IN ('accounts','credentials'))
                        AND NOT has_table_privilege(oid, 'DELETE,TRUNCATE,REFERENCES,TRIGGER')
                    ) FROM account_tables)
                """;
            command.Parameters.Add(new NpgsqlParameter("is_api", actor == DatabaseActor.Api));
            if (await command.ExecuteScalarAsync(cancellationToken) is not true)
                throw new InvalidOperationException("PostgreSQL runtime grants do not match the current least-privilege baseline.");
            if ((await database.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
                throw new InvalidOperationException("Apply database migrations before starting Minos.");
        }
        finally { await database.Database.CloseConnectionAsync(); }
    }
}
