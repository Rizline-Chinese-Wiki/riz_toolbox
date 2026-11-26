using RizToolbox.Sdk;

namespace RizSongProject;

public sealed class Plugin : IRizPlugin
{
    private const string SongId = "tide-signal";
    private const string SongTitle = "Tide Signal";
    private const string Artist = "RizToolbox";
    private const string ChartId = "easy";

    public PluginManifest Manifest => new("wiki.song", new(0, 1, 0), new(1, 6, 0),
        MaxSdkExclusive: new(2, 0, 0), RequiredCapabilities: new[]
        {
            AudioContentCapabilityIds.LocalContentBatch,
            AudioContentCapabilityIds.LocalCatalog,
            AudioContentCapabilityIds.WorkshopChartReplacement
        });

    public void Enable(IPluginContext context)
    {
        context.MainThread.VerifyAccess();
        var batch = context.GetService<ILocalContentBatchProviderService>(AudioContentCapabilityIds.LocalContentBatch);
        if (!batch.Available || batch.Service is null)
            throw RegistrationError("Open LOCAL content service", batch.Error);
        var catalog = context.GetService<ILocalCatalogProviderService>(AudioContentCapabilityIds.LocalCatalog);
        if (!catalog.Available || catalog.Service is null)
            throw RegistrationError("Open LOCAL catalog service", catalog.Error);

        var audio = new LocalContentId(context.PluginId, "song-audio");
        var cover = new LocalContentId(context.PluginId, "song-cover");
        var chart = new LocalContentId(context.PluginId, "song-chart");
        var assets = batch.Service.RegisterBatch(new[]
        {
            new LocalContentRequest(audio.Id, LocalContentKind.Audio, "wav.pcm16", 1,
                new(ContentFileArea.Package, "assets/song.wav")),
            new LocalContentRequest(cover.Id, LocalContentKind.Cover, "png.rgba8", 1,
                new(ContentFileArea.Package, "assets/cover.png")),
            new LocalContentRequest(chart.Id, LocalContentKind.Chart, "rizline.chart.json", 0,
                new(ContentFileArea.Package, "assets/chart.json"),
                new LocalContentDependency[] { new(audio), new(cover) })
        });
        if (!assets.Success || assets.Value is null)
            throw RegistrationError("Register LOCAL assets", assets.Error);

        var difficulty = new LocalSongChartRequest(ChartId, "Easy", chart)
        {
            Playback = new LocalChartPlaybackMetadata(
                DifficultyIndex: 0, MaxHit: 1, MaxScore: 1_000_000,
                DifficultyText: "Easy", Designer: "RizToolbox",
                ThemeColor: new ResourceColor(.18f, .8f, .76f, 1f), Illustrator: "RizToolbox")
        };
        var song = catalog.Service.RegisterSong(
            new LocalSongRequest(SongId, SongTitle, Artist, audio, cover, new[] { difficulty }));
        if (!song.Success || song.Value is null)
            throw RegistrationError("Register LOCAL song", song.Error);
        if (!song.Value.Snapshot.Charts[0].HasPlaybackContent)
            throw new InvalidOperationException("The LOCAL chart needs playback metadata and at least one note on a playable line segment.");

        context.Log.Log(LogLevel.Info, $"Registered LOCAL song {context.PluginId}/{SongId} ({SongTitle}) with chart {ChartId}.");
    }

    // The activation scope owns both registrations and withdraws the song before the asset batch.
    public void Disable() { }

    private static InvalidOperationException RegistrationError(string operation, OperationError? error) =>
        new(error is null ? $"{operation} returned no result." : $"{operation} [{error.Code}] {error.Message}");
}
