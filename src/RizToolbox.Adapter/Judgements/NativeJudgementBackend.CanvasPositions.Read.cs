using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.UiInput;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private sealed partial class NativeSeekNoteFlagsAccess
    {
        private sealed partial class CanvasSource
        {
            private sealed partial class PositionAccess
            {
                private NativeVector3 ReadOne(int ordinal)
                {
                    var root = Transform(ordinal); var receiver = _source.Target(root);
                    var method = Method("get_localPosition", N(() => _profile.GetLocalPositionMethod));
                    if (!SameMethod(method, _get)) throw new InvalidOperationException("The Canvas position getter changed after capture.");
                    var value = default(NativeVector3);
                    _source._core._view!.WithRetainedSource(() => value = N(() =>
                        _source._core._owner._api.InvokeValueWithPublicationGuarded<NativeVector3>(method.MethodInfo, receiver, VerifyLocal)));
                    VerifyLocal(); return value;
                }
                public IReadOnlyList<NativeVector3> ReadSnapshot()
                {
                    VerifySource();
                    var first = new NativeVector3[_transforms.Length];
                    for (var i = 0; i < first.Length; ++i) first[i] = ReadOne(i);
                    var second = new NativeVector3[_transforms.Length];
                    for (var i = 0; i < second.Length; ++i) second[i] = ReadOne(i);
                    VerifySource();
                    for (var i = 0; i < first.Length; ++i)
                        if (!SameBits(first[i], second[i]))
                            throw NativeSeekCanvasPositionConflict.SnapshotError(i, _transforms, first, second);
                    return Array.AsReadOnly(second);
                }
                public NativeVector3 ReadSlot(int ordinal)
                {
                    VerifySource(); var first = ReadOne(ordinal); var second = ReadOne(ordinal); VerifySource();
                    if (!SameBits(first, second))
                        throw NativeSeekCanvasPositionConflict.Error(ordinal, _transforms[ordinal], first, second, multipleSamples: true);
                    return second;
                }
                public void WriteSlot(int ordinal, NativeVector3 value, NativeVector3 expected, Action beforeDispatch)
                {
                    ArgumentNullException.ThrowIfNull(beforeDispatch); VerifySource();
                    var root = Transform(ordinal);
                    var method = Method("set_localPosition", N(() => _profile.SetLocalPositionMethod));
                    if (!SameMethod(method, _set)) throw new InvalidOperationException("The Canvas position setter changed after capture.");
                    var observed = ReadOne(ordinal);
                    if (!SameBits(observed, expected))
                        throw NativeSeekCanvasPositionConflict.Error(ordinal, _transforms[ordinal], expected, observed);
                    var receiver = _source.Target(root);
                    using var argument = A.Value(value);
                    _source._core._view!.WithRetainedSource(() => N(() =>
                    {
                        _source._core._owner._api.InvokeVoidWithPublicationAdmission(method.MethodInfo, receiver, VerifyLocal,
                            () => { VerifyLocal(); beforeDispatch(); VerifyLocal(); }, argument);
                        return 0;
                    }));
                    VerifyLocal();
                }
            }
        }
    }
}
