namespace RizToolbox.Runtime.Hooks;

internal readonly record struct NativeTweenSingleEventRootBatch(long Event, long List, long Invokable, long ArgumentArray)
{
    // Safety ordering only, not a native authority issuer. The caller must separately hold a
    // Bridge.Delivery/current physical parent. Adopt records every successful root immediately;
    // a later failed root never erases the preceding responsibility records.
    internal static NativeTweenSingleEventRootBatch Capture(NativeTweenSingleEventFrame frame, Func<IntPtr, long> adopt)
    {
        ArgumentNullException.ThrowIfNull(adopt);
        if (frame.Event == IntPtr.Zero || frame.List == IntPtr.Zero)
            throw new UnsupportedSignatureException("Actual Single event/list references are required before metadata inspection.");
        var receiver = adopt(frame.Event); var list = adopt(frame.List);
        var invokable = frame.Invokable != IntPtr.Zero ? adopt(frame.Invokable) : 0;
        var array = frame.Site == 2 && frame.ArgRdx != IntPtr.Zero ? adopt(frame.ArgRdx) : 0;
        if (receiver != frame.Event.ToInt64() || list != frame.List.ToInt64()
            || invokable != frame.Invokable.ToInt64() || frame.Site == 2 && array != frame.ArgRdx.ToInt64())
            throw new UnsupportedSignatureException("An actual Single input root changed identity before the full batch was acquired.");
        return new(receiver, list, invokable, array);
    }
}
