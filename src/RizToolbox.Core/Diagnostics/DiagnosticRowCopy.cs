using RizToolbox.Sdk;

namespace RizToolbox.Core.Diagnostics;

/// <summary>A bounded diagnostic selection. Omitted values are reported rather than silently treated as complete.</summary>
public sealed record DiagnosticRowCopy<T>(IReadOnlyList<T> Items, long TruncatedValues);

/// <summary>Provider-free metadata values, projected into the host's detached status copy before background use.</summary>
public sealed record DiagnosticCapabilityRow(string Id, Version Version, CapabilityStability Stability,
    bool Available, long Generation, string? UnavailableReason, IReadOnlyList<CapabilityOperationInfo> Operations);

internal static class DiagnosticRowLimit
{
    internal static void Validate(int maximumRows)
    {
        if (maximumRows is < 0 or > 4096) throw new ArgumentOutOfRangeException(nameof(maximumRows));
    }
}
