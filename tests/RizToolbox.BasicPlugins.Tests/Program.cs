using RizToolbox.BasicPlugins;
using RizToolbox.Core;
using RizToolbox.Sdk;

namespace RizToolbox.BasicPlugins.Tests;

internal static class Program
{
    private static int Main()
    {
        var cases = new (string Name, Action Run)[]
        {
            ("Actual plugins wait, activate, withdraw and recover through the host", WaitingAndProviderRecovery),
            ("Song pages and filters persist while copied scores avoid frame recapture", SongPaginationAndSavedFilter),
            ("Invalid song configuration is preserved until an explicit valid reload", InvalidSongConfigurationAndReload),
            ("A failed explicit score refresh invalidates the previous displayed snapshot", FailedSongRefreshInvalidatesCache),
            ("Theme disable restores another owner's layer and then the game baseline", ThemeDisableRestoresRemainingLayer),
            ("Theme scene withdrawal restores an adopted baseline and uses a new target generation", ThemeSceneRestoresAndRebinds),
            ("F8 conflict keeps its existing owner and later retries through the real broker", QuickPanelF8ConflictAndRecovery),
            ("A saved hidden panel remains reachable after enabling with an F8 conflict", QuickPanelHiddenPreferenceWithF8Conflict),
            ("Text focus, dead windows and scene boundaries preserve one owned quick panel", QuickPanelFocusAndWindowRecreation),
            ("Explicit WAV playback uses saved settings and releases source before asset and scope", WavSavedSettingsAndSuccessfulStop),
            ("A refused WAV stop retains ownership, blocks replacement and permits a confirmed retry", WavFailedStopRetainsResponsibility),
            ("A throwing WAV stop retains its manual scope through a scene change", WavStopExceptionAcrossScene),
            ("A throwing WAV start without a returned stop lease waits for host cleanup", () => WavUnconfirmedStartWithoutLease(StartBehavior.ThrowAfterOwning)),
            ("A successful WAV start without its stop lease retains responsibility", () => WavUnconfirmedStartWithoutLease(StartBehavior.SuccessWithoutLease)),
            ("A WAV start returning no result retains responsibility", () => WavUnconfirmedStartWithoutLease(StartBehavior.MissingResult))
        };
        var failures = 0;
        foreach (var test in cases)
        {
            try
            {
                test.Run();
                Console.WriteLine("PASS " + test.Name);
            }
            catch (Exception error)
            {
                failures++;
                Console.Error.WriteLine("FAIL " + test.Name + Environment.NewLine + error);
            }
        }
        Console.WriteLine($"Basic plugin offline integration: {cases.Length - failures}/{cases.Length} passed.");
        return failures == 0 ? 0 : 1;
    }

