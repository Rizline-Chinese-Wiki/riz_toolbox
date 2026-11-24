namespace RizToolbox.Sdk;

public static class PerformanceCapabilityIds { public const string Read = "riz.performance.read"; }
public sealed record CallbackPerformance(string Owner, string Operation, long TotalSamples, int WindowSamples,
    double P50Milliseconds, double P95Milliseconds, double P99Milliseconds, double MaximumMilliseconds,
    long TotalAllocatedBytes, long MaximumAllocatedBytes, long OverBudget, int ConsecutiveOverBudget);
public sealed record PerformanceSnapshot(string SessionId, DateTimeOffset CapturedAtUtc, int RetainedSamplesPerSeries,
    int MaximumSeries, long DroppedSeriesSamples, double? CallbackBudgetMilliseconds, int ConsecutiveLimit,
    IReadOnlyList<CallbackPerformance> Callbacks);
/// <summary>Bounded recent callback windows; p50/p95/p99 are nearest-rank estimates of that window.
/// Durations and thread allocations include nested callbacks. This is not a GPU or whole-game frame measurement.</summary>
public interface IPerformanceService { PerformanceSnapshot GetSnapshot(); }
