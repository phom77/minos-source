using Minos.Application;
using Minos.Application.Problems;
using Minos.Domain;
using Minos.Domain.Problems;
using Xunit;

namespace Minos.IntegrationTests;

// Pure Application tests; no HTTP server or PostgreSQL is used by this class.
public sealed class ProblemDraftUseCaseTests
{
    [Fact]
    public async Task Create_uses_domain_validation_and_commits_once_with_utc_time()
    {
        var repository = new RecordingRepository();
        var clock = new FixedClock();
        var useCases = new ProblemDraftUseCases(repository, new ResourceAccess(null), clock);
        var token = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<DomainValidationException>(() => useCases.CreateAsync(new(" ", "Statement"), token));
        Assert.Equal(0, repository.Writes);
        var created = await useCases.CreateAsync(new("  A + B  ", "Statement"), token);
        Assert.Equal(1, repository.Writes);
        Assert.Equal("A + B", created.Title);
        Assert.Equal(clock.GetUtcNow(), created.CreatedAtUtc);
        Assert.Equal(token, repository.Token);
    }

    [Fact]
    public async Task Denied_create_and_cross_resource_read_never_reach_persistence()
    {
        var repository = new RecordingRepository();
        var permittedId = Guid.NewGuid();
        var useCases = new ProblemDraftUseCases(repository, new ResourceAccess(permittedId), TimeProvider.System);
        var token = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<AccessDeniedException>(() => useCases.CreateAsync(new("Title", "Statement"), token));
        await Assert.ThrowsAsync<AccessDeniedException>(() => useCases.FindAsync(Guid.NewGuid(), token));
        Assert.Equal(0, repository.Reads + repository.Writes);
        Assert.Null(await useCases.FindAsync(permittedId, token));
        Assert.Equal(1, repository.Reads);
        Assert.Equal(token, repository.Token);
    }

    [Fact]
    public async Task Cancellation_stops_before_persistence()
    {
        var repository = new RecordingRepository();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var useCases = new ProblemDraftUseCases(repository, new ResourceAccess(null), TimeProvider.System);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => useCases.CreateAsync(new("Title", "Statement"), cancellation.Token));
        Assert.Equal(0, repository.Writes);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 14, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class ResourceAccess(Guid? allowedId) : IProblemDraftAccess
    {
        public Task EnsureCanCreateAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (allowedId is not null) throw new AccessDeniedException();
            return Task.CompletedTask;
        }
        public Task EnsureCanReadAsync(Guid id, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (id != allowedId) throw new AccessDeniedException();
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingRepository : IProblemDraftRepository
    {
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        public CancellationToken Token { get; private set; }
        public Task AddAsync(ProblemDraft draft, CancellationToken cancellationToken) { Writes++; Token = cancellationToken; return Task.CompletedTask; }
        public Task<ProblemDraft?> FindAsync(Guid id, CancellationToken cancellationToken) { Reads++; Token = cancellationToken; return Task.FromResult<ProblemDraft?>(null); }
    }
}