    private static void WaitingAndProviderRecovery()
    {
        using var f = new HostFixture(registerProviders: false);
        f.Register(new SongScoresPlugin());
        f.Register(new ThemePlugin());
        f.Register(new QuickPanelPlugin());
        foreach (var id in new[] { BasicIds.Songs, BasicIds.Theme, BasicIds.Quick })
        {
            var requested = f.Host.Enable(id);
            Check.True(!requested.Success && requested.Error?.Code == ErrorCode.CapabilityUnavailable, "Missing public providers must report an explicit waiting failure.");
            Check.Equal(PluginState.Waiting, f.Status(id).State, "Plugin waits without providers");
            Check.True(f.Status(id).DesiredEnabled, "Waiting must retain the user's enable intent.");
            Check.Equal(0, f.Counts(id).Scopes, "Waiting must not allocate an activation");
        }
        f.Tick(0);
        Check.Equal(0, f.Ui.Windows.Count, "Waiting must not open a panel");
        Check.Equal(0, f.Catalog.Reads, "Waiting must not read catalog data");
        Check.Equal(0, f.Audio.Plays.Count, "Waiting must not start audio");

        f.RegisterScenes();
        f.RegisterCatalog();
        f.RegisterScores();
        Check.Equal(PluginState.Waiting, f.Status(BasicIds.Songs).State, "Song plugin still needs Windows");
        f.RegisterWindows();
        Check.Equal(PluginState.Enabled, f.Status(BasicIds.Songs).State, "Final song requirement enables it automatically");
        Check.Equal(PluginState.Waiting, f.Status(BasicIds.Theme).State, "Theme waits independently for Resources");
        Check.Equal(PluginState.Waiting, f.Status(BasicIds.Quick).State, "Quick panel waits independently for audio/input/storage");
        f.Tick(1);
        f.Tick(2);
        var oldSongWindow = f.SongWindow;

        f.RegisterResources();
        f.RegisterInput();
        f.RegisterStorage();
        Check.Equal(PluginState.Waiting, f.Status(BasicIds.Quick).State, "Storage alone does not satisfy audio requirement");
        f.RegisterAudio();
        f.Tick(3);
        f.Tick(4);
        var oldQuickWindow = f.QuickWindow;
        Check.True(f.Host.Statuses.All(status => status.State == PluginState.Enabled), "Each actual plugin should activate once its public requirements are ready.");
        Check.Equal(1, f.Input.GetBindings().Count, "Quick panel owns one hotkey");
        Check.Equal(0, f.Audio.Loads.Count, "Activation must not load the default WAV");

        f.Capabilities.SetUnavailable(CapabilityIds.Scores, "Controlled score provider withdrawal.");
        Check.Equal(PluginState.Waiting, f.Status(BasicIds.Songs).State, "Song plugin stops when its score provider is withdrawn");
        Check.True(!oldSongWindow.IsAlive && !oldSongWindow.Scope.IsActive, "Score withdrawal must release the dependent panel scope.");
        Check.Equal(0, f.Counts(BasicIds.Songs).RetainedOwnedResources, "Dependent activation ownership is released");
        Check.True(oldQuickWindow.IsAlive && f.Status(BasicIds.Theme).State == PluginState.Enabled, "An unrelated required-provider withdrawal must leave other plugins active.");
        f.RegisterScores();
        f.Tick(5);
        f.Tick(6);
        Check.True(!ReferenceEquals(oldSongWindow, f.SongWindow), "Provider recovery must create a new song panel activation.");

        f.Capabilities.SetUnavailable(GameCapabilityIds.Scenes, "Controlled scene provider withdrawal.");
        Check.True(f.Host.Statuses.All(status => status.State == PluginState.Waiting && status.DesiredEnabled), "Scene withdrawal leaves all three requested plugins waiting.");
        Check.True(f.Ui.Windows.All(window => !window.IsAlive), "Scene provider withdrawal releases every plugin window.");
        Check.True(f.Host.GetResourceCounts().All(count => count.Scopes == 0 && count.RetainedOwnedResources == 0), "Waiting activations retain no resources.");
        Check.Equal(0, f.Input.GetBindings().Count, "Scene provider withdrawal removes the hotkey");
        f.RegisterScenes();
        f.Tick(7);
        f.Tick(8);
        Check.True(f.Host.Statuses.All(status => status.State == PluginState.Enabled), "Restored scenes activate all desired plugins.");
        Check.Equal(2, f.Ui.Windows.Count(window => window.IsAlive), "Recovery opens one panel per UI plugin");
        Check.Equal(1, f.Input.GetBindings().Count, "Recovery re-registers one F8 action");
    }

    private static void SongPaginationAndSavedFilter()
    {
        using var f = new HostFixture();
        f.SeedConfiguration(BasicIds.Songs, "song-scores", new SongScoresSettings { RowsPerPage = 2 });
        f.Start(new SongScoresPlugin());
        Check.Contains(f.SongWindow.Text("rows"), "Alpha", "First page is sorted by title");
        Check.Contains(f.SongWindow.Text("rows"), "Bravo", "First page contains two rows");
        Check.Excludes(f.SongWindow.Text("rows"), "Charlie", "First page is bounded");
        Check.Contains(f.SongWindow.Text("rows"), "Best 12345  raw completion 0.9876", "Scores preserve raw completion");
        Check.Contains(f.SongWindow.Text("status"), "Page 1/3", "Initial pagination reflects all five charts");
        f.SongWindow.Click("next");
        f.Tick(2);
        Check.Contains(f.SongWindow.Text("rows"), "Charlie", "Next page advances displayed rows");
        Check.Contains(f.SongWindow.Text("rows"), "Delta", "Next page contains the remaining row");
        Check.Equal(1, f.ReadConfiguration<SongScoresSettings>(BasicIds.Songs, "song-scores").Page, "Page changes use real host configuration");

        var chart = f.Catalog.Charts.FindIndex(value => value.Id == "chart-c");
        f.Catalog.Charts[chart] = f.Catalog.Charts[chart] with { Title = "Changed after capture" };
        f.Scores.Records[0] = f.Scores.Records[0] with { Score = 90001 };
        f.Tick(3);
        f.Tick(8);
        Check.Contains(f.SongWindow.Text("rows"), "Charlie", "Published DTO list mutation cannot alter the copied page");
        Check.Excludes(f.SongWindow.Text("rows"), "Changed after capture", "Only explicit refresh replaces the display cache");
        Check.Equal(1, f.Catalog.Reads, "Ordinary frames and paging do not recapture catalog");
        Check.Equal(1, f.Scores.Reads, "Ordinary frames and paging do not recapture scores");
        f.SongWindow.Click("previous");
        f.Tick(9);
        Check.Contains(f.SongWindow.Text("rows"), "Best 12345", "Cached best score remains the copied value");

        f.SongWindow.Enter("filter", "  echo  ");
        f.SongWindow.Click("apply-filter");
        f.Tick(10);
        Check.Contains(f.SongWindow.Text("rows"), "Echo", "Saved case-insensitive filter selects the matching chart");
        Check.Excludes(f.SongWindow.Text("rows"), "Alpha", "Filtering removes unrelated copied rows");
        Check.Contains(f.SongWindow.Text("status"), "Page 1/1", "Filter resets the saved page");
        var settings = f.ReadConfiguration<SongScoresSettings>(BasicIds.Songs, "song-scores");
        Check.Equal("echo", settings.Filter, "Filter is trimmed and persisted");
        Check.Equal(0, settings.Page, "Filter save resets page to zero");
        Check.Equal(2, settings.RowsPerPage, "Page size survives user saves");
        var previousWindow = f.SongWindow;
        f.Disable(BasicIds.Songs);
        f.Enable(BasicIds.Songs);
        f.Tick(11);
        f.Tick(12);
        Check.True(!previousWindow.IsAlive && !ReferenceEquals(previousWindow, f.SongWindow), "A fresh activation gets a fresh panel.");
        Check.Equal("echo", f.SongWindow.Text("filter"), "Saved filter is loaded by a fresh activation");
        Check.Contains(f.SongWindow.Text("rows"), "Echo", "Reactivated plugin applies saved configuration");
        Check.Equal(2, f.Catalog.Reads, "New activation captures one fresh catalog snapshot");
    }

