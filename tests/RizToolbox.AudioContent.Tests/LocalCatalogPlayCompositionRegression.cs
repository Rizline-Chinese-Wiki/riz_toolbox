using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using RizToolbox.Adapter;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Sdk;
using static Assert;

internal static partial class LocalCatalogRegression
{
    internal static readonly (string Name, Action Test)[] PlayCompositionCases = {
        ("Formal LOCAL play dispatch borrows the exact selected resource graph and returns its preparation fork", PlayDispatchIdentity),
        ("Accepted LOCAL play may retain a pinned request through provider withdrawal without re-resolving IDs", PlayDispatchRetention),
        ("Cancelled stale and wrong-thread LOCAL requests never reach the installed play dispatcher", PlayDispatchAdmission),
        ("LOCAL play dispatcher reentry cannot recursively prepare another native request", PlayDispatchReentry),
        ("Formal play unavailability and dispatcher exceptions return structured failures and release the borrowed request", PlayDispatchFailure),
        ("Only a valid uncancelled LOCAL play request starts retained native composition", PlayDispatchLazyComposition),
        ("Mixed LOCAL chart reports the note-bearing line without a segment before native preparation", PlayDispatchSegmentlessLine),
        ("LOCAL play refuses a note earlier than all of its line points before native preparation", PlayDispatchEarlyNote),
        ("LOCAL play refuses a note that only reaches the last point of an unordered line", PlayDispatchEndpointOnly)
    };
    internal static readonly (string Name, Action Test)[] PlaybackMetadataCases = {
        ("Optional LOCAL playback metadata preserves legacy constructors and deconstruction while copying authored values", PlaybackMetadataCopy),
        ("Native LOCAL descriptors use the exact selected revision and never infer scoring counters", PlaybackDescriptorIdentity),
        ("Missing LOCAL playback metadata remains CPU-valid but cannot create a native play descriptor", PlaybackMetadataMissing),
        ("Invalid authored difficulty counters credits or color reject publication without a catalog revision", PlaybackMetadataRejected),
        ("Retained LOCAL retry metadata and resources stay pinned after same-ID reconstruction", PlaybackMetadataPinned),
        ("A single-point note-bearing LOCAL chart remains browseable without playback content", PlaybackSegmentlessChart),
        ("A one-point decorative line and notes beyond the last point preserve LOCAL playback content", PlaybackDecorativeLine)
    };
    private static LocalChartPlaybackMetadata AuthoredPlayback() => new(2, 123, 456789, "IN", "Local Author", new(.2f, .4f, .6f), 12.5f, "Cover Author");
    private static void PlaybackMetadataCopy()
    {
        using var f = new CatalogFixture(); using var context = f.Context(); var song = f.Resources(context);
        var legacy = song.Charts[0]; var (id, name, content) = legacy;
        Equal("easy", id); Equal("Easy", name); Equal(legacy.Content, content);
        var metadata = AuthoredPlayback(); var authored = legacy with { Playback = metadata };
        var registration = Need(f.Catalog.ForPlugin(context).RegisterSong(song with { Charts = new[] { authored } }));
        var published = registration.Snapshot.Charts[0]; var (snapshotId, snapshotName, _) = published;
        Equal(id, snapshotId); Equal(name, snapshotName); Equal(metadata, published.Playback);
        Check(!ReferenceEquals(metadata, published.Playback) && !ReferenceEquals(metadata.ThemeColor, published.Playback!.ThemeColor));
        Check(!registration.Snapshot.AvailableForGamePlayback);
    }
    private static void PlaybackDescriptorIdentity()
    {
        using var f = new CatalogFixture(); using var context = f.Context(); var song = f.Resources(context);
        var metadata = AuthoredPlayback();
        var registration = Need(f.Catalog.ForPlugin(context).RegisterSong(song with { Charts = new[] { song.Charts[0] with { Playback = metadata } } }));
        using var selected = Need(f.Catalog.SelectionForPlugin(context).Select(registration.Snapshot.Song, "easy"));
        using var request = Need(f.Catalog.Prepare(selected)); var result = LocalChartPlaybackMetadataValidation.Require(request);
        Equal(2, result.JudgeIndex); Equal(metadata.MaxHit, result.Descriptor.MaxHit); Equal(metadata.MaxScore, result.Descriptor.MaxScore);
        Equal(selected.Snapshot.Song.Revision, result.Descriptor.Revision); Equal(selected.Snapshot.Song.Song, result.Descriptor.LocalId);
        Equal(selected.Snapshot.Chart.Id, result.Descriptor.ChartId); Equal(metadata.Designer, result.Descriptor.Designer);
        Equal(metadata.DifficultyRating, result.Descriptor.Difficulty);
    }
    private static void PlaybackMetadataMissing()
    {
        using var f = new CatalogFixture(); using var context = f.Context();
        var registration = Need(f.Catalog.ForPlugin(context).RegisterSong(f.Resources(context)));
        using var selected = Need(f.Catalog.SelectionForPlugin(context).Select(registration.Snapshot.Song, "easy"));
        using var request = Need(f.Catalog.Prepare(selected));
        Throws<InvalidDataException>(() => LocalChartPlaybackMetadataValidation.Require(request));
        Equal(3, request.Resources.Count); Check(selected.IsCurrent);
    }
    private static void PlaybackMetadataRejected()
    {
        using var f = new CatalogFixture(); using var context = f.Context(); var song = f.Resources(context);
        var authored = AuthoredPlayback(); var provider = f.Catalog.ForPlugin(context);
        foreach (var invalid in new[] { authored with { DifficultyIndex = -1 }, authored with { DifficultyIndex = 3 },
            authored with { MaxHit = 0 }, authored with { MaxScore = 0 }, authored with { MaxScore = 1_000_000_001 },
            authored with { Designer = "" }, authored with { ThemeColor = new(float.NaN, 0, 0) } })
        {
            var result = provider.RegisterSong(song with { Charts = new[] { song.Charts[0] with { Playback = invalid } } });
            Equal(ErrorCode.ContentInvalid, result.Error!.Code); Equal(0L, Need(provider.Read()).Revision);
        }
    }
    private static void PlaybackMetadataPinned()
    {
        using var f = new CatalogFixture(); using var context = f.Context(); var song = f.Resources(context);
        var provider = f.Catalog.ForPlugin(context);
        var old = Need(provider.RegisterSong(song with { Charts = new[] { song.Charts[0] with { Playback = AuthoredPlayback() } } }));
        using var selected = Need(f.Catalog.SelectionForPlugin(context).Select(old.Snapshot.Song, "easy"));
        using var prepared = Need(f.Catalog.Prepare(selected)); old.Dispose();
        var replacement = Need(provider.RegisterSong(song with { Charts = new[] { song.Charts[0] with { Playback = AuthoredPlayback() with { DifficultyIndex = 0, MaxScore = 777 } } } }));
        using var retry = prepared.Retain(); var pinned = LocalChartPlaybackMetadataValidation.Require(retry);
        Equal(2, pinned.JudgeIndex); Equal(456789, pinned.Descriptor.MaxScore);
        Check(pinned.Descriptor.Revision != replacement.Snapshot.Revision);
        Check(ReferenceEquals(prepared.Selection.Chart, retry.Selection.Chart));
    }

