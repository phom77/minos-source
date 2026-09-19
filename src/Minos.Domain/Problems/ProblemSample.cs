namespace Minos.Domain.Problems;

public sealed class ProblemSample
{
    public const int MaximumContentLength = 65536;
    private ProblemSample() { }
    public Guid Id { get; private set; }
    public int Order { get; private set; }
    public string Input { get; private set; } = "";
    public string Output { get; private set; } = "";

    internal static ProblemSample Create(Guid id, int order, string? input, string? output)
    {
        if (id == Guid.Empty) throw new DomainValidationException(nameof(Id), "Sample ID is required.");
        if (order <= 0) throw new DomainValidationException(nameof(Order), "Sample order must be positive.");
        if (input is null || input.Length > MaximumContentLength)
            throw new DomainValidationException(nameof(Input), $"Sample input must be at most {MaximumContentLength} characters.");
        if (output is null || output.Length > MaximumContentLength)
            throw new DomainValidationException(nameof(Output), $"Sample output must be at most {MaximumContentLength} characters.");
        return new ProblemSample { Id = id, Order = order, Input = input, Output = output };
    }
}
