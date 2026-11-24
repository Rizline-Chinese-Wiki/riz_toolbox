using RizToolbox.Runtime;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal enum NativeLocalActiveDestructionPhase
{ AwaitingCutout, AwaitingLaterFrame, CloneDestroyed, StagingDestroyPending, Confirmed, Released, Unknown }
internal sealed record ActiveDestructionIdentity(long Controller, long CloneGameObject, long CloneTransform,
    long StagingGameObject, long StagingTransform);
internal sealed record ActiveDestructionLiveness(ActiveDestructionIdentity Identity, bool ControllerAlive,
    bool CloneGameObjectAlive, bool CloneTransformAlive, bool StagingGameObjectAlive, bool StagingTransformAlive)
{
    internal bool CloneDead => !ControllerAlive && !CloneGameObjectAlive && !CloneTransformAlive;
    internal bool StagingDead => !StagingGameObjectAlive && !StagingTransformAlive;
}
internal sealed record ActiveDestructionReadiness(bool Known, bool Drained, bool RequiredCutoutNaturallyEnded, long Frame);
internal sealed record NativeLocalActiveDestructionSnapshot(LocalRunSessionIdentity Session,
    NativeLocalActiveDestructionPhase Phase, ActiveDestructionIdentity? Identity, int CutoutObservedFrame,
    int CloneConfirmedFrame, int StagingDestroyFrame, int ConfirmedFrame, bool StagingDestroyAttempted,
    bool CloneDestroyed, bool FiveObjectsDestroyed, bool Unknown, bool Closed, int Roots, string? Failure);

internal interface IActiveDestructionBackend
{
    bool CleanupUnknown { get; }
    int RootRecords { get; }
    void Initialize();
    int Frame();
    ActiveDestructionLiveness ReadLiveness();
    void VerifyStagingCanDestroy();
    void DestroyStagingOnce();
    void ReleaseObservationRoots(Func<bool> mayFreeNext);
}
internal interface IActiveDestructionAuthority
{
    LocalRunSessionIdentity Identity { get; }
    long Controller { get; }
    bool Unknown { get; }
    void VerifyBeforeCapture();
    void VerifyObservation();
    ActiveDestructionReadiness ReadReadiness();
    void MarkUnknown(string failure);
}
internal sealed class NativeLocalActiveDestructionException : InvalidOperationException
{
    internal NativeLocalActiveDestructionTracker Responsibility { get; }
    internal NativeLocalActiveDestructionException(NativeLocalActiveDestructionTracker owner, Exception error)
        : base("Active destruction observations are unconfirmed; independent native responsibility remains retained.", error)
        => Responsibility = owner;
}

/// <summary>Five independently rooted Unity wrappers, adopted before activation.
/// Only the real terminal Cutout destroys the active clone. This tracker observes
/// later-frame Unity death and may destroy its empty staging parent once. Neither
/// observation-root release nor Unity death signs whole-run consumers-ended proof.</summary>
internal sealed partial class NativeLocalActiveDestructionTracker
{
    private const int MaximumOwners = 32;
    private static readonly object Gate = new();
    private static readonly HashSet<NativeLocalActiveDestructionTracker> Owners = new();
    private readonly IMainThreadDispatcher _main;
    private readonly IActiveDestructionBackend _backend;
    private readonly IActiveDestructionAuthority _authority;
    private readonly LocalRunLaunchCommitState? _launch;
    private readonly NativeLocalTerminalCoroutineTracker? _terminal;
    private NativeLocalActiveDestructionPhase _phase;
    private ActiveDestructionIdentity? _identity;
    private ActiveDestructionLiveness? _liveness;
    private int _lastFrame = -1, _cutoutFrame = -1, _cloneFrame = -1, _stagingFrame = -1, _confirmedFrame = -1;
    private bool _ready, _busy, _unknown, _closed, _stagingAttempted, _releaseAttempted;
    private string? _failure;
    private NativeLocalActiveDestructionTracker(IMainThreadDispatcher main, IActiveDestructionBackend backend,
        IActiveDestructionAuthority authority, LocalRunLaunchCommitState? launch, NativeLocalTerminalCoroutineTracker? terminal)
        => (_main, _backend, _authority, _launch, _terminal) = (main, backend, authority, launch, terminal);