    private static void InvalidSongConfigurationAndReload()
    {
        using var f = new HostFixture();
        const string invalid = "{ \"RowsPerPage\": 0, \"Filter\": \"\", \"Page\": 0 }";
        f.SeedRawConfiguration(BasicIds.Songs, "song-scores", invalid);
        f.Start(new SongScoresPlugin());
        Check.True(f.Log.Has("ConfigInvalid"), "Invalid configuration must be explained in the plugin log.");
        f.SongWindow.Click("next");
        f.SongWindow.Enter("filter", "Alpha");
        f.SongWindow.Click("apply-filter");
        f.Tick(2);
        Check.Equal(invalid, File.ReadAllText(f.ConfigurationPath(BasicIds.Songs, "song-scores")), "Invalid configuration remains byte-for-byte unchanged after refused saves");
        Check.Contains(f.SongWindow.Text("status"), "ConfigInvalid", "Panel reports why filter/page saving is unavailable");
        Check.Equal(PluginState.Enabled, f.Status(BasicIds.Songs).State, "Invalid configuration is an operation error, not an activation fault");

        f.SeedConfiguration(BasicIds.Songs, "song-scores", new SongScoresSettings { RowsPerPage = 2, Page = 1 });
        var oldWindow = f.SongWindow;
        oldWindow.Click("reload-settings");
        Check.True(!oldWindow.IsAlive, "Valid explicit reload rebuilds controls with current settings.");
        f.Tick(3);
        Check.Contains(f.SongWindow.Text("status"), "Page 2/3", "Reload takes the corrected saved page");
        Check.Contains(f.SongWindow.Text("rows"), "Charlie", "Reload takes the corrected page size");
        f.SongWindow.Click("next");
        f.Tick(4);
        Check.Equal(2, f.ReadConfiguration<SongScoresSettings>(BasicIds.Songs, "song-scores").Page, "Valid reload restores configuration mutation");
        Check.Contains(f.SongWindow.Text("rows"), "Echo", "Recovered page action changes the visible page");
    }

    private static void FailedSongRefreshInvalidatesCache()
    {
        using var f = new HostFixture();
        f.Start(new SongScoresPlugin());
        Check.Contains(f.SongWindow.Text("rows"), "Best 12345", "Initial display has a current score snapshot");
        f.Scores.Error = new(ErrorCode.ServiceProviderFailed, "Controlled score capture failed.", CapabilityIds.Scores);
        f.SongWindow.Click("refresh");
        f.Tick(6);
        Check.Contains(f.SongWindow.Text("rows"), "Current song/score snapshot is unavailable", "Failed refresh must invalidate the previous display");
        Check.Excludes(f.SongWindow.Text("rows"), "Best 12345", "Old scores cannot remain labeled current after refresh failure");
        Check.Contains(f.SongWindow.Text("status"), "ServiceProviderFailed", "Refresh failure carries its structured reason");
        f.Tick(7);
        Check.Equal(2, f.Scores.Reads, "An ordinary provider error does not trigger frame-by-frame capture");
        Check.Equal(PluginState.Enabled, f.Status(BasicIds.Songs).State, "An explicit refresh failure leaves the plugin usable");
        f.Scores.Error = null;
        f.Scores.Records[0] = f.Scores.Records[0] with { Score = 70001, RawCompletion = .9911 };
        f.SongWindow.Click("refresh");
        f.Tick(11);
        Check.Contains(f.SongWindow.Text("rows"), "Best 70001  raw completion 0.9911", "Explicit retry displays the new snapshot");
        Check.Excludes(f.SongWindow.Text("rows"), "snapshot is unavailable", "Successful retry clears stale display failure");
        Check.Equal(3, f.Catalog.Reads, "Only the initial capture and two explicit refreshes read catalog");
    }

