using Microsoft.EntityFrameworkCore;
using Minos.Application.Evaluation;
using Minos.Domain.Evaluation;

namespace Minos.Infrastructure.Persistence;

internal sealed class SubmissionPerformanceQuery(MinosDbContext database) : ISubmissionPerformanceQuery
{
    public Task<SubmissionPerformanceTarget?> FindTargetAsync(Guid id, CancellationToken token) =>
        (from submission in database.Submissions.AsNoTracking()
         join result in database.SubmissionResults.AsNoTracking() on submission.Id equals result.SubmissionId into results
         from result in results.DefaultIfEmpty()
         where submission.Id == id
         select new SubmissionPerformanceTarget(submission.Id, submission.OwnerAccountId, submission.ProblemVersionId, submission.Language,
             submission.ToolchainId, submission.Status, result == null ? null : result.Verdict, result == null ? null : result.MaximumCpuTimeMilliseconds,
             result == null ? null : result.MaximumMemoryBytes, result == null ? null : result.MeasurementProfileId)).SingleOrDefaultAsync(token);

    public async Task<SubmissionPerformanceDistribution> ReadDistributionAsync(SubmissionPerformanceTarget target, CancellationToken token)
    {
        // Filter by the immutable cohort first, choose one latest Accepted per OTHER account,
        // then aggregate inside PostgreSQL. Only 40 rows cross the application boundary.
        var rows = await database.Database.SqlQuery<PerformanceRow>($"""
            WITH peers AS MATERIALIZED (
                SELECT DISTINCT ON (s.owner_account_id)
                    r.maximum_cpu_time_ms AS cpu, r.maximum_memory_bytes AS memory
                FROM evaluation.submissions s
                JOIN evaluation.submission_results r ON r.submission_id = s.id
                WHERE s.problem_version_id = {target.ProblemVersionId} AND s.language = {target.Language}
                    AND s.toolchain_id = {target.ToolchainId} AND s.status = 2 AND r.verdict = 0
                    AND r.measurement_profile_id = {target.MeasurementProfileId}
                    AND s.owner_account_id <> {target.OwnerAccountId}
                ORDER BY s.owner_account_id, r.completed_at_utc DESC, s.id DESC
            ), stats AS (
                SELECT count(*) AS samples,
                    count(*) FILTER (WHERE cpu > {target.RuntimeMilliseconds}) AS cpu_slower,
                    count(*) FILTER (WHERE cpu = {target.RuntimeMilliseconds}) AS cpu_equal,
                    count(*) FILTER (WHERE memory > {target.MemoryBytes}) AS memory_slower,
                    count(*) FILTER (WHERE memory = {target.MemoryBytes}) AS memory_equal,
                    greatest(coalesce(max(cpu), 0), {target.RuntimeMilliseconds})::numeric + 1 AS cpu_max,
                    greatest(coalesce(max(memory), 0), {target.MemoryBytes})::numeric + 1 AS memory_max
                FROM peers
            ), counts AS (
                SELECT metric, floor(value / range_max * {SubmissionPerformanceUseCases.BucketCount})::integer AS bucket, count(*) AS total
                FROM peers CROSS JOIN stats
                CROSS JOIN LATERAL (VALUES (0, cpu::numeric, cpu_max), (1, memory::numeric, memory_max)) m(metric, value, range_max)
                WHERE samples >= {SubmissionPerformanceUseCases.MinimumSamples}
                GROUP BY metric, bucket
            )
            SELECT m.metric AS "Metric", b.bucket AS "Bucket", coalesce(c.total, 0) AS "Count", stats.samples AS "Samples",
                CASE WHEN m.metric = 0 THEN cpu_slower ELSE memory_slower END AS "Slower",
                CASE WHEN m.metric = 0 THEN cpu_equal ELSE memory_equal END AS "Equal",
                CASE WHEN m.metric = 0 THEN cpu_max ELSE memory_max END AS "RangeMaximum"
            FROM stats CROSS JOIN generate_series(0, 1) m(metric)
            CROSS JOIN generate_series(0, {SubmissionPerformanceUseCases.BucketCount - 1}) b(bucket)
            LEFT JOIN counts c ON c.metric = m.metric AND c.bucket = b.bucket
            ORDER BY m.metric, b.bucket
            """).ToArrayAsync(token);
        var samples = rows[0].Samples;
        PerformanceMetric Metric(int metric, long value)
        {
            var data = rows.Where(row => row.Metric == metric).ToArray();
            return samples < SubmissionPerformanceUseCases.MinimumSamples ? new(value, null, null, 0, [])
                : new(value, decimal.Round(data[0].Slower * 100m / samples, 2, MidpointRounding.AwayFromZero), data[0].Equal, data[0].RangeMaximum,
                    data.Select(row => new PerformanceBucket(row.Bucket, row.Count)).ToArray());
        }
        return new(samples, Metric(0, target.RuntimeMilliseconds!.Value), Metric(1, target.MemoryBytes!.Value));
    }

    private sealed class PerformanceRow
    {
        public int Metric { get; set; }
        public int Bucket { get; set; }
        public long Count { get; set; }
        public long Samples { get; set; }
        public long Slower { get; set; }
        public long Equal { get; set; }
        public decimal RangeMaximum { get; set; }
    }
}
