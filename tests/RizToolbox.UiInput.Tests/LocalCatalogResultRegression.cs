using System.Globalization;
using System.Text.RegularExpressions;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Examples;
using RizToolbox.Sdk;

internal static partial class LocalCatalogScreenRegression
{
    internal static void RunStandardBrowserResultLifecycle() => WithResultBrowser(browser =>
    {
        using var original = browser.Publish("harbor", "Harbor Signal", "easy", "Original Easy");
        using var other = browser.Publish("ridge", "Ridge Echo", "hard", "Ridge Hard");
        browser.Visit("LevelList");
        browser.Click("song-0");
        Verify(browser.Play.Attempts == 0, "selecting an author song does not request a result");

        browser.Click("play");
        var observation = browser.Play.Observation;
        Verify(observation.Read().Error is { Code: ErrorCode.NotReady, Retryable: true }, "accepted run has a pending result handle");
        var waiting = browser.ResultText;
        Verify(waiting.Contains("Waiting", StringComparison.OrdinalIgnoreCase), "show the accepted run's pending result");

        browser.PublishUnrelatedResult();
        browser.Poll();
        Verify(browser.ResultText == waiting, "an official result cannot complete a pending LOCAL request");
        browser.Play.Refuse = true;
        browser.Click("play");
        Verify(browser.ResultText == waiting && browser.Control("notice").Text.Contains("waiting for song card", StringComparison.Ordinal)
            && observation.Read().Error?.Code == ErrorCode.NotReady, "a refused request preserves the previous pending handle");
        browser.Play.Refuse = false;

        browser.Visit("Level");
        Verify(browser.LiveWindows == 0, "playing retires the catalog window");
        original.Dispose();
        using var replacement = browser.Publish("harbor", "Harbor Remix", "easy", "Remix Easy");
        Verify(replacement.Snapshot.Revision != original.Snapshot.Revision, "publish a new revision of the launched song");
        browser.Visit("Result");
        Verify(browser.LiveWindows == 0, "leave the game's result scene to the host");

        var captured = new RunFinished(null, null, 681, 0.625, 13, 17, 19, 23, false, false)
            { EndReason = RunEndReason.Completed };
        browser.Play.Capture(captured);
        browser.Play.Retire();
        var receipt = observation.Read().Value;
        Verify(receipt is not null && receipt.Song == original.Snapshot.Song
            && receipt.SongRevision == original.Snapshot.Revision && receipt.ChartId == "easy",
            "the actual result belongs to the requested song revision after retirement");

        browser.Visit("MainMenu");
        var result = browser.ResultText;
        Verify(result.Contains("Harbor Signal", StringComparison.Ordinal) && result.Contains("Original Easy", StringComparison.Ordinal)
            && !result.Contains("Harbor Remix", StringComparison.Ordinal), "render the launched song and chart after returning to the menu");
        VerifyResultFields(result, captured);
        Verify(observation.Read().Error?.Code == ErrorCode.ScopeExpired, "release the observed handle after copying its result");
        Verify(browser.Play.OpenSessions == 0 && browser.PreparedRequests == 0, "retired capture leaves no host play resources");

        browser.Click("song-0");
        Verify(browser.Control("selection").Text.Contains("Harbor Remix", StringComparison.Ordinal)
            && browser.ResultText == result, "selecting the replacement revision leaves the result of the original revision");
        browser.Click("song-1");
        Verify(browser.Control("selection").Text.Contains("Ridge Echo", StringComparison.Ordinal)
            && browser.ResultText == result, "selecting another song cannot relabel the prior result");

        browser.Visit("LevelList");
        browser.Play.Refuse = true;
        browser.Click("play");
        browser.Poll();
        Verify(browser.ResultText == result && browser.Control("notice").Text.Contains("waiting for song card", StringComparison.Ordinal),
            "a refused launch leaves the previous result visible next to the new selection");
        browser.Play.Refuse = false;
        browser.Click("play");
        Verify(browser.Play.Observation.Read().Error?.Code == ErrorCode.NotReady && browser.ResultText != result,
            "a new accepted launch replaces the previous result with its own pending observation");
        var next = new RunFinished(null, null, 712, 0.5, 31, 37, 41, 43, true, false)
            { EndReason = RunEndReason.Failed };
        browser.Play.Capture(next);
        browser.Play.Retire();
        browser.Poll();
        Verify(browser.ResultText.Contains("Ridge Echo", StringComparison.Ordinal) && browser.ResultText.Contains("Ridge Hard", StringComparison.Ordinal)
            && !browser.ResultText.Contains("Harbor Signal", StringComparison.Ordinal),
            "render the second capture under the second launch's own song and chart");
        VerifyResultFields(browser.ResultText, next);
    });

