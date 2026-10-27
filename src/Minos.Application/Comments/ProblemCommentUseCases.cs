using Minos.Application.Accounts;
using Minos.Domain;
using Minos.Domain.Accounts;
using Minos.Domain.Comments;

namespace Minos.Application.Comments;

public sealed class ProblemCommentUseCases(IProblemCommentRepository repository, AccountUseCases accounts,
    ICurrentAccount current, TimeProvider clock)
{
    public async Task<ProblemCommentPage> ListAsync(Guid problemId, Guid? parentId, CommentPageQuery query, CancellationToken token)
    {
        if (query.PageSize is < 1 or > 50 || query.After == Guid.Empty)
            throw new DomainValidationException(nameof(query.PageSize), "Request 1–50 comments with a valid cursor.");
        await RequirePublicAsync(problemId, token);
        Account? account = current.AccountId is null ? null : await accounts.RequireActiveAsync(token);
        if (parentId is Guid id)
        {
            var parent = await RequireCommentAsync(problemId, id, token);
            if (parent.ParentCommentId is not null) throw new ResourceNotFoundException();
        }
        var rows = await repository.ListAsync(problemId, parentId, query, token);
        var items = rows.Take(query.PageSize).Select(row => Map(row, account)).ToArray();
        return new(items, rows.Count > query.PageSize ? items[^1].Id : null);
    }

    public async Task<ProblemCommentDetails> CreateAsync(Guid problemId, CreateProblemComment request, CancellationToken token)
    {
        var account = await accounts.RequireActiveAsync(token);
        await RequirePublicAsync(problemId, token);
        if (!await repository.VersionBelongsToProblemAsync(problemId, request.PublishedVersionId, token))
            throw new DomainValidationException(nameof(request.PublishedVersionId), "Choose a published version of this problem.");
        var parent = request.ParentCommentId is Guid id ? await RequireCommentAsync(problemId, id, token) : null;
        var value = ProblemComment.Create(problemId, request.PublishedVersionId, account.Id, request.Body, request.IsSpoiler, clock.GetUtcNow(), parent);
        await repository.SaveAsync(value, token);
        return Map(value, account);
    }

    public async Task<ProblemCommentDetails> EditAsync(Guid problemId, Guid commentId, EditProblemComment request, CancellationToken token)
    {
        var account = await accounts.RequireActiveAsync(token);
        await RequirePublicAsync(problemId, token);
        var value = await RequireCommentAsync(problemId, commentId, token);
        if (value.AuthorAccountId != account.Id) throw new AccessDeniedException();
        RequireRevision(value, request.ExpectedRevision);
        value.Edit(request.Body, request.IsSpoiler, clock.GetUtcNow());
        await repository.SaveAsync(value, token);
        return Map(value, account);
    }

    public async Task RemoveAsync(Guid problemId, Guid commentId, long expectedRevision, bool moderate, CancellationToken token)
    {
        var account = await accounts.RequireActiveAsync(token);
        await RequirePublicAsync(problemId, token);
        var value = await RequireCommentAsync(problemId, commentId, token);
        if (moderate ? account.Role != AccountRole.Administrator : value.AuthorAccountId != account.Id)
            throw new AccessDeniedException();
        RequireRevision(value, expectedRevision);
        if (moderate) value.Hide(clock.GetUtcNow()); else value.Delete(clock.GetUtcNow());
        await repository.SaveAsync(value, token);
    }

    private async Task RequirePublicAsync(Guid problemId, CancellationToken token)
    {
        if (!await repository.IsPublicProblemAsync(problemId, token)) throw new ResourceNotFoundException();
    }
    private async Task<ProblemComment> RequireCommentAsync(Guid problemId, Guid commentId, CancellationToken token) =>
        await repository.FindAsync(problemId, commentId, token) ?? throw new ResourceNotFoundException();
    private static void RequireRevision(ProblemComment value, long expected)
    {
        if (value.Revision != expected) throw new ConcurrencyConflictException();
    }
    private static ProblemCommentDetails Map(ProblemComment value, Account account) => Map(new CommentReadRow(value.Id, value.ProblemId,
        value.PublishedVersionId, value.AuthorAccountId, value.ParentCommentId, account.DisplayName, value.Body, value.IsSpoiler,
        value.State, value.Revision, value.CreatedAtUtc, value.UpdatedAtUtc, 0), account);
    private static ProblemCommentDetails Map(CommentReadRow row, Account? account)
    {
        var visible = row.State == ProblemCommentState.Visible;
        var owned = visible && account?.Id == row.AuthorAccountId;
        return new(row.Id, row.ProblemId, row.PublishedVersionId, row.ParentCommentId, row.AuthorName,
            visible ? row.Body : null, visible && row.IsSpoiler, row.State, row.Revision, row.CreatedAtUtc, row.UpdatedAtUtc,
            row.ReplyCount, owned, owned, visible && account?.Role == AccountRole.Administrator);
    }
}
