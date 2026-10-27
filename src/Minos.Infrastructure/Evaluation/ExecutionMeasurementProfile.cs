using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Minos.Application.Evaluation;

namespace Minos.Infrastructure.Evaluation;

// One conservative cohort per Linux boot, runner, pinned toolchain and execution budget.
// No host identifiers are exposed by the owner API. Bump the policy when accounting changes.
internal sealed class ExecutionMeasurementProfile(string runner, ExecutionBudgetOptions? budget, int boxes,
    ILogger<ExecutionMeasurementProfile> logger)
{
    private readonly Lazy<Task<string?>> identity = new(() => ReadAsync(runner, budget, boxes, logger));
    public Task<string?> ReadAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return identity.Value.WaitAsync(token);
    }

    private static async Task<string?> ReadAsync(string runner, ExecutionBudgetOptions? budget, int boxes,
        ILogger logger)
    {
        if (!OperatingSystem.IsLinux()) return null;
        try
        {
            var boot = (await File.ReadAllTextAsync("/proc/sys/kernel/random/boot_id")).Trim();
            var kernel = (await File.ReadAllTextAsync("/proc/sys/kernel/osrelease")).Trim();
            var cpu = await File.ReadAllLinesAsync("/proc/cpuinfo");
            var model = cpu.FirstOrDefault(line => line.StartsWith("model name", StringComparison.Ordinal) || line.StartsWith("Hardware", StringComparison.Ordinal));
            var memory = (await File.ReadAllLinesAsync("/proc/meminfo")).FirstOrDefault(line => line.StartsWith("MemTotal:", StringComparison.Ordinal));
            if (!Guid.TryParse(boot, out _) || string.IsNullOrWhiteSpace(model) || memory is null) return null;
            var evidence = JsonSerializer.Serialize(new { Policy = "max-test-cgroup-v1", Runner = runner, Boot = boot, Kernel = kernel,
                Model = model, Memory = memory, Cpus = Environment.ProcessorCount, Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                Toolchain = SupportedSubmission.ToolchainId, Budget = budget, Boxes = boxes });
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(evidence)));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Performance comparison profile unavailable; new results retain measurements without comparison eligibility");
            return null;
        }
    }
}
