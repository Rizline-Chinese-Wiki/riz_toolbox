using System.Runtime.InteropServices;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Runtime;

/// <summary>Copied read-only observations of one independently rooted Unity
/// wrapper. This is neither a root nor a watch/installation capability. The
/// native owner must read the actual instance ID under SEH and bind its own
/// watch to the real source/clip roots; this record grants no authority.</summary>
internal readonly record struct NativeUnityObjectBackingObservation(long ManagedObject, IntPtr ExactClass,
    long CachedNativePointer, int NativeInstanceIdOffset, IntPtr UnityObjectClass, int InstanceIdOffsetFieldOffset);

// Official non-unloading metadata only. This contains no object identity, root,
// backing observation, watch receipt, initialization credential or Clock grant.
internal readonly record struct NativeUnityObjectBackingLayout(IntPtr UnityObjectClass, IntPtr AudioSourceClass,
    IntPtr AudioClipClass, int NativeInstanceIdOffset);

internal sealed partial class NativeRuntime
{
    internal const int MaximumUnityNativeInstanceIdOffset = 0x400;

    /// <summary>Reads only the official Object layout before a first Start has
    /// supplied any independently rooted player/source/clip. No object field,
    /// native backing, managed method, class initializer or extra root is used.
    /// Native must still capture its own identity from the actual completed Start.</summary>
    internal NativeUnityObjectBackingLayout ReadUnityObjectBackingLayout()
    {
        VerifyAccess();
        if (IntPtr.Size != 8)
            throw new UnsupportedSignatureException("Unity backing layout requires the exact x64 metadata contract.");
        var unityObject = Class("UnityEngine.CoreModule.dll", "UnityEngine", "Object");
        var audioSource = Class("UnityEngine.AudioModule.dll", "UnityEngine", "AudioSource");
        var audioClip = Class("UnityEngine.AudioModule.dll", "UnityEngine", "AudioClip");
        var cached = InspectExactDeclaredReadField(unityObject, "m_CachedPtr", "System.IntPtr", false, 0x10);
        var instanceIdOffset = InspectExactDeclaredReadField(unityObject, "OffsetOfInstanceIDInCPlusPlusObject", "System.Int32", true, 0);
        IntPtr staticStorage = IntPtr.Zero;

        void VerifyWindow()
        {
            VerifyAccess();
            if (Class("UnityEngine.CoreModule.dll", "UnityEngine", "Object") != unityObject
                || Class("UnityEngine.AudioModule.dll", "UnityEngine", "AudioSource") != audioSource
                || Class("UnityEngine.AudioModule.dll", "UnityEngine", "AudioClip") != audioClip)
                throw new UnsupportedSignatureException("The official Unity backing classes changed during layout observation.");
            ValidateUnityBackingClasses(unityObject, audioSource, "AudioSource");
            ValidateUnityBackingClasses(unityObject, audioClip, "AudioClip");
            if (InspectExactDeclaredReadField(unityObject, "m_CachedPtr", "System.IntPtr", false, 0x10) != cached
                || InspectExactDeclaredReadField(unityObject, "OffsetOfInstanceIDInCPlusPlusObject", "System.Int32", true, 0) != instanceIdOffset)
                throw new UnsupportedSignatureException("The official Unity backing fields changed during layout observation.");
            ValidateUnityBackingScalar(cached, "IntPtr", NativeValueKind.IntPtr, 24, IntPtr.Size);
            ValidateUnityBackingScalar(instanceIdOffset, "Int32", NativeValueKind.Int32, 8, sizeof(int));
            RequireUnityBackingField(unityObject, audioSource, cached);
            RequireUnityBackingField(unityObject, audioSource, instanceIdOffset);
            RequireUnityBackingField(unityObject, audioClip, cached);
            RequireUnityBackingField(unityObject, audioClip, instanceIdOffset);
        }

        int ReadOffset()
        {
            VerifyWindow();
            // The fixed official leaf only returns Il2CppClass+0xB8. It cannot
            // initialize Object or invoke managed code; static_get_value is unused.
            var storage = ReadGraphExport<Ptr1>("il2cpp_class_get_static_field_data")(unityObject);
            if (staticStorage != IntPtr.Zero && storage != staticStorage)
                throw new TargetExpiredException("Unity Object static storage changed during layout observation.");
            HookTargetValidation.VerifyReadable(storage, sizeof(int));
            var offset = Marshal.ReadInt32(storage);
            staticStorage = storage;
            VerifyWindow();
            if (offset < 0 || offset > MaximumUnityNativeInstanceIdOffset || (offset & (sizeof(int) - 1)) != 0)
                throw new UnsupportedSignatureException("Unity native instance-ID offset is uninitialized, unaligned or outside the bounded layout.");
            return offset;
        }

        var firstOffset = ReadOffset(); var secondOffset = ReadOffset();
        if (firstOffset != secondOffset)
            throw new TargetExpiredException("The Unity instance-ID offset changed during layout observation.");
        VerifyWindow();
        return new(unityObject, audioSource, audioClip, firstOffset);
    }

