using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Minos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M4ProblemPublishing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_problem_drafts_accounts_owner_account_id",
                schema: "problems",
                table: "problem_drafts");
            migrationBuilder.DropCheckConstraint(name: "ck_problem_drafts_statement", schema: "problems", table: "problem_drafts");
            migrationBuilder.DropCheckConstraint(name: "ck_problem_drafts_title", schema: "problems", table: "problem_drafts");
            migrationBuilder.RenameTable(name: "problem_drafts", schema: "problems", newName: "problems", newSchema: "problems");
            migrationBuilder.Sql("ALTER TABLE problems.problems RENAME CONSTRAINT \"PK_problem_drafts\" TO \"PK_problems\"");
            migrationBuilder.RenameIndex(
                name: "IX_problem_drafts_owner_account_id",
                schema: "problems",
                table: "problems",
                newName: "IX_problems_owner_account_id");

            migrationBuilder.AddColumn<int>(
                name: "role",
                schema: "accounts",
                table: "accounts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(name: "time_limit_ms", schema: "problems", table: "problems", type: "integer", nullable: true);
            migrationBuilder.AddColumn<int>(name: "memory_limit_mb", schema: "problems", table: "problems", type: "integer", nullable: true);
            migrationBuilder.AddColumn<int>(name: "checker", schema: "problems", table: "problems", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "latest_published_version", schema: "problems", table: "problems", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<bool>(name: "is_archived", schema: "problems", table: "problems", type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<long>(name: "revision", schema: "problems", table: "problems", type: "bigint", nullable: false, defaultValue: 0L);
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at_utc", schema: "problems", table: "problems",
                type: "timestamp with time zone", nullable: true);
            migrationBuilder.Sql("UPDATE problems.problems SET updated_at_utc = created_at_utc");
            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "updated_at_utc", schema: "problems", table: "problems",
                type: "timestamp with time zone", nullable: false,
                oldClrType: typeof(DateTimeOffset), oldType: "timestamp with time zone", oldNullable: true);

            migrationBuilder.AddCheckConstraint(name: "ck_problems_checker", schema: "problems", table: "problems", sql: "checker = 0");
            migrationBuilder.AddCheckConstraint(name: "ck_problems_memory_limit", schema: "problems", table: "problems", sql: "memory_limit_mb IS NULL OR memory_limit_mb BETWEEN 16 AND 1024");
            migrationBuilder.AddCheckConstraint(name: "ck_problems_revision", schema: "problems", table: "problems", sql: "revision >= 0");
            migrationBuilder.AddCheckConstraint(
                name: "ck_problems_statement", schema: "problems", table: "problems",
                sql: "(length(statement) + length(regexp_replace(statement, U&'[^\\+010000-\\+10FFFF]', '', 'g'))) BETWEEN 1 AND 65536 AND length(btrim(statement, E' \\t\\n\\r\\f\\v\\u0085\\u00a0\\u1680\\u2000\\u2001\\u2002\\u2003\\u2004\\u2005\\u2006\\u2007\\u2008\\u2009\\u200a\\u2028\\u2029\\u202f\\u205f\\u3000')) > 0");
            migrationBuilder.AddCheckConstraint(name: "ck_problems_time_limit", schema: "problems", table: "problems", sql: "time_limit_ms IS NULL OR time_limit_ms BETWEEN 100 AND 10000");
            migrationBuilder.AddCheckConstraint(
                name: "ck_problems_title", schema: "problems", table: "problems",
                sql: "(length(title) + length(regexp_replace(title, U&'[^\\+010000-\\+10FFFF]', '', 'g'))) BETWEEN 1 AND 120 AND length(btrim(title, E' \\t\\n\\r\\f\\v\\u0085\\u00a0\\u1680\\u2000\\u2001\\u2002\\u2003\\u2004\\u2005\\u2006\\u2007\\u2008\\u2009\\u200a\\u2028\\u2029\\u202f\\u205f\\u3000')) > 0 AND title = btrim(title, E' \\t\\n\\r\\f\\v\\u0085\\u00a0\\u1680\\u2000\\u2001\\u2002\\u2003\\u2004\\u2005\\u2006\\u2007\\u2008\\u2009\\u200a\\u2028\\u2029\\u202f\\u205f\\u3000')");
            migrationBuilder.AddCheckConstraint(name: "ck_problems_version", schema: "problems", table: "problems", sql: "latest_published_version >= 0");
            migrationBuilder.AddForeignKey(
                name: "FK_problems_accounts_owner_account_id", schema: "problems", table: "problems",
                column: "owner_account_id", principalSchema: "accounts", principalTable: "accounts",
                principalColumn: "id", onDelete: ReferentialAction.Restrict);

            migrationBuilder.CreateTable(
                name: "problem_samples",
                schema: "problems",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    input = table.Column<string>(type: "character varying(65536)", maxLength: 65536, nullable: false),
                    output = table.Column<string>(type: "character varying(65536)", maxLength: 65536, nullable: false),
                    problem_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_problem_samples", x => x.id);
                    table.CheckConstraint("ck_problem_samples_input", "(length(input) + length(regexp_replace(input, U&'[^\\+010000-\\+10FFFF]', '', 'g'))) <= 65536");
                    table.CheckConstraint("ck_problem_samples_order", " \"order\" > 0 ");
                    table.CheckConstraint("ck_problem_samples_output", "(length(output) + length(regexp_replace(output, U&'[^\\+010000-\\+10FFFF]', '', 'g'))) <= 65536");
                    table.ForeignKey(
                        name: "FK_problem_samples_problems_problem_id",
                        column: x => x.problem_id,
                        principalSchema: "problems",
                        principalTable: "problems",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "problem_test_cases",
                schema: "problems",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    input_artifact_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    input_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    input_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    output_artifact_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    output_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    output_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    problem_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_problem_test_cases", x => x.id);
                    table.CheckConstraint("ck_problem_test_cases_input_artifact", "input_artifact_key ~ '^tests/[0-9a-f]{32}/input[.]bin$' AND input_sha256 ~ '^[0-9a-f]{64}$' AND input_size_bytes BETWEEN 0 AND 8388608");
                    table.CheckConstraint("ck_problem_test_cases_order", " \"order\" > 0 ");
                    table.CheckConstraint("ck_problem_test_cases_output_artifact", "output_artifact_key ~ '^tests/[0-9a-f]{32}/output[.]bin$' AND output_sha256 ~ '^[0-9a-f]{64}$' AND output_size_bytes BETWEEN 0 AND 8388608");
                    table.ForeignKey(
                        name: "FK_problem_test_cases_problems_problem_id",
                        column: x => x.problem_id,
                        principalSchema: "problems",
                        principalTable: "problems",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "published_problem_versions",
                schema: "problems",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    problem_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    statement = table.Column<string>(type: "character varying(65536)", maxLength: 65536, nullable: false),
                    time_limit_ms = table.Column<int>(type: "integer", nullable: false),
                    memory_limit_mb = table.Column<int>(type: "integer", nullable: false),
                    checker = table.Column<int>(type: "integer", nullable: false),
                    private_test_count = table.Column<int>(type: "integer", nullable: false),
                    published_by_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    published_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    statement_reviewed = table.Column<bool>(type: "boolean", nullable: false),
                    samples_verified = table.Column<bool>(type: "boolean", nullable: false),
                    reference_solution_checked = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_published_problem_versions", x => x.id);
                    table.CheckConstraint("ck_published_problem_versions_checker", "checker = 0");
                    table.CheckConstraint("ck_published_problem_versions_checklist", "statement_reviewed AND samples_verified AND reference_solution_checked");
                    table.CheckConstraint("ck_published_problem_versions_memory_limit", "memory_limit_mb BETWEEN 16 AND 1024");
                    table.CheckConstraint("ck_published_problem_versions_number", "version_number > 0");
                    table.CheckConstraint("ck_published_problem_versions_test_count", "private_test_count BETWEEN 1 AND 100");
                    table.CheckConstraint("ck_published_problem_versions_time_limit", "time_limit_ms BETWEEN 100 AND 10000");
                    table.ForeignKey(
                        name: "FK_published_problem_versions_accounts_published_by_account_id",
                        column: x => x.published_by_account_id,
                        principalSchema: "accounts",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_published_problem_versions_problems_problem_id",
                        column: x => x.problem_id,
                        principalSchema: "problems",
                        principalTable: "problems",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "published_problem_samples",
                schema: "problems",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    problem_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    input = table.Column<string>(type: "character varying(65536)", maxLength: 65536, nullable: false),
                    output = table.Column<string>(type: "character varying(65536)", maxLength: 65536, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_published_problem_samples", x => x.id);
                    table.CheckConstraint("ck_published_problem_samples_order", " \"order\" > 0 ");
                    table.ForeignKey(
                        name: "FK_published_problem_samples_published_problem_versions_proble~",
                        column: x => x.problem_version_id,
                        principalSchema: "problems",
                        principalTable: "published_problem_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "published_problem_test_cases",
                schema: "problems",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    problem_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    input_artifact_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    input_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    input_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    output_artifact_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    output_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    output_size_bytes = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_published_problem_test_cases", x => x.id);
                    table.CheckConstraint("ck_published_problem_test_cases_input_artifact", "input_artifact_key ~ '^tests/[0-9a-f]{32}/input[.]bin$' AND input_sha256 ~ '^[0-9a-f]{64}$' AND input_size_bytes BETWEEN 0 AND 8388608");
                    table.CheckConstraint("ck_published_problem_test_cases_order", " \"order\" > 0 ");
                    table.CheckConstraint("ck_published_problem_test_cases_output_artifact", "output_artifact_key ~ '^tests/[0-9a-f]{32}/output[.]bin$' AND output_sha256 ~ '^[0-9a-f]{64}$' AND output_size_bytes BETWEEN 0 AND 8388608");
                    table.ForeignKey(
                        name: "FK_published_problem_test_cases_published_problem_versions_pro~",
                        column: x => x.problem_version_id,
                        principalSchema: "problems",
                        principalTable: "published_problem_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_accounts_role",
                schema: "accounts",
                table: "accounts",
                sql: "role BETWEEN 0 AND 2");

            migrationBuilder.CreateIndex(
                name: "IX_problem_samples_problem_id_order",
                schema: "problems",
                table: "problem_samples",
                columns: new[] { "problem_id", "order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_problem_test_cases_problem_id_order",
                schema: "problems",
                table: "problem_test_cases",
                columns: new[] { "problem_id", "order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_published_problem_samples_problem_version_id_order",
                schema: "problems",
                table: "published_problem_samples",
                columns: new[] { "problem_version_id", "order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_published_problem_test_cases_problem_version_id_order",
                schema: "problems",
                table: "published_problem_test_cases",
                columns: new[] { "problem_version_id", "order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_published_problem_versions_problem_id_version_number",
                schema: "problems",
                table: "published_problem_versions",
                columns: new[] { "problem_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_published_problem_versions_published_by_account_id",
                schema: "problems",
                table: "published_problem_versions",
                column: "published_by_account_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "problem_samples",
                schema: "problems");

            migrationBuilder.DropTable(
                name: "problem_test_cases",
                schema: "problems");

            migrationBuilder.DropTable(
                name: "published_problem_samples",
                schema: "problems");

            migrationBuilder.DropTable(
                name: "published_problem_test_cases",
                schema: "problems");

            migrationBuilder.DropTable(
                name: "published_problem_versions",
                schema: "problems");

            migrationBuilder.DropForeignKey(
                name: "FK_problems_accounts_owner_account_id",
                schema: "problems",
                table: "problems");

            migrationBuilder.DropCheckConstraint(name: "ck_problems_checker", schema: "problems", table: "problems");
            migrationBuilder.DropCheckConstraint(name: "ck_problems_memory_limit", schema: "problems", table: "problems");
            migrationBuilder.DropCheckConstraint(name: "ck_problems_revision", schema: "problems", table: "problems");
            migrationBuilder.DropCheckConstraint(name: "ck_problems_statement", schema: "problems", table: "problems");
            migrationBuilder.DropCheckConstraint(name: "ck_problems_time_limit", schema: "problems", table: "problems");
            migrationBuilder.DropCheckConstraint(name: "ck_problems_title", schema: "problems", table: "problems");
            migrationBuilder.DropCheckConstraint(name: "ck_problems_version", schema: "problems", table: "problems");

            migrationBuilder.DropColumn(name: "checker", schema: "problems", table: "problems");
            migrationBuilder.DropColumn(name: "is_archived", schema: "problems", table: "problems");
            migrationBuilder.DropColumn(name: "latest_published_version", schema: "problems", table: "problems");
            migrationBuilder.DropColumn(name: "memory_limit_mb", schema: "problems", table: "problems");
            migrationBuilder.DropColumn(name: "revision", schema: "problems", table: "problems");
            migrationBuilder.DropColumn(name: "time_limit_ms", schema: "problems", table: "problems");
            migrationBuilder.DropColumn(name: "updated_at_utc", schema: "problems", table: "problems");

            migrationBuilder.Sql("ALTER TABLE problems.problems RENAME CONSTRAINT \"PK_problems\" TO \"PK_problem_drafts\"");
            migrationBuilder.RenameTable(name: "problems", schema: "problems", newName: "problem_drafts", newSchema: "problems");
            migrationBuilder.RenameIndex(
                name: "IX_problems_owner_account_id",
                schema: "problems",
                table: "problem_drafts",
                newName: "IX_problem_drafts_owner_account_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_problem_drafts_statement", schema: "problems", table: "problem_drafts",
                sql: "(length(statement) + length(regexp_replace(statement, U&'[^\\+010000-\\+10FFFF]', '', 'g'))) BETWEEN 1 AND 65536 AND length(btrim(statement, E' \\t\\n\\r\\f\\v\\u0085\\u00a0\\u1680\\u2000\\u2001\\u2002\\u2003\\u2004\\u2005\\u2006\\u2007\\u2008\\u2009\\u200a\\u2028\\u2029\\u202f\\u205f\\u3000')) > 0");
            migrationBuilder.AddCheckConstraint(
                name: "ck_problem_drafts_title", schema: "problems", table: "problem_drafts",
                sql: "(length(title) + length(regexp_replace(title, U&'[^\\+010000-\\+10FFFF]', '', 'g'))) BETWEEN 1 AND 120 AND length(btrim(title, E' \\t\\n\\r\\f\\v\\u0085\\u00a0\\u1680\\u2000\\u2001\\u2002\\u2003\\u2004\\u2005\\u2006\\u2007\\u2008\\u2009\\u200a\\u2028\\u2029\\u202f\\u205f\\u3000')) > 0 AND title = btrim(title, E' \\t\\n\\r\\f\\v\\u0085\\u00a0\\u1680\\u2000\\u2001\\u2002\\u2003\\u2004\\u2005\\u2006\\u2007\\u2008\\u2009\\u200a\\u2028\\u2029\\u202f\\u205f\\u3000')");
            migrationBuilder.AddForeignKey(
                name: "FK_problem_drafts_accounts_owner_account_id", schema: "problems", table: "problem_drafts",
                column: "owner_account_id", principalSchema: "accounts", principalTable: "accounts",
                principalColumn: "id", onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropCheckConstraint(
                name: "ck_accounts_role",
                schema: "accounts",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "role",
                schema: "accounts",
                table: "accounts");

        }
    }
}
