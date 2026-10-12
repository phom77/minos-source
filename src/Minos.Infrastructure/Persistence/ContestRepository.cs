using Microsoft.EntityFrameworkCore;
using Minos.Application;
using Minos.Application.Contests;
using Minos.Application.Evaluation;
using Minos.Domain.Contests;
using Minos.Domain.Evaluation;
using Npgsql;

namespace Minos.Infrastructure.Persistence;

internal sealed class ContestRepository(MinosDbContext database, SubmissionCapacity capacity, TimeProvider clock) : IContestRepository
{
    public async Task AddAsync(Contest contest, CancellationToken token)
    {
        database.Contests.Add(contest);
        await SaveAsync(contest, token);
    }
    public Task<Contest?> FindAsync(Guid id, CancellationToken token) =>
        database.Contests.Include(c => c.Problems).SingleOrDefaultAsync(c => c.Id == id, token);
    public async Task SaveAsync(Contest contest, CancellationToken token)
    {
        try { await database.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw new ConcurrencyConflictException(); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new ConcurrencyConflictException(); }
    }
    public async Task<(IReadOnlyList<Contest> Items, int Total)> ListAsync(Guid? ownerId, int offset, int limit, CancellationToken token)
    {
        var query = database.Contests.AsNoTracking().Where(c => ownerId.HasValue ? c.OwnerAccountId == ownerId : c.PublishedAtUtc != null);
        var total = await query.CountAsync(token);
        var items = await query.OrderByDescending(c => c.StartsAtUtc).ThenBy(c => c.Id).Skip(offset).Take(limit)
            .Include(c => c.Problems).ToArrayAsync(token);
        return (items, total);
    }
    public Task<ContestRegistration?> FindRegistrationAsync(Guid contestId, Guid accountId, CancellationToken token) =>
        database.ContestRegistrations.AsNoTracking().SingleOrDefaultAsync(r => r.ContestId == contestId && r.AccountId == accountId, token);
    public async Task RegisterAsync(ContestRegistration registration, CancellationToken token)
    {
        // Schedule is immutable after publication. ON CONFLICT makes duplicate/concurrent joins idempotent.
        var now = clock.GetUtcNow();
        var written = await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO contests.registrations (contest_id, account_id, registered_at_utc)
            SELECT id, {registration.AccountId}, {now} FROM contests.contests
            WHERE id = {registration.ContestId} AND published_at_utc IS NOT NULL AND ends_at_utc > {now}
                AND owner_account_id <> {registration.AccountId}
            ON CONFLICT (contest_id, account_id) DO NOTHING
            """, token);
        if (written == 0 && await FindRegistrationAsync(registration.ContestId, registration.AccountId, token) is null)
            throw new Minos.Domain.DomainValidationException("Registration", "Registration is closed.");
    }
    public async Task<bool> AdmitAsync(Contest contest, ContestSubmission association, Submission submission, JudgeJob job, CancellationToken token)
        => await AdmitCoreAsync(contest, () => association, submission, job, null, token) is not null;

    public Task<Guid?> AdmitOnceAsync(Contest contest, ContestRegistration registration, Guid contestProblemId, Submission submission, JudgeJob job,
        SubmissionRequestIdentity request, CancellationToken token) => AdmitCoreAsync(contest,
            () => ContestSubmission.Create(contest, registration, contestProblemId, submission), submission, job, request, token);

    private async Task<Guid?> AdmitCoreAsync(Contest contest, Func<ContestSubmission> association, Submission submission, JudgeJob job,
        SubmissionRequestIdentity? request, CancellationToken token)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        // Same lock as practice admission: a contest is never a separate capacity pool.
        await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(21754185638891831)", token);
        if (request is not null && await SubmissionRequestStore.FindAsync(database, submission.OwnerAccountId, request, token) is { } replay)
            return replay;
        contest.EnsureCanSubmit(submission.OwnerAccountId, clock.GetUtcNow());
        var active = database.Submissions.Where(s => s.Status == SubmissionStatus.Pending || s.Status == SubmissionStatus.Judging);
        if (await active.CountAsync(token) >= capacity.MaximumActive ||
            await active.CountAsync(s => s.OwnerAccountId == submission.OwnerAccountId, token) >= SupportedSubmission.MaximumActivePerAccount)
            return null;
        database.Submissions.Add(submission); database.JudgeJobs.Add(job); database.ContestSubmissions.Add(association());
        if (request is not null) SubmissionRequestStore.Add(database, submission, request);
        await database.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return submission.Id;
    }
    public async Task<(IReadOnlyList<ContestSubmissionItem> Items, int Total)> ListSubmissionsAsync(Guid contestId, Guid ownerId,
        int offset, int limit, CancellationToken token)
    {
        var query = from link in database.ContestSubmissions.AsNoTracking()
                    join submission in database.Submissions.AsNoTracking() on link.SubmissionId equals submission.Id
                    join result in database.SubmissionResults.AsNoTracking() on submission.Id equals result.SubmissionId into results
                    from result in results.DefaultIfEmpty()
                    where link.ContestId == contestId && link.AccountId == ownerId
                    select new { submission.Id, link.ContestProblemId, submission.Status, Verdict = (SubmissionVerdict?)result!.Verdict, submission.CreatedAtUtc };
        var total = await query.CountAsync(token);
        var items = await query.OrderByDescending(s => s.CreatedAtUtc).ThenBy(s => s.Id).Skip(offset).Take(limit)
            .Select(s => new ContestSubmissionItem(s.Id, s.ContestProblemId, s.Status, s.Verdict, s.CreatedAtUtc)).ToArrayAsync(token);
        return (items, total);
    }
}
