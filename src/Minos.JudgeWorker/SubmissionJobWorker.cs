using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Minos.Application.Evaluation;

namespace Minos.JudgeWorker;

internal sealed class SubmissionJobWorker(
    IServiceScopeFactory scopes, JudgeWorkerOptions options,
    ILogger<SubmissionJobWorker> logger) : BackgroundService
{
    private static readonly Meter Metrics = new("Minos.JudgeWorker");
    private static readonly Counter<long> Claims = Metrics.CreateCounter<long>("minos.worker.claims");
    private static readonly Counter<long> IterationFailures = Metrics.CreateCounter<long>("minos.worker.iteration_failures");
    private static readonly Counter<long> LostLeases = Metrics.CreateCounter<long>("minos.worker.lost_leases");
    private static readonly UpDownCounter<long> ActiveJobs = Metrics.CreateUpDownCounter<long>("minos.worker.active_jobs");
    private static readonly Histogram<double> JobDuration = Metrics.CreateHistogram<double>("minos.worker.job_duration", "s");
    private readonly string workerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        options.Validate();
        var active = new HashSet<Task>();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                active.RemoveWhere(task => task.IsCompleted);
                // Claim only when a bounded processing slot is available; leases never wait in an unbounded local queue.
                if (active.Count >= options.MaximumConcurrentJobs)
                {
                    await Task.WhenAny(active).WaitAsync(stoppingToken);
                    continue;
                }
                var scope = scopes.CreateAsyncScope();
                var transferred = false;
                try
                {
                    var id = await scope.ServiceProvider.GetRequiredService<IJudgeJobDelivery>().ReceiveAsync(stoppingToken);
                    if (id is null)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
                        continue;
                    }
                    var cases = scope.ServiceProvider.GetRequiredService<JudgeJobUseCases>();
                    var work = await cases.ClaimAsync(id.Value, workerId, stoppingToken);
                    if (work is null) continue;
                    Claims.Add(1);
                    logger.LogInformation("Claimed submission job {JobId}, submission {SubmissionId}, attempt {AttemptId}",
                        work.JobId, work.SubmissionId, work.AttemptId);
                    active.Add(ProcessJobAsync(scope, cases, work, stoppingToken));
                    transferred = true;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception error)
                {
                    IterationFailures.Add(1);
                    logger.LogError("Submission delivery failed with {ErrorType}", error.GetType().Name);
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
                finally { if (!transferred) await scope.DisposeAsync(); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { await Task.WhenAll(active); }
    }

    private async Task ProcessJobAsync(AsyncServiceScope scope, JudgeJobUseCases cases, ClaimedSubmissionWork work, CancellationToken token)
    {
        ActiveJobs.Add(1);
        var timer = Stopwatch.StartNew();
        try
        {
            await using var ownedScope = scope;
            var result = await ProcessWithRenewalAsync(cases, work, token);
            logger.LogInformation("Processed submission job {JobId}, submission {SubmissionId}, attempt {AttemptId}, disposition {Disposition}",
                work.JobId, work.SubmissionId, work.AttemptId, result);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            IterationFailures.Add(1);
            logger.LogError("Submission processing failed with {ErrorType}; job {JobId}, submission {SubmissionId}, attempt {AttemptId}",
                error.GetType().Name, work.JobId, work.SubmissionId, work.AttemptId);
        }
        finally { ActiveJobs.Add(-1); JobDuration.Record(timer.Elapsed.TotalSeconds); }
    }

    private async Task<LeaseMutationResult> ProcessWithRenewalAsync(
        JudgeJobUseCases processing, ClaimedSubmissionWork work, CancellationToken stoppingToken)
    {
        using var executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var processingTask = processing.ProcessAsync(work, executionCancellation.Token);
        try
        {
            while (!processingTask.IsCompleted)
            {
                var renewalDelay = Task.Delay(TimeSpan.FromSeconds(10), executionCancellation.Token);
                if (await Task.WhenAny(processingTask, renewalDelay) == processingTask) break;
                stoppingToken.ThrowIfCancellationRequested();
                await using var renewalScope = scopes.CreateAsyncScope();
                var renewal = renewalScope.ServiceProvider.GetRequiredService<JudgeJobUseCases>();
                if (await renewal.RenewAsync(work, stoppingToken) == LeaseMutationResult.Accepted) continue;
                logger.LogWarning("Lost lease for submission job {JobId}, submission {SubmissionId}, attempt {AttemptId}",
                    work.JobId, work.SubmissionId, work.AttemptId);
                LostLeases.Add(1);
                executionCancellation.Cancel();
                try { await processingTask; }
                catch (OperationCanceledException) when (executionCancellation.IsCancellationRequested) { }
                return LeaseMutationResult.Stale;
            }
            return await processingTask;
        }
        finally
        {
            // Keep the processing scope and resource permits alive until sandbox cleanup finishes.
            executionCancellation.Cancel();
            try { await processingTask; }
            catch (OperationCanceledException) when (executionCancellation.IsCancellationRequested) { }
            catch { /* The original processing/renewal failure is reported by the job wrapper. */ }
        }
    }
}
