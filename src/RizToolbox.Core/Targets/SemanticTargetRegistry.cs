using RizToolbox.Core.Diagnostics;
using RizToolbox.Sdk;

namespace RizToolbox.Core.Targets;

/// <summary>The adapter performs discovery once and publishes bindings here. Queries do not scan the game.</summary>
public sealed class SemanticTargetRegistry : IDisposable
{
    private readonly IMainThreadDispatcher _main;
    private readonly ILogger _log;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private static readonly HashSet<IDisposable> UnconfirmedLifetimes = new(ReferenceEqualityComparer.Instance);
    // Shared across registry lifetimes and value types; a foreign/retired registry handle
    // must never identify another binding merely because its canonical key was reused.
    private static long _generation;
    private bool _disposed;
    public event Action<TargetIdentity>? Invalidated;

    private abstract class Entry
    {
        public string Key = null!;
        public long Generation;
        public TargetMetadata Metadata = null!;
        public string[] Aliases = null!;
        public Func<bool> Alive = null!;
        public IDisposable? Lifetime;
        public bool Valid = true;
        public abstract Type ValueType { get; }
        public TargetDescriptor Describe() => new(Key, Generation, ValueType, Metadata, Array.AsReadOnly(Aliases));
    }
    private sealed class Entry<T> : Entry
    {
        public Func<T> Read = null!;
        public override Type ValueType => typeof(T);
    }

    public SemanticTargetRegistry(IMainThreadDispatcher main, ILogger log) { _main = main; _log = log; }

    /// <summary>Transfers lifetime ownership only on success. Each registration gets a new generation, even if its key was previously removed.</summary>
    public OperationResult<TargetHandle<T>> Register<T>(string canonicalKey, Func<T> read,
        Func<bool>? isAlive = null, IDisposable? lifetime = null, TargetMetadata? metadata = null, IEnumerable<string>? aliases = null)
    {
        _main.VerifyAccess();
        if (_disposed) return OperationResult<TargetHandle<T>>.Fail(ErrorCode.ScopeExpired, "Registry is disposed.", TargetCapabilityIds.Targets, canonicalKey);
        if (!ValidKey(canonicalKey)) return OperationResult<TargetHandle<T>>.Fail(ErrorCode.InvalidArgument, "Canonical key is empty or contains control characters.", TargetCapabilityIds.Targets, canonicalKey);
        if (read is null) return OperationResult<TargetHandle<T>>.Fail(ErrorCode.InvalidArgument, "A read callback is required.", TargetCapabilityIds.Targets, canonicalKey);
        if (_entries.ContainsKey(canonicalKey)) return OperationResult<TargetHandle<T>>.Fail(ErrorCode.Conflict, "Invalidate the existing canonical target before rebinding it.", TargetCapabilityIds.Targets, canonicalKey);
        string[] aliasValues;
        try { aliasValues = (aliases ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).Where(x => x != canonicalKey).OrderBy(x => x, StringComparer.Ordinal).ToArray(); }
        catch (Exception ex) { return OperationResult<TargetHandle<T>>.Fail(ErrorCode.InvalidArgument, ex.Message, TargetCapabilityIds.Targets, canonicalKey); }
        if (aliasValues.Any(x => !ValidKey(x))) return OperationResult<TargetHandle<T>>.Fail(ErrorCode.InvalidArgument, "An alias is invalid.", TargetCapabilityIds.Targets, canonicalKey);
        if (!TryAllocateGeneration(out var generation))
            return OperationResult<TargetHandle<T>>.Fail(ErrorCode.TargetGenerationExhausted, "No non-reused binding generations remain.", TargetCapabilityIds.Targets, canonicalKey);
        var entry = new Entry<T>
        {
            Key = canonicalKey, Generation = generation, Read = read, Alive = isAlive ?? (() => true),
            Lifetime = lifetime, Metadata = metadata ?? new(), Aliases = aliasValues
        };
        _entries.Add(canonicalKey, entry);
        return OperationResult<TargetHandle<T>>.Ok(new(canonicalKey, entry.Generation));
    }

    private static bool TryAllocateGeneration(out long generation)
    {
        while (true)
        {
            var previous = Volatile.Read(ref _generation);
            // Never increment first and detect overflow afterward: another registry could
            // otherwise consume a wrapped value before the exhausted counter was repaired.
            if (previous < 0 || previous == long.MaxValue) { generation = 0; return false; }
            var next = previous + 1;
            if (Interlocked.CompareExchange(ref _generation, next, previous) == previous)
            { generation = next; return true; }
        }
    }

