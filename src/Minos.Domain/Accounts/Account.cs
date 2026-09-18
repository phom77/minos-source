namespace Minos.Domain.Accounts;

public sealed class Account
{
    public const int MaximumDisplayNameLength = 80;
    private Account() { }
    public Guid Id { get; private set; }
    public string DisplayName { get; private set; } = "";
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public bool IsDisabled { get; private set; }

    public static Account Create(string? displayName, DateTimeOffset createdAtUtc)
    {
        if (createdAtUtc.Offset != TimeSpan.Zero)
            throw new DomainValidationException(nameof(CreatedAtUtc), "Creation time must use UTC.");
        var account = new Account { Id = Guid.NewGuid(), CreatedAtUtc = createdAtUtc };
        account.Rename(displayName);
        return account;
    }

    public void Rename(string? displayName)
    {
        if (IsDisabled)
            throw new DomainValidationException(nameof(IsDisabled), "A disabled account cannot change its profile.");
        var name = displayName?.Trim() ?? "";
        if (name.Length is 0 or > MaximumDisplayNameLength || name.Any(char.IsControl))
            throw new DomainValidationException(nameof(DisplayName), "Display name must contain 1–80 characters without control characters.");
        DisplayName = name;
    }

    public void Disable() => IsDisabled = true;
}
