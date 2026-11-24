namespace RizToolbox.Runtime;

internal sealed record NativeNoteJudgeListLayout(IntPtr Class, IntPtr ArrayClass, IntPtr ElementClass,
    NativeExactReadField BackingField, NativeExactReadField SizeField, NativeExactReadField VersionField);

internal sealed partial class NativeRuntime
{
    /// <summary>Metadata-only, fixed List&lt;InLevel.NoteInformation&gt; observation.
    /// The closed class must come from an actual declared Judge field. This adds
    /// no writer, allocator or array reader and leaves all Tween bounds intact.</summary>
    internal NativeNoteJudgeListLayout InspectNoteJudgeListClass(IntPtr actualClosedClass)
    {
        VerifyAccess();
        var note = Class("Assembly-CSharp.dll", "InLevel", "NoteInformation");
        var objectClass = Class("mscorlib.dll", "System", "Object");
        if (actualClosedClass == IntPtr.Zero)
            throw new UnsupportedSignatureException("The actual Judge list class is absent.");
        var image = ReadGraphExport<Ptr1>("il2cpp_class_get_image");
        var type = _classType(actualClosedClass);
        if (type == IntPtr.Zero || _typeClass(type) != actualClosedClass || _byRef(type) || _typeCode(type) != 21
            || _isValueType(actualClosedClass) || ReadGraphExport<Predicate>("il2cpp_class_is_generic")(actualClosedClass)
            || !ReadGraphExport<Predicate>("il2cpp_class_is_inflated")(actualClosedClass)
            || _classParent(actualClosedClass) != objectClass || image(actualClosedClass) != image(objectClass)
            || Utf8(_classNamespace(actualClosedClass)) != "System.Collections.Generic"
            || Utf8(_className(actualClosedClass)) != "List`1"
            || MetadataTypeName(type) != "System.Collections.Generic.List<InLevel.NoteInformation>")
            throw new UnsupportedSignatureException("The actual Judge queue is not the exact closed mscorlib NoteInformation list.");
        var backing = InspectExactDeclaredReadField(actualClosedClass, "_items", "InLevel.NoteInformation[]", false);
        var size = InspectExactDeclaredReadField(actualClosedClass, "_size", "System.Int32", false);
        var version = InspectExactDeclaredReadField(actualClosedClass, "_version", "System.Int32", false);
        var integer = Class("mscorlib.dll", "System", "Int32");
        if (size.FieldClass != integer || version.FieldClass != integer || size.Size != sizeof(int) || version.Size != sizeof(int)
            || backing.Size != IntPtr.Size || Overlaps(backing, size) || Overlaps(backing, version) || Overlaps(size, version))
            throw new UnsupportedSignatureException("The actual Judge list has overlapping or different backing/count/version storage.");
        VerifyReferenceArrayClass(backing.FieldClass, note);
        return new(actualClosedClass, backing.FieldClass, note, backing, size, version);
    }
}
