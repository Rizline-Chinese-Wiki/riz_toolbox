using System.Diagnostics;
using RizToolbox.Core.Diagnostics;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class GameplayService
{
    private readonly CallbackPerformanceMonitor? _performance;

    // Only ExecuteReserved supplies fixed phase names. Probe includes availability selection;
    // capture includes service identity/binding and prior-capture release. Other scopes cover the
    // prepared transaction call alone. Release is recorded only for a returned preparation.
    private OperationMeasurement MeasureOperation(GameplayControlAction action, string phase)
    {
        if (_performance is null) return default;
        try
        {
            var name = action switch
            {
                GameplayControlAction.Pause => "Pause", GameplayControlAction.Resume => "Resume",
                GameplayControlAction.Retry => "Retry", GameplayControlAction.Skip => "Skip",
                GameplayControlAction.SetSpeed => "SetSpeed", GameplayControlAction.Seek => "Seek",
                _ => "Unknown"
            };
            return new(_performance, name + "." + phase);
        }
        catch { return default; } // Instrumentation cannot change dispatch or recovery semantics.
    }

    private readonly struct OperationMeasurement : IDisposable
    {
        private readonly CallbackPerformanceMonitor? _performance;
        private readonly string? _operation;
        private readonly long _start, _allocated;
        internal OperationMeasurement(CallbackPerformanceMonitor performance, string operation)
        {
            _performance = performance; _operation = operation;
            _start = Stopwatch.GetTimestamp(); _allocated = GC.GetAllocatedBytesForCurrentThread();
        }
        public void Dispose()
        {
            if (_performance is null) return;
            try
            {
                _performance.Record("gameplay-control", _operation!,
                    (Stopwatch.GetTimestamp() - _start) * 1000d / Stopwatch.Frequency,
                    Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - _allocated), enforceBudget: false);
            }
            catch { /* Preserve the original control result, including exceptions and restoration evidence. */ }
        }
    }
}
