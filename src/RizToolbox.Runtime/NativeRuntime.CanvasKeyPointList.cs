namespace RizToolbox.Runtime;

internal sealed partial class NativeRuntime
{
    internal void VerifyCanvasKeyPointListClass(IntPtr cls)
    {
        VerifyAccess(); var point = Class("Assembly-CSharp.dll", "InLevel", "KeyPoint"); var obj = Class("mscorlib.dll", "System", "Object");
        if (cls == IntPtr.Zero) throw new UnsupportedSignatureException("The actual Canvas keypoint List is absent.");
        var type = _classType(cls); var image = ReadGraphExport<Ptr1>("il2cpp_class_get_image");
        if (type == IntPtr.Zero || _typeClass(type) != cls || _byRef(type) || _typeCode(type) != 21 || _isValueType(cls)
            || ReadGraphExport<Predicate>("il2cpp_class_is_generic")(cls) || !ReadGraphExport<Predicate>("il2cpp_class_is_inflated")(cls)
            || _classParent(cls) != obj || image(cls) != image(obj) || Utf8(_classNamespace(cls)) != "System.Collections.Generic"
            || Utf8(_className(cls)) != "List`1" || MetadataTypeName(type) != "System.Collections.Generic.List<InLevel.KeyPoint>")
            throw new UnsupportedSignatureException("The actual Canvas source is not its exact closed mscorlib KeyPoint List.");
        var backing = InspectExactDeclaredReadField(cls, "_items", "InLevel.KeyPoint[]", false, 0x10);
        var size = InspectExactDeclaredReadField(cls, "_size", "System.Int32", false, 0x18);
        var version = InspectExactDeclaredReadField(cls, "_version", "System.Int32", false, 0x1c);
        if (size.Size != sizeof(int) || version.Size != sizeof(int) || backing.Size != IntPtr.Size)
            throw new UnsupportedSignatureException("The complete Canvas List storage has different native widths.");
        VerifyReferenceArrayClass(backing.FieldClass, point);
    }
}
