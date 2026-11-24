using RizToolbox.Runtime;
using RizToolbox.Sdk;
using System.Text.Json;

namespace RizToolbox.Adapter.AudioContent;

/// <summary>One opt-in synchronous observation of an already-held owner. Never owns the supplied root.</summary>
internal sealed class LocalResourceReferenceOwnerProbe
{
    private readonly IMainThreadDispatcher _main;
    private readonly ILogger _log;
    private readonly bool _enabled;
    private readonly Func<NativeRuntime.RootHandle, LocalResourceReferenceDiagnosticReport> _capture;
    private bool _attempted;
    internal LocalResourceReferenceOwnerProbe(IMainThreadDispatcher main, ILogger log, bool enabled,
        Func<NativeRuntime.RootHandle, LocalResourceReferenceDiagnosticReport> capture)
        => (_main, _log, _enabled, _capture) = (main, log, enabled, capture);
    internal void TryCapture(NativeRuntime.RootHandle owner)
    {
        if (!_enabled || !_main.IsMainThread || _attempted) return;
        // Claim the opportunity before any metadata or log callback can reenter.
        _attempted = true;
        try { WriteReport(_log, _capture(owner)); }
        catch (Exception error)
        {
            var message = error.GetType().Name + ": " + error.Message;
            if (message.Length > 1400) message = message[..1400];
            try { _log.Log(LogLevel.Warning, "LocalResourceReferenceProbe existing-owner unavailable: " + message); } catch { }
        }
    }
    internal static void WriteReport(ILogger log, LocalResourceReferenceDiagnosticReport report,
        string phase = "existing-owner", string rowKind = "owner-row")
    {
        if (report is null || report.Evidence is null || report.Evidence.Count is < 0 or > 64)
            throw new InvalidDataException("Resource owner evidence exceeds its row limit.");
        var header = JsonSerializer.Serialize(new { report.Stage, report.Build, report.CapturedUtc, count = report.Evidence.Count });
        if (header.Length > 1800) throw new InvalidDataException("Resource owner header exceeds its diagnostic bound.");
        // Preflight the entire bounded report before logging a header or a partial row set.
        var rows = new string[report.Evidence.Count];
        for (var index = 0; index < rows.Length; index++)
        {
            rows[index] = JsonSerializer.Serialize(new { index, evidence = report.Evidence[index] });
            if (rows[index].Length > 1800) throw new InvalidDataException("Resource owner row exceeds its diagnostic bound: " + index);
        }
        log.Log(LogLevel.Info, "LocalResourceReferenceProbe " + phase + " " + header);
        foreach (var row in rows) log.Log(LogLevel.Info, "LocalResourceReferenceProbe " + rowKind + " " + row);
    }
}