    internal static NativeLocalActiveDestructionTracker Capture(NativeRuntime api, IMainThreadDispatcher main,
        AdapterBuildProfile profile, IntPtr module, LocalRunProtectionHost host, NativeLocalRunTerminationBoundaryState bridge,
        LocalRunLaunchCommitState launch, OwnedNativeInactiveController controller, NativeLocalTerminalCoroutineTracker terminal,
        NativeLocalCutoutEventSource cutoutEvents, NativeLocalCutoutCoroutineTracker cutoutCoroutines, NativeLocalUnityEventLedger unityEvents)
    {
        ArgumentNullException.ThrowIfNull(main); main.VerifyAccess(); ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(host); ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(launch); ArgumentNullException.ThrowIfNull(controller); ArgumentNullException.ThrowIfNull(terminal);
        ArgumentNullException.ThrowIfNull(cutoutEvents);
        ArgumentNullException.ThrowIfNull(cutoutCoroutines);
        ArgumentNullException.ThrowIfNull(unityEvents);
        if (terminal.IsControlled || !terminal.IsFor(launch) || !cutoutEvents.IsFor(launch) || !ReferenceEquals(host.Sessions, launch.Registry))
            throw new InvalidOperationException("Production destruction capture requires the same actual protected launch and terminal tracker.");
        return CaptureCore(main, new NativeLocalActiveDestructionBackend(api, main, profile, module, controller, launch),
            new Authority(host, bridge, launch, controller, terminal, cutoutEvents, cutoutCoroutines, unityEvents), launch, terminal, controller);
    }
    internal static NativeLocalActiveDestructionTracker CaptureBackend(IMainThreadDispatcher main,
        IActiveDestructionBackend backend, IActiveDestructionAuthority authority)
    {
        if (backend is NativeLocalActiveDestructionBackend)
            throw new InvalidOperationException("Controlled capture cannot adopt a production destruction backend.");
        return CaptureCore(main, backend, authority, null, null, null);
    }
    private static NativeLocalActiveDestructionTracker CaptureCore(IMainThreadDispatcher main,
        IActiveDestructionBackend backend, IActiveDestructionAuthority authority,
        LocalRunLaunchCommitState? launch, NativeLocalTerminalCoroutineTracker? terminal, OwnedNativeInactiveController? factory)
    {
        ArgumentNullException.ThrowIfNull(main); main.VerifyAccess(); ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(authority); authority.VerifyBeforeCapture();
        if (authority.Controller == 0) throw new InvalidOperationException("An exact bound controller is required.");
        var owner = new NativeLocalActiveDestructionTracker(main, backend, authority, launch, terminal);
        owner._activeFactory = factory;
        lock (Gate)
        {
            if (Owners.Count >= MaximumOwners || launch is not null && Owners.Any(item => ReferenceEquals(item._launch, launch)))
                throw new InvalidOperationException("Active destruction observation capacity is exhausted or this launch already has an owner.");
            Owners.Add(owner); // Fixed backend slots and strong owner precede all native work.
        }
        owner._busy = true;
        try
        {
            backend.Initialize(); var observed = backend.ReadLiveness(); owner.RequireIdentity(observed.Identity);
            if (observed.CloneDead || !observed.ControllerAlive || !observed.CloneGameObjectAlive || !observed.CloneTransformAlive
                || !observed.StagingGameObjectAlive || !observed.StagingTransformAlive)
                throw new InvalidOperationException("All five actual factory objects must be live before activation.");
            owner._liveness = observed; owner._lastFrame = backend.Frame();
            if (owner._lastFrame < 0) throw new InvalidOperationException("The captured Unity frame is invalid.");
            authority.VerifyBeforeCapture(); owner.RequireKnown(); owner._phase = NativeLocalActiveDestructionPhase.AwaitingCutout;
            owner._ready = true; return owner;
        }
        catch (Exception error) { owner.Fail(error); throw new NativeLocalActiveDestructionException(owner, error); }
        finally { owner._busy = false; }
    }
    internal bool IsFor(LocalRunLaunchCommitState launch)
    {
        _main.VerifyAccess();
        return _launch is not null && ReferenceEquals(_launch, launch) && _terminal?.IsFor(launch) == true && !Unknown && !_closed;
    }
    internal bool Unknown { get { _main.VerifyAccess(); return _unknown || _backend.CleanupUnknown
        || (_factoryRetirementFrozen ? _launch?.Read().Phase == LocalRunLaunchCommitPhase.Unknown : _authority.Unknown); } }
    internal NativeLocalActiveDestructionSnapshot Read()
    {
        _main.VerifyAccess(); var unknown = Unknown;
        return new(_authority.Identity, unknown ? NativeLocalActiveDestructionPhase.Unknown : _phase, _identity,
            _cutoutFrame, _cloneFrame, _stagingFrame, _confirmedFrame, _stagingAttempted,
            _cloneFrame >= 0 && !unknown, _confirmedFrame >= 0 && !unknown, unknown, _closed, _backend.RootRecords, _failure);
    }

