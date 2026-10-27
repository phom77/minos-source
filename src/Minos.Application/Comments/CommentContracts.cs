using Minos.Domain.Comments;

namespace Minos.Application.Comments;

public sealed record CreateProblemComment(Guid PublishedVersionId, string? Body, bool IsSpoiler, Guid? ParentCommentId = null);
public sealed record EditProblemComment(long ExpectedRevision, string? Body, bool IsSpoiler);
public sealed record CommentPageQuery(Guid? After = null, int PageSize = 20);
public sealed record ProblemCommentPage(IReadOnlyList<ProblemCommentDetails> Items, Guid? NextCursor);
public sealed record ProblemCommentDetails(Guid Id, Guid ProblemId, Guid PublishedVersionId, Guid? ParentCommentId,
    string AuthorName, string? Body, bool IsSpoiler, ProblemCommentState State, long Revision,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, int ReplyCount, bool CanEdit, bool CanDelete, bool CanHide);
public sealed record CommentReadRow(Guid Id, Guid ProblemId, Guid PublishedVersionId, Guid AuthorAccountId, Guid? ParentCommentId,
    string AuthorName, string? Body, bool IsSpoiler, ProblemCommentState State, long Revision,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, int ReplyCount);

public interface IProblemCommentRepository
{
    Task<bool> IsPublicProblemAsync(Guid problemId, CancellationToken token);
    Task<bool> VersionBelongsToProblemAsync(Guid problemId, Guid versionId, CancellationToken token);
    Task<ProblemComment?> FindAsync(Guid problemId, Guid commentId, CancellationToken token);
    Task<IReadOnlyList<CommentReadRow>> ListAsync(Guid problemId, Guid? parentId, CommentPageQuery query, CancellationToken token);
    Task SaveAsync(ProblemComment comment, CancellationToken token);
}
