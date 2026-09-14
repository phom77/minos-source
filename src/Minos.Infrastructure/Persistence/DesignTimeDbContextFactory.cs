using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Minos.Infrastructure.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MinosDbContext>
{
    public MinosDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Minos")
            ?? throw new InvalidOperationException("Set ConnectionStrings__Minos before running database tools.");
        return new MinosDbContext(new DbContextOptionsBuilder<MinosDbContext>().UseNpgsql(connection).Options);
    }
}
