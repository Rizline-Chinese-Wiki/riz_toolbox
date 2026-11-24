using System.Runtime.InteropServices;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Runtime;

internal sealed partial class NativeRuntime
{
    internal VerifiedHookTarget ResolveExactHook(string id, IntPtr module, IntPtr declaringClass, string methodName,
        string[] parameters, int expectedRva, ExactHookAbi abi)
    {
        VerifyAccess();
        if (abi is ExactHookAbi.StaticTimelineModeVoid or ExactHookAbi.StaticTimelineInstantaneousHandle or ExactHookAbi.StaticTimelineEndRangeVoid
            or ExactHookAbi.LocalInitializationReference1 or ExactHookAbi.LocalProgressPlayBooleanVoid or ExactHookAbi.StaticTimelineTooltipVoid or ExactHookAbi.CoroutineStartReference1 or ExactHookAbi.CoroutineFactoryReference0 or ExactHookAbi.NativeVoid0
            or ExactHookAbi.OperationVoid0 or ExactHookAbi.OperationReference1Void or ExactHookAbi.OperationReference2Void
            or ExactHookAbi.OperationSingle1Void)
            throw new UnsupportedSignatureException("Timeline static ABIs require the sealed Timeline metadata resolver.");
        if (module == IntPtr.Zero || expectedRva <= 0 || !Enum.IsDefined(typeof(ExactHookAbi), abi)) throw new UnsupportedSignatureException("Validated module, RVA and supported ABI are required.");
        var method = Method(declaringClass, methodName, parameters.Length, false, parameters);
        if (_methodClass(method) != declaringClass || (_methodFlags(method, out _) & 0x10) != 0 || _isGenericMethod(method) || _isInflatedMethod(method))
            throw new UnsupportedSignatureException("Exact hook requires the declared non-generic instance method.");
        var returns=Describe(_returnType(method));
        if(abi==ExactHookAbi.InstanceReferenceReturn0)
        {
            if(parameters.Length!=0||returns.Kind!=NativeValueKind.Reference||returns.ByRef||returns.Class==IntPtr.Zero)
                throw new UnsupportedSignatureException("Reference-return hook requires exactly one verified object return and no arguments.");
        }
        else if (abi is ExactHookAbi.InstanceBooleanReturn0 or ExactHookAbi.PlaybackBooleanReturn0)
        {
            if (parameters.Length != 0 || returns.Kind != NativeValueKind.Boolean || returns.ByRef || returns.Size != 1)
                throw new UnsupportedSignatureException("Boolean-return hook requires a non-byref one-byte Boolean and no arguments.");
        }
        else if (returns.Kind != NativeValueKind.Void) throw new UnsupportedSignatureException("This exact hook ABI requires a void return.");
        if (abi == ExactHookAbi.InstanceVoid && parameters.Length != 0) throw new UnsupportedSignatureException("InstanceVoid ABI requires no explicit arguments.");
        if (abi == ExactHookAbi.InstanceBooleanVoid)
            HookTargetValidation.ValidateBooleanArgumentSignature(returns,
                Enumerable.Range(0, parameters.Length).Select(index => Describe(_parameterType(method, (uint)index))).ToArray());
        if (abi == ExactHookAbi.InstanceEnum32Void)
        {
            if (parameters.Length != 1) throw new UnsupportedSignatureException("Enum32 ABI requires exactly one signed Int32 enum argument.");
            var argument = Describe(_parameterType(method, 0));
            if (argument.ByRef || argument.Kind != NativeValueKind.Enum || argument.Size != 4 || argument.Underlying?.Kind != NativeValueKind.Int32)
                throw new UnsupportedSignatureException("Enum32 ABI is not the verified signed Int32 enum signature.");
        }
        if (abi is ExactHookAbi.InstanceEnum32SingleVoid or ExactHookAbi.InstanceInt32SingleVoid)
        {
            if (parameters.Length != 2) throw new UnsupportedSignatureException("Judge ABI requires exactly enum32 and Single.");
            var first = Describe(_parameterType(method, 0)); var second = Describe(_parameterType(method, 1));
            var validFirst = abi == ExactHookAbi.InstanceEnum32SingleVoid
                ? first.Kind == NativeValueKind.Enum && first.Underlying?.Kind == NativeValueKind.Int32
                : first.Kind == NativeValueKind.Int32;
            if (first.ByRef || !validFirst || first.Size != 4
                || second.ByRef || second.Kind != NativeValueKind.Single || second.Size != 4)
                throw new UnsupportedSignatureException("Judge ABI is not the verified signed Int32 enum and float signature.");
        }
        var expected = IntPtr.Add(module, expectedRva);
        HookTargetValidation.VerifyReadable(method, (nuint)Marshal.SizeOf<HookTargetValidation.MethodInfo2022>());
        var layout = Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(method);
        HookTargetValidation.ValidateMetadata(layout, _methodName(method), _methodClass(method), _returnType(method), _methodFlags(method, out _), _parameterCount(method), expected);
        HookTargetValidation.VerifyExecutable(expected, module);
        return new(id, expected, method, declaringClass, abi,abi==ExactHookAbi.InstanceReferenceReturn0?returns.Class:IntPtr.Zero);
    }

