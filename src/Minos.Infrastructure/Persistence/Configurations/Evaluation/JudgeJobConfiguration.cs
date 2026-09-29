using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Minos.Domain.Evaluation;

namespace Minos.Infrastructure.Persistence.Configurations.Evaluation;

internal sealed class JudgeJobConfiguration : IEntityTypeConfiguration<JudgeJob>
{
    public void Configure(EntityTypeBuilder<JudgeJob> job)
    {
        job.ToTable("judge_jobs", "evaluation", table =>
        {
            table.HasCheckConstraint("ck_judge_jobs_status", "status BETWEEN 0 AND 3");
            table.HasCheckConstraint("ck_judge_jobs_attempt_count", $"attempt_count BETWEEN 0 AND {JudgeJob.MaximumAttempts}");
            table.HasCheckConstraint("ck_judge_jobs_lease_state", "(status = 0 AND active_attempt_id IS NULL AND active_lease_token IS NULL AND active_worker_id IS NULL AND lease_expires_at_utc IS NULL AND completed_attempt_id IS NULL) OR (status = 1 AND active_attempt_id IS NOT NULL AND active_lease_token IS NOT NULL AND active_worker_id IS NOT NULL AND lease_expires_at_utc IS NOT NULL AND completed_attempt_id IS NULL) OR (status IN (2,3) AND active_attempt_id IS NULL AND active_lease_token IS NULL AND active_worker_id IS NULL AND lease_expires_at_utc IS NULL AND completed_attempt_id IS NOT NULL)");
        });
        job.HasKey(x => x.Id);
        job.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        job.Property(x => x.SubmissionId).HasColumnName("submission_id").IsRequired();
        job.HasOne<Submission>().WithOne().HasForeignKey<JudgeJob>(x => x.SubmissionId).OnDelete(DeleteBehavior.Restrict);
        job.HasIndex(x => x.SubmissionId).IsUnique();
        job.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        job.Property(x => x.AttemptCount).HasColumnName("attempt_count").IsRequired();
        job.Property(x => x.AvailableAtUtc).HasColumnName("available_at_utc").IsRequired();
        job.Property(x => x.ActiveAttemptId).HasColumnName("active_attempt_id");
        job.HasOne<JudgingAttempt>().WithMany().HasForeignKey(x => x.ActiveAttemptId).OnDelete(DeleteBehavior.Restrict);
        job.Property(x => x.ActiveLeaseToken).HasColumnName("active_lease_token");
        job.Property(x => x.ActiveWorkerId).HasColumnName("active_worker_id").HasMaxLength(128);
        job.Property(x => x.LeaseExpiresAtUtc).HasColumnName("lease_expires_at_utc");
        job.Property(x => x.CompletedAttemptId).HasColumnName("completed_attempt_id");
        job.HasOne<JudgingAttempt>().WithMany().HasForeignKey(x => x.CompletedAttemptId).OnDelete(DeleteBehavior.Restrict);
        job.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        job.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        job.HasIndex(x => new { x.Status, x.AvailableAtUtc });
        job.HasIndex(x => new { x.Status, x.LeaseExpiresAtUtc });
    }
}

internal sealed class JudgingAttemptConfiguration : IEntityTypeConfiguration<JudgingAttempt>
{
    public void Configure(EntityTypeBuilder<JudgingAttempt> attempt)
    {
        attempt.ToTable("judging_attempts", "evaluation", table =>
        {
            table.HasCheckConstraint("ck_judging_attempts_number", $"attempt_number BETWEEN 1 AND {JudgeJob.MaximumAttempts}");
            table.HasCheckConstraint("ck_judging_attempts_status", "status BETWEEN 0 AND 3");
            table.HasCheckConstraint("ck_judging_attempts_failure_kind", "failure_kind IS NULL OR failure_kind BETWEEN 0 AND 2");
            table.HasCheckConstraint("ck_judging_attempts_failure", "(status = 0 AND failure_kind IS NULL AND finished_at_utc IS NULL) OR (status = 1 AND failure_kind IS NULL AND finished_at_utc IS NOT NULL) OR (status IN (2,3) AND failure_kind IS NOT NULL AND finished_at_utc IS NOT NULL)");
            table.HasCheckConstraint("ck_judging_attempts_lease", "lease_expires_at_utc > started_at_utc");
        });
        attempt.HasKey(x => x.Id);
        attempt.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        attempt.Property(x => x.JudgeJobId).HasColumnName("judge_job_id").IsRequired();
        attempt.HasOne<JudgeJob>().WithMany().HasForeignKey(x => x.JudgeJobId).OnDelete(DeleteBehavior.Restrict);
        attempt.Property(x => x.SubmissionId).HasColumnName("submission_id").IsRequired();
        attempt.HasOne<Submission>().WithMany().HasForeignKey(x => x.SubmissionId).OnDelete(DeleteBehavior.Restrict);
        attempt.Property(x => x.AttemptNumber).HasColumnName("attempt_number").IsRequired();
        attempt.HasIndex(x => new { x.JudgeJobId, x.AttemptNumber }).IsUnique();
        attempt.Property(x => x.LeaseToken).HasColumnName("lease_token").IsRequired();
        attempt.HasIndex(x => x.LeaseToken).IsUnique();
        attempt.Property(x => x.WorkerId).HasColumnName("worker_id").HasMaxLength(128).IsRequired();
        attempt.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        attempt.Property(x => x.FailureKind).HasColumnName("failure_kind").HasConversion<int?>();
        attempt.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc").IsRequired();
        attempt.Property(x => x.LeaseExpiresAtUtc).HasColumnName("lease_expires_at_utc").IsRequired();
        attempt.Property(x => x.FinishedAtUtc).HasColumnName("finished_at_utc");
        attempt.HasIndex(x => new { x.SubmissionId, x.AttemptNumber });
    }
}
