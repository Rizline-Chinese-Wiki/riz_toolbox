using System.Diagnostics;
using RizToolbox.Core.Mutation;
using RizToolbox.Core.Targets;
using RizToolbox.Runtime;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.GameAppearance;

internal sealed record GameAppearanceRun(string RunId, string ChartId, string Scene, long SceneGeneration);

/// <summary>Private discovery seam. Retain transfers roots only when registration succeeds.</summary>
internal sealed record GameAppearanceCandidate(string Identity, string CanonicalBase, string PhysicalIdentity,
    GameAppearanceRun Run, TargetMetadata Metadata, GameAppearanceProperty Property, IReadOnlyList<string> Aliases,
    Func<ResourceColor> Read, Action<ResourceColor> Write, Func<bool> IsAlive, Func<bool> IsEligible,
    Func<IDisposable> Retain);

internal interface IGameAppearanceBackend
{
    IReadOnlyList<GameAppearanceProperty> Properties { get; }
    string? UnsupportedReason(string role) => null;
    IEnumerable<GameAppearanceCandidate> Discover(GameAppearanceRun run, int maximumScanned);
}

/// <summary>Owns only its game appearance bindings; the target registry and mutation coordinator remain host-owned.</summary>
internal sealed class GameAppearanceService : IDisposable
{
    internal const int MaximumTargets = 512, MaximumScanned = 4096;
    private readonly IMainThreadDispatcher _main;
    private readonly ILogger _log;
    private readonly SemanticTargetRegistry _targets;
    private readonly MutationCoordinator _mutations;
    private readonly IGameAppearanceBackend _backend;
    private readonly Func<GameLifecycleSnapshot?> _lifecycle;
    private readonly Dictionary<string, Binding> _bindings = new(StringComparer.Ordinal);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private GameAppearanceRun? _run;
    private double _nextDiscovery;
    private bool _disposed;
    private sealed record Binding(GameAppearanceCandidate Source, TargetHandle<ResourceColor> Target,
        MutationChannel<ResourceColor> Channel);

    internal GameAppearanceService(NativeRuntime api, IMainThreadDispatcher main, ILogger log,
        SemanticTargetRegistry targets, MutationCoordinator mutations, Func<GameLifecycleSnapshot?> lifecycle,
        Func<long, long, string, long, bool> matchesRun)
        : this(main, log, targets, mutations, new NativeGameAppearanceBackend(api, main, log, matchesRun), lifecycle) { }

    internal GameAppearanceService(IMainThreadDispatcher main, ILogger log, SemanticTargetRegistry targets,
        MutationCoordinator mutations, IGameAppearanceBackend backend, Func<GameLifecycleSnapshot?>? lifecycle = null)
    {
        _main = main; _log = log; _targets = targets; _mutations = mutations; _backend = backend;
        _lifecycle = lifecycle ?? (() => null); _main.VerifyAccess(); _targets.Invalidated += Invalidated;
    }

    internal IGameAppearanceService ForPlugin(IPluginContext context) => new View(this, context);

    internal int Refresh(bool forceDiscovery = false)
    {
        _main.VerifyAccess(); if (_disposed) return 0;
        var snapshot = _lifecycle();
        var current = snapshot is { RunsAvailable: true, RunId: not null, ChartId: not null,
            Phase: RunLifecyclePhase.Playing or RunLifecyclePhase.Paused }
            && !string.IsNullOrWhiteSpace(snapshot.RunId) && !string.IsNullOrWhiteSpace(snapshot.ChartId)
            ? new GameAppearanceRun(snapshot.RunId, snapshot.ChartId, snapshot.Scene, snapshot.SceneGeneration) : null;
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
        if (_run is not null && (forceDiscovery || _bindings.Count == 0 || _clock.Elapsed.TotalSeconds >= _nextDiscovery))
        {
            Discover(_run); _nextDiscovery = _clock.Elapsed.TotalSeconds + .25;
        }
        foreach (var binding in _bindings.Values.ToArray())
        {
            var result = binding.Channel.Refresh();
            if (!result.Success) _log.Log(LogLevel.Warning, $"Game appearance coordination failed: role={binding.Source.Property.Role}; run={binding.Source.Run.RunId}; target={binding.Target.Key}; {result.Error}");
        }
        return _bindings.Count;
    }

