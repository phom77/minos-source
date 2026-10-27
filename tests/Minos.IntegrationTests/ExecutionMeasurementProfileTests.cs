using Microsoft.Extensions.Logging.Abstractions;
using Minos.Infrastructure.Evaluation;
using Xunit;

namespace Minos.IntegrationTests;

public sealed class ExecutionMeasurementProfileTests
{
    [Fact]
    public async Task Identity_is_stable_within_a_boot_but_separates_runner_and_budget_and_honors_cancellation()
    {
        var token = TestContext.Current.CancellationToken;
        static ExecutionMeasurementProfile Profile(string runner, int slots) => new(runner, new(SandboxSlots: slots), 1,
            NullLogger<ExecutionMeasurementProfile>.Instance);
        var profile = Profile("docker", 2);
        var value = await profile.ReadAsync(token);
        if (OperatingSystem.IsLinux())
        {
            Assert.Matches("^[0-9a-f]{64}$", value!);
            Assert.Equal(value, await Profile("docker", 2).ReadAsync(token));
            Assert.NotEqual(value, await Profile("isolate", 2).ReadAsync(token));
            Assert.NotEqual(value, await Profile("docker", 3).ReadAsync(token));
        }
        else Assert.Null(value);
        Assert.Equal(value, await profile.ReadAsync(token));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => profile.ReadAsync(cancelled.Token));
    }
}
