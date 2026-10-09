namespace Minos.Domain.Contests;

public enum ContestState { Draft, Scheduled, Live, Ended }

public sealed class Contest
{
    public const int MaximumProblems = 26;
    private readonly List<ContestProblem> problems = [];
    private Contest() { }
    public Guid Id { get; private set; }
    public Guid OwnerAccountId { get; private set; }
    public string Title { get; private set; } = "";
    public string Description { get; private set; } = "";
    public DateTimeOffset StartsAtUtc { get; private set; }
    public DateTimeOffset EndsAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? PublishedAtUtc { get; private set; }
    public long Revision { get; private set; }
    public IReadOnlyList<ContestProblem> Problems => problems.AsReadOnly();

    public static Contest Create(Guid owner, string? title, string? description, DateTimeOffset start,
        DateTimeOffset end, DateTimeOffset now)
    {
        if (owner == Guid.Empty) throw new DomainValidationException(nameof(OwnerAccountId), "An organizer is required.");
        EnsureUtc(now);
        var contest = new Contest { Id = Guid.NewGuid(), OwnerAccountId = owner, CreatedAtUtc = now };
        contest.Edit(title, description, start, end, [], now);
        return contest;
    }

    public void Edit(string? title, string? description, DateTimeOffset start, DateTimeOffset end,
        IReadOnlyList<ContestProblemSelection> selections, DateTimeOffset now)
    {
        EnsureDraft(); EnsureUtc(now); EnsureUtc(start); EnsureUtc(end);
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 120)
            throw new DomainValidationException(nameof(Title), "Enter a title of 1–120 characters.");
        if ((description?.Length ?? 0) > 8000)
            throw new DomainValidationException(nameof(Description), "Description must be at most 8,000 characters.");
        if (start <= now) throw new DomainValidationException(nameof(StartsAtUtc), "Choose a future start time.");
        if (end - start < TimeSpan.FromMinutes(1) || end - start > TimeSpan.FromDays(7))
            throw new DomainValidationException(nameof(EndsAtUtc), "Contest duration must be between 1 minute and 7 days.");
        if (selections.Count > MaximumProblems || selections.Any(p => p.ProblemId == Guid.Empty || p.VersionId == Guid.Empty) ||
            selections.Select(p => p.ProblemId).Distinct().Count() != selections.Count ||
            selections.Select(p => p.VersionId).Distinct().Count() != selections.Count)
            throw new DomainValidationException(nameof(Problems), "Choose up to 26 distinct published problems.");
        var replacements = selections.Select((p, i) => ContestProblem.Create(Id, p, i + 1)).ToArray();
        Title = title.Trim(); Description = description?.Trim() ?? ""; StartsAtUtc = start; EndsAtUtc = end;
        problems.Clear(); problems.AddRange(replacements); Revision++;
    }

    public void Publish(DateTimeOffset now)
    {
        EnsureDraft(); EnsureUtc(now);
        if (StartsAtUtc <= now) throw new DomainValidationException(nameof(StartsAtUtc), "Choose a future start time before publishing.");
        if (problems.Count == 0) throw new DomainValidationException(nameof(Problems), "Add at least one published problem.");
        PublishedAtUtc = now; Revision++;
    }

    public ContestState StateAt(DateTimeOffset now) => PublishedAtUtc is null ? ContestState.Draft :
        now < StartsAtUtc ? ContestState.Scheduled : now < EndsAtUtc ? ContestState.Live : ContestState.Ended;

    public void EnsureCanRegister(Guid accountId, DateTimeOffset now)
    {
        EnsureUtc(now);
        if (accountId == Guid.Empty || accountId == OwnerAccountId)
            throw new DomainValidationException("Registration", "Organizers cannot enter their own contests.");
        if (PublishedAtUtc is null || now >= EndsAtUtc)
            throw new DomainValidationException("Registration", "Registration is closed.");
    }

    public void EnsureCanSubmit(Guid accountId, DateTimeOffset now)
    {
        EnsureCanRegister(accountId, now);
        if (StateAt(now) != ContestState.Live)
            throw new DomainValidationException("Contest", "Submissions are accepted only while the contest is live.");
    }

    private void EnsureDraft()
    {
        if (PublishedAtUtc is not null) throw new DomainValidationException("Contest", "A published contest cannot be changed.");
    }
    private static void EnsureUtc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero) throw new DomainValidationException("Time", "Use UTC timestamps.");
    }
}

public sealed record ContestProblemSelection(Guid ProblemId, Guid VersionId);

public sealed class ContestProblem
{
    private ContestProblem() { }
    public Guid Id { get; private set; }
    public Guid ContestId { get; private set; }
    public Guid ProblemId { get; private set; }
    public Guid VersionId { get; private set; }
    public int Order { get; private set; }
    internal static ContestProblem Create(Guid contest, ContestProblemSelection selection, int order) =>
        new() { Id = Guid.NewGuid(), ContestId = contest, ProblemId = selection.ProblemId, VersionId = selection.VersionId, Order = order };
}

public sealed class ContestRegistration
{
    private ContestRegistration() { }
    public Guid ContestId { get; private set; }
    public Guid AccountId { get; private set; }
    public DateTimeOffset RegisteredAtUtc { get; private set; }
    public static ContestRegistration Create(Contest contest, Guid accountId, DateTimeOffset now)
    {
        contest.EnsureCanRegister(accountId, now);
        return new() { ContestId = contest.Id, AccountId = accountId, RegisteredAtUtc = now };
    }
}

public sealed class ContestSubmission
{
    private ContestSubmission() { }
    public Guid SubmissionId { get; private set; }
    public Guid ContestId { get; private set; }
    public Guid AccountId { get; private set; }
    public Guid ContestProblemId { get; private set; }
    public static ContestSubmission Create(Contest contest, ContestRegistration registration, Guid problemId,
        Minos.Domain.Evaluation.Submission submission)
    {
        contest.EnsureCanSubmit(submission.OwnerAccountId, submission.CreatedAtUtc);
        var problem = contest.Problems.SingleOrDefault(p => p.Id == problemId);
        if (registration.ContestId != contest.Id || registration.AccountId != submission.OwnerAccountId ||
            problem is null || problem.VersionId != submission.ProblemVersionId || problem.ProblemId != submission.ProblemId)
            throw new DomainValidationException("Submission", "The submission must match a registered contest problem.");
        return new() { SubmissionId = submission.Id, ContestId = contest.Id, AccountId = registration.AccountId, ContestProblemId = problem.Id };
    }
}
