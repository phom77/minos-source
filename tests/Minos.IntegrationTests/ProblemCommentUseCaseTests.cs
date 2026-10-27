using Minos.Application;
using Minos.Application.Accounts;
using Minos.Application.Comments;
using Minos.Domain;
using Minos.Domain.Accounts;
using Minos.Domain.Comments;
using Xunit;

namespace Minos.IntegrationTests;

public sealed class ProblemCommentUseCaseTests
{
    private readonly CancellationToken token = TestContext.Current.CancellationToken;
    private readonly Account owner = Account.Create("Writer", DateTimeOffset.UtcNow);
    private readonly Repository repository = new();
    private ProblemCommentUseCases Cases(Account? account, Guid? current = null) => new(repository,
        new(new AppAccountStore(account ?? owner), new TestCurrentAccount(current ?? account?.Id), TimeProvider.System),
        new TestCurrentAccount(current ?? account?.Id), TimeProvider.System);

    [Fact]
    public async Task Anonymous_reads_have_no_capabilities_and_anonymous_or_disabled_writes_are_denied()
    {
        await Cases(owner).CreateAsync(repository.Problem, new(repository.Version, "Question", false), token);
        var row = Assert.Single((await Cases(null).ListAsync(repository.Problem, null, new(), token)).Items);
        Assert.False(row.CanEdit); Assert.False(row.CanDelete); Assert.False(row.CanHide);
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => Cases(null).CreateAsync(repository.Problem, new(repository.Version, "Question", false), token));
        owner.Disable();
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => Cases(owner).EditAsync(repository.Problem, row.Id, new(1, "Edit", false), token));
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => Cases(owner).RemoveAsync(repository.Problem, row.Id, 1, false, token));
        Assert.Equal(1, repository.Writes);
    }

    [Fact]
    public async Task Only_author_edits_or_deletes_and_only_admin_moderates_even_outside_http()
    {
        var own = await Cases(owner).CreateAsync(repository.Problem, new(repository.Version, "Question", true), token);
        var other = Account.Create("Other", DateTimeOffset.UtcNow);
        other.AssignRole(AccountRole.Setter);
        await Assert.ThrowsAsync<AccessDeniedException>(() => Cases(other).EditAsync(repository.Problem, own.Id, new(1, "Hijack", false), token));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Cases(other).RemoveAsync(repository.Problem, own.Id, 1, false, token));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Cases(other).RemoveAsync(repository.Problem, own.Id, 1, true, token));
        other.AssignRole(AccountRole.Administrator);
        await Assert.ThrowsAsync<AccessDeniedException>(() => Cases(other).EditAsync(repository.Problem, own.Id, new(1, "Admin rewrite", false), token));
        await Cases(other).RemoveAsync(repository.Problem, own.Id, 1, true, token);
        var hidden = Assert.Single((await Cases(null).ListAsync(repository.Problem, null, new(), token)).Items);
        Assert.Null(hidden.Body); Assert.False(hidden.IsSpoiler); Assert.False(hidden.CanEdit);
    }

    [Fact]
    public async Task Publication_problem_scope_thread_depth_and_revision_are_enforced()
    {
        var cases = Cases(owner);
        await Assert.ThrowsAsync<DomainValidationException>(() => cases.CreateAsync(repository.Problem, new(Guid.NewGuid(), "Wrong version", false), token));
        var root = await cases.CreateAsync(repository.Problem, new(repository.Version, "Question", false), token);
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => cases.EditAsync(Guid.NewGuid(), root.Id, new(1, "Wrong problem", false), token));
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => cases.EditAsync(repository.Problem, root.Id, new(0, "Stale", false), token));
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => cases.RemoveAsync(repository.Problem, root.Id, 0, false, token));
        var reply = await cases.CreateAsync(repository.Problem, new(repository.Version, "Reply", false, root.Id), token);
        await Assert.ThrowsAsync<DomainValidationException>(() => cases.CreateAsync(repository.Problem, new(repository.Version, "Nested", false, reply.Id), token));
        await cases.RemoveAsync(repository.Problem, root.Id, 1, false, token);
        Assert.Single((await cases.ListAsync(repository.Problem, root.Id, new(), token)).Items);
        await Assert.ThrowsAsync<DomainValidationException>(() => cases.CreateAsync(repository.Problem, new(repository.Version, "Late reply", false, root.Id), token));
        repository.Public = false;
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => cases.ListAsync(repository.Problem, null, new(), token));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => cases.RemoveAsync(repository.Problem, reply.Id, 1, false, token));
    }

    [Theory]
    [InlineData(0)] [InlineData(51)]
    public async Task Pagination_is_bounded(int size) =>
        await Assert.ThrowsAsync<DomainValidationException>(() => Cases(null).ListAsync(repository.Problem, null, new(null, size), token));

    private sealed class Repository : IProblemCommentRepository
    {
        internal Guid Problem { get; } = Guid.NewGuid();
        internal Guid Version { get; } = Guid.NewGuid();
        internal bool Public { get; set; } = true;
        internal int Writes { get; private set; }
        private readonly Dictionary<Guid, ProblemComment> values = [];
        public Task<bool> IsPublicProblemAsync(Guid id, CancellationToken token) => Task.FromResult(Public && id == Problem);
        public Task<bool> VersionBelongsToProblemAsync(Guid id, Guid version, CancellationToken token) => Task.FromResult(id == Problem && version == Version);
        public Task<ProblemComment?> FindAsync(Guid id, Guid commentId, CancellationToken token) =>
            Task.FromResult(values.TryGetValue(commentId, out var value) && value.ProblemId == id ? value : null);
        public Task SaveAsync(ProblemComment value, CancellationToken token) { values[value.Id] = value; Writes++; return Task.CompletedTask; }
        public Task<IReadOnlyList<CommentReadRow>> ListAsync(Guid id, Guid? parentId, CommentPageQuery query, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<CommentReadRow>>(values.Values.Where(value => value.ProblemId == id && value.ParentCommentId == parentId)
                .Take(query.PageSize + 1).Select(value => new CommentReadRow(value.Id, id, value.PublishedVersionId, value.AuthorAccountId,
                    value.ParentCommentId, "Writer", value.Body, value.IsSpoiler, value.State, value.Revision, value.CreatedAtUtc, value.UpdatedAtUtc,
                    values.Values.Count(reply => reply.ParentCommentId == value.Id))).ToArray());
    }
}
