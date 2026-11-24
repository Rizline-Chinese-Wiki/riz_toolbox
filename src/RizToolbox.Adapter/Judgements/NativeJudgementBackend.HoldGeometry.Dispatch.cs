using RizToolbox.Adapter.Gameplay;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private sealed partial class NativeSeekNoteFlagsAccess
    {
        private sealed partial class HoldAccess
        {
            internal sealed partial class GeometryAccess
            {
                public void Dispatch(SeekHoldGeometryOperation operation, SeekHoldGeometrySnapshot expected, Action beforeDispatch)
                {
                    ArgumentNullException.ThrowIfNull(operation); ArgumentNullException.ThrowIfNull(expected); ArgumentNullException.ThrowIfNull(beforeDispatch);
                    VerifyCurrent();
                    if ((uint)operation.Ordinal >= (uint)_source._holds.Count) throw new ArgumentOutOfRangeException(nameof(operation));
                    var current = ReadSnapshot(operation.Ordinal);
                    if (!SeekHoldGeometryState.Same(current, expected)) throw Changed("The selected geometry changed before its conditional dispatch.");
                    var note = current.Notes.Single();
                    if (!SeekHoldGeometryState.Finite(operation.Value)) throw new InvalidDataException("A geometry operation vector is nonfinite.");
                    switch (operation.Kind)
                    {
                        case SeekHoldGeometryOperationKind.SetParent:
                            var allowed = operation.Transform == note.EndFlagTransform && operation.Parent == note.EndFlagCanvasParent
                                || operation.Transform == note.NoteTransform && (operation.Parent == note.LineTransform
                                    || operation.Parent == note.RingTransform || operation.Parent == note.CanvasParent);
                            if (!allowed || !operation.WorldPositionStays || operation.Parent == 0)
                                throw new InvalidDataException("The parent operation is outside the selected Hold contract.");
                            for (var cursor = operation.Parent; cursor != 0; cursor = _parents[cursor])
                                if (cursor == operation.Transform) throw new InvalidDataException("A geometry reparent would introduce a cycle.");
                            DispatchParent(operation.Transform, operation.Parent, beforeDispatch); break;
                        case SeekHoldGeometryOperationKind.SetLocalPosition:
                            if (operation.Transform != note.NoteTransform && operation.Transform != note.EndFlagTransform)
                                throw new InvalidDataException("The local-position receiver is not a selected Hold head/end flag.");
                            DispatchVector(operation.Transform, _profile.SetLocalPositionMethod, "set_localPosition", operation.Value, beforeDispatch); break;
                        case SeekHoldGeometryOperationKind.SetWorldPosition:
                            if (operation.Transform != note.NoteTransform) throw new InvalidDataException("Only the selected Hold head may be world-aligned.");
                            DispatchVector(operation.Transform, _profile.SetPositionMethod, "set_position", operation.Value, beforeDispatch); break;
                        case SeekHoldGeometryOperationKind.SetLocalScale:
                            if (operation.Transform != note.HoldLineTransform) throw new InvalidDataException("Only the selected Hold body may be scaled.");
                            DispatchVector(operation.Transform, _profile.SetLocalScaleMethod, "set_localScale", operation.Value, beforeDispatch); break;
                        default: throw new ArgumentOutOfRangeException(nameof(operation));
                    }
                }
            }
        }
    }
}
