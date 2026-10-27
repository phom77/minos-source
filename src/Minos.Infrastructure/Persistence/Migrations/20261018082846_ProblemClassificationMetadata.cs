using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Minos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProblemClassificationMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_published_problem_versions_id_problem_id",
                schema: "problems",
                table: "published_problem_versions",
                columns: new[] { "id", "problem_id" });

            migrationBuilder.CreateTable(
                name: "problem_classifications",
                schema: "problems",
                columns: table => new
                {
                    problem_id = table.Column<Guid>(type: "uuid", nullable: false),
                    basis_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    difficulty = table.Column<int>(type: "integer", nullable: true),
                    difficulty_origin = table.Column<int>(type: "integer", nullable: false),
                    source_problem_key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    source_rating = table.Column<int>(type: "integer", nullable: true),
                    source_tags = table.Column<string[]>(type: "text[]", nullable: false),
                    dataset_revision = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    manifest_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    policy_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    policy_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_problem_classifications", x => x.problem_id);
                    table.CheckConstraint("ck_problem_classifications_difficulty", "difficulty IS NULL OR difficulty BETWEEN 1 AND 3");
                    table.CheckConstraint("ck_problem_classifications_origin", "difficulty_origin IN (0,1) AND (difficulty_origin <> 1 OR (difficulty IS NOT NULL AND source_rating IS NOT NULL))");
                    table.CheckConstraint("ck_problem_classifications_revision", "revision > 0");
                    table.CheckConstraint("ck_problem_classifications_source", "(source_problem_key IS NULL AND source_rating IS NULL AND dataset_revision IS NULL\n    AND manifest_sha256 IS NULL AND policy_version IS NULL AND policy_sha256 IS NULL AND cardinality(source_tags) = 0)\nOR (source_problem_key IS NOT NULL AND source_problem_key ~ '^cf-[1-9][0-9]{0,8}-[a-z]{1,2}[0-9]?$'\n    AND (source_rating IS NULL OR source_rating BETWEEN 1 AND 10000)\n    AND dataset_revision IS NOT NULL AND dataset_revision ~ '^[0-9a-f]{40}$'\n    AND manifest_sha256 IS NOT NULL AND manifest_sha256 ~ '^[0-9a-f]{64}$'\n    AND policy_version IS NOT NULL AND length(btrim(policy_version)) > 0\n    AND policy_sha256 IS NOT NULL AND policy_sha256 ~ '^[0-9a-f]{64}$')");
                    table.CheckConstraint("ck_problem_classifications_source_tags", "cardinality(source_tags) <= 32 AND array_position(source_tags, NULL) IS NULL AND octet_length(array_to_string(source_tags, '')) <= 8192");
                    table.ForeignKey(
                        name: "FK_problem_classifications_problems_problem_id",
                        column: x => x.problem_id,
                        principalSchema: "problems",
                        principalTable: "problems",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_problem_classifications_published_problem_versions_basis_ve~",
                        columns: x => new { x.basis_version_id, x.problem_id },
                        principalSchema: "problems",
                        principalTable: "published_problem_versions",
                        principalColumns: new[] { "id", "problem_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "topics",
                schema: "problems",
                columns: table => new
                {
                    slug = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    display_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_topics", x => x.slug);
                    table.CheckConstraint("ck_topics_name", "length(btrim(display_name)) > 0 AND display_name = btrim(display_name)");
                    table.CheckConstraint("ck_topics_slug", "slug ~ '^[a-z]+(-[a-z]+)*$'");
                });

            migrationBuilder.CreateTable(
                name: "problem_topics",
                schema: "problems",
                columns: table => new
                {
                    problem_id = table.Column<Guid>(type: "uuid", nullable: false),
                    topic_slug = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    origin = table.Column<int>(type: "integer", nullable: false),
                    confidence = table.Column<int>(type: "integer", nullable: true),
                    rationale = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_problem_topics", x => new { x.problem_id, x.topic_slug });
                    table.CheckConstraint("ck_problem_topics_inference", "(confidence IS NULL OR confidence BETWEEN 0 AND 2)\nAND ((origin = 1 AND confidence IS NOT NULL AND rationale IS NOT NULL AND length(btrim(rationale)) > 0)\n    OR (origin <> 1 AND confidence IS NULL))");
                    table.CheckConstraint("ck_problem_topics_origin", "origin BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "FK_problem_topics_problem_classifications_problem_id",
                        column: x => x.problem_id,
                        principalSchema: "problems",
                        principalTable: "problem_classifications",
                        principalColumn: "problem_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_problem_topics_topics_topic_slug",
                        column: x => x.topic_slug,
                        principalSchema: "problems",
                        principalTable: "topics",
                        principalColumn: "slug",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "problems",
                table: "topics",
                columns: new[] { "slug", "display_name" },
                values: new object[,]
                {
                    { "binary-search", "Binary Search" },
                    { "bitmasks", "Bitmasks" },
                    { "brute-force", "Brute Force" },
                    { "combinatorics", "Combinatorics" },
                    { "constructive-algorithms", "Constructive Algorithms" },
                    { "data-structures", "Data Structures" },
                    { "divide-and-conquer", "Divide and Conquer" },
                    { "dynamic-programming", "Dynamic Programming" },
                    { "game-theory", "Game Theory" },
                    { "geometry", "Geometry" },
                    { "graph-matching", "Graph Matching" },
                    { "graph-traversal", "Graph Traversal" },
                    { "graphs", "Graphs" },
                    { "greedy", "Greedy" },
                    { "implementation", "Implementation" },
                    { "math", "Math" },
                    { "matrices", "Matrices" },
                    { "number-theory", "Number Theory" },
                    { "shortest-paths", "Shortest Paths" },
                    { "sorting", "Sorting" },
                    { "strings", "Strings" },
                    { "ternary-search", "Ternary Search" },
                    { "trees", "Trees" },
                    { "two-pointers", "Two Pointers" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_problem_classifications_basis_version_id_problem_id",
                schema: "problems",
                table: "problem_classifications",
                columns: new[] { "basis_version_id", "problem_id" });

            migrationBuilder.CreateIndex(
                name: "IX_problem_topics_topic_slug_problem_id",
                schema: "problems",
                table: "problem_topics",
                columns: new[] { "topic_slug", "problem_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "problem_topics",
                schema: "problems");

            migrationBuilder.DropTable(
                name: "problem_classifications",
                schema: "problems");

            migrationBuilder.DropTable(
                name: "topics",
                schema: "problems");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_published_problem_versions_id_problem_id",
                schema: "problems",
                table: "published_problem_versions");
        }
    }
}
