namespace Minos.Application.Problems;

public interface IProblemDraftAccess
{
    Task EnsureCanCreateAsync(CancellationToken cancellationToken);
    Task EnsureCanReadAsync(Guid draftId, CancellationToken cancellationToken);
}
