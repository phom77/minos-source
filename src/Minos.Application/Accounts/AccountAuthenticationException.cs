namespace Minos.Application.Accounts;

public sealed class AccountAuthenticationException() : Exception("Authentication failed.");
public sealed class AccountRegistrationException() : Exception("Unable to register with those details.");
public sealed class ExternalLoginException() : Exception("Unable to complete external sign-in. Sign in to your existing account to link the provider.");
