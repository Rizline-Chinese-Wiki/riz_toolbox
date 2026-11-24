using RizToolbox.Runtime;

namespace RizToolbox.Adapter.AudioContent;

internal sealed partial class NativeLocalRunLaunchCoordinator
{
    private NativeRuntime.RootHandle? _activeReturnManager;
    private NativeLocalRunReturnTransition? _activeReturnOperation;
    private bool _activeReturnQueued, _activeReturnBegun, _activeReturnMenuRestored, _activeReturnObserversReleased;
    private bool _activeReturnManagerFreeAttempted;

    // Called only by the already-classified actual ResultBack boundary. Record
    // intent here; native scene/globals/menu work belongs to a later drained Tick.
    internal static bool EnqueueActiveReturn(LocalRunSessionRegistry registry,
        LocalRunSessionRegistry.SessionHandle session, long exactReceiver)
    {
        NativeLocalRunLaunchCoordinator[] owners;
        lock (Gate) owners = ProcessOwners.Where(owner => ReferenceEquals(owner._session, session)
            && ReferenceEquals(owner._host.Sessions, registry)).ToArray();
        if (owners.Length == 0) return false;
        if (owners.Length != 1) throw new InvalidOperationException("An exact active return has multiple retained launch owners.");
        var owner = owners[0]; owner._main.VerifyAccess(); var current = registry.Read(session);
        if (exactReceiver == 0 || current.ResultSceneIdentity != exactReceiver
            || !ReferenceEquals(registry.FindProtectedSession(LocalBoundaryKind.ResultBack, exactReceiver), session)
            || current.Identity != owner._reservation?.State.Identity || current.ControllerIdentity == 0
            || current.CleanupAttempted || current.ProtectionRetired || current.Phase == LocalRunSessionPhase.CleanupUnknown
            || owner._phase == NativeLocalRunLaunchPhase.Unknown)
            throw new InvalidOperationException("The active return request is not this exact protected result receiver.");
        owner._activeReturnQueued = true; return true;
    }

    private void PumpRequestedActiveReturn()
    {
        if (_session is null || _reservation is null || _sceneTransition is null || _menu is null || _globals is null) return;
        var current = _host.Sessions.Read(_session);
        if (!_activeReturnQueued && !current.ReturnRequested || current.Phase == LocalRunSessionPhase.Closed) return;
        if (current.Identity != _reservation.State.Identity || _phase == NativeLocalRunLaunchPhase.Unknown
            || current.CleanupAttempted || current.ProtectionRetired || current.Phase == LocalRunSessionPhase.CleanupUnknown)
            throw new InvalidOperationException("The requested active return lost its exact protected launch.");
        var host = _host.ReadNativeBoundaryActivity(); var engine = _engineCoroutines!.Read();
        var cancellation = _coroutineCancellation!.Read();
        if (!host.Known || engine.Unknown || !engine.NativeKnown || cancellation.Unknown || !cancellation.NativeKnown)
            throw new InvalidOperationException("Active return native boundary activity is Unknown.");
        // An explicit cleanup return must remain possible while cancellation
        // completion awaits death/source closure. Only actual open physical
        // calls block navigation; semantic Pending still blocks Close/free.
        if (host.InFlight != 0 || engine.NativeInFlight != 0 || cancellation.NativeInFlight != 0
            || cancellation.Ledger.Outstanding != 0 || !cancellation.Ledger.CanConfirmRequestIdle) return;
        if (!_activeReturnBegun)
        {
            // An explicit classified request (or an already begun exact registry
            // return) precedes every side effect. This is not automatic Return.
            if (!current.ReturnRequested) _host.Sessions.BeginReturn(_session);
            if (!current.ProviderWithdrawn) _host.Sessions.MarkProviderWithdrawn(_session);
            // Shared UI retires windows on scene changes. Checked disposal must
            // precede the native request, while callbacks still belong to this
            // exact presentation; external retirement cannot certify their end.
            if (!_handler.TryWithdrawOwnedPresentation(_reservation.State)) return;
            _activeReturnBegun = true; // Unknown native outcomes cannot be retried.
            if (_activeReturnManager is null) throw new InvalidOperationException("The active return has no independently retained original manager.");
            _menu.BeginReturnOnce();
            var existing = _sceneTransition.OwnedReturnFor(_reservation.State);
            try { _activeReturnOperation = existing ?? NativeLocalRunReturnTransition.Begin(_api, _main, _profile, _module,
                _activeReturnManager, _host.Sessions, _session, _sceneTransition); }
            catch (NativeLocalRunReturnTransitionException error) { _activeReturnOperation = error.Responsibility; throw; }
        }
        if (_activeReturnOperation is null || !_activeReturnOperation.Tick()) return;
        if (!_activeReturnMenuRestored)
        {
            _menu.RestoreAfterReturn(_activeReturnOperation);
            _ = _globals.RestoreIfOwned();
            _activeReturnMenuRestored = true;
        }
        _handler.TryWithdrawOwnedPresentation(_reservation.State);
    }

    private bool ReleaseActiveReturnObserversAfterClose()
    {
        if (_activeReturnObserversReleased) return true;
        VerifyActiveControllerFactoryRetirement(_controller!, _factoryRetirementSeal!);
        if (_activeReturnOperation is null || !_activeReturnMenuRestored)
            throw new InvalidOperationException("Exact return completion is missing before its independent observer cleanup.");
        _activeReturnOperation.ReleaseAfterCompleted();
        VerifyActiveControllerFactoryRetirement(_controller!, _factoryRetirementSeal!);
        if (!_sceneTransition!.Read().Closed) _sceneTransition.ReleaseAfterCompleted();
        VerifyActiveControllerFactoryRetirement(_controller!, _factoryRetirementSeal!);
        if (_activeReturnManager is not null)
        {
            if (_activeReturnManagerFreeAttempted)
                throw new InvalidOperationException("The active return manager root free is Unknown and cannot be retried.");
            _activeReturnManagerFreeAttempted = true;
            _activeReturnManager.Dispose(); _activeReturnManager = null;
            VerifyActiveControllerFactoryRetirement(_controller!, _factoryRetirementSeal!);
        }
        _reservation!.ReleaseUntransferredAfterClose();
        if (!_reservation.UntransferredCleanupCompleted) return false;
        _activeReturnObserversReleased = true; return true;
    }
}
