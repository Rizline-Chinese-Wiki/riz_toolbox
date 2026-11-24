namespace RizToolbox.Runtime;

internal sealed partial class NativeRuntime
{
    /// <summary>Metadata-only source contracts, including exported byref/layout
    /// evidence that a name-only signature comparison cannot establish.</summary>
    internal NativeMetadataMethod InspectCanvasSourceMethod(IntPtr cls, string name, string[] parameters,
        string returns, IntPtr module)
    {
        VerifyAccess();
        var range = name == "ChartTimeRageToRealTime" && returns == "System.Single"
            && parameters.SequenceEqual(new[] { "System.Double", "System.Double" });
        var count = name == "get_Count" && returns == "System.Int32" && parameters.Length == 0;
        var item = name == "get_Item" && returns == "InLevel.KeyPoint" && parameters.SequenceEqual(new[] { "System.Int32" });
        if (!range && !count && !item) throw new UnsupportedSignatureException("The Canvas source method is outside its exact observation contract.");
        var method = InspectDeclaredMethod(cls, name, parameters, returns, module);
        if ((method.Flags & (0x10u | 0x400u)) != 0 || method.GenericMethod || range && method.InflatedMethod)
            throw new UnsupportedSignatureException("The Canvas source method has another static, abstract or generic shape.");
        var returnType = _returnType(method.MethodInfo);
        if (_byRef(returnType)) throw new UnsupportedSignatureException("The Canvas source result is byref.");
        VerifyCanvasSourceType(returnType, returns);
        var output = Describe(returnType);
        if (output.ByRef || output.Name != returns)
            throw new UnsupportedSignatureException("The Canvas source method has another exact non-byref result.");
        if (range) NativeAbi.ValidateValue(typeof(float), output);
        else if (count) NativeAbi.ValidateValue(typeof(int), output);
        else if (output.Kind != NativeValueKind.Reference || output.Class != Class("Assembly-CSharp.dll", "InLevel", "KeyPoint"))
            throw new UnsupportedSignatureException("The official KeyPoint List item result has another actual class.");
        for (var i = 0; i < parameters.Length; ++i)
        {
            var parameter = _parameterType(method.MethodInfo, (uint)i);
            if (_byRef(parameter)) throw new UnsupportedSignatureException("The Canvas source input is byref.");
            VerifyCanvasSourceType(parameter, parameters[i]);
            var input = Describe(parameter);
            if (input.ByRef || input.Name != parameters[i])
                throw new UnsupportedSignatureException("The Canvas source method has another exact non-byref input.");
            NativeAbi.ValidateValue(range ? typeof(double) : typeof(int), input);
        }
        return method;
    }

    // Describe caches successful layouts. An observation contract must also
    // check current exported storage, so a warm cache cannot hide ABI drift.
    private void VerifyCanvasSourceType(IntPtr type, string name)
    {
        var reference = name == "InLevel.KeyPoint";
        var cls = reference ? Class("Assembly-CSharp.dll", "InLevel", "KeyPoint")
            : Class("mscorlib.dll", "System", name.Substring("System.".Length));
        var canonicalType = _classType(cls);
        var (code, size) = name switch
        {
            "System.Single" => (12, sizeof(float)),
            "System.Double" => (13, sizeof(double)),
            "System.Int32" => (8, sizeof(int)),
            "InLevel.KeyPoint" => (18, IntPtr.Size),
            _ => throw new UnsupportedSignatureException("Unsupported Canvas source scalar or reference type.")
        };
        if (type == IntPtr.Zero || canonicalType == IntPtr.Zero || _byRef(type) || _byRef(canonicalType)
            || _typeClass(type) != cls || _typeClass(canonicalType) != cls
            || _typeCode(type) != code || _typeCode(canonicalType) != code
            || _isValueType(cls) == reference || _isEnum(cls)
            || !reference && _valueSize(cls, out _) != size
            || MetadataTypeName(type) != name || MetadataTypeName(canonicalType) != name)
            throw new UnsupportedSignatureException("The current Canvas source type differs from its exact native storage.");
        var layout = Describe(type);
        if (layout.Class != cls || layout.Size != size || layout.ByRef
            || reference && layout.Kind != NativeValueKind.Reference)
            throw new UnsupportedSignatureException("The Canvas source type differs from its canonical ABI layout.");
    }
}
