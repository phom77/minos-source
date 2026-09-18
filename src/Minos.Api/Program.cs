using Microsoft.EntityFrameworkCore;
using Minos.Api;
using Minos.Api.Problems;
using Minos.Infrastructure;
using Minos.Infrastructure.Persistence;
using Minos.Infrastructure.Hosting;
using Minos.Api.Accounts;

var builder = WebApplication.CreateBuilder(args);
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"])) builder.WebHost.UseUrls("http://127.0.0.1:5080");
builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize = 512 * 1024);
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
builder.Services.AddMinosInfrastructure(builder.Configuration.GetConnectionString("Minos"));
var migrating = args.Contains("--migrate", StringComparer.Ordinal);
if (!migrating)
{
    builder.Services.AddSingleton(new PrivateArtifactDirectory(builder.Configuration["Minos:Artifacts:Root"], builder.Environment.ContentRootPath));
    builder.AddAccountAuthentication();
    builder.Services.AddHostedService<FoundationStartupCheck>();
}
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("postgresql", tags: ["ready"]);
var app = builder.Build();

if (migrating)
{
    await using var scope = app.Services.CreateAsyncScope();
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
    await scope.ServiceProvider.GetRequiredService<DatabaseOperationalCheck>().ValidateAsync(DatabaseActor.Migrator, cancellation.Token);
    await scope.ServiceProvider.GetRequiredService<MinosDbContext>().Database.MigrateAsync(cancellation.Token);
    return;
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRouting();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAccountRequestProtection();
app.MapAccountEndpoints();
app.MapGoogleEndpoints();
app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") });

// A local foundation experiment, not the future public authoring API.
if (app.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Minos:EnableDevelopmentEndpoints"))
{
    app.MapDevelopmentProblemDraftEndpoints();
}
await app.RunAsync();

public partial class Program;
