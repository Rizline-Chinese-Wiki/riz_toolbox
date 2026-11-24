using RizToolbox.Runtime;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal sealed class NativeLocalRunLaunchReservationException : InvalidOperationException
{
    internal NativeLocalRunLaunchReservation Responsibility { get; }
    internal NativeLocalRunLaunchReservationException(NativeLocalRunLaunchReservation owner, Exception error)
        : base("LOCAL launch reservation did not complete; all adopted responsibility remains explicitly owned.", error) => Responsibility = owner;
}

/// <summary>Reserves the existing queue, graph, factory and actual-capture slots as a
/// group before any controller creation. It does not activate, initialize settings, issue
/// consumer-end evidence, or retire the registry's separately owned native bundle.</summary>
internal sealed class NativeLocalRunLaunchReservation
{
    private readonly IMainThreadDispatcher _main;
    private readonly LocalRunTerminationHandler _handler;
    private readonly NativeLocalRunTerminationBoundaryState _bridge;
    private NativeLocalResultCapture? _capture;
    private NativeLocalRunTerminationCaptureFactory? _factory;
    // Fixed handoff storage exists before an owned independent root may enter this API.
    private NativeRuntime.RootHandle? _untransferredController;
    private bool _handlerReserved, _bridgeReserved, _factoryTransferred, _busy, _cancelled, _untransferredFreeAttempted;
    internal LocalRunLaunchCommitState State { get; }
    internal bool IsCancelled { get { _main.VerifyAccess(); return _cancelled; } }
    internal bool UntransferredCleanupCompleted { get { _main.VerifyAccess(); return _untransferredFreeAttempted
        && _untransferredController is null && (_factoryTransferred || _factory is null)
        && State.Read().Phase != LocalRunLaunchCommitPhase.Unknown; } }
    internal void AttachTerminalCoroutines(NativeLocalTerminalCoroutineTracker tracker)
    {
        Guard(); ArgumentNullException.ThrowIfNull(tracker); State.VerifyPendingOwnership(State.Registry);
        if (!tracker.IsFor(State) || _capture is null || State.Read().ActivationAttempted)
            throw new InvalidOperationException("The pending launch requires its own terminal coroutine tracker before activation.");
        _capture.AttachTerminalCoroutines(tracker);
    }
    private NativeLocalRunLaunchReservation(IMainThreadDispatcher main, LocalRunTerminationHandler handler,
        NativeLocalRunTerminationBoundaryState bridge, LocalRunLaunchCommitState state)
    { (_main, _handler, _bridge, State) = (main, handler, bridge, state); state.AttachCoordinator(this); }

    internal static NativeLocalRunLaunchReservation Reserve(NativeRuntime api, IMainThreadDispatcher main,
        AdapterBuildProfile profile, IntPtr module, LocalRunProtectionHost host,
        LocalRunTerminationHandler handler, NativeLocalRunTerminationBoundaryState bridge,
        IDisposable ownedBundle, LocalRunSessionRegistry.CommitClaim claim)
    {
        ArgumentNullException.ThrowIfNull(main); main.VerifyAccess(); ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(handler); ArgumentNullException.ThrowIfNull(bridge);
        if (!handler.UsesBoundaryState(bridge))
            throw new InvalidOperationException("The launch queue and reservation coordinator must own the same exact native boundary bridge.");
        handler.ValidateHost(host.Sessions);
        var activity = host.ReadNativeBoundaryActivity();
        if (!bridge.IsReadyForHost(host.Sessions) || !activity.Known || activity.InFlight != 0)
            throw new InvalidOperationException("Launch reservations require the exact idle native host integration.");
        var state = new LocalRunLaunchCommitState(main, host.Sessions, claim, ownedBundle);
        var owner = new NativeLocalRunLaunchReservation(main, handler, bridge, state);
        try
        {
            owner._capture = NativeLocalResultCapture.ReservePending(main, host.Sessions, claim, ownedBundle);
            state.AttachCapture(owner._capture);
            owner._factory = NativeLocalRunTerminationCaptureFactory.ReservePending(api, main, profile, module, state, owner._capture);
            handler.ReservePending(state); owner._handlerReserved = true;
            bridge.ReservePending(state); owner._bridgeReserved = true;
            return owner;
        }
        catch (Exception error)
        {
            Exception failure = error;
            try { owner.CancelEmpty(); }
            catch (Exception cleanup) { state.MarkUnknown("Pure launch reservation rollback is unconfirmed."); failure = new AggregateException(error, cleanup); }
            throw new NativeLocalRunLaunchReservationException(owner, failure);
        }
    }