    private static void PlaybackSegmentlessChart()
    {
        using var f = new CatalogFixture(chartJson: ChartWithLines((false, true)));
        using var context = f.Context();
        var provider = f.Catalog.ForPlugin(context);
        using var registration = RegisterAuthoredChart(provider, f.Resources(context));
        var song = Need(provider.Read()).Songs.Single();
        Check(registration.IsPublished && ReferenceEquals(registration.Snapshot, song));
        Check(!song.Charts.Single().HasPlaybackContent);
        using var selected = Need(f.Catalog.SelectionForPlugin(context).SelectRevision(song, "easy"));
        Check(selected.IsCurrent && ReferenceEquals(song.Charts.Single(), selected.Snapshot.Chart));
    }

    private static void PlayDispatchSegmentlessLine()
    {
        var play = new PlayTarget { Execute = _ => Result.Fail(new(ErrorCode.CapabilityUnavailable, "Controlled native admission is unavailable.")) };
        using var f = new CatalogFixture(play: play, chartJson: ChartWithLines((false, false), (true, true), (false, true)));
        using var context = f.Context();
        using var registration = RegisterAuthoredChart(f.Catalog.ForPlugin(context), f.Resources(context));
        using var selected = Need(f.Catalog.SelectionForPlugin(context).Select(registration.Snapshot.Song, "easy"));
        var error = selected.RequestPlay().Error!;
        Equal(ErrorCode.ContentInvalid, error.Code);
        Equal(AudioContentCapabilityIds.LocalCatalogSelection, error.CapabilityId);
        Equal("easy", error.TargetId);
        Check(error.Message.Contains("lines[2].linePoints", StringComparison.Ordinal) &&
            error.Message.Contains("at least two", StringComparison.OrdinalIgnoreCase));
        Check(selected.IsCurrent && !selected.Snapshot.Chart.HasPlaybackContent);
        Equal(0, play.Calls);
        Equal(0, f.Catalog.PreparedRequestCount);

        using var request = Need(f.Catalog.Prepare(selected));
        try { _ = LocalChartPlaybackMetadataValidation.Require(request); }
        catch (InvalidDataException refusal) { Equal(error.Message, refusal.Message); return; }
        throw new InvalidOperationException("The native metadata check accepted the segmentless chart.");
    }

