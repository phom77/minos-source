using Minos.Domain.Problems;

namespace Minos.Application.Problems;

public sealed record ProblemClassificationTarget(Guid OwnerAccountId, bool IsArchived, Guid? LatestVersionId);
public sealed record UpdateProblemClassification(long ExpectedRevision, Guid BasisVersionId, ProblemDifficulty? Difficulty,
    DifficultyOrigin DifficultyOrigin, ClassificationEvidence? Evidence, IReadOnlyList<TopicClassification> Topics);
public sealed record ProblemTopicSummary(string Slug, string DisplayName);
public sealed record ProblemClassificationSummary(ProblemDifficulty? Difficulty, IReadOnlyList<ProblemTopicSummary> Topics, bool NeedsReassessment);
public sealed record ProblemClassificationDetails(Guid ProblemId, long Revision, Guid? BasisVersionId,
    ProblemDifficulty? Difficulty, DifficultyOrigin DifficultyOrigin, ClassificationEvidence? Evidence,
    IReadOnlyList<TopicClassification> Topics, bool NeedsReassessment);

public interface IProblemClassificationRepository
{
    Task<ProblemClassificationTarget?> FindTargetAsync(Guid problemId, CancellationToken token);
    Task<bool> VersionBelongsToProblemAsync(Guid problemId, Guid versionId, CancellationToken token);
    Task<ProblemClassification?> FindAsync(Guid problemId, CancellationToken token);
    Task<IReadOnlyList<ProblemTopicSummary>> ListTopicsAsync(CancellationToken token);
    Task SaveAsync(ProblemClassification classification, CancellationToken token);
}

public sealed class ProblemClassificationUseCases(IProblemClassificationRepository repository, ProblemAccess access, TimeProvider clock)
{
    public Task<IReadOnlyList<ProblemTopicSummary>> ListTopicsAsync(CancellationToken token) => repository.ListTopicsAsync(token);

    public async Task<ProblemClassificationDetails> FindOwnedAsync(Guid problemId, CancellationToken token)
    {
        var target = await OwnedAsync(problemId, token);
        return Map(problemId, await repository.FindAsync(problemId, token), target.LatestVersionId);
    }

    public async Task<ProblemClassificationSummary> FindPublicAsync(Guid problemId, CancellationToken token)
    {
        var target = await repository.FindTargetAsync(problemId, token);
        if (target is null || target.IsArchived || target.LatestVersionId is null) throw new ResourceNotFoundException();
        var value = await repository.FindAsync(problemId, token);
        var topics = await repository.ListTopicsAsync(token);
        return new(value?.Difficulty, topics.Where(topic => value?.Topics.Any(item => item.TopicSlug == topic.Slug) == true).ToArray(),
            value is not null && value.BasisVersionId != target.LatestVersionId);
    }

    public async Task<ProblemClassificationDetails> UpdateAsync(Guid problemId, UpdateProblemClassification request, CancellationToken token)
    {
        var target = await OwnedAsync(problemId, token);
        if (target.IsArchived) throw new Minos.Domain.DomainValidationException(nameof(problemId), "An archived problem cannot be changed.");
        var value = await repository.FindAsync(problemId, token);
        if (request.ExpectedRevision < 0 || request.ExpectedRevision != (value?.Revision ?? 0)) throw new ConcurrencyConflictException();
        if (!await repository.VersionBelongsToProblemAsync(problemId, request.BasisVersionId, token))
            throw new Minos.Domain.DomainValidationException(nameof(request.BasisVersionId), "The basis must be a published version of this problem.");
        // Validate the entire replacement before modifying tracked state.
        var replacement = ProblemClassification.Create(problemId, request.BasisVersionId, request.Difficulty,
            request.DifficultyOrigin, request.Evidence, request.Topics, clock.GetUtcNow());
        var known = await repository.ListTopicsAsync(token);
        if (replacement.Topics.Any(item => known.All(topic => topic.Slug != item.TopicSlug)))
            throw new Minos.Domain.DomainValidationException(nameof(request.Topics), "An unknown topic was supplied.");
        if (value is null) value = replacement;
        else value.Revise(request.BasisVersionId, request.Difficulty, request.DifficultyOrigin, request.Evidence, request.Topics, clock.GetUtcNow());
        await repository.SaveAsync(value, token);
        return Map(problemId, value, target.LatestVersionId);
    }

    private async Task<ProblemClassificationTarget> OwnedAsync(Guid problemId, CancellationToken token)
    {
        var author = await access.RequireAuthorAsync(token);
        var target = await repository.FindTargetAsync(problemId, token) ?? throw new ResourceNotFoundException();
        if (target.OwnerAccountId != author.Id) throw new AccessDeniedException();
        return target;
    }

    private static ProblemClassificationDetails Map(Guid problemId, ProblemClassification? value, Guid? latest) =>
        new(problemId, value?.Revision ?? 0, value?.BasisVersionId, value?.Difficulty, value?.DifficultyOrigin ?? DifficultyOrigin.Manual,
            value?.SourceProblemKey is null ? null : new(value.SourceProblemKey, value.SourceRating, value.SourceTags.ToArray(),
                value.DatasetRevision!, value.ManifestSha256!, value.PolicyVersion!, value.PolicySha256!),
            value?.Topics.OrderBy(item => item.TopicSlug).Select(item => new TopicClassification(item.TopicSlug, item.Origin, item.Confidence, item.Rationale)).ToArray() ?? [],
            value is not null && value.BasisVersionId != latest);
}
