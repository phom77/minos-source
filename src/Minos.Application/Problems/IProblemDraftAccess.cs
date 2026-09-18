using Minos.Domain.Problems;

namespace Minos.Application.Problems;

public interface IProblemDraftAccess
{
    Task<Guid> RequireActiveAccountAsync(CancellationToken cancellationToken);
    void EnsureCanRead(ProblemDraft draft, Guid accountId);
}
