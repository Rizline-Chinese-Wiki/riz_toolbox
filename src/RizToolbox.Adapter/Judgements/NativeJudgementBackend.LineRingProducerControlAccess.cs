using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private sealed partial class NativeSeekNoteFlagsAccess
    {
        private sealed partial class CanvasSource
        {
            private sealed partial class LineRingReader
            {
                public NativeLineProducerControlSnapshot ReadProducerControl()
                {
                    VerifySource();
                    var result = N(() => _source._core._owner._api.ReadRootedLineProducerControl(_control, VerifyLocal));
                    VerifySource();
                    if (result.Control.ToInt64() != _controlAddress)
                        throw Changed("The selected producer control observation lost its original receiver.");
                    return result;
                }

                public NativeLineProducerControlWriteObservation WriteProducerControl(
                    NativeLineProducerControlSnapshot expected, NativeLineProducerPatch patch,
                    Action verifyExpected, Action beforeStore)
                {
                    ArgumentNullException.ThrowIfNull(verifyExpected); ArgumentNullException.ThrowIfNull(beforeStore);
                    VerifySource();
                    if (expected.Control.ToInt64() != _controlAddress)
                        throw new InvalidOperationException("The producer control patch names another original source.");
                    NativeLineProducerControlWriteObservation? completed = null;
                    void Source() { VerifySource(); VerifyLinePositionsAssociation(); verifyExpected(); VerifyLocal(); }
                    try
                    {
                        var result = N(() =>
                        {
                            var value = _source._core._owner._api.WriteRootedLineProducerControlSlice(
                                _control, expected, patch, VerifyLocal, Source, beforeStore);
                            completed = value; return value;
                        });
                        VerifySource(); VerifyLinePositionsAssociation(); return result;
                    }
                    catch (Exception error)
                    {
                        if (completed is not null)
                        {
                            error.Data["LineProducerControlOriginalRoot"] = _control;
                            error.Data["LineProducerControlStoreEntered"] = true;
                            error.Data["LineProducerControlWriteObservation"] = completed;
                        }
                        throw;
                    }
                }

                public NativeVector3BufferSliceWriteObservation WriteProducerArraySlice(
                    NativeRootedVector3Buffer expected, NativeVector3BufferSlice slice,
                    Action verifyExpected, Action beforeStore)
                {
                    ArgumentNullException.ThrowIfNull(verifyExpected); ArgumentNullException.ThrowIfNull(beforeStore);
                    var original = OriginalLinePositions();
                    var field = Field(_classes[3], "linePositions", "UnityEngine.Vector3[]", 0x78);
                    if (expected.Array.ToInt64() != _linePositionsAddress || expected.Class != field.FieldClass)
                        throw new InvalidOperationException("The producer slice is not in the original shared scratch.");
                    NativeVector3BufferSliceWriteObservation? completed = null;
                    void Source() { VerifySource(); VerifyLinePositionsAssociation(); verifyExpected(); VerifyLocal(); }
                    try
                    {
                        var result = N(() =>
                        {
                            var value = _source._core._owner._api.WriteRootedLineVector3BufferSlice(
                                original, field.FieldClass, expected, slice, VerifyLocal, Source, beforeStore);
                            completed = value; return value;
                        });
                        VerifySource(); VerifyLinePositionsAssociation(); return result;
                    }
                    catch (Exception error)
                    {
                        if (completed is not null)
                        {
                            error.Data["LineVector3SliceOriginalRoot"] = original;
                            error.Data["LineVector3SliceStoreEntered"] = true;
                            error.Data["LineVector3SliceWriteObservation"] = completed;
                        }
                        throw;
                    }
                }
            }
        }
    }
}
