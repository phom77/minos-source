using Minos.Domain.Problems;

namespace Minos.Application.Problems;

public interface IProblemDraftRepository
{
    Task AddAsync(ProblemDraft draft, CancellationToken cancellationToken);
    Task<ProblemDraft?> FindAsync(Guid id, CancellationToken cancellationToken);
}
