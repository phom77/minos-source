using Microsoft.EntityFrameworkCore;
using Minos.Application.Evaluation;
using Minos.Domain.Evaluation;

namespace Minos.Infrastructure.Persistence;

internal sealed class SubmissionRepository(MinosDbContext database) : ISubmissionRepository
{
    public async Task<bool> AdmitAsync(
        Submission submission,
        JudgeJob job,
        int maximumActive,
        CancellationToken token)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({submission.OwnerAccountId.ToString()}, 0))", token);
        var active = await database.Submissions.CountAsync(candidate =>
            candidate.OwnerAccountId == submission.OwnerAccountId &&
            (candidate.Status == SubmissionStatus.Pending || candidate.Status == SubmissionStatus.Judging), token);
        if (active >= maximumActive)
        {
            await transaction.RollbackAsync(token);
            return false;
        }

        database.Submissions.Add(submission);
        database.JudgeJobs.Add(job);
        await database.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return true;
    }

    public async Task<(Submission Submission, IReadOnlyList<JudgingAttempt> Attempts)?> FindAsync(
        Guid id,
        CancellationToken token)
    {
        var submission = await database.Submissions.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == id, token);
        if (submission is null) return null;
        var attempts = await database.JudgingAttempts.AsNoTracking()
            .Where(attempt => attempt.SubmissionId == id)
            .OrderBy(attempt => attempt.AttemptNumber)
            .ToArrayAsync(token);
        return (submission, attempts);
    }

    public async Task<(IReadOnlyList<Submission> Items, int Total)> ListByOwnerAsync(
        Guid ownerAccountId,
        int offset,
        int limit,
        CancellationToken token)
    {
        var query = database.Submissions.AsNoTracking().Where(submission => submission.OwnerAccountId == ownerAccountId);
        var total = await query.CountAsync(token);
        var items = await query.OrderByDescending(submission => submission.CreatedAtUtc).ThenBy(submission => submission.Id)
            .Skip(offset).Take(limit).ToArrayAsync(token);
        return (items, total);
    }
}
