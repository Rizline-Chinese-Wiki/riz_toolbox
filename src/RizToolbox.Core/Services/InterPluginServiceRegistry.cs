using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using RizToolbox.Core.Diagnostics;
using RizToolbox.Sdk;

namespace RizToolbox.Core.Services;

/// <summary>Versioned, scope-owned services with host-shared contracts. This is an in-process trust boundary, not a sandbox.</summary>
public sealed class InterPluginServiceRegistry : IDisposable
{
    private readonly IMainThreadDispatcher _main;
    private readonly ILogger _log;
    private readonly ServiceCallLimits _limits;
    private readonly SharedContractPolicy _policy = new();
    private readonly Dictionary<string, Entry> _services = new(StringComparer.Ordinal);
    private readonly List<Watcher> _watchers = new();
    private readonly List<MessageSubscriber> _messages = new();
    private readonly Dictionary<string, (Type Type, Version Version)> _messageTypes = new(StringComparer.Ordinal);
    private readonly object _faultGate = new();
    private readonly Dictionary<Entry, Exception> _pendingFaults = new();
    private long _generation, _sequence;
    private bool _disposed;
    private static readonly AsyncLocal<CallFrame?> CurrentCall = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = 32 };
    public event Action<string, Exception>? ProviderFaulted;

    private sealed class Entry
    {
        private readonly CancellationTokenSource _withdrawal = new();
        private int _failed;

        public PluginServiceDescriptor Descriptor = null!;
        public object Provider = null!;
        public IPluginContext Context = null!;
        public Action<Exception>? Fault;
        public volatile bool Active = true;
        public CancellationToken Lifetime { get; }
        public bool Available => Active && Volatile.Read(ref _failed) == 0 && !ScopeEnded(Context);

        public Entry() => Lifetime = _withdrawal.Token;
        public bool TryFail() => Interlocked.Exchange(ref _failed, 1) == 0;
        public void EndCalls()
        {
            _withdrawal.Cancel();
            _withdrawal.Dispose();
        }
    }
    private sealed record CallFrame(string Service, string Owner, CallFrame? Parent, int Depth);
    private sealed class ProviderFailure : InvalidOperationException
    {
        public ProviderFailure(string owner, Exception inner) : base($"ServiceProviderFailed ({owner}): {inner.Message}", inner) { }
    }
    private sealed class ConsumerStopped : OperationCanceledException
    {
        public ConsumerStopped() : base("ServiceConsumerStopped: consumer scope expired while processing this call.") { }
    }
    private sealed class ServiceWithdrawn : InvalidOperationException
    {
        public ServiceWithdrawn() : base("ServiceUnavailable: this proxy's provider generation was withdrawn.") { }
    }
    private sealed class Watcher : IDisposable
    {
        public IPluginContext Context = null!; public ServiceQuery Query = null!;
        public Action<ServiceAvailability> Changed = null!; public Action<Exception>? Fault;
        public Action<Watcher> Remove = null!; public bool Active = true;
        public ServiceAvailability? Last;
        public void Dispose() => Remove(this);
    }
    private sealed class MessageSubscriber : IDisposable
    {
        public string Id = null!; public Type Type = null!; public Version Version = null!;
        public Action<byte[]> Invoke = null!; public IPluginContext Context = null!; public Action<Exception>? Fault;
        public int Priority; public long Order; public bool Active = true;
        public Action<MessageSubscriber> Remove = null!;
        public void Dispose() => Remove(this);
    }
    private sealed class Registration : IServiceRegistration
    {
        private InterPluginServiceRegistry? _owner; private readonly Entry _entry;
        public Registration(InterPluginServiceRegistry owner, Entry entry) { _owner = owner; _entry = entry; }
        public PluginServiceDescriptor Descriptor => _entry.Descriptor;
        public void Dispose()
        {
            var owner = _owner; if (owner is null) return;
            owner._main.VerifyAccess(); _owner = null; owner.Remove(_entry);
        }
    }

    public InterPluginServiceRegistry(IMainThreadDispatcher main, ILogger log, ServiceCallLimits? limits = null)
    {
        _main = main; _log = log; _limits = limits ?? new(); _main.VerifyAccess();
        if (_limits.MaximumPayloadBytes is < 128 or > 1048576 || _limits.TimeoutMilliseconds is < 1 or > 30000 || _limits.MaximumCallDepth is < 1 or > 64)
            throw new ArgumentOutOfRangeException(nameof(limits), "Service limits must bound payloads to128B-1MiB, time to1-30000ms, and call depth to1-64.");
    }

    /// <summary>Host only: the assembly must already be deliberately shared in the default ALC, with an exact hash pin.</summary>
    public OperationResult AllowSharedContract<T>(string assemblySha256) where T : class
    {
        _main.VerifyAccess(); if (_disposed) return OperationResult.Fail(ErrorCode.ServiceUnavailable, "Registry is disposed.", InterPluginCapabilityIds.Services, typeof(T).FullName);
        try { return _policy.Allow<T>(assemblySha256); }
        catch (Exception ex) { return OperationResult.Fail(ErrorCode.ContractIdentityMismatch, ex.Message, InterPluginCapabilityIds.Services, typeof(T).FullName); }
    }

    public IPluginServices ForPlugin(IPluginContext context, Action<Exception>? onFault = null)
    {
        _main.VerifyAccess(); context.MainThread.VerifyAccess();
        if (_disposed) throw new ObjectDisposedException(nameof(InterPluginServiceRegistry));
        return new PluginView(this, context, onFault);
    }

    private OperationResult<IServiceRegistration> Publish<T>(IPluginContext context, string id, Version version, T provider, Action<Exception>? fault) where T : class
    {
        _main.VerifyAccess(); context.MainThread.VerifyAccess();
        if (_disposed) return OperationResult<IServiceRegistration>.Fail(ErrorCode.ServiceUnavailable, "Registry is disposed.", InterPluginCapabilityIds.Services, id);
        if (!ValidId(id) || version is null || provider is null) return OperationResult<IServiceRegistration>.Fail(ErrorCode.InvalidArgument, "ID, version and provider are required.", InterPluginCapabilityIds.Services, id);
        var contractError = _policy.Contract(typeof(T));
        if (contractError is not null) return OperationResult<IServiceRegistration>.Fail(WithTarget(contractError, id));
        if (_services.ContainsKey(id)) return OperationResult<IServiceRegistration>.Fail(ErrorCode.Conflict, "ID is already provided by " + _services[id].Descriptor.ProviderId, InterPluginCapabilityIds.Services, id);
        var entry = new Entry { Descriptor = new(id, version, context.PluginId, typeof(T), ++_generation), Context = context, Provider = provider, Fault = fault };
        _services.Add(id, entry);
        var registration = new Registration(this, entry);
        try { context.Own<IServiceRegistration>(registration); }
        catch { registration.Dispose(); throw; }
        Notify(id);
        return OperationResult<IServiceRegistration>.Ok(registration);
    }

    private ServiceResolution<T> Lookup<T>(IPluginContext consumer, ServiceQuery query) where T : class
    {
        _main.VerifyAccess(); consumer.MainThread.VerifyAccess();
        var error = ValidateQuery(query) ?? _policy.Contract(typeof(T));
        if (error is not null) return new(false, null, null, WithTarget(error, query?.Id));
        var availability = Availability(query);
        if (!availability.Available) return new(false, null, availability.Service, availability.Error);
        var entry = _services[query.Id];
        if (entry.Descriptor.Contract != typeof(T)) return new(false, null, entry.Descriptor,
            new(ErrorCode.ContractIdentityMismatch, "Exact shared Type identity differs from the registered contract.", InterPluginCapabilityIds.Services, query.Id));
        var proxy = DispatchProxy.Create<T, ServiceInvocationProxy>();
        ((ServiceInvocationProxy)(object)proxy).InvokeService = (method, args) => Invoke(entry, consumer, method, args);
        return new(true, proxy, entry.Descriptor);
    }

    private ServiceAvailability Availability(ServiceQuery query)
    {
        if (_disposed || !_services.TryGetValue(query.Id, out var entry) || !entry.Available)
            return new(query, false, null, new(ErrorCode.ServiceUnavailable, "Provider is not ready or was withdrawn.", InterPluginCapabilityIds.Services, query.Id, true));
        var descriptor = entry.Descriptor;
        if (descriptor.Version < query.MinVersion || (query.MaxVersionExclusive is not null && descriptor.Version >= query.MaxVersionExclusive))
            return new(query, false, descriptor, new(ErrorCode.ServiceUnavailable, $"Provider version {descriptor.Version} is outside the requested range.", InterPluginCapabilityIds.Services, query.Id));
        return new(query, true, descriptor);
    }

    private IDisposable Watch(IPluginContext context, ServiceQuery query, Action<ServiceAvailability> callback, Action<Exception>? fault)
    {
        _main.VerifyAccess(); context.MainThread.VerifyAccess();
        if (_disposed) throw new ObjectDisposedException(nameof(InterPluginServiceRegistry));
        var error = ValidateQuery(query); if (error is not null) throw new ArgumentException(error.Message, nameof(query));
        if (callback is null) throw new ArgumentNullException(nameof(callback));
        var watcher = new Watcher { Context = context, Query = query, Changed = callback, Fault = fault, Remove = RemoveWatcher };
        _watchers.Add(watcher);
        try { context.Own<IDisposable>(watcher); }
        catch { watcher.Dispose(); throw; }
        Deliver(watcher);
        return watcher;
    }
    private void Notify(string id)
    {
        foreach (var watcher in _watchers.Where(x => x.Query.Id == id).ToArray()) Deliver(watcher);
    }
    private void Deliver(Watcher watcher)
    {
        if (!watcher.Active) return;
        var availability = Availability(watcher.Query);
        if (Equals(watcher.Last, availability)) return;
        watcher.Last = availability;
        try { watcher.Context.MainThread.VerifyAccess(); }
        catch (ObjectDisposedException) { watcher.Dispose(); return; }
        try { watcher.Changed(availability); }
        catch (Exception ex)
        {
            HandleObserverFailure(watcher, watcher.Context, watcher.Fault, ex, "Service availability observer failed.");
        }
    }
    private void RemoveWatcher(Watcher watcher)
    {
        _main.VerifyAccess(); if (!watcher.Active) return; watcher.Active = false; _watchers.Remove(watcher);
    }

    private object? Invoke(Entry entry, IPluginContext consumer, MethodInfo method, object?[]? arguments)
    {
        _main.VerifyAccess(); consumer.MainThread.VerifyAccess();
        if (!entry.Available || _disposed) throw new ServiceWithdrawn();
        try { entry.Context.MainThread.VerifyAccess(); }
        catch (ObjectDisposedException) { Remove(entry); throw new ServiceWithdrawn(); }
        var parent = CurrentCall.Value;
        for (var frame = parent; frame is not null; frame = frame.Parent)
            if (frame.Service == entry.Descriptor.Id)
                throw new InvalidOperationException("ServiceCallCycle: " + CallPath(parent, entry.Descriptor.Id));
        if ((parent?.Depth ?? 0) >= _limits.MaximumCallDepth)
            throw new InvalidOperationException("ServiceCallDepthExceeded: " + CallPath(parent, entry.Descriptor.Id));
        var args = arguments ?? Array.Empty<object?>();
        var parameters = method.GetParameters();
        object?[] copied;
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(args, JsonOptions);
            if (bytes.Length > _limits.MaximumPayloadBytes) throw new InvalidOperationException("ServicePayloadTooLarge: request exceeds the configured byte limit.");
            copied = args.Select((value, index) => Snapshot(value, parameters[index].ParameterType)).ToArray();
        }
        catch (Exception ex) { throw new InvalidOperationException("ServiceRequestRejected: " + ex.Message, ex); }
        VerifyCallLifetime(entry, consumer);
        CurrentCall.Value = new(entry.Descriptor.Id, entry.Descriptor.ProviderId, parent, (parent?.Depth ?? 0) + 1);
        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = method.Invoke(entry.Provider, copied);
            VerifyCallLifetime(entry, consumer);
            if (method.ReturnType == typeof(Task)) return FinishTask((Task)result!, entry, consumer, started);
            if (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
                return typeof(InterPluginServiceRegistry).GetMethod(nameof(FinishGenericTask), BindingFlags.Instance | BindingFlags.NonPublic)!
                    .MakeGenericMethod(method.ReturnType.GetGenericArguments()[0]).Invoke(this, new[] { result, entry, consumer, (object)started });
            CheckBudget(started);
            var response = method.ReturnType == typeof(void) ? null : Snapshot(result, method.ReturnType);
            VerifyCallLifetime(entry, consumer);
            return response;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw Fault(entry, ex.InnerException);
        }
        catch (Exception ex) when (ex is not (ConsumerStopped or ServiceWithdrawn)) { throw Fault(entry, ex); }
        finally { CurrentCall.Value = parent; }
    }

    private async Task FinishTask(Task task, Entry entry, IPluginContext consumer, long started)
    {
        using var lifetime = LinkCallLifetime(entry, consumer);
        try
        {
            await task.WaitAsync(Remaining(started), lifetime.Token).ConfigureAwait(false);
            CheckBudget(started);
            VerifyCallLifetime(entry, consumer);
        }
        catch (Exception ex) { throw CallEndReason(entry, consumer) ?? Fault(entry, ex); }
    }
    private async Task<T> FinishGenericTask<T>(Task<T> task, Entry entry, IPluginContext consumer, long started)
    {
        using var lifetime = LinkCallLifetime(entry, consumer);
        try
        {
            var value = await task.WaitAsync(Remaining(started), lifetime.Token).ConfigureAwait(false);
            VerifyCallLifetime(entry, consumer);
            CheckBudget(started);
            var response = (T)Snapshot(value, typeof(T))!;
            VerifyCallLifetime(entry, consumer);
            return response;
        }
        catch (Exception ex) { throw CallEndReason(entry, consumer) ?? Fault(entry, ex); }
    }
    private static CancellationTokenSource LinkCallLifetime(Entry entry, IPluginContext consumer)
        => CancellationTokenSource.CreateLinkedTokenSource(entry.Lifetime, (consumer as IScopedPluginContext)?.Lifetime ?? default);

    private static bool ScopeEnded(IPluginContext context)
        => context is IPluginScope { IsActive: false } || context is IScopedPluginContext { Lifetime.IsCancellationRequested: true };

    private static Exception? CallEndReason(Entry entry, IPluginContext consumer)
        => ScopeEnded(consumer) ? new ConsumerStopped() : !entry.Available ? new ServiceWithdrawn() : null;

    private static void VerifyCallLifetime(Entry entry, IPluginContext consumer)
    {
        if (CallEndReason(entry, consumer) is { } ended) throw ended;
    }
    private TimeSpan Remaining(long started)
    {
        var remaining = _limits.TimeoutMilliseconds - Elapsed(started);
        if (remaining <= 0) throw new TimeoutException("ServiceTimeout: provider exceeded the host call budget.");
        return TimeSpan.FromMilliseconds(remaining);
    }
    private void CheckBudget(long started) { if (Elapsed(started) > _limits.TimeoutMilliseconds) throw new TimeoutException("ServiceTimeout: provider exceeded the host call budget."); }
    private static double Elapsed(long started) => (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;

    private object? Snapshot(object? value, Type declared)
    {
        if (value is null && (declared == typeof(OperationResult) || declared == typeof(Result)
            || declared.IsGenericType && (declared.GetGenericTypeDefinition() == typeof(OperationResult<>) || declared.GetGenericTypeDefinition() == typeof(Result<>))))
            throw new InvalidOperationException("A provider must return a complete operation result.");
        var data = JsonSerializer.SerializeToUtf8Bytes(value, declared, JsonOptions);
        if (data.Length > _limits.MaximumPayloadBytes) throw new InvalidOperationException("ServicePayloadTooLarge: value exceeds the configured byte limit.");
        return JsonSerializer.Deserialize(data, declared, JsonOptions);
    }
    private static string CallPath(CallFrame? frame, string final)
    {
        var path = new List<string>(); for (; frame is not null; frame = frame.Parent) path.Add(frame.Service + "(" + frame.Owner + ")");
        path.Reverse(); path.Add(final); return string.Join(" -> ", path);
    }
    private Exception Fault(Entry entry, Exception error)
    {
        if (error is ProviderFailure) return error;
        if (!entry.Available || !entry.TryFail()) return error;

        if (_main.IsMainThread) DeliverProviderFault(entry, error);
        else
        {
            lock (_faultGate)
            {
                if (!_disposed && entry.Active) _pendingFaults.Add(entry, error);
            }
        }
        return new ProviderFailure(entry.Descriptor.ProviderId, error);
    }

    /// <summary>Drains asynchronous provider failures once per host tick on the main thread.</summary>
    public void DrainProviderFaults()
    {
        _main.VerifyAccess();
        KeyValuePair<Entry, Exception>[] faults;
        lock (_faultGate)
        {
            if (_pendingFaults.Count == 0) return;
            faults = _pendingFaults.ToArray();
            _pendingFaults.Clear();
        }
        foreach (var fault in faults) DeliverProviderFault(fault.Key, fault.Value);
    }

    private void DeliverProviderFault(Entry entry, Exception error)
    {
        if (!entry.Active || _disposed) return;
        Remove(entry);
        ReportCallbackFault(entry.Context, entry.Fault, error, $"Service provider '{entry.Descriptor.Id}' failed.");
    }

    private void HandleObserverFailure(IDisposable subscription, IPluginContext scope, Action<Exception>? callback,
        Exception error, string message)
    {
        // A service proxy already attributed this failure to its provider. The observer can receive future updates.
        if (error is ProviderFailure) return;
        subscription.Dispose();
        ReportCallbackFault(scope, callback, error, message);
    }

    private void ReportCallbackFault(IPluginContext scope, Action<Exception>? callback, Exception error, string message)
    {
        _log.TryLog(LogLevel.Error, message, error);
        if (_disposed || ScopeEnded(scope)) return;
        try { callback?.Invoke(error); } catch (Exception ex) { _log.TryLog(LogLevel.Error, "Service owner fault handler failed.", ex); }
        foreach (var handler in ProviderFaulted?.GetInvocationList() ?? Array.Empty<Delegate>())
        {
            if (_disposed || ScopeEnded(scope)) return;
            try { ((Action<string, Exception>)handler)(scope.PluginId, error); }
            catch (Exception ex) { _log.TryLog(LogLevel.Error, "Service fault observer failed.", ex); }
        }
    }
    private void Remove(Entry entry)
    {
        _main.VerifyAccess();
        if (!entry.Active) return;
        lock (_faultGate)
        {
            entry.Active = false;
            _pendingFaults.Remove(entry);
        }
        if (_services.TryGetValue(entry.Descriptor.Id, out var current) && ReferenceEquals(current, entry)) _services.Remove(entry.Descriptor.Id);
        entry.EndCalls();
        Notify(entry.Descriptor.Id);
    }

    private OperationResult<IDisposable> SubscribeMessage<T>(IPluginContext context, MessageChannel<T> channel, Action<T> callback, int priority, Action<Exception>? fault)
    {
        _main.VerifyAccess(); context.MainThread.VerifyAccess();
        var error = CheckMessage(channel);
        if (error is not null || callback is null) return OperationResult<IDisposable>.Fail(error ?? new(ErrorCode.InvalidArgument, "Message callback is null.", InterPluginCapabilityIds.Services, channel?.Id));
        var subscriber = new MessageSubscriber
        {
            Id = channel.Id, Type = typeof(T), Version = channel.Version, Context = context, Fault = fault,
            Priority = priority, Order = ++_sequence, Invoke = bytes => callback(JsonSerializer.Deserialize<T>(bytes, JsonOptions)!),
            Remove = value => { _main.VerifyAccess(); if (!value.Active) return; value.Active = false; _messages.Remove(value); }
        };
        _messages.Add(subscriber);
        try { context.Own<IDisposable>(subscriber); }
        catch { subscriber.Dispose(); throw; }
        return OperationResult<IDisposable>.Ok(subscriber);
    }
    private OperationResult PublishMessage<T>(IPluginContext context, MessageChannel<T> channel, T value)
    {
        _main.VerifyAccess(); context.MainThread.VerifyAccess();
        var error = CheckMessage(channel); if (error is not null) return OperationResult.Fail(error);
        var parent = CurrentCall.Value;
        var id = "message:" + channel.Id;
        for (var frame = parent; frame is not null; frame = frame.Parent)
            if (frame.Service == id) return OperationResult.Fail(ErrorCode.ServiceCallCycle, CallPath(parent, id), InterPluginCapabilityIds.Services, channel.Id);
        if ((parent?.Depth ?? 0) >= _limits.MaximumCallDepth) return OperationResult.Fail(ErrorCode.ServiceCallDepthExceeded, CallPath(parent, id), InterPluginCapabilityIds.Services, channel.Id);
        byte[] data;
        try
        {
            data = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
            if (data.Length > _limits.MaximumPayloadBytes) return OperationResult.Fail(ErrorCode.ServicePayloadTooLarge, "Message exceeds the configured byte limit.", InterPluginCapabilityIds.Services, channel.Id);
        }
        catch (Exception ex) { return OperationResult.Fail(ErrorCode.InvalidArgument, ex.Message, InterPluginCapabilityIds.Services, channel.Id); }
        CurrentCall.Value = new(id, context.PluginId, parent, (parent?.Depth ?? 0) + 1);
        try
        {
            foreach (var subscriber in _messages.Where(x => x.Id == channel.Id).OrderByDescending(x => x.Priority).ThenBy(x => x.Order).ToArray())
            {
                if (!subscriber.Active) continue;
                try { subscriber.Context.MainThread.VerifyAccess(); }
                catch (ObjectDisposedException) { subscriber.Dispose(); continue; }
                try { subscriber.Invoke(data); }
                catch (Exception ex)
                {
                    HandleObserverFailure(subscriber, subscriber.Context, subscriber.Fault, ex, "Read-only message observer failed.");
                }
            }
        }
        finally { CurrentCall.Value = parent; }
        return OperationResult.Ok();
    }
    private OperationError? CheckMessage<T>(MessageChannel<T>? channel)
    {
        if (_disposed) return new(ErrorCode.ServiceUnavailable, "Registry is disposed.", InterPluginCapabilityIds.Services, channel?.Id);
        if (channel is null || !ValidId(channel.Id) || channel.Version is null) return new(ErrorCode.InvalidArgument, "Message ID and version are required.", InterPluginCapabilityIds.Services, channel?.Id);
        var error = _policy.Data(typeof(T)); if (error is not null) return WithTarget(error, channel.Id);
        if (_messageTypes.TryGetValue(channel.Id, out var prior))
            return prior.Type == typeof(T) && prior.Version == channel.Version ? null : new(ErrorCode.ContractIdentityMismatch, "Message channel type or version differs from its first declaration.", InterPluginCapabilityIds.Services, channel.Id);
        _messageTypes.Add(channel.Id, (typeof(T), channel.Version)); return null;
    }
    private static OperationError? ValidateQuery(ServiceQuery? query)
        => query is null || !ValidId(query.Id) || query.MinVersion is null || (query.MaxVersionExclusive is not null && query.MaxVersionExclusive <= query.MinVersion)
            ? new(ErrorCode.InvalidArgument, "Service ID and a nonempty version range are required.", InterPluginCapabilityIds.Services, query?.Id) : null;
    private static bool ValidId(string? id) => id is not null && id.Length <= 128 && Regex.IsMatch(id, "^[a-z0-9][a-z0-9._/-]*$");
    private static OperationError WithTarget(OperationError error, string? target)
        => error.TargetId is null && target is not null ? error with { TargetId = target } : error;

    public void Dispose()
    {
        _main.VerifyAccess();
        lock (_faultGate)
        {
            if (_disposed) return;
            _disposed = true;
            _pendingFaults.Clear();
        }
        foreach (var entry in _services.Values.ToArray()) Remove(entry);
        foreach (var watcher in _watchers.ToArray()) watcher.Dispose();
        foreach (var message in _messages.ToArray()) message.Dispose();
        _messageTypes.Clear(); ProviderFaulted = null;
    }

    private sealed class PluginView : IPluginServices
    {
        private readonly InterPluginServiceRegistry _owner; private readonly IPluginContext _context; private readonly Action<Exception>? _fault;
        public PluginView(InterPluginServiceRegistry owner, IPluginContext context, Action<Exception>? fault) { _owner = owner; _context = context; _fault = fault; }
        public OperationResult<IServiceRegistration> Publish<T>(string id, Version version, T provider) where T : class => _owner.Publish(_context, id, version, provider, _fault);
        public ServiceResolution<T> Lookup<T>(ServiceQuery query) where T : class => _owner.Lookup<T>(_context, query);
        public IDisposable Watch(ServiceQuery query, Action<ServiceAvailability> changed) => _owner.Watch(_context, query, changed, _fault);
        public OperationResult<IDisposable> SubscribeMessage<T>(MessageChannel<T> channel, Action<T> handler, int priority = 0) => _owner.SubscribeMessage(_context, channel, handler, priority, _fault);
        public OperationResult PublishMessage<T>(MessageChannel<T> channel, T message) => _owner.PublishMessage(_context, channel, message);
    }
}

/// <summary>Public for DispatchProxy's generated assembly; instances are configured only by the host registry.</summary>
public class ServiceInvocationProxy : DispatchProxy
{
    internal Func<MethodInfo, object?[]?, object?> InvokeService = null!;
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => InvokeService(targetMethod!, args);
}
