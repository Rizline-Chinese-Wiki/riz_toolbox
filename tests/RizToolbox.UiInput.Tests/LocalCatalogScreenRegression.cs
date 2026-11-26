using System.Text;
using System.Text.Json.Nodes;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core.Storage;
using RizToolbox.Examples;
using RizToolbox.Sdk;

internal static partial class LocalCatalogScreenRegression
{
    internal static void Run()
    {
        var root = Directory.CreateTempSubdirectory("RizToolbox.CatalogScreen.");
        try
        {
            using var f = new Fixture(root.FullName);
            var context = f.Contexts["a"];
            var storage = new PluginStorageManager(root.FullName);
            using var content = new LocalContentService(f.Main, new(_ => null, storage.GetDataDirectory));
            content.SetChartParser(new Parser());
            var play = new Dispatch();
            using var catalog = new LocalCatalogService(f.Main, content, f.Log, play);
            f.Registry.RegisterScoped<IPluginStorageService>(StorageCapabilityIds.Storage, storage.ForPlugin);
            f.Registry.RegisterScoped<ILocalContentBatchProviderService>(AudioContentCapabilityIds.LocalContentBatch, content.BatchForPlugin);
            f.Registry.RegisterScoped<ILocalCatalogProviderService>(AudioContentCapabilityIds.LocalCatalog, catalog.ForPlugin);
            f.Registry.RegisterScoped<ILocalCatalogSelectionService>(AudioContentCapabilityIds.LocalCatalogSelection, catalog.SelectionForPlugin);
            Verify(context.Configuration.Save("local-catalog-proof", new { Enabled = true, PublishValidationFixture = true }).Success, "enable");
            LocalCatalogRuntimeProbe.Start(context);
            f.Service.Tick();
            f.Events.Publish(new FrameUpdate(1));
            var provider = context.GetService<ILocalCatalogProviderService>(AudioContentCapabilityIds.LocalCatalog).Service!;
            var selection = context.GetService<ILocalCatalogSelectionService>(AudioContentCapabilityIds.LocalCatalogSelection).Service!;
            var fixture = provider.Read().Value!.Songs.First();
            var files = context.GetService<IPluginStorageService>(StorageCapabilityIds.Storage).Service!;
            var json = JsonNode.Parse(files.Read(PluginStorageArea.Data, "catalog-validation/chart.json").Value!)!;
            json["lines"]![0]!["notes"] = new JsonArray();
            Verify(files.Write(PluginStorageArea.Data, "empty.json", Encoding.UTF8.GetBytes(json.ToJsonString())).Success, "write empty chart");
            using var empty = content.ForPlugin(context).Register(new("empty", LocalContentKind.Chart, "rizline.chart.json", 0,
                new(ContentFileArea.PluginData, "empty.json"))).Value!;
            var authored = new LocalSongChartRequest("play", "Playable", fixture.Charts[0].Content.Content)
                { Playback = new(0, 1, 100, "EZ", "Test", new(1, 1, 1)) };
            var song = new LocalSongRequest("authored", "Authored test song", "Test", fixture.Audio.Content, fixture.Cover.Content,
                new[] { authored, authored with { Id = "empty", Content = empty.Content.Content }, authored with { Id = "browse", Playback = null } });
            using var published = provider.RegisterSong(song).Value!;
            f.Events.Publish(new FrameUpdate(3));
            NativeControl Control(string id) => f.Backend.Last.Controls.Single(control => control.Id == id);
            void Click(string id)
            {
                var control = Control(id);
                control.UsesNativeActivation = true;
                control.QueueActivations();
                f.Service.Tick();
            }
            int generation = 1;
            void Change(string from, string to)
            {
                f.Backend.SceneIdentity = ++generation + ":" + to;
                f.Service.Tick();
                f.Events.Publish(new GameSceneChanged(from, to) { SceneGeneration = generation });
                f.Events.Publish(new FrameUpdate(3 + 2 * generation));
            }

            Verify(f.Backend.Last.MountId == UiMountIds.MainMenu, "browse on menu");
            Click("song-0");
            var chosen = selection.ReadCurrent().Value!;
            Verify(chosen.Song.Song == published.Snapshot.Song && chosen.Chart.Id == "play", "select authored chart");
            Verify(Control("play").Text == "Play in song list", "menu identifies where the play action is available");
            Click("play");
            Verify(play.Calls == 0 && Control("notice").Text.Contains("song list", StringComparison.OrdinalIgnoreCase), "guide to song list before dispatch");
            var oldMenuPlay = Control("play");
            Change("MainMenu", "LevelList");
            Verify(f.Backend.LiveWindows == 1 && f.Backend.Last.MountId == UiMountIds.Overlay, "open song-list overlay");
            Verify(selection.ReadCurrent().Value?.Generation == chosen.Generation, "keep exact selection across menus");
            Verify(Control("play").Text == "Request play", "action reflects selected chart");
            oldMenuPlay.QueueActivations();
            f.Service.Tick();
            Verify(play.Calls == 0, "retired menu control cannot dispatch");
            Click("play");
            Verify(play.Calls == 1 && play.Last == chosen && Control("notice").Text.Contains("NotReady: waiting for song card"), "preserve refusal");
            play.Accept = true;
            Click("play");
            Verify(play.Calls == 2 && Control("notice").Text == "Play request accepted.", "show actual receipt");
            Click("next-chart");
            Click("play");
            Verify(play.Calls == 2 && Control("notice").Text.Contains("notes", StringComparison.OrdinalIgnoreCase), "empty chart stays browse-only");
            Click("next-chart");
            Click("play");
            Verify(play.Calls == 2 && Control("notice").Text.Contains("metadata", StringComparison.OrdinalIgnoreCase), "metadata-free chart stays browse-only");
            Click("next-chart");
            published.Dispose();
            using var replacement = provider.RegisterSong(song).Value!;
            Click("play");
            Verify(play.Calls == 2, "withdrawn selection cannot launch same-ID replacement");
            Click("song-0");
            Verify(selection.ReadCurrent().Value is null && Control("notice").Text.Contains("TargetExpired"),
                "stale displayed row must not acquire a same-ID replacement");
            f.Events.Publish(new FrameUpdate(8));
            Verify(selection.ReadCurrent().Value is null, "replacement requires explicit selection");
            Click("song-0");
            Verify(selection.ReadCurrent().Value!.Song.Revision == replacement.Snapshot.Revision, "select replacement");
            var current = selection.ReadCurrent().Value!;
            var rejected = selection.SelectRevision(published.Snapshot, "play");
            Verify(rejected.Error?.KnownCode == ErrorCode.TargetExpired && selection.ReadCurrent().Value == current,
                "stale revision does not displace a valid current selection");
            Verify(selection.SelectRevision(replacement.Snapshot with { }, "play").Error?.KnownCode == ErrorCode.TargetExpired,
                "an equal record cannot stand in for the catalog publication");
            var oldListPlay = Control("play");
            oldListPlay.UsesNativeActivation = true;
            oldListPlay.QueueActivations();
            Change("LevelList", "Level");
            Verify(f.Backend.LiveWindows == 0 && selection.ReadCurrent().Value is null && play.Calls == 2, "playing retires window, input and selection");
            Change("Level", "Result");
            Verify(f.Backend.LiveWindows == 0, "no results overlay");
            Change("Result", "FakeMainMenu");
            Verify(f.Backend.LiveWindows == 1 && f.Backend.Last.MountId == UiMountIds.MainMenu, "return menu uses actual Canvas");
            Verify(selection.ReadCurrent().Value?.Song == current.Song, "return restores the same publication");
            Change("FakeMainMenu", "Level");
            replacement.Dispose();
            using var newer = provider.RegisterSong(song).Value!;
            Change("Level", "LevelList");
            Verify(selection.ReadCurrent().Value is null && Control("notice").Text.Contains("replaced"),
                "return requires a new choice if the song was republished during play");
            Click("song-0");
            Verify(selection.ReadCurrent().Value?.Song == newer.Snapshot, "fresh choice on returning directly to the song list");
        }
        finally { root.Delete(true); }
    }

