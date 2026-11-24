using System.Runtime.InteropServices;

namespace RizToolbox.Runtime;

/// <summary>Copied identities, not object liveness or a native installation receipt.
/// The caller keeps the borrowed receiver root alive for its entire observation.
/// A node, Coroutine wrapper and this native MonoBehaviour have separate identities.</summary>
internal readonly record struct NativeUnityCoroutineOwner(long ManagedReceiver, long NativeOwner, IntPtr ManagedClass);
internal readonly record struct NativeUnityCoroutineOwnerObservation(long ManagedReceiver, long CachedNativeOwner, IntPtr ManagedClass);

internal sealed partial class NativeRuntime
{
    /// <summary>Reads the official Object.m_CachedPtr field from an already rooted
    /// MonoBehaviour. The pinned Start icall loads the same receiver+0x10 value
    /// before passing RCX to the engine Start. No native owner/node is dereferenced,
    /// no object is created and a null cached pointer never invokes a fallback.</summary>
    internal NativeUnityCoroutineOwner ReadUnityCoroutineOwner(RootHandle receiverRoot, long expectedReceiver)
    {
        var value = ReadUnityCoroutineOwnerCore(receiverRoot, expectedReceiver, allowStableZero: false);
        return new(value.ManagedReceiver, value.CachedNativeOwner, value.ManagedClass);
    }

    /// <summary>Observes the same retained wrapper without interpreting a stable
    /// zero cached pointer as proof of death. The caller must keep its original
    /// owner identity and obtain independent destruction evidence. This method
    /// never dereferences that old native owner or invokes a Unity fallback.</summary>
    internal NativeUnityCoroutineOwnerObservation ObserveUnityCoroutineOwner(RootHandle receiverRoot, long expectedReceiver)
        => ReadUnityCoroutineOwnerCore(receiverRoot, expectedReceiver, allowStableZero: true);

    // Hold observations can be withdrawn by any official export callback. The
    // guarded overload checks the source between the individual native reads;
    // existing two-argument engine callers retain their original contract.
    internal NativeUnityCoroutineOwner ReadUnityCoroutineOwner(RootHandle receiverRoot, long expectedReceiver, Action guard)
    {
        var value = ReadUnityCoroutineOwnerGuarded(receiverRoot, expectedReceiver, allowStableZero: false, guard);
        return new(value.ManagedReceiver, value.CachedNativeOwner, value.ManagedClass);
    }

    internal NativeUnityCoroutineOwnerObservation ObserveUnityCoroutineOwner(RootHandle receiverRoot, long expectedReceiver, Action guard)
        => ReadUnityCoroutineOwnerGuarded(receiverRoot, expectedReceiver, allowStableZero: true, guard);

    private NativeUnityCoroutineOwnerObservation ReadUnityCoroutineOwnerGuarded(RootHandle receiverRoot,
        long expectedReceiver, bool allowStableZero, Action guard)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(receiverRoot); ArgumentNullException.ThrowIfNull(guard);
        guard();
        if (IntPtr.Size != 8 || expectedReceiver == 0)
            throw new UnsupportedSignatureException("Unity coroutine owner identity requires an exact rooted x64 receiver.");
        T O<T>(Func<T> read) => LineRingObserve(guard, read);

        // Canonical class resolution can call several exports on a cache miss;
        // its guarded path checks between those calls.
        var unityObject = LineEndpointClass("UnityEngine.CoreModule.dll", "UnityEngine", "Object", guard);
        var monoBehaviour = LineEndpointClass("UnityEngine.CoreModule.dll", "UnityEngine", "MonoBehaviour", guard);
        var receiver = O(() => receiverRoot.Target);
        if (receiver.ToInt64() != expectedReceiver)
            throw new TargetExpiredException("The retained coroutine receiver changed its managed identity.");
        var receiverClass = O(() => _objectClass(receiver));
        if (!O(() => _assignable(monoBehaviour, receiverClass))
            || !O(() => _assignable(unityObject, monoBehaviour)))
            throw new UnsupportedSignatureException("The source class is not assignable to the required target class.");

