using RizToolbox.Sdk;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    /// <summary>Lookup never creates or patches an entry. Logical detach does not
    /// stop physical native forwarding, so its real activity remains observable.</summary>
    internal ManagedBoundaryActivity ReadExactActivity(VerifiedHookTarget target, bool requireNativeJudgeTransport = false)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(target);
        Entry? entry;
        lock (Gate) Entries.TryGetValue(target.Address, out entry);
        if (entry is null || entry.Target.Abi != target.Abi || entry.Target.MethodInfo != target.MethodInfo
            || entry.Target.DeclaringClass != target.DeclaringClass || entry.Target.ReturnClass != target.ReturnClass
            || entry.NativeInstallFailure is not null || requireNativeJudgeTransport && entry.NativeJudgeBridge is null)
            return new(0, false);
        // Activity belongs to the physical entry, independently of any one sink.
        var count = Interlocked.Read(ref entry.VoidInFlight);
        var supported = entry.Target.Abi is ExactHookAbi.InstanceInt32SingleVoid or ExactHookAbi.InstanceEnum32SingleVoid
            or ExactHookAbi.InstanceBooleanReturn0;
        var known = supported && Volatile.Read(ref entry.JudgeActivityUnknown) == 0;
        if (entry.NativeJudgeBridge is { } bridge)
        { var native = ReadJudgeActivity(entry, bridge); count = Math.Max(count, native.InFlight); known &= native.Known; }
        if (entry.CoroutineBridge is { } coroutine)
        { var native = ReadCoroutineActivity(entry, coroutine); count = Math.Max(count, native.InFlight); known &= native.Known; }
        if (entry.NativeVoidBridge is { } nativeVoid)
        { var native = ReadNativeVoidActivity(entry, nativeVoid); count = Math.Max(count, native.InFlight); known = native.Known; }
        if (entry.OperationBridge is { } operation)
        { var native = ReadOperationActivity(entry, operation); count = Math.Max(count, native.InFlight); known = native.Known; }
        return new(count, known);
    }
    private static ManagedBoundaryActivity ReadJudgeActivity(Entry entry, INativeJudgeContextBridge bridge)
    {
        try
        {
            var activity = bridge.ReadActivity();
            if (activity.Known && activity.InFlight >= 0) return new(activity.InFlight, true);
        }
        catch { }
        Interlocked.Exchange(ref entry.JudgeActivityUnknown, 1);
        return new(0, false);
    }
    [ThreadStatic] private static int _nativeJudgeDepth;
    internal static int NativeJudgeContextDepth => _nativeJudgeDepth;
    internal sealed record NativeJudgeInvocation(long Sequence, Sink[] Sinks,
        List<(Sink Sink, IDisposable Context)> Contexts);

    /// <summary>The native transport must own the first patch. Ordinary logical
    /// judge observers can subsequently share this same entry and trampoline.</summary>
    internal Binding BindNativeJudgeContext<T>(VerifiedHookTarget target, Func<INativeJudgeContextBridge> createBridge,
        Func<NativeJudgeFrame, T?> capture, Action<T, NativeJudgeFrame> after) where T : class, IDisposable
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(createBridge);
        ArgumentNullException.ThrowIfNull(capture); ArgumentNullException.ThrowIfNull(after);
        if (target.Address == IntPtr.Zero || target.MethodInfo == IntPtr.Zero || target.DeclaringClass == IntPtr.Zero
            || target.Abi is not (ExactHookAbi.InstanceEnum32SingleVoid or ExactHookAbi.InstanceInt32SingleVoid) || target.ReturnClass != IntPtr.Zero)
            throw new UnsupportedSignatureException("Native judge context requires the verified instance enum32/single void ABI.");
        var sink = new Sink { Id = target.Id, Main = _main, Log = _log, MethodInfo = target.MethodInfo,
            DeclaringClass = target.DeclaringClass, Matches = _ => true, Deliver = (_, _, _, _) => { },
            CaptureNativeJudge = frame => capture(frame), AfterNativeJudge = (context, frame) => after((T)context, frame) };
        lock (Gate)
        {
            if (Entries.TryGetValue(target.Address, out var existing))
            {
                if (existing.Target.Abi != target.Abi || existing.Target.ReturnClass != target.ReturnClass)
                    throw new UnsupportedSignatureException("An address already has a different exact hook ABI.");
                if (existing.Target.MethodInfo != target.MethodInfo || existing.Target.DeclaringClass != target.DeclaringClass)
                    throw new UnsupportedSignatureException("Native judge context cannot share a foreign declared method alias.");
                if (existing.NativeInstallFailure is not null) throw new InvalidOperationException(existing.NativeInstallFailure);
                if (existing.NativeJudgeBridge is null)
                    throw new InvalidOperationException("Native context must be installed before the first managed patch; a second patch is forbidden.");
                existing.Sinks.Add(sink); return Binding.Create(existing, sink);
            }
            if (_engine is not IPreparedNativeDetourEngine prepared)
                throw new UnsupportedSignatureException("Native judge context requires a verified Prepare/Commit detour engine.");
            var entry = new Entry { Target = target, Engine = _engine, NativeMain = _main, NativeLog = _log };
            // Root the native library, callbacks, original and entry even on an
            // uncertain preparation/commit failure. Never destroy a live patch or
            // retry an address whose native publication outcome is unknown.
            entry.NativeJudgeBridge = createBridge() ?? throw new InvalidOperationException("The native judge bridge is null.");
            entry.Sinks.Add(sink); Entries.Add(target.Address, entry);
            try
            {
                var code = _engine is DobbyDetourEngine actualEngine && entry.NativeJudgeBridge is NativeJudgeContextBridge actualBridge
                    ? NativeJudgeCodeIdentity.TryCapture(entry, actualEngine, actualBridge, _main, _log) : null;
                var original = prepared.Prepare(target, entry.NativeJudgeBridge.EntryAddress);
                if (original == IntPtr.Zero) throw new InvalidOperationException("Native judge preparation returned a null original.");
                code?.Prepared(original);
                entry.NativeJudgeBridge.Configure(original, frame => EnterNativeJudge(entry, frame),
                    (frame, token, flags) => ExitNativeJudge(entry, frame, token, flags));
                code?.VerifyBeforeCommit();
                prepared.Commit(target); // configuration and all roots exist BEFORE executable publication
                code?.Installed();
                return Binding.Create(entry, sink);
            }
            catch (Exception error)
            {
                sink.Active = false; entry.Sinks.Clear();
                entry.NativeInstallFailure = "NativeJudgeInstallFailed: this address remains process-owned after an uncertain native preparation/publication failure. " + error.Message;
                throw;
            }
        }
    }

    private static nuint EnterNativeJudge(Entry entry, NativeJudgeFrame frame)
    {
        // Even suppressed reentry creates an invocation boundary. An adapter's
        // retained scope must match this depth, so an unassociated nested call
        // cannot inherit the outer call's note. Exit pairs depth for token zero.
        ++_nativeJudgeDepth;
        Interlocked.Increment(ref entry.NativeCalls); Interlocked.Increment(ref entry.OriginalCalls);
        if (!entry.NativeMain!.IsMainThread) { Interlocked.Increment(ref entry.WrongThread); return 0; }
        if (!frame.IsValid) return 0;
        Entered ??= new();
        if (!Entered.Add(entry.Target.Address)) { Interlocked.Increment(ref entry.Reentries); return 0; }
        var contexts = new List<(Sink Sink, IDisposable Context)>();
        try
        {
            if (entry.NativeJudgeInvocations.Count >= 4096) throw new InvalidOperationException("Native judge invocation capacity reached.");
            var sequence = Interlocked.Increment(ref entry.Invocation);
            var token = unchecked((nuint)sequence);
            if (token == 0) throw new InvalidOperationException("Native judge invocation sequence exhausted.");
            Sink[] snapshot; lock (Gate) snapshot = entry.Sinks.Where(x => x.Active).ToArray();
            var matched = new List<Sink>();
            foreach (var sink in snapshot)
            {
                if (!sink.Main.IsMainThread) continue;
                try { if (sink.Active && sink.Matches(frame.Receiver)) matched.Add(sink); }
                catch (Exception error) { SafeLog(sink, "Native judge receiver filter failed.", error); }
            }
            // Retain caller context before ordinary before callbacks can mutate the game.
            foreach (var sink in matched)
                if (sink.Active && (sink.CaptureNativeJudge is not null || sink.RetainJudge is not null))
                    try
                    {
                        var context = sink.CaptureNativeJudge is not null ? sink.CaptureNativeJudge(frame)
                            : sink.RetainJudge!(frame.Receiver,frame.Judge,frame.Delta);
                        if (context is not null)
                        {
                            try { contexts.Add((sink, context)); }
                            catch { context.Dispose(); throw; }
                        }
                    }
                    catch (Exception error) { Interlocked.Exchange(ref entry.JudgeActivityUnknown, 1); SafeLog(sink, "Native judge context capture failed; forwarding original.", error); }
            var sinks = matched.ToArray();
            entry.NativeJudgeInvocations.Add(token, new(sequence, sinks, contexts));
            foreach (var sink in sinks) SafeDeliver(sink, HookPhase.Before, sequence, frame.Judge, frame.Delta);
            return token;
        }
        catch (Exception error)
        {
            Interlocked.Exchange(ref entry.JudgeActivityUnknown, 1);
            ReleaseNativeJudgeContexts(entry, contexts); Entered.Remove(entry.Target.Address);
            try { entry.NativeLog!.Log(LogLevel.Error, "Native judge dispatch capture failed; forwarding original.", error); } catch { }
            return 0;
        }
    }
    private static void ExitNativeJudge(Entry entry, NativeJudgeFrame frame, nuint token, NativeJudgeExitFlags flags)
    {
        try { FinishNativeJudge(entry, frame, token, flags); }
        finally { if (_nativeJudgeDepth > 0) --_nativeJudgeDepth; }
    }
    private static void FinishNativeJudge(Entry entry, NativeJudgeFrame frame, nuint token, NativeJudgeExitFlags flags)
    {
        if (token == 0 || !entry.NativeMain!.IsMainThread) return;
        if (!entry.NativeJudgeInvocations.Remove(token, out var invocation)) return;
        var returned = flags.HasFlag(NativeJudgeExitFlags.Returned) && !flags.HasFlag(NativeJudgeExitFlags.Abnormal);
        try
        {
            if (returned)
                foreach (var item in invocation.Contexts)
                    if (item.Sink.Active)
                        try
                        {
                            if (item.Sink.AfterNativeJudge is not null) item.Sink.AfterNativeJudge(item.Context, frame);
                            else item.Sink.AfterRetainedJudge!(item.Context, invocation.Sequence);
                        }
                        catch (Exception error) { Interlocked.Exchange(ref entry.JudgeActivityUnknown, 1); SafeLog(item.Sink, "Native judge retained observation failed.", error); }
            if (returned)
                foreach (var sink in invocation.Sinks)
                    if (sink.Active) SafeDeliver(sink, HookPhase.After, invocation.Sequence, frame.Judge, frame.Delta);
        }
        finally
        {
            ReleaseNativeJudgeContexts(entry, invocation.Contexts);
            Entered?.Remove(entry.Target.Address);
        }
    }
    private static void ReleaseNativeJudgeContexts(Entry entry, List<(Sink Sink, IDisposable Context)> contexts)
    {
        for (var index = contexts.Count - 1; index >= 0; --index)
            try { contexts[index].Context.Dispose(); }
            catch (Exception error) { Interlocked.Exchange(ref entry.JudgeActivityUnknown, 1); SafeLog(contexts[index].Sink, "Native judge context release failed.", error); }
    }
}