    private static void PlaybackDecorativeLine()
    {
        var play = new PlayTarget();
        var chart = JsonNode.Parse(ChartWithLines((false, false), (true, true), (true, true)))!;
        var unorderedLine = chart["lines"]![2]!;
        SetLinePointTimes(unorderedLine, 2, 0, 3);
        unorderedLine["notes"]![0]!["time"] = 0;
        using var f = new CatalogFixture(play: play, chartJson: chart.ToJsonString());
        using var context = f.Context();
        using var registration = RegisterAuthoredChart(f.Catalog.ForPlugin(context), f.Resources(context));
        using var selected = Need(f.Catalog.SelectionForPlugin(context).Select(registration.Snapshot.Song, "easy"));
        Check(selected.Snapshot.Chart.HasPlaybackContent);
        play.Execute = request =>
        {
            Equal("easy", LocalChartPlaybackMetadataValidation.Require(request).Descriptor.ChartId);
            return Result.Ok();
        };
        Need(selected.RequestPlay());
        Equal(1, play.Calls);
        Equal(0, f.Catalog.PreparedRequestCount);
    }

    private static void PlayDispatchEarlyNote()
    {
        var chart = JsonNode.Parse(ChartWithLines((true, true)))!;
        var notes = chart["lines"]![0]!["notes"]!.AsArray();
        notes[0]!["time"] = 0;
        var early = notes[0]!.DeepClone();
        early["time"] = -1;
        notes.Add(early);
        CheckEarlyNoteRejected(chart, "lines[0].notes[1].time");
    }

    private static void PlayDispatchEndpointOnly()
    {
        var chart = JsonNode.Parse(ChartWithLines((true, true)))!;
        var line = chart["lines"]![0]!;
        SetLinePointTimes(line, 2, 3, 0);
        line["notes"]![0]!["time"] = 1;
        CheckEarlyNoteRejected(chart, "lines[0].notes[0].time");
    }

    private static void CheckEarlyNoteRejected(JsonNode chart, string notePath)
    {
        var play = new PlayTarget { Execute = _ => Result.Fail(new(ErrorCode.CapabilityUnavailable, "Controlled native admission is unavailable.")) };
        using var f = new CatalogFixture(play: play, chartJson: chart.ToJsonString());
        using var context = f.Context();
        using var registration = RegisterAuthoredChart(f.Catalog.ForPlugin(context), f.Resources(context));
        using var selected = Need(f.Catalog.SelectionForPlugin(context).Select(registration.Snapshot.Song, "easy"));
        var error = selected.RequestPlay().Error!;
        Equal(ErrorCode.ContentInvalid, error.Code);
        Equal(AudioContentCapabilityIds.LocalCatalogSelection, error.CapabilityId);
        Equal("easy", error.TargetId);
        Check(error.Message.Contains(notePath, StringComparison.Ordinal)
            && error.Message.Contains("earlier than every segment start", StringComparison.Ordinal));
        Check(registration.IsPublished && selected.IsCurrent && !selected.Snapshot.Chart.HasPlaybackContent);
        Equal(0, play.Calls);
        Equal(0, f.Catalog.PreparedRequestCount);
    }

