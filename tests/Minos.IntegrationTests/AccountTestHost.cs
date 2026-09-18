using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Minos.Api.Accounts;
using Xunit;

namespace Minos.IntegrationTests;

internal static class AccountTestHost
{
    internal const string Password = "correct horse battery staple";
    internal static WebApplicationFactory<Program> Factory() => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        { ["Minos:EnableDevelopmentEndpoints"] = "true" }));
    });
    internal static HttpClient Client(WebApplicationFactory<Program> factory, bool cookies = true) => factory.CreateClient(new()
    { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = cookies });
    internal static async Task CsrfAsync(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<CsrfResponse>("/accounts/csrf", TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", response!.RequestToken);
    }
    internal static async Task<HttpResponseMessage> RegisterAsync(HttpClient client, string email)
    {
        await CsrfAsync(client);
        return await client.PostAsJsonAsync("/accounts/register", new { email, password = Password, displayName = "Developer" }, TestContext.Current.CancellationToken);
    }
    internal static async Task LoginAsync(HttpClient client, string email, string password = Password)
    {
        await CsrfAsync(client);
        var result = await client.PostAsJsonAsync("/accounts/login", new { email, password }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        await CsrfAsync(client);
    }
    internal static string Email() => $"{Guid.NewGuid():N}@example.test";
    internal static string Cookie(HttpResponseMessage response) => response.Headers.GetValues("Set-Cookie")
        .Single(value => value.StartsWith("__Host-Minos.Session=", StringComparison.Ordinal)).Split(';')[0];
}
