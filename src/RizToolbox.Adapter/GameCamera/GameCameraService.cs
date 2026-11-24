using System.Diagnostics;
using RizToolbox.Core.Diagnostics;
using RizToolbox.Core.Mutation;
using RizToolbox.Core.Targets;
using RizToolbox.Runtime;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.GameCamera;

internal sealed record GameCameraRun(string RunId, string ChartId, string Scene, long SceneGeneration);

/// <summary>Private discovery seam. Retain transfers roots only when registration succeeds.</summary>
internal sealed record GameCameraCandidate(string Identity, string CanonicalBase, string PhysicalIdentity,
    GameCameraRun Run, TargetMetadata Metadata, GameCameraScalarProperty Property, IReadOnlyList<string> Aliases,
    Func<float> Read, Action<float> Write, Func<bool> IsAlive, Func<bool> IsEligible,
    Func<IDisposable> Retain);

internal interface IGameCameraBackend
{
    IReadOnlyList<GameCameraScalarProperty> Properties { get; }
    IEnumerable<GameCameraCandidate> Discover(GameCameraRun run, int maximumScanned);
}

/// <summary>Owns only its game camera bindings; the target registry and mutation coordinator remain host-owned.</summary>
internal sealed class GameCameraService : IDisposable
{
    internal const int MaximumTargets = 512, MaximumScanned = 4096;
    internal const double DiscoveryIntervalSeconds = .25;
    private readonly IMainThreadDispatcher _main;
    private readonly ILogger _log;
    private readonly SemanticTargetRegistry _targets;
    private readonly MutationCoordinator _mutations;
    private readonly IGameCameraBackend _backend;
    private readonly IReadOnlyList<GameCameraScalarProperty> _properties;
    private readonly Func<GameLifecycleSnapshot?> _lifecycle;
    private readonly Dictionary<string, Binding> _bindings = new(StringComparer.Ordinal);
    private readonly Func<double> _monotonicSeconds;
    private GameCameraRun? _run;
    private double _nextDiscovery;
    private bool _refreshing, _stopping, _disposed;
    private sealed record Binding(GameCameraCandidate Source, TargetHandle<float> Target,
        MutationChannel<float> Channel);

    internal GameCameraService(NativeRuntime api, IMainThreadDispatcher main, ILogger log,
        SemanticTargetRegistry targets, MutationCoordinator mutations, Func<GameLifecycleSnapshot?> lifecycle,
        Func<long, long, string, long, bool> matchesRun)
        : this(main, log, targets, mutations, new NativeGameCameraBackend(api, main, log, matchesRun), lifecycle) { }

    internal GameCameraService(IMainThreadDispatcher main, ILogger log, SemanticTargetRegistry targets,
        MutationCoordinator mutations, IGameCameraBackend backend, Func<GameLifecycleSnapshot?>? lifecycle = null,
        Func<double>? monotonicSeconds = null)
    {
        _main = main; _log = log; _targets = targets; _mutations = mutations; _backend = backend; _main.VerifyAccess();
        var properties = backend.Properties.ToArray();
        if (properties.Length == 0 || properties.Any(property => property is null
            || string.IsNullOrWhiteSpace(property.Role) || string.IsNullOrWhiteSpace(property.Property))
            || properties.Select(property => property.Role).Distinct(StringComparer.Ordinal).Count() != properties.Length
            || properties.Select(property => property.Property).Distinct(StringComparer.Ordinal).Count() != properties.Length)
            throw new UnsupportedSignatureException("The camera backend must declare unique concrete scalar roles and properties.");
        // Snapshot the complete descriptor. A candidate cannot invent a new
        // property or silently change its unit/policy after the service binds.
        _properties = Array.AsReadOnly(properties);
        _monotonicSeconds = monotonicSeconds ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        _lifecycle = lifecycle ?? (() => null); _targets.Invalidated += Invalidated;
    }

    internal IGameCameraService ForPlugin(IPluginContext context) => new View(this, context);

    internal int Refresh(bool forceDiscovery = false)
    {
        _main.VerifyAccess();
        if (_disposed || _stopping) return 0;
        if (_refreshing) return _bindings.Count;
        _refreshing = true;
        try { return RefreshCurrentRun(forceDiscovery); }
        finally { _refreshing = false; }
    }

