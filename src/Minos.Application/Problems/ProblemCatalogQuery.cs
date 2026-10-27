using Minos.Domain;

namespace Minos.Application.Problems;

public enum ProblemSolveStatus { NotAttempted, Attempted, Solved }
public enum ProblemDifficultyFilter { Unclassified, Easy, Medium, Hard }
public enum ProblemCatalogSort { Number, Newest, DifficultyAscending, DifficultyDescending, Title }
public enum ProblemFilterMatch { All, Any }
public sealed record ProblemCatalogQuery(int Page = 1, int PageSize = 20, string? Search = null,
    ProblemDifficultyFilter? Difficulty = null, string? Topic = null, ProblemSolveStatus? Status = null,
    ProblemCatalogSort Sort = ProblemCatalogSort.Number, ProblemFilterMatch Match = ProblemFilterMatch.All,
    bool DifficultyIsNot = false, bool TopicIsNot = false, bool StatusIsNot = false)
{
    public ProblemCatalogQuery Validate()
    {
        if (Page < 1 || PageSize is < 1 or > 100 || (long)(Page - 1) * PageSize > int.MaxValue)
            throw new DomainValidationException(nameof(Page), "Use a valid page and a page size between 1 and 100.");
        if (Search?.Length > 120 || Search?.Any(char.IsControl) == true ||
            (Difficulty is not null && !Enum.IsDefined(Difficulty.Value)) ||
            (Status is not null && !Enum.IsDefined(Status.Value)) || !Enum.IsDefined(Sort) || !Enum.IsDefined(Match) ||
            (DifficultyIsNot && Difficulty is null) || (TopicIsNot && Topic is null) || (StatusIsNot && Status is null) ||
            (Topic is not null && !Minos.Domain.Problems.ProblemTopic.ValidSlug(Topic)))
            throw new DomainValidationException(nameof(Search), "Invalid library filter.");
        return this with { Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim() };
    }
}
public sealed record ProblemProgressSummary(int Solved, int Attempted, int Total);
public sealed record ProblemTopicCountSummary(string Slug, string DisplayName, int ProblemCount);
public sealed record ProblemCatalogResult(IReadOnlyList<PublishedProblemSummary> Items, int Total, ProblemProgressSummary? Progress);
