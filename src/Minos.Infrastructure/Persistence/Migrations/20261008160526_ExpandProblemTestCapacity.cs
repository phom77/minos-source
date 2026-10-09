using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Minos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExpandProblemTestCapacity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_published_problem_versions_test_count",
                schema: "problems",
                table: "published_problem_versions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_published_problem_versions_test_count",
                schema: "problems",
                table: "published_problem_versions",
                sql: "private_test_count BETWEEN 1 AND 1500");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_published_problem_versions_test_count",
                schema: "problems",
                table: "published_problem_versions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_published_problem_versions_test_count",
                schema: "problems",
                table: "published_problem_versions",
                sql: "private_test_count BETWEEN 1 AND 100");
        }
    }
}
