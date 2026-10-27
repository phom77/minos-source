using Microsoft.EntityFrameworkCore;
using Minos.Application;
using Minos.Application.Problems;
using Minos.Domain.Problems;
using Npgsql;

namespace Minos.Infrastructure.Persistence;

internal sealed class ProblemClassificationRepository(MinosDbContext database) : IProblemClassificationRepository
{
    public Task<ProblemClassificationTarget?> FindTargetAsync(Guid problemId, CancellationToken token) =>
        database.Problems.AsNoTracking().Where(problem => problem.Id == problemId)
            .Select(problem => new ProblemClassificationTarget(problem.OwnerAccountId, problem.IsArchived,
                database.PublishedProblemVersions.Where(version => version.ProblemId == problem.Id && version.VersionNumber == problem.LatestPublishedVersion)
                    .Select(version => (Guid?)version.Id).SingleOrDefault())).SingleOrDefaultAsync(token);
    public Task<bool> VersionBelongsToProblemAsync(Guid problemId, Guid versionId, CancellationToken token) =>
        database.PublishedProblemVersions.AnyAsync(version => version.ProblemId == problemId && version.Id == versionId, token);
    public Task<ProblemClassification?> FindAsync(Guid problemId, CancellationToken token) =>
        database.ProblemClassifications.Include(value => value.Topics).SingleOrDefaultAsync(value => value.ProblemId == problemId, token);
    public async Task<IReadOnlyList<ProblemTopicSummary>> ListTopicsAsync(CancellationToken token) =>
        await database.ProblemTopics.AsNoTracking().OrderBy(topic => topic.Slug).Select(topic => new ProblemTopicSummary(topic.Slug, topic.DisplayName)).ToArrayAsync(token);
    public async Task SaveAsync(ProblemClassification classification, CancellationToken token)
    {
        // Serialize metadata writes with publication/archive without modifying the problem draft.
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        await database.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM problems.problems WHERE id = {classification.ProblemId} FOR UPDATE", token);
        if (await database.Problems.AsNoTracking().AnyAsync(problem => problem.Id == classification.ProblemId && problem.IsArchived, token))
            throw new Minos.Domain.DomainValidationException(nameof(classification.ProblemId), "An archived problem cannot be changed.");
        if (database.Entry(classification).State == EntityState.Detached) database.ProblemClassifications.Add(classification);
        try
        {
            await database.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }
        catch (DbUpdateConcurrencyException) { throw new ConcurrencyConflictException(); }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new ConcurrencyConflictException(); }
    }
}
