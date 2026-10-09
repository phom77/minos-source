using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Minos.Application.Evaluation;
using Minos.Domain.Evaluation;
using Minos.Infrastructure;
using Minos.Infrastructure.Evaluation;
using Xunit;

namespace Minos.IntegrationTests;

public sealed class IsolateExecutionTests
{
    public static bool Enabled => SandboxTestProfile.Native && DockerExecutionTests.SandboxEnabled;
    public static bool MultiBoxEnabled => Enabled && SandboxTestProfile.BoxCount >= 2;

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public void Invalid_box_count_is_rejected_before_execution(int count) =>
        Assert.Throws<InvalidOperationException>(() => IsolateExecutionBackend.ValidateBoxCount(count));

    [Fact]
    public void Runtime_quota_cannot_exceed_native_boxes_and_docker_cannot_select_multiple_boxes()
    {
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddMinosInfrastructure(
            "Host=localhost;Database=unused;Username=unused", executionRunner: "isolate", isolateRoot: "/opt/minos-test",
            executionBudget: new(4, RuntimeSlots: 3), isolateBoxCount: 2));
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddMinosInfrastructure(
            "Host=localhost;Database=unused;Username=unused", isolateBoxCount: 2));
    }

    [Fact(Skip = "Requires the trusted multi-box Linux fixture profile.", SkipUnless = nameof(MultiBoxEnabled))]
    public async Task Concurrent_boxes_have_distinct_identities_scratch_and_independent_cancellation()
    {
        var token = TestContext.Current.CancellationToken;
        await using var provider = Provider();
        var backend = provider.GetRequiredService<IExecutionBackend>();
        var source = """
            #include <iostream>
            #include <fstream>
            #include <unistd.h>
            #include <sched.h>
            int main(){int n;std::cin>>n;cpu_set_t c;CPU_ZERO(&c);
                if(sched_getaffinity(0,sizeof(c),&c)||CPU_COUNT(&c)!=1||!CPU_ISSET(getuid()-61000,&c))return 9;
                std::ofstream("/scratch/marker")<<n;usleep(1500000);int saved;
                std::ifstream("/scratch/marker")>>saved;if(saved!=n)return 8;
                std::cout<<getuid()<<' '<<n<<'\n';}
            """;
        var compiled = await backend.ExecuteAsync(new(ExecutionStage.Compile, Encoding.UTF8.GetBytes(source), [],
            new(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(8), 512 * 1024 * 1024, 64 * 1024 * 1024, 32, 32768, 32768)), token);
        Assert.True(OfficialJudgingPolicy.HasCompleteEvidence(compiled), Describe(compiled));
        ExecutionRequest Request(int n) => Run(compiled.Artifact!, n);
        var root = Environment.GetEnvironmentVariable("MINOS_TEST_ISOLATE_ROOT")!;
        var cg = (await File.ReadAllTextAsync(Path.Combine(root, "cgroup"), token)).Trim();
        var first = backend.ExecuteAsync(Request(11), token);
        var second = backend.ExecuteAsync(Request(22), token);
        var timer = Stopwatch.StartNew();
        while (Directory.EnumerateDirectories(cg, "box-*").Count() < 2)
        {
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(5));
            await Task.Delay(5, token);
        }
        var together = await Task.WhenAll(first, second);
        Assert.All(together, evidence => Assert.True(OfficialJudgingPolicy.HasCompleteEvidence(evidence) && evidence.ExitCode == 0, Describe(evidence)));
        Assert.Equal(2, together.Select(evidence => Encoding.UTF8.GetString(evidence.Stdout).Split(' ')[0]).Distinct().Count());
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
        first = backend.ExecuteAsync(Request(11), cancel.Token);
        second = backend.ExecuteAsync(Request(22), token);
        timer.Restart();
        while (Directory.EnumerateDirectories(cg, "box-*").Count() < 2)
        {
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(5));
            await Task.Delay(5, token);
        }
        cancel.Cancel();
        var cancelled = await first;
        Assert.True(cancelled.Cancelled && cancelled.CleanupComplete, Describe(cancelled));
        var healthy = await second;
        Assert.True(OfficialJudgingPolicy.HasCompleteEvidence(healthy), Describe(healthy));
        var output = Encoding.UTF8.GetString(healthy.Stdout).Trim().Split(' ');
        Assert.InRange(int.Parse(output[0]), 61000, 61000 + SandboxTestProfile.BoxCount - 1);
        Assert.Equal("22", output[1]);
        Assert.Equal(0, healthy.ExitCode);
        Assert.Empty(Directory.EnumerateDirectories(cg, "box-*"));
        // Reuse every slot with a fresh scratch after both healthy and cancelled executions.
        var artifact = await CompileAsync(backend, token);
        for (var i = 0; i < SandboxTestProfile.BoxCount; i++)
        {
            var clean = await backend.ExecuteAsync(Run(artifact, 4), token);
            Assert.True(OfficialJudgingPolicy.HasCompleteEvidence(clean), Describe(clean));
            Assert.Equal(TestCaseResultStatus.Passed, OfficialJudgingPolicy.ClassifyTest(clean, "42\n"u8));
        }
    }

    [Theory]
    [InlineData("unknown", null, null)]
    [InlineData("isolate", null, null)]
    [InlineData("isolate", "/opt/minos-test", "volume-name")]
    [InlineData("isolate", "/opt/minos-test:rw", null)]
    [InlineData("isolate", "/opt/minos-test/../other", null)]
    [InlineData("isolate", "/opt/minos-test/child", null)]
    [InlineData("isolate", "relative", null)]
    [InlineData("docker", "/opt/minos-test", null)]
    public void Invalid_native_configuration_is_rejected_before_any_execution(string runner, string? root, string? volume)
    {
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddMinosInfrastructure(
            "Host=localhost;Database=unused;Username=unused", executionWorkspaceVolume: volume, executionRunner: runner, isolateRoot: root));
        Assert.DoesNotContain("unknown", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("/opt/minos-test", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("time:0.1\ntime:0.2\n")]
    [InlineData("missing separator\n")]
    [InlineData(":empty-key\n")]
    public void Duplicate_or_malformed_native_evidence_cannot_be_trusted(string text) =>
        Assert.Throws<IOException>(() => IsolateExecutionBackend.ReadPairs(text, ':'));

    private const string Source = """
        #include <iostream>
        #include <unistd.h>
        #include <sys/stat.h>
        #include <sys/prctl.h>
        #include <csignal>
        #include <fstream>
        #include <cstdlib>
        #include <cerrno>
        int main(){int mode;std::cin>>mode;
            if(mode==0){for(;;)std::cerr<<"01234567890123456789012345678901234567890123456789";}
            if(mode==1){signal(SIGXFSZ,SIG_IGN);for(;;)std::cout<<"01234567890123456789012345678901234567890123456789";}
            if(mode==2){if(fork()==0){volatile unsigned long long n=0;for(;;)++n;}sleep(20);}
            if(mode==3){mkdir("/scratch/deep",0700);std::ofstream f("/scratch/deep/file");f<<"private";f.close();chmod("/scratch/deep",0000);symlink("/etc/passwd","/scratch/link");std::cout<<"42\n";return 0;}
            if(mode==4){if(access("/scratch/deep",F_OK)==0 || access("/scratch/link",F_OK)==0)return 7;std::cout<<"42\n";return 0;}
            if(mode==5){auto p=(volatile char*)malloc(16*1024*1024);if(!p)return 8;for(int i=0;i<16*1024*1024;i+=4096)p[i]=1;std::cout<<"42\n";return 0;}
            if(mode==6){std::cout<<"time:0\ntime-wall:0\nexitcode:0\ncg-mem:1\n";return 7;}
            if(mode==7){usleep(400000);std::cout<<"42\n";return 0;}
            if(mode==8){if(getuid()<61000 || getuid()>=61016 || geteuid()!=getuid() || prctl(PR_GET_NO_NEW_PRIVS,0,0,0,0)!=1)return 9;std::cout<<"42\n";return 0;}
            if(mode==9){signal(SIGXFSZ,SIG_IGN);std::ofstream f("/scratch/full");char bytes[4096]{};int n=0;while(f.good()){f.write(bytes,sizeof bytes);n++;if(n>4096)return 10;}std::cout<<"42\n";return 0;}
        }
        """;

    private static ServiceProvider Provider()
    {
        var services = new ServiceCollection();
        SandboxTestProfile.AddInfrastructure(services, "Host=localhost;Database=unused;Username=unused");
        return services.BuildServiceProvider();
    }

    private static async Task<byte[]> CompileAsync(IExecutionBackend backend, CancellationToken token)
    {
        var evidence = await backend.ExecuteAsync(new(ExecutionStage.Compile, Encoding.UTF8.GetBytes(Source), [],
            new(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(8), 256 * 1024 * 1024, 64 * 1024 * 1024, 32, 32768, 32768)), token);
        Assert.True(OfficialJudgingPolicy.HasCompleteEvidence(evidence), Describe(evidence));
        Assert.False(OfficialJudgingPolicy.IsCompilationError(evidence), Describe(evidence));
        return evidence.Artifact!;
    }

    private static ExecutionRequest Run(byte[] artifact, int mode) => new(ExecutionStage.Run, artifact, Encoding.UTF8.GetBytes($"{mode}\n"),
        new(TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(300), 64 * 1024 * 1024, 8 * 1024 * 1024, 32, 4096, 4096));

    [Fact(Skip = "Requires the explicit trusted direct-Linux Isolate fixture profile.", SkipUnless = nameof(Enabled))]
    public async Task Native_output_limits_descendant_cpu_and_metadata_trust_preserve_verdict_precedence()
    {
        await using var provider = Provider();
        var backend = provider.GetRequiredService<IExecutionBackend>();
        var token = TestContext.Current.CancellationToken;
        var artifact = await CompileAsync(backend, token);
        foreach (var (mode, expected) in new[] { (0, TestCaseResultStatus.OutputLimitExceeded), (1, TestCaseResultStatus.OutputLimitExceeded),
            (2, TestCaseResultStatus.TimeLimitExceeded), (6, TestCaseResultStatus.RuntimeError), (8, TestCaseResultStatus.Passed) })
        {
            var evidence = await backend.ExecuteAsync(Run(artifact, mode), token);
            Assert.True(OfficialJudgingPolicy.HasCompleteEvidence(evidence), Describe(evidence));
            Assert.Equal(expected, OfficialJudgingPolicy.ClassifyTest(evidence, "42\n"u8));
            Assert.True(evidence.Stdout.Length <= 4096 && evidence.Stderr.Length <= 4096);
            if (mode == 2) Assert.True(evidence.CpuUsage >= TimeSpan.FromMilliseconds(300));
        }
    }

    [Fact(Skip = "Requires the explicit trusted direct-Linux Isolate fixture profile.", SkipUnless = nameof(Enabled))]
    public async Task Fresh_native_boxes_clear_hostile_scratch_and_reset_memory_peaks()
    {
        await using var provider = Provider();
        var backend = provider.GetRequiredService<IExecutionBackend>();
        var token = TestContext.Current.CancellationToken;
        var artifact = await CompileAsync(backend, token);
        long? previousMemory = null;
        foreach (var mode in new[] { 3, 4, 5, 4, 9, 4 })
        {
            var evidence = await backend.ExecuteAsync(Run(artifact, mode), token);
            Assert.True(OfficialJudgingPolicy.HasCompleteEvidence(evidence), Describe(evidence));
            Assert.Equal(TestCaseResultStatus.Passed, OfficialJudgingPolicy.ClassifyTest(evidence, "42\n"u8));
            if (mode == 5) previousMemory = evidence.MemoryPeakBytes;
            else if (mode == 4 && previousMemory is not null) Assert.True(evidence.MemoryPeakBytes < previousMemory);
        }
    }

    [Fact(Skip = "Requires the explicit trusted direct-Linux Isolate fixture profile.", SkipUnless = nameof(Enabled))]
    public async Task Missing_native_metadata_is_an_infrastructure_fault_and_cleanup_allows_the_next_test()
    {
        await using var provider = Provider();
        var backend = provider.GetRequiredService<IExecutionBackend>();
        var token = TestContext.Current.CancellationToken;
        var artifact = await CompileAsync(backend, token);
        var running = backend.ExecuteAsync(Run(artifact, 7), token);
        var work = Path.Combine(Environment.GetEnvironmentVariable("MINOS_TEST_ISOLATE_ROOT")!, "work");
        var timer = Stopwatch.StartNew();
        string? metadata;
        do
        {
            metadata = Directory.EnumerateFiles(work, "stage-*.meta").SingleOrDefault();
            if (metadata is not null) break;
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(5));
            await Task.Delay(5, token);
        } while (true);
        File.Delete(metadata);
        var incomplete = await running;
        Assert.False(OfficialJudgingPolicy.HasCompleteEvidence(incomplete));
        Assert.NotNull(incomplete.InfrastructureFailure);
        Assert.True(incomplete.CleanupComplete, Describe(incomplete));
        var recovered = await backend.ExecuteAsync(Run(artifact, 4), token);
        Assert.True(OfficialJudgingPolicy.HasCompleteEvidence(recovered), Describe(recovered));
        Assert.Equal(TestCaseResultStatus.Passed, OfficialJudgingPolicy.ClassifyTest(recovered, "42\n"u8));
        Assert.Empty(Directory.EnumerateDirectories(work, "stage-*"));
    }

    [Fact(Skip = "Requires the explicit trusted direct-Linux Isolate fixture profile.", SkipUnless = nameof(Enabled))]
    public async Task Live_native_configuration_refuses_a_piped_host_core_collector()
    {
        if (!(await File.ReadAllTextAsync("/proc/sys/kernel/core_pattern", TestContext.Current.CancellationToken)).TrimStart().StartsWith('|')) return;
        var services = new ServiceCollection();
        services.AddMinosInfrastructure("Host=localhost;Database=unused;Username=unused", executionRunner: "isolate",
            isolateRoot: Environment.GetEnvironmentVariable("MINOS_TEST_ISOLATE_ROOT"), isolateBoxCount: SandboxTestProfile.BoxCount);
        await using var provider = services.BuildServiceProvider();
        var error = await Assert.ThrowsAsync<IOException>(() => provider.GetRequiredService<Minos.Infrastructure.Hosting.JudgeSandboxLifecycle>()
            .InitializeAsync(TestContext.Current.CancellationToken));
        Assert.Contains("core-dump", error.Message, StringComparison.Ordinal);
    }

    private static string Describe(ExecutionEvidence value) => $"fault={value.InfrastructureFailure}, exit={value.ExitCode}, snapshot={value.ResourceSnapshotComplete}, cleanup={value.CleanupComplete}, fixture_stderr={Encoding.UTF8.GetString(value.Stderr.AsSpan(0, Math.Min(value.Stderr.Length, 1024)))}";
}
