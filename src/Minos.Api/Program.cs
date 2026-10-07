using Microsoft.EntityFrameworkCore;
using Minos.Api;
using Minos.Api.Problems;
using Minos.Infrastructure;
using Minos.Infrastructure.Persistence;
using Minos.Infrastructure.Hosting;
using Minos.Api.Accounts;
using Minos.Application.Accounts;
using Minos.Api.Submissions;

var builder = WebApplication.CreateBuilder(args);
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"])) builder.WebHost.UseUrls("http://127.0.0.1:5080");
builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize = Minos.Application.Problems.ProblemArtifactLimits.MaximumPackageBytes);
builder.Logging.AddJsonConsole();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    context.ProblemDetails.Detail = null;
    context.ProblemDetails.Extensions.Remove("exception");
});
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
// The exception handler logs only safe metadata. Suppress middleware's raw exception logging.
builder.Services.Configure<ExceptionHandlerOptions>(options => options.SuppressDiagnosticsCallback = _ => true);
builder.Logging.AddFilter("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", LogLevel.None);
builder.Services.AddMinosInfrastructure(builder.Configuration.GetConnectionString("Minos"),
    maximumActiveSubmissions: builder.Configuration.GetValue<int?>("Minos:Submissions:MaximumActive") ?? 32);
var migrating = args.Contains("--migrate", StringComparer.Ordinal);
var provisionIndex = Array.FindIndex(args, argument => string.Equals(argument, "--provision-admin", StringComparison.Ordinal));
Guid? administratorId = null;
if (provisionIndex >= 0)
{
    if (provisionIndex + 1 >= args.Length || !Guid.TryParse(args[provisionIndex + 1], out var parsed) || parsed == Guid.Empty)
        throw new InvalidOperationException("--provision-admin requires a non-empty account GUID.");
    administratorId = parsed;
}
if (migrating && administratorId is not null)
    throw new InvalidOperationException("Run migration and administrator provisioning as separate operations.");
var maintenance = migrating || administratorId is not null;
if (!maintenance)
{
    builder.Services.AddSingleton(new PrivateArtifactDirectory(builder.Configuration["Minos:Artifacts:Root"], builder.Environment.ContentRootPath));
    builder.AddAccountAuthentication();
    builder.Services.AddHostedService<FoundationStartupCheck>();
    builder.Services.AddHostedService<ProblemArtifactCleanupWorker>();
}
else if (administratorId is not null) builder.Services.AddMinosAccountPersistence();
builder.Services.AddAuthorization();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("postgresql", tags: ["ready"]);
var app = builder.Build();

if (maintenance)
{
    await using var scope = app.Services.CreateAsyncScope();
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
    await scope.ServiceProvider.GetRequiredService<DatabaseOperationalCheck>().ValidateAsync(DatabaseActor.Migrator, cancellation.Token);
    if (migrating) await scope.ServiceProvider.GetRequiredService<MinosDbContext>().Database.MigrateAsync(cancellation.Token);
    else await scope.ServiceProvider.GetRequiredService<TrustedAccountProvisioningUseCases>()
        .ProvisionAdministratorAsync(administratorId!.Value, cancellation.Token);
    return;
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAccountRequestProtection();
app.MapAccountEndpoints();
app.MapGoogleEndpoints();
app.MapProblemEndpoints();
app.MapSubmissionEndpoints();
app.MapJudgingOperationsEndpoints();
app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") });

await app.RunAsync();

public partial class Program;
