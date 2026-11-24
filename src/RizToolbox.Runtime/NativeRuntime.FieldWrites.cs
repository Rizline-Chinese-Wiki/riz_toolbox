using System.Runtime.InteropServices;

namespace RizToolbox.Runtime;

internal sealed partial class NativeRuntime
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FieldSetObject(IntPtr instance, IntPtr field, IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FieldSetValue(IntPtr instance, IntPtr field, IntPtr valueBuffer);

    private Ptr1? _writeFieldParent;
    private FieldSetObject? _writeFieldObject;
    private FieldSetValue? _writeFieldValue;

    /// <summary>
    /// Writes one exact mutable instance field through official IL2CPP exports. After thread
    /// admission consumes the argument but never the caller's owner root. Reference writes pass Il2CppObject* directly;
    /// only value writes pass a native value buffer.
    /// </summary>
    internal void WriteExact(RootHandle owner, IntPtr exactOwnerClass, string fieldName,
        string expectedFieldType, Arg value)
        => WriteExactCore(owner, exactOwnerClass, fieldName, expectedFieldType, value, null);

    /// <summary>Resolves every metadata/export/receiver lookup before the caller's
    /// pure managed final guard. No native getter occurs between that guard and
    /// setter dispatch. An already dispatched setter cannot be cancelled.
    /// Temporary reference roots and owned arguments remain process-owned if
    /// cleanup is unknown. Thread/capacity rejection leaves inputs caller-owned.</summary>
    internal void WriteExactGuarded(RootHandle owner, IntPtr exactOwnerClass, string fieldName,
        string expectedFieldType, Arg value, Action beforeDispatch)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(beforeDispatch);
        var call = new NativePublicationCall(VerifyAccess, new[] { value });
        try { WriteExactCore(owner, exactOwnerClass, fieldName, expectedFieldType, value, beforeDispatch, call); }
        catch (Exception error) { call.OperationFailed(error); throw; }
        finally { call.Dispose(); }
    }

    private void WriteExactCore(RootHandle owner, IntPtr exactOwnerClass, string fieldName,
        string expectedFieldType, Arg value, Action? beforeDispatch, NativePublicationCall? publication = null)
    {
        // Reject before taking responsibility for an argument whose attached native root
        // may only be freed by its owning thread. The caller can release it on that thread.
        VerifyAccess();
        RootHandle? reference = null;
        try
        {
            ArgumentNullException.ThrowIfNull(owner); ArgumentNullException.ThrowIfNull(value);
            value.VerifyActive();
            // Root the incoming object before metadata decoding or lazy export/delegate
            // resolution can allocate. Null is a valid reference assignment.
            if (value.IsReference && value.Pointer != IntPtr.Zero)
                reference = publication is null ? Root(value.Pointer) : publication.Root(() => Root(value.Pointer));
            if (exactOwnerClass == IntPtr.Zero || _objectClass(owner.Target) != exactOwnerClass)
                throw new UnsupportedSignatureException("Field-write owner differs from the exact required class.");
            if (string.IsNullOrWhiteSpace(fieldName) || string.IsNullOrWhiteSpace(expectedFieldType))
                throw new UnsupportedSignatureException("Exact field writes require a named field and exact expected type.");

            var field = Field(exactOwnerClass, fieldName);
            _writeFieldParent ??= OptionalFieldWriteExport<Ptr1>("il2cpp_field_get_parent");
            if (_writeFieldParent(field) != exactOwnerClass)
                throw new UnsupportedSignatureException("The field is not declared by the exact owner class.");
            // FieldAttributes.Static | InitOnly | Literal. Mutating inherited or readonly
            // storage is deliberately outside this exact factory bridge.
            if ((_fieldFlags(field) & (0x10u | 0x20u | 0x40u)) != 0)
                throw new UnsupportedSignatureException("Exact field writes require a mutable instance field.");
            var type = _fieldType(field);
            if (type == IntPtr.Zero || _byRef(type) || MetadataTypeName(type) != expectedFieldType)
                throw new UnsupportedSignatureException("Field-write metadata differs from the required exact non-byref type.");
            var layout = Describe(type);
            NativeAbi.ValidateArgument(layout, value);

            if (value.IsReference)
            {
                var target = reference?.Target ?? IntPtr.Zero;
                if (target != IntPtr.Zero) RequireAssignable(layout.Class, _objectClass(target));
                var setter = _writeFieldObject ??= OptionalFieldWriteExport<FieldSetObject>("il2cpp_field_set_value_object");
                var receiver = owner.Target;
                beforeDispatch?.Invoke();
                setter(receiver, field, target);
            }
            else
            {
                var setter = _writeFieldValue ??= OptionalFieldWriteExport<FieldSetValue>("il2cpp_field_set_value");
                var buffer = value.Pointer;
                var receiver = owner.Target;
                beforeDispatch?.Invoke();
                setter(receiver, field, buffer);
            }
        }
        finally
        {
            if (publication is null)
            {
                try { reference?.Dispose(); }
                finally { value?.Dispose(); }
            }
        }
    }

    /// <summary>Returns the exact parameter's exported class, including an already-closed generic class.</summary>
    internal NativeMetadataClass InspectMethodParameterClass(IntPtr method, uint index, string expectedType)
    {
        VerifyAccess();
        if (method == IntPtr.Zero) throw new MissingMethodException("Method parameter metadata is absent.");
        var count = _parameterCount(method);
        if (count > 128 || index >= count || string.IsNullOrWhiteSpace(expectedType))
            throw new UnsupportedSignatureException("Exact parameter metadata requires a valid bounded index and expected type.");
        var type = _parameterType(method, index);
        if (type == IntPtr.Zero || _byRef(type) || MetadataTypeName(type) != expectedType)
            throw new UnsupportedSignatureException("Method parameter differs from the exact required non-byref type.");
        var description = InspectMetadataClass(_typeClass(type));
        if (description.TypeName != expectedType)
            throw new UnsupportedSignatureException("Method parameter class/type metadata round trip changed its exact type.");
        return description;
    }

    internal void RequireAssignable(IntPtr targetClass, IntPtr sourceClass)
    {
        VerifyAccess();
        if (targetClass == IntPtr.Zero || sourceClass == IntPtr.Zero || !_assignable(targetClass, sourceClass))
            throw new UnsupportedSignatureException("The source class is not assignable to the required target class.");
    }

    private T OptionalFieldWriteExport<T>(string name) where T : Delegate
    {
        try { return Api<T>(name); }
        catch (EntryPointNotFoundException error)
        {
            throw new UnsupportedSignatureException($"Exact field writing requires the optional official export {name}: {error.Message}");
        }
    }
}
