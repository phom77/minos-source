using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Minos.Application.Problems;
using Minos.Infrastructure.Persistence;
using Minos.Infrastructure.Hosting;
using Npgsql;

namespace Minos.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddMinosInfrastructure(this IServiceCollection services, string? connectionString,
        Func<bool>? enableDeveloperDraftAccess = null)
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
        services.AddSingleton<IProblemDraftAccess>(_ => new DeveloperProblemDraftAccess(enableDeveloperDraftAccess?.Invoke() ?? false));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ProblemDraftUseCases>();
        services.AddScoped<DatabaseOperationalCheck>();
        return services;
    }
}
