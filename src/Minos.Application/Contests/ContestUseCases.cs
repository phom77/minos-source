using Minos.Application.Accounts;
using Minos.Application.Evaluation;
using Minos.Application.Problems;
using Minos.Domain;
using Minos.Domain.Accounts;
using Minos.Domain.Contests;
using Minos.Domain.Evaluation;

namespace Minos.Application.Contests;

public sealed class ContestUseCases(IContestRepository repository, IProblemRepository problems,
    AccountUseCases accounts, ICurrentAccount current, TimeProvider clock, ISubmissionRepository submissions)
{
    public async Task<ContestPage> ListAsync(bool mine, int page, int pageSize, CancellationToken token)
    {
        var offset = Offset(page, pageSize);
        Guid? owner = mine ? (await AuthorAsync(token)).Id : null;
        var (items, total) = await repository.ListAsync(owner, offset, pageSize, token);
        var now = clock.GetUtcNow();
        return new(items.Select(c => new ContestSummary(c.Id, c.Title, c.StartsAtUtc, c.EndsAtUtc,
            c.StateAt(now), c.Problems.Count)).ToArray(), page, pageSize, total);
    }

    public async Task<ContestDetails> CreateAsync(SaveContest request, CancellationToken token)
    {
        var author = await AuthorAsync(token);
        var now = clock.GetUtcNow();
        var contest = Contest.Create(author.Id, request.Title, request.Description, request.StartsAtUtc, request.EndsAtUtc, now);
        contest.Edit(request.Title, request.Description, request.StartsAtUtc, request.EndsAtUtc,
            await SelectionsAsync(request.VersionIds, token), now);
        await repository.AddAsync(contest, token);
        return await DetailsAsync(contest, author, token);
    }

    public async Task<ContestDetails> UpdateAsync(Guid id, SaveContest request, CancellationToken token)
    {
        var (contest, author) = await OwnedAsync(id, request.Revision, token);
        contest.Edit(request.Title, request.Description, request.StartsAtUtc, request.EndsAtUtc,
            await SelectionsAsync(request.VersionIds, token), clock.GetUtcNow());
        await repository.SaveAsync(contest, token);
        return await DetailsAsync(contest, author, token);
    }

    public async Task<ContestDetails> PublishAsync(Guid id, long revision, CancellationToken token)
    {
        var (contest, author) = await OwnedAsync(id, revision, token);
        await SelectionsAsync(contest.Problems.Select(p => p.VersionId).ToArray(), token);
        contest.Publish(clock.GetUtcNow());
        await repository.SaveAsync(contest, token);
        return await DetailsAsync(contest, author, token);
    }

    public async Task<ContestDetails> FindAsync(Guid id, CancellationToken token)
    {
        var account = current.AccountId.HasValue ? await accounts.RequireActiveAsync(token) : null;
        var contest = await VisibleAsync(id, account, token);
        return await DetailsAsync(contest, account, token);
    }

    public async Task<ContestDetails> RegisterAsync(Guid id, CancellationToken token)
    {
        var account = await accounts.RequireActiveAsync(token);
        var contest = await VisibleAsync(id, account, token);
        var existing = await repository.FindRegistrationAsync(id, account.Id, token);
        if (existing is null)
            await repository.RegisterAsync(ContestRegistration.Create(contest, account.Id, clock.GetUtcNow()), token);
        return await DetailsAsync(contest, account, token);
    }

    public async Task<PublishedProblemDetails> ProblemAsync(Guid id, Guid contestProblemId, CancellationToken token)
    {
        var account = await accounts.RequireActiveAsync(token);
        var contest = await VisibleAsync(id, account, token);
        var state = contest.StateAt(clock.GetUtcNow());
        if (contest.OwnerAccountId != account.Id &&
            (state is ContestState.Draft or ContestState.Scheduled ||
             await repository.FindRegistrationAsync(id, account.Id, token) is null)) throw new AccessDeniedException();
        var problem = contest.Problems.SingleOrDefault(p => p.Id == contestProblemId) ?? throw new ResourceNotFoundException();
        var version = await problems.FindPublishedVersionAsync(problem.VersionId, token) ?? throw new ResourceNotFoundException();
        return ProblemAuthoringUseCases.MapPublished(version);
    }

    public async Task<Guid> SubmitAsync(Guid id, Guid contestProblemId, CreateSubmission request, CancellationToken token)
    {
        var account = await accounts.RequireActiveAsync(token);
        var contest = await VisibleAsync(id, account, token);
        var registration = await repository.FindRegistrationAsync(id, account.Id, token) ?? throw new AccessDeniedException();
        var identity = SubmissionRequestIdentity.Create(request, contestProblemId, id);
        // An authorized replay may return accepted work after the contest ends; it never creates new work.
        if (identity is not null && await submissions.FindRequestAsync(account.Id, identity, token) is { } replay)
            return replay;
        if (request.Language != SupportedSubmission.Language)
            throw new DomainValidationException(nameof(request.Language), "Only cpp20 is supported.");
        var problem = contest.Problems.SingleOrDefault(p => p.Id == contestProblemId) ?? throw new ResourceNotFoundException();
        var submission = Submission.Create(account.Id, problem.ProblemId, problem.VersionId, SupportedSubmission.Language,
            SupportedSubmission.ToolchainId, request.Source, clock.GetUtcNow());
        if (identity is not null)
            return await repository.AdmitOnceAsync(contest, registration, contestProblemId, submission, JudgeJob.Create(submission.Id, submission.CreatedAtUtc), identity, token)
                ?? throw new AdmissionLimitExceededException();
        var association = ContestSubmission.Create(contest, registration, contestProblemId, submission);
        if (!await repository.AdmitAsync(contest, association, submission, JudgeJob.Create(submission.Id, submission.CreatedAtUtc), token))
            throw new AdmissionLimitExceededException();
        return submission.Id;
    }

    public async Task<ContestSubmissionPage> SubmissionsAsync(Guid id, int page, int pageSize, CancellationToken token)
    {
        var offset = Offset(page, pageSize);
        var account = await accounts.RequireActiveAsync(token);
        await VisibleAsync(id, account, token);
        var (items, total) = await repository.ListSubmissionsAsync(id, account.Id, offset, pageSize, token);
        return new(items, page, pageSize, total);
    }

    private async Task<Account> AuthorAsync(CancellationToken token)
    {
        var account = await accounts.RequireActiveAsync(token);
        if (!account.CanAuthorProblems) throw new AccessDeniedException();
        return account;
    }
    private async Task<(Contest, Account)> OwnedAsync(Guid id, long? revision, CancellationToken token)
    {
        var account = await AuthorAsync(token);
        var contest = await repository.FindAsync(id, token) ?? throw new ResourceNotFoundException();
        if (contest.OwnerAccountId != account.Id) throw new AccessDeniedException();
        if (revision != contest.Revision) throw new ConcurrencyConflictException();
        return (contest, account);
    }
    private async Task<Contest> VisibleAsync(Guid id, Account? account, CancellationToken token)
    {
        var contest = await repository.FindAsync(id, token) ?? throw new ResourceNotFoundException();
        if (contest.PublishedAtUtc is null && (contest.OwnerAccountId != account?.Id || !account.CanAuthorProblems))
            throw new ResourceNotFoundException();
        return contest;
    }
    private async Task<IReadOnlyList<ContestProblemSelection>> SelectionsAsync(IReadOnlyList<Guid> ids, CancellationToken token)
    {
        if (ids.Count > Contest.MaximumProblems) throw new DomainValidationException("Problems", "Choose at most 26 problems.");
        var selections = new List<ContestProblemSelection>();
        foreach (var id in ids)
        {
            var version = await problems.FindPublishedVersionAsync(id, token) ?? throw new ResourceNotFoundException();
            if (await problems.FindLatestPublishedAsync(version.ProblemId, token) is null)
                throw new DomainValidationException("Problems", "An archived problem cannot be selected for publication.");
            selections.Add(new(version.ProblemId, version.Id));
        }
        return selections;
    }
    private async Task<ContestDetails> DetailsAsync(Contest contest, Account? account, CancellationToken token)
    {
        var now = clock.GetUtcNow(); var state = contest.StateAt(now);
        var owner = contest.OwnerAccountId == account?.Id;
        var registered = account is not null && await repository.FindRegistrationAsync(contest.Id, account.Id, token) is not null;
        var items = new List<ContestProblemDetails>();
        if (owner || state is ContestState.Live or ContestState.Ended)
            foreach (var problem in contest.Problems.OrderBy(p => p.Order))
            {
                var version = await problems.FindPublishedVersionAsync(problem.VersionId, token) ?? throw new ResourceNotFoundException();
                items.Add(new(problem.Id, problem.ProblemId, problem.VersionId, problem.Order, version.Title));
            }
        return new(contest.Id, contest.Title, contest.Description, contest.StartsAtUtc, contest.EndsAtUtc, state,
            contest.Revision, owner && account!.CanAuthorProblems, registered,
            account is not null && !owner && !registered && state is ContestState.Scheduled or ContestState.Live,
            registered && state == ContestState.Live, contest.Problems.Count, now, items);
    }
    private static int Offset(int page, int size)
    {
        if (page < 1 || size is < 1 or > 100 || (long)(page - 1) * size > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(page));
        return (page - 1) * size;
    }
}
