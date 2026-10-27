using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Minos.Api.Accounts;
using Minos.Api.Problems;
using Minos.Application.Accounts;
using Minos.Application.Evaluation;
using Minos.Application.Problems;
using Minos.Domain.Accounts;
using Minos.Domain.Evaluation;
using Minos.Domain.Problems;
using Npgsql;
using Xunit;

namespace Minos.IntegrationTests;

public sealed class ProblemLibraryTests
{
    [Fact]
    public async Task Filter_groups_exclusions_and_topic_counts_use_public_data_and_preserve_account_scope()
    {
        var token = TestContext.Current.CancellationToken;
        await using var factory = AccountTestHost.Factory();
        using var owner = AccountTestHost.Client(factory);
        var account = (await (await AccountTestHost.RegisterAsync(owner, AccountTestHost.Email())).Content.ReadFromJsonAsync<AccountResponse>(token))!;
        using var guest = AccountTestHost.Client(factory);
        var baseline = (await guest.GetFromJsonAsync<ProblemTopicCountResponse[]>("/api/problems/topics", token))!.Single(topic => topic.Slug == "math").ProblemCount;
        await using var database = PostgresPrivilegeTests.Database(PostgresPrivilegeTests.Connection("MINOS_TEST_MIGRATION_CONNECTION"));
        var prefix = "filter-group-" + Guid.NewGuid().ToString("N");
        var seeded = await SeedAsync(database, prefix);
        await AddResultAsync(database, account.Id, seeded[0], true);
        var url = $"/api/problems?page=1&pageSize=20&q={prefix}";
        foreach (var (filter, expected) in new (string, int)[] {
            ("difficulty=Hard&topic=math", 1), ("difficulty=Hard&topic=math&match=Any", 3),
            ("difficulty=Hard&difficultyIsNot=true&topic=math", 2),
            ("difficulty=Hard&difficultyIsNot=true&topic=math&match=Any", 4),
            ("difficulty=Unclassified&difficultyIsNot=true", 3), ("topic=math&topicIsNot=true", 1),
            ("status=Solved&statusIsNot=true", 3), ("status=Solved&difficulty=Hard&match=Any", 2),
            ("status=Solved&statusIsNot=true&difficulty=Hard", 1), ("match=Any", 4) })
        {
            var result = (await owner.GetFromJsonAsync<PublishedProblemPageResponse>(url + "&" + filter, token))!;
            Assert.Equal(expected, result.Total);
            Assert.All(result.Items, item => Assert.Contains(prefix, item.Title, StringComparison.Ordinal)); // Search remains AND with either group.
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync(url + "&difficulty=Easy&status=Solved&match=Any", token)).StatusCode);
        foreach (var filter in new[] { "match=Unknown", "difficultyIsNot=true", "topicIsNot=true", "statusIsNot=true", "difficulty=Easy&difficultyIsNot=maybe" })
            Assert.Equal(HttpStatusCode.BadRequest, (await guest.GetAsync(url + "&" + filter, token)).StatusCode);
        var topics = (await guest.GetFromJsonAsync<ProblemTopicCountResponse[]>("/api/problems/topics", token))!;
        Assert.Equal(baseline + 3, topics.Single(topic => topic.Slug == "math").ProblemCount);
        seeded[0].Problem.Archive(DateTimeOffset.UtcNow);
        database.Problems.Add(Problem.Create(seeded[0].Problem.OwnerAccountId, "Private draft", "Private content", DateTimeOffset.UtcNow));
        await database.SaveChangesAsync(token);
        topics = (await guest.GetFromJsonAsync<ProblemTopicCountResponse[]>("/api/problems/topics", token))!;
        Assert.Equal(baseline + 2, topics.Single(topic => topic.Slug == "math").ProblemCount);
        Assert.All(topics, topic => Assert.True(topic.ProblemCount >= 0));
    }

    [Fact]
    public async Task Search_filters_sorting_and_numbers_use_the_full_published_library_without_draft_leaks()
    {
        var token = TestContext.Current.CancellationToken;
        var prefix = "library-" + Guid.NewGuid().ToString("N");
        await using var factory = AccountTestHost.Factory();
        using var reader = AccountTestHost.Client(factory);
        await using var database = PostgresPrivilegeTests.Database(PostgresPrivilegeTests.Connection("MINOS_TEST_MIGRATION_CONNECTION"));
        var seeded = await SeedAsync(database, prefix);
        var all = (await reader.GetFromJsonAsync<PublishedProblemPageResponse>($"/api/problems?page=1&pageSize=1&q={prefix}", token))!;
        Assert.Equal(4, all.Total);
        Assert.Single(all.Items);
        Assert.Null(all.Progress);
        Assert.All(all.Items, item => { Assert.True(item.PublicNumber > 0); Assert.Null(item.SolveStatus); });
        var filtered = (await reader.GetFromJsonAsync<PublishedProblemPageResponse>($"/api/problems?page=1&pageSize=1&q={prefix}&difficulty=Hard&topic=math", token))!;
        Assert.Equal(seeded[2].Problem.Id, Assert.Single(filtered.Items).ProblemId);
        Assert.Equal(1, filtered.Total);
        var number = seeded[2].Problem.PublicNumber;
        var numberMatch = (await reader.GetFromJsonAsync<PublishedProblemPageResponse>($"/api/problems?page=1&pageSize=20&q=%23{number}", token))!;
        Assert.Equal(seeded[2].Problem.Id, Assert.Single(numberMatch.Items).ProblemId);
        var sorted = (await reader.GetFromJsonAsync<PublishedProblemPageResponse>($"/api/problems?page=1&pageSize=20&q={prefix}&sort=DifficultyDescending", token))!;
        Assert.Equal(new string?[] { "Hard", "Medium", "Easy", null }, sorted.Items.Select(item => item.Classification!.Difficulty));
        var literal = (await reader.GetFromJsonAsync<PublishedProblemPageResponse>($"/api/problems?page=1&pageSize=20&q={prefix}%5F%25", token))!;
        Assert.Equal(seeded[0].Problem.Id, Assert.Single(literal.Items).ProblemId);
        seeded[2].Problem.EditContent("Unpublished replacement", "Private draft", DateTimeOffset.UtcNow);
        await database.SaveChangesAsync(token);
        var original = (await reader.GetFromJsonAsync<PublishedProblemPageResponse>($"/api/problems?page=1&pageSize=20&q=%23{number}", token))!;
        Assert.Equal(number, original.Items[0].PublicNumber);
        Assert.Equal(seeded[2].Version.Title, original.Items[0].Title);
        Assert.DoesNotContain("Private draft", await reader.GetStringAsync($"/api/problems?page=1&pageSize=20&q={prefix}", token), StringComparison.Ordinal);
        seeded[3].Problem.Archive(DateTimeOffset.UtcNow);
        await database.SaveChangesAsync(token);
        Assert.Equal(3, (await reader.GetFromJsonAsync<PublishedProblemPageResponse>($"/api/problems?page=1&pageSize=20&q={prefix}", token))!.Total);
        var response = await reader.GetAsync("/api/problems?page=1&pageSize=20", token);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.True(response.Headers.CacheControl.Private);
        foreach (var query in new[] { "difficulty=999", "status=WrongAnswer", "sort=Unknown", "topic=Bad%20Tag", "q=" + new string('a', 121), "page=2147483647&pageSize=100" })
            Assert.Equal(HttpStatusCode.BadRequest, (await reader.GetAsync("/api/problems?page=1&pageSize=20&" + query, token)).StatusCode);
    }

    [Fact]
    public async Task Progress_is_account_owned_accepted_on_any_version_and_enforced_without_http()
    {
        var token = TestContext.Current.CancellationToken;
        var prefix = "progress-" + Guid.NewGuid().ToString("N");
        await using var factory = AccountTestHost.Factory();
        using var owner = AccountTestHost.Client(factory);
        var account = (await (await AccountTestHost.RegisterAsync(owner, AccountTestHost.Email())).Content.ReadFromJsonAsync<AccountResponse>(token))!;
        using var other = AccountTestHost.Client(factory);
        await AccountTestHost.RegisterAsync(other, AccountTestHost.Email());
        using var guest = AccountTestHost.Client(factory);
        await using var database = PostgresPrivilegeTests.Database(PostgresPrivilegeTests.Connection("MINOS_TEST_MIGRATION_CONNECTION"));
        var seeded = await SeedAsync(database, prefix);
        await AddResultAsync(database, account.Id, seeded[0], true);
        await AddResultAsync(database, account.Id, seeded[0], false); // A later failed submission does not erase success.
        await AddResultAsync(database, account.Id, seeded[1], false);
        seeded[0].Problem.EditContent(prefix + " revised", "New content", DateTimeOffset.UtcNow);
        database.PublishedProblemVersions.Add(seeded[0].Problem.Publish(seeded[0].Problem.OwnerAccountId, new(true, true, true), DateTimeOffset.UtcNow));
        await database.SaveChangesAsync(token);
        var mine = (await owner.GetFromJsonAsync<PublishedProblemPageResponse>($"/api/problems?page=1&pageSize=20&q={prefix}", token))!;
        Assert.Equal(1, mine.Progress!.Solved);
        Assert.Equal(2, mine.Progress.Attempted);
        Assert.True(mine.Progress.Total >= 4);
        Assert.Equal("Solved", mine.Items.Single(item => item.ProblemId == seeded[0].Problem.Id).SolveStatus);
        Assert.Equal("Attempted", mine.Items.Single(item => item.ProblemId == seeded[1].Problem.Id).SolveStatus);
        Assert.Equal("NotAttempted", mine.Items.Single(item => item.ProblemId == seeded[2].Problem.Id).SolveStatus);
        var solved = (await owner.GetFromJsonAsync<PublishedProblemPageResponse>($"/api/problems?page=1&pageSize=20&q={prefix}&status=Solved", token))!;
        Assert.Single(solved.Items);
        Assert.Equal(mine.Progress, solved.Progress);
        Assert.Equal(1, (await owner.GetFromJsonAsync<PublishedProblemPageResponse>($"/api/problems?page=1&pageSize=20&q={prefix}&status=Attempted", token))!.Total);
        Assert.Equal(2, (await owner.GetFromJsonAsync<PublishedProblemPageResponse>($"/api/problems?page=1&pageSize=20&q={prefix}&status=NotAttempted", token))!.Total);
        var isolated = (await other.GetFromJsonAsync<PublishedProblemPageResponse>($"/api/problems?page=1&pageSize=20&q={prefix}&status=Solved&accountId={account.Id}", token))!;
        Assert.Empty(isolated.Items);
        Assert.Equal(0, isolated.Progress!.Solved);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/problems?page=1&pageSize=20&status=Solved", token)).StatusCode);
        await database.Accounts.Where(value => value.Id == account.Id).ExecuteUpdateAsync(update => update.SetProperty(value => value.IsDisabled, true), token);
        await using var scope = factory.Services.CreateAsyncScope();
        var current = new TestCurrentAccount(account.Id);
        var cases = new ProblemCatalogUseCases(scope.ServiceProvider.GetRequiredService<IProblemRepository>(), current,
            new(scope.ServiceProvider.GetRequiredService<IAccountStore>(), current, TimeProvider.System));
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => cases.ListAsync(new(Status: ProblemSolveStatus.Solved), token));
        current.AccountId = null;
        await Assert.ThrowsAsync<AccountAuthenticationException>(() => cases.ListAsync(new(Status: ProblemSolveStatus.Solved), token));
        Assert.Null((await cases.ListAsync(1, 20, token)).Progress);
    }

    private static async Task<(Problem Problem, PublishedProblemVersion Version)[]> SeedAsync(Minos.Infrastructure.Persistence.MinosDbContext database, string prefix)
    {
        var token = TestContext.Current.CancellationToken;
        var author = Account.Create("Library fixture", DateTimeOffset.UtcNow);
        author.AssignRole(AccountRole.Setter);
        database.Accounts.Add(author);
        var result = new List<(Problem, PublishedProblemVersion)>();
        for (var i = 0; i < 4; i++)
        {
            var problem = Problem.Create(author.Id, prefix + (i == 0 ? "_%" : "-" + i), "Published content", DateTimeOffset.UtcNow);
            problem.ConfigureExecution(1000, 64, BuiltInChecker.Exact, DateTimeOffset.UtcNow);
            problem.ReplaceSamples([new("1", "1")], DateTimeOffset.UtcNow);
            problem.AddTestCase(Artifact("input.bin"), Artifact("output.bin"), DateTimeOffset.UtcNow);
            var version = problem.Publish(author.Id, new(true, true, true), DateTimeOffset.UtcNow);
            database.Problems.Add(problem);
            database.PublishedProblemVersions.Add(version);
            if (i < 3) database.ProblemClassifications.Add(ProblemClassification.Create(problem.Id, version.Id, (ProblemDifficulty)(i + 1),
                DifficultyOrigin.Manual, null, [new("math", TopicOrigin.Manual)], DateTimeOffset.UtcNow));
            result.Add((problem, version));
        }
        await database.SaveChangesAsync(token);
        return result.ToArray();
    }

    private static async Task AddResultAsync(Minos.Infrastructure.Persistence.MinosDbContext database, Guid accountId,
        (Problem Problem, PublishedProblemVersion Version) item, bool accepted)
    {
        var token = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var submission = Submission.Create(accountId, item.Problem.Id, item.Version.Id, "cpp20", SupportedSubmission.ToolchainId, "int main(){}", now);
        var job = JudgeJob.Create(submission.Id, now);
        database.Submissions.Add(submission);
        database.JudgeJobs.Add(job);
        await database.SaveChangesAsync(token);
        var attempt = job.Claim("library-fixture", now);
        submission.StartAttempt(attempt.Id);
        var outcome = JudgingOutcome.Create(accepted ? SubmissionVerdict.Accepted : SubmissionVerdict.WrongAnswer, "",
            [new(1, accepted ? TestCaseResultStatus.Passed : TestCaseResultStatus.WrongAnswer, 1, 2, 4096, 0)]);
        database.JudgingAttempts.Add(attempt);
        database.SubmissionResults.Add(submission.Complete(attempt.Id, outcome, now.AddMilliseconds(1)));
        job.Complete(attempt.Id, attempt.LeaseToken, "library-fixture", now.AddMilliseconds(1));
        attempt.Succeed(now.AddMilliseconds(1));
        await database.SaveChangesAsync(token);
    }

    private static ProblemArtifactReference Artifact(string name) => ProblemArtifactReference.Create($"tests/{Guid.NewGuid():N}/{name}", new('a', 64), 1);
}
