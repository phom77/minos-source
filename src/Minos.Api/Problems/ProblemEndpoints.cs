using Microsoft.AspNetCore.Mvc;
using Minos.Application.Problems;
using Minos.Domain.Problems;

namespace Minos.Api.Problems;

internal static class ProblemEndpoints
{
    private const long JsonBodyLimit = 256 * 1024;

    public static void MapProblemEndpoints(this WebApplication app)
    {
        app.MapProblemClassificationEndpoints();
        var authoring = app.MapGroup("/api/authoring/problems").RequireAuthorization();
        authoring.MapPost("/", CreateAsync).WithMetadata(new RequestSizeLimitAttribute(JsonBodyLimit));
        authoring.MapGet("/{id:guid}/draft", FindDraftAsync);
        authoring.MapPut("/{id:guid}/draft", UpdateDraftAsync).WithMetadata(new RequestSizeLimitAttribute(JsonBodyLimit));
        authoring.MapPost("/{id:guid}/draft/tests", AddTestAsync)
            .WithMetadata(new RequestSizeLimitAttribute(ProblemArtifactLimits.MaximumPackageBytes));
        authoring.MapDelete("/{id:guid}/draft/tests/{testCaseId:guid}", RemoveTestAsync);
        authoring.MapPost("/{id:guid}/publish", PublishAsync).WithMetadata(new RequestSizeLimitAttribute(JsonBodyLimit));
        authoring.MapPost("/{id:guid}/archive", ArchiveAsync);

        var catalog = app.MapGroup("/api/problems");
        catalog.MapGet("/", ListAsync);
        catalog.MapGet("/{id:guid}", FindLatestAsync);
        catalog.MapGet("/versions/{versionId:guid}", FindVersionAsync);
    }

    private static async Task<IResult> CreateAsync(
        CreateProblemRequest request,
        ProblemAuthoringUseCases useCases,
        CancellationToken token)
    {
        var problem = await useCases.CreateAsync(new(request.Title, request.Statement), token);
        return Results.Created($"/api/authoring/problems/{problem.Id}/draft", Map(problem));
    }

    private static async Task<IResult> FindDraftAsync(Guid id, ProblemAuthoringUseCases useCases, CancellationToken token) =>
        Results.Ok(Map(await useCases.FindDraftAsync(id, token)));

    private static async Task<IResult> UpdateDraftAsync(
        Guid id,
        UpdateProblemDraftRequest request,
        ProblemAuthoringUseCases useCases,
        CancellationToken token)
    {
        if (!string.Equals(request.Checker, nameof(BuiltInChecker.Exact), StringComparison.OrdinalIgnoreCase))
            throw new BadHttpRequestException("Invalid request.");
        var checker = BuiltInChecker.Exact;
        var samples = request.Samples?.Select(sample => new ProblemSampleContent(sample.Input, sample.Output)).ToArray() ?? [];
        return Results.Ok(Map(await useCases.UpdateDraftAsync(id,
            new(request.Title, request.Statement, request.TimeLimitMilliseconds, request.MemoryLimitMegabytes, checker, samples), token)));
    }

    private static async Task<IResult> AddTestAsync(
        Guid id,
        HttpRequest request,
        ProblemAuthoringUseCases useCases,
        CancellationToken token)
    {
        if (!string.Equals(request.ContentType, "application/zip", StringComparison.OrdinalIgnoreCase) ||
            request.ContentLength > ProblemArtifactLimits.MaximumPackageBytes)
            throw new BadHttpRequestException("Invalid test package.");
        return Results.Ok(Map(await useCases.AddTestCaseAsync(id, request.Body, token)));
    }

    private static async Task<IResult> RemoveTestAsync(
        Guid id,
        Guid testCaseId,
        ProblemAuthoringUseCases useCases,
        CancellationToken token) =>
        Results.Ok(Map(await useCases.RemoveTestCaseAsync(id, testCaseId, token)));

