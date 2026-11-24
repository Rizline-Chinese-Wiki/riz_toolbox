namespace RizToolbox.Runtime;

internal sealed partial class NativeRuntime
{
    internal NativeRootedReferenceArray ReadRootedLineRingSourceArray(RootHandle array, IntPtr arrayClass,
        IntPtr elementClass, int maximum, Action<int, IntPtr> publish, Action guard)
    {
        ArgumentNullException.ThrowIfNull(guard); VerifyAccess(); guard();
        var line = LineRingObserve(guard, () => Class("Assembly-CSharp.dll", "InLevel", "LineInformation"));
        var group = LineRingObserve(guard, () => Class("Assembly-CSharp.dll", "InLevel", "LinesGroupInformation"));
        if (elementClass != line && elementClass != group)
            throw new UnsupportedSignatureException("Only exact line/ring source reference arrays are admitted.");
        return ReadRootedReferenceArrayCore(array, arrayClass, elementClass, maximum, 65536, publish, guard);
    }

    internal IntPtr InvokeReferenceWithPublicationGuarded(IntPtr method, IntPtr receiver,
        Action<IntPtr> publish, Action guard, params Arg[] args)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(guard); ArgumentNullException.ThrowIfNull(publish); guard();
        var call = new NativePublicationCall(VerifyAccess, args);
        try { return InvokeCore(method, receiver, publish, call, false, args, guard, guard, guard); }
        catch (Exception error) { call.OperationFailed(error); throw; }
        finally { call.Dispose(); }
    }

    /// <summary>Keep all preparation and post-invoke guards, but record a
    /// mutation attempt only after metadata, temporary roots and argument
    /// marshalling have succeeded. There is no native getter between this
    /// final managed recorder and runtime_invoke. A successful returned token
    /// enters publication responsibility before the post-invoke guard, so a
    /// withdrawal cannot silently discard the already allocated output.</summary>
    internal IntPtr InvokeReferenceWithPublicationAdmission(IntPtr method, IntPtr receiver,
        Action<IntPtr> publish, Action guard, Action beforeDispatch, params Arg[] args)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(publish);
        ArgumentNullException.ThrowIfNull(guard); ArgumentNullException.ThrowIfNull(beforeDispatch); guard();
        var call = new NativePublicationCall(VerifyAccess, args);
        try { return InvokeCore(method, receiver, publish, call, false, args,
            () => { guard(); beforeDispatch(); }, guard, guard, publishBeforePostGuard: true); }
        catch (Exception error) { call.OperationFailed(error); throw; }
        finally { call.Dispose(); }
    }
}
