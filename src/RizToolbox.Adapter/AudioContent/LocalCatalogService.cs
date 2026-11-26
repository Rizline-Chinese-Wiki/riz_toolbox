using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal interface ILocalRunPlayDispatcher
{
    // Borrowed exact catalog request. Asynchronous native work must retain its
    // own fork before returning; no ID-based re-resolution is permitted.
    Result RequestPlay(LocalPlayRequest request, CancellationToken cancellationToken);
}

/// <summary>Exact-generation CPU song catalog. Native playback is an independent resolution boundary.</summary>
internal sealed class LocalCatalogService : IDisposable
{
    internal const int MaximumSongs = 128, MaximumOwnerSongs = 32, MaximumCharts = 16, MaximumPreparedRequests = 32;
    private const int MaximumResultHandlesPerSelector = 32;
    private readonly IMainThreadDispatcher _main;
    private readonly LocalContentService _content;
    private readonly ILogger? _log;
    private readonly ILocalRunPlayDispatcher? _play;
    private readonly Dictionary<LocalContentId, Song> _published = new();
    private readonly HashSet<Song> _retained = new();
    private readonly HashSet<Selector> _selectors = new();
    private bool _disposed, _busy;
    private long _revision;
    private int _preparedRequests;
    internal bool IsDisposed => _disposed;
    internal int RetainedCount => _retained.Count;
    internal int PreparedRequestCount => _preparedRequests;
    internal bool HasUnconfirmedCleanup => _retained.Any(x => x.References == 0);
    private sealed class Song
    {
        internal LocalSongSnapshot Snapshot = null!; internal Gate Gate = null!;
        internal ValidatedContentLease[] Resources = Array.Empty<ValidatedContentLease>();
        internal int References = 1; internal bool Published;
    }
    internal LocalCatalogService(IMainThreadDispatcher main, LocalContentService content, ILogger? log = null,
        ILocalRunPlayDispatcher? play = null)
    { _main = main; _content = content; _log = log; _play = play; }
    internal ILocalCatalogProviderService ForPlugin(IPluginContext context)
    { _main.VerifyAccess(); return new Provider(this, context, context.Own(new Gate(context))); }
    internal ILocalCatalogSelectionService SelectionForPlugin(IPluginContext context)
    {
        _main.VerifyAccess(); var selector = new Selector(this, context, context.Own(new Gate(context)));
        _selectors.Add(selector); try { return context.Own(selector); } catch { selector.Dispose(); throw; }
    }
    private OperationError? Guard(Gate? gate = null)
    {
        if (!_main.IsMainThread) return new(ErrorCode.WrongThread, "Local catalog operations require the main thread.");
        if (_disposed || gate is { Active: false }) return new(ErrorCode.ScopeExpired, "Local catalog scope has expired.");
        return _busy ? new(ErrorCode.Conflict, "Reentrant catalog operation rejected.") : null;
    }
    private static bool Id(string? value) => value is { Length: > 0 and <= 128 }
        && value.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-');
    private static bool Text(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256 && !value.Any(char.IsControl);
    private bool Current(Song song) => !_disposed && song.Published && song.Gate.Active
        && _published.TryGetValue(song.Snapshot.Song, out var current) && ReferenceEquals(song, current)
        && song.Resources.All(_content.IsCatalogResourceCurrent);
    private void Prune() { foreach (var song in _published.Values.ToArray()) if (!Current(song)) Withdraw(song); }
    internal void Refresh() { _main.VerifyAccess(); if (!_disposed) Prune(); }
    private Result<LocalCatalogSnapshot> Read(Gate gate)
    {
        var guard = Guard(gate); if (guard is not null) return Result<LocalCatalogSnapshot>.Fail(guard);
        Prune(); return Result<LocalCatalogSnapshot>.Ok(new(_revision, Array.AsReadOnly(_published.Values
            .Select(x => x.Snapshot).OrderBy(x => x.Song.Namespace, StringComparer.Ordinal).ThenBy(x => x.Song.Id, StringComparer.Ordinal).ToArray())));
    }
    private Result<ILocalSongRegistration> Register(IPluginContext context, Gate gate, LocalSongRequest request, CancellationToken token)
    {
        Result<ILocalSongRegistration> Fail(ErrorCode code, string message) => Result<ILocalSongRegistration>.Fail(new(code, message, AudioContentCapabilityIds.LocalCatalog));
        var guard = Guard(gate); if (guard is not null) return Result<ILocalSongRegistration>.Fail(guard);
        _busy = true; var leases = new List<ValidatedContentLease>(); Registration? registration = null; bool committed = false;
        try
        {
            token.ThrowIfCancellationRequested(); Prune();
            if (request is null || !Id(request.Id) || !Text(request.Title) || !Text(request.Artist) || request.Audio is null || request.Cover is null)
                return Fail(ErrorCode.ContentInvalid, "A bounded ID, title, artist, audio and cover are required.");
            var count = request.Charts?.Count ?? 0;
            if (count is < 1 or > MaximumCharts) return Fail(ErrorCode.ContentInvalid, "A song requires 1..16 validated charts.");
            var charts = new LocalSongChartRequest[count]; var names = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < count; i++)
            {
                var chart = request.Charts![i];
                if (chart is null || !Id(chart.Id) || !Text(chart.Name) || chart.Content is null || !names.Add(chart.Id))
                    return Fail(ErrorCode.ContentInvalid, "Chart IDs must be unique and metadata bounded.");
                charts[i] = chart with { Content = chart.Content with { }, Playback = LocalChartPlaybackMetadataValidation.CopyOptional(chart.Playback) };
            }
            var songId = new LocalContentId(context.PluginId, request.Id);
            if (!Id(songId.Namespace)) return Fail(ErrorCode.ContentInvalid, "Invalid provider namespace.");
            if (_published.ContainsKey(songId)) return Fail(ErrorCode.Conflict, "Duplicate local song ID.");
            if (_retained.Count >= MaximumSongs || _retained.Count(x => x.Snapshot.Song.Namespace == context.PluginId) >= MaximumOwnerSongs)
                return Fail(ErrorCode.QuotaExceeded, "Live and retained songs exhaust the catalog quota.");
            var acquired = new Dictionary<LocalContentId, ValidatedContentLease>();
            ValidatedContentLease Acquire(LocalContentId id, LocalContentKind kind)
            {
                token.ThrowIfCancellationRequested();
                if (!Id(id.Namespace) || !Id(id.Id)) throw new InvalidDataException("Invalid content ID.");
                if (!acquired.TryGetValue(id, out var lease))
                {
                    var result = _content.AcquireValidated(id, "catalog:" + Guid.NewGuid().ToString("N"));
                    if (!result.Success) throw new InvalidDataException("Required validated content is absent.");
                    lease = result.Value!; leases.Add(lease); acquired.Add(id, lease);
                }
                if (lease.Snapshot.Kind != kind || !_content.IsCatalogResourceCurrent(lease)) throw new InvalidDataException("Content kind or generation mismatch.");
                return lease;
            }
            var audio = Acquire(request.Audio with { }, LocalContentKind.Audio); var cover = Acquire(request.Cover with { }, LocalContentKind.Cover);
            var chartSnapshots = charts.Select(chart =>
            {
                var resource = Acquire(chart.Content, LocalContentKind.Chart);
                return new LocalSongChartSnapshot(chart.Id, chart.Name, resource.Snapshot)
                {
                    Playback = chart.Playback,
                    HasPlaybackContent = chart.Playback is not null && LocalChartPlaybackMetadataValidation.HasNotes(resource)
                        && LocalChartPlaybackMetadataValidation.GetPlaybackLineError(resource) is null
                };
            }).ToArray();
            var revisionBeforeOwnership = _revision;
            var next = checked(revisionBeforeOwnership + 1);
            // The active game and selected chart determine admission at request time.
            var song = new Song { Gate = gate, Snapshot = new(songId, request.Title, request.Artist, next, audio.Snapshot, cover.Snapshot, Array.AsReadOnly(chartSnapshots), false), Resources = leases.ToArray() };
            registration = new(this, song); context.Own(registration); token.ThrowIfCancellationRequested();
            if (_disposed || !gate.Active || registration.Disposed) return Fail(ErrorCode.ScopeExpired, "Provider expired before publication.");
            if (!song.Resources.All(_content.IsCatalogResourceCurrent)) return Fail(ErrorCode.ContentInvalid, "Content changed during ownership registration.");
            if (_revision != revisionBeforeOwnership) return Fail(ErrorCode.Conflict, "Catalog changed during ownership registration; retry against the new revision.");
            _published.EnsureCapacity(_published.Count + 1); _retained.EnsureCapacity(_retained.Count + 1);
            _retained.Add(song); _published.Add(songId, song); song.Published = true; _revision = next; committed = true;
            return Result<ILocalSongRegistration>.Ok(registration);
        }
        catch (OperationCanceledException) { return Fail(ErrorCode.Cancelled, "Song registration cancelled."); }
        catch (InvalidDataException e) { return Fail(ErrorCode.ContentInvalid, e.Message); }
        catch (Exception e) { return Fail(ErrorCode.InternalError, "Song registration failed: " + e.Message); }
        finally { if (!committed) { if (registration is not null) registration.Disposed = true; ReleaseResources(leases); } _busy = false; }
    }
    private void Withdraw(Song song)
    {
        if (!song.Published) return; song.Published = false;
        if (_published.TryGetValue(song.Snapshot.Song, out var current) && ReferenceEquals(current, song)) _published.Remove(song.Snapshot.Song);
        _revision = checked(_revision + 1); Release(song);
    }
    private void Release(Song song)
    { if (--song.References == 0 && ReleaseResources(song.Resources)) { song.Resources = Array.Empty<ValidatedContentLease>(); _retained.Remove(song); } }
    private bool ReleaseResources(IEnumerable<ValidatedContentLease> resources)
    {
        bool success = true;
        foreach (var lease in resources.Reverse()) try { lease.Dispose(); } catch (Exception e)
        { success = false; try { _log?.Log(LogLevel.Error, "Catalog cleanup failed; underlying content store retains unknown cleanup responsibility.", e); } catch { } }
        return success;
    }
    private Result<ILocalCatalogSelection> Select(Selector selector, LocalContentId? id, string chartId,
        CancellationToken token, LocalSongSnapshot? expected = null)
    {
        Result<ILocalCatalogSelection> Fail(ErrorCode code, string message) => Result<ILocalCatalogSelection>.Fail(new(code, message, AudioContentCapabilityIds.LocalCatalogSelection));
        var guard = Guard(selector.Gate); if (guard is not null) return Result<ILocalCatalogSelection>.Fail(guard);
        if (selector.Disposed) return Fail(ErrorCode.ScopeExpired, "Selection view is disposed.");
        _busy = true; Selection? selection = null; bool committed = false;
        try
        {
            token.ThrowIfCancellationRequested(); Prune();
            if (id is null || !Id(id.Namespace) || !Id(id.Id) || !Id(chartId)) return Fail(ErrorCode.InvalidArgument, "Invalid song/chart ID.");
            if (!_published.TryGetValue(id, out var song) || !Current(song))
                return Fail(expected is null ? ErrorCode.TargetMissing : ErrorCode.TargetExpired, "Song withdrawn or absent.");
            if (expected is not null && !ReferenceEquals(expected, song.Snapshot))
                return Fail(ErrorCode.TargetExpired, "Song revision changed. Select it again from the refreshed catalog.");
            var chart = song.Snapshot.Charts.SingleOrDefault(x => x.Id == chartId);
            if (chart is null) return Fail(ErrorCode.TargetMissing, "Chart absent from song revision.");
            song.References++; selection = new(this, selector, song, new(song.Snapshot, chart, checked(selector.Generation + 1)));
            selector.Context.Own(selection); token.ThrowIfCancellationRequested();
            if (_disposed || !selector.Gate.Active || selector.Disposed || selection.Disposed) return Fail(ErrorCode.ScopeExpired, "Selection expired before commit.");
            if (!Current(song)) return Fail(ErrorCode.TargetExpired, "Song changed before selection commit.");
            selector.Generation = selection.Snapshot.Generation; selector.Current = selection; committed = true;
            return Result<ILocalCatalogSelection>.Ok(selection);
        }
        catch (OperationCanceledException) { return Fail(ErrorCode.Cancelled, "Selection cancelled."); }
        catch (Exception e) { return Fail(ErrorCode.InternalError, "Selection failed: " + e.Message); }
        finally { if (!committed) selection?.Dispose(); _busy = false; }
    }
    internal Result<LocalPlayRequest> Prepare(ILocalCatalogSelection selected, CancellationToken token = default,
        LocalRunResultObservation? observation = null)
    {
        var guard = Guard(); if (guard is not null) return Result<LocalPlayRequest>.Fail(guard);
        if (token.IsCancellationRequested) return Result<LocalPlayRequest>.Fail(new(ErrorCode.Cancelled, "Preparation cancelled."));
        if (selected is not Selection selection || !ReferenceEquals(selection.Owner, this) || !selection.IsCurrent)
            return Result<LocalPlayRequest>.Fail(new(ErrorCode.TargetExpired, "Selection generation expired."));
        if (_preparedRequests >= MaximumPreparedRequests)
            return Result<LocalPlayRequest>.Fail(new(ErrorCode.QuotaExceeded, "Prepared local requests exhaust the host consumer quota."));
        return Result<LocalPlayRequest>.Ok(RetainPrepared(selection.Song, selection.Snapshot, observation));
    }
    private Result RequestPlay(Selection selection, CancellationToken token, LocalRunResultObservation? observation = null)
    {
        var guard = Guard(selection.Selector.Gate); if (guard is not null) return Result.Fail(guard);
        if (token.IsCancellationRequested) return Result.Fail(new(ErrorCode.Cancelled, "Play cancelled."));
        if (!selection.IsCurrent) return Result.Fail(new(ErrorCode.TargetExpired, "Song or selection generation expired."));
        if (_play is null) return Result.Fail(new(ErrorCode.OperationUnsupported,
            "This catalog has no native play composition.", AudioContentCapabilityIds.LocalCatalogSelection));
        var chart = selection.Snapshot.Chart;
        if (!chart.HasPlaybackContent)
        {
            var resource = selection.Song.Resources.Single(lease => ReferenceEquals(lease.Snapshot, chart.Content));
            if (LocalChartPlaybackMetadataValidation.GetPlaybackLineError(resource) is { } lineError)
                return Result.Fail(new(ErrorCode.ContentInvalid, lineError, AudioContentCapabilityIds.LocalCatalogSelection, chart.Id));
        }
        var prepared = Prepare(selection, token, observation); if (!prepared.Success) return Result.Fail(prepared.Error!);
        var request = prepared.Value!; _busy = true;
        Result outcome;
        try
        {
            if (token.IsCancellationRequested) outcome = Result.Fail(new(ErrorCode.Cancelled, "Play cancelled before dispatch."));
            else if (!selection.IsCurrent) outcome = Result.Fail(new(ErrorCode.TargetExpired, "Selection changed before native dispatch."));
            else outcome = _play.RequestPlay(request, token)
                ?? throw new InvalidOperationException("Native LOCAL dispatch returned no result.");
        }
        catch (Exception error) { outcome = Result.Fail(new(ErrorCode.InternalError, "Native LOCAL dispatch is unconfirmed: " + error.GetType().Name)); }
        try { request.Dispose(); }
        catch (Exception error) { outcome = Result.Fail(new(ErrorCode.InternalError, "LOCAL request cleanup is unconfirmed: " + error.GetType().Name)); }
        finally { _busy = false; }
        return outcome;
    }
    private Result<ILocalRunResultHandle> RequestPlayWithResult(Selection selection, CancellationToken token)
    {
        var gate = selection.Selector.Gate;
        var guard = Guard(gate);
        if (guard is not null) return Result<ILocalRunResultHandle>.Fail(guard);
        var observation = new LocalRunResultObservation(selection.Snapshot);
        if (!gate.TryOwnResult(observation))
            return Result<ILocalRunResultHandle>.Fail(new(ErrorCode.QuotaExceeded,
                "Release an unused LOCAL result handle before requesting another run.", AudioContentCapabilityIds.LocalCatalogSelection,
                selection.Snapshot.Chart.Id));
        var handle = new RunResultHandle(_main, gate, observation);
        var returned = false;
        try
        {
            var accepted = RequestPlay(selection, token, observation);
            if (!accepted.Success) return Result<ILocalRunResultHandle>.Fail(accepted.Error!);
            if (!gate.Active) return Result<ILocalRunResultHandle>.Fail(new(ErrorCode.ScopeExpired,
                "The LOCAL request was accepted but its observation scope ended.", AudioContentCapabilityIds.LocalCatalogSelection));
            returned = true;
            return Result<ILocalRunResultHandle>.Ok(handle);
        }
        finally { if (!returned) handle.Dispose(); }
    }
    private LocalPlayRequest RetainPrepared(Song song, LocalSongSelectionSnapshot selection, LocalRunResultObservation? observation)
    {
        _main.VerifyAccess();
        // This is a host continuation of an existing consumer, including after publication or
        // module withdrawal. It must retain the pinned revision rather than re-resolve an ID.
        if (song.References <= 0 || song.Resources.Length == 0)
            throw new ObjectDisposedException(nameof(LocalPlayRequest));
        if (_preparedRequests >= MaximumPreparedRequests)
            throw new InvalidOperationException("Prepared local requests exhaust the host consumer quota.");
        var references = checked(song.References + 1);
        var request = new LocalPlayRequest(selection, song.Resources,
            () => RetainPrepared(song, selection, observation),
            () => { _preparedRequests--; Release(song); }, _main.VerifyAccess, observation);
        song.References = references; _preparedRequests++;
        return request;
    }
    public void Dispose()
    {
        _main.VerifyAccess(); if (_disposed) return; _disposed = true;
        foreach (var selector in _selectors.ToArray()) selector.Dispose();
        foreach (var song in _published.Values.ToArray()) Withdraw(song);
    }
    private sealed class Gate : IDisposable
    {
        private readonly CancellationToken _lifetime;
        private HashSet<LocalRunResultObservation>? _results;
        private bool _active = true;
        internal Gate(IPluginContext context) => _lifetime = (context as IScopedPluginContext)?.Lifetime ?? default;
        internal bool Active => _active && !_lifetime.IsCancellationRequested;
        internal bool TryOwnResult(LocalRunResultObservation observation)
        {
            _results ??= new();
            return _results.Count < MaximumResultHandlesPerSelector && _results.Add(observation);
        }
        internal void ReleaseResult(LocalRunResultObservation observation)
        {
            if (_results?.Remove(observation) == true) observation.Release();
        }
        public void Dispose()
        {
            _active = false;
            if (_results is null) return;
            foreach (var observation in _results) observation.Release();
            _results.Clear();
        }
    }
    private sealed class RunResultHandle : ILocalRunResultHandle
    {
        private readonly IMainThreadDispatcher _main;
        private readonly LocalRunResultObservation _observation;
        private Gate? _gate;
        internal RunResultHandle(IMainThreadDispatcher main, Gate gate, LocalRunResultObservation observation)
            => (_main, _gate, _observation) = (main, gate, observation);
        public Result<LocalRunResultSnapshot> Read()
        {
            if (!_main.IsMainThread) return Result<LocalRunResultSnapshot>.Fail(new(ErrorCode.WrongThread,
                "Reading a LOCAL result handle requires the main thread.", AudioContentCapabilityIds.LocalCatalogSelection));
            if (_gate is not { Active: true }) return Result<LocalRunResultSnapshot>.Fail(new(ErrorCode.ScopeExpired,
                "The LOCAL result handle or its requesting scope has expired.", AudioContentCapabilityIds.LocalCatalogSelection));
            return _observation.Read();
        }
        public void Dispose()
        {
            _main.VerifyAccess();
            var gate = _gate; _gate = null;
            gate?.ReleaseResult(_observation);
        }
    }
    private sealed class Provider : ILocalCatalogProviderService
    {
        private readonly LocalCatalogService _owner; private readonly IPluginContext _context; private readonly Gate _gate;
        internal Provider(LocalCatalogService owner, IPluginContext context, Gate gate) => (_owner, _context, _gate) = (owner, context, gate);
        public Result<ILocalSongRegistration> RegisterSong(LocalSongRequest request, CancellationToken cancellationToken = default)
            => _owner.Register(_context, _gate, request, cancellationToken);
        public Result<LocalCatalogSnapshot> Read() => _owner.Read(_gate);
    }
    private sealed class Registration : ILocalSongRegistration
    {
        private readonly LocalCatalogService _owner; private readonly Song _song; internal bool Disposed;
        internal Registration(LocalCatalogService owner, Song song) => (_owner, _song) = (owner, song);
        public LocalSongSnapshot Snapshot => _song.Snapshot;
        public bool IsPublished => !Disposed && _owner._main.IsMainThread && _owner.Current(_song);
        public void Dispose() { _owner._main.VerifyAccess(); if (Disposed) return; Disposed = true; _owner.Withdraw(_song); }
    }
    private sealed class Selector : ILocalCatalogSelectionService, IDisposable
    {
        private readonly LocalCatalogService _owner; internal readonly IPluginContext Context; internal readonly Gate Gate;
        internal long Generation; internal Selection? Current; internal bool Disposed;
        internal Selector(LocalCatalogService owner, IPluginContext context, Gate gate) => (_owner, Context, Gate) = (owner, context, gate);
        public Result<ILocalCatalogSelection> Select(LocalContentId song, string chartId, CancellationToken cancellationToken = default)
            => _owner.Select(this, song, chartId, cancellationToken);
        public Result<ILocalCatalogSelection> SelectRevision(LocalSongSnapshot song, string chartId, CancellationToken cancellationToken = default)
            => _owner.Select(this, song?.Song, chartId, cancellationToken, song);
        public Result<LocalSongSelectionSnapshot?> ReadCurrent()
        {
            var guard = _owner.Guard(Gate); if (guard is not null) return Result<LocalSongSelectionSnapshot?>.Fail(guard);
            if (Disposed) return Result<LocalSongSelectionSnapshot?>.Fail(new(ErrorCode.ScopeExpired, "Selection view disposed."));
            return Result<LocalSongSelectionSnapshot?>.Ok(Current?.IsCurrent == true ? Current.Snapshot : null);
        }
        public void Dispose() { _owner._main.VerifyAccess(); if (Disposed) return; Disposed = true; Current?.Dispose(); Current = null; _owner._selectors.Remove(this); }
    }
    private sealed class Selection : ILocalCatalogSelection
    {
        internal readonly LocalCatalogService Owner; internal readonly Selector Selector; internal readonly Song Song; internal bool Disposed;
        internal Selection(LocalCatalogService owner, Selector selector, Song song, LocalSongSelectionSnapshot snapshot)
            => (Owner, Selector, Song, Snapshot) = (owner, selector, song, snapshot);
        public LocalSongSelectionSnapshot Snapshot { get; }
        public bool IsCurrent => Owner._main.IsMainThread && !Disposed && !Selector.Disposed && Selector.Gate.Active && ReferenceEquals(Selector.Current, this) && Owner.Current(Song);
        public Result RequestPlay(CancellationToken cancellationToken = default)
            => Owner.RequestPlay(this, cancellationToken);
        public Result<ILocalRunResultHandle> RequestPlayWithResult(CancellationToken cancellationToken = default)
            => Owner.RequestPlayWithResult(this, cancellationToken);
        public void Dispose() { Owner._main.VerifyAccess(); if (Disposed) return; Disposed = true; if (ReferenceEquals(Selector.Current, this)) Selector.Current = null; Owner.Release(Song); }
    }
}

