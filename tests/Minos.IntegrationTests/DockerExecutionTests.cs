using Microsoft.Extensions.DependencyInjection;
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
        var error = Assert.Throws<InvalidOperationException>(() =>
            services.AddMinosInfrastructure("Host=localhost;Database=unused;Username=unused", volume));
        Assert.Contains("Minos:Execution:WorkspaceVolume", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sentinel", error.Message, StringComparison.Ordinal);
    }

    public static bool SandboxEnabled => OperatingSystem.IsLinux() &&
        Environment.GetEnvironmentVariable("MINOS_TEST_SANDBOX") == "1";

    [Fact(Skip = "Requires the approved Linux Docker sandbox profile.", SkipUnless = nameof(SandboxEnabled))]
    public async Task Compiler_and_runtime_return_complete_evidence_for_correct_solution()
    {
        var services = new ServiceCollection();
        services.AddMinosInfrastructure("Host=localhost;Database=unused;Username=unused",
            Environment.GetEnvironmentVariable("MINOS_TEST_WORKSPACE_VOLUME"));
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
    }

    private static string Summary(ExecutionEvidence evidence) =>
        $"failure={evidence.InfrastructureFailure}, exit={evidence.ExitCode}, snapshot={evidence.ResourceSnapshotComplete}, cleanup={evidence.CleanupComplete}, cpu={evidence.CpuUsage}, memory={evidence.MemoryPeakBytes}, wall={evidence.WallTimeout}, memoryFailure={evidence.MemoryFailure}";

    [Fact(Skip = "Requires the approved Linux Docker sandbox profile.", SkipUnless = nameof(SandboxEnabled))]
    public async Task Sandbox_cannot_read_other_workspaces_private_artifacts_or_docker_control_socket()
    {
        if (!OperatingSystem.IsLinux()) return;
        var siblingName = "other-" + Guid.NewGuid().ToString("N");
        var sibling = Path.Combine("/tmp/minos-judge-execution", siblingName);
        Directory.CreateDirectory(sibling, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        await File.WriteAllTextAsync(Path.Combine(sibling, "secret.txt"), "private-sentinel", TestContext.Current.CancellationToken);
        try
        {
            var services = new ServiceCollection();
            services.AddMinosInfrastructure("Host=localhost;Database=unused;Username=unused",
                Environment.GetEnvironmentVariable("MINOS_TEST_WORKSPACE_VOLUME"));
            await using var provider = services.BuildServiceProvider();
            var backend = provider.GetRequiredService<IExecutionBackend>();
            var token = TestContext.Current.CancellationToken;
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
        services.AddMinosInfrastructure("Host=localhost;Database=unused;Username=unused",
            Environment.GetEnvironmentVariable("MINOS_TEST_WORKSPACE_VOLUME"));
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
        services.AddMinosInfrastructure("Host=localhost;Database=unused;Username=unused",
            Environment.GetEnvironmentVariable("MINOS_TEST_WORKSPACE_VOLUME"));
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
