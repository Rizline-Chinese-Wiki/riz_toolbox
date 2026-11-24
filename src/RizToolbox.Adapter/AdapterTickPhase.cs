using RizToolbox.Core.Diagnostics;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter;

internal static class AdapterTickPhase
{
    // Each phase owns its own failure policy. The caller decides whether a
    // dependent phase may proceed, and independent phases can still run.
    internal static bool Run<T>(ILogger log, string phase, T state, Action<T> work, Action<T, Exception>? onFault = null)
    {
        try { work(state); return true; }
        catch (Exception error)
        {
            if (onFault is not null)
            {
                try { onFault(state, error); }
                catch (Exception cleanupError) { log.TryLog(LogLevel.Error, $"Adapter phase '{phase}' fault handling failed.", cleanupError); }
            }
            log.TryLog(LogLevel.Warning, $"Adapter phase '{phase}' failed.", error);
            return false;
        }
    }
}
