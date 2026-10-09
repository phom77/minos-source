using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Minos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "contests");

            migrationBuilder.CreateTable(
                name: "contests",
                schema: "contests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    starts_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contests", x => x.id);
                    table.CheckConstraint("ck_contest_description", "length(description) <= 8000");
                    table.CheckConstraint("ck_contest_publication", "published_at_utc IS NULL OR published_at_utc < starts_at_utc");
                    table.CheckConstraint("ck_contest_revision", "revision > 0");
                    table.CheckConstraint("ck_contest_schedule", "ends_at_utc >= starts_at_utc + interval '1 minute' AND ends_at_utc <= starts_at_utc + interval '7 days'");
                    table.CheckConstraint("ck_contest_title", "length(btrim(title)) BETWEEN 1 AND 120");
                    table.ForeignKey(
                        name: "FK_contests_accounts_owner_account_id",
                        column: x => x.owner_account_id,
                        principalSchema: "accounts",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contest_problems",
                schema: "contests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contest_id = table.Column<Guid>(type: "uuid", nullable: false),
                    problem_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contest_problems", x => x.id);
                    table.UniqueConstraint("AK_contest_problems_contest_id_id", x => new { x.contest_id, x.id });
                    table.CheckConstraint("ck_contest_problem_order", "position BETWEEN 1 AND 26");
                    table.ForeignKey(
                        name: "FK_contest_problems_contests_contest_id",
                        column: x => x.contest_id,
                        principalSchema: "contests",
                        principalTable: "contests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_contest_problems_problems_problem_id",
                        column: x => x.problem_id,
                        principalSchema: "problems",
                        principalTable: "problems",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_contest_problems_published_problem_versions_version_id",
                        column: x => x.version_id,
                        principalSchema: "problems",
                        principalTable: "published_problem_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "registrations",
                schema: "contests",
                columns: table => new
                {
                    contest_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    registered_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_registrations", x => new { x.contest_id, x.account_id });
                    table.ForeignKey(
                        name: "FK_registrations_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_registrations_contests_contest_id",
                        column: x => x.contest_id,
                        principalSchema: "contests",
                        principalTable: "contests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "submissions",
                schema: "contests",
                columns: table => new
                {
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contest_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contest_problem_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contest_submissions", x => x.submission_id);
                    table.ForeignKey(
                        name: "FK_submissions_contest_problems_contest_id_contest_problem_id",
                        columns: x => new { x.contest_id, x.contest_problem_id },
                        principalSchema: "contests",
                        principalTable: "contest_problems",
                        principalColumns: new[] { "contest_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_submissions_registrations_contest_id_account_id",
                        columns: x => new { x.contest_id, x.account_id },
                        principalSchema: "contests",
                        principalTable: "registrations",
                        principalColumns: new[] { "contest_id", "account_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_submissions_submissions_submission_id",
                        column: x => x.submission_id,
                        principalSchema: "evaluation",
                        principalTable: "submissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_contest_problems_contest_id_position",
                schema: "contests",
                table: "contest_problems",
                columns: new[] { "contest_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_contest_problems_contest_id_problem_id",
                schema: "contests",
                table: "contest_problems",
                columns: new[] { "contest_id", "problem_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_contest_problems_contest_id_version_id",
                schema: "contests",
                table: "contest_problems",
                columns: new[] { "contest_id", "version_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_contest_problems_problem_id",
                schema: "contests",
                table: "contest_problems",
                column: "problem_id");

            migrationBuilder.CreateIndex(
                name: "IX_contest_problems_version_id",
                schema: "contests",
                table: "contest_problems",
                column: "version_id");

            migrationBuilder.CreateIndex(
                name: "IX_contests_owner_account_id",
                schema: "contests",
                table: "contests",
                column: "owner_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_contests_starts_at_utc_id",
                schema: "contests",
                table: "contests",
                columns: new[] { "starts_at_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_registrations_account_id",
                schema: "contests",
                table: "registrations",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_submissions_contest_id_account_id",
                schema: "contests",
                table: "submissions",
                columns: new[] { "contest_id", "account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_submissions_contest_id_contest_problem_id",
                schema: "contests",
                table: "submissions",
                columns: new[] { "contest_id", "contest_problem_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "submissions",
                schema: "contests");

            migrationBuilder.DropTable(
                name: "contest_problems",
                schema: "contests");

            migrationBuilder.DropTable(
                name: "registrations",
                schema: "contests");

            migrationBuilder.DropTable(
                name: "contests",
                schema: "contests");
        }
    }
}