    private static async Task<IResult> PublishAsync(
        Guid id,
        PublishProblemRequest request,
        ProblemAuthoringUseCases useCases,
        CancellationToken token)
    {
        var version = await useCases.PublishAsync(id,
            new(request.StatementReviewed, request.SamplesVerified, request.ReferenceSolutionChecked), token);
        return Results.Created($"/api/problems/versions/{version.VersionId}", Map(version));
    }

    private static async Task<IResult> ArchiveAsync(Guid id, ProblemAuthoringUseCases useCases, CancellationToken token)
    {
        await useCases.ArchiveAsync(id, token);
        return Results.NoContent();
    }

    private static async Task<IResult> ListAsync(
        int page,
        int pageSize,
        string? q,
        string? difficulty,
        string? topic,
        string? status,
        string? sort,
        string? match,
        bool? difficultyIsNot,
        bool? topicIsNot,
        bool? statusIsNot,
        HttpResponse response,
        ProblemCatalogUseCases useCases,
        CancellationToken token)
    {
        response.Headers.CacheControl = "private, no-store";
        if (page == 0) page = 1;
        if (pageSize == 0) pageSize = 20;
        var result = await useCases.ListAsync(new(page, pageSize, q, ParseFilter<ProblemDifficultyFilter>(difficulty),
            topic, ParseFilter<ProblemSolveStatus>(status), ParseFilter<ProblemCatalogSort>(sort) ?? ProblemCatalogSort.Number,
            ParseFilter<ProblemFilterMatch>(match) ?? ProblemFilterMatch.All, difficultyIsNot ?? false, topicIsNot ?? false, statusIsNot ?? false), token);
        return Results.Ok(new PublishedProblemPageResponse(
            result.Items.Select(item => new PublishedProblemSummaryResponse(
                item.ProblemId, item.VersionId, item.VersionNumber, item.Title, item.PublishedAtUtc,
                item.Classification is null ? null : ProblemClassificationEndpoints.Map(item.Classification), item.PublicNumber, item.SolveStatus?.ToString())).ToArray(),
            result.Page, result.PageSize, result.Total,
            result.Progress is null ? null : new(result.Progress.Solved, result.Progress.Attempted, result.Progress.Total)));
    }

    private static T? ParseFilter<T>(string? value) where T : struct, Enum => value is null ? null :
        Enum.GetNames<T>().Any(name => string.Equals(name, value, StringComparison.OrdinalIgnoreCase)) && Enum.TryParse<T>(value, true, out var parsed)
            ? parsed : throw new BadHttpRequestException("Invalid library filter.");

    private static async Task<IResult> FindLatestAsync(Guid id, ProblemCatalogUseCases useCases, CancellationToken token) =>
        Results.Ok(Map(await useCases.FindLatestAsync(id, token)));

    private static async Task<IResult> FindVersionAsync(Guid versionId, ProblemCatalogUseCases useCases, CancellationToken token) =>
        Results.Ok(Map(await useCases.FindVersionAsync(versionId, token)));

    private static ProblemDraftResponse Map(ProblemDraftDetails problem) => new(
        problem.Id, problem.Title, problem.Statement, problem.TimeLimitMilliseconds, problem.MemoryLimitMegabytes,
        problem.Checker.ToString(), problem.LatestPublishedVersion, problem.IsArchived, problem.Revision,
        problem.CreatedAtUtc, problem.UpdatedAtUtc,
        problem.Samples.Select(sample => new ProblemSampleResponse(sample.Input ?? "", sample.Output ?? "")).ToArray(),
        problem.TestCases.Select(test => new ProblemTestCaseResponse(test.Id, test.Order, test.InputSizeBytes, test.OutputSizeBytes)).ToArray());

    private static PublishedProblemResponse Map(PublishedProblemDetails problem) => new(
        problem.ProblemId, problem.VersionId, problem.VersionNumber, problem.Title, problem.Statement,
        problem.TimeLimitMilliseconds, problem.MemoryLimitMegabytes, problem.Checker.ToString(), problem.PrivateTestCount,
        problem.PublishedAtUtc,
        problem.Samples.Select(sample => new ProblemSampleResponse(sample.Input ?? "", sample.Output ?? "")).ToArray());
}
