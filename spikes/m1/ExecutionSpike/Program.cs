using System.Text;
using System.Text.Json;
using Minos.ExecutionSpike;

if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Run this experiment inside WSL2 Ubuntu.");
if (args is ["--crash-fixture", var fixture])
{
    await using var crashBackend = await DockerExecutionBackend.OpenAsync();
    await crashBackend.ExecuteAsync(new(ExecutionStage.Run, await File.ReadAllBytesAsync(fixture), Experiments.Bytes("crash\n"),
        ExecutionLimits.Runtime with { WallTime = TimeSpan.FromSeconds(30), CpuTime = TimeSpan.FromSeconds(10) }));
    return;
}
var backend = await DockerExecutionBackend.OpenAsync();
byte[]? recoveryArtifact = null;
try
{
    var smoke = await Experiments.SmokeAsync(backend);
    var replacement = new FixtureBackend();
    var replacementResult = await Experiments.SmokeAsync(replacement);
    Experiments.Require(replacement.Calls == 2 && replacementResult.Stdout.SequenceEqual(smoke.Stdout), "Adapter replacement changed orchestration.");
    Console.WriteLine("adapter_replacement=passed");
    Console.WriteLine("m1_smoke=passed");
    if (args is ["--all", var fixtures]) recoveryArtifact = await ResourceExperiments.RunAsync(backend, fixtures);
}
finally { await backend.DisposeAsync(); }
if (recoveryArtifact is not null)
{
    await ResourceExperiments.RecoveryAsync(recoveryArtifact);
    Console.WriteLine("m1_exit_criteria=passed");
}

internal static class Experiments
{
    public static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
    public static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    public static void Report(string name, ExecutionEvidence evidence)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { name, evidence.ExitCode, evidence.WallTimeout, evidence.CpuLimit,
            evidence.MemoryFailure, evidence.OutputLimit, evidence.Cancelled, wallMs = evidence.WallElapsed.TotalMilliseconds,
            cpuMs = evidence.CpuUsage?.TotalMilliseconds, evidence.MemoryPeakBytes, evidence.MemoryLimitEvents,
            evidence.OomKills, evidence.ProcessLimitEvents, stdoutBytes = evidence.Stdout.Length, stderrBytes = evidence.Stderr.Length,
            artifactBytes = evidence.Artifact?.Length, evidence.InfrastructureFailure, evidence.CleanupComplete,
            evidence.ResourceSnapshotComplete, evidence.ObservedMemoryBytes, evidence.ProcessesAtCompletion }));
    }

    public static async Task<ExecutionEvidence> CompileAsync(IExecutionBackend backend, string source, ExecutionLimits? limits = null)
    {
        var result = await backend.ExecuteAsync(new(ExecutionStage.Compile, Bytes(source), [], limits ?? ExecutionLimits.Compilation));
        Report("compile", result);
        Require(result.ExitCode == 0 && result.Artifact is not null && result.InfrastructureFailure is null && result.CleanupComplete,
            "Compilation did not produce a clean isolated artifact.");
        return result;
    }

    public static async Task<ExecutionEvidence> SmokeAsync(IExecutionBackend backend)
    {
        var compilation = await CompileAsync(backend, "#include <iostream>\nint main(){long long a,b; if(!(std::cin>>a>>b))return 2; std::cout<<a+b<<'\\n';}");
        var execution = await backend.ExecuteAsync(new(ExecutionStage.Run, compilation.Artifact!, Bytes("19 23\n"), ExecutionLimits.Runtime));
        Report("sum", execution);
        Require(execution.ExitCode == 0 && execution.InfrastructureFailure is null && execution.CleanupComplete
            && execution.Stdout.SequenceEqual(Bytes("42\n")), "Trusted output check failed.");
        return execution;
    }
}

internal sealed class FixtureBackend : IExecutionBackend
{
    public int Calls { get; private set; }
    public Task<ExecutionEvidence> ExecuteAsync(ExecutionRequest request, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(new ExecutionEvidence(0, false, false, false, false, false, TimeSpan.Zero, TimeSpan.Zero,
            0, 0, 0, 0, request.Stage == ExecutionStage.Run ? Experiments.Bytes("42\n") : [], [],
            request.Stage == ExecutionStage.Compile ? [1] : null, null, true));
    }
}
