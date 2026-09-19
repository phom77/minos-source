using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Minos.Domain.Accounts;

namespace Minos.Infrastructure.Persistence.Configurations.Accounts;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> account)
    {
        account.ToTable("accounts", "accounts", table =>
        {
            table.HasCheckConstraint("ck_accounts_display_name", $"{PostgresTextConstraints.Utf16Length("display_name")} BETWEEN 1 AND 80 AND length(btrim(display_name, {PostgresTextConstraints.Whitespace})) > 0 AND display_name = btrim(display_name, {PostgresTextConstraints.Whitespace}) AND display_name !~ U&'[\\0001-\\001F\\007F-\\009F]'");
            table.HasCheckConstraint("ck_accounts_id", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("ck_accounts_role", "role BETWEEN 0 AND 2");
        });
        account.HasKey(x => x.Id);
        account.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        account.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(Account.MaximumDisplayNameLength).IsRequired();
        account.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        account.Property(x => x.IsDisabled).HasColumnName("is_disabled").IsRequired();
        account.Property(x => x.Role).HasColumnName("role").HasConversion<int>().IsRequired();
    }
}
