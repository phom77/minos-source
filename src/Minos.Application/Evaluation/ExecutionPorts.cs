namespace Minos.Application.Evaluation;

public enum ExecutionStage { Compile, Run }

public sealed record ExecutionLimits(
    TimeSpan WallTime,
    TimeSpan CpuTime,
    long MemoryBytes,
    long ScratchBytes,
    int Processes,
    int StdoutBytes,
    int StderrBytes);

public sealed record ExecutionRequest(
    ExecutionStage Stage,
    byte[] Payload,
    byte[] Input,
    ExecutionLimits Limits);

public sealed record ExecutionEvidence(
    int? ExitCode,
    bool WallTimeout,
    bool CpuLimit,
    bool MemoryFailure,
    bool OutputLimit,
    bool Cancelled,
    TimeSpan WallElapsed,
    TimeSpan? CpuUsage,
    long? MemoryPeakBytes,
    byte[] Stdout,
    byte[] Stderr,
    byte[]? Artifact,
    bool ResourceSnapshotComplete,
    bool CleanupComplete,
    string? InfrastructureFailure);

public interface IExecutionBackend
{
    Task<ExecutionEvidence> ExecuteAsync(ExecutionRequest request, CancellationToken token);
}
