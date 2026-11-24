using System.Diagnostics;
using RizToolbox.Sdk;

namespace RizToolbox.Core.Diagnostics;

/// <summary>One bounded host-owned collector. Null budget means observation only, pending machine baseline calibration.</summary>
public sealed class CallbackPerformanceMonitor : IPerformanceService
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly int _window, _maximumSeries, _consecutiveLimit;
    private readonly double? _budget;
    private readonly string _session = Guid.NewGuid().ToString("N");
    private readonly Dictionary<(string Owner, string Operation), Series> _series = new();
    private long _dropped;
    private sealed class Series
    {
        public double[] Samples = null!;
        public int Next, Count, Consecutive;
        public long Total, Allocated, MaximumAllocation, OverBudget;
        public double Maximum;
    }
    public CallbackPerformanceMonitor(int retainedSamplesPerSeries = 2048, int maximumSeries = 256,
        double? callbackBudgetMilliseconds = null, int consecutiveLimit = 3)
    {
        if (retainedSamplesPerSeries is < 16 or > 16384 || maximumSeries is < 1 or > 2048 ||
            callbackBudgetMilliseconds is double budget && (!double.IsFinite(budget) || budget <= 0 || budget > 30000) ||
            consecutiveLimit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(retainedSamplesPerSeries));
        (_window, _maximumSeries, _budget, _consecutiveLimit) = (retainedSamplesPerSeries, maximumSeries, callbackBudgetMilliseconds, consecutiveLimit);
    }
    public void Invoke(string owner, string operation, Action callback)
    {
        Verify(); ArgumentNullException.ThrowIfNull(callback); ValidateKey(owner, operation);
        long start = Stopwatch.GetTimestamp(), allocation = GC.GetAllocatedBytesForCurrentThread();
        bool completed = false, violation = false;
        try { callback(); completed = true; }
        finally
        {
            violation = Record(owner, operation, (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency,
                Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - allocation));
        }
        // A thrown callback keeps its original exception; only successful slow callbacks get the budget error.
        if (completed && violation) throw new CallbackBudgetExceededException(owner, operation, _budget!.Value, _consecutiveLimit);
    }
    public bool Record(string owner, string operation, double milliseconds, long allocatedBytes = 0, bool enforceBudget = true)
    {
        Verify(); ValidateKey(owner, operation);
        if (!double.IsFinite(milliseconds) || milliseconds < 0 || allocatedBytes < 0) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        var key = (owner, operation);
        if (!_series.TryGetValue(key, out var series))
        {
            if (_series.Count >= _maximumSeries) { _dropped++; return false; }
            _series.Add(key, series = new() { Samples = new double[_window] });
        }
        series.Samples[series.Next] = milliseconds; series.Next = (series.Next + 1) % _window;
        series.Count = Math.Min(_window, series.Count + 1); series.Total++;
        series.Allocated += allocatedBytes; series.MaximumAllocation = Math.Max(series.MaximumAllocation, allocatedBytes);
        series.Maximum = Math.Max(series.Maximum, milliseconds);
        bool over = enforceBudget && _budget is double budget && milliseconds > budget;
        if (over) { series.OverBudget++; series.Consecutive++; } else series.Consecutive = 0;
        return over && series.Consecutive >= _consecutiveLimit;
    }
    /// <summary>Copies bounded raw windows on the collection thread, without sorting or retaining the collector.</summary>
    public PerformanceWindowCopy CaptureWindow()
    {
        Verify();
        var capturedAt = DateTimeOffset.UtcNow;
        var result = new CallbackPerformanceWindowCopy[_series.Count];
        var index = 0;
        foreach (var item in _series)
        {
            var s = item.Value;
            var samples = new double[s.Count];
            var start = s.Count == _window ? s.Next : 0;
            var first = Math.Min(s.Count, _window - start);
            Array.Copy(s.Samples, start, samples, 0, first);
            if (first < s.Count) Array.Copy(s.Samples, 0, samples, first, s.Count - first);
            result[index++] = new(item.Key.Owner, item.Key.Operation, s.Total, s.Maximum,
                s.Allocated, s.MaximumAllocation, s.OverBudget, s.Consecutive, samples);
        }
        return new(_session, capturedAt, _window, _maximumSeries, _dropped, _budget, _consecutiveLimit, result);
    }

    /// <summary>Summarizes a detached copy on any thread. Sorting affects only local arrays and preserves capture time.</summary>
    public static PerformanceSnapshot Summarize(PerformanceWindowCopy copy)
    {
        ArgumentNullException.ThrowIfNull(copy);
        var result = new CallbackPerformance[copy.Callbacks.Count];
        for (var i = 0; i < result.Length; i++)
        {
            var s = copy.Callbacks[i];
            var sorted = s.CopySamples();
            Array.Sort(sorted);
            result[i] = new(s.Owner, s.Operation, s.TotalSamples, s.WindowSamples,
                Percentile(sorted, .5), Percentile(sorted, .95), Percentile(sorted, .99), s.MaximumMilliseconds,
                s.TotalAllocatedBytes, s.MaximumAllocatedBytes, s.OverBudget, s.ConsecutiveOverBudget);
        }
        Array.Sort(result, static (left, right) =>
        {
            var owner = StringComparer.Ordinal.Compare(left.Owner, right.Owner);
            return owner != 0 ? owner : StringComparer.Ordinal.Compare(left.Operation, right.Operation);
        });
        return new(copy.SessionId, copy.CapturedAtUtc, copy.RetainedSamplesPerSeries, copy.MaximumSeries,
            copy.DroppedSeriesSamples, copy.CallbackBudgetMilliseconds, copy.ConsecutiveLimit, Array.AsReadOnly(result));
    }

    public PerformanceSnapshot GetSnapshot() => Summarize(CaptureWindow());

    private static double Percentile(double[] sorted, double rank)
        => sorted[Math.Max(0, (int)Math.Ceiling(sorted.Length * rank) - 1)];
    private void Verify() { if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("Performance collection requires the verified host thread."); }
    private static void ValidateKey(string owner, string operation)
    {
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(operation) || owner.Length > 128 || operation.Length > 512 ||
            owner.Any(char.IsControl) || operation.Any(char.IsControl)) throw new ArgumentException("Bounded owner and operation identities are required.");
    }
}
public sealed class CallbackBudgetExceededException : InvalidOperationException
{
    public CallbackBudgetExceededException(string owner, string operation, double budget, int consecutive)
        : base($"CallbackBudgetExceeded: '{owner}' '{operation}' exceeded {budget} ms for {consecutive} consecutive calls; the completed callback is not rolled back.") { }
}
