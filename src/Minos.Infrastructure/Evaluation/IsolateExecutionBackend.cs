using System.Collections.Concurrent;
using System.Diagnostics;
using System.Formats.Tar;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Minos.Application.Evaluation;

namespace Minos.Infrastructure.Evaluation;

// Direct-Linux opt-in runtime adapter. Compilation/evidence still use the pinned Docker compiler.
internal sealed class IsolateExecutionBackend(
    DockerExecutionBackend compiler, string root, bool requireSafeCoreDumpPolicy = true, int boxCount = 1) : IExecutionBackend, IDisposable
{
    private const long ScratchBytes = 8 * 1024 * 1024;
    private const string Commit = "8f185bb37f3f23e29b33b0c7727c91c13429abe3";
    private static readonly string[] Libraries = ["libstdc++.so.6", "libgcc_s.so.1", "libc.so.6", "libm.so.6", "ld-linux-x86-64.so.2"];
    private readonly SemaphoreSlim gate = new(ValidateBoxCount(boxCount), boxCount);
    private readonly ConcurrentQueue<int> availableBoxes = new(Enumerable.Range(0, boxCount));
    private volatile bool cleanupFault;
    private readonly SemaphoreSlim initialization = new(1, 1);
    private bool initialized;
    private string Helper(int id) => Path.Combine(HelperRoot(id), "isolate");
    private string HelperRoot(int id) => boxCount == 1 ? root : Path.Combine(root, "helpers", id.ToString(CultureInfo.InvariantCulture));
    private string BoxRoot(int id) => boxCount == 1 ? Path.Combine(root, "boxes") : Path.Combine(root, "boxes", "slot-" + id.ToString(CultureInfo.InvariantCulture));
    private string Work => Path.Combine(root, "work");
    private string Runtime => Path.Combine(Work, "runtime");
    private string Box(int id) => Path.Combine(BoxRoot(id), id.ToString(CultureInfo.InvariantCulture));

    internal static int ValidateBoxCount(int count) => count is >= 1 and <= 16 ? count
        : throw new InvalidOperationException("Isolate box count must be between 1 and 16.");
    private string? cgroup;

    internal static void ValidateConfiguration(string? root, string? volume)
    {
        if (volume is not null)
            throw new InvalidOperationException("The Isolate runner requires direct Linux hosting without Minos:Execution:WorkspaceVolume.");
        if (string.IsNullOrEmpty(root) || root.Length > 200 || !root.StartsWith("/opt/minos-", StringComparison.Ordinal) ||
            root.EndsWith('/') || root.Contains("..", StringComparison.Ordinal) ||
            root.AsSpan(5).Contains('/') ||
            root.Any(value => !char.IsAsciiLetterOrDigit(value) && value is not '/' and not '-' and not '_'))
            throw new InvalidOperationException("Minos:Execution:IsolateRoot must identify a dedicated /opt/minos-* installation.");
    }

    internal async Task EnsureInitializedAsync(CancellationToken token)
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("The pinned Isolate runtime requires direct x86-64 Linux hosting.");
        if (cleanupFault) throw new IOException("Native cleanup failed; restart after host recovery.");
        if (initialized) return;
        await initialization.WaitAsync(token);
        try
        {
            if (initialized) return;
            ValidateConfiguration(root, null);
            if (File.Exists("/.dockerenv")) throw new InvalidOperationException("Do not enable Isolate inside the Compose Worker.");
            // The existing lifetime host lock guards recovery for both execution profiles.
            await compiler.EnsureInitializedAsync(token);
            var uid = (await ControlAsync("/usr/bin/id", ["-u"], token)).Trim();
            var workerUid = ParseLong(uid);
            var workerGid = ParseLong((await ControlAsync("/usr/bin/id", ["-g"], token)).Trim());
            if (workerUid == 0 || workerGid == 0 || workerUid >= 61000 && workerUid < 61000 + boxCount || workerGid >= 61000 && workerGid < 61000 + boxCount)
                throw new InvalidOperationException("The Worker identity must be non-root and outside the reserved sandbox identities.");
            var allowedCpus = (await File.ReadAllLinesAsync("/proc/self/status", token)).Single(line => line.StartsWith("Cpus_allowed_list:", StringComparison.Ordinal)).Split(':', 2)[1].Trim();
            if (!Enumerable.Range(0, boxCount).All(id => CpuAllowed(allowedCpus, id)) || boxCount >= Environment.ProcessorCount)
                throw new IOException("Native boxes require distinct available CPUs and management headroom.");
            foreach (var identityFile in new[] { "/etc/passwd", "/etc/group" })
                if ((await File.ReadAllLinesAsync(identityFile, token)).Any(line => { var fields = line.Split(':'); return fields.Length > 2 && long.TryParse(fields[2], out var id) && id >= 61000 && id < 61000 + boxCount; }))
                    throw new IOException("Reserved sandbox identities must not belong to host accounts.");
            foreach (var directory in new[] { "/opt", root, Path.Combine(root, "boxes"), Path.Combine(root, "locks") })
                await RequireOwnershipAsync(directory, "0", token);
            if (boxCount > 1) await RequireOwnershipAsync(Path.Combine(root, "helpers"), "0", token);
            await RequireOwnershipAsync(Path.Combine(root, "cgroup"), "0", token);
            var mounts = await File.ReadAllLinesAsync("/proc/mounts", token);
            for (var id = 0; id < boxCount; id++)
            {
                await RequireOwnershipAsync(HelperRoot(id), "0", token);
                foreach (var file in new[] { Helper(id), Path.Combine(HelperRoot(id), "isolate.cf"), BoxRoot(id) })
                    await RequireOwnershipAsync(file, "0", token);
                var helperMode = (await ControlAsync("/usr/bin/stat", ["-c", "%a", Helper(id)], token)).Trim();
                if (helperMode != "4755") throw new IOException("The pinned helper must be root-owned mode 4755.");
                var version = await ControlAsync(Helper(id), ["--version"], token);
                if (!version.Contains("isolator 2.7", StringComparison.Ordinal) || !version.Contains("commit " + Commit[..7], StringComparison.Ordinal))
                    throw new IOException("The configured helper does not match the tested Isolate version/commit.");
                await ControlAsync(Helper(id), ["--check-config"], token);
                var configuration = ReadPairs(await File.ReadAllTextAsync(Path.Combine(HelperRoot(id), "isolate.cf"), token), '=');
                if (configuration.GetValueOrDefault("box_root") != BoxRoot(id) ||
                    configuration.GetValueOrDefault("lock_root") != Path.Combine(root, "locks") ||
                    configuration.GetValueOrDefault("cg_root") != "auto:" + Path.Combine(root, "cgroup") ||
                    configuration.GetValueOrDefault("num_boxes") != boxCount.ToString(CultureInfo.InvariantCulture) ||
                    configuration.GetValueOrDefault($"box{id}.cpus") != id.ToString(CultureInfo.InvariantCulture) ||
                    configuration.GetValueOrDefault("first_uid") != "61000" || configuration.GetValueOrDefault("first_gid") != "61000")
                    throw new IOException("Native helper configuration must match its box, identity range and CPU.");
                var mount = mounts.Select(line => line.Split(' ')).SingleOrDefault(parts => parts.Length >= 4 && parts[1] == BoxRoot(id));
                if (mount is null || mount[2] != "tmpfs" || !new[] { "noexec", "nosuid", "nodev" }.All(option => mount[3].Split(',').Contains(option)))
                    throw new IOException("Each native box requires its own bounded noexec/nosuid/nodev tmpfs.");
                var size = (await ControlAsync("/usr/bin/stat", ["-f", "-c", "%S %b", BoxRoot(id)], token)).Trim().Split(' ');
                if (size.Length != 2 || checked(ParseLong(size[0]) * ParseLong(size[1])) != ScratchBytes)
                    throw new IOException("Each native box must have an independent eight-MiB filesystem budget.");
            }
            await RequireOwnershipAsync(Work, uid, token);
            if (File.GetUnixFileMode(Work) != (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute))
                throw new IOException("The native controller workspace must be private.");
            if ((await File.ReadAllTextAsync("/proc/sys/fs/protected_hardlinks", token)).Trim() != "1")
                throw new IOException("Native hosting requires protected hardlinks.");
            if (requireSafeCoreDumpPolicy && (await File.ReadAllTextAsync("/proc/sys/kernel/core_pattern", token)).TrimStart().StartsWith('|'))
                throw new IOException("Native hosting requires a non-piped core-dump policy; RLIMIT_CORE cannot disable a host pipe collector.");
            cgroup = (await ControlAsync(Helper(0), ["--cg", "--print-cg-root"], token)).Trim();
            if (!cgroup.StartsWith("/sys/fs/cgroup/", StringComparison.Ordinal) || cgroup.Contains("..", StringComparison.Ordinal) ||
                cgroup != (await File.ReadAllTextAsync(Path.Combine(root, "cgroup"), token)).Trim() ||
                (await File.ReadAllTextAsync(Path.Combine(cgroup, "memory.swap.max"), token)).Trim() != "0")
                throw new IOException("Native hosting requires its dedicated swap-disabled delegated cgroup.");
            for (var id = 0; id < boxCount; id++)
            {
                if ((await ControlAsync(Helper(id), ["--cg", "--print-cg-root"], token)).Trim() != cgroup)
                    throw new IOException("Native helpers must share the dedicated delegated cgroup root.");
                await CleanupBoxAsync(id);
                if (Directory.EnumerateFileSystemEntries(BoxRoot(id)).Any())
                    throw new IOException("Unrecognized native box workspace; manual inspection is required.");
            }
            if (Directory.EnumerateDirectories(cgroup).Any(path => Path.GetFileName(path) != "daemon"))
                throw new IOException("Unrecognized native cgroups; manual inspection is required.");
            foreach (var directory in Directory.EnumerateDirectories(Work).Where(path => path != Runtime)) DeleteStage(directory);
            await ExportRuntimeAsync(token);
            initialized = true;
        }
        finally { initialization.Release(); }
    }

    public async Task<ExecutionEvidence> ExecuteAsync(ExecutionRequest request, CancellationToken token)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Run the native Worker on Linux.");
        DockerExecutionBackend.Validate(request);
        if (request.Stage == ExecutionStage.Compile)
        {
            await EnsureInitializedAsync(token);
            return await compiler.ExecuteAsync(request, token);
        }
        if (request.Limits.ScratchBytes != ScratchBytes)
            throw new ArgumentOutOfRangeException(nameof(request), "This native profile uses the official eight-MiB runtime scratch budget.");
        int boxId;
        using (JudgingMetrics.Measure("run", "initialize"))
        {
            await EnsureInitializedAsync(token);
            await gate.WaitAsync(token);
            if (!availableBoxes.TryDequeue(out boxId)) { gate.Release(); throw new IOException("Native box pool is inconsistent."); }
            if (cleanupFault) { availableBoxes.Enqueue(boxId); gate.Release(); throw new IOException("Native cleanup failed; further execution is blocked."); }
        }
        using var total = JudgingMetrics.Measure("run", "total");
        var stage = Path.Combine(Work, "stage-" + Guid.NewGuid().ToString("N"));
        var metadata = stage + ".meta"; // Never mounted into the submitted process.
        using var captureCancellation = new CancellationTokenSource();
        Process? process = null;
        LinuxProcessSignal? signal = null;
        CancellationTokenRegistration cancellation = default;
        Task<byte[]>? captureOut = null, captureError = null;
        var stdout = Array.Empty<byte>(); var stderr = Array.Empty<byte>();
        int? exitCode = null;
        TimeSpan wall = default;
        TimeSpan? cpu = null;
        long? memory = null;
        var outputLimit = 0;
        var cancelled = false; var snapshot = false; var cleanup = false;
        var cpuLimit = false; var wallTimeout = false; var memoryFailure = false;
        string? failure = null;
        var operation = "NativeWorkspace";
        try
        {
            token.ThrowIfCancellationRequested();
            using (JudgingMetrics.Measure("run", "workspace"))
            {
                Directory.CreateDirectory(stage, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                // Directory creation honors the Worker's private umask; the bind root must also admit the distinct sandbox UID.
                File.SetUnixFileMode(stage, (UnixFileMode)Convert.ToInt32("755", 8));
                await File.WriteAllBytesAsync(Path.Combine(stage, "main"), request.Payload, token);
                File.SetUnixFileMode(Path.Combine(stage, "main"), (UnixFileMode)Convert.ToInt32("555", 8));
                await File.WriteAllBytesAsync(Path.Combine(stage, "stdin.bin"), request.Input, token);
                File.SetUnixFileMode(Path.Combine(stage, "stdin.bin"), (UnixFileMode)Convert.ToInt32("444", 8));
            }
            operation = "NativeProvision";
            using (JudgingMetrics.Measure("run", "provision"))
            {
                var box = (await ControlAsync(Helper(boxId), ["--cg", "--box-id=" + boxId, "--init"], token)).Trim();
                if (box != Box(boxId)) throw new IOException("Unexpected native box path.");
            }
            var limits = request.Limits;
            var args = new[]
            {
                "--cg", "--box-id=" + boxId, "--silent", "--cg-mem=" + (limits.MemoryBytes / 1024).ToString(CultureInfo.InvariantCulture),
                "--time=" + limits.CpuTime.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture),
                "--wall-time=" + limits.WallTime.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture),
                "--processes=" + limits.Processes, "--open-files=64", "--core=0", "--fsize=" + (ScratchBytes / 1024), "--no-default-dirs",
                "--dir=/box=" + stage, "--dir=/input=" + stage, "--dir=/scratch=" + Path.Combine(Box(boxId), "box") + ":rw:noexec",
                "--dir=/lib64=" + Runtime, "--dir=/lib/x86_64-linux-gnu=" + Runtime, "--dir=/usr/local/lib64=" + Runtime,
                "--dir=/proc=proc:fs", "--chdir=/scratch", "--meta=" + metadata, "--stdin=/input/stdin.bin",
                "--env=PATH=/usr/local/bin:/usr/bin:/bin", "--env=TMPDIR=/scratch", "--env=HOME=/scratch", "--run", "--", "/input/main"
            };
            operation = "NativeExecution";
            using (JudgingMetrics.Measure("run", "execution"))
            {
                process = Start(Helper(boxId), args);
                var managerSignal = signal = new(process.Id);
                cancellation = token.Register(managerSignal.Interrupt);
                void Overflow() { if (Interlocked.Exchange(ref outputLimit, 1) == 0) managerSignal.Interrupt(); }
                // Inherited pipes are outside scratch. Enforce each stream while draining, including ignored SIGXFSZ.
                captureOut = DockerCommand.CaptureAsync(process.StandardOutput.BaseStream, limits.StdoutBytes, Overflow, captureCancellation.Token);
                captureError = DockerCommand.CaptureAsync(process.StandardError.BaseStream, limits.StderrBytes, Overflow, captureCancellation.Token);
                await process.WaitForExitAsync().WaitAsync(limits.WallTime + TimeSpan.FromSeconds(5));
                stdout = await captureOut.WaitAsync(TimeSpan.FromSeconds(5));
                stderr = await captureError.WaitAsync(TimeSpan.FromSeconds(5));
                cancelled = token.IsCancellationRequested;
            }
            operation = "NativeEvidence";
            using (JudgingMetrics.Measure("run", "evidence"))
            {
                if (process.ExitCode is not (0 or 1)) throw new IOException("The native sandbox reported an infrastructure failure.");
                if (new FileInfo(metadata).Length > 16 * 1024) throw new IOException("Native metadata exceeded its bound.");
                var meta = ReadPairs(await File.ReadAllTextAsync(metadata, CancellationToken.None), ':');
                if (meta.GetValueOrDefault("status") is not (null or "RE" or "SG" or "TO")) throw new IOException("Unrecognized native status.");
                wall = ParseSeconds(meta["time-wall"]);
                _ = ParseSeconds(meta["time"]);
                _ = ParseLong(meta["cg-mem"]);
                var cpuStats = ReadPairs(await File.ReadAllTextAsync(Path.Combine(cgroup!, "box-" + boxId, "cpu.stat")), ' ');
                var events = ReadPairs(await File.ReadAllTextAsync(Path.Combine(cgroup!, "box-" + boxId, "memory.events")), ' ');
                cpu = TimeSpan.FromTicks(checked(ParseLong(cpuStats["usage_usec"]) * 10));
                memory = ParseLong((await File.ReadAllTextAsync(Path.Combine(cgroup!, "box-" + boxId, "memory.peak"))).Trim());
                memoryFailure = ParseLong(events["max"]) != 0 || ParseLong(events["oom_kill"]) != 0;
                if ((await File.ReadAllTextAsync(Path.Combine(cgroup!, "box-" + boxId, "cgroup.procs"))).Trim().Length != 0 ||
                    ParseLong((await File.ReadAllTextAsync(Path.Combine(cgroup!, "box-" + boxId, "memory.max"))).Trim()) != limits.MemoryBytes ||
                    (await File.ReadAllTextAsync(Path.Combine(cgroup!, "box-" + boxId, "cpuset.cpus.effective"))).Trim() != boxId.ToString(CultureInfo.InvariantCulture) ||
                    (await File.ReadAllTextAsync(Path.Combine(cgroup!, "box-" + boxId, "memory.swap.max"))).Trim() != "0")
                    throw new IOException("Native descendants or memory-limit evidence are incomplete.");
                cpuLimit = cpu >= limits.CpuTime;
                wallTimeout = wall >= limits.WallTime;
                if (meta.GetValueOrDefault("status") == "TO")
                {
                    if (meta.GetValueOrDefault("message")?.Contains("wall clock", StringComparison.Ordinal) == true) wallTimeout = true;
                    else cpuLimit = true;
                }
                exitCode = meta.TryGetValue("exitcode", out var code) ? checked((int)ParseLong(code)) :
                    meta.TryGetValue("exitsig", out var sig) ? checked(128 + (int)ParseLong(sig)) :
                    meta.GetValueOrDefault("killed") == "1" && (cpuLimit || wallTimeout) ? 137 : null;
                if (exitCode is not (>= 0 and <= 255)) throw new IOException("Native exit evidence is missing.");
                snapshot = true;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { cancelled = true; }
        catch (Exception error) { failure = operation + ":" + error.GetType().Name; cancelled = token.IsCancellationRequested; }
        finally
        {
            using var cleanupTiming = JudgingMetrics.Measure("run", "cleanup");
            var boxClean = false;
            try
            {
                await cancellation.DisposeAsync();
                if (process is not null && !process.HasExited)
                {
                    signal?.Interrupt();
                    try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
                    catch { process.Kill(); await process.WaitForExitAsync(); failure ??= "NativeManagerCleanupFailure"; }
                }
            }
            catch { failure ??= "NativeManagerCleanupFailure"; }
            try { await CleanupBoxAsync(boxId); boxClean = true; }
            catch { failure ??= "NativeSandboxCleanupFailure"; }
            // Observe both streams independently even if control/cleanup failed.
            try
            {
                if (!boxClean) captureCancellation.Cancel();
                if (captureOut is not null) stdout = await captureOut.WaitAsync(TimeSpan.FromSeconds(5));
                if (captureError is not null) stderr = await captureError.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch { failure ??= "NativeOutputCleanupFailure"; }
            finally
            {
                captureCancellation.Cancel();
                if (captureOut is not null) { try { await captureOut; } catch { failure ??= "NativeOutputCleanupFailure"; } }
                if (captureError is not null) { try { await captureError; } catch { failure ??= "NativeOutputCleanupFailure"; } }
                try { DeleteStage(stage); File.Delete(metadata); cleanup = boxClean; }
                catch { failure ??= "NativeWorkspaceCleanupFailure"; }
                if (!cleanup) { cleanupFault = true; DockerExecutionBackend.RecordCleanupFailure(); }
                signal?.Dispose(); process?.Dispose();
                availableBoxes.Enqueue(boxId); gate.Release();
            }
        }
        return new(exitCode, wallTimeout, cpuLimit, memoryFailure, outputLimit != 0, cancelled,
            wall, cpu, memory, stdout, stderr, null, snapshot, cleanup, failure);
    }

    private async Task CleanupBoxAsync(int id)
    {
        await ControlAsync(Helper(id), ["--cg", "--box-id=" + id, "--cleanup"], CancellationToken.None);
        if (Directory.Exists(Box(id)) || (cgroup is not null && Directory.Exists(Path.Combine(cgroup, "box-" + id))))
            throw new IOException("Native cleanup left a box or cgroup behind.");
    }

    [SupportedOSPlatform("linux")]
    private async Task ExportRuntimeAsync(CancellationToken token)
    {
        using var measurement = JudgingMetrics.Measure("runtime", "export");
        if (new DirectoryInfo(Runtime).LinkTarget is not null) throw new IOException("Native runtime cache is unsafe.");
        Directory.CreateDirectory(Runtime, (UnixFileMode)Convert.ToInt32("755", 8));
        File.SetUnixFileMode(Runtime, (UnixFileMode)Convert.ToInt32("755", 8));
        if (Directory.EnumerateFileSystemEntries(Runtime).Any(path => !Libraries.Contains(Path.GetFileName(path))))
            throw new IOException("Unrecognized native runtime cache contents.");
        var archive = await DockerCommand.RunAsync([
            "run", "--rm", "--network", "none", "--cap-drop", "ALL", "--security-opt", "no-new-privileges", "--read-only",
            DockerExecutionBackend.Image, "tar", "-chf", "-", "-C", "/usr/local/lib64", Libraries[0], Libraries[1],
            "-C", "/lib/x86_64-linux-gnu", Libraries[2], Libraries[3], Libraries[4]
        ], 32 * 1024 * 1024, TimeSpan.FromSeconds(15));
        if (archive.ExitCode != 0) throw new IOException("Pinned runtime export failed.");
        using var bytes = new MemoryStream(archive.Output);
        await using var reader = new TarReader(bytes);
        var names = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.GetNextEntryAsync(cancellationToken: token) is { } entry)
        {
            if (!Libraries.Contains(entry.Name) || !names.Add(entry.Name) || entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) ||
                entry.Length is < 1 or > 16 * 1024 * 1024 || entry.DataStream is null)
                throw new IOException("Pinned runtime archive is invalid.");
            var target = Path.Combine(Runtime, entry.Name);
            if (new FileInfo(target).LinkTarget is not null) throw new IOException("Native runtime file is unsafe.");
            File.Delete(target);
            await using (var output = File.Create(target)) await entry.DataStream.CopyToAsync(output, token);
            File.SetUnixFileMode(target, (UnixFileMode)Convert.ToInt32("555", 8));
        }
        if (names.Count != Libraries.Length) throw new IOException("Pinned runtime archive is incomplete.");
    }

    [SupportedOSPlatform("linux")]
    private void DeleteStage(string stage)
    {
        if (!Directory.Exists(stage)) return;
        var name = Path.GetFileName(stage);
        if (Path.GetDirectoryName(stage) != Work || !name.StartsWith("stage-", StringComparison.Ordinal) ||
            !Guid.TryParseExact(name[6..], "N", out _) || new DirectoryInfo(stage).LinkTarget is not null)
            throw new IOException("Unrecognized native workspace; manual inspection is required.");
        foreach (var file in new[] { "main", "stdin.bin" }) File.Delete(Path.Combine(stage, file));
        Directory.Delete(stage);
        File.Delete(stage + ".meta");
    }

    private static async Task RequireOwnershipAsync(string path, string owner, CancellationToken token)
    {
        if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Native installation paths must not be symbolic links.");
        var fields = (await ControlAsync("/usr/bin/stat", ["-c", "%u %a", path], token)).Trim().Split(' ');
        if (fields.Length != 2 || fields[0] != owner || (Convert.ToInt32(fields[1], 8) & Convert.ToInt32("022", 8)) != 0)
            throw new IOException("Native installation ownership/permissions are unsafe.");
    }

    private static Process Start(string executable, string[] arguments)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return Process.Start(info) ?? throw new IOException("The native manager could not start.");
    }

    private static async Task<string> ControlAsync(string executable, string[] arguments, CancellationToken token)
    {
        using var process = Start(executable, arguments);
        var overflow = 0;
        var output = DockerCommand.CaptureAsync(process.StandardOutput.BaseStream, 16 * 1024, () => Interlocked.Exchange(ref overflow, 1));
        var errors = DockerCommand.CaptureAsync(process.StandardError.BaseStream, 16 * 1024, () => Interlocked.Exchange(ref overflow, 1));
        try { await process.WaitForExitAsync(token).WaitAsync(TimeSpan.FromSeconds(10), token); }
        finally
        {
            if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(); }
            await Task.WhenAll(output, errors);
        }
        if (process.ExitCode != 0 || overflow != 0) throw new IOException("A bounded native control command failed.");
        return Encoding.UTF8.GetString(await output);
    }

    internal static Dictionary<string, string> ReadPairs(string contents, char separator)
    {
        var pairs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in contents.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(line => !line.TrimStart().StartsWith('#')))
        {
            var parts = line.Split(separator, 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || parts[0].Length == 0 || !pairs.TryAdd(parts[0], parts[1])) throw new IOException("Native evidence/configuration has malformed or duplicate fields.");
        }
        return pairs;
    }

    private static bool CpuAllowed(string list, int id) => list.Split(',').Any(item =>
    {
        var bounds = item.Split('-');
        return bounds.Length == 1 ? int.Parse(bounds[0], CultureInfo.InvariantCulture) == id :
            id >= int.Parse(bounds[0], CultureInfo.InvariantCulture) && id <= int.Parse(bounds[1], CultureInfo.InvariantCulture);
    });

    private static long ParseLong(string value) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
        ? parsed : throw new IOException("Native resource evidence is invalid.");
    private static TimeSpan ParseSeconds(string value) => double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed) &&
        double.IsFinite(parsed) && parsed is >= 0 and <= 120 ? TimeSpan.FromSeconds(parsed) : throw new IOException("Native time evidence is invalid.");

    public void Dispose() { gate.Dispose(); initialization.Dispose(); }
}
