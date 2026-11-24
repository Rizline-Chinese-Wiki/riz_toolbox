namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekLineEndpointCommands
{
    private bool _targetOwnershipAcknowledged;

    internal void AcknowledgeVerifiedTargetOwnership()
    {
        RevokeTargetOwnershipAcknowledgement();
        if (!VerifyAppliedValues())
            throw new InvalidOperationException("The original scene endpoint targets are not current.");
        if (_rendererPreparationEntered && !VerifyAppliedDynamicRendererValues())
            throw new InvalidOperationException("The original line renderer targets are not current.");
        if (_lineBufferPrepared && !VerifyOriginalLinePositionsBuffer())
            throw new InvalidOperationException("The original LineControl scratch target is not current.");
        Local();
        if (_restorationEntered || _rendererRestorationEntered || _lineBufferRestorationEntered
            || _rendererStarting || _pending >= 0 || _rendererPending >= 0 || _lineBufferPending >= 0
            || _unrecognized || _rendererUnrecognized || _lineBufferUnrecognized
            || _failure is not null || _cleanupFailure is not null
            || _commands.Any(row => row.NativeEntered && (!row.NativeReturned || !row.TargetObserved || row.Failure is not null))
            || _rendererCommands.Any(row => row.AdmissionEntered && (!row.NativeEntered || !row.NativeReturned || !row.TargetObserved || row.Failure is not null))
            || _lineBufferCommands.Any(row => row.StoreAdmissionEntered && (!row.StoreEntered || !row.StoreReturned || !row.TargetObserved || row.Failure is not null)))
            throw new InvalidOperationException("The scene target has an unfinished, failed or unrecognized line-writer responsibility.");
        // The scene still owns these actual objects. Historical external
        // effects stay recorded; only duplicate roots release at Dispose after
        // the existing reader/source operation scopes have drained.
        _targetOwnershipAcknowledged = true;
    }

    internal void RevokeTargetOwnershipAcknowledgement()
    { CheckThread(); _targetOwnershipAcknowledged = false; }

    internal void AcknowledgeVerifiedRestoredOwnership()
    {
        RevokeTargetOwnershipAcknowledgement();
        if (!_restorationFinished || !VerifyRestoredValues())
            throw new InvalidOperationException("The complete original endpoint parent/local values were not restored.");
        if (_rendererPreparationEntered && (!_rendererRestorationFinished || !VerifyRestoredDynamicRendererSavedValues()))
            throw new InvalidOperationException("The original renderer, producer and scratch values were not restored.");
        if (_lineBufferPrepared && (_lineBufferStoreAdmitted
            ? !_lineBufferRestorationFinished || !VerifyRestoredOriginalLinePositionsBuffer()
            : !VerifyOriginalLinePositionsBuffer()))
            throw new InvalidOperationException("The original complete LineControl scratch was not restored.");
        // Renderer/scratch getters can reenter native code. Re-observe the
        // endpoint owner after those scopes, and retain its final proof.
        if (!VerifyRestoredValues())
            throw new InvalidOperationException("The original endpoint values changed during the other inverse readbacks.");
        Local();
        if (_rendererStarting || _pending >= 0 || _rendererPending >= 0 || _lineBufferPending >= 0
            || _unrecognized || _rendererUnrecognized || _lineBufferUnrecognized
            || _failure is not null || _cleanupFailure is not null
            || _commands.Any(row => row.NativeEntered && (!row.NativeReturned || !row.TargetObserved || row.Failure is not null))
            || _rendererCommands.Any(row => row.AdmissionEntered && (!row.NativeEntered || !row.NativeReturned || !row.TargetObserved || row.Failure is not null))
            || _lineBufferCommands.Any(row => row.StoreAdmissionEntered && (!row.StoreEntered || !row.StoreReturned || !row.TargetObserved || row.Failure is not null)))
            throw new InvalidOperationException("The original line values retain an unfinished, failed or unrecognized inverse.");
        _targetOwnershipAcknowledged = true;
    }
}
