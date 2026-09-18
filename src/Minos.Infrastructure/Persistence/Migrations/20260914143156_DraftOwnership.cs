using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Minos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DraftOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Legacy development records have no trustworthy owner. Require an explicit data
            // migration instead of silently assigning ownership or deleting private content.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM problems.problem_drafts) THEN
                        RAISE EXCEPTION 'Ownerless development drafts require an explicit ownership migration before M3.';
                    END IF;
                END $$;
                """);
            migrationBuilder.AddColumn<Guid>(
                name: "owner_account_id",
                schema: "problems",
                table: "problem_drafts",
                type: "uuid",
                nullable: false);

            migrationBuilder.CreateIndex(
                name: "IX_problem_drafts_owner_account_id",
                schema: "problems",
                table: "problem_drafts",
                column: "owner_account_id");

            migrationBuilder.AddForeignKey(
                name: "FK_problem_drafts_accounts_owner_account_id",
                schema: "problems",
                table: "problem_drafts",
                column: "owner_account_id",
                principalSchema: "accounts",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_problem_drafts_accounts_owner_account_id",
                schema: "problems",
                table: "problem_drafts");

            migrationBuilder.DropIndex(
                name: "IX_problem_drafts_owner_account_id",
                schema: "problems",
                table: "problem_drafts");

            migrationBuilder.DropColumn(
                name: "owner_account_id",
                schema: "problems",
                table: "problem_drafts");
        }
    }
}
