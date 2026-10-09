using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Minos.Domain.Accounts;
using Minos.Domain.Problems;
using Minos.Infrastructure.Accounts;
using Minos.Infrastructure.Persistence.Configurations.Accounts;
using Minos.Infrastructure.Persistence.Configurations.Problems;
using Minos.Infrastructure.Persistence.Configurations.Evaluation;
using Minos.Domain.Evaluation;
using Minos.Domain.Contests;
using Minos.Infrastructure.Persistence.Configurations;

namespace Minos.Infrastructure.Persistence;

public sealed class MinosDbContext(DbContextOptions<MinosDbContext> options) : IdentityUserContext<CredentialUser, Guid>(options)
{
    public DbSet<Contest> Contests => Set<Contest>();
    public DbSet<ContestRegistration> ContestRegistrations => Set<ContestRegistration>();
    public DbSet<ContestSubmission> ContestSubmissions => Set<ContestSubmission>();
    public DbSet<Problem> Problems => Set<Problem>();
    public DbSet<ProblemTestCase> ProblemTestCases => Set<ProblemTestCase>();
    public DbSet<PublishedProblemVersion> PublishedProblemVersions => Set<PublishedProblemVersion>();
    public DbSet<PublishedProblemTestCase> PublishedProblemTestCases => Set<PublishedProblemTestCase>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<JudgeJob> JudgeJobs => Set<JudgeJob>();
    public DbSet<JudgingAttempt> JudgingAttempts => Set<JudgingAttempt>();
    public DbSet<SubmissionResult> SubmissionResults => Set<SubmissionResult>();
    public DbSet<TestCaseResult> TestCaseResults => Set<TestCaseResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        var contests = new ContestConfiguration();
        modelBuilder.ApplyConfiguration<Contest>(contests);
        modelBuilder.ApplyConfiguration<ContestProblem>(contests);
        modelBuilder.ApplyConfiguration<ContestRegistration>(contests);
        modelBuilder.ApplyConfiguration<ContestSubmission>(contests);

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
        modelBuilder.ApplyConfiguration(new SubmissionConfiguration());
        modelBuilder.ApplyConfiguration(new JudgeJobConfiguration());
        modelBuilder.ApplyConfiguration(new JudgingAttemptConfiguration());
        modelBuilder.ApplyConfiguration(new SubmissionResultConfiguration());
        modelBuilder.ApplyConfiguration(new TestCaseResultConfiguration());
    }
}
