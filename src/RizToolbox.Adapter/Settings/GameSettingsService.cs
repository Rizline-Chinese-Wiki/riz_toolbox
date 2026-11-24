using System.Globalization;
using RizToolbox.Core.Diagnostics;
using RizToolbox.Core.Mutation;
using RizToolbox.Core.Targets;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Settings;

internal sealed record GameSettingRange(float Minimum, float Maximum, bool WholeNumbers = false);
internal sealed record GameSettingBinding(string Id, string Source, Func<float>? Read, Action<float>? Write,
    string? ReadUnsupportedReason = null, string? WriteUnsupportedReason = null)
{
    internal string Unit { get; init; } = "game-setting float; setter units";
}
internal interface IGameSettingsBackend : IDisposable
{
    bool Ready { get; }
    IReadOnlyList<GameSettingBinding> Bindings { get; }
    Result<IReadOnlyDictionary<string, GameSettingRange>> GetWriteRanges();
}

/// <summary>Reversible verified float properties. All settings leases use the common priority/baseline coordinator.</summary>
internal sealed partial class GameSettingsService : IDisposable
{
    private readonly IMainThreadDispatcher _main;
    private readonly IGameSettingsBackend _backend;
    private readonly ILogger _log;
    private readonly SemanticTargetRegistry _targets;
    private readonly MutationCoordinator _mutations;
    private readonly Queue<IMutationLease<float>> _pendingReleases = new();
    private readonly Dictionary<string, Bound> _bound = new(StringComparer.Ordinal);
    private Dictionary<string, GameSettingRange> _ranges = new(StringComparer.Ordinal);
    private bool _disposeRequested, _releasingBackend, _disposed, _refreshingRanges;
    private int _backendUses, _mutationDepth;
    private OperationError? _rangeFailure;
    private sealed record Bound(GameSettingBinding Source, TargetHandle<float> Target, MutationChannel<float> Channel);
    internal int ReadableCount => _backend.Bindings.Count(x => x.Read is not null);
    internal bool Ready => !_disposeRequested && _backend.Ready;
    internal bool OverrideReady => Ready && _bound.Any(pair => pair.Value.Channel.Valid && _ranges.ContainsKey(pair.Key));
    internal OperationError? OverrideFailure => _rangeFailure;

    internal GameSettingsService(IMainThreadDispatcher main, ILogger log, IGameSettingsBackend backend)
    {
        _main = main; _backend = backend; _log = log; _main.VerifyAccess(); _targets = new(main, log); _mutations = new(_targets, main, log);
    }

    internal Result Refresh()
    {
        var invalid = Verify(); if (invalid is not null) return Result.Fail(invalid);
        // Callback-triggered refresh cannot publish a second subject while an earlier revision is being delivered.
        if (_refreshingSettings || _settingsDeliveryDepth != 0) return Result.Fail(new(ErrorCode.Conflict, "A settings observation is already in progress."));
        _settingsObservationUsable = false;
        using var backend = UseBackend();
        if (!Ready) return Result.Fail(new(ErrorCode.NotReady, "Game settings are not initialized."));
        _refreshingSettings = true;
        try
        {
            foreach (var source in _backend.Bindings.Where(x => x.Read is not null && x.Write is not null))
            {
                if (Verify() is { } retired) return Result.Fail(retired);
                if (_bound.ContainsKey(source.Id)) continue;
                var target = _targets.Register($"game.settings/{source.Id}/binding/{Guid.NewGuid():N}",
                    () => ReadFinite(source), () => !_disposed && _backend.Ready, metadata: new(Role: "game-setting", ResourceId: source.Id));
                if (!target.Success) return Result.Fail(target.Error!);
                // The verified setters preserve a Single exactly, including values adjacent to another owner's write.
                var channel = _mutations.Register(target.Value!, "value", source.Write!, MutationStrategy.PriorityOverride,
                    ExternalWritePolicy.AdoptAndReapply, value => !float.IsFinite(value) ? "Setting must be finite." :
                        _ranges.TryGetValue(source.Id, out var range) ? Validate(value, range) : null);
                if (!channel.Success) { _targets.Invalidate(target.Value!); return Result.Fail(channel.Error!); }
                _bound.Add(source.Id, new(source, target.Value!, channel.Value!));
            }
            RefreshRanges();
            foreach (var bound in _bound.Values.ToArray())
            {
                if (Verify() is { } retired) return Result.Fail(retired);
                if (!bound.Channel.Valid) continue;
                using var mutation = UseMutation();
                var result = bound.Channel.Refresh();
                if (!result.Success) return Result.Fail(result.Error!);
            }
            return ObserveSettingsChanges();
        }
        catch (Exception ex) { return Result.Fail(new(ErrorCode.CapabilityUnavailable, "Game settings refresh failed: " + ex.Message)); }
        finally { _refreshingSettings = false; }
    }

