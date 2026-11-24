using RizToolbox.Sdk;
using RizToolbox.Core.Diagnostics;

namespace RizToolbox.Core.Hooks;

/// <summary>Typed logical subscriptions. The Runtime owns one native detour per address, independent of plugin count.</summary>
public sealed class HookDispatcher : IDisposable
{
    private readonly IMainThreadDispatcher _main; private readonly ILogger _log;
    private readonly Dictionary<string, Target> _targets = new(StringComparer.Ordinal);
    private long _sequence; private bool _disposed;
    public event Action<string, Exception>? ProviderFaulted;
    public CallbackPerformanceMonitor? Performance { get; set; }
    private sealed class Target
    {
        public HookTargetDescriptor Descriptor = null!;
        public List<Subscription> Subscriptions = new();
        public Func<HookDiagnostics>? NativeDiagnostics;
        public long Before, After;
    }
    private sealed class Subscription : IDisposable
    {
        public Action<object> Invoke = null!; public HookPhase Phase; public int Priority; public long Order; public bool Active = true;
        public Action<Subscription> Remove = null!;
        public void Dispose() => Remove(this);
    }
    public HookDispatcher(IMainThreadDispatcher main, ILogger log) { _main = main; _log = log; }

    public OperationResult Declare<T>(HookPoint<T> point, string signature, string fingerprint, string evidence)
    {
        _main.VerifyAccess();
        if (_disposed || point is null || string.IsNullOrWhiteSpace(point.Id) || point.Version is null || string.IsNullOrWhiteSpace(signature)
            || string.IsNullOrWhiteSpace(fingerprint) || string.IsNullOrWhiteSpace(evidence)) return OperationResult.Fail(ErrorCode.UnsupportedSignature, "Complete hook declaration is required.", HookCapabilityIds.Observe, point?.Id);
        if (_targets.TryGetValue(point.Id, out var prior)) return prior.Descriptor.Version == point.Version && prior.Descriptor.PayloadType == typeof(T)
            && prior.Descriptor.Signature == signature && prior.Descriptor.BuildFingerprint == fingerprint ? OperationResult.Ok() : OperationResult.Fail(ErrorCode.UnsupportedSignature, "Hook declaration differs from existing target.", HookCapabilityIds.Observe, point.Id);
        _targets.Add(point.Id, new() { Descriptor = new(point.Id, point.Version, typeof(T), signature, fingerprint, evidence) });
        return OperationResult.Ok();
    }
    public void SetNativeInstalled(string id, Func<HookDiagnostics> diagnostics)
    {
        _main.VerifyAccess(); var target = _targets[id]; target.Descriptor = target.Descriptor with { NativeInstalled = true }; target.NativeDiagnostics = diagnostics;
    }
    public void SetNativeDetached(string id)
    {
        _main.VerifyAccess();
        if (_targets.TryGetValue(id, out var target)) target.Descriptor = target.Descriptor with { NativeInstalled = false };
    }
    public void Publish<T>(HookPoint<T> point, HookPhase phase, T payload)
    {
        _main.VerifyAccess();
        if (_disposed || !Enum.IsDefined(typeof(HookPhase), phase) || !_targets.TryGetValue(point.Id, out var target) || target.Descriptor.PayloadType != typeof(T) || target.Descriptor.Version != point.Version) return;
        if (phase == HookPhase.Before) target.Before++; else target.After++;
        foreach (var item in target.Subscriptions.Where(x => x.Phase == phase).OrderByDescending(x => x.Priority).ThenBy(x => x.Order).ToArray())
        {
            if (!item.Active) continue;
            try { item.Invoke(payload!); }
            catch (Exception ex) { item.Dispose(); _log.Log(LogLevel.Error, $"Hook subscriber failed for '{point.Id}'.", ex); }
        }
    }
    public IHookService ForPlugin(IPluginContext context, Action<Exception>? onFault = null) => new View(this, context, onFault);
    private OperationResult<IDisposable> Subscribe<T>(IPluginContext context, HookPoint<T> point, HookPhase phase, Action<T> handler, int priority, Action<Exception>? fault)
    {
        _main.VerifyAccess(); context.MainThread.VerifyAccess();
        if (_disposed || point is null || !_targets.TryGetValue(point.Id, out var target)) return OperationResult<IDisposable>.Fail(ErrorCode.CapabilityUnavailable, "Hook target is not declared.", HookCapabilityIds.Observe, point?.Id);
        if (target.Descriptor.PayloadType != typeof(T) || target.Descriptor.Version != point.Version || !Enum.IsDefined(typeof(HookPhase), phase))
            return OperationResult<IDisposable>.Fail(ErrorCode.UnsupportedSignature, "Hook version, phase or payload differs.", HookCapabilityIds.Observe, point.Id);
        if (handler is null) return OperationResult<IDisposable>.Fail(ErrorCode.InvalidArgument, "Hook callback is null.", HookCapabilityIds.Observe, point.Id);
        var item = new Subscription
        {
            Phase = phase, Priority = priority, Order = _sequence++, Remove = value => { _main.VerifyAccess(); value.Active = false; target.Subscriptions.Remove(value); },
            Invoke = payload =>
            {
                try { context.MainThread.VerifyAccess(); }
                catch (ObjectDisposedException) { return; }
                try
                {
                    if (Performance is { } performance) performance.Invoke(context.PluginId, "hook:" + point.Id + ":" + phase, () => handler((T)payload));
                    else handler((T)payload);
                }
                catch (Exception ex)
                {
                    try { fault?.Invoke(ex); } catch (Exception callbackError) { _log.Log(LogLevel.Error, "Hook fault routing failed.", callbackError); }
                    foreach (var callback in ProviderFaulted?.GetInvocationList() ?? Array.Empty<Delegate>())
                        try { ((Action<string, Exception>)callback)(context.PluginId, ex); }
                        catch (Exception callbackError) { _log.Log(LogLevel.Error, "Hook owner fault routing failed.", callbackError); }
                    throw;
                }
            }
        };
        target.Subscriptions.Add(item);
        try { context.Own<IDisposable>(item); } catch { item.Dispose(); throw; }
        return OperationResult<IDisposable>.Ok(item);
    }
    public IReadOnlyList<HookTargetDescriptor> GetTargets() { _main.VerifyAccess(); return _targets.Values.Select(x => x.Descriptor).OrderBy(x => x.Id, StringComparer.Ordinal).ToArray(); }
    public IReadOnlyList<HookDiagnostics> GetDiagnostics()
    {
        _main.VerifyAccess();
        return _targets.Values.Select(x => (x.NativeDiagnostics?.Invoke() ?? new(x.Descriptor.Id, 0, 0, 0, 0, 0, 0, 0))
            with { BeforeDeliveries = x.Before, AfterDeliveries = x.After, Subscribers = x.Subscriptions.Count }).ToArray();
    }
    public void Dispose() { _main.VerifyAccess(); if (_disposed) return; _disposed = true; foreach (var target in _targets.Values) foreach (var item in target.Subscriptions.ToArray()) item.Dispose(); ProviderFaulted = null; }
    private sealed class View : IHookService
    {
        private readonly HookDispatcher _owner; private readonly IPluginContext _context; private readonly Action<Exception>? _fault;
        public View(HookDispatcher owner, IPluginContext context, Action<Exception>? fault) { _owner = owner; _context = context; _fault = fault; }
        public IReadOnlyList<HookTargetDescriptor> GetTargets() { _context.MainThread.VerifyAccess(); return _owner.GetTargets(); }
        public IReadOnlyList<HookDiagnostics> GetDiagnostics() { _context.MainThread.VerifyAccess(); return _owner.GetDiagnostics(); }
        public OperationResult<IDisposable> Subscribe<T>(HookPoint<T> point, HookPhase phase, Action<T> handler, int priority = 0) => _owner.Subscribe(_context, point, phase, handler, priority, _fault);
        public OperationResult RequestMutation(string targetId) { _context.MainThread.VerifyAccess(); return OperationResult.Fail(ErrorCode.MutationNotSupported, "Exact hooks expose observation only; argument/return changes and skipping the original are not adapted.", HookCapabilityIds.Observe, targetId); }
    }
}