    private int RefreshCurrentRun(bool forceDiscovery)
    {
        var snapshot = _lifecycle();
        var current = snapshot is { RunsAvailable: true, RunId: not null, ChartId: not null,
            Phase: RunLifecyclePhase.Playing or RunLifecyclePhase.Paused }
            && !string.IsNullOrWhiteSpace(snapshot.RunId) && !string.IsNullOrWhiteSpace(snapshot.ChartId)
            ? new GameCameraRun(snapshot.RunId, snapshot.ChartId, snapshot.Scene, snapshot.SceneGeneration) : null;
        if (current != _run)
        {
            // Liveness checks the actual lifecycle too: an old run cannot write into a replacement,
            // even through a generic mutation lease between discovery ticks.
            foreach (var binding in _bindings.Values.ToArray()) Remove(binding, true);
            _run = current; forceDiscovery = true;
        }
        foreach (var binding in _bindings.Values.ToArray())
        {
            if (!Safe(binding.Source.IsAlive)) Remove(binding, false);
            else if (!Safe(binding.Source.IsEligible) || !binding.Channel.Valid) Remove(binding, true);
        }
        if (_run is not null && (forceDiscovery || _monotonicSeconds() >= _nextDiscovery))
        {
            try { Discover(_run); }
            finally { _nextDiscovery = _monotonicSeconds() + DiscoveryIntervalSeconds; }
        }
        foreach (var binding in _bindings.Values.ToArray())
        {
            var result = binding.Channel.Refresh();
            if (!result.Success) _log.TryLog(LogLevel.Warning, $"Game camera coordination failed: role={binding.Source.Property.Role}; run={binding.Source.Run.RunId}; target={binding.Target.Key}; {result.Error}");
        }
        return _bindings.Count;
    }

    private void Discover(GameCameraRun run)
    {
        var scanned = 0;
        var pending = new List<Pending>();
        try
        {
            foreach (var candidate in _backend.Discover(run, MaximumScanned))
            {
                if (_stopping) break;
                if (++scanned > MaximumScanned) throw new InvalidOperationException("Complete camera discovery exceeded its candidate bound.");
                if (candidate.Run != run || candidate.Metadata.Template || !candidate.Metadata.Active
                    || candidate.Metadata.Scene != run.Scene || candidate.Metadata.ContentId != run.ChartId
                    || candidate.Metadata.Role != candidate.Property.Role || !_properties.Contains(candidate.Property)
                    || !Safe(candidate.IsAlive) || !Safe(candidate.IsEligible)) continue;
                pending.Add(new(candidate, candidate.Retain()));
            }
            // Complete-source discovery must finish before any target is published. A truncated or
            // failing native graph cannot accidentally turn an ambiguous role into a unique target.
            var refusedPhysical = new HashSet<string>(StringComparer.Ordinal);
            foreach (var staged in pending)
            {
                if (_stopping) break;
                var candidate = staged.Source;
                if (refusedPhysical.Contains(candidate.PhysicalIdentity)) continue;
                if (_bindings.TryGetValue(candidate.Identity, out var existing))
                {
                    // A physical property cannot move to another semantic role inside an existing generation.
                    if (existing.Source.PhysicalIdentity != candidate.PhysicalIdentity || existing.Source.Property != candidate.Property)
                    { Remove(existing, true); continue; }
                    if (!_targets.SetAliases(existing.Target, candidate.Aliases).Success
                        || !_targets.UpdateMetadata(existing.Target, candidate.Metadata).Success) Remove(existing, true);
                    continue;
                }
                var conflicts = _bindings.Values.Where(x => x.Source.PhysicalIdentity == candidate.PhysicalIdentity).ToArray();
                if (conflicts.Length != 0)
                {
                    foreach (var conflict in conflicts) Remove(conflict, true);
                    refusedPhysical.Add(candidate.PhysicalIdentity);
                    _log.TryLog(LogLevel.Warning, $"Game camera binding refused: physical property already belongs to a semantic channel; role={candidate.Property.Role}; run={run.RunId}.");
                    continue;
                }
                if (_bindings.Count >= MaximumTargets) break;
                Bind(candidate with { Retain = staged.Take });
            }
        }
        catch (Exception error) { _log.TryLog(LogLevel.Warning, $"Game camera discovery is waiting: run={run.RunId}; {error.Message}"); }
        finally { foreach (var item in pending) item.Dispose(); }
    }
    private sealed class Pending : IDisposable
    {
        internal GameCameraCandidate Source { get; }
        private IDisposable? _lifetime;
        internal Pending(GameCameraCandidate source, IDisposable lifetime) { Source = source; _lifetime = lifetime; }
        internal IDisposable Take() => Interlocked.Exchange(ref _lifetime, null)
            ?? throw new InvalidOperationException("Camera discovery lifetime already transferred.");
        public void Dispose() => Interlocked.Exchange(ref _lifetime, null)?.Dispose();
    }

