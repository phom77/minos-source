using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Minos.Application.Evaluation;
using Minos.Application.Problems;
using Minos.Domain.Evaluation;
using Minos.Domain.Problems;
using Minos.Infrastructure.Persistence;

namespace Minos.Infrastructure.Evaluation;

internal sealed class OfficialSubmissionExecutor(
    MinosDbContext database,
    IProblemArtifactStore artifacts,
    IExecutionBackend backend,
    ILogger<OfficialSubmissionExecutor> logger) : ISubmissionExecutor
{
    private const long Megabyte = 1024 * 1024;
    private static readonly ExecutionLimits CompilationLimits = new(
        TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(8), 512 * Megabyte, 64 * Megabyte, 32,
        JudgingOutcome.MaximumCompilerDiagnosticsBytes, JudgingOutcome.MaximumCompilerDiagnosticsBytes);

    public async Task<SubmissionExecutionResult> ExecuteAsync(ClaimedSubmissionWork work, CancellationToken token)
    {
        using var totalMeasurement = JudgingMetrics.Measure("submission", "total");
        try
        {
            if (work.Language != SupportedSubmission.Language || work.ToolchainId != SupportedSubmission.ToolchainId)
                return SubmissionExecutionResult.InfrastructureFailed();

            var version = await database.PublishedProblemVersions.AsNoTracking()
                .Include(candidate => candidate.TestCases)
                .SingleOrDefaultAsync(candidate => candidate.Id == work.ProblemVersionId, token);
            if (version is null || version.Checker != BuiltInChecker.Exact)
                return SubmissionExecutionResult.InfrastructureFailed();
            var tests = version.TestCases.OrderBy(test => test.Order).ToArray();
            if (tests.Length != version.PrivateTestCount || tests.Length == 0 ||
                tests.Where((test, index) => test.Order != index + 1).Any())
                return SubmissionExecutionResult.InfrastructureFailed();

            using (JudgingMetrics.Measure("submission", "artifact-preflight"))
                await artifacts.VerifyAsync(tests.SelectMany(test => new[] { test.InputArtifact, test.OutputArtifact }), token);

            var compilation = await backend.ExecuteAsync(new(
                ExecutionStage.Compile,
                Encoding.UTF8.GetBytes(work.Source),
                [],
                CompilationLimits), token);
            if (token.IsCancellationRequested) token.ThrowIfCancellationRequested();
            if (!OfficialJudgingPolicy.HasCompleteEvidence(compilation))
            {
                LogIncomplete(work, "Compile", compilation);
                return SubmissionExecutionResult.InfrastructureFailed();
            }

            JudgingMetrics.RecordCompilerMemory(compilation.MemoryPeakBytes!.Value);
            if (OfficialJudgingPolicy.IsCompilationError(compilation))
            {
                var diagnostics = DecodeDiagnostics(compilation.Stderr);
                return SubmissionExecutionResult.Completed(JudgingOutcome.Create(
                    SubmissionVerdict.CompilationError,
                    diagnostics,
                    tests.Select(test => NotExecuted(test.Order)).ToArray()));
            }
            if (compilation.Artifact is null or { Length: 0 })
                return SubmissionExecutionResult.InfrastructureFailed();

            var outcomes = new List<JudgingTestOutcome>(tests.Length);
            SubmissionVerdict? finalVerdict = null;
            foreach (var test in tests)
            {
                byte[] input;
                byte[] expected;
                using (JudgingMetrics.Measure("submission", "artifact-read"))
                {
                    input = await artifacts.ReadVerifiedAsync(test.InputArtifact, token);
                    expected = await artifacts.ReadVerifiedAsync(test.OutputArtifact, token);
                }
                const int outputLimit = (int)ProblemArtifactReference.MaximumBytes;
                var runtimeLimits = new ExecutionLimits(
                    MaximumWallTime(version.TimeLimitMilliseconds),
                    TimeSpan.FromMilliseconds(version.TimeLimitMilliseconds),
                    version.MemoryLimitMegabytes * Megabyte,
                    8 * Megabyte,
                    32,
                    outputLimit,
                    JudgingOutcome.MaximumCompilerDiagnosticsBytes);
                var execution = await backend.ExecuteAsync(new(
                    ExecutionStage.Run,
                    compilation.Artifact,
                    input,
                    runtimeLimits), token);
                if (token.IsCancellationRequested) token.ThrowIfCancellationRequested();
                if (!OfficialJudgingPolicy.HasCompleteEvidence(execution))
                {
                    LogIncomplete(work, "Run", execution);
                    return SubmissionExecutionResult.InfrastructureFailed();
                }

                using var checking = JudgingMetrics.Measure("submission", "check");
                var status = OfficialJudgingPolicy.ClassifyTest(execution, expected);
                outcomes.Add(new(
                    test.Order,
                    status,
                    Milliseconds(execution.CpuUsage!.Value),
                    Milliseconds(execution.WallElapsed),
                    execution.MemoryPeakBytes!.Value,
                    execution.ExitCode,
                    execution.WallTimeout,
                    execution.CpuLimit,
                    execution.MemoryFailure,
                    execution.OutputLimit));
                finalVerdict ??= OfficialJudgingPolicy.Verdict(status);
            }

            return SubmissionExecutionResult.Completed(JudgingOutcome.Create(
                finalVerdict ?? SubmissionVerdict.Accepted,
                "",
                outcomes));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            logger.LogWarning("Official judging failed with infrastructure error {ErrorType}; job {JobId}, submission {SubmissionId}, attempt {AttemptId}",
                error.GetType().Name, work.JobId, work.SubmissionId, work.AttemptId);
            return SubmissionExecutionResult.InfrastructureFailed();
        }
    }

    private void LogIncomplete(ClaimedSubmissionWork work, string stage, ExecutionEvidence evidence) => logger.LogWarning(
        "{Stage} evidence was incomplete: failure {Failure}, snapshot {Snapshot}, cleanup {Cleanup}, cancelled {Cancelled}, exit {ExitCode}, wall {WallLimit}, cpu {CpuLimit}, memory {MemoryLimit}, output {OutputLimit}; job {JobId}, submission {SubmissionId}, attempt {AttemptId}",
        stage,
        evidence.InfrastructureFailure,
        evidence.ResourceSnapshotComplete,
        evidence.CleanupComplete,
        evidence.Cancelled,
        evidence.ExitCode,
        evidence.WallTimeout,
        evidence.CpuLimit,
        evidence.MemoryFailure,
        evidence.OutputLimit, work.JobId, work.SubmissionId, work.AttemptId);

    private static JudgingTestOutcome NotExecuted(int order) =>
        new(order, TestCaseResultStatus.NotExecuted, null, null, null, null);

    private static TimeSpan MaximumWallTime(int timeLimitMilliseconds) =>
        TimeSpan.FromMilliseconds(Math.Max(timeLimitMilliseconds * 2L, timeLimitMilliseconds + 1000L));

    private static long Milliseconds(TimeSpan duration) =>
        checked((long)Math.Ceiling(Math.Max(0, duration.TotalMilliseconds)));

    private static string DecodeDiagnostics(byte[] diagnostics)
    {
        var text = Encoding.UTF8.GetString(diagnostics).Replace("\0", "", StringComparison.Ordinal);
        while (Encoding.UTF8.GetByteCount(text) > JudgingOutcome.MaximumCompilerDiagnosticsBytes)
            text = text[..^1];
        return text;
    }
}
