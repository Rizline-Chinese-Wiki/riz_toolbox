using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private sealed partial class NativeSeekNoteFlagsAccess
    {
        private sealed partial class CanvasSource
        {
            private sealed partial class LineRingReader : IRetainedSeekLineBufferAccess
            {
                private const int MaximumOriginalLinePositions = NativeRuntime.MaximumLineVector3BufferLength;

                private NativeRuntime.RootHandle OriginalLinePositions()
                {
                    VerifySource();
                    var current = RequiredReference(_control, _classes[3],
                        "linePositions", "UnityEngine.Vector3[]", 0x78);
                    var address = Address(current);
                    if (_linePositions is null)
                    {
                        // The original shared source table retains this exact
                        // physical root before any array metadata or copy runs.
                        _linePositions = current;
                        _linePositionsAddress = address;
                    }
                    else if (address != _linePositionsAddress)
                        throw Changed("The original LineControl.linePositions array reference changed.");
                    VerifyLinePositionsAssociation();
                    return _linePositions;
                }

                private void VerifyLinePositionsAssociation()
                {
                    var original = _linePositions;
                    if (original is null) return;
                    var current = RequiredReference(_control, _classes[3],
                        "linePositions", "UnityEngine.Vector3[]", 0x78);
                    if (Address(original) != _linePositionsAddress
                        || Address(current) != _linePositionsAddress)
                        throw Changed("The original LineControl no longer owns its retained linePositions array.");
                    VerifyLocal();
                }

                public NativeRootedVector3Buffer ReadOriginalLinePositions()
                {
                    var original = OriginalLinePositions();
                    var field = Field(_classes[3], "linePositions", "UnityEngine.Vector3[]", 0x78);
                    var observed = N(() => _source._core._owner._api.ReadRootedLineVector3Buffer(
                        original, field.FieldClass, MaximumOriginalLinePositions, VerifyLocal));
                    VerifySource();
                    if (observed.Array.ToInt64() != _linePositionsAddress || observed.Class != field.FieldClass)
                        throw Changed("The complete original linePositions observation names another array.");
                    VerifyLinePositionsAssociation();
                    return observed;
                }

                public NativeVector3BufferWriteObservation WriteOriginalLinePositionsPrefix(
                    NativeRootedVector3Buffer expected, IReadOnlyList<NativeVector3BufferBits> prefix,
                    Action beforeStore)
                    => WriteOriginalLinePositionsPrefixCore(expected, prefix, beforeStore, null);

                private NativeVector3BufferWriteObservation WriteOriginalLinePositionsPrefixCore(
                    NativeRootedVector3Buffer expected, IReadOnlyList<NativeVector3BufferBits> prefix,
                    Action beforeStore, Action? verifyExpected)
                {
                    ArgumentNullException.ThrowIfNull(expected);
                    ArgumentNullException.ThrowIfNull(prefix);
                    ArgumentNullException.ThrowIfNull(beforeStore);
                    var original = OriginalLinePositions();
                    var field = Field(_classes[3], "linePositions", "UnityEngine.Vector3[]", 0x78);
                    if (expected.Array.ToInt64() != _linePositionsAddress || expected.Class != field.FieldClass)
                        throw new NativeVector3BufferConflict("The expected Vector3 buffer is not the original LineControl scratch.");
                    void Source()
                    {
                        // Runtime calls this again after its metadata, pin and
                        // before-store callbacks. No public source Operation is
                        // opened inside the already held shared operation.
                        VerifySource();
                        VerifyLinePositionsAssociation();
                        VerifyLocal();
                    }
                    Source();
                    NativeVector3BufferWriteObservation? completed = null;
                    try
                    {
                        // N's post-return guard can withdraw after Runtime has
                        // stored and observed the entire array. Save the actual
                        // result inside its delegate before that guard runs.
                        var observed = N(() =>
                        {
                            var value = _source._core._owner._api.WriteRootedLineVector3BufferPrefix(
                                original, field.FieldClass, expected, prefix, VerifyLocal,
                                () => { Source(); verifyExpected?.Invoke(); VerifyLocal(); }, beforeStore);
                            completed = value;
                            return value;
                        });
                        Source();
                        if (observed.Before.Array.ToInt64() != _linePositionsAddress
                            || observed.After.Array.ToInt64() != _linePositionsAddress)
                            throw Changed("The completed Vector3 buffer write lost the original array identity.");
                        return observed;
                    }
                    catch (Exception error)
                    {
                        if (completed is not null)
                        {
                            error.Data["LineVector3BufferOriginalRoot"] = original;
                            error.Data["LineVector3BufferBefore"] = completed.Before;
                            error.Data["LineVector3BufferAfter"] = completed.After;
                            error.Data["LineVector3BufferStoreEntered"] = completed.StoreEntered;
                        }
                        throw;
                    }
                }
            }
        }
    }
}
