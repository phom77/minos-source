using Microsoft.EntityFrameworkCore;
using Minos.Application;
using Minos.Application.Comments;
using Minos.Domain;
using Minos.Domain.Comments;

namespace Minos.Infrastructure.Persistence;

internal sealed class ProblemCommentRepository(MinosDbContext database) : IProblemCommentRepository
{
    public Task<bool> IsPublicProblemAsync(Guid problemId, CancellationToken token) =>
        database.Problems.AsNoTracking().AnyAsync(problem => problem.Id == problemId && !problem.IsArchived && problem.LatestPublishedVersion > 0, token);
    public Task<bool> VersionBelongsToProblemAsync(Guid problemId, Guid versionId, CancellationToken token) =>
        database.PublishedProblemVersions.AnyAsync(version => version.ProblemId == problemId && version.Id == versionId, token);
    public Task<ProblemComment?> FindAsync(Guid problemId, Guid commentId, CancellationToken token) =>
        database.ProblemComments.SingleOrDefaultAsync(value => value.ProblemId == problemId && value.Id == commentId, token);

    public async Task<IReadOnlyList<CommentReadRow>> ListAsync(Guid problemId, Guid? parentId, CommentPageQuery query, CancellationToken token)
    {
        var comments = database.ProblemComments.AsNoTracking().Where(value => value.ProblemId == problemId && value.ParentCommentId == parentId);
        if (query.After is Guid after)
        {
            var anchor = await comments.Where(value => value.Id == after).Select(value => new { value.CreatedAtUtc, value.Id }).SingleOrDefaultAsync(token)
                ?? throw new DomainValidationException(nameof(query.After), "This comment cursor is not available in this thread.");
            comments = comments.Where(value => value.CreatedAtUtc < anchor.CreatedAtUtc || value.CreatedAtUtc == anchor.CreatedAtUtc && value.Id.CompareTo(anchor.Id) < 0);
        }
        return await comments.Join(database.Accounts, value => value.AuthorAccountId, account => account.Id,
            (value, account) => new { Comment = value, account.DisplayName })
            .OrderByDescending(row => row.Comment.CreatedAtUtc).ThenByDescending(row => row.Comment.Id)
            .Take(query.PageSize + 1)
            .Select(row => new CommentReadRow(row.Comment.Id, row.Comment.ProblemId, row.Comment.PublishedVersionId, row.Comment.AuthorAccountId,
                row.Comment.ParentCommentId, row.DisplayName, row.Comment.State == ProblemCommentState.Visible ? row.Comment.Body : null,
                row.Comment.State == ProblemCommentState.Visible && row.Comment.IsSpoiler, row.Comment.State, row.Comment.Revision,
                row.Comment.CreatedAtUtc, row.Comment.UpdatedAtUtc,
                database.ProblemComments.Count(reply => reply.ParentCommentId == row.Comment.Id && reply.ProblemId == problemId)))
            .ToArrayAsync(token);
    }

    public async Task SaveAsync(ProblemComment comment, CancellationToken token)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        // One lock order for writes, including replies and moderation. Archive/publication also lock this problem row.
        await database.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM problems.problems WHERE id = {comment.ProblemId} FOR UPDATE", token);
        if (!await IsPublicProblemAsync(comment.ProblemId, token)) throw new ResourceNotFoundException();
        var inserting = database.Entry(comment).State == EntityState.Detached;
        if (inserting && comment.ParentCommentId is Guid parent)
        {
            if (!await database.ProblemComments.AsNoTracking().AnyAsync(value => value.ProblemId == comment.ProblemId && value.Id == parent
                && value.ParentCommentId == null && value.State == ProblemCommentState.Visible, token))
                throw new DomainValidationException(nameof(comment.ParentCommentId), "This comment can no longer receive replies.");
        }
        if (inserting) database.ProblemComments.Add(comment);
        try
        {
            await database.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }
        catch (DbUpdateConcurrencyException) { throw new ConcurrencyConflictException(); }
    }
}