    private void Discover(GameAppearanceRun run)
    {
        var scanned = 0;
        var pending = new List<Pending>();
        try
        {
            foreach (var candidate in _backend.Discover(run, MaximumScanned))
            {
                if (++scanned > MaximumScanned) throw new InvalidOperationException("Complete appearance discovery exceeded its candidate bound.");
                if (candidate.Run != run || candidate.Metadata.Template || !candidate.Metadata.Active
                    || candidate.Metadata.Scene != run.Scene || candidate.Metadata.ContentId != run.ChartId
                    || candidate.Metadata.Role != candidate.Property.Role || candidate.Property.Property != "color"
                    || _backend.UnsupportedReason(candidate.Property.Role) is not null
                    || !Safe(candidate.IsAlive) || !Safe(candidate.IsEligible)) continue;
                pending.Add(new(candidate, candidate.Retain()));
            }
            // Complete-source discovery must finish before any target is published. A truncated or
            // failing native graph cannot accidentally turn an ambiguous role into a unique target.
            var refusedPhysical = new HashSet<string>(StringComparer.Ordinal);
            foreach (var staged in pending)
            {
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
                    _log.Log(LogLevel.Warning, $"Game appearance binding refused: physical property already belongs to a semantic channel; role={candidate.Property.Role}; run={run.RunId}.");
                    continue;
                }
                if (_bindings.Count >= MaximumTargets) break;
                Bind(candidate with { Retain = staged.Take });
            }
        }
        catch (Exception error) { _log.Log(LogLevel.Warning, $"Game appearance discovery is waiting: run={run.RunId}; {error.Message}"); }
        finally { foreach (var item in pending) item.Dispose(); }
    }
    private sealed class Pending : IDisposable
    {
        internal GameAppearanceCandidate Source { get; }
        private IDisposable? _lifetime;
        internal Pending(GameAppearanceCandidate source, IDisposable lifetime) { Source = source; _lifetime = lifetime; }
        internal IDisposable Take() => Interlocked.Exchange(ref _lifetime, null)
            ?? throw new InvalidOperationException("Appearance discovery lifetime already transferred.");
        public void Dispose() => Interlocked.Exchange(ref _lifetime, null)?.Dispose();
    }

    private void Bind(GameAppearanceCandidate candidate)
    {
        IDisposable? retained = null; TargetHandle<ResourceColor>? target = null;
        try
        {
            retained = candidate.Retain();
            var registration = _targets.Register(candidate.CanonicalBase + "/binding/" + Guid.NewGuid().ToString("N"),
                candidate.Read, () => !_disposed && _run == candidate.Run && CurrentRun(candidate.Run) && Safe(candidate.IsAlive), retained,
                candidate.Metadata, candidate.Aliases);
            if (!registration.Success) throw new AdapterOperationException(registration.Error!);
            target = registration.Value!; retained = null;
            var channel = _mutations.Register(target, "color", candidate.Write, MutationStrategy.PriorityOverride,
                ExternalWritePolicy.AdoptAndReapply, ValidateColor, ColorComparer.Instance);
            if (!channel.Success) throw new AdapterOperationException(channel.Error!);
            _bindings.Add(candidate.Identity, new(candidate, target, channel.Value!));
            _log.Log(LogLevel.Info, $"Experimental game appearance bound: role={candidate.Property.Role}; content={candidate.Run.ChartId}; run={candidate.Run.RunId}; target={target.Key}; generation={target.Generation}.");
        }
        catch (Exception error)
        {
            if (target is not null) _targets.Invalidate(target);
            retained?.Dispose(); _log.Log(LogLevel.Warning, "Game appearance binding refused: " + error.Message);
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
    private bool CurrentRun(GameAppearanceRun run)
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
    internal static string? ValidateColor(ResourceColor value) => value is null
        || new[] { value.R, value.G, value.B, value.A }.Any(x => !float.IsFinite(x) || x < 0 || x > 1)
        ? "RGBA channels must be finite values in [0,1]." : null;

    private OperationError? Guard(IPluginContext context)
    {
        if (!_main.IsMainThread) return Error(ErrorCode.WrongThread, "Game appearance requires the verified main thread.");
        if (_disposed) return Error(ErrorCode.ScopeExpired, "Game appearance service is disposed.");
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
        => new(code, message, GameAppearanceCapabilityIds.Appearance, target, retryable);
    private static OperationError Contextual(OperationError error, string? target = null)
        => error with { CapabilityId = GameAppearanceCapabilityIds.Appearance, TargetId = target ?? error.TargetId };

    private Result<IReadOnlyList<GameAppearanceTarget>> GetTargets(IPluginContext context, TargetQuery? query)
    {
        var guard = Guard(context); if (guard is not null) return Result<IReadOnlyList<GameAppearanceTarget>>.Fail(guard);
        if (query?.Role is string role && _backend.UnsupportedReason(role) is string reason)
            return Result<IReadOnlyList<GameAppearanceTarget>>.Fail(Error(ErrorCode.OperationUnsupported,
                "The requested appearance property is unsupported: " + reason, role));
        var descriptor = _targets.GetTargets().ToDictionary(x => (x.Key, x.Generation));
        var values = _bindings.Values.Where(x => x.Channel.Valid && descriptor.ContainsKey((x.Target.Key, x.Target.Generation)))
            .Select(x => new GameAppearanceTarget(descriptor[(x.Target.Key, x.Target.Generation)], x.Source.Run.RunId,
                x.Source.Run.SceneGeneration, x.Source.Property)).Where(x => Matches(x.Target, query))
            .OrderBy(x => x.Target.Key, StringComparer.Ordinal).ToArray();
        return Result<IReadOnlyList<GameAppearanceTarget>>.Ok(Array.AsReadOnly(values));
    }
    private static bool Matches(TargetDescriptor value, TargetQuery? query) => query is null
        || (query.Key is null || value.Key == query.Key || value.Aliases.Contains(query.Key, StringComparer.Ordinal))
        && (query.Scene is null || value.Metadata.Scene == query.Scene) && (query.Role is null || value.Metadata.Role == query.Role)
        && (query.ContentId is null || value.Metadata.ContentId == query.ContentId) && (query.ResourceId is null || value.Metadata.ResourceId == query.ResourceId)
        && (!query.ActiveOnly || value.Metadata.Active) && (query.IncludeTemplates || !value.Metadata.Template);

    private Result<IMutationLease<ResourceColor>> Override(IPluginContext context, GameAppearanceColorOverrideRequest request)
    {
        var guard = Guard(context); if (guard is not null) return Result<IMutationLease<ResourceColor>>.Fail(guard);
        if (request is null || request.Query is null || string.IsNullOrWhiteSpace(request.RunId) || ValidateColor(request.Color) is { })
            return Result<IMutationLease<ResourceColor>>.Fail(Error(ErrorCode.InvalidArgument, "A query, expected run ID and finite RGBA values are required.", request?.Query?.Key));
        if (request.Query.Role is string role && _backend.UnsupportedReason(role) is string reason)
            return Result<IMutationLease<ResourceColor>>.Fail(Error(ErrorCode.OperationUnsupported,
                "The requested appearance property is unsupported: " + reason, role));
        // Refresh the observed run before accepting a cached SDK view. This never manufactures new targets.
        if (_run is null || !CurrentRun(_run)
            || request.RunId != _run.RunId || request.SceneGeneration is { } scene && scene != _run.SceneGeneration)
            return Result<IMutationLease<ResourceColor>>.Fail(Error(ErrorCode.TargetExpired, "The expected game appearance run or scene generation is no longer current.", request.Query.Key));
        var available = GetTargets(context, request.Query);
        if (!available.Success) return Result<IMutationLease<ResourceColor>>.Fail(available.Error!);
        if (available.Value!.Count != 1)
            return Result<IMutationLease<ResourceColor>>.Fail(Error(available.Value.Count == 0 ? ErrorCode.TargetMissing : ErrorCode.AmbiguousTarget,
                available.Value.Count == 0 ? "No eligible concrete game appearance target is present." : "Multiple concrete game appearance targets match; refine the query.",
                request.Query.Key, available.Value.Count == 0));
        var selected = available.Value[0];
        var handle = new TargetHandle<ResourceColor>(selected.Target.Key, selected.Target.Generation);
        var result = _mutations.ForPlugin(context).Override(new MutationKey<ResourceColor>(handle, selected.Property.Property), request.Color, request.Priority);
        return result.Success ? Result<IMutationLease<ResourceColor>>.Ok(result.Value!)
            : Result<IMutationLease<ResourceColor>>.Fail(Contextual(result.Error!, handle.Key));
    }

    public void Dispose()
    {
        _main.VerifyAccess(); if (_disposed) return;
        // Keep liveness true during conditional restoration, then revoke the view.
        foreach (var binding in _bindings.Values.ToArray()) Remove(binding, true);
        _disposed = true; _targets.Invalidated -= Invalidated; _bindings.Clear();
    }

    private sealed class View : IGameAppearanceService
    {
        private readonly GameAppearanceService _owner; private readonly IPluginContext _context;
        internal View(GameAppearanceService owner, IPluginContext context) { _owner = owner; _context = context; }
        public Result<IReadOnlyList<GameAppearanceProperty>> GetSupportedProperties()
        {
            var guard = _owner.Guard(_context);
            return guard is not null ? Result<IReadOnlyList<GameAppearanceProperty>>.Fail(guard)
                : Result<IReadOnlyList<GameAppearanceProperty>>.Ok(_owner._backend.Properties);
        }
        public Result<IReadOnlyList<GameAppearanceTarget>> GetTargets(TargetQuery? query = null) => _owner.GetTargets(_context, query);
        public Result<IMutationLease<ResourceColor>> OverrideColor(GameAppearanceColorOverrideRequest request) => _owner.Override(_context, request);
    }
    private sealed class ColorComparer : IEqualityComparer<ResourceColor>
    {
        internal static readonly ColorComparer Instance = new();
        public bool Equals(ResourceColor? x, ResourceColor? y) => x is not null && y is not null
            && Math.Abs(x.R - y.R) < .0001f && Math.Abs(x.G - y.G) < .0001f
            && Math.Abs(x.B - y.B) < .0001f && Math.Abs(x.A - y.A) < .0001f;
        public int GetHashCode(ResourceColor value) => 0;
    }
}
