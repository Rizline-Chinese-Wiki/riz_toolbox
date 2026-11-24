using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal enum LocalMenuOperation { SetActive, Lock, Unlock, UnlockAll }
internal sealed record NativeMenuObject(long Component, long GameObject, bool Current, bool Alive, bool ActiveSelf);
internal sealed record NativeMenuLock(long Identity, bool Current, int Count);
internal sealed record NativeMenuObservation(long Manager, bool CurrentManager, NativeMenuObject Camera,
    NativeMenuObject Particle, NativeMenuLock Normal, NativeMenuLock Back);
internal interface INativeLocalMenuBackend
{
    bool CleanupUnknown { get; }
    int RootRecords { get; }
    NativeMenuObservation Capture();
    NativeMenuObservation Read();
    void Bind(NativeLocalMenuLedger owner);
    ExactHookBroker.ManagedBoundaryActivity ReadActivity();
    void Invoke(LocalMenuOperation operation, int slot, byte value);
    void ReleaseObservers();
}
internal sealed record NativeLocalMenuSnapshot(bool LaunchAttempted, bool LaunchSceneCompleted, bool ReturnAttempted,
    bool Restored, bool Closed, bool Unknown, bool NormalCreditHeld, bool BackCreditHeld,
    bool CameraRestoreRevoked, bool ParticleRestoreRevoked, long Entered, long Exited, int Roots);

