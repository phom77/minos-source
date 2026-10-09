using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Minos.Infrastructure.Evaluation;

// Only fixed stage/phase names belong in these labels, never job IDs or private contents.
internal static class JudgingMetrics
{
    private static readonly Meter Meter = new("Minos.Judging");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        "minos.judging.phase_duration", "s");
    private static readonly Histogram<long> CompilerMemory = Meter.CreateHistogram<long>("minos.judging.compiler_memory_peak", "By");
    internal static void RecordCompilerMemory(long bytes) => CompilerMemory.Record(bytes);

    internal static Measurement Measure(string stage, string phase) => new(stage, phase);

    internal sealed class Measurement(string stage, string phase) : IDisposable
    {
        private readonly long started = Stopwatch.GetTimestamp();
        public void Dispose() => Duration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds,
            new KeyValuePair<string, object?>("stage", stage),
            new KeyValuePair<string, object?>("phase", phase));
    }
}
