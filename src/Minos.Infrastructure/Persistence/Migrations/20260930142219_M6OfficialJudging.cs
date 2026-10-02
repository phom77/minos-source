using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Minos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M6OfficialJudging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "compiler_diagnostics",
                schema: "evaluation",
                table: "submission_results",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "maximum_cpu_time_ms",
                schema: "evaluation",
                table: "submission_results",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "maximum_memory_bytes",
                schema: "evaluation",
                table: "submission_results",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "maximum_wall_time_ms",
                schema: "evaluation",
                table: "submission_results",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "verdict",
                schema: "evaluation",
                table: "submission_results",
                type: "integer",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_submission_results_submission_id_attempt_id",
                schema: "evaluation",
                table: "submission_results",
                columns: new[] { "submission_id", "attempt_id" });

            migrationBuilder.CreateTable(
                name: "test_case_results",
                schema: "evaluation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    test_order = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    cpu_time_ms = table.Column<long>(type: "bigint", nullable: true),
                    wall_time_ms = table.Column<long>(type: "bigint", nullable: true),
                    memory_peak_bytes = table.Column<long>(type: "bigint", nullable: true),
                    exit_code = table.Column<int>(type: "integer", nullable: true),
                    wall_timeout = table.Column<bool>(type: "boolean", nullable: false),
                    cpu_limit = table.Column<bool>(type: "boolean", nullable: false),
                    memory_failure = table.Column<bool>(type: "boolean", nullable: false),
                    output_limit = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_test_case_results", x => x.id);
                    table.CheckConstraint("ck_test_case_results_execution", "(status = 0 AND exit_code IS NULL AND NOT wall_timeout AND NOT cpu_limit AND NOT memory_failure AND NOT output_limit) OR (status <> 0 AND exit_code IS NOT NULL)");
                    table.CheckConstraint("ck_test_case_results_measurements", "(status = 0 AND cpu_time_ms IS NULL AND wall_time_ms IS NULL AND memory_peak_bytes IS NULL) OR (status <> 0 AND cpu_time_ms IS NOT NULL AND cpu_time_ms >= 0 AND wall_time_ms IS NOT NULL AND wall_time_ms >= 0 AND memory_peak_bytes IS NOT NULL AND memory_peak_bytes >= 0)");
                    table.CheckConstraint("ck_test_case_results_order", "test_order > 0");
                    table.CheckConstraint("ck_test_case_results_status", "status BETWEEN 0 AND 6");
                    table.ForeignKey(
                        name: "FK_test_case_results_judging_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalSchema: "evaluation",
                        principalTable: "judging_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_test_case_results_submission_results_submission_id_attempt_~",
                        columns: x => new { x.submission_id, x.attempt_id },
                        principalSchema: "evaluation",
                        principalTable: "submission_results",
                        principalColumns: new[] { "submission_id", "attempt_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_submission_results_diagnostics",
                schema: "evaluation",
                table: "submission_results",
                sql: "octet_length(compiler_diagnostics) <= 32768");

            migrationBuilder.AddCheckConstraint(
                name: "ck_submission_results_summary",
                schema: "evaluation",
                table: "submission_results",
                sql: "(verdict IS NULL AND maximum_cpu_time_ms IS NULL AND maximum_wall_time_ms IS NULL AND maximum_memory_bytes IS NULL AND compiler_diagnostics = '') OR (verdict = 2 AND maximum_cpu_time_ms IS NULL AND maximum_wall_time_ms IS NULL AND maximum_memory_bytes IS NULL) OR (verdict IS NOT NULL AND verdict <> 2 AND maximum_cpu_time_ms IS NOT NULL AND maximum_cpu_time_ms >= 0 AND maximum_wall_time_ms IS NOT NULL AND maximum_wall_time_ms >= 0 AND maximum_memory_bytes IS NOT NULL AND maximum_memory_bytes >= 0 AND compiler_diagnostics = '')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_submission_results_verdict",
                schema: "evaluation",
                table: "submission_results",
                sql: "verdict IS NULL OR verdict BETWEEN 0 AND 6");

            migrationBuilder.CreateIndex(
                name: "IX_test_case_results_attempt_id",
                schema: "evaluation",
                table: "test_case_results",
                column: "attempt_id");

            migrationBuilder.CreateIndex(
                name: "IX_test_case_results_submission_id_attempt_id",
                schema: "evaluation",
                table: "test_case_results",
                columns: new[] { "submission_id", "attempt_id" });

            migrationBuilder.CreateIndex(
                name: "IX_test_case_results_submission_id_test_order",
                schema: "evaluation",
                table: "test_case_results",
                columns: new[] { "submission_id", "test_order" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "test_case_results",
                schema: "evaluation");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_submission_results_submission_id_attempt_id",
                schema: "evaluation",
                table: "submission_results");

            migrationBuilder.DropCheckConstraint(
                name: "ck_submission_results_diagnostics",
                schema: "evaluation",
                table: "submission_results");

            migrationBuilder.DropCheckConstraint(
                name: "ck_submission_results_summary",
                schema: "evaluation",
                table: "submission_results");

            migrationBuilder.DropCheckConstraint(
                name: "ck_submission_results_verdict",
                schema: "evaluation",
                table: "submission_results");

            migrationBuilder.DropColumn(
                name: "compiler_diagnostics",
                schema: "evaluation",
                table: "submission_results");

            migrationBuilder.DropColumn(
                name: "maximum_cpu_time_ms",
                schema: "evaluation",
                table: "submission_results");

            migrationBuilder.DropColumn(
                name: "maximum_memory_bytes",
                schema: "evaluation",
                table: "submission_results");

            migrationBuilder.DropColumn(
                name: "maximum_wall_time_ms",
                schema: "evaluation",
                table: "submission_results");

            migrationBuilder.DropColumn(
                name: "verdict",
                schema: "evaluation",
                table: "submission_results");
        }
    }
}