    internal static void RunStandardBrowser()
    {
        var root = Directory.CreateTempSubdirectory("RizToolbox.StandardCatalog.");
        try
        {
            using var f = new Fixture(root.FullName);
            var assetRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "LocalSong");
            using var content = new LocalContentService(f.Main, new(owner => owner == "a" ? assetRoot : null, _ => null));
            content.SetChartParser(new Parser());
            var play = new Dispatch { Accept = true };
            using var catalog = new LocalCatalogService(f.Main, content, f.Log, play);
            f.Registry.RegisterScoped<ILocalCatalogProviderService>(AudioContentCapabilityIds.LocalCatalog, catalog.ForPlugin);
            f.Registry.RegisterScoped<ILocalCatalogSelectionService>(AudioContentCapabilityIds.LocalCatalogSelection, catalog.SelectionForPlugin);
            var batchLookups = 0;
            f.Registry.RegisterScoped<ILocalContentBatchProviderService>(AudioContentCapabilityIds.LocalContentBatch, context =>
            {
                batchLookups++;
                return content.BatchForPlugin(context);
            });

            var provider = f.Contexts["a"];
            using var assets = content.BatchForPlugin(provider).RegisterBatch(new[]
            {
                new LocalContentRequest("audio", LocalContentKind.Audio, "wav.pcm16", 1, new(ContentFileArea.Package, "song.wav")),
                new LocalContentRequest("cover", LocalContentKind.Cover, "png.rgba8", 1, new(ContentFileArea.Package, "cover.png")),
                new LocalContentRequest("chart", LocalContentKind.Chart, "rizline.chart.json", 0, new(ContentFileArea.Package, "chart.json"))
            }).Value!;
            Verify(assets.IsPublished, "register the included template assets under a separate provider");
            var difficulty = new LocalSongChartRequest("easy", "Playable", new(provider.PluginId, "chart"))
                { Playback = new(0, 1, 100, "EZ", "Test", new(1, 1, 1)) };
            using var song = catalog.ForPlugin(provider).RegisterSong(new("author-song", "Independent author song", "Fixture composer",
                new(provider.PluginId, "audio"), new(provider.PluginId, "cover"), new[] { difficulty })).Value!;
            Verify(song.Snapshot.Charts.Single().HasPlaybackContent, "template chart can be requested");

            var browser = new LocalCatalogBrowserPlugin();
            var id = browser.Manifest.Id;
            var proofFile = Path.Combine(root.FullName, "config", "plugin-" + id, "config-local-catalog-proof.json");
            Directory.CreateDirectory(Path.GetDirectoryName(proofFile)!);
            File.WriteAllText(proofFile, "intentionally invalid proof configuration");
            Verify(f.Host.Register(browser).Success && f.Host.Enable(id).Success, "start the ordinary plugin");
            Verify(f.Host.GetRequiredCapabilityDemand().ToHashSet().SetEquals(new[]
            {
                AudioContentCapabilityIds.LocalCatalog, AudioContentCapabilityIds.LocalCatalogSelection, UiInputCapabilityIds.Windows
            }), "declare demand for the browser's services");

            f.Backend.SceneIdentity = "2:LevelList";
            f.Service.Tick();
            f.Events.Publish(new FrameUpdate(1));
            Verify(f.Backend.LiveWindows == 1 && f.Backend.Last.MountId == UiMountIds.Overlay, "open without valid proof configuration");
            NativeControl Control(string name) => f.Backend.Last.Controls.Single(control => control.Id == name);
            void Click(string name)
            {
                var control = Control(name);
                control.UsesNativeActivation = true;
                control.QueueActivations();
                f.Service.Tick();
            }
            Verify(batchLookups == 0 && Control("song-0").Text == "Independent author song / Fixture composer",
                "render the existing song without starting a validation provider");
            Click("song-0");
            f.Events.Publish(new FrameUpdate(3));
            Verify(play.Calls == 0 && Control("play").Text == "Request play", "selection never starts a run");
            Click("play");
            Verify(play.Calls == 1 && play.Last is { } launch && ReferenceEquals(launch.Song, song.Snapshot) && launch.Chart.Id == "easy",
                "dispatch the exact selection after the player's click");
            Verify(f.Host.Disable(id).Success && f.Backend.LiveWindows == 0 && song.IsPublished,
                "retire the browser window and preserve content owned by the other plugin");
        }
        finally { root.Delete(true); }
    }


    private static void Verify(bool condition, string step)
    {
        if (!condition) throw new InvalidOperationException("LOCAL catalog: " + step);
    }
    private sealed class Parser : ILocalChartParser
    {
        public IParsedLocalChart Parse(byte[] bytes, CancellationToken token) => new Parsed(ChartJsonValidator.Validate(bytes, token));
    }
    private sealed record Parsed(ValidatedChartJson Source) : IParsedLocalChart { public void Dispose() { } }
    private sealed class Dispatch : ILocalRunPlayDispatcher
    {
        internal int Calls;
        internal bool Accept;
        internal LocalSongSelectionSnapshot? Last;
        public Result RequestPlay(LocalPlayRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            Last = request.Selection;
            LocalChartPlaybackMetadataValidation.Require(request);
            return Accept ? Result.Ok() : Result.Fail(new(ErrorCode.NotReady, "waiting for song card"));
        }
    }
}
