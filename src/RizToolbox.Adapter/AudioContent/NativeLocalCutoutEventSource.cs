using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal sealed record NativeCutoutTrigger(long Trigger, long Events, int OffsetBits);
internal sealed record NativeCutoutEventObservation(long Controller, long EventTrigger, long GameObject,
    long Transform, long List, int Version, bool Alive, IReadOnlyList<NativeCutoutTrigger> Triggers);
internal sealed record NativeLocalCutoutEventSourceSnapshot(LocalRunSessionIdentity? Session,
    NativeCutoutEventObservation? Source, bool DispatchAttempted, bool UnsealedDispatchSource,
    bool Pending, bool Unknown, long Entered, long Exited, long InFlight, int RootRecords, string? Failure);
internal interface INativeCutoutEventSourceBackend
{
    bool CleanupUnknown { get; }
    int RootRecords { get; }
    NativeCutoutEventObservation Capture();
    void Bind(NativeLocalCutoutEventSource owner);
    ExactHookBroker.ManagedBoundaryActivity ReadActivity();
    NativeCutoutEventObservation Refresh();
    void BorrowUnityEvents(Action<int, NativeRuntime.RootHandle> adopt)
        => throw new UnsupportedSignatureException("This source has no production UnityEvent root borrowing authority.");
}
internal sealed class NativeLocalCutoutEventSourceException : InvalidOperationException
{
    internal NativeLocalCutoutEventSource Responsibility { get; }
    internal NativeLocalCutoutEventSourceException(NativeLocalCutoutEventSource owner, Exception error)
        : base("Cutout event-source observation is unconfirmed; all acquired responsibility remains held.", error) => Responsibility = owner;
}

/// <summary>Captures real event sources before activation. StartEvents may dispatch
/// arbitrary UnityEvent listeners and EventTrigger-owned delayed work. This limited
/// ledger deliberately never certifies those consumers ended or frees their roots.</summary>
internal sealed partial class NativeLocalCutoutEventSource
{
    private static readonly object Gate = new();
    private static readonly HashSet<NativeLocalCutoutEventSource> Owners = new();
    private readonly IMainThreadDispatcher _main;
    private readonly INativeCutoutEventSourceBackend _backend;
    private readonly LocalRunLaunchCommitState? _launch;
    private readonly bool _controlled;
    private NativeCutoutEventObservation? _source;
    private long _receiver, _entered, _exited, _inFlight;
    private int _fault, _dispatch;
    private bool _busy, _eventRootsBorrowAttempted;
    private string? _failure;
    private NativeLocalCutoutEventSource(IMainThreadDispatcher main, INativeCutoutEventSourceBackend backend,
        LocalRunLaunchCommitState? launch, bool controlled)
        => (_main, _backend, _launch, _controlled) = (main, backend, launch, controlled);

