namespace Minos.Api.Accounts;

// Classes avoid generated record ToString methods that would print passwords.
internal sealed class RegisterAccountRequest
{
    public string? Email { get; init; }
    public string? Password { get; init; }
    public string? DisplayName { get; init; }
}
internal sealed class LoginAccountRequest
{
    public string? Email { get; init; }
    public string? Password { get; init; }
}
internal sealed class ChangePasswordRequest
{
    public string? CurrentPassword { get; init; }
    public string? NewPassword { get; init; }
}
internal sealed class ConfirmPasswordRequest { public string? Password { get; init; } }
internal sealed record RenameAccountRequest(string? DisplayName);
public sealed record AccountResponse(Guid Id, string DisplayName, DateTimeOffset CreatedAtUtc);
public sealed record CsrfResponse(string RequestToken);
