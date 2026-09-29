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
        Assert.Throws<DomainValidationException>(() => submission.Complete(Guid.NewGuid(), Now));

        var result = submission.Complete(attempt, Now);
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
        Assert.Throws<DomainValidationException>(() => submission.Complete(first.Id, Now.AddMinutes(1)));
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

    private static Submission CreateSubmission() => Submission.Create(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "cpp20", "toolchain", "int main(){}", Now);
}
