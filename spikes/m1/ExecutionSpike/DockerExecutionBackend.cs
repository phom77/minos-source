using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Minos.ExecutionSpike;

// Controlled M1 experiment; the application does not expose this adapter to users yet.
public sealed class DockerExecutionBackend : IExecutionBackend, IAsyncDisposable
{
    public const string Image = "gcc@sha256:5e927c284bf55a7dc796262e311a0703344f62f41f5621eb56843111b1d37e15";
    private const string Label = "minos.spike=m1-harness";
    private const string Root = "/tmp/minos-m1-execution";
    private readonly HttpClient engine;
    private readonly FileStream sessionLock;

    private DockerExecutionBackend(FileStream sessionLock)
    {
        this.sessionLock = sessionLock;
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, token) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try { await socket.ConnectAsync(new UnixDomainSocketEndPoint("/var/run/docker.sock"), token); }
                catch { socket.Dispose(); throw; }
                return new NetworkStream(socket, ownsSocket: true);
            }
        };
        engine = new HttpClient(handler) { BaseAddress = new Uri("http://docker/v1.51/"), Timeout = TimeSpan.FromSeconds(2) };
    }

    public static async Task<DockerExecutionBackend> OpenAsync()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Run the adapter in WSL2 Ubuntu.");
        if (Path.Exists(Root) && new DirectoryInfo(Root).LinkTarget is not null) throw new IOException("Workspace root is a symlink.");
        Directory.CreateDirectory(Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.SetUnixFileMode(Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var filesystem = await Command.RunAsync("stat", ["-f", "-c", "%T", Root]);
        if (filesystem.Exit != 0 || Encoding.UTF8.GetString(filesystem.Output).Trim() is not ("ext2/ext3" or "tmpfs" or "xfs" or "btrfs"))
            throw new IOException("Private workspace must use a Linux-native filesystem.");
        var lockFile = new FileStream(Path.Combine(Root, "session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        try { lockFile.Lock(0, 1); }
        catch { lockFile.Dispose(); throw new IOException("Another M1 harness owns the workspace; concurrent runs are disabled."); }
        var backend = new DockerExecutionBackend(lockFile);
        try
        {
            // Single-developer spike recovery. Durable job/lease ownership is separate M5 work.
            var abandoned = await Command.RunAsync("docker", ["ps", "-aq", "--filter", $"label={Label}"]);
            if (abandoned.Exit != 0) throw new IOException(abandoned.Error);
            foreach (var id in Encoding.UTF8.GetString(abandoned.Output).Split('\n', StringSplitOptions.RemoveEmptyEntries))
                await backend.RemoveAsync(id.Trim());
            foreach (var directory in Directory.EnumerateDirectories(Root, "job-*")) DeleteWorkspace(directory);
            return backend;
        }
        catch { await backend.DisposeAsync(); throw; }
    }

    public async Task<ExecutionEvidence> ExecuteAsync(ExecutionRequest request, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Run the adapter in WSL2 Ubuntu.");
        request.Limits.Validate();
        if (request.Payload.Length > (request.Stage == ExecutionStage.Compile ? 65536 : 8 * 1024 * 1024) || request.Input.Length > 65536)
            throw new ArgumentOutOfRangeException(nameof(request), "Spike source, artifact, or input size exceeded.");
        var id = "minos-m1-" + Guid.NewGuid().ToString("N");
        var workspace = Path.Combine(Root, "job-" + Guid.NewGuid().ToString("N"));
        var payloadName = request.Stage == ExecutionStage.Compile ? "main.cpp" : "main";
        var limits = request.Limits;
        var watch = new Stopwatch();
        int? exit = null;
        var wallTimeout = false; var cpuLimit = false; var memoryFailure = false; var cancelled = false;
        var outputLimit = 0;
        TimeSpan? cpu = null; long? memory = null; long? memoryEvents = null; long? oom = null; long? pidsEvents = null;
        long? observedMemory = null; long? processesAtCompletion = null; var snapshotComplete = false;
        byte[] stdout = []; byte[] stderr = []; byte[]? artifact = null;
        string? infrastructure = null;
        var cleanupComplete = false;
        Process? execution = null;
        Task<byte[]>? stdoutCapture = null; Task<byte[]>? stderrCapture = null;
        Task<(long Cpu, long Memory)>? sample = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(workspace, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            await File.WriteAllBytesAsync(Path.Combine(workspace, payloadName), request.Payload, cancellationToken);
            File.SetUnixFileMode(Path.Combine(workspace, payloadName), request.Stage == ExecutionStage.Compile
                ? UnixFileMode.UserRead : UnixFileMode.UserRead | UnixFileMode.UserExecute);
            await File.WriteAllBytesAsync(Path.Combine(workspace, "stdin.txt"), request.Input, cancellationToken);
            File.SetUnixFileMode(Path.Combine(workspace, "stdin.txt"), UnixFileMode.UserRead);
            var arguments = new List<string> { "create", "--name", id, "--label", Label,
                "--network", "none", "--user", "1000:1000", "--cap-drop", "ALL", "--security-opt", "no-new-privileges",
                "--read-only", "--memory", limits.MemoryBytes.ToString(CultureInfo.InvariantCulture),
                "--memory-swap", limits.MemoryBytes.ToString(CultureInfo.InvariantCulture), "--cpus", "1",
                "--pids-limit", limits.Processes.ToString(CultureInfo.InvariantCulture), "--shm-size", "1m",
                "--tmpfs", $"/scratch:size={limits.ScratchBytes},mode=700,uid=1000,gid=1000,nosuid,nodev",
                "--log-driver", "none", "--ulimit", "nofile=64:64", "--ulimit", "core=0:0",
                "--ulimit", "fsize=8388608:8388608", "--ulimit", $"cpu={Math.Ceiling(limits.CpuTime.TotalSeconds) + 1}",
                "--mount", $"type=bind,src={workspace},dst=/input,readonly", Image,
                "sleep", ((int)Math.Ceiling(limits.WallTime.TotalSeconds) + 30).ToString(CultureInfo.InvariantCulture) };
            await CheckedDockerAsync(arguments);
            watch.Start();
            await CheckedDockerAsync(["start", id]);
            // No caller-supplied shell or compiler arguments. Fresh /input holds only payload and current input.
            var command = request.Stage == ExecutionStage.Compile
                ? new[] { "g++", "-std=c++20", "-O2", "-pipe", "/input/main.cpp", "-o", "/scratch/main" }
                : new[] { "/bin/sh", "-c", "exec /input/main < /input/stdin.txt" };
            execution = Command.Start("docker", new[] { "exec", "--env", "TMPDIR=/scratch", id, "/usr/bin/env", "-i",
                "PATH=/usr/local/bin:/usr/bin:/bin", "TMPDIR=/scratch", "HOME=/scratch" }.Concat(command));
            stdoutCapture = Command.CaptureAsync(execution.StandardOutput.BaseStream, limits.StdoutBytes, () => Interlocked.Exchange(ref outputLimit, 1));
            stderrCapture = Command.CaptureAsync(execution.StandardError.BaseStream, limits.StderrBytes, () => Interlocked.Exchange(ref outputLimit, 1));
            var completed = execution.WaitForExitAsync();
            while (!completed.IsCompleted)
            {
                if (sample is null) sample = SampleAsync(id);
                if (sample.IsCompleted)
                {
                    var observation = await sample;
                    cpu = TimeSpan.FromTicks(observation.Cpu / 100);
                    observedMemory = Math.Max(observedMemory ?? 0, observation.Memory);
                    sample = null;
                }
                wallTimeout = watch.Elapsed >= limits.WallTime;
                cpuLimit = cpu >= limits.CpuTime;
                cancelled = cancellationToken.IsCancellationRequested;
                if (wallTimeout || cpuLimit || outputLimit != 0 || cancelled)
                {
                    await KillAsync(id);
                    break;
                }
                await Task.Delay(25);
            }
            try { await completed.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch { if (!execution.HasExited) execution.Kill(entireProcessTree: true); throw new IOException("Execution attachment did not terminate."); }
            exit = execution.ExitCode;
            stdout = await stdoutCapture.WaitAsync(TimeSpan.FromSeconds(5));
            stderr = await stderrCapture.WaitAsync(TimeSpan.FromSeconds(5));
            if (!(wallTimeout || cpuLimit || outputLimit != 0 || cancelled))
            {
                // Container stays alive to preserve authoritative cgroup counters, then all children are removed.
                var telemetry = await Command.RunAsync("docker", ["exec", id, "/bin/sh", "-c",
                    "set -e; cat /sys/fs/cgroup/cpu.stat; echo MEMORY_PEAK; cat /sys/fs/cgroup/memory.peak; echo MEMORY_EVENTS; cat /sys/fs/cgroup/memory.events; echo PIDS_EVENTS; cat /sys/fs/cgroup/pids.events; echo PIDS_CURRENT; cat /sys/fs/cgroup/pids.current"], timeout: TimeSpan.FromSeconds(3));
                if (telemetry.Exit == 0)
                {
                    var values = ParseTelemetry(Encoding.UTF8.GetString(telemetry.Output));
                    foreach (var required in new[] { "cpu.usage_usec", "memory.peak", "memory.max", "memory.oom_kill", "pids.max", "pids.current" })
                        if (!values.ContainsKey(required)) throw new IOException("Required resource evidence is missing: " + required);
                    cpu = TimeSpan.FromMicroseconds(values.GetValueOrDefault("cpu.usage_usec"));
                    memory = values.GetValueOrDefault("memory.peak");
                    memoryEvents = values.GetValueOrDefault("memory.max"); oom = values.GetValueOrDefault("memory.oom_kill");
                    pidsEvents = values.GetValueOrDefault("pids.max"); memoryFailure = oom > 0;
                    processesAtCompletion = values.GetValueOrDefault("pids.current"); snapshotComplete = true;
                }
                else
                {
                    var inspection = await Command.RunAsync("docker", ["inspect", "--format", "{{.State.OOMKilled}}", id]);
                    memoryFailure = Encoding.UTF8.GetString(inspection.Output).Trim() == "true";
                    if (!memoryFailure) infrastructure = "Container stopped before cgroup evidence could be collected: " + telemetry.Error;
                }
            }
            if (request.Stage == ExecutionStage.Compile && exit == 0 && !(wallTimeout || cpuLimit || outputLimit != 0 || cancelled || memoryFailure) && infrastructure is null)
            {
                var result = await Command.RunAsync("docker", ["exec", id, "/bin/sh", "-c",
                    "test -f /scratch/main && test ! -L /scratch/main && cat /scratch/main"], outputLimit: 8 * 1024 * 1024);
                if (result.Exit != 0 || result.Output.Length == 0) throw new IOException("Compiler did not produce a regular bounded artifact.");
                artifact = result.Output;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { cancelled = true; }
        catch (Exception error) { infrastructure = error.Message; }
        finally
        {
            watch.Stop();
            try { await RemoveAsync(id); if (Directory.Exists(workspace)) DeleteWorkspace(workspace); cleanupComplete = true; }
            catch (Exception error) { infrastructure = "Cleanup failed: " + error.Message; }
            if (execution is not null)
            {
                try
                {
                    if (!execution.HasExited) { execution.Kill(entireProcessTree: true); await execution.WaitForExitAsync(); }
                    if (stdoutCapture is not null) stdout = await stdoutCapture.WaitAsync(TimeSpan.FromSeconds(5));
                    if (stderrCapture is not null) stderr = await stderrCapture.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (Exception error) { infrastructure = "Attachment cleanup failed: " + error.Message; cleanupComplete = false; }
                finally { execution.Dispose(); }
            }
            if (sample is not null) { try { await sample; } catch { /* Removal can interrupt the last monitoring request. */ } }
        }
        return new ExecutionEvidence(exit, wallTimeout, cpuLimit, memoryFailure, outputLimit != 0, cancelled, watch.Elapsed,
            cpu, memory, memoryEvents, oom, pidsEvents, stdout, stderr, artifact, infrastructure, cleanupComplete,
            snapshotComplete, observedMemory, processesAtCompletion);
    }

    private async Task<(long Cpu, long Memory)> SampleAsync(string id)
    {
        using var response = await engine.GetAsync($"containers/{id}/stats?stream=false&one-shot=true");
        response.EnsureSuccessStatusCode();
        using var stats = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = stats.RootElement;
        var cpu = root.GetProperty("cpu_stats").GetProperty("cpu_usage").GetProperty("total_usage").GetInt64();
        var memory = root.GetProperty("memory_stats").TryGetProperty("usage", out var usage) ? usage.GetInt64() : 0;
        return (cpu, memory);
    }

    private async Task KillAsync(string id)
    {
        using var response = await engine.PostAsync($"containers/{id}/kill?signal=KILL", null);
        if (response.StatusCode is not (System.Net.HttpStatusCode.NoContent or System.Net.HttpStatusCode.Conflict)) response.EnsureSuccessStatusCode();
    }

    private async Task RemoveAsync(string id)
    {
        var deadline = Stopwatch.StartNew();
        do
        {
            using var response = await engine.DeleteAsync($"containers/{id}?force=true&v=true");
            if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.NoContent) return;
            if (response.StatusCode != System.Net.HttpStatusCode.Conflict) response.EnsureSuccessStatusCode();
            // Another remover can own the same deletion. Wait for confirmed absence, never claim success on 409.
            await Task.Delay(100);
        } while (deadline.Elapsed < TimeSpan.FromSeconds(5));
        throw new IOException("Container removal did not complete within the cleanup deadline.");
    }

    private static async Task CheckedDockerAsync(IEnumerable<string> arguments)
    {
        var result = await Command.RunAsync("docker", arguments);
        if (result.Exit != 0) throw new IOException(result.Error);
    }

    private static Dictionary<string, long> ParseTelemetry(string text)
    {
        var result = new Dictionary<string, long>(); var section = "cpu";
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line == "MEMORY_PEAK") { section = "peak"; continue; }
            if (line == "MEMORY_EVENTS") { section = "memory"; continue; }
            if (line == "PIDS_EVENTS") { section = "pids"; continue; }
            if (line == "PIDS_CURRENT") { section = "current"; continue; }
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (section == "peak") result["memory.peak"] = long.Parse(parts[0], CultureInfo.InvariantCulture);
            else if (section == "current") result["pids.current"] = long.Parse(parts[0], CultureInfo.InvariantCulture);
            else if (parts.Length == 2) result[$"{section}.{parts[0]}"] = long.Parse(parts[1], CultureInfo.InvariantCulture);
        }
        return result;
    }

    private static void DeleteWorkspace(string directory)
    {
        var full = Path.GetFullPath(directory);
        var name = Path.GetFileName(full);
        if (Path.GetDirectoryName(full) != Root || !name.StartsWith("job-", StringComparison.Ordinal)
            || !Guid.TryParseExact(name[4..], "N", out _) || new DirectoryInfo(full).LinkTarget is not null)
            throw new IOException("Refusing unexpected recovery workspace.");
        foreach (var file in new[] { "main.cpp", "main", "stdin.txt" }) File.Delete(Path.Combine(full, file));
        Directory.Delete(full); // Known files only; no recursive filesystem operation.
    }

    public ValueTask DisposeAsync()
    {
        engine.Dispose(); sessionLock.Dispose(); return ValueTask.CompletedTask;
    }
}