    internal Result RefreshRanges()
    {
        var invalid = Verify(); if (invalid is not null) return Result.Fail(invalid);
        if (_refreshingRanges) return Result.Fail(new(ErrorCode.Conflict, "A setting slider range probe is already in progress."));
        using var backend = UseBackend();
        _refreshingRanges = true;
        try
        {
            var ranges = _backend.GetWriteRanges();
            if (Verify() is { } retired) return Result.Fail(retired);
            if (!ranges.Success) return FailRanges(ranges.Error!);
            var captured = new Dictionary<string, GameSettingRange>(StringComparer.Ordinal);
            foreach (var pair in ranges.Value!)
            {
                if (!_bound.ContainsKey(pair.Key)) continue;
                if (!float.IsFinite(pair.Value.Minimum) || !float.IsFinite(pair.Value.Maximum) || pair.Value.Minimum > pair.Value.Maximum)
                    return FailRanges(new(ErrorCode.UnsupportedSignature, "The actual settings slider has an invalid float range.", TargetId: pair.Key));
                captured.Add(pair.Key, pair.Value);
            }
            if (Verify() is { } stopped) return Result.Fail(stopped);
            if (captured.Count == 0) return FailRanges(new(ErrorCode.OperationUnsupported, "No verified setting setter and actual slider range are jointly available."));
            _ranges = captured; _rangeFailure = null;
            return Result.Ok();
        }
        catch (Exception ex)
        {
            if (Verify() is { } retired) return Result.Fail(retired);
            return FailRanges(new(ErrorCode.CapabilityUnavailable, "Setting slider range is unavailable: " + ex.Message));
        }
        finally { _refreshingRanges = false; }
    }
    private Result FailRanges(OperationError failure)
    {
        _ranges.Clear(); _rangeFailure = failure;
        return Result.Fail(failure);
    }

    internal IGameSettingsReadService ReadForPlugin(IPluginContext context) => context.Own(new View(this, context));
    internal IGameSettingsOverrideService OverrideForPlugin(IPluginContext context) => context.Own(new View(this, context));

    internal Result<IReadOnlyList<GameSettingSnapshot>> CaptureDataSettings()
    {
        var values = GetAll();
        return values.Success ? Result<IReadOnlyList<GameSettingSnapshot>>.Ok(Array.AsReadOnly(values.Value!.Select(value =>
            new GameSettingSnapshot(value.Descriptor.Id, value.Value.ToString("R", CultureInfo.InvariantCulture), value.Descriptor.Source, value.Descriptor.Unit)).ToArray())) :
            Result<IReadOnlyList<GameSettingSnapshot>>.Fail(values.Error!);
    }

