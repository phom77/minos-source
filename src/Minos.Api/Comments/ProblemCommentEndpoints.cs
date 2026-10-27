using Microsoft.AspNetCore.Mvc;
using Minos.Application.Comments;

namespace Minos.Api.Comments;

public sealed record CreateProblemCommentRequest(Guid PublishedVersionId, string? Body, bool IsSpoiler = false, Guid? ParentCommentId = null);
public sealed record EditProblemCommentRequest(long ExpectedRevision, string? Body, bool IsSpoiler = false);
public sealed record RemoveProblemCommentRequest(long ExpectedRevision);
public sealed record ProblemCommentResponse(Guid Id, Guid ProblemId, Guid PublishedVersionId, Guid? ParentCommentId,
    string AuthorName, string? Body, bool IsSpoiler, string State, long Revision,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, int ReplyCount, bool CanEdit, bool CanDelete, bool CanHide);
public sealed record ProblemCommentPageResponse(IReadOnlyList<ProblemCommentResponse> Items, Guid? NextCursor);

internal static class ProblemCommentEndpoints
{
    internal static void MapProblemCommentEndpoints(this WebApplication app)
    {
        var comments = app.MapGroup("/api/problems/{problemId:guid}/comments");
        comments.AddEndpointFilter(async (context, next) =>
        {
            // Capabilities depend on the current account; never share these responses between users.
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            return await next(context);
        });
        comments.MapGet("", async (Guid problemId, Guid? after, int? pageSize, ProblemCommentUseCases cases, CancellationToken token) =>
            Results.Ok(Map(await cases.ListAsync(problemId, null, new(after, pageSize ?? 20), token))));
        comments.MapGet("/{commentId:guid}/replies", async (Guid problemId, Guid commentId, Guid? after, int? pageSize,
            ProblemCommentUseCases cases, CancellationToken token) =>
            Results.Ok(Map(await cases.ListAsync(problemId, commentId, new(after, pageSize ?? 20), token))));
        comments.MapPost("", async (Guid problemId, CreateProblemCommentRequest request, ProblemCommentUseCases cases, CancellationToken token) =>
            Results.Created($"/api/problems/{problemId}/comments", Map(await cases.CreateAsync(problemId,
                new(request.PublishedVersionId, request.Body, request.IsSpoiler, request.ParentCommentId), token))))
            .RequireAuthorization().RequireRateLimiting("comments").WithMetadata(new RequestSizeLimitAttribute(32 * 1024));
        comments.MapPut("/{commentId:guid}", async (Guid problemId, Guid commentId, EditProblemCommentRequest request,
            ProblemCommentUseCases cases, CancellationToken token) =>
            Results.Ok(Map(await cases.EditAsync(problemId, commentId, new(request.ExpectedRevision, request.Body, request.IsSpoiler), token))))
            .RequireAuthorization().RequireRateLimiting("comments").WithMetadata(new RequestSizeLimitAttribute(32 * 1024));
        // POST actions carry the expected revision and use the existing CSRF boundary.
        comments.MapPost("/{commentId:guid}/delete", async (Guid problemId, Guid commentId, RemoveProblemCommentRequest request,
            ProblemCommentUseCases cases, CancellationToken token) =>
        {
            await cases.RemoveAsync(problemId, commentId, request.ExpectedRevision, false, token);
            return Results.NoContent();
        }).RequireAuthorization().RequireRateLimiting("comments");
        comments.MapPost("/{commentId:guid}/hide", async (Guid problemId, Guid commentId, RemoveProblemCommentRequest request,
            ProblemCommentUseCases cases, CancellationToken token) =>
        {
            await cases.RemoveAsync(problemId, commentId, request.ExpectedRevision, true, token);
            return Results.NoContent();
        }).RequireAuthorization().RequireRateLimiting("comments");
    }

    private static ProblemCommentPageResponse Map(ProblemCommentPage page) => new(page.Items.Select(Map).ToArray(), page.NextCursor);
    private static ProblemCommentResponse Map(ProblemCommentDetails value) => new(value.Id, value.ProblemId, value.PublishedVersionId,
        value.ParentCommentId, value.AuthorName, value.Body, value.IsSpoiler, value.State.ToString(), value.Revision,
        value.CreatedAtUtc, value.UpdatedAtUtc, value.ReplyCount, value.CanEdit, value.CanDelete, value.CanHide);
}
