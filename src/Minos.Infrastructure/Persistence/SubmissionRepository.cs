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

    public async Task<(Submission Submission, IReadOnlyList<JudgingAttempt> Attempts, SubmissionResult? Result)?> FindAsync(
        Guid id,
        CancellationToken token)
    {
        // A poll must not combine pre-completion status with post-completion evidence.
        await using var transaction = await database.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, token);
        var submission = await database.Submissions.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == id, token);
        if (submission is null) return null;
        var attempts = await database.JudgingAttempts.AsNoTracking()
            .Where(attempt => attempt.SubmissionId == id)
            .OrderBy(attempt => attempt.AttemptNumber)
            .ToArrayAsync(token);
        var result = await database.SubmissionResults.AsNoTracking()
            .Include(candidate => candidate.TestCaseResults)
            .SingleOrDefaultAsync(candidate => candidate.SubmissionId == id, token);
        await transaction.CommitAsync(token);
        return (submission, attempts, result);
    }

    public async Task<(IReadOnlyList<(Submission Submission, SubmissionVerdict? Verdict)> Items, int Total)> ListByOwnerAsync(
        Guid ownerAccountId,
        int offset,
        int limit,
        CancellationToken token)
    {
        var query = database.Submissions.AsNoTracking().Where(submission => submission.OwnerAccountId == ownerAccountId);
        var total = await query.CountAsync(token);
        var items = await query
            .GroupJoin(database.SubmissionResults.AsNoTracking(), submission => submission.Id, result => result.SubmissionId,
                (submission, results) => new { submission, results })
            .SelectMany(item => item.results.DefaultIfEmpty(), (item, result) => new { item.submission, Verdict = (SubmissionVerdict?)result!.Verdict })
            .OrderByDescending(item => item.submission.CreatedAtUtc).ThenBy(item => item.submission.Id)
            .Skip(offset).Take(limit).ToArrayAsync(token);
        return (items.Select(item => (item.submission, item.Verdict)).ToArray(), total);
    }
}
