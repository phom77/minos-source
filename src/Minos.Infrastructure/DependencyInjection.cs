using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Minos.Application.Problems;
using Minos.Infrastructure.Persistence;
using Minos.Infrastructure.Hosting;
using Npgsql;
using Minos.Application.Accounts;
using Minos.Infrastructure.Accounts;

namespace Minos.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddMinosInfrastructure(this IServiceCollection services, string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:Minos is required. Supply it through environment variables or user secrets.");
        NpgsqlConnectionStringBuilder parsed;
        try { parsed = new NpgsqlConnectionStringBuilder(connectionString); }
        catch (ArgumentException) { throw new InvalidOperationException("ConnectionStrings:Minos is malformed."); }
        if (string.IsNullOrWhiteSpace(parsed.Host) || string.IsNullOrWhiteSpace(parsed.Database) || string.IsNullOrWhiteSpace(parsed.Username))
            throw new InvalidOperationException("ConnectionStrings:Minos must specify Host, Database, and Username.");
        services.AddDbContext<MinosDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IProblemDraftRepository, ProblemDraftRepository>();
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
        services.AddScoped<ProblemDraftUseCases>();
        services.AddScoped<IProblemDraftAccess, ProblemDraftAccess>();
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
