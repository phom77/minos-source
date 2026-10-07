using System.Data;
using Microsoft.EntityFrameworkCore;
using Minos.Application.Evaluation;
using Minos.Domain.Evaluation;

namespace Minos.Infrastructure.Persistence;

internal sealed class JudgingOperationsQuery(MinosDbContext database, SubmissionCapacity capacity) : IJudgingOperationsQuery
{
    public async Task<JudgingOperationsSnapshot> ReadAsync(DateTimeOffset nowUtc, CancellationToken token)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        var jobs = database.JudgeJobs.AsNoTracking();
        var pending = await jobs.CountAsync(job => job.Status == JudgeJobStatus.Pending, token);
        var leased = await jobs.CountAsync(job => job.Status == JudgeJobStatus.Leased, token);
        var expired = await jobs.CountAsync(job => job.Status == JudgeJobStatus.Leased && job.LeaseExpiresAtUtc <= nowUtc, token);
        var oldest = await jobs.Where(job => job.Status == JudgeJobStatus.Pending)
            .MinAsync(job => (DateTimeOffset?)job.CreatedAtUtc, token);
        var since = nowUtc.AddHours(-1);
        var completed = await database.Submissions.CountAsync(submission =>
            submission.Status == SubmissionStatus.Completed && submission.CompletedAtUtc >= since && submission.CompletedAtUtc <= nowUtc, token);
        var systemFailed = await database.Submissions.CountAsync(submission =>
            submission.Status == SubmissionStatus.SystemFailed && submission.CompletedAtUtc >= since && submission.CompletedAtUtc <= nowUtc, token);
        var failures = database.JudgingAttempts.Where(attempt => attempt.FinishedAtUtc >= since && attempt.FinishedAtUtc <= nowUtc && attempt.FailureKind != null);
        var failed = await failures.CountAsync(token);
        var executor = await failures.CountAsync(attempt => attempt.FailureKind == InfrastructureFailureKind.ExecutorFailure, token);
        await transaction.CommitAsync(token);
        return new(nowUtc, capacity.MaximumActive, pending, leased, expired,
            oldest is null ? 0 : Math.Max(0, (nowUtc - oldest.Value).TotalSeconds), completed, systemFailed, failed, executor);
    }
}
