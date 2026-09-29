namespace Minos.Application;

public sealed class ResourceNotFoundException : Exception;
public sealed class ConcurrencyConflictException : Exception;
public sealed class ArtifactValidationException(string message) : Exception(message);
public sealed class AdmissionLimitExceededException : Exception;