/// <summary>Owns only its explicit menu mutations and independent observer roots.
/// It never supplies a run consumer-end proof or balances unknown foreign credits.</summary>
internal sealed class NativeLocalMenuLedger
{
    private static readonly object Gate = new();
    private static readonly HashSet<NativeLocalMenuLedger> Owners = new();
    private readonly IMainThreadDispatcher _main;
    private readonly INativeLocalMenuBackend _backend;
    private readonly LocalRunLaunchCommitState? _launch;
    private readonly NativeLocalInitializationCoroutineTracker? _initialCoroutines;
    private readonly bool _controlled;
    private readonly Frame[] _frames;
    private readonly long[] _identities = new long[4]; // camera GO, particle GO, normal, back
    private readonly int[] _counts = new int[2];
    private readonly bool[] _lastActive = new bool[2], _changed = new bool[2], _revoked = new bool[2], _credits = new bool[2];
    private long _entered, _exited, _captured, _disposed;
    private int _fault;
    private bool _busy, _attempted, _initialDone, _returnAttempted, _restored, _closed, _releaseAttempted;
    private int _expectedSlot = -1; private LocalMenuOperation _expectedOperation; private bool _seenExpected, _finishedExpected;
    private NativeMenuObservation _baseline = null!;
    private NativeLocalRunSceneTransition? _initialOwner;
    private NativeLocalRunSceneTransitionReceipt? _initial;
    private NativeLocalMenuLedger(IMainThreadDispatcher main, INativeLocalMenuBackend backend,
        LocalRunLaunchCommitState? launch, bool controlled, NativeLocalInitializationCoroutineTracker? initialCoroutines)
    {
        (_main, _backend, _launch, _controlled, _initialCoroutines) = (main, backend, launch, controlled, initialCoroutines);
        _frames = Enumerable.Range(0, 32).Select(_ => new Frame(this)).ToArray();
    }
    internal static NativeLocalMenuLedger Capture(NativeRuntime api, IMainThreadDispatcher main, AdapterBuildProfile profile,
        IntPtr module, ExactHookBroker broker, NativeRuntime.RootHandle manager, LocalRunLaunchCommitState launch,
        NativeLocalInitializationCoroutineTracker initialCoroutines)
    {
        ArgumentNullException.ThrowIfNull(main); main.VerifyAccess(); ArgumentNullException.ThrowIfNull(launch);
        VerifyLaunch(launch);
        ArgumentNullException.ThrowIfNull(initialCoroutines);
        if (!initialCoroutines.IsFor(launch)) throw new InvalidOperationException("Menu handoff requires this same launch's production coroutine tracker.");
        return CaptureCore(main, new NativeLocalMenuBackend(api, main, profile, module, broker, manager), launch, false, initialCoroutines);
    }
    internal static NativeLocalMenuLedger CaptureBackend(IMainThreadDispatcher main, INativeLocalMenuBackend backend,
        NativeLocalInitializationCoroutineTracker? initialCoroutines = null)
    {
        main.VerifyAccess();
        if (backend is NativeLocalMenuBackend) throw new InvalidOperationException("A controlled ledger cannot adopt a production backend.");
        if (initialCoroutines is { IsControlled: false }) throw new InvalidOperationException("Controlled menu capture cannot adopt production coroutine authority.");
        return CaptureCore(main, backend, null, true, initialCoroutines);
    }
    private static NativeLocalMenuLedger CaptureCore(IMainThreadDispatcher main, INativeLocalMenuBackend backend,
        LocalRunLaunchCommitState? launch, bool controlled, NativeLocalInitializationCoroutineTracker? initialCoroutines)
    {
        ArgumentNullException.ThrowIfNull(backend); var owner = new NativeLocalMenuLedger(main, backend, launch, controlled, initialCoroutines);
        lock (Gate) { if (Owners.Count >= 32) throw new InvalidOperationException("Menu ledger process capacity exhausted."); Owners.Add(owner); }
        try
        {
            owner._baseline = backend.Capture(); owner.ValidateInitial(owner._baseline);
            var state = owner._baseline;
            owner._identities[0] = state.Camera.GameObject; owner._identities[1] = state.Particle.GameObject;
            owner._identities[2] = state.Normal.Identity; owner._identities[3] = state.Back.Identity;
            owner._counts[0] = state.Normal.Count; owner._counts[1] = state.Back.Count;
            owner._lastActive[0] = state.Camera.ActiveSelf; owner._lastActive[1] = state.Particle.ActiveSelf;
            backend.Bind(owner);
            if (backend.ReadActivity() is not { Known: true, InFlight: 0 })
                throw new InvalidOperationException("Pre-probe native calls must drain before establishing menu ownership.");
            owner.Reconcile();
            // Ownership starts after all four probes are installed. Earlier foreign
            // operations cannot grant us a restore right over their old baseline.
            owner._baseline = backend.Read(); owner.ValidateInitial(owner._baseline);
            return owner;
        }
        catch (Exception error) { owner.Fault(); throw new NativeLocalMenuException(owner, error); }
    }
    private static void VerifyLaunch(LocalRunLaunchCommitState launch)
    {
        launch.VerifyPendingOwnership(launch.Registry); var state = launch.Read();
        if (state.ActivationAttempted || state.EarlyTerminal || state.Phase != LocalRunLaunchCommitPhase.ControllerBound || launch.ParameterBinding is null)
            throw new InvalidOperationException("Menu handoff must follow exact inactive binding and precede activation.");
    }
    internal void BeginLaunchOnce()
    {
        Guard(); if (_attempted) throw new InvalidOperationException("Menu launch handoff is once-only.");
        if (_launch is not null) VerifyLaunch(_launch);
        Execute(() => {
            Reconcile(); _attempted = true;
            Mutate(LocalMenuOperation.Lock, 2); Mutate(LocalMenuOperation.Lock, 3);
            for (var slot = 0; slot < 2; ++slot) if (_lastActive[slot]) Mutate(LocalMenuOperation.SetActive, slot, 0);
        });
    }
    internal void CompleteInitialScene(NativeLocalRunSceneTransition source)
    {
        Guard(); if (_controlled) throw new InvalidOperationException("Controlled ledger requires controlled completion.");
        var receipt = source.VerifyReturnSource(_launch!.Registry, _launch.Session);
        if (receipt.Session != _launch.Identity || receipt.Controller != _launch.Read().Controller)
            throw new InvalidOperationException("Initial scene belongs to another LOCAL launch.");
        CompleteInitial(receipt, source);
    }
    internal void CompleteInitialControlled(NativeLocalRunSceneTransitionReceipt receipt)
    { Guard(); if (!_controlled) throw new InvalidOperationException("Constructed scene receipts cannot authorize production menu work."); CompleteInitial(receipt, null); }
    private void CompleteInitial(NativeLocalRunSceneTransitionReceipt receipt, NativeLocalRunSceneTransition? source)
    {
        if (!_attempted || _initialDone || receipt.Destination.Name != "Level" || !receipt.Destination.Loaded || receipt.AsyncOperation == 0)
            throw new InvalidOperationException("Menu initial scene completion is missing, invalid or already consumed.");
        Execute(() => { Reconcile(); if (_credits[0]) Mutate(LocalMenuOperation.Unlock, 2); _initial = receipt; _initialOwner = source; _initialDone = true; });
    }
    internal void BeginReturnOnce()
    {
        Guard(); if (!_initialDone || _returnAttempted) throw new InvalidOperationException("Menu return requires the completed first scene, once.");
        if (_launch is not null && _launch.Registry.Read(_launch.Session).Phase != LocalRunSessionPhase.Returning)
            throw new InvalidOperationException("Registry.BeginReturn must precede native menu return work.");
        if (_initialCoroutines is not null && !_initialCoroutines.TryConfirmNaturalEndForReturn())
            throw new InvalidOperationException("Initial getter coroutines must naturally end before acquiring a return lock credit.");
        Execute(() => { Reconcile(); _returnAttempted = true; Mutate(LocalMenuOperation.Lock, 2); });
    }
    internal void RestoreAfterReturn(NativeLocalRunReturnTransition returned)
    {
        Guard(); if (_controlled || _initialOwner is null) throw new InvalidOperationException("Production menu restore requires actual scene owners.");
        var receipt = returned.VerifyMenuSource(_initialOwner, _launch!.Registry, _launch.Session); Restore(receipt);
    }
    internal void RestoreControlled(NativeLocalRunReturnTransitionReceipt receipt)
    { Guard(); if (!_controlled) throw new InvalidOperationException("Constructed return cannot authorize production menu work."); Restore(receipt); }
    private void Restore(NativeLocalRunReturnTransitionReceipt receipt)
    {
        if (!_returnAttempted || _restored || _initial is null || receipt.Session != _initial.Session || receipt.Controller != _initial.Controller
            || receipt.OriginLevel != _initial.Destination || receipt.OriginalMenu != _initial.Origin
            || !receipt.ReturnedScene.Valid || !receipt.ReturnedScene.Loaded || receipt.ReturnedScene.Name != _initial.Origin.Name)
            throw new InvalidOperationException("Menu restore requires this exact launch's completed return and unloaded Level.");
        Execute(() => {
            Reconcile();
            if (_credits[0]) Mutate(LocalMenuOperation.Unlock, 2);
            if (_credits[1]) Mutate(LocalMenuOperation.Unlock, 3);
            for (var slot = 0; slot < 2; ++slot)
            {
                var state = _backend.Read(); var item = slot == 0 ? state.Camera : state.Particle;
                if (_changed[slot] && !_revoked[slot] && item.Current && item.Alive) Mutate(LocalMenuOperation.SetActive, slot, 1);
                // A successor/dead object is never overwritten or revived.
                _changed[slot] = false;
            }
            _restored = true;
        });
    }
    private void Mutate(LocalMenuOperation operation, int slot, byte value = 0)
    {
        Reconcile(); _expectedSlot = slot; _expectedOperation = operation; _seenExpected = _finishedExpected = false;
        var entered = Interlocked.Read(ref _entered);
        try
        {
            _backend.Invoke(operation, slot, value); Reconcile();
            if (!_seenExpected || !_finishedExpected || Interlocked.Read(ref _entered) != entered + 1)
                throw new InvalidOperationException("Native menu mutation did not have one exact paired owned observation.");
        }
        finally { _expectedSlot = -1; }
    }
    internal bool Matches(LocalMenuOperation operation, long receiver)
        => operation == LocalMenuOperation.SetActive ? receiver == _identities[0] || receiver == _identities[1]
            : receiver == _identities[2] || receiver == _identities[3];
    internal void ProbeEnter(LocalMenuOperation operation, long receiver, bool methodMatches)
    {
        if (!Matches(operation, receiver)) return;
        Interlocked.Increment(ref _entered);
        if (!methodMatches || !_main.IsMainThread) Fault();
    }
    internal void ProbeExit(LocalMenuOperation operation, long receiver, bool returned)
    {
        if (!Matches(operation, receiver)) return;
        if (!returned) Fault(); Interlocked.Increment(ref _exited);
    }
    internal IDisposable CaptureInvocation(LocalMenuOperation operation, long receiver, byte value)
    {
        _main.VerifyAccess(); if (Volatile.Read(ref _fault) != 0) throw new InvalidOperationException("Menu observation is unknown.");
        var slot = Array.IndexOf(_identities, receiver);
        if (slot < 0 || operation == LocalMenuOperation.SetActive != (slot < 2)) { Fault(); throw new InvalidOperationException("Wrong menu receiver role."); }
        var frame = _frames.FirstOrDefault(item => !item.Used);
        if (frame is null) { Fault(); throw new InvalidOperationException("Menu observation capacity exhausted."); }
        frame.Used = true; frame.Finished = false; frame.Operation = operation; frame.Slot = slot; frame.Value = value;
        frame.Own = _expectedSlot == slot && _expectedOperation == operation && !_seenExpected;
        if (frame.Own) _seenExpected = true;
        Interlocked.Increment(ref _captured);
        try { ValidateState(_backend.Read()); return frame; }
        catch { frame.Dispose(); Fault(); throw; }
    }
    internal void CompleteInvocation(IDisposable context)
    {
        var frame = (Frame)context; var state = _backend.Read();
        try
        {
            if (frame.Slot < 2)
            {
                var slot = frame.Slot; var item = slot == 0 ? state.Camera : state.Particle;
                if (!item.Alive || frame.Value > 1 || item.ActiveSelf != (frame.Value != 0)) throw new InvalidOperationException("SetActive readback changed.");
                if (frame.Own) _changed[slot] = frame.Value == 0;
                else if (_changed[slot]) { _changed[slot] = false; _revoked[slot] = true; }
                _lastActive[slot] = item.ActiveSelf;
            }
            else
            {
                var slot = frame.Slot - 2; var before = _counts[slot]; var after = slot == 0 ? state.Normal.Count : state.Back.Count;
                var expected = frame.Operation == LocalMenuOperation.Lock ? checked(before + 1)
                    : frame.Operation == LocalMenuOperation.Unlock ? Math.Max(0, before - 1) : 0;
                if (after != expected) throw new InvalidOperationException("MultiLock readback differs from the exact observed operation.");
                if (frame.Own)
                {
                    if (frame.Operation == LocalMenuOperation.Lock && _credits[slot]
                        || frame.Operation == LocalMenuOperation.Unlock && (!_credits[slot] || before <= 0))
                        throw new InvalidOperationException("Owned menu credit cannot be acquired twice or released without ownership.");
                    _credits[slot] = frame.Operation == LocalMenuOperation.Lock;
                }
                else if (frame.Operation == LocalMenuOperation.UnlockAll || frame.Operation == LocalMenuOperation.Unlock && before <= 1)
                    _credits[slot] = false; // Foreign consumption revokes release rights; later locks belong to their callers.
                _counts[slot] = after;
            }
            if (frame.Own) _finishedExpected = true; frame.Finished = true;
        }
        catch { Fault(); throw; }
    }
    private void Reconcile()
    {
        var activity = _backend.ReadActivity();
        if (Volatile.Read(ref _fault) != 0 || _backend.CleanupUnknown || !activity.Known || activity.InFlight < 0
            || Interlocked.Read(ref _entered) != Interlocked.Read(ref _exited)
            || Interlocked.Read(ref _entered) != Interlocked.Read(ref _captured)
            || Interlocked.Read(ref _captured) != Interlocked.Read(ref _disposed))
            throw new InvalidOperationException("Menu callbacks are unpaired, in flight or unknown.");
        ValidateState(_backend.Read());
    }
    private void ValidateInitial(NativeMenuObservation state)
    {
        if (!state.CurrentManager || state.Manager == 0 || !state.Camera.Current || !state.Particle.Current || !state.Camera.Alive || !state.Particle.Alive
            || !state.Normal.Current || !state.Back.Current || state.Normal.Count < 0 || state.Back.Count < 0
            || new[] { state.Camera.GameObject, state.Particle.GameObject, state.Normal.Identity, state.Back.Identity }.Any(item => item == 0)
            || new[] { state.Camera.GameObject, state.Particle.GameObject, state.Normal.Identity, state.Back.Identity }.Distinct().Count() != 4)
            throw new InvalidOperationException("Menu capture requires exact current distinct live objects and nonnegative lock counters.");
    }
    private void ValidateState(NativeMenuObservation state)
    {
        if (!state.CurrentManager || state.Manager != _baseline.Manager || !state.Normal.Current || !state.Back.Current
            || state.Normal.Identity != _identities[2] || state.Back.Identity != _identities[3]
            || state.Normal.Count != _counts[0] || state.Back.Count != _counts[1]
            || state.Normal.Count < 0 || state.Back.Count < 0)
            throw new InvalidOperationException("Menu manager/lock ownership changed outside the recorded operations.");
        if (state.Camera.GameObject != _identities[0] || state.Particle.GameObject != _identities[1]
            || state.Camera.Alive && state.Camera.ActiveSelf != _lastActive[0]
            || state.Particle.Alive && state.Particle.ActiveSelf != _lastActive[1])
            throw new InvalidOperationException("Menu object state changed outside its recorded operations.");
    }
    internal NativeLocalMenuSnapshot Read()
    {
        _main.VerifyAccess(); return new(_attempted, _initialDone, _returnAttempted, _restored, _closed,
            Volatile.Read(ref _fault) != 0 || _backend.CleanupUnknown, _credits[0], _credits[1], _revoked[0], _revoked[1],
            Interlocked.Read(ref _entered), Interlocked.Read(ref _exited), _backend.RootRecords);
    }
    internal bool TryVerifyCurrent()
    {
        Guard(); if (!_initialDone || _returnAttempted) throw new InvalidOperationException("Launch commit requires the initial menu handoff only.");
        if (Interlocked.Read(ref _entered) != Interlocked.Read(ref _exited)
            || Interlocked.Read(ref _captured) != Interlocked.Read(ref _disposed)) return false;
        Execute(Reconcile); return true;
    }
    internal bool TryReleaseAfterRestored()
    {
        Guard(); if (!_restored || _credits.Any(item => item) || _changed.Any(item => item)) throw new InvalidOperationException("Menu ownership has not ended.");
        if (_backend.ReadActivity() is { Known: true, InFlight: > 0 }) return false;
        Execute(() => { Reconcile(); _releaseAttempted = true; _backend.ReleaseObservers(); _closed = true; lock (Gate) Owners.Remove(this); });
        return true;
    }
    internal bool TryReleaseBeforeAttempt()
    {
        Guard(); if (_attempted) throw new InvalidOperationException("Mutated menu state cannot use untouched observer cleanup.");
        if (_backend.ReadActivity() is { Known: true, InFlight: > 0 }) return false;
        Execute(() => { Reconcile(); _releaseAttempted = true; _backend.ReleaseObservers(); _closed = true; lock (Gate) Owners.Remove(this); });
        return true;
    }
    private void Execute(Action action)
    {
        _busy = true; try { action(); } catch (Exception error) { Fault(); throw new NativeLocalMenuException(this, error); } finally { _busy = false; }
    }
    private void Fault() => Interlocked.Exchange(ref _fault, 1);
    private void Guard()
    { _main.VerifyAccess(); if (_busy || _closed || _releaseAttempted || Volatile.Read(ref _fault) != 0 || _backend.CleanupUnknown) throw new InvalidOperationException("Menu ownership is closed, busy or unknown."); }
    private sealed class Frame : IDisposable
    {
        private readonly NativeLocalMenuLedger _owner;
        internal bool Used, Finished, Own; internal int Slot; internal LocalMenuOperation Operation; internal byte Value;
        internal Frame(NativeLocalMenuLedger owner) => _owner = owner;
        public void Dispose()
        {
            if (!Used) return; if (!Finished) _owner.Fault(); Used = false; Interlocked.Increment(ref _owner._disposed);
        }
    }
}
internal sealed class NativeLocalMenuException : InvalidOperationException
{
    internal NativeLocalMenuLedger Responsibility { get; }
    internal NativeLocalMenuException(NativeLocalMenuLedger owner, Exception error)
        : base("Menu handoff retains acquired roots and mutation responsibility without retry.", error) => Responsibility = owner;
}
