using RizToolbox.Sdk;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    internal Binding BindNativeCoroutineObservation<T>(VerifiedHookTarget target, NativeCoroutineKind kind,
        Func<NativeCoroutineFrame, T?> capture, Action<T, NativeCoroutineFrame, nuint> after,
        Action<NativeCoroutineFrame, string> fault, VerifiedHookTarget? automaticAlias = null) where T : class, IDisposable
        => BindNativeCoroutineObservation(target, kind, () => new NativeCoroutineContextBridge(
            Path.Combine(Path.GetDirectoryName(typeof(ExactHookBroker).Assembly.Location)!, "RizToolbox.JudgeContext.dll"), kind, _log),
            capture, after, fault, automaticAlias);

    internal sealed record CoroutineInvocation(long Sequence, Sink[]? Ordinary,
        List<(Sink Sink, IDisposable Context, bool Native)> Contexts);

    internal Binding BindNativeCoroutineObservation<T>(VerifiedHookTarget target, NativeCoroutineKind kind,
        Func<INativeCoroutineContextBridge> createBridge, Func<NativeCoroutineFrame, T?> capture,
        Action<T, NativeCoroutineFrame, nuint> after, Action<NativeCoroutineFrame, string> fault,
        VerifiedHookTarget? automaticAlias = null) where T : class, IDisposable
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(createBridge);
        ArgumentNullException.ThrowIfNull(capture); ArgumentNullException.ThrowIfNull(after); ArgumentNullException.ThrowIfNull(fault);
        if (target.Address == IntPtr.Zero || target.MethodInfo == IntPtr.Zero || target.DeclaringClass == IntPtr.Zero
            || (kind switch {
                NativeCoroutineKind.Start => target.Abi != ExactHookAbi.CoroutineStartReference1 || target.ReturnClass == IntPtr.Zero,
                NativeCoroutineKind.Factory => target.Abi != ExactHookAbi.CoroutineFactoryReference0 || target.ReturnClass == IntPtr.Zero,
                NativeCoroutineKind.MoveNext => target.Abi != ExactHookAbi.InstanceBooleanReturn0 || target.ReturnClass != IntPtr.Zero,
                _ => true }))
            throw new UnsupportedSignatureException("Coroutine observation requires its exact Start reference1, Factory reference0 or MoveNext byte-return ABI.");
        if (automaticAlias is not null && (kind != NativeCoroutineKind.Start || automaticAlias.Address != target.Address
            || automaticAlias.Abi != target.Abi || automaticAlias.ReturnClass != target.ReturnClass
            || automaticAlias.DeclaringClass != target.DeclaringClass || automaticAlias.MethodInfo == IntPtr.Zero
            || automaticAlias.MethodInfo == target.MethodInfo))
            throw new UnsupportedSignatureException("Coroutine alias must be an independently verified matching declared StartCoroutine_Auto entry.");
        var sink = new Sink { Id = target.Id, Main = _main, Log = _log, MethodInfo = target.MethodInfo, DeclaringClass = target.DeclaringClass,
            Matches = _ => true, Deliver = (_, _, _, _) => { }, CaptureCoroutine = frame => capture(frame),
            AfterCoroutine = (context, frame, result) => after((T)context, frame, result), CoroutineFault = fault };
        lock (Gate)
        {
            if (Entries.TryGetValue(target.Address, out var existing))
            {
                if (existing.Target.Abi != target.Abi || existing.Target.MethodInfo != target.MethodInfo
                    || existing.Target.DeclaringClass != target.DeclaringClass || existing.Target.ReturnClass != target.ReturnClass)
                    throw new UnsupportedSignatureException("Coroutine entry cannot share a foreign method alias.");
                if (existing.NativeInstallFailure is not null) throw new InvalidOperationException(existing.NativeInstallFailure);
                if (existing.CoroutineBridge?.Kind != kind) throw new UnsupportedSignatureException("Native coroutine transport must own the first patch.");
                if (existing.CoroutineMethodAlias != (automaticAlias?.MethodInfo ?? IntPtr.Zero))
                    throw new UnsupportedSignatureException("Coroutine observers must agree on the exact declared alias.");
                existing.Sinks.Add(sink); PublishCoroutineObservers(existing); return Binding.Create(existing, sink);
            }
            if (_engine is not IPreparedNativeDetourEngine prepared) throw new UnsupportedSignatureException("Coroutine transport requires Prepare/Configure/Commit.");
            var entry = new Entry { Target = target, Engine = _engine, NativeMain = _main, NativeLog = _log,
                CoroutineMethodAlias = automaticAlias?.MethodInfo ?? IntPtr.Zero };
            entry.CoroutineBridge = createBridge() ?? throw new InvalidOperationException("No native coroutine bridge.");
            if (entry.CoroutineBridge.Kind != kind) throw new UnsupportedSignatureException("Native coroutine bridge kind differs.");
            entry.Sinks.Add(sink); PublishCoroutineObservers(entry); Entries.Add(target.Address, entry);
            try
            {
                var code = prepared is DobbyDetourEngine actualEngine && entry.CoroutineBridge is NativeCoroutineContextBridge actualBridge
                    ? CoroutineCodeIdentity.TryCapture(entry, actualEngine, actualBridge, _main, _log) : null;
                var original = prepared.Prepare(target, entry.CoroutineBridge.EntryAddress);
                if (original == IntPtr.Zero) throw new InvalidOperationException("No original coroutine trampoline.");
                code?.Prepared(original);
                entry.CoroutineBridge.Configure(original, frame => EnterCoroutine(entry, frame),
                    (frame, token, result, flags) => ExitCoroutine(entry, frame, token, result, flags));
                code?.VerifyBeforeCommit();
                prepared.Commit(target);
                code?.Installed();
                return Binding.Create(entry, sink);
            }
            catch (Exception error)
            {
                sink.Active = false; entry.Sinks.Clear(); PublishCoroutineObservers(entry);
                entry.NativeInstallFailure = "Native coroutine installation remains process-owned without retry: " + error.Message; throw;
            }
        }
    }
    private static void PublishCoroutineObservers(Entry entry)
        => Volatile.Write(ref entry.CoroutineObservers, entry.Sinks.Where(s => s.Active && s.CaptureCoroutine is not null).ToArray());
    private static ManagedBoundaryActivity ReadCoroutineActivity(Entry entry, INativeCoroutineContextBridge bridge)
    {
        try
        {
            var value = bridge.ReadActivity();
            if (value.Known && value.InFlight >= 0) return new(value.InFlight, Volatile.Read(ref entry.CoroutineUnknown) == 0);
        }
        catch { }
        Interlocked.Exchange(ref entry.CoroutineUnknown, 1); return new(0, false);
    }
    private static void CoroutineFaults(Sink[] observers, NativeCoroutineFrame frame, string reason)
    {
        // May run on a native worker while the installing thread holds Gate.
        // Fault handlers are restricted to managed identities/atomic flags.
        foreach (var sink in observers) try { sink.CoroutineFault?.Invoke(frame, reason); } catch { }
    }
    private static nuint EnterCoroutine(Entry entry, NativeCoroutineFrame frame)
    {
        if (entry.CoroutineBridge!.Kind == NativeCoroutineKind.MoveNext) ++_booleanInvocationDepth;
        Interlocked.Increment(ref entry.NativeCalls); Interlocked.Increment(ref entry.OriginalCalls);
        var observers = Volatile.Read(ref entry.CoroutineObservers);
        if (!frame.IsValid || frame.Kind != entry.CoroutineBridge.Kind)
        { Interlocked.Exchange(ref entry.CoroutineUnknown, 1); CoroutineFaults(observers, frame, "Invalid native coroutine frame."); return 0; }
        if (!entry.NativeMain!.IsMainThread)
        { Interlocked.Increment(ref entry.WrongThread); CoroutineFaults(observers, frame, "Coroutine invoked off the verified main thread."); return 0; }
        if (frame.Method != IntPtr.Zero && frame.Method != entry.Target.MethodInfo && frame.Method != entry.CoroutineMethodAlias)
        { CoroutineFaults(observers, frame, "Foreign coroutine MethodInfo alias."); return 0; }
        Sink[]? ordinary = null; var contexts = new List<(Sink Sink, IDisposable Context, bool Native)>();
        try
        {
            if (entry.CoroutineInvocations.Count >= 4096) throw new InvalidOperationException("Coroutine invocation capacity exhausted.");
            var sequence = Interlocked.Increment(ref entry.Invocation); var token = unchecked((nuint)sequence);
            if (token == 0) throw new InvalidOperationException("Coroutine invocation sequence exhausted.");
            // Physical coroutine ownership is observed even when ordinary logical
            // reentry is suppressed. No child inherits a parent receiver's role.
            foreach (var sink in observers)
                try
                {
                    var context = sink.CaptureCoroutine!(frame);
                    if (context is not null) try { contexts.Add((sink, context, true)); } catch { context.Dispose(); throw; }
                }
                catch (Exception error)
                { Interlocked.Exchange(ref entry.CoroutineUnknown, 1); SafeLog(sink, "Coroutine capture failed; original is preserved.", error); CoroutineFaults(new[] { sink }, frame, "Coroutine capture failed."); }
            if (frame.Kind == NativeCoroutineKind.MoveNext)
            {
                ordinary = Before(entry, frame.Self, sequence, 0, 0, false);
                if (ordinary is not null)
                    foreach (var sink in ordinary)
                        if (sink.Active && sink.CaptureCoroutine is null && sink.AfterRetained is not null)
                            try
                            {
                                var context = sink.RetainReceiver!(frame.Self);
                                try { contexts.Add((sink, context, false)); } catch { context.Dispose(); throw; }
                            }
                            catch (Exception error) { Interlocked.Exchange(ref entry.CoroutineUnknown, 1); SafeLog(sink, "Coroutine shared Boolean capture failed.", error); }
            }
            entry.CoroutineInvocations.Add(token, new(sequence, ordinary, contexts));
            if (ordinary is not null) foreach (var sink in ordinary) SafeDeliver(sink, HookPhase.Before, sequence, 0, 0);
            return token;
        }
        catch (Exception error)
        {
            Interlocked.Exchange(ref entry.CoroutineUnknown, 1); CoroutineFaults(observers, frame, "Coroutine dispatch failed.");
            try { Finish(entry, ordinary, false, 0, 0, 0); } finally { ReleaseCoroutineContexts(entry, contexts); }
            try { entry.NativeLog!.Log(LogLevel.Error, "Coroutine dispatch failed; original is preserved.", error); } catch { }
            return 0;
        }
    }
    private static void ExitCoroutine(Entry entry, NativeCoroutineFrame frame, nuint token, nuint result, NativeCoroutineExitFlags flags)
    {
        try
        {
            if (token == 0 || !entry.NativeMain!.IsMainThread || !entry.CoroutineInvocations.Remove(token, out var invocation)) return;
            var returned = (flags & (NativeCoroutineExitFlags.Returned | NativeCoroutineExitFlags.Abnormal | NativeCoroutineExitFlags.EnterFaulted)) == NativeCoroutineExitFlags.Returned;
            try
            {
                if (returned)
                    foreach (var item in invocation.Contexts)
                        if (item.Sink.Active)
                            try
                            {
                                if (item.Native) item.Sink.AfterCoroutine!(item.Context, frame, result);
                                else item.Sink.AfterRetained!(item.Context);
                            }
                            catch (Exception error) { Interlocked.Exchange(ref entry.CoroutineUnknown, 1); SafeLog(item.Sink, "Coroutine after observation failed.", error); }
            }
            finally
            {
                try { Finish(entry, invocation.Ordinary, returned, invocation.Sequence, 0, 0); }
                finally { ReleaseCoroutineContexts(entry, invocation.Contexts); }
            }
        }
        finally { if (entry.CoroutineBridge!.Kind == NativeCoroutineKind.MoveNext) --_booleanInvocationDepth; }
    }
    private static void ReleaseCoroutineContexts(Entry entry, List<(Sink Sink, IDisposable Context, bool Native)> contexts)
    {
        for (var index = contexts.Count - 1; index >= 0; --index)
            try { contexts[index].Context.Dispose(); }
            catch (Exception error) { Interlocked.Exchange(ref entry.CoroutineUnknown, 1); SafeLog(contexts[index].Sink, "Coroutine context release failed.", error); }
    }
}
