using System.Text.RegularExpressions;
using RizToolbox.Core.Diagnostics;
using RizToolbox.Sdk;

namespace RizToolbox.Core;

public enum PluginState { Disabled, Enabling, Enabled, Disabling, Faulted, Waiting }
public sealed record PluginResourceCounts(string PluginId, int Scopes, int RetainedOwnedResources, int CachedServiceViews);
public sealed record PluginStatus(string Id, Version Version, PluginState State, string? LastError, bool DesiredEnabled = false)
{
    public OperationError? Failure { get; init; }
}

/// <summary>All lifecycle calls must be made on the dispatcher thread. Disable cascades to active dependents.</summary>
public sealed class PluginHost : IDisposable
{
    private readonly CapabilityRegistry _capabilities;
    private readonly MainThreadDispatcher _dispatcher;
    private readonly EventHub _events;
    private readonly ILogger _log;
    private readonly Dictionary<string, Entry> _plugins = new(StringComparer.Ordinal);
    private readonly List<string> _enabledOrder = new();
    private readonly Dictionary<string, Exception> _pendingFaults = new(StringComparer.Ordinal);
    private bool _transitioning;
    private bool _reconciling;
    private bool _reconcilePending;
    private bool _disposed;
    private readonly string _dataRoot;
    private readonly Dictionary<string, PluginActivationIntent> _savedIntents = new(StringComparer.Ordinal);
    private HashSet<string> _demanded = new(StringComparer.Ordinal);
    private IReadOnlyCollection<string> _requiredCapabilityDemand = Array.Empty<string>();
    private bool _capabilityDemandDirty = true;
    private bool _enableUnspecified;

    private sealed class Entry
    {
        public IRizPlugin Plugin = null!;
        public PluginManifest Manifest = null!;
        public PluginState State;
        public PluginScope? Scope;
        public OperationError? Error;
        public bool DesiredEnabled;
        public bool ExplicitlyDisabled;
    }

    public PluginHost(CapabilityRegistry capabilities, MainThreadDispatcher dispatcher, EventHub events, ILogger log,
        string? dataRoot = null, ExtensionPointRegistry? extensions = null)
    {
        _capabilities = capabilities;
        _dispatcher = dispatcher;
        _events = events;
        _log = log;
        _dispatcher.VerifyAccess();
        _dataRoot = Path.GetFullPath(dataRoot ?? Path.Combine(AppContext.BaseDirectory, "RizToolboxData"));
        var existingExtensions = _capabilities.GetService<IExtensionService>(CapabilityIds.Extensions);
        if (existingExtensions.Available)
        {
            if (existingExtensions.Service is not ExtensionPointRegistry existing || (extensions is not null && !ReferenceEquals(extensions, existing)))
                throw new ArgumentException("The extension capability must use the host's ExtensionPointRegistry.", nameof(extensions));
            Extensions = existing;
        }
        else
        {
            Extensions = extensions ?? new ExtensionPointRegistry(log);
            _capabilities.Register<IExtensionService>(CapabilityIds.Extensions, Extensions);
        }
        if (!_capabilities.GetCapability(CapabilityIds.Metadata).Available)
            _capabilities.Register<ICapabilityMetadataProvider>(CapabilityIds.Metadata, _capabilities);
        if (!_capabilities.GetCapability(PluginLifecycleCapabilityIds.Dependencies).Available)
            _capabilities.RegisterScoped<IPluginDependencyService>(PluginLifecycleCapabilityIds.Dependencies,
                context => new DependencyView(this, context.PluginId));
        _capabilities.Changed += OnCapabilitiesChanged;
    }

    // Observed on the dispatcher thread around a host command. Cleanup can
    // fail even when the existing lifecycle transition or SDK result returns.
    internal OperationError? LatestLifecycleFailure { get; private set; }

    public ExtensionPointRegistry Extensions { get; }
    /// <summary>Raised after an explicit user intent changes; runtime capability/fault changes do not rewrite user intent.</summary>
    public event Action? ActivationIntentsChanged;

