using System.Runtime.InteropServices;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Runtime;

internal sealed record NativeMetadataClass(IntPtr Class, string TypeName);
internal sealed record NativeMetadataField(IntPtr Field, IntPtr Type, NativeMetadataClass FieldClass, string Name);
internal sealed record NativeMetadataEntry(IntPtr Address, long Rva, ushort MethodInfoSlot);
internal sealed record NativeMetadataMethod(IntPtr MethodInfo, NativeMetadataClass DeclaringClass, string Name,
    string ReturnType, IReadOnlyList<string> ParameterTypes, uint Flags, uint ImplementationFlags,
    bool GenericMethod, bool InflatedMethod, NativeMetadataEntry? Entry, string EntryStatus);
internal sealed record NativeMetadataReference(NativeMetadataField Field, NativeMetadataClass? ObjectClass,
    NativeMetadataMethod? InterfaceMethod, NativeMetadataMethod? ResolvedMethod, string ResolutionStatus);

/// <summary>Optional metadata evidence only. Never constructs a native object, invokes a game method, or installs a hook.</summary>
internal sealed partial class NativeRuntime
{
    internal NativeMetadataClass InspectMetadataClass(IntPtr cls)
    {
        VerifyAccess();
        if (cls == IntPtr.Zero) throw new UnsupportedSignatureException("Metadata class is absent.");
        return new(cls, MetadataTypeName(_classType(cls)));
    }

    /// <summary>The closed class comes from the exact field's exported Il2CppType, never a constructed generic name.</summary>
    internal NativeMetadataField InspectMetadataFieldClass(IntPtr declaringClass, string name, string expectedType)
    {
        VerifyAccess();
        var field = Field(declaringClass, name); var type = _fieldType(field);
        if ((_fieldFlags(field) & 0x10) != 0 || _byRef(type) || MetadataTypeName(type) != expectedType)
            throw new UnsupportedSignatureException($"Metadata field {name} differs from the required instance reference type {expectedType}.");
        var cls = _typeClass(type); var description = InspectMetadataClass(cls);
        if (_isValueType(cls) || description.TypeName != expectedType)
            throw new UnsupportedSignatureException("Field-to-class metadata round trip differs from the exact closed reference type.");
        return new(field, type, description, name);
    }

    internal NativeMetadataMethod InspectDeclaredMethod(IntPtr cls, string name, string[] parameters,
        string expectedReturnType, IntPtr gameAssemblyModule)
    {
        VerifyAccess();
        var method = FindCurrentDeclaredMetadataMethod(cls, name, parameters, false);
        if (_methodClass(method) != cls || MetadataTypeName(_returnType(method)) != expectedReturnType)
            throw new UnsupportedSignatureException("Method metadata differs from the exact declaring class or return type.");
        return InspectMetadataMethod(method, gameAssemblyModule);
    }

    /// <summary>Exact static counterpart used for non-generic Unity construction APIs.</summary>
    internal NativeMetadataMethod InspectDeclaredStaticMethod(IntPtr cls, string name, string[] parameters,
        string expectedReturnType, IntPtr gameAssemblyModule)
    {
        VerifyAccess();
        var method = FindCurrentDeclaredMetadataMethod(cls, name, parameters, true);
        if (_methodClass(method) != cls || (_methodFlags(method, out _) & 0x10) == 0
            || _isGenericMethod(method) || _isInflatedMethod(method)
            || MetadataTypeName(_returnType(method)) != expectedReturnType)
            throw new UnsupportedSignatureException("Static method metadata differs from its exact non-generic declaring class or return type.");
        return InspectMetadataMethod(method, gameAssemblyModule);
    }

    private IntPtr FindCurrentDeclaredMetadataMethod(IntPtr cls, string name, string[] parameters, bool isStatic)
    {
        if (cls == IntPtr.Zero) throw new TargetExpiredException("Method declaring class is null.");
        if (parameters.Length > 128) throw new ArgumentException("Invalid parameter signature.");
        // Declaration evidence must be freshly enumerated even when ordinary
        // invocation previously cached this signature. A retained MethodInfo
        // cannot prove that it is still the class's unique current declaration.
        using var iterator = Arg.Value(IntPtr.Zero);
        var match = IntPtr.Zero;
        while (true)
        {
            var method = _methods(cls, iterator.Pointer);
            if (method == IntPtr.Zero) break;
            if (Utf8(_methodName(method)) != name || _parameterCount(method) != (uint)parameters.Length
                || ((_methodFlags(method, out _) & 0x10) != 0) != isStatic
                || Enumerable.Range(0, parameters.Length).Any(i => ParameterName(method, (uint)i) != parameters[i])) continue;
            if (match != IntPtr.Zero)
                throw new UnsupportedSignatureException("Method metadata has more than one exact current declaration: " + name);
            match = method;
        }
        if (match == IntPtr.Zero) throw new MissingMethodException($"{Utf8(_className(cls))}.{name}/{parameters.Length}");
        return match;
    }