    public TargetMatch<T> Resolve<T>(TargetQuery query)
    {
        _main.VerifyAccess();
        if (_disposed) return new(TargetMatchKind.Missing, null, Array.Empty<TargetDescriptor>(), new(ErrorCode.ScopeExpired, "Target registry is disposed.", TargetCapabilityIds.Targets, query?.Key));
        if (query is null) return new(TargetMatchKind.Missing, null, Array.Empty<TargetDescriptor>(), new(ErrorCode.InvalidArgument, "Target query is required.", TargetCapabilityIds.Targets));
        Prune();
        var candidates = _entries.Values.Where(entry => entry.ValueType == typeof(T)
            && (query.Key is null || entry.Key == query.Key || entry.Aliases.Contains(query.Key, StringComparer.Ordinal))
            && (query.Scene is null || entry.Metadata.Scene == query.Scene)
            && (query.Role is null || entry.Metadata.Role == query.Role)
            && (query.ContentId is null || entry.Metadata.ContentId == query.ContentId)
            && (query.ResourceId is null || entry.Metadata.ResourceId == query.ResourceId)
            && (!query.ActiveOnly || entry.Metadata.Active) && (query.IncludeTemplates || !entry.Metadata.Template))
            .OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Describe()).ToArray();
        return candidates.Length switch
        {
            0 => new(TargetMatchKind.Missing, null, candidates, new(ErrorCode.TargetMissing, "Target has not appeared or does not match this query.", TargetCapabilityIds.Targets, query.Key, true)),
            1 => new(TargetMatchKind.Unique, new(candidates[0].Key, candidates[0].Generation), candidates),
            _ => new(TargetMatchKind.Ambiguous, null, candidates, new(ErrorCode.AmbiguousTarget, "Query matches multiple bindings; specify scene, role, resource ID or canonical key.", TargetCapabilityIds.Targets, query.Key))
        };
    }

    public IReadOnlyList<TargetDescriptor> GetTargets()
    {
        _main.VerifyAccess();
        if (_disposed) return Array.Empty<TargetDescriptor>();
        Prune();
        return _entries.Values.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Describe()).ToArray();
    }

    public OperationResult<ITargetValueLease<T>> Acquire<T>(TargetHandle<T> target)
    {
        _main.VerifyAccess();
        var error = Find(target, out Entry<T>? entry);
        return error is null ? OperationResult<ITargetValueLease<T>>.Ok(new Lease<T>(this, target, entry!))
            : OperationResult<ITargetValueLease<T>>.Fail(error);
    }

    public OperationResult Invalidate<T>(TargetHandle<T> target)
    {
        _main.VerifyAccess();
        if (target is null) return OperationResult.Fail(ErrorCode.InvalidArgument, "Target is null.", TargetCapabilityIds.Targets);
        if (!_entries.TryGetValue(target.Key, out var entry) || entry.Generation != target.Generation) return OperationResult.Ok();
        if (entry.ValueType != typeof(T)) return OperationResult.Fail(ErrorCode.TargetTypeMismatch, "Binding exposes a different value type.", TargetCapabilityIds.Targets, target.Key);
        Remove(entry);
        return OperationResult.Ok();
    }

    /// <summary>Updates semantic aliases without changing the object generation or its leases. Ambiguous aliases remain explicit query failures.</summary>
    public OperationResult SetAliases<T>(TargetHandle<T> target, IEnumerable<string> aliases)
    {
        _main.VerifyAccess();
        var error = Find(target, out Entry<T>? entry);
        if (error is not null) return OperationResult.Fail(error);
        string[] values;
        try { values = aliases.Distinct(StringComparer.Ordinal).Where(x => x != entry!.Key).OrderBy(x => x, StringComparer.Ordinal).ToArray(); }
        catch (Exception ex) { return OperationResult.Fail(ErrorCode.InvalidArgument, ex.Message, TargetCapabilityIds.Targets, target.Key); }
        if (values.Any(x => !ValidKey(x))) return OperationResult.Fail(ErrorCode.InvalidArgument, "An alias is invalid.", TargetCapabilityIds.Targets, target.Key);
        entry!.Aliases = values;
        return OperationResult.Ok();
    }

    /// <summary>Updates scene, role or activity metadata for the same live object. Rebuilt objects require invalidation and Register.</summary>
    public OperationResult UpdateMetadata<T>(TargetHandle<T> target, TargetMetadata metadata)
    {
        _main.VerifyAccess();
        var error = Find(target, out Entry<T>? entry);
        if (error is not null) return OperationResult.Fail(error);
        if (metadata is null) return OperationResult.Fail(ErrorCode.InvalidArgument, "Metadata is null.", TargetCapabilityIds.Targets, target.Key);
        entry!.Metadata = metadata;
        return OperationResult.Ok();
    }

    public int InvalidateScene(string scene)
    {
        _main.VerifyAccess();
        var entries = _entries.Values.Where(x => x.Metadata.Scene == scene).ToArray();
        foreach (var entry in entries) Remove(entry);
        return entries.Length;
    }

    public ITargetService ForPlugin(IPluginContext context) => new PluginView(this, context);

    private void Prune()
    {
        foreach (var entry in _entries.Values.ToArray()) CheckAlive(entry);
    }
    private bool CheckAlive(Entry entry)
    {
        if (!entry.Valid || _disposed) return false;
        try { if (entry.Alive() && entry.Valid && !_disposed) return true; }
        catch (Exception ex) { _log.TryLog(LogLevel.Warning, $"Target liveness callback failed for '{entry.Key}'.", ex); }
        Remove(entry); return false;
    }
    private OperationError? Find<T>(TargetHandle<T>? target, out Entry<T>? entry)
    {
        entry = null;
        if (_disposed || target is null || !_entries.TryGetValue(target.Key, out var binding) || binding.Generation != target.Generation)
            return new(ErrorCode.TargetExpired, "Binding generation is absent or has been replaced.", TargetCapabilityIds.Targets, target?.Key);
        if (binding is not Entry<T> typed) return new(ErrorCode.TargetTypeMismatch, "Binding exposes a different value type.", TargetCapabilityIds.Targets, target.Key);
        if (!CheckAlive(binding)) return new(ErrorCode.TargetExpired, "Object was destroyed or invalidated.", TargetCapabilityIds.Targets, target.Key);
        entry = typed; return null;
    }
    private void Remove(Entry entry)
    {
        if (!entry.Valid) return;
        entry.Valid = false; _entries.Remove(entry.Key);
        // Invalidate subscribers before releasing adapter roots; subscribers may detach leases but must not touch the dead target.
        foreach (var callback in Invalidated?.GetInvocationList() ?? Array.Empty<Delegate>())
            try { ((Action<TargetIdentity>)callback)(new(entry.Key, entry.Generation)); }
            catch (Exception ex) { _log.TryLog(LogLevel.Error, "Target invalidation subscriber failed.", ex); }
        var lifetime = entry.Lifetime; entry.Lifetime = null;
        if (lifetime is null) return;
        try { lifetime.Dispose(); }
        catch (Exception ex)
        {
            lock (UnconfirmedLifetimes) UnconfirmedLifetimes.Add(lifetime);
            _log.TryLog(LogLevel.Warning, $"Target lifetime cleanup for '{entry.Key}' is unconfirmed. The owner is retained until process exit.", ex);
        }
    }
    private static bool ValidKey(string? key) => !string.IsNullOrWhiteSpace(key) && key.Length <= 512 && !key.Any(char.IsControl);

    public void Dispose()
    {
        _main.VerifyAccess(); if (_disposed) return;
        _disposed = true;
        foreach (var entry in _entries.Values.ToArray()) Remove(entry);
        Invalidated = null;
    }

    private sealed class Lease<T> : ITargetValueLease<T>
    {
        private SemanticTargetRegistry? _owner;
        private readonly Entry<T> _entry;
        public TargetHandle<T> Target { get; }
        public Lease(SemanticTargetRegistry owner, TargetHandle<T> target, Entry<T> entry) { _owner = owner; Target = target; _entry = entry; }
        public bool IsValid { get { var owner = _owner; if (owner is null) return false; owner._main.VerifyAccess(); return owner.Find(Target, out Entry<T>? _) is null; } }
        public OperationResult<T> Read()
        {
            var owner = _owner;
            if (owner is null) return OperationResult<T>.Fail(ErrorCode.TargetExpired, "Target lease is disposed.", TargetCapabilityIds.Targets, Target.Key);
            owner._main.VerifyAccess();
            var error = owner.Find(Target, out Entry<T>? _);
            if (error is not null) return OperationResult<T>.Fail(error);
            try
            {
                var value = _entry.Read();
                error = owner.Find(Target, out Entry<T>? _);
                return error is null ? OperationResult<T>.Ok(value) : OperationResult<T>.Fail(error);
            }
            catch (Exception ex) { return OperationResult<T>.Fail(ErrorCode.ObservationUnavailable, ex.Message, TargetCapabilityIds.Targets, Target.Key); }
        }
        public void Dispose() { var owner = _owner; if (owner is null) return; owner._main.VerifyAccess(); _owner = null; }
    }

    private sealed class PluginView : ITargetService
    {
        private readonly SemanticTargetRegistry _owner; private readonly IPluginContext _context;
        public PluginView(SemanticTargetRegistry owner, IPluginContext context) { _owner = owner; _context = context; }
        public TargetMatch<T> Resolve<T>(TargetQuery query) { _context.MainThread.VerifyAccess(); return _owner.Resolve<T>(query); }
        public IReadOnlyList<TargetDescriptor> GetTargets() { _context.MainThread.VerifyAccess(); return _owner.GetTargets(); }
        public OperationResult<ITargetValueLease<T>> Acquire<T>(TargetHandle<T> target)
        {
            _context.MainThread.VerifyAccess();
            var result = _owner.Acquire(target);
            if (!result.Success || result.Value is null) return result;
            return OperationResult<ITargetValueLease<T>>.Ok(_context.Own<ITargetValueLease<T>>(new ScopedLease<T>(_context, result.Value)));
        }
    }
    private sealed class ScopedLease<T> : ITargetValueLease<T>
    {
        private readonly IPluginContext _context; private readonly ITargetValueLease<T> _inner;
        public ScopedLease(IPluginContext context, ITargetValueLease<T> inner) { _context = context; _inner = inner; }
        public TargetHandle<T> Target => _inner.Target;
        public bool IsValid { get { _context.MainThread.VerifyAccess(); return _inner.IsValid; } }
        public OperationResult<T> Read() { _context.MainThread.VerifyAccess(); return _inner.Read(); }
        public void Dispose() => _inner.Dispose();
    }
}
