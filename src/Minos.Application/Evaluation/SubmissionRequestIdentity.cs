using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Minos.Domain;
using Minos.Domain.Evaluation;

namespace Minos.Application.Evaluation;

// Request identity is independent of a later latest-version lookup. A replay keeps the original pin.
public sealed record SubmissionRequestIdentity
{
    private SubmissionRequestIdentity(Guid key, string fingerprint) { Key = key; Fingerprint = fingerprint; }
    public Guid Key { get; }
    public string Fingerprint { get; }

    public static SubmissionRequestIdentity? Create(CreateSubmission request, Guid problemId, Guid? contestId = null)
    {
        if (request.RequestKey is null) return null;
        if (request.RequestKey == Guid.Empty) throw new DomainValidationException("RequestKey", "A non-empty request key is required.");
        Submission.ValidateSource(request.Source);
        var payload = JsonSerializer.Serialize(new { Kind = contestId.HasValue ? "contest" : "practice", ContestId = contestId,
            ProblemId = problemId, request.Language, request.Source });
        return new(request.RequestKey.Value, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload))));
    }
}

public sealed class SubmissionRequestConflictException() : Exception("The request key was already used for a different submission.");
