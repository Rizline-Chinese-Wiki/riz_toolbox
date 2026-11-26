using System.Text.Json;
using RizToolbox.Sdk;

namespace RizToolbox.Examples;

/// <summary>An opt-in consumer of the independent LOCAL catalog. It never supplies a Workshop ID or calls stock SetInfo.</summary>
internal sealed class LocalCatalogRuntimeProbe : IDisposable
{
    private const int ProofPageSize = 6, BrowserPageSize = 4, SongRowPitch = 48;
    private const int MaximumVisibleSongs = 128, MaximumFavorites = 128;
    private const string PreferencesName = "local-catalog-selection";
    private readonly IPluginContext _context;
    private readonly bool _observeResults;
    private readonly int _pageSize;
    private readonly List<IUiControlHandle> _rows = new();
    private readonly HashSet<LocalContentId> _favorites = new();
    private IUiWindowHandle? _window;
    private IUiControlHandle? _status, _pageLabel, _details, _chart, _favorite, _filter, _play, _noticeLabel;
    private IUiControlHandle? _resultLabel;
    private ILocalCatalogSelection? _selection;
    private PendingResult? _pendingResult;
    private string _resultText = "No LOCAL result yet.";
    private LocalSongSnapshot[] _songs = Array.Empty<LocalSongSnapshot>();
    private Preferences _preferences = new();
    private string _catalogStatus = "Waiting for the LOCAL catalog capability.";
    private string _notice = "Select a song and chart.";
    private string? _storageFailure;
    private string? _mountNotice;
    private string? _windowMount;
    private LocalSongSnapshot? _restoreSong;
    private double _nextPoll;
    private long _sceneGeneration, _lastCatalogRevision = -1;
    private int _page, _lastPublishedCount = -1;
    private bool _preferencesAttempted, _preferencesLoaded, _restorePending = true, _favoritesOnly, _disposed;

    internal static void Start(IPluginContext context)
    {
        var config = context.Configuration.Load("local-catalog-proof", () => new ProbeSettings());
        if (!config.Success) { context.Log.Log(LogLevel.Warning, "LocalCatalogProbe configuration refused: " + config.Error); return; }
        if (config.Value?.Enabled != true) return;
        if (config.Value.PublishValidationFixture) LocalCatalogValidationProvider.Start(context);
        StartBrowser(context, observeResults: false);
    }

    internal static void StartBrowser(IPluginContext context, bool observeResults)
    {
        var probe = context.Own(new LocalCatalogRuntimeProbe(context, observeResults));
        context.Events.Subscribe<GameSceneChanged>(probe.SceneChanged);
        context.Events.Subscribe<FrameUpdate>(probe.Tick);
    }

    private LocalCatalogRuntimeProbe(IPluginContext context, bool observeResults)
    {
        _context = context;
        _observeResults = observeResults;
        _pageSize = observeResults ? BrowserPageSize : ProofPageSize;
    }

    private void Tick(FrameUpdate frame)
    {
        if (_disposed || frame.ElapsedSeconds < _nextPoll) return;
        _nextPoll = frame.ElapsedSeconds + 1;
        try
        {
            EnsurePreferences();
            EnsureWindow();
            if (_window?.IsAlive != true) return;
            PollResult();
            RefreshCatalog();
            Render();
        }
        catch (Exception error) { SetNotice("Catalog observation failed: " + Limit(error.Message, 180)); Log("OBSERVATION FAILED", error.Message); }
    }

    private void EnsureWindow()
    {
        if (_window?.IsAlive == true) return;
        CloseWindow();
        var ui = _context.GetService<IOwnedUiService>(UiInputCapabilityIds.Windows).Service;
        if (ui is null) return;
        var mounts = ui.GetMounts();
        var scene = mounts.FirstOrDefault(x => x.Id == UiMountIds.Overlay)?.Scene;
        var mountId = CatalogMount(scene);
        if (mountId is null) { LeaveCatalog(); return; }
        var mount = mounts.FirstOrDefault(x => x.Id == mountId && x.Scene == scene);
        if (mount?.Available != true)
        {
            var notice = mount?.Reason ?? $"Catalog mount unavailable in {scene}.";
            if (_mountNotice != notice) { _mountNotice = notice; Log("MOUNT UNAVAILABLE", new { scene, mountId, notice }); }
            return;
        }
        _mountNotice = null;
        var created = ui.CreateWindow(new("local-catalog", mountId, "LOCAL catalog",
            new(new(75, 165, 680, 800)), Theme: new(FontSize: 18)));
        if (!created.Success || created.Value is null) { Log("WINDOW REFUSED", created.Error); return; }
        _window = created.Value;
        _windowMount = mountId;
        try { AddControls(); }
        catch { CloseWindow(); throw; }
        Log("WINDOW CREATED", new { _sceneGeneration, scene, mount = mountId });
    }

