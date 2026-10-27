using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Minos.Domain.Problems;

namespace Minos.Infrastructure.Persistence.Configurations.Problems;

internal sealed class ProblemClassificationConfiguration : IEntityTypeConfiguration<ProblemClassification>,
    IEntityTypeConfiguration<ProblemTopic>, IEntityTypeConfiguration<ProblemTopicAssignment>
{
    public void Configure(EntityTypeBuilder<ProblemClassification> value)
    {
        value.ToTable("problem_classifications", "problems", table =>
        {
            table.HasCheckConstraint("ck_problem_classifications_difficulty", "difficulty IS NULL OR difficulty BETWEEN 1 AND 3");
            table.HasCheckConstraint("ck_problem_classifications_origin", "difficulty_origin IN (0,1) AND (difficulty_origin <> 1 OR (difficulty IS NOT NULL AND source_rating IS NOT NULL))");
            table.HasCheckConstraint("ck_problem_classifications_revision", "revision > 0");
            table.HasCheckConstraint("ck_problem_classifications_source_tags", "cardinality(source_tags) <= 32 AND array_position(source_tags, NULL) IS NULL AND octet_length(array_to_string(source_tags, '')) <= 8192");
            table.HasCheckConstraint("ck_problem_classifications_source", """
                (source_problem_key IS NULL AND source_rating IS NULL AND dataset_revision IS NULL
                    AND manifest_sha256 IS NULL AND policy_version IS NULL AND policy_sha256 IS NULL AND cardinality(source_tags) = 0)
                OR (source_problem_key IS NOT NULL AND source_problem_key ~ '^cf-[1-9][0-9]{0,8}-[a-z]{1,2}[0-9]?$'
                    AND (source_rating IS NULL OR source_rating BETWEEN 1 AND 10000)
                    AND dataset_revision IS NOT NULL AND dataset_revision ~ '^[0-9a-f]{40}$'
                    AND manifest_sha256 IS NOT NULL AND manifest_sha256 ~ '^[0-9a-f]{64}$'
                    AND policy_version IS NOT NULL AND length(btrim(policy_version)) > 0
                    AND policy_sha256 IS NOT NULL AND policy_sha256 ~ '^[0-9a-f]{64}$')
                """);
        });
        value.HasKey(item => item.ProblemId);
        value.Property(item => item.ProblemId).HasColumnName("problem_id").ValueGeneratedNever();
        value.HasOne<Problem>().WithOne().HasForeignKey<ProblemClassification>(item => item.ProblemId).OnDelete(DeleteBehavior.Restrict);
        value.Property(item => item.BasisVersionId).HasColumnName("basis_version_id");
        value.HasOne<PublishedProblemVersion>().WithMany().HasForeignKey(item => new { item.BasisVersionId, item.ProblemId })
            .HasPrincipalKey(item => new { item.Id, item.ProblemId }).OnDelete(DeleteBehavior.Restrict);
        value.Property(item => item.Difficulty).HasColumnName("difficulty").HasConversion<int?>();
        value.Property(item => item.DifficultyOrigin).HasColumnName("difficulty_origin").HasConversion<int>();
        value.Property(item => item.SourceProblemKey).HasColumnName("source_problem_key").HasMaxLength(32);
        value.Property(item => item.SourceRating).HasColumnName("source_rating");
        value.Property(item => item.SourceTags).HasColumnName("source_tags").HasColumnType("text[]").IsRequired();
        value.Property(item => item.DatasetRevision).HasColumnName("dataset_revision").HasMaxLength(40);
        value.Property(item => item.ManifestSha256).HasColumnName("manifest_sha256").HasMaxLength(64);
        value.Property(item => item.PolicyVersion).HasColumnName("policy_version").HasMaxLength(64);
        value.Property(item => item.PolicySha256).HasColumnName("policy_sha256").HasMaxLength(64);
        value.Property(item => item.Revision).HasColumnName("revision").IsConcurrencyToken();
        value.Property(item => item.UpdatedAtUtc).HasColumnName("updated_at_utc");
        value.HasMany(item => item.Topics).WithOne().HasForeignKey(item => item.ProblemId).OnDelete(DeleteBehavior.Cascade);
        value.Navigation(item => item.Topics).UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    public void Configure(EntityTypeBuilder<ProblemTopic> topic)
    {
        topic.ToTable("topics", "problems", table =>
        {
            table.HasCheckConstraint("ck_topics_slug", "slug ~ '^[a-z]+(-[a-z]+)*$'");
            table.HasCheckConstraint("ck_topics_name", "length(btrim(display_name)) > 0 AND display_name = btrim(display_name)");
        });
        topic.HasKey(item => item.Slug);
        topic.Property(item => item.Slug).HasColumnName("slug").HasMaxLength(48);
        topic.Property(item => item.DisplayName).HasColumnName("display_name").HasMaxLength(80);
        topic.HasData(new[] {
            ("binary-search", "Binary Search"), ("bitmasks", "Bitmasks"), ("brute-force", "Brute Force"),
            ("combinatorics", "Combinatorics"), ("constructive-algorithms", "Constructive Algorithms"),
            ("data-structures", "Data Structures"), ("divide-and-conquer", "Divide and Conquer"),
            ("dynamic-programming", "Dynamic Programming"), ("game-theory", "Game Theory"), ("geometry", "Geometry"),
            ("graph-matching", "Graph Matching"), ("graph-traversal", "Graph Traversal"), ("graphs", "Graphs"),
            ("greedy", "Greedy"), ("implementation", "Implementation"), ("math", "Math"), ("matrices", "Matrices"),
            ("number-theory", "Number Theory"), ("shortest-paths", "Shortest Paths"), ("sorting", "Sorting"),
            ("strings", "Strings"), ("ternary-search", "Ternary Search"), ("trees", "Trees"), ("two-pointers", "Two Pointers")
        }.Select(item => ProblemTopic.Create(item.Item1, item.Item2)));
    }

    public void Configure(EntityTypeBuilder<ProblemTopicAssignment> topic)
    {
        topic.ToTable("problem_topics", "problems", table =>
        {
            table.HasCheckConstraint("ck_problem_topics_origin", "origin BETWEEN 0 AND 2");
            table.HasCheckConstraint("ck_problem_topics_inference", """
                (confidence IS NULL OR confidence BETWEEN 0 AND 2)
                AND ((origin = 1 AND confidence IS NOT NULL AND rationale IS NOT NULL AND length(btrim(rationale)) > 0)
                    OR (origin <> 1 AND confidence IS NULL))
                """);
        });
        topic.HasKey(item => new { item.ProblemId, item.TopicSlug });
        topic.Property(item => item.ProblemId).HasColumnName("problem_id");
        topic.Property(item => item.TopicSlug).HasColumnName("topic_slug").HasMaxLength(48);
        topic.HasOne<ProblemTopic>().WithMany().HasForeignKey(item => item.TopicSlug).OnDelete(DeleteBehavior.Restrict);
        topic.Property(item => item.Origin).HasColumnName("origin").HasConversion<int>();
        topic.Property(item => item.Confidence).HasColumnName("confidence").HasConversion<int?>();
        topic.Property(item => item.Rationale).HasColumnName("rationale").HasMaxLength(1000);
        topic.HasIndex(item => new { item.TopicSlug, item.ProblemId });
    }
}