internal sealed class LocalPlayRequest : IDisposable
{
    internal LocalSongSelectionSnapshot Selection { get; }
    internal LocalRunResultObservation? ResultObservation { get; }
    private readonly IReadOnlyList<ValidatedContentLease> _resources;
    internal IReadOnlyList<ValidatedContentLease> Resources { get { VerifyActive(); return _resources; } }
    internal bool IsDisposed => _release is null;
    private readonly Action _verify; private readonly Func<LocalPlayRequest> _retain; private Action? _release;
    internal LocalPlayRequest(LocalSongSelectionSnapshot selection, ValidatedContentLease[] resources,
        Func<LocalPlayRequest> retain, Action release, Action verify, LocalRunResultObservation? observation = null)
    {
        Selection = selection; ResultObservation = observation; _resources = Array.AsReadOnly(resources);
        _retain = retain; _release = release; _verify = verify;
    }
    /// <summary>Forks host responsibility for the exact loaded revision; does not grant game playback.</summary>
    internal LocalPlayRequest Retain() { VerifyActive(); return _retain(); }
    private void VerifyActive()
    { _verify(); if (_release is null) throw new ObjectDisposedException(nameof(LocalPlayRequest)); }
    public void Dispose() { _verify(); var release = _release; _release = null; release?.Invoke(); }
}