    /// <summary>Required capabilities for structurally valid desired packages and their hard dependencies.
    /// Availability is deliberately excluded so waiting packages can request providers.</summary>
    public IReadOnlyCollection<string> GetRequiredCapabilityDemand()
    {
        _dispatcher.VerifyAccess();
        if (_disposed) return Array.Empty<string>();
        if (!_capabilityDemandDirty) return _requiredCapabilityDemand;

        var capabilities = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var entry in _plugins.Values.Where(x => x.DesiredEnabled))
        {
            var order = new List<Entry>();
            var error = Resolve(entry.Manifest.Id, new(StringComparer.Ordinal), new(StringComparer.Ordinal), order,
                planRequiredCapabilities: true);
            if (error is not null) continue;
            foreach (var participant in order)
                capabilities.UnionWith(participant.Manifest.RequiredCapabilities!);
        }
        if (!capabilities.SequenceEqual(_requiredCapabilityDemand))
            _requiredCapabilityDemand = Array.AsReadOnly(capabilities.ToArray());
        _capabilityDemandDirty = false;
        return _requiredCapabilityDemand;
    }

    public IReadOnlyList<PluginActivationIntent> CaptureActivationIntents()
    {
        _dispatcher.VerifyAccess();
        var all = new Dictionary<string, PluginActivationIntent>(_savedIntents, StringComparer.Ordinal);
        foreach (var entry in _plugins.Values) all[entry.Manifest.Id] = new(entry.Manifest.Id, entry.DesiredEnabled, entry.ExplicitlyDisabled);
        return Array.AsReadOnly(all.Values.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray());
    }

    /// <summary>Validates the complete snapshot before applying it. Unknown IDs are retained for packages registered later.</summary>
    public OperationResult RestoreActivationIntents(IEnumerable<PluginActivationIntent> intents, bool enableUnspecified = false)
    {
        _dispatcher.VerifyAccess();
        if (_disposed) return OperationResult.Fail(ErrorCode.ScopeExpired, "Plugin host is disposed.");
        if (_transitioning || _reconciling) return OperationResult.Fail(ErrorCode.Conflict, "Cannot restore activation intents during a lifecycle transition.");
        PluginActivationIntent[] snapshot;
        try { snapshot = intents.ToArray(); }
        catch (Exception ex) { return OperationResult.Fail(ErrorCode.ConfigInvalid, "Activation intents are invalid: " + ex.Message); }
        var error = PluginActivationStore.Validate(snapshot);
        if (error is not null) return OperationResult.Fail(ErrorCode.ConfigInvalid, error);
        _savedIntents.Clear();
        foreach (var intent in snapshot) _savedIntents.Add(intent.Id, intent);
        _enableUnspecified = enableUnspecified;
        foreach (var entry in _plugins.Values)
        {
            _savedIntents.TryGetValue(entry.Manifest.Id, out var intent);
            entry.DesiredEnabled = intent?.DesiredEnabled ?? enableUnspecified;
            entry.ExplicitlyDisabled = intent?.ExplicitlyDisabled ?? false;
        }
        Reconcile();
        return OperationResult.Ok();
    }

    public OperationResult<PluginDependencyStatus> GetDependencyStatus(string pluginId, string dependencyId)
    {
        _dispatcher.VerifyAccess();
        if (_disposed) return OperationResult<PluginDependencyStatus>.Fail(ErrorCode.ScopeExpired, "Plugin host is disposed.", PluginLifecycleCapabilityIds.Dependencies, pluginId);
        if (!_plugins.TryGetValue(pluginId, out var owner)) return OperationResult<PluginDependencyStatus>.Fail(ErrorCode.TargetMissing, $"Plugin '{pluginId}' is not registered.", PluginLifecycleCapabilityIds.Dependencies, pluginId);
        var hard = owner.Manifest.Dependencies!.FirstOrDefault(x => x.Id == dependencyId);
        var optional = owner.Manifest.OptionalDependencies!.FirstOrDefault(x => x.Id == dependencyId);
        var dependency = hard ?? optional;
        if (dependency is null) return OperationResult<PluginDependencyStatus>.Fail(ErrorCode.InvalidArgument, $"'{dependencyId}' is not a declared dependency of '{pluginId}'.", PluginLifecycleCapabilityIds.Dependencies, dependencyId);
        _plugins.TryGetValue(dependencyId, out var provider);
        bool compatible = provider is not null && Compatible(provider.Manifest.Version, dependency);
        var reason = provider is null ? "Dependency is not installed." : !compatible ? "Installed dependency version is outside the declared range." :
            provider.State != PluginState.Enabled ? provider.Error?.Message ?? "Dependency is not enabled." : null;
        return OperationResult<PluginDependencyStatus>.Ok(new(dependencyId, hard is null, provider is not null, compatible,
            provider?.State == PluginState.Enabled, provider?.Manifest.Version, reason));
    }

    private sealed class DependencyView : IPluginDependencyService
    {
        private readonly PluginHost _host; private readonly string _id;
        public DependencyView(PluginHost host, string id) => (_host, _id) = (host, id);
        public OperationResult<PluginDependencyStatus> Query(string dependencyId) => _host.GetDependencyStatus(_id, dependencyId);
        public IReadOnlyList<PluginDependencyStatus> GetAll()
        {
            _host._dispatcher.VerifyAccess();
            var manifest = _host._plugins[_id].Manifest;
            return Array.AsReadOnly(manifest.Dependencies!.Concat(manifest.OptionalDependencies!).OrderBy(x => x.Id, StringComparer.Ordinal)
                .Select(x => Query(x.Id).Value!).ToArray());
        }
    }

    private void RaiseIntentChanged()
    {
        foreach (var callback in ActivationIntentsChanged?.GetInvocationList() ?? Array.Empty<Delegate>())
            try { ((Action)callback)(); } catch (Exception ex) { _log.Log(LogLevel.Error, "Cannot persist plugin activation intent.", ex); }
    }

    public IReadOnlyList<PluginStatus> Statuses
    {
        get
        {
            _dispatcher.VerifyAccess();
            return _plugins.Values.OrderBy(x => x.Manifest.Id, StringComparer.Ordinal)
                .Select(x => new PluginStatus(x.Manifest.Id, x.Manifest.Version, x.State, x.Error?.Message, x.DesiredEnabled)
                { Failure = x.Error }).ToArray();
        }
    }
    public IReadOnlyList<PluginResourceCounts> GetResourceCounts()
    {
        _dispatcher.VerifyAccess();
        return _plugins.Values.OrderBy(x => x.Manifest.Id, StringComparer.Ordinal).Select(entry =>
        {
            var count = entry.Scope?.ResourceCounts() ?? (Scopes: 0, Resources: 0, Services: 0);
            return new PluginResourceCounts(entry.Manifest.Id, count.Scopes, count.Resources, count.Services);
        }).ToArray();
    }

    /// <summary>Copies only the selected status rows; complete SDK queries retain their normal ordering.</summary>
    public DiagnosticRowCopy<PluginStatus> CaptureDiagnosticStatuses(int maximumRows)
    {
        _dispatcher.VerifyAccess(); DiagnosticRowLimit.Validate(maximumRows);
        var rows = new List<PluginStatus>(maximumRows);
        foreach (var entry in _plugins.Values)
        {
            if (rows.Count == maximumRows) break;
            rows.Add(new(entry.Manifest.Id, entry.Manifest.Version, entry.State, entry.Error?.Message, entry.DesiredEnabled)
                { Failure = entry.Error });
        }
        return new(Array.AsReadOnly(rows.ToArray()), _plugins.Count - rows.Count);
    }

    /// <summary>A shared scope-node budget bounds diagnostic traversal across all selected plugins.</summary>
    public DiagnosticRowCopy<PluginResourceCounts> CaptureDiagnosticResourceCounts(int maximumRows, int maximumScopeNodes)
    {
        _dispatcher.VerifyAccess(); DiagnosticRowLimit.Validate(maximumRows); DiagnosticRowLimit.Validate(maximumScopeNodes);
        var rows = new List<PluginResourceCounts>(maximumRows);
        var remaining = maximumScopeNodes;
        long truncated = 0;
        foreach (var entry in _plugins.Values)
        {
            if (rows.Count == maximumRows) break;
            var count = (Scopes: 0, Resources: 0, Services: 0);
            if (entry.Scope is { } scope)
            {
                count = scope.CaptureDiagnosticResourceCounts(ref remaining, out var incomplete);
                if (incomplete) truncated++;
            }
            rows.Add(new(entry.Manifest.Id, count.Scopes, count.Resources, count.Services));
        }
        return new(Array.AsReadOnly(rows.ToArray()), truncated + _plugins.Count - rows.Count);
    }

    public OperationResult Register(IRizPlugin plugin)
    {
        _dispatcher.VerifyAccess();
        if (_disposed) return OperationResult.Fail(ErrorCode.ScopeExpired, "Plugin host is disposed.");
        if (_transitioning) return OperationResult.Fail(ErrorCode.Conflict, "Plugin lifecycle transition is already in progress.");
        if (plugin is null) return OperationResult.Fail(ErrorCode.InvalidArgument, "Plugin is null.");
        PluginManifest manifest;
        try
        {
            var source = plugin.Manifest;
            var error = ValidateManifest(source);
            if (error is not null) return OperationResult.Fail(ErrorCode.InvalidArgument, error, targetId: source?.Id);
            // Freeze lists so subsequent plugin mutation cannot change the resolved dependency graph.
            manifest = source with
            {
                Dependencies = source.Dependencies?.ToArray() ?? Array.Empty<PluginDependency>(),
                RequiredCapabilities = source.RequiredCapabilities?.ToArray() ?? Array.Empty<string>(),
                OptionalDependencies = source.OptionalDependencies?.ToArray() ?? Array.Empty<PluginDependency>(),
                Conflicts = source.Conflicts?.ToArray() ?? Array.Empty<PluginConflict>(),
                LoadAfter = source.LoadAfter?.ToArray() ?? Array.Empty<string>()
            };
        }
        catch (Exception ex) { return OperationResult.Fail(ErrorCode.InvalidArgument, $"Cannot read plugin manifest: {ex.Message}"); }
        if (_plugins.ContainsKey(manifest.Id)) return OperationResult.Fail(ErrorCode.Conflict, $"Duplicate plugin ID '{manifest.Id}'.", targetId: manifest.Id);
        _savedIntents.TryGetValue(manifest.Id, out var saved);
        _plugins.Add(manifest.Id, new() { Plugin = plugin, Manifest = manifest, State = PluginState.Disabled,
            DesiredEnabled = saved?.DesiredEnabled ?? _enableUnspecified, ExplicitlyDisabled = saved?.ExplicitlyDisabled ?? false });
        Reconcile();
        return OperationResult.Ok();
    }

    /// <summary>Explicit enable intent also permits retrying this plugin after a fault.</summary>
    public OperationResult Enable(string id) => SetDesiredEnabled(id, true);

    public OperationResult Disable(string id) => SetDesiredEnabled(id, false);

    public OperationResult SetDesiredEnabled(string id, bool enabled)
    {
        _dispatcher.VerifyAccess();
        if (_disposed) return OperationResult.Fail(ErrorCode.ScopeExpired, "Plugin host is disposed.", targetId: id);
        if (_transitioning) return OperationResult.Fail(ErrorCode.Conflict, "Plugin lifecycle transition is already in progress.", targetId: id);
        if (!_plugins.TryGetValue(id, out var entry)) return OperationResult.Fail(ErrorCode.TargetMissing, $"Plugin '{id}' is not registered.", targetId: id);
        entry.DesiredEnabled = enabled;
        entry.ExplicitlyDisabled = !enabled;
        _savedIntents[id] = new(id, enabled, !enabled);
        if (enabled && entry.State == PluginState.Faulted) { entry.State = PluginState.Disabled; entry.Error = null; }
        Reconcile();
        RaiseIntentChanged();
        return !enabled || entry.State == PluginState.Enabled
            ? OperationResult.Ok() : OperationResult.Fail(entry.Error ?? new(ErrorCode.NotReady, $"Plugin '{id}' is waiting to activate.", TargetId: id, Retryable: true));
    }

    private OperationResult Activate(string id)
    {
        _dispatcher.VerifyAccess();
        if (_disposed) return OperationResult.Fail(ErrorCode.ScopeExpired, "Plugin host is disposed.", targetId: id);
        if (_transitioning) return OperationResult.Fail(ErrorCode.Conflict, "Plugin lifecycle transition is already in progress.", targetId: id);
        var order = new List<Entry>();
        var error = Resolve(id, new(StringComparer.Ordinal), new(StringComparer.Ordinal), order);
        if (error is not null) return OperationResult.Fail(error);
        if (_plugins[id].State == PluginState.Enabled) return OperationResult.Ok();

        var started = new List<Entry>();
        OperationResult result = OperationResult.Ok();
        _transitioning = true;
        try
        {
            foreach (var entry in order)
            {
                if (entry.State == PluginState.Enabled) continue;
                entry.Error = null;
                entry.State = PluginState.Enabling;
                entry.Scope = new PluginScope(entry.Manifest.Id, _capabilities, _dispatcher, _events, _log,
                    ex => OnFault(entry.Manifest.Id, ex),
                    ex => RecordLifecycleFailure(entry.Manifest.Id, ex), _dataRoot);
                try
                {
                    entry.Plugin.Enable(entry.Scope);
                    if (_pendingFaults.Count != 0)
                    {
                        // Fault attribution must remain with the subscriber, even when another plugin publishes during Enable.
                        _transitioning = false;
                        try { DrainFaults(); }
                        finally { _transitioning = true; }
                    }
                    if (entry.State == PluginState.Faulted)
                    {
                        for (int i = started.Count - 1; i >= 0; i--) StopEntry(started[i], false);
                        result = OperationResult.Fail(entry.Error ?? new(ErrorCode.InternalError, $"Plugin '{entry.Manifest.Id}' faulted during activation.", TargetId: entry.Manifest.Id));
                        break;
                    }
                    // A service factory or adapter callback can withdraw a capability during Enable.
                    // Roll back that activation as waiting, rather than exposing a briefly enabled dependent or faulting it.
                    var changedRequirements = Resolve(entry.Manifest.Id, new(StringComparer.Ordinal), new(StringComparer.Ordinal), new());
                    if (changedRequirements is not null)
                    {
                        entry.Error = changedRequirements;
                        StopEntry(entry, false);
                        for (int i = started.Count - 1; i >= 0; i--) StopEntry(started[i], false);
                        result = OperationResult.Fail(changedRequirements);
                        break;
                    }
                    entry.State = PluginState.Enabled;
                    _enabledOrder.Add(entry.Manifest.Id);
                    started.Add(entry);
                }
                catch (Exception ex)
                {
                    entry.Error = RecordLifecycleFailure(entry.Manifest.Id, ex);
                    StopEntry(entry, true);
                    for (int i = started.Count - 1; i >= 0; i--) StopEntry(started[i], false);
                    result = OperationResult.Fail(entry.Error);
                    _log.Log(LogLevel.Error, result.Error!.Message, ex);
                    break;
                }
            }
        }
        finally { _transitioning = false; DrainFaults(); }
        return result;
    }

    public IReadOnlyList<OperationResult> EnableAll()
    {
        _dispatcher.VerifyAccess();
        if (_disposed) return new[] { OperationResult.Fail(ErrorCode.ScopeExpired, "Plugin host is disposed.") };
        if (_transitioning) return new[] { OperationResult.Fail(ErrorCode.Conflict, "Plugin lifecycle transition is already in progress.") };
        foreach (var entry in _plugins.Values)
        {
            entry.DesiredEnabled = true;
            entry.ExplicitlyDisabled = false;
            _savedIntents[entry.Manifest.Id] = new(entry.Manifest.Id, true, false);
        }
        var results = Reconcile();
        RaiseIntentChanged();
        return results;
    }

    /// <summary>Reconciles all desired plugins. Faulted plugins require an explicit Enable before they can run again.</summary>
    public IReadOnlyList<OperationResult> Reconcile()
    {
        _dispatcher.VerifyAccess();
        if (_disposed) return new[] { OperationResult.Fail(ErrorCode.ScopeExpired, "Plugin host is disposed.") };
        _capabilityDemandDirty = true;
        if (_transitioning || _reconciling) { _reconcilePending = true; return Array.Empty<OperationResult>(); }
        var results = new List<OperationResult>();
        _reconciling = true;
        try
        {
            int passes = 0;
            do
            {
                _reconcilePending = false;
                var demanded = new HashSet<string>(StringComparer.Ordinal);
                void Demand(Entry entry)
                {
                    if (entry.ExplicitlyDisabled || !demanded.Add(entry.Manifest.Id)) return;
                    foreach (var dependency in entry.Manifest.Dependencies!)
                        if (_plugins.TryGetValue(dependency.Id, out var required)) Demand(required);
                }
                foreach (var entry in _plugins.Values.Where(x => x.DesiredEnabled).OrderBy(x => x.Manifest.Id, StringComparer.Ordinal)) Demand(entry);
                _demanded = demanded;
                _transitioning = true;
                try
                {
                    foreach (var id in _enabledOrder.AsEnumerable().Reverse().ToArray())
                    {
                        var error = Resolve(id, new(StringComparer.Ordinal), new(StringComparer.Ordinal), new());
                        if (!demanded.Contains(id) || error is not null)
                        {
                            StopEntry(_plugins[id], false);
                            if (_plugins[id].State != PluginState.Faulted) _plugins[id].Error = error;
                        }
                    }
                }
                finally { _transitioning = false; DrainFaults(); }
                foreach (var entry in _plugins.Values.Where(x => x.DesiredEnabled && !x.ExplicitlyDisabled)
                    .OrderBy(x => x.Manifest.Id, StringComparer.Ordinal).ToArray())
                {
                    if (entry.State == PluginState.Faulted) continue;
                    results.Add(Activate(entry.Manifest.Id));
                }
                foreach (var entry in _plugins.Values)
                {
                    if (entry.State is PluginState.Enabled or PluginState.Faulted) continue;
                    if (demanded.Contains(entry.Manifest.Id))
                    {
                        entry.Error = Resolve(entry.Manifest.Id, new(StringComparer.Ordinal), new(StringComparer.Ordinal), new())
                            ?? new(ErrorCode.NotReady, "Waiting for an enabled dependent to activate.", TargetId: entry.Manifest.Id, Retryable: true);
                        entry.State = PluginState.Waiting;
                    }
                    else { entry.State = PluginState.Disabled; entry.Error = null; }
                }
            } while (_reconcilePending && ++passes < 32);
            if (_reconcilePending) _log.Log(LogLevel.Error, "Capability changes did not settle after 32 lifecycle passes; reconcile again on the next host tick.");
        }
        finally { _reconciling = false; }
        return results;
    }

    private void OnCapabilitiesChanged()
    {
        _dispatcher.VerifyAccess();
        if (_disposed) return;
        if (_transitioning || _reconciling) _reconcilePending = true;
        else Reconcile();
    }

    private OperationError? Resolve(string id, HashSet<string> visiting, HashSet<string> visited, List<Entry> order,
        bool planRequiredCapabilities = false)
    {
        var path = new List<string>();
        var error = Visit(id);
        if (error is not null) return error;
        var participants = order.Concat(_enabledOrder.Select(key => _plugins[key])).Distinct().OrderBy(x => x.Manifest.Id, StringComparer.Ordinal).ToArray();
        foreach (var left in order.OrderBy(x => x.Manifest.Id, StringComparer.Ordinal))
        foreach (var right in participants)
        {
            if (left == right) continue;
            if (Conflicts(left.Manifest, right.Manifest) || Conflicts(right.Manifest, left.Manifest))
                return new(ErrorCode.Conflict, $"Activation '{id}' -> '{left.Manifest.Id}' <-> '{right.Manifest.Id}'; explicit package conflicts forbid simultaneous activation.", TargetId: id, Retryable: true);
        }
        return null;

        OperationError? Visit(string key)
        {
            if (visiting.Contains(key)) return new(ErrorCode.DependencyCycle, $"Plugin dependency/order cycle: {string.Join(" -> ", path.Concat(new[] { key }))}.", TargetId: key);
            if (visited.Contains(key)) return null;
            var route = string.Join(" -> ", path.Concat(new[] { key }));
            if (!_plugins.TryGetValue(key, out var entry)) return new(ErrorCode.DependencyUnsatisfied, $"{route}: required plugin '{key}' is not registered.", TargetId: key, Retryable: true);
            if (entry.ExplicitlyDisabled) return new(ErrorCode.DependencyUnsatisfied, $"{route}: required plugin '{key}' was explicitly disabled.", TargetId: key, Retryable: true);
            if (entry.State == PluginState.Faulted) return new(ErrorCode.DependencyUnsatisfied, $"{route}: plugin '{key}' is faulted and requires explicit enable: {entry.Error?.Message}", TargetId: key, Retryable: true);
            var manifest = entry.Manifest;
            if (SdkInfo.Version < manifest.MinSdk || (manifest.MaxSdkExclusive is not null && SdkInfo.Version >= manifest.MaxSdkExclusive))
                return new(ErrorCode.SdkIncompatible, $"{route}: plugin '{key}' does not support SDK {SdkInfo.Version}.", TargetId: key);
            if (!planRequiredCapabilities)
            {
                foreach (var capability in manifest.RequiredCapabilities!)
                {
                    var descriptor = _capabilities.GetCapability(capability);
                    if (!descriptor.Available) return new(ErrorCode.CapabilityUnavailable, $"{route}: plugin '{key}' requires unavailable capability '{capability}': {descriptor.UnavailableReason}", capability, key, true);
                }
            }
            visiting.Add(key); path.Add(key);
            try
            {
                foreach (var dependency in manifest.Dependencies!.OrderBy(x => x.Id, StringComparer.Ordinal))
                {
                    OperationError? dependencyError;
                    if (!_plugins.TryGetValue(dependency.Id, out var provider))
                        dependencyError = new(ErrorCode.DependencyUnsatisfied, $"{string.Join(" -> ", path.Concat(new[] { dependency.Id }))}: plugin '{key}' requires missing plugin '{dependency.Id}'.", TargetId: dependency.Id, Retryable: true);
                    else if (!Compatible(provider.Manifest.Version, dependency))
                        dependencyError = new(ErrorCode.DependencyUnsatisfied, $"{string.Join(" -> ", path.Concat(new[] { dependency.Id }))}: plugin '{key}' requires a different version of '{dependency.Id}' (installed {provider.Manifest.Version}).", TargetId: dependency.Id, Retryable: true);
                    else dependencyError = Visit(dependency.Id);
                    if (dependencyError is not null) return dependencyError;
                }
                // Ordering references never auto-enable absent/unrequested packages and cannot turn an optional provider into a hard dependency.
                if (!planRequiredCapabilities)
                {
                    var soft = manifest.LoadAfter!.Concat(manifest.OptionalDependencies!
                            .Where(d => _plugins.TryGetValue(d.Id, out var provider) && Compatible(provider.Manifest.Version, d)).Select(d => d.Id))
                        .Distinct(StringComparer.Ordinal).Where(candidate => _demanded.Contains(candidate))
                        .OrderBy(candidate => candidate, StringComparer.Ordinal);
                    foreach (var predecessor in soft)
                    {
                        int previousCount = order.Count;
                        var softError = Visit(predecessor);
                        if (softError is null) continue;
                        // An unavailable soft predecessor must not leave its partially resolved dependencies in this transaction.
                        foreach (var added in order.Skip(previousCount).ToArray()) visited.Remove(added.Manifest.Id);
                        order.RemoveRange(previousCount, order.Count - previousCount);
                        if (softError.Code == ErrorCode.DependencyCycle) return softError;
                    }
                }
                visited.Add(key); order.Add(entry);
                return null;
            }
            finally { visiting.Remove(key); path.RemoveAt(path.Count - 1); }
        }
    }

    private static bool Compatible(Version installed, PluginDependency dependency)
        => installed >= dependency.MinVersion && (dependency.MaxVersionExclusive is null || installed < dependency.MaxVersionExclusive);
    private static bool Conflicts(PluginManifest owner, PluginManifest other)
        => owner.Conflicts!.Any(rule => rule.Id == other.Id && (rule.MinVersion is null || other.Version >= rule.MinVersion) &&
            (rule.MaxVersionExclusive is null || other.Version < rule.MaxVersionExclusive));

    /// <summary>Reports an attributed adapter/service callback fault using the normal dependency-aware stop path.</summary>
    public OperationResult ReportPluginFault(string id, Exception error)
    {
        _dispatcher.VerifyAccess();
        if (_disposed) return OperationResult.Fail(ErrorCode.ScopeExpired, "Plugin host is disposed.", targetId: id);
        if (string.IsNullOrWhiteSpace(id) || !_plugins.TryGetValue(id, out var entry))
            return OperationResult.Fail(ErrorCode.TargetMissing, $"Plugin '{id}' is not registered.", targetId: id);
        if (error is null) return OperationResult.Fail(ErrorCode.InvalidArgument, "A plugin fault exception is required.", targetId: id);
        if (entry.State is not (PluginState.Enabled or PluginState.Enabling))
            return OperationResult.Fail(ErrorCode.ScopeExpired, $"Plugin '{id}' has no active scope to fault.", targetId: id);
        OnFault(id, error);
        return OperationResult.Ok();
    }

    private void OnFault(string id, Exception error)
    {
        _pendingFaults[id] = error;
        if (!_transitioning) DrainFaults();
    }

    private void DrainFaults()
    {
        if (_transitioning) return;
        if (_pendingFaults.Count != 0) _capabilityDemandDirty = true;
        _transitioning = true;
        try
        {
            while (_pendingFaults.Count > 0)
            {
                var fault = _pendingFaults.First();
                _pendingFaults.Remove(fault.Key);
                if (!_plugins.TryGetValue(fault.Key, out var entry)) continue;
                entry.Error = RecordLifecycleFailure(entry.Manifest.Id, fault.Value);
                StopWithDependents(fault.Key, true);
                _log.TryLog(LogLevel.Error, $"Plugin '{fault.Key}' was stopped after a callback exception.", fault.Value);
            }
        }
        finally { _transitioning = false; }
    }

    private void StopWithDependents(string id, bool faulted)
    {
        var stop = new HashSet<string>(StringComparer.Ordinal) { id };
        bool changed;
        do
        {
            changed = false;
            foreach (var activeId in _enabledOrder)
                if (_plugins[activeId].Manifest.Dependencies!.Any(d => stop.Contains(d.Id)))
                    changed |= stop.Add(activeId);
        } while (changed);
        foreach (var activeId in _enabledOrder.AsEnumerable().Reverse().Where(stop.Contains).ToArray())
            StopEntry(_plugins[activeId], faulted && activeId == id);
        var target = _plugins[id];
        if (target.State is PluginState.Enabled or PluginState.Enabling) StopEntry(target, faulted);
        else if (faulted) target.State = PluginState.Faulted;
    }

    private OperationError RecordLifecycleFailure(string pluginId, Exception error)
    {
        string message;
        try { message = error.Message; }
        catch { message = "Plugin lifecycle error details are unavailable."; }
        return LatestLifecycleFailure = new(ErrorCode.InternalError, message, TargetId: pluginId);
    }

    private void StopEntry(Entry entry, bool faulted)
    {
        if (entry.State is not (PluginState.Enabled or PluginState.Enabling)) return;
        entry.State = PluginState.Disabling;
        entry.Scope?.Deactivate();
        try { entry.Plugin.Disable(); }
        catch (Exception ex)
        {
            faulted = true;
            var failure = RecordLifecycleFailure(entry.Manifest.Id, ex);
            entry.Error ??= failure;
            _log.TryLog(LogLevel.Error, $"Plugin '{entry.Manifest.Id}' failed during disable.", ex);
        }
        finally
        {
            entry.Scope?.Dispose();
            entry.Scope = null;
            entry.State = faulted ? PluginState.Faulted : PluginState.Disabled;
            _enabledOrder.Remove(entry.Manifest.Id);
        }
    }

    private static string? ValidateManifest(PluginManifest? manifest)
    {
        if (manifest is null) return "Plugin manifest is null.";
        if (string.IsNullOrWhiteSpace(manifest.Id) || manifest.Id.Length > 128 || manifest.Id.EndsWith('.') || !Regex.IsMatch(manifest.Id, "^[a-z0-9][a-z0-9._-]*$"))
            return "Plugin ID must contain lowercase ASCII letters, numbers, dots, underscores or hyphens.";
        if (manifest.Version is null || manifest.MinSdk is null) return "Plugin and SDK versions are required.";
        if (manifest.MaxSdkExclusive is not null && manifest.MaxSdkExclusive <= manifest.MinSdk)
            return "SDK version range is empty.";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dependency in manifest.Dependencies ?? Array.Empty<PluginDependency>())
        {
            if (dependency is null || string.IsNullOrWhiteSpace(dependency.Id) || dependency.MinVersion is null)
                return "Plugin dependency is invalid.";
            if (!seen.Add(dependency.Id)) return $"Dependency '{dependency.Id}' appears more than once.";
            if (dependency.MaxVersionExclusive is not null && dependency.MaxVersionExclusive <= dependency.MinVersion)
                return $"Dependency '{dependency.Id}' has an empty version range.";
        }
        foreach (var dependency in manifest.OptionalDependencies ?? Array.Empty<PluginDependency>())
        {
            if (dependency is null || !ValidId(dependency.Id) || dependency.MinVersion is null) return "Optional dependency is invalid.";
            if (!seen.Add(dependency.Id)) return $"Dependency '{dependency.Id}' is declared more than once or as both required and optional.";
            if (dependency.MaxVersionExclusive is not null && dependency.MaxVersionExclusive <= dependency.MinVersion) return $"Optional dependency '{dependency.Id}' has an empty version range.";
        }
        if ((manifest.Dependencies ?? Array.Empty<PluginDependency>()).Any(x => x.Id == manifest.Id) ||
            (manifest.OptionalDependencies ?? Array.Empty<PluginDependency>()).Any(x => x.Id == manifest.Id)) return "A plugin cannot depend on itself.";
        var conflicts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var conflict in manifest.Conflicts ?? Array.Empty<PluginConflict>())
        {
            if (conflict is null || !ValidId(conflict.Id) || conflict.Id == manifest.Id || !conflicts.Add(conflict.Id)) return "Plugin conflict is invalid or repeated.";
            if (conflict.MinVersion is not null && conflict.MaxVersionExclusive is not null && conflict.MaxVersionExclusive <= conflict.MinVersion) return "Conflict version range is empty.";
        }
        var after = new HashSet<string>(StringComparer.Ordinal);
        foreach (var predecessor in manifest.LoadAfter ?? Array.Empty<string>())
            if (!ValidId(predecessor) || predecessor == manifest.Id || !after.Add(predecessor)) return "loadAfter reference is invalid or repeated.";
        if (manifest.RequiredCapabilities?.Any(string.IsNullOrWhiteSpace) == true) return "Capability ID is empty.";
        return null;
    }

    private static bool ValidId(string? id) => id is not null && id.Length <= 128 && !id.EndsWith('.') && Regex.IsMatch(id, "^[a-z0-9][a-z0-9._-]*$");

    public void Dispose()
    {
        _dispatcher.VerifyAccess();
        if (_disposed) return;
        if (_transitioning) throw new InvalidOperationException("Cannot dispose the host during a plugin lifecycle transition.");
        _transitioning = true;
        try
        {
            foreach (var id in _enabledOrder.AsEnumerable().Reverse().ToArray()) StopEntry(_plugins[id], false);
            _disposed = true;
            _capabilities.Changed -= OnCapabilitiesChanged;
        }
        finally { _transitioning = false; _pendingFaults.Clear(); }
    }
}
