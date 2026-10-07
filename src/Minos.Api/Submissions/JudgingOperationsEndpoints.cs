using Minos.Application.Evaluation;

namespace Minos.Api.Submissions;

internal static class JudgingOperationsEndpoints
{
    private sealed record OperationsResponse(DateTimeOffset ObservedAtUtc, int MaximumActive, int Pending,
        int Leased, int ExpiredLeases, double OldestPendingSeconds, int CompletedLastHour,
        int SystemFailedLastHour, int FailedAttemptsLastHour, int ExecutorFailuresLastHour);

    public static void MapJudgingOperationsEndpoints(this WebApplication app) =>
        app.MapGet("/api/operations/judging", async (JudgingOperationsUseCases useCases, HttpContext context, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var snapshot = await useCases.ReadAsync(token);
            return Results.Ok(new OperationsResponse(snapshot.ObservedAtUtc, snapshot.MaximumActive, snapshot.Pending,
                snapshot.Leased, snapshot.ExpiredLeases, snapshot.OldestPendingSeconds, snapshot.CompletedLastHour,
                snapshot.SystemFailedLastHour, snapshot.FailedAttemptsLastHour, snapshot.ExecutorFailuresLastHour));
        }).RequireAuthorization();
}
