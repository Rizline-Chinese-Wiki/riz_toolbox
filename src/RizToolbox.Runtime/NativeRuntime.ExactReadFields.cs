using System.Runtime.InteropServices;

namespace RizToolbox.Runtime;

/// <summary>Official declared-field evidence. A saved descriptor grants no access:
/// every read rechecks current metadata and the actual caller-owned receiver root.</summary>
internal sealed record NativeExactReadField(IntPtr Field, IntPtr Type, IntPtr DeclaringClass,
    IntPtr FieldClass, string Name, string TypeName, bool IsStatic, int Offset, int Size, uint Flags);

internal sealed partial class NativeRuntime
{
    internal NativeExactReadField InspectExactDeclaredReadField(IntPtr declaringClass, string name,
        string expectedType, bool isStatic, int? expectedOffset = null)
    {
        VerifyAccess();
        if (declaringClass == IntPtr.Zero || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(expectedType))
            throw new UnsupportedSignatureException("An exact read requires an official declaring class, field name and type.");
        var parent = ReadGraphExport<Ptr1>("il2cpp_field_get_parent");
        IntPtr found = IntPtr.Zero; var terminated = false;
        using (var iterator = Arg.Value(IntPtr.Zero))
            for (var count = 0; count < 256; ++count)
            {
                var field = _fields(declaringClass, iterator.Pointer);
                if (field == IntPtr.Zero) { terminated = true; break; }
                if (parent(field) != declaringClass)
                    throw new UnsupportedSignatureException("Declared read field enumeration returned a foreign owner.");
                if (Utf8(_fieldName(field)) != name) continue;
                if (found != IntPtr.Zero) throw new UnsupportedSignatureException("Exact read field metadata is ambiguous.");
                found = field;
            }
        if (!terminated || found == IntPtr.Zero || Field(declaringClass, name) != found)
            throw new UnsupportedSignatureException("Exact read field is absent, shadowed or exceeds its metadata bound.");
        var flags = _fieldFlags(found); var type = _fieldType(found); var offset = _fieldOffset(found);
        if (((flags & 0x10u) != 0) != isStatic || (flags & 0x40u) != 0 || type == IntPtr.Zero || _byRef(type)
            || MetadataTypeName(type) != expectedType || offset < 0 || expectedOffset is { } expected && offset != expected)
            throw new UnsupportedSignatureException("Exact read field staticness, type or official offset differs.");
        var cls = _typeClass(type);
        if (cls == IntPtr.Zero || _typeClass(_classType(cls)) != cls || MetadataTypeName(_classType(cls)) != expectedType)
            throw new UnsupportedSignatureException("Exact read field class/type round trip differs.");
        var layout = Describe(type); var code = _typeCode(type);
        if (layout.ByRef || layout.Size <= 0 || layout.Class != cls || layout.Kind is NativeValueKind.Unsupported or NativeValueKind.Void
            || layout.Kind == NativeValueKind.Reference && (_isValueType(cls) || layout.Size != IntPtr.Size || code is not (14 or 18 or 20 or 21 or 28 or 29))
            || layout.Kind != NativeValueKind.Reference && (!_isValueType(cls) || _valueSize(cls, out _) != layout.Size))
            throw new UnsupportedSignatureException("Exact read field has changed or unsupported native storage.");
        if (!isStatic)
        {
            var instanceSize = ReadGraphExport<ArraySize>("il2cpp_class_instance_size")(declaringClass);
            if (offset < 2 * IntPtr.Size || (long)offset + layout.Size > instanceSize)
                throw new UnsupportedSignatureException("Exact read field exceeds its official declaring instance storage.");
        }
        return new(found, type, declaringClass, cls, name, expectedType, isStatic, offset, layout.Size, flags);
    }

    internal T ReadExactField<T>(RootHandle owner, NativeExactReadField field) where T : struct
    {
        VerifyAccess(); var value = VerifyExactReadOwner(owner, field, false);
        if (Describe(field.Type).Kind == NativeValueKind.Reference)
            throw new UnsupportedSignatureException("Reference field reads require immediate publication and root adoption.");
        return ReadField<T>(value, field.Field, false);
    }

