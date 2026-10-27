using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Minos.Domain.Accounts;
using Minos.Domain.Comments;
using Minos.Domain.Problems;

namespace Minos.Infrastructure.Persistence.Configurations;

internal sealed class ProblemCommentConfiguration : IEntityTypeConfiguration<ProblemComment>
{
    public void Configure(EntityTypeBuilder<ProblemComment> value)
    {
        value.ToTable("problem_comments", "problems", table =>
        {
            table.HasCheckConstraint("ck_problem_comments_body", $"""
                (state = 0 AND {PostgresTextConstraints.Utf16Length("body")} BETWEEN 1 AND {ProblemComment.MaximumBodyLength}
                    AND body = btrim(body, {PostgresTextConstraints.Whitespace})
                    AND length(btrim(body, {PostgresTextConstraints.Whitespace})) > 0
                    AND body !~ '[\x01-\x08\x0B\x0C\x0E-\x1F\x7F-\x9F]')
                OR (state IN (1,2) AND body = '' AND NOT is_spoiler)
                """);
            table.HasCheckConstraint("ck_problem_comments_revision", "revision > 0 AND updated_at_utc >= created_at_utc");
            table.HasCheckConstraint("ck_problem_comments_parent", "parent_comment_id IS NULL OR parent_comment_id <> id");
        });
        value.HasKey(item => item.Id);
        value.HasAlternateKey(item => new { item.Id, item.ProblemId });
        value.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
        value.Property(item => item.ProblemId).HasColumnName("problem_id");
        value.Property(item => item.PublishedVersionId).HasColumnName("published_version_id");
        value.Property(item => item.AuthorAccountId).HasColumnName("author_account_id");
        value.Property(item => item.ParentCommentId).HasColumnName("parent_comment_id");
        value.Property(item => item.Body).HasColumnName("body").HasMaxLength(ProblemComment.MaximumBodyLength).IsRequired();
        value.Property(item => item.IsSpoiler).HasColumnName("is_spoiler");
        value.Property(item => item.State).HasColumnName("state").HasConversion<int>();
        value.Property(item => item.Revision).HasColumnName("revision").IsConcurrencyToken();
        value.Property(item => item.CreatedAtUtc).HasColumnName("created_at_utc");
        value.Property(item => item.UpdatedAtUtc).HasColumnName("updated_at_utc");
        value.HasOne<Problem>().WithMany().HasForeignKey(item => item.ProblemId).OnDelete(DeleteBehavior.Restrict);
        value.HasOne<PublishedProblemVersion>().WithMany().HasForeignKey(item => new { item.PublishedVersionId, item.ProblemId })
            .HasPrincipalKey(item => new { item.Id, item.ProblemId }).OnDelete(DeleteBehavior.Restrict);
        value.HasOne<Account>().WithMany().HasForeignKey(item => item.AuthorAccountId).OnDelete(DeleteBehavior.Restrict);
        value.HasOne<ProblemComment>().WithMany().HasForeignKey(item => new { item.ParentCommentId, item.ProblemId })
            .HasPrincipalKey(item => new { item.Id, item.ProblemId }).OnDelete(DeleteBehavior.Restrict);
        value.HasIndex(item => new { item.ProblemId, item.ParentCommentId, item.CreatedAtUtc, item.Id });
        value.HasIndex(item => item.AuthorAccountId);
    }
}
