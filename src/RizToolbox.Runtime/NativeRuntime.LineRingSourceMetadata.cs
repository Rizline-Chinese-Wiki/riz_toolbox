using System.Runtime.InteropServices;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Runtime;

internal sealed partial class NativeRuntime
{
    private T LineRingObserve<T>(Action guard, Func<T> read)
    { VerifyAccess(); guard(); var result = read(); guard(); return result; }

    private string LineRingTypeName(IntPtr type, Action guard)
    {
        if (type == IntPtr.Zero) throw new UnsupportedSignatureException("Line/ring type metadata is absent.");
        guard(); var text = _typeName(type);
        try
        {
            guard(); var name = Utf8(text);
            if (string.IsNullOrWhiteSpace(name)) throw new UnsupportedSignatureException("Line/ring type name is absent.");
            return name;
        }
        finally { if (text != IntPtr.Zero) _free(text); } // Acquired text must be freed even after caller withdrawal.
    }

    private NativeTypeLayout LineRingLayout(IntPtr type, Action guard)
    {
        T O<T>(Func<T> read) => LineRingObserve(guard, read);
        var name = LineRingTypeName(type, guard); var cls = O(() => _typeClass(type));
        if (cls == IntPtr.Zero || O(() => _byRef(type))) throw new UnsupportedSignatureException("Line/ring requires exact non-byref storage.");
        var canonical = O(() => _classType(cls)); var code = O(() => _typeCode(type));
        if (O(() => _typeClass(canonical)) != cls || O(() => _byRef(canonical))
            || O(() => _typeCode(canonical)) != code || LineRingTypeName(canonical, guard) != name)
            throw new UnsupportedSignatureException("Line/ring native type/class identity changed.");
        var isValue = O(() => _isValueType(cls));
        if (!isValue)
        {
            if (code is not (18 or 29) || O(() => _isEnum(cls)))
                throw new UnsupportedSignatureException("Line/ring source reference has another native kind.");
            VerifyLineRingReferenceClass(name, cls, code, guard);
            return new(name, NativeValueKind.Reference, IntPtr.Size, Class: cls);
        }
        if (O(() => _isEnum(cls))) throw new UnsupportedSignatureException("Line/ring source scalars cannot substitute enums.");
        var scalar = NativeAbi.Scalar(name);
        if (scalar is not null)
        {
            var expectedCode = name switch { "System.Boolean" => 2, "System.Int32" => 8, "System.Single" => 12, _ => -1 };
            if (expectedCode < 0 || code != expectedCode || O(() => _valueSize(cls, out _)) != scalar.Size)
                throw new UnsupportedSignatureException("The exact line/ring scalar ABI changed.");
            var expected = O(() => Class("mscorlib.dll", "System", name[7..]));
            if (expected != cls) throw new UnsupportedSignatureException("Line/ring scalar class is not canonical.");
            return scalar with { Class = cls };
        }
        var count = name == "UnityEngine.Vector3" ? 3 : name == "UnityEngine.Quaternion" ? 4 : 0;
        if (count == 0 || code != 17 || O(() => _valueSize(cls, out _)) != count * sizeof(float)
            || O(() => Class("UnityEngine.CoreModule.dll", "UnityEngine", count == 3 ? "Vector3" : "Quaternion")) != cls)
            throw new UnsupportedSignatureException("Line/ring getter requires exact Vector3 or Quaternion storage.");
        var parent = O(() => ReadGraphExport<Ptr1>("il2cpp_field_get_parent"));
        var fields = new List<NativeFieldLayout>(); var terminated = false;
        using (var iterator = Arg.Value(IntPtr.Zero))
            for (var i = 0; i < 256; ++i)
            {
                var field = O(() => _fields(cls, iterator.Pointer));
                if (field == IntPtr.Zero) { terminated = true; break; }
                if (O(() => parent(field)) != cls) throw new UnsupportedSignatureException("Foreign line/ring struct field declaration.");
                var flags = O(() => _fieldFlags(field)); if ((flags & 0x10u) != 0) continue;
                var fieldName = Utf8(O(() => _fieldName(field)));
                var slot = fieldName switch { "x" => 0, "y" => 1, "z" => 2, "w" when count == 4 => 3, _ => -1 };
                if (slot < 0 || (flags & (0x20u | 0x40u)) != 0 || fields.Any(item => item.Name == fieldName)
                    || O(() => _findField(cls, fieldName)) != field || O(() => _fieldOffset(field)) != 2 * IntPtr.Size + slot * sizeof(float))
                    throw new UnsupportedSignatureException("Line/ring struct component layout differs.");
                var component = LineRingLayout(O(() => _fieldType(field)), guard);
                if (component.Name != "System.Single" || component.Size != 4)
                    throw new UnsupportedSignatureException("Line/ring struct component is not exact Single.");
                fields.Add(new(fieldName, component.Name, slot * sizeof(float)));
            }
        if (!terminated || fields.Count != count) throw new UnsupportedSignatureException("Incomplete line/ring struct declaration.");
        var result = new NativeTypeLayout(name, NativeValueKind.Struct, count * sizeof(float), Class: cls,
            Fields: Array.AsReadOnly(fields.OrderBy(field => field.Offset).ToArray()));
        NativeAbi.ValidateNativeStruct(result); return result;
    }

