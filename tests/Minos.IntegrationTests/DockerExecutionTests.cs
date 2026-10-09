using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Minos.Application.Evaluation;
using Minos.Infrastructure;
using Xunit;

namespace Minos.IntegrationTests;

public sealed class DockerExecutionTests
{
    [Theory]
    [InlineData("x")]
    [InlineData("../sentinel-volume")]
    [InlineData("sentinel,readonly=false")]
    [InlineData("sentinel volume")]
    [InlineData(" ")]
    public void Invalid_volume_configuration_cannot_change_sandbox_mount_options(string volume)
    {
        var services = new ServiceCollection();
        var error = Assert.Throws<InvalidOperationException>(() => services.AddMinosInfrastructure(
            "Host=localhost;Database=unused;Username=unused", executionWorkspaceVolume: volume));
        Assert.Contains("Minos:Execution:WorkspaceVolume", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sentinel", error.Message, StringComparison.Ordinal);
    }

    [Fact(Skip = "Requires the approved Linux Docker sandbox profile.", SkipUnless = nameof(SandboxEnabled))]
    public async Task Sandbox_cannot_reach_network_host_files_or_management_interfaces_and_host_is_exclusive()
    {
        var services = new ServiceCollection();
        SandboxTestProfile.AddInfrastructure(services, "Host=localhost;Database=unused;Username=unused");
        await using var provider = services.BuildServiceProvider();
        var backend = provider.GetRequiredService<IExecutionBackend>();
        var token = TestContext.Current.CancellationToken;
        var source = """
            #include <unistd.h>
            #include <sys/socket.h>
            #include <arpa/inet.h>
            #include <fcntl.h>
            #include <cerrno>
            #include <iostream>
            int main(){
                if(getuid()==0) return 1;
                const char* forbidden[]={"/var/run/docker.sock","/input/main.cpp","/input/output.bin","/mnt/d/Projects/PetProject/Minos/README.md","/root/.ssh/id_rsa"};
                for(auto path:forbidden) if(access(path,F_OK)==0) return 2;
                int s=socket(AF_INET,SOCK_STREAM,0); if(s<0) return 3;
                sockaddr_in a{}; a.sin_family=AF_INET;a.sin_port=htons(80);inet_pton(AF_INET,"1.1.1.1",&a.sin_addr);
                fcntl(s,F_SETFL,O_NONBLOCK); if(connect(s,(sockaddr*)&a,sizeof(a))==0 || errno!=ENETUNREACH) return 4;
                close(s);std::cout<<"contained\n";
            }
            """;
        var compilation = await backend.ExecuteAsync(new(ExecutionStage.Compile, System.Text.Encoding.UTF8.GetBytes(source), [],
            new(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(8), 256*1024*1024, 64*1024*1024, 32, 32768, 32768)), token);
        Assert.True(OfficialJudgingPolicy.HasCompleteEvidence(compilation), Summary(compilation));
        Assert.False(OfficialJudgingPolicy.IsCompilationError(compilation), Summary(compilation));
        var execution = await backend.ExecuteAsync(new(ExecutionStage.Run, compilation.Artifact!, [],
            new(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1), 64*1024*1024, 8*1024*1024, 32, 32768, 32768)), token);
        Assert.True(OfficialJudgingPolicy.HasCompleteEvidence(execution), Summary(execution));
        Assert.Equal(0, execution.ExitCode);
        Assert.Equal("contained\n"u8.ToArray(), execution.Stdout);
        await using var second = services.BuildServiceProvider();
        await Assert.ThrowsAsync<IOException>(() => second.GetRequiredService<Minos.Infrastructure.Hosting.JudgeSandboxLifecycle>().InitializeAsync(token));
    }

    public static bool SandboxEnabled => OperatingSystem.IsLinux() &&
        Environment.GetEnvironmentVariable("MINOS_TEST_SANDBOX") == "1";