    /// <summary>Projects the official Object fields of an already independently
    /// rooted, exact AudioSource or AudioClip. Every scalar read is surrounded
    /// by fresh field/class/root validation, and both values are sampled twice.
    /// No game method, class initializer, extra GC root, or native backing
    /// dereference is used. A later consumer must independently revalidate.</summary>
    internal NativeUnityObjectBackingObservation ReadUnityObjectBackingObservation(RootHandle objectRoot,
        long expectedObject, IntPtr expectedExactClass)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(objectRoot);
        if (IntPtr.Size != 8 || expectedObject == 0 || expectedExactClass == IntPtr.Zero)
            throw new UnsupportedSignatureException("Unity backing observation requires an exact rooted x64 wrapper and class.");
        var wrapper = objectRoot.Target;
        if (wrapper.ToInt64() != expectedObject)
            throw new TargetExpiredException("The independently rooted Unity wrapper changed its managed identity.");
        if (_objectClass(wrapper) != expectedExactClass)
            throw new UnsupportedSignatureException("The actual Unity wrapper does not have the expected exact class.");

        var unityObject = Class("UnityEngine.CoreModule.dll", "UnityEngine", "Object");
        var audioSource = Class("UnityEngine.AudioModule.dll", "UnityEngine", "AudioSource");
        var audioClip = Class("UnityEngine.AudioModule.dll", "UnityEngine", "AudioClip");
        var exactName = expectedExactClass == audioSource ? "AudioSource" : expectedExactClass == audioClip ? "AudioClip" : null;
        if (exactName is null)
            throw new UnsupportedSignatureException("Only the official exact AudioSource and AudioClip classes have this backing projection.");

        ValidateUnityBackingClasses(unityObject, expectedExactClass, exactName);
        var cached = InspectExactDeclaredReadField(unityObject, "m_CachedPtr", "System.IntPtr", false, 0x10);
        var instanceIdOffset = InspectExactDeclaredReadField(unityObject, "OffsetOfInstanceIDInCPlusPlusObject", "System.Int32", true, 0);
        IntPtr staticStorage = IntPtr.Zero;

        void VerifyWindow()
        {
            VerifyAccess(); VerifyUnityBackingRoot(objectRoot, wrapper, expectedExactClass);
            ValidateUnityBackingClasses(unityObject, expectedExactClass, exactName);
            var currentCached = InspectExactDeclaredReadField(unityObject, "m_CachedPtr", "System.IntPtr", false, 0x10);
            var currentOffset = InspectExactDeclaredReadField(unityObject, "OffsetOfInstanceIDInCPlusPlusObject", "System.Int32", true, 0);
            if (currentCached != cached || currentOffset != instanceIdOffset)
                throw new UnsupportedSignatureException("Unity backing field declarations changed during observation.");
            ValidateUnityBackingScalar(cached, "IntPtr", NativeValueKind.IntPtr, 24, IntPtr.Size);
            ValidateUnityBackingScalar(instanceIdOffset, "Int32", NativeValueKind.Int32, 8, sizeof(int));
            RequireUnityBackingField(unityObject, expectedExactClass, cached);
            RequireUnityBackingField(unityObject, expectedExactClass, instanceIdOffset);
            VerifyUnityBackingRoot(objectRoot, wrapper, expectedExactClass);
        }

        int ReadOffset()
        {
            VerifyWindow();
            // The fixed official export is the 8-byte metadata leaf
            // `mov rax,[rcx+0xB8]; ret`; it neither initializes Object nor
            // invokes managed code. Generic static_get_value is deliberately
            // excluded here. The official non-unloading static storage is
            // bounded/readability checked before this scalar metadata read.
            var storage = ReadGraphExport<Ptr1>("il2cpp_class_get_static_field_data")(unityObject);
            if (staticStorage != IntPtr.Zero && storage != staticStorage)
                throw new TargetExpiredException("Unity Object static storage changed during backing observation.");
            HookTargetValidation.VerifyReadable(storage, sizeof(int));
            var offset = Marshal.ReadInt32(storage);
            staticStorage = storage;
            VerifyWindow();
            if (offset < 0 || offset > MaximumUnityNativeInstanceIdOffset || (offset & (sizeof(int) - 1)) != 0)
                throw new UnsupportedSignatureException("Unity native instance-ID offset is uninitialized, unaligned or outside the bounded observation layout.");
            return offset;
        }

