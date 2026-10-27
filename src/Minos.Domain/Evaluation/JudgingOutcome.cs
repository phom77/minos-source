using System.Text;

namespace Minos.Domain.Evaluation;

public enum SubmissionVerdict
{
    Accepted,
    WrongAnswer,
    CompilationError,
    RuntimeError,
    TimeLimitExceeded,
    MemoryLimitExceeded,
    OutputLimitExceeded
}

public enum TestCaseResultStatus
{
    NotExecuted,
    Passed,
    WrongAnswer,
    RuntimeError,
    TimeLimitExceeded,
    MemoryLimitExceeded,
    OutputLimitExceeded
}

public sealed record JudgingTestOutcome(
    int Order,
    TestCaseResultStatus Status,
    long? CpuTimeMilliseconds,
    long? WallTimeMilliseconds,
    long? MemoryPeakBytes,
    int? ExitCode,
    bool WallTimeout = false,
    bool CpuLimit = false,
    bool MemoryFailure = false,
    bool OutputLimit = false);

public sealed class JudgingOutcome
{
    public const int MaximumCompilerDiagnosticsBytes = 32 * 1024;

    private JudgingOutcome(
        SubmissionVerdict verdict,
        string compilerDiagnostics,
        IReadOnlyList<JudgingTestOutcome> testCases,
        string? measurementProfileId)
    {
        Verdict = verdict;
        CompilerDiagnostics = compilerDiagnostics;
        TestCases = testCases;
        MeasurementProfileId = measurementProfileId;
    }

    public SubmissionVerdict Verdict { get; }
    public string CompilerDiagnostics { get; }
    public IReadOnlyList<JudgingTestOutcome> TestCases { get; }
    public string? MeasurementProfileId { get; }

