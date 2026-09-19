using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Minos.Domain.Accounts;
using Minos.Domain.Problems;

namespace Minos.Infrastructure.Persistence.Configurations.Problems;

internal sealed class PublishedProblemVersionConfiguration : IEntityTypeConfiguration<PublishedProblemVersion>
{
    public void Configure(EntityTypeBuilder<PublishedProblemVersion> version)
    {
        version.ToTable("published_problem_versions", "problems", table =>
        {
            table.HasCheckConstraint("ck_published_problem_versions_number", "version_number > 0");
            table.HasCheckConstraint("ck_published_problem_versions_time_limit", $"time_limit_ms BETWEEN {Problem.MinimumTimeLimitMilliseconds} AND {Problem.MaximumTimeLimitMilliseconds}");
            table.HasCheckConstraint("ck_published_problem_versions_memory_limit", $"memory_limit_mb BETWEEN {Problem.MinimumMemoryLimitMegabytes} AND {Problem.MaximumMemoryLimitMegabytes}");
            table.HasCheckConstraint("ck_published_problem_versions_checker", "checker = 0");
            table.HasCheckConstraint("ck_published_problem_versions_test_count", $"private_test_count BETWEEN 1 AND {Problem.MaximumTestCases}");
            table.HasCheckConstraint("ck_published_problem_versions_checklist", "statement_reviewed AND samples_verified AND reference_solution_checked");
        });
        version.HasKey(x => x.Id);
        version.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        version.Property(x => x.ProblemId).HasColumnName("problem_id").IsRequired();
        version.HasOne<Problem>().WithMany().HasForeignKey(x => x.ProblemId).OnDelete(DeleteBehavior.Restrict);
        version.Property(x => x.VersionNumber).HasColumnName("version_number").IsRequired();
        version.HasIndex(x => new { x.ProblemId, x.VersionNumber }).IsUnique();
        version.Property(x => x.Title).HasColumnName("title").HasMaxLength(Problem.MaximumTitleLength).IsRequired();
        version.Property(x => x.Statement).HasColumnName("statement").HasMaxLength(Problem.MaximumStatementLength).IsRequired();
        version.Property(x => x.TimeLimitMilliseconds).HasColumnName("time_limit_ms").IsRequired();
        version.Property(x => x.MemoryLimitMegabytes).HasColumnName("memory_limit_mb").IsRequired();
        version.Property(x => x.Checker).HasColumnName("checker").HasConversion<int>().IsRequired();
        version.Property(x => x.PrivateTestCount).HasColumnName("private_test_count").IsRequired();
        version.Property(x => x.PublishedByAccountId).HasColumnName("published_by_account_id").IsRequired();
        version.HasOne<Account>().WithMany().HasForeignKey(x => x.PublishedByAccountId).OnDelete(DeleteBehavior.Restrict);
        version.Property(x => x.PublishedAtUtc).HasColumnName("published_at_utc").IsRequired();
        version.Property(x => x.StatementReviewed).HasColumnName("statement_reviewed").IsRequired();
        version.Property(x => x.SamplesVerified).HasColumnName("samples_verified").IsRequired();
        version.Property(x => x.ReferenceSolutionChecked).HasColumnName("reference_solution_checked").IsRequired();
        version.HasMany(x => x.Samples).WithOne().HasForeignKey(x => x.ProblemVersionId).OnDelete(DeleteBehavior.Restrict);
        version.Navigation(x => x.Samples).UsePropertyAccessMode(PropertyAccessMode.Field);
        version.HasMany(x => x.TestCases).WithOne().HasForeignKey(x => x.ProblemVersionId).OnDelete(DeleteBehavior.Restrict);
        version.Navigation(x => x.TestCases).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class PublishedProblemSampleConfiguration : IEntityTypeConfiguration<PublishedProblemSample>
{
    public void Configure(EntityTypeBuilder<PublishedProblemSample> sample)
    {
        sample.ToTable("published_problem_samples", "problems", table =>
            table.HasCheckConstraint("ck_published_problem_samples_order", """ "order" > 0 """));
        sample.HasKey(x => x.Id);
        sample.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        sample.Property(x => x.ProblemVersionId).HasColumnName("problem_version_id").IsRequired();
        sample.Property(x => x.Order).HasColumnName("order").IsRequired();
        sample.Property(x => x.Input).HasColumnName("input").HasMaxLength(ProblemSample.MaximumContentLength).IsRequired();
        sample.Property(x => x.Output).HasColumnName("output").HasMaxLength(ProblemSample.MaximumContentLength).IsRequired();
        sample.HasIndex(x => new { x.ProblemVersionId, x.Order }).IsUnique();
    }
}

internal sealed class PublishedProblemTestCaseConfiguration : IEntityTypeConfiguration<PublishedProblemTestCase>
{
    public void Configure(EntityTypeBuilder<PublishedProblemTestCase> test)
    {
        test.ToTable("published_problem_test_cases", "problems", table =>
        {
            table.HasCheckConstraint("ck_published_problem_test_cases_order", """ "order" > 0 """);
            table.HasCheckConstraint("ck_published_problem_test_cases_input_artifact", "input_artifact_key ~ '^tests/[0-9a-f]{32}/input[.]bin$' AND input_sha256 ~ '^[0-9a-f]{64}$' AND input_size_bytes BETWEEN 0 AND 8388608");
            table.HasCheckConstraint("ck_published_problem_test_cases_output_artifact", "output_artifact_key ~ '^tests/[0-9a-f]{32}/output[.]bin$' AND output_sha256 ~ '^[0-9a-f]{64}$' AND output_size_bytes BETWEEN 0 AND 8388608");
        });
        test.HasKey(x => x.Id);
        test.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        test.Property(x => x.ProblemVersionId).HasColumnName("problem_version_id").IsRequired();
        test.Property(x => x.Order).HasColumnName("order").IsRequired();
        test.HasIndex(x => new { x.ProblemVersionId, x.Order }).IsUnique();
        ConfigureArtifact(test.OwnsOne(x => x.InputArtifact), "input");
        ConfigureArtifact(test.OwnsOne(x => x.OutputArtifact), "output");
        test.Navigation(x => x.InputArtifact).IsRequired();
        test.Navigation(x => x.OutputArtifact).IsRequired();
    }

    private static void ConfigureArtifact(OwnedNavigationBuilder<PublishedProblemTestCase, ProblemArtifactReference> artifact, string prefix)
    {
        artifact.Property(x => x.Key).HasColumnName($"{prefix}_artifact_key").HasMaxLength(128).IsRequired();
        artifact.Property(x => x.Sha256).HasColumnName($"{prefix}_sha256").HasMaxLength(64).IsRequired();
        artifact.Property(x => x.SizeBytes).HasColumnName($"{prefix}_size_bytes").IsRequired();
    }
}