    private Result<IReadOnlyList<GameFloatSettingValue>> GetAll()
    {
        var invalid = Verify(); if (invalid is not null) return Result<IReadOnlyList<GameFloatSettingValue>>.Fail(invalid);
        using var backend = UseBackend();
        if (!Ready) return Result<IReadOnlyList<GameFloatSettingValue>>.Fail(new(ErrorCode.NotReady, "Game settings are not initialized."));
        // Range discovery may fail independently. Reads still succeed, with fresh write availability in their descriptors.
        RefreshRanges();
        if (Verify() is { } retired) return Result<IReadOnlyList<GameFloatSettingValue>>.Fail(retired);
        var values = new List<GameFloatSettingValue>();
        foreach (var binding in _backend.Bindings.Where(x => x.Read is not null).OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            var value = Read(binding.Id); if (!value.Success) return Result<IReadOnlyList<GameFloatSettingValue>>.Fail(value.Error!); values.Add(value.Value!);
        }
        return values.Count > 0 ? Result<IReadOnlyList<GameFloatSettingValue>>.Ok(Array.AsReadOnly(values.ToArray())) :
            Result<IReadOnlyList<GameFloatSettingValue>>.Fail(new(ErrorCode.OperationUnsupported, "No supported game setting getters were found."));
    }
    private Result<GameFloatSettingValue> Read(string id)
    {
        var invalid = Verify(); if (invalid is not null) return Result<GameFloatSettingValue>.Fail(invalid);
        using var backend = UseBackend();
        var binding = _backend.Bindings.FirstOrDefault(x => x.Id == id);
        if (binding is null || binding.Read is null) return Result<GameFloatSettingValue>.Fail(new(ErrorCode.OperationUnsupported,
            binding?.ReadUnsupportedReason ?? "Unknown or unsupported game setting.", TargetId: id));
        if (!Ready) return Result<GameFloatSettingValue>.Fail(new(ErrorCode.NotReady, "Game settings are not initialized.", TargetId: id));
        try
        {
            var descriptor = Descriptor(binding);
            if (Verify() is { } retired) return Result<GameFloatSettingValue>.Fail(retired);
            var value = ReadFinite(binding);
            return Verify() is { } stopped ? Result<GameFloatSettingValue>.Fail(stopped)
                : Result<GameFloatSettingValue>.Ok(new(descriptor, value, DateTimeOffset.UtcNow));
        }
        catch (Exception ex) { return Result<GameFloatSettingValue>.Fail(new(ErrorCode.CapabilityUnavailable, "Cannot read game setting: " + ex.Message, TargetId: id)); }
    }
    private GameFloatSettingDescriptor Descriptor(GameSettingBinding source)
    {
        _ranges.TryGetValue(source.Id, out var range);
        bool expired = _bound.TryGetValue(source.Id, out var binding) && !binding.Channel.Valid;
        return new(source.Id, source.Source, source.Unit, source.Read is not null,
            source.Write is not null && range is not null && Ready && !expired, range?.Minimum, range?.Maximum, range?.WholeNumbers ?? false,
            source.Write is null ? source.WriteUnsupportedReason ?? "Setter signature is unsupported." : expired ? "Setting write ownership was lost; the binding has expired." :
                range is null ? _rangeFailure?.Message ?? "Actual setting slider is not ready." : null);
    }
    private OperationError? Verify()
    {
        try { _main.VerifyAccess(); return _disposeRequested ? new(ErrorCode.ScopeExpired, "Game settings module has been withdrawn.") : null; }
        catch (Exception ex) { return new(ErrorCode.WrongThread, ex.Message); }
    }
    private static float ReadFinite(GameSettingBinding source)
    { float value = source.Read!(); return float.IsFinite(value) ? value : throw new InvalidDataException("Game setting getter returned a non-finite value."); }
    private static string? Validate(float value, GameSettingRange range)
        => !float.IsFinite(value) ? "Setting must be finite." : value < range.Minimum || value > range.Maximum ? $"Setting is outside the verified setter range [{range.Minimum}, {range.Maximum}]." :
            range.WholeNumbers && value != MathF.Round(value) ? "The verified setting accepts whole-number values only." : null;

    public void Dispose()
    {
        _main.VerifyAccess(); if (_disposeRequested) return;
        _disposeRequested = true;
        CloseSettingsChanges();
        if (_backendUses == 0) ReleaseBackend();
    }
    private BackendUse UseBackend()
    {
        ++_backendUses;
        return new(this);
    }
    private readonly struct BackendUse : IDisposable
    {
        private readonly GameSettingsService _owner;
        internal BackendUse(GameSettingsService owner) => _owner = owner;
        public void Dispose()
        {
            if (--_owner._backendUses != 0 || !_owner._disposeRequested || _owner._releasingBackend || _owner._disposed) return;
            try { _owner.ReleaseBackend(); }
            catch (Exception error) { _owner._log.TryLog(LogLevel.Error, "Deferred setting cleanup failed.", error); }
        }
    }
    private void ReleaseBackend()
    {
        // Keep the static property live until the coordinator restores any still-owned baseline.
        _releasingBackend = true;
        try { using var mutation = UseMutation(); _mutations.Dispose(); }
        finally
        {
            _disposed = true;
            try { _targets.Dispose(); }
            finally { _bound.Clear(); _ranges.Clear(); _backend.Dispose(); }
        }
    }