    private static ILocalSongRegistration RegisterAuthoredChart(ILocalCatalogProviderService provider, LocalSongRequest song)
        => Need(provider.RegisterSong(song with { Charts = new[] { song.Charts[0] with { Playback = AuthoredPlayback() } } }));

    private static void SetLinePointTimes(JsonNode line, params int[] times)
    {
        var points = line["linePoints"]!.AsArray();
        var template = points[0]!.DeepClone();
        points.Clear();
        foreach (var time in times)
        {
            var point = template.DeepClone();
            point["time"] = time;
            point["floorPosition"] = time;
            points.Add(point);
        }
    }

    private static string ChartWithLines(params (bool HasSegment, bool HasNotes)[] rows)
    {
        var chart = JsonNode.Parse(ChartJson())!;
        var template = chart["lines"]![0]!;
        var lines = new JsonArray();
        foreach (var row in rows)
        {
            var line = template.DeepClone();
            if (!row.HasSegment) line["linePoints"]!.AsArray().RemoveAt(1);
            if (!row.HasNotes) line["notes"] = new JsonArray();
            else line["notes"]![0]!["time"] = 4; // The second point is at time 2.
            lines.Add(line);
        }
        chart["lines"] = lines;
        return chart.ToJsonString();
    }

    private static void PlayDispatchIdentity()
    {
        var play = new PlayTarget(); using var f = new CatalogFixture(play: play); using var context = f.Context();
        var descriptor = f.Resources(context); var registration = Need(f.Catalog.ForPlugin(context).RegisterSong(descriptor));
        using var selected = Need(f.Catalog.SelectionForPlugin(context).Select(registration.Snapshot.Song, "easy"));
        play.Execute = request => {
            Check(ReferenceEquals(selected.Snapshot, request.Selection)); Equal(3, request.Resources.Count);
            Check(ReferenceEquals(request.Resources.Single(row => row.Snapshot.Kind == LocalContentKind.Chart).Snapshot,
                selected.Snapshot.Chart.Content)); Equal(1, f.Catalog.PreparedRequestCount); return Result.Ok();
        };
        Need(selected.RequestPlay()); Equal(1, play.Calls); Check(play.Borrowed!.IsDisposed); Equal(0, f.Catalog.PreparedRequestCount);
    }
    private static void PlayDispatchRetention()
    {
        var play = new PlayTarget(); using var f = new CatalogFixture(play: play); using var context = f.Context();
        var descriptor = f.Resources(context); var registration = Need(f.Catalog.ForPlugin(context).RegisterSong(descriptor));
        using var selected = Need(f.Catalog.SelectionForPlugin(context).Select(registration.Snapshot.Song, "easy"));
        LocalPlayRequest? retained = null;
        play.Execute = request => { retained = request.Retain(); return Result.Ok(); };
        Need(selected.RequestPlay()); Check(play.Borrowed!.IsDisposed && !retained!.IsDisposed);
        registration.Dispose(); f.LastResources!.Dispose();
        Check(!selected.IsCurrent); Check(ReferenceEquals(selected.Snapshot, retained.Selection));
        Equal(3, retained.Resources.Count); Check(!f.Parsed.Disposed); retained.Dispose(); selected.Dispose();
        Equal(0, f.Catalog.PreparedRequestCount); Check(f.Parsed.Disposed);
    }
    private static void PlayDispatchAdmission()
    {
        var play = new PlayTarget(); using var f = new CatalogFixture(play: play); using var context = f.Context();
        var registration = Need(f.Catalog.ForPlugin(context).RegisterSong(f.Resources(context)));
        using var selected = Need(f.Catalog.SelectionForPlugin(context).Select(registration.Snapshot.Song, "easy"));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Equal(ErrorCode.Cancelled, selected.RequestPlay(cancelled.Token).Error!.Code);
        Equal(ErrorCode.WrongThread, Task.Run(() => selected.RequestPlay().Error!.Code).Result);
        registration.Dispose(); Equal(ErrorCode.TargetExpired, selected.RequestPlay().Error!.Code);
        Equal(0, play.Calls); Equal(0, f.Catalog.PreparedRequestCount);
    }
    private static void PlayDispatchReentry()
    {
        var play = new PlayTarget(); using var f = new CatalogFixture(play: play); using var context = f.Context();
        var registration = Need(f.Catalog.ForPlugin(context).RegisterSong(f.Resources(context)));
        using var selected = Need(f.Catalog.SelectionForPlugin(context).Select(registration.Snapshot.Song, "easy"));
        play.Execute = _ => { Equal(ErrorCode.Conflict, selected.RequestPlay().Error!.Code); return Result.Ok(); };
        Need(selected.RequestPlay()); Equal(1, play.Calls); Equal(0, f.Catalog.PreparedRequestCount);
    }
    private static void PlayDispatchFailure()
    {
        var play = new PlayTarget(); using var f = new CatalogFixture(play: play); using var context = f.Context();
        var registration = Need(f.Catalog.ForPlugin(context).RegisterSong(f.Resources(context)));
        using var selected = Need(f.Catalog.SelectionForPlugin(context).Select(registration.Snapshot.Song, "easy"));
        play.Execute = _ => Result.Fail(new(ErrorCode.CapabilityUnavailable, "Production gate remains closed."));
        Equal(ErrorCode.CapabilityUnavailable, selected.RequestPlay().Error!.Code);
        Check(play.Borrowed!.IsDisposed); Equal(0, f.Catalog.PreparedRequestCount);
        play.Execute = _ => throw new IOException("Controlled dispatch failure.");
        Equal(ErrorCode.InternalError, selected.RequestPlay().Error!.Code);
        Check(play.Borrowed!.IsDisposed); Equal(0, f.Catalog.PreparedRequestCount);
    }

