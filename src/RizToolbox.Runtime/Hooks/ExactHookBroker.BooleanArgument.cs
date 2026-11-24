using System.Runtime.InteropServices;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    // IL2CPP Boolean is an unsigned byte in DL, not a marshalled Win32 BOOL.
    // Preserve its raw value and the full trailing MethodInfo pointer unchanged.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void InstanceBooleanVoid(IntPtr instance, byte value, IntPtr methodInfo);

    /// <summary>Internal read-only observation of a declared void(Boolean).
    /// Capture precedes logical delivery; After runs only on original return.
    /// Context disposal and activity accounting include failures and logical detach.
    /// This managed transport does not promise native original SEH/C++ unwind.</summary>
    internal Binding BindRetainedBooleanArgumentObservation<T>(VerifiedHookTarget target, Func<IntPtr, bool> matches,
        Func<IntPtr, byte, T> capture, Action<T, byte> after) where T : class, IDisposable
    {
        _main.VerifyAccess();
        ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(capture); ArgumentNullException.ThrowIfNull(after);
        if (target.Abi != ExactHookAbi.InstanceBooleanVoid || target.ReturnClass != IntPtr.Zero
            || target.MethodInfo == IntPtr.Zero || target.DeclaringClass == IntPtr.Zero)
            throw new UnsupportedSignatureException("Boolean-argument observation requires the exact declared void(Boolean) target.");
        return BindInternal(target, matches, (_, _, _, _) => { }, null, null, null,
            retainBooleanArgument: (receiver, value) => capture(receiver, value)
                ?? throw new InvalidOperationException("Boolean-argument observation context is null."),
            afterRetainedBooleanArgument: (context, value) => after((T)context, value));
    }

    private static void InvokeBooleanArgument(Entry entry, IntPtr instance, byte value, IntPtr method)
    {
        Interlocked.Increment(ref entry.VoidInFlight);
        var probes = EnterProbes(entry, instance, value, method); var returned = false;
        try
        {
            var invocation = Interlocked.Increment(ref entry.Invocation);
            Interlocked.Increment(ref entry.NativeCalls);
            if (Original(entry) is not InstanceBooleanVoid original) return;
            // Native direct IL2CPP callers may supply null MethodInfo. A foreign
            // non-null alias forwards unchanged without touching receiver metadata.
            var sinks = method == IntPtr.Zero || method == entry.Target.MethodInfo
                ? Before(entry, instance, invocation, value, 0, false) : null;
            List<(Sink Sink, IDisposable Context)>? retained = null;
            if (sinks is not null)
                foreach (var sink in sinks)
                    if (sink.Active && sink.RetainBooleanArgument is not null)
                        try
                        {
                            retained ??= new(sinks.Length);
                            var context = sink.RetainBooleanArgument(instance, value);
                            try { retained.Add((sink, context)); }
                            catch { context.Dispose(); throw; }
                        }
                        catch (Exception error) { SafeLog(sink, "Boolean-argument capture failed; forwarding original.", error); }
            if (sinks is not null)
                foreach (var sink in sinks) SafeDeliver(sink, Sdk.HookPhase.Before, invocation, value, 0);
            Interlocked.Increment(ref entry.OriginalCalls);
            var completed = false;
            try { original(instance, value, method); completed = true; }
            finally
            {
                try
                {
                    if (completed && retained is not null)
                        foreach (var item in retained)
                            if (item.Sink.Active)
                                try { item.Sink.AfterRetainedBooleanArgument!(item.Context, value); }
                                catch (Exception error) { SafeLog(item.Sink, "Boolean-argument observation failed.", error); }
                }
                finally
                {
                    try { Finish(entry, sinks, completed, invocation, value, 0); }
                    finally
                    {
                        if (retained is not null)
                            for (var index = retained.Count - 1; index >= 0; --index)
                                try { retained[index].Context.Dispose(); }
                                catch (Exception error) { SafeLog(retained[index].Sink, "Boolean-argument context release failed.", error); }
                    }
                }
            }
            returned = completed;
        }
        finally { try { ExitProbes(entry, probes, instance, value, method, returned); } finally { Interlocked.Decrement(ref entry.VoidInFlight); } }
    }
}
