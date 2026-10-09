using Microsoft.AspNetCore.Mvc;
using Minos.Application.Evaluation;

namespace Minos.Api.Submissions;

internal static class SubmissionEndpoints
{
    private const long BodyLimit = 128 * 1024;

    public static void MapSubmissionEndpoints(this WebApplication app)
    {
        app.MapPost("/api/problems/{problemId:guid}/submissions", CreateAsync)
            .RequireAuthorization()
            .WithMetadata(new RequestSizeLimitAttribute(BodyLimit));
        app.MapGet("/api/submissions/{id:guid}", FindAsync).RequireAuthorization();
        app.MapGet("/api/me/submissions", ListMineAsync).RequireAuthorization();
    }

    private static async Task<IResult> CreateAsync(
        Guid problemId,
        CreateSubmissionRequest request,
        SubmissionUseCases useCases,
        CancellationToken token)
    {
        var submission = await useCases.CreateAsync(problemId, new(request.Language, request.Source), token);
        return Results.Created($"/api/submissions/{submission.Id}", Map(submission));
    }

    private static async Task<IResult> FindAsync(
        Guid id,
        SubmissionUseCases useCases,
        CancellationToken token) => Results.Ok(Map(await useCases.FindAsync(id, token)));

    private static async Task<IResult> ListMineAsync(
        int page,
        int pageSize,
        SubmissionUseCases useCases,
        CancellationToken token)
    {
        if (page == 0) page = 1;
        if (pageSize == 0) pageSize = 20;
        var result = await useCases.ListMineAsync(page, pageSize, token);
        return Results.Ok(new SubmissionPageResponse(result.Items.Select(item => new SubmissionSummaryResponse(
            item.Id,
            item.ProblemId,
            item.ProblemVersionId,
            item.Status.ToString(),
            item.Verdict?.ToString(),
            item.CreatedAtUtc,
            item.CompletedAtUtc)).ToArray(), result.Page, result.PageSize, result.Total));
    }

    private static SubmissionResponse Map(SubmissionDetails submission) => new(
        submission.Id,
        submission.ProblemId,
        submission.ProblemVersionId,
        submission.Language,
        submission.ToolchainId,
        submission.Source,
        submission.Status.ToString(),
        submission.CreatedAtUtc,
        submission.CompletedAtUtc,
        submission.Attempts.Select(attempt => new SubmissionAttemptResponse(
            attempt.Id,
            attempt.Number,
            attempt.Status.ToString(),
            attempt.FailureKind?.ToString(),
            attempt.StartedAtUtc,
            attempt.FinishedAtUtc)).ToArray(),
        submission.Result is null ? null : new SubmissionResultResponse(
            submission.Result.AttemptId,
            submission.Result.Verdict?.ToString(),
            submission.Result.CompilerDiagnostics,
            submission.Result.MaximumCpuTimeMilliseconds,
            submission.Result.MaximumWallTimeMilliseconds,
            submission.Result.MaximumMemoryBytes,
            submission.Result.CompletedAtUtc,
            submission.Result.Tests.Select(test => new SubmissionTestResultResponse(
                test.Order,
                test.Status.ToString(),
                test.CpuTimeMilliseconds,
                test.WallTimeMilliseconds,
                test.MemoryPeakBytes)).ToArray(),
            submission.Result.TestSummary is null ? null : new SubmissionTestSummaryResponse(
                submission.Result.TestSummary.TotalTests,
                submission.Result.TestSummary.ExecutedTests,
                submission.Result.TestSummary.PassedTests,
                submission.Result.TestSummary.FailedTests)),
        submission.Configuration is null ? null : new SubmissionJudgingConfigurationResponse(
            submission.Configuration.TimeLimitMilliseconds,
            submission.Configuration.MemoryLimitMegabytes,
            submission.Configuration.Checker,
            submission.Configuration.TestCount));
}