        IntPtr ReadCached()
        {
            VerifyWindow();
            var pointer = ReadField<IntPtr>(wrapper, cached.Field, false);
            VerifyWindow();
            if (pointer == IntPtr.Zero)
                throw new TargetExpiredException("The independently rooted Unity wrapper has no cached native backing.");
            return pointer;
        }

        // Read initialized static storage first, so a -1/unreasonable offset
        // cannot lead even to an attempted native-backing observation.
        var firstOffset = ReadOffset(); var firstCached = ReadCached();
        var secondOffset = ReadOffset(); var secondCached = ReadCached();
        if (firstOffset != secondOffset || firstCached != secondCached)
            throw new TargetExpiredException("The Unity cached pointer or instance-ID offset changed during observation.");
        VerifyWindow();
        return new(expectedObject, expectedExactClass, firstCached.ToInt64(), firstOffset, unityObject, instanceIdOffset.Offset);
    }

    private void VerifyUnityBackingRoot(RootHandle root, IntPtr wrapper, IntPtr exactClass)
    {
        if (root.Target != wrapper || _objectClass(wrapper) != exactClass || root.Target != wrapper)
            throw new TargetExpiredException("The independently rooted Unity wrapper or exact class changed during observation.");
    }

    private void ValidateUnityBackingClasses(IntPtr unityObject, IntPtr exactClass, string exactName)
    {
        void ReferenceClass(IntPtr cls, string imageName, string name)
        {
            var type = _classType(cls);
            var image = ReadGraphExport<Ptr1>("il2cpp_class_get_image")(cls);
            if (type == IntPtr.Zero || image == IntPtr.Zero || _typeClass(type) != cls || _byRef(type)
                || _typeCode(type) != 18 || _isValueType(cls) || _isEnum(cls)
                || Utf8(_classNamespace(cls)) != "UnityEngine" || Utf8(_className(cls)) != name
                || Utf8(_imageName(image)) != imageName || MetadataTypeName(type) != "UnityEngine." + name)
                throw new UnsupportedSignatureException("Unity backing class no longer has its exact official reference metadata.");
        }

        ReferenceClass(unityObject, "UnityEngine.CoreModule.dll", "Object");
        ReferenceClass(exactClass, "UnityEngine.AudioModule.dll", exactName);
        if (_classParent(unityObject) != Class("mscorlib.dll", "System", "Object") || !_assignable(unityObject, exactClass))
            throw new UnsupportedSignatureException("Unity backing class ancestry differs from the official Object declaration.");
        // Bound the actual metadata chain rather than treating an assignable
        // bit or a caller-supplied exact-class pointer as sufficient evidence.
        var current = exactClass;
        for (var depth = 0; current != IntPtr.Zero && depth < 32; ++depth, current = _classParent(current))
            if (current == unityObject) return;
        throw new UnsupportedSignatureException("Unity backing class has no bounded official Object ancestry.");
    }

    private void ValidateUnityBackingScalar(NativeExactReadField field, string name, NativeValueKind kind, int code, int size)
    {
        var cls = Class("mscorlib.dll", "System", name); var type = _classType(cls);
        var image = ReadGraphExport<Ptr1>("il2cpp_class_get_image")(cls);
        if (field.FieldClass != cls || field.Size != size || field.Type == IntPtr.Zero || type == IntPtr.Zero
            || _typeClass(field.Type) != cls || _typeClass(type) != cls || _byRef(field.Type) || _byRef(type)
            || _typeCode(field.Type) != code || _typeCode(type) != code || !_isValueType(cls) || _isEnum(cls)
            || _valueSize(cls, out _) != size || MetadataTypeName(field.Type) != "System." + name
            || MetadataTypeName(type) != "System." + name || image == IntPtr.Zero || Utf8(_imageName(image)) != "mscorlib.dll"
            || Utf8(_classNamespace(cls)) != "System" || Utf8(_className(cls)) != name
            || Describe(field.Type) is not { ByRef: false } layout || layout.Kind != kind || layout.Size != size || layout.Class != cls)
            throw new UnsupportedSignatureException("Unity backing scalar differs from its official exact primitive class and storage.");
    }

    private void RequireUnityBackingField(IntPtr unityObject, IntPtr exactClass, NativeExactReadField field)
    {
        var current = exactClass;
        for (var depth = 0; current != IntPtr.Zero && depth < 32; ++depth, current = _classParent(current))
        {
            var resolved = _findField(current, field.Name);
            if (resolved != IntPtr.Zero && resolved != field.Field)
                throw new UnsupportedSignatureException("The exact Unity wrapper shadows its official Object backing field.");
            if (current == unityObject)
            {
                if (resolved != field.Field)
                    throw new UnsupportedSignatureException("The official Unity Object backing field is no longer declared.");
                return;
            }
        }
        throw new UnsupportedSignatureException("The exact Unity backing field has no bounded declaring-class path.");
    }
}
