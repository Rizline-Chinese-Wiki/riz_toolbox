using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.UiInput;
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
                // All returned references acquire a root at publication, before
                // type inspection or a post-call lifecycle check can withdraw us.
                private NativeRuntime.RootHandle? InvokeReference(IntPtr method, NativeRuntime.RootHandle receiver, bool required)
                {
                    NativeRuntime.RootHandle? value = null; var published = false; var c = _source._core;
                    c.Native(() =>
                    {
                        c._owner._api.InvokeReferenceWithPublication(method, c.Target(receiver), pointer =>
                        {
                            if (published) throw Changed("A geometry reference was published twice.");
                            published = true;
                            if (pointer != IntPtr.Zero) value = c.TargetAdopt(pointer);
                        }); return 0;
                    });
                    if (!published || required && value is null) throw Changed("A required geometry reference is absent.");
                    if (value is not null && c.Native(() => c._owner._api.ObjectClass(c.Target(value))) != _profile.TransformClass)
                        throw new UnsupportedSignatureException("A geometry getter returned another exact Transform class.");
                    return value;
                }
                private NativeRuntime.RootHandle? TransformField(NativeRuntime.RootHandle receiver, IntPtr cls,
                    string name, int offset)
                {
                    var c = _source._core;
                    var field = c.Native(() => c._owner._api.InspectExactDeclaredReadField(cls, name, "UnityEngine.Transform", false, offset));
                    NativeRuntime.RootHandle? value = null; var published = false;
                    c.Native(() =>
                    {
                        c._owner._api.PublishExactReferenceField(receiver, field, pointer =>
                        {
                            if (published) throw Changed("A geometry field was published twice.");
                            published = true;
                            if (pointer != IntPtr.Zero) value = c.TargetAdopt(pointer);
                        }); return 0;
                    });
                    if (!published) throw Changed("A geometry field had no reference publication.");
                    if (value is not null && c.Native(() => c._owner._api.ObjectClass(c.Target(value))) != _profile.TransformClass)
                        throw new UnsupportedSignatureException("An actual geometry field has another exact Transform class.");
                    return value;
                }
                private long Address(NativeRuntime.RootHandle? root)
                    => root is null ? 0 : _source._core.Target(root).ToInt64();
                private NativeRuntime.RootHandle Root(long reference)
                {
                    var c = _source._core;
                    if (reference == 0 || !c._retained.TryGetValue(reference, out var root))
                        throw Changed("Only independently retained geometry references may be dispatched.");
                    if (c.Target(root).ToInt64() != reference) throw Changed("A retained geometry reference changed identity.");
                    return root;
                }
                private void VerifyTransform(NativeRuntime.RootHandle root)
                {
                    var c = _source._core;
                    if (c.Native(() => c._owner._api.ObjectClass(c.Target(root))) != _profile.TransformClass || !c.Alive(root))
                        throw Changed("A retained geometry Transform died or changed its exact class.");
                }
                private T Value<T>(IntPtr method, NativeRuntime.RootHandle root) where T : struct
                {
                    var c = _source._core;
                    return c.Native(() => c._owner._api.InvokeValueWithPublication<T>(method, c.Target(root)));
                }
                private NativeVector3 Point(IntPtr method, NativeRuntime.RootHandle root, NativeVector3 point)
                {
                    var c = _source._core;
                    return c.Native(() =>
                    {
                        using var argument = A.Value(point);
                        return c._owner._api.InvokeValueWithPublication<NativeVector3>(method, c.Target(root), argument);
                    });
                }
                private IntPtr Setter(string name, string[] parameters, IntPtr selected)
                {
                    var c = _source._core;
                    var current = c.Native(() => c._owner._api.InspectDeclaredMethod(_profile.TransformClass,
                        name, parameters, "System.Void", IntPtr.Zero));
                    if (current.MethodInfo != selected || current.GenericMethod || current.InflatedMethod
                        || current.DeclaringClass.Class != _profile.TransformClass || (current.Flags & 0x10u) != 0)
                        throw new UnsupportedSignatureException("An actual geometry setter differs from its current exact native profile.");
                    return current.MethodInfo;
                }
                private void DispatchVector(long reference, IntPtr selected, string name, NativeVector3 value, Action beforeDispatch)
                {
                    VerifyLocal(); var c = _source._core; var root = Root(reference); VerifyTransform(root);
                    var method = Setter(name, new[] { "UnityEngine.Vector3" }, selected); var receiver = c.Target(root);
                    using var argument = A.Value(value);
                    c.Native(() =>
                    {
                        c._owner._api.InvokeVoidWithPublicationGuarded(method, receiver, () =>
                        { VerifyLocal(); beforeDispatch(); }, argument); return 0;
                    });
                }
                private void DispatchParent(long reference, long parent, Action beforeDispatch)
                {
                    VerifyLocal(); var c = _source._core; var root = Root(reference); var target = parent == 0 ? null : Root(parent);
                    VerifyTransform(root); if (target is not null) VerifyTransform(target);
                    var method = Setter("SetParent", new[] { "UnityEngine.Transform", "System.Boolean" }, _profile.SetParentMethod);
                    var receiver = c.Target(root); var targetPointer = target is null ? IntPtr.Zero : c.Target(target);
                    using var parentArgument = A.Ref(targetPointer); using var stays = A.Value(true);
                    c.Native(() =>
                    {
                        c._owner._api.InvokeVoidWithPublicationGuarded(method, receiver, () =>
                        { VerifyLocal(); beforeDispatch(); }, parentArgument, stays); return 0;
                    });
                }
            }
        }
    }
}
