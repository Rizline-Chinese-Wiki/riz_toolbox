using System.Runtime.InteropServices;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Runtime;

/// <summary>One actual UnityEvent&lt;Single&gt;.Invoke observation. RawBits keeps
/// every float payload; Parent linkage and listener effects are separate work.</summary>
internal readonly record struct NativeTweenSingleEventInvocation(IntPtr Event, uint ValueBits,
    IntPtr Method, IntPtr Caller, ulong Sequence)
{
    internal float Value => BitConverter.Int32BitsToSingle(unchecked((int)ValueBits));
}

internal sealed partial class NativeRuntime
{
    /// <summary>Validates the exact closed UnityEvent&lt;Single&gt;.Invoke declaration
    /// at the fixed Tween call target for the native Single1 observation lane.
    /// Resolving metadata does not itself install or execute a hook.</summary>
    internal VerifiedHookTarget ResolveObservedInstanceSingle1(IntPtr module, IntPtr actualEventClass, int rva)
    {
        VerifyAccess();
        if (module == IntPtr.Zero || actualEventClass == IntPtr.Zero || rva != 0xbc0ff0)
            throw new UnsupportedSignatureException("Single-event observation requires the fixed UnityEvent<float>.Invoke RVA.");
        var eventBase = Class("UnityEngine.CoreModule.dll", "UnityEngine.Events", "UnityEventBase");
        var single = Class("mscorlib.dll", "System", "Single");
        var voidClass = Class("mscorlib.dll", "System", "Void");
        var classImage = Api<Ptr1>("il2cpp_class_get_image");
        var generic = Api<Predicate>("il2cpp_class_is_generic");
        var inflated = Api<Predicate>("il2cpp_class_is_inflated");
        var unityImage = classImage(eventBase);
        var type = _classType(actualEventClass);
        if (type == IntPtr.Zero || _typeClass(type) != actualEventClass || _byRef(type) || _typeCode(type) != 21
            || _isValueType(actualEventClass) || generic(actualEventClass) || !inflated(actualEventClass)
            || classImage(actualEventClass) != unityImage || classImage(single) == unityImage
            || _classParent(actualEventClass) != eventBase
            || Utf8(_className(actualEventClass)) != "UnityEvent`1"
            || Utf8(_classNamespace(actualEventClass)) != "UnityEngine.Events"
            || MetadataTypeName(type) != "UnityEngine.Events.UnityEvent<System.Single>")
            throw new UnsupportedSignatureException("The actual event is not the exact closed UnityEngine.Events.UnityEvent<System.Single> class.");
        var singleType = _classType(single);
        if (singleType == IntPtr.Zero || _typeClass(singleType) != single || _byRef(singleType) || _typeCode(singleType) != 12
            || !_isValueType(single) || _isEnum(single) || _valueSize(single, out _) != sizeof(float)
            || MetadataTypeName(singleType) != "System.Single")
            throw new UnsupportedSignatureException("UnityEvent<float>.Invoke requires the exact official System.Single argument.");

        IntPtr found = IntPtr.Zero; var terminated = false;
        using (var iterator = Arg.Value(IntPtr.Zero))
            for (var count = 0; count < 256; ++count)
            {
                var method = _methods(actualEventClass, iterator.Pointer);
                if (method == IntPtr.Zero) { terminated = true; break; }
                if (_methodClass(method) != actualEventClass) continue;
                if (Utf8(_methodName(method)) != "Invoke") continue;
                if (found != IntPtr.Zero)
                    throw new UnsupportedSignatureException("UnityEvent<Single> has ambiguous declared Invoke metadata.");
                found = method;
            }
        if (!terminated || found == IntPtr.Zero)
            throw new MissingMethodException("UnityEvent<Single>.Invoke");
        var returns = _returnType(found); var parameterCount = _parameterCount(found);
        var parameter = parameterCount == 1 ? _parameterType(found, 0) : IntPtr.Zero;
        var flags = _methodFlags(found, out var implementation);
        if ((flags & (0x10u | 0x400u)) != 0 || _isGenericMethod(found) || parameterCount != 1
            || returns == IntPtr.Zero || _byRef(returns) || _typeCode(returns) != 1 || _typeClass(returns) != voidClass
            || parameter == IntPtr.Zero || _byRef(parameter) || _typeCode(parameter) != 12 || _typeClass(parameter) != single
            || Describe(returns) is not { Kind: NativeValueKind.Void, ByRef: false }
            || Describe(parameter) is not { Kind: NativeValueKind.Single, Size: 4, ByRef: false })
            throw new UnsupportedSignatureException("UnityEvent<Single>.Invoke differs from its exact instance void(Single) ABI.");
        var expected = IntPtr.Add(module, rva);
        HookTargetValidation.VerifyReadable(found, (nuint)Marshal.SizeOf<HookTargetValidation.MethodInfo2022>());
        var layout = Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(found);
        HookTargetValidation.ValidateMetadata(layout, _methodName(found), _methodClass(found), returns, flags,
            parameterCount, expected);
        if (layout.ImplementationFlags != (ushort)implementation)
            throw new UnsupportedSignatureException("UnityEvent<Single>.Invoke implementation flags changed.");
        HookTargetValidation.VerifyExecutable(expected, module);
        // Re-read the complete observed class/method shape after the fixed entry
        // lookup so a concurrent metadata writer cannot earn a stale target.
        if (_classType(actualEventClass) != type || _typeClass(type) != actualEventClass
            || generic(actualEventClass) || !inflated(actualEventClass) || classImage(actualEventClass) != unityImage
            || _classParent(actualEventClass) != eventBase || Utf8(_className(actualEventClass)) != "UnityEvent`1"
            || Utf8(_classNamespace(actualEventClass)) != "UnityEngine.Events"
            || MetadataTypeName(type) != "UnityEngine.Events.UnityEvent<System.Single>"
            || _classType(single) != singleType || _typeClass(singleType) != single
            || _methodClass(found) != actualEventClass || Utf8(_methodName(found)) != "Invoke"
            || _returnType(found) != returns || _parameterCount(found) != 1 || _parameterType(found, 0) != parameter
            || _methodFlags(found, out implementation) != flags || implementation != layout.ImplementationFlags)
            throw new UnsupportedSignatureException("UnityEvent<Single>.Invoke metadata changed across its target admission.");
        return new("local.unityevent.single.invoke", expected, found, actualEventClass, ExactHookAbi.OperationSingle1Void);
    }