    private static void ThemeDisableRestoresRemainingLayer()
    {
        using var f = new HostFixture();
        var baseline = new ResourceColor(.1f, .2f, .3f);
        var companionColor = new ResourceColor(.7f, .6f, .5f);
        var themeColor = new ResourceColor(.15f, .95f, 1f);
        var cell = f.AddColor("game.menu/button/play/color", baseline);
        using var companion = Check.Value(cell.Channel.Override("test.companion", companionColor, 5), "Add companion color layer through Core");
        f.SeedConfiguration(BasicIds.Theme, "theme", new ThemeSettings { Color = themeColor, Priority = 10 });
        f.Start(new ThemePlugin());
        Check.Equal(themeColor, cell.Color, "Basic theme wins the configured priority");
        Check.Equal(2, cell.Channel.Describe().Holders.Count, "Both owners remain in the real Core channel");
        f.Disable(BasicIds.Theme);
        Check.Equal(companionColor, cell.Color, "Disabling basic theme restores the remaining owner's color");
        Check.True(f.Resources.Leases.All(lease => !lease.IsActive), "Basic theme releases its owned layer.");
        Check.Equal(1, cell.Channel.Describe().Holders.Count, "Theme withdrawal leaves the companion holder");
        Check.Equal(0, f.Counts(BasicIds.Theme).Scopes, "Theme activation scope is released");
        companion.Dispose();
        Check.Equal(baseline, cell.Color, "Removing the remaining owner restores the game baseline");
        Check.Equal(0, cell.Channel.Describe().Holders.Count, "All reversible color ownership has ended");

        f.SeedConfiguration(BasicIds.Theme, "theme", new ThemeSettings { Enabled = false, Color = themeColor });
        f.Enable(BasicIds.Theme);
        f.Tick(2);
        f.Tick(3);
        Check.Equal(baseline, cell.Color, "A fresh activation honors disabled theme configuration");
        Check.True(f.Resources.Leases.All(lease => !lease.IsActive), "Disabled theme configuration creates no replacement layer.");
    }

    private static void ThemeSceneRestoresAndRebinds()
    {
        using var f = new HostFixture();
        const string key = "game.menu/button/play/color";
        var old = f.AddColor(key, new(.1f, .1f, .1f));
        var themeColor = new ResourceColor(.2f, .8f, .9f);
        f.SeedConfiguration(BasicIds.Theme, "theme", new ThemeSettings { Color = themeColor });
        f.Start(new ThemePlugin());
        var oldLease = f.Resources.Leases.Single();
        var adopted = new ResourceColor(.55f, .44f, .33f);
        old.Color = adopted;
        foreach (var result in f.Mutations.RefreshAll()) Check.Ok(result, "Core adopts and reapplies an external game color write");
        Check.Equal(themeColor, old.Color, "Active theme is reapplied on the adopted game baseline");
        Check.Equal(adopted, old.Channel.Describe().Baseline, "External game write becomes the restoration baseline");

        f.ChangeScene("SongSelect", 2);
        Check.True(!oldLease.IsActive && !oldLease.Scope.IsActive, "Scene boundary releases the prior theme child and lease.");
        Check.Equal(adopted, old.Color, "Scene withdrawal restores the adopted baseline");
        Check.Equal(0, old.Channel.Describe().Holders.Count, "Scene withdrawal removes the old theme holder");
        Check.Equal(1, f.Counts(BasicIds.Theme).Scopes, "Scene withdrawal leaves only the active plugin scope");
        Check.Equal(1, f.Targets.InvalidateScene("MainMenu"), "Old target is retired after restoration");
        f.Resources.Cells.Remove(key);
        old.Alive = false;
        var nextBaseline = new ResourceColor(.22f, .33f, .44f);
        var next = f.AddColor(key, nextBaseline, "SongSelect");
        Check.True(next.Target.Generation != old.Target.Generation, "A recreated game object has a new Core target generation.");
        f.Tick(2);
        var nextLease = f.Resources.Leases.Last();
        Check.Equal(themeColor, next.Color, "Next scene discovers and applies a fresh theme layer");
        Check.True(nextLease.IsActive && nextLease.Scope.ScopeId != oldLease.Scope.ScopeId, "A fresh theme child owns the new scene target.");
        Check.Equal(adopted, old.Color, "Old game value remains restored after new target application");
        f.Tick(3);
        f.Tick(4);
        Check.Equal(2, f.Resources.TargetReads, "Stable scenes do not rediscover colors every frame");
        f.Disable(BasicIds.Theme);
        Check.Equal(nextBaseline, next.Color, "Disabling in the new scene restores its own baseline");
    }

