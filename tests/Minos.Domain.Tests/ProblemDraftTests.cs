using Minos.Domain;
using Minos.Domain.Problems;
using Xunit;

namespace Minos.Domain.Tests;

public sealed class ProblemDraftTests
{
    [Fact]
    public void Draft_requires_and_preserves_its_owner()
    {
        Assert.Throws<DomainValidationException>(() => ProblemDraft.Create(Guid.Empty, "Title", "Statement", DateTimeOffset.UtcNow));
        var owner = Guid.NewGuid();
        var draft = ProblemDraft.Create(owner, "Title", "Statement", DateTimeOffset.UtcNow);
        Assert.True(draft.IsOwnedBy(owner));
        Assert.False(draft.IsOwnedBy(Guid.NewGuid()));
        Assert.False(draft.IsOwnedBy(Guid.Empty));
    }
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\n ")]
    public void Whitespace_titles_are_rejected(string? title) =>
        Assert.Throws<DomainValidationException>(() => ProblemDraft.Create(Guid.NewGuid(), title, "Statement", DateTimeOffset.UtcNow));

    [Fact]
    public void Draft_preserves_statement_and_normalizes_title()
    {
        var instant = new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.Zero);
        var draft = ProblemDraft.Create(Guid.NewGuid(), "  A + B  ", "  Keep formatting\n", instant);
        Assert.NotEqual(Guid.Empty, draft.Id);
        Assert.Equal("A + B", draft.Title);
        Assert.Equal("  Keep formatting\n", draft.Statement);
        Assert.Equal(instant, draft.CreatedAtUtc);
    }

    [Fact]
    public void Oversized_drafts_and_non_utc_creation_are_rejected()
    {
        Assert.Throws<DomainValidationException>(() => ProblemDraft.Create(Guid.NewGuid(), new string('x', 121), "Statement", DateTimeOffset.UtcNow));
        Assert.Throws<DomainValidationException>(() => ProblemDraft.Create(Guid.NewGuid(), "Title", new string('x', 65537), DateTimeOffset.UtcNow));
        Assert.Throws<DomainValidationException>(() => ProblemDraft.Create(Guid.NewGuid(), "Title", " \n", DateTimeOffset.UtcNow));
        Assert.Throws<DomainValidationException>(() => ProblemDraft.Create(Guid.NewGuid(), "Title", "Statement", DateTimeOffset.Now.ToOffset(TimeSpan.FromHours(7))));
    }

    [Fact]
    public void Unicode_text_obeys_utf16_limits_at_the_exact_boundary()
    {
        var title = string.Concat(Enumerable.Repeat("🧩", 60));
        var statement = string.Concat(Enumerable.Repeat("🧩", 32768));
        var draft = ProblemDraft.Create(Guid.NewGuid(), title, statement, DateTimeOffset.UtcNow);
        Assert.Equal(title, draft.Title);
        Assert.Equal(statement, draft.Statement);
        Assert.Throws<DomainValidationException>(() => ProblemDraft.Create(Guid.NewGuid(), title + "🧩", "Statement", DateTimeOffset.UtcNow));
        Assert.Throws<DomainValidationException>(() => ProblemDraft.Create(Guid.NewGuid(), "Title", statement + "🧩", DateTimeOffset.UtcNow));
    }
}
