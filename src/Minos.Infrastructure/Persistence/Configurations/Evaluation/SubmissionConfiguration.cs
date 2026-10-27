using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Minos.Domain.Accounts;
using Minos.Domain.Evaluation;
using Minos.Domain.Problems;
using Minos.Application.Evaluation;
using Minos.Infrastructure.Persistence.Configurations;

namespace Minos.Infrastructure.Persistence.Configurations.Evaluation;

internal sealed class SubmissionConfiguration : IEntityTypeConfiguration<Submission>
{
    public void Configure(EntityTypeBuilder<Submission> submission)
    {
        submission.ToTable("submissions", "evaluation", table =>
        {
            table.HasCheckConstraint("ck_submissions_language", "language = 'cpp20'");
            table.HasCheckConstraint("ck_submissions_toolchain", $"toolchain_id = '{SupportedSubmission.ToolchainId}'");
            table.HasCheckConstraint("ck_submissions_source", $"octet_length(source) BETWEEN 1 AND {Submission.MaximumSourceBytes} AND length(btrim(source, {PostgresTextConstraints.Whitespace})) > 0");
            table.HasCheckConstraint("ck_submissions_status", "status BETWEEN 0 AND 3");
            table.HasCheckConstraint("ck_submissions_attempt_state", "(status = 1) = (current_attempt_id IS NOT NULL)");
            table.HasCheckConstraint("ck_submissions_completion", "((status IN (0,1)) AND completed_attempt_id IS NULL AND completed_at_utc IS NULL) OR ((status IN (2,3)) AND completed_attempt_id IS NOT NULL AND completed_at_utc IS NOT NULL)");
        });
        submission.HasKey(x => x.Id);
        submission.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        submission.Property(x => x.OwnerAccountId).HasColumnName("owner_account_id").IsRequired();
        submission.HasOne<Account>().WithMany().HasForeignKey(x => x.OwnerAccountId).OnDelete(DeleteBehavior.Restrict);
        submission.Property(x => x.ProblemId).HasColumnName("problem_id").IsRequired();
        submission.HasOne<Problem>().WithMany().HasForeignKey(x => x.ProblemId).OnDelete(DeleteBehavior.Restrict);
        submission.Property(x => x.ProblemVersionId).HasColumnName("problem_version_id").IsRequired();
        submission.HasOne<PublishedProblemVersion>().WithMany().HasForeignKey(x => x.ProblemVersionId).OnDelete(DeleteBehavior.Restrict);
        submission.Property(x => x.Language).HasColumnName("language").HasMaxLength(32).IsRequired();
        submission.Property(x => x.ToolchainId).HasColumnName("toolchain_id").HasMaxLength(160).IsRequired();
        submission.Property(x => x.Source).HasColumnName("source").HasColumnType("text").IsRequired();
        submission.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        submission.Property(x => x.CurrentAttemptId).HasColumnName("current_attempt_id");
        submission.HasOne<JudgingAttempt>().WithMany().HasForeignKey(x => x.CurrentAttemptId).OnDelete(DeleteBehavior.Restrict);
        submission.Property(x => x.CompletedAttemptId).HasColumnName("completed_attempt_id");
        submission.HasOne<JudgingAttempt>().WithMany().HasForeignKey(x => x.CompletedAttemptId).OnDelete(DeleteBehavior.Restrict);
        submission.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        submission.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");
        submission.HasIndex(x => new { x.OwnerAccountId, x.CreatedAtUtc });
        submission.HasIndex(x => new { x.OwnerAccountId, x.Status });
        submission.HasIndex(x => new { x.OwnerAccountId, x.ProblemId });
        submission.HasIndex(x => x.ProblemVersionId);
        submission.HasIndex(x => new { x.ProblemVersionId, x.Language, x.ToolchainId, x.OwnerAccountId })
            .HasDatabaseName("ix_submissions_performance_cohort").HasFilter("status = 2");
    }
}