    private MutationUse UseMutation()
    {
        ++_mutationDepth;
        return new(this);
    }
    private readonly struct MutationUse : IDisposable
    {
        private readonly GameSettingsService _owner;
        internal MutationUse(GameSettingsService owner) => _owner = owner;
        public void Dispose()
        {
            if (--_owner._mutationDepth != 0 || _owner._pendingReleases.Count == 0) return;
            ++_owner._mutationDepth;
            try
            {
                while (_owner._pendingReleases.TryDequeue(out var lease))
                {
                    try { lease.Dispose(); }
                    catch (Exception error) { _owner._log.TryLog(LogLevel.Error, "Deferred setting lease cleanup failed.", error); }
                }
            }
            finally { --_owner._mutationDepth; }
        }
    }
    private void ReleaseLayer(IMutationLease<float> lease)
    {
        // The coordinator drops holders without an inverse while a channel is busy.
        // Retain the physical layer until the setter and its readback have returned.
        if (_mutationDepth != 0) { _pendingReleases.Enqueue(lease); return; }
        using var backend = UseBackend();
        using var mutation = UseMutation();
        lease.Dispose();
    }

    private sealed class View : IGameSettingsReadService, IGameSettingsOverrideService, IDisposable
    {
        private readonly GameSettingsService _owner; private readonly IPluginContext _context; private bool _disposed;
        internal View(GameSettingsService owner, IPluginContext context) => (_owner, _context) = (owner, context);
        private OperationError? Check()
        {
            var error = _owner.Verify(); if (error is not null) return error;
            if (_disposed || _context is IPluginScope { IsActive: false }) return new(ErrorCode.ScopeExpired, "Plugin settings activation has ended.");
            try { _context.MainThread.VerifyAccess(); return null; }
            catch (ObjectDisposedException) { return new(ErrorCode.ScopeExpired, "Plugin settings activation has ended."); }
            catch (Exception ex) { return new(ErrorCode.WrongThread, ex.Message); }
        }
        public Result<IReadOnlyList<GameFloatSettingDescriptor>> Describe()
        {
            var invalid = Check(); if (invalid is not null) return Result<IReadOnlyList<GameFloatSettingDescriptor>>.Fail(invalid);
            using var backend = _owner.UseBackend();
            _owner.RefreshRanges();
            if (Check() is { } stopped) return Result<IReadOnlyList<GameFloatSettingDescriptor>>.Fail(stopped);
            var descriptors = _owner._backend.Bindings.OrderBy(x => x.Id, StringComparer.Ordinal).Select(_owner.Descriptor).ToArray();
            return Check() is { } retired ? Result<IReadOnlyList<GameFloatSettingDescriptor>>.Fail(retired)
                : Result<IReadOnlyList<GameFloatSettingDescriptor>>.Ok(Array.AsReadOnly(descriptors));
        }
        public Result<IReadOnlyList<GameFloatSettingValue>> GetSettings()
        {
            if (Check() is { } error) return Result<IReadOnlyList<GameFloatSettingValue>>.Fail(error);
            var settings = _owner.GetAll();
            return Check() is { } stopped ? Result<IReadOnlyList<GameFloatSettingValue>>.Fail(stopped) : settings;
        }
        public Result<GameFloatSettingValue> Read(string id)
        {
            var error = Check(); if (error is not null) return Result<GameFloatSettingValue>.Fail(error);
            _owner.RefreshRanges();
            if (Check() is { } stopped) return Result<GameFloatSettingValue>.Fail(stopped);
            var value = _owner.Read(id);
            return Check() is { } retired ? Result<GameFloatSettingValue>.Fail(retired) : value;
        }
        public Result<IGameSettingLease> Override(GameSettingOverrideRequest request)
        {
            var error = Check(); if (error is not null) return Result<IGameSettingLease>.Fail(error);
            if (request is null || string.IsNullOrEmpty(request.Id)) return Result<IGameSettingLease>.Fail(new(ErrorCode.InvalidArgument, "Setting request and ID are required."));
            using var backend = _owner.UseBackend();
            if (!_owner.Ready) return Result<IGameSettingLease>.Fail(new(ErrorCode.NotReady, "Game settings are not initialized."));
            if (!_owner._bound.TryGetValue(request.Id, out var binding)) return Result<IGameSettingLease>.Fail(new(ErrorCode.OperationUnsupported, "No verified getter/setter pair exists for this setting.", TargetId: request.Id));
            if (!binding.Channel.Valid) return Result<IGameSettingLease>.Fail(new(ErrorCode.TargetExpired, "Setting write ownership was lost; the binding has expired.", TargetId: request.Id));
            var refreshed = _owner.RefreshRanges();
            if (Check() is { } stopped) return Result<IGameSettingLease>.Fail(stopped);
            if (!refreshed.Success) return Result<IGameSettingLease>.Fail(refreshed.Error!);
            if (!_owner._ranges.TryGetValue(request.Id, out var range)) return Result<IGameSettingLease>.Fail(_owner._rangeFailure ?? new(ErrorCode.NotReady, "The actual setting slider is not ready.", TargetId: request.Id));
            var invalid = Validate(request.Value, range);
            if (invalid is not null) return Result<IGameSettingLease>.Fail(new(ErrorCode.InvalidArgument, invalid, TargetId: request.Id));
            OperationResult<IMutationLease<float>> applied;
            using (_owner.UseMutation()) applied = binding.Channel.Override(_context.PluginId, request.Value, request.Priority);
            if (Check() is { } retired)
            {
                if (applied.Value is { } layer) _owner.ReleaseLayer(layer);
                return Result<IGameSettingLease>.Fail(retired);
            }
            if (!applied.Success) return Result<IGameSettingLease>.Fail(applied.Error!);
            var lease = new Lease(this, request.Id, applied.Value!);
            try
            {
                _context.Own(lease);
                if (lease.IsActive) return Result<IGameSettingLease>.Ok(lease);
                lease.Dispose();
                return Result<IGameSettingLease>.Fail(Check() ?? Lease.Expired());
            }
            catch (ObjectDisposedException) { lease.Dispose(); return Result<IGameSettingLease>.Fail(Lease.Expired()); }
            catch { lease.Dispose(); throw; }
        }
        public Result<MutationSnapshot<float>> Describe(string id)
        {
            var error = Check(); if (error is not null) return Result<MutationSnapshot<float>>.Fail(error);
            return _owner._bound.TryGetValue(id, out var binding) ? Result<MutationSnapshot<float>>.Ok(binding.Channel.Describe()) :
                Result<MutationSnapshot<float>>.Fail(new(ErrorCode.OperationUnsupported, "No setting mutation channel exists.", TargetId: id));
        }
        public void Dispose() => _disposed = true;

