using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Minos.Application.Problems;
using Minos.Application.Contests;
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
        string? executionWorkspaceVolume = null, string executionRunner = "docker", string? isolateRoot = null,
        ExecutionBudgetOptions? executionBudget = null, int isolateBoxCount = 1)
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
        if (executionRunner is not ("docker" or "isolate"))
            throw new InvalidOperationException("Minos:Execution:Runner must be docker or isolate.");
        if (executionRunner == "isolate") IsolateExecutionBackend.ValidateConfiguration(isolateRoot, executionWorkspaceVolume);
        else if (isolateRoot is not null) throw new InvalidOperationException("Minos:Execution:IsolateRoot requires the isolate runner.");
        if (executionRunner == "isolate" && executionBudget is not null && executionBudget.RuntimeSlots is null)
            executionBudget = executionBudget with { RuntimeSlots = Math.Min(executionBudget.SandboxSlots, isolateBoxCount) };
        executionBudget?.Validate();
        IsolateExecutionBackend.ValidateBoxCount(isolateBoxCount);
        if (executionRunner != "isolate" && isolateBoxCount != 1)
            throw new InvalidOperationException("Multiple Isolate boxes require the isolate runner.");
        if (executionRunner == "isolate" && executionBudget is not null && executionBudget.MaximumRuntimes > isolateBoxCount)
            throw new InvalidOperationException("Runtime slots cannot exceed the configured Isolate box count.");
        services.AddDbContext<MinosDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IProblemRepository, ProblemRepository>();
        services.AddScoped<IContestRepository, ContestRepository>();
        services.AddScoped<ISubmissionRepository, SubmissionRepository>();
        services.AddScoped<IJudgeJobRepository, JudgeJobRepository>();
        services.AddScoped<PostgresJudgeJobDelivery>();
        services.AddScoped<IJudgeJobDelivery>(provider => executionBudget is null ? provider.GetRequiredService<PostgresJudgeJobDelivery>() :
            new BudgetedJudgeJobDelivery(provider.GetRequiredService<PostgresJudgeJobDelivery>(), provider.GetRequiredService<ExecutionBudget>()));
        services.AddSingleton(_ => new DockerExecutionBackend(executionWorkspaceVolume));
        if (executionRunner == "isolate")
        {
            services.AddSingleton(provider => new IsolateExecutionBackend(provider.GetRequiredService<DockerExecutionBackend>(), isolateRoot!, boxCount: isolateBoxCount));
            services.AddSingleton<IExecutionBackend>(provider => provider.GetRequiredService<IsolateExecutionBackend>());
            services.AddSingleton(provider => new JudgeSandboxLifecycle(provider.GetRequiredService<IsolateExecutionBackend>().EnsureInitializedAsync));
        }
        else
        {
            services.AddSingleton<IExecutionBackend>(provider => provider.GetRequiredService<DockerExecutionBackend>());
            services.AddSingleton(provider => new JudgeSandboxLifecycle(provider.GetRequiredService<DockerExecutionBackend>().EnsureInitializedAsync));
        }
        if (executionBudget is not null)
        {
            services.AddSingleton(new ExecutionBudget(executionBudget));
            services.AddSingleton<IExecutionBackend>(provider => new BudgetedExecutionBackend(
                executionRunner == "isolate" ? provider.GetRequiredService<IsolateExecutionBackend>() : provider.GetRequiredService<DockerExecutionBackend>(),
                provider.GetRequiredService<ExecutionBudget>()));
            services.AddSingleton(provider => new JudgeSandboxLifecycle(async token =>
            {
                var compiler = provider.GetRequiredService<DockerExecutionBackend>();
                await compiler.EnsureInitializedAsync(token);
                var capacity = await compiler.ReadHostCapacityAsync(token);
                executionBudget.ValidateHost(capacity.Cpus, capacity.MemoryBytes);
                if (executionRunner == "isolate")
                {
                    executionBudget.ValidateHost(Environment.ProcessorCount, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);
                    await provider.GetRequiredService<IsolateExecutionBackend>().EnsureInitializedAsync(token);
                }
            }));
        }
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
        services.AddScoped<ContestUseCases>();
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
