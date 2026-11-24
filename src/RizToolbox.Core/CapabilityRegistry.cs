using RizToolbox.Core.Diagnostics;
using RizToolbox.Sdk;

namespace RizToolbox.Core;

/// <summary>One exact contract per capability. Mutations run on the constructing thread; lookups are synchronized.</summary>
public sealed class CapabilityRegistry : ICapabilityMetadataProvider
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CapabilityInfo> _metadata = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private readonly ILogger? _log;
    private long _generation;
    private sealed record Entry(CapabilityDescriptor Descriptor, Type? Contract, object? Service,
        Func<IPluginContext, object>? Factory, long Generation);

    public CapabilityRegistry(GameBuildDescriptor gameBuild, ILogger? log = null) => (GameBuild, _log) = (gameBuild, log);
    public GameBuildDescriptor GameBuild { get; }
    /// <summary>Raised synchronously after a change. Hosts use this to stop dependents or activate waiting plugins.</summary>
    public event Action? Changed;

    /// <summary>Declares operation semantics without advertising an unavailable implementation as ready.</summary>
    public void DeclareMetadata(string id, Version version, CapabilityStability stability,
        IEnumerable<CapabilityOperationInfo> operations)
    {
        VerifyMutation(id);
        var declared = new CapabilityInfo(id, version, stability, false, 0, operations, "Provider has not registered a service.");
        lock (_gate) _metadata[id] = declared;
        NotifyChanged();
    }

    public IReadOnlyList<CapabilityInfo> GetAll()
    {
        lock (_gate) return Array.AsReadOnly(_entries.Keys.Concat(_metadata.Keys).Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal).Select(DescribeLocked).ToArray());
    }

    /// <summary>Bounded status selection without a full catalog sort or cloning unbounded operation lists.</summary>
    public DiagnosticRowCopy<DiagnosticCapabilityRow> CaptureDiagnosticMetadata(int maximumRows)
    {
        DiagnosticRowLimit.Validate(maximumRows);
        lock (_gate)
        {
            var rows = new List<DiagnosticCapabilityRow>(maximumRows);
            var entryVisits = 0;
            foreach (var pair in _entries)
            {
                if (rows.Count == maximumRows) break;
                entryVisits++;
                _metadata.TryGetValue(pair.Key, out var metadata);
                rows.Add(new(pair.Key, metadata?.Version ?? new Version(0, 0),
                    metadata?.Stability ?? CapabilityStability.Unknown, pair.Value.Descriptor.Available,
                    pair.Value.Generation, pair.Value.Descriptor.UnavailableReason,
                    metadata?.Operations ?? Array.Empty<CapabilityOperationInfo>()));
            }
            var metadataVisits = 0;
            foreach (var pair in _metadata)
            {
                // Duplicate registered IDs also count as inspected inputs. Even
                // a catalog dominated by duplicates has a finite traversal.
                if (metadataVisits == maximumRows || rows.Count == maximumRows) break;
                metadataVisits++;
                if (_entries.ContainsKey(pair.Key)) continue;
                rows.Add(new(pair.Key, pair.Value.Version, pair.Value.Stability, false, 0,
                    "Provider has not registered a service.", pair.Value.Operations));
            }
            return new(Array.AsReadOnly(rows.ToArray()),
                _entries.Count - entryVisits + _metadata.Count - metadataVisits);
        }
    }

    public Result<CapabilityInfo> Describe(string capabilityId)
    {
        if (string.IsNullOrWhiteSpace(capabilityId)) return Result<CapabilityInfo>.Fail(new(ErrorCode.InvalidArgument, "A capability ID is required."));
        lock (_gate) return _entries.ContainsKey(capabilityId) || _metadata.ContainsKey(capabilityId)
            ? Result<CapabilityInfo>.Ok(DescribeLocked(capabilityId))
            : Result<CapabilityInfo>.Fail(new(ErrorCode.CapabilityUnavailable, "The capability has not been declared.", CapabilityId: capabilityId));
    }

    private CapabilityInfo DescribeLocked(string id)
    {
        _entries.TryGetValue(id, out var entry); _metadata.TryGetValue(id, out var metadata);
        return new(id, metadata?.Version ?? new Version(0, 0), metadata?.Stability ?? CapabilityStability.Unknown,
            entry?.Descriptor.Available ?? false, entry?.Generation ?? 0,
            metadata?.Operations ?? Array.Empty<CapabilityOperationInfo>(),
            entry?.Descriptor.UnavailableReason ?? "Provider has not registered a service.");
    }

    public void Register<T>(string id, T service) where T : class
    {
        if (service is null) throw new ArgumentNullException(nameof(service));
        RegisterCore(id, typeof(T), service, null);
    }

    /// <summary>The factory creates a service view for one enable scope.</summary>
    public void RegisterScoped<T>(string id, Func<IPluginContext, T> factory) where T : class
    {
        if (factory is null) throw new ArgumentNullException(nameof(factory));
        RegisterCore(id, typeof(T), null, context => factory(context));
    }

    private void RegisterCore(string id, Type contract, object? service, Func<IPluginContext, object>? factory)
    {
        VerifyMutation(id);
        lock (_gate)
        {
            if (_entries.TryGetValue(id, out var prior) && prior.Descriptor.Available)
                throw new InvalidOperationException($"Capability '{id}' already has a service. Withdraw it before replacing it.");
            _entries[id] = new(new(id, true), contract, service, factory, ++_generation);
        }
        NotifyChanged();
    }

    /// <summary>Withdraws a capability. Existing scoped views immediately become invalid.</summary>
    public void SetUnavailable(string id, string reason)
    {
        VerifyWithdrawal(id, reason);
        lock (_gate) if (!SetUnavailableLocked(id, reason)) return;
        NotifyChanged();
    }

    /// <summary>Withdraw all capabilities in the affected group before notifying plugin scopes.</summary>
    public void SetUnavailableBatch(IReadOnlyList<(string Id, string Reason)> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        foreach (var (id, reason) in changes) VerifyWithdrawal(id, reason);
        var changed = false;
        lock (_gate)
            foreach (var (id, reason) in changes) changed |= SetUnavailableLocked(id, reason);
        if (changed) NotifyChanged();
    }

    private void VerifyWithdrawal(string id, string reason)
    {
        VerifyMutation(id);
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("An unavailable reason is required.", nameof(reason));
    }

    private bool SetUnavailableLocked(string id, string reason)
    {
        if (_entries.TryGetValue(id, out var prior) && !prior.Descriptor.Available && prior.Descriptor.UnavailableReason == reason) return false;
        _entries[id] = new(new(id, false, reason), null, null, null, ++_generation);
        return true;
    }

    private void NotifyChanged()
    {
        if (Changed is not { } handlers) return;
        foreach (Action handler in handlers.GetInvocationList())
        {
            try { handler(); }
            catch (Exception error) { _log?.TryLog(LogLevel.Error, "Capability change listener failed.", error); }
        }
    }

    public CapabilityDescriptor GetCapability(string id)
    {
        lock (_gate)
            return _entries.TryGetValue(id, out var entry)
                ? entry.Descriptor : new(id, false, $"Capability '{id}' was not provided by this adapter.");
    }

    /// <summary>Host-side unscoped lookup. Plugins receive guarded views through IPluginContext instead.</summary>
    public ServiceLookup<T> GetService<T>(string id) where T : class => GetService<T>(id, null);

    internal ServiceLookup<T> GetService<T>(string id, IPluginContext? context) where T : class
    {
        if (string.IsNullOrWhiteSpace(id)) return new(false, null, new(ErrorCode.InvalidArgument, "Capability ID is required.", id));
        Entry entry;
        lock (_gate)
        {
            if (!_entries.TryGetValue(id, out entry!))
                return new(false, null, new(ErrorCode.CapabilityUnavailable, $"Capability '{id}' was not provided by this adapter.", id, Retryable: true));
            if (!entry.Descriptor.Available) return new(false, null, new(ErrorCode.CapabilityUnavailable, entry.Descriptor.UnavailableReason ?? $"Capability '{id}' is unavailable.", id, Retryable: true));
            if (entry.Contract != typeof(T))
                return new(false, null, new(ErrorCode.ContractIdentityMismatch, $"Capability '{id}' does not expose contract '{typeof(T).FullName}'.", id));
        }
        if (entry.Factory is null) return new(true, (T)entry.Service!, null);
        if (context is null) return new(false, null, new(ErrorCode.ScopeExpired, $"Capability '{id}' requires a plugin enable scope.", id));
        try
        {
            var service = entry.Factory(context);
            return service is T typed ? new(true, typed, null) : new(false, null, new(ErrorCode.ContractIdentityMismatch, "Scoped service factory returned null or an invalid contract.", id));
        }
        catch (Exception ex) { return new(false, null, new(ErrorCode.InternalError, $"Scoped service factory failed: {ex.Message}", id)); }
    }

    internal long GetGeneration(string id)
    {
        lock (_gate) return _entries.TryGetValue(id, out var entry) && entry.Descriptor.Available ? entry.Generation : -1;
    }

    internal bool IsScoped(string id)
    {
        lock (_gate) return _entries.TryGetValue(id, out var entry) && entry.Factory is not null;
    }

    private void VerifyMutation(string id)
    {
        if (Environment.CurrentManagedThreadId != _threadId)
            throw new InvalidOperationException("Capability changes must run on the host main thread.");
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Capability ID is required.", nameof(id));
    }
}
