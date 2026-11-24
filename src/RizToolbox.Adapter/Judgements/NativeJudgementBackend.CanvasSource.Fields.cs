using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private sealed partial class NativeSeekNoteFlagsAccess
    {
        private sealed partial class CanvasSource
        {
            // A publication callback confirms ownership before its caller's
            // post-call lifetime check. If allocating the root withdraws this
            // source, keep the handle recorded and return acceptance; throwing
            // here would falsely retain an unconfirmed runtime publication.
            private NativeRuntime.RootHandle? AdoptPublication(IntPtr pointer)
            {
                VerifyOwnerThread();
                // A field/List getter can withdraw admission before publishing
                // its token. Acknowledge the unconsumed GC reference locally,
                // then reject outside publication; never allocate after that.
                try { VerifyLocal(); }
                catch (Exception error)
                {
                    // A withdrawn child cannot publish a new root, but its
                    // local cancellation must not poison the sibling's source.
                    if (!SeekCanvasBasisCallerWithdrawal.Is(error)) _publicationAdmissionFailure ??= error;
                    return null;
                }
                if (pointer == IntPtr.Zero) throw new InvalidDataException("A required Canvas source reference is absent.");
                if (_core._retained.TryGetValue(pointer.ToInt64(), out var existing)) return existing;
                if (_core._roots.Count >= _core._rootLimit)
                {
                    // No native root was requested for this GC-owned token.
                    // Acknowledge it before the enclosing N/OwnLifetime post
                    // check rejects the source; throwing inside the publisher
                    // would leave an inaccessible unconfirmed publication.
                    _publicationAdmissionFailure ??= new NotSupportedException("Complete Canvas source roots exceed their bounded budget.");
                    return null;
                }
                var size = checked(_core._roots.Count + 1);
                _core._roots.EnsureCapacity(size); _core._retained.EnsureCapacity(size); _core._addresses.EnsureCapacity(size);
                var root = _core._owner._api.Root(pointer);
                _core._roots.Add(root); _core._retained.Add(pointer.ToInt64(), root); _core._addresses.Add(root, pointer.ToInt64());
                return root;
            }
            private IntPtr Target(NativeRuntime.RootHandle root) => N(() => _core.Target(root));
            private NativeExactReadField Field(IntPtr cls, string name, string type, int offset, bool isStatic = false)
            {
                var key = (cls, name, type, isStatic, offset);
                var value = N(() => _core._owner._api.InspectExactDeclaredReadField(cls, name, type, isStatic, offset));
                if (isStatic && (name is "downVector" or "rightVector") && (value.Flags & 0x20u) == 0)
                    throw new UnsupportedSignatureException("Canvas direction observations require the exact readonly static Vector3 fields.");
                if (_fields.TryGetValue(key, out var expected))
                { if (expected != value) throw Changed("The saved/current Canvas field descriptor changed: " + name); }
                else _fields.Add(key, value);
                return value;
            }
            private T Scalar<T>(NativeRuntime.RootHandle receiver, IntPtr cls, string name, string type, int offset) where T : struct
            {
                var field = Field(cls, name, type, offset);
                return N(() => _core._owner._api.ReadExactField<T>(receiver, field));
            }
            private NativeRuntime.RootHandle Reference(NativeRuntime.RootHandle? receiver, IntPtr cls,
                string name, string type, int offset, bool isStatic = false)
            {
                var field = Field(cls, name, type, offset, isStatic);
                NativeRuntime.RootHandle? result = null; var published = false;
                void Publish(IntPtr pointer)
                {
                    if (published) throw Changed("A Canvas source reference was published twice.");
                    published = true;
                    if (pointer != IntPtr.Zero) result = AdoptPublication(pointer);
                }
                N(() =>
                {
                    if (isStatic) _core._owner._api.PublishExactStaticReferenceField(field, Publish);
                    else _core._owner._api.PublishExactReferenceField(receiver!, field, Publish);
                    return 0;
                });
                if (!published || result is null) throw new InvalidDataException("A required Canvas source reference is absent: " + name);
                if (N(() => _core._owner._api.ObjectClass(Target(result))) != field.FieldClass)
                    throw new UnsupportedSignatureException("A Canvas source reference has another exact field class: " + name);
                return result;
            }
            private static bool SameMethod(NativeMetadataMethod a, NativeMetadataMethod b)
                => a.MethodInfo == b.MethodInfo && a.DeclaringClass == b.DeclaringClass && a.Name == b.Name
                    && a.ReturnType == b.ReturnType && a.ParameterTypes.SequenceEqual(b.ParameterTypes) && a.Flags == b.Flags
                    && a.ImplementationFlags == b.ImplementationFlags && a.GenericMethod == b.GenericMethod
                    && a.InflatedMethod == b.InflatedMethod && a.Entry == b.Entry;
            private NativeMetadataMethod InspectMethod(IntPtr cls, string name, string[] parameters, string returns)
            {
                var method = N(() => _core._owner._api.InspectCanvasSourceMethod(cls, name, parameters, returns, IntPtr.Zero));
                var key = (cls, name);
                if (_methods.TryGetValue(key, out var previous))
                { if (!SameMethod(previous, method)) throw Changed("The exact Canvas source method changed: " + name); }
                else _methods.Add(key, method);
                return method;
            }
            private int Count(NativeRuntime.RootHandle list, IntPtr cls)
            {
                var method = InspectMethod(cls, "get_Count", Array.Empty<string>(), "System.Int32");
                return N(() => _core._owner._api.InvokeValueWithPublicationGuarded<int>(method.MethodInfo, Target(list), VerifyLocal));
            }
            private long Item(NativeRuntime.RootHandle list, IntPtr cls, int slot)
            {
                var method = InspectMethod(cls, "get_Item", new[] { "System.Int32" }, "InLevel.KeyPoint");
                NativeRuntime.RootHandle? result = null; var published = false;
                N(() =>
                {
                    using var argument = A.Value(slot);
                    _core._owner._api.InvokeReferenceWithPublication(method.MethodInfo, Target(list), pointer =>
                    {
                        if (published) throw Changed("An official Canvas List item was published twice.");
                        published = true;
                        if (pointer != IntPtr.Zero) result = AdoptPublication(pointer);
                    }, argument); return 0;
                });
                if (!published || result is null) throw new InvalidDataException("An active Canvas List item is absent.");
                if (N(() => _core._owner._api.ObjectClass(Target(result))) != _classes[5])
                    throw new UnsupportedSignatureException("The official Canvas List item has another exact KeyPoint class.");
                return Target(result).ToInt64();
            }
            private int ArrayCount(NativeRuntime.RootHandle array, string methodName, int maximum)
            {
                var cls = N(() => _core._owner._api.ObjectClass(Target(array)));
                var method = N(() => _core._owner._api.Method(cls, methodName, 0));
                var count = N(() => _core._owner._api.InvokeValueWithPublication<int>(method, Target(array)));
                if (count < 0 || count > maximum) throw new InvalidDataException("The complete Canvas source vector exceeds its bound.");
                return count;
            }
            private long[] ArrayReferences(NativeRuntime.RootHandle array, IntPtr element, int maximum)
            {
                var cls = N(() => _core._owner._api.ObjectClass(Target(array)));
                var observed = N(() => _core._owner._api.ReadRootedCanvasSourceArray(array, cls, element, maximum, (_, pointer) =>
                {
                    if (pointer != IntPtr.Zero) AdoptPublication(pointer);
                }, VerifyLocal));
                if (observed.Array != Target(array) || observed.Class != cls || observed.ElementClass != element || observed.Length != observed.Items.Count)
                    throw Changed("The complete Canvas source vector lost its actual identity.");
                var items = new long[observed.Length];
                for (var i = 0; i < items.Length; ++i)
                {
                    var pointer = observed.Items[i];
                    if (pointer != IntPtr.Zero && N(() => _core._owner._api.ObjectClass(Target(_core._retained[pointer.ToInt64()]))) != element)
                        throw new UnsupportedSignatureException("A Canvas source vector slot has another exact element class.");
                    items[i] = pointer.ToInt64();
                }
                return items;
            }
        }
    }
}
