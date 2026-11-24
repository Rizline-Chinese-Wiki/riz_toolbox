namespace RizToolbox.Adapter.AudioContent;

internal sealed partial class NativeLocalRunLaunchCoordinator
{
    private readonly object _completedCommandIssuer = new();
    internal bool CommandOutcomeUnknown { get { _main.VerifyAccess(); return _phase == NativeLocalRunLaunchPhase.Unknown; } }

    // The host UI has no stock ResultScene receiver. Its authority is this
    // exact retained launch and actual cached result, never receiver==0 alone.
    internal void QueueOwnedResultReturn(LocalRunSessionRegistry registry,
        LocalRunSessionRegistry.SessionHandle session, LocalRunCapturedResult result)
    {
        RequireCommandOwner(registry, session);
        var current = registry.Read(session);
        if (current.Phase != LocalRunSessionPhase.ResultReady || current.ReturnRequested
            || current.Identity != result.Session || !ReferenceEquals(current.Result, result))
            throw new InvalidOperationException("Only this launch's exact current cached result may queue a host return.");
        _activeReturnQueued = true;
    }

    internal void QueueOwnedBoundaryReturn(LocalRunSessionRegistry registry,
        LocalRunSessionRegistry.SessionHandle session, LocalBoundaryKind boundary, long receiver)
    {
        RequireCommandOwner(registry, session);
        var current = registry.Read(session);
        if (boundary is not (LocalBoundaryKind.ControllerRetry or LocalBoundaryKind.ResultRetry or LocalBoundaryKind.ResultBack)
            || receiver == 0 || !ReferenceEquals(registry.FindProtectedSession(boundary, receiver), session)
            || (boundary == LocalBoundaryKind.ControllerRetry ? current.Phase != LocalRunSessionPhase.Committed
                : current.Phase != LocalRunSessionPhase.ResultReady))
            throw new InvalidOperationException("The queued command is not this actual protected controller/result boundary.");
        _activeReturnQueued = true;
    }

    private void RequireCommandOwner(LocalRunSessionRegistry registry, LocalRunSessionRegistry.SessionHandle session)
    {
        _main.VerifyAccess();
        var current = registry.Read(session);
        if (!ReferenceEquals(_host.Sessions, registry) || !ReferenceEquals(_session, session)
            || _reservation is null || current.Identity != _reservation.State.Identity || current.ControllerIdentity == 0
            || _phase == NativeLocalRunLaunchPhase.Unknown || _wholeLaunchRetired || current.CleanupAttempted
            || current.ProtectionRetired || current.Phase == LocalRunSessionPhase.CleanupUnknown || current.Failure is not null)
            throw new InvalidOperationException("The result command lost its exact live launch owner.");
        lock (Gate) if (!ProcessOwners.Contains(this))
            throw new InvalidOperationException("The result command has no retained actual launch coordinator.");
    }

    internal CompletedCommandReceipt? TryReadCompletedCommandReceipt(LocalRunSessionRegistry registry,
        LocalRunSessionRegistry.SessionHandle session)
    {
        _main.VerifyAccess();
        if (!ReferenceEquals(_host.Sessions, registry) || !ReferenceEquals(_session, session))
            throw new InvalidOperationException("A completed command receipt belongs to a different registry/session.");
        if (!_wholeLaunchRetired) return null;
        var receipt = new CompletedCommandReceipt(this, _completedCommandIssuer, session);
        receipt.ValidateFor(this, registry, session); return receipt;
    }

    internal sealed class CompletedCommandReceipt
    {
        private readonly NativeLocalRunLaunchCoordinator _owner;
        private readonly object _issuer;
        private readonly LocalRunSessionRegistry.SessionHandle _session;
        internal CompletedCommandReceipt(NativeLocalRunLaunchCoordinator owner, object issuer,
            LocalRunSessionRegistry.SessionHandle session) => (_owner, _issuer, _session) = (owner, issuer, session);
        internal void ValidateFor(NativeLocalRunLaunchCoordinator owner, LocalRunSessionRegistry registry,
            LocalRunSessionRegistry.SessionHandle session)
        {
            owner._main.VerifyAccess();
            if (!ReferenceEquals(owner, _owner) || !ReferenceEquals(_issuer, owner._completedCommandIssuer)
                || !ReferenceEquals(_session, session) || !ReferenceEquals(owner._session, session)
                || !ReferenceEquals(owner._host.Sessions, registry) || owner._reservation is null
                || !owner._wholeLaunchRetired || owner.CommandOutcomeUnknown || !owner._factoryRootsReleased
                || !owner._activeTerminationReceiptConsumed || string.IsNullOrWhiteSpace(owner._activeFinalOwnerEvidence))
                throw new InvalidOperationException("Retry requires the actual completed all-owner launch receipt.");
            var current = registry.Read(session);
            if (current.Identity != owner._reservation.State.Identity || current.Phase != LocalRunSessionPhase.Closed
                || !current.ProtectionRetired || !current.CleanupAttempted || current.OutstandingOwnershipRecords != 0)
                throw new InvalidOperationException("The completed launch no longer matches the exact closed registry generation.");
            lock (Gate) if (ProcessOwners.Contains(owner))
                throw new InvalidOperationException("The old launch remains a process owner; a new launch cannot begin.");
        }
    }
}
