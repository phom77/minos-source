using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Minos.Domain.Accounts;
using Minos.Infrastructure.Accounts;

namespace Minos.Infrastructure.Persistence.Configurations.Accounts;

internal sealed class CredentialUserConfiguration : IEntityTypeConfiguration<CredentialUser>
{
    public void Configure(EntityTypeBuilder<CredentialUser> credential)
    {
        credential.ToTable("credentials", "accounts");
        credential.Property(x => x.Id).ValueGeneratedNever();
        credential.Property(x => x.Email).IsRequired();
        credential.Property(x => x.NormalizedEmail).IsRequired();
        credential.HasIndex(x => x.NormalizedEmail).IsUnique();
        credential.HasOne<Account>().WithOne().HasForeignKey<CredentialUser>(x => x.Id).OnDelete(DeleteBehavior.Restrict);
    }
}
