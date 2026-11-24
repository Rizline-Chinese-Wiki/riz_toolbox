using System.Globalization;
using System.Text;
using RizToolbox.Sdk;

namespace RizToolbox.BasicPlugins;

/// <summary>A bounded page of copied song/score values. Native catalog capture happens on refresh, never every frame.</summary>
public sealed class SongScoresPlugin : IRizPlugin
{
    private const int MaximumCombinedEntries = 20000;
    private PluginRuntime? _runtime;
    private ScopedWindow? _window;
    private IUiControlHandle? _rowsLabel, _statusLabel, _filterField;
    private SongScoresSettings _settings = new();
    private bool _configurationReady, _refreshRequested;
    private double _nextWindowAttempt, _nextCaptureAttempt;
    private SongScoreRow[] _rows = Array.Empty<SongScoreRow>();
    private int[] _matches = Array.Empty<int>();
    private DateTimeOffset? _capturedAt;
    private string? _lastRows, _lastStatus;

    public PluginManifest Manifest => PluginManifests.SongScores;

    public void Enable(IPluginContext context)
    {
        Disable();
        var runtime = _runtime = new PluginRuntime(context);
        _settings = new();
        _configurationReady = false;
        _refreshRequested = true;
        _nextWindowAttempt = _nextCaptureAttempt = 0;
        runtime.Run("configuration-load", ReloadSettings);
        runtime.Start(Pulse, OnSceneChanged, Cleanup);
    }

    private void Pulse()
    {
        var runtime = _runtime!;
        if (!runtime.HasScene) return;
        if (_window is not null && !_window.IsAlive) ReleaseWindow();
        if (_window is null && runtime.Now >= _nextWindowAttempt)
        {
            _nextWindowAttempt = runtime.Now + 5;
            CreateWindow();
        }
        if (_refreshRequested && runtime.Now >= _nextCaptureAttempt)
        {
            _refreshRequested = false;
            _nextCaptureAttempt = runtime.Now + 5;
            Capture();
        }
        Render();
    }

    private void CreateWindow()
    {
        var runtime = _runtime!;
        var window = ScopedWindow.Create(runtime, "song-scores", "Songs and best scores", new(20, 90, 735, 730));
        if (window is null) return;
        try
        {
            var ui = window.Handle;
            _rowsLabel = Label(ui, "rows", "Waiting for a current snapshot.", new(15, 50, 705, 410));
            _statusLabel = Label(ui, "status", "", new(15, 465, 705, 115));
            _filterField = PluginRuntime.Need(ui.AddTextField(new("filter", _settings.Filter, new(15, 590, 400, 42)),
                characterLimit: 128), UiInputCapabilityIds.Windows, "filter");
            Button(ui, "apply-filter", "Apply filter", new(430, 590, 140, 42), ApplyFilter);
            Button(ui, "reload-settings", "Reload settings", new(585, 590, 135, 42), ReloadSettingsAndWindow);
            Button(ui, "previous", "Previous", new(15, 650, 155, 44), () => ChangePage(-1));
            Button(ui, "next", "Next", new(185, 650, 155, 44), () => ChangePage(1));
            Button(ui, "refresh", "Refresh scores", new(355, 650, 200, 44), () => _refreshRequested = true);
            _window = window;
            _lastRows = _lastStatus = null;
            runtime.Clear("window-controls");
        }
        catch (Exception error)
        {
            runtime.Fail("window-controls", PluginRuntime.Error(error, UiInputCapabilityIds.Windows));
            runtime.Release(window, "window-release");
            _rowsLabel = _statusLabel = _filterField = null;
        }
    }

    private IUiControlHandle Label(IUiWindowHandle window, string id, string text, UiRect rectangle)
        => PluginRuntime.Need(window.AddLabel(new(id, text, rectangle)), UiInputCapabilityIds.Windows, id);

    private void Button(IUiWindowHandle window, string id, string text, UiRect rectangle, Action action)
        => PluginRuntime.Need(window.AddButton(new(id, text, rectangle),
            () => _runtime?.Run("button-" + id, action)), UiInputCapabilityIds.Windows, id);