    private static void QuickPanelF8ConflictAndRecovery()
    {
        using var f = new HostFixture();
        var competing = new F8CompetingPlugin();
        f.Start(competing);
        f.Register(new QuickPanelPlugin());
        f.Enable(BasicIds.Quick);
        f.Tick(2);
        f.Tick(3);
        Check.Equal(PluginState.Enabled, f.Status(BasicIds.Quick).State, "A binding conflict does not fault the basic panel");
        Check.Contains(f.QuickWindow.Text("status"), "F8: unavailable", "Panel reports that F8 registration was refused");
        Check.True(f.Log.Has("Conflict") && f.Log.Has("test.f8-owner"), "Conflict diagnostics identify the real existing owner.");
        f.F8();
        Check.Equal(1, competing.Hits, "Refused binding preserves the existing plugin's callback");
        var panel = f.QuickWindow;
        panel.Click("hide");
        Check.True(panel.IsAlive, "Hide must keep the panel reachable while another plugin owns F8.");
        Check.True(!File.Exists(f.ConfigurationPath(BasicIds.Quick, "quick-panel")), "A refused hide must leave the default visibility preference untouched.");
        Check.Contains(panel.Text("hide"), "unavailable", "The Hide button shows that the shortcut cannot reopen the panel");
        f.Disable(competing.Manifest.Id);
        f.Tick(4);
        Check.Equal(0, f.Input.GetBindings().Count, "Basic panel respects its conflict retry interval");
        f.Tick(8);
        Check.Equal(BasicIds.Quick, f.Input.GetBindings().Single().PluginId, "Later retry acquires F8 after its owner releases it");
        Check.Equal("Hide · F8", panel.Text("hide"), "The Hide button recovers with the shortcut");
        f.F8();
        Check.True(f.OpenWindow(BasicIds.Quick, "quick-panel") is null, "Owned F8 action hides the panel.");
        Check.True(!f.ReadConfiguration<QuickPanelSettings>(BasicIds.Quick, "quick-panel").Visible, "F8 visibility changes are saved by the real configuration service.");
        f.F8();
        f.Tick(9);
        Check.True(f.QuickWindow.IsAlive && f.ReadConfiguration<QuickPanelSettings>(BasicIds.Quick, "quick-panel").Visible, "F8 reopens the panel and persists visibility.");
        Check.Equal(1, f.Input.GetBindings().Count, "Reopening a window does not duplicate the hotkey");
        f.Disable(BasicIds.Quick);
        Check.Equal(0, f.Input.GetBindings().Count, "Disabling releases the owned F8 action");
    }

    private static void QuickPanelHiddenPreferenceWithF8Conflict()
    {
        using var f = PlayingQuickPanel();
        var playback = f.Audio.Plays.Single().Playback;
        f.F8();
        Check.True(f.OpenWindow(BasicIds.Quick, "quick-panel") is null && playback.IsActive,
            "F8 hides the panel while the owned WAV keeps playing.");
        var saved = f.ReadConfiguration<QuickPanelSettings>(BasicIds.Quick, "quick-panel");
        Check.True(!saved.Visible, "The player chose to keep the panel hidden.");
        f.Disable(BasicIds.Quick);
        Check.True(!playback.IsActive && playback.StopCalls == 1, "Disabling the hidden panel stops its WAV.");
        Check.Equal(0, f.Counts(BasicIds.Quick).Scopes, "Disabling releases all quick-panel scopes");

        var competing = new F8CompetingPlugin();
        f.Start(competing);
        f.Enable(BasicIds.Quick);
        f.Tick(4);
        f.Tick(5);
        Check.True(f.OpenWindow(BasicIds.Quick, "quick-panel")?.IsAlive == true,
            "A saved hidden panel must open while its shortcut belongs to another plugin.");
        var fallback = f.QuickWindow;
        Check.Contains(fallback.Text("status"), "F8: unavailable", "The reopened panel shows the shortcut conflict");
        Check.Equal(saved, f.ReadConfiguration<QuickPanelSettings>(BasicIds.Quick, "quick-panel"),
            "Displaying the panel during the conflict preserves the player's saved preference");

        f.Disable(competing.Manifest.Id);
        f.Tick(10);
        Check.True(!fallback.IsAlive && !fallback.Scope.IsActive, "The saved hidden preference takes effect once F8 recovers.");
        f.F8();
        f.Tick(11);
        Check.True(f.QuickWindow.IsAlive, "F8 can reopen the panel after recovery.");
        Check.Equal(1, f.Audio.Plays.Count, "Disabling and reopening never starts a WAV automatically");
    }

