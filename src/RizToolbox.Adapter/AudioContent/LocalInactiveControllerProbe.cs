using System.Diagnostics;
using System.Text.Json;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.AudioContent;

/// <summary>Explicit inactive-clone lifecycle experiment. No activation, gameplay parameters, result or save invocation.</summary>
internal sealed class LocalInactiveControllerProbe : IDisposable
{
    private static readonly HashSet<LocalInactiveControllerProbe> ProcessOwners = new();
    private readonly NativeRuntime _api;
    private readonly IMainThreadDispatcher _main;
    private readonly ILogger _log;
    private readonly AdapterBuildProfile _profile;
    private readonly IntPtr _module;
    private readonly ExactHookBroker _broker;
    private OwnedNativeControllerPrefab? _prefab;
    private OwnedNativeControllerGlobals? _globals;
    private NativeControllerAwakeLedger? _ledger;
    private NativeControllerAwakeLedger.CaptureWindow? _window;
    private OwnedNativeInactiveController? _clone;
    private NativeRuntime.RootHandle? _manager, _control, _source, _cloneObservation;
    private ControllerGlobalsSnapshot? _baseline;
    private NativeInactiveControllerObservation? _created;
    private IntPtr _time, _unityObject;
    private int _phase, _attempts, _createdFrame, _destroyFrame, _polls;
    private long _nextAttempt;
    private bool _started, _freeAttempted, _cleanupUnknown;

    internal LocalInactiveControllerProbe(NativeRuntime api, IMainThreadDispatcher main, ILogger log,
        AdapterBuildProfile profile, IntPtr module, ExactHookBroker broker)
        => (_api, _main, _log, _profile, _module, _broker) = (api, main, log, profile, module, broker);

    internal void TryStart()
    {
        _main.VerifyAccess(); if (_started) return;
        if (ProcessOwners.Count >= 4) throw new InvalidOperationException("Inactive probe responsibility capacity is exhausted.");
        ProcessOwners.Add(this); _started = true;
        Log("waiting", new { maximumAttempts = 180, minimumIntervalSeconds = 1, requiresSongList = true,
            controllerActivated = false, canLaunch = false });
    }

