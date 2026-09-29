namespace Minos.Domain.Evaluation;

public enum JudgeJobStatus
{
    Pending,
    Leased,
    Completed,
    SystemFailed
}

public enum JudgingAttemptStatus
{
    Active,
    Succeeded,
    InfrastructureFailed,
    LeaseExpired
}

public enum InfrastructureFailureKind
{
    ExecutorFailure,
    WorkerFailure,
    LeaseExpired
}

public sealed class JudgeJob
{
    public const int MaximumAttempts = 3;
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);

    private JudgeJob() { }

    public Guid Id { get; private set; }
    public Guid SubmissionId { get; private set; }
    public JudgeJobStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset AvailableAtUtc { get; private set; }
    public Guid? ActiveAttemptId { get; private set; }
    public Guid? ActiveLeaseToken { get; private set; }
    public string? ActiveWorkerId { get; private set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; private set; }
    public Guid? CompletedAttemptId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static JudgeJob Create(Guid submissionId, DateTimeOffset createdAtUtc)
    {
        if (submissionId == Guid.Empty)
            throw new DomainValidationException(nameof(SubmissionId), "Submission is required.");
        Submission.EnsureUtc(createdAtUtc, nameof(CreatedAtUtc));
        return new JudgeJob
        {
            Id = Guid.NewGuid(),
            SubmissionId = submissionId,
            Status = JudgeJobStatus.Pending,
            AvailableAtUtc = createdAtUtc,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc
        };
    }

    public JudgingAttempt Claim(string? workerId, DateTimeOffset nowUtc)
    {
        Submission.EnsureUtc(nowUtc, nameof(nowUtc));
        if (string.IsNullOrWhiteSpace(workerId) || workerId.Length > 128)
            throw new DomainValidationException(nameof(workerId), "Worker identity is required and must be at most 128 characters.");
        if (AttemptCount >= MaximumAttempts ||
            (Status == JudgeJobStatus.Pending && AvailableAtUtc > nowUtc) ||
            (Status == JudgeJobStatus.Leased && LeaseExpiresAtUtc > nowUtc) ||
            Status is JudgeJobStatus.Completed or JudgeJobStatus.SystemFailed)
            throw InvalidState();

        AttemptCount++;
        var attemptId = Guid.NewGuid();
        var leaseToken = Guid.NewGuid();
        var leaseExpiresAtUtc = nowUtc.Add(LeaseDuration);
        Status = JudgeJobStatus.Leased;
        ActiveAttemptId = attemptId;
        ActiveLeaseToken = leaseToken;
        ActiveWorkerId = workerId;
        LeaseExpiresAtUtc = leaseExpiresAtUtc;
        UpdatedAtUtc = nowUtc;
        return JudgingAttempt.Start(attemptId, Id, SubmissionId, AttemptCount, leaseToken, workerId, nowUtc, leaseExpiresAtUtc);
    }

    public bool Renew(Guid attemptId, Guid leaseToken, string workerId, DateTimeOffset nowUtc)
    {
        Submission.EnsureUtc(nowUtc, nameof(nowUtc));
        if (!OwnsActiveLease(attemptId, leaseToken, workerId) || LeaseExpiresAtUtc <= nowUtc) return false;
        LeaseExpiresAtUtc = nowUtc.Add(LeaseDuration);
        UpdatedAtUtc = nowUtc;
        return true;
    }

    public bool OwnsActiveLease(Guid attemptId, Guid leaseToken, string workerId) =>
        Status == JudgeJobStatus.Leased && ActiveAttemptId == attemptId && ActiveLeaseToken == leaseToken &&
        string.Equals(ActiveWorkerId, workerId, StringComparison.Ordinal);

    public void Complete(Guid attemptId, Guid leaseToken, string workerId, DateTimeOffset nowUtc)
    {
        RequireLiveLease(attemptId, leaseToken, workerId, nowUtc);
        Status = JudgeJobStatus.Completed;
        CompletedAttemptId = attemptId;
        ClearLease();
        UpdatedAtUtc = nowUtc;
    }

    public void Retry(Guid attemptId, Guid leaseToken, string workerId, DateTimeOffset nowUtc, DateTimeOffset retryAtUtc)
    {
        RequireLiveLease(attemptId, leaseToken, workerId, nowUtc);
        Submission.EnsureUtc(retryAtUtc, nameof(retryAtUtc));
        if (retryAtUtc < nowUtc) throw new DomainValidationException(nameof(retryAtUtc), "Retry time cannot be in the past.");
        Status = JudgeJobStatus.Pending;
        AvailableAtUtc = retryAtUtc;
        ClearLease();
        UpdatedAtUtc = nowUtc;
    }

    public void Exhaust(Guid attemptId, Guid leaseToken, string workerId, DateTimeOffset nowUtc)
    {
        RequireLiveLease(attemptId, leaseToken, workerId, nowUtc);
        Status = JudgeJobStatus.SystemFailed;
        CompletedAttemptId = attemptId;
        ClearLease();
        UpdatedAtUtc = nowUtc;
    }

    public void ExhaustExpired(DateTimeOffset nowUtc)
    {
        Submission.EnsureUtc(nowUtc, nameof(nowUtc));
        if (Status != JudgeJobStatus.Leased || LeaseExpiresAtUtc > nowUtc || AttemptCount < MaximumAttempts || ActiveAttemptId is null)
            throw InvalidState();
        Status = JudgeJobStatus.SystemFailed;
        CompletedAttemptId = ActiveAttemptId;
        ClearLease();
        UpdatedAtUtc = nowUtc;
    }

    private void RequireLiveLease(Guid attemptId, Guid leaseToken, string workerId, DateTimeOffset nowUtc)
    {
        Submission.EnsureUtc(nowUtc, nameof(nowUtc));
        if (!OwnsActiveLease(attemptId, leaseToken, workerId) || LeaseExpiresAtUtc <= nowUtc) throw InvalidState();
    }

    private void ClearLease()
    {
        ActiveAttemptId = null;
        ActiveLeaseToken = null;
        ActiveWorkerId = null;
        LeaseExpiresAtUtc = null;
    }

    private static DomainValidationException InvalidState() =>
        new(nameof(Status), "Judge job transition is not allowed from its current state or lease.");
}

