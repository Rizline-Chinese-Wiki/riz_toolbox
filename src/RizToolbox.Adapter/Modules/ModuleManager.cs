using System.Reflection;
using RizToolbox.Core;
using RizToolbox.Core.Diagnostics;
using RizToolbox.Runtime;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Modules;

internal sealed record ModuleProbe(bool Ready, string? Reason = null, bool Permanent = false)
{
    internal static ModuleProbe Available { get; } = new(true);
    internal static ModuleProbe Waiting(string reason) => new(false, reason);
    internal static ModuleProbe Unsupported(string reason) => new(false, reason, true);
}

internal enum AdapterModuleTickCadence { Interval, EveryHostTick }

/// <summary>Internal adapter extension seam; ordinary plugin packages cannot load this contract.</summary>
internal interface IAdapterModule
{
    AdapterModuleManifest Manifest { get; }
    double IntervalSeconds { get; }
    AdapterModuleTickCadence TickCadence => AdapterModuleTickCadence.Interval;
    ModuleProbe Probe();
    void Register(ModuleRegistration registration);
    ModuleProbe Tick();
    void Release();
}

internal interface IModuleSymbolResolver
{
    AdapterSymbolProbe Probe(AdapterSymbolRequirement symbol);
}

internal sealed class NativeModuleSymbolResolver : IModuleSymbolResolver
{
    private readonly NativeRuntime _api;
    internal NativeModuleSymbolResolver(NativeRuntime api) => _api = api;
    public AdapterSymbolProbe Probe(AdapterSymbolRequirement symbol)
    {
        var signature = $"{symbol.Assembly}:{symbol.Namespace}.{symbol.Type}";
        try
        {
            var type = _api.Class(symbol.Assembly, symbol.Namespace, symbol.Type);
            string? actualType = null;
            if (symbol.Kind == AdapterSymbolKind.Field)
            { actualType = _api.DescribeFieldType(type, symbol.Member!); signature += $".{symbol.Member}: {actualType} [field]"; }
            else if (symbol.Kind == AdapterSymbolKind.Method)
            {
                var parameters = symbol.ParameterTypes?.ToArray() ?? Array.Empty<string>();
                signature += $".{symbol.Member}({string.Join(",", parameters)}) [{(symbol.IsStatic ? "static" : "instance")}]";
                var method = _api.Method(type, symbol.Member!, parameters.Length, symbol.IsStatic, parameters);
                actualType = _api.DescribeReturnType(method); signature += $" -> {actualType}";
            }
            if (symbol.ExpectedType is not null && actualType != symbol.ExpectedType)
                return new(symbol.Id, false, signature, $"Type changed: expected '{symbol.ExpectedType}', observed '{actualType}'.");
            return new(symbol.Id, true, signature, null);
        }
        catch (Exception ex) { return new(symbol.Id, false, signature, ex.Message); }
    }
}

/// <summary>Owns registrations and rolls back partial activation before releasing game handles.</summary>
internal sealed class ModuleRegistration
{
    private readonly CapabilityRegistry _capabilities;
    private readonly HashSet<string> _allowed;
    private readonly HashSet<string> _registered = new(StringComparer.Ordinal);
    internal ModuleRegistration(CapabilityRegistry capabilities, AdapterModuleManifest manifest)
    { _capabilities = capabilities; _allowed = manifest.Capabilities.ToHashSet(StringComparer.Ordinal); }
    internal void Register<T>(string id, T service) where T : class
    {
        if (!_allowed.Contains(id)) throw new InvalidOperationException($"Module did not declare capability '{id}'.");
        if (!_registered.Add(id)) throw new InvalidOperationException($"Module registered '{id}' twice.");
        try { _capabilities.Register<T>(id, service); }
        catch { _registered.Remove(id); throw; }
    }
    internal void RegisterScoped<T>(string id,Func<IPluginContext,T> factory) where T:class
    {
        if (!_allowed.Contains(id)) throw new InvalidOperationException($"Module did not declare capability '{id}'.");
        if (!_registered.Add(id)) throw new InvalidOperationException($"Module registered '{id}' twice.");
        try { _capabilities.RegisterScoped<T>(id,factory); }
        catch { _registered.Remove(id); throw; }
    }
    internal void RegisterScoped<T>(string id, Func<IPluginContext, Func<bool>, T> factory) where T : class
        => RegisterScoped<T>(id, context =>
        {
            // The registry assigns a new descriptor to each registration and withdrawal.
            var descriptor = _capabilities.GetCapability(id);
            return factory(context, () => descriptor.Available && ReferenceEquals(descriptor, _capabilities.GetCapability(id)));
        });