    internal void Tick()
    {
        _main.VerifyAccess(); if (!_started || _phase >= 3) return;
        try
        {
            if (_phase == 0)
            {
                var now = Stopwatch.GetTimestamp(); if (now < _nextAttempt) return;
                _nextAttempt = now + Stopwatch.Frequency;
                if (++_attempts > 180) throw new TimeoutException("No unique active song card supplied a prefab within 180 discovery attempts.");
                var resolved = new NativeControllerPrefabResolver(_api, _main, _profile, _module).Resolve();
                if (resolved.Status == ControllerPrefabResolutionStatus.NotReady)
                {
                    if (_attempts == 1 || _attempts % 30 == 0)
                        Log("not-ready", new { attempt = _attempts, resolved.Reason, resolved.ScannedElements, resolved.MatchingCards });
                    return;
                }
                if (resolved.Status != ControllerPrefabResolutionStatus.Ready || resolved.Prefab is null)
                    throw new InvalidOperationException("Complete prefab discovery refused: " + resolved.Status + ": " + resolved.Reason);
                _prefab = resolved.Prefab;
                _source = _api.Root(_prefab.BorrowPrefabPointer);
                _unityObject = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Object");
                _time = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Time");
                var managerClass = _api.Class("Assembly-CSharp.dll", "", "GameManager");
                _manager = _api.Root(_api.ReadStatic<IntPtr>(managerClass, "Instance"));
                _control = _api.Root(_api.Read<IntPtr>(_manager.Target, "inLevelControl"));
                _globals = OwnedNativeControllerGlobals.Capture(_api, _main, _profile, _module, _manager, _control);
                _baseline = _globals.BaselineSnapshot(); RequireUnchangedGlobals();
                _ledger = NativeControllerAwakeLedger.Bind(_api, _main, _profile, _module, _broker, Frame);
                _window = _ledger.BeginCaptureWindow("inactive-clone-lifecycle");
                // Revalidate the semantic source immediately before construction. A retained card
                // across later frames is not a new assertion of global prefab uniqueness.
                _prefab.VerifyStillCurrent();
                try { _clone = new NativeInactiveControllerFactory(_api, _main, _profile, _module).CreateBorrowing(_source); }
                catch (NativeInactiveControllerCreationException error) { _clone = error.Responsibility; throw; }
                _cloneObservation = _clone.RetainController();
                _created = _clone.Read(); _createdFrame = Frame();
                var awake = RequireNoAwake(); var currentGlobals = RequireUnchangedGlobals(); RequireSource();
                if (_created.Controller == _source.Target.ToInt64() || _clone.RootRecords != 8)
                    throw new InvalidDataException("Inactive clone independence or complete seven-root responsibility differs.");
                _phase = 1;
                Log("constructed", new { frame = _createdFrame, attempt = _attempts, resolved.ScannedElements, resolved.MatchingCards,
                    sourcePrefab = _source.Target.ToInt64(), cardOwner = _prefab.CardOwnerIdentity,
                    rootRecords = _clone.RootRecords, observation = _created, globalsUnchanged = true,
                    globalsBaseline = _baseline, globalsAfterClone = currentGlobals,
                    awake.TimingVerifiable, awake.NativeCalls, awake.OriginalCalls, awake.CloneBefore, awake.CloneAfter,
                    controllerActivated = false, gameResultOrSavePathInvoked = false, canLaunch = false });
                return;
            }
            if (_phase == 1)
            {
                var frame = Frame(); if (frame <= _createdFrame) return;
                Collect(); var readback = _clone!.Read();
                if (readback != _created) throw new InvalidDataException("Inactive clone graph changed across native GC and a later frame.");
                var awake = RequireNoAwake(); var currentGlobals = RequireUnchangedGlobals(); RequireSource();
                _clone.RequestDestroy(); _destroyFrame = Frame(); _phase = 2;
                Log("destroy-requested", new { frame = _destroyFrame, constructedFrame = _createdFrame,
                    nativeGcCollectReturned = true, inactiveReadbackUnchanged = true, sourceStillCurrent = true,
                    globalsUnchanged = true, awake.TimingVerifiable, awake.NativeCalls, awake.OriginalCalls,
                    globalsAfterGc = currentGlobals,
                    awake.CloneBefore, awake.CloneAfter, rootRecords = _clone.RootRecords,
                    destructionVerified = false, controllerActivated = false });
                return;
            }
            if (_phase == 2)
            {
                var frame = Frame(); if (frame <= _destroyFrame) return;
                RequireNoAwake(); RequireUnchangedGlobals(); RequireSource();
                if (!_clone!.TryCompleteDestroy())
                { if (++_polls < 8) return; throw new InvalidDataException("Owned inactive controller destruction remains unconfirmed after eight later-frame polls."); }
                if (!_clone.IsClosed || _clone.CleanupUnknown || _clone.RootRecords != 0)
                    throw new InvalidDataException("Owned inactive clone roots were not completely released.");
                // Only after no publication and delayed Unity destruction were verified can
                // this read-only globals baseline and the separate observation roots end.
                ReleaseObservations(); _phase = 3; ProcessOwners.Remove(this);
                Log("verified", new { frame, constructedFrame = _createdFrame, destructionRequestedFrame = _destroyFrame,
                    cloneAndGameObjectAndParentAndTransformsDestroyed = true, ownedRootRecordsReleased = 8,
                    observationRootsReleased = true, prefabSourcePreserved = true, globalsUnchanged = true,
                    awakeBefore = 0, awakeAfter = 0, nativeGcReadback = true, controllerActivated = false,
                    parametersWritten = false, gameCatalogModified = false, gameResultOrSavePathInvoked = false, canLaunch = false });
            }
        }
        catch (Exception error) { Fail(error); }
    }

