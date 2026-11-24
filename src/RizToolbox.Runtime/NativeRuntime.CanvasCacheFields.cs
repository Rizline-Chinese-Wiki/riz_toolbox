namespace RizToolbox.Runtime;

internal sealed partial class NativeRuntime
{
    // Read evidence is not write admission. Recheck the five actual cache
    // declarations and fresh canonical Single storage independently of Describe.
    internal NativeExactReadField InspectCanvasCacheField(IntPtr cls, string name)
    {
        VerifyAccess();
        if (cls != Class("Assembly-CSharp.dll", "InLevel", "CanvasControl"))
            throw new UnsupportedSignatureException("Canvas cache fields require the exact official CanvasControl class.");
        var (type, offset) = name switch
        {
            "floorPositions" => ("System.Single[]", 0x38),
            "floorSpeeds" => ("System.Single[]", 0x40),
            "xPositions" => ("System.Single[]", 0x48),
            "nowTime" => ("System.Single", 0x50),
            "speed" => ("System.Single", 0x54),
            _ => throw new UnsupportedSignatureException("The field is outside the five exact Canvas cache declarations.")
        };
        var field = InspectExactDeclaredReadField(cls, name, type, false, offset);
        if ((field.Flags & (0x10u | 0x20u | 0x40u)) != 0)
            throw new UnsupportedSignatureException("Canvas cache storage requires its current mutable instance declaration.");
        var single = VerifyCanvasCacheSingle();
        if (type == "System.Single")
        {
            if (field.FieldClass != single || field.Size != sizeof(float) || _typeCode(field.Type) != 12)
                throw new UnsupportedSignatureException("Canvas time and speed require exact current Single storage.");
        }
        else
        {
            if (field.Size != IntPtr.Size) throw new UnsupportedSignatureException("Canvas cache vector reference width changed.");
            VerifyGeometrySingleArrayClass(field.FieldClass, single);
        }
        return field;
    }

    private IntPtr VerifyCanvasCacheSingle()
    {
        var cls = Class("mscorlib.dll", "System", "Single"); var type = _classType(cls);
        if (type == IntPtr.Zero || _byRef(type) || _typeClass(type) != cls || _typeCode(type) != 12
            || !_isValueType(cls) || _isEnum(cls) || _valueSize(cls, out _) != sizeof(float)
            || MetadataTypeName(type) != "System.Single")
            throw new UnsupportedSignatureException("The current Canvas cache scalar differs from canonical Single storage.");
        var layout = Describe(type);
        if (layout.Class != cls || layout.ByRef || layout.Kind != NativeValueKind.Single || layout.Size != sizeof(float))
            throw new UnsupportedSignatureException("The current Canvas cache scalar differs from its cached ABI.");
        return cls;
    }
}
