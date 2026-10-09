using Microsoft.Extensions.DependencyInjection;
using Minos.Infrastructure;
using Minos.Infrastructure.Evaluation;

namespace Minos.IntegrationTests;

internal static class SandboxTestProfile
{
    internal static bool Native => OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("MINOS_TEST_ISOLATE_ROOT") is not null;
    internal static int BoxCount => int.Parse(Environment.GetEnvironmentVariable("MINOS_TEST_ISOLATE_BOXES") ?? "1", System.Globalization.CultureInfo.InvariantCulture);

    internal static void AddInfrastructure(IServiceCollection services, string connection, ExecutionBudgetOptions? budget = null)
    {
        var root = Environment.GetEnvironmentVariable("MINOS_TEST_ISOLATE_ROOT");
        services.AddMinosInfrastructure(connection,
            executionWorkspaceVolume: Native ? null : Environment.GetEnvironmentVariable("MINOS_TEST_WORKSPACE_VOLUME"),
            executionRunner: Native ? "isolate" : "docker", isolateRoot: Native ? root : null, executionBudget: budget,
            isolateBoxCount: Native ? BoxCount : 1);
        if (Native)
        {
            if (Environment.GetEnvironmentVariable("MINOS_TEST_TRUSTED_ISOLATE_FIXTURES") != "1")
                throw new InvalidOperationException("Native checks may execute only the explicit trusted fixture profile.");
            // Test-only composition for known fixtures on WSL's piped core collector; Worker configuration cannot bypass this gate.
            services.AddSingleton(provider => new IsolateExecutionBackend(provider.GetRequiredService<DockerExecutionBackend>(), root!, requireSafeCoreDumpPolicy: false, boxCount: BoxCount));
        }
    }
}
