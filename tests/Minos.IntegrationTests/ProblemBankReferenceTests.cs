using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Minos.Application.Evaluation;
using Minos.Domain.Evaluation;
using Minos.Infrastructure;
using Minos.Infrastructure.Evaluation;
using Minos.Infrastructure.Hosting;
using Xunit;

namespace Minos.IntegrationTests;

// Explicit developer-run reference checks. No production publication gate or stored reference solution.
public sealed class ProblemBankReferenceTests
{
    public static bool Enabled => OperatingSystem.IsLinux() &&
        Environment.GetEnvironmentVariable("MINOS_TEST_SANDBOX") == "1" &&
        Environment.GetEnvironmentVariable("MINOS_TEST_PROBLEM_BANK") is not null;

    [Fact(Skip = "Opt-in external bank reference checks in the approved Docker sandbox.", SkipUnless = nameof(Enabled))]
    public async Task Selected_references_match_samples_and_representative_tests_with_exact_bytes()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.GetFullPath(Environment.GetEnvironmentVariable("MINOS_TEST_PROBLEM_BANK")!);
        Assert.Null(Environment.GetEnvironmentVariable("MINOS_TEST_ISOLATE_ROOT"));
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "candidates.json"), token));
        var problems = document.RootElement.GetProperty("problems").EnumerateArray().ToArray();
        var target = int.Parse(Environment.GetEnvironmentVariable("MINOS_TEST_BANK_TARGET") ?? "200");
        Assert.InRange(target, 1, 500);
        var parallelism = int.Parse(Environment.GetEnvironmentVariable("MINOS_TEST_BANK_PARALLELISM") ?? "2");
        Assert.InRange(parallelism, 1, 8);
        var output = Path.Combine(root, "validation");
        Directory.CreateDirectory(output);
        var services = new ServiceCollection();
        services.AddMinosInfrastructure("Host=localhost;Database=unused;Username=unused",
            executionBudget: new ExecutionBudgetOptions(parallelism, parallelism > 2 ? 4096 : 2048));
        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<JudgeSandboxLifecycle>().InitializeAsync(token);
        var backend = provider.GetRequiredService<IExecutionBackend>();
        foreach (var header in new[] { "iostream", "bits/stdc++.h" })
        {
            var probe = await backend.ExecuteAsync(new(ExecutionStage.Compile,
                Encoding.UTF8.GetBytes($"#include <{header}>\nint main(){{std::cout<<1<<'\\n';}}\n"), [],
                new(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(8), 512L * 1024 * 1024, 64L * 1024 * 1024, 32, 32768, 32768)), token);
            await File.WriteAllTextAsync(Path.Combine(output, header == "iostream" ? "probe-iostream.json" : "probe-bits.json"),
                JsonSerializer.Serialize(new { probe.ExitCode, probe.MemoryFailure, probe.CpuLimit, probe.WallTimeout, probe.OutputLimit,
                    probe.MemoryPeakBytes, probe.CpuUsage, probe.CleanupComplete, probe.ResourceSnapshotComplete,
                    probe.InfrastructureFailure, probe.Cancelled, probe.WallElapsed, artifactBytes = probe.Artifact?.Length,
                    diagnostics = Encoding.UTF8.GetString(probe.Stderr) },
                    new JsonSerializerOptions { WriteIndented = true }), token);
            Assert.True(OfficialJudgingPolicy.HasCompleteEvidence(probe) && !OfficialJudgingPolicy.IsCompilationError(probe), "Compiler baseline probe failed; inspect bounded local diagnostics.");
        }
        var accepted = 0;
        await Parallel.ForEachAsync(problems, new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = token }, async (problem, cancellation) =>
        {
            var id = problem.GetProperty("id").GetString()!;
            Assert.Matches("^cf-[0-9]+-[a-z]{1,2}[0-9]?$", id);
            var fingerprint = Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(problem)));
            var destination = Path.Combine(output, id + ".json");
            if (File.Exists(destination))
            {
                using var existing = JsonDocument.Parse(await File.ReadAllTextAsync(destination, cancellation));
                if (existing.RootElement.GetProperty("manifestSha256").GetString() == fingerprint &&
                    existing.RootElement.TryGetProperty("compilationMemoryMegabytes", out var compilerMemory) && compilerMemory.GetInt32() == 512)
                {
                    if (!existing.RootElement.TryGetProperty("manifestFileSha256", out _))
                    {
                        var receipt = System.Text.Json.Nodes.JsonNode.Parse(existing.RootElement.GetRawText())!;
                        receipt["manifestFileSha256"] = Hash(await File.ReadAllBytesAsync(Path.Combine(root, "candidates", id, "manifest.json"), cancellation));
                        await File.WriteAllTextAsync(destination + ".tmp", receipt.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), cancellation);
                        File.Move(destination + ".tmp", destination, true);
                    }
                    if (existing.RootElement.GetProperty("accepted").GetBoolean()) Interlocked.Increment(ref accepted);
                    return;
                }
            }
            if (Volatile.Read(ref accepted) >= target) return;
            var checks = problem.GetProperty("referenceCheckOrders").EnumerateArray().Select(x => x.GetInt32()).ToHashSet();
            var tests = problem.GetProperty("tests").EnumerateArray().Where(test => checks.Contains(test.GetProperty("order").GetInt32())).ToArray();
            Assert.InRange(tests.Length, 1, 24);
            var attempts = new List<object>();
            string? chosen = null;
            foreach (var reference in problem.GetProperty("references").EnumerateArray().Take(3))
            {
                var source = await ReadAsync(root, reference, 65536, cancellation);
                var compilation = await backend.ExecuteAsync(new(ExecutionStage.Compile, source, [],
                    new(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(8), 512L * 1024 * 1024, 64L * 1024 * 1024, 32, 32768, 32768)), cancellation);
                Assert.True(OfficialJudgingPolicy.HasCompleteEvidence(compilation), "Incomplete compilation/cleanup evidence; stop reference validation.");
                if (OfficialJudgingPolicy.IsCompilationError(compilation))
                {
                    attempts.Add(new { reference = reference.GetProperty("path").GetString(), compilation = "CompilationError" });
                    continue;
                }
                Assert.NotEmpty(compilation.Artifact!);
                var measurements = new List<object>();
                var passed = true;
                foreach (var test in tests)
                {
                    var input = await ReadAsync(root, test.GetProperty("input"), 8 * 1024 * 1024, cancellation);
                    var expected = await ReadAsync(root, test.GetProperty("output"), 8 * 1024 * 1024, cancellation);
                    var milliseconds = problem.GetProperty("timeLimitMilliseconds").GetInt32();
                    var execution = await backend.ExecuteAsync(new(ExecutionStage.Run, compilation.Artifact!, input,
                        new(TimeSpan.FromMilliseconds(Math.Max(milliseconds * 2L, milliseconds + 1000L)), TimeSpan.FromMilliseconds(milliseconds),
                            problem.GetProperty("memoryLimitMegabytes").GetInt32() * 1024L * 1024, 8L * 1024 * 1024, 32, 8 * 1024 * 1024, 32768)), cancellation);
                    Assert.True(OfficialJudgingPolicy.HasCompleteEvidence(execution), "Incomplete runtime/cleanup evidence; stop reference validation.");
                    var status = OfficialJudgingPolicy.ClassifyTest(execution, expected);
                    passed &= status == TestCaseResultStatus.Passed;
                    measurements.Add(new { order = test.GetProperty("order").GetInt32(), status = status.ToString(),
                        cpuMilliseconds = execution.CpuUsage!.Value.TotalMilliseconds, wallMilliseconds = execution.WallElapsed.TotalMilliseconds,
                        memoryBytes = execution.MemoryPeakBytes!.Value, cleanupComplete = execution.CleanupComplete });
                }
                var path = reference.GetProperty("path").GetString()!;
                attempts.Add(new { reference = path, compilation = "Succeeded", tests = measurements });
                if (passed) { chosen = path; break; }
            }
            var evidence = new { id, manifestSha256 = fingerprint,
                manifestFileSha256 = Hash(await File.ReadAllBytesAsync(Path.Combine(root, "candidates", id, "manifest.json"), cancellation)),
                accepted = chosen is not null, reference = chosen, compilationMemoryMegabytes = 512,
                checkedAtUtc = DateTimeOffset.UtcNow, toolchain = SupportedSubmission.ToolchainId, checker = "Exact", scope = problem.GetProperty("referenceCheckScope").GetString(),
                checkedOrders = checks.Order().ToArray(), totalAvailableTests = problem.GetProperty("tests").GetArrayLength(), attempts };
            await File.WriteAllTextAsync(destination + ".tmp", JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }), cancellation);
            File.Move(destination + ".tmp", destination, true);
            if (chosen is not null) Interlocked.Increment(ref accepted);
            Console.WriteLine($"BANK_REFERENCE id={id} accepted={chosen is not null} checked={tests.Length} ready={Volatile.Read(ref accepted)}");
        });
        Assert.True(accepted >= target, $"Only {accepted} references passed; target {target}. Retain evidence and extract more candidates.");
    }

    private static async Task<byte[]> ReadAsync(string root, JsonElement file, int maximum, CancellationToken token)
    {
        var relative = file.GetProperty("path").GetString()!;
        Assert.False(Path.IsPathRooted(relative));
        Assert.DoesNotContain("..", relative.Split('/'));
        var path = Path.GetFullPath(Path.Combine(root, relative));
        Assert.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, path);
        for (var parent = new FileInfo(path) as FileSystemInfo; parent is not null && parent.FullName != root;
             parent = parent is FileInfo info ? info.Directory : ((DirectoryInfo)parent).Parent)
            Assert.False(parent.Attributes.HasFlag(FileAttributes.ReparsePoint));
        Assert.InRange(new FileInfo(path).Length, 0, maximum);
        var data = await File.ReadAllBytesAsync(path, token);
        Assert.Equal(file.GetProperty("sha256").GetString(), Hash(data));
        return data;
    }

    private static string Hash(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));
}
