using Minos.Domain;
using Minos.Domain.Contests;
using Xunit;

namespace Minos.Domain.Tests;

public sealed class ContestTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);
    private static Contest Draft() => Contest.Create(Guid.NewGuid(), "  Weekly  ", "Description", Now.AddHours(1), Now.AddHours(2), Now);
    private static Contest Published()
    {
        var c = Draft(); c.Edit(c.Title, c.Description, c.StartsAtUtc, c.EndsAtUtc, [new(Guid.NewGuid(), Guid.NewGuid())], Now); c.Publish(Now); return c;
    }
    [Fact]
    public void Lifecycle_uses_start_inclusive_and_end_exclusive_boundaries()
    {
        var c = Published();
        Assert.Equal("Weekly", c.Title);
        Assert.Equal(ContestState.Scheduled, c.StateAt(c.StartsAtUtc.AddTicks(-1)));
        Assert.Equal(ContestState.Live, c.StateAt(c.StartsAtUtc));
        Assert.Equal(ContestState.Live, c.StateAt(c.EndsAtUtc.AddTicks(-1)));
        Assert.Equal(ContestState.Ended, c.StateAt(c.EndsAtUtc));
        var user = Guid.NewGuid();
        Assert.Throws<DomainValidationException>(() => c.EnsureCanSubmit(user, c.StartsAtUtc.AddTicks(-1)));
        c.EnsureCanSubmit(user, c.StartsAtUtc);
        Assert.Throws<DomainValidationException>(() => c.EnsureCanSubmit(user, c.EndsAtUtc));
    }
    [Fact]
    public void Publication_requires_problems_and_freezes_schedule_and_selection()
    {
        Assert.Throws<DomainValidationException>(() => Draft().Publish(Now));
        var c = Published(); var pinned = c.Problems.Single().VersionId;
        Assert.Throws<DomainValidationException>(() => c.Edit("New", "", c.StartsAtUtc, c.EndsAtUtc, [], Now));
        Assert.Throws<DomainValidationException>(() => c.Publish(Now));
        Assert.Equal(pinned, c.Problems.Single().VersionId);
    }
    [Fact]
    public void Registration_excludes_organizer_draft_and_end_but_allows_late_join()
    {
        var c = Published(); var user = Guid.NewGuid();
        Assert.Throws<DomainValidationException>(() => ContestRegistration.Create(c, c.OwnerAccountId, Now));
        Assert.Throws<DomainValidationException>(() => ContestRegistration.Create(Draft(), user, Now));
        Assert.Throws<DomainValidationException>(() => ContestRegistration.Create(c, user, c.EndsAtUtc));
        Assert.Equal(user, ContestRegistration.Create(c, user, c.StartsAtUtc.AddMinutes(1)).AccountId);
    }
    [Fact]
    public void Invalid_replacement_is_atomic_and_duplicate_problem_versions_are_rejected()
    {
        var c = Draft(); var p = Guid.NewGuid();
        c.Edit("Saved", "", c.StartsAtUtc, c.EndsAtUtc, [new(p, Guid.NewGuid())], Now);
        var revision = c.Revision;
        Assert.Throws<DomainValidationException>(() => c.Edit("Wrong", "", c.StartsAtUtc, c.EndsAtUtc,
            [new(p, Guid.NewGuid()), new(p, Guid.NewGuid())], Now));
        Assert.Equal("Saved", c.Title); Assert.Equal(revision, c.Revision); Assert.Single(c.Problems);
    }
    [Theory]
    [InlineData(0)] [InlineData(59)] [InlineData(604801)]
    public void Duration_is_bounded(int seconds) => Assert.Throws<DomainValidationException>(() =>
        Contest.Create(Guid.NewGuid(), "Title", "", Now.AddHours(1), Now.AddHours(1).AddSeconds(seconds), Now));
    [Fact]
    public void Schedule_requires_future_utc_and_selection_is_bounded()
    {
        Assert.Throws<DomainValidationException>(() => Contest.Create(Guid.NewGuid(), "Title", "", Now, Now.AddHours(1), Now));
        Assert.Throws<DomainValidationException>(() => Contest.Create(Guid.NewGuid(), "Title", "", Now.AddHours(1).ToOffset(TimeSpan.FromHours(7)), Now.AddHours(2), Now));
        var c = Draft();
        Assert.Throws<DomainValidationException>(() => c.Edit(c.Title, "", c.StartsAtUtc, c.EndsAtUtc,
            Enumerable.Range(0, 27).Select(_ => new ContestProblemSelection(Guid.NewGuid(), Guid.NewGuid())).ToArray(), Now));
    }
}
