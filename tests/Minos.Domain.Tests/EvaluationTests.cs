using Minos.Domain;
using Minos.Domain.Evaluation;
using Xunit;

namespace Minos.Domain.Tests;

public sealed class EvaluationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Submission_pins_owner_version_toolchain_and_bounded_source()
    {
        Assert.Throws<DomainValidationException>(() => Submission.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "cpp20", "toolchain", " ", Now));
        Assert.Throws<DomainValidationException>(() => Submission.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "cpp20", "toolchain",
            string.Concat(Enumerable.Repeat("🧩", Submission.MaximumSourceBytes / 4 + 1)), Now));

        var owner = Guid.NewGuid();
        var problem = Guid.NewGuid();
        var version = Guid.NewGuid();
        var submission = Submission.Create(owner, problem, version, "cpp20", "toolchain", "int main(){}", Now);

        Assert.Equal(owner, submission.OwnerAccountId);
        Assert.Equal(problem, submission.ProblemId);
        Assert.Equal(version, submission.ProblemVersionId);
        Assert.Equal(SubmissionStatus.Pending, submission.Status);
    }

    [Fact]
    public void Submission_only_current_attempt_can_transition_or_complete()
    {
        var submission = CreateSubmission();
        var attempt = Guid.NewGuid();
        submission.StartAttempt(attempt);
        Assert.Equal(SubmissionStatus.Judging, submission.Status);
        Assert.Throws<DomainValidationException>(() => submission.Complete(Guid.NewGuid(), AcceptedOutcome(), Now));

        var result = submission.Complete(attempt, AcceptedOutcome(), Now);
        Assert.Equal(SubmissionStatus.Completed, submission.Status);
        Assert.Equal(attempt, result.AttemptId);
        Assert.Throws<DomainValidationException>(() => submission.StartAttempt(Guid.NewGuid()));
    }

    [Fact]
    public void Job_claims_short_lease_and_rejects_stale_ownership()
    {
        var submission = CreateSubmission();
        var job = JudgeJob.Create(submission.Id, Now);
        var first = job.Claim("worker-a", Now);
        submission.StartAttempt(first.Id);

        Assert.Equal(1, first.AttemptNumber);
        Assert.Equal(Now.Add(JudgeJob.LeaseDuration), first.LeaseExpiresAtUtc);
        Assert.False(job.Renew(first.Id, Guid.NewGuid(), "worker-a", Now.AddSeconds(1)));
        Assert.Throws<DomainValidationException>(() =>
            job.Complete(first.Id, first.LeaseToken, "worker-a", first.LeaseExpiresAtUtc));

        var second = job.Claim("worker-b", first.LeaseExpiresAtUtc);
        first.Expire(first.LeaseExpiresAtUtc);
        submission.StartAttempt(second.Id);
        Assert.Equal(2, second.AttemptNumber);
        Assert.False(job.OwnsActiveLease(first.Id, first.LeaseToken, "worker-a"));
        Assert.Throws<DomainValidationException>(() => submission.Complete(first.Id, AcceptedOutcome(), Now.AddMinutes(1)));
    }

    [Fact]
    public void Retry_and_exhaustion_are_explicit_and_preserve_attempts()
    {
        var submission = CreateSubmission();
        var job = JudgeJob.Create(submission.Id, Now);

        for (var number = 1; number <= JudgeJob.MaximumAttempts; number++)
        {
            var now = Now.AddMinutes(number);
            var attempt = job.Claim("worker", now);
            submission.StartAttempt(attempt.Id);
            attempt.Fail(InfrastructureFailureKind.ExecutorFailure, now.AddSeconds(1));
            if (number < JudgeJob.MaximumAttempts)
            {
                job.Retry(attempt.Id, attempt.LeaseToken, "worker", now.AddSeconds(1), now.AddSeconds(2));
                submission.ReturnToPending(attempt.Id);
            }
            else
            {
                job.Exhaust(attempt.Id, attempt.LeaseToken, "worker", now.AddSeconds(1));
                submission.FailSystem(attempt.Id, now.AddSeconds(1));
            }
        }

        Assert.Equal(JudgeJobStatus.SystemFailed, job.Status);
        Assert.Equal(SubmissionStatus.SystemFailed, submission.Status);
        Assert.Equal(JudgeJob.MaximumAttempts, job.AttemptCount);
    }

    [Fact]
    public void Accepted_requires_every_test_and_calculates_maximum_resource_summary()
    {
        var outcome = JudgingOutcome.Create(
            SubmissionVerdict.Accepted,
            "",
            [
                new(1, TestCaseResultStatus.Passed, 4, 8, 1024, 0),
                new(2, TestCaseResultStatus.Passed, 7, 6, 2048, 0)
            ]);
        var submission = CreateSubmission();
        var attempt = Guid.NewGuid();
        submission.StartAttempt(attempt);

        var result = submission.Complete(attempt, outcome, Now);

        Assert.Equal(SubmissionVerdict.Accepted, result.Verdict);
        Assert.Equal(7, result.MaximumCpuTimeMilliseconds);
        Assert.Equal(8, result.MaximumWallTimeMilliseconds);
        Assert.Equal(2048, result.MaximumMemoryBytes);
        Assert.Equal(2, result.TestCaseResults.Count);
    }

    [Fact]
    public void Failure_must_match_first_failed_test_and_leave_later_tests_unexecuted()
    {
        var valid = JudgingOutcome.Create(
            SubmissionVerdict.WrongAnswer,
            "",
            [
                new(1, TestCaseResultStatus.Passed, 1, 1, 1024, 0),
                new(2, TestCaseResultStatus.WrongAnswer, 1, 1, 1024, 0),
                new(3, TestCaseResultStatus.NotExecuted, null, null, null, null)
            ]);

        Assert.Equal(SubmissionVerdict.WrongAnswer, valid.Verdict);
        Assert.Throws<DomainValidationException>(() => JudgingOutcome.Create(
            SubmissionVerdict.WrongAnswer,
            "",
            [
                new(1, TestCaseResultStatus.WrongAnswer, 1, 1, 1024, 0),
                new(2, TestCaseResultStatus.Passed, 1, 1, 1024, 0)
            ]));
        Assert.Throws<DomainValidationException>(() => JudgingOutcome.Create(
            SubmissionVerdict.Accepted,
            "",
            [new(1, TestCaseResultStatus.RuntimeError, 1, 1, 1024, 9)]));
    }

    [Fact]
    public void Compilation_error_keeps_bounded_diagnostics_and_executes_no_tests()
    {
        var outcome = JudgingOutcome.Create(
            SubmissionVerdict.CompilationError,
            "main.cpp: error",
            [new(1, TestCaseResultStatus.NotExecuted, null, null, null, null)]);

        Assert.Equal("main.cpp: error", outcome.CompilerDiagnostics);
        Assert.Throws<DomainValidationException>(() => JudgingOutcome.Create(
            SubmissionVerdict.CompilationError,
            new string('x', JudgingOutcome.MaximumCompilerDiagnosticsBytes + 1),
            [new(1, TestCaseResultStatus.NotExecuted, null, null, null, null)]));
    }

    private static Submission CreateSubmission() => Submission.Create(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "cpp20", "toolchain", "int main(){}", Now);

    [Fact]
    public void Verdict_cannot_disagree_with_limit_evidence_or_omit_measurements()
    {
        Assert.Throws<DomainValidationException>(() => JudgingOutcome.Create(SubmissionVerdict.Accepted, "",
            [new(1, TestCaseResultStatus.Passed, 1, 1, 1024, 0, OutputLimit: true)]));
        Assert.Throws<DomainValidationException>(() => JudgingOutcome.Create(SubmissionVerdict.RuntimeError, "",
            [new(1, TestCaseResultStatus.RuntimeError, 1, 1, 1024, 9, MemoryFailure: true)]));
        Assert.Throws<DomainValidationException>(() => JudgingOutcome.Create(SubmissionVerdict.Accepted, "",
            [new(1, TestCaseResultStatus.Passed, null, 1, 1024, 0)]));
        Assert.Throws<DomainValidationException>(() => JudgingOutcome.Create(SubmissionVerdict.CompilationError, "",
            [new(1, TestCaseResultStatus.NotExecuted, null, null, null, null, CpuLimit: true)]));
    }

    [Fact]
    public void Completion_evidence_copies_and_protects_the_test_collection()
    {
        var tests = new[] { new JudgingTestOutcome(1, TestCaseResultStatus.Passed, 1, 1, 1024, 0) };
        var outcome = JudgingOutcome.Create(SubmissionVerdict.Accepted, "", tests);
        tests[0] = new(1, TestCaseResultStatus.WrongAnswer, 1, 1, 1024, 0);
        Assert.Equal(TestCaseResultStatus.Passed, outcome.TestCases[0].Status);
        Assert.Throws<NotSupportedException>(() => ((IList<JudgingTestOutcome>)outcome.TestCases).Clear());
    }

    private static JudgingOutcome AcceptedOutcome() => JudgingOutcome.Create(
        SubmissionVerdict.Accepted,
        "",
        [new(1, TestCaseResultStatus.Passed, 1, 1, 1024, 0)]);
}