    /// <summary>Must precede the caller's actual controller-creation operation. This
    /// accounting operation itself grants no native activation or game-play capability.</summary>
    internal void NoteNativeLaunchAttempt()
    { Guard(); if (_cancelled) throw new ObjectDisposedException(nameof(NativeLocalRunLaunchReservation)); State.NoteNativeAttempt(); }

    /// <summary>Adopts only the independent factory root; the registry root stays borrowed.
    /// Both roots must already belong to the exact pending controller publication transaction.
    /// Every native attempt was accounted before creation, and a failed handoff remains owned.</summary>
    internal void AttachPendingController(NativeRuntime.RootHandle ownedIndependentFactoryRoot,
        NativeRuntime.RootHandle borrowedRegistryControllerRoot)
    {
        Guard(); ArgumentNullException.ThrowIfNull(ownedIndependentFactoryRoot); ArgumentNullException.ThrowIfNull(borrowedRegistryControllerRoot);
        State.VerifyPendingOwnership(State.Registry);
        if (_cancelled || !State.Read().NativeAttempted || !_handlerReserved || !_bridgeReserved || _factory is null
            || _factoryTransferred || _untransferredController is not null
            || ReferenceEquals(ownedIndependentFactoryRoot, borrowedRegistryControllerRoot))
            throw new InvalidOperationException("The pre-reserved launch must receive one independent exact factory root after native publication.");
        var controller = State.Registry.Read(State.Session).ControllerIdentity;
        if (controller == 0) throw new InvalidOperationException("Registry protection must own the controller before any other handoff.");
        // A rejected foreign/duplicate root stays with its existing owner. Adopt the
        // handoff record only after the factory's pure ownership checks have succeeded.
        _factory.VerifyPendingControllerHandoff(ownedIndependentFactoryRoot, controller);
        _busy = true; _untransferredController = ownedIndependentFactoryRoot;
        try
        {
            State.NoteControllerBound(controller);
            _factory.AttachPendingController(ownedIndependentFactoryRoot, controller);
            _untransferredController = null;
            _handler.AttachPendingController(State, _factory); _factoryTransferred = true;
            _bridge.AttachPendingController(State, borrowedRegistryControllerRoot);
        }
        catch (Exception error)
        {
            if (_factory.OwnsControllerRoot(ownedIndependentFactoryRoot)) _untransferredController = null;
            State.MarkUnknown("Pending controller handoff is unconfirmed: " + error.GetType().Name);
            throw new NativeLocalRunLaunchReservationException(this, error);
        }
        finally { _busy = false; }
    }

    internal void CancelEmpty()
    {
        Guard(); if (_cancelled) return;
        if (!State.CanCancelPureReservation || _factoryTransferred || _untransferredController is not null)
            throw new InvalidOperationException("Only an entirely empty, never-attempted launch reservation can be cancelled here.");
        _busy = true;
        try
        {
            var failures = new List<Exception>(4);
            if (_handlerReserved)
                try { if (!_handler.CancelPurePending(State)) throw new InvalidOperationException("The empty queue slot did not retire."); _handlerReserved = false; } catch (Exception error) { failures.Add(error); }
            if (_bridgeReserved)
                try { if (!_bridge.CancelPurePending(State)) throw new InvalidOperationException("The empty graph slot did not retire."); _bridgeReserved = false; } catch (Exception error) { failures.Add(error); }
            if (_factory is { } factory)
                try { factory.CancelPendingFactory(); _factory = null; } catch (Exception error) { failures.Add(error); }
            if (_capture is { } capture)
                try { capture.CancelEmptyPendingReservation(); _capture = null; } catch (Exception error) { failures.Add(error); }
            if (failures.Count != 0)
            {
                State.MarkUnknown("One or more pure reservation cancellations are unconfirmed.");
                throw new AggregateException("Independent empty-slot cleanup did not complete.", failures);
            }
            _cancelled = true;
        }
        finally { _busy = false; }
    }

