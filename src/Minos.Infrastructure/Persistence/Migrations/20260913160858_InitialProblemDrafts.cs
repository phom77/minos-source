using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Minos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialProblemDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "problems");

            migrationBuilder.CreateTable(
                name: "problem_drafts",
                schema: "problems",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    statement = table.Column<string>(type: "character varying(65536)", maxLength: 65536, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_problem_drafts", x => x.id);
                    table.CheckConstraint("ck_problem_drafts_statement", "length(statement) BETWEEN 1 AND 65536");
                    table.CheckConstraint("ck_problem_drafts_title", "length(btrim(title)) BETWEEN 1 AND 120");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "problem_drafts",
                schema: "problems");
        }
    }
}