    internal static void RunStandardBrowserResultCleanup() => WithResultBrowser(browser =>
    {
        using var song = browser.Publish("harbor", "Harbor Signal", "easy", "Original Easy");
        browser.Visit("LevelList");
        browser.Click("song-0");
        browser.Click("play");
        var closed = browser.Play.Observation;
        browser.Play.Retire();
        browser.Poll();
        Verify(browser.ResultText.Contains(nameof(ErrorCode.TargetExpired), StringComparison.Ordinal)
            && closed.Read().Error?.Code == ErrorCode.ScopeExpired, "show a completed observation error and release the handle");

        const int launches = LocalRunSessionRegistry.MaximumProcessSessions + 4;
        LocalRunResultObservation? previous = null;
        for (var index = 0; index < launches; index++)
        {
            browser.Click("play");
            Verify(browser.Play.Accepted == index + 2 && browser.Play.OpenSessions == 1,
                "accept each explicit replay without exhausting the result-handle quota");
            if (previous is not null)
                Verify(previous.Read().Error?.Code == ErrorCode.ScopeExpired, "replacing an unpolled result releases the old observation");
            previous = browser.Play.Observation;
            if (index + 1 < launches) browser.Play.Retire();
        }

        var pending = browser.Play.Observation;
        Verify(pending.Read().Error?.Code == ErrorCode.NotReady && browser.PreparedRequests == 1,
            "the final accepted launch still owns its result observation and prepared request");
        browser.Disable();
        Verify(browser.LiveWindows == 0 && pending.Read().Error?.Code == ErrorCode.ScopeExpired && song.IsPublished,
            "disabling the browser releases its window and result while the separate author stays published");
        Verify(browser.Play.OpenSessions == 1 && browser.PreparedRequests == 1,
            "ending the browser's observation leaves the active host run retained");
        browser.Play.Retire();
        Verify(browser.Play.OpenSessions == 0 && browser.PreparedRequests == 0, "ending the host run releases its prepared request");
    });

