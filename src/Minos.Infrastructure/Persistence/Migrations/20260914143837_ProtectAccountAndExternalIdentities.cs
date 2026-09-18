using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Minos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProtectAccountAndExternalIdentities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_external_logins_UserId",
                schema: "accounts",
                table: "external_logins");

            migrationBuilder.DropCheckConstraint(
                name: "ck_accounts_display_name",
                schema: "accounts",
                table: "accounts");

            migrationBuilder.AlterColumn<string>(
                name: "ProviderKey",
                schema: "accounts",
                table: "external_logins",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "LoginProvider",
                schema: "accounts",
                table: "external_logins",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.CreateIndex(
                name: "IX_external_logins_UserId_LoginProvider",
                schema: "accounts",
                table: "external_logins",
                columns: new[] { "UserId", "LoginProvider" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_accounts_display_name",
                schema: "accounts",
                table: "accounts",
                sql: "(length(display_name) + length(regexp_replace(display_name, U&'[^\\+010000-\\+10FFFF]', '', 'g'))) BETWEEN 1 AND 80 AND length(btrim(display_name, E' \\t\\n\\r\\f\\v\\u0085\\u00a0\\u1680\\u2000\\u2001\\u2002\\u2003\\u2004\\u2005\\u2006\\u2007\\u2008\\u2009\\u200a\\u2028\\u2029\\u202f\\u205f\\u3000')) > 0 AND display_name = btrim(display_name, E' \\t\\n\\r\\f\\v\\u0085\\u00a0\\u1680\\u2000\\u2001\\u2002\\u2003\\u2004\\u2005\\u2006\\u2007\\u2008\\u2009\\u200a\\u2028\\u2029\\u202f\\u205f\\u3000') AND display_name !~ U&'[\\0001-\\001F\\007F-\\009F]'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_accounts_id",
                schema: "accounts",
                table: "accounts",
                sql: "id <> '00000000-0000-0000-0000-000000000000'::uuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_external_logins_UserId_LoginProvider",
                schema: "accounts",
                table: "external_logins");

            migrationBuilder.DropCheckConstraint(
                name: "ck_accounts_display_name",
                schema: "accounts",
                table: "accounts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_accounts_id",
                schema: "accounts",
                table: "accounts");

            migrationBuilder.AlterColumn<string>(
                name: "ProviderKey",
                schema: "accounts",
                table: "external_logins",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "LoginProvider",
                schema: "accounts",
                table: "external_logins",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.CreateIndex(
                name: "IX_external_logins_UserId",
                schema: "accounts",
                table: "external_logins",
                column: "UserId");

            migrationBuilder.AddCheckConstraint(
                name: "ck_accounts_display_name",
                schema: "accounts",
                table: "accounts",
                sql: "length(display_name) BETWEEN 1 AND 80 AND display_name = btrim(display_name) AND display_name !~ '[[:cntrl:]]'");
        }
    }
}
