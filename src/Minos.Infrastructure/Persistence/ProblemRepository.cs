using Microsoft.EntityFrameworkCore;
using Minos.Application;
using Minos.Application.Problems;
using Minos.Domain.Problems;
using Npgsql;

namespace Minos.Infrastructure.Persistence;

internal sealed class ProblemRepository(MinosDbContext database) : IProblemRepository
{
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

    public async Task<(IReadOnlyList<PublishedProblemSummary> Items, int Total)> ListPublishedAsync(
        int offset,
        int limit,
        CancellationToken token)
    {
        var current =
            from problem in database.Problems.AsNoTracking()
            join version in database.PublishedProblemVersions.AsNoTracking()
                on new { ProblemId = problem.Id, Version = problem.LatestPublishedVersion }
                equals new { version.ProblemId, Version = version.VersionNumber }
            where !problem.IsArchived && problem.LatestPublishedVersion > 0
            select new PublishedProblemSummary(problem.Id, version.Id, version.VersionNumber, version.Title, version.PublishedAtUtc);
        var total = await current.CountAsync(token);
        var items = await current.OrderByDescending(item => item.PublishedAtUtc).ThenBy(item => item.ProblemId)
            .Skip(offset).Take(limit).ToArrayAsync(token);
        return (items, total);
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
