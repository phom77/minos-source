namespace Minos.Domain.Evaluation;

public static class JudgingPolicy
{
    public static TestCaseResultStatus ClassifyTest(
        int exitCode, bool wallTimeout, bool cpuLimit, bool memoryFailure, bool outputLimit, bool outputMatches)
    {
        if (outputLimit) return TestCaseResultStatus.OutputLimitExceeded;
        if (memoryFailure) return TestCaseResultStatus.MemoryLimitExceeded;
        if (cpuLimit || wallTimeout) return TestCaseResultStatus.TimeLimitExceeded;
        if (exitCode != 0) return TestCaseResultStatus.RuntimeError;
        return outputMatches ? TestCaseResultStatus.Passed : TestCaseResultStatus.WrongAnswer;
    }
}
