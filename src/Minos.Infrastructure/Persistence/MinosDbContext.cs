using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Minos.Domain.Accounts;
using Minos.Domain.Problems;
using Minos.Infrastructure.Accounts;
using Minos.Infrastructure.Persistence.Configurations.Accounts;
using Minos.Infrastructure.Persistence.Configurations.Problems;

namespace Minos.Infrastructure.Persistence;

public sealed class MinosDbContext(DbContextOptions<MinosDbContext> options) : IdentityUserContext<CredentialUser, Guid>(options)
{
    public DbSet<Problem> Problems => Set<Problem>();
    public DbSet<ProblemTestCase> ProblemTestCases => Set<ProblemTestCase>();
    public DbSet<PublishedProblemVersion> PublishedProblemVersions => Set<PublishedProblemVersion>();
    public DbSet<PublishedProblemTestCase> PublishedProblemTestCases => Set<PublishedProblemTestCase>();
    public DbSet<Account> Accounts => Set<Account>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new AccountConfiguration());
        modelBuilder.ApplyConfiguration(new CredentialUserConfiguration());
        modelBuilder.ApplyConfiguration(new IdentityUserClaimConfiguration());
        modelBuilder.ApplyConfiguration(new ExternalLoginConfiguration());
        modelBuilder.ApplyConfiguration(new IdentityUserTokenConfiguration());
        modelBuilder.ApplyConfiguration(new ProblemConfiguration());
        modelBuilder.ApplyConfiguration(new ProblemSampleConfiguration());
        modelBuilder.ApplyConfiguration(new ProblemTestCaseConfiguration());
        modelBuilder.ApplyConfiguration(new PublishedProblemVersionConfiguration());
        modelBuilder.ApplyConfiguration(new PublishedProblemSampleConfiguration());
        modelBuilder.ApplyConfiguration(new PublishedProblemTestCaseConfiguration());
    }
}