    private static void QuickPanelFocusAndWindowRecreation()
    {
        using var f = new HostFixture();
        f.Start(new QuickPanelPlugin());
        var first = f.QuickWindow;
        Check.Ok(first.Control("wav-path").Focus(), "Focus the owned text field");
        f.F8();
        Check.True(first.IsAlive, "Real input broker suppresses F8 while the owned text field is editing.");
        f.Input.SetTextEditing(first.Id, false);
        f.F8();
        Check.True(!first.IsAlive, "Ending text editing allows F8 to hide the panel.");
        f.F8();
        f.Tick(2);
        var recreated = f.QuickWindow;
        var oldCallback = recreated.Control("play").QueuedActivation();
        recreated.Expire();
        oldCallback();
        f.Tick(3);
        Check.True(!recreated.Scope.IsActive && recreated.DisposeCalls == 1, "A dead window releases its scene scope on observation.");
        f.Tick(7);
        var fresh = f.QuickWindow;
        oldCallback();
        Check.True(!ReferenceEquals(recreated, fresh), "A dead window gets a fresh owned handle.");
        Check.Equal(0, f.Audio.Loads.Count, "Queued callbacks from the expired window stay inert after recreation");
        Check.Equal(1, f.Input.GetBindings().Count, "Window recreation keeps one existing action");

        var sceneCallback = fresh.Control("play").QueuedActivation();
        f.ChangeScene("SongSelect", 2);
        sceneCallback();
        Check.True(!fresh.IsAlive && !fresh.Scope.IsActive, "Core scene scope withdrawal destroys the previous panel.");
        Check.Equal(0, f.Input.GetBindings().Count, "Scene change releases the previous F8 registration");
        f.Tick(8);
        Check.True(f.QuickWindow.Scope.ScopeId != fresh.Scope.ScopeId, "Next scene receives a new window scope.");
        Check.Contains(f.QuickWindow.Text("status"), "generation 2", "Recreated panel renders the new scene generation");
        sceneCallback();
        Check.Equal(0, f.Audio.Loads.Count, "Queued callbacks from the previous scene remain inert");
        Check.Equal(1, f.Input.GetBindings().Count, "Next scene registers one action");
        Check.Equal(2, f.Counts(BasicIds.Quick).Scopes, "Only activation and current window scope remain");
    }

    private static void WavSavedSettingsAndSuccessfulStop()
    {
        using var f = new HostFixture();
        var bytes = HostFixture.SilentPcm16Wav();
        f.SeedConfiguration(BasicIds.Quick, "quick-panel", new QuickPanelSettings
        { WavPath = "audio/saved.wav", Volume = .37f, Loop = true });
        f.SeedData(BasicIds.Quick, "audio/saved.wav", bytes);
        f.SeedData(BasicIds.Quick, "audio/unsaved.wav", bytes);
        f.Start(new QuickPanelPlugin());
        Check.Equal(0, f.Audio.Loads.Count, "Panel activation never loads a WAV automatically");
        Check.Equal(0, f.Audio.Plays.Count, "Panel activation never starts audio automatically");
        var idleScopes = f.Counts(BasicIds.Quick).Scopes;
        f.QuickWindow.Enter("wav-path", "audio/unsaved.wav");
        f.QuickWindow.Enter("volume", "0.9");
        f.QuickWindow.Click("play");
        var load = f.Audio.Loads.Single();
        var play = f.Audio.Plays.Single();
        Check.Equal("audio/saved.wav", load.File.RelativePath, "Explicit Play uses the saved path");
        Check.Equal(ContentFileArea.PluginData, load.File.Area, "WAV comes from this plugin's Data storage");
        Check.Equal(.37f, play.Volume, "Unsaved volume text cannot change playback");
        Check.True(play.Loop, "Explicit Play honors the saved loop setting.");
        Check.True(bytes.SequenceEqual(load.Bytes), "The audio provider receives the exact selected Data WAV bytes.");
        Check.True(load.Scope.IsActive && load.Token.CanBeCanceled && load.Token == load.Scope.Lifetime, "WAV loading receives the owned playback scope's cancellation token.");
        Check.Equal(idleScopes + 1, f.Counts(BasicIds.Quick).Scopes, "Explicit playback owns one additional manual scope");
        f.QuickWindow.Click("hide");
        Check.True(play.Playback.IsActive && load.Asset.IsValid && load.Scope.IsActive, "Hiding UI preserves independently owned audio.");
        f.F8();
        f.Tick(2);
        Check.Equal(1, f.Audio.Plays.Count, "Reopening a panel does not replay the WAV");
        f.QuickWindow.Click("stop");
        Check.Equal(1, play.Playback.StopCalls, "Stop explicitly confirms source shutdown");
        Check.True(!play.Playback.IsActive && load.Asset.DisposeCalls == 1 && play.Playback.DisposeCalls == 1, "Confirmed stop releases both audio leases once.");
        Check.True(!load.Scope.IsActive && load.Token.IsCancellationRequested, "Confirmed stop releases and cancels the playback child.");
        Check.Before(f.Audio.Events, "stop-ok:" + play.Playback.Id, "dispose-playback:" + play.Playback.Id, "Stop confirmation precedes source lease disposal");
        Check.Before(f.Audio.Events, "dispose-playback:" + play.Playback.Id, "dispose-asset:" + load.Asset.Asset.Id, "Source release precedes WAV asset release");
        Check.Before(f.Audio.Events, "dispose-asset:" + load.Asset.Asset.Id, "scope-cancelled:" + load.Scope.ScopeId, "WAV release precedes explicit scope withdrawal");
        Check.Equal(idleScopes, f.Counts(BasicIds.Quick).Scopes, "Confirmed stop removes its manual child");
        f.Tick(3);
        Check.Contains(f.QuickWindow.Text("status"), "Stopped.", "Panel reflects the confirmed stop");
        Check.Equal(1, f.Audio.Plays.Count, "Ordinary frames do not restart stopped audio");
    }

