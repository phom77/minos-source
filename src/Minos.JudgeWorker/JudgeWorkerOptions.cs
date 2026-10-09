namespace Minos.JudgeWorker;

internal sealed record JudgeWorkerOptions(int MaximumConcurrentJobs = 4)
{
    internal void Validate()
    {
        if (MaximumConcurrentJobs is < 1 or > 16)
            throw new InvalidOperationException("Minos:Judging:MaximumConcurrentJobs must be between 1 and 16.");
    }
}