    public static JudgingOutcome Create(
        SubmissionVerdict verdict,
        string? compilerDiagnostics,
        IReadOnlyList<JudgingTestOutcome> testCases,
        string? measurementProfileId = null)
    {
        ArgumentNullException.ThrowIfNull(testCases);
        if (measurementProfileId is not null && (measurementProfileId.Length != 64 ||
            measurementProfileId.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))))
            throw new DomainValidationException(nameof(MeasurementProfileId), "Measurement profile must be a lowercase SHA-256 identity.");
        compilerDiagnostics ??= "";
        if (compilerDiagnostics.Contains('\0') || Encoding.UTF8.GetByteCount(compilerDiagnostics) > MaximumCompilerDiagnosticsBytes)
            throw new DomainValidationException(nameof(CompilerDiagnostics), "Compiler diagnostics are invalid or exceed their byte limit.");
        if (!Enum.IsDefined(verdict))
            throw new DomainValidationException(nameof(Verdict), "Verdict is unsupported.");
        if (testCases.Count == 0 || testCases.Count > Minos.Domain.Problems.Problem.MaximumTestCases)
            throw new DomainValidationException(nameof(TestCases), "Judging evidence must represent every private test.");

        for (var index = 0; index < testCases.Count; index++)
        {
            var test = testCases[index];
            if (test.Order != index + 1 || !Enum.IsDefined(test.Status))
                throw new DomainValidationException(nameof(TestCases), "Test results must use contiguous published order.");
            var executed = test.Status != TestCaseResultStatus.NotExecuted;
            if ((executed && (test.CpuTimeMilliseconds is null || test.WallTimeMilliseconds is null || test.MemoryPeakBytes is null || test.ExitCode is null)) ||
                (!executed && (test.CpuTimeMilliseconds is not null || test.WallTimeMilliseconds is not null || test.MemoryPeakBytes is not null ||
                    test.ExitCode is not null || test.WallTimeout || test.CpuLimit || test.MemoryFailure || test.OutputLimit)) ||
                test.CpuTimeMilliseconds < 0 || test.WallTimeMilliseconds < 0 || test.MemoryPeakBytes < 0)
                throw new DomainValidationException(nameof(TestCases), "Executed tests require non-negative resource measurements.");
            if (executed && JudgingPolicy.ClassifyTest(test.ExitCode!.Value, test.WallTimeout, test.CpuLimit,
                test.MemoryFailure, test.OutputLimit, test.Status == TestCaseResultStatus.Passed) != test.Status)
                throw new DomainValidationException(nameof(TestCases), "Test status must match the recorded execution evidence and approved precedence.");
        }

        if (verdict == SubmissionVerdict.CompilationError)
        {
            if (testCases.Any(test => test.Status != TestCaseResultStatus.NotExecuted))
                throw new DomainValidationException(nameof(TestCases), "Compilation failure cannot contain executed tests.");
            return new(verdict, compilerDiagnostics, Array.AsReadOnly(testCases.ToArray()), measurementProfileId);
        }

        if (compilerDiagnostics.Length != 0)
            throw new DomainValidationException(nameof(CompilerDiagnostics), "Compiler diagnostics are retained only for compilation errors.");

        if (verdict == SubmissionVerdict.Accepted)
        {
            if (testCases.Any(test => test.Status != TestCaseResultStatus.Passed))
                throw new DomainValidationException(nameof(TestCases), "Accepted requires every private test to pass.");
            return new(verdict, "", Array.AsReadOnly(testCases.ToArray()), measurementProfileId);
        }

        if (testCases.Any(test => test.Status == TestCaseResultStatus.NotExecuted))
            throw new DomainValidationException(nameof(TestCases), "Runtime results require every private test to be executed.");
        var firstFailure = testCases.FirstOrDefault(test => test.Status != TestCaseResultStatus.Passed);
        if (firstFailure is null || firstFailure.Status != VerdictStatus(verdict))
            throw new DomainValidationException(nameof(TestCases), "The verdict must match the first failed test.");
        return new(verdict, "", Array.AsReadOnly(testCases.ToArray()), measurementProfileId);
    }

    private static TestCaseResultStatus VerdictStatus(SubmissionVerdict verdict) => verdict switch
    {
        SubmissionVerdict.WrongAnswer => TestCaseResultStatus.WrongAnswer,
        SubmissionVerdict.RuntimeError => TestCaseResultStatus.RuntimeError,
        SubmissionVerdict.TimeLimitExceeded => TestCaseResultStatus.TimeLimitExceeded,
        SubmissionVerdict.MemoryLimitExceeded => TestCaseResultStatus.MemoryLimitExceeded,
        SubmissionVerdict.OutputLimitExceeded => TestCaseResultStatus.OutputLimitExceeded,
        _ => throw new DomainValidationException(nameof(Verdict), "This verdict is not a testcase failure.")
    };
}

public sealed class TestCaseResult
{
    private TestCaseResult() { }

    public Guid Id { get; private set; }
    public Guid SubmissionId { get; private set; }
    public Guid AttemptId { get; private set; }
    public int Order { get; private set; }
    public TestCaseResultStatus Status { get; private set; }
    public long? CpuTimeMilliseconds { get; private set; }
    public long? WallTimeMilliseconds { get; private set; }
    public long? MemoryPeakBytes { get; private set; }
    public int? ExitCode { get; private set; }
    public bool WallTimeout { get; private set; }
    public bool CpuLimit { get; private set; }
    public bool MemoryFailure { get; private set; }
    public bool OutputLimit { get; private set; }

    internal static TestCaseResult Create(Guid submissionId, Guid attemptId, JudgingTestOutcome outcome) => new()
    {
        Id = Guid.NewGuid(),
        SubmissionId = submissionId,
        AttemptId = attemptId,
        Order = outcome.Order,
        Status = outcome.Status,
        CpuTimeMilliseconds = outcome.CpuTimeMilliseconds,
        WallTimeMilliseconds = outcome.WallTimeMilliseconds,
        MemoryPeakBytes = outcome.MemoryPeakBytes,
        ExitCode = outcome.ExitCode,
        WallTimeout = outcome.WallTimeout,
        CpuLimit = outcome.CpuLimit,
        MemoryFailure = outcome.MemoryFailure,
        OutputLimit = outcome.OutputLimit
    };
}
