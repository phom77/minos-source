using Minos.Application.Accounts;
using Minos.Domain.Evaluation;

namespace Minos.Application.Evaluation;

public interface ISubmissionPerformanceQuery
{
    Task<SubmissionPerformanceTarget?> FindTargetAsync(Guid id, CancellationToken token);
    Task<SubmissionPerformanceDistribution> ReadDistributionAsync(SubmissionPerformanceTarget target, CancellationToken token);
}

public sealed record SubmissionPerformanceTarget(Guid Id, Guid OwnerAccountId, Guid ProblemVersionId, string Language,
    string ToolchainId, SubmissionStatus Status, SubmissionVerdict? Verdict, long? RuntimeMilliseconds, long? MemoryBytes, string? MeasurementProfileId);
public sealed record PerformanceBucket(int Index, long Count);
public sealed record PerformanceMetric(long Value, decimal? BeatsPercentage, long? EqualCount, decimal RangeMaximum, IReadOnlyList<PerformanceBucket> Buckets);
public sealed record SubmissionPerformanceDistribution(long SampleCount, PerformanceMetric Runtime, PerformanceMetric Memory);
public sealed record SubmissionPerformance(string Status, long SampleCount, int MinimumSampleCount, PerformanceMetric? Runtime, PerformanceMetric? Memory);

public sealed class SubmissionPerformanceUseCases(ISubmissionPerformanceQuery query, AccountUseCases accounts)
{
    public const int MinimumSamples = 20;
    public const int BucketCount = 20;

    public async Task<SubmissionPerformance> ReadAsync(Guid id, CancellationToken token)
    {
        var account = await accounts.RequireActiveAsync(token);
        var target = await query.FindTargetAsync(id, token) ?? throw new ResourceNotFoundException();
        if (target.OwnerAccountId != account.Id) throw new AccessDeniedException();
        static PerformanceMetric? Measurement(long? value) => value is null ? null : new(value.Value, null, null, 0, []);
        if (target.Status != SubmissionStatus.Completed || target.Verdict != SubmissionVerdict.Accepted)
            return new("NotAccepted", 0, MinimumSamples, Measurement(target.RuntimeMilliseconds), Measurement(target.MemoryBytes));
        if (target.MeasurementProfileId is null || target.RuntimeMilliseconds is null || target.MemoryBytes is null)
            return new("Unprofiled", 0, MinimumSamples, Measurement(target.RuntimeMilliseconds), Measurement(target.MemoryBytes));
        var distribution = await query.ReadDistributionAsync(target, token);
        return new(distribution.SampleCount >= MinimumSamples ? "Ready" : "InsufficientSamples", distribution.SampleCount, MinimumSamples,
            distribution.Runtime, distribution.Memory);
    }
}
