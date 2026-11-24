namespace RizToolbox.Runtime;

internal sealed partial class NativeRuntime
{
    /// <summary>Metadata-only admission for the two exact Canvas position
    /// methods. Fresh native Vector3/Single storage is checked independently of
    /// Describe's cache. No getter, setter or internal call is invoked here.</summary>
    internal NativeMetadataMethod InspectCanvasPositionMethod(IntPtr cls, string name, IntPtr module)
    {
        VerifyAccess();
        var getter = name == "get_localPosition";
        if (!getter && name != "set_localPosition")
            throw new UnsupportedSignatureException("Canvas position permits only the exact local-position getter and setter.");
        var transform = Class("UnityEngine.CoreModule.dll", "UnityEngine", "Transform");
        if (cls != transform)
            throw new UnsupportedSignatureException("Canvas position requires the official exact Transform declaring class.");
        VerifyCanvasPositionClass(transform, "UnityEngine.CoreModule.dll", "UnityEngine", "Transform", 18, false);
        var parameters = getter ? Array.Empty<string>() : new[] { "UnityEngine.Vector3" };
        var returns = getter ? "UnityEngine.Vector3" : "System.Void";
        var method = InspectDeclaredMethod(cls, name, parameters, returns, module);
        if (method.MethodInfo == IntPtr.Zero || method.DeclaringClass.Class != cls || method.Name != name
            || method.ReturnType != returns || !method.ParameterTypes.SequenceEqual(parameters)
            || (method.Flags & (0x10u | 0x400u)) != 0 || method.GenericMethod || method.InflatedMethod
            || _methodClass(method.MethodInfo) != cls || _parameterCount(method.MethodInfo) != (uint)parameters.Length)
            throw new UnsupportedSignatureException("The current Canvas position MethodInfo has another exact instance signature.");
        var output = _returnType(method.MethodInfo);
        if (getter) VerifyCanvasPositionVector(output);
        else
        {
            VerifyCanvasPositionVoid(output);
            VerifyCanvasPositionVector(_parameterType(method.MethodInfo, 0));
        }
        return method;
    }

    private IntPtr VerifyCanvasPositionClass(IntPtr cls, string imageName, string ns, string name, int code, bool valueType)
    {
        var type = _classType(cls);
        var image = ReadGraphExport<Ptr1>("il2cpp_class_get_image")(cls);
        var typeName = ns + "." + name;
        if (type == IntPtr.Zero || image == IntPtr.Zero || _byRef(type) || _typeClass(type) != cls
            || _typeCode(type) != code || _isValueType(cls) != valueType || _isEnum(cls)
            || MetadataTypeName(type) != typeName || Utf8(_classNamespace(cls)) != ns || Utf8(_className(cls)) != name
            || Utf8(_imageName(image)) != imageName)
            throw new UnsupportedSignatureException("The current Canvas position class differs from its canonical declaration: " + typeName);
        return type;
    }
    private void VerifyCanvasPositionVoid(IntPtr type)
    {
        var cls = Class("mscorlib.dll", "System", "Void");
        var canonical = VerifyCanvasPositionClass(cls, "mscorlib.dll", "System", "Void", 1, true);
        if (type == IntPtr.Zero || canonical == IntPtr.Zero || _byRef(type) || _byRef(canonical)
            || _typeCode(type) != 1 || _typeCode(canonical) != 1 || _typeClass(type) != cls || _typeClass(canonical) != cls
            || MetadataTypeName(type) != "System.Void" || MetadataTypeName(canonical) != "System.Void")
            throw new UnsupportedSignatureException("The Canvas position setter result is not the exact non-byref Void type.");
        var layout = Describe(type);
        if (layout.Kind != NativeValueKind.Void || layout.ByRef || layout.Size != 0 || layout.Name != "System.Void")
            throw new UnsupportedSignatureException("The Canvas position setter result has another cached ABI layout.");
    }
    private void VerifyCanvasPositionSingle(IntPtr type)
    {
        var cls = Class("mscorlib.dll", "System", "Single");
        _ = VerifyCanvasPositionClass(cls, "mscorlib.dll", "System", "Single", 12, true);
        if (type == IntPtr.Zero || _byRef(type) || _typeClass(type) != cls || _typeCode(type) != 12
            || _valueSize(cls, out _) != sizeof(float) || MetadataTypeName(type) != "System.Single")
            throw new UnsupportedSignatureException("A current Canvas position vector component differs from exact Single storage.");
        var layout = Describe(type);
        if (layout.Class != cls || layout.ByRef || layout.Kind != NativeValueKind.Single || layout.Size != sizeof(float)
            || layout.Name != "System.Single")
            throw new UnsupportedSignatureException("A Canvas position vector component differs from its cached canonical ABI.");
        NativeAbi.ValidateValue(typeof(float), layout);
    }
    private void VerifyCanvasPositionVector(IntPtr type)
    {
        var cls = Class("UnityEngine.CoreModule.dll", "UnityEngine", "Vector3");
        _ = VerifyCanvasPositionClass(cls, "UnityEngine.CoreModule.dll", "UnityEngine", "Vector3", 17, true);
        if (type == IntPtr.Zero || _byRef(type) || _typeClass(type) != cls || _typeCode(type) != 17
            || _valueSize(cls, out _) != 3 * sizeof(float) || MetadataTypeName(type) != "UnityEngine.Vector3")
            throw new UnsupportedSignatureException("The current Canvas position type differs from exact Vector3 storage.");
        var parent = ReadGraphExport<Ptr1>("il2cpp_field_get_parent");
        var fields = new Dictionary<string, NativeFieldLayout>(); var terminated = false;
        using (var iterator = Arg.Value(IntPtr.Zero))
            for (var count = 0; count < 256; ++count)
            {
                var field = _fields(cls, iterator.Pointer);
                if (field == IntPtr.Zero) { terminated = true; break; }
                if (parent(field) != cls)
                    throw new UnsupportedSignatureException("Canvas position Vector3 field enumeration returned a foreign declaration.");
                var flags = _fieldFlags(field);
                if ((flags & 0x10u) != 0) continue;
                var name = Utf8(_fieldName(field));
                var expected = name switch { "x" => 0, "y" => sizeof(float), "z" => 2 * sizeof(float), _ => -1 };
                if (expected < 0 || (flags & (0x20u | 0x40u)) != 0 || _fieldOffset(field) != 2 * IntPtr.Size + expected
                    || _findField(cls, name) != field || fields.ContainsKey(name))
                    throw new UnsupportedSignatureException("The current Canvas position Vector3 fields are absent, duplicated or have another layout.");
                VerifyCanvasPositionSingle(_fieldType(field));
                fields.Add(name, new(name, "System.Single", expected));
            }
        if (!terminated || fields.Count != 3)
            throw new UnsupportedSignatureException("The complete current Canvas position Vector3 declaration exceeds its bound or lacks three components.");
        var current = new NativeTypeLayout("UnityEngine.Vector3", NativeValueKind.Struct, 3 * sizeof(float),
            Class: cls, Fields: Array.AsReadOnly(fields.Values.OrderBy(field => field.Offset).ToArray()));
        NativeAbi.ValidateNativeStruct(current);
        var cached = Describe(type);
        NativeAbi.ValidateNativeStruct(cached);
        if (cached.Class != cls || cached.ByRef || cached.Name != current.Name || cached.Size != current.Size
            || cached.Kind != current.Kind || !cached.Fields!.OrderBy(field => field.Offset).SequenceEqual(current.Fields!))
            throw new UnsupportedSignatureException("The current Canvas position Vector3 differs from its cached ABI layout.");
    }
}