    [Fact(Skip = "Requires the approved Linux Docker sandbox profile.", SkipUnless = nameof(SandboxEnabled))]
    public async Task Compiler_and_runtime_return_complete_evidence_for_correct_solution()
    {
        var measurements = new ConcurrentBag<(double Seconds, string Stage, string Phase)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "Minos.Judging") meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
        {
            var labels = tags.ToArray();
            Assert.Equal(new[] { "stage", "phase" }, labels.Select(tag => tag.Key));
            measurements.Add((value, (string)labels[0].Value!, (string)labels[1].Value!));
        });
        listener.Start();
        var services = new ServiceCollection();
        SandboxTestProfile.AddInfrastructure(services, "Host=localhost;Database=unused;Username=unused");
        await using var provider = services.BuildServiceProvider();
        var backend = provider.GetRequiredService<IExecutionBackend>();
        var token = TestContext.Current.CancellationToken;
        var compilation = await backend.ExecuteAsync(new(
            ExecutionStage.Compile,
            "#include <iostream>\nint main(){int a,b;std::cin>>a>>b;std::cout<<a+b<<'\\n';}"u8.ToArray(),
            [],
            new(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(8), 256*1024*1024, 64*1024*1024, 32, 32768, 32768)), token);
        Assert.True(OfficialJudgingPolicy.HasCompleteEvidence(compilation), Summary(compilation));
        Assert.False(OfficialJudgingPolicy.IsCompilationError(compilation), Summary(compilation));
        Assert.NotNull(compilation.Artifact);
        var execution = await backend.ExecuteAsync(new(
            ExecutionStage.Run,
            compilation.Artifact,
            "19 23\n"u8.ToArray(),
            new(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1), 64*1024*1024, 8*1024*1024, 32, 32768, 32768)), token);
        Assert.True(OfficialJudgingPolicy.HasCompleteEvidence(execution), Summary(execution));
        Assert.Equal("42\n"u8.ToArray(), execution.Stdout);
        Assert.Equal(0, execution.ExitCode);
        foreach (var stage in new[] { "compile", "run" })
            foreach (var phase in new[] { "total", "initialize", "workspace", "provision", "guard", "baseline-evidence", "execution", "evidence", "cleanup" })
                if (stage != "run" || !SandboxTestProfile.Native || phase is not ("guard" or "baseline-evidence"))
                    Assert.Contains(measurements, sample => sample.Stage == stage && sample.Phase == phase && sample.Seconds >= 0);
    }

    [Fact(Skip = "Opt-in 100-test performance measurement on the Linux sandbox profile.", SkipUnless = nameof(BenchmarkEnabled))]
    public async Task Benchmark_compile_and_100_serial_tests()
    {
        var measurements = new ConcurrentBag<(double Seconds, string Stage, string Phase)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "Minos.Judging") meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
            measurements.Add((value, (string)tags[0].Value!, (string)tags[1].Value!)));
        listener.Start();
        var services = new ServiceCollection();
        SandboxTestProfile.AddInfrastructure(services, "Host=localhost;Database=unused;Username=unused");
        await using var provider = services.BuildServiceProvider();
        var backend = provider.GetRequiredService<IExecutionBackend>();
        var token = TestContext.Current.CancellationToken;
        var timer = Stopwatch.StartNew();
        await provider.GetRequiredService<Minos.Infrastructure.Hosting.JudgeSandboxLifecycle>().InitializeAsync(token);
        var initializeSeconds = timer.Elapsed.TotalSeconds;
        timer.Restart();
        var compilation = await backend.ExecuteAsync(new(ExecutionStage.Compile,
            "#include <iostream>\nint main(){int a,b;std::cin>>a>>b;std::cout<<a+b<<'\\n';}"u8.ToArray(), [],
            new(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(8), 256*1024*1024, 64*1024*1024, 32, 32768, 32768)), token);
        var compileSeconds = timer.Elapsed.TotalSeconds;
        Assert.True(OfficialJudgingPolicy.HasCompleteEvidence(compilation), Summary(compilation));
        Assert.False(OfficialJudgingPolicy.IsCompilationError(compilation), Summary(compilation));
        Assert.NotNull(compilation.Artifact);
        var limits = new ExecutionLimits(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), 64*1024*1024, 8*1024*1024, 32, 8*1024*1024, 32768);
        var durations = new List<double>();
        var wallSeconds = 0d;
        var runs = Stopwatch.StartNew();
        for (var index = 0; index < 100; index++)
        {
            timer.Restart();
            var result = await backend.ExecuteAsync(new(ExecutionStage.Run, compilation.Artifact, "19 23\n"u8.ToArray(), limits), token);
            durations.Add(timer.Elapsed.TotalSeconds);
            wallSeconds += result.WallElapsed.TotalSeconds;
            Assert.True(OfficialJudgingPolicy.HasCompleteEvidence(result), Summary(result));
            Assert.Equal(Minos.Domain.Evaluation.TestCaseResultStatus.Passed, OfficialJudgingPolicy.ClassifyTest(result, "42\n"u8));
        }
        var runtimeSeconds = runs.Elapsed.TotalSeconds;
        var phases = measurements.GroupBy(value => (value.Stage, value.Phase)).Select(group => new
        {
            stage = group.Key.Stage, phase = group.Key.Phase, count = group.Count(), seconds = group.Sum(value => value.Seconds)
        }).OrderBy(value => value.stage).ThenBy(value => value.phase).ToArray();
        var ordered = durations.Order().ToArray();
        TestContext.Current.TestOutputHelper!.WriteLine("JUDGING_BENCHMARK_JSON=" + JsonSerializer.Serialize(new
        {
            measuredAtUtc = DateTimeOffset.UtcNow, fixture = "trusted C++20 sum, 100 serial executions, exact checker",
            scope = "IExecutionBackend; excludes API/database/queue; image already present",
            initializeSeconds, compileSeconds, runtimeSeconds, compileAnd100RunsSeconds = compileSeconds + runtimeSeconds,
            runMedianSeconds = (ordered[49] + ordered[50]) / 2, runP95Seconds = ordered[94],
            capturedExecutionWallSeconds = wallSeconds, outsideExecutionWatchSeconds = durations.Sum() - wallSeconds, phases
        }));
    }

    public static bool BenchmarkEnabled => SandboxEnabled && Environment.GetEnvironmentVariable("MINOS_TEST_BENCHMARK") == "1";

    [Theory(Skip = "Requires the approved Linux Docker sandbox profile.", SkipUnless = nameof(SandboxEnabled))]
    [InlineData(0, Minos.Domain.Evaluation.TestCaseResultStatus.Passed)]
    [InlineData(1, Minos.Domain.Evaluation.TestCaseResultStatus.WrongAnswer)]
    [InlineData(2, Minos.Domain.Evaluation.TestCaseResultStatus.RuntimeError)]
    [InlineData(3, Minos.Domain.Evaluation.TestCaseResultStatus.TimeLimitExceeded)]
    [InlineData(4, Minos.Domain.Evaluation.TestCaseResultStatus.OutputLimitExceeded)]
    [InlineData(5, Minos.Domain.Evaluation.TestCaseResultStatus.MemoryLimitExceeded)]
    public async Task Cpu_output_memory_and_failure_fixtures_keep_complete_evidence_and_cleanup(int mode, Minos.Domain.Evaluation.TestCaseResultStatus expected)
    {
        var services = new ServiceCollection();
        SandboxTestProfile.AddInfrastructure(services, "Host=localhost;Database=unused;Username=unused");
        await using var provider = services.BuildServiceProvider();
        var backend = provider.GetRequiredService<IExecutionBackend>();
        var token = TestContext.Current.CancellationToken;
        var compilation = await backend.ExecuteAsync(new(ExecutionStage.Compile, """
            #include <iostream>
            #include <cstdlib>
            #include <cstring>
            int main(){int mode;std::cin>>mode;
                if(mode==0){volatile unsigned long long value=0;for(int i=0;i<10000000;i++)value+=i;std::cout<<"42\n";return 0;}
                if(mode==1){std::cout<<"wrong\n";return 0;}
                if(mode==2)return 7;
                if(mode==3){volatile unsigned long long value=0;for(;;)++value;}
                if(mode==4){for(;;)std::cout<<"01234567890123456789012345678901234567890123456789";}
                for(int i=0;i<512;i++){auto p=(volatile char*)malloc(1024*1024);if(!p)return 9;for(int j=0;j<1024*1024;j+=4096)p[j]=1;}
            }
            """u8.ToArray(), [],
            new(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(8), 256*1024*1024, 64*1024*1024, 32, 32768, 32768)), token);
        Assert.True(OfficialJudgingPolicy.HasCompleteEvidence(compilation), Summary(compilation));
        Assert.False(OfficialJudgingPolicy.IsCompilationError(compilation), Summary(compilation));
        var wall = Stopwatch.StartNew();
        var result = await backend.ExecuteAsync(new(ExecutionStage.Run, compilation.Artifact!, System.Text.Encoding.UTF8.GetBytes($"{mode}\n"),
            new(TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(300), 64*1024*1024, 8*1024*1024, 32, 4096, 32768)), token);
        Assert.True(OfficialJudgingPolicy.HasCompleteEvidence(result), Summary(result));
        Assert.Equal(expected, OfficialJudgingPolicy.ClassifyTest(result, "42\n"u8));
        Assert.True(result.Stdout.Length <= 4096);
        TestContext.Current.TestOutputHelper!.WriteLine($"JUDGING_FIXTURE mode={mode} status={expected} total_seconds={wall.Elapsed.TotalSeconds:F3} cpu_ms={result.CpuUsage!.Value.TotalMilliseconds:F3} wall_ms={result.WallElapsed.TotalMilliseconds:F3}");
    }

    private static string Summary(ExecutionEvidence evidence) =>
        $"failure={evidence.InfrastructureFailure}, exit={evidence.ExitCode}, snapshot={evidence.ResourceSnapshotComplete}, cleanup={evidence.CleanupComplete}, cpu={evidence.CpuUsage}, memory={evidence.MemoryPeakBytes}, wall={evidence.WallTimeout}, memoryFailure={evidence.MemoryFailure}";

    [Fact(Skip = "Requires the approved Linux Docker sandbox profile.", SkipUnless = nameof(SandboxEnabled))]
    public async Task Sandbox_cannot_read_other_workspaces_private_artifacts_or_docker_control_socket()
    {
        if (!OperatingSystem.IsLinux()) return;
        var services = new ServiceCollection();
        SandboxTestProfile.AddInfrastructure(services, "Host=localhost;Database=unused;Username=unused");
        await using var provider = services.BuildServiceProvider();
        var backend = provider.GetRequiredService<IExecutionBackend>();
        var token = TestContext.Current.CancellationToken;
        // Complete recovery first. The sentinel represents a live sibling, not crash leftovers.
        await provider.GetRequiredService<Minos.Infrastructure.Hosting.JudgeSandboxLifecycle>().InitializeAsync(token);
        var siblingName = "other-" + Guid.NewGuid().ToString("N");
        var sibling = Path.Combine("/tmp/minos-judge-execution", siblingName);
        Directory.CreateDirectory(sibling, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(sibling, "secret.txt"), "private-sentinel", token);
            var compilation = await backend.ExecuteAsync(new(ExecutionStage.Compile,
                "#include <filesystem>\n#include <iostream>\n#include <string>\nint main(){std::string name;std::cin>>name;for(auto path:{std::string(\"/input/\")+name+\"/secret.txt\",std::string(\"/input/../\")+name+\"/secret.txt\",std::string(\"/private/data-protection\"),std::string(\"/var/run/docker.sock\")}){if(std::filesystem::exists(path))return 7;}std::cout<<\"isolated\\n\";}"u8.ToArray(), [],
                new(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(8), 256*1024*1024, 64*1024*1024, 32, 32768, 32768)), token);
            Assert.True(OfficialJudgingPolicy.HasCompleteEvidence(compilation), Summary(compilation));
            Assert.NotNull(compilation.Artifact);
            var execution = await backend.ExecuteAsync(new(ExecutionStage.Run, compilation.Artifact,
                System.Text.Encoding.UTF8.GetBytes(siblingName + "\n"),
                new(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1), 64*1024*1024, 8*1024*1024, 32, 32768, 32768)), token);
            Assert.True(OfficialJudgingPolicy.HasCompleteEvidence(execution), Summary(execution));
            Assert.Equal(0, execution.ExitCode);
            Assert.Equal("isolated\n"u8.ToArray(), execution.Stdout);
            Assert.Equal("private-sentinel", await File.ReadAllTextAsync(Path.Combine(sibling, "secret.txt"), token));
        }
        finally { Directory.Delete(sibling, recursive: true); }
    }

    [Fact(Skip = "Requires the approved Linux Docker sandbox profile.", SkipUnless = nameof(SandboxEnabled))]
    public async Task Silent_program_can_run_past_transport_deadlines_without_a_cpu_timeout()
    {
        var services = new ServiceCollection();
        SandboxTestProfile.AddInfrastructure(services, "Host=localhost;Database=unused;Username=unused");
        await using var provider = services.BuildServiceProvider();
        var backend = provider.GetRequiredService<IExecutionBackend>();
        var token = TestContext.Current.CancellationToken;
        var compilation = await backend.ExecuteAsync(new(ExecutionStage.Compile,
            "#include <unistd.h>\nint main(){sleep(5);}"u8.ToArray(), [],
            new(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(8), 256*1024*1024, 64*1024*1024, 32, 32768, 32768)), token);
        Assert.True(OfficialJudgingPolicy.HasCompleteEvidence(compilation), Summary(compilation));
        Assert.False(OfficialJudgingPolicy.IsCompilationError(compilation), Summary(compilation));
        var execution = await backend.ExecuteAsync(new(ExecutionStage.Run, compilation.Artifact!, [],
            new(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(4), 64*1024*1024, 8*1024*1024, 32, 32768, 32768)), token);
        Assert.True(OfficialJudgingPolicy.HasCompleteEvidence(execution), Summary(execution));
        Assert.Equal(0, execution.ExitCode);
        Assert.False(execution.CpuLimit, Summary(execution));
        Assert.False(execution.WallTimeout, Summary(execution));
        Assert.True(execution.WallElapsed >= TimeSpan.FromSeconds(5));
    }

    [Fact(Skip = "Requires the approved Linux Docker sandbox profile.", SkipUnless = nameof(SandboxEnabled))]
    public async Task Descendants_do_not_extend_wall_time_and_cancellation_cleans_the_sandbox()
    {
        var services = new ServiceCollection();
        SandboxTestProfile.AddInfrastructure(services, "Host=localhost;Database=unused;Username=unused");
        await using var provider = services.BuildServiceProvider();
        var backend = provider.GetRequiredService<IExecutionBackend>();
        var token = TestContext.Current.CancellationToken;
        var compilation = await backend.ExecuteAsync(new(ExecutionStage.Compile,
            "#include <unistd.h>\n#include <iostream>\nint main(){int mode;std::cin>>mode;if(mode==1){if(fork()==0){sleep(20);return 0;}std::cout<<42<<std::endl;return 0;}if(mode==2){int rejected=0;for(int i=0;i<100;i++){int p=fork();if(p==0){sleep(20);return 0;}if(p<0)rejected++;}std::cout<<(rejected>0)<<std::endl;return 0;}for(;;){} }"u8.ToArray(), [],
            new(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(8), 256*1024*1024, 64*1024*1024, 32, 32768, 32768)), token);
        Assert.True(OfficialJudgingPolicy.HasCompleteEvidence(compilation), Summary(compilation));
        Assert.False(OfficialJudgingPolicy.IsCompilationError(compilation), Summary(compilation));
        var limits = new ExecutionLimits(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), 64*1024*1024, 8*1024*1024, 32, 32768, 32768);
        var child = await backend.ExecuteAsync(new(ExecutionStage.Run, compilation.Artifact!, "1\n"u8.ToArray(), limits), token);
        Assert.True(OfficialJudgingPolicy.HasCompleteEvidence(child), Summary(child));
        Assert.False(child.WallTimeout, Summary(child));
        Assert.Equal(0, child.ExitCode);
        Assert.Equal("42\n"u8.ToArray(), child.Stdout);
        var processes = await backend.ExecuteAsync(new(ExecutionStage.Run, compilation.Artifact!, "2\n"u8.ToArray(), limits), token);
        Assert.True(OfficialJudgingPolicy.HasCompleteEvidence(processes), Summary(processes));
        Assert.Equal(0, processes.ExitCode);
        Assert.Equal("1\n"u8.ToArray(), processes.Stdout);

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        cancellation.CancelAfter(TimeSpan.FromSeconds(2));
        var cancelled = await backend.ExecuteAsync(new(ExecutionStage.Run, compilation.Artifact!, "3\n"u8.ToArray(),
            limits with { WallTime = TimeSpan.FromSeconds(10), CpuTime = TimeSpan.FromSeconds(8) }), cancellation.Token);
        Assert.True(cancelled.Cancelled, Summary(cancelled));
        Assert.True(cancelled.CleanupComplete, Summary(cancelled));
        Assert.False(OfficialJudgingPolicy.HasCompleteEvidence(cancelled));
    }
}
