using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Minos.Api.Accounts;
using Minos.Api.Problems;
using Minos.Application.Problems;
using Minos.Domain.Accounts;
using Minos.Domain.Problems;
using Minos.Infrastructure.Persistence;
using Npgsql;
using Xunit;

namespace Minos.IntegrationTests;

public sealed class ProblemPublicationTests
{
    [Fact]
    public async Task Author_can_publish_two_immutable_versions_and_archive_without_losing_history()
    {
        var token = TestContext.Current.CancellationToken;
        Guid problemId;
        Guid firstVersionId;
        Guid secondVersionId;

        await using (var factory = AccountTestHost.Factory())
        using (var author = AccountTestHost.Client(factory))
        {
            var registration = await AccountTestHost.RegisterAsync(author, AccountTestHost.Email());
            var account = (await registration.Content.ReadFromJsonAsync<AccountResponse>(token))!;
            await AccountTestHost.SetRoleAsync(factory, account.Id, AccountRole.Setter);
            await AccountTestHost.CsrfAsync(author);

            var create = await author.PostAsJsonAsync(
                "/api/authoring/problems/", new CreateProblemRequest("A + B", "Print the sum."), token);
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            problemId = (await create.Content.ReadFromJsonAsync<ProblemDraftResponse>(token))!.Id;

            var configured = await author.PutAsJsonAsync($"/api/authoring/problems/{problemId}/draft",
                new UpdateProblemDraftRequest("A + B", "Print the sum.", 1000, 64, "Exact",
                    [new ProblemSampleRequest("1 2\n", "3\n")]), token);
            Assert.Equal(HttpStatusCode.OK, configured.StatusCode);

            using (var package = ZipPackage(("input.txt", "hidden-input-secret"), ("output.txt", "hidden-output-secret")))
            {
                var uploaded = await author.PostAsync($"/api/authoring/problems/{problemId}/draft/tests", package, token);
                Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
                var uploadBody = await uploaded.Content.ReadAsStringAsync(token);
                Assert.DoesNotContain("hidden-input-secret", uploadBody, StringComparison.Ordinal);
                Assert.DoesNotContain("tests/", uploadBody, StringComparison.Ordinal);
            }

            var firstPublish = await author.PostAsJsonAsync($"/api/authoring/problems/{problemId}/publish",
                new PublishProblemRequest(true, true, true), token);
            Assert.Equal(HttpStatusCode.Created, firstPublish.StatusCode);
            var first = (await firstPublish.Content.ReadFromJsonAsync<PublishedProblemResponse>(token))!;
            firstVersionId = first.VersionId;
            Assert.Equal(1, first.VersionNumber);
            Assert.Equal(1, first.PrivateTestCount);

            var changed = await author.PutAsJsonAsync($"/api/authoring/problems/{problemId}/draft",
                new UpdateProblemDraftRequest("A + B, revised", "Print a revised sum.", 2000, 128, "Exact",
                    [new ProblemSampleRequest("2 3\n", "5\n")]), token);
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
            var secondPublish = await author.PostAsJsonAsync($"/api/authoring/problems/{problemId}/publish",
                new PublishProblemRequest(true, true, true), token);
            Assert.Equal(HttpStatusCode.Created, secondPublish.StatusCode);
            var second = (await secondPublish.Content.ReadFromJsonAsync<PublishedProblemResponse>(token))!;
            secondVersionId = second.VersionId;
            Assert.Equal(2, second.VersionNumber);

            var historical = await author.GetFromJsonAsync<PublishedProblemResponse>($"/api/problems/versions/{firstVersionId}", token);
            Assert.Equal("A + B", historical!.Title);
            Assert.Equal(1000, historical.TimeLimitMilliseconds);
            Assert.Equal("1 2\n", historical.Samples.Single().Input);

            var latest = await author.GetFromJsonAsync<PublishedProblemResponse>($"/api/problems/{problemId}", token);
            Assert.Equal(secondVersionId, latest!.VersionId);
            Assert.Equal("A + B, revised", latest.Title);
            var publicJson = await (await author.GetAsync($"/api/problems/versions/{secondVersionId}", token)).Content.ReadAsStringAsync(token);
            Assert.DoesNotContain("hidden-input-secret", publicJson, StringComparison.Ordinal);
            Assert.DoesNotContain("hidden-output-secret", publicJson, StringComparison.Ordinal);
            Assert.DoesNotContain("artifact", publicJson, StringComparison.OrdinalIgnoreCase);

            using var publicReader = AccountTestHost.Client(factory);
            var catalogResponse = await publicReader.GetAsync("/api/problems/?page=1&pageSize=100", token);
            Assert.Equal(HttpStatusCode.OK, catalogResponse.StatusCode);
            var catalog = (await catalogResponse.Content.ReadFromJsonAsync<PublishedProblemPageResponse>(token))!;
            var catalogItem = Assert.Single(catalog.Items, item => item.ProblemId == problemId);
            Assert.Equal(secondVersionId, catalogItem.VersionId);
            Assert.Equal("A + B, revised", catalogItem.Title);

            var archive = await author.PostAsync($"/api/authoring/problems/{problemId}/archive", null, token);
            Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await author.GetAsync($"/api/problems/{problemId}", token)).StatusCode);
            var historyAfterArchive = await author.GetFromJsonAsync<PublishedProblemResponse>($"/api/problems/versions/{firstVersionId}", token);
            Assert.Equal(firstVersionId, historyAfterArchive!.VersionId);
            var archivedCatalog = await publicReader.GetFromJsonAsync<PublishedProblemPageResponse>(
                "/api/problems/?page=1&pageSize=100", token);
            Assert.DoesNotContain(archivedCatalog!.Items, item => item.ProblemId == problemId);
        }

        await using var restarted = AccountTestHost.Factory();
        using var reader = AccountTestHost.Client(restarted);
        var persisted = await reader.GetFromJsonAsync<PublishedProblemResponse>($"/api/problems/versions/{secondVersionId}", token);
        Assert.Equal("A + B, revised", persisted!.Title);
        await using var scope = restarted.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MinosDbContext>();
        Assert.Equal(2, await database.PublishedProblemVersions.CountAsync(version => version.ProblemId == problemId, token));
    }

    [Fact]
    public async Task Publication_requires_complete_configuration_and_verified_private_artifacts()
    {
        await using var factory = AccountTestHost.Factory();
        using var author = AccountTestHost.Client(factory);
        var token = TestContext.Current.CancellationToken;
        var registration = await AccountTestHost.RegisterAsync(author, AccountTestHost.Email());
        var account = (await registration.Content.ReadFromJsonAsync<AccountResponse>(token))!;
        await AccountTestHost.SetRoleAsync(factory, account.Id, AccountRole.Setter);
        await AccountTestHost.CsrfAsync(author);
        var create = await author.PostAsJsonAsync(
            "/api/authoring/problems/", new CreateProblemRequest("Incomplete", "Statement"), token);
        var id = (await create.Content.ReadFromJsonAsync<ProblemDraftResponse>(token))!.Id;

        var publish = await author.PostAsJsonAsync($"/api/authoring/problems/{id}/publish",
            new PublishProblemRequest(true, true, true), token);
        Assert.Equal(HttpStatusCode.BadRequest, publish.StatusCode);
        Assert.Equal("application/problem+json", publish.Content.Headers.ContentType?.MediaType);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MinosDbContext>();
        Assert.False(await database.PublishedProblemVersions.AnyAsync(version => version.ProblemId == id, token));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Unsafe_and_oversized_test_packages_are_rejected_without_metadata(bool oversized)
    {
        await using var factory = AccountTestHost.Factory();
        using var author = AccountTestHost.Client(factory);
        var token = TestContext.Current.CancellationToken;
        var registration = await AccountTestHost.RegisterAsync(author, AccountTestHost.Email());
        var account = (await registration.Content.ReadFromJsonAsync<AccountResponse>(token))!;
        await AccountTestHost.SetRoleAsync(factory, account.Id, AccountRole.Setter);
        await AccountTestHost.CsrfAsync(author);
        var create = await author.PostAsJsonAsync(
            "/api/authoring/problems/", new CreateProblemRequest("Package", "Statement"), token);
        var id = (await create.Content.ReadFromJsonAsync<ProblemDraftResponse>(token))!.Id;

        using var package = oversized
            ? ZipPackage(("input.txt", new string('x', checked((int)ProblemArtifactReference.MaximumBytes + 1))), ("output.txt", "ok"))
            : ZipPackage(("../input.txt", "escape"), ("output.txt", "ok"));
        var result = await author.PostAsync($"/api/authoring/problems/{id}/draft/tests", package, token);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        var draft = await author.GetFromJsonAsync<ProblemDraftResponse>($"/api/authoring/problems/{id}/draft", token);
        Assert.Empty(draft!.TestCases);
    }

    [Fact]
    public async Task Symbolic_link_test_package_entries_are_rejected()
    {
        await using var factory = AccountTestHost.Factory();
        using var author = AccountTestHost.Client(factory);
        var token = TestContext.Current.CancellationToken;
        var registration = await AccountTestHost.RegisterAsync(author, AccountTestHost.Email());
        var account = (await registration.Content.ReadFromJsonAsync<AccountResponse>(token))!;
        await AccountTestHost.SetRoleAsync(factory, account.Id, AccountRole.Setter);
        await AccountTestHost.CsrfAsync(author);
        var create = await author.PostAsJsonAsync(
            "/api/authoring/problems/", new CreateProblemRequest("Links", "Statement"), token);
        var id = (await create.Content.ReadFromJsonAsync<ProblemDraftResponse>(token))!.Id;

        using var package = SymlinkZipPackage();
        var result = await author.PostAsync($"/api/authoring/problems/{id}/draft/tests", package, token);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        var draft = await author.GetFromJsonAsync<ProblemDraftResponse>($"/api/authoring/problems/{id}/draft", token);
        Assert.Empty(draft!.TestCases);
    }

    [Fact]
    public async Task Concurrent_publication_creates_only_one_next_version()
    {
        await using var factory = AccountTestHost.Factory();
        using var client = AccountTestHost.Client(factory);
        var token = TestContext.Current.CancellationToken;
        var registration = await AccountTestHost.RegisterAsync(client, AccountTestHost.Email());
        var owner = (await registration.Content.ReadFromJsonAsync<AccountResponse>(token))!.Id;
        await AccountTestHost.SetRoleAsync(factory, owner, AccountRole.Setter);

        var problem = Problem.Create(owner, "Concurrent", "Statement", DateTimeOffset.UtcNow);
        problem.ConfigureExecution(1000, 64, BuiltInChecker.Exact, DateTimeOffset.UtcNow);
        problem.ReplaceSamples([new("1", "1")], DateTimeOffset.UtcNow);
        problem.AddTestCase(Artifact("input.bin"), Artifact("output.bin"), DateTimeOffset.UtcNow);
        await using (var seed = factory.Services.CreateAsyncScope())
        {
            var database = seed.ServiceProvider.GetRequiredService<MinosDbContext>();
            database.Problems.Add(problem);
            await database.SaveChangesAsync(token);
        }

        await using var firstScope = factory.Services.CreateAsyncScope();
        await using var secondScope = factory.Services.CreateAsyncScope();
        var firstDatabase = firstScope.ServiceProvider.GetRequiredService<MinosDbContext>();
        var secondDatabase = secondScope.ServiceProvider.GetRequiredService<MinosDbContext>();
        var firstProblem = await LoadProblemAsync(firstDatabase, problem.Id, token);
        var secondProblem = await LoadProblemAsync(secondDatabase, problem.Id, token);
        var firstVersion = firstProblem.Publish(owner, new(true, true, true), DateTimeOffset.UtcNow);
        var secondVersion = secondProblem.Publish(owner, new(true, true, true), DateTimeOffset.UtcNow);
        firstDatabase.PublishedProblemVersions.Add(firstVersion);
        secondDatabase.PublishedProblemVersions.Add(secondVersion);

        var outcomes = await Task.WhenAll(SaveAsync(firstDatabase, token), SaveAsync(secondDatabase, token));
        Assert.Single(outcomes, failure => failure is null);
        Assert.Single(outcomes, failure => failure is DbUpdateConcurrencyException or DbUpdateException);
        await using var verification = factory.Services.CreateAsyncScope();
        var verified = verification.ServiceProvider.GetRequiredService<MinosDbContext>();
        Assert.Equal(1, await verified.PublishedProblemVersions.CountAsync(version => version.ProblemId == problem.Id, token));
        Assert.Equal(1, (await verified.Problems.SingleAsync(candidate => candidate.Id == problem.Id, token)).LatestPublishedVersion);
    }

    [Fact]
    public async Task PostgreSQL_constraints_reject_invalid_problem_limits()
    {
        var token = TestContext.Current.CancellationToken;
        await using var connection = new NpgsqlConnection(PostgresPrivilegeTests.Connection("ConnectionStrings__Minos"));
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        var accountId = Guid.NewGuid();
        await using (var account = new NpgsqlCommand(
            "INSERT INTO accounts.accounts (id,display_name,created_at_utc,is_disabled,role) VALUES (@id,'Owner',now(),false,1)", connection, transaction))
        {
            account.Parameters.AddWithValue("id", accountId);
            await account.ExecuteNonQueryAsync(token);
        }
        var problemId = Guid.NewGuid();
        await using (var invalidLimit = new NpgsqlCommand(
            "INSERT INTO problems.problems (id,owner_account_id,title,statement,time_limit_ms,memory_limit_mb,checker,latest_published_version,is_archived,revision,created_at_utc,updated_at_utc) VALUES (@id,@owner,'Title','Statement',99,64,0,0,false,0,now(),now())",
            connection, transaction))
        {
            invalidLimit.Parameters.AddWithValue("id", problemId);
            invalidLimit.Parameters.AddWithValue("owner", accountId);
            Assert.Equal(PostgresErrorCodes.CheckViolation,
                (await Assert.ThrowsAsync<PostgresException>(() => invalidLimit.ExecuteNonQueryAsync(token))).SqlState);
        }
    }

    [Fact]
    public async Task PostgreSQL_constraints_reject_unsafe_artifact_keys()
    {
        var token = TestContext.Current.CancellationToken;
        await using var connection = new NpgsqlConnection(PostgresPrivilegeTests.Connection("ConnectionStrings__Minos"));
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        var (accountId, problemId) = await SeedProblemAsync(connection, transaction, token);
        await using var invalidArtifact = new NpgsqlCommand(
            "INSERT INTO problems.problem_test_cases (id,\"order\",input_artifact_key,input_sha256,input_size_bytes,output_artifact_key,output_sha256,output_size_bytes,problem_id) VALUES (gen_random_uuid(),1,'../input.bin',@sha,1,@output,@sha,1,@problem)",
            connection, transaction);
        invalidArtifact.Parameters.AddWithValue("sha", new string('a', 64));
        invalidArtifact.Parameters.AddWithValue("output", $"tests/{Guid.NewGuid():N}/output.bin");
        invalidArtifact.Parameters.AddWithValue("problem", problemId);
        Assert.Equal(PostgresErrorCodes.CheckViolation,
            (await Assert.ThrowsAsync<PostgresException>(() => invalidArtifact.ExecuteNonQueryAsync(token))).SqlState);
    }

    [Fact]
    public async Task PostgreSQL_constraints_reject_incomplete_publication_checklists()
    {
        var token = TestContext.Current.CancellationToken;
        await using var connection = new NpgsqlConnection(PostgresPrivilegeTests.Connection("ConnectionStrings__Minos"));
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        var (accountId, problemId) = await SeedProblemAsync(connection, transaction, token);
        await using var incompleteVersion = new NpgsqlCommand(
            "INSERT INTO problems.published_problem_versions (id,problem_id,version_number,title,statement,time_limit_ms,memory_limit_mb,checker,private_test_count,published_by_account_id,published_at_utc,statement_reviewed,samples_verified,reference_solution_checked) VALUES (gen_random_uuid(),@problem,1,'Title','Statement',1000,64,0,1,@account,now(),true,true,false)",
            connection, transaction);
        incompleteVersion.Parameters.AddWithValue("problem", problemId);
        incompleteVersion.Parameters.AddWithValue("account", accountId);
        Assert.Equal(PostgresErrorCodes.CheckViolation,
            (await Assert.ThrowsAsync<PostgresException>(() => incompleteVersion.ExecuteNonQueryAsync(token))).SqlState);
    }

    [Fact]
    public async Task Abandoned_unreferenced_artifacts_are_cleaned_after_the_safety_window()
    {
        var root = Environment.GetEnvironmentVariable("Minos__Artifacts__Root")
            ?? throw new InvalidOperationException("Run scripts/check-foundation.sh; Minos__Artifacts__Root is required.");
        var abandoned = Path.Combine(root, "problem-artifacts", "tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(abandoned);
        await File.WriteAllTextAsync(Path.Combine(abandoned, "input.bin"), "orphan", TestContext.Current.CancellationToken);
        Directory.SetLastWriteTimeUtc(abandoned, DateTime.UtcNow.AddDays(-2));

        await using var factory = AccountTestHost.Factory();
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ProblemArtifactMaintenanceUseCases>()
            .CleanupAbandonedAsync(TestContext.Current.CancellationToken);

        Assert.False(Directory.Exists(abandoned));
    }

    private static async Task<Problem> LoadProblemAsync(MinosDbContext database, Guid id, CancellationToken token) =>
        await database.Problems.Include(candidate => candidate.Samples).Include(candidate => candidate.TestCases)
            .SingleAsync(candidate => candidate.Id == id, token);

    private static async Task<(Guid AccountId, Guid ProblemId)> SeedProblemAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken token)
    {
        var accountId = Guid.NewGuid();
        await using (var account = new NpgsqlCommand(
            "INSERT INTO accounts.accounts (id,display_name,created_at_utc,is_disabled,role) VALUES (@id,'Owner',now(),false,1)", connection, transaction))
        {
            account.Parameters.AddWithValue("id", accountId);
            await account.ExecuteNonQueryAsync(token);
        }
        var problemId = Guid.NewGuid();
        await using (var problem = new NpgsqlCommand(
            "INSERT INTO problems.problems (id,owner_account_id,title,statement,time_limit_ms,memory_limit_mb,checker,latest_published_version,is_archived,revision,created_at_utc,updated_at_utc) VALUES (@id,@owner,'Title','Statement',1000,64,0,0,false,0,now(),now())",
            connection, transaction))
        {
            problem.Parameters.AddWithValue("id", problemId);
            problem.Parameters.AddWithValue("owner", accountId);
            await problem.ExecuteNonQueryAsync(token);
        }
        return (accountId, problemId);
    }

    private static async Task<Exception?> SaveAsync(MinosDbContext database, CancellationToken token)
    {
        try { await database.SaveChangesAsync(token); return null; }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException or DbUpdateException) { return exception; }
    }

    private static ProblemArtifactReference Artifact(string name) =>
        ProblemArtifactReference.Create($"tests/{Guid.NewGuid():N}/{name}", new string('a', 64), 1);

    private static ByteArrayContent ZipPackage(params (string Name, string Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            foreach (var item in entries)
            {
                var entry = archive.CreateEntry(item.Name, CompressionLevel.Fastest);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(item.Content);
            }
        }
        var content = new ByteArrayContent(stream.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        return content;
    }

    private static ByteArrayContent SymlinkZipPackage()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            var input = archive.CreateEntry("input.txt");
            input.ExternalAttributes = unchecked((int)0xA0000000);
            using (var writer = new StreamWriter(input.Open())) writer.Write("output.txt");
            var output = archive.CreateEntry("output.txt");
            using var outputWriter = new StreamWriter(output.Open());
            outputWriter.Write("answer");
        }
        var content = new ByteArrayContent(stream.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        return content;
    }
}
