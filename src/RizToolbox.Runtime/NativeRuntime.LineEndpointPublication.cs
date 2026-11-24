namespace RizToolbox.Runtime;

internal sealed partial class NativeRuntime
{
    /// <summary>Endpoint setter publication with lifetime guards around every
    /// metadata/preparation export and one caller-controlled revalidation after
    /// temporary roots are held. Revalidation may read native endpoint state in
    /// the already held source scope; it is separate from the once-only attempt
    /// recorder. This does not make Unity invocation an atomic transaction.</summary>
    internal void InvokeLineEndpointLocalWithPublicationAdmission(IntPtr method, IntPtr receiver,
        Action guard, Action revalidatePreparedState, Action beforeDispatch, params Arg[] args)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(revalidatePreparedState); ArgumentNullException.ThrowIfNull(beforeDispatch);
        guard(); var call = new NativePublicationCall(VerifyAccess, args);
        try
        {
            guard();
            if (method == IntPtr.Zero) throw new MissingMethodException("The exact endpoint setter is absent.");
            var output = _returnType(method); guard();
            var returns = Describe(output, guard); guard();
            if (returns.Kind != NativeValueKind.Void || returns.ByRef)
                throw new UnsupportedSignatureException("Endpoint setter publication requires exact non-byref Void.");
            InvokeCore(method, receiver, null, call, false, args,
                () => { guard(); revalidatePreparedState(); guard(); beforeDispatch(); guard(); }, guard, guard);
        }
        catch (Exception error) { call.OperationFailed(error); throw; }
        finally { call.Dispose(); }
    }
}
