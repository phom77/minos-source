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
                var result = await useCases.ProcessAsync(work, stoppingToken);
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
}
