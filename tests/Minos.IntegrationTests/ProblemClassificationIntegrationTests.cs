using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Minos.Api.Accounts;
using Minos.Api.Problems;
using Minos.Application;
using Minos.Application.Problems;
using Minos.Domain.Accounts;
using Minos.Domain.Problems;
using Minos.Infrastructure.Persistence;
using Npgsql;
using Xunit;

namespace Minos.IntegrationTests;

public sealed class ProblemClassificationIntegrationTests
{
    [Fact]
    public async Task Owner_can_edit_library_metadata_without_republishing_and_new_publication_flags_reassessment()
    {
        var token = TestContext.Current.CancellationToken;
        await using var factory = AccountTestHost.Factory();
        using var author = AccountTestHost.Client(factory);
        var account = (await (await AccountTestHost.RegisterAsync(author, AccountTestHost.Email())).Content.ReadFromJsonAsync<AccountResponse>(token))!;
        await AccountTestHost.SetRoleAsync(factory, account.Id, AccountRole.Setter);
        await AccountTestHost.CsrfAsync(author);
        var version = await PublishAsync(author);
        var url = $"/api/authoring/problems/{version.ProblemId}/classification";
        var initial = await author.GetFromJsonAsync<ProblemClassificationDetailsResponse>(url, token);
        Assert.Equal(0, initial!.Revision);
        Assert.Null(initial.Difficulty);
        var request = new UpdateProblemClassificationRequest(0, version.VersionId, "Easy", "CodeforcesRating",
            new("cf-1-a", 1200, ["math"], new('a', 40), new('b', 64), "codeforces-v1", new('c', 64)),
            [new("math", "Inferred", "Medium", "Author-only inference evidence.")]);
        var first = await author.PutAsJsonAsync(url, request, token);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstValue = (await first.Content.ReadFromJsonAsync<ProblemClassificationDetailsResponse>(token))!;
        Assert.Equal(1, firstValue.Revision);
        Assert.Equal(1200, firstValue.Evidence!.SourceRating);
        var revised = request with { ExpectedRevision = 1, Difficulty = "Hard", DifficultyOrigin = "Manual", Topics = [new("math", "Manual"), new("greedy", "Manual")] };
        Assert.Equal(HttpStatusCode.OK, (await author.PutAsJsonAsync(url, revised, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await author.PutAsJsonAsync(url, request, token)).StatusCode);
        var unknown = revised with { ExpectedRevision = 2, Topics = [new("unknown", "Manual")] };
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PutAsJsonAsync(url, unknown, token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PutAsJsonAsync(url, revised with { Difficulty = "99" }, token)).StatusCode);

        using var reader = AccountTestHost.Client(factory);
        Assert.Equal(24, (await reader.GetFromJsonAsync<ProblemTopicResponse[]>("/api/problems/topics", token))!.Length);
        var catalog = (await reader.GetFromJsonAsync<PublishedProblemPageResponse>("/api/problems/?page=1&pageSize=100", token))!;
        var item = Assert.Single(catalog.Items, item => item.ProblemId == version.ProblemId);
        Assert.Equal("Hard", item.Classification!.Difficulty);
        Assert.Equal(2, item.Classification.Topics.Count);
        Assert.False(item.Classification.NeedsReassessment);
        var publicJson = await reader.GetStringAsync($"/api/problems/{version.ProblemId}/classification", token);
        Assert.DoesNotContain("rationale", publicJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sourceRating", publicJson, StringComparison.OrdinalIgnoreCase);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<MinosDbContext>();
            Assert.Equal(1, await database.PublishedProblemVersions.CountAsync(value => value.ProblemId == version.ProblemId, token));
        }
        var changed = await author.PutAsJsonAsync($"/api/authoring/problems/{version.ProblemId}/draft",
            new UpdateProblemDraftRequest("New draft", "Changed statement", 2000, 64, "Exact", [new("1", "1")]), token);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(version.Title, (await reader.GetFromJsonAsync<PublishedProblemResponse>($"/api/problems/{version.ProblemId}", token))!.Title);
        Assert.Equal(HttpStatusCode.Created, (await author.PostAsJsonAsync($"/api/authoring/problems/{version.ProblemId}/publish", new PublishProblemRequest(true, true, true), token)).StatusCode);
        var metadata = (await reader.GetFromJsonAsync<ProblemClassificationResponse>($"/api/problems/{version.ProblemId}/classification", token))!;
        Assert.True(metadata.NeedsReassessment);
        Assert.Equal("Hard", metadata.Difficulty);
        Assert.Equal(version.Title, (await reader.GetFromJsonAsync<PublishedProblemResponse>($"/api/problems/versions/{version.VersionId}", token))!.Title);
        await using var pendingScope = factory.Services.CreateAsyncScope();
        var pendingRepository = pendingScope.ServiceProvider.GetRequiredService<IProblemClassificationRepository>();
        var pendingEdit = (await pendingRepository.FindAsync(version.ProblemId, token))!;
        pendingEdit.Revise(version.VersionId, ProblemDifficulty.Easy, DifficultyOrigin.Manual, null, [], DateTimeOffset.UtcNow);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/authoring/problems/{version.ProblemId}/archive", null, token)).StatusCode);
        await Assert.ThrowsAsync<Minos.Domain.DomainValidationException>(() => pendingRepository.SaveAsync(pendingEdit, token));
        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync($"/api/problems/{version.ProblemId}/classification", token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PutAsJsonAsync(url, revised with { ExpectedRevision = 2 }, token)).StatusCode);
    }

    [Fact]
    public async Task Http_authorization_and_database_constraints_protect_classification()
    {
        var token = TestContext.Current.CancellationToken;
        await using var factory = AccountTestHost.Factory();
        using var author = AccountTestHost.Client(factory);
        var owner = (await (await AccountTestHost.RegisterAsync(author, AccountTestHost.Email())).Content.ReadFromJsonAsync<AccountResponse>(token))!;
        await AccountTestHost.SetRoleAsync(factory, owner.Id, AccountRole.Setter);
        await AccountTestHost.CsrfAsync(author);
        var version = await PublishAsync(author);
        var otherVersion = await PublishAsync(author);
        var url = $"/api/authoring/problems/{version.ProblemId}/classification";
        var request = new UpdateProblemClassificationRequest(0, version.VersionId, "Medium", "Manual", null, [new("math", "Manual")]);
        using var anonymous = AccountTestHost.Client(factory);
        await AccountTestHost.CsrfAsync(anonymous);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync(url, request, token)).StatusCode);
        using var other = AccountTestHost.Client(factory);
        var otherAccount = (await (await AccountTestHost.RegisterAsync(other, AccountTestHost.Email())).Content.ReadFromJsonAsync<AccountResponse>(token))!;
        await AccountTestHost.CsrfAsync(other);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PutAsJsonAsync(url, request, token)).StatusCode);
        await AccountTestHost.SetRoleAsync(factory, otherAccount.Id, AccountRole.Setter);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync(url, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PutAsJsonAsync(url, request, token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PutAsJsonAsync(url, request with { BasisVersionId = otherVersion.VersionId }, token)).StatusCode);
        author.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PutAsJsonAsync(url, request, token)).StatusCode);
        await AccountTestHost.CsrfAsync(author);
        Assert.Equal(HttpStatusCode.OK, (await author.PutAsJsonAsync(url, request, token)).StatusCode);

        foreach (var sql in new[] {
            "UPDATE problems.problem_classifications SET difficulty = 99 WHERE problem_id = @id",
            "INSERT INTO problems.problem_topics(problem_id,topic_slug,origin,confidence) VALUES (@id,'greedy',1,NULL)",
            "INSERT INTO problems.problem_topics(problem_id,topic_slug,origin) VALUES (@id,'unknown',0)",
            "INSERT INTO problems.problem_topics(problem_id,topic_slug,origin) VALUES (@id,'math',0)",
            $"UPDATE problems.problem_classifications SET basis_version_id = '{otherVersion.VersionId}' WHERE problem_id = @id"
        })
        {
            await using var connection = new NpgsqlConnection(PostgresPrivilegeTests.Connection("ConnectionStrings__Minos"));
            await connection.OpenAsync(token);
            await using var transaction = await connection.BeginTransactionAsync(token);
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("id", version.ProblemId);
            var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(token));
            Assert.Contains(error.SqlState, new[] { PostgresErrorCodes.CheckViolation, PostgresErrorCodes.ForeignKeyViolation, PostgresErrorCodes.UniqueViolation });
        }
    }

    [Fact]
    public async Task Concurrent_metadata_edits_have_exactly_one_winner()
    {
        var token = TestContext.Current.CancellationToken;
        await using var factory = AccountTestHost.Factory();
        using var author = AccountTestHost.Client(factory);
        var owner = (await (await AccountTestHost.RegisterAsync(author, AccountTestHost.Email())).Content.ReadFromJsonAsync<AccountResponse>(token))!;
        await AccountTestHost.SetRoleAsync(factory, owner.Id, AccountRole.Setter);
        await AccountTestHost.CsrfAsync(author);
        var version = await PublishAsync(author);
        Assert.Equal(HttpStatusCode.OK, (await author.PutAsJsonAsync($"/api/authoring/problems/{version.ProblemId}/classification",
            new UpdateProblemClassificationRequest(0, version.VersionId, "Easy", "Manual", null, []), token)).StatusCode);
        await using var left = factory.Services.CreateAsyncScope();
        await using var right = factory.Services.CreateAsyncScope();
        var a = left.ServiceProvider.GetRequiredService<IProblemClassificationRepository>();
        var b = right.ServiceProvider.GetRequiredService<IProblemClassificationRepository>();
        var first = (await a.FindAsync(version.ProblemId, token))!;
        var second = (await b.FindAsync(version.ProblemId, token))!;
        first.Revise(version.VersionId, ProblemDifficulty.Medium, DifficultyOrigin.Manual, null, [new("math", TopicOrigin.Manual)], DateTimeOffset.UtcNow);
        second.Revise(version.VersionId, ProblemDifficulty.Hard, DifficultyOrigin.Manual, null, [new("greedy", TopicOrigin.Manual)], DateTimeOffset.UtcNow);
        async Task<bool> Save(IProblemClassificationRepository repository, ProblemClassification value)
        {
            try { await repository.SaveAsync(value, token); return true; }
            catch (ConcurrencyConflictException) { return false; }
        }
        var outcomes = await Task.WhenAll(Save(a, first), Save(b, second));
        Assert.Single(outcomes, result => result);
        var actual = (await author.GetFromJsonAsync<ProblemClassificationDetailsResponse>($"/api/authoring/problems/{version.ProblemId}/classification", token))!;
        Assert.Equal(2, actual.Revision);
        Assert.Single(actual.Topics);
        Assert.Equal(actual.Difficulty == "Medium" ? "math" : "greedy", actual.Topics[0].Slug);
    }

    private static async Task<PublishedProblemResponse> PublishAsync(HttpClient author)
    {
        var token = TestContext.Current.CancellationToken;
        var create = await author.PostAsJsonAsync("/api/authoring/problems/", new CreateProblemRequest("Classification test", "Print one."), token);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var problem = (await create.Content.ReadFromJsonAsync<ProblemDraftResponse>(token))!;
        Assert.Equal(HttpStatusCode.OK, (await author.PutAsJsonAsync($"/api/authoring/problems/{problem.Id}/draft",
            new UpdateProblemDraftRequest("Classification test", "Print one.", 1000, 64, "Exact", [new("1", "1")]), token)).StatusCode);
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var name in new[] { "input.txt", "output.txt" })
            { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write("1"); }
        using var content = new ByteArrayContent(bytes.ToArray());
        content.Headers.ContentType = new("application/zip");
        Assert.Equal(HttpStatusCode.OK, (await author.PostAsync($"/api/authoring/problems/{problem.Id}/draft/tests", content, token)).StatusCode);
        var published = await author.PostAsJsonAsync($"/api/authoring/problems/{problem.Id}/publish", new PublishProblemRequest(true, true, true), token);
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        return (await published.Content.ReadFromJsonAsync<PublishedProblemResponse>(token))!;
    }
}
