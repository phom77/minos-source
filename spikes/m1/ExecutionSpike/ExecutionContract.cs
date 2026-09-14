namespace Minos.ExecutionSpike;

public enum ExecutionStage { Compile, Run }

public sealed record ExecutionLimits(
    TimeSpan WallTime, TimeSpan CpuTime, long MemoryBytes, long ScratchBytes,
    int Processes, int StdoutBytes, int StderrBytes)
{
    public static ExecutionLimits Compilation => new(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(8),
        256 * 1024 * 1024, 64 * 1024 * 1024, 32, 32768, 32768);
    public static ExecutionLimits Runtime => new(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1),
        64 * 1024 * 1024, 8 * 1024 * 1024, 32, 32768, 32768);

    public void Validate()
    {
        if (WallTime <= TimeSpan.Zero || WallTime > TimeSpan.FromMinutes(2) || CpuTime <= TimeSpan.Zero
            || MemoryBytes < 16 * 1024 * 1024 || MemoryBytes > 512 * 1024 * 1024
            || ScratchBytes < 1024 * 1024 || ScratchBytes > 64 * 1024 * 1024
            || Processes < 8 || Processes > 64 || StdoutBytes <= 0 || StdoutBytes > 131072
            || StderrBytes <= 0 || StderrBytes > 131072)
            throw new ArgumentOutOfRangeException(nameof(ExecutionLimits));
    }
}

public sealed record ExecutionRequest(ExecutionStage Stage, byte[] Payload, byte[] Input, ExecutionLimits Limits);

// Observations only. Contestant verdicts and final accounting policy belong to M6.
public sealed record ExecutionEvidence(
    int? ExitCode, bool WallTimeout, bool CpuLimit, bool MemoryFailure, bool OutputLimit, bool Cancelled,
    TimeSpan WallElapsed, TimeSpan? CpuUsage, long? MemoryPeakBytes,
    long? MemoryLimitEvents, long? OomKills, long? ProcessLimitEvents,
    byte[] Stdout, byte[] Stderr, byte[]? Artifact, string? InfrastructureFailure, bool CleanupComplete,
    bool ResourceSnapshotComplete = false, long? ObservedMemoryBytes = null, long? ProcessesAtCompletion = null);

public interface IExecutionBackend
{
    Task<ExecutionEvidence> ExecuteAsync(ExecutionRequest request, CancellationToken cancellationToken = default);
}