    private static void PlayDispatchLazyComposition()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) return;
        using var fixture = new CatalogFixture(); using var context = fixture.Context(); using var native = new FakeNative();
        var profile = new AdapterBuildProfile("fixture", WorkshopChartModule.Fingerprint, "fixture", "2022.3.62f2",
            "fixture", "fixture", new Dictionary<string, TypeBinding>());
        var transport = new NativeLocalRunResultSinkRegression.IdleCompositionTransport();
        var log = new CompositionLogger(); var uiReads = 0;
        var module = new LocalRunProtectionModule(native.Api, fixture.Main, log, profile, "unused.dll",
            () => { uiReads++; throw new InvalidOperationException("Controlled UI is unavailable."); });
        using var catalog = new LocalCatalogService(fixture.Main, fixture.Content, log, module);
        var provider = catalog.ForPlugin(context); var selector = catalog.SelectionForPlugin(context);
        var song = fixture.Resources(context);
        using var registration = Need(provider.RegisterSong(song with { Charts = new[] {
            song.Charts[0], song.Charts[0] with { Id = "playable", Playback = AuthoredPlayback() }
        } }));
        using var selected = Need(selector.Select(registration.Snapshot.Song, "easy"));
        Equal(ErrorCode.CapabilityUnavailable, selected.RequestPlay().Error!.Code); // The five base gates are absent.

        var hostField = typeof(LocalRunProtectionModule).GetField("_host", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var runtimeField = typeof(LocalRunProtectionModule).GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var missingUi = new LocalRunProtectionModule(native.Api, fixture.Main, log, profile, "unused.dll");
        var missingUiHost = LocalRunProtectionHost.Create(fixture.Main, log, new NativeLocalRunResultSinkRegression.IdleCompositionTransport());
        hostField.SetValue(missingUi, missingUiHost);
        try
        {
            using var request = Need(catalog.Prepare(selected));
            var error = missingUi.RequestPlay(request, default).Error!;
            Equal(ErrorCode.CapabilityUnavailable, error.Code);
            Check(error.Message.Contains("shared host UI owner", StringComparison.Ordinal));
            Check(runtimeField.GetValue(missingUi) is null);
        }
        finally { missingUi.Release(); }
        Check(missingUiHost.Read().Stopped);

        var host = LocalRunProtectionHost.Create(fixture.Main, log, transport);
        hostField.SetValue(module, host);
        try
        {
            Check(module.Probe().Ready && module.Tick().Ready);
            Check(host.Read() is { AllGatesInstalled: true, RetainedBindings: 5, AcceptingNewRuns: false, HandlerWorkPending: false });
            Check(runtimeField.GetValue(module) is null);
            Equal(1, Need(provider.Read()).Songs.Count); Check(selected.IsCurrent);
            using (Need(catalog.Prepare(selected))) Check(module.Probe().Ready && module.Tick().Ready);
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            Equal(ErrorCode.Cancelled, selected.RequestPlay(cancelled.Token).Error!.Code);
            Check(module.Tick().Ready); Check(runtimeField.GetValue(module) is null);
            Equal(1, log.CompositionRows); Check(log.Composition is { Installed: false, Unknown: false, Reason: null });

            // A catalog entry can legitimately be CPU-only. Trying to play it must
            // not schedule native installation, including on a later ordinary tick.
            var invalid = selected.RequestPlay().Error!;
            Equal(ErrorCode.ContentInvalid, invalid.Code);
            Check(invalid.Message.Contains("playback metadata", StringComparison.Ordinal));
            Check(selected.IsCurrent && catalog.PreparedRequestCount == 0);
            var idle = (NativeLocalAudioContentRuntime?)runtimeField.GetValue(module);
            Check(idle is null || (!idle.InstallationPending && !idle.Installed && idle.Failure is null));
            Check(module.Tick().Ready && host.Read().RetainedBindings == 5);
            Check(log.Composition is { Installed: false, Unknown: false, Reason: null });
            Equal(0, uiReads); Equal(0, native.Invocations); Equal(0, native.NewRoots);

            using var playable = Need(selector.Select(registration.Snapshot.Song, "playable"));
            var error = playable.RequestPlay().Error!;
            Equal(ErrorCode.CapabilityUnavailable, error.Code);
            Check(error.Message.Contains("production engine admission", StringComparison.Ordinal));
            var runtime = (NativeLocalAudioContentRuntime)runtimeField.GetValue(module)!;
            var failure = typeof(NativeLocalAudioContentRuntime).GetProperty(nameof(NativeLocalAudioContentRuntime.Failure),
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            failure.SetValue(runtime, "Controlled prior admission result.");
            Check(module.Tick().Ready); Check(ReferenceEquals(runtime, runtimeField.GetValue(module)));
            Check(log.Composition is { Installed: false, Unknown: false, Reason: { } reason }
                && reason.Contains("production engine admission", StringComparison.Ordinal));
            Equal(0, catalog.PreparedRequestCount); Equal(0, uiReads); Equal(0, native.Invocations); Equal(0, native.NewRoots);
        }
        finally { module.Release(); }
        Check(host.Read().Stopped); Equal(5, transport.Disposals);
        Equal(ErrorCode.TargetExpired, selected.RequestPlay().Error!.Code);
        using var afterShutdown = Need(selector.Select(registration.Snapshot.Song, "playable"));
        Equal(ErrorCode.CapabilityUnavailable, afterShutdown.RequestPlay().Error!.Code);
    }

    private sealed class CompositionLogger : ILogger
    {
        internal int CompositionRows;
        internal (bool Installed, bool Unknown, string? Reason) Composition;
        public void Log(LogLevel level, string message, Exception? exception = null)
        {
            const string prefix = "LocalRunComposition ";
            if (!message.StartsWith(prefix, StringComparison.Ordinal)) return;
            CompositionRows++;
            using var row = JsonDocument.Parse(message[prefix.Length..]);
            var value = row.RootElement;
            Composition = (value.GetProperty("installed").GetBoolean(), value.GetProperty("unknown").GetBoolean(),
                value.GetProperty("reason").GetString());
        }
    }

    private sealed class PlayTarget : ILocalRunPlayDispatcher
    {
        internal int Calls;
        internal LocalPlayRequest? Borrowed;
        internal Func<LocalPlayRequest, Result> Execute = _ => Result.Ok();
        public Result RequestPlay(LocalPlayRequest request, CancellationToken token)
        { Calls++; Borrowed = request; return Execute(request); }
    }
}
