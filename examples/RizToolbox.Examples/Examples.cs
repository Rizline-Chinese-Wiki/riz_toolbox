using System.Text.Json;
using RizToolbox.Sdk;

namespace RizToolbox.Examples;

public sealed class StatusPanelPlugin : IRizPlugin
{
    private IPluginScope? _moduleProof;
    public PluginManifest Manifest => ExampleManifest.Create("example.panel", CapabilityIds.Ui);

    public void Enable(IPluginContext context)
    {
        ObservationRuntimeProbe.Start(context, color: true);
        _moduleProof = ModuleRuntimeProbe.Start(context);
        LocalCatalogRuntimeProbe.Start(context);
        var scope = ((IScopedPluginContext)context).CreateScope("status-panel");
        scope.Own(scope.Lifetime.Register(() => scope.Log.Log(LogLevel.Info, "Panel child scope cancelled before resource release.")));
        context = scope;
        context.Log.Log(LogLevel.Info, $"Created child ownership scope '{scope.ScopeId}'.");
        var configuration = context.Configuration.Load("panel", () => new PanelSettings("RizToolbox 0.3", true));
        if (!configuration.Success || configuration.Value is null) throw new InvalidOperationException(configuration.Error?.Message ?? "Panel configuration is unavailable.");
        var settings = configuration.Value;
        var save = context.Configuration.Save("panel", settings);
        if (!save.Success) throw new InvalidOperationException(save.Error?.Message);
        var ui = context.GetService<IUiService>(CapabilityIds.Ui).Service
            ?? throw new InvalidOperationException("UI service is unavailable.");
        var created = ui.CreatePanel(new("status", settings.Title, Text(context, settings, 0)));
        if (!created.Success || created.Value is null) throw new InvalidOperationException(created.Error?.Message);
        var panel = created.Value;
        // The scoped UI service owns this panel even though this plugin never calls Own(panel).
        double lastUpdate = -1;
        context.Events.Subscribe<FrameUpdate>(frame =>
        {
            if (frame.ElapsedSeconds - lastUpdate < 1) return;
            lastUpdate = frame.ElapsedSeconds;
            var changed = panel.SetText(Text(context, settings, frame.ElapsedSeconds));
            if (!changed.Success) throw new InvalidOperationException(changed.Error?.Message);
        });
        context.Log.Log(LogLevel.Info, $"Created automatically owned panel '{panel.Id}'.");
    }

    private static string Text(IPluginContext context, PanelSettings settings, double elapsed)
        => $"SDK {SdkInfo.Version}\nGame {context.GameBuild.GameVersion}\nPlugin {context.PluginId}" +
            (settings.ShowElapsedTime ? $"\nHost elapsed {elapsed:0}s" : "");

    private sealed record PanelSettings(string Title, bool ShowElapsedTime);
    public void Disable() => Interlocked.Exchange(ref _moduleProof, null)?.Dispose();
}

public sealed class EventObserverPlugin : IRizPlugin
{
    public PluginManifest Manifest => ExampleManifest.Create("example.events", CapabilityIds.Extensions, GameCapabilityIds.Scenes)
        with { OptionalDependencies = new[] { new PluginDependency("example.scores", new(0, 3, 0), new(1, 0, 0)) } };

    public void Enable(IPluginContext context)
    {
        var service = context.GetService<IExtensionService>(CapabilityIds.Extensions).Service
            ?? throw new InvalidOperationException("Extension service is unavailable.");
        FoundationRuntimeProbe.Start(context, provider: false);
        ObservationRuntimeProbe.Start(context);
        Observe(GameExtensionPoints.SceneChanged);
        Observe(GameExtensionPoints.RunStarted);
        Observe(GameExtensionPoints.RunFinished);
        var state = context.GetService<IGameStateService>(GameCapabilityIds.Scenes);
        if (state.Available)
        {
            var snapshot = state.Service!.GetSnapshot();
            if (snapshot.Success) context.Log.Log(LogLevel.Info, "Current game state: " + JsonSerializer.Serialize(snapshot.Value));
        }

        void Observe<T>(ExtensionPoint<T> point)
        {
            var descriptor = service.Describe(point.Id);
            if (!descriptor.Success)
            {
                context.Log.Log(LogLevel.Warning, $"Observation '{point.Id}' is unavailable: {descriptor.Error}");
                return;
            }
            // Payloads originate from the game adapter. The host automatically owns these subscriptions.
            var subscription = service.Subscribe(point, payload =>
                context.Log.Log(LogLevel.Info, $"Observed {point.Id}: {JsonSerializer.Serialize(payload)}"));
            context.Log.Log(subscription.Success ? LogLevel.Info : LogLevel.Warning,
                subscription.Success ? $"Subscribed to {point.Id} v{point.Version}." : $"Cannot observe {point.Id}: {subscription.Error}");
        }
    }

    public void Disable() { }
}

