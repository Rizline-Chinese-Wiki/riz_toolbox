using RizToolbox.Core.Diagnostics;
using RizToolbox.Sdk;

namespace RizToolbox.Core;

/// <summary>Adapter-owned observation targets. All subscribers share one adapter integration.</summary>
public sealed class ExtensionPointRegistry : IExtensionService
{
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private readonly ILogger _log;
    private readonly Dictionary<string, Target> _targets = new(StringComparer.Ordinal);
    private long _sequence;
    private sealed class Target
    {
        public ExtensionPointDescriptor Descriptor = null!;
        public List<Subscription> Subscriptions = new();
    }
    private sealed class Subscription : IDisposable
    {
        public Action<object?> Invoke = null!;
        public Action<Exception>? Fault;
        public int Priority;
        public long Sequence;
        public bool Active = true;
        public Action<Subscription> Remove = null!;
        public void Dispose() => Remove(this);
    }

    public ExtensionPointRegistry(ILogger log) => _log = log ?? throw new ArgumentNullException(nameof(log));

    public OperationResult Register<T>(ExtensionPoint<T> point, ExtensionPointPhase phase = ExtensionPointPhase.After, string? description = null)
    {
        VerifyAccess();
        var error = Validate(point);
        if (error is not null) return OperationResult.Fail(error);
        if (!Enum.IsDefined(typeof(ExtensionPointPhase), phase)) return OperationResult.Fail(ErrorCode.InvalidArgument, "Unknown extension point phase.", CapabilityIds.Extensions, point.Id);
        if (_targets.ContainsKey(point.Id)) return OperationResult.Fail(ErrorCode.Conflict, $"Extension point '{point.Id}' is already declared.", CapabilityIds.Extensions, point.Id);
        _targets.Add(point.Id, new() { Descriptor = new(point.Id, point.Version, typeof(T), phase, description) });
        return OperationResult.Ok();
    }

    public IReadOnlyList<ExtensionPointDescriptor> GetAll()
    {
        VerifyAccess();
        return _targets.Values.Select(x => x.Descriptor).OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Owner-thread selection for diagnostic projection; no full catalog sort is performed.</summary>
    public DiagnosticRowCopy<ExtensionPointDescriptor> CaptureDiagnosticDescriptors(int maximumRows)
    {
        VerifyAccess(); DiagnosticRowLimit.Validate(maximumRows);
        var rows = new List<ExtensionPointDescriptor>(maximumRows);
        foreach (var target in _targets.Values)
        {
            if (rows.Count == maximumRows) break;
            rows.Add(target.Descriptor);
        }
        return new(Array.AsReadOnly(rows.ToArray()), _targets.Count - rows.Count);
    }

    public OperationResult<ExtensionPointDescriptor> Describe(string id)
    {
        VerifyAccess();
        return id is not null && _targets.TryGetValue(id, out var target)
            ? OperationResult<ExtensionPointDescriptor>.Ok(target.Descriptor)
            : OperationResult<ExtensionPointDescriptor>.Fail(ErrorCode.TargetMissing, $"Extension point '{id}' is not declared by this adapter.", CapabilityIds.Extensions, id);
    }

    public OperationResult<IDisposable> Subscribe<T>(ExtensionPoint<T> point, Action<T> handler, int priority = 0)
        => SubscribeOwned(point, handler, priority, null);

    internal OperationResult<IDisposable> SubscribeOwned<T>(ExtensionPoint<T> point, Action<T> handler, int priority, Action<Exception>? fault)
    {
        VerifyAccess();
        var error = Resolve(point, out var target);
        if (error is not null) return OperationResult<IDisposable>.Fail(error);
        if (handler is null) return OperationResult<IDisposable>.Fail(ErrorCode.InvalidArgument, "Extension subscriber is null.", CapabilityIds.Extensions, point.Id);
        var subscription = new Subscription
        {
            Invoke = message => handler((T)message!), Priority = priority, Sequence = _sequence++, Fault = fault,
            Remove = entry => { VerifyAccess(); if (!entry.Active) return; entry.Active = false; target!.Subscriptions.Remove(entry); }
        };
        target!.Subscriptions.Add(subscription);
        return OperationResult<IDisposable>.Ok(subscription);
    }

    public OperationResult Publish<T>(ExtensionPoint<T> point, T message)
    {
        VerifyAccess();
        var error = Resolve(point, out var target);
        if (error is not null) return OperationResult.Fail(error);
        foreach (var entry in target!.Subscriptions.OrderByDescending(x => x.Priority).ThenBy(x => x.Sequence).ToArray())
        {
            if (!entry.Active) continue;
            try { entry.Invoke(message); }
            catch (Exception ex)
            {
                entry.Dispose();
                _log.TryLog(LogLevel.Error, $"Extension subscriber failed for '{point.Id}'.", ex);
                try { entry.Fault?.Invoke(ex); }
                catch (Exception cleanupError) { _log.TryLog(LogLevel.Error, "Extension fault cleanup failed.", cleanupError); }
            }
        }
        return OperationResult.Ok();
    }

    private OperationError? Resolve<T>(ExtensionPoint<T> point, out Target? target)
    {
        target = null;
        var error = Validate(point);
        if (error is not null) return error;
        if (!_targets.TryGetValue(point.Id, out target)) return new(ErrorCode.TargetMissing, $"Extension point '{point.Id}' is not declared by this adapter.", CapabilityIds.Extensions, point.Id);
        if (target.Descriptor.Version != point.Version) return new(ErrorCode.UnsupportedSignature, $"Extension point '{point.Id}' requires exact version {target.Descriptor.Version}.", CapabilityIds.Extensions, point.Id);
        if (target.Descriptor.PayloadType != typeof(T)) return new(ErrorCode.UnsupportedSignature, $"Extension point '{point.Id}' exposes payload '{target.Descriptor.PayloadType.FullName}', not '{typeof(T).FullName}'.", CapabilityIds.Extensions, point.Id);
        return null;
    }
    private static OperationError? Validate<T>(ExtensionPoint<T>? point)
        => point is null || string.IsNullOrWhiteSpace(point.Id) || point.Version is null
            ? new(ErrorCode.InvalidArgument, "Extension point ID and version are required.", CapabilityIds.Extensions, point?.Id) : null;
    private void VerifyAccess()
    {
        if (Environment.CurrentManagedThreadId != _threadId) throw new InvalidOperationException("Extension points must be used on the host main thread.");
    }
}
