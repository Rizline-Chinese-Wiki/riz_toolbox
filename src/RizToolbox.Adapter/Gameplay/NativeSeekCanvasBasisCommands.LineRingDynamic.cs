namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekCanvasBasisCommands
{
    // Only the original aggregate can attest its cache and position values.
    // The caller must first perform VerifyAppliedValues, which takes fresh
    // complete native observations. This managed check rejects a withdrawn or
    // superseded proof without accepting a journal/value-matched replacement.
    internal void RequireAppliedLineRingSource(NativeSeekLineRingSourceCapture source)
    {
        CheckThread(); DiagnosticEntry(); ArgumentNullException.ThrowIfNull(source);
        InvalidateStaleProofs();
        if (!_executionFinished || _restorationEntered || !_appliedValuesVerified
            || _sourceCapture is null || _plan is null
            || !ReferenceEquals(source.CanvasCapture, _sourceCapture)
            || !ReferenceEquals(source.Snapshot.Identity.CanvasCapture, _sourceCapture)
            || !ReferenceEquals(source.Snapshot.Identity.CanvasPlan, _plan))
            throw new InvalidOperationException("The exact original Canvas aggregate has no current verified applied values for this line source.");
        Local();
    }

    // The source is already inside its one managed operation. Accessing its
    // public diagnostic properties here would reenter that operation. This
    // exact-owner check uses its retained private association in place.
    internal void RequireAppliedLineRendererSourceInOperation(NativeSeekLineRingSourceCapture source,
        NativeSeekLineEndpointCommands owner)
    {
        CheckThread(); DiagnosticEntry(); ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(owner); InvalidateStaleProofs();
        if (!_executionFinished || _restorationEntered || !_appliedValuesVerified
            || _sourceCapture is null || _plan is null)
            throw new InvalidOperationException("The original Canvas aggregate has no current applied proof for this renderer operation.");
        source.RequireRendererCanvasAssociationInOperation(owner, _sourceCapture, _plan);
        Local();
    }
}
