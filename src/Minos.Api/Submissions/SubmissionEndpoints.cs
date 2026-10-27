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
        app.MapGet("/api/submissions/{id:guid}/performance", PerformanceAsync).RequireAuthorization();
        app.MapGet("/api/me/submissions", ListMineAsync).RequireAuthorization();
    }

    private static async Task<IResult> CreateAsync(
        Guid problemId,
        CreateSubmissionRequest request,
        SubmissionUseCases useCases,
        HttpContext context,
        ILoggerFactory logging,
        CancellationToken token)
    {
        var submission = await useCases.CreateAsync(problemId, new(request.Language, request.Source, request.RequestKey), token);
        logging.CreateLogger("Minos.SubmissionAdmission").LogInformation(
            "Submission request resolved to {SubmissionId}; TraceId {TraceId}; kind practice", submission.Id, context.TraceIdentifier);
        return Results.Created($"/api/submissions/{submission.Id}", Map(submission));
    }

    private static async Task<IResult> FindAsync(
        Guid id,
        SubmissionUseCases useCases,
        CancellationToken token) => Results.Ok(Map(await useCases.FindAsync(id, token)));

    private static async Task<IResult> PerformanceAsync(Guid id, SubmissionPerformanceUseCases useCases,
        HttpContext context, CancellationToken token)
    {
        var result = await useCases.ReadAsync(id, token);
        context.Response.Headers.CacheControl = "no-store";
        static PerformanceMetricResponse? Metric(PerformanceMetric? metric) => metric is null ? null : new(metric.Value,
            metric.BeatsPercentage, metric.EqualCount, metric.RangeMaximum, metric.Buckets.Select(bucket => new PerformanceBucketResponse(bucket.Index, bucket.Count)).ToArray());
        return Results.Ok(new SubmissionPerformanceResponse(id, result.Status, result.SampleCount, result.MinimumSampleCount,
            Metric(result.Runtime), Metric(result.Memory)));
    }

    private static async Task<IResult> ListMineAsync(
        int page,
        int pageSize,
        Guid? problemId,
        SubmissionUseCases useCases,
        HttpContext context,
        CancellationToken token)
    {
        if (page == 0) page = 1;
        if (pageSize == 0) pageSize = 20;
        var result = await useCases.ListMineAsync(page, pageSize, token, problemId);
        context.Response.Headers.CacheControl = "no-store";
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
                submission.Result.TestSummary.FailedTests), submission.Result.ComparisonEligible),
        submission.Configuration is null ? null : new SubmissionJudgingConfigurationResponse(
            submission.Configuration.TimeLimitMilliseconds,
            submission.Configuration.MemoryLimitMegabytes,
            submission.Configuration.Checker,
            submission.Configuration.TestCount));
}
