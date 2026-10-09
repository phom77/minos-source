using Microsoft.AspNetCore.Mvc;
using Minos.Application.Contests;
using Minos.Api.Problems;
using Minos.Application.Evaluation;

namespace Minos.Api.Contests;

public sealed record SaveContestRequest(string? Title, string? Description, DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc, IReadOnlyList<Guid>? VersionIds, long? Revision);
public sealed record PublishContestRequest(long Revision);
public sealed record ContestSubmissionRequest(string? Language, string? Source);
public sealed record ContestSummaryResponse(Guid Id, string Title, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc, string State, int ProblemCount);
public sealed record ContestPageResponse(IReadOnlyList<ContestSummaryResponse> Items, int Page, int PageSize, int Total);
public sealed record ContestProblemResponse(Guid Id, Guid ProblemId, Guid VersionId, int Order, string Title);
public sealed record ContestResponse(Guid Id, string Title, string Description, DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc, string State, long Revision, bool CanManage, bool IsRegistered, bool CanRegister,
    bool CanSubmit, int ProblemCount, DateTimeOffset ServerTimeUtc, IReadOnlyList<ContestProblemResponse> Problems);
public sealed record ContestSubmissionResponse(Guid Id, Guid ContestProblemId, string Status, string? Verdict, DateTimeOffset CreatedAtUtc);
public sealed record ContestSubmissionPageResponse(IReadOnlyList<ContestSubmissionResponse> Items, int Page, int PageSize, int Total);
public sealed record CreatedContestSubmissionResponse(Guid Id);

internal static class ContestEndpoints
{
    public static void MapContestEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/contests");
        group.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            return await next(context);
        });
        group.MapGet("/", async (int? page, int? pageSize, ContestUseCases cases, CancellationToken ct) =>
            Results.Ok(Map(await cases.ListAsync(false, page ?? 1, pageSize ?? 20, ct))));
        group.MapGet("/mine", async (int? page, int? pageSize, ContestUseCases cases, CancellationToken ct) =>
            Results.Ok(Map(await cases.ListAsync(true, page ?? 1, pageSize ?? 20, ct)))).RequireAuthorization();
        group.MapGet("/{id:guid}", async (Guid id, ContestUseCases cases, CancellationToken ct) => Results.Ok(Map(await cases.FindAsync(id, ct))));
        group.MapPost("/", async (SaveContestRequest request, ContestUseCases cases, CancellationToken ct) =>
        {
            var contest = await cases.CreateAsync(Command(request), ct);
            return Results.Created($"/api/contests/{contest.Id}", Map(contest));
        }).RequireAuthorization().WithMetadata(new RequestSizeLimitAttribute(64 * 1024));
        group.MapPut("/{id:guid}", async (Guid id, SaveContestRequest request, ContestUseCases cases, CancellationToken ct) =>
            Results.Ok(Map(await cases.UpdateAsync(id, Command(request), ct))))
            .RequireAuthorization().WithMetadata(new RequestSizeLimitAttribute(64 * 1024));
        group.MapPost("/{id:guid}/publish", async (Guid id, PublishContestRequest request, ContestUseCases cases, CancellationToken ct) =>
            Results.Ok(Map(await cases.PublishAsync(id, request.Revision, ct))))
            .RequireAuthorization().WithMetadata(new RequestSizeLimitAttribute(1024));
        group.MapPut("/{id:guid}/registration", async (Guid id, ContestUseCases cases, CancellationToken ct) =>
            Results.Ok(Map(await cases.RegisterAsync(id, ct)))).RequireAuthorization();
        group.MapGet("/{id:guid}/problems/{problemId:guid}", async (Guid id, Guid problemId, ContestUseCases cases, CancellationToken ct) =>
        {
            var p = await cases.ProblemAsync(id, problemId, ct);
            return Results.Ok(new PublishedProblemResponse(p.ProblemId, p.VersionId, p.VersionNumber, p.Title, p.Statement,
                p.TimeLimitMilliseconds, p.MemoryLimitMegabytes, p.Checker.ToString(), p.PrivateTestCount, p.PublishedAtUtc,
                p.Samples.Select(s => new ProblemSampleResponse(s.Input ?? "", s.Output ?? "")).ToArray()));
        }).RequireAuthorization();
        group.MapPost("/{id:guid}/problems/{problemId:guid}/submissions", async (Guid id, Guid problemId,
            ContestSubmissionRequest request, ContestUseCases cases, CancellationToken ct) =>
        {
            var submission = await cases.SubmitAsync(id, problemId, new(request.Language, request.Source), ct);
            return Results.Created($"/api/submissions/{submission}", new CreatedContestSubmissionResponse(submission));
        }).RequireAuthorization().WithMetadata(new RequestSizeLimitAttribute(Minos.Domain.Evaluation.Submission.MaximumSourceBytes * 6L + 4096));
        group.MapGet("/{id:guid}/submissions/mine", async (Guid id, int? page, int? pageSize, ContestUseCases cases, CancellationToken ct) =>
        {
            var result = await cases.SubmissionsAsync(id, page ?? 1, pageSize ?? 20, ct);
            return Results.Ok(new ContestSubmissionPageResponse(result.Items.Select(s => new ContestSubmissionResponse(
                s.Id, s.ContestProblemId, s.Status.ToString(), s.Verdict?.ToString(), s.CreatedAtUtc)).ToArray(), result.Page, result.PageSize, result.Total));
        }).RequireAuthorization();
    }
    private static SaveContest Command(SaveContestRequest r) => new(r.Title, r.Description,
        r.StartsAtUtc.ToUniversalTime(), r.EndsAtUtc.ToUniversalTime(), r.VersionIds ?? [], r.Revision);
    private static ContestPageResponse Map(ContestPage page) => new(page.Items.Select(c => new ContestSummaryResponse(
        c.Id, c.Title, c.StartsAtUtc, c.EndsAtUtc, c.State.ToString(), c.ProblemCount)).ToArray(), page.Page, page.PageSize, page.Total);
    private static ContestResponse Map(ContestDetails c) => new(c.Id, c.Title, c.Description, c.StartsAtUtc,
        c.EndsAtUtc, c.State.ToString(), c.Revision, c.CanManage, c.IsRegistered, c.CanRegister, c.CanSubmit,
        c.ProblemCount, c.ServerTimeUtc, c.Problems.Select(p => new ContestProblemResponse(p.Id, p.ProblemId, p.VersionId, p.Order, p.Title)).ToArray());
}
