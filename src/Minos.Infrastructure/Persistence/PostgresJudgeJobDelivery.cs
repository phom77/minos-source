using Microsoft.EntityFrameworkCore;
using Minos.Application.Evaluation;
using Minos.Domain.Evaluation;

namespace Minos.Infrastructure.Persistence;

internal sealed class PostgresJudgeJobDelivery(MinosDbContext database, TimeProvider clock) : IJudgeJobDelivery
{
    public Task<Guid?> ReceiveAsync(CancellationToken token)
    {
        var now = clock.GetUtcNow();
        return database.JudgeJobs.AsNoTracking()
            .Where(job => (job.Status == JudgeJobStatus.Pending && job.AttemptCount < JudgeJob.MaximumAttempts && job.AvailableAtUtc <= now) ||
                (job.Status == JudgeJobStatus.Leased && job.LeaseExpiresAtUtc <= now))
            .OrderBy(job => job.Status == JudgeJobStatus.Leased ? job.LeaseExpiresAtUtc : job.AvailableAtUtc)
            .ThenBy(job => job.CreatedAtUtc).ThenBy(job => job.Id)
            .Select(job => (Guid?)job.Id).FirstOrDefaultAsync(token);
    }
}
