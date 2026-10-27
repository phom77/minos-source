using Microsoft.EntityFrameworkCore;
using Minos.Application;
using Minos.Application.Problems;
using Minos.Domain.Problems;
using Minos.Domain.Evaluation;
using Npgsql;

namespace Minos.Infrastructure.Persistence;

internal sealed class ProblemRepository(MinosDbContext database) : IProblemRepository
{
    public async Task<IReadOnlyList<ProblemTopicCountSummary>> ListTopicCountsAsync(CancellationToken token) =>
        await database.ProblemTopics.AsNoTracking().OrderBy(topic => topic.Slug)
            .Select(topic => new ProblemTopicCountSummary(topic.Slug, topic.DisplayName,
                (from assignment in database.ProblemTopicAssignments
                 join problem in database.Problems on assignment.ProblemId equals problem.Id
                 where assignment.TopicSlug == topic.Slug && !problem.IsArchived && problem.LatestPublishedVersion > 0
                 select problem.Id).Count())).ToArrayAsync(token);
    public async Task AddAsync(Problem problem, CancellationToken token)
    {
        database.Problems.Add(problem);
        await database.SaveChangesAsync(token);
    }

    public Task<Problem?> FindForAuthoringAsync(Guid id, CancellationToken token) =>
        database.Problems.Include(problem => problem.Samples).Include(problem => problem.TestCases)
            .SingleOrDefaultAsync(problem => problem.Id == id, token);

    public async Task SaveAsync(Problem problem, CancellationToken token)
    {
        try { await database.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw new ConcurrencyConflictException(); }
    }

    public async Task PublishAsync(Problem problem, PublishedProblemVersion version, CancellationToken token)
    {
        database.PublishedProblemVersions.Add(version);
        try { await database.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw new ConcurrencyConflictException(); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new ConcurrencyConflictException(); }
    }

