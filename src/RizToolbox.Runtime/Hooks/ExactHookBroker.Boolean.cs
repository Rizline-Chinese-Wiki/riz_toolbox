using System.Runtime.InteropServices;
using RizToolbox.Sdk;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    // IL2CPP System.Boolean occupies AL. A managed bool without explicit
    // marshalling would assume a four-byte Win32 BOOL at this native boundary.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate byte InstanceBooleanReturn0(IntPtr instance, IntPtr methodInfo);
    [ThreadStatic] private static int _booleanInvocationDepth;
    internal static int BooleanInvocationDepth => _booleanInvocationDepth;

    internal Binding BindRetainedBooleanObservation<T>(VerifiedHookTarget target, Func<IntPtr, bool> matches,
        Func<IntPtr, T> retain, Action<T> after) where T : class, IDisposable
    {
        if (target.Abi != ExactHookAbi.InstanceBooleanReturn0 || target.ReturnClass != IntPtr.Zero)
            throw new UnsupportedSignatureException("Retained Boolean observation requires a verified one-byte Boolean return.");
        ArgumentNullException.ThrowIfNull(matches); ArgumentNullException.ThrowIfNull(retain); ArgumentNullException.ThrowIfNull(after);
        return BindInternal(target, matches, (_, _, _, _) => { },
            instance => retain(instance) ?? throw new InvalidOperationException("Boolean observation context is null."),
            null, null, context => after((T)context));
    }

    private static byte InvokeBoolean(Entry entry, IntPtr instance, IntPtr method)
    {
        Interlocked.Increment(ref entry.VoidInFlight);
        try { return InvokeBooleanCore(entry, instance, method); }
        finally { Interlocked.Decrement(ref entry.VoidInFlight); }
    }
    private static byte InvokeBooleanCore(Entry entry, IntPtr instance, IntPtr method)
    {
        var invocation = Interlocked.Increment(ref entry.Invocation);
        Interlocked.Increment(ref entry.NativeCalls);
        if (Original(entry) is not InstanceBooleanReturn0 original) return 0;
        // Even suppressed nested entries advance depth: their GetJudge calls
        // must never borrow a retained coroutine from an outer MoveNext.
        ++_booleanInvocationDepth;
        Sink[]? sinks = null;
        List<(Sink Sink, IDisposable Context)>? retained = null;
        var completed = false;
        try
        {
            sinks = Before(entry, instance, invocation, 0, 0, false);
            if (sinks is not null)
                foreach (var sink in sinks)
                    if (sink.Active && sink.AfterRetained is not null)
                        try
                        {
                            retained ??= new(sinks.Length);
                            var context = sink.RetainReceiver!(instance);
                            try { retained.Add((sink, context)); }
                            catch { context.Dispose(); throw; }
                        }
                        catch (Exception error) { Interlocked.Exchange(ref entry.JudgeActivityUnknown, 1); SafeLog(sink, "Boolean context capture failed; forwarding original.", error); }
            if (sinks is not null) foreach (var sink in sinks) SafeDeliver(sink, HookPhase.Before, invocation, 0, 0);
            Interlocked.Increment(ref entry.OriginalCalls);
            var result = original(instance, method); // Exactly once; original failures propagate.
            completed = true;
            if (retained is not null)
                foreach (var item in retained)
                    if (item.Sink.Active)
                        try { item.Sink.AfterRetained!(item.Context); }
                        catch (Exception error) { Interlocked.Exchange(ref entry.JudgeActivityUnknown, 1); SafeLog(item.Sink, "Boolean observation failed; original return is preserved.", error); }
            return result; // Preserve AL, including noncanonical nonzero values.
        }
        finally
        {
            try { Finish(entry, sinks, completed, invocation, 0, 0); }
            finally
            {
                try
                {
                    if (retained is not null)
                        for (var index = retained.Count - 1; index >= 0; --index)
                            try { retained[index].Context.Dispose(); }
                            catch (Exception error) { Interlocked.Exchange(ref entry.JudgeActivityUnknown, 1); SafeLog(retained[index].Sink, "Boolean context release failed.", error); }
                }
                finally { --_booleanInvocationDepth; }
            }
        }
    }
}
