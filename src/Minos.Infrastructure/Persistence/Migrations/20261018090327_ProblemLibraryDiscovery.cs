using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Minos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProblemLibraryDiscovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence<int>(
                name: "problem_public_number_seq",
                schema: "problems");

            migrationBuilder.AddColumn<int>(
                name: "public_number",
                schema: "problems",
                table: "problems",
                type: "integer",
                nullable: false,
                defaultValueSql: "nextval('problems.problem_public_number_seq')");

            // Assign existing problems in a reproducible order before installing uniqueness.
            // Subsequent allocation uses the sequence; numbers are never renumbered on edits.
            migrationBuilder.Sql("""
                WITH numbered AS (
                    SELECT id, row_number() OVER (ORDER BY created_at_utc, id)::integer AS number
                    FROM problems.problems
                )
                UPDATE problems.problems p SET public_number = n.number FROM numbered n WHERE n.id = p.id;
                SELECT setval('problems.problem_public_number_seq',
                    COALESCE((SELECT max(public_number) FROM problems.problems), 1),
                    EXISTS(SELECT 1 FROM problems.problems));
                """);

            migrationBuilder.CreateIndex(
                name: "IX_submissions_owner_account_id_problem_id",
                schema: "evaluation",
                table: "submissions",
                columns: new[] { "owner_account_id", "problem_id" });

            migrationBuilder.CreateIndex(
                name: "IX_problems_public_number",
                schema: "problems",
                table: "problems",
                column: "public_number",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_problems_public_number",
                schema: "problems",
                table: "problems",
                sql: "public_number > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_submissions_owner_account_id_problem_id",
                schema: "evaluation",
                table: "submissions");

            migrationBuilder.DropIndex(
                name: "IX_problems_public_number",
                schema: "problems",
                table: "problems");

            migrationBuilder.DropCheckConstraint(
                name: "ck_problems_public_number",
                schema: "problems",
                table: "problems");

            migrationBuilder.DropColumn(
                name: "public_number",
                schema: "problems",
                table: "problems");

            migrationBuilder.DropSequence(
                name: "problem_public_number_seq",
                schema: "problems");
        }
    }
}
