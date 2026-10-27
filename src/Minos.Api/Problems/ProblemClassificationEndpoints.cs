using Microsoft.AspNetCore.Mvc;
using Minos.Application.Problems;
using Minos.Domain.Problems;

namespace Minos.Api.Problems;

public sealed record ClassificationEvidenceContract(string SourceProblemKey, int? SourceRating, IReadOnlyList<string>? SourceTags,
    string DatasetRevision, string ManifestSha256, string PolicyVersion, string PolicySha256);
public sealed record TopicClassificationContract(string Slug, string Origin, string? Confidence = null, string? Rationale = null);
public sealed record UpdateProblemClassificationRequest(long ExpectedRevision, Guid BasisVersionId, string? Difficulty,
    string DifficultyOrigin, ClassificationEvidenceContract? Evidence, IReadOnlyList<TopicClassificationContract>? Topics);
public sealed record ProblemClassificationDetailsResponse(Guid ProblemId, long Revision, Guid? BasisVersionId,
    string? Difficulty, string DifficultyOrigin, ClassificationEvidenceContract? Evidence,
    IReadOnlyList<TopicClassificationContract> Topics, bool NeedsReassessment);
public sealed record ProblemTopicResponse(string Slug, string DisplayName);
public sealed record ProblemTopicCountResponse(string Slug, string DisplayName, int ProblemCount);
public sealed record ProblemClassificationResponse(string? Difficulty, IReadOnlyList<ProblemTopicResponse> Topics, bool NeedsReassessment);

internal static class ProblemClassificationEndpoints
{
    internal static void MapProblemClassificationEndpoints(this WebApplication app)
    {
        app.MapGet("/api/problems/topics", async (ProblemCatalogUseCases cases, CancellationToken token) =>
            Results.Ok((await cases.ListTopicCountsAsync(token)).Select(topic => new ProblemTopicCountResponse(topic.Slug, topic.DisplayName, topic.ProblemCount))));
        app.MapGet("/api/problems/{id:guid}/classification", async (Guid id, ProblemClassificationUseCases cases, CancellationToken token) =>
            Results.Ok(Map(await cases.FindPublicAsync(id, token))));
        var authoring = app.MapGroup("/api/authoring/problems").RequireAuthorization();
        authoring.MapGet("/{id:guid}/classification", async (Guid id, ProblemClassificationUseCases cases, CancellationToken token) =>
            Results.Ok(Map(await cases.FindOwnedAsync(id, token))));
        authoring.MapPut("/{id:guid}/classification", UpdateAsync).WithMetadata(new RequestSizeLimitAttribute(64 * 1024));
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateProblemClassificationRequest request,
        ProblemClassificationUseCases cases, CancellationToken token)
    {
        var evidence = request.Evidence;
        var topics = (request.Topics ?? []).Select(topic => topic is null ? throw new BadHttpRequestException("Invalid topic.") :
            new TopicClassification(topic.Slug, Parse<TopicOrigin>(topic.Origin),
                topic.Confidence is null ? null : Parse<ClassificationConfidence>(topic.Confidence), topic.Rationale)).ToArray();
        return Results.Ok(Map(await cases.UpdateAsync(id, new(request.ExpectedRevision, request.BasisVersionId,
            request.Difficulty is null ? null : Parse<ProblemDifficulty>(request.Difficulty), Parse<DifficultyOrigin>(request.DifficultyOrigin),
            evidence is null ? null : new(evidence.SourceProblemKey, evidence.SourceRating, evidence.SourceTags ?? [],
                evidence.DatasetRevision, evidence.ManifestSha256, evidence.PolicyVersion, evidence.PolicySha256), topics), token)));
    }

    private static T Parse<T>(string? value) where T : struct, Enum =>
        Enum.GetNames<T>().Any(name => string.Equals(name, value, StringComparison.OrdinalIgnoreCase)) && Enum.TryParse<T>(value, true, out var parsed)
            ? parsed : throw new BadHttpRequestException("Invalid classification value.");

    internal static ProblemClassificationResponse Map(ProblemClassificationSummary value) =>
        new(value.Difficulty?.ToString(), value.Topics.Select(topic => new ProblemTopicResponse(topic.Slug, topic.DisplayName)).ToArray(), value.NeedsReassessment);

    private static ProblemClassificationDetailsResponse Map(ProblemClassificationDetails value) =>
        new(value.ProblemId, value.Revision, value.BasisVersionId, value.Difficulty?.ToString(), value.DifficultyOrigin.ToString(),
            value.Evidence is null ? null : new(value.Evidence.SourceProblemKey, value.Evidence.SourceRating, value.Evidence.SourceTags,
                value.Evidence.DatasetRevision, value.Evidence.ManifestSha256, value.Evidence.PolicyVersion, value.Evidence.PolicySha256),
            value.Topics.Select(topic => new TopicClassificationContract(topic.Slug, topic.Origin.ToString(), topic.Confidence?.ToString(), topic.Rationale)).ToArray(),
            value.NeedsReassessment);
}