    private static string? CatalogMount(string? scene) => scene switch
    {
        "MainMenu" or "FakeMainMenu" => UiMountIds.MainMenu,
        "LevelList" => UiMountIds.Overlay,
        _ => null
    };

    private void AddControls()
    {
        _status = Need(_window!.AddLabel(new("catalog-status", "", new(15, 40, 650, 65))));
        for (var i = 0; i < _pageSize; i++)
        {
            var row = i;
            _rows.Add(Need(_window.AddButton(new("song-" + i, "", new(15, 115 + i * SongRowPitch, 650, 42)), () => Run(() => SelectRow(row)))));
        }
        var rowSpace = (ProofPageSize - _pageSize) * SongRowPitch;
        Button("previous", "Previous", new(15, 415 - rowSpace, 135, 42), () => { _page = Math.Max(0, _page - 1); Render(); });
        Button("next", "Next", new(160, 415 - rowSpace, 135, 42), () => { _page = Math.Min(PageCount() - 1, _page + 1); Render(); });
        Button("refresh", "Refresh", new(305, 415 - rowSpace, 135, 42), () => { EnsurePreferences(true); RefreshCatalog(); Render(); });
        _filter = Button("favorites-filter", "All songs", new(450, 415 - rowSpace, 215, 42), () => { _favoritesOnly = !_favoritesOnly; _page = 0; Render(); });
        _pageLabel = Need(_window.AddLabel(new("page", "", new(15, 465 - rowSpace, 650, 30))));
        var detailSpace = _observeResults ? 23 : 0;
        _details = Need(_window.AddLabel(new("selection", "", new(15, 505 - rowSpace, 650, 105 - detailSpace))));
        rowSpace += detailSpace;
        _chart = Need(_window.AddLabel(new("chart", "", new(15, 620 - rowSpace, 390, 38))));
        Button("previous-chart", "< Chart", new(420, 615 - rowSpace, 115, 42), () => SelectChart(-1));
        Button("next-chart", "Chart >", new(550, 615 - rowSpace, 115, 42), () => SelectChart(1));
        _favorite = Button("favorite", "Favorite", new(15, 675 - rowSpace, 210, 44), ToggleFavorite);
        _play = Button("play", "Select a chart", new(235, 675 - rowSpace, 210, 44), RequestPlay);
        Button("clear-selection", "Clear selection", new(455, 675 - rowSpace, 210, 44), ClearSelection);
        _noticeLabel = Need(_window.AddLabel(new("notice", "", new(15, 730 - rowSpace, 650, 60))));
        if (_observeResults)
            _resultLabel = Need(_window.AddLabel(new("result", _resultText, new(15, 677, 650, 118))));
    }

    private IUiControlHandle Button(string id, string text, UiRect rect, Action clicked)
        => Need(_window!.AddButton(new(id, text, rect), () => Run(clicked)));

    private void Run(Action action)
    {
        if (_disposed) return;
        try { action(); }
        catch (Exception error) { SetNotice("Action failed: " + Limit(error.Message, 180)); Log("ACTION FAILED", error.Message); }
    }