    private void VerifyLineRingReferenceClass(string name, IntPtr cls, int code, Action guard)
    {
        T O<T>(Func<T> read) => LineRingObserve(guard, read);
        if (name == "UnityEngine.Vector3[]")
        {
            // Only LineControl.linePositions admits this value-element array.
            // Reference-array validation assumes pointer-sized reference slots
            // and therefore cannot validate the twelve-byte Vector3 storage.
            var element = LineEndpointClass("UnityEngine.CoreModule.dll", "UnityEngine", "Vector3", guard);
            var elementType = LineEndpointClassType(element, "UnityEngine.CoreModule.dll",
                "UnityEngine", "Vector3", 17, true, guard);
            VerifyLineEndpointVector(elementType, guard);
            var arrayClass = O(() => ReadGraphExport<ArrayClassGet>("il2cpp_array_class_get"));
            var rank = O(() => ReadGraphExport<Flags>("il2cpp_class_get_rank"));
            var elementClass = O(() => ReadGraphExport<Ptr1>("il2cpp_class_get_element_class"));
            var stride = O(() => ReadGraphExport<ArrayElementSize>("il2cpp_array_element_size"));
            if (code != 29 || O(() => arrayClass(element, 1)) != cls
                || O(() => rank(cls)) != 1 || O(() => elementClass(cls)) != element
                || O(() => stride(cls)) != 12 || O(() => _isValueType(cls)) || O(() => _isEnum(cls)))
                throw new UnsupportedSignatureException("LineControl.linePositions is not the canonical Vector3 array class.");
            return;
        }
        if (name is "InLevel.LineInformation[]" or "InLevel.LinesGroupInformation[]")
        {
            var elementName = name[8..^2];
            var element = O(() => Class("Assembly-CSharp.dll", "InLevel", elementName));
            // Resolve and call separately: an export lookup callback can revoke
            // admission before the official canonical array-class lookup runs.
            var arrayClass = O(() => ReadGraphExport<ArrayClassGet>("il2cpp_array_class_get"));
            if (code != 29 || O(() => arrayClass(element, 1)) != cls)
                throw new UnsupportedSignatureException("Line/ring array declaration is not the canonical element array class.");
            VerifyReferenceArrayClass(cls, element, guard);
            return;
        }
        var canonical = name switch
        {
            "InLevel.LevelInformation" or "InLevel.LineControl" or "InLevel.CanvasControl"
                or "InLevel.UIControl" or "InLevel.CameraControl"
                or "InLevel.LineInformation" or "InLevel.LinePoint" or "InLevel.LinesGroupInformation"
                => O(() => Class("Assembly-CSharp.dll", "InLevel", name[8..])),
            "UnityEngine.Transform" => O(() => Class("UnityEngine.CoreModule.dll", "UnityEngine", "Transform")),
            _ => throw new UnsupportedSignatureException("The reference is outside the exact line/ring source contract.")
        };
        if (code != 18 || canonical == IntPtr.Zero || canonical != cls)
            throw new UnsupportedSignatureException("Line/ring reference declaration is not its exact canonical class.");
    }

