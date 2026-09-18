using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Minos.Infrastructure.Persistence.Configurations.Accounts;

internal sealed class IdentityUserClaimConfiguration : IEntityTypeConfiguration<IdentityUserClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserClaim<Guid>> claim) => claim.ToTable("claims", "accounts");
}

internal sealed class ExternalLoginConfiguration : IEntityTypeConfiguration<IdentityUserLogin<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserLogin<Guid>> login)
    {
        login.ToTable("external_logins", "accounts");
        login.Property(x => x.LoginProvider).HasMaxLength(32);
        login.Property(x => x.ProviderKey).HasMaxLength(256);
        login.HasIndex(x => new { x.UserId, x.LoginProvider }).IsUnique();
    }
}

internal sealed class IdentityUserTokenConfiguration : IEntityTypeConfiguration<IdentityUserToken<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserToken<Guid>> token) => token.ToTable("tokens", "accounts");
}
