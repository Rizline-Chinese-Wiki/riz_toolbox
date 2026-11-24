using System.Runtime.InteropServices;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Runtime;

internal sealed partial class NativeRuntime
{
    // Class() can make several native calls on a cache miss. Resolve each
    // export separately so source withdrawal is observed between those calls.
    private IntPtr LineEndpointClass(string imageName, string ns, string name, Action guard)
    {
        T O<T>(Func<T> read) => LineRingObserve(guard, read);
        if (_classes.TryGetValue((imageName, ns, name), out var cached)) { guard(); return cached; }
        using var size = Arg.Value(IntPtr.Zero);
        var domain = O(() => _domain());
        var assemblies = O(() => _assemblies(domain, size.Pointer));
        guard(); var count = Marshal.ReadIntPtr(size.Pointer).ToInt64(); guard();
        if (count is < 1 or > 4096)
            throw new UnsupportedSignatureException("The line endpoint assembly list is not ready.");
        for (var i = 0; i < count; i++)
        {
            guard(); var assembly = Marshal.ReadIntPtr(assemblies, i * IntPtr.Size); guard();
            var image = O(() => _image(assembly));
            if (Utf8(O(() => _imageName(image))) != imageName) continue;
            var cls = O(() => _findClass(image, ns, name));
            if (cls == IntPtr.Zero) continue;
            _classes[(imageName, ns, name)] = cls;
            guard(); return cls;
        }
        throw new UnsupportedSignatureException("The canonical line endpoint class is absent: " + ns + "." + name);
    }

    private IntPtr LineEndpointClassType(IntPtr cls, string imageName, string ns, string name,
        int code, bool valueType, Action guard)
    {
        T O<T>(Func<T> read) => LineRingObserve(guard, read);
        if (cls == IntPtr.Zero || cls != LineEndpointClass(imageName, ns, name, guard))
            throw new UnsupportedSignatureException("The line endpoint class is not canonical: " + ns + "." + name);
        var getImage = O(() => ReadGraphExport<Ptr1>("il2cpp_class_get_image"));
        var image = O(() => getImage(cls));
        var type = O(() => _classType(cls));
        if (image == IntPtr.Zero || type == IntPtr.Zero || O(() => _typeClass(type)) != cls
            || O(() => _byRef(type)) || O(() => _typeCode(type)) != code
            || O(() => _isValueType(cls)) != valueType || O(() => _isEnum(cls))
            || LineRingTypeName(type, guard) != ns + "." + name
            || Utf8(O(() => _classNamespace(cls))) != ns || Utf8(O(() => _className(cls))) != name
            || Utf8(O(() => _imageName(image))) != imageName)
            throw new UnsupportedSignatureException("The line endpoint class declaration changed: " + ns + "." + name);
        return type;
    }

