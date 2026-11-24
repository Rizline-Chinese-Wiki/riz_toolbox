using RizToolbox.Sdk;

namespace RizToolbox.Core.Diagnostics;

public static class DiagnosticLog
{
    /// <summary>Diagnostic sinks do not participate in operational state changes or cleanup.</summary>
    public static void TryLog(this ILogger log, LogLevel level, string message, Exception? error = null)
    {
        try { log.Log(level, message, error); }
        catch (Exception) { }
    }
}
