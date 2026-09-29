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
    }
}

internal sealed class SubmissionResultConfiguration : IEntityTypeConfiguration<SubmissionResult>
{
    public void Configure(EntityTypeBuilder<SubmissionResult> result)
    {
        result.ToTable("submission_results", "evaluation");
        result.HasKey(x => x.SubmissionId);
        result.Property(x => x.SubmissionId).HasColumnName("submission_id").ValueGeneratedNever();
        result.HasOne<Submission>().WithOne().HasForeignKey<SubmissionResult>(x => x.SubmissionId).OnDelete(DeleteBehavior.Restrict);
        result.Property(x => x.AttemptId).HasColumnName("attempt_id").IsRequired();
        result.HasOne<JudgingAttempt>().WithOne().HasForeignKey<SubmissionResult>(x => x.AttemptId).OnDelete(DeleteBehavior.Restrict);
        result.HasIndex(x => x.AttemptId).IsUnique();
        result.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc").IsRequired();
    }
}
