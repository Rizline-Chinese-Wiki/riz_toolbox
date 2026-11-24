using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.UiInput;
using RizToolbox.Runtime;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private sealed partial class NativeSeekNoteFlagsAccess
    {
        private sealed partial class CanvasSource
        {
            private sealed partial class LineRingReader : IRetainedSeekLineEndpointAccess
            {
                private ISeekCanvasPositionNativeProfile? _endpointProfile;
                private NativeMetadataMethod? _endpointGet, _endpointSet, _endpointParent;
                private bool _endpointFinalAdmission;

                public void BindWriterProfile(ISeekCanvasPositionNativeProfile profile)
                {
                    VerifyLocal(); ArgumentNullException.ThrowIfNull(profile);
                    if (_endpointProfile is not null)
                        throw new InvalidOperationException("The retained line reader accepts one endpoint writer only.");
                    _endpointProfile = profile;
                    VerifyWriterProfile(initial: true);
                }

                private void VerifyWriterProfile(bool initial = false)
                {
                    VerifyLocal();
                    var profile = _endpointProfile ?? throw new InvalidOperationException("The endpoint writer profile is absent.");
                    N(() => { profile.VerifyEndpointCurrent(VerifyLocal); return 0; });
                    if (N(() => profile.TransformClass) != _classes[7])
                        throw new UnsupportedSignatureException("The endpoint writer names another Transform class.");
                    var api = _source._core._owner._api;
                    var get = N(() => api.InspectLineEndpointPositionMethod(_classes[7], "get_localPosition", IntPtr.Zero, VerifyLocal));
                    var set = N(() => api.InspectLineEndpointPositionMethod(_classes[7], "set_localPosition", IntPtr.Zero, VerifyLocal));
                    var parent = N(() => api.InspectLineEndpointParentMethod(_classes[7], IntPtr.Zero, VerifyLocal));
                    if (get.MethodInfo != N(() => profile.GetLocalPositionMethod)
                        || set.MethodInfo != N(() => profile.SetLocalPositionMethod)
                        || parent.MethodInfo != N(() => profile.SetParentMethod)
                        || !SameMethod(get, _getters["get_localPosition"])
                        || !initial && (_endpointGet is null || _endpointSet is null
                            || _endpointParent is null || !SameMethod(get, _endpointGet)
                            || !SameMethod(set, _endpointSet) || !SameMethod(parent, _endpointParent)))
                        throw new UnsupportedSignatureException("The selected endpoint getter/setters differ from the original pinned Transform ABI.");
                    if (initial) { _endpointGet = get; _endpointSet = set; _endpointParent = parent; }
                }

                public SeekLineEndpointObserved ReadEndpoint(long transform)
                {
                    VerifyWriterProfile();
                    // Final setter revalidation already holds NoteView's native
                    // dispatch gate; do not open a nested source verification.
                    // The outer capture operation checks the full source before
                    // and after this native sequence, preserving all effects.
                    if (!_endpointFinalAdmission) VerifySource();
                    var root = Root(transform); VerifyTransform(root);
                    var parent = Address(TransformReference("get_parent", root, required: false));
                    var local = TransformValue<NativeVector3>("get_localPosition", root);
                    var middleParent = Address(TransformReference("get_parent", root, required: false));
                    var second = TransformValue<NativeVector3>("get_localPosition", root);
                    var finalParent = Address(TransformReference("get_parent", root, required: false));
                    if (parent != middleParent || parent != finalParent || !SameVector(local, second))
                        throw new SeekLineEndpointObservationConflict("The selected endpoint parent/local changed between observations.");
                    VerifyLocal(); return new(transform, parent, local);
                }

                public long ReadParent(long transform)
                {
                    VerifyWriterProfile();
                    if (!_endpointFinalAdmission) VerifySource();
                    var root = Root(transform); VerifyTransform(root);
                    var first = Address(TransformReference("get_parent", root, required: false));
                    var second = Address(TransformReference("get_parent", root, required: false));
                    var third = Address(TransformReference("get_parent", root, required: false));
                    if (first != second || first != third)
                        throw new SeekLineEndpointObservationConflict("A parent graph edge changed within one observation.");
                    VerifyLocal(); return third;
                }

                public NativeVector3 ReadWorld(long transform)
                {
                    VerifyWriterProfile();
                    if (!_endpointFinalAdmission) VerifySource();
                    var root = Root(transform); VerifyTransform(root);
                    // This is the source reader's already pinned get_position
                    // on the actual rooted Transform, after endpoint writes.
                    // The command owner compares two full parent/local/world
                    // observations before publishing any owned result.
                    var world = TransformValue<NativeVector3>("get_position", root);
                    VerifyLocal(); return world;
                }

                public void WriteEndpointLocal(long transform, long expectedParent, NativeVector3 expected,
                    NativeVector3 value, Action verifyExpectedVector, Action beforeDispatch)
                {
                    ArgumentNullException.ThrowIfNull(beforeDispatch); ArgumentNullException.ThrowIfNull(verifyExpectedVector);
                    VerifyWriterProfile(); VerifySource();
                    var current = ReadEndpoint(transform);
                    if (current.Parent != expectedParent || !SameVector(current.Local, expected))
                        throw new SeekLineEndpointObservationConflict("The selected endpoint parent/local differs before conditional setter dispatch.");
                    var root = Root(transform); VerifyTransform(root);
                    var receiver = _source.Target(root);
                    var method = _endpointSet ?? throw new InvalidOperationException("The exact endpoint setter is absent.");
                    using var argument = A.Value(value);
                    _source._core._view!.WithRetainedSource(() => N(() =>
                    {
                        _source._core._owner._api.InvokeLineEndpointLocalWithPublicationAdmission(method.MethodInfo, receiver,
                            VerifyLocal, () =>
                            {
                                if (_endpointFinalAdmission)
                                    throw new InvalidOperationException("Endpoint final admission cannot synchronously reenter.");
                                _endpointFinalAdmission = true;
                                try
                                {
                                    VerifyWriterProfile(); verifyExpectedVector(); VerifyWriterProfile();
                                    VerifyLocal();
                                }
                                finally { _endpointFinalAdmission = false; }
                            }, () => { VerifyLocal(); beforeDispatch(); VerifyLocal(); }, argument);
                        return 0;
                    }));
                    VerifyLocal();
                }

                public void WriteEndpointParent(long transform, long expectedParent, NativeVector3 expectedLocal,
                    long parent, Action verifyExpectedVector, Action beforeDispatch)
                {
                    ArgumentNullException.ThrowIfNull(beforeDispatch); ArgumentNullException.ThrowIfNull(verifyExpectedVector);
                    VerifyWriterProfile(); VerifySource();
                    var current = ReadEndpoint(transform);
                    if (current.Parent != expectedParent || !SameVector(current.Local, expectedLocal))
                        throw new SeekLineEndpointObservationConflict("The selected endpoint parent/local differs before SetParent dispatch.");
                    var root = Root(transform); VerifyTransform(root);
                    var receiver = _source.Target(root);
                    var parentReceiver = IntPtr.Zero;
                    if (parent != 0)
                    {
                        var parentRoot = Root(parent); VerifyTransform(parentRoot);
                        parentReceiver = _source.Target(parentRoot);
                    }
                    var method = _endpointParent ?? throw new InvalidOperationException("The exact SetParent wrapper is absent.");
                    using var parentArgument = A.Ref(parentReceiver);
                    using var falseArgument = A.Value(false);
                    _source._core._view!.WithRetainedSource(() => N(() =>
                    {
                        _source._core._owner._api.InvokeLineEndpointLocalWithPublicationAdmission(method.MethodInfo, receiver,
                            VerifyLocal, () =>
                            {
                                if (_endpointFinalAdmission)
                                    throw new InvalidOperationException("SetParent final admission cannot synchronously reenter.");
                                _endpointFinalAdmission = true;
                                try { VerifyWriterProfile(); verifyExpectedVector(); VerifyWriterProfile(); VerifyLocal(); }
                                finally { _endpointFinalAdmission = false; }
                            }, () => { VerifyLocal(); beforeDispatch(); VerifyLocal(); }, parentArgument, falseArgument);
                        return 0;
                    }));
                    VerifyLocal();
                }

                private static bool SameVector(NativeVector3 a, NativeVector3 b)
                    => BitConverter.SingleToInt32Bits(a.X) == BitConverter.SingleToInt32Bits(b.X)
                    && BitConverter.SingleToInt32Bits(a.Y) == BitConverter.SingleToInt32Bits(b.Y)
                    && BitConverter.SingleToInt32Bits(a.Z) == BitConverter.SingleToInt32Bits(b.Z);
            }
        }
    }
}
