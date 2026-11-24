using RizToolbox.Core.Diagnostics;
using RizToolbox.Core.Targets;
using RizToolbox.Sdk;

namespace RizToolbox.Core.Mutation;

/// <summary>Coordinates only adapter-declared reversible properties; it never executes arbitrary game commands.</summary>
public sealed class MutationCoordinator : IDisposable
{
    private readonly SemanticTargetRegistry _targets;
    private readonly IMainThreadDispatcher _main;
    private readonly ILogger _log;
    private readonly Dictionary<(string Key, long Generation, string Property), IChannel> _channels = new();
    private bool _disposed;
    public event Action<string, Exception>? ProviderFaulted;

    internal interface IChannel : IDisposable
    {
        bool Valid { get; }
        void Invalidate();
        OperationResult Refresh();
    }

    public MutationCoordinator(SemanticTargetRegistry targets, IMainThreadDispatcher main, ILogger log)
    {
        _targets = targets; _main = main; _log = log;
        _main.VerifyAccess(); _targets.Invalidated += OnInvalidated;
    }

    public OperationResult<MutationChannel<T>> Register<T>(TargetHandle<T> target, string property, Action<T> write,
        MutationStrategy strategy = MutationStrategy.PriorityOverride,
        ExternalWritePolicy externalWrites = ExternalWritePolicy.AdoptAndReapply,
        Func<T, string?>? validate = null, IEqualityComparer<T>? comparer = null)
    {
        _main.VerifyAccess();
        if (_disposed) return OperationResult<MutationChannel<T>>.Fail(ErrorCode.ScopeExpired, "Coordinator is disposed.", TargetCapabilityIds.Mutations, target?.Key);
        if (target is null || string.IsNullOrWhiteSpace(property) || property.Length > 128 || property.Any(char.IsControl) || write is null)
            return OperationResult<MutationChannel<T>>.Fail(ErrorCode.InvalidArgument, "Target, property and write callback are required.", TargetCapabilityIds.Mutations, target?.Key);
        if (!Enum.IsDefined(typeof(MutationStrategy), strategy) || !Enum.IsDefined(typeof(ExternalWritePolicy), externalWrites))
            return OperationResult<MutationChannel<T>>.Fail(ErrorCode.MutationNotSupported, "Unknown strategy or external-write policy.", TargetCapabilityIds.Mutations, target.Key);
        var key = (target.Key, target.Generation, property);
        if (_channels.TryGetValue(key, out var existing) && existing.Valid)
            return OperationResult<MutationChannel<T>>.Fail(ErrorCode.Conflict, "Target property already has a coordinator channel.", TargetCapabilityIds.Mutations, target.Key);
        var acquired = _targets.Acquire(target);
        if (!acquired.Success || acquired.Value is null) return OperationResult<MutationChannel<T>>.Fail(acquired.Error!);
        var initial = acquired.Value.Read();
        if (!initial.Success) { acquired.Value.Dispose(); return OperationResult<MutationChannel<T>>.Fail(initial.Error!); }
        var channel = new MutationChannel<T>(new(target, property), acquired.Value, write, strategy, externalWrites,
            initial.Value!, validate, comparer ?? EqualityComparer<T>.Default, _main, _log, ReportFault,
            finished => { if (_channels.TryGetValue(key, out var current) && ReferenceEquals(current, finished)) _channels.Remove(key); });
        _channels[key] = channel;
        return OperationResult<MutationChannel<T>>.Ok(channel);
    }

    public IMutationService ForPlugin(IPluginContext context, Action<Exception>? onFault = null)
        => new PluginView(this, context, onFault);

    public IReadOnlyList<OperationResult> RefreshAll()
    {
        _main.VerifyAccess();
        if (_disposed) return new[] { OperationResult.Fail(ErrorCode.ScopeExpired, "Coordinator is disposed.", TargetCapabilityIds.Mutations) };
        return _channels.Values.ToArray().Select(x => x.Refresh()).ToArray();
    }