public sealed class LowColorLayerPlugin : IRizPlugin
{
    public PluginManifest Manifest => ExampleManifest.Create("example.color-low", GameCapabilityIds.Resources);
    public void Enable(IPluginContext context) => ExampleManifest.ApplyColor(context, new(0.15f, 0.95f, 1f), 10);
    public void Disable() { }
}

public sealed class HighColorLayerPlugin : IRizPlugin
{
    public PluginManifest Manifest => ExampleManifest.Create("example.color-high", GameCapabilityIds.Resources);
    public void Enable(IPluginContext context) => ExampleManifest.ApplyColor(context, new(1f, 0.25f, 0.7f), 20);
    public void Disable() { }
}

public sealed class ScoreExportPlugin : IRizPlugin
{
    private CancellationTokenSource? _stop;
    public PluginManifest Manifest => ExampleManifest.Create("example.scores", CapabilityIds.Catalog, CapabilityIds.Scores);

    public void Enable(IPluginContext context)
    {
        FoundationRuntimeProbe.Start(context, provider: true);
        ObservationRuntimeProbe.Start(context);
        var catalog = context.GetService<ICatalogService>(CapabilityIds.Catalog).Service
            ?? throw new InvalidOperationException("Chart catalog is unavailable.");
        var scoreService = context.GetService<IScoreService>(CapabilityIds.Scores).Service
            ?? throw new InvalidOperationException("Score service is unavailable.");
        var charts = catalog.GetCharts();
        var scores = scoreService.GetBestScoresSnapshot();
        if (!charts.Success || !scores.Success || charts.Value is null || scores.Value is null)
            throw new InvalidOperationException(charts.Error?.Message ?? scores.Error?.Message ?? "Snapshot is unavailable.");
        // Copy the game data before starting background IO. The worker only sees SDK value objects.
        var chartSnapshot = charts.Value.ToArray();
        var scoreSnapshot = scores.Value.ToArray();
        var snapshot = new { schemaVersion = 1, gameBuild = context.GameBuild, capturedAt = DateTimeOffset.UtcNow, charts = chartSnapshot, scores = scoreSnapshot };
        var exports = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "BepInEx", "RizToolbox", "exports");
        _stop = context.Own(CancellationTokenSource.CreateLinkedTokenSource(((IScopedPluginContext)context).Lifetime));
        var token = _stop.Token;
        _ = Task.Run(async () =>
        {
            string? incompleteFile = null;
            try
            {
                token.ThrowIfCancellationRequested();
                byte[] json = JsonSerializer.SerializeToUtf8Bytes(snapshot, new JsonSerializerOptions { WriteIndented = true });
                token.ThrowIfCancellationRequested();
                Directory.CreateDirectory(exports);
                var file = Path.Combine(exports, $"scores-{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}-{Guid.NewGuid():N}.json");
                await using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, useAsync: true))
                {
                    incompleteFile = file;
                    await stream.WriteAsync(json, token);
                    await stream.FlushAsync(token);
                }
                incompleteFile = null;
                context.Log.Log(LogLevel.Info, $"Exported {chartSnapshot.Length} charts / {scoreSnapshot.Length} score records to '{file}'.");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { context.Log.Log(LogLevel.Error, "Snapshot export failed.", ex); }
            finally
            {
                if (incompleteFile is not null)
                {
                    try { File.Delete(incompleteFile); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }, token);
    }

    public void Disable()
    {
        var stop = Interlocked.Exchange(ref _stop, null);
        try { stop?.Cancel(); }
        catch (ObjectDisposedException) { }
    }
}

internal static class ExampleManifest
{
    public static PluginManifest Create(string id, params string[] capabilities)
        => new(id, new(0, 3, 0), new(1, 3, 0), new(2, 0, 0), RequiredCapabilities: capabilities);

    public static void ApplyColor(IPluginContext context, ResourceColor color, int priority)
    {
        TmpColorRuntimeProbe.Start(context, color, priority);
        var resources = context.GetService<IGameResourceService>(GameCapabilityIds.Resources).Service
            ?? throw new InvalidOperationException("Real game resource service is unavailable.");
        var targets = resources.GetTargets();
        if (!targets.Success || targets.Value is null) throw new InvalidOperationException(targets.Error?.Message);
        // Demonstrate layering on the game's edge cover, leaving input blockers and full-screen overlays alone.
        var target = targets.Value.Where(item => item.Description.Contains("EdgeCoverCanvas/", StringComparison.Ordinal))
            .OrderBy(item => item.Key, StringComparer.Ordinal).FirstOrDefault()
            ?? throw new InvalidOperationException("The game's edge-cover color target is not currently available.");
        var applied = resources.OverrideColor(new(target.Key, color, priority));
        if (!applied.Success || applied.Value is null) throw new InvalidOperationException(applied.Error?.Message);
        // The scoped resource service owns this layer. No Own(handle) call is needed for disable/restore.
        context.Log.Log(LogLevel.Info, $"Applied real game color target '{applied.Value.ResourceKey}', priority {priority}, RGBA {color.R},{color.G},{color.B},{color.A}.");
    }
}
