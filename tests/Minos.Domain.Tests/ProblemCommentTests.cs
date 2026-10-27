using Minos.Domain;
using Minos.Domain.Comments;
using Xunit;

namespace Minos.Domain.Tests;

public sealed class ProblemCommentTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static ProblemComment Root(string body = "A useful question.") =>
        ProblemComment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), body, true, Now);

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData(" \t\n\u00a0")] [InlineData("unsafe\0body")] [InlineData("unsafe\u0085body")]
    public void Empty_or_control_content_is_rejected(string? body) =>
        Assert.Throws<DomainValidationException>(() => Root(body!));

    [Fact]
    public void Body_is_trimmed_and_utf16_bound_includes_non_bmp_text()
    {
        Assert.Equal("Question", Root(" \nQuestion\t").Body);
        Assert.Equal(4000, Root(string.Concat(Enumerable.Repeat("🧩", 2000))).Body.Length);
        Assert.Throws<DomainValidationException>(() => Root(new string('a', 4001)));
    }

    [Fact]
    public void Replies_require_a_visible_root_in_the_same_problem()
    {
        var parent = Root();
        var reply = ProblemComment.Create(parent.ProblemId, parent.PublishedVersionId, Guid.NewGuid(), "Reply", false, Now, parent);
        Assert.Equal(parent.Id, reply.ParentCommentId);
        Assert.Throws<DomainValidationException>(() => ProblemComment.Create(Guid.NewGuid(), parent.PublishedVersionId, Guid.NewGuid(), "Reply", false, Now, parent));
        Assert.Throws<DomainValidationException>(() => ProblemComment.Create(parent.ProblemId, parent.PublishedVersionId, Guid.NewGuid(), "Nested", false, Now, reply));
        parent.Delete(Now.AddSeconds(1));
        Assert.Throws<DomainValidationException>(() => ProblemComment.Create(parent.ProblemId, parent.PublishedVersionId, Guid.NewGuid(), "Reply", false, Now.AddSeconds(2), parent));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Removal_erases_body_and_spoiler_and_prevents_further_changes(bool hide)
    {
        var value = Root();
        value.Edit("Updated", true, Now.AddSeconds(1));
        Assert.Equal(2, value.Revision);
        if (hide) value.Hide(Now.AddSeconds(2)); else value.Delete(Now.AddSeconds(2));
        Assert.Equal(hide ? ProblemCommentState.Hidden : ProblemCommentState.Deleted, value.State);
        Assert.Empty(value.Body);
        Assert.False(value.IsSpoiler);
        Assert.Equal(3, value.Revision);
        Assert.Throws<DomainValidationException>(() => value.Edit("Restore", false, Now.AddSeconds(3)));
        Assert.Throws<DomainValidationException>(() => value.Delete(Now.AddSeconds(3)));
    }

    [Fact]
    public void Invalid_time_or_edit_does_not_partially_change_the_aggregate()
    {
        var value = Root();
        Assert.Throws<DomainValidationException>(() => value.Edit("Changed", false, Now.ToOffset(TimeSpan.FromHours(7))));
        Assert.Throws<DomainValidationException>(() => value.Edit("Changed", false, Now.AddSeconds(-1)));
        Assert.Throws<DomainValidationException>(() => value.Edit(" ", false, Now.AddSeconds(1)));
        Assert.Equal(1, value.Revision);
        Assert.Equal("A useful question.", value.Body);
        Assert.True(value.IsSpoiler);
        Assert.Throws<DomainValidationException>(() => ProblemComment.Create(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), "Body", false, Now));
    }
}
