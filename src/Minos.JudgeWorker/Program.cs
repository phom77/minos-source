using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Minos.Infrastructure;
using Minos.Infrastructure.Hosting;
using Minos.Infrastructure.Evaluation;

namespace Minos.JudgeWorker;

internal static class WorkerEntryPoint
{
    public static async Task Main(string[] args)
    {
        if (!OperatingSystem.IsLinux())
        {
            Console.Error.WriteLine("Run the Judge Worker on Linux, directly or in a Linux container.");
            Environment.ExitCode = 1;
            return;
        }
        try
        {
            var builder = Host.CreateApplicationBuilder(args);
            builder.Logging.AddJsonConsole();
            builder.Logging.AddFilter("Microsoft.Extensions.Hosting.Internal.Host", LogLevel.None);
            var scheduling = new JudgeWorkerOptions(builder.Configuration.GetValue("Minos:Judging:MaximumConcurrentJobs", 4));
            scheduling.Validate();
            var budget = new ExecutionBudgetOptions(
                builder.Configuration.GetValue("Minos:Execution:SandboxSlots", Math.Min(2, Math.Max(1, Environment.ProcessorCount - 1))),
                builder.Configuration.GetValue("Minos:Execution:MemoryBudgetMegabytes", 2048),
                builder.Configuration.GetValue("Minos:Execution:ControllerReserveMegabytes", 512),
                builder.Configuration.GetValue("Minos:Execution:CompileSlots", 1),
                builder.Configuration.GetValue("Minos:Execution:RuntimeSlots", 1));
            if (budget.ControllerReserveMegabytes < 256 + 64 * scheduling.MaximumConcurrentJobs)
                throw new InvalidOperationException("Controller memory reserve must cover management and the bounded active job buffers.");
            builder.Services.AddSingleton(scheduling);
            builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(40));
            builder.Services.AddMinosInfrastructure(builder.Configuration.GetConnectionString("Minos"),
                executionWorkspaceVolume: builder.Configuration["Minos:Execution:WorkspaceVolume"],
                executionRunner: builder.Configuration["Minos:Execution:Runner"] ?? "docker",
                isolateRoot: builder.Configuration["Minos:Execution:IsolateRoot"], executionBudget: budget,
                isolateBoxCount: builder.Configuration.GetValue("Minos:Execution:IsolateBoxCount", 1));
            builder.Services.AddSingleton(new PrivateArtifactDirectory(builder.Configuration["Minos:Artifacts:Root"], builder.Environment.ContentRootPath));
            builder.Services.AddHostedService<WorkerFoundationStartupCheck>();
            if (!args.Contains("--check", StringComparer.Ordinal)) builder.Services.AddHostedService<SubmissionJobWorker>();
            using var host = builder.Build();
            if (args.Contains("--check", StringComparer.Ordinal))
            {
                await host.StartAsync(); // Hosted startup receives cancellation from the host.
                await host.StopAsync();
                Console.WriteLine("worker_foundation=passed");
                return;
            }
            await host.RunAsync();
        }
        catch (OperationCanceledException) { Environment.ExitCode = 130; }
        catch (Exception error)
        {
            // Infrastructure check messages are safe; connection errors must not echo raw details.
            Console.Error.WriteLine(error is InvalidOperationException { InnerException: null }
                ? error.Message : "Judge Worker cannot connect to PostgreSQL or initialize private storage.");
            Environment.ExitCode = 1;
        }
    }
}