    private void RefreshCatalog()
    {
        var lookup = _context.GetService<ILocalCatalogProviderService>(AudioContentCapabilityIds.LocalCatalog);
        if (!lookup.Available || lookup.Service is null)
        {
            _songs = Array.Empty<LocalSongSnapshot>(); _catalogStatus = "Catalog unavailable: " + Limit(lookup.Error?.Message, 160);
            ReleaseSelection("catalog-unavailable"); return;
        }
        var read = lookup.Service.Read();
        if (!read.Success || read.Value is null)
        {
            _songs = Array.Empty<LocalSongSnapshot>(); _catalogStatus = Describe(read.Error);
            ReleaseSelection("catalog-read-refused"); return;
        }
        var catalog = read.Value;
        _songs = catalog.Songs.Take(MaximumVisibleSongs).ToArray();
        _catalogStatus = $"LOCAL revision {catalog.Revision}: {catalog.Songs.Count} published song(s)" +
            (catalog.Songs.Count > MaximumVisibleSongs ? $"; showing first {MaximumVisibleSongs}" : "");
        if (_lastCatalogRevision != catalog.Revision || _lastPublishedCount != catalog.Songs.Count)
        {
            Log("CATALOG", new { catalog.Revision, count = catalog.Songs.Count, displayed = _songs.Length });
            _lastCatalogRevision = catalog.Revision; _lastPublishedCount = catalog.Songs.Count;
        }
        if (_selection is not null && (!_selection.IsCurrent || !_songs.Any(x => x.Song == _selection.Snapshot.Song.Song && x.Revision == _selection.Snapshot.Song.Revision)))
        { ReleaseSelection("song-withdrawn-or-revised"); SetNotice("Selected content was withdrawn or revised; select a current row."); }
        if (_restorePending && _preferencesLoaded && _preferences.LastSong is { } id)
        {
            var saved = _songs.SingleOrDefault(x => x.Song == id);
            if (saved is not null && _context.GetCapability(AudioContentCapabilityIds.LocalCatalogSelection).Available)
            {
                _restorePending = false;
                var expected = _restoreSong; _restoreSong = null;
                if (expected is not null && !ReferenceEquals(expected, saved))
                { SetNotice("Saved song was replaced. Select its current revision."); return; }
                var chart = saved.Charts.FirstOrDefault(x => x.Id == _preferences.LastChart) ?? saved.Charts.First();
                Select(saved, chart, persist: false);
            }
        }
    }

    private LocalSongSnapshot[] Visible() => _favoritesOnly ? _songs.Where(x => _favorites.Contains(x.Song)).ToArray() : _songs;
    private int PageCount() => Math.Max(1, (Visible().Length + _pageSize - 1) / _pageSize);
    private void SelectRow(int row)
    {
        var visible = Visible(); var index = _page * _pageSize + row;
        if ((uint)index >= (uint)visible.Length) { SetNotice("No published song in this row."); return; }
        _restorePending = false;
        Select(visible[index], visible[index].Charts.First(), persist: true);
    }

    private void SelectChart(int delta)
    {
        if (_selection is null || !_selection.IsCurrent) { SetNotice("Select a current song first."); return; }
        var current = _selection.Snapshot; var charts = current.Song.Charts;
        var index = 0; while (index < charts.Count && charts[index].Id != current.Chart.Id) index++;
        var next = (index + delta + charts.Count) % charts.Count;
        Select(current.Song, charts[next], persist: true);
    }

    private void Select(LocalSongSnapshot song, LocalSongChartSnapshot chart, bool persist)
    {
        var lookup = _context.GetService<ILocalCatalogSelectionService>(AudioContentCapabilityIds.LocalCatalogSelection);
        if (!lookup.Available || lookup.Service is null) { SetNotice("Selection unavailable: " + Limit(lookup.Error?.Message, 180)); return; }
        var result = lookup.Service.SelectRevision(song, chart.Id);
        if (!result.Success || result.Value is null) { SetNotice(Describe(result.Error)); Log("SELECT REFUSED", result.Error); return; }
        var previous = _selection; _selection = result.Value;
        try { previous?.Dispose(); } catch (Exception error) { Log("PREVIOUS SELECTION RELEASE FAILED", error.Message); }
        var selected = _selection.Snapshot;
        Log("SELECTED", new { selected.Song.Song, selected.Song.Revision, chart = selected.Chart.Id, selected.Generation, _selection.IsCurrent });
        SetNotice("Selected " + Limit(selected.Chart.Name, 60) + ".");
        if (persist && _preferencesLoaded) SavePreferences(_preferences with { LastSong = selected.Song.Song, LastChart = selected.Chart.Id });
        Render();
    }