    private void Bind(GameCameraCandidate candidate)
    {
        IDisposable? retained = null; TargetHandle<float>? target = null;
        try
        {
            retained = candidate.Retain();
            var registration = _targets.Register(candidate.CanonicalBase + "/binding/" + Guid.NewGuid().ToString("N"),
                candidate.Read, () => !_disposed && _run == candidate.Run && CurrentRun(candidate.Run) && Safe(candidate.IsAlive), retained,
                candidate.Metadata, candidate.Aliases);
            if (!registration.Success) throw new AdapterOperationException(registration.Error!);
            target = registration.Value!; retained = null;
            var channel = _mutations.Register(target, candidate.Property.Property, candidate.Write, MutationStrategy.PriorityOverride,
                candidate.Property.ExternalWrites, ValidateScalar, EqualityComparer<float>.Default);
            if (!channel.Success) throw new AdapterOperationException(channel.Error!);
            _bindings.Add(candidate.Identity, new(candidate, target, channel.Value!));
            _log.TryLog(LogLevel.Info, $"Experimental game camera bound: role={candidate.Property.Role}; content={candidate.Run.ChartId}; run={candidate.Run.RunId}; target={target.Key}; generation={target.Generation}.");
        }
        catch (Exception error)
        {
            if (target is not null) _targets.Invalidate(target);
            retained?.Dispose(); _log.TryLog(LogLevel.Warning, "Game camera binding refused: " + error.Message);
        }
    }

    private void Remove(Binding binding, bool restore)
    {
        try { if (restore) binding.Channel.Dispose(); }
        finally { _targets.Invalidate(binding.Target); _bindings.Remove(binding.Source.Identity); }
    }
    private void Invalidated(TargetIdentity identity)
    {
        foreach (var binding in _bindings.Values.Where(x => x.Target.Key == identity.Key && x.Target.Generation == identity.Generation).ToArray())
            _bindings.Remove(binding.Source.Identity);
    }
    private static bool Safe(Func<bool> read) { try { return read(); } catch { return false; } }
    private bool CurrentRun(GameCameraRun run)
    {
        try
        {
            var snapshot = _lifecycle();
            return snapshot is { RunsAvailable: true, Phase: RunLifecyclePhase.Playing or RunLifecyclePhase.Paused }
                && snapshot.RunId == run.RunId && snapshot.ChartId == run.ChartId && snapshot.Scene == run.Scene
                && snapshot.SceneGeneration == run.SceneGeneration;
        }
        catch { return false; }
    }
    internal static string? ValidateScalar(float value) => float.IsFinite(value) ? null : "Camera X must be finite; no game numeric range is established.";

    private OperationError? Guard(IPluginContext context)
    {
        if (!_main.IsMainThread) return Error(ErrorCode.WrongThread, "Game camera requires the verified main thread.");
        if (_stopping || _disposed) return Error(ErrorCode.ScopeExpired, "Game camera service is disposed.");
        try
        {
            context.MainThread.VerifyAccess();
            if (context is IScopedPluginContext scoped && scoped.Lifetime.IsCancellationRequested)
                return Error(ErrorCode.ScopeExpired, "Plugin scope has ended.");
            return null;
        }
        catch (ObjectDisposedException ex) { return Error(ErrorCode.ScopeExpired, ex.Message); }
        catch (InvalidOperationException ex) { return Error(ErrorCode.WrongThread, ex.Message); }
    }
    private static OperationError Error(ErrorCode code, string message, string? target = null, bool retryable = false)
        => new(code, message, GameCameraCapabilityIds.Camera, target, retryable);
    private static OperationError Contextual(OperationError error, string? target = null)
        => error with { CapabilityId = GameCameraCapabilityIds.Camera, TargetId = target ?? error.TargetId };

