using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Minos.Application.Evaluation;

namespace Minos.JudgeWorker;

internal sealed class SubmissionJobWorker(
    IServiceScopeFactory scopes,
    ILogger<SubmissionJobWorker> logger) : BackgroundService
{
    private readonly string workerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var useCases = scope.ServiceProvider.GetRequiredService<JudgeJobUseCases>();
                var work = await useCases.ClaimAsync(workerId, stoppingToken);
                if (work is null)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
                    continue;
                }

                logger.LogInformation(
                    "Claimed submission job {JobId}, submission {SubmissionId}, attempt {AttemptId}",
                    work.JobId, work.SubmissionId, work.AttemptId);
                var result = await ProcessWithRenewalAsync(useCases, work, stoppingToken);
                logger.LogInformation(
                    "Processed submission job {JobId}, submission {SubmissionId}, attempt {AttemptId}, disposition {Disposition}",
                    work.JobId, work.SubmissionId, work.AttemptId, result);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogError("Submission worker iteration failed with {ErrorType}", error.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
    }

    private async Task<LeaseMutationResult> ProcessWithRenewalAsync(
        JudgeJobUseCases processing,
        ClaimedSubmissionWork work,
        CancellationToken stoppingToken)
    {
        using var executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var processingTask = processing.ProcessAsync(work, executionCancellation.Token);
        try
        {
            while (!processingTask.IsCompleted)
            {
                var renewalDelay = Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                if (await Task.WhenAny(processingTask, renewalDelay) == processingTask) break;
                stoppingToken.ThrowIfCancellationRequested();

                await using var renewalScope = scopes.CreateAsyncScope();
                var renewal = renewalScope.ServiceProvider.GetRequiredService<JudgeJobUseCases>();
                if (await renewal.RenewAsync(work, stoppingToken) == LeaseMutationResult.Accepted) continue;

                logger.LogWarning(
                    "Lost lease for submission job {JobId}, submission {SubmissionId}, attempt {AttemptId}",
                    work.JobId, work.SubmissionId, work.AttemptId);
                executionCancellation.Cancel();
                try { await processingTask; }
                catch (OperationCanceledException) when (executionCancellation.IsCancellationRequested) { }
                return LeaseMutationResult.Stale;
            }
            return await processingTask;
        }
        finally
        {
            // Keep the job scope alive until the sandbox operation observes cancellation and cleans up.
            executionCancellation.Cancel();
            try { await processingTask; }
            catch (OperationCanceledException) when (executionCancellation.IsCancellationRequested) { }
            catch { /* The original processing/renewal failure is reported by the worker loop. */ }
        }
    }
}