    private static (string Owner, string Type, int Offset) LineRingFieldSpecification(string owner, string name)
        => (owner, name) switch
        {
            ("InLevel.LevelInformation", "lineControl") => (owner, "InLevel.LineControl", 0x38),
            ("InLevel.LevelInformation", "uIControl") => (owner, "InLevel.UIControl", 0x68),
            ("InLevel.LevelInformation", "cameraControl") => (owner, "InLevel.CameraControl", 0x78),
            ("InLevel.LevelInformation", "sceneWidth") => (owner, "System.Single", 0xc4),
            ("InLevel.UIControl", "gameSceneCanvas") => (owner, "UnityEngine.Transform", 0x20),
            ("InLevel.CameraControl", "levelInformation") => (owner, "InLevel.LevelInformation", 0x20),
            ("InLevel.CameraControl", "xPosition") => (owner, "System.Single", 0x30),
            ("InLevel.CameraControl", "scale") => (owner, "System.Single", 0x34),
            ("InLevel.LineControl", "levelInformation") => (owner, "InLevel.LevelInformation", 0x20),
            ("InLevel.LineControl", "dynamicLines") => (owner, "InLevel.LineInformation[]", 0x28),
            ("InLevel.LineControl", "lineGroups") => (owner, "InLevel.LinesGroupInformation[]", 0x30),
            ("InLevel.LineControl", "_canvasControl") => (owner, "InLevel.CanvasControl", 0x38),
            ("InLevel.LineControl", "_nowTime") => (owner, "System.Single", 0x40),
            ("InLevel.LineControl", "_sceneWidth") => (owner, "System.Single", 0x44),
            ("InLevel.LineControl", "_speed") => (owner, "System.Single", 0x48),
            ("InLevel.LineControl", "_gameSceneScale") => (owner, "System.Single", 0x4c),
            ("InLevel.LineControl", "linePositions") => (owner, "UnityEngine.Vector3[]", 0x78),
            ("InLevel.LineInformation", "lineIsStatic") => (owner, "System.Boolean", 0x20),
            ("InLevel.LineInformation", "canvasIndex") => (owner, "System.Int32", 0x24),
            ("InLevel.LineInformation", "startLinePoint") => (owner, "InLevel.LinePoint", 0x30),
            ("InLevel.LineInformation", "endLinePoint") => (owner, "InLevel.LinePoint", 0x38),
            ("InLevel.LineInformation", "endTransform") => (owner, "UnityEngine.Transform", 0x40),
            ("InLevel.LineInformation", "endLineInformation") => (owner, "InLevel.LineInformation", 0x58),
            ("InLevel.LineInformation", "linesGroup") => (owner, "InLevel.LinesGroupInformation", 0x60),
            ("InLevel.LinePoint", "time") => (owner, "System.Single", 0x10),
            ("InLevel.LinePoint", "xPosition") => (owner, "System.Single", 0x14),
            ("InLevel.LinePoint", "easeType") => (owner, "System.Int32", 0x1c),
            ("InLevel.LinePoint", "canvasIndex") => (owner, "System.Int32", 0x20),
            ("InLevel.LinePoint", "floorPosition") => (owner, "System.Single", 0x24),
            ("InLevel.LinesGroupInformation", "judgeRingTransform") => (owner, "UnityEngine.Transform", 0x20),
            ("InLevel.LinesGroupInformation", "ringActive") => (owner, "System.Boolean", 0x29),
            ("InLevel.LinesGroupInformation", "lines") => (owner, "InLevel.LineInformation[]", 0x68),
            ("InLevel.LinesGroupInformation", "nowLineIndex") => (owner, "System.Int32", 0x70),
            _ => throw new UnsupportedSignatureException("The field is outside the exact line/ring source contract.")
        };