    private static void WavFailedStopRetainsResponsibility()
    {
        using var f = PlayingQuickPanel();
        var load = f.Audio.Loads.Single();
        var play = f.Audio.Plays.Single();
        var owned = f.Counts(BasicIds.Quick);
        f.Audio.StopBehavior = StopBehavior.Fail;
        f.QuickWindow.Click("stop");
        AssertAudioRetained(f, load, play, owned);
        Check.True(f.Log.Has("Controlled Stop refused") && f.Log.Has("ServiceProviderFailed"), "Refused stop reports its provider failure.");
        f.QuickWindow.Click("play");
        Check.Equal(2, play.Playback.StopCalls, "Replacement Play retries confirmation of the existing source");
        Check.Equal(1, f.Audio.Loads.Count, "A refused stop blocks replacement WAV loading");
        Check.Equal(1, f.Audio.Plays.Count, "A refused stop blocks a second source");
        f.Tick(2);
        AssertAudioRetained(f, load, play, owned);
        Check.Contains(f.QuickWindow.Text("status"), "unconfirmed", "Panel explains that WAV responsibility is still retained");
        f.Audio.StopBehavior = StopBehavior.Succeed;
        f.QuickWindow.Click("stop");
        Check.True(!load.Scope.IsActive && load.Asset.DisposeCalls == 1 && play.Playback.DisposeCalls == 1, "A confirmed retry releases the original audio responsibility.");
        f.QuickWindow.Click("play");
        Check.Equal(2, f.Audio.Loads.Count, "Confirmed retry permits a new explicit WAV load");
        Check.Equal(2, f.Audio.Plays.Count, "Confirmed retry permits a new source");
        Check.True(f.Audio.Loads.Last().Scope.ScopeId != load.Scope.ScopeId, "Replacement playback owns a fresh child.");
        f.QuickWindow.Click("stop");
    }

    private static void WavStopExceptionAcrossScene()
    {
        using var f = PlayingQuickPanel();
        var load = f.Audio.Loads.Single();
        var play = f.Audio.Plays.Single();
        var owned = f.Counts(BasicIds.Quick);
        f.Audio.StopBehavior = StopBehavior.Throw;
        f.QuickWindow.Click("stop");
        AssertAudioRetained(f, load, play, owned);
        Check.True(f.Log.Has("Controlled Stop threw"), "Throwing stop is recorded as an operation error.");
        f.ChangeScene("SongSelect", 2);
        Check.True(load.Scope.IsActive && !load.Token.IsCancellationRequested, "The manual WAV scope survives a scene boundary while stop is unconfirmed.");
        Check.True(play.Playback.IsActive && load.Asset.DisposeCalls == 0, "A scene stop exception cannot release a playing source's clip.");
        f.Tick(2);
        f.QuickWindow.Click("play");
        Check.Equal(3, play.Playback.StopCalls, "Button, scene boundary and replacement all request confirmation");
        Check.Equal(1, f.Audio.Loads.Count, "Scene recreation cannot bypass an unconfirmed WAV stop");
        Check.Equal(1, f.Audio.Plays.Count, "Scene recreation cannot start a replacement source");
        Check.Equal(PluginState.Enabled, f.Status(BasicIds.Quick).State, "Throwing provider operations leave the plugin available for recovery");
        f.Audio.StopBehavior = StopBehavior.Succeed;
        f.QuickWindow.Click("stop");
        Check.True(!load.Scope.IsActive && !play.Playback.IsActive && load.Asset.DisposeCalls == 1, "An explicit successful retry in the new scene releases the retained audio.");
        Check.Before(f.Audio.Events, "dispose-playback:" + play.Playback.Id, "dispose-asset:" + load.Asset.Asset.Id, "Exception recovery releases source before asset");
        f.Disable(BasicIds.Quick);
        Check.Equal(0, f.Counts(BasicIds.Quick).Scopes, "Post-recovery disable releases all activation children");
    }

