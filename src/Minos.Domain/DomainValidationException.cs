namespace Minos.Domain;

public sealed class DomainValidationException(string property, string message) : Exception(message)
{
    public string Property { get; } = property;
}
