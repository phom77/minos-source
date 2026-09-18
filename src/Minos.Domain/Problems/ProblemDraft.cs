namespace Minos.Domain.Problems;

public sealed class ProblemDraft
{
    public const int MaximumTitleLength = 120;
    public const int MaximumStatementLength = 65536;
    private ProblemDraft() { }

    public Guid Id { get; private set; }
    public Guid OwnerAccountId { get; private set; }
    public string Title { get; private set; } = "";
    public string Statement { get; private set; } = "";
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static ProblemDraft Create(Guid ownerAccountId, string? title, string? statement, DateTimeOffset createdAtUtc)
    {
        if (ownerAccountId == Guid.Empty)
            throw new DomainValidationException(nameof(OwnerAccountId), "A draft must have an account owner.");
        var normalizedTitle = title?.Trim() ?? "";
        if (normalizedTitle.Length is 0 or > MaximumTitleLength)
            throw new DomainValidationException(nameof(Title), $"Title must contain 1–{MaximumTitleLength} characters after trimming.");
        if (string.IsNullOrWhiteSpace(statement) || statement.Length > MaximumStatementLength)
            throw new DomainValidationException(nameof(Statement), $"Statement must contain text and at most {MaximumStatementLength} characters.");
        if (createdAtUtc.Offset != TimeSpan.Zero)
            throw new DomainValidationException(nameof(CreatedAtUtc), "Creation time must use UTC.");
        return new ProblemDraft { Id = Guid.NewGuid(), OwnerAccountId = ownerAccountId, Title = normalizedTitle, Statement = statement, CreatedAtUtc = createdAtUtc };
    }

    public bool IsOwnedBy(Guid accountId) => accountId != Guid.Empty && OwnerAccountId == accountId;
}