    private void RequestPlay()
    {
        var selection = _selection;
        if (selection is null || !selection.IsCurrent) { SetNotice("Select a current song first."); return; }
        var snapshot = selection.Snapshot;
        if (!snapshot.Chart.HasPlaybackContent)
        {
            SetNotice(snapshot.Chart.Playback is null ? "This chart has no playback metadata." : "Check the chart's notes and line points for playback.");
            return;
        }
        if (_windowMount != UiMountIds.Overlay || _window?.IsAlive != true)
        { SetNotice("Open the song list to request play."); return; }
        var ui = _context.GetService<IOwnedUiService>(UiInputCapabilityIds.Windows).Service;
        if (ui?.GetMounts().Any(x => x.Id == UiMountIds.Overlay && x.Available && x.Scene == "LevelList") != true)
        { SetNotice("Open the song list to request play."); return; }
        if (_observeResults)
        {
            RequestObservedPlay(selection, snapshot);
            return;
        }

        var result = selection.RequestPlay();
        Log("PLAY RECEIPT", new { snapshot.Song.Song, snapshot.Generation, result.Success, result.Error });
        SetNotice(result.Success ? "Play request accepted." : Describe(result.Error));
    }

    private void RequestObservedPlay(ILocalCatalogSelection selection, LocalSongSelectionSnapshot snapshot)
    {
        var request = selection.RequestPlayWithResult();
        Log("PLAY RECEIPT", new { snapshot.Song.Song, snapshot.Generation, request.Success, request.Error });
        if (!request.Success || request.Value is null)
        {
            SetNotice(Describe(request.Error));
            return;
        }
        if (_disposed)
        {
            ReleaseResult(request.Value);
            return;
        }

        var previous = _pendingResult;
        var title = $"{Limit(snapshot.Song.Title, 34)} / {Limit(snapshot.Chart.Name, 22)}";
        _pendingResult = new(request.Value, snapshot.Song.Song, snapshot.Song.Revision, snapshot.Chart.Id, title);
        _resultText = $"{title}\nWaiting for this LOCAL result.";
        ReleaseResult(previous?.Handle);
        Text(_resultLabel, _resultText);
        SetNotice("Play request accepted.");
    }

    private void PollResult()
    {
        if (_pendingResult is not { } pending) return;

        Result<LocalRunResultSnapshot> read;
        try { read = pending.Handle.Read(); }
        catch (Exception error) { read = Result<LocalRunResultSnapshot>.Fail(new(ErrorCode.ObservationUnavailable, error.Message)); }
        if (!read.Success && read.Error is { KnownCode: ErrorCode.NotReady, Retryable: true }) return;

        if (read.Success && read.Value is { } snapshot && snapshot.Result is { } result &&
            snapshot.Song == pending.Song && snapshot.SongRevision == pending.SongRevision && snapshot.ChartId == pending.ChartId)
        {
            var score = FormattableString.Invariant($"Score {result.Score}   Raw completion {result.RawCompletion:R}");
            var judgement = FormattableString.Invariant($"Perfect {result.Perfect}   MaxPerfect {result.MaxPerfect}   Bad {result.Bad}   Miss {result.Miss}");
            _resultText = $"{pending.Title}\n{score}\n{judgement}\nEnd reason {result.EndReason}   Crashed {result.Crashed}   Skipped {result.Skipped}";
        }
        else
        {
            var error = read.Error ?? new(ErrorCode.ObservationUnavailable, "The captured result did not match this LOCAL request.");
            _resultText = $"{pending.Title}\n{error.Code}  {Limit(error.Message, 112)}";
        }

        _pendingResult = null;
        ReleaseResult(pending.Handle);
    }

    private void ReleaseResult(ILocalRunResultHandle? handle)
    {
        if (handle is null) return;
        try { handle.Dispose(); }
        catch (Exception error) { Log("RESULT RELEASE FAILED", error.Message); }
    }

