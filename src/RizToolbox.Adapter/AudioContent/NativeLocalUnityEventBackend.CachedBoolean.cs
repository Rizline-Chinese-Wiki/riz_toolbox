using RizToolbox.Runtime;

namespace RizToolbox.Adapter.AudioContent;

internal sealed partial class NativeLocalUnityEventBackend
{
    private NativeUnityEventInvokable ReadCachedBooleanInvokable(long item, IntPtr actual, ref int leafBudget)
    {
        NativeCachedBooleanInvokable before;
        try
        {
            before = Native(() => _api.InspectRootedCachedBooleanInvokable(_roots[_rootSlots[item]]!));
        }
        catch (UnsupportedSignatureException)
        {
            // Ordinary generic invokables and other cached argument types have
            // no fixed Boolean projection. Their dispatch remains opaque.
            RequireObservationKnown();
            return new(item, actual.ToInt64(), true, Array.Empty<NativeUnityEventLeaf>());
        }
        if (before.Object.ToInt64() != item || before.ActualClass != actual)
            throw new InvalidOperationException("Cached Boolean metadata differs from the actual rooted invokable.");
        var argument = new NativeUnityEventCachedBooleanArgument(before.ArgumentClass.ToInt64(), before.Argument,
            before.InvokableParentClass.ToInt64(), before.ActionClass.ToInt64(), before.DelegateField.Field.ToInt64(),
            before.ArgumentField.Field.ToInt64(), before.ActionInvokeMethod.ToInt64());
        // The projection's raw Delegate is diagnostic. Read the verified official
        // FieldInfo through publication and adopt before the result fault barrier.
        long action = 0;
        RequireObservationKnown();
        _api.ReadCachedBooleanDelegateWithPublication(_roots[_rootSlots[item]]!, before,
            pointer => action = Hold(pointer, before.ActionClass, nullable: true, published: true));
        RequireObservationKnown();
        var observed = ReadDelegateInvokable(item, actual, before.ActionClass, argument, ref leafBudget, action);
        if (observed.Delegate != before.Delegate.ToInt64())
            throw new InvalidOperationException("Cached Boolean delegate changed before its independent root was adopted.");
        var after = Native(() => _api.InspectRootedCachedBooleanInvokable(_roots[_rootSlots[item]]!));
        if (before != after)
            throw new InvalidOperationException("Cached Boolean class, argument or delegate changed during rooted capture.");
        return observed;
    }

    // This only classifies copied method metadata for a specific rooted shape.
    // It grants neither invocation authority nor an effects completion receipt.
    internal static bool SupportedListenerSignature(NativeMetadataMethod metadata, long target, bool isVirtual,
        IntPtr actualActionClass, IntPtr expectedActionClass, IntPtr cachedBooleanClass, Func<IntPtr>? readBooleanParameterClass)
    {
        if (target == 0 || isVirtual || expectedActionClass == IntPtr.Zero || actualActionClass != expectedActionClass
            || (metadata.Flags & (0x10 | 0x40 | 0x400)) != 0 || metadata.GenericMethod || metadata.InflatedMethod
            || metadata.Entry is null || metadata.ReturnType != "System.Void") return false;
        if (cachedBooleanClass == IntPtr.Zero) return metadata.ParameterTypes.Count == 0;
        return metadata.ParameterTypes.Count == 1 && metadata.ParameterTypes[0] == "System.Boolean"
            && readBooleanParameterClass is not null && readBooleanParameterClass() == cachedBooleanClass;
    }
}
