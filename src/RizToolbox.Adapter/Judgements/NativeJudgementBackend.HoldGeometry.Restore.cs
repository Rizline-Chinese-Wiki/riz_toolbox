using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private sealed partial class NativeSeekNoteFlagsAccess
    {
        private sealed partial class HoldAccess
        {
            internal sealed partial class GeometryAccess
            {
                public void RestoreTransform(int ordinal, SeekHoldGeometryTransformState original,
                    SeekHoldGeometryRestoreField field, SeekHoldGeometrySnapshot expected, Action beforeDispatch)
                {
                    ArgumentNullException.ThrowIfNull(original); ArgumentNullException.ThrowIfNull(expected);
                    ArgumentNullException.ThrowIfNull(beforeDispatch); VerifyCurrent();
                    if (_context is null || (uint)ordinal >= (uint)_context.Notes.Count)
                        throw new InvalidOperationException("The original complete Hold geometry was not captured.");
                    var note = _context.Notes[ordinal];
                    if (original.Reference != note.NoteTransform && original.Reference != note.EndFlagTransform
                        && original.Reference != note.HoldLineTransform)
                        throw new InvalidOperationException("Only the original selected Hold's commanded transforms may be restored.");
                    var saved = _context.Transforms.Single(row => row.Reference == original.Reference);
                    if (!SeekHoldGeometryState.Same(saved, original))
                        throw new InvalidOperationException("A supplied transform is not this accessor's original retained baseline.");
                    var current = ReadSnapshot(ordinal);
                    if (!SeekHoldGeometryState.Same(current, expected))
                        throw Changed("The selected Hold geometry changed before its conditional inverse.");
                    switch (field)
                    {
                        case SeekHoldGeometryRestoreField.Parent:
                            if (original.Parent != 0 && !_allTransforms.Contains(original.Parent))
                                throw new InvalidOperationException("The original parent is outside the independently retained closure.");
                            for (var cursor = original.Parent; cursor != 0; cursor = _parents[cursor])
                                if (cursor == original.Reference)
                                    throw new InvalidOperationException("Restoring the original parent would introduce a current cycle.");
                            DispatchParent(original.Reference, original.Parent, beforeDispatch); break;
                        case SeekHoldGeometryRestoreField.LocalPosition:
                            DispatchVector(original.Reference, _profile.SetLocalPositionMethod,
                                "set_localPosition", original.LocalPosition, beforeDispatch); break;
                        case SeekHoldGeometryRestoreField.LocalScale:
                            DispatchVector(original.Reference, _profile.SetLocalScaleMethod,
                                "set_localScale", original.LocalScale, beforeDispatch); break;
                        case SeekHoldGeometryRestoreField.LocalRotation:
                            var c = _source._core; var root = Root(original.Reference); VerifyTransform(root);
                            var method = Setter("set_localRotation", new[] { "UnityEngine.Quaternion" }, _profile.SetLocalRotationMethod);
                            var receiver = c.Target(root);
                            using (var argument = A.Value(original.LocalRotation))
                                c.Native(() =>
                                {
                                    c._owner._api.InvokeVoidWithPublicationGuarded(method, receiver,
                                        () => { VerifyLocal(); beforeDispatch(); }, argument); return 0;
                                });
                            break;
                        default: throw new ArgumentOutOfRangeException(nameof(field));
                    }
                    VerifyLocal();
                }
            }
        }
    }
}
