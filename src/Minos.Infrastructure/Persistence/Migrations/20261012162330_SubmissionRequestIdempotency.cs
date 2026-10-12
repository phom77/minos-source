using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Minos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SubmissionRequestIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_submissions_id_owner_account_id",
                schema: "evaluation",
                table: "submissions",
                columns: new[] { "id", "owner_account_id" });

            migrationBuilder.CreateTable(
                name: "submission_requests",
                schema: "evaluation",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_key = table.Column<Guid>(type: "uuid", nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_submission_requests", x => new { x.account_id, x.request_key });
                    table.CheckConstraint("ck_submission_requests_fingerprint", "fingerprint ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_submission_requests_key", "request_key <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_submission_requests_submissions_submission_id_account_id",
                        columns: x => new { x.submission_id, x.account_id },
                        principalSchema: "evaluation",
                        principalTable: "submissions",
                        principalColumns: new[] { "id", "owner_account_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_submission_requests_submission_id",
                schema: "evaluation",
                table: "submission_requests",
                column: "submission_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_submission_requests_submission_id_account_id",
                schema: "evaluation",
                table: "submission_requests",
                columns: new[] { "submission_id", "account_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "submission_requests",
                schema: "evaluation");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_submissions_id_owner_account_id",
                schema: "evaluation",
                table: "submissions");
        }
    }
}
