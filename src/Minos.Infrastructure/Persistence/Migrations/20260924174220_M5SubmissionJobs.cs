using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Minos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M5SubmissionJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "evaluation");

            migrationBuilder.CreateTable(
                name: "judge_jobs",
                schema: "evaluation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    available_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    active_attempt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    active_lease_token = table.Column<Guid>(type: "uuid", nullable: true),
                    active_worker_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    lease_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_attempt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_judge_jobs", x => x.id);
                    table.CheckConstraint("ck_judge_jobs_attempt_count", "attempt_count BETWEEN 0 AND 3");
                    table.CheckConstraint("ck_judge_jobs_lease_state", "(status = 0 AND active_attempt_id IS NULL AND active_lease_token IS NULL AND active_worker_id IS NULL AND lease_expires_at_utc IS NULL AND completed_attempt_id IS NULL) OR (status = 1 AND active_attempt_id IS NOT NULL AND active_lease_token IS NOT NULL AND active_worker_id IS NOT NULL AND lease_expires_at_utc IS NOT NULL AND completed_attempt_id IS NULL) OR (status IN (2,3) AND active_attempt_id IS NULL AND active_lease_token IS NULL AND active_worker_id IS NULL AND lease_expires_at_utc IS NULL AND completed_attempt_id IS NOT NULL)");
                    table.CheckConstraint("ck_judge_jobs_status", "status BETWEEN 0 AND 3");
                });

            migrationBuilder.CreateTable(
                name: "judging_attempts",
                schema: "evaluation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    judge_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_number = table.Column<int>(type: "integer", nullable: false),
                    lease_token = table.Column<Guid>(type: "uuid", nullable: false),
                    worker_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    failure_kind = table.Column<int>(type: "integer", nullable: true),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    lease_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_judging_attempts", x => x.id);
                    table.CheckConstraint("ck_judging_attempts_failure", "(status = 0 AND failure_kind IS NULL AND finished_at_utc IS NULL) OR (status = 1 AND failure_kind IS NULL AND finished_at_utc IS NOT NULL) OR (status IN (2,3) AND failure_kind IS NOT NULL AND finished_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_judging_attempts_failure_kind", "failure_kind IS NULL OR failure_kind BETWEEN 0 AND 2");
                    table.CheckConstraint("ck_judging_attempts_lease", "lease_expires_at_utc > started_at_utc");
                    table.CheckConstraint("ck_judging_attempts_number", "attempt_number BETWEEN 1 AND 3");
                    table.CheckConstraint("ck_judging_attempts_status", "status BETWEEN 0 AND 3");
                    table.ForeignKey(
                        name: "FK_judging_attempts_judge_jobs_judge_job_id",
                        column: x => x.judge_job_id,
                        principalSchema: "evaluation",
                        principalTable: "judge_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "submissions",
                schema: "evaluation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    problem_id = table.Column<Guid>(type: "uuid", nullable: false),
                    problem_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    language = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    toolchain_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    current_attempt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    completed_attempt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_submissions", x => x.id);
                    table.CheckConstraint("ck_submissions_attempt_state", "(status = 1) = (current_attempt_id IS NOT NULL)");
                    table.CheckConstraint("ck_submissions_completion", "((status IN (0,1)) AND completed_attempt_id IS NULL AND completed_at_utc IS NULL) OR ((status IN (2,3)) AND completed_attempt_id IS NOT NULL AND completed_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_submissions_language", "language = 'cpp20'");
                    table.CheckConstraint("ck_submissions_source", "octet_length(source) BETWEEN 1 AND 65536 AND length(btrim(source, E' \\t\\n\\r\\f\\v\\u0085\\u00a0\\u1680\\u2000\\u2001\\u2002\\u2003\\u2004\\u2005\\u2006\\u2007\\u2008\\u2009\\u200a\\u2028\\u2029\\u202f\\u205f\\u3000')) > 0");
                    table.CheckConstraint("ck_submissions_status", "status BETWEEN 0 AND 3");
                    table.CheckConstraint("ck_submissions_toolchain", "toolchain_id = 'gcc-14.3.0-cpp20-o2-pipe@sha256:5e927c284bf55a7dc796262e311a0703344f62f41f5621eb56843111b1d37e15'");
                    table.ForeignKey(
                        name: "FK_submissions_accounts_owner_account_id",
                        column: x => x.owner_account_id,
                        principalSchema: "accounts",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_submissions_judging_attempts_completed_attempt_id",
                        column: x => x.completed_attempt_id,
                        principalSchema: "evaluation",
                        principalTable: "judging_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_submissions_judging_attempts_current_attempt_id",
                        column: x => x.current_attempt_id,
                        principalSchema: "evaluation",
                        principalTable: "judging_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_submissions_problems_problem_id",
                        column: x => x.problem_id,
                        principalSchema: "problems",
                        principalTable: "problems",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_submissions_published_problem_versions_problem_version_id",
                        column: x => x.problem_version_id,
                        principalSchema: "problems",
                        principalTable: "published_problem_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "submission_results",
                schema: "evaluation",
                columns: table => new
                {
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_submission_results", x => x.submission_id);
                    table.ForeignKey(
                        name: "FK_submission_results_judging_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalSchema: "evaluation",
                        principalTable: "judging_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_submission_results_submissions_submission_id",
                        column: x => x.submission_id,
                        principalSchema: "evaluation",
                        principalTable: "submissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_judge_jobs_active_attempt_id",
                schema: "evaluation",
                table: "judge_jobs",
                column: "active_attempt_id");

            migrationBuilder.CreateIndex(
                name: "IX_judge_jobs_completed_attempt_id",
                schema: "evaluation",
                table: "judge_jobs",
                column: "completed_attempt_id");

            migrationBuilder.CreateIndex(
                name: "IX_judge_jobs_status_available_at_utc",
                schema: "evaluation",
                table: "judge_jobs",
                columns: new[] { "status", "available_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_judge_jobs_status_lease_expires_at_utc",
                schema: "evaluation",
                table: "judge_jobs",
                columns: new[] { "status", "lease_expires_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_judge_jobs_submission_id",
                schema: "evaluation",
                table: "judge_jobs",
                column: "submission_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_judging_attempts_judge_job_id_attempt_number",
                schema: "evaluation",
                table: "judging_attempts",
                columns: new[] { "judge_job_id", "attempt_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_judging_attempts_lease_token",
                schema: "evaluation",
                table: "judging_attempts",
                column: "lease_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_judging_attempts_submission_id_attempt_number",
                schema: "evaluation",
                table: "judging_attempts",
                columns: new[] { "submission_id", "attempt_number" });

            migrationBuilder.CreateIndex(
                name: "IX_submission_results_attempt_id",
                schema: "evaluation",
                table: "submission_results",
                column: "attempt_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_submissions_completed_attempt_id",
                schema: "evaluation",
                table: "submissions",
                column: "completed_attempt_id");

            migrationBuilder.CreateIndex(
                name: "IX_submissions_current_attempt_id",
                schema: "evaluation",
                table: "submissions",
                column: "current_attempt_id");

            migrationBuilder.CreateIndex(
                name: "IX_submissions_owner_account_id_created_at_utc",
                schema: "evaluation",
                table: "submissions",
                columns: new[] { "owner_account_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_submissions_owner_account_id_status",
                schema: "evaluation",
                table: "submissions",
                columns: new[] { "owner_account_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_submissions_problem_id",
                schema: "evaluation",
                table: "submissions",
                column: "problem_id");

            migrationBuilder.CreateIndex(
                name: "IX_submissions_problem_version_id",
                schema: "evaluation",
                table: "submissions",
                column: "problem_version_id");

            migrationBuilder.AddForeignKey(
                name: "FK_judge_jobs_judging_attempts_active_attempt_id",
                schema: "evaluation",
                table: "judge_jobs",
                column: "active_attempt_id",
                principalSchema: "evaluation",
                principalTable: "judging_attempts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_judge_jobs_judging_attempts_completed_attempt_id",
                schema: "evaluation",
                table: "judge_jobs",
                column: "completed_attempt_id",
                principalSchema: "evaluation",
                principalTable: "judging_attempts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_judge_jobs_submissions_submission_id",
                schema: "evaluation",
                table: "judge_jobs",
                column: "submission_id",
                principalSchema: "evaluation",
                principalTable: "submissions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_judging_attempts_submissions_submission_id",
                schema: "evaluation",
                table: "judging_attempts",
                column: "submission_id",
                principalSchema: "evaluation",
                principalTable: "submissions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_judge_jobs_judging_attempts_active_attempt_id",
                schema: "evaluation",
                table: "judge_jobs");

            migrationBuilder.DropForeignKey(
                name: "FK_judge_jobs_judging_attempts_completed_attempt_id",
                schema: "evaluation",
                table: "judge_jobs");

            migrationBuilder.DropForeignKey(
                name: "FK_submissions_judging_attempts_completed_attempt_id",
                schema: "evaluation",
                table: "submissions");

            migrationBuilder.DropForeignKey(
                name: "FK_submissions_judging_attempts_current_attempt_id",
                schema: "evaluation",
                table: "submissions");

            migrationBuilder.DropTable(
                name: "submission_results",
                schema: "evaluation");

            migrationBuilder.DropTable(
                name: "judging_attempts",
                schema: "evaluation");

            migrationBuilder.DropTable(
                name: "judge_jobs",
                schema: "evaluation");

            migrationBuilder.DropTable(
                name: "submissions",
                schema: "evaluation");
        }
    }
}
