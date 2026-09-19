using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Minos.Api.Accounts;
using Minos.Application.Problems;
using Minos.Domain.Problems;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Minos.IntegrationTests;

public sealed class FoundationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("unsupported=sentinel-value")]
    [InlineData("Host=localhost")]
    public async Task Invalid_database_configuration_fails_at_startup_without_echoing_values(string connection)
    {
        var original = Environment.GetEnvironmentVariable("ConnectionStrings__Minos");
        Environment.SetEnvironmentVariable("ConnectionStrings__Minos", connection);
        try
        {
            await using var factory = AccountTestHost.Factory();
            var failure = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
            Assert.Contains("ConnectionStrings:Minos", failure.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("sentinel-value", failure.Message, StringComparison.Ordinal);
        }
        finally { Environment.SetEnvironmentVariable("ConnectionStrings__Minos", original); }
    }

    [Theory]
    [InlineData("{broken-json")]
    [InlineData("null")]
    public async Task Invalid_json_returns_safe_problem_details(string json)
    {
        await using var factory = AccountTestHost.Factory();
        using var client = AccountTestHost.Client(factory);
        await AccountTestHost.CsrfAsync(client);
        using var body = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/accounts/register", body, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var details = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("traceId", details, StringComparison.Ordinal);
        Assert.DoesNotContain("stack", details, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Authoring_requires_authentication_and_setter_permission()
    {
        await using var factory = AccountTestHost.Factory();
        using var anonymous = AccountTestHost.Client(factory);
        await AccountTestHost.CsrfAsync(anonymous);
        var request = new { title = "Title", statement = "Statement" };
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/authoring/problems/", request, TestContext.Current.CancellationToken)).StatusCode);

        using var solver = AccountTestHost.Client(factory);
        Assert.Equal(HttpStatusCode.Created, (await AccountTestHost.RegisterAsync(solver, AccountTestHost.Email())).StatusCode);
        await AccountTestHost.CsrfAsync(solver);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await solver.PostAsJsonAsync("/api/authoring/problems/", request, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Unexpected_application_failures_return_safe_problem_details()
    {
        await using var factory = AccountTestHost.Factory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.Replace(ServiceDescriptor.Scoped<IProblemRepository, FailingRepository>())));
        using var client = AccountTestHost.Client(factory);
        var registration = await AccountTestHost.RegisterAsync(client, AccountTestHost.Email());
        var account = (await registration.Content.ReadFromJsonAsync<AccountResponse>(TestContext.Current.CancellationToken))!;
        await AccountTestHost.SetRoleAsync(factory, account.Id, Minos.Domain.Accounts.AccountRole.Setter);
        await AccountTestHost.CsrfAsync(client);
        var response = await client.GetAsync($"/api/authoring/problems/{Guid.NewGuid()}/draft", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var details = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("sensitive-sentinel", details, StringComparison.Ordinal);
        Assert.DoesNotContain("stack", details, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FailingRepository : IProblemRepository
    {
        public Task AddAsync(Problem problem, CancellationToken token) => throw new IOException("sensitive-sentinel");
        public Task<Problem?> FindForAuthoringAsync(Guid id, CancellationToken token) => throw new IOException("sensitive-sentinel");
        public Task SaveAsync(Problem problem, CancellationToken token) => throw new IOException("sensitive-sentinel");
        public Task PublishAsync(Problem problem, PublishedProblemVersion version, CancellationToken token) => throw new IOException("sensitive-sentinel");
        public Task<(IReadOnlyList<PublishedProblemSummary> Items, int Total)> ListPublishedAsync(int offset, int limit, CancellationToken token) => throw new IOException("sensitive-sentinel");
        public Task<PublishedProblemVersion?> FindLatestPublishedAsync(Guid problemId, CancellationToken token) => throw new IOException("sensitive-sentinel");
        public Task<PublishedProblemVersion?> FindPublishedVersionAsync(Guid versionId, CancellationToken token) => throw new IOException("sensitive-sentinel");
        public Task<IReadOnlySet<string>> ReferencedArtifactKeysAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
    }
}