    internal void Complete()
    {
        if (!_allowed.SetEquals(_registered))
            throw new InvalidOperationException("Module did not register every declared capability.");
    }
    internal void Withdraw(string reason)
    {
        var changes = _registered.Select(id => (id, reason)).ToArray();
        _registered.Clear();
        _capabilities.SetUnavailableBatch(changes);
    }
}

/// <summary>One main-thread lifecycle per module, max four probes per tick, bounded retry epochs.</summary>
internal sealed class ModuleManager : IAdapterModuleStatusProvider, IDisposable
{
    internal const int MaximumAutomaticAttempts = 8;
    private static readonly HashSet<IAdapterModule> RetainedCleanupOwners = new(ReferenceEqualityComparer.Instance);
    private readonly CapabilityRegistry _capabilities;
    private readonly IModuleSymbolResolver _symbols;
    private readonly ILogger _log;
    private readonly AdapterSlowDiagnostics? _slowDiagnostics;
    private readonly string _profile, _fingerprint;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly List<Entry> _entries = new();
    private readonly Dictionary<string, Entry> _owners = new(StringComparer.Ordinal);
    private HashSet<string>? _capabilityDemand = new(StringComparer.Ordinal);
    private readonly HashSet<string> _retainedCapabilities = new(StringComparer.Ordinal);
    private readonly HashSet<string> _plannedCapabilities = new(StringComparer.Ordinal);
    private bool _demandPlanDirty = true;
    private string? _observedScene;
    private bool _disposed;
    private double _now;
    private sealed class Entry
    {
        internal IAdapterModule Module = null!;
        internal AdapterModuleManifest Manifest = null!;
        internal bool OnDemand;
        internal bool NeedsRelease;
        internal bool ReleaseFailed;
        internal AdapterModuleState State = AdapterModuleState.Waiting;
        internal AdapterSymbolProbe[] Symbols = Array.Empty<AdapterSymbolProbe>();
        internal AdapterVerificationTier Tier;
        internal AdapterModuleTickCadence TickCadence;
        internal double IntervalSeconds;
        internal int Attempts;
        internal int Failures;
        internal double Next;
        internal string? Failure = "Not yet probed.";
        internal Dictionary<string, long> DependencyGenerations = new(StringComparer.Ordinal);
    }
    internal ModuleManager(CapabilityRegistry capabilities, IModuleSymbolResolver symbols, ILogger log,
        string profile, string fingerprint, AdapterSlowDiagnostics? slowDiagnostics = null)
    {
        _capabilities = capabilities; _symbols = symbols; _log = log;
        _profile = profile; _fingerprint = fingerprint; _slowDiagnostics = slowDiagnostics;
    }