        // Field(Object, ...) would walk parent classes. The pinned Start icall
        // reads Object's own slot, so require the exact declaration directly.
        var field = O(() => _findField(unityObject, "m_CachedPtr"));
        var fieldParent = O(() => Api<Ptr1>("il2cpp_field_get_parent"));
        if (field == IntPtr.Zero || O(() => fieldParent(field)) != unityObject
            || (O(() => _fieldFlags(field)) & 0x10) != 0 || O(() => _fieldOffset(field)) != 0x10)
            throw new UnsupportedSignatureException("Unity Object cached-pointer metadata differs from the pinned owner layout.");
        var fieldType = O(() => _fieldType(field));
        var pointerClass = LineEndpointClass("mscorlib.dll", "System", "IntPtr", guard);
        var canonicalType = O(() => _classType(pointerClass));
        // Describe's type-name allocation can release through another export.
        // Check the exact scalar declaration here so every export in this
        // observation has a guard on both sides, including cache misses.
        if (fieldType == IntPtr.Zero || canonicalType == IntPtr.Zero
            || O(() => _typeClass(fieldType)) != pointerClass
            || O(() => _typeClass(canonicalType)) != pointerClass
            || O(() => _byRef(fieldType)) || O(() => _byRef(canonicalType))
            || O(() => _typeCode(fieldType)) != 24 || O(() => _typeCode(canonicalType)) != 24
            || !O(() => _isValueType(pointerClass)) || O(() => _isEnum(pointerClass))
            || O(() => _valueSize(pointerClass, out _)) != 8
            || Utf8(O(() => _classNamespace(pointerClass))) != "System"
            || Utf8(O(() => _className(pointerClass))) != "IntPtr")
            throw new UnsupportedSignatureException("Unity Object cached-pointer field is not a non-byref native pointer.");
        NativeAbi.ValidateValue(typeof(IntPtr), new("System.IntPtr", NativeValueKind.IntPtr, 8, Class: pointerClass));

        IntPtr ReadOwner()
        {
            var current = O(() => receiverRoot.Target);
            if (current != receiver) throw new TargetExpiredException("The retained coroutine receiver changed its managed identity.");
            using var output = new Arg(IntPtr.Size);
            O(() => { _fieldGet(current, field, output.Pointer); return 0; });
            guard(); var value = Marshal.ReadIntPtr(output.Pointer); guard();
            return value;
        }

        var owner = ReadOwner();
        if (owner == IntPtr.Zero && !allowStableZero)
            throw new TargetExpiredException("The retained Unity coroutine receiver has no live cached native owner.");
        if (O(() => receiverRoot.Target) != receiver || O(() => _objectClass(receiver)) != receiverClass
            || ReadOwner() != owner || O(() => receiverRoot.Target) != receiver
            || O(() => _objectClass(receiver)) != receiverClass || O(() => receiverRoot.Target) != receiver)
            throw new TargetExpiredException("The managed receiver or its cached native owner changed during capture.");
        guard();
        return new(expectedReceiver, owner.ToInt64(), receiverClass);
    }

    private NativeUnityCoroutineOwnerObservation ReadUnityCoroutineOwnerCore(RootHandle receiverRoot,
        long expectedReceiver, bool allowStableZero)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(receiverRoot);
        if (IntPtr.Size != 8 || expectedReceiver == 0)
            throw new UnsupportedSignatureException("Unity coroutine owner identity requires an exact rooted x64 receiver.");
        var receiver = receiverRoot.Target;
        if (receiver.ToInt64() != expectedReceiver)
            throw new TargetExpiredException("The retained coroutine receiver changed its managed identity.");
        var unityObject = Class("UnityEngine.CoreModule.dll", "UnityEngine", "Object");
        var monoBehaviour = Class("UnityEngine.CoreModule.dll", "UnityEngine", "MonoBehaviour");
        var receiverClass = ObjectClass(receiver);
        RequireAssignable(monoBehaviour, receiverClass);
        RequireAssignable(unityObject, monoBehaviour);
        var field = Field(unityObject, "m_CachedPtr");
        // Require the declaration on Object itself. A same-named subclass field
        // is not the cached pointer loaded by the native icall wrapper.
        if (Api<Ptr1>("il2cpp_field_get_parent")(field) != unityObject
            || (_fieldFlags(field) & 0x10) != 0 || _fieldOffset(field) != 0x10
            || _byRef(_fieldType(field)))
            throw new UnsupportedSignatureException("Unity Object cached-pointer metadata differs from the pinned owner layout.");
        var layout = Describe(_fieldType(field));
        if (layout.Name != "System.IntPtr" || layout.Kind != NativeValueKind.IntPtr || layout.Size != 8 || layout.ByRef)
            throw new UnsupportedSignatureException("Unity Object cached-pointer field is not a non-byref native pointer.");
        var owner = ReadField<IntPtr>(receiver, field, false);
        if (owner == IntPtr.Zero && !allowStableZero)
            throw new TargetExpiredException("The retained Unity coroutine receiver has no live cached native owner.");
        if (receiverRoot.Target != receiver || ObjectClass(receiver) != receiverClass
            || ReadField<IntPtr>(receiver, field, false) != owner || receiverRoot.Target != receiver
            || ObjectClass(receiver) != receiverClass || receiverRoot.Target != receiver)
            throw new TargetExpiredException("The managed receiver or its cached native owner changed during capture.");
        return new(expectedReceiver, owner.ToInt64(), receiverClass);
    }
}
