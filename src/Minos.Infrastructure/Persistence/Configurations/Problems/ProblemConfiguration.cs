using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Minos.Domain.Accounts;
using Minos.Domain.Problems;

namespace Minos.Infrastructure.Persistence.Configurations.Problems;

internal sealed class ProblemConfiguration : IEntityTypeConfiguration<Problem>
{
    public void Configure(EntityTypeBuilder<Problem> problem)
    {
        problem.ToTable("problems", "problems", table =>
        {
            table.HasCheckConstraint("ck_problems_title", $"{PostgresTextConstraints.Utf16Length("title")} BETWEEN 1 AND 120 AND length(btrim(title, {PostgresTextConstraints.Whitespace})) > 0 AND title = btrim(title, {PostgresTextConstraints.Whitespace})");
            table.HasCheckConstraint("ck_problems_statement", $"{PostgresTextConstraints.Utf16Length("statement")} BETWEEN 1 AND 65536 AND length(btrim(statement, {PostgresTextConstraints.Whitespace})) > 0");
            table.HasCheckConstraint("ck_problems_time_limit", $"time_limit_ms IS NULL OR time_limit_ms BETWEEN {Problem.MinimumTimeLimitMilliseconds} AND {Problem.MaximumTimeLimitMilliseconds}");
            table.HasCheckConstraint("ck_problems_memory_limit", $"memory_limit_mb IS NULL OR memory_limit_mb BETWEEN {Problem.MinimumMemoryLimitMegabytes} AND {Problem.MaximumMemoryLimitMegabytes}");
            table.HasCheckConstraint("ck_problems_checker", "checker = 0");
            table.HasCheckConstraint("ck_problems_version", "latest_published_version >= 0");
            table.HasCheckConstraint("ck_problems_revision", "revision >= 0");
        });
        problem.HasKey(x => x.Id);
        problem.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        problem.Property(x => x.OwnerAccountId).HasColumnName("owner_account_id").IsRequired();
        problem.HasOne<Account>().WithMany().HasForeignKey(x => x.OwnerAccountId).OnDelete(DeleteBehavior.Restrict);
        problem.Property(x => x.Title).HasColumnName("title").HasMaxLength(Problem.MaximumTitleLength).IsRequired();
        problem.Property(x => x.Statement).HasColumnName("statement").HasMaxLength(Problem.MaximumStatementLength).IsRequired();
        problem.Property(x => x.TimeLimitMilliseconds).HasColumnName("time_limit_ms");
        problem.Property(x => x.MemoryLimitMegabytes).HasColumnName("memory_limit_mb");
        problem.Property(x => x.Checker).HasColumnName("checker").HasConversion<int>().IsRequired();
        problem.Property(x => x.LatestPublishedVersion).HasColumnName("latest_published_version").IsRequired();
        problem.Property(x => x.IsArchived).HasColumnName("is_archived").IsRequired();
        problem.Property(x => x.Revision).HasColumnName("revision").IsConcurrencyToken().IsRequired();
        problem.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        problem.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        problem.HasMany(x => x.Samples).WithOne().HasForeignKey("problem_id").OnDelete(DeleteBehavior.Cascade);
        problem.Navigation(x => x.Samples).UsePropertyAccessMode(PropertyAccessMode.Field);
        problem.HasMany(x => x.TestCases).WithOne().HasForeignKey("problem_id").OnDelete(DeleteBehavior.Cascade);
        problem.Navigation(x => x.TestCases).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ProblemSampleConfiguration : IEntityTypeConfiguration<ProblemSample>
{
    public void Configure(EntityTypeBuilder<ProblemSample> sample)
    {
        sample.ToTable("problem_samples", "problems", table =>
        {
            table.HasCheckConstraint("ck_problem_samples_order", """ "order" > 0 """);
            table.HasCheckConstraint("ck_problem_samples_input", $"{PostgresTextConstraints.Utf16Length("input")} <= {ProblemSample.MaximumContentLength}");
            table.HasCheckConstraint("ck_problem_samples_output", $"{PostgresTextConstraints.Utf16Length("output")} <= {ProblemSample.MaximumContentLength}");
        });
        sample.HasKey(x => x.Id);
        sample.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        sample.Property<Guid>("problem_id").HasColumnName("problem_id");
        sample.Property(x => x.Order).HasColumnName("order").IsRequired();
        sample.Property(x => x.Input).HasColumnName("input").HasMaxLength(ProblemSample.MaximumContentLength).IsRequired();
        sample.Property(x => x.Output).HasColumnName("output").HasMaxLength(ProblemSample.MaximumContentLength).IsRequired();
        sample.HasIndex("problem_id", nameof(ProblemSample.Order)).IsUnique();
    }
}

internal sealed class ProblemTestCaseConfiguration : IEntityTypeConfiguration<ProblemTestCase>
{
    public void Configure(EntityTypeBuilder<ProblemTestCase> test)
    {
        test.ToTable("problem_test_cases", "problems", table =>
        {
            table.HasCheckConstraint("ck_problem_test_cases_order", """ "order" > 0 """);
            table.HasCheckConstraint("ck_problem_test_cases_input_artifact", "input_artifact_key ~ '^tests/[0-9a-f]{32}/input[.]bin$' AND input_sha256 ~ '^[0-9a-f]{64}$' AND input_size_bytes BETWEEN 0 AND 8388608");
            table.HasCheckConstraint("ck_problem_test_cases_output_artifact", "output_artifact_key ~ '^tests/[0-9a-f]{32}/output[.]bin$' AND output_sha256 ~ '^[0-9a-f]{64}$' AND output_size_bytes BETWEEN 0 AND 8388608");
        });
        test.HasKey(x => x.Id);
        test.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        test.Property<Guid>("problem_id").HasColumnName("problem_id");
        test.Property(x => x.Order).HasColumnName("order").IsRequired();
        test.HasIndex("problem_id", nameof(ProblemTestCase.Order)).IsUnique();
        ConfigureArtifact(test.OwnsOne(x => x.InputArtifact), "input");
        ConfigureArtifact(test.OwnsOne(x => x.OutputArtifact), "output");
        test.Navigation(x => x.InputArtifact).IsRequired();
        test.Navigation(x => x.OutputArtifact).IsRequired();
    }

    internal static void ConfigureArtifact(OwnedNavigationBuilder<ProblemTestCase, ProblemArtifactReference> artifact, string prefix)
    {
        artifact.Property(x => x.Key).HasColumnName($"{prefix}_artifact_key").HasMaxLength(128).IsRequired();
        artifact.Property(x => x.Sha256).HasColumnName($"{prefix}_sha256").HasMaxLength(64).IsRequired();
        artifact.Property(x => x.SizeBytes).HasColumnName($"{prefix}_size_bytes").IsRequired();
    }
}