    /// <summary>Only cleanup of a root which never transferred is performed here. Queue,
    /// graph and adopted factory owners retain and clean their own records through the host.
    /// This method supplies no consumer-end proof and cannot close a session.</summary>
    internal void ReleaseUntransferredAfterClose()
    {
        Guard();
        if (_factoryTransferred && _untransferredController is null && State.Read().Phase == LocalRunLaunchCommitPhase.Unknown)
        {
            // The queue/factory has already adopted the only root. This exact
            // closed-session call has no remaining root to free and grants no
            // completion permission: the launch remains Unknown and the queue
            // continues to own its independent cleanup responsibility.
            VerifyUntransferredClosure(requireKnown: false);
            return;
        }
        VerifyUntransferredClosure();
        if (_untransferredFreeAttempted)
        {
            if (_untransferredController is not null || !_factoryTransferred && _factory is not null)
                throw new InvalidOperationException("Previous untransferred-root cleanup remains unknown and will not be retried.");
            return;
        }
        _untransferredFreeAttempted = true; _busy = true;
        try
        {
            var failures = new List<Exception>(2);
            if (!_factoryTransferred && _factory is { } factory)
                try { VerifyUntransferredClosure(); factory.ReleaseAfterConsumersEnded(State.Registry, State.Session);
                    _factory = null; VerifyUntransferredClosure(); }
                catch (Exception error) { State.MarkUnknown("Untransferred factory cleanup is unknown."); failures.Add(error); }
            if (_untransferredController is { } root)
                try { VerifyUntransferredClosure(); root.Dispose(); _untransferredController = null; VerifyUntransferredClosure(); }
                catch (Exception error) { State.MarkUnknown("Untransferred controller cleanup is unknown."); failures.Add(error); }
            if (failures.Count != 0) { State.MarkUnknown("Untransferred native launch cleanup is unknown."); throw new AggregateException(failures); }
        }
        finally { _busy = false; }
    }
    private void VerifyUntransferredClosure(bool requireKnown = true)
    {
        var current = State.Registry.Read(State.Session); var launch = State.Read();
        if (requireKnown && launch.Phase == LocalRunLaunchCommitPhase.Unknown || current.Identity != State.Identity
            || current.ControllerIdentity != launch.Controller || current.MetadataIdentity != State.MetadataIdentity
            || current.Phase != LocalRunSessionPhase.Closed || !current.ReturnRequested || !current.ProtectionRetired
            || !current.CleanupAttempted || current.OutstandingOwnershipRecords != 0 || string.IsNullOrWhiteSpace(current.ConsumerEndEvidence)
            || current.ControllerIdentity != 0 && (
                State.Registry.FindProtectedSession(LocalBoundaryKind.ControllerRetry, current.ControllerIdentity) is not null
                || State.Registry.FindProtectedSession(LocalBoundaryKind.ControllerLevelOver, current.ControllerIdentity) is not null)
            || current.ResultSceneIdentity != 0 && (
                State.Registry.FindProtectedSession(LocalBoundaryKind.ResultStart, current.ResultSceneIdentity) is not null
                || State.Registry.FindProtectedSession(LocalBoundaryKind.ResultRetry, current.ResultSceneIdentity) is not null
                || State.Registry.FindProtectedSession(LocalBoundaryKind.ResultBack, current.ResultSceneIdentity) is not null))
            throw new InvalidOperationException("Untransferred launch roots require the exact known closed session, identities and withdrawn classifications before and after each free.");
    }
    private void Guard()
    { _main.VerifyAccess(); if (_busy) throw new InvalidOperationException("Launch reservation ownership cannot reenter a handoff or cleanup."); }
}