    /// <summary>Validates one already-rooted operation frame at the five fixed Tween
    /// onTween call returns. The caller must retain eventRoot independently.</summary>
    internal NativeTweenSingleEventInvocation ObserveRootedSingleEventInvocation(IntPtr module,
        RootHandle eventRoot, VerifiedHookTarget target, NativeOperationFrame frame, NativeTweenOperation parentOperation)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(eventRoot); ArgumentNullException.ThrowIfNull(target);
        if (!frame.IsValid || frame.Kind != NativeOperationKind.Single1 || frame.Arg1 != IntPtr.Zero)
            throw new UnsupportedSignatureException("Single-event invocation requires one valid Single1 operation frame.");
        var actual = eventRoot.Target;
        if (actual == IntPtr.Zero || frame.Self != actual || frame.Method != target.MethodInfo
            || target.Abi != ExactHookAbi.OperationSingle1Void || target.DeclaringClass == IntPtr.Zero
            || target.Address.ToInt64() < module.ToInt64())
            throw new UnsupportedSignatureException("Single-event frame does not match its independently rooted event target.");
        var rva = checked((int)(target.Address.ToInt64() - module.ToInt64()));
        var verified = ResolveObservedInstanceSingle1(module, target.DeclaringClass, rva);
        if (verified != target) throw new UnsupportedSignatureException("Single-event target is not the current exact admitted declaration.");
        if (_objectClass(actual) != target.DeclaringClass || eventRoot.Target != actual)
            throw new UnsupportedSignatureException("Single-event root changed its exact closed event class.");
        // The hardware frame carries an absolute return PC. These are the five
        // E8 rel32 CALLs in the pinned PE, so the return is module + CALL RVA + 5.
        var callerRva = checked(frame.Caller.ToInt64() - module.ToInt64());
        var callRva = callerRva switch {
            0x395c07 or 0x3962ab when parentOperation == NativeTweenOperation.Play => checked((int)callerRva - 5),
            0x397f8a or 0x397fbf or 0x398026 when parentOperation == NativeTweenOperation.ExecuteTween => checked((int)callerRva - 5),
            _ => -1
        };
        if (callRva < 0)
            throw new UnsupportedSignatureException("Single-event frame has a foreign Tween caller return PC.");
        var call = IntPtr.Add(module, callRva);
        HookTargetValidation.VerifyReadable(call, 5);
        HookTargetValidation.VerifyExecutable(call, module);
        if (Marshal.ReadByte(call) != 0xe8
            || checked(call.ToInt64() + 5L + Marshal.ReadInt32(call, 1)) != target.Address.ToInt64())
            throw new UnsupportedSignatureException("Single-event caller no longer contains its exact direct CALL to UnityEvent<Single>.Invoke.");
        if (eventRoot.Target != actual || _objectClass(actual) != target.DeclaringClass)
            throw new UnsupportedSignatureException("Single-event root or class changed across its admission.");
        return new(actual, frame.ScalarBits, frame.Method, frame.Caller, frame.Sequence);
    }
}