        private sealed class Lease : IGameSettingLease
        {
            private readonly View _view; private IMutationLease<float>? _inner;
            private bool _updating;
            public string SettingId { get; } public string OwnerId => _view._context.PluginId;
            public bool IsActive => _view.Check() is null && _inner?.IsActive == true;
            internal Lease(View view, string settingId, IMutationLease<float> inner) => (_view, SettingId, _inner) = (view, settingId, inner);
            internal static OperationError Expired() => new(ErrorCode.ScopeExpired, "Setting lease is disposed.");
            public Result SetValue(float value)
            {
                var error = _view.Check(); if (error is not null) return Result.Fail(error);
                if (_inner is not { } inner) return Result.Fail(Expired());
                if (_updating) return Result.Fail(new(ErrorCode.Conflict, "This setting lease is already being updated."));
                using var backend = _view._owner.UseBackend();
                _updating = true;
                try
                {
                    var refreshed = _view._owner.RefreshRanges();
                    if (_view.Check() is { } stopped) return Result.Fail(stopped);
                    if (_inner is null) return Result.Fail(Expired());
                    if (!refreshed.Success) return Result.Fail(refreshed.Error!);
                    if (!_view._owner._ranges.TryGetValue(SettingId, out var range)) return Result.Fail(new(ErrorCode.NotReady, "Setting UI is unavailable."));
                    var invalid = Validate(value, range); if (invalid is not null) return Result.Fail(new(ErrorCode.InvalidArgument, invalid));
                    OperationResult result;
                    using (_view._owner.UseMutation()) result = inner.SetValue(value);
                    if (_view.Check() is { } retired) return Result.Fail(retired);
                    if (_inner is null) return Result.Fail(Expired());
                    return result.Success ? Result.Ok() : Result.Fail(result.Error!);
                }
                finally { _updating = false; }
            }
            public void Dispose()
            {
                _view._owner._main.VerifyAccess();
                if (_inner is not { } inner) return;
                _inner = null;
                _view._owner.ReleaseLayer(inner);
            }
        }
    }
}
