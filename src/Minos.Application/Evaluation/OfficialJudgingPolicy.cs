using Minos.Domain.Evaluation;

namespace Minos.Application.Evaluation;

public static class OfficialJudgingPolicy
{
    public static bool HasCompleteEvidence(ExecutionEvidence evidence) =>
        !evidence.Cancelled && evidence.InfrastructureFailure is null && evidence.CleanupComplete &&
        evidence.ResourceSnapshotComplete && evidence.ExitCode is not null &&
        evidence.CpuUsage is { } cpu && cpu >= TimeSpan.Zero &&
        evidence.MemoryPeakBytes is >= 0 && evidence.WallElapsed >= TimeSpan.Zero;

    public static bool IsCompilationError(ExecutionEvidence evidence) =>
        evidence.OutputLimit || evidence.MemoryFailure || evidence.CpuLimit || evidence.WallTimeout || evidence.ExitCode != 0;

    public static TestCaseResultStatus ClassifyTest(ExecutionEvidence evidence, ReadOnlySpan<byte> expected)
    {
        if (!HasCompleteEvidence(evidence))
            throw new InvalidOperationException("Untrustworthy execution evidence cannot produce a contestant verdict.");
        return JudgingPolicy.ClassifyTest(evidence.ExitCode!.Value, evidence.WallTimeout, evidence.CpuLimit,
            evidence.MemoryFailure, evidence.OutputLimit, evidence.Stdout.AsSpan().SequenceEqual(expected));
    }

    public static SubmissionVerdict? Verdict(TestCaseResultStatus status) => status switch
    {
        TestCaseResultStatus.Passed => null,
        TestCaseResultStatus.WrongAnswer => SubmissionVerdict.WrongAnswer,
        TestCaseResultStatus.RuntimeError => SubmissionVerdict.RuntimeError,
        TestCaseResultStatus.TimeLimitExceeded => SubmissionVerdict.TimeLimitExceeded,
        TestCaseResultStatus.MemoryLimitExceeded => SubmissionVerdict.MemoryLimitExceeded,
        TestCaseResultStatus.OutputLimitExceeded => SubmissionVerdict.OutputLimitExceeded,
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };
}
