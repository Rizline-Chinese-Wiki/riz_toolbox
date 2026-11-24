namespace RizToolbox.Runtime;

/// <summary>The admission contract is selected once, before the native observer is attached.</summary>
internal sealed record RuntimeExecutionOptions
{
    internal bool DeveloperDiagnostics { get; }
    internal bool FullAuditOnTick { get; }

    internal RuntimeExecutionOptions(bool DeveloperDiagnostics = false, bool FullAuditOnTick = false)
    {
        if (FullAuditOnTick && !DeveloperDiagnostics)
            throw new ArgumentException("Full Tick publication audits require explicit Developer diagnostics.", nameof(FullAuditOnTick));
        this.DeveloperDiagnostics = DeveloperDiagnostics;
        this.FullAuditOnTick = FullAuditOnTick;
    }
}

internal enum RuntimeEntryOutcome { Ignored, AbnormalReturn, Dispatched, Rejected, StoppedUnknown }

/// <summary>Managed After entry through host return, excluding the already completed game original
/// and measurement delivery. Sequence counts admitted host ticks, never Unity rendered frames.</summary>
internal readonly record struct RuntimeEntryMeasurement(long HostTickSequence, RuntimeEntryOutcome Outcome,
    double AdmissionMilliseconds, double HostMilliseconds, double TotalMilliseconds, bool FullAuditOnTick);
