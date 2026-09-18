using Minos.Application.Problems;

namespace Minos.Api.Problems;

internal static class ProblemDraftEndpoints
{
    public static void MapDevelopmentProblemDraftEndpoints(this WebApplication app)
    {
        var drafts = app.MapGroup("/_development/problem-drafts");
        drafts.MapPost("/", async (CreateProblemDraftRequest request, ProblemDraftUseCases useCases, CancellationToken token) =>
        {
            var draft = await useCases.CreateAsync(new CreateProblemDraft(request.Title, request.Statement), token);
            return Results.Created($"/_development/problem-drafts/{draft.Id}", new ProblemDraftResponse(draft.Id, draft.Title, draft.Statement, draft.CreatedAtUtc));
        });
        drafts.MapGet("/{id:guid}", async (Guid id, ProblemDraftUseCases useCases, CancellationToken token) =>
        {
            var draft = await useCases.FindAsync(id, token);
            return draft is null ? Results.NotFound() : Results.Ok(new ProblemDraftResponse(draft.Id, draft.Title, draft.Statement, draft.CreatedAtUtc));
        });
    }
}
