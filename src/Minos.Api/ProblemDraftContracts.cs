namespace Minos.Api;

public sealed record CreateProblemDraftRequest(string? Title, string? Statement);
public sealed record ProblemDraftResponse(Guid Id, string Title, string Statement, DateTimeOffset CreatedAtUtc);
