using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Minos.Domain.Accounts;
using Minos.Domain.Contests;
using Minos.Domain.Evaluation;
using Minos.Domain.Problems;

namespace Minos.Infrastructure.Persistence.Configurations;

internal sealed class ContestConfiguration : IEntityTypeConfiguration<Contest>, IEntityTypeConfiguration<ContestProblem>,
    IEntityTypeConfiguration<ContestRegistration>, IEntityTypeConfiguration<ContestSubmission>
{
    public void Configure(EntityTypeBuilder<Contest> b)
    {
        b.ToTable("contests", "contests", t =>
        {
            t.HasCheckConstraint("ck_contest_title", "length(btrim(title)) BETWEEN 1 AND 120");
            t.HasCheckConstraint("ck_contest_description", "length(description) <= 8000");
            t.HasCheckConstraint("ck_contest_schedule", "ends_at_utc >= starts_at_utc + interval '1 minute' AND ends_at_utc <= starts_at_utc + interval '7 days'");
            t.HasCheckConstraint("ck_contest_publication", "published_at_utc IS NULL OR published_at_utc < starts_at_utc");
            t.HasCheckConstraint("ck_contest_revision", "revision > 0");
        });
        b.HasKey(c => c.Id); b.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(c => c.OwnerAccountId).HasColumnName("owner_account_id");
        b.HasOne<Account>().WithMany().HasForeignKey(c => c.OwnerAccountId).OnDelete(DeleteBehavior.Restrict);
        b.Property(c => c.Title).HasColumnName("title").HasMaxLength(120);
        b.Property(c => c.Description).HasColumnName("description").HasMaxLength(8000);
        b.Property(c => c.StartsAtUtc).HasColumnName("starts_at_utc");
        b.Property(c => c.EndsAtUtc).HasColumnName("ends_at_utc");
        b.Property(c => c.CreatedAtUtc).HasColumnName("created_at_utc");
        b.Property(c => c.PublishedAtUtc).HasColumnName("published_at_utc");
        b.Property(c => c.Revision).HasColumnName("revision").IsConcurrencyToken();
        b.HasIndex(c => new { c.StartsAtUtc, c.Id });
        b.HasMany(c => c.Problems).WithOne().HasForeignKey(p => p.ContestId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(c => c.Problems).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
    public void Configure(EntityTypeBuilder<ContestProblem> b)
    {
        b.ToTable("contest_problems", "contests", t => t.HasCheckConstraint("ck_contest_problem_order", "position BETWEEN 1 AND 26"));
        b.HasKey(p => p.Id); b.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();
        b.HasAlternateKey(p => new { p.ContestId, p.Id });
        b.Property(p => p.ContestId).HasColumnName("contest_id");
        b.Property(p => p.ProblemId).HasColumnName("problem_id");
        b.Property(p => p.VersionId).HasColumnName("version_id");
        b.Property(p => p.Order).HasColumnName("position");
        b.HasOne<Problem>().WithMany().HasForeignKey(p => p.ProblemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PublishedProblemVersion>().WithMany().HasForeignKey(p => p.VersionId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(p => new { p.ContestId, p.ProblemId }).IsUnique();
        b.HasIndex(p => new { p.ContestId, p.VersionId }).IsUnique();
        b.HasIndex(p => new { p.ContestId, p.Order }).IsUnique();
    }
    public void Configure(EntityTypeBuilder<ContestRegistration> b)
    {
        b.ToTable("registrations", "contests");
        b.HasKey(r => new { r.ContestId, r.AccountId });
        b.Property(r => r.ContestId).HasColumnName("contest_id");
        b.Property(r => r.AccountId).HasColumnName("account_id");
        b.Property(r => r.RegisteredAtUtc).HasColumnName("registered_at_utc");
        b.HasOne<Contest>().WithMany().HasForeignKey(r => r.ContestId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(r => r.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
    public void Configure(EntityTypeBuilder<ContestSubmission> b)
    {
        b.ToTable("submissions", "contests");
        b.HasKey(s => s.SubmissionId).HasName("PK_contest_submissions");
        b.Property(s => s.SubmissionId).HasColumnName("submission_id").ValueGeneratedNever();
        b.Property(s => s.ContestId).HasColumnName("contest_id");
        b.Property(s => s.AccountId).HasColumnName("account_id");
        b.Property(s => s.ContestProblemId).HasColumnName("contest_problem_id");
        b.HasOne<Submission>().WithMany().HasForeignKey(s => s.SubmissionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ContestRegistration>().WithMany().HasForeignKey(s => new { s.ContestId, s.AccountId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ContestProblem>().WithMany().HasForeignKey(s => new { s.ContestId, s.ContestProblemId })
            .HasPrincipalKey(p => new { p.ContestId, p.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