    private Result<IReadOnlyList<GameCameraTarget>> GetTargets(IPluginContext context, TargetQuery? query)
    {
        var guard = Guard(context); if (guard is not null) return Result<IReadOnlyList<GameCameraTarget>>.Fail(guard);
        var descriptor = _targets.GetTargets().ToDictionary(x => (x.Key, x.Generation));
        var values = _bindings.Values.Where(x => x.Channel.Valid && descriptor.ContainsKey((x.Target.Key, x.Target.Generation)))
            .Select(x => new GameCameraTarget(descriptor[(x.Target.Key, x.Target.Generation)], x.Source.Run.RunId,
                x.Source.Run.SceneGeneration, x.Source.Property)).Where(x => Matches(x.Target, query))
            .OrderBy(x => x.Target.Key, StringComparer.Ordinal).ToArray();
        return Result<IReadOnlyList<GameCameraTarget>>.Ok(Array.AsReadOnly(values));
    }
    private static bool Matches(TargetDescriptor value, TargetQuery? query) => query is null
        || (query.Key is null || value.Key == query.Key || value.Aliases.Contains(query.Key, StringComparer.Ordinal))
        && (query.Scene is null || value.Metadata.Scene == query.Scene) && (query.Role is null || value.Metadata.Role == query.Role)
        && (query.ContentId is null || value.Metadata.ContentId == query.ContentId) && (query.ResourceId is null || value.Metadata.ResourceId == query.ResourceId)
        && (!query.ActiveOnly || value.Metadata.Active) && (query.IncludeTemplates || !value.Metadata.Template);

    private Result<IMutationLease<float>> Override(IPluginContext context, GameCameraScalarOverrideRequest request)
    {
        var guard = Guard(context); if (guard is not null) return Result<IMutationLease<float>>.Fail(guard);
        if (request is null || request.Query is null || string.IsNullOrWhiteSpace(request.RunId) || ValidateScalar(request.Value) is { })
            return Result<IMutationLease<float>>.Fail(Error(ErrorCode.InvalidArgument, "A query, expected run ID and a finite scalar value are required.", request?.Query?.Key));
        // Refresh the observed run before accepting a cached SDK view. This never manufactures new targets.
        if (_run is null || !CurrentRun(_run)
            || request.RunId != _run.RunId || request.SceneGeneration is { } scene && scene != _run.SceneGeneration)
            return Result<IMutationLease<float>>.Fail(Error(ErrorCode.TargetExpired, "The expected game camera run or scene generation is no longer current.", request.Query.Key));
        var available = GetTargets(context, request.Query);
        if (!available.Success) return Result<IMutationLease<float>>.Fail(available.Error!);
        if (available.Value!.Count != 1)
            return Result<IMutationLease<float>>.Fail(Error(available.Value.Count == 0 ? ErrorCode.TargetMissing : ErrorCode.AmbiguousTarget,
                available.Value.Count == 0 ? "No eligible concrete game camera target is present." : "Multiple concrete game camera targets match; refine the query.",
                request.Query.Key, available.Value.Count == 0));
        var selected = available.Value[0];
        var handle = new TargetHandle<float>(selected.Target.Key, selected.Target.Generation);
        var result = _mutations.ForPlugin(context).Override(new MutationKey<float>(handle, selected.Property.Property), request.Value, request.Priority);
        return result.Success ? Result<IMutationLease<float>>.Ok(result.Value!)
            : Result<IMutationLease<float>>.Fail(Contextual(result.Error!, handle.Key));
    }

    public void Dispose()
    {
        _main.VerifyAccess(); if (_stopping || _disposed) return;
        // Revoke user operations while the bindings can still restore their baselines.
        _stopping = true;
        Exception? cleanupFailure = null;
        foreach (var binding in _bindings.Values.ToArray())
        {
            try { Remove(binding, true); }
            catch (Exception error) { cleanupFailure ??= error; }
        }
        _disposed = true; _targets.Invalidated -= Invalidated;
        if (cleanupFailure is not null) throw new AggregateException("Game camera cleanup failed.", cleanupFailure);
    }

    private sealed class View : IGameCameraService
    {
        private readonly GameCameraService _owner; private readonly IPluginContext _context;
        internal View(GameCameraService owner, IPluginContext context) { _owner = owner; _context = context; }
        public Result<IReadOnlyList<GameCameraScalarProperty>> GetSupportedProperties()
        {
            var guard = _owner.Guard(_context);
            return guard is not null ? Result<IReadOnlyList<GameCameraScalarProperty>>.Fail(guard)
                : Result<IReadOnlyList<GameCameraScalarProperty>>.Ok(_owner._properties);
        }
        public Result<IReadOnlyList<GameCameraTarget>> GetTargets(TargetQuery? query = null) => _owner.GetTargets(_context, query);
        public Result<IMutationLease<float>> OverrideScalar(GameCameraScalarOverrideRequest request) => _owner.Override(_context, request);
    }
}
