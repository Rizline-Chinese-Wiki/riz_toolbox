namespace RizToolbox.Runtime;

internal sealed record NativeRendererChildListLayout(IntPtr Class, IntPtr ArrayClass, IntPtr ElementClass,
    NativeExactReadField BackingField, NativeExactReadField SizeField, NativeExactReadField VersionField);

internal sealed partial class NativeRuntime
{
    internal const int MaximumRendererReferenceArrayLength = 65536;

    /// <summary>Accepts only the actual closed EasyPoolChild List declared by an
    /// observed pool. Generic dump offsets are not accepted as an instance layout.</summary>
    internal NativeRendererChildListLayout InspectRendererChildListClass(IntPtr actualClosedClass)
    {
        VerifyAccess();
        var child = Class("Assembly-CSharp.dll", "InLevel", "EasyPoolChild");
        var objectClass = Class("mscorlib.dll", "System", "Object");
        if (actualClosedClass == IntPtr.Zero)
            throw new UnsupportedSignatureException("The actual renderer pool List class is absent.");
        var image = ReadGraphExport<Ptr1>("il2cpp_class_get_image");
        var type = _classType(actualClosedClass);
        if (type == IntPtr.Zero || _typeClass(type) != actualClosedClass || _byRef(type) || _typeCode(type) != 21
            || _isValueType(actualClosedClass) || ReadGraphExport<Predicate>("il2cpp_class_is_generic")(actualClosedClass)
            || !ReadGraphExport<Predicate>("il2cpp_class_is_inflated")(actualClosedClass)
            || _classParent(actualClosedClass) != objectClass || image(actualClosedClass) != image(objectClass)
            || Utf8(_classNamespace(actualClosedClass)) != "System.Collections.Generic"
            || Utf8(_className(actualClosedClass)) != "List`1"
            || MetadataTypeName(type) != "System.Collections.Generic.List<InLevel.EasyPoolChild>")
            throw new UnsupportedSignatureException("The actual pool storage is not the exact closed mscorlib EasyPoolChild List.");
        var backing = InspectExactDeclaredReadField(actualClosedClass, "_items", "InLevel.EasyPoolChild[]", false);
        var size = InspectExactDeclaredReadField(actualClosedClass, "_size", "System.Int32", false);
        var version = InspectExactDeclaredReadField(actualClosedClass, "_version", "System.Int32", false);
        var integer = Class("mscorlib.dll", "System", "Int32");
        if (size.FieldClass != integer || version.FieldClass != integer || size.Size != sizeof(int) || version.Size != sizeof(int)
            || backing.Size != IntPtr.Size || Overlaps(backing, size) || Overlaps(backing, version) || Overlaps(size, version))
            throw new UnsupportedSignatureException("The actual pool List has overlapping or different backing/count/version storage.");
        VerifyReferenceArrayClass(backing.FieldClass, child);
        return new(actualClosedClass, backing.FieldClass, child, backing, size, version);
    }

    /// <summary>Renderer observation has its own complete bound. It does not widen
    /// the Tween reader or accept arbitrary element types. Every loaded reference
    /// is published before inspection, including unused non-null capacity slots.</summary>
    internal NativeRootedReferenceArray ReadRootedRendererReferenceArrayWithPublication(RootHandle array,
        IntPtr exactArrayClass, IntPtr exactElementClass, int maximum, Action<int, IntPtr> publish)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(array); ArgumentNullException.ThrowIfNull(publish);
        if (maximum < 0 || maximum > MaximumRendererReferenceArrayLength)
            throw new ArgumentOutOfRangeException(nameof(maximum), "Renderer array observation exceeds its complete bound.");
        var child = Class("Assembly-CSharp.dll", "InLevel", "EasyPoolChild");
        var note = Class("Assembly-CSharp.dll", "InLevel", "NoteInformation");
        var renderer = NestedClass(note, "NoteRenderer");
        if (exactElementClass != child && exactElementClass != renderer)
            throw new UnsupportedSignatureException("Renderer arrays require the exact EasyPoolChild or nested NoteRenderer class.");
        return ReadRootedReferenceArrayCore(array, exactArrayClass, exactElementClass, maximum,
            MaximumRendererReferenceArrayLength, publish);
    }
}
