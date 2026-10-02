using Minos.Application.Evaluation;
using Minos.Domain.Evaluation;
using Xunit;

namespace Minos.IntegrationTests;

public sealed class OfficialJudgingPolicyTests
{
    [Fact]
    public void Exact_checker_compares_every_byte()
    {
        Assert.Equal(TestCaseResultStatus.Passed,
            OfficialJudgingPolicy.ClassifyTest(Evidence(stdout: "42\n"u8.ToArray()), "42\n"u8));
        Assert.Equal(TestCaseResultStatus.WrongAnswer,
            OfficialJudgingPolicy.ClassifyTest(Evidence(stdout: "42"u8.ToArray()), "42\n"u8));
        Assert.Equal(TestCaseResultStatus.WrongAnswer,
            OfficialJudgingPolicy.ClassifyTest(Evidence(stdout: "42 \n"u8.ToArray()), "42\n"u8));
    }

    [Fact]
    public void Overlapping_failures_use_approved_precedence()
    {
        Assert.Equal(TestCaseResultStatus.OutputLimitExceeded,
            OfficialJudgingPolicy.ClassifyTest(Evidence(output: true, memory: true, cpu: true, wall: true, exitCode: 137), []));
        Assert.Equal(TestCaseResultStatus.MemoryLimitExceeded,
            OfficialJudgingPolicy.ClassifyTest(Evidence(memory: true, cpu: true, wall: true, exitCode: 137), []));
        Assert.Equal(TestCaseResultStatus.TimeLimitExceeded,
            OfficialJudgingPolicy.ClassifyTest(Evidence(cpu: true, wall: true, exitCode: 137), []));
        Assert.Equal(TestCaseResultStatus.RuntimeError,
            OfficialJudgingPolicy.ClassifyTest(Evidence(exitCode: 9), []));
    }

    [Fact]
    public void Missing_resource_snapshot_is_not_trustworthy()
    {
        Assert.False(OfficialJudgingPolicy.HasCompleteEvidence(Evidence(snapshot: false)));
        Assert.False(OfficialJudgingPolicy.HasCompleteEvidence(Evidence(cleanup: false)));
        Assert.False(OfficialJudgingPolicy.HasCompleteEvidence(Evidence(infrastructure: "fault")));
        Assert.False(OfficialJudgingPolicy.HasCompleteEvidence(Evidence() with { CpuUsage = null }));
        Assert.False(OfficialJudgingPolicy.HasCompleteEvidence(Evidence() with { MemoryPeakBytes = null }));
        Assert.False(OfficialJudgingPolicy.HasCompleteEvidence(Evidence() with { CpuUsage = TimeSpan.FromTicks(-1) }));
        Assert.Throws<InvalidOperationException>(() => OfficialJudgingPolicy.ClassifyTest(Evidence(snapshot: false), []));
    }

    private static ExecutionEvidence Evidence(
        byte[]? stdout = null,
        bool output = false,
        bool memory = false,
        bool cpu = false,
        bool wall = false,
        int exitCode = 0,
        bool snapshot = true,
        bool cleanup = true,
        string? infrastructure = null) => new(
            exitCode,
            wall,
            cpu,
            memory,
            output,
            false,
            TimeSpan.FromMilliseconds(2),
            TimeSpan.FromMilliseconds(1),
            1024,
            stdout ?? [],
            [],
            null,
            snapshot,
            cleanup,
            infrastructure);
}