    internal void Add(IAdapterModule module, bool onDemand = false)
    {
        Verify();
        ArgumentNullException.ThrowIfNull(module);
        var m = module.Manifest;
        var cadence = module.TickCadence;
        var interval = module.IntervalSeconds;
        if (m is null || string.IsNullOrWhiteSpace(m.Id) || m.Version is null || m.Capabilities is null || m.RequiredCapabilities is null || m.RequiredSymbols is null || m.SupportedScenes is null || m.Capabilities.Count == 0 ||
            m.Capabilities.Any(string.IsNullOrWhiteSpace) || m.Capabilities.Distinct(StringComparer.Ordinal).Count() != m.Capabilities.Count ||
            m.RequiredCapabilities.Any(string.IsNullOrWhiteSpace) || m.RequiredCapabilities.Distinct(StringComparer.Ordinal).Count() != m.RequiredCapabilities.Count ||
            m.RequiredCapabilities.Intersect(m.Capabilities, StringComparer.Ordinal).Any() ||
            m.RequiredSymbols.Any(s => s is null || string.IsNullOrWhiteSpace(s.Id) || string.IsNullOrWhiteSpace(s.Assembly) || s.Namespace is null ||
                string.IsNullOrWhiteSpace(s.Type) || !Enum.IsDefined(typeof(AdapterSymbolKind), s.Kind) ||
                (s.Kind != AdapterSymbolKind.Type && string.IsNullOrWhiteSpace(s.Member)) || (s.ParameterTypes?.Any(string.IsNullOrWhiteSpace) ?? false)) ||
            m.RequiredSymbols.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != m.RequiredSymbols.Count ||
            m.SupportedScenes.Any(string.IsNullOrWhiteSpace) || !Enum.IsDefined(typeof(AdapterVerificationTier), m.VerifiedTier) ||
            !Enum.IsDefined(typeof(AdapterModuleTickCadence), cadence) || !double.IsFinite(interval) || interval <= 0 || string.IsNullOrWhiteSpace(m.Evidence))
            throw new ArgumentException("Module manifest or interval is invalid.", nameof(module));
        if (_entries.Any(x => x.Manifest.Id == m.Id) || m.Capabilities.Any(_owners.ContainsKey))
            throw new InvalidOperationException("A module ID or capability already has an owner.");
        if (m.Capabilities.Any(id => _capabilities.GetCapability(id).Available))
            throw new InvalidOperationException("Cannot replace a capability owned by a different provider.");
        var frozen = m with { Capabilities = Array.AsReadOnly(m.Capabilities.ToArray()), RequiredCapabilities = Array.AsReadOnly(m.RequiredCapabilities.ToArray()),
            SupportedScenes = Array.AsReadOnly(m.SupportedScenes.ToArray()), RequiredSymbols = Array.AsReadOnly(m.RequiredSymbols.Select(s => s with {
                ParameterTypes = s.ParameterTypes is null ? null : Array.AsReadOnly(s.ParameterTypes.ToArray()) }).ToArray()) };
        var entry = new Entry { Module = module, Manifest = frozen, Tier = m.VerifiedTier, OnDemand = onDemand,
            TickCadence = cadence, IntervalSeconds = interval };
        RejectCycles(_entries.Select(x => x.Manifest).Append(frozen).ToArray());
        _entries.Add(entry);
        foreach (var id in frozen.Capabilities) _owners.Add(id, entry);
        _demandPlanDirty = true;
        entry.State = onDemand ? AdapterModuleState.Dormant : AdapterModuleState.Waiting;
        SetUnavailable(entry, onDemand ? NoDemandReason(entry) : $"Module '{m.Id}' is waiting for probe.");
    }

    /// <summary>Saves intent for the next tick. Null selects the explicit full diagnostic profile.</summary>
    internal void SetCapabilityDemand(IReadOnlyCollection<string>? capabilityIds)
    {
        Verify();
        if (_capabilityDemand is null ? capabilityIds is null : capabilityIds is not null && _capabilityDemand.SetEquals(capabilityIds))
            return;
        _capabilityDemand = capabilityIds?.ToHashSet(StringComparer.Ordinal);
        _demandPlanDirty = true;
    }

    // A native operation may still require a service after its submitting
    // plugin has gone away. This demand ends with the actual retained owner.
    internal void SetRetainedCapability(string capability, bool retained)
    {
        Verify();
        if (retained ? _retainedCapabilities.Add(capability) : _retainedCapabilities.Remove(capability))
            _demandPlanDirty = true;
    }