    internal NativeExactReadField InspectLineRingSourceField(IntPtr cls, string name, string expectedType, int offset, Action guard)
    {
        ArgumentNullException.ThrowIfNull(guard);
        T O<T>(Func<T> read) => LineRingObserve(guard, read);
        var owner = LineRingTypeName(O(() => _classType(cls)), guard);
        var specification = LineRingFieldSpecification(owner, name);
        if (specification.Type != expectedType || specification.Offset != offset
            || O(() => Class("Assembly-CSharp.dll", "InLevel", owner[8..])) != cls)
            throw new UnsupportedSignatureException("The exact line/ring source declaration differs.");
        var parent = O(() => ReadGraphExport<Ptr1>("il2cpp_field_get_parent"));
        var found = IntPtr.Zero; var terminated = false;
        using (var iterator = Arg.Value(IntPtr.Zero))
            for (var i = 0; i < 256; ++i)
            {
                var field = O(() => _fields(cls, iterator.Pointer));
                if (field == IntPtr.Zero) { terminated = true; break; }
                if (O(() => parent(field)) != cls) throw new UnsupportedSignatureException("A line/ring field has a foreign declaring class.");
                if (Utf8(O(() => _fieldName(field))) != name) continue;
                if (found != IntPtr.Zero) throw new UnsupportedSignatureException("Ambiguous line/ring source field.");
                found = field;
            }
        if (!terminated || found == IntPtr.Zero || O(() => _findField(cls, name)) != found)
            throw new UnsupportedSignatureException("Exact line/ring source field is absent.");
        var flags = O(() => _fieldFlags(found)); var type = O(() => _fieldType(found));
        var actualOffset = O(() => _fieldOffset(found)); var layout = LineRingLayout(type, guard);
        if ((flags & (0x10u | 0x40u)) != 0 || layout.Name != expectedType || actualOffset != offset)
            throw new UnsupportedSignatureException("Line/ring source field flags/type/offset changed.");
        var size = O(() => ReadGraphExport<ArraySize>("il2cpp_class_instance_size")(cls));
        if (offset < 2 * IntPtr.Size || (long)offset + layout.Size > (long)size)
            throw new UnsupportedSignatureException("Line/ring field exceeds its complete instance storage.");
        return new(found, type, cls, layout.Class, name, expectedType, false, offset, layout.Size, flags);
    }

    private IntPtr LineRingFieldOwner(RootHandle root, NativeExactReadField field, Action guard)
    {
        T O<T>(Func<T> read) => LineRingObserve(guard, read);
        if (field.IsStatic || InspectLineRingSourceField(field.DeclaringClass, field.Name, field.TypeName, field.Offset, guard) != field)
            throw new UnsupportedSignatureException("Saved/current line/ring source descriptor differs.");
        var value = O(() => root.Target); var actual = O(() => _objectClass(value));
        if (actual != field.DeclaringClass || O(() => _findField(actual, field.Name)) != field.Field)
            throw new UnsupportedSignatureException("A line/ring receiver has another exact class or field.");
        return value;
    }

    internal T ReadLineRingSourceField<T>(RootHandle owner, NativeExactReadField field, Action guard) where T : struct
    {
        var value = LineRingFieldOwner(owner, field, guard); var layout = LineRingLayout(field.Type, guard);
        if (layout.Kind == NativeValueKind.Reference) throw new UnsupportedSignatureException("References require immediate line/ring publication.");
        NativeAbi.ValidateValue(typeof(T), layout); using var result = new Arg(layout.Size);
        guard(); _fieldGet(value, field.Field, result.Pointer); guard(); return ReadValue<T>(result.Pointer);
    }

