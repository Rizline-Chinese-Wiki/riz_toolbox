using RizToolbox.Adapter.UiInput;

namespace RizToolbox.Adapter.Gameplay;

internal sealed record SeekLineEndpointObserved(long Transform, long Parent, NativeVector3 Local);
internal sealed record SeekLineEndpointWorldObserved(long Transform, long Parent,
    NativeVector3 Local, NativeVector3 World);

// A fresh, owned observation after the endpoint writer applied its selected
// parent/local values. This carries the exact retained owner and source; it is
// neither an atomic snapshot nor an independently reconstructed world target.
internal sealed record SeekLineEndpointAppliedWorldObservation(
    NativeSeekLineRingSourceCapture OriginalSource,
    NativeSeekLineEndpointCommands CommandOwner,
    SeekLineRingSourceIdentity SourceIdentity,
    IReadOnlyList<SeekLineEndpointWorldObserved> Endpoints)
{
    // The exact projection saved by the writer during preparation. A caller
    // cannot substitute a matching reconstruction for the writer-issued record.
    internal SeekLineRingLocalTargetProjection? LocalProjection { get; init; }
    internal bool AtomicSnapshot => false;
    internal bool CompleteSeekPermitted => false;
}

internal sealed class SeekLineEndpointObservationConflict : InvalidOperationException
{
    internal SeekLineEndpointObservationConflict(string message) : base(message) { }
}

// The original retained line reader implements this capability. It does not
// acquire a second line child or own the caller's Canvas aggregate.
internal interface IRetainedSeekLineEndpointAccess
{
    void BindWriterProfile(ISeekCanvasPositionNativeProfile profile);
    SeekLineEndpointObserved ReadEndpoint(long transform);
    long ReadParent(long transform);
    NativeVector3 ReadWorld(long transform);
    void WriteEndpointLocal(long transform, long expectedParent, NativeVector3 expected,
        NativeVector3 value, Action verifyExpectedVector, Action beforeDispatch);
    void WriteEndpointParent(long transform, long expectedParent, NativeVector3 expectedLocal,
        long parent, Action verifyExpectedVector, Action beforeDispatch);
}
