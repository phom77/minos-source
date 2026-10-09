using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net.Sockets;
using System.Net.Http.Json;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Minos.Application.Evaluation;
using Minos.Domain.Evaluation;
using Minos.Domain.Problems;

namespace Minos.Infrastructure.Evaluation;

internal sealed class DockerExecutionBackend : IExecutionBackend, IDisposable
{
    internal const string Image = "gcc@sha256:5e927c284bf55a7dc796262e311a0703344f62f41f5621eb56843111b1d37e15";
    private const string Label = "minos.judge=m6";
    private const string Root = "/tmp/minos-judge-execution";
    private const int MaximumExecutableBytes = 16 * 1024 * 1024;
    private static readonly Meter Metrics = new("Minos.Sandbox");
    private static readonly Counter<long> CleanupFailures = Metrics.CreateCounter<long>("minos.sandbox.cleanup_failures");
    private static readonly Counter<long> RecoveredContainers = Metrics.CreateCounter<long>("minos.sandbox.recovered_containers");
    internal static void RecordCleanupFailure() => CleanupFailures.Add(1);
    internal async Task<(int Cpus, long MemoryBytes)> ReadHostCapacityAsync(CancellationToken token)
    {
        using var response = await engine.GetAsync("info", token);
        response.EnsureSuccessStatusCode();
        using var data = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
        return (data.RootElement.GetProperty("NCPU").GetInt32(), data.RootElement.GetProperty("MemTotal").GetInt64());
    }
    private readonly HttpClient engine;
    private readonly string? workspaceVolume;
    private readonly SemaphoreSlim initialization = new(1, 1);
    private bool initialized;
    private int sandboxUserId;
    private int sandboxGroupId;
    private FileStream? hostLock;

