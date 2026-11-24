using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.AudioContent;

/// <summary>Opt-in experiment over a private CPU catalog and two independent native graphs.
/// Reads judging windows from an existing ordinary run; never injects, activates or saves a LOCAL run.</summary>
internal sealed class LocalNativeBundleProbe : IDisposable
{
    private static readonly HashSet<LocalNativeBundleProbe> ProcessOwners = new();
    private readonly NativeRuntime _api; private readonly IMainThreadDispatcher _main; private readonly ILogger _log;
    private readonly AdapterBuildProfile _profile; private readonly IntPtr _module;
    private readonly NativeRuntime.RootHandle?[] _source = new NativeRuntime.RootHandle?[6];
    private readonly NativeRuntime.RootHandle?[] _candidate = new NativeRuntime.RootHandle?[6];
    private LocalContentService? _content; private LocalCatalogService? _catalog; private ProbeContext? _context;
    private LocalPlayRequest? _request; private OwnedNativeLocalPlayBundle? _first, _second;
    private LocalNativePlayBundleObservation? _firstRead, _secondRead;
    private IntPtr _time, _unityObject; private int _phase, _attempts, _createdFrame, _firstDestroyFrame, _secondDestroyFrame, _polls;
    private bool _started, _failed, _sourceFreeAttempted, _candidateCleanupUnknown;
    internal LocalNativeBundleProbe(NativeRuntime api, IMainThreadDispatcher main, ILogger log, AdapterBuildProfile profile, IntPtr module)
        => (_api, _main, _log, _profile, _module) = (api, main, log, profile, module);
    internal void TryStart()
    {
        _main.VerifyAccess(); if (_started) return;
        if (ProcessOwners.Count >= 4) throw new InvalidOperationException("Bundle probe responsibility quota is exhausted.");
        ProcessOwners.Add(this); _started = true;
        _unityObject = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Object");
        _time = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Time");
        Log("waiting", new { requiresObservedOrdinaryRunJudgeData = true, maximumAttempts = 300, controllerCreated = false });
    }
    internal void Tick()
    {
        _main.VerifyAccess(); if (!_started || _failed || _phase == 4) return;
        try
        {
            if (_phase == 0)
            {
                if (++_attempts > 300) throw new TimeoutException("No live ordinary controller supplied real judge settings within 300 module ticks.");
                if (!TryCaptureJudge())
                { if (_attempts == 1 || _attempts % 60 == 0) Log("not-ready", new { attempt = _attempts, reason = "Start an ordinary stock song; no zero/default judge settings are substituted." }); return; }
                PreparePrivateCatalog();
                var selection = _request!.Selection;
                // Credits/score metadata are explicit sentinels. No note-count scoring formula
                // is inferred and none of these test metadata enter an actual controller.
                var descriptor = new LocalNativeMetadataDescriptor(selection.Song.Song, selection.Song.Revision, selection.Chart.Id,
                    selection.Song.Title, selection.Song.Artist, "LOCAL", "TEST", "RizToolbox", new(.2f, .4f, .6f, 1), 0, 0);
                var factory = new NativeLocalPlayBundleFactory(_api, _main, _profile, _module);
                try { _first = factory.CreateBorrowing(_request, descriptor, _source[5]!); }
                catch (NativeLocalPlayBundleCreationException error) { _first = error.Responsibility; throw; }
                try { _second = factory.CreateBorrowing(_request, descriptor, _source[5]!); }
                catch (NativeLocalPlayBundleCreationException error) { _second = error.Responsibility; throw; }
                _firstRead = _first.Read(); _secondRead = _second.Read();
                if (_firstRead.ParametersIdentity == _secondRead.ParametersIdentity
                    || _firstRead.Parameters.Metadata == _secondRead.Parameters.Metadata || _firstRead.Parameters.Chart == _secondRead.Parameters.Chart
                    || _firstRead.Parameters.JudgeData == _secondRead.Parameters.JudgeData || _firstRead.Audio == _secondRead.Audio
                    || _firstRead.Sprite == _secondRead.Sprite || _firstRead.Texture == _secondRead.Texture
                    || _first.DirectRootRecords != 5 || _second.DirectRootRecords != 5)
                    throw new InvalidDataException("Per-run native bundle graphs or fixed root records were not independent.");
                _createdFrame = Frame(); _phase = 1;
                Log("constructed-first", new { frame = _createdFrame, observation = _firstRead, directRootRecords = 5 });
                Log("constructed-second", new { frame = _createdFrame, observation = _secondRead, directRootRecords = 5,
                    realSourceController = _source[3]!.Target.ToInt64(), realSourceJudgeData = _source[5]!.Target.ToInt64(),
                    distinctGraphs = true, scoresAreSentinels = true, nativeControllerCreated = false, canLaunch = false });
                return;
            }
            if (_phase == 1)
            {
                if (Frame() <= _createdFrame) return;
                _context!.Dispose(); _catalog!.Dispose(); _content!.Dispose(); _request!.Dispose();
                if (_catalog.PreparedRequestCount != 2) throw new InvalidDataException("Native bundles did not retain exactly their two private CPU request forks.");
                Collect(); RequireReadbacks();
                _first!.Dispose(); _firstDestroyFrame = Frame(); _phase = 2;
                Log("withdrawn-and-first-destroy-requested", new { frame = _firstDestroyFrame, constructedFrame = _createdFrame,
                    providerAndCatalogAndContentWithdrawn = true, callerRequestDisposed = true, preparedRequests = _catalog.PreparedRequestCount,
                    nativeGcReadbacksUnchanged = true, firstDirectRoots = _first.DirectRootRecords, secondDirectRoots = _second!.DirectRootRecords,
                    destructionVerified = false, canLaunch = false });
                return;
            }
            if (_phase == 2)
            {
                if (Frame() <= _firstDestroyFrame) return;
                if (!_first!.TryCompleteDestroy()) { if (++_polls < 8) return; throw new InvalidDataException("First bundle cleanup remains unconfirmed."); }
                if (!_first.IsClosed || _first.DirectRootRecords != 0 || _catalog!.PreparedRequestCount != 1)
                    throw new InvalidDataException("First bundle did not return all direct roots and exactly one CPU fork.");
                Collect(); if (_second!.Read() != _secondRead) throw new InvalidDataException("First retirement changed the second bundle.");
                var rejected = false; try { _first.Read(); } catch (ObjectDisposedException) { rejected = true; }
                if (!rejected) throw new InvalidDataException("Retired bundle allowed native reads.");
                _second.Dispose(); _secondDestroyFrame = Frame(); _polls = 0; _phase = 3;
                Log("first-verified-second-destroy-requested", new { frame = _secondDestroyFrame, firstDestructionRequestedFrame = _firstDestroyFrame,
                    firstClosed = true, firstDirectRoots = 0, preparedRequests = 1, firstReadRejected = true,
                    secondAfterNativeGcUnchanged = true, secondDirectRoots = _second.DirectRootRecords, destructionVerified = false });
                return;
            }
            if (_phase == 3)
            {
                var frame = Frame(); if (frame <= _secondDestroyFrame) return;
                if (!_second!.TryCompleteDestroy()) { if (++_polls < 8) return; throw new InvalidDataException("Second bundle cleanup remains unconfirmed."); }
                if (!_second.IsClosed || _second.DirectRootRecords != 0 || _catalog!.PreparedRequestCount != 0
                    || _catalog.RetainedCount != 0 || _content!.RetainedCount != 0 || _content.HasUnconfirmedCleanup || _catalog.HasUnconfirmedCleanup)
                    throw new InvalidDataException("Private native/CPU ownership did not fully retire.");
                var sourceRecords = _source.Count(x => x is not null);
                if (sourceRecords != 6) throw new InvalidDataException("The complete source observation must retain six roots before retirement.");
                FreeSourceRoots(); _phase = 4; ProcessOwners.Remove(this);
                Log("verified", new { frame, constructedFrame = _createdFrame, firstDestructionRequestedFrame = _firstDestroyFrame,
                    secondDestructionRequestedFrame = _secondDestroyFrame, allSixOwnedUnityResourcesDestroyed = true,
                    bothNativeGraphsClosed = true, allTenDirectRootsReleased = true, sharedCpuTemplateReleased = true,
                    preparedRequests = 0, retainedSongs = 0, retainedContent = 0, sourceObservationRootsReleased = true,
                    sourceRootRecordsReleased = sourceRecords, sourceRootRecordsRemaining = _source.Count(x => x is not null),
                    controllerCreated = false, gameCatalogModified = false, gameResultOrSavePathInvoked = false, canLaunch = false });
            }
        }
        catch (Exception error) { Fail(error); }
    }
    private bool TryCaptureJudge()
    {
      if (_candidateCleanupUnknown) throw new InvalidOperationException("Unknown candidate root cleanup cannot be retried.");
      try
      {
        var managerClass = _api.Class("Assembly-CSharp.dll", "", "GameManager");
        var manager = _api.ReadStatic<IntPtr>(managerClass, "Instance"); if (manager == IntPtr.Zero) return false;
        HoldCandidate(0, manager);
        var control = _api.Read<IntPtr>(manager, "inLevelControl"); if (control == IntPtr.Zero) return false;
        HoldCandidate(1, control);
        var callback = _api.Read<IntPtr>(control, "GetJudge"); if (callback == IntPtr.Zero) return false;
        HoldCandidate(2, callback);
        var getter = _api.InspectDeclaredMethod(_api.Class("mscorlib.dll", "System", "Delegate"), "get_Target", Array.Empty<string>(), "System.Object", _module);
        if (getter.Entry is not { Rva: 0x374D80 }) throw new UnsupportedSignatureException("Observed delegate Target getter is not pinned.");
        var controller = _api.Invoke(getter.MethodInfo, callback); if (controller == IntPtr.Zero) return false;
        HoldCandidate(3, controller);
        if (_api.ObjectClass(controller) != _api.Class("Assembly-CSharp.dll", "", "NormalModeInLevelController")
            || !Alive(controller) || !_api.Unbox<bool>(_api.Call(controller, "get_isActiveAndEnabled"))) return false;
        var parameters = _api.Read<IntPtr>(controller, "LevelParams"); if (parameters == IntPtr.Zero) return false;
        HoldCandidate(4, parameters);
        var judge = _api.Read<IntPtr>(parameters, "judgeData"); if (judge == IntPtr.Zero) return false;
        HoldCandidate(5, judge);
        var judgeClass = _api.Class("Assembly-CSharp.dll", "", "JudgeData");
        if (_api.ObjectClass(judge) != judgeClass) throw new UnsupportedSignatureException("Observed judging data class differs from the exact JudgeData contract.");
        var fields = new[] { "judgeTime", "normalPerfectJudgeTimeInChallengeTime", "maxPerfectJudgeTimeInChallengeTime", "badTime" };
        for (var i = 0; i < fields.Length; i++)
        {
            _api.RequireInstanceFieldLayout(judgeClass, fields[i], "System.Single", 0x10 + i * 4);
            var value = _api.Read<float>(judge, fields[i]);
            if (!float.IsFinite(value) || value <= 0) return false;
        }
        if (_api.ReadStatic<IntPtr>(managerClass, "Instance") != manager || _api.Read<IntPtr>(manager, "inLevelControl") != control
            || _api.Read<IntPtr>(control, "GetJudge") != callback || _api.Read<IntPtr>(controller, "LevelParams") != parameters
            || _api.Read<IntPtr>(parameters, "judgeData") != judge) return false;
        // Publish only a complete ready snapshot. Rejected menu/old controller observations
        // never pin the next attempt to stale identities.
        for (var i = 0; i < _source.Length; i++) { _source[i] = _candidate[i]; _candidate[i] = null; }
        return true;
      }
      finally { FreeCandidateRoots(); }
    }
    private void HoldCandidate(int slot, IntPtr value) => _candidate[slot] = _api.Root(value);
    private void FreeCandidateRoots()
    {
        var errors = new List<Exception>();
        for (var i = _candidate.Length - 1; i >= 0; i--) if (_candidate[i] is { } root)
            try { root.Dispose(); _candidate[i] = null; } catch (Exception error) { errors.Add(error); }
        if (errors.Count != 0) { _candidateCleanupUnknown = true; throw new AggregateException("Candidate observation cleanup is unknown; failed records remain owned without retry.", errors); }
    }
    private void PreparePrivateCatalog()
    {
        var directory = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "BepInEx", "RizToolbox", "probes", "bundle-fixtures-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(directory)) throw new IOException("Probe fixture directory already exists.");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "tone.wav"), BundleProbeFixtures.Wav());
        File.WriteAllBytes(Path.Combine(directory, "cover.png"), BundleProbeFixtures.Png());
        File.WriteAllText(Path.Combine(directory, "chart.json"), BundleProbeFixtures.Chart, new UTF8Encoding(false));
        Log("fixture-directory", new { path = directory, originalTestData = true, gameCatalogModified = false });
        _context = new(_main, _log);
        _content = new(_main, new ContentFileAccess(_ => directory, _ => directory)); _content.SetChartParser(new NativeChartParser(_api));
        _catalog = new(_main, _content, _log);
        var batch = Need(_content.BatchForPlugin(_context).RegisterBatch(new[] {
            new LocalContentRequest("audio", LocalContentKind.Audio, "wav.pcm16", 1, new(ContentFileArea.Package, "tone.wav")),
            new LocalContentRequest("cover", LocalContentKind.Cover, "png.rgba8", 1, new(ContentFileArea.Package, "cover.png")),
            new LocalContentRequest("chart", LocalContentKind.Chart, "rizline.chart.json", 0, new(ContentFileArea.Package, "chart.json")) }));
        var song = Need(_catalog.ForPlugin(_context).RegisterSong(new("song", "LOCAL Bundle Sentinel", "RizToolbox original test data",
            new(_context.PluginId, "audio"), new(_context.PluginId, "cover"), new[] { new LocalSongChartRequest("chart", "LOCAL", new(_context.PluginId, "chart")) })));
        var selected = Need(_catalog.SelectionForPlugin(_context).Select(song.Snapshot.Song, "chart")); _request = Need(_catalog.Prepare(selected));
    }
    private void RequireReadbacks() { if (_first!.Read() != _firstRead || _second!.Read() != _secondRead) throw new InvalidDataException("Bundle readbacks changed across withdrawal/native collection."); }
    private void Collect() => _api.Invoke(_api.Method(_api.Class("mscorlib.dll", "System", "GC"), "Collect", 0, true, Array.Empty<string>()), IntPtr.Zero);
    private int Frame() => _api.Unbox<int>(_api.CallStatic(_time, "get_frameCount"));
    private bool Alive(IntPtr value) => _api.Unbox<bool>(_api.CallStatic(_unityObject, "op_Implicit", A.Ref(value)));
    private void FreeSourceRoots()
    {
        if (_sourceFreeAttempted) throw new InvalidOperationException("Unknown source root free cannot be retried.");
        _sourceFreeAttempted = true; var errors = new List<Exception>();
        for (var i = _source.Length - 1; i >= 0; i--) if (_source[i] is { } root)
            try { root.Dispose(); _source[i] = null; } catch (Exception error) { errors.Add(error); }
        if (errors.Count != 0) throw new AggregateException("Source observation roots retain unknown cleanup.", errors);
    }
    private static T Need<T>(Result<T> result) => result.Success ? result.Value! : throw new InvalidDataException(result.Error!.Message);
    private void Log(string phase, object value)
    {
        var body = JsonSerializer.Serialize(value); if (body.Length > 1800) throw new InvalidDataException("Bundle probe row exceeds its bound.");
        _log.Log(LogLevel.Info, "LocalNativeBundleProbe " + phase + " " + body);
    }
    private void Fail(Exception error)
    {
        _failed = true;
        try { _log.Log(LogLevel.Error, "LocalNativeBundleProbe unavailable; bounded responsibility retained until process exit: " + error.GetType().Name + ": " + error.Message); } catch { }
    }
    public void Dispose()
    {
        _main.VerifyAccess(); if (!_started || _phase == 4 || _failed) return;
        if (_phase == 0 && _first is null && _second is null && _content is null)
        { try { FreeSourceRoots(); _phase = 4; ProcessOwners.Remove(this); } catch (Exception error) { Fail(error); } return; }
        Fail(new InvalidOperationException("Module ended before native bundle cleanup verification."));
    }
    private sealed class ProbeContext : IPluginContext, IDisposable
    {
        private readonly Stack<IDisposable> _owned = new(); private bool _disposed;
        internal ProbeContext(IMainThreadDispatcher main, ILogger log) => (MainThread, Log) = (main, log);
        public string PluginId => "probe.bundle";
        public ILogger Log { get; } public IMainThreadDispatcher MainThread { get; }
        public IEventBus Events => throw new NotSupportedException();
        public GameBuildDescriptor GameBuild => new("probe", "probe", "probe", "probe");
        public CapabilityDescriptor GetCapability(string id) => new(id, false);
        public ServiceLookup<T> GetService<T>(string id) where T : class => new(false, null, new(ErrorCode.CapabilityUnavailable,"Private fixture context.",id));
        public T Own<T>(T resource) where T : IDisposable { MainThread.VerifyAccess(); if (_disposed) throw new ObjectDisposedException(nameof(ProbeContext)); _owned.Push(resource); return resource; }
        public void Dispose()
        {
            MainThread.VerifyAccess(); if (_disposed) return; _disposed = true; var errors = new List<Exception>();
            while (_owned.Count != 0) try { _owned.Pop().Dispose(); } catch (Exception error) { errors.Add(error); }
            if (errors.Count != 0) throw new AggregateException("Private probe scope cleanup is unconfirmed.", errors);
        }
    }
}
