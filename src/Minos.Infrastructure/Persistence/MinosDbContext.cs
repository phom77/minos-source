using Microsoft.EntityFrameworkCore;
using Minos.Domain.Problems;

namespace Minos.Infrastructure.Persistence;

public sealed class MinosDbContext(DbContextOptions<MinosDbContext> options) : DbContext(options)
{
    public DbSet<ProblemDraft> ProblemDrafts => Set<ProblemDraft>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var draft = modelBuilder.Entity<ProblemDraft>();
        // Match .NET's whitespace set so direct SQL cannot persist whitespace-only text.
        const string whitespace = "E' \\t\\n\\r\\f\\v\\u0085\\u00a0\\u1680\\u2000\\u2001\\u2002\\u2003\\u2004\\u2005\\u2006\\u2007\\u2008\\u2009\\u200a\\u2028\\u2029\\u202f\\u205f\\u3000'";
        // PostgreSQL length counts Unicode scalars; Domain limits count UTF-16 code units.
        static string Utf16Length(string column) => $"(length({column}) + length(regexp_replace({column}, U&'[^\\+010000-\\+10FFFF]', '', 'g')))";
        draft.ToTable("problem_drafts", "problems", table =>
        {
            table.HasCheckConstraint("ck_problem_drafts_title", $"{Utf16Length("title")} BETWEEN 1 AND 120 AND length(btrim(title, {whitespace})) > 0 AND title = btrim(title, {whitespace})");
            table.HasCheckConstraint("ck_problem_drafts_statement", $"{Utf16Length("statement")} BETWEEN 1 AND 65536 AND length(btrim(statement, {whitespace})) > 0");
        });
        draft.HasKey(x => x.Id);
        draft.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        draft.Property(x => x.Title).HasColumnName("title").HasMaxLength(ProblemDraft.MaximumTitleLength).IsRequired();
        draft.Property(x => x.Statement).HasColumnName("statement").HasMaxLength(ProblemDraft.MaximumStatementLength).IsRequired();
        draft.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
    }
}