    private void Capture()
    {
        var runtime = _runtime!;
        var catalog = runtime.Service<ICatalogService>(runtime.Context, CapabilityIds.Catalog, "catalog-service");
        var scores = runtime.Service<IScoreService>(runtime.Context, CapabilityIds.Scores, "scores-service");
        InvalidateSnapshot();
        if (catalog is null || scores is null) { _refreshRequested = true; return; }
        var chartsResult = catalog.GetCharts();
        if (!chartsResult.Success || chartsResult.Value is null)
        {
            runtime.Fail("snapshot", chartsResult.Error, CapabilityIds.Catalog);
            RetryExpiredSnapshot(chartsResult.Error);
            return;
        }
        var scoresResult = scores.GetBestScoresSnapshot();
        if (!scoresResult.Success || scoresResult.Value is null)
        {
            runtime.Fail("snapshot", scoresResult.Error, CapabilityIds.Scores);
            RetryExpiredSnapshot(scoresResult.Error);
            return;
        }
        if ((long)chartsResult.Value.Count + scoresResult.Value.Count > MaximumCombinedEntries)
        {
            runtime.Fail("snapshot", new(ErrorCode.QuotaExceeded,
                "Catalog and score snapshot exceeds the 20000-entry display cache limit."));
            return;
        }
        var chartIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chart in chartsResult.Value)
        {
            if (chart is null || string.IsNullOrWhiteSpace(chart.Id) || chart.Id.Length > 512 ||
                !chartIds.Add(chart.Id) || chart.Title is null || chart.SongId is null ||
                chart.DifficultyName is null || chart.Rating is double rating && !double.IsFinite(rating))
            {
                runtime.Fail("snapshot", new(ErrorCode.ContentInvalid, "The catalog contains an invalid or duplicate chart."));
                return;
            }
        }
        var best = new Dictionary<string, ScoreRecord>(StringComparer.Ordinal);
        foreach (var score in scoresResult.Value)
        {
            if (score is null || score.ChartId is null || !chartIds.Contains(score.ChartId) ||
                !double.IsFinite(score.RawCompletion) || !best.TryAdd(score.ChartId, score))
            {
                runtime.Fail("snapshot", new(ErrorCode.ContentInvalid, "Best scores contain an invalid, duplicate or unassociated chart."));
                return;
            }
        }
        _rows = chartsResult.Value.Select(chart => new SongScoreRow(chart.Id,
            PluginRuntime.Clip(chart.SongId, 128), PluginRuntime.Clip(chart.Title, 56),
            PluginRuntime.Clip(chart.DifficultyName, 12), chart.Rating,
            best.TryGetValue(chart.Id, out var score) ? score with { } : null))
            .OrderBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.SongId, StringComparer.Ordinal).ThenBy(row => row.Id, StringComparer.Ordinal).ToArray();
        _capturedAt = DateTimeOffset.UtcNow;
        Refilter();
        runtime.Clear("snapshot");
        runtime.Context.Log.Log(LogLevel.Info, $"Current song/score snapshot copied: charts={_rows.Length}, bestScores={best.Count}, " +
            $"scene={PluginRuntime.Clip(runtime.Scene, 128)}, generation={runtime.SceneGeneration}.");
    }

    private void RetryExpiredSnapshot(OperationError? error)
    {
        if (error?.Code is ErrorCode.TargetExpired or ErrorCode.ScopeExpired or ErrorCode.CapabilityUnavailable)
            _refreshRequested = true;
    }

    private void Refilter()
    {
        var filter = _settings.Filter;
        _matches = _rows.Select((row, index) => (row, index))
            .Where(item => filter.Length == 0 || item.row.Title.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                item.row.SongId.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                item.row.Id.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.index).ToArray();
        _lastRows = _lastStatus = null;
    }

    private int PageCount => Math.Max(1, (_matches.Length + _settings.RowsPerPage - 1) / _settings.RowsPerPage);
    private int EffectivePage => Math.Min(_settings.Page, PageCount - 1);

    private void ChangePage(int delta)
    {
        if (!_configurationReady) { ConfigurationUnavailable(); return; }
        var next = _settings with { Page = Math.Clamp(EffectivePage + delta, 0, PageCount - 1) };
        if (next.Page == _settings.Page) return;
        if (!_runtime!.Save("song-scores", next, SettingsValidation.SongScores)) return;
        _settings = next;
        _lastRows = _lastStatus = null;
    }

    private void ApplyFilter()
    {
        if (!_configurationReady) { ConfigurationUnavailable(); return; }
        if (_filterField is null) return;
        var text = _filterField.GetText();
        if (!text.Success || text.Value is null) { _runtime!.Fail("filter", text.Error, UiInputCapabilityIds.Windows); return; }
        var next = _settings with { Filter = text.Value.Trim(), Page = 0 };
        if (!_runtime!.Save("song-scores", next, SettingsValidation.SongScores)) return;
        _settings = next;
        _runtime.Clear("filter");
        Refilter();
    }

    private void ReloadSettings()
    {
        _configurationReady = _runtime!.Load("song-scores", () => new SongScoresSettings(),
            SettingsValidation.SongScores, out var settings);
        if (!_configurationReady || settings is null) return;
        _settings = settings;
        Refilter();
    }

    private void ReloadSettingsAndWindow()
    {
        ReloadSettings();
        if (_configurationReady) { ReleaseWindow(); _nextWindowAttempt = 0; }
    }

    private void ConfigurationUnavailable()
        => _runtime!.Fail("configuration", new(ErrorCode.ConfigInvalid,
            "Reload valid settings before saving a filter or page. The existing file is preserved."));

    private void Render()
    {
        if (_window?.IsAlive != true || _rowsLabel is null || _statusLabel is null) return;
        var runtime = _runtime!;
        var rows = new StringBuilder(1800);
        if (_capturedAt is null) rows.Append("Current song/score snapshot is unavailable. Use Refresh scores.");
        else if (_matches.Length == 0) rows.Append("No songs match the current filter.");
        else
        {
            var first = EffectivePage * _settings.RowsPerPage;
            var count = Math.Min(_settings.RowsPerPage, _matches.Length - first);
            for (int offset = 0; offset < count; offset++)
            {
                var row = _rows[_matches[first + offset]];
                rows.Append(row.Title).Append(" · ").Append(row.Difficulty);
                if (row.Rating is double rating) rows.Append(' ').Append(rating.ToString("0.##", CultureInfo.InvariantCulture));
                rows.Append('\n');
                if (row.Score is { } score)
                    rows.Append("Best ").Append(score.Score.ToString(CultureInfo.InvariantCulture))
                        .Append("  raw completion ").Append(score.RawCompletion.ToString("0.####", CultureInfo.InvariantCulture))
                        .Append(score.IsFullCombo ? "  FC" : score.IsClear ? "  Clear" : "");
                else rows.Append("No best-score record.");
                rows.Append('\n');
            }
        }
        var status = $"Page {EffectivePage + 1}/{PageCount} · {_matches.Length} matching charts · " +
            $"Scene {PluginRuntime.Clip(runtime.Scene, 48)}\n" +
            (_capturedAt is { } captured ? $"Snapshot {captured.ToLocalTime():HH:mm:ss}. Refresh after a new result." : "Waiting for a current snapshot.") +
            runtime.IssuesText();
        var rowsLabel = _rowsLabel;
        var statusLabel = _statusLabel;
        if (!SetLabel(rowsLabel, rows.ToString(), ref _lastRows)) return;
        SetLabel(statusLabel, status, ref _lastStatus);
    }

    private bool SetLabel(IUiControlHandle label, string text, ref string? previous)
    {
        if (text == previous) return true;
        var result = label.SetText(text);
        if (result.Success) { previous = text; _runtime!.Clear("panel-text"); return true; }
        else
        {
            _runtime!.Fail("panel-text", result.Error, UiInputCapabilityIds.Windows, label.Id);
            ReleaseWindow();
            _nextWindowAttempt = _runtime.Now + 5;
            return false;
        }
    }

    private void InvalidateSnapshot()
    {
        _rows = Array.Empty<SongScoreRow>();
        _matches = Array.Empty<int>();
        _capturedAt = null;
        _lastRows = _lastStatus = null;
    }

    private void OnSceneChanged()
    {
        ReleaseWindow();
        InvalidateSnapshot();
        _refreshRequested = true;
        _nextCaptureAttempt = _runtime!.Now + 1;
        _nextWindowAttempt = _runtime!.Now + 1;
    }

    private void ReleaseWindow()
    {
        _runtime?.Release(_window, "window-release");
        _window = null;
        _rowsLabel = _statusLabel = _filterField = null;
        _lastRows = _lastStatus = null;
    }

    private void Cleanup() { ReleaseWindow(); InvalidateSnapshot(); }

    public void Disable()
    {
        _runtime?.Dispose();
        _runtime = null;
    }

    private sealed record SongScoreRow(string Id, string SongId, string Title, string Difficulty, double? Rating, ScoreRecord? Score);
}
