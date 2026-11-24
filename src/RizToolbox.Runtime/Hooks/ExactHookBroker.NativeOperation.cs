using RizToolbox.Sdk;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    [ThreadStatic] private static OperationInvocation? _operationTop;
    [ThreadStatic] private static int _operationOverflow;
    internal sealed class OperationInvocation
    {
        internal readonly Entry Entry;
        internal readonly NativeOperationFrame Frame;
        internal readonly int Thread = Environment.CurrentManagedThreadId;
        internal readonly OperationInvocation? Parent = _operationTop;
        internal readonly int Overflow = _operationOverflow;
        internal readonly Sink[] Observers;
        internal Sink? CapturingObserver;
        internal readonly List<(Sink Sink, IDisposable Context)> Contexts = new();
        internal OperationInvocation(Entry entry, NativeOperationFrame frame, Sink[] observers) => (Entry, Frame, Observers) = (entry, frame, observers);
    }
    internal Binding BindNativeOperationObservation<T>(VerifiedHookTarget target, NativeOperationKind kind,
        Func<NativeOperationFrame, T?> capture, Action<T, NativeOperationFrame, NativeOperationExitFlags> complete,
        Action<NativeOperationFrame, string> fault) where T : class, IDisposable
        => BindBootstrappedNativeOperationObservation(target, kind, capture, complete, fault);

    internal Binding BindNativeOperationObservation<T>(VerifiedHookTarget target, NativeOperationKind kind,
        Func<INativeOperationContextBridge> createBridge, Func<NativeOperationFrame, T?> capture,
        Action<T, NativeOperationFrame, NativeOperationExitFlags> complete, Action<NativeOperationFrame, string> fault) where T : class, IDisposable
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(createBridge);
        ArgumentNullException.ThrowIfNull(capture); ArgumentNullException.ThrowIfNull(complete); ArgumentNullException.ThrowIfNull(fault);
        var abi = OperationAbi(kind);
        if (target.Address == IntPtr.Zero || target.MethodInfo == IntPtr.Zero || target.DeclaringClass == IntPtr.Zero
            || target.Abi != abi || target.ReturnClass != IntPtr.Zero)
            throw new UnsupportedSignatureException("Native operation requires its exact sealed instance void/reference ABI.");
        var sink = new Sink { Id = target.Id, Main = _main, Log = _log, MethodInfo = target.MethodInfo, DeclaringClass = target.DeclaringClass,
            Matches = _ => true, Deliver = (_, _, _, _) => { }, CaptureOperation = frame => capture(frame),
            CompleteOperation = (context, frame, flags) => complete((T)context, frame, flags), OperationFault = fault };
        lock (Gate)
        {
            if (Entries.TryGetValue(target.Address, out var existing))
            {
                if (existing.Target.Abi != target.Abi || existing.Target.MethodInfo != target.MethodInfo
                    || existing.Target.DeclaringClass != target.DeclaringClass || existing.Target.ReturnClass != target.ReturnClass
                    || existing.OperationBridge?.Kind != kind || existing.NativeInstallFailure is not null)
                    throw new UnsupportedSignatureException("Operation cannot share a foreign identity or replace an existing transport.");
                existing.Sinks.Add(sink); PublishOperationObservers(existing); return Binding.Create(existing, sink);
            }
            if (_engine is not IPreparedNativeDetourEngine prepared) throw new UnsupportedSignatureException("Operation transport requires Prepare/Configure/Commit.");
            var entry = new Entry { Target = target, Engine = _engine, NativeMain = _main, NativeLog = _log };
            entry.OperationBridge = createBridge() ?? throw new InvalidOperationException("Missing native operation bridge.");
            if (entry.OperationBridge.Kind != kind || entry.OperationBridge.EntryAddress == IntPtr.Zero)
                throw new UnsupportedSignatureException("Operation bridge kind differs from the verified declaration.");
            entry.Sinks.Add(sink); PublishOperationObservers(entry); Entries.Add(target.Address, entry);
            try
            {
                var original = prepared.Prepare(target, entry.OperationBridge.EntryAddress);
                if (original == IntPtr.Zero) throw new InvalidOperationException("Missing operation original trampoline.");
                entry.OperationBridge.Configure(original, frame => EnterOperation(entry, frame),
                    (frame, token, flags) => ExitOperation(entry, frame, token, flags));
                prepared.Commit(target); return Binding.Create(entry, sink);
            }
            catch (Exception error)
            {
                sink.Active = false; entry.Sinks.Clear(); PublishOperationObservers(entry);
                entry.NativeInstallFailure = "Native operation installation remains permanently owned without retry: " + error.Message;
                Interlocked.Exchange(ref entry.OperationUnknown, 1); throw;
            }
        }
    }
    internal static ExactHookAbi OperationAbi(NativeOperationKind kind) => kind switch {
        NativeOperationKind.Void0 => ExactHookAbi.OperationVoid0,
        NativeOperationKind.Reference1 => ExactHookAbi.OperationReference1Void,
        NativeOperationKind.Reference2 => ExactHookAbi.OperationReference2Void,
        NativeOperationKind.Single1 => ExactHookAbi.OperationSingle1Void,
        _ => throw new UnsupportedSignatureException("Unsupported native operation kind.") };
    private static void PublishOperationObservers(Entry entry)
        => Volatile.Write(ref entry.OperationObservers, entry.Sinks.Where(s => s.Active && s.CaptureOperation is not null).ToArray());
    private static ManagedBoundaryActivity ReadOperationActivity(Entry entry, INativeOperationContextBridge bridge)
    {
        var retained = Math.Max(0, Volatile.Read(ref entry.OperationCount));
        try
        {
            var native = bridge.ReadActivity();
            if (native.InFlight >= 0)
            {
                if (!native.Known) Interlocked.Exchange(ref entry.OperationUnknown, 1);
                return new(Math.Max(retained, native.InFlight), native.Known && Volatile.Read(ref entry.OperationUnknown) == 0);
            }
        }
        catch { }
        Interlocked.Exchange(ref entry.OperationUnknown, 1); return new(retained, false);
    }
    private static void OperationFaults(Entry entry, Sink[] observers, NativeOperationFrame frame, string reason)
    { foreach (var sink in observers) try { sink.OperationFault?.Invoke(frame, reason); } catch { Interlocked.Exchange(ref entry.OperationUnknown, 1); } }
    private static nuint EnterOperation(Entry entry, NativeOperationFrame frame)
    {
        Interlocked.Increment(ref entry.NativeCalls);
        var observers = Volatile.Read(ref entry.OperationObservers);
        if (Interlocked.Increment(ref entry.OperationCount) > 4096)
        {
            Interlocked.Decrement(ref entry.OperationCount); ++_operationOverflow;
            Interlocked.Exchange(ref entry.OperationUnknown, 1); OperationFaults(entry, observers, frame, "Operation physical record capacity exhausted."); return 0;
        }
        OperationInvocation? call = null; var registered = false; nuint token = 0;
        try
        {
            var sequence = Interlocked.Increment(ref entry.OperationSequence);
            if (sequence <= 0) throw new InvalidOperationException("Operation managed sequence exhausted.");
            token = checked((nuint)sequence); call = new(entry, frame, observers);
            if (!entry.OperationInvocations.TryAdd(token, call)) throw new InvalidOperationException("Operation token collision.");
            registered = true; _operationTop = call;
            if (!frame.IsValid || frame.Kind != entry.OperationBridge!.Kind)
            { Interlocked.Exchange(ref entry.OperationUnknown, 1); OperationFaults(entry, observers, frame, "Invalid native operation frame."); return token; }
            if (!entry.NativeMain!.IsMainThread)
            { Interlocked.Increment(ref entry.WrongThread); OperationFaults(entry, observers, frame, "Owned operation entered from a worker."); return token; }
            if (frame.Method != IntPtr.Zero && frame.Method != entry.Target.MethodInfo)
            { OperationFaults(entry, observers, frame, "Foreign operation MethodInfo alias."); return token; }
            if (Volatile.Read(ref entry.OperationUnknown) != 0)
            { OperationFaults(entry, observers, frame, "Operation transport remains unknown."); return token; }
            foreach (var sink in observers)
            {
                if (!sink.Active) continue;
                try
                {
                    IDisposable? context;
                    call.CapturingObserver = sink;
                    try { context = sink.CaptureOperation!(frame); }
                    finally { call.CapturingObserver = null; }
                    if (context is not null) try { call.Contexts.Add((sink, context)); } catch { context.Dispose(); throw; }
                }
                catch (Exception error)
                {
                    Interlocked.Exchange(ref entry.OperationUnknown, 1);
                    SafeLog(sink, "Operation capture failed; original is preserved. "
                        + $"Target '{sink.Id}', native sequence {frame.Sequence}, observed parent sequence {call.Parent?.Frame.Sequence ?? 0}, "
                        + $"caller return address 0x{frame.Caller.ToInt64():x}.", error);
                    OperationFaults(entry, new[] { sink }, frame, "Operation capture failed.");
                }
            }
            return token;
        }
        catch (Exception error)
        {
            Interlocked.Exchange(ref entry.OperationUnknown, 1); OperationFaults(entry, observers, frame, "Operation entry observation failed.");
            try { entry.NativeLog!.Log(LogLevel.Error, "Operation entry observation failed.", error); } catch { }
            if (registered) return token;
            Interlocked.Decrement(ref entry.OperationCount); ++_operationOverflow; return 0;
        }
    }
    private static void ExitOperation(Entry entry, NativeOperationFrame frame, nuint token, NativeOperationExitFlags flags)
    {
        if (token == 0)
        {
            if (_operationOverflow > 0) --_operationOverflow;
            else { Interlocked.Exchange(ref entry.OperationUnknown, 1); OperationFaults(entry, Volatile.Read(ref entry.OperationObservers), frame, "Unpaired operation without token."); }
            return;
        }
        if (!entry.OperationInvocations.TryGetValue(token, out var call))
        { Interlocked.Exchange(ref entry.OperationUnknown, 1); OperationFaults(entry, Volatile.Read(ref entry.OperationObservers), frame, "Unknown operation exit token."); return; }
        if (call.Thread != Environment.CurrentManagedThreadId || !ReferenceEquals(_operationTop, call) || _operationOverflow != call.Overflow
            || !SameOperationFrame(call.Frame, frame))
        { Interlocked.Exchange(ref entry.OperationUnknown, 1); OperationFaults(entry, call.Observers, frame, "Operation completion crossed its exact thread, frame or physical parent."); return; }
        if (!entry.OperationInvocations.TryRemove(token, out _))
        { Interlocked.Exchange(ref entry.OperationUnknown, 1); return; }
        // Native admission can suppress the original before it starts. That
        // attempt still owns Before/After/context disposal, but earns neither
        // returned-original credit nor a listener/effects completion receipt.
        // Count only a confirmed original window; malformed flags stay Unknown.
        if (flags is NativeOperationExitFlags.Returned or NativeOperationExitFlags.Abnormal)
            Interlocked.Increment(ref entry.OriginalCalls);
        if (flags is not (NativeOperationExitFlags.Returned or NativeOperationExitFlags.Abnormal or NativeOperationExitFlags.Skipped))
            Interlocked.Exchange(ref entry.OperationUnknown, 1);
        if (flags is not (NativeOperationExitFlags.Returned or NativeOperationExitFlags.Skipped))
            OperationFaults(entry, call.Observers, frame, "Operation original exited abnormally or with invalid completion flags.");
        try
        {
            // A captured operation keeps its completion duty after logical detach
            // and after a peer faults. Completion is observation, never cancellation.
            foreach (var item in call.Contexts)
                try { item.Sink.CompleteOperation!(item.Context, frame, flags); }
                catch (Exception error)
                { Interlocked.Exchange(ref entry.OperationUnknown, 1); SafeLog(item.Sink, "Operation completion observer failed.", error); OperationFaults(entry, new[] { item.Sink }, frame, "Operation completion observer failed."); }
        }
        finally
        {
            for (var index = call.Contexts.Count - 1; index >= 0; --index)
                try { call.Contexts[index].Context.Dispose(); }
                catch (Exception error)
                { Interlocked.Exchange(ref entry.OperationUnknown, 1); SafeLog(call.Contexts[index].Sink, "Operation context cleanup failed.", error); OperationFaults(entry, new[] { call.Contexts[index].Sink }, frame, "Operation context cleanup failed."); }
            _operationTop = call.Parent; Interlocked.Decrement(ref entry.OperationCount);
        }
    }
    private static bool SameOperationFrame(NativeOperationFrame a, NativeOperationFrame b)
        => a.Version == b.Version && a.Size == b.Size && a.Kind == b.Kind && a.Reserved == b.Reserved
            && a.Self == b.Self && a.Arg0 == b.Arg0 && a.Arg1 == b.Arg1 && a.Method == b.Method && a.Caller == b.Caller && a.Sequence == b.Sequence;
}
