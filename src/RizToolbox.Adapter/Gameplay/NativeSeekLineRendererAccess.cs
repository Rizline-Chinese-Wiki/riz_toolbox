using RizToolbox.Runtime;

namespace RizToolbox.Adapter.Gameplay;

// Complete selected saved fields plus the actual plan/consumer associations.
// Roots remain in the original shared table; this immutable record is history.
internal sealed record SeekLineRendererSavedState(long Line, long SavedArray, int SavedCount,
    int? SavedArrayLength, long Plan, long Pool, long Child, long PlanTransform,
    bool Active, long TargetComponent, long ChildPool, long TargetTransform)
{
    internal bool CanDispatch => Plan != 0 && (!Active || Pool != 0 && Child != 0
        && PlanTransform != 0 && TargetComponent != 0 && ChildPool == Pool
        && TargetTransform == PlanTransform);
    internal bool AtomicSnapshot => false;
}

internal interface IRetainedSeekLineRendererAccess : IRetainedSeekLineBufferAccess,
    IRetainedSeekLineEndpointAccess, IRetainedSeekLineRingSceneReader
{
    void BindRendererProfile(ISeekLineSetPositionsNativeProfile profile, IReadOnlyList<long> completeLines);
    IReadOnlyList<SeekLineRendererSavedState> ReadRendererSnapshot();
    void VerifyAppliedCanvasInOperation(NativeSeekCanvasTargetPlan plan);
    NativeVector3BufferWriteObservation WriteRendererPrefix(NativeRootedVector3Buffer expected,
        IReadOnlyList<NativeVector3BufferBits> prefix, Action verifyExpected, Action beforeStore);
    void InvokeRendererSetPositions(long line, int count, Action verifyExpected, Action beforeEntry);
    void RestoreRendererField(long line, bool array, long expected, long target,
        Action verifyExpected, Action beforeStore);
    NativeLineProducerControlSnapshot ReadProducerControl();
    NativeLineProducerControlWriteObservation WriteProducerControl(NativeLineProducerControlSnapshot expected,
        NativeLineProducerPatch patch, Action verifyExpected, Action beforeStore);
    NativeVector3BufferSliceWriteObservation WriteProducerArraySlice(NativeRootedVector3Buffer expected,
        NativeVector3BufferSlice slice, Action verifyExpected, Action beforeStore);
}