    /// <returns>True only after the actual three clone objects were observed dead
    /// in a later real frame. Read.FiveObjectsDestroyed separately includes staging.</returns>
    internal bool Tick()
    {
        Guard(); _busy = true;
        try
        {
            _authority.VerifyObservation(); var readiness = ReadReadiness();
            if (!readiness.Drained || !readiness.RequiredCutoutNaturallyEnded) return false;
            var frame = ReadFrame(readiness); // First record terminal completion; never prove death in that frame.
            if (_cutoutFrame < 0) { _cutoutFrame = frame; _phase = NativeLocalActiveDestructionPhase.AwaitingLaterFrame; return false; }
            if (frame <= _cutoutFrame) return false;
            var observed = _backend.ReadLiveness(); RequireIdentity(observed.Identity);
            var after = ReadReadiness(); if (!after.Drained) return false;
            if (!after.RequiredCutoutNaturallyEnded) throw new InvalidOperationException("Required natural Cutout evidence was withdrawn.");
            ReadFrame(after); RequireKnown();
            if (_cloneFrame >= 0 && !observed.CloneDead || _liveness is { } previous && (
                !previous.ControllerAlive && observed.ControllerAlive || !previous.CloneGameObjectAlive && observed.CloneGameObjectAlive
                || !previous.CloneTransformAlive && observed.CloneTransformAlive || !previous.StagingGameObjectAlive && observed.StagingGameObjectAlive
                || !previous.StagingTransformAlive && observed.StagingTransformAlive))
                throw new InvalidOperationException("A retained Unity wrapper changed from dead back to live.");
            _liveness = observed;
            if (!observed.CloneDead) return false;
            if (_cloneFrame < 0) _cloneFrame = frame;
            _phase = _stagingAttempted ? NativeLocalActiveDestructionPhase.StagingDestroyPending : NativeLocalActiveDestructionPhase.CloneDestroyed;
            if (observed.StagingDead && (!_stagingAttempted || frame > _stagingFrame))
            { if (_confirmedFrame < 0) _confirmedFrame = frame; _phase = NativeLocalActiveDestructionPhase.Confirmed; }
            return true;
        }
        catch (Exception error) { Fail(error); throw new NativeLocalActiveDestructionException(this, error); }
        finally { _busy = false; }
    }
    internal void RequestStagingDestroyOnce()
    {
        Guard();
        if (_stagingAttempted || _confirmedFrame >= 0) return;
        if (_cloneFrame < 0) throw new InvalidOperationException("Only a confirmed naturally destroyed clone permits staging cleanup.");
        _busy = true;
        try
        {
            _authority.VerifyObservation(); var readiness = ReadReadiness();
            if (!readiness.Drained || !readiness.RequiredCutoutNaturallyEnded) return;
            var frame = ReadFrame(readiness); var observed = _backend.ReadLiveness(); RequireIdentity(observed.Identity); RequireKnown();
            if (!observed.CloneDead) throw new InvalidOperationException("The exact clone is no longer confirmed dead.");
            if (observed.StagingDead) { _liveness = observed; _confirmedFrame = frame; _phase = NativeLocalActiveDestructionPhase.Confirmed; return; }
            if (!observed.StagingGameObjectAlive || !observed.StagingTransformAlive) return; // Already being destroyed; keep both wrappers.
            _backend.VerifyStagingCanDestroy(); var after = ReadReadiness();
            if (!after.Drained || !after.RequiredCutoutNaturallyEnded) return;
            frame = ReadFrame(after); RequireKnown();
            _stagingAttempted = true; _stagingFrame = frame; _phase = NativeLocalActiveDestructionPhase.StagingDestroyPending;
            _backend.DestroyStagingOnce(); RequireKnown();
        }
        catch (Exception error) { Fail(error); throw new NativeLocalActiveDestructionException(this, error); }
        finally { _busy = false; }
    }
    internal void ReleaseObservationRootsAfterConfirmed()
    {
        Guard(); if (_confirmedFrame < 0) throw new InvalidOperationException("All five objects require confirmed later-frame Unity death before observation roots can release.");
        _busy = true;
        try
        {
            _authority.VerifyObservation(); var readiness = ReadReadiness();
            if (!readiness.Drained || !readiness.RequiredCutoutNaturallyEnded)
                throw new InvalidOperationException("Native observations must remain drained before releasing independent roots.");
            ReadFrame(readiness); var observed = _backend.ReadLiveness(); RequireIdentity(observed.Identity); RequireKnown();
            if (!observed.CloneDead || !observed.StagingDead) throw new InvalidOperationException("Confirmed Unity objects are not all dead.");
            _releaseAttempted = true;
            _backend.ReleaseObservationRoots(() => {
                _authority.VerifyObservation(); var activity = _authority.ReadReadiness();
                return !_unknown && !_authority.Unknown && activity.Known && activity.Drained && activity.RequiredCutoutNaturallyEnded;
            });
            RequireKnown(); var after = ReadReadiness();
            if (!after.Drained || !after.RequiredCutoutNaturallyEnded || _backend.RootRecords != 0)
                throw new InvalidOperationException("Native authority changed while independently releasing observation roots.");
            _closed = true; _phase = NativeLocalActiveDestructionPhase.Released;
            lock (Gate) Owners.Remove(this);
        }
        catch (Exception error) { Fail(error); throw new NativeLocalActiveDestructionException(this, error); }
        finally { _busy = false; }
    }
    private ActiveDestructionReadiness ReadReadiness()
    {
        var value = _authority.ReadReadiness(); RequireKnown();
        if (!value.Known || value.Drained && (value.Frame < 0 || value.Frame > int.MaxValue))
            throw new InvalidOperationException("Complete native terminal/progress/judge/initialization activity is unknown.");
        return value;
    }
    private int ReadFrame(ActiveDestructionReadiness readiness)
    {
        var value = _backend.Frame(); RequireKnown();
        if (value < _lastFrame || value < 0 || value != readiness.Frame)
            throw new InvalidOperationException("Actual Unity frame moved backwards or differs from the protected native bridge observation.");
        _lastFrame = value; return value;
    }
    private void RequireIdentity(ActiveDestructionIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var values = new[] { identity.Controller, identity.CloneGameObject, identity.CloneTransform, identity.StagingGameObject, identity.StagingTransform };
        if (identity.Controller != _authority.Controller || values.Any(value => value == 0) || values.Distinct().Count() != 5
            || _identity is not null && _identity != identity)
            throw new InvalidOperationException("The five independently held factory object identities changed.");
        _identity ??= identity;
    }
    private void RequireKnown()
    { if (_unknown || _backend.CleanupUnknown || _authority.Unknown) throw new InvalidOperationException("Active destruction authority became unknown during native observation."); }
    private void Fail(Exception error)
    {
        if (_unknown) return;
        _unknown = true; _phase = NativeLocalActiveDestructionPhase.Unknown;
        _failure = "Active destruction responsibility retained: " + error.GetType().Name;
        try { _authority.MarkUnknown(_failure); } catch { }
    }
    private void Guard()
    {
        _main.VerifyAccess();
        if (!_ready || _busy || _closed || _releaseAttempted || _factoryRetirementFrozen || Unknown)
            throw new InvalidOperationException("Active destruction observations cannot reenter or reuse closed/unknown responsibility.");
    }
    private sealed class Authority : IActiveDestructionAuthority
    {
        private readonly LocalRunProtectionHost _host;
        private readonly NativeLocalRunTerminationBoundaryState _bridge;
        private readonly LocalRunLaunchCommitState _launch;
        private readonly OwnedNativeInactiveController _controller;
        private readonly NativeLocalTerminalCoroutineTracker _terminal;
        private readonly NativeLocalCutoutEventSource _cutoutEvents;
        private readonly NativeLocalCutoutCoroutineTracker _cutoutCoroutines;
        private readonly NativeLocalUnityEventLedger _unityEvents;
        private readonly LocalRunSessionIdentity _identity;
        internal Authority(LocalRunProtectionHost host, NativeLocalRunTerminationBoundaryState bridge,
            LocalRunLaunchCommitState launch, OwnedNativeInactiveController controller, NativeLocalTerminalCoroutineTracker terminal,
            NativeLocalCutoutEventSource cutoutEvents, NativeLocalCutoutCoroutineTracker cutoutCoroutines, NativeLocalUnityEventLedger unityEvents)
        { (_host, _bridge, _launch, _controller, _terminal, _cutoutEvents, _cutoutCoroutines, _unityEvents) = (host, bridge, launch, controller, terminal, cutoutEvents, cutoutCoroutines, unityEvents); _identity = launch.Identity; }
        public LocalRunSessionIdentity Identity => _identity;
        public long Controller => _terminal.ControllerIdentity;
        public bool Unknown => _terminal.Unknown || !_cutoutEvents.IsFor(_launch) || !_cutoutCoroutines.IsFor(_launch) || !_unityEvents.IsFor(_launch) || _launch.Read().Phase == LocalRunLaunchCommitPhase.Unknown;
        public void VerifyBeforeCapture()
        {
            _launch.VerifyPendingOwnership(_host.Sessions); var state = _launch.Read(); var binding = _launch.ParameterBinding;
            if (Unknown || _controller.ActivationAttempted || _controller.CleanupUnknown || _controller.IsClosed || binding is null
                || state.ActivationAttempted || state.Controller != Controller || !binding.IsBoundFor(_host.Sessions, _launch.Claim, _launch.OwnedBundle, _controller)
                || _terminal.IsControlled || !_terminal.IsFor(_launch))
                throw new InvalidOperationException("Actual destruction roots must be captured for the same exact bound run before activation.");
            var activity = _host.ReadNativeBoundaryActivity();
            var sourceIdle = _cutoutEvents.TryConfirmNativeIdle();
            if (Unknown || !sourceIdle || !activity.Known || activity.InFlight != 0) throw new InvalidOperationException("Capture requires drained complete protection and Cutout event-source entries.");
        }
        public void VerifyObservation()
        {
            var state = _launch.Read(); var current = _host.Sessions.Read(_launch.Session);
            if (Unknown || _terminal.IsControlled || !_terminal.IsFor(_launch) || state.Identity != _identity
                || !state.ActivationAttempted || state.Controller != Controller || current.Identity != _identity
                || current.ControllerIdentity != Controller || current.ProtectionRetired || current.CleanupAttempted
                || current.Phase is LocalRunSessionPhase.Closed or LocalRunSessionPhase.CleanupUnknown
                || !ReferenceEquals(_host.Sessions.FindProtectedSession(LocalBoundaryKind.ControllerLevelOver, Controller), _launch.Session))
                throw new InvalidOperationException("The same activated run must remain protected through result and return while destruction is observed.");
        }
        public ActiveDestructionReadiness ReadReadiness()
        {
            VerifyObservation(); var beforeSourceIdle = _cutoutEvents.TryConfirmNativeIdle();
            // A foreign StartEvents entry is sufficient to defer every other native
            // read, even when this exact source has not itself observed a fault.
            if (!beforeSourceIdle) return new(!Unknown, false, false, -1);
            if (!_cutoutCoroutines.TryConfirmNativeIdle()) return new(!Unknown, false, false, -1);
            if (!_unityEvents.TryConfirmNativeIdle()) return new(!Unknown, false, false, -1);
            var terminal = _terminal.Read();
            var natural = terminal.ResultAttempted && terminal.ResultReturned && terminal.CutoutNamedReturned && terminal.CutoutNaturallyEnded;
            if (Unknown || terminal.Unknown || !natural) return new(!Unknown && !terminal.Unknown, false, natural, -1);
            var ended = _terminal.TryConfirmObservedEndedForSourceClosure();
            if (!ended) return new(!Unknown, false, natural, -1);
            if (Unknown) return new(false, false, natural, -1);
            var boundaries = _bridge.Read(_launch.Session, Controller);
            var actual = _terminal.Read(); var afterSourceIdle = _cutoutEvents.TryConfirmNativeIdle();
            var afterCoroutinesIdle = _cutoutCoroutines.TryConfirmNativeIdle();
            var afterEventsIdle = _unityEvents.TryConfirmNativeIdle();
            return new(!Unknown && !actual.Unknown && boundaries.AllFiveGatesInstalled && boundaries.NativeBoundaryStateKnown,
                afterSourceIdle && afterCoroutinesIdle && afterEventsIdle && boundaries.InFlightNativeBoundaries == 0,
                terminal.ResultAttempted && actual.ResultReturned && actual.CutoutNamedReturned && actual.CutoutNaturallyEnded,
                boundaries.Frame);
        }
        public void MarkUnknown(string failure) => _launch.MarkUnknown(failure);
    }
}
