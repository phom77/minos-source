using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Minos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProblemComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "problem_comments",
                schema: "problems",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    problem_id = table.Column<Guid>(type: "uuid", nullable: false),
                    published_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_comment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    is_spoiler = table.Column<bool>(type: "boolean", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_problem_comments", x => x.id);
                    table.UniqueConstraint("AK_problem_comments_id_problem_id", x => new { x.id, x.problem_id });
                    table.CheckConstraint("ck_problem_comments_body", "(state = 0 AND (length(body) + length(regexp_replace(body, U&'[^\\+010000-\\+10FFFF]', '', 'g'))) BETWEEN 1 AND 4000\n    AND body = btrim(body, E' \\t\\n\\r\\f\\v\\u0085\\u00a0\\u1680\\u2000\\u2001\\u2002\\u2003\\u2004\\u2005\\u2006\\u2007\\u2008\\u2009\\u200a\\u2028\\u2029\\u202f\\u205f\\u3000')\n    AND length(btrim(body, E' \\t\\n\\r\\f\\v\\u0085\\u00a0\\u1680\\u2000\\u2001\\u2002\\u2003\\u2004\\u2005\\u2006\\u2007\\u2008\\u2009\\u200a\\u2028\\u2029\\u202f\\u205f\\u3000')) > 0\n    AND body !~ '[\\x01-\\x08\\x0B\\x0C\\x0E-\\x1F\\x7F-\\x9F]')\nOR (state IN (1,2) AND body = '' AND NOT is_spoiler)");
                    table.CheckConstraint("ck_problem_comments_parent", "parent_comment_id IS NULL OR parent_comment_id <> id");
                    table.CheckConstraint("ck_problem_comments_revision", "revision > 0 AND updated_at_utc >= created_at_utc");
                    table.ForeignKey(
                        name: "FK_problem_comments_accounts_author_account_id",
                        column: x => x.author_account_id,
                        principalSchema: "accounts",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_problem_comments_problem_comments_parent_comment_id_problem~",
                        columns: x => new { x.parent_comment_id, x.problem_id },
                        principalSchema: "problems",
                        principalTable: "problem_comments",
                        principalColumns: new[] { "id", "problem_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_problem_comments_problems_problem_id",
                        column: x => x.problem_id,
                        principalSchema: "problems",
                        principalTable: "problems",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_problem_comments_published_problem_versions_published_versi~",
                        columns: x => new { x.published_version_id, x.problem_id },
                        principalSchema: "problems",
                        principalTable: "published_problem_versions",
                        principalColumns: new[] { "id", "problem_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_problem_comments_author_account_id",
                schema: "problems",
                table: "problem_comments",
                column: "author_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_problem_comments_parent_comment_id_problem_id",
                schema: "problems",
                table: "problem_comments",
                columns: new[] { "parent_comment_id", "problem_id" });

            migrationBuilder.CreateIndex(
                name: "IX_problem_comments_problem_id_parent_comment_id_created_at_ut~",
                schema: "problems",
                table: "problem_comments",
                columns: new[] { "problem_id", "parent_comment_id", "created_at_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_problem_comments_published_version_id_problem_id",
                schema: "problems",
                table: "problem_comments",
                columns: new[] { "published_version_id", "problem_id" });

            // Cross-row thread rules cannot be expressed as CHECK constraints. The parent/version
            // associations never change; this trigger also protects direct writes with the API role.
            migrationBuilder.Sql("""
                CREATE FUNCTION problems.guard_problem_comment() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'UPDATE' THEN
                        IF (NEW.id, NEW.problem_id, NEW.published_version_id, NEW.author_account_id, NEW.parent_comment_id, NEW.created_at_utc)
                            IS DISTINCT FROM (OLD.id, OLD.problem_id, OLD.published_version_id, OLD.author_account_id, OLD.parent_comment_id, OLD.created_at_utc)
                            OR OLD.state <> 0 OR NEW.revision <> OLD.revision + 1 OR NEW.updated_at_utc < OLD.updated_at_utc THEN
                            RAISE EXCEPTION 'Invalid comment transition' USING ERRCODE = '23514';
                        END IF;
                    ELSE
                        IF NEW.revision <> 1 OR NEW.state <> 0 THEN
                            RAISE EXCEPTION 'Invalid initial comment state' USING ERRCODE = '23514';
                        END IF;
                        IF NEW.parent_comment_id IS NOT NULL THEN
                            PERFORM 1 FROM problems.problem_comments
                                WHERE id = NEW.parent_comment_id AND problem_id = NEW.problem_id
                                    AND parent_comment_id IS NULL AND state = 0 FOR SHARE;
                            IF NOT FOUND THEN
                                RAISE EXCEPTION 'Reply requires an available root comment' USING ERRCODE = '23514';
                            END IF;
                        END IF;
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER guard_problem_comment BEFORE INSERT OR UPDATE ON problems.problem_comments
                    FOR EACH ROW EXECUTE FUNCTION problems.guard_problem_comment();
                REVOKE ALL ON FUNCTION problems.guard_problem_comment() FROM PUBLIC;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "problem_comments",
                schema: "problems");
            migrationBuilder.Sql("DROP FUNCTION problems.guard_problem_comment();");
        }
    }
}
