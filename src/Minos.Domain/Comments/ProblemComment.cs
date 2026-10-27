namespace Minos.Domain.Comments;

public enum ProblemCommentState { Visible, Deleted, Hidden }

public sealed class ProblemComment
{
    public const int MaximumBodyLength = 4000;
    private ProblemComment() { }
    public Guid Id { get; private set; }
    public Guid ProblemId { get; private set; }
    public Guid PublishedVersionId { get; private set; }
    public Guid AuthorAccountId { get; private set; }
    public Guid? ParentCommentId { get; private set; }
    public string Body { get; private set; } = "";
    public bool IsSpoiler { get; private set; }
    public ProblemCommentState State { get; private set; }
    public long Revision { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static ProblemComment Create(Guid problemId, Guid publishedVersionId, Guid authorAccountId,
        string? body, bool isSpoiler, DateTimeOffset now, ProblemComment? parent = null)
    {
        if (problemId == Guid.Empty || publishedVersionId == Guid.Empty || authorAccountId == Guid.Empty)
            throw new DomainValidationException(nameof(ProblemId), "A comment requires a problem, published version and author.");
        EnsureUtc(now);
        if (parent is not null && (parent.ProblemId != problemId || parent.ParentCommentId is not null || parent.State != ProblemCommentState.Visible))
            throw new DomainValidationException(nameof(ParentCommentId), "Reply to an available top-level comment on this problem.");
        return new()
        {
            Id = Guid.NewGuid(), ProblemId = problemId, PublishedVersionId = publishedVersionId, AuthorAccountId = authorAccountId,
            ParentCommentId = parent?.Id, Body = ValidateBody(body), IsSpoiler = isSpoiler, Revision = 1,
            CreatedAtUtc = now, UpdatedAtUtc = now
        };
    }

    public void Edit(string? body, bool isSpoiler, DateTimeOffset now)
    {
        EnsureVisible();
        var validated = ValidateBody(body);
        Touch(now);
        Body = validated;
        IsSpoiler = isSpoiler;
    }

    public void Delete(DateTimeOffset now) => Remove(ProblemCommentState.Deleted, now);
    public void Hide(DateTimeOffset now) => Remove(ProblemCommentState.Hidden, now);

    private void Remove(ProblemCommentState state, DateTimeOffset now)
    {
        EnsureVisible();
        Touch(now);
        Body = "";
        IsSpoiler = false;
        State = state;
    }

    private static string ValidateBody(string? body)
    {
        var value = body?.Trim() ?? "";
        if (value.Length is 0 or > MaximumBodyLength || value.Any(character => char.IsControl(character) && character is not ('\n' or '\r' or '\t')))
            throw new DomainValidationException(nameof(Body), "Write 1–4000 characters without unsupported control characters.");
        return value;
    }
    private void EnsureVisible()
    {
        if (State != ProblemCommentState.Visible)
            throw new DomainValidationException(nameof(State), "This comment is no longer available.");
    }
    private static void EnsureUtc(DateTimeOffset now)
    {
        if (now.Offset != TimeSpan.Zero) throw new DomainValidationException(nameof(UpdatedAtUtc), "Comment timestamps must use UTC.");
    }
    private void Touch(DateTimeOffset now)
    {
        EnsureUtc(now);
        if (now < CreatedAtUtc || now < UpdatedAtUtc)
            throw new DomainValidationException(nameof(UpdatedAtUtc), "Comment timestamps cannot move backwards.");
        Revision = checked(Revision + 1);
        UpdatedAtUtc = now;
    }
}
