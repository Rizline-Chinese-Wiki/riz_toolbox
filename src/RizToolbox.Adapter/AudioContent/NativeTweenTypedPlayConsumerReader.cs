using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.AudioContent;

// Rooted metadata projection only. The work owner separately issues and validates a private
// transfer from its exact current child and actual committed source-journal occurrences.
internal static class NativeTweenTypedPlayConsumerReader
{
    internal static NativeTweenTypedPlayConsumer? Read(NativeRuntime api, IntPtr module,
        NativeTweenSingleEventFrame frame, NativeSingleDispatchTarget target, VerifiedHookTarget play,
        IntPtr tweenClass, NativeExactReadField delegates, NativeExactReadField delegateTarget,
        Func<long, NativeRuntime.RootHandle> Root, Func<long, NativeExactReadField, bool, long> readReference,
        Func<long, bool> ownsTween, Action RequireKnown)
    {
        T Native<T>(Func<T> read) { RequireKnown(); var value = read(); RequireKnown(); return value; }
        IntPtr Pointer(long identity) => Native(() => Root(identity).Target);
        IntPtr ObjectClass(IntPtr pointer) => Native(() => api.ObjectClass(pointer));
        long Reference(long identity, NativeExactReadField field, bool nullable) => readReference(identity, field, nullable);
        RequireKnown();
        if (frame.Site != 1 || target.Site != 1 || target.Invokable != frame.Invokable.ToInt64()
            || target.OriginalValueBits != frame.ValueBits || target.EffectiveSingleBits is not null || target.CachedSingle
            || frame.Stage is not (NativeTweenSingleEventStage.Before or NativeTweenSingleEventStage.Returned)
            || frame.Flags != 0) return null;
        var invokable = Native(() => api.Class("UnityEngine.CoreModule.dll", "UnityEngine.Events", "InvokableCall"));
        var actionClass = Native(() => api.Class("UnityEngine.CoreModule.dll", "UnityEngine.Events", "UnityAction"));
        var root = Root(target.Invokable);
        if (root.Target != frame.Invokable || ObjectClass(root.Target) != invokable) return null;
        var invoke = Native(() => api.InspectDeclaredMethod(invokable, "Invoke", Array.Empty<string>(), "System.Void", module));
        if (invoke.Entry?.Rva != 0x154e5b0 || invoke.MethodInfo.ToInt64() != target.Method
            || target.Target != module.ToInt64() + 0x154e5b0 || frame.ActualTarget.ToInt64() != target.Target) return null;
        var field = Native(() => api.InspectExactDeclaredReadField(invokable, "Delegate", "UnityEngine.Events.UnityAction", false, 0x10));
        if (field.FieldClass != actionClass) return null;
        // Reference publishes and independently roots before any target metadata read.
        var action = Reference(target.Invokable, field, nullable: true);
        if (action == 0 || ObjectClass(Pointer(action)) != actionClass || Reference(action, delegates, nullable: true) != 0) return null;
        var tween = Reference(action, delegateTarget, nullable: true);
        if (tween == 0 || !ownsTween(tween) || ObjectClass(Pointer(tween)) != tweenClass) return null;
        NativeRootedDelegateMethod projection;
        try { projection = Native(() => api.InspectRootedDelegateMethod(Root(action), actionClass, module)); }
        catch (UnsupportedSignatureException) { RequireKnown(); return null; }
        var method = projection.Method;
        // The pinned UnityAction/Action ctor at 0x377890 writes this closed instance
        // shape (0x3778e2..0x3778ee), also used by the existing coroutine consumers.
        // Support the direct closed instance delegate only. Open/adapted/thunked, virtual,
        // shared and multicast delegates do not inherit Play's complete effect contract.
        if (projection.Multicast || projection.Virtual || method.GenericMethod || method.InflatedMethod
            || projection.Target.ToInt64() != tween || method.MethodInfo != play.MethodInfo
            || method.DeclaringClass.Class != tweenClass || method.Name != "Play"
            || method.ReturnType != "System.Void" || method.ParameterTypes.Count != 0
            || (method.Flags & (0x10 | 0x40 | 0x400)) != 0 || method.Entry?.Address != play.Address
            || method.Entry.Rva != 0x3956f0 || projection.MethodPointer != play.Address
            || projection.InvokeImplementation != play.Address || projection.MethodCode.ToInt64() != tween) return null;
        if (Reference(target.Invokable, field, nullable: true) != action
            || Reference(action, delegates, nullable: true) != 0
            || Reference(action, delegateTarget, nullable: true) != tween)
            throw new UnsupportedSignatureException("The exact Single Play consumer delegate changed during its rooted read.");
        return new(target.Invokable, action, tween, method.MethodInfo.ToInt64(), play.Address.ToInt64(),
            projection.InvokeImplementation.ToInt64(), projection.MethodCode.ToInt64());
    }
}
