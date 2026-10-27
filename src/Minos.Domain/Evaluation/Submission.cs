using System.Text;
using System.Diagnostics.CodeAnalysis;

namespace Minos.Domain.Evaluation;

public enum SubmissionStatus
{
    Pending,
    Judging,
    Completed,
    SystemFailed
}

public sealed class Submission
{
    public const int MaximumSourceBytes = 65_536;

    private Submission() { }

    public Guid Id { get; private set; }
    public Guid OwnerAccountId { get; private set; }
    public Guid ProblemId { get; private set; }
    public Guid ProblemVersionId { get; private set; }
    public string Language { get; private set; } = "";
    public string ToolchainId { get; private set; } = "";
    public string Source { get; private set; } = "";
    public SubmissionStatus Status { get; private set; }
    public Guid? CurrentAttemptId { get; private set; }
    public Guid? CompletedAttemptId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public static Submission Create(
        Guid ownerAccountId,
        Guid problemId,
        Guid problemVersionId,
        string? language,
        string? toolchainId,
        string? source,
        DateTimeOffset createdAtUtc)
    {
        if (ownerAccountId == Guid.Empty) throw Required(nameof(OwnerAccountId));
        if (problemId == Guid.Empty) throw Required(nameof(ProblemId));
        if (problemVersionId == Guid.Empty) throw Required(nameof(ProblemVersionId));
        if (string.IsNullOrWhiteSpace(language) || language.Length > 32)
            throw new DomainValidationException(nameof(Language), "Language is required and must be at most 32 characters.");
        if (string.IsNullOrWhiteSpace(toolchainId) || toolchainId.Length > 160)
            throw new DomainValidationException(nameof(ToolchainId), "Toolchain identity is required and must be at most 160 characters.");
        ValidateSource(source);
        EnsureUtc(createdAtUtc, nameof(CreatedAtUtc));

        return new Submission
        {
            Id = Guid.NewGuid(),
            OwnerAccountId = ownerAccountId,
            ProblemId = problemId,
            ProblemVersionId = problemVersionId,
            Language = language,
            ToolchainId = toolchainId,
            Source = source,
            Status = SubmissionStatus.Pending,
            CreatedAtUtc = createdAtUtc
        };
    }

    public void StartAttempt(Guid attemptId)
    {
        if (attemptId == Guid.Empty) throw Required(nameof(attemptId));
        if (Status is SubmissionStatus.Completed or SubmissionStatus.SystemFailed)
            throw InvalidState();
        Status = SubmissionStatus.Judging;
        CurrentAttemptId = attemptId;
    }

    public static void ValidateSource([NotNull] string? source)
    {
        if (string.IsNullOrWhiteSpace(source) || source.Contains('\0') || Encoding.UTF8.GetByteCount(source) > MaximumSourceBytes)
            throw new DomainValidationException(nameof(Source), $"Source must contain text, no null characters, and at most {MaximumSourceBytes} UTF-8 bytes.");
    }

    public void ReturnToPending(Guid attemptId)
    {
        RequireCurrent(attemptId);
        Status = SubmissionStatus.Pending;
        CurrentAttemptId = null;
    }

    public SubmissionResult Complete(Guid attemptId, JudgingOutcome outcome, DateTimeOffset completedAtUtc)
    {
        RequireCurrent(attemptId);
        ArgumentNullException.ThrowIfNull(outcome);
        EnsureUtc(completedAtUtc, nameof(CompletedAtUtc));
        Status = SubmissionStatus.Completed;
        CurrentAttemptId = null;
        CompletedAttemptId = attemptId;
        CompletedAtUtc = completedAtUtc;
        return SubmissionResult.Create(Id, attemptId, outcome, completedAtUtc);
    }

    public void FailSystem(Guid attemptId, DateTimeOffset completedAtUtc)
    {
        RequireCurrent(attemptId);
        EnsureUtc(completedAtUtc, nameof(CompletedAtUtc));
        Status = SubmissionStatus.SystemFailed;
        CurrentAttemptId = null;
        CompletedAttemptId = attemptId;
        CompletedAtUtc = completedAtUtc;
    }

    private void RequireCurrent(Guid attemptId)
    {
        if (Status != SubmissionStatus.Judging || attemptId == Guid.Empty || CurrentAttemptId != attemptId)
            throw InvalidState();
    }

    internal static void EnsureUtc(DateTimeOffset instant, string property)
    {
        if (instant.Offset != TimeSpan.Zero)
            throw new DomainValidationException(property, "Time must use UTC.");
    }

    private static DomainValidationException Required(string property) =>
        new(property, "A non-empty identifier is required.");

    private static DomainValidationException InvalidState() =>
        new(nameof(Status), "Submission transition is not allowed from its current state or attempt.");
}

public sealed class SubmissionResult
{
    private readonly List<TestCaseResult> testCaseResults = [];
    private SubmissionResult() { }

    public Guid SubmissionId { get; private set; }
    public Guid AttemptId { get; private set; }
    // Null is retained only for pre-M6 lifecycle markers migrated from M5.
    public SubmissionVerdict? Verdict { get; private set; }
    public string CompilerDiagnostics { get; private set; } = "";
    public long? MaximumCpuTimeMilliseconds { get; private set; }
    public long? MaximumWallTimeMilliseconds { get; private set; }
    public long? MaximumMemoryBytes { get; private set; }
    public string? MeasurementProfileId { get; private set; }
    public DateTimeOffset CompletedAtUtc { get; private set; }
    public IReadOnlyList<TestCaseResult> TestCaseResults => testCaseResults.AsReadOnly();

    internal static SubmissionResult Create(
        Guid submissionId,
        Guid attemptId,
        JudgingOutcome outcome,
        DateTimeOffset completedAtUtc)
    {
        var executed = outcome.TestCases.Where(test => test.Status != TestCaseResultStatus.NotExecuted).ToArray();
        var result = new SubmissionResult
        {
            SubmissionId = submissionId,
            AttemptId = attemptId,
            Verdict = outcome.Verdict,
            CompilerDiagnostics = outcome.CompilerDiagnostics,
            MaximumCpuTimeMilliseconds = executed.Length == 0 ? null : executed.Max(test => test.CpuTimeMilliseconds),
            MaximumWallTimeMilliseconds = executed.Length == 0 ? null : executed.Max(test => test.WallTimeMilliseconds),
            MaximumMemoryBytes = executed.Length == 0 ? null : executed.Max(test => test.MemoryPeakBytes),
            MeasurementProfileId = outcome.MeasurementProfileId,
            CompletedAtUtc = completedAtUtc
        };
        result.testCaseResults.AddRange(outcome.TestCases.Select(test => TestCaseResult.Create(submissionId, attemptId, test)));
        return result;
    }
}
