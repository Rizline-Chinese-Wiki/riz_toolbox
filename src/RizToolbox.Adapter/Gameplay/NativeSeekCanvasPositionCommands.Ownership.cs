namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekCanvasPositionCommands
{
    private bool _targetOwnershipAcknowledged;

    // The scene retains these existing Transforms. This only authorizes dropping
    // our duplicate handles after every owned synchronous call has returned;
    // historical external effects remain recorded and are not called quiescent.
    internal void AcknowledgeVerifiedTargetOwnership()
    {
        RevokeTargetOwnershipAcknowledgement();
        Operation(() =>
        {
            // The shared source guard belongs to this child access. Keep the
            // actual readback and acknowledgement inside its original scope;
            // the aggregate's managed operation does not hold that scope.
            if (!VerifyAppliedValuesCore() || _restorationEntered || _pendingCommand >= 0 || _unrecognized
                || _failure is not null || _cleanupException is not null
                || _commands.Any(row => row.NativeEntered && (!row.NativeReturned || !row.TargetObserved || row.Failure is not null)))
                throw new InvalidOperationException("The Canvas target has an unfinished, failed or unrecognized original setter responsibility.");
            Local(); _targetOwnershipAcknowledged = true; return 0;
        }, SeekCanvasPositionDirection.Apply);
    }

    internal void RevokeTargetOwnershipAcknowledgement()
    { CheckThread(); _targetOwnershipAcknowledged = false; }

    internal void AcknowledgeVerifiedRestoredOwnership()
    {
        RevokeTargetOwnershipAcknowledgement();
        Operation(() =>
        {
            if (!_restorationFinished || !VerifyRestoredValuesCore()
                || _pendingCommand >= 0 || _unrecognized || _failure is not null || _cleanupException is not null
                || _commands.Any(row => row.NativeEntered && (!row.NativeReturned || !row.TargetObserved || row.Failure is not null
                    || row.Direction == SeekCanvasPositionDirection.Restore && !row.OriginalObserved)))
                throw new InvalidOperationException("The original Canvas positions retain an unfinished, failed or unrecognized inverse.");
            Local(); _targetOwnershipAcknowledged = true; return 0;
        }, SeekCanvasPositionDirection.Restore);
    }
}