    public async Task<ProblemCatalogResult> ListPublishedAsync(
        ProblemCatalogQuery query,
        Guid? accountId,
        CancellationToken token)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, token);
        var current =
            from problem in database.Problems.AsNoTracking()
            join version in database.PublishedProblemVersions.AsNoTracking()
                on new { ProblemId = problem.Id, Version = problem.LatestPublishedVersion }
                equals new { version.ProblemId, Version = version.VersionNumber }
            join metadata in database.ProblemClassifications.AsNoTracking() on problem.Id equals metadata.ProblemId into labels
            from classification in labels.DefaultIfEmpty()
            where !problem.IsArchived && problem.LatestPublishedVersion > 0
            select new { ProblemId = problem.Id, VersionId = version.Id, problem.PublicNumber, version.VersionNumber, version.Title, version.PublishedAtUtc,
                Difficulty = classification == null ? null : classification.Difficulty,
                HasSubmission = accountId != null && database.Submissions.Any(submission => submission.OwnerAccountId == accountId && submission.ProblemId == problem.Id),
                HasAccepted = accountId != null && (from submission in database.Submissions
                    join result in database.SubmissionResults on submission.Id equals result.SubmissionId
                    where submission.OwnerAccountId == accountId && submission.ProblemId == problem.Id && submission.Status == SubmissionStatus.Completed && result.Verdict == SubmissionVerdict.Accepted
                    select submission.Id).Any() };
        ProblemProgressSummary? progress = accountId is null ? null : new(
            await current.CountAsync(item => item.HasAccepted, token), await current.CountAsync(item => item.HasSubmission, token), await current.CountAsync(token));
        if (query.Search is { } search)
        {
            var numberText = search.StartsWith('#') ? search[1..] : search;
            var isNumber = int.TryParse(numberText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number) && number > 0;
            var pattern = "%" + search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            current = current.Where(item => (isNumber && item.PublicNumber == number) || EF.Functions.ILike(item.Title, pattern, "\\"));
        }
        if (query.Difficulty is not null || query.Topic is not null || query.Status is not null)
        {
            var matching = current.Select(item => new { Item = item,
                DifficultyMatches = (query.Difficulty == ProblemDifficultyFilter.Unclassified ? item.Difficulty == null : item.Difficulty == (ProblemDifficulty?)query.Difficulty) != query.DifficultyIsNot,
                TopicMatches = database.ProblemTopicAssignments.Any(topic => topic.ProblemId == item.ProblemId && topic.TopicSlug == query.Topic) != query.TopicIsNot,
                StatusMatches = (query.Status == ProblemSolveStatus.Solved ? item.HasAccepted : query.Status == ProblemSolveStatus.Attempted
                    ? item.HasSubmission && !item.HasAccepted : !item.HasSubmission) != query.StatusIsNot });
            current = matching.Where(value => query.Match == ProblemFilterMatch.All
                ? (query.Difficulty == null || value.DifficultyMatches) && (query.Topic == null || value.TopicMatches) && (query.Status == null || value.StatusMatches)
                : (query.Difficulty != null && value.DifficultyMatches) || (query.Topic != null && value.TopicMatches) || (query.Status != null && value.StatusMatches))
                .Select(value => value.Item);
        }
        var total = await current.CountAsync(token);
        var sorted = query.Sort switch
        {
            ProblemCatalogSort.Newest => current.OrderByDescending(item => item.PublishedAtUtc).ThenBy(item => item.PublicNumber),
            ProblemCatalogSort.Title => current.OrderBy(item => item.Title).ThenBy(item => item.PublicNumber),
            ProblemCatalogSort.DifficultyAscending => current.OrderBy(item => item.Difficulty == null).ThenBy(item => item.Difficulty).ThenBy(item => item.PublicNumber),
            ProblemCatalogSort.DifficultyDescending => current.OrderBy(item => item.Difficulty == null).ThenByDescending(item => item.Difficulty).ThenBy(item => item.PublicNumber),
            _ => current.OrderBy(item => item.PublicNumber)
        };
        var items = await sorted.Skip(checked((query.Page - 1) * query.PageSize)).Take(query.PageSize)
            .Select(item => new PublishedProblemSummary(item.ProblemId, item.VersionId, item.VersionNumber, item.Title, item.PublishedAtUtc)
            {
                PublicNumber = item.PublicNumber,
                SolveStatus = accountId == null ? null : item.HasAccepted ? ProblemSolveStatus.Solved : item.HasSubmission ? ProblemSolveStatus.Attempted : ProblemSolveStatus.NotAttempted
            })
            .ToArrayAsync(token);
        var ids = items.Select(item => item.ProblemId).ToArray();
        var classifications = await database.ProblemClassifications.AsNoTracking().Where(value => ids.Contains(value.ProblemId)).ToArrayAsync(token);
        var topics = await (from assignment in database.ProblemTopicAssignments.AsNoTracking()
            join topic in database.ProblemTopics.AsNoTracking() on assignment.TopicSlug equals topic.Slug
            where ids.Contains(assignment.ProblemId)
            orderby topic.Slug
            select new { assignment.ProblemId, topic.Slug, topic.DisplayName }).ToArrayAsync(token);
        items = items.Select(item =>
        {
            var classification = classifications.SingleOrDefault(value => value.ProblemId == item.ProblemId);
            return item with { Classification = new(classification?.Difficulty,
                topics.Where(topic => topic.ProblemId == item.ProblemId).Select(topic => new ProblemTopicSummary(topic.Slug, topic.DisplayName)).ToArray(),
                classification is not null && classification.BasisVersionId != item.VersionId) };
        }).ToArray();
        await transaction.CommitAsync(token);
        return new(items, total, progress);
    }

    public Task<PublishedProblemVersion?> FindLatestPublishedAsync(Guid problemId, CancellationToken token) =>
        (from problem in database.Problems.AsNoTracking()
         join version in database.PublishedProblemVersions.AsNoTracking()
             on new { ProblemId = problem.Id, Version = problem.LatestPublishedVersion }
             equals new { version.ProblemId, Version = version.VersionNumber }
         where problem.Id == problemId && !problem.IsArchived
         select version)
        .Include(version => version.Samples)
        .SingleOrDefaultAsync(token);

    public Task<PublishedProblemVersion?> FindPublishedVersionAsync(Guid versionId, CancellationToken token) =>
        database.PublishedProblemVersions.AsNoTracking()
            .Where(version => version.Id == versionId)
            .Include(version => version.Samples)
            .SingleOrDefaultAsync(token);

    public async Task<IReadOnlySet<string>> ReferencedArtifactKeysAsync(CancellationToken token)
    {
        var draft = database.ProblemTestCases.AsNoTracking().Select(test => test.InputArtifact.Key)
            .Concat(database.ProblemTestCases.AsNoTracking().Select(test => test.OutputArtifact.Key));
        var published = database.PublishedProblemTestCases.AsNoTracking().Select(test => test.InputArtifact.Key)
            .Concat(database.PublishedProblemTestCases.AsNoTracking().Select(test => test.OutputArtifact.Key));
        return (await draft.Concat(published).Distinct().ToArrayAsync(token)).ToHashSet(StringComparer.Ordinal);
    }
}
