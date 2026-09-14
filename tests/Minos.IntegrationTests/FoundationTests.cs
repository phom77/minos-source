using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Minos.Application.Problems;
using Minos.Infrastructure.Persistence;
using Minos.Api;
using Minos.Application;
using Minos.Domain.Problems;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Minos.IntegrationTests;

public sealed class FoundationTests
{
    private static WebApplicationFactory<Program> Factory(string environment = "Development", bool enabled = true) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Minos:EnableDevelopmentEndpoints"] = enabled.ToString()
            }));
        });

    [Fact]
    public async Task Draft_survives_api_restart_and_invalid_requests_leave_no_row()
    {
        var token = TestContext.Current.CancellationToken;
        Guid id;
        await using (var first = Factory())
        {
            using var client = first.CreateClient();
            await using var scope = first.Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<MinosDbContext>();
            var before = await database.ProblemDrafts.CountAsync(token);
            var invalid = await client.PostAsJsonAsync("/_development/problem-drafts/", new CreateProblemDraftRequest(" ", "Statement"), token);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal("application/problem+json", invalid.Content.Headers.ContentType?.MediaType);
            Assert.Equal(before, await database.ProblemDrafts.CountAsync(token));
            var created = await client.PostAsJsonAsync("/_development/problem-drafts/", new CreateProblemDraftRequest("  A + B  ", "Read a and b.\nPrint their sum."), token);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var draft = await created.Content.ReadFromJsonAsync<ProblemDraftResponse>(token);
            Assert.NotNull(draft);
            Assert.Equal("A + B", draft.Title);
            Assert.Equal(TimeSpan.Zero, draft.CreatedAtUtc.Offset);
            Assert.Equal($"/_development/problem-drafts/{draft.Id}", created.Headers.Location?.OriginalString);
            id = draft.Id;
        }
        await using var restarted = Factory();
        using var restartedClient = restarted.CreateClient();
        var persisted = await restartedClient.GetFromJsonAsync<ProblemDraftResponse>($"/_development/problem-drafts/{id}", token);
        Assert.NotNull(persisted);
        Assert.Equal("Read a and b.\nPrint their sum.", persisted.Statement);
        Assert.Equal(HttpStatusCode.OK, (await restartedClient.GetAsync("/health/ready", token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await restartedClient.GetAsync($"/_development/problem-drafts/{Guid.NewGuid()}", token)).StatusCode);
    }

    [Theory]
    [InlineData("Production", true)]
    [InlineData("Development", false)]
    public async Task Development_endpoints_require_both_environment_and_opt_in(string environment, bool enabled)
    {
        await using var factory = Factory(environment, enabled);
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/_development/problem-drafts/", new CreateProblemDraftRequest("Title", "Statement"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        await using var scope = factory.Services.CreateAsyncScope();
        var useCases = scope.ServiceProvider.GetRequiredService<ProblemDraftUseCases>();
        await Assert.ThrowsAsync<AccessDeniedException>(() => useCases.FindAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("")]
    [InlineData("unsupported=sentinel-value")]
    [InlineData("Host=localhost")]
    public async Task Invalid_database_configuration_fails_at_startup_without_echoing_values(string connection)
    {
        // Exercise the bootstrap environment; test-host configuration callbacks run too late for early registration.
        var original = Environment.GetEnvironmentVariable("ConnectionStrings__Minos");
        Environment.SetEnvironmentVariable("ConnectionStrings__Minos", connection);
        try
        {
            await using var factory = Factory();
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
        await using var factory = Factory();
        using var client = factory.CreateClient();
        using var body = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/_development/problem-drafts/", body, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var details = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("traceId", details, StringComparison.Ordinal);
        Assert.DoesNotContain("stack", details, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false, HttpStatusCode.InternalServerError)]
    [InlineData(true, HttpStatusCode.Forbidden)]
    public async Task Exceptions_have_safe_http_contracts(bool denied, HttpStatusCode expected)
    {
        await using var factory = Factory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            if (denied) services.Replace(ServiceDescriptor.Singleton<IProblemDraftAccess>(new DenyAccess()));
            else services.Replace(ServiceDescriptor.Scoped<IProblemDraftRepository, FailingRepository>());
        }));
        using var client = factory.CreateClient();
        var response = await client.GetAsync($"/_development/problem-drafts/{Guid.NewGuid()}", TestContext.Current.CancellationToken);
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var details = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("traceId", details, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-sentinel", details, StringComparison.Ordinal);
        Assert.DoesNotContain("stack", details, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class DenyAccess : IProblemDraftAccess
    {
        public Task EnsureCanCreateAsync(CancellationToken cancellationToken) => throw new AccessDeniedException();
        public Task EnsureCanReadAsync(Guid draftId, CancellationToken cancellationToken) => throw new AccessDeniedException();
    }

    private sealed class FailingRepository : IProblemDraftRepository
    {
        public Task AddAsync(ProblemDraft draft, CancellationToken cancellationToken) => throw new IOException("sensitive-sentinel");
        public Task<ProblemDraft?> FindAsync(Guid id, CancellationToken cancellationToken) => throw new IOException("sensitive-sentinel");
    }
}
