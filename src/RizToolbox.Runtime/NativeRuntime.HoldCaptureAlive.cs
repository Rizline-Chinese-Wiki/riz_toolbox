namespace RizToolbox.Runtime;

internal sealed partial class NativeRuntime
{
    /// <summary>Fresh, bounded exact static metadata for the retained Hold graph's
    /// Unity truth-value observation. This does not attest native owner death,
    /// detach, scheduler completion, a module body, or engine Idle.</summary>
    internal IntPtr InspectHoldCaptureAliveMethod(IntPtr unityObjectClass, Action guard)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(guard); guard();
        T O<T>(Func<T> read) => LineRingObserve(guard, read);
        var canonical = LineEndpointClass("UnityEngine.CoreModule.dll", "UnityEngine", "Object", guard);
        var boolean = LineEndpointClass("mscorlib.dll", "System", "Boolean", guard);
        if (unityObjectClass == IntPtr.Zero || unityObjectClass != canonical)
            throw new UnsupportedSignatureException("Hold liveness requires the canonical UnityEngine.Object declaration.");
        var found = IntPtr.Zero; var terminated = false;
        using (var iterator = Arg.Value(IntPtr.Zero))
            for (var index = 0; index < 4096; ++index)
            {
                var method = O(() => _methods(canonical, iterator.Pointer));
                if (method == IntPtr.Zero) { terminated = true; break; }
                if (Utf8(O(() => _methodName(method))) != "op_Implicit"
                    || O(() => _parameterCount(method)) != 1) continue;
                uint ignored = 0;
                var flags = O(() => _methodFlags(method, out ignored));
                if ((flags & (0x10u | 0x400u)) != 0x10u) continue;
                // Describe() caches layouts. This fresh admission must check
                // the official type again even after an earlier warm read.
                var parameter = O(() => _parameterType(method, 0));
                if (parameter == IntPtr.Zero || O(() => _typeClass(parameter)) != canonical
                    || O(() => _byRef(parameter)) || O(() => _typeCode(parameter)) != 18
                    || O(() => _isValueType(canonical))
                    || LineRingTypeName(parameter, guard) != "UnityEngine.Object") continue;
                if (found != IntPtr.Zero)
                    throw new UnsupportedSignatureException("The physical Hold Unity truth-value operator is ambiguous.");
                found = method;
            }
        if (!terminated || found == IntPtr.Zero || O(() => _methodClass(found)) != canonical)
            throw new UnsupportedSignatureException("The exact declared physical Hold Unity truth-value operator is absent.");
        var generic = O(() => _isGenericMethod(found)); var inflated = O(() => _isInflatedMethod(found));
        if (generic || inflated)
            throw new UnsupportedSignatureException("Physical Hold liveness does not use a generic method instance.");
        var returns = O(() => _returnType(found));
        if (returns == IntPtr.Zero || O(() => _typeClass(returns)) != boolean
            || O(() => _byRef(returns)) || O(() => _typeCode(returns)) != 2
            || !O(() => _isValueType(boolean)) || O(() => _isEnum(boolean))
            || O(() => _valueSize(boolean, out _)) != 1 || LineRingTypeName(returns, guard) != "System.Boolean")
            throw new UnsupportedSignatureException("The physical Hold Unity truth-value operator has another exact return ABI.");
        guard(); return found;
    }

    /// <summary>Keep the original receiver root caller-owned. Method selection,
    /// argument preparation, native invocation and boxed Boolean consumption
    /// use the same source guard. Only acquired temporary roots need cleanup
    /// after withdrawal; this read never establishes lifecycle terminal state.</summary>
    internal bool ReadHoldCaptureAlive(RootHandle receiver, IntPtr unityObjectClass, Action guard)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(receiver); ArgumentNullException.ThrowIfNull(guard);
        var method = InspectHoldCaptureAliveMethod(unityObjectClass, guard);
        var target = LineRingObserve(guard, () => receiver.Target);
        guard();
        return InvokeValueWithPublicationGuarded<bool>(method, IntPtr.Zero, guard, Arg.Ref(target));
    }
}
