using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Runtime;

/// <summary>One process-lifetime native bridge. Plugins never register independent native patches.</summary>
internal sealed class RuntimeBridge
{
    private readonly ILogger _log;
    private readonly string _assembly, _namespace, _type, _method;
    private readonly Action<NativeRuntime, long> _tick;
    private readonly Action<Exception>? _admissionFailed;
    internal RuntimeExecutionOptions ExecutionOptions { get; }
    internal Action<RuntimeEntryMeasurement>? MeasureEntry { get; }
    private NativeRuntime? _api;
    private NativeRuntimeInvokeBridge? _transport;
    private int _started;
    internal bool IsReady => _transport is not null;
    internal string? StartupFailureDetail { get; private set; }

    internal RuntimeBridge(string assembly, string ns, string type, string method,
        Action<NativeRuntime, long> tick, ILogger log, RuntimeExecutionOptions? options = null,
        Action<RuntimeEntryMeasurement>? measure = null, Action<Exception>? admissionFailed = null)
    {
        _assembly = assembly; _namespace = ns; _type = type; _method = method; _tick = tick; _log = log;
        ExecutionOptions = options ?? new RuntimeExecutionOptions(); MeasureEntry = measure;
        _admissionFailed = admissionFailed;
    }

    internal void Start()
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0) return;
        try
        {
            var producer = NativeFirstInstall.RequireReady();
            _api = new NativeRuntime(producer.Module, _log);
            _transport = NativeRuntimeInvokeBridge.Adopt(producer, this, _log);
            _log.Log(LogLevel.Info, "Runtime bridge adopted the native pre-init entry; awaiting Unity lifecycle.");
        }
        catch (Exception ex)
        {
            StartupFailureDetail = ex.Message;
            SafeLog("Runtime bridge initialization failed.", ex);
        }
    }

    // Admission uses this metadata lookup only until the first completed Tick
    // binds its method pointer and operating-system thread.
    internal bool MatchesTick(IntPtr method) => _api!.MatchesStartupTick(method, _assembly, _namespace, _type, _method);

    internal void ReportAdmissionFailure(Exception error)
    {
        try { _admissionFailed?.Invoke(error); } catch { /* Startup diagnostics cannot fault the native observer. */ }
    }

    // Only the concrete native bridge calls this after its frame/channel checks,
    // this consumer's acceptance predicate, the selected Standard channel or
    // explicit Developer full-audit contract, and a second current native check.
    internal void OnAdmittedAfterInvoke(NativeRuntimeInvokeFrame frame, long hostTickSequence)
    {
        // The permanent native channel has already called original exactly once
        // and copied its normal return. CLR only observes that completed call.
        if (hostTickSequence == 1)
        {
            try { _log.Log(LogLevel.Info, "Unity tick bound; subsequent invocation dispatch compares method pointers."); }
            catch { /* The first Tick still runs if the logger fails. */ }
        }
        try { _api!.BindCurrentThread(); }
        catch (Exception error)
        {
            SafeLog("Runtime tick failed.", error);
            ReportAdmissionFailure(error);
            return;
        }
        try { _tick(_api!, hostTickSequence); }
        catch (Exception ex) { SafeLog("Runtime tick failed.", ex); }
    }

    private void SafeLog(string message, Exception exception)
    { try { _log.Log(LogLevel.Error, message, exception); } catch { /* Logging must not throw through the native callback. */ } }

}
