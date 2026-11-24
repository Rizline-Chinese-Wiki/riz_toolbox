using System.Collections.ObjectModel;

namespace RizToolbox.Core.Diagnostics;

/// <summary>Immutable bounded managed data captured before background diagnostic work.</summary>
public sealed class PerformanceWindowCopy
{
    public string SessionId { get; }
    public DateTimeOffset CapturedAtUtc { get; }
    public int RetainedSamplesPerSeries { get; }
    public int MaximumSeries { get; }
    public long DroppedSeriesSamples { get; }
    public double? CallbackBudgetMilliseconds { get; }
    public int ConsecutiveLimit { get; }
    public IReadOnlyList<CallbackPerformanceWindowCopy> Callbacks { get; }

    // Both constructors receive exclusively owned copies, never collector ring arrays.
    internal PerformanceWindowCopy(string sessionId, DateTimeOffset capturedAtUtc, int retainedSamplesPerSeries,
        int maximumSeries, long droppedSeriesSamples, double? callbackBudgetMilliseconds, int consecutiveLimit,
        CallbackPerformanceWindowCopy[] callbacks)
    {
        SessionId = sessionId;
        CapturedAtUtc = capturedAtUtc;
        RetainedSamplesPerSeries = retainedSamplesPerSeries;
        MaximumSeries = maximumSeries;
        DroppedSeriesSamples = droppedSeriesSamples;
        CallbackBudgetMilliseconds = callbackBudgetMilliseconds;
        ConsecutiveLimit = consecutiveLimit;
        Callbacks = Array.AsReadOnly(callbacks);
    }
}

/// <summary>One detached recent window in oldest-to-newest order, with all-time counters.</summary>
public sealed class CallbackPerformanceWindowCopy
{
    private readonly ReadOnlyCollection<double> _samples;
    public string Owner { get; }
    public string Operation { get; }
    public long TotalSamples { get; }
    public int WindowSamples => _samples.Count;
    public double MaximumMilliseconds { get; }
    public long TotalAllocatedBytes { get; }
    public long MaximumAllocatedBytes { get; }
    public long OverBudget { get; }
    public int ConsecutiveOverBudget { get; }
    public IReadOnlyList<double> Samples => _samples;

    internal CallbackPerformanceWindowCopy(string owner, string operation, long totalSamples,
        double maximumMilliseconds, long totalAllocatedBytes, long maximumAllocatedBytes,
        long overBudget, int consecutiveOverBudget, double[] samples)
    {
        Owner = owner;
        Operation = operation;
        TotalSamples = totalSamples;
        MaximumMilliseconds = maximumMilliseconds;
        TotalAllocatedBytes = totalAllocatedBytes;
        MaximumAllocatedBytes = maximumAllocatedBytes;
        OverBudget = overBudget;
        ConsecutiveOverBudget = consecutiveOverBudget;
        _samples = Array.AsReadOnly(samples);
    }

    internal double[] CopySamples()
    {
        var samples = new double[_samples.Count];
        _samples.CopyTo(samples, 0);
        return samples;
    }
}