    private void ReconcileDemand(string? scene)
    {
        _demandPlanDirty = false;
        _observedScene = scene;
        _plannedCapabilities.Clear();
        var wanted = new HashSet<Entry>();
        void IncludeCapability(string capability)
        {
            if (_owners.TryGetValue(capability, out var owner) && SupportsScene(owner, scene))
            {
                _plannedCapabilities.Add(capability);
                Include(owner);
            }
        }
        void Include(Entry entry)
        {
            if (!SupportsScene(entry, scene) || !wanted.Add(entry)) return;
            foreach (var capability in entry.Manifest.RequiredCapabilities)
                IncludeCapability(capability);
        }
        foreach (var entry in _entries)
            if (!entry.OnDemand || _capabilityDemand is null) Include(entry);
        if (_capabilityDemand is not null)
            foreach (var capability in _capabilityDemand)
                if (capability is not null) IncludeCapability(capability);
        foreach (var capability in _retainedCapabilities)
            IncludeCapability(capability);

        // Stop dependents before providers. Stop also handles dependency order
        // when modules were registered in a different order.
        foreach (var entry in _entries.AsEnumerable().Reverse())
        {
            if (!entry.OnDemand || entry.State == AdapterModuleState.Unsupported) continue;
            if (wanted.Contains(entry))
            {
                if (entry.State == AdapterModuleState.Dormant)
                {
                    ResetRetry(entry);
                    SetUnavailable(entry, $"Module '{entry.Manifest.Id}' is waiting for probe.");
                }
                continue;
            }
            var reason = SupportsScene(entry, scene) ? NoDemandReason(entry)
                : $"Module '{entry.Manifest.Id}' requires a supported observed scene.";
            if (entry.State != AdapterModuleState.Dormant) Stop(entry, reason);
            if (!entry.ReleaseFailed)
            {
                entry.State = AdapterModuleState.Dormant;
                entry.Attempts = 0;
                entry.Failures = 0;
                if (entry.Failure != reason) SetUnavailable(entry, reason);
            }
        }
    }

    /// <summary>Demand for this capability, independent of siblings in the same module.</summary>
    internal bool HasPlannedCapability(string capability)
        => _capabilityDemand is null || _plannedCapabilities.Contains(capability);

    // An empty declaration leaves scene eligibility to the module's own probe.
    private static bool SupportsScene(Entry entry, string? scene) => !entry.OnDemand
        || entry.Manifest.SupportedScenes.Count == 0
        || entry.Manifest.SupportedScenes.Contains("*", StringComparer.Ordinal)
        || scene is not null && entry.Manifest.SupportedScenes.Contains(scene, StringComparer.Ordinal);

    private static string NoDemandReason(Entry entry)
        => $"Module '{entry.Manifest.Id}' is dormant until a consumer requests a capability.";