    private static void WavUnconfirmedStartWithoutLease(StartBehavior behavior)
    {
        using var f = new HostFixture();
        f.SeedData(BasicIds.Quick, "audio/owned.wav", HostFixture.SilentPcm16Wav());
        f.Audio.StartBehavior = behavior;
        f.Start(new QuickPanelPlugin());
        f.QuickWindow.Click("play");
        var load = f.Audio.Loads.Single();
        var unknown = f.Audio.Plays.Single();
        Check.True(!unknown.Returned && unknown.Playback.IsActive, "Controlled provider owns a source before returning without its lease.");
        Check.True(load.Asset.IsValid && load.Scope.IsActive && load.Asset.DisposeCalls == 0, "Plugin retains the WAV and scope when source creation is unconfirmed.");
        f.QuickWindow.Click("stop");
        f.QuickWindow.Click("play");
        f.Tick(2);
        Check.Equal(0, unknown.Playback.StopCalls, "Plugin cannot confirm an unknown source with a missing stop lease");
        Check.Equal(1, f.Audio.Loads.Count, "Unknown source creation blocks replacement WAV loading");
        Check.Equal(1, f.Audio.Plays.Count, "Unknown source creation blocks replacement playback");
        Check.True(f.Log.Has("without returning a stop lease"), "Panel operations explain that host scope cleanup is required.");
        Check.Contains(f.QuickWindow.Text("status"), "replacement is blocked", "Panel reports the unconfirmed creation state");
        f.Disable(BasicIds.Quick);
        Check.True(!unknown.Playback.IsActive && unknown.Playback.DisposeCalls == 1, "Host withdrawal cleans its provider-owned unknown source.");
        Check.True(load.Asset.DisposeCalls == 1 && !load.Scope.IsActive && load.Token.IsCancellationRequested, "Host cleanup releases the retained asset and child.");
        Check.Before(f.Audio.Events, "dispose-playback:" + unknown.Playback.Id, "dispose-asset:" + load.Asset.Asset.Id, "Host cleanup releases unknown source before the retained clip");
        Check.Equal(0, f.Counts(BasicIds.Quick).RetainedOwnedResources, "Host cleanup releases the activation's retained responsibility");

        f.Audio.StartBehavior = StartBehavior.Confirmed;
        f.Enable(BasicIds.Quick);
        f.Tick(3);
        f.Tick(4);
        f.QuickWindow.Click("play");
        Check.Equal(2, f.Audio.Plays.Count, "Fresh activation may explicitly start after host cleanup");
        Check.True(f.Audio.Plays.Last().Returned && f.Audio.Loads.Last().Scope.ScopeId != load.Scope.ScopeId, "Fresh activation receives a new playback scope and a confirmed stop lease.");
        f.QuickWindow.Click("stop");
    }

    private static HostFixture PlayingQuickPanel()
    {
        var f = new HostFixture();
        try
        {
            f.SeedData(BasicIds.Quick, "audio/owned.wav", HostFixture.SilentPcm16Wav());
            f.Start(new QuickPanelPlugin());
            f.QuickWindow.Click("play");
            Check.True(f.Audio.Plays.Single().Playback.IsActive, "Explicit controlled WAV should start before the fault is injected.");
            return f;
        }
        catch
        {
            f.Dispose();
            throw;
        }
    }

    private static void AssertAudioRetained(HostFixture f, AudioLoad load, AudioPlay play, PluginResourceCounts before)
    {
        Check.True(play.Playback.IsActive && load.Asset.IsValid && load.Scope.IsActive, "Unconfirmed stop retains source, WAV asset and active child.");
        Check.True(!load.Token.IsCancellationRequested && load.Asset.DisposeCalls == 0 && play.Playback.DisposeCalls == 0, "Unconfirmed stop releases none of its owned audio responsibility.");
        var after = f.Counts(BasicIds.Quick);
        Check.Equal(before.Scopes, after.Scopes, "Unconfirmed stop preserves the owned scope count");
        Check.Equal(before.RetainedOwnedResources, after.RetainedOwnedResources, "Unconfirmed stop preserves retained ownership");
    }
}
