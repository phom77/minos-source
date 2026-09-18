using Minos.Application;
using Minos.Application.Accounts;
using Minos.Application.Problems;
using Minos.Domain;
using Minos.Domain.Accounts;
using Minos.Domain.Problems;
using Xunit;

namespace Minos.IntegrationTests;

// Exercise the real Application access policy with no HTTP or PostgreSQL.
public sealed class ProblemDraftUseCaseTests
{
    [Fact]
    public async Task Create_uses_domain_validation_owner_and_commits_once_with_utc_time()
    {
        var account = Account.Create("Owner", DateTimeOffset.UtcNow);
        var repository = new RecordingRepository();
        var clock = new FixedClock();
        var useCases = Cases(account, new(account.Id), repository, clock);
        var token = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<DomainValidationException>(() => useCases.CreateAsync(new(" ", "Statement"), token));
        Assert.Equal(0, repository.Writes);
        var created = await useCases.CreateAsync(new("  A + B  ", "Statement"), token);
        Assert.Equal(1, repository.Writes);
        Assert.Equal("A + B", created.Title);
        Assert.Equal(clock.GetUtcNow(), created.CreatedAtUtc);
        Assert.Equal(account.Id, repository.Draft!.OwnerAccountId);
        Assert.Equal(token, repository.Token);
    }

    [Fact]
    public async Task Anonymous_and_disabled_callers_are_rejected_before_draft_persistence_and_cross_owner_read_is_denied()
    {
        var account = Account.Create("Owner", DateTimeOffset.UtcNow);
        var actor = new TestCurrentAccount(null);
        var repository = new RecordingRepository();
        var useCases = Cases(account, actor, repository, TimeProvider.System);
        var token = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => useCases.CreateAsync(new("Title", "Statement"), token));
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => useCases.FindAsync(Guid.NewGuid(), token));
        Assert.Equal(0, repository.Reads + repository.Writes);
        actor.AccountId = account.Id;
        repository.Draft = ProblemDraft.Create(Guid.NewGuid(), "Other's draft", "Private", DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<AccessDeniedException>(() => useCases.FindAsync(repository.Draft.Id, token));
        Assert.Equal(1, repository.Reads);
        repository.Draft = ProblemDraft.Create(account.Id, "Mine", "Private", DateTimeOffset.UtcNow);
        Assert.NotNull(await useCases.FindAsync(repository.Draft.Id, token));
        account.Disable();
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => useCases.CreateAsync(new("Title", "Statement"), token));
        Assert.Equal(0, repository.Writes);
    }

    [Fact]
    public async Task Cancellation_stops_before_persistence()
    {
        var account = Account.Create("Owner", DateTimeOffset.UtcNow);
        var repository = new RecordingRepository();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var useCases = Cases(account, new(account.Id), repository, TimeProvider.System);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => useCases.CreateAsync(new("Title", "Statement"), cancellation.Token));
        Assert.Equal(0, repository.Writes);
    }

    private static ProblemDraftUseCases Cases(Account account, TestCurrentAccount actor, RecordingRepository repository, TimeProvider clock) =>
        new(repository, new ProblemDraftAccess(new(new AppAccountStore(account), actor, clock)), clock);
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 14, 0, 0, 0, TimeSpan.Zero);
    }
    private sealed class RecordingRepository : IProblemDraftRepository
    {
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        public ProblemDraft? Draft { get; set; }
        public CancellationToken Token { get; private set; }
        public Task AddAsync(ProblemDraft draft, CancellationToken token) { Writes++; Token = token; Draft = draft; return Task.CompletedTask; }
        public Task<ProblemDraft?> FindAsync(Guid id, CancellationToken token) { Reads++; Token = token; return Task.FromResult(Draft?.Id == id ? Draft : null); }
    }
}
