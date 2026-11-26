using System.Diagnostics;
using System.Globalization;
using RizToolbox.Core.Diagnostics;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter;

internal sealed class AdapterSlowDiagnostics
{
    private const int ReportsPerWindow = 4;
    private static readonly long SlowThresholdTicks = Stopwatch.Frequency / 10;
    private static readonly long ReportWindowTicks = 10 * Stopwatch.Frequency;

    private readonly ILogger _log;
    private readonly Func<long> _timestamp;
    private ReportWindow _phases, _activations;

    internal AdapterSlowDiagnostics(ILogger log, Func<long>? timestamp = null)
    {
        _log = log;
        _timestamp = timestamp ?? Stopwatch.GetTimestamp;
    }

    internal readonly record struct Measurement(long Timestamp, long Allocated);

    internal Measurement Start() => new(_timestamp(), GC.GetAllocatedBytesForCurrentThread());

    internal void ObservePhase(Measurement start, long hostTickSequence, string phase, bool completed)
    {
        if (!TryMeasure(start, phase, ref _phases, out var elapsedMs, out var allocated)) return;

        var outcome = completed ? "completed" : "faulted";
        _log.TryLog(LogLevel.Warning, string.Create(CultureInfo.InvariantCulture,
            $"Slow adapter phase '{phase}' hostTick={hostTickSequence} elapsedMs={elapsedMs:F1} allocatedBytes={allocated} outcome={outcome}."));
    }

    internal void ObserveActivation(Measurement start, long hostTickSequence, string moduleId, int attempt, AdapterModuleState state)
    {
        if (!TryMeasure(start, moduleId, ref _activations, out var elapsedMs, out var allocated)) return;

        _log.TryLog(LogLevel.Warning, string.Create(CultureInfo.InvariantCulture,
            $"Slow adapter activation '{moduleId}' hostTick={hostTickSequence} elapsedMs={elapsedMs:F1} allocatedBytes={allocated} attempt={attempt} state={state}."));
    }

    private bool TryMeasure(Measurement start, string name, ref ReportWindow window, out double elapsedMs, out long allocated)
    {
        elapsedMs = 0;
        allocated = 0;
        var now = _timestamp();
        var elapsed = now - start.Timestamp;
        if (elapsed < SlowThresholdTicks) return false;

        allocated = GC.GetAllocatedBytesForCurrentThread() - start.Allocated;
        if (!window.TryAdmit(name, now)) return false;
        elapsedMs = elapsed * 1000d / Stopwatch.Frequency;
        return true;
    }

    private struct ReportWindow
    {
        private long _resetAt;
        private HashSet<string>? _names;

        // Activation has a separate allowance so retries cannot crowd out other phases.
        internal bool TryAdmit(string name, long now)
        {
            if (now >= _resetAt)
            {
                _resetAt = now + ReportWindowTicks;
                _names?.Clear();
            }

            _names ??= new(StringComparer.Ordinal);
            return _names.Count < ReportsPerWindow && _names.Add(name);
        }
    }
}