    public DockerExecutionBackend(string? workspaceVolume = null)
    {
        ValidateWorkspaceVolume(workspaceVolume);
        this.workspaceVolume = workspaceVolume;
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, token) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try
                {
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint("/var/run/docker.sock"), token);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        };
        engine = new HttpClient(handler) { BaseAddress = new Uri("http://docker/v1.51/"), Timeout = TimeSpan.FromSeconds(3) };
    }

    public async Task<ExecutionEvidence> ExecuteAsync(ExecutionRequest request, CancellationToken token)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Run the Judge Worker on Linux, directly or in a Linux container.");
        Validate(request);
        var metricStage = request.Stage == ExecutionStage.Compile ? "compile" : "run";
        using var totalMeasurement = JudgingMetrics.Measure(metricStage, "total");
        using (JudgingMetrics.Measure(metricStage, "initialize"))
            await EnsureInitializedAsync(token);
        var container = "minos-m6-" + Guid.NewGuid().ToString("N");
        var workspace = Path.Combine(Root, "job-" + Guid.NewGuid().ToString("N"));
        var payloadName = request.Stage == ExecutionStage.Compile ? "main.cpp" : "main";
        var watch = new Stopwatch();
        DockerExecutionSession? execution = null;
        DockerProcessGuard? processGuard = null;
        Task<(byte[] Stdout, byte[] Stderr)>? capture = null;
        byte[] stdout = [];
        byte[] stderr = [];
        byte[]? artifact = null;
        int? exitCode = null;
        var wallTimeout = false;
        var cpuLimit = false;
        var memoryFailure = false;
        var outputLimit = 0;
        var cancelled = false;
        var snapshotComplete = false;
        var cleanupComplete = false;
        TimeSpan? cpuUsage = null;
        long? memoryPeak = null;
        string? infrastructureFailure = null;
        var operation = "Provision";
        var phaseMeasurement = JudgingMetrics.Measure(metricStage, "workspace");

        try
        {
            token.ThrowIfCancellationRequested();
            CreatePrivateWorkspace(workspace);
            var payloadPath = Path.Combine(workspace, payloadName);
            await File.WriteAllBytesAsync(payloadPath, request.Payload, token);
            File.SetUnixFileMode(payloadPath, request.Stage == ExecutionStage.Compile
                ? UnixFileMode.UserRead
                : UnixFileMode.UserRead | UnixFileMode.UserExecute);
            var inputPath = Path.Combine(workspace, "stdin.bin");
            await File.WriteAllBytesAsync(inputPath, request.Input, token);
            File.SetUnixFileMode(inputPath, UnixFileMode.UserRead);

            var limits = request.Limits;
            NextPhase("provision");
            await ProvisionAsync(container, workspace, limits, token);
            operation = "ProcessGuard";
            NextPhase("guard");
            processGuard = await DockerProcessGuard.StartAsync(container, $"{sandboxUserId}:{sandboxGroupId}");
            operation = "BaselineEvidence";
            NextPhase("baseline-evidence");
            var baseline = await ReadTelemetryAsync(container);

            var command = request.Stage == ExecutionStage.Compile
                ? new[] { "g++", "-std=c++20", "-O2", "-pipe", "/input/main.cpp", "-o", "/scratch/main" }
                : new[] { "/bin/sh", "-c", "exec /input/main < /input/stdin.bin" };
            watch.Start();
            operation = "StartExecution";
            NextPhase("execution");
            execution = await DockerExecutionSession.StartAsync(engine, container, $"{sandboxUserId}:{sandboxGroupId}", new[]
            {
                "/usr/bin/env", "-i", "PATH=/usr/local/bin:/usr/bin:/bin", "TMPDIR=/scratch", "HOME=/scratch"
            }.Concat(command).ToArray(), token);
            capture = execution.CaptureAsync(limits.StdoutBytes, limits.StderrBytes,
                () => Interlocked.Exchange(ref outputLimit, 1));
            long lastProcessId = 0;
            while (true)
            {
                operation = "ProcessEvidence";
                var state = await execution.InspectAsync(token);
                if (state.ProcessId > 0) lastProcessId = state.ProcessId;
                if (!state.Running)
                {
                    exitCode = state.ExitCode;
                    wallTimeout = watch.Elapsed >= limits.WallTime;
                    break;
                }
                // Docker can keep an exec marked Running while orphaned descendants
                // retain its output pipe. Inspect the actual PID through the daemon.
                if (lastProcessId > 0 && !await IsProcessRunningAsync(container, lastProcessId, token))
                {
                    watch.Stop();
                    wallTimeout = watch.Elapsed >= limits.WallTime;
                    await processGuard.TerminateAsync();
                    var attachmentDeadline = Stopwatch.StartNew();
                    do
                    {
                        state = await execution.InspectAsync(CancellationToken.None);
                        if (!state.Running) { exitCode = state.ExitCode; break; }
                        if (attachmentDeadline.Elapsed > TimeSpan.FromSeconds(5))
                            throw new IOException("Docker did not retain the completed process exit code.");
                        await Task.Delay(25, CancellationToken.None);
                    } while (true);
                    break;
                }
                var sample = await ReadStatsAsync(container, token);
                cpuUsage = TimeSpan.FromTicks(Math.Max(0, sample.CpuNanoseconds - baseline.CpuMicroseconds * 1000) / 100);
                wallTimeout = watch.Elapsed >= limits.WallTime;
                cpuLimit = cpuUsage >= limits.CpuTime;
                cancelled = token.IsCancellationRequested;
                if (wallTimeout || cpuLimit || outputLimit != 0 || cancelled)
                {
                    if (lastProcessId == 0 && !cancelled)
                        throw new IOException("Docker never identified the submitted process.");
                    await processGuard.TerminateAsync();
                    var termination = Stopwatch.StartNew();
                    do
                    {
                        state = await execution.InspectAsync(CancellationToken.None);
                        if (!state.Running) { exitCode = state.ExitCode; break; }
                        if (termination.Elapsed > TimeSpan.FromSeconds(5))
                            throw new IOException("The sandboxed process did not terminate.");
                        await Task.Delay(25, CancellationToken.None);
                    } while (true);
                    break;
                }
                await Task.Delay(25, CancellationToken.None);
            }
            watch.Stop();
            NextPhase("evidence");
            operation = "TerminateDescendants";
            await processGuard.TerminateAsync();
            operation = "CaptureOutput";
            (stdout, stderr) = await capture.WaitAsync(TimeSpan.FromSeconds(5));

            operation = "FinalEvidence";
            var final = await ReadTelemetryAsync(container);
            cpuUsage = TimeSpan.FromMicroseconds(Math.Max(0, final.CpuMicroseconds - baseline.CpuMicroseconds));
            cpuLimit |= cpuUsage >= limits.CpuTime;
            memoryPeak = final.MemoryPeakBytes;
            memoryFailure = final.OomKills > baseline.OomKills || final.MemoryLimitEvents > baseline.MemoryLimitEvents;
            snapshotComplete = true;

            if (request.Stage == ExecutionStage.Compile && !CompilationFailed())
            {
                operation = "CompiledArtifact";
                var compiled = await RunControlCommandAsync(container, $"{sandboxUserId}:{sandboxGroupId}",
                    ["/bin/sh", "-c", "test -f /scratch/main && test ! -L /scratch/main && cat /scratch/main"],
                    MaximumExecutableBytes,
                    TimeSpan.FromSeconds(5));
                if (compiled.ExitCode != 0 || compiled.Output.Length == 0)
                    throw new IOException("Compiler did not produce a regular bounded executable.");
                artifact = compiled.Output;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            cancelled = true;
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            // Closing a Docker attachment during cancellation can surface as I/O failure.
            cancelled = true;
        }
        catch (Exception error)
        {
            infrastructureFailure = operation + ":" + error.GetType().Name;
        }
        finally
        {
            watch.Stop();
            NextPhase("cleanup");
            if (execution is not null)
            {
                try
                {
                    if (processGuard is not null) await processGuard.TerminateAsync();
                    if (capture is not null) (stdout, stderr) = await capture.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch
                {
                    infrastructureFailure ??= "AttachmentCleanupFailure";
                }
                finally
                {
                    execution.Dispose();
                }
            }
            processGuard?.Dispose();
            try
            {
                await RemoveContainerAsync(container);
                DeleteWorkspace(workspace);
                cleanupComplete = true;
            }
            catch
            {
                CleanupFailures.Add(1);
                infrastructureFailure ??= "SandboxCleanupFailure";
            }
            phaseMeasurement.Dispose();
        }

        return new(
            exitCode, wallTimeout, cpuLimit, memoryFailure, outputLimit != 0, cancelled,
            watch.Elapsed, cpuUsage, memoryPeak, stdout, stderr, artifact,
            snapshotComplete, cleanupComplete, infrastructureFailure);

        bool CompilationFailed() =>
            wallTimeout || cpuLimit || memoryFailure || outputLimit != 0 || cancelled ||
            infrastructureFailure is not null || exitCode != 0;

        void NextPhase(string phase)
        {
            phaseMeasurement.Dispose();
            phaseMeasurement = JudgingMetrics.Measure(metricStage, phase);
        }
    }

    internal async Task EnsureInitializedAsync(CancellationToken token)
    {
        if (initialized) return;
        await initialization.WaitAsync(token);
        try
        {
            if (initialized) return;
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Run the Judge Worker on Linux, directly or in a Linux container.");
            if (Path.Exists(Root) && new DirectoryInfo(Root).LinkTarget is not null)
                throw new IOException("Execution workspace root is unsafe.");
            Directory.CreateDirectory(Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var lockPath = Path.Combine(Root, ".worker.lock");
            if (new FileInfo(lockPath).LinkTarget is not null)
                throw new IOException("Judge host lock is unsafe.");
            hostLock ??= new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            // The lifetime lock prevents startup cleanup from touching a live runner.
            var filesystem = await RunTrustedAsync(["run", "--rm", "--network", "none", "--entrypoint", "/bin/true", Image]);
            if (filesystem.ExitCode != 0) throw new IOException("The pinned execution image is unavailable.");
            var stat = await RunProcessAsync("stat", ["-f", "-c", "%T", Root]);
            if (stat.ExitCode != 0 || Encoding.UTF8.GetString(stat.Output).Trim() is not ("ext2/ext3" or "tmpfs" or "xfs" or "btrfs" or "overlayfs"))
                throw new IOException("Execution workspaces require a Linux-native filesystem.");
            var uid = await RunProcessAsync("id", ["-u"]);
            var gid = await RunProcessAsync("id", ["-g"]);
            if (uid.ExitCode != 0 || gid.ExitCode != 0 ||
                !int.TryParse(Encoding.UTF8.GetString(uid.Output).Trim(), CultureInfo.InvariantCulture, out sandboxUserId) ||
                !int.TryParse(Encoding.UTF8.GetString(gid.Output).Trim(), CultureInfo.InvariantCulture, out sandboxGroupId) ||
                sandboxUserId == 0)
                throw new IOException("The Judge Worker must run as a non-root Linux user.");
            await RecoverAbandonedAsync(token);
            initialized = true;
        }
        finally
        {
            initialization.Release();
        }
    }

    private async Task RecoverAbandonedAsync(CancellationToken token)
    {
        var filters = Uri.EscapeDataString(JsonSerializer.Serialize(new { label = new[] { Label } }));
        using var response = await engine.GetAsync($"containers/json?all=true&filters={filters}", token);
        response.EnsureSuccessStatusCode();
        using var containers = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        foreach (var container in containers.RootElement.EnumerateArray())
        {
            var name = container.GetProperty("Names")[0].GetString()!.TrimStart('/');
            if (!name.StartsWith("minos-m6-", StringComparison.Ordinal) || !Guid.TryParseExact(name[9..], "N", out _))
                throw new IOException("Unexpected sandbox ownership; recovery requires operator inspection.");
            token.ThrowIfCancellationRequested();
            await RemoveContainerAsync(name);
            RecoveredContainers.Add(1);
        }
        foreach (var directory in Directory.EnumerateDirectories(Root))
        {
            token.ThrowIfCancellationRequested();
            DeleteWorkspace(directory);
        }
    }

    private async Task<(long CpuNanoseconds, long MemoryBytes)> ReadStatsAsync(string container, CancellationToken token)
    {
        using var response = await engine.GetAsync($"containers/{container}/stats?stream=false&one-shot=true", token);
        response.EnsureSuccessStatusCode();
        using var stats = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var root = stats.RootElement;
        var cpu = root.GetProperty("cpu_stats").GetProperty("cpu_usage").GetProperty("total_usage").GetInt64();
        var memory = root.GetProperty("memory_stats").TryGetProperty("usage", out var usage) ? usage.GetInt64() : 0;
        return (cpu, memory);
    }

    private async Task<bool> IsProcessRunningAsync(string container, long processId, CancellationToken token)
    {
        if (processId <= 0) throw new IOException("Docker did not identify the running process.");
        using var response = await engine.GetAsync($"containers/{container}/top?ps_args=-eo%20pid,stat", token);
        response.EnsureSuccessStatusCode();
        using var state = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var titles = state.RootElement.GetProperty("Titles").EnumerateArray().Select(value => value.GetString()).ToArray();
        var pidColumn = Array.IndexOf(titles, "PID");
        var statusColumn = Array.IndexOf(titles, "STAT");
        if (pidColumn < 0 || statusColumn < 0) throw new IOException("Docker process evidence was incomplete.");
        foreach (var process in state.RootElement.GetProperty("Processes").EnumerateArray())
        {
            var values = process.EnumerateArray().Select(value => value.GetString()!).ToArray();
            if (values.Length != titles.Length || !long.TryParse(values[pidColumn], CultureInfo.InvariantCulture, out var pid))
                throw new IOException("Docker process evidence was invalid.");
            if (pid == processId) return !values[statusColumn].StartsWith('Z') && !values[statusColumn].StartsWith('X');
        }
        return false;
    }

    private async Task ProvisionAsync(string container, string workspace, ExecutionLimits limits, CancellationToken token)
    {
        var fileSize = Math.Max(limits.ScratchBytes, MaximumExecutableBytes);
        var cpuSeconds = (long)Math.Ceiling(limits.CpuTime.TotalSeconds) + 1;
        var definition = new
        {
            Image,
            Cmd = new[] { "sleep", ((int)Math.Ceiling(limits.WallTime.TotalSeconds) + 30).ToString(CultureInfo.InvariantCulture) },
            Labels = new Dictionary<string, string> { ["minos.judge"] = "m6" },
            HostConfig = new
            {
                NetworkMode = "none", CapDrop = new[] { "ALL" }, SecurityOpt = new[] { "no-new-privileges" },
                ReadonlyRootfs = true, Memory = limits.MemoryBytes, MemorySwap = limits.MemoryBytes,
                NanoCpus = 1_000_000_000L, PidsLimit = limits.Processes + 2, ShmSize = 1024 * 1024,
                Tmpfs = new Dictionary<string, string>
                {
                    ["/scratch"] = $"size={limits.ScratchBytes},mode=700,uid={sandboxUserId},gid={sandboxGroupId},nosuid,nodev,noexec"
                },
                LogConfig = new { Type = "none" },
                Ulimits = new[]
                {
                    new { Name = "nofile", Soft = 64L, Hard = 64L },
                    new { Name = "core", Soft = 0L, Hard = 0L },
                    new { Name = "fsize", Soft = fileSize, Hard = fileSize },
                    new { Name = "cpu", Soft = cpuSeconds, Hard = cpuSeconds }
                },
                // Never mount the volume root: only this stage's read-only subdirectory.
                Mounts = new[] { new
                {
                    Type = workspaceVolume is null ? "bind" : "volume",
                    Source = workspaceVolume ?? workspace, Target = "/input", ReadOnly = true,
                    VolumeOptions = workspaceVolume is null ? null : new { NoCopy = true, Subpath = Path.GetFileName(workspace) }
                } }
            }
        };
        using var created = await engine.PostAsJsonAsync($"containers/create?name={container}", definition, token);
        created.EnsureSuccessStatusCode();
        using var started = await engine.PostAsync($"containers/{container}/start", null, token);
        started.EnsureSuccessStatusCode();
    }

    private async Task<(int ExitCode, byte[] Output)> RunControlCommandAsync(
        string container, string user, string[] command, int outputLimit, TimeSpan timeout)
    {
        using var deadline = new CancellationTokenSource(timeout);
        using var session = await DockerExecutionSession.StartAsync(engine, container, user, command, deadline.Token);
        var overflow = 0;
        var capture = session.CaptureAsync(outputLimit, 16 * 1024, () => Interlocked.Exchange(ref overflow, 1));
        // Observe stream faults/timeouts even when inspection fails; disposing the session cancels capture.
        try
        {
            var output = await capture.WaitAsync(deadline.Token);
            (bool Running, int ExitCode, long ProcessId) state;
            do
            {
                state = await session.InspectAsync(deadline.Token);
                if (state.Running) await Task.Delay(5, deadline.Token);
            } while (state.Running);
            if (overflow != 0) throw new IOException("Trusted Docker command output exceeded its capture bound.");
            return (state.ExitCode, output.Stdout);
        }
        finally
        {
            session.Dispose();
            try { await capture; } catch { /* The caller reports the original control/evidence fault. */ }
        }
    }

    private async Task<Telemetry> ReadTelemetryAsync(string container)
    {
        var result = await RunControlCommandAsync(container, "0:0", ["/bin/sh", "-c",
            "set -e; cat /sys/fs/cgroup/cpu.stat; echo MEMORY_PEAK; cat /sys/fs/cgroup/memory.peak; echo MEMORY_EVENTS; cat /sys/fs/cgroup/memory.events"
        ], 16 * 1024, TimeSpan.FromSeconds(3));
        if (result.ExitCode != 0) throw new IOException("Required cgroup evidence is unavailable.");
        var values = ParseTelemetry(Encoding.UTF8.GetString(result.Output));
        foreach (var required in new[] { "cpu.usage_usec", "memory.peak", "memory.max", "memory.oom_kill" })
            if (!values.ContainsKey(required)) throw new IOException("Required cgroup evidence is incomplete.");
        return new(values["cpu.usage_usec"], values["memory.peak"], values["memory.max"], values["memory.oom_kill"]);
    }

    private async Task RemoveContainerAsync(string container)
    {
        using var response = await engine.DeleteAsync($"containers/{container}?force=true&v=true");
        if (response.StatusCode is not (System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.NoContent))
            response.EnsureSuccessStatusCode();
    }

    private static Task<(int ExitCode, byte[] Output, string Error)> RunTrustedAsync(IEnumerable<string> arguments) =>
        DockerCommand.RunAsync(arguments, timeout: TimeSpan.FromSeconds(15));

    private static async Task<(int ExitCode, byte[] Output, string Error)> RunProcessAsync(
        string executable,
        IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException("Trusted process could not start.");
        var output = await process.StandardOutput.BaseStream.ReadAllBytesAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, output, error);
    }

    private static Dictionary<string, long> ParseTelemetry(string text)
    {
        var values = new Dictionary<string, long>();
        var section = "cpu";
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line == "MEMORY_PEAK") { section = "peak"; continue; }
            if (line == "MEMORY_EVENTS") { section = "memory"; continue; }
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (section == "peak") values["memory.peak"] = long.Parse(parts[0], CultureInfo.InvariantCulture);
            else if (parts.Length == 2) values[$"{section}.{parts[0]}"] = long.Parse(parts[1], CultureInfo.InvariantCulture);
        }
        return values;
    }

    internal static void ValidateWorkspaceVolume(string? volume)
    {
        if (volume is not null && (volume.Length is < 2 or > 200 ||
            !char.IsAsciiLetterOrDigit(volume[0]) ||
            volume.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '.' and not '-')))
            throw new InvalidOperationException("Minos:Execution:WorkspaceVolume must be a valid Docker volume name.");
    }

    internal static void Validate(ExecutionRequest request)
    {
        var limits = request.Limits;
        if (limits.WallTime <= TimeSpan.Zero || limits.WallTime > TimeSpan.FromMinutes(1) ||
            limits.CpuTime <= TimeSpan.Zero || limits.CpuTime > TimeSpan.FromSeconds(10) ||
            limits.MemoryBytes is < 16 * 1024 * 1024 or > 1024L * 1024 * 1024 ||
            limits.ScratchBytes is < 1024 * 1024 or > 64L * 1024 * 1024 ||
            limits.Processes is < 8 or > 64 || limits.StdoutBytes < 1 || limits.StdoutBytes > ProblemArtifactReference.MaximumBytes + 1 ||
            limits.StderrBytes is < 1 or > JudgingOutcome.MaximumCompilerDiagnosticsBytes ||
            request.Payload.Length > (request.Stage == ExecutionStage.Compile ? Submission.MaximumSourceBytes : MaximumExecutableBytes) ||
            request.Input.LongLength > ProblemArtifactReference.MaximumBytes)
            throw new ArgumentOutOfRangeException(nameof(request), "Execution request exceeds the supported sandbox limits.");
    }

    [SupportedOSPlatform("linux")]
    private static void CreatePrivateWorkspace(string workspace)
    {
        if (Directory.Exists(workspace) || File.Exists(workspace)) throw new IOException("Execution workspace collision.");
        Directory.CreateDirectory(workspace, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void DeleteWorkspace(string workspace)
    {
        if (!Directory.Exists(workspace)) return;
        var full = Path.GetFullPath(workspace);
        var name = Path.GetFileName(full);
        if (Path.GetDirectoryName(full) != Root || !name.StartsWith("job-", StringComparison.Ordinal) ||
            !Guid.TryParseExact(name[4..], "N", out _) || new DirectoryInfo(full).LinkTarget is not null)
            throw new IOException("Refusing to remove an unexpected execution workspace.");
        File.Delete(Path.Combine(full, "main.cpp"));
        File.Delete(Path.Combine(full, "main"));
        File.Delete(Path.Combine(full, "stdin.bin"));
        Directory.Delete(full);
    }

    public void Dispose()
    {
        engine.Dispose();
        hostLock?.Dispose();
        initialization.Dispose();
    }

    private sealed record Telemetry(long CpuMicroseconds, long MemoryPeakBytes, long MemoryLimitEvents, long OomKills);
}

internal static class StreamExtensions
{
    public static async Task<byte[]> ReadAllBytesAsync(this Stream stream)
    {
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }
}