    private NativeMetadataMethod InspectMetadataMethod(IntPtr method, IntPtr gameAssemblyModule)
    {
        if (method == IntPtr.Zero) throw new MissingMethodException("Method metadata is absent.");
        var count = _parameterCount(method);
        if (count > 128) throw new UnsupportedSignatureException("Method parameter metadata exceeds the diagnostic limit.");
        var flags = _methodFlags(method, out var implementationFlags);
        var parameters = Enumerable.Range(0, (int)count).Select(i => ParameterName(method, (uint)i)).ToArray();
        NativeMetadataEntry? entry = null; var entryStatus = "Unverified: no pinned executable entry was established.";
        try
        {
            if (gameAssemblyModule == IntPtr.Zero) throw new UnsupportedSignatureException("GameAssembly module is absent.");
            HookTargetValidation.VerifyReadable(method, (nuint)Marshal.SizeOf<HookTargetValidation.MethodInfo2022>());
            var layout = Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(method);
            // Reuse the project's pinned Unity 2022 layout. The name/class/return/flags/count are
            // cross-checked against exports; a nonzero entry must also belong to this module's executable image.
            HookTargetValidation.ValidateMetadata(layout, _methodName(method), _methodClass(method), _returnType(method),
                flags, count, layout.MethodPointer);
            if (layout.ImplementationFlags != (ushort)implementationFlags)
                throw new UnsupportedSignatureException("MethodInfo implementation flags differ from exported metadata.");
            HookTargetValidation.VerifyExecutable(layout.MethodPointer, gameAssemblyModule);
            entry = new(layout.MethodPointer, layout.MethodPointer.ToInt64() - gameAssemblyModule.ToInt64(), layout.Slot);
            entryStatus = "Verified: existing MethodInfo2022 layout, six exported metadata fields, executable GameAssembly range. MethodInfoSlot is not a vtable/interface-offset-table walk.";
        }
        catch (Exception error) when (error is UnsupportedSignatureException or PlatformNotSupportedException)
        {
            entryStatus = "Unverified: " + error.Message;
        }
        return new(method, InspectMetadataClass(_methodClass(method)), Utf8(_methodName(method)),
            MetadataTypeName(_returnType(method)), Array.AsReadOnly(parameters), flags, implementationFlags,
            _isGenericMethod(method), _isInflatedMethod(method), entry, entryStatus);
    }

    /// <summary>Caller must already own the root. Reads only the named reference and resolves its existing interface method.</summary>
    internal NativeMetadataReference InspectExistingReference(NativeRuntime.RootHandle owner, IntPtr exactOwnerClass,
        string fieldName, string expectedFieldType, IntPtr gameAssemblyModule)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(owner);
        var source = owner.Target;
        if (_objectClass(source) != exactOwnerClass)
            throw new UnsupportedSignatureException("Diagnostic owner differs from its exact metadata class.");
        var field = InspectMetadataFieldClass(exactOwnerClass, fieldName, expectedFieldType);
        var value = ReadField<IntPtr>(source, field.Field, false);
        if (value == IntPtr.Zero) return new(field, null, null, null, "Unknown: the existing owner field is null.");
        using var retained = Root(value); var objectClass = InspectMetadataClass(_objectClass(retained.Target));
        if (!_assignable(field.FieldClass.Class, objectClass.Class))
            throw new UnsupportedSignatureException("Existing reference is not assignable to its exact closed interface.");
        var interfaceMethod = InspectDeclaredMethod(field.FieldClass.Class, "Release", Array.Empty<string>(), "System.Void", gameAssemblyModule);
        if ((interfaceMethod.Flags & 0x40) == 0)
            throw new UnsupportedSignatureException("The closed interface Release metadata is not virtual.");
        Ptr2 resolve;
        try { resolve = Api<Ptr2>("il2cpp_object_get_virtual_method"); }
        catch (EntryPointNotFoundException)
        {
            return new(field, objectClass, interfaceMethod, null, "Unknown: optional il2cpp_object_get_virtual_method export is absent.");
        }
        var resolved = InspectMetadataMethod(resolve(retained.Target, interfaceMethod.MethodInfo), gameAssemblyModule);
        if (resolved.Name != "Release" || resolved.ReturnType != "System.Void" || resolved.ParameterTypes.Count != 0
            || (resolved.Flags & (0x10 | 0x400)) != 0 || resolved.MethodInfo == interfaceMethod.MethodInfo
            || !_assignable(resolved.DeclaringClass.Class, objectClass.Class))
            throw new UnsupportedSignatureException("Virtual resolver returned an incompatible Release signature or declaring class.");
        return new(field, objectClass, interfaceMethod, resolved,
            "ResolvedMethodInfo: official object_get_virtual_method on an existing rooted object; no Release invocation and no vtable memory walk.");
    }

}