    internal static NativeLocalCutoutEventSource Capture(NativeRuntime api, IMainThreadDispatcher main,
        AdapterBuildProfile profile, IntPtr module, ExactHookBroker broker, LocalRunLaunchCommitState launch,
        NativeRuntime.RootHandle actualController)
    {
        ArgumentNullException.ThrowIfNull(main); main.VerifyAccess(); ArgumentNullException.ThrowIfNull(launch);
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || module == IntPtr.Zero
            || profile.GameAssemblySha256 != WorkshopChartModule.Fingerprint || profile.UnityVersion != "2022.3.62f2")
            throw new UnsupportedSignatureException("Cutout source requires the pinned Windows x64 Unity profile before root observation.");
        ArgumentNullException.ThrowIfNull(actualController); launch.VerifyPendingOwnership(launch.Registry);
        var state = launch.Read();
        if (state.Phase != LocalRunLaunchCommitPhase.ControllerBound || state.ActivationAttempted || state.EarlyTerminal
            || launch.ParameterBinding is not { } binding || binding.SetterBaseline.Controller != state.Controller
            || actualController.Target.ToInt64() != state.Controller
            || !binding.IsBoundFor(launch.Registry, launch.Claim, launch.OwnedBundle, binding.ControllerResponsibilityForBinding))
            throw new InvalidOperationException("Cutout source capture requires this exact adopted setter/controller before activation.");
        return CaptureCore(main, new NativeLocalCutoutEventSourceBackend(api, main, profile, module, broker, actualController, launch), launch, false);
    }
    internal static NativeLocalCutoutEventSource CaptureBackend(IMainThreadDispatcher main, INativeCutoutEventSourceBackend backend)
    {
        main.VerifyAccess();
        if (backend is NativeLocalCutoutEventSourceBackend) throw new InvalidOperationException("Controlled capture cannot adopt the production backend.");
        return CaptureCore(main, backend, null, true);
    }
    private static NativeLocalCutoutEventSource CaptureCore(IMainThreadDispatcher main,
        INativeCutoutEventSourceBackend backend, LocalRunLaunchCommitState? launch, bool controlled)
    {
        ArgumentNullException.ThrowIfNull(backend);
        var owner = new NativeLocalCutoutEventSource(main, backend, launch, controlled);
        lock (Gate)
        {
            if (Owners.Count >= 32) throw new InvalidOperationException("Cutout source process responsibility capacity exhausted.");
            Owners.Add(owner);
        }
        owner._busy = true;
        try
        {
            var source = backend.Capture(); Validate(source);
            if (!source.Alive) throw new TargetExpiredException("Cutout event-source objects must be alive at preactivation capture.");
            if (launch is not null && source.Controller != launch.Read().Controller)
                throw new InvalidOperationException("The native source belongs to a different launch controller.");
            owner._source = Freeze(source); Interlocked.Exchange(ref owner._receiver, source.EventTrigger);
            backend.Bind(owner); owner.VerifySession();
            var activity = backend.ReadActivity();
            if (!activity.Known || activity.InFlight != 0 || Volatile.Read(ref owner._fault) != 0)
                throw new InvalidOperationException("Cutout source installation did not establish an idle known boundary.");
            return owner;
        }
        catch (Exception error) { owner.Fail(error.Message); throw new NativeLocalCutoutEventSourceException(owner, error); }
        finally { owner._busy = false; }
    }
    // Probe callbacks run on every thread, even broker-suppressed reentry. Only
    // managed scalar comparisons/atomics occur here; no root targets or callbacks.
    internal void ProbeEnter(long receiver, bool exactMethod)
    {
        if (receiver == 0 || receiver != Interlocked.Read(ref _receiver)) return;
        Interlocked.Exchange(ref _dispatch, 1);
        if (Interlocked.Increment(ref _entered) <= 0) FaultFromAnyThread();
        var count = Interlocked.Increment(ref _inFlight);
        if (!_main.IsMainThread || !exactMethod || count != 1 || _busy) FaultFromAnyThread();
    }
    internal void ProbeExit(long receiver, bool returned)
    {
        if (receiver == 0 || receiver != Interlocked.Read(ref _receiver)) return;
        if (Interlocked.Increment(ref _exited) <= 0) FaultFromAnyThread();
        if (Interlocked.Decrement(ref _inFlight) < 0 || !returned) FaultFromAnyThread();
    }
    internal bool IsFor(LocalRunLaunchCommitState launch)
    { _main.VerifyAccess(); return !_controlled && ReferenceEquals(_launch, launch) && !ObservationAuthorityUnknown && !_backend.CleanupUnknown; }
    internal bool ObservationAuthorityUnknown => Volatile.Read(ref _fault) != 0
        || _launch?.Read().Phase == LocalRunLaunchCommitPhase.Unknown;
    /// <summary>Synchronous adoption of existing production event roots only.
    /// The listener owner must publish its own independent roots immediately;
    /// original source roots and their cleanup responsibility never transfer.</summary>
    internal void BorrowUnityEvents(LocalRunLaunchCommitState launch, Action<int, NativeRuntime.RootHandle> adopt)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(launch); ArgumentNullException.ThrowIfNull(adopt);
        launch.VerifyPendingOwnership(launch.Registry); var state = launch.Read();
        if (!IsFor(launch) || _busy || _eventRootsBorrowAttempted || _source is null || !_source.Alive
            || _backend is not NativeLocalCutoutEventSourceBackend || state.ActivationAttempted
            || state.Phase != LocalRunLaunchCommitPhase.ControllerBound || state.Controller != _source.Controller)
            throw new InvalidOperationException("Actual listener roots must be adopted from this complete source once before activation.");
        _eventRootsBorrowAttempted = true; _busy = true;
        try
        {
            _backend.BorrowUnityEvents((index, root) => {
                RequireKnown();
                if (index < 0 || index >= _source.Triggers.Count)
                    throw new InvalidOperationException("Borrowed event root has no captured Trigger association.");
                var target = root.Target.ToInt64(); RequireKnown();
                if (target != _source.Triggers[index].Events)
                    throw new InvalidOperationException("Borrowed event root differs from its exact captured Trigger association.");
                adopt(index, root); RequireKnown();
            });
            RequireKnown(); launch.VerifyPendingOwnership(launch.Registry);
            if (launch.Read().ActivationAttempted) throw new InvalidOperationException("Activation began during event root adoption.");
        }
        catch (Exception error) { Fail(error.Message); throw; }
        finally { _busy = false; }
    }
    /// <summary>Pure physical StartEvents window for its typed coroutine starts.
    /// Idle is deliberately false during this window. A caller must additionally
    /// match the pinned native Start return site, actual host and enum fields.</summary>
    internal bool TryGetCurrentDispatch(out long generation)
    {
        _main.VerifyAccess(); generation = 0;
        if (_source is null || _busy || ObservationAuthorityUnknown || _backend.CleanupUnknown) return false;
        var entered = Interlocked.Read(ref _entered); var exited = Interlocked.Read(ref _exited);
        if (entered <= 0 || exited < 0 || exited == long.MaxValue || entered != exited + 1
            || Interlocked.Read(ref _inFlight) != 1 || ObservationAuthorityUnknown || _backend.CleanupUnknown) return false;
        generation = entered; return true;
    }
    internal bool TryConfirmNativeIdle()
    {
        _main.VerifyAccess();
        try
        {
            RequireKnown(); VerifySession();
            var activity = _backend.ReadActivity(); RequireKnown();
            if (!activity.Known || activity.InFlight < 0 || Interlocked.Read(ref _inFlight) < 0)
                throw new InvalidOperationException("Cutout native boundary activity is unknown.");
            return activity.InFlight == 0 && Interlocked.Read(ref _inFlight) == 0;
        }
        catch (Exception error) { Fail(error.Message); throw; }
    }
    internal void Tick()
    {
        if (_activeObserverRetirement?.IsCompleted == true) throw new ObjectDisposedException(nameof(NativeLocalCutoutEventSource));
        _main.VerifyAccess(); if (_busy) throw new InvalidOperationException("Cutout source observation cannot reenter.");
        if (ObservationAuthorityUnknown) return;
        _busy = true;
        try
        {
            VerifySession(); var activity = _backend.ReadActivity(); RequireKnown();
            if (_backend.CleanupUnknown || !activity.Known || activity.InFlight < 0)
                throw new InvalidOperationException("Cutout native boundary activity is unknown.");
            if (activity.InFlight != 0 || Interlocked.Read(ref _inFlight) != 0) return;
            RequireKnown(); var next = _backend.Refresh(); RequireKnown(); Validate(next);
            var old = _source!;
            if (next.Controller != old.Controller || next.EventTrigger != old.EventTrigger || next.GameObject != old.GameObject
                || next.Transform != old.Transform || next.List != old.List || next.Version != old.Version
                || !next.Triggers.SequenceEqual(old.Triggers))
                throw new InvalidOperationException("The actual captured cutout event-source graph changed.");
            if (!old.Alive && next.Alive) throw new InvalidOperationException("A Unity-dead cutout source cannot become live again.");
            activity = _backend.ReadActivity(); RequireKnown();
            if (!activity.Known || activity.InFlight != 0 || Interlocked.Read(ref _inFlight) != 0)
                throw new InvalidOperationException("Cutout activity changed during source observation.");
            _source = Freeze(next);
        }
        catch (Exception error) { Fail(error.Message); }
        finally { _busy = false; }
    }
    internal NativeLocalCutoutEventSourceSnapshot Read()
    {
        _main.VerifyAccess();
        // Pure copied state: expected Unity destruction must never trigger native
        // property access here. Pending is intentional until listener/operation
        // observation is implemented, even when DispatchAttempted is still false.
        return new(_launch?.Identity, _source, Volatile.Read(ref _dispatch) != 0, Volatile.Read(ref _dispatch) != 0,
            true, ObservationAuthorityUnknown || _backend.CleanupUnknown, Interlocked.Read(ref _entered), Interlocked.Read(ref _exited),
            Interlocked.Read(ref _inFlight), _backend.RootRecords, _failure);
    }
    private void VerifySession()
    {
        if (_controlled) return;
        var current = _launch!.Registry.Read(_launch.Session);
        if (current.Identity != _launch.Identity || current.ControllerIdentity != _source!.Controller || current.ProtectionRetired
            || current.CleanupAttempted || current.Phase is LocalRunSessionPhase.Closed or LocalRunSessionPhase.CleanupUnknown
            || _launch.Read().Phase == LocalRunLaunchCommitPhase.Unknown
            || !ReferenceEquals(_launch.Registry.FindProtectedSession(LocalBoundaryKind.ControllerLevelOver, current.ControllerIdentity), _launch.Session))
            throw new InvalidOperationException("The retained cutout source no longer has its exact protected run.");
    }
    private static void Validate(NativeCutoutEventObservation source)
    {
        if (source is null || source.Controller == 0 || source.EventTrigger == 0 || source.GameObject == 0 || source.Transform == 0
            || source.List == 0 || source.Triggers is null || source.Triggers.Count > 32
            || source.Triggers.Any(item => item is null || item.Trigger == 0 || item.Events == 0
                || !float.IsFinite(BitConverter.Int32BitsToSingle(item.OffsetBits))))
            throw new InvalidOperationException("The bounded actual cutout event closure is incomplete or unsupported.");
    }
    private static NativeCutoutEventObservation Freeze(NativeCutoutEventObservation source)
        => source with { Triggers = Array.AsReadOnly(source.Triggers.ToArray()) };
    private void RequireKnown()
    {
        if (ObservationAuthorityUnknown || _backend.CleanupUnknown)
        {
            FaultFromAnyThread();
            throw new InvalidOperationException("Cutout event-source responsibility is unknown.");
        }
    }
    private void FaultFromAnyThread()
    {
        Interlocked.Exchange(ref _fault, 1);
        _launch?.ObserveNativeFaultFromAnyThread("Cutout event-source observation is unknown.");
    }
    private void Fail(string failure)
    { FaultFromAnyThread(); _failure ??= failure.Length > 768 ? failure[..768] : failure; }
}