    /// <summary>Exact static scalar/known value-layout observation. Reference
    /// fields still require publication; no instance owner or root is substituted.</summary>
    internal T ReadExactStaticField<T>(NativeExactReadField field) where T : struct
    {
        VerifyAccess(); VerifyExactReadOwner(null, field, true);
        if (Describe(field.Type).Kind == NativeValueKind.Reference)
            throw new UnsupportedSignatureException("Static reference fields require immediate publication and root adoption.");
        return ReadField<T>(IntPtr.Zero, field.Field, true);
    }

    /// <summary>The two game enums are observed as their verified Int32 bits only.
    /// This does not permit passing an Int32 in place of an enum to runtime_invoke.</summary>
    internal int ReadTweenInt32EnumField(RootHandle owner, NativeExactReadField field)
    {
        VerifyAccess(); var value = VerifyExactReadOwner(owner, field, false);
        var tween = Class("Assembly-CSharp.dll", "", "TweenInfo");
        if (field.DeclaringClass != tween || !(field.Name == "easeType" && field.TypeName == "TweenInfo.EaseType"
            || field.Name == "tweenType" && field.TypeName == "TweenInfo.TweenType") || !_isEnum(field.FieldClass))
            throw new UnsupportedSignatureException("The enum observation is not one of the two official TweenInfo fields.");
        var underlying = _enumBase(field.FieldClass);
        if (Describe(field.Type) is not { Kind: NativeValueKind.Enum, Size: 4 }
            || underlying == IntPtr.Zero || _byRef(underlying) || _typeCode(underlying) != 8
            || _typeClass(underlying) != Class("mscorlib.dll", "System", "Int32")
            || MetadataTypeName(underlying) != "System.Int32")
            throw new UnsupportedSignatureException("TweenInfo enum metadata is not official Int32 storage.");
        using var output = new Arg(sizeof(int)); _fieldGet(value, field.Field, output.Pointer);
        return Marshal.ReadInt32(output.Pointer);
    }

    internal void PublishExactReferenceField(RootHandle owner, NativeExactReadField field, Action<IntPtr> publish)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(publish);
        PublishExactReferenceFieldCore(owner, field, false, publish);
    }

    internal void PublishExactStaticReferenceField(NativeExactReadField field, Action<IntPtr> publish)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(publish);
        PublishExactReferenceFieldCore(null, field, true, publish);
    }

    private void PublishExactReferenceFieldCore(RootHandle? owner, NativeExactReadField field, bool isStatic, Action<IntPtr> publish)
    {
        var call = new NativePublicationCall(VerifyAccess, Array.Empty<Arg>());
        try
        {
            var value = VerifyExactReadOwner(owner, field, isStatic);
            if (Describe(field.Type).Kind != NativeValueKind.Reference)
                throw new UnsupportedSignatureException("Reference publication requires an exact reference field.");
            var result = ReadField<IntPtr>(value, field.Field, isStatic);
            // A field getter has already returned a reference. No class, target
            // or metadata inspection of that token precedes its owner's adoption.
            call.BeginPublication(result);
            try { publish(result); call.PublicationAccepted(); }
            catch { call.PublicationFailed(); throw; }
        }
        catch (Exception error) { call.OperationFailed(error); throw; }
        finally { call.Dispose(); }
    }

    private IntPtr VerifyExactReadOwner(RootHandle? owner, NativeExactReadField field, bool isStatic)
    {
        ArgumentNullException.ThrowIfNull(field);
        IntPtr value = IntPtr.Zero;
        if (isStatic)
        {
            if (owner is not null) throw new UnsupportedSignatureException("A static read must not substitute an instance receiver.");
        }
        else { ArgumentNullException.ThrowIfNull(owner); value = owner.Target; }
        var current = InspectExactDeclaredReadField(field.DeclaringClass, field.Name, field.TypeName, isStatic, field.Offset);
        if (current != field)
            throw new UnsupportedSignatureException("Saved exact read field differs from current official metadata.");
        if (isStatic)
        {
            return IntPtr.Zero;
        }
        var actual = _objectClass(value);
        if (actual == IntPtr.Zero || !_assignable(field.DeclaringClass, actual) || Field(actual, field.Name) != field.Field)
            throw new UnsupportedSignatureException("The rooted field owner is foreign or shadows the official declared field.");
        return value;
    }

    private T ReadGraphExport<T>(string name) where T : Delegate
    {
        try { return Api<T>(name); }
        catch (EntryPointNotFoundException error)
        { throw new UnsupportedSignatureException("Read-only graph observation requires official " + name + ": " + error.Message); }
    }
}
