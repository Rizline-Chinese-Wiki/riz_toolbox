namespace RizToolbox.Runtime;

internal sealed partial class NativeRuntime
{
    /// <summary>Exact void invocation with the bounded temporary-root/argument responsibility
    /// used by publication calls. There is no returned object to publish. Native mutation may
    /// already have happened when an exception is thrown; callers must never blindly retry.
    /// Thread/capacity rejection occurs before input adoption and leaves inputs caller-owned.</summary>
    internal void InvokeVoidWithPublication(IntPtr method, IntPtr receiver, params Arg[] args)
        => InvokeVoidWithPublicationCore(method, receiver, null, args);

    /// <summary>Runs a pure managed final guard after metadata validation,
    /// temporary rooting and argument marshalling, directly before runtime_invoke.
    /// A throwing guard prevents dispatch; a dispatched call cannot be cancelled.
    /// Temporary and owned-input cleanup retains the same publication responsibility.</summary>
    internal void InvokeVoidWithPublicationGuarded(IntPtr method, IntPtr receiver, Action beforeDispatch, params Arg[] args)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(beforeDispatch);
        InvokeVoidWithPublicationCore(method, receiver, beforeDispatch, args);
    }

    /// <summary>Separates source admission from the once-only attempt recorder.
    /// The post-invoke guard also precedes exceptional-return metadata access.</summary>
    internal void InvokeVoidWithPublicationAdmission(IntPtr method, IntPtr receiver,
        Action guard, Action beforeDispatch, params Arg[] args)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(guard); ArgumentNullException.ThrowIfNull(beforeDispatch);
        guard();
        InvokeVoidWithPublicationCore(method, receiver, () => { guard(); beforeDispatch(); guard(); }, args, guard);
    }

    private void InvokeVoidWithPublicationCore(IntPtr method, IntPtr receiver, Action? beforeDispatch, Arg[] args, Action? afterInvoke = null)
    {
        VerifyAccess();
        var call = new NativePublicationCall(VerifyAccess, args);
        try
        {
            if (method == IntPtr.Zero) throw new MissingMethodException("IL2CPP method pointer is null.");
            var returns = Describe(_returnType(method));
            if (returns.Kind != NativeValueKind.Void || returns.ByRef)
                throw new UnsupportedSignatureException("Void publication requires a non-byref System.Void return.");
            InvokeCore(method, receiver, null, call, false, args, beforeDispatch, afterInvoke);
        }
        catch (Exception error) { call.OperationFailed(error); throw; }
        finally { call.Dispose(); }
    }
}
