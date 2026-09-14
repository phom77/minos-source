using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Minos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProtectDraftText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_problem_drafts_statement",
                schema: "problems",
                table: "problem_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_problem_drafts_title",
                schema: "problems",
                table: "problem_drafts");

            migrationBuilder.AddCheckConstraint(
                name: "ck_problem_drafts_statement",
                schema: "problems",
                table: "problem_drafts",
                sql: "length(statement) BETWEEN 1 AND 65536 AND length(btrim(statement, E' \\t\\n\\r\\f\\v\\u0085\\u00a0\\u1680\\u2000\\u2001\\u2002\\u2003\\u2004\\u2005\\u2006\\u2007\\u2008\\u2009\\u200a\\u2028\\u2029\\u202f\\u205f\\u3000')) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_problem_drafts_title",
                schema: "problems",
                table: "problem_drafts",
                sql: "length(title) BETWEEN 1 AND 120 AND length(btrim(title, E' \\t\\n\\r\\f\\v\\u0085\\u00a0\\u1680\\u2000\\u2001\\u2002\\u2003\\u2004\\u2005\\u2006\\u2007\\u2008\\u2009\\u200a\\u2028\\u2029\\u202f\\u205f\\u3000')) > 0 AND title = btrim(title, E' \\t\\n\\r\\f\\v\\u0085\\u00a0\\u1680\\u2000\\u2001\\u2002\\u2003\\u2004\\u2005\\u2006\\u2007\\u2008\\u2009\\u200a\\u2028\\u2029\\u202f\\u205f\\u3000')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_problem_drafts_statement",
                schema: "problems",
                table: "problem_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_problem_drafts_title",
                schema: "problems",
                table: "problem_drafts");

            migrationBuilder.AddCheckConstraint(
                name: "ck_problem_drafts_statement",
                schema: "problems",
                table: "problem_drafts",
                sql: "length(statement) BETWEEN 1 AND 65536");

            migrationBuilder.AddCheckConstraint(
                name: "ck_problem_drafts_title",
                schema: "problems",
                table: "problem_drafts",
                sql: "length(btrim(title)) BETWEEN 1 AND 120");
        }
    }
}
