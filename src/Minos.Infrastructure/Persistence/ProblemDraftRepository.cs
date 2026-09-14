using Microsoft.EntityFrameworkCore;
using Minos.Application.Problems;
using Minos.Domain.Problems;

namespace Minos.Infrastructure.Persistence;

internal sealed class ProblemDraftRepository(MinosDbContext database) : IProblemDraftRepository
{
    public async Task AddAsync(ProblemDraft draft, CancellationToken cancellationToken)
    {
        database.ProblemDrafts.Add(draft);
        // One aggregate insert is the transaction boundary; SaveChanges commits it atomically.
        await database.SaveChangesAsync(cancellationToken);
    }

    public Task<ProblemDraft?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        database.ProblemDrafts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
}
