using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Minos.Api.Accounts;
using Minos.Api.Comments;
using Minos.Application;
using Minos.Application.Comments;
using Minos.Domain;
using Minos.Domain.Accounts;
using Minos.Domain.Comments;
using Minos.Domain.Problems;
using Minos.Infrastructure.Persistence;
using Npgsql;
using Xunit;

namespace Minos.IntegrationTests;

public sealed class ProblemCommentIntegrationTests
{
    private readonly CancellationToken token = TestContext.Current.CancellationToken;
    private static string Url(Guid problem) => $"/api/problems/{problem}/comments";

    [Fact]
    public async Task Http_comments_support_solver_writes_public_reads_author_edits_and_admin_moderation()
    {
        await using var factory = AccountTestHost.Factory();
        using var writer = AccountTestHost.Client(factory);
        Assert.Equal(HttpStatusCode.Created, (await AccountTestHost.RegisterAsync(writer, AccountTestHost.Email())).StatusCode);
        await AccountTestHost.CsrfAsync(writer);
        var (problem, version) = await SeedAsync();
        var url = Url(problem.Id);
        var root = await PostAsync(writer, problem.Id, new(version.Id, "Question with **Markdown**", true));
        var reply = await PostAsync(writer, problem.Id, new(version.Id, "Reply", false, root.Id));
        using var guest = AccountTestHost.Client(factory);
        var response = await guest.GetAsync(url, token);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        var visible = Assert.Single((await response.Content.ReadFromJsonAsync<ProblemCommentPageResponse>(token))!.Items);
        Assert.False(visible.CanEdit); Assert.False(visible.CanDelete); Assert.False(visible.CanHide);
        Assert.Equal(1, visible.ReplyCount);
        Assert.DoesNotContain("authorAccountId", await response.Content.ReadAsStringAsync(token), StringComparison.OrdinalIgnoreCase);
        Assert.True(Assert.Single((await writer.GetFromJsonAsync<ProblemCommentPageResponse>(url, token))!.Items).CanEdit);
        await AccountTestHost.CsrfAsync(guest);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.PostAsJsonAsync(url, new CreateProblemCommentRequest(version.Id, "Anonymous"), token)).StatusCode);

        using var other = AccountTestHost.Client(factory);
        var otherId = (await (await AccountTestHost.RegisterAsync(other, AccountTestHost.Email())).Content.ReadFromJsonAsync<AccountResponse>(token))!.Id;
        await AccountTestHost.CsrfAsync(other);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PutAsJsonAsync(url + "/" + root.Id, new EditProblemCommentRequest(1, "Hijack"), token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsJsonAsync(url + "/" + root.Id + "/delete", new RemoveProblemCommentRequest(1), token)).StatusCode);
        await AccountTestHost.SetRoleAsync(factory, otherId, AccountRole.Setter);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsJsonAsync(url + "/" + reply.Id + "/hide", new RemoveProblemCommentRequest(1), token)).StatusCode);
        await AccountTestHost.SetRoleAsync(factory, otherId, AccountRole.Administrator);
        Assert.Equal(HttpStatusCode.NoContent, (await other.PostAsJsonAsync(url + "/" + reply.Id + "/hide", new RemoveProblemCommentRequest(1), token)).StatusCode);

        var edited = await writer.PutAsJsonAsync(url + "/" + root.Id, new EditProblemCommentRequest(1, "Updated question"), token);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal(2, (await edited.Content.ReadFromJsonAsync<ProblemCommentResponse>(token))!.Revision);
        Assert.Equal(HttpStatusCode.Conflict, (await writer.PutAsJsonAsync(url + "/" + root.Id, new EditProblemCommentRequest(1, "Stale edit"), token)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await writer.PostAsJsonAsync(url + "/" + root.Id + "/delete", new RemoveProblemCommentRequest(2), token)).StatusCode);
        var deleted = Assert.Single((await guest.GetFromJsonAsync<ProblemCommentPageResponse>(url, token))!.Items);
        Assert.Equal("Deleted", deleted.State); Assert.Null(deleted.Body); Assert.False(deleted.IsSpoiler);
        var hidden = Assert.Single((await guest.GetFromJsonAsync<ProblemCommentPageResponse>(url + "/" + root.Id + "/replies", token))!.Items);
        Assert.Equal("Hidden", hidden.State); Assert.Null(hidden.Body); Assert.False(hidden.IsSpoiler);
        Assert.Equal(HttpStatusCode.BadRequest, (await writer.PostAsJsonAsync(url, new CreateProblemCommentRequest(version.Id, "Late reply", false, root.Id), token)).StatusCode);
        await using var database = Migrator();
        Assert.All(await database.ProblemComments.Where(value => value.ProblemId == problem.Id).ToArrayAsync(token), value => Assert.Empty(value.Body));
    }

    [Fact]
    public async Task Http_validates_csrf_version_problem_depth_body_and_disabled_account_without_leaking_diagnostics()
    {
        await using var factory = AccountTestHost.Factory();
        using var writer = AccountTestHost.Client(factory);
        var account = (await (await AccountTestHost.RegisterAsync(writer, AccountTestHost.Email())).Content.ReadFromJsonAsync<AccountResponse>(token))!;
        await AccountTestHost.CsrfAsync(writer);
        var (problem, version) = await SeedAsync();
        var (_, otherVersion) = await SeedAsync();
        var root = await PostAsync(writer, problem.Id, new(version.Id, "Question"));
        var reply = await PostAsync(writer, problem.Id, new(version.Id, "Reply", false, root.Id));
        var url = Url(problem.Id);
        foreach (var request in new[] {
            new CreateProblemCommentRequest(version.Id, " "), new(version.Id, new string('a', 4001)),
            new(version.Id, "bad\0text"), new(otherVersion.Id, "Wrong version"), new(version.Id, "Nested", false, reply.Id)
        })
        {
            var response = await writer.PostAsJsonAsync(url, request, token);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
            Assert.DoesNotContain("stack", await response.Content.ReadAsStringAsync(token), StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await writer.PostAsJsonAsync(Url(otherVersion.ProblemId), new CreateProblemCommentRequest(otherVersion.Id, "Cross problem", false, root.Id), token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await writer.GetAsync(url + "/" + reply.Id + "/replies", token)).StatusCode);
        writer.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await writer.PostAsJsonAsync(url, new CreateProblemCommentRequest(version.Id, "No CSRF"), token)).StatusCode);
        await AccountTestHost.CsrfAsync(writer);
        await using var database = Migrator();
        var stored = (await database.Accounts.FindAsync([account.Id], token))!;
        stored.Disable(); await database.SaveChangesAsync(token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await writer.PostAsJsonAsync(url, new CreateProblemCommentRequest(version.Id, "Disabled"), token)).StatusCode);
    }

    [Fact]
    public async Task Cursor_pages_are_stable_with_tied_timestamps_new_roots_and_tombstones_and_replies_are_independent()
    {
        var (problem, version) = await SeedAsync();
        var now = DateTimeOffset.UtcNow;
        var roots = Enumerable.Range(0, 5).Select(index => ProblemComment.Create(problem.Id, version.Id, problem.OwnerAccountId, "Root " + index, false, now)).ToArray();
        var replies = Enumerable.Range(0, 3).Select(index => ProblemComment.Create(problem.Id, version.Id, problem.OwnerAccountId, "Reply " + index, false, now, roots[0])).ToArray();
        await using (var database = Migrator()) { database.ProblemComments.AddRange(roots); await database.SaveChangesAsync(token); database.ProblemComments.AddRange(replies); await database.SaveChangesAsync(token); }
        await using var factory = AccountTestHost.Factory(); using var guest = AccountTestHost.Client(factory);
        await using (var scope = factory.Services.CreateAsyncScope())
            Assert.Equal(3, (await scope.ServiceProvider.GetRequiredService<IProblemCommentRepository>().ListAsync(problem.Id, null, new(null, 2), token)).Count);
        var first = (await guest.GetFromJsonAsync<ProblemCommentPageResponse>(Url(problem.Id) + "?pageSize=2", token))!;
        Assert.Equal(2, first.Items.Count); Assert.NotNull(first.NextCursor);
        await using (var database = Migrator())
        {
            database.ProblemComments.Add(ProblemComment.Create(problem.Id, version.Id, problem.OwnerAccountId, "New root", false, now.AddSeconds(1)));
            var anchor = (await database.ProblemComments.FindAsync([first.NextCursor!.Value], token))!;
            anchor.Delete(now.AddSeconds(1)); await database.SaveChangesAsync(token);
        }
        var second = (await guest.GetFromJsonAsync<ProblemCommentPageResponse>(Url(problem.Id) + "?pageSize=2&after=" + first.NextCursor, token))!;
        var third = (await guest.GetFromJsonAsync<ProblemCommentPageResponse>(Url(problem.Id) + "?pageSize=2&after=" + second.NextCursor, token))!;
        Assert.Null(third.NextCursor);
        Assert.Equal(roots.Select(value => value.Id).Order(), first.Items.Concat(second.Items).Concat(third.Items).Select(value => value.Id).Order());
        var repliesUrl = Url(problem.Id) + "/" + roots[0].Id + "/replies?pageSize=2";
        var replyPage = (await guest.GetFromJsonAsync<ProblemCommentPageResponse>(repliesUrl, token))!;
        Assert.Equal(2, replyPage.Items.Count);
        Assert.Single((await guest.GetFromJsonAsync<ProblemCommentPageResponse>(repliesUrl + "&after=" + replyPage.NextCursor, token))!.Items);
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.GetAsync(Url(problem.Id) + "?after=" + replies[0].Id, token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.GetAsync(Url(problem.Id) + "?pageSize=51", token)).StatusCode);
    }

    [Fact]
    public async Task Publication_keeps_the_discussion_and_archive_closes_reads_and_pending_writes()
    {
        var (problem, version) = await SeedAsync();
        var comment = ProblemComment.Create(problem.Id, version.Id, problem.OwnerAccountId, "Old version context", false, DateTimeOffset.UtcNow);
        await using (var database = Migrator())
        {
            database.ProblemComments.Add(comment); await database.SaveChangesAsync(token);
            var stored = (await database.Problems.Include(value => value.Samples).Include(value => value.TestCases).SingleAsync(value => value.Id == problem.Id, token));
            database.PublishedProblemVersions.Add(stored.Publish(problem.OwnerAccountId, new(true, true, true), DateTimeOffset.UtcNow));
            await database.SaveChangesAsync(token);
        }
        await using var factory = AccountTestHost.Factory(); using var guest = AccountTestHost.Client(factory);
        Assert.Equal(version.Id, Assert.Single((await guest.GetFromJsonAsync<ProblemCommentPageResponse>(Url(problem.Id), token))!.Items).PublishedVersionId);
        await using var scope = factory.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IProblemCommentRepository>();
        var pending = (await repository.FindAsync(problem.Id, comment.Id, token))!;
        pending.Edit("Pending edit", false, DateTimeOffset.UtcNow);
        await using (var database = Migrator())
        {
            var stored = (await database.Problems.FindAsync([problem.Id], token))!;
            stored.Archive(DateTimeOffset.UtcNow); await database.SaveChangesAsync(token);
        }
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => repository.SaveAsync(pending, token));
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync(Url(problem.Id), token)).StatusCode);
    }

    [Fact]
    public async Task Concurrent_edits_have_one_winner_and_a_stale_visible_parent_cannot_receive_a_reply()
    {
        var (problem, version) = await SeedAsync();
        var root = ProblemComment.Create(problem.Id, version.Id, problem.OwnerAccountId, "Question", false, DateTimeOffset.UtcNow);
        await using (var database = Migrator()) { database.ProblemComments.Add(root); await database.SaveChangesAsync(token); }
        await using var factory = AccountTestHost.Factory();
        await using var left = factory.Services.CreateAsyncScope(); await using var right = factory.Services.CreateAsyncScope();
        var a = left.ServiceProvider.GetRequiredService<IProblemCommentRepository>(); var b = right.ServiceProvider.GetRequiredService<IProblemCommentRepository>();
        var first = (await a.FindAsync(problem.Id, root.Id, token))!; var second = (await b.FindAsync(problem.Id, root.Id, token))!;
        first.Edit("First", false, DateTimeOffset.UtcNow); second.Edit("Second", false, DateTimeOffset.UtcNow);
        async Task<bool> Save(IProblemCommentRepository repository, ProblemComment value)
        { try { await repository.SaveAsync(value, token); return true; } catch (ConcurrencyConflictException) { return false; } }
        Assert.Single(await Task.WhenAll(Save(a, first), Save(b, second)), value => value);
        var reply = ProblemComment.Create(problem.Id, version.Id, problem.OwnerAccountId, "Pending reply", false, DateTimeOffset.UtcNow, root);
        await using (var database = Migrator())
        { var stored = (await database.ProblemComments.FindAsync([root.Id], token))!; stored.Delete(DateTimeOffset.UtcNow); await database.SaveChangesAsync(token); }
        await using var pendingScope = factory.Services.CreateAsyncScope();
        await Assert.ThrowsAsync<DomainValidationException>(() => pendingScope.ServiceProvider.GetRequiredService<IProblemCommentRepository>().SaveAsync(reply, token));
    }

    [Fact]
    public async Task Database_constraints_protect_text_thread_depth_and_immutable_associations_when_domain_is_bypassed()
    {
        var (problem, version) = await SeedAsync(); var (_, otherVersion) = await SeedAsync();
        var root = ProblemComment.Create(problem.Id, version.Id, problem.OwnerAccountId, "Question", false, DateTimeOffset.UtcNow);
        var reply = ProblemComment.Create(problem.Id, version.Id, problem.OwnerAccountId, "Reply", false, DateTimeOffset.UtcNow, root);
        await using (var database = Migrator()) { database.ProblemComments.Add(root); await database.SaveChangesAsync(token); database.ProblemComments.Add(reply); await database.SaveChangesAsync(token); }
        var copy = "INSERT INTO problems.problem_comments SELECT gen_random_uuid(),problem_id,published_version_id,author_account_id,{0},'Reply',false,0,1,now(),now() FROM problems.problem_comments WHERE id = @id";
        foreach (var sql in new[] {
            "UPDATE problems.problem_comments SET body = ' ',revision=revision+1 WHERE id = @id",
            "UPDATE problems.problem_comments SET body = U&'a\\0085b',revision=revision+1 WHERE id = @id",
            "UPDATE problems.problem_comments SET body = repeat('🧩',2001),revision=revision+1 WHERE id = @id",
            "UPDATE problems.problem_comments SET state=1,revision=revision+1 WHERE id = @id",
            "UPDATE problems.problem_comments SET state=99,revision=revision+1 WHERE id = @id",
            "UPDATE problems.problem_comments SET parent_comment_id = @id,revision=revision+1 WHERE id = @id",
            $"UPDATE problems.problem_comments SET published_version_id = '{otherVersion.Id}',revision=revision+1 WHERE id = @id",
            "UPDATE problems.problem_comments SET author_account_id = gen_random_uuid(),revision=revision+1 WHERE id = @id",
            string.Format(System.Globalization.CultureInfo.InvariantCulture, copy, $"'{reply.Id}'")
        })
        {
            await using var connection = new NpgsqlConnection(PostgresPrivilegeTests.Connection("ConnectionStrings__Minos"));
            await connection.OpenAsync(token); await using var transaction = await connection.BeginTransactionAsync(token);
            await using var command = new NpgsqlCommand(sql, connection, transaction); command.Parameters.AddWithValue("id", root.Id);
            var failure = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
            Assert.Contains(failure.SqlState, new[] { PostgresErrorCodes.CheckViolation, PostgresErrorCodes.ForeignKeyViolation });
        }
    }

    [Fact]
    public async Task Comment_rate_limit_is_account_scoped_and_returns_safe_retry_feedback()
    {
        await using var factory = AccountTestHost.Factory(); using var writer = AccountTestHost.Client(factory);
        await AccountTestHost.RegisterAsync(writer, AccountTestHost.Email()); await AccountTestHost.CsrfAsync(writer);
        var (problem, version) = await SeedAsync();
        for (var index = 0; index < 20; index++) await PostAsync(writer, problem.Id, new(version.Id, "Question " + index));
        var rejected = await writer.PostAsJsonAsync(Url(problem.Id), new CreateProblemCommentRequest(version.Id, "Over limit"), token);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.NotNull(rejected.Headers.RetryAfter);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType!.MediaType);
        using var other = AccountTestHost.Client(factory);
        await AccountTestHost.RegisterAsync(other, AccountTestHost.Email()); await AccountTestHost.CsrfAsync(other);
        await PostAsync(other, problem.Id, new(version.Id, "Another account"));
    }

    private async Task<ProblemCommentResponse> PostAsync(HttpClient client, Guid problemId, CreateProblemCommentRequest request)
    {
        var response = await client.PostAsJsonAsync(Url(problemId), request, token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProblemCommentResponse>(token))!;
    }
    private static MinosDbContext Migrator() => PostgresPrivilegeTests.Database(PostgresPrivilegeTests.Connection("MINOS_TEST_MIGRATION_CONNECTION"));
    private async Task<(Problem, PublishedProblemVersion)> SeedAsync()
    {
        await using var database = Migrator(); var now = DateTimeOffset.UtcNow;
        var owner = Account.Create("Comment fixture author", now); owner.AssignRole(AccountRole.Setter);
        var problem = Problem.Create(owner.Id, "Comment fixture", "Print one.", now);
        problem.ConfigureExecution(1000, 64, BuiltInChecker.Exact, now); problem.ReplaceSamples([new("1", "1")], now);
        problem.AddTestCase(ProblemArtifactReference.Create($"tests/{Guid.NewGuid():N}/input.bin", new('a', 64), 1),
            ProblemArtifactReference.Create($"tests/{Guid.NewGuid():N}/output.bin", new('b', 64), 1), now);
        var version = problem.Publish(owner.Id, new(true, true, true), now);
        database.Accounts.Add(owner); database.Problems.Add(problem); database.PublishedProblemVersions.Add(version);
        await database.SaveChangesAsync(token); return (problem, version);
    }
}