    internal void PublishLineRingSourceReferenceField(RootHandle owner, NativeExactReadField field, Action<IntPtr> publish, Action guard)
    {
        ArgumentNullException.ThrowIfNull(publish); var call = new NativePublicationCall(VerifyAccess, Array.Empty<Arg>());
        try
        {
            var value = LineRingFieldOwner(owner, field, guard);
            if (LineRingLayout(field.Type, guard).Kind != NativeValueKind.Reference)
                throw new UnsupportedSignatureException("Line/ring publication requires an exact reference field.");
            using var output = new Arg(IntPtr.Size); guard(); _fieldGet(value, field.Field, output.Pointer);
            var result = Marshal.ReadIntPtr(output.Pointer);
            // Accept publication before the post-get guard. The caller can
            // acknowledge a withdrawn GC-owned result without creating a root.
            call.BeginPublication(result);
            try { publish(result); call.PublicationAccepted(); }
            catch { call.PublicationFailed(); throw; }
            guard();
        }
        catch (Exception error) { call.OperationFailed(error); throw; }
        finally { call.Dispose(); }
    }

    internal NativeMetadataMethod InspectLineRingSourceGetter(IntPtr cls, string name, IntPtr module, Action? guard = null)
    {
        guard ??= VerifyAccess; T O<T>(Func<T> read) => LineRingObserve(guard, read);
        var reference = name is "get_transform" or "get_parent";
        var returns = reference ? "UnityEngine.Transform" : name == "get_localRotation" ? "UnityEngine.Quaternion" : "UnityEngine.Vector3";
        if (name is not ("get_transform" or "get_parent" or "get_position" or "get_localPosition" or "get_localRotation" or "get_localScale")
            || O(() => Class("UnityEngine.CoreModule.dll", "UnityEngine", name == "get_transform" ? "Component" : "Transform")) != cls)
            throw new UnsupportedSignatureException("Only six exact line/ring instance getters are admitted.");
        var found = IntPtr.Zero; var terminated = false;
        using (var iterator = Arg.Value(IntPtr.Zero))
            for (var i = 0; i < 4096; ++i)
            {
                var method = O(() => _methods(cls, iterator.Pointer));
                if (method == IntPtr.Zero) { terminated = true; break; }
                if (Utf8(O(() => _methodName(method))) != name || O(() => _parameterCount(method)) != 0) continue;
                if (found != IntPtr.Zero) throw new UnsupportedSignatureException("Ambiguous exact line/ring getter.");
                found = method;
            }
        if (!terminated || found == IntPtr.Zero || O(() => _methodClass(found)) != cls)
            throw new UnsupportedSignatureException("The exact line/ring getter is absent or foreign.");
        uint implementation = 0; var flags = O(() => _methodFlags(found, out implementation));
        var type = O(() => _returnType(found)); var layout = LineRingLayout(type, guard);
        var generic = O(() => _isGenericMethod(found)); var inflated = O(() => _isInflatedMethod(found));
        if ((flags & (0x10u | 0x400u)) != 0 || generic || inflated || layout.Name != returns
            || reference != (layout.Kind == NativeValueKind.Reference))
            throw new UnsupportedSignatureException("The fresh line/ring getter signature or ABI changed.");
        NativeMetadataEntry? entry = null; var status = "Unverified: no pinned executable module was supplied.";
        if (module != IntPtr.Zero)
        {
            guard(); HookTargetValidation.VerifyReadable(found, (nuint)Marshal.SizeOf<HookTargetValidation.MethodInfo2022>());
            var record = Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(found);
            HookTargetValidation.ValidateMetadata(record, O(() => _methodName(found)), cls, type, flags, 0, record.MethodPointer);
            if (record.ImplementationFlags != (ushort)implementation) throw new UnsupportedSignatureException("Getter implementation flags changed.");
            guard(); HookTargetValidation.VerifyExecutable(record.MethodPointer, module); guard();
            entry = new(record.MethodPointer, record.MethodPointer.ToInt64() - module.ToInt64(), record.Slot); status = "Verified exact MethodInfo2022 metadata and executable module entry.";
        }
        return new(found, new(cls, LineRingTypeName(O(() => _classType(cls)), guard)), name, returns,
            Array.Empty<string>(), flags, implementation, generic, inflated, entry, status);
    }
}
