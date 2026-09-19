namespace Minos.Domain.Problems;

public sealed class ProblemArtifactReference
{
    public const long MaximumBytes = 8 * 1024 * 1024;
    private ProblemArtifactReference() { }

    public string Key { get; private set; } = "";
    public string Sha256 { get; private set; } = "";
    public long SizeBytes { get; private set; }

    public static ProblemArtifactReference Create(string? key, string? sha256, long sizeBytes)
    {
        if (!ValidKey(key))
            throw new DomainValidationException(nameof(Key), "Artifact key is invalid.");
        if (sha256 is null || sha256.Length != 64 || sha256.Any(character => !char.IsAsciiHexDigit(character) || char.IsUpper(character)))
            throw new DomainValidationException(nameof(Sha256), "Artifact checksum must be lowercase SHA-256 hex.");
        if (sizeBytes is < 0 or > MaximumBytes)
            throw new DomainValidationException(nameof(SizeBytes), $"Artifact size must be at most {MaximumBytes} bytes.");
        return new ProblemArtifactReference { Key = key!, Sha256 = sha256, SizeBytes = sizeBytes };
    }

    public ProblemArtifactReference Copy() => Create(Key, Sha256, SizeBytes);

    private static bool ValidKey(string? key)
    {
        if (key is null || !key.StartsWith("tests/", StringComparison.Ordinal) || !key.EndsWith(".bin", StringComparison.Ordinal))
            return false;
        var parts = key.Split('/');
        return parts is ["tests", var id, "input.bin" or "output.bin"]
            && id.Length == 32 && id.All(character => char.IsAsciiHexDigit(character) && !char.IsUpper(character));
    }
}

public sealed class ProblemTestCase
{
    private ProblemTestCase() { }
    public Guid Id { get; private set; }
    public int Order { get; private set; }
    public ProblemArtifactReference InputArtifact { get; private set; } = null!;
    public ProblemArtifactReference OutputArtifact { get; private set; } = null!;

    internal static ProblemTestCase Create(Guid id, int order, ProblemArtifactReference input, ProblemArtifactReference output)
    {
        if (id == Guid.Empty) throw new DomainValidationException(nameof(Id), "Test case ID is required.");
        if (order <= 0) throw new DomainValidationException(nameof(Order), "Test case order must be positive.");
        return new ProblemTestCase { Id = id, Order = order, InputArtifact = input.Copy(), OutputArtifact = output.Copy() };
    }

    internal void SetOrder(int order) => Order = order;
}