    /// <summary>The reference-return broker uses this for both original and replacement results before any managed transform runs.</summary>
    internal RootHandle RetainExactHookResult(VerifiedHookTarget target,IntPtr value)
    {
        VerifyAccess();RequireObject(value);
        if(target.Abi!=ExactHookAbi.InstanceReferenceReturn0||target.ReturnClass==IntPtr.Zero||_objectClass(value)!=target.ReturnClass)
            throw new UnsupportedSignatureException("Exact hook result differs from its declared reference class.");
        return Root(value);
    }

    internal bool ExactHookReceiverMatches(IntPtr instance, IntPtr declaringClass)
    {
        VerifyAccess();
        return instance != IntPtr.Zero && _assignable(declaringClass, _objectClass(instance));
    }

    internal RootHandle RetainExactObject(IntPtr value, IntPtr expectedClass)
    {
        VerifyAccess();
        // Caller registers are untrusted until their object header is readable
        // and matches independently resolved metadata. Never call an IL2CPP API
        // with an arbitrary saved register as its presumed object receiver.
        HookTargetValidation.VerifyReadable(value, 16);
        if (expectedClass == IntPtr.Zero || Marshal.ReadIntPtr(value) != expectedClass)
            throw new UnsupportedSignatureException("The captured object header differs from its exact source class.");
        return Root(value);
    }
    internal void RequireInstanceFieldLayout(IntPtr cls, string name, string typeName, int expectedOffset)
    {
        VerifyAccess(); var field = Field(cls, name);
        if ((_fieldFlags(field) & 0x10) != 0 || _fieldOffset(field) != expectedOffset || Describe(_fieldType(field)).Name != typeName)
            throw new UnsupportedSignatureException($"Field {name} differs from its pinned instance layout.");
    }
    internal void RequireStaticFieldLayout(IntPtr cls, string name, string typeName, int expectedOffset)
    {
        VerifyAccess(); var field = Field(cls, name);
        if ((_fieldFlags(field) & 0x10) == 0 || _fieldOffset(field) != expectedOffset || Describe(_fieldType(field)).Name != typeName)
            throw new UnsupportedSignatureException($"Field {name} differs from its pinned static layout.");
    }
    internal IntPtr NestedClass(IntPtr parent, string name)
    {
        VerifyAccess(); var nested = Api<Ptr2>("il2cpp_class_get_nested_types"); using var iterator = Arg.Value(IntPtr.Zero);
        for (var count = 0; count < 4096; ++count)
        {
            var cls = nested(parent, iterator.Pointer);
            if (cls == IntPtr.Zero) throw new MissingMemberException($"Nested class {name} was not found.");
            if (Utf8(_className(cls)) == name) return cls;
        }
        throw new UnsupportedSignatureException("Nested metadata exceeds its traversal limit.");
    }
}
