using Minos.Domain;
using Minos.Domain.Accounts;
using Xunit;

namespace Minos.Domain.Tests;

public sealed class AccountTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("Name\ncontrol")]
    public void Invalid_profile_is_rejected(string? name) =>
        Assert.Throws<DomainValidationException>(() => Account.Create(name, DateTimeOffset.UtcNow));

    [Fact]
    public void Account_protects_profile_utc_and_disabled_state()
    {
        var account = Account.Create("  Khanh  ", DateTimeOffset.UtcNow);
        Assert.NotEqual(Guid.Empty, account.Id);
        Assert.Equal("Khanh", account.DisplayName);
        Assert.Throws<DomainValidationException>(() => account.Rename(new string('x', 81)));
        account.Rename(string.Concat(Enumerable.Repeat("🧩", 40)));
        Assert.Throws<DomainValidationException>(() => account.Rename(string.Concat(Enumerable.Repeat("🧩", 41))));
        account.Rename("New name");
        account.Disable();
        account.Disable();
        Assert.True(account.IsDisabled);
        Assert.Throws<DomainValidationException>(() => account.Rename("Another name"));
        Assert.Throws<DomainValidationException>(() => Account.Create("Name", DateTimeOffset.Now.ToOffset(TimeSpan.FromHours(7))));
    }
}