internal sealed class SubmissionResultConfiguration : IEntityTypeConfiguration<SubmissionResult>
{
    public void Configure(EntityTypeBuilder<SubmissionResult> result)
    {
        result.ToTable("submission_results", "evaluation", table =>
        {
            table.HasCheckConstraint("ck_submission_results_verdict", "verdict IS NULL OR verdict BETWEEN 0 AND 6");
            table.HasCheckConstraint("ck_submission_results_measurement_profile", "measurement_profile_id IS NULL OR measurement_profile_id ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_submission_results_diagnostics", $"octet_length(compiler_diagnostics) <= {JudgingOutcome.MaximumCompilerDiagnosticsBytes}");
            table.HasCheckConstraint("ck_submission_results_summary", "(verdict IS NULL AND maximum_cpu_time_ms IS NULL AND maximum_wall_time_ms IS NULL AND maximum_memory_bytes IS NULL AND compiler_diagnostics = '') OR (verdict = 2 AND maximum_cpu_time_ms IS NULL AND maximum_wall_time_ms IS NULL AND maximum_memory_bytes IS NULL) OR (verdict IS NOT NULL AND verdict <> 2 AND maximum_cpu_time_ms IS NOT NULL AND maximum_cpu_time_ms >= 0 AND maximum_wall_time_ms IS NOT NULL AND maximum_wall_time_ms >= 0 AND maximum_memory_bytes IS NOT NULL AND maximum_memory_bytes >= 0 AND compiler_diagnostics = '')");
        });
        result.HasKey(x => x.SubmissionId);
        result.HasAlternateKey(x => new { x.SubmissionId, x.AttemptId });
        result.Property(x => x.SubmissionId).HasColumnName("submission_id").ValueGeneratedNever();
        result.HasOne<Submission>().WithOne().HasForeignKey<SubmissionResult>(x => x.SubmissionId).OnDelete(DeleteBehavior.Restrict);
        result.Property(x => x.AttemptId).HasColumnName("attempt_id").IsRequired();
        result.HasOne<JudgingAttempt>().WithOne().HasForeignKey<SubmissionResult>(x => x.AttemptId).OnDelete(DeleteBehavior.Restrict);
        result.HasIndex(x => x.AttemptId).IsUnique();
        result.Property(x => x.Verdict).HasColumnName("verdict").HasConversion<int?>();
        result.Property(x => x.CompilerDiagnostics).HasColumnName("compiler_diagnostics").HasColumnType("text").IsRequired();
        result.Property(x => x.MaximumCpuTimeMilliseconds).HasColumnName("maximum_cpu_time_ms");
        result.Property(x => x.MaximumWallTimeMilliseconds).HasColumnName("maximum_wall_time_ms");
        result.Property(x => x.MaximumMemoryBytes).HasColumnName("maximum_memory_bytes");
        result.Property(x => x.MeasurementProfileId).HasColumnName("measurement_profile_id").HasMaxLength(64);
        result.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc").IsRequired();
        result.HasMany(x => x.TestCaseResults).WithOne()
            .HasForeignKey(x => new { x.SubmissionId, x.AttemptId })
            .HasPrincipalKey(x => new { x.SubmissionId, x.AttemptId }).OnDelete(DeleteBehavior.Restrict);
        result.Navigation(x => x.TestCaseResults).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class TestCaseResultConfiguration : IEntityTypeConfiguration<TestCaseResult>
{
    public void Configure(EntityTypeBuilder<TestCaseResult> test)
    {
        test.ToTable("test_case_results", "evaluation", table =>
        {
            table.HasCheckConstraint("ck_test_case_results_order", "test_order > 0");
            table.HasCheckConstraint("ck_test_case_results_status", "status BETWEEN 0 AND 6");
            table.HasCheckConstraint("ck_test_case_results_measurements", "(status = 0 AND cpu_time_ms IS NULL AND wall_time_ms IS NULL AND memory_peak_bytes IS NULL) OR (status <> 0 AND cpu_time_ms IS NOT NULL AND cpu_time_ms >= 0 AND wall_time_ms IS NOT NULL AND wall_time_ms >= 0 AND memory_peak_bytes IS NOT NULL AND memory_peak_bytes >= 0)");
            table.HasCheckConstraint("ck_test_case_results_execution", "(status = 0 AND exit_code IS NULL AND NOT wall_timeout AND NOT cpu_limit AND NOT memory_failure AND NOT output_limit) OR (status <> 0 AND exit_code IS NOT NULL)");
        });
        test.HasKey(x => x.Id);
        test.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        test.Property(x => x.SubmissionId).HasColumnName("submission_id").IsRequired();
        test.Property(x => x.AttemptId).HasColumnName("attempt_id").IsRequired();
        test.HasOne<JudgingAttempt>().WithMany().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Restrict);
        test.Property(x => x.Order).HasColumnName("test_order").IsRequired();
        test.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        test.Property(x => x.CpuTimeMilliseconds).HasColumnName("cpu_time_ms");
        test.Property(x => x.WallTimeMilliseconds).HasColumnName("wall_time_ms");
        test.Property(x => x.MemoryPeakBytes).HasColumnName("memory_peak_bytes");
        test.Property(x => x.ExitCode).HasColumnName("exit_code");
        test.Property(x => x.WallTimeout).HasColumnName("wall_timeout").IsRequired();
        test.Property(x => x.CpuLimit).HasColumnName("cpu_limit").IsRequired();
        test.Property(x => x.MemoryFailure).HasColumnName("memory_failure").IsRequired();
        test.Property(x => x.OutputLimit).HasColumnName("output_limit").IsRequired();
        test.HasIndex(x => new { x.SubmissionId, x.Order }).IsUnique();
        test.HasIndex(x => x.AttemptId);
    }
}
