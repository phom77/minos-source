using System.Diagnostics;
using System.Text;

namespace Minos.ExecutionSpike;

internal static class ResourceExperiments
{
    public static async Task<byte[]> RunAsync(IExecutionBackend backend, string fixtureDirectory)
    {
        var compilation = await Experiments.CompileAsync(backend, await File.ReadAllTextAsync(Path.Combine(fixtureDirectory, "adversarial.cpp")));
        var artifact = compilation.Artifact!;
        var concurrent = await Task.WhenAll(new[] { "alpha-private-input", "beta-private-input" }.Select(marker =>
            backend.ExecuteAsync(new(ExecutionStage.Run, artifact, Experiments.Bytes("workspace " + marker + "\n"), ExecutionLimits.Runtime))));
        for (var index = 0; index < concurrent.Length; index++)
        {
            var marker = index == 0 ? "alpha-private-input" : "beta-private-input";
            Experiments.Report("concurrent-" + marker, concurrent[index]);
            Experiments.Require(concurrent[index].ExitCode == 0 && concurrent[index].Stdout.SequenceEqual(Experiments.Bytes(marker + "\n"))
                && concurrent[index].CleanupComplete && concurrent[index].InfrastructureFailure is null, "Concurrent workspaces crossed or failed cleanup.");
        }
        await RequireCleanAsync();
        async Task<ExecutionEvidence> Run(string mode, ExecutionLimits? limits = null, CancellationToken token = default)
        {
            var result = await backend.ExecuteAsync(new(ExecutionStage.Run, artifact, Experiments.Bytes(mode + "\n"), limits ?? ExecutionLimits.Runtime), token);
            Experiments.Report(mode, result);
            Experiments.Require(result.InfrastructureFailure is null && result.CleanupComplete, $"{mode}: infrastructure or cleanup failure.");
            Experiments.Require(result.Stdout.Length <= (limits ?? ExecutionLimits.Runtime).StdoutBytes
                && result.Stderr.Length <= (limits ?? ExecutionLimits.Runtime).StderrBytes, $"{mode}: unbounded output capture.");
            await RequireCleanAsync();
            return result;
        }
        var invalid = await backend.ExecuteAsync(new(ExecutionStage.Compile, Experiments.Bytes("int main(){invalid C++;}"), [], ExecutionLimits.Compilation));
        Experiments.Report("compile-error", invalid);
        Experiments.Require(invalid.ExitCode == 1 && invalid.Stderr.Length > 0 && invalid.Artifact is null && invalid.CleanupComplete && invalid.InfrastructureFailure is null, "Compile error evidence missing.");
        Experiments.Require((await Run("runtime")).ExitCode == 17, "Runtime exit code was lost.");
        var wall = await Run("wall", ExecutionLimits.Runtime with { WallTime = TimeSpan.FromMilliseconds(800) });
        Experiments.Require(wall.WallTimeout && !wall.CpuLimit && wall.WallElapsed < TimeSpan.FromSeconds(3), "Wall watchdog failed.");
        var cpu = await Run("cpu", ExecutionLimits.Runtime with { CpuTime = TimeSpan.FromMilliseconds(400) });
        Experiments.Require(cpu.CpuLimit && !cpu.WallTimeout && cpu.CpuUsage >= TimeSpan.FromMilliseconds(400), "Aggregate CPU budget was not observed and enforced.");
        var descendants = await Run("cpu-children", ExecutionLimits.Runtime with { CpuTime = TimeSpan.FromMilliseconds(400) });
        Experiments.Require(descendants.CpuLimit && !descendants.WallTimeout, "CPU-consuming descendants bypassed the group budget.");
        var memory = await Run("memory");
        Experiments.Require(memory.MemoryFailure && (memory.OomKills > 0 || memory.ExitCode == 137), "Memory failure evidence missing.");
        foreach (var stream in new[] { "stdout", "stderr" })
        {
            var output = await Run(stream, ExecutionLimits.Runtime with { StdoutBytes = 4096, StderrBytes = 4096 });
            Experiments.Require(output.OutputLimit && (stream == "stdout" ? output.Stdout.Length : output.Stderr.Length) == 4096, "Output budget did not terminate and truncate.");
        }
        var scratch = await Run("scratch");
        Experiments.Require(scratch.ExitCode == 43 && Encoding.UTF8.GetString(scratch.Stdout).Contains("scratch_bytes=8388608 errno=28", StringComparison.Ordinal), "Aggregate scratch did not stop at 8 MiB.");
        foreach (var mode in new[] { "fork", "threads" })
        {
            var result = await Run(mode);
            Experiments.Require(result.ExitCode == 42 && result.ProcessLimitEvents > 0, "Process/thread cgroup limit was not enforced.");
        }
        var network = await Run("network");
        Experiments.Require(network.ExitCode == 0 && Encoding.UTF8.GetString(network.Stdout).Contains("connect_errno=101", StringComparison.Ordinal), "Network attempt was not blocked.");
        const string hidden = "/tmp/minos-m1-private-expected-answer.txt";
        await File.WriteAllTextAsync(hidden, "host-only hidden answer and next-test sentinel");
        Environment.SetEnvironmentVariable("MINOS_M1_SECRET", "host-only sentinel");
        try { Experiments.Require((await Run("isolation")).ExitCode == 0, "Contestant could see forbidden state or write outside scratch."); }
        finally { File.Delete(hidden); Environment.SetEnvironmentVariable("MINOS_M1_SECRET", null); }
        for (var repetition = 0; repetition < 3; repetition++)
        {
            var children = await Run("children");
            Experiments.Require(children.ExitCode == 0 && children.ProcessesAtCompletion >= 5, "Persistent children were not observed before container cleanup.");
        }
        using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(700)))
            Experiments.Require((await Run("wall", token: cancellation.Token)).Cancelled, "In-flight cancellation did not clean up.");
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            Experiments.Require((await Run("ok", token: cancellation.Token)).Cancelled, "Pre-cancelled staging left work behind.");
        }
        var heavySource = "#include <vector>\n#include <string>\ntemplate<int N>struct T:T<N-1>{char data[4096];};template<>struct T<0>{};T<850> x;int main(){return sizeof(x);}";
        var heavy = await backend.ExecuteAsync(new(ExecutionStage.Compile, Experiments.Bytes(heavySource), [],
            ExecutionLimits.Compilation with { MemoryBytes = 32 * 1024 * 1024, WallTime = TimeSpan.FromSeconds(3), CpuTime = TimeSpan.FromSeconds(1) }));
        Experiments.Report("heavy-compilation", heavy);
        Experiments.Require(heavy.MemoryFailure || heavy.CpuLimit || heavy.WallTimeout, "Resource-heavy compilation was not bounded by evidence.");
        Experiments.Require(heavy.CleanupComplete && heavy.InfrastructureFailure is null, "Heavy compilation cleanup failed.");
        await RequireCleanAsync();
        // Real infrastructure fault: remove the active container from outside its controller.
        var interrupted = backend.ExecuteAsync(new(ExecutionStage.Run, artifact, Experiments.Bytes("wall\n"), ExecutionLimits.Runtime with { WallTime = TimeSpan.FromSeconds(10) }));
        var victim = await WaitForRunningAsync();
        var removed = await Command.RunAsync("docker", ["rm", "-f", victim]);
        Experiments.Require(removed.Exit == 0, "Infrastructure fault injection failed.");
        var fault = await interrupted;
        Experiments.Report("container-disappeared", fault);
        Experiments.Require(fault.InfrastructureFailure is not null && fault.CleanupComplete, "Infrastructure failure was reported as a contestant result.");
        await RequireCleanAsync();
        Console.WriteLine("resource_experiments=passed");
        return artifact;
    }

    public static async Task RecoveryAsync(byte[] artifact)
    {
        var fixture = "/tmp/minos-m1-recovery-" + Guid.NewGuid().ToString("N") + ".bin";
        await File.WriteAllBytesAsync(fixture, artifact);
        using var child = Command.Start(Environment.ProcessPath!, [typeof(ResourceExperiments).Assembly.Location, "--crash-fixture", fixture]);
        var stdout = Command.CaptureAsync(child.StandardOutput.BaseStream, 16384, () => { });
        var stderr = Command.CaptureAsync(child.StandardError.BaseStream, 16384, () => { });
        try
        {
            var victim = await WaitForRunningAsync();
            var before = await Command.RunAsync("docker", ["top", victim, "-eo", "pid,comm"]);
            Experiments.Require(before.Exit == 0 && Encoding.UTF8.GetString(before.Output).Contains("main", StringComparison.Ordinal), "Crash fixture never reached contestant execution.");
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync();
            await stdout; await stderr;
            await using var recovered = await DockerExecutionBackend.OpenAsync();
            await RequireCleanAsync();
            var result = await recovered.ExecuteAsync(new(ExecutionStage.Run, artifact, Experiments.Bytes("ok\n"), ExecutionLimits.Runtime));
            Experiments.Report("after-controller-crash", result);
            Experiments.Require(result.ExitCode == 0 && result.CleanupComplete && result.InfrastructureFailure is null, "Recovered adapter could not execute again.");
            await RequireCleanAsync();
            Console.WriteLine("controller_crash_recovery=passed");
        }
        finally
        {
            if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); }
            File.Delete(fixture);
        }
    }

    private static async Task<string> WaitForRunningAsync()
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(10))
        {
            var result = await Command.RunAsync("docker", ["ps", "-q", "--filter", "label=minos.spike=m1-harness"]);
            var ids = Encoding.UTF8.GetString(result.Output).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (ids.Length == 1)
            {
                var processes = await Command.RunAsync("docker", ["top", ids[0], "-eo", "pid,comm"]);
                if (processes.Exit == 0 && Encoding.UTF8.GetString(processes.Output).Contains("main", StringComparison.Ordinal)) return ids[0];
            }
            await Task.Delay(50);
        }
        throw new TimeoutException("Contestant container did not become observable.");
    }

    public static async Task RequireCleanAsync()
    {
        var containers = await Command.RunAsync("docker", ["ps", "-aq", "--filter", "label=minos.spike=m1-harness"]);
        Experiments.Require(containers.Exit == 0 && containers.Output.Length == 0, "A contestant container survived cleanup.");
        Experiments.Require(!Directory.EnumerateDirectories("/tmp/minos-m1-execution", "job-*").Any(), "A private workspace survived cleanup.");
    }
}