    private OperationResult<MutationChannel<T>> Find<T>(MutationKey<T>? key)
    {
        _main.VerifyAccess();
        if (_disposed || key?.Target is null || !_channels.TryGetValue((key.Target.Key, key.Target.Generation, key.Property), out var entry) || !entry.Valid)
            return OperationResult<MutationChannel<T>>.Fail(ErrorCode.MutationNotSupported, "No live adapter property matches this target generation.", TargetCapabilityIds.Mutations, key?.Target?.Key);
        return entry is MutationChannel<T> typed ? OperationResult<MutationChannel<T>>.Ok(typed)
            : OperationResult<MutationChannel<T>>.Fail(ErrorCode.TargetTypeMismatch, "Mutation property exposes a different value type.", TargetCapabilityIds.Mutations, key.Target.Key);
    }
    private void OnInvalidated(TargetIdentity identity)
    {
        foreach (var channel in _channels.Where(x => x.Key.Key == identity.Key && x.Key.Generation == identity.Generation).Select(x => x.Value).ToArray())
            channel.Invalidate();
    }
    private void ReportFault(string owner, Exception exception)
    {
        _log.TryLog(LogLevel.Error, $"Mutation provider '{owner}' failed and its lease was removed.", exception);
        foreach (var callback in ProviderFaulted?.GetInvocationList() ?? Array.Empty<Delegate>())
            try { ((Action<string, Exception>)callback)(owner, exception); }
            catch (Exception ex) { _log.TryLog(LogLevel.Error, "Mutation fault observer failed.", ex); }
    }
    public void Dispose()
    {
        _main.VerifyAccess(); if (_disposed) return;
        _disposed = true;
        foreach (var channel in _channels.Values.ToArray()) channel.Dispose();
        _channels.Clear(); _targets.Invalidated -= OnInvalidated; ProviderFaulted = null;
    }

    private sealed class PluginView : IMutationService
    {
        private readonly MutationCoordinator _owner; private readonly IPluginContext _context; private readonly Action<Exception>? _fault;
        public PluginView(MutationCoordinator owner, IPluginContext context, Action<Exception>? fault) { _owner = owner; _context = context; _fault = fault; }
        private OperationResult<IMutationLease<T>> Own<T>(OperationResult<IMutationLease<T>> result)
        {
            if (!result.Success || result.Value is null) return result;
            return OperationResult<IMutationLease<T>>.Ok(_context.Own<IMutationLease<T>>(new ScopedLease<T>(_context, result.Value)));
        }
        public OperationResult<IMutationLease<T>> Override<T>(MutationKey<T> key, T value, int priority = 0)
        {
            _context.MainThread.VerifyAccess(); var channel = _owner.Find(key);
            return channel.Success ? Own(channel.Value!.Override(_context.PluginId, value, priority, _fault)) : OperationResult<IMutationLease<T>>.Fail(channel.Error!);
        }
        public OperationResult<IMutationLease<T>> Transform<T>(MutationKey<T> key, Func<T, T> transform, int order = 0)
        {
            _context.MainThread.VerifyAccess(); var channel = _owner.Find(key);
            if (!channel.Success) return OperationResult<IMutationLease<T>>.Fail(channel.Error!);
            if (transform is null) return OperationResult<IMutationLease<T>>.Fail(ErrorCode.InvalidArgument, "Transform is null.", TargetCapabilityIds.Mutations, key.Target.Key);
            return Own(channel.Value!.Transform(_context.PluginId, value => { _context.MainThread.VerifyAccess(); return transform(value); }, order, _fault));
        }
        public OperationResult<IMutationLease<T>> AcquireExclusive<T>(MutationKey<T> key, T initialValue)
        {
            _context.MainThread.VerifyAccess(); var channel = _owner.Find(key);
            return channel.Success ? Own(channel.Value!.AcquireExclusive(_context.PluginId, initialValue, _fault)) : OperationResult<IMutationLease<T>>.Fail(channel.Error!);
        }
        public OperationResult<MutationSnapshot<T>> Describe<T>(MutationKey<T> key)
        {
            _context.MainThread.VerifyAccess(); var channel = _owner.Find(key);
            return channel.Success ? OperationResult<MutationSnapshot<T>>.Ok(channel.Value!.Describe()) : OperationResult<MutationSnapshot<T>>.Fail(channel.Error!);
        }
    }
    private sealed class ScopedLease<T> : IMutationLease<T>
    {
        private readonly IPluginContext _context; private readonly IMutationLease<T> _inner;
        public ScopedLease(IPluginContext context, IMutationLease<T> inner) { _context = context; _inner = inner; }
        public string LeaseId => _inner.LeaseId;
        public MutationKey<T> Key => _inner.Key;
        public bool IsActive { get { _context.MainThread.VerifyAccess(); return _inner.IsActive; } }
        public OperationResult SetValue(T value) { _context.MainThread.VerifyAccess(); return _inner.SetValue(value); }
        public void Dispose() => _inner.Dispose();
    }
}
