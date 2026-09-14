using Minos.Application;
using Minos.Application.Problems;

namespace Minos.Infrastructure.Hosting;

// Only the explicitly opted-in development host grants access. This is not product authentication.
internal sealed class DeveloperProblemDraftAccess(bool enabled) : IProblemDraftAccess
{
    public Task EnsureCanCreateAsync(CancellationToken cancellationToken) => EnsureAsync(cancellationToken);
    public Task EnsureCanReadAsync(Guid draftId, CancellationToken cancellationToken) => EnsureAsync(cancellationToken);

    private Task EnsureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!enabled) throw new AccessDeniedException();
        return Task.CompletedTask;
    }
}
