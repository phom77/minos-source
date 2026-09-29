using Minos.Application.Evaluation;

namespace Minos.Infrastructure.Evaluation;

// M5 lifecycle fixture only. It validates durable worker orchestration and never executes source.
internal sealed class DeterministicSubmissionExecutor : ISubmissionExecutor
{
    public Task<DeterministicExecutionResult> ExecuteAsync(ClaimedSubmissionWork work, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(new DeterministicExecutionResult(DeterministicExecutionOutcome.Succeeded));
    }
}
