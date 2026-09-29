using Microsoft.EntityFrameworkCore;
using Minos.Application.Evaluation;
using Minos.Domain.Evaluation;

namespace Minos.Infrastructure.Persistence;

internal sealed class JudgeJobRepository(MinosDbContext database) : IJudgeJobRepository
{
    public async Task<ClaimedSubmissionWork?> ClaimNextAsync(
        string workerId,
        DateTimeOffset nowUtc,
        CancellationToken token)
    {
        while (true)
        {
            await using var transaction = await database.Database.BeginTransactionAsync(token);
            var jobs = await database.JudgeJobs.FromSqlInterpolated($$"""
                SELECT job.* FROM evaluation.judge_jobs AS job
                WHERE (job.status = 0 AND job.attempt_count < {{JudgeJob.MaximumAttempts}} AND job.available_at_utc <= {{nowUtc}})
                   OR (job.status = 1 AND job.lease_expires_at_utc <= {{nowUtc}})
                ORDER BY CASE WHEN job.status = 1 THEN job.lease_expires_at_utc ELSE job.available_at_utc END, job.created_at_utc
                FOR UPDATE OF job SKIP LOCKED
                LIMIT 1
                """).ToArrayAsync(token);
            var job = jobs.SingleOrDefault();
            if (job is null)
            {
                await transaction.RollbackAsync(token);
                return null;
            }

            var submission = await database.Submissions.SingleAsync(candidate => candidate.Id == job.SubmissionId, token);
            JudgingAttempt? expired = null;
            if (job.Status == JudgeJobStatus.Leased)
            {
                expired = await database.JudgingAttempts.SingleAsync(
                    attempt => attempt.Id == job.ActiveAttemptId, token);
                expired.Expire(nowUtc);
                if (job.AttemptCount >= JudgeJob.MaximumAttempts)
                {
                    var expiredId = expired.Id;
                    job.ExhaustExpired(nowUtc);
                    submission.FailSystem(expiredId, nowUtc);
                    await database.SaveChangesAsync(token);
                    await transaction.CommitAsync(token);
                    database.ChangeTracker.Clear();
                    continue;
                }
            }

            var attempt = job.Claim(workerId, nowUtc);
            submission.StartAttempt(attempt.Id);
            database.JudgingAttempts.Add(attempt);
            await database.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return new(
                job.Id,
                submission.Id,
                attempt.Id,
                attempt.LeaseToken,
                workerId,
                attempt.LeaseExpiresAtUtc,
                submission.ProblemVersionId,
                submission.Language,
                submission.ToolchainId,
                submission.Source);
        }
    }

    public async Task<LeaseMutationResult> RenewAsync(
        Guid attemptId,
        Guid leaseToken,
        string workerId,
        DateTimeOffset nowUtc,
        CancellationToken token)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        var job = await LockForAttemptAsync(attemptId, token);
        if (job is null || !job.Renew(attemptId, leaseToken, workerId, nowUtc))
        {
            await transaction.RollbackAsync(token);
            return LeaseMutationResult.Stale;
        }
        var attempt = await database.JudgingAttempts.SingleAsync(candidate => candidate.Id == attemptId, token);
        attempt.Renew(job.LeaseExpiresAtUtc!.Value);
        await database.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return LeaseMutationResult.Accepted;
    }

    public async Task<LeaseMutationResult> CompleteAsync(
        Guid attemptId,
        Guid leaseToken,
        string workerId,
        DateTimeOffset nowUtc,
        CancellationToken token)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        var job = await LockForAttemptAsync(attemptId, token);
        if (job is null)
        {
            await transaction.RollbackAsync(token);
            return LeaseMutationResult.Stale;
        }
        var attempt = await database.JudgingAttempts.SingleAsync(candidate => candidate.Id == attemptId, token);
        if (job.Status == JudgeJobStatus.Completed && job.CompletedAttemptId == attemptId &&
            attempt.LeaseToken == leaseToken && string.Equals(attempt.WorkerId, workerId, StringComparison.Ordinal))
        {
            await transaction.CommitAsync(token);
            return LeaseMutationResult.Accepted;
        }
        if (!job.OwnsActiveLease(attemptId, leaseToken, workerId) || job.LeaseExpiresAtUtc <= nowUtc)
        {
            await transaction.RollbackAsync(token);
            return LeaseMutationResult.Stale;
        }

        var submission = await database.Submissions.SingleAsync(candidate => candidate.Id == job.SubmissionId, token);
        job.Complete(attemptId, leaseToken, workerId, nowUtc);
        attempt.Succeed(nowUtc);
        database.SubmissionResults.Add(submission.Complete(attemptId, nowUtc));
        await database.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return LeaseMutationResult.Accepted;
    }

    public async Task<AttemptFailureResult> FailAsync(
        Guid attemptId,
        Guid leaseToken,
        string workerId,
        InfrastructureFailureKind failureKind,
        DateTimeOffset nowUtc,
        CancellationToken token)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        var job = await LockForAttemptAsync(attemptId, token);
        if (job is null || !job.OwnsActiveLease(attemptId, leaseToken, workerId) || job.LeaseExpiresAtUtc <= nowUtc)
        {
            await transaction.RollbackAsync(token);
            return AttemptFailureResult.Stale;
        }

        var attempt = await database.JudgingAttempts.SingleAsync(candidate => candidate.Id == attemptId, token);
        var submission = await database.Submissions.SingleAsync(candidate => candidate.Id == job.SubmissionId, token);
        attempt.Fail(failureKind, nowUtc);
        AttemptFailureResult result;
        if (job.AttemptCount >= JudgeJob.MaximumAttempts)
        {
            job.Exhaust(attemptId, leaseToken, workerId, nowUtc);
            submission.FailSystem(attemptId, nowUtc);
            result = AttemptFailureResult.SystemFailed;
        }
        else
        {
            var delay = job.AttemptCount == 1 ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(5);
            job.Retry(attemptId, leaseToken, workerId, nowUtc, nowUtc.Add(delay));
            submission.ReturnToPending(attemptId);
            result = AttemptFailureResult.RetryScheduled;
        }
        await database.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return result;
    }

    private async Task<JudgeJob?> LockForAttemptAsync(Guid attemptId, CancellationToken token)
    {
        var jobs = await database.JudgeJobs.FromSqlInterpolated($"""
            SELECT job.* FROM evaluation.judge_jobs AS job
            INNER JOIN evaluation.judging_attempts AS attempt ON attempt.judge_job_id = job.id
            WHERE attempt.id = {attemptId}
            FOR UPDATE OF job
            """).ToArrayAsync(token);
        return jobs.SingleOrDefault();
    }
}