    private ControllerAwakeLedgerSnapshot RequireNoAwake()
    {
        var snapshot = _window!.SnapshotFor(_cloneObservation!);
        if (!snapshot.TimingVerifiable || snapshot.CloneBefore != 0 || snapshot.CloneAfter != 0
            || snapshot.NativeCalls != 0 || snapshot.OriginalCalls != 0)
            throw new InvalidDataException("Inactive construction Awake timing is not verified as zero: " + JsonSerializer.Serialize(snapshot));
        return snapshot;
    }
    private ControllerGlobalsSnapshot RequireUnchangedGlobals()
    {
        var current = _globals!.ObserveCurrent(); var before = _baseline!;
        if (!current.IsCurrentOwner || !before.IsCurrentOwner || current.OwnerIdentity != before.OwnerIdentity
            || current.ControllerSet != before.ControllerSet || !current.DelegateIdentities.SequenceEqual(before.DelegateIdentities))
            throw new InvalidDataException("Inactive controller changed the retained six-delegate/flag/manager baseline.");
        return current;
    }
    private void RequireSource()
    {
        _prefab!.VerifyStillCurrent();
        if (_prefab.BorrowPrefabPointer != _source!.Target || !Alive(_source.Target)
            || _api.Read<IntPtr>(_source.Target, "LevelParams") != IntPtr.Zero
            || _api.Read<IntPtr>(_source.Target, "LevelInformation") != IntPtr.Zero)
            throw new InvalidDataException("The retained source prefab changed or acquired gameplay state.");
    }
    private int Frame() => _api.Unbox<int>(_api.CallStatic(_time, "get_frameCount"));
    private bool Alive(IntPtr pointer) => _api.Unbox<bool>(_api.CallStatic(_unityObject, "op_Implicit", A.Ref(pointer)));
    private void Collect()
    {
        var gc = _api.Class("mscorlib.dll", "System", "GC"); var method = _api.Method(gc, "Collect", 0, true, Array.Empty<string>());
        if (_api.DescribeReturnType(method) != "System.Void") throw new UnsupportedSignatureException("Native GC signature changed.");
        _api.Invoke(method, IntPtr.Zero);
    }
    private void ReleaseObservations()
    {
        if (_freeAttempted) throw new InvalidOperationException("Unknown probe frees are not retried.");
        _freeAttempted = true; var failures = new List<Exception>();
        void Attempt(Action action) { try { action(); } catch (Exception error) { failures.Add(error); } }
        Attempt(() => _window!.Dispose()); Attempt(() => _ledger!.Dispose());
        Attempt(() => _globals!.Dispose()); Attempt(() => _cloneObservation!.Dispose());
        Attempt(() => _source!.Dispose()); Attempt(() => _prefab!.Dispose());
        Attempt(() => _control!.Dispose()); Attempt(() => _manager!.Dispose());
        if (failures.Count != 0) throw new AggregateException("Inactive probe observation cleanup is unconfirmed.", failures);
        _window = null; _ledger = null; _globals = null; _cloneObservation = null;
        _source = null; _prefab = null; _control = null; _manager = null;
    }
    private void Fail(Exception error)
    {
        _phase = 4; _cleanupUnknown = true;
        // In particular, an unexpected Awake may have published callbacks. Never implicitly
        // destroy it or discard baseline roots: recovery requires the publication/consumer proof.
        try { _log.Log(LogLevel.Error, "LocalInactiveControllerProbe unavailable; bounded responsibility retained until process exit: "
            + error.GetType().Name + ": " + error.Message); } catch { }
    }
    private void Log(string phase, object values)
    {
        var body = JsonSerializer.Serialize(values); if (body.Length > 1800) throw new InvalidDataException("Inactive probe diagnostic row exceeds its bound.");
        _log.Log(LogLevel.Info, "LocalInactiveControllerProbe " + phase + " " + body);
    }
    public void Dispose()
    {
        _main.VerifyAccess(); if (!_started || _phase == 3 || _cleanupUnknown) return;
        if (_phase == 0 && _prefab is null && _globals is null && _clone is null)
        { _phase = 3; ProcessOwners.Remove(this); return; }
        Fail(new InvalidOperationException("Module stopped before inactive construction/destruction verification finished."));
    }
}
