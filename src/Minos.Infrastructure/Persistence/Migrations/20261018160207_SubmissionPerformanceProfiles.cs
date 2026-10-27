using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Minos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SubmissionPerformanceProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "measurement_profile_id",
                schema: "evaluation",
                table: "submission_results",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_submissions_performance_cohort",
                schema: "evaluation",
                table: "submissions",
                columns: new[] { "problem_version_id", "language", "toolchain_id", "owner_account_id" },
                filter: "status = 2");

            migrationBuilder.AddCheckConstraint(
                name: "ck_submission_results_measurement_profile",
                schema: "evaluation",
                table: "submission_results",
                sql: "measurement_profile_id IS NULL OR measurement_profile_id ~ '^[0-9a-f]{64}$'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_submissions_performance_cohort",
                schema: "evaluation",
                table: "submissions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_submission_results_measurement_profile",
                schema: "evaluation",
                table: "submission_results");

            migrationBuilder.DropColumn(
                name: "measurement_profile_id",
                schema: "evaluation",
                table: "submission_results");

        }
    }
}
