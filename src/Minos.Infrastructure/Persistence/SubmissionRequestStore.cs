using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Minos.Application.Evaluation;
using Minos.Domain.Evaluation;

namespace Minos.Infrastructure.Persistence;

internal sealed class SubmissionRequestRecord
{
    public Guid AccountId { get; init; }
    public Guid Key { get; init; }
    public string Fingerprint { get; init; } = "";
    public Guid SubmissionId { get; init; }
}

internal sealed class SubmissionRequestConfiguration : IEntityTypeConfiguration<SubmissionRequestRecord>
{
    public void Configure(EntityTypeBuilder<SubmissionRequestRecord> request)
    {
        request.ToTable("submission_requests", "evaluation", table =>
        {
            table.HasCheckConstraint("ck_submission_requests_key", "request_key <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("ck_submission_requests_fingerprint", "fingerprint ~ '^[0-9a-f]{64}$'");
        });
        request.HasKey(x => new { x.AccountId, x.Key });
        request.Property(x => x.AccountId).HasColumnName("account_id").ValueGeneratedNever();
        request.Property(x => x.Key).HasColumnName("request_key").ValueGeneratedNever();
        request.Property(x => x.Fingerprint).HasColumnName("fingerprint").HasMaxLength(64).IsRequired();
        request.Property(x => x.SubmissionId).HasColumnName("submission_id");
        request.HasOne<Submission>().WithMany().HasForeignKey(x => new { x.SubmissionId, x.AccountId })
            .HasPrincipalKey(x => new { x.Id, x.OwnerAccountId }).OnDelete(DeleteBehavior.Restrict);
        request.HasIndex(x => x.SubmissionId).IsUnique();
    }
}

internal static class SubmissionRequestStore
{
    public static async Task<Guid?> FindAsync(MinosDbContext database, Guid ownerAccountId,
        SubmissionRequestIdentity request, CancellationToken token)
    {
        var found = await database.SubmissionRequests.AsNoTracking()
            .SingleOrDefaultAsync(x => x.AccountId == ownerAccountId && x.Key == request.Key, token);
        if (found is null) return null;
        if (found.Fingerprint != request.Fingerprint) throw new SubmissionRequestConflictException();
        return found.SubmissionId;
    }

    public static void Add(MinosDbContext database, Submission submission, SubmissionRequestIdentity request) =>
        database.SubmissionRequests.Add(new() { AccountId = submission.OwnerAccountId, Key = request.Key,
            Fingerprint = request.Fingerprint, SubmissionId = submission.Id });
}