    private void ToggleFavorite()
    {
        if (_selection is null || !_selection.IsCurrent) { SetNotice("Select a current song first."); return; }
        if (!_preferencesLoaded) { SetNotice("Preferences unavailable: " + Limit(_storageFailure, 150)); return; }
        var id = _selection.Snapshot.Song.Song; var items = _favorites.ToHashSet();
        if (!items.Remove(id))
        {
            if (items.Count >= MaximumFavorites) { SetNotice($"Favorite limit {MaximumFavorites} reached."); return; }
            items.Add(id);
        }
        SavePreferences(_preferences with { Favorites = items.OrderBy(x => x.Namespace, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray() });
        Render();
    }

    private void ClearSelection()
    {
        _restorePending = false; ReleaseSelection("clear"); SetNotice("Selection released.");
        if (_preferencesLoaded) SavePreferences(_preferences with { LastSong = null, LastChart = null });
        Render();
    }

    private void EnsurePreferences(bool retry = false)
    {
        if (_preferencesLoaded || (_preferencesAttempted && !retry)) return;
        var storage = _context.GetService<IPluginStorageService>(StorageCapabilityIds.Storage);
        if (!storage.Available || storage.Service is null) { _storageFailure = storage.Error?.Message; return; }
        _preferencesAttempted = true;
        var loaded = storage.Service.LoadConfiguration(PreferencesName, 1, () => new Preferences(), validate: ValidatePreferences);
        if (!loaded.Success || loaded.Value is null) { _storageFailure = Describe(loaded.Error); Log("PREFERENCES REFUSED", loaded.Error); return; }
        _preferences = loaded.Value.Value; _preferencesLoaded = true; _storageFailure = null;
        _favorites.Clear(); foreach (var id in _preferences.Favorites) _favorites.Add(id);
        Log("PREFERENCES LOADED", new { _preferences.LastSong, _preferences.LastChart, favorites = _favorites.Count });
    }

    private void SavePreferences(Preferences value)
    {
        var storage = _context.GetService<IPluginStorageService>(StorageCapabilityIds.Storage);
        if (!storage.Available || storage.Service is null) { SetNotice("Preferences unavailable: " + Limit(storage.Error?.Message, 160)); return; }
        var saved = storage.Service.SaveConfiguration(PreferencesName, 1, value, ValidatePreferences);
        if (!saved.Success) { SetNotice(Describe(saved.Error)); Log("PREFERENCES SAVE REFUSED", saved.Error); return; }
        _preferences = value; _favorites.Clear(); foreach (var id in value.Favorites) _favorites.Add(id);
        Log("PREFERENCES SAVED", new { value.LastSong, value.LastChart, favorites = _favorites.Count });
    }

    private static string? ValidatePreferences(Preferences value)
    {
        if (value is null || value.Favorites is null || value.Favorites.Length > MaximumFavorites ||
            value.Favorites.Any(x => !ValidId(x)) || value.Favorites.Distinct().Count() != value.Favorites.Length ||
            (value.LastSong is not null && !ValidId(value.LastSong)) ||
            (value.LastChart is not null && !ValidPart(value.LastChart)) || (value.LastSong is null && value.LastChart is not null))
            return "LOCAL preferences require bounded unique stable content IDs; the original configuration is preserved.";
        return null;
    }
    private static bool ValidId(LocalContentId? id) => id is not null && ValidPart(id.Namespace) && ValidPart(id.Id);
    private static bool ValidPart(string? value) => value is { Length: > 0 and <= 128 } && value.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-');

    private void Render()
    {
        if (_window?.IsAlive != true) return;
        Text(_status, _catalogStatus);
        var visible = Visible(); _page = Math.Clamp(_page, 0, Math.Max(0, (visible.Length - 1) / _pageSize));
        var selected = _selection?.Snapshot;
        for (var i = 0; i < _rows.Count; i++)
        {
            var index = _page * _pageSize + i;
            Text(_rows[i], index >= visible.Length ? "-" :
                (selected?.Song.Song == visible[index].Song ? "> " : "") + (_favorites.Contains(visible[index].Song) ? "[*] " : "") +
                Limit(visible[index].Title, 44) + " / " + Limit(visible[index].Artist, 24));
        }
        Text(_filter, _favoritesOnly ? "Favorites only" : "All songs");
        Text(_pageLabel, visible.Length == 0 ? (_favoritesOnly ? "No published favorites." : "No complete LOCAL songs are registered.") : $"Page {_page + 1}/{PageCount()} | {visible.Length} song(s)");
        Text(_details, selected is null ? "No selection. LOCAL IDs are independent of Steam Workshop IDs." :
            $"{Limit(selected.Song.Title, 40)} / {Limit(selected.Song.Artist, 20)}\n" +
            $"{Limit(selected.Song.Song.Namespace, 24)}:{Limit(selected.Song.Song.Id, 28)} | revision {selected.Song.Revision}\n" +
            $"Audio {selected.Song.Audio.Format} v{selected.Song.Audio.FormatVersion}; cover {selected.Song.Cover.Format} v{selected.Song.Cover.FormatVersion}");
        Text(_chart, selected is null ? "Chart: none" : "Chart: " + Limit(selected.Chart.Name, 36));
        Text(_favorite, selected is not null && _favorites.Contains(selected.Song.Song) ? "Remove favorite" : "Add favorite");
        var playLabel = selected is null ? "Select a chart" : !selected.Chart.HasPlaybackContent ? "Browse only"
            : _windowMount == UiMountIds.Overlay ? "Request play" : "Play in song list";
        Text(_play, playLabel);
        Text(_noticeLabel, _notice);
        Text(_resultLabel, _resultText);
    }

    private void SetNotice(string value) { _notice = Limit(value, 160); Text(_noticeLabel, _notice); }
    private static void Text(IUiControlHandle? control, string value) { if (control is not null) control.SetText(value); }
    private void ReleaseSelection(string reason)
    {
        var selection = _selection; _selection = null;
        if (selection is null) return;
        try { var generation = selection.Snapshot.Generation; selection.Dispose(); Log("SELECTION RELEASED", new { reason, generation }); }
        catch (Exception error) { Log("SELECTION RELEASE FAILED", new { reason, error = error.Message }); }
    }
    private void SceneChanged(GameSceneChanged change)
    {
        // Native scene detection may precede this sampled public event. A window
        // created in that gap can still be live; retire it before forgetting its ID.
        CloseWindow();
        _sceneGeneration = change.SceneGeneration;
        _nextPoll = 0;
        if (CatalogMount(change.Scene) is null) LeaveCatalog();
    }
    private void LeaveCatalog()
    {
        if (_selection is null) return;
        var snapshot = _selection.Snapshot;
        _restoreSong = snapshot.Song;
        _restorePending = _selection.IsCurrent && _preferences.LastSong == snapshot.Song.Song;
        ReleaseSelection("left-catalog");
    }
    private void CloseWindow()
    {
        var previous = _window;
        _window = null; _windowMount = null; _rows.Clear();
        _status = _pageLabel = _details = _chart = _favorite = _filter = _play = _noticeLabel = _resultLabel = null;
        try { previous?.Dispose(); } catch (Exception error) { Log("WINDOW RELEASE FAILED", error.Message); }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        var pending = _pendingResult;
        _pendingResult = null;
        ReleaseResult(pending?.Handle);
        ReleaseSelection("scope-ended");
        CloseWindow();
    }
    private void Log(string label, object? value)
    {
        try { _context.Log.Log(LogLevel.Info, "LocalCatalogProbe " + label + " " + Limit(JsonSerializer.Serialize(value), 1800)); } catch { }
    }
    private static string Describe(OperationError? error) => error is null ? "Operation unavailable." : error.KnownCode + ": " + Limit(error.Message, 190);
    private static string Limit(string? value, int length) => value is null ? "" : value.Length <= length ? value : value[..length] + "...";
    private static T Need<T>(Result<T> result) => result.Success && result.Value is not null ? result.Value : throw new InvalidOperationException(Describe(result.Error));
    private sealed record PendingResult(ILocalRunResultHandle Handle, LocalContentId Song, long SongRevision, string ChartId, string Title);
    private sealed record ProbeSettings(bool Enabled = false, bool PublishValidationFixture = false);
    private sealed record Preferences
    {
        public LocalContentId? LastSong { get; init; }
        public string? LastChart { get; init; }
        public LocalContentId[] Favorites { get; init; } = Array.Empty<LocalContentId>();
    }
}