    internal NativeMetadataClass InspectLineEndpointTransformClass(IntPtr cls, Action guard)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(guard);
        var type = LineEndpointClassType(cls, "UnityEngine.CoreModule.dll", "UnityEngine", "Transform", 18, false, guard);
        var layout = LineRingLayout(type, guard);
        if (layout.Name != "UnityEngine.Transform" || layout.Kind != NativeValueKind.Reference
            || layout.Class != cls || layout.ByRef || layout.Size != IntPtr.Size)
            throw new UnsupportedSignatureException("The line endpoint Transform ABI changed.");
        guard(); return new(cls, "UnityEngine.Transform");
    }

    private void VerifyLineEndpointSingle(IntPtr type, Action guard)
    {
        var cls = LineEndpointClass("mscorlib.dll", "System", "Single", guard);
        _ = LineEndpointClassType(cls, "mscorlib.dll", "System", "Single", 12, true, guard);
        var fresh = LineRingLayout(type, guard);
        var cached = Describe(type, guard);
        if (fresh.Class != cls || fresh.Name != "System.Single" || fresh.Kind != NativeValueKind.Single
            || fresh.Size != sizeof(float) || fresh.ByRef || cached.Class != cls
            || cached.Name != fresh.Name || cached.Kind != fresh.Kind || cached.Size != fresh.Size || cached.ByRef)
            throw new UnsupportedSignatureException("The line endpoint Single ABI changed.");
        NativeAbi.ValidateValue(typeof(float), cached);
        guard();
    }

    private void VerifyLineEndpointVector(IntPtr type, Action guard)
    {
        T O<T>(Func<T> read) => LineRingObserve(guard, read);
        var cls = LineEndpointClass("UnityEngine.CoreModule.dll", "UnityEngine", "Vector3", guard);
        _ = LineEndpointClassType(cls, "UnityEngine.CoreModule.dll", "UnityEngine", "Vector3", 17, true, guard);
        _ = LineEndpointClass("mscorlib.dll", "System", "Single", guard);
        var fresh = LineRingLayout(type, guard);
        var cached = Describe(type, guard);
        if (fresh.Class != cls || fresh.Name != "UnityEngine.Vector3" || fresh.Kind != NativeValueKind.Struct
            || fresh.Size != 3 * sizeof(float) || fresh.ByRef || cached.Class != cls
            || cached.Name != fresh.Name || cached.Kind != fresh.Kind || cached.Size != fresh.Size || cached.ByRef
            || cached.Fields is null || fresh.Fields is null
            || !cached.Fields.OrderBy(field => field.Offset).SequenceEqual(fresh.Fields))
            throw new UnsupportedSignatureException("The line endpoint Vector3 ABI changed.");
        NativeAbi.ValidateNativeStruct(cached);
        // Describe can return a cached Vector3 without visiting its component
        // type cache. Check each actual component as well as the fresh fields.
        using (var iterator = Arg.Value(IntPtr.Zero))
        {
            var terminated = false;
            for (var i = 0; i < 256; i++)
            {
                var field = O(() => _fields(cls, iterator.Pointer));
                if (field == IntPtr.Zero) { terminated = true; break; }
                if ((O(() => _fieldFlags(field)) & 0x10u) != 0) continue;
                VerifyLineEndpointSingle(O(() => _fieldType(field)), guard);
            }
            if (!terminated) throw new UnsupportedSignatureException("The line endpoint Vector3 fields exceed their bound.");
        }
        guard();
    }

    private void VerifyLineEndpointVoid(IntPtr type, Action guard)
    {
        T O<T>(Func<T> read) => LineRingObserve(guard, read);
        var cls = LineEndpointClass("mscorlib.dll", "System", "Void", guard);
        var canonical = LineEndpointClassType(cls, "mscorlib.dll", "System", "Void", 1, true, guard);
        if (type == IntPtr.Zero || O(() => _typeClass(type)) != cls || O(() => _byRef(type))
            || O(() => _typeCode(type)) != 1 || O(() => _typeClass(canonical)) != cls
            || LineRingTypeName(type, guard) != "System.Void")
            throw new UnsupportedSignatureException("The line endpoint Void result changed.");
        var cached = Describe(type, guard);
        if (cached.Kind != NativeValueKind.Void || cached.ByRef || cached.Size != 0 || cached.Name != "System.Void")
            throw new UnsupportedSignatureException("The cached line endpoint Void ABI changed.");
        guard();
    }

    internal NativeMetadataMethod InspectLineEndpointPositionMethod(IntPtr cls, string name, IntPtr module, Action guard)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(guard);
        if (name is not ("get_localPosition" or "set_localPosition"))
            throw new UnsupportedSignatureException("Only exact line endpoint local-position methods are admitted.");
        _ = InspectLineEndpointTransformClass(cls, guard);
        _ = LineEndpointClass("UnityEngine.CoreModule.dll", "UnityEngine", "Vector3", guard);
        _ = LineEndpointClass("mscorlib.dll", "System", "Single", guard);
        if (name == "get_localPosition")
        {
            var getter = InspectLineRingSourceGetter(cls, name, module, guard);
            VerifyLineEndpointVector(LineRingObserve(guard, () => _returnType(getter.MethodInfo)), guard);
            guard(); return getter;
        }
        T O<T>(Func<T> read) => LineRingObserve(guard, read);
        var found = IntPtr.Zero; var terminated = false;
        using (var iterator = Arg.Value(IntPtr.Zero))
            for (var i = 0; i < 4096; i++)
            {
                var candidate = O(() => _methods(cls, iterator.Pointer));
                if (candidate == IntPtr.Zero) { terminated = true; break; }
                if (Utf8(O(() => _methodName(candidate))) != name
                    || O(() => _parameterCount(candidate)) != 1) continue;
                uint ignored = 0;
                if ((O(() => _methodFlags(candidate, out ignored)) & 0x10u) != 0) continue;
                var parameter = O(() => _parameterType(candidate, 0));
                if (LineRingTypeName(parameter, guard) != "UnityEngine.Vector3") continue;
                if (found != IntPtr.Zero)
                    throw new UnsupportedSignatureException("The exact line endpoint setter is ambiguous.");
                found = candidate;
            }
        if (!terminated || found == IntPtr.Zero || O(() => _methodClass(found)) != cls)
            throw new UnsupportedSignatureException("The exact line endpoint setter is absent or foreign.");
        uint implementation = 0; var flags = O(() => _methodFlags(found, out implementation));
        var generic = O(() => _isGenericMethod(found)); var inflated = O(() => _isInflatedMethod(found));
        if ((flags & (0x10u | 0x400u)) != 0 || generic || inflated)
            throw new UnsupportedSignatureException("The line endpoint setter must be an exact instance wrapper.");
        var parameterType = O(() => _parameterType(found, 0));
        VerifyLineEndpointVector(parameterType, guard);
        var resultType = O(() => _returnType(found));
        VerifyLineEndpointVoid(resultType, guard);
        NativeMetadataEntry? entry = null;
        var status = "Fresh exact local-position setter signature; no pinned module supplied.";
        if (module != IntPtr.Zero)
        {
            guard(); HookTargetValidation.VerifyReadable(found, (nuint)Marshal.SizeOf<HookTargetValidation.MethodInfo2022>()); guard();
            var record = Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(found); guard();
            HookTargetValidation.ValidateMetadata(record, O(() => _methodName(found)), cls, resultType, flags, 1, record.MethodPointer);
            guard();
            if (record.ImplementationFlags != (ushort)implementation)
                throw new UnsupportedSignatureException("The line endpoint setter implementation flags changed.");
            HookTargetValidation.VerifyExecutable(record.MethodPointer, module); guard();
            entry = new(record.MethodPointer, record.MethodPointer.ToInt64() - module.ToInt64(), record.Slot);
            status = "Verified exact setter MethodInfo2022 and executable module entry.";
        }
        guard(); return new(found, new(cls, "UnityEngine.Transform"), name, "System.Void",
            new[] { "UnityEngine.Vector3" }, flags, implementation, generic, inflated, entry, status);
    }

    // The two-argument wrapper is deliberately selected by full signature.
    // Every metadata export is bracketed by the retained source lifetime guard.
    internal NativeMetadataMethod InspectLineEndpointParentMethod(IntPtr cls, IntPtr module, Action guard)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(guard);
        T O<T>(Func<T> read) => LineRingObserve(guard, read);
        _ = InspectLineEndpointTransformClass(cls, guard);
        _ = LineRingLayout(O(() => _classType(cls)), guard);
        var found = IntPtr.Zero; var terminated = false;
        using (var iterator = Arg.Value(IntPtr.Zero))
            for (var i = 0; i < 4096; ++i)
            {
                var candidate = O(() => _methods(cls, iterator.Pointer));
                if (candidate == IntPtr.Zero) { terminated = true; break; }
                if (Utf8(O(() => _methodName(candidate))) != "SetParent"
                    || O(() => _parameterCount(candidate)) != 2) continue;
                if (found != IntPtr.Zero)
                    throw new UnsupportedSignatureException("The exact two-argument SetParent is ambiguous.");
                found = candidate;
            }
        if (!terminated || found == IntPtr.Zero || O(() => _methodClass(found)) != cls)
            throw new UnsupportedSignatureException("The exact two-argument SetParent is absent or foreign.");
        var transform = LineRingLayout(O(() => _parameterType(found, 0)), guard);
        var booleanClass = LineEndpointClass("mscorlib.dll", "System", "Boolean", guard);
        _ = LineEndpointClassType(booleanClass, "mscorlib.dll", "System", "Boolean", 2, true, guard);
        var booleanType = O(() => _parameterType(found, 1));
        var boolean = LineRingLayout(booleanType, guard);
        var cachedBoolean = Describe(booleanType, guard);
        if (transform.Name != "UnityEngine.Transform" || transform.Kind != NativeValueKind.Reference
            || transform.Class != cls || boolean.Name != "System.Boolean"
            || boolean.Kind != NativeValueKind.Boolean || boolean.Size != 1 || boolean.ByRef
            || boolean.Class != booleanClass || cachedBoolean.Name != boolean.Name
            || cachedBoolean.Kind != boolean.Kind || cachedBoolean.Size != boolean.Size
            || cachedBoolean.ByRef || cachedBoolean.Class != booleanClass)
            throw new UnsupportedSignatureException("SetParent has another canonical Transform/Boolean ABI.");
        NativeAbi.ValidateValue(typeof(bool), cachedBoolean);
        var resultType = O(() => _returnType(found));
        VerifyLineEndpointVoid(resultType, guard);
        uint implementation = 0; var flags = O(() => _methodFlags(found, out implementation));
        if ((flags & (0x10u | 0x400u)) != 0 || O(() => _isGenericMethod(found))
            || O(() => _isInflatedMethod(found)))
            throw new UnsupportedSignatureException("SetParent must be an exact non-generic instance wrapper.");
        NativeMetadataEntry? entry = null;
        var status = "Fresh exact SetParent signature; no pinned module supplied.";
        if (module != IntPtr.Zero)
        {
            guard(); HookTargetValidation.VerifyReadable(found, (nuint)Marshal.SizeOf<HookTargetValidation.MethodInfo2022>());
            var record = Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(found);
            HookTargetValidation.ValidateMetadata(record, O(() => _methodName(found)), cls, resultType, flags, 2, record.MethodPointer);
            if (record.ImplementationFlags != (ushort)implementation)
                throw new UnsupportedSignatureException("SetParent implementation flags changed.");
            guard(); HookTargetValidation.VerifyExecutable(record.MethodPointer, module); guard();
            entry = new(record.MethodPointer, record.MethodPointer.ToInt64() - module.ToInt64(), record.Slot);
            status = "Verified exact SetParent MethodInfo2022 and executable module entry.";
        }
        return new(found, new(cls, "UnityEngine.Transform"), "SetParent", "System.Void",
            new[] { "UnityEngine.Transform", "System.Boolean" }, flags, implementation,
            false, false, entry, status);
    }
}
