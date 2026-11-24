namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekHoldGeometryCommands
{
    private bool _transformOwnershipAcknowledged;

    private void VerifyRegisteredOwner()
    {
        if (_thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("The original Hold geometry owner belongs to its capture thread.");
        if (!ReferenceEquals(_registered, this))
            throw new InvalidOperationException("A copied Hold geometry owner cannot acknowledge the original graph.");
    }

    internal void AcknowledgeVerifiedTransformOwnership()
    {
        VerifyRegisteredOwner();
        VerifyAppliedTransforms();
        Operation(() =>
        {
            if (_unknown || _failure is not null || _cleanupFailure is not null
                || !_executionFinished || _restorationEntered
                || _rows.Any(row => row is { NativeEntered: true }
                    && (!row.NativeReturned || !row.CommandedPropertiesObserved || row.Failure is not null)))
                throw new InvalidOperationException("Only independently verified, normally returned Hold Transform commands can release their separate roots.");
            _transformOwnershipAcknowledged = true; return 0;
        });
    }

    internal void RevokeTransformOwnershipAcknowledgement()
    { VerifyRegisteredOwner(); _transformOwnershipAcknowledged = false; }

    internal void AcknowledgeRestoredTransformOwnership()
    {
        VerifyRegisteredOwner();
        if (!VerifyRestoredValues()) throw new InvalidOperationException("The original Hold transform values were not observed.");
        Operation(() =>
        {
            if (_unknown || _cleanupFailure is not null || !_restorationFinished
                || _restoreEffects.Any(row => row.NativeEntered
                    && (!row.NativeReturned || !row.OriginalValueObserved || row.Failure is not null)))
                throw new InvalidOperationException("An unresolved Hold inverse cannot release its separate roots.");
            _transformOwnershipAcknowledged = true; return 0;
        });
    }
}
