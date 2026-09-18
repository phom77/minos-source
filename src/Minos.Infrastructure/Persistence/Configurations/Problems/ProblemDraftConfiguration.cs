using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Minos.Domain.Accounts;
using Minos.Domain.Problems;

namespace Minos.Infrastructure.Persistence.Configurations.Problems;

internal sealed class ProblemDraftConfiguration : IEntityTypeConfiguration<ProblemDraft>
{
    public void Configure(EntityTypeBuilder<ProblemDraft> draft)
    {
        draft.ToTable("problem_drafts", "problems", table =>
        {
            table.HasCheckConstraint("ck_problem_drafts_title", $"{PostgresTextConstraints.Utf16Length("title")} BETWEEN 1 AND 120 AND length(btrim(title, {PostgresTextConstraints.Whitespace})) > 0 AND title = btrim(title, {PostgresTextConstraints.Whitespace})");
            table.HasCheckConstraint("ck_problem_drafts_statement", $"{PostgresTextConstraints.Utf16Length("statement")} BETWEEN 1 AND 65536 AND length(btrim(statement, {PostgresTextConstraints.Whitespace})) > 0");
        });
        draft.HasKey(x => x.Id);
        draft.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        draft.Property(x => x.OwnerAccountId).HasColumnName("owner_account_id").IsRequired();
        draft.HasOne<Account>().WithMany().HasForeignKey(x => x.OwnerAccountId).OnDelete(DeleteBehavior.Restrict);
        draft.Property(x => x.Title).HasColumnName("title").HasMaxLength(ProblemDraft.MaximumTitleLength).IsRequired();
        draft.Property(x => x.Statement).HasColumnName("statement").HasMaxLength(ProblemDraft.MaximumStatementLength).IsRequired();
        draft.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
    }
}