    internal void Tick(double elapsedSeconds, string? observedScene = null, long hostTickSequence = 0)
    {
        Verify();
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < _now) throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        _now = elapsedSeconds;
        if (_demandPlanDirty || _observedScene != observedScene) ReconcileDemand(observedScene);
        int budget = 4;
        foreach (var entry in _entries)
        {
            if (entry.State == AdapterModuleState.Dormant) continue;
            var missing = entry.Manifest.RequiredCapabilities.FirstOrDefault(x => !_capabilities.GetCapability(x).Available);
            if (missing is not null)
            {
                if (entry.State == AdapterModuleState.Active) Stop(entry, $"Required capability '{missing}' is unavailable.");
                if (entry.State is not (AdapterModuleState.Unsupported or AdapterModuleState.Suspended))
                    entry.Failure = $"Required capability '{missing}' is unavailable.";
                continue;
            }
            if (entry.State == AdapterModuleState.Active && entry.DependencyGenerations.Any(x => Generation(x.Key) != x.Value))
            { Stop(entry, "Required capability provider was replaced."); entry.Next = _now; }
            if (entry.State is AdapterModuleState.Unsupported or AdapterModuleState.Suspended || _now < entry.Next) continue;
            if (entry.State == AdapterModuleState.Active)
            {
                try
                {
                    var health = entry.Module.Tick();
                    if (!health.Ready) Fail(entry, health);
                    else { entry.Failures = 0; entry.Next = NextMaintenance(entry); }
                }
                catch (Exception ex) { Fail(entry, FromException(ex)); }
            }
            else if (budget-- > 0) Activate(entry, hostTickSequence);
        }
    }

    private double NextMaintenance(Entry entry) => entry.TickCadence == AdapterModuleTickCadence.EveryHostTick
        ? _now : _now + entry.IntervalSeconds;

    internal void ReportFault(IAdapterModule module, Exception error)
    {
        Verify();
        var entry = _entries.Find(candidate => ReferenceEquals(candidate.Module, module));
        if (entry is { State: AdapterModuleState.Active }) Fail(entry, FromException(error));
    }

    private void Activate(Entry entry, long hostTickSequence)
    {
        var measurement = _slowDiagnostics?.Start() ?? default;
        var attempt = ++entry.Attempts;
        try
        {
            entry.Symbols = entry.Manifest.RequiredSymbols.Select(_symbols.Probe).ToArray();
            var bad = entry.Symbols.FirstOrDefault(x => !x.Available);
            if (bad is not null) { Fail(entry, ModuleProbe.Unsupported($"Required symbol '{bad.Id}': {bad.Failure}")); return; }
            entry.Tier = Max(entry.Tier, AdapterVerificationTier.SymbolVerified);
            entry.NeedsRelease = true;
            var probe = entry.Module.Probe();
            if (!probe.Ready) { Fail(entry, probe); return; }
            var registration = new ModuleRegistration(_capabilities, entry.Manifest);
            entry.Module.Register(registration); registration.Complete();
            entry.State = AdapterModuleState.Active; entry.Failure = null;
            entry.DependencyGenerations = entry.Manifest.RequiredCapabilities.ToDictionary(x => x, Generation, StringComparer.Ordinal);
            entry.Tier = Max(entry.Tier, AdapterVerificationTier.RuntimeProbed);
            entry.Attempts = 0; entry.Next = NextMaintenance(entry);
            _log.TryLog(LogLevel.Info, $"Adapter module '{entry.Manifest.Id}' active; tier={entry.Tier}.");
        }
        catch (Exception ex) { Fail(entry, FromException(ex)); }
        finally { _slowDiagnostics?.ObserveActivation(measurement, hostTickSequence, entry.Manifest.Id, attempt, entry.State); }
    }

    private void Fail(Entry entry, ModuleProbe failure)
    {
        var reason = string.IsNullOrWhiteSpace(failure.Reason) ? "Module probe failed." : failure.Reason!;
        var prior = entry.Failure;
        entry.Failures++;
        Stop(entry, reason);
        if (entry.ReleaseFailed) return;
        entry.State = failure.Permanent ? AdapterModuleState.Unsupported :
            entry.Failures >= MaximumAutomaticAttempts ? AdapterModuleState.Suspended : AdapterModuleState.Waiting;
        entry.Next = _now + Math.Min(30, .5 * Math.Pow(2, Math.Min(entry.Failures, 6)));
        if (prior != reason || entry.State is AdapterModuleState.Unsupported or AdapterModuleState.Suspended)
            _log.TryLog(LogLevel.Warning, $"Adapter module '{entry.Manifest.Id}' {entry.State}: {reason}");
    }

    private void Stop(Entry entry, string reason)
    {
        if (entry.State == AdapterModuleState.Disposed || entry.ReleaseFailed) return;
        var releaseOrder = new List<Entry>();
        var withdrawals = new List<(string Id, string Reason)>();
        void Collect(Entry affected, string failure)
        {
            affected.State = AdapterModuleState.Waiting;
            affected.Failure = failure;
            foreach (var capability in affected.Manifest.Capabilities) withdrawals.Add((capability, failure));
            foreach (var dependent in _entries)
            {
                if (dependent.State == AdapterModuleState.Active && dependent.Manifest.RequiredCapabilities
                    .Intersect(affected.Manifest.Capabilities, StringComparer.Ordinal).Any())
                    Collect(dependent, $"Required module '{affected.Manifest.Id}' is unavailable.");
            }
            releaseOrder.Add(affected);
        }
        Collect(entry, reason);
        // Commit the whole withdrawal before scope callbacks or native cleanup can reenter.
        _capabilities.SetUnavailableBatch(withdrawals);
        foreach (var affected in releaseOrder) Release(affected);
    }

    private void Release(Entry entry)
    {
        if (!entry.NeedsRelease) return;
        var retainedDependent = _entries.Find(other => other.ReleaseFailed && other.Manifest.RequiredCapabilities
            .Intersect(entry.Manifest.Capabilities, StringComparer.Ordinal).Any());
        if (retainedDependent is not null)
        {
            RetainCleanup(entry, $"Cleanup of '{retainedDependent.Manifest.Id}' still depends on this provider.");
            return;
        }
        try
        {
            entry.Module.Release();
            entry.NeedsRelease = false;
        }
        catch (Exception ex)
        {
            RetainCleanup(entry, ex.Message, ex);
        }
    }

    private void RetainCleanup(Entry entry, string reason, Exception? error = null)
    {
        entry.ReleaseFailed = true;
        entry.State = AdapterModuleState.Unsupported;
        // Unconfirmed ownership is kept even if this manager is disposed.
        lock (RetainedCleanupOwners) RetainedCleanupOwners.Add(entry.Module);
        var detail = $"Module '{entry.Manifest.Id}' cleanup is unconfirmed. Process restart is required. {reason}";
        SetUnavailable(entry, detail);
        _log.TryLog(LogLevel.Warning, detail, error);
    }

    private void SetUnavailable(Entry entry, string reason)
    {
        entry.Failure = reason;
        _capabilities.SetUnavailableBatch(entry.Manifest.Capabilities.Select(id => (id, reason)).ToArray());
    }

    /// <summary>Scene changes begin a fresh bounded retry epoch for temporary failures, never unsupported signatures.</summary>
    internal void NotifySceneChanged()
    {
        Verify();
        foreach (var entry in _entries.Where(x => x.State is AdapterModuleState.Waiting or AdapterModuleState.Suspended))
            ResetRetry(entry);
    }

    private void ResetRetry(Entry entry)
    {
        entry.Attempts = 0;
        entry.Failures = 0;
        entry.Next = _now;
        entry.State = AdapterModuleState.Waiting;
    }

    internal bool RequestProbe(string moduleId)
    {
        Verify(); var entry = _entries.SingleOrDefault(x => x.Manifest.Id == moduleId);
        if (entry is null || entry.State is AdapterModuleState.Unsupported or AdapterModuleState.Active or AdapterModuleState.Dormant) return false;
        ResetRetry(entry);
        return true;
    }
    public IReadOnlyList<AdapterModuleStatus> GetModules()
    {
        Verify(false);
        return Array.AsReadOnly(_entries.Select(e => new AdapterModuleStatus(e.Manifest, _profile, _fingerprint,
            e.State, e.Tier, e.Attempts, e.State == AdapterModuleState.Waiting ? e.Next : null,
            e.Failure, Array.AsReadOnly(e.Symbols.ToArray()), e.Failure is null ? null : ErrorCode.CapabilityUnavailable, e.Failures)).ToArray());
    }
    internal string? UnconfirmedCleanup => _entries.Find(entry => entry.ReleaseFailed)?.Failure;

    public void Dispose()
    {
        Verify(false); if (_disposed) return;
        foreach (var entry in _entries.AsEnumerable().Reverse())
        {
            Stop(entry, "Adapter disposed.");
            if (!entry.ReleaseFailed) entry.State = AdapterModuleState.Disposed;
        }
        _disposed = true;
    }
    private void Verify(bool active = true)
    {
        if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("Adapter modules require the main thread.");
        if (active && _disposed) throw new ObjectDisposedException(nameof(ModuleManager));
    }
    private static AdapterVerificationTier Max(AdapterVerificationTier a, AdapterVerificationTier b) => a > b ? a : b;
    private long Generation(string id) => _capabilities.Describe(id).Value?.Generation ?? -1;
    private static void RejectCycles(AdapterModuleManifest[] manifests)
    {
        var owners = manifests.SelectMany(m => m.Capabilities.Select(c => (Capability: c, Module: m))).ToDictionary(x => x.Capability, x => x.Module, StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal); var visited = new HashSet<string>(StringComparer.Ordinal);
        void Visit(AdapterModuleManifest module)
        {
            if (!visiting.Add(module.Id)) throw new InvalidOperationException($"Adapter module dependency cycle includes '{module.Id}'.");
            if (visited.Add(module.Id))
                foreach (var required in module.RequiredCapabilities)
                    if (owners.TryGetValue(required, out var owner)) Visit(owner);
            visiting.Remove(module.Id);
        }
        foreach (var manifest in manifests) Visit(manifest);
    }
    private static ModuleProbe FromException(Exception ex) => ex is MissingMemberException or AmbiguousMatchException or UnsupportedSignatureException or InvalidDataException
        ? ModuleProbe.Unsupported(ex.Message) : ModuleProbe.Waiting(ex.Message);
}
