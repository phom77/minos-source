using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Minos.Application.Problems;
using Minos.Infrastructure.Persistence;
using Minos.Infrastructure.Hosting;
using Npgsql;
using Minos.Application.Accounts;
using Minos.Infrastructure.Accounts;
using Minos.Infrastructure.Storage;
using Minos.Application.Evaluation;
using Minos.Infrastructure.Evaluation;

namespace Minos.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddMinosInfrastructure(
        this IServiceCollection services, string? connectionString, int maximumActiveSubmissions = 32,
        string? executionWorkspaceVolume = null)
    {
        if (maximumActiveSubmissions is < 1 or > 1000)
            throw new InvalidOperationException("Minos:Submissions:MaximumActive must be between 1 and 1000.");
        services.AddSingleton(new SubmissionCapacity(maximumActiveSubmissions));
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:Minos is required. Supply it through environment variables or user secrets.");
        NpgsqlConnectionStringBuilder parsed;
        try { parsed = new NpgsqlConnectionStringBuilder(connectionString); }
        catch (ArgumentException) { throw new InvalidOperationException("ConnectionStrings:Minos is malformed."); }
        if (string.IsNullOrWhiteSpace(parsed.Host) || string.IsNullOrWhiteSpace(parsed.Database) || string.IsNullOrWhiteSpace(parsed.Username))
            throw new InvalidOperationException("ConnectionStrings:Minos must specify Host, Database, and Username.");
        DockerExecutionBackend.ValidateWorkspaceVolume(executionWorkspaceVolume);
        services.AddDbContext<MinosDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IProblemRepository, ProblemRepository>();
        services.AddScoped<ISubmissionRepository, SubmissionRepository>();
        services.AddScoped<IJudgeJobRepository, JudgeJobRepository>();
        services.AddSingleton(_ => new DockerExecutionBackend(executionWorkspaceVolume));
        services.AddSingleton<IExecutionBackend>(provider => provider.GetRequiredService<DockerExecutionBackend>());
        services.AddSingleton(provider => new JudgeSandboxLifecycle(provider.GetRequiredService<DockerExecutionBackend>()));
        services.AddScoped<IJudgingOperationsQuery, JudgingOperationsQuery>();
        services.AddScoped<ISubmissionExecutor, OfficialSubmissionExecutor>();
        services.AddScoped<JudgeJobUseCases>();
        services.AddSingleton<IProblemArtifactStore, ProblemArtifactStore>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<DatabaseOperationalCheck>();
        return services;
    }

    public static Microsoft.AspNetCore.Identity.IdentityBuilder AddMinosAccountPersistence(this IServiceCollection services)
    {
        services.AddScoped<IAccountStore, AccountStore>();
        services.AddScoped<IExternalLoginStore, ExternalLoginStore>();
        services.AddScoped<ExternalLoginUseCases>();
        services.AddScoped<AccountUseCases>();
        services.AddScoped<AccountAdministrationUseCases>();
        services.AddScoped<TrustedAccountProvisioningUseCases>();
        services.AddScoped<ProblemAccess>();
        services.AddScoped<ProblemAuthoringUseCases>();
        services.AddScoped<ProblemCatalogUseCases>();
        services.AddScoped<ProblemArtifactMaintenanceUseCases>();
        services.AddScoped<SubmissionUseCases>();
        services.AddScoped<JudgingOperationsUseCases>();
        return services.AddIdentityCore<CredentialUser>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 12;
            options.Password.RequireDigit = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddEntityFrameworkStores<MinosDbContext>().AddUserManager<AccountUserManager>();
    }
}
