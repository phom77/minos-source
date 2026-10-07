using Minos.Application.Accounts;

namespace Minos.Application.Evaluation;

public sealed record JudgingOperationsSnapshot(
    DateTimeOffset ObservedAtUtc, int MaximumActive, int Pending, int Leased, int ExpiredLeases,
    double OldestPendingSeconds, int CompletedLastHour, int SystemFailedLastHour,
    int FailedAttemptsLastHour, int ExecutorFailuresLastHour);

public interface IJudgingOperationsQuery
{
    Task<JudgingOperationsSnapshot> ReadAsync(DateTimeOffset nowUtc, CancellationToken token);
}

public sealed class JudgingOperationsUseCases(
    AccountUseCases accounts, IJudgingOperationsQuery query, TimeProvider clock)
{
    public async Task<JudgingOperationsSnapshot> ReadAsync(CancellationToken token)
    {
        var account = await accounts.RequireActiveAsync(token);
        if (!account.CanManageAccountRoles) throw new AccessDeniedException();
        return await query.ReadAsync(clock.GetUtcNow(), token);
    }
}
