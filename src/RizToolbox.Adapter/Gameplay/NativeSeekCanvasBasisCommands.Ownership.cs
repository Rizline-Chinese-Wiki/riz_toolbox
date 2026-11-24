namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekCanvasBasisCommands
{
    internal void AcknowledgeVerifiedTargetOwnership()
    {
        Operation("AcknowledgeTargetOwnership", () =>
        {
            if (!_executionFinished || _restorationEntered || _cleanupFailure is not null)
                throw new InvalidOperationException("Only the complete original Canvas target may release its duplicate scene roots.");
            ObserveApplied();
            _positions!.AcknowledgeVerifiedTargetOwnership();
            CommonSource(); RecordProof(restored: false); return 0;
        });
    }
    internal void RevokeTargetOwnershipAcknowledgement()
    { CheckThread(); _positions?.RevokeTargetOwnershipAcknowledgement(); }

    internal void AcknowledgeVerifiedRestoredOwnership()
    {
        RevokeTargetOwnershipAcknowledgement();
        Operation("AcknowledgeRestoredOwnership", () =>
        {
            if (!_restorationFinished || _restorationFailure is not null || _cleanupFailure is not null)
                throw new InvalidOperationException("The original Canvas basis retains an incomplete or failed inverse.");
            // A recognized cache commit failure can be fully reversed. Fresh
            // complete readback and the original position owner's native-effect
            // checks still govern release; historical errors remain recorded.
            ObserveRestored();
            _positions!.AcknowledgeVerifiedRestoredOwnership();
            CommonSource(); RecordProof(restored: true); return 0;
        });
    }
}
