using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Minos.Infrastructure;
using Minos.Infrastructure.Hosting;
using Minos.JudgeWorker;

if (!OperatingSystem.IsLinux())
{
    Console.Error.WriteLine("Run the Judge Worker in WSL2 Ubuntu.");
    Environment.ExitCode = 1;
    return;
}
try
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Logging.AddJsonConsole();
    builder.Logging.AddFilter("Microsoft.Extensions.Hosting.Internal.Host", LogLevel.None);
    builder.Services.AddMinosInfrastructure(builder.Configuration.GetConnectionString("Minos"));
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
