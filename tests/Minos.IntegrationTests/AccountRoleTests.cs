using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Minos.Api.Accounts;
using Minos.Api.Problems;
using Minos.Application.Accounts;
using Minos.Domain.Accounts;
using Minos.Infrastructure.Persistence;
using Xunit;

namespace Minos.IntegrationTests;

public sealed class AccountRoleTests
{
    [Fact]
    public async Task Only_an_administrator_can_assign_a_named_role_and_the_change_revokes_target_sessions()
    {
        await using var factory = AccountTestHost.Factory();
        using var administrator = AccountTestHost.Client(factory);
        using var target = AccountTestHost.Client(factory);
        var token = TestContext.Current.CancellationToken;
        var adminRegistration = await AccountTestHost.RegisterAsync(administrator, AccountTestHost.Email());
        var admin = (await adminRegistration.Content.ReadFromJsonAsync<AccountResponse>(token))!;
        var targetEmail = AccountTestHost.Email();
        var targetRegistration = await AccountTestHost.RegisterAsync(target, targetEmail);
        var targetAccount = (await targetRegistration.Content.ReadFromJsonAsync<AccountResponse>(token))!;
        await AccountTestHost.CsrfAsync(administrator);

        var denied = await administrator.PutAsJsonAsync($"/accounts/{targetAccount.Id}/role",
            new { role = "Setter" }, token);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        await AccountTestHost.SetRoleAsync(factory, admin.Id, AccountRole.Administrator);
        var invalid = await administrator.PutAsJsonAsync($"/accounts/{targetAccount.Id}/role",
            new { role = "1" }, token);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var assigned = await administrator.PutAsJsonAsync($"/accounts/{targetAccount.Id}/role",
            new { role = "Setter" }, token);
        Assert.Equal(HttpStatusCode.NoContent, assigned.StatusCode);

        await AccountTestHost.CsrfAsync(target);
        var expired = await target.PostAsJsonAsync(
            "/api/authoring/problems/", new CreateProblemRequest("Title", "Statement"), token);
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        await AccountTestHost.LoginAsync(target, targetEmail);
        var created = await target.PostAsJsonAsync(
            "/api/authoring/problems/", new CreateProblemRequest("Title", "Statement"), token);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    [Fact]
    public async Task Trusted_provisioning_can_create_the_first_administrator_without_an_HTTP_backdoor()
    {
        await using var factory = AccountTestHost.Factory();
        using var client = AccountTestHost.Client(factory);
        var token = TestContext.Current.CancellationToken;
        var registration = await AccountTestHost.RegisterAsync(client, AccountTestHost.Email());
        var account = (await registration.Content.ReadFromJsonAsync<AccountResponse>(token))!;
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TrustedAccountProvisioningUseCases>()
            .ProvisionAdministratorAsync(account.Id, token);
        var database = scope.ServiceProvider.GetRequiredService<MinosDbContext>();
        Assert.Equal(AccountRole.Administrator,
            await database.Accounts.Where(candidate => candidate.Id == account.Id).Select(candidate => candidate.Role).SingleAsync(token));
    }
}