public sealed class JudgingAttempt
{
    private JudgingAttempt() { }

    public Guid Id { get; private set; }
    public Guid JudgeJobId { get; private set; }
    public Guid SubmissionId { get; private set; }
    public int AttemptNumber { get; private set; }
    public Guid LeaseToken { get; private set; }
    public string WorkerId { get; private set; } = "";
    public JudgingAttemptStatus Status { get; private set; }
    public InfrastructureFailureKind? FailureKind { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset LeaseExpiresAtUtc { get; private set; }
    public DateTimeOffset? FinishedAtUtc { get; private set; }

    internal static JudgingAttempt Start(
        Guid id,
        Guid jobId,
        Guid submissionId,
        int number,
        Guid leaseToken,
        string workerId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset leaseExpiresAtUtc) =>
        new()
        {
            Id = id,
            JudgeJobId = jobId,
            SubmissionId = submissionId,
            AttemptNumber = number,
            LeaseToken = leaseToken,
            WorkerId = workerId,
            Status = JudgingAttemptStatus.Active,
            StartedAtUtc = startedAtUtc,
            LeaseExpiresAtUtc = leaseExpiresAtUtc
        };

    public void Renew(DateTimeOffset leaseExpiresAtUtc)
    {
        if (Status != JudgingAttemptStatus.Active || leaseExpiresAtUtc <= LeaseExpiresAtUtc)
            throw new DomainValidationException(nameof(Status), "Attempt renewal is not allowed.");
        Submission.EnsureUtc(leaseExpiresAtUtc, nameof(LeaseExpiresAtUtc));
        LeaseExpiresAtUtc = leaseExpiresAtUtc;
    }

    public void Succeed(DateTimeOffset finishedAtUtc) => Finish(JudgingAttemptStatus.Succeeded, null, finishedAtUtc);

    public void Fail(InfrastructureFailureKind kind, DateTimeOffset finishedAtUtc) =>
        Finish(JudgingAttemptStatus.InfrastructureFailed, kind, finishedAtUtc);

    public void Expire(DateTimeOffset finishedAtUtc) =>
        Finish(JudgingAttemptStatus.LeaseExpired, InfrastructureFailureKind.LeaseExpired, finishedAtUtc);

    private void Finish(JudgingAttemptStatus status, InfrastructureFailureKind? failure, DateTimeOffset finishedAtUtc)
    {
        Submission.EnsureUtc(finishedAtUtc, nameof(FinishedAtUtc));
        if (Status != JudgingAttemptStatus.Active)
            throw new DomainValidationException(nameof(Status), "Attempt is already finished.");
        Status = status;
        FailureKind = failure;
        FinishedAtUtc = finishedAtUtc;
    }
}