    private static void VerifyResultFields(string text, RunFinished result)
    {
        void Field(string name, string value)
        {
            var pattern = $@"\b{Regex.Escape(name)}\b[\s:=]+{Regex.Escape(value)}(?![\w.])";
            Verify(Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant), $"display {name} from the result snapshot");
        }
        Field("Score", result.Score.ToString(CultureInfo.InvariantCulture));
        Field("Raw completion", result.RawCompletion.ToString("R", CultureInfo.InvariantCulture));
        Field("Perfect", result.Perfect.ToString(CultureInfo.InvariantCulture));
        Field("MaxPerfect", result.MaxPerfect.ToString(CultureInfo.InvariantCulture));
        Field("Bad", result.Bad.ToString(CultureInfo.InvariantCulture));
        Field("Miss", result.Miss.ToString(CultureInfo.InvariantCulture));
        Field("End reason", result.EndReason.ToString());
        Field("Crashed", result.Crashed.ToString());
        Field("Skipped", result.Skipped.ToString());
    }

    private static void WithResultBrowser(Action<ResultBrowser> test)
    {
        var root = Directory.CreateTempSubdirectory("RizToolbox.CatalogResult.");
        try
        {
            using var fixture = new Fixture(root.FullName);
            var provider = fixture.Contexts["a"];
            var assetRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "LocalSong");
            using var content = new LocalContentService(fixture.Main, new(owner => owner == provider.PluginId ? assetRoot : null, _ => null));
            content.SetChartParser(new Parser());
            using var play = new ResultDispatch(fixture.Main);
            using var catalog = new LocalCatalogService(fixture.Main, content, fixture.Log, play);
            fixture.Registry.RegisterScoped<ILocalCatalogProviderService>(AudioContentCapabilityIds.LocalCatalog, catalog.ForPlugin);
            fixture.Registry.RegisterScoped<ILocalCatalogSelectionService>(AudioContentCapabilityIds.LocalCatalogSelection, catalog.SelectionForPlugin);
            using var assets = content.BatchForPlugin(provider).RegisterBatch(new[]
            {
                new LocalContentRequest("audio", LocalContentKind.Audio, "wav.pcm16", 1, new(ContentFileArea.Package, "song.wav")),
                new LocalContentRequest("cover", LocalContentKind.Cover, "png.rgba8", 1, new(ContentFileArea.Package, "cover.png")),
                new LocalContentRequest("chart", LocalContentKind.Chart, "rizline.chart.json", 0, new(ContentFileArea.Package, "chart.json"))
            }).Value!;
            Verify(assets is { IsPublished: true }, "register author assets before launching the browser");
            var browser = new LocalCatalogBrowserPlugin();
            Verify(fixture.Host.Register(browser).Success && fixture.Host.Enable(browser.Manifest.Id).Success,
                "start the standalone LOCAL browser");
            test(new ResultBrowser(fixture, catalog, catalog.ForPlugin(provider), provider.PluginId, browser.Manifest.Id, play));
        }
        finally { root.Delete(true); }
    }

    private sealed class ResultBrowser
    {
        private readonly Fixture _fixture;
        private readonly LocalCatalogService _catalog;
        private readonly ILocalCatalogProviderService _provider;
        private readonly string _authorId, _browserId;
        private long _sceneGeneration = 1;
        private double _seconds;
        private string _scene = "MainMenu";

        internal ResultBrowser(Fixture fixture, LocalCatalogService catalog, ILocalCatalogProviderService provider,
            string authorId, string browserId, ResultDispatch play)
            => (_fixture, _catalog, _provider, _authorId, _browserId, Play) = (fixture, catalog, provider, authorId, browserId, play);

        internal ResultDispatch Play { get; }
        internal int PreparedRequests => _catalog.PreparedRequestCount;
        internal int LiveWindows => _fixture.Backend.LiveWindows;
        internal string ResultText => Control("result").Text;

        internal ILocalSongRegistration Publish(string id, string title, string chartId, string chartName)
        {
            var chart = new LocalSongChartRequest(chartId, chartName, new(_authorId, "chart"))
                { Playback = new(0, 1, 1000, chartName, "Test", new(1, 1, 1)) };
            var receipt = _provider.RegisterSong(new(id, title, "Fixture composer",
                new(_authorId, "audio"), new(_authorId, "cover"), new[] { chart }));
            return receipt.Value ?? throw new InvalidOperationException(receipt.Error?.Message);
        }

        internal NativeControl Control(string id) => _fixture.Backend.Last.Controls.Single(control => control.Id == id);

        internal void Click(string id)
        {
            var control = Control(id);
            control.UsesNativeActivation = true;
            control.QueueActivations();
            _fixture.Service.Tick();
        }

        internal void Poll() => _fixture.Events.Publish(new FrameUpdate(_seconds += 2));

        internal void Visit(string scene)
        {
            _fixture.Backend.SceneIdentity = $"{++_sceneGeneration}:{scene}";
            _fixture.Service.Tick();
            _fixture.Events.Publish(new GameSceneChanged(_scene, scene) { SceneGeneration = _sceneGeneration });
            _scene = scene;
            Poll();
        }

        internal void PublishUnrelatedResult() => _fixture.Events.Publish(
            new RunFinished("official-run", "official-chart", 999_999, 1, 99, 99, 0, 0, false, false)
                { AssociatedWithObservedStart = true, EndReason = RunEndReason.Completed });

        internal void Disable() => Verify(_fixture.Host.Disable(_browserId).Success, "disable the standalone browser");
    }

    private sealed class ResultDispatch : ILocalRunPlayDispatcher, IDisposable
    {
        private readonly LocalRunSessionRegistry _sessions;
        private LocalRunSessionRegistry.SessionHandle? _active;
        private LocalRunResultObservation? _observation;
        private long _metadataIdentity;

        internal ResultDispatch(IMainThreadDispatcher main) => _sessions = new(main);
        internal int Attempts { get; private set; }
        internal int Accepted { get; private set; }
        internal bool Refuse { get; set; }
        internal int OpenSessions => _sessions.UnretiredSessionCount;
        internal LocalRunResultObservation Observation => _observation
            ?? throw new InvalidOperationException("The browser did not request an observation of its accepted LOCAL run.");

        public Result RequestPlay(LocalPlayRequest request, CancellationToken cancellationToken)
        {
            Attempts++;
            cancellationToken.ThrowIfCancellationRequested();
            LocalChartPlaybackMetadataValidation.Require(request);
            if (Refuse) return Result.Fail(new(ErrorCode.NotReady, "waiting for song card", Retryable: true));
            Verify(_active is null, "finish the previously accepted fixture run before starting another");
            var selection = request.Selection;
            var retained = request.Retain();
            _metadataIdentity += 4;
            try
            {
                _active = _sessions.RegisterPrepared(new(selection.Song.Song, selection.Song.Revision, selection.Chart.Id),
                    _metadataIdentity, retained, request.ResultObservation);
            }
            catch { retained.Dispose(); throw; }
            _observation = request.ResultObservation;
            var commit = _sessions.BeginCommit(_active);
            _sessions.BindPendingController(commit, _metadataIdentity + 1, new FixtureRoot());
            _sessions.Commit(commit);
            Accepted++;
            return Result.Ok();
        }

        internal void Capture(RunFinished result)
        {
            var run = _active ?? throw new InvalidOperationException("No fixture run to capture.");
            var claim = _sessions.TryClaimResult(run);
            Verify(claim.Kind == LocalRunResultClaimKind.InvokeOnce, "capture once on the accepted fixture run");
            _sessions.CompleteResult(claim.Claim!, _metadataIdentity + 2, _metadataIdentity, result, new FixtureRoot());
        }

        internal void Retire()
        {
            if (_active is not { } run) return;
            _sessions.BeginReturn(run);
            var verified = _sessions.VerifyConsumersEnded(run, new FixtureConsumersEnded());
            Verify(verified is not null && _sessions.Close(run, verified), "retire the fixture's prepared run");
            _active = null;
        }

        public void Dispose() => Retire();

        private sealed class FixtureRoot : IDisposable { public void Dispose() { } }
        private sealed class FixtureConsumersEnded : ILocalRunConsumersEndedVerifier
        {
            public bool Verify(LocalRunRetirementRequest request, out string evidence)
            {
                evidence = "Controlled fixture owners ended.";
                return true;
            }
        }
    }
}
