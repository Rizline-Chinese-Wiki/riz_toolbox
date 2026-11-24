using RizToolbox.Sdk;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    internal const int NativeVoidInvocationLimit = 4096;
    internal sealed class NativeVoidInvocation
    {
        internal readonly NativeVoidFrame Frame;
        internal readonly Entry Entry;
        internal readonly nuint Token;
        internal readonly NativeVoidInvocation? ParentPhysical;
        internal readonly int OverflowDepthAtEnter;
        internal NativeVoidPhysicalStage Stage;
        internal readonly int ThreadId = Environment.CurrentManagedThreadId;
        internal readonly Sink[] Observers;
        internal BoundaryProbe[] Probes = Array.Empty<BoundaryProbe>();
        internal readonly List<(Sink Sink, IDisposable Context)> Contexts = new();
        internal NativeVoidInvocation(Entry entry, nuint token, NativeVoidFrame frame, Sink[] observers)
        {
            (Entry, Token, Frame, Observers) = (entry, token, frame, observers);
            ParentPhysical = _nativeVoidPhysicalTop; OverflowDepthAtEnter = _nativeVoidOverflowDepth;
            Stage = NativeVoidPhysicalStage.Capturing;
        }
    }

    internal Binding BindNativeVoidObservation<T>(VerifiedHookTarget target,
        Func<NativeVoidFrame, T?> capture, Action<T, NativeVoidFrame> after,
        Action<NativeVoidFrame, string> fault) where T : class, IDisposable
        => BindBootstrappedNativeVoidObservation(target, capture, after, fault, null, null).Binding;

    internal Binding BindNativeVoidObservation<T>(VerifiedHookTarget target,
        Func<INativeVoidContextBridge> createBridge, Func<NativeVoidFrame, T?> capture,
        Action<T, NativeVoidFrame> after, Action<NativeVoidFrame, string> fault) where T : class, IDisposable
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(createBridge);
        ArgumentNullException.ThrowIfNull(capture); ArgumentNullException.ThrowIfNull(after); ArgumentNullException.ThrowIfNull(fault);
        if (target.Address == IntPtr.Zero || target.MethodInfo == IntPtr.Zero || target.DeclaringClass == IntPtr.Zero
            || target.Abi != ExactHookAbi.NativeVoid0 || target.ReturnClass != IntPtr.Zero)
            throw new UnsupportedSignatureException("Native void0 observation requires its sealed exact instance void0 ABI.");
        var sink = new Sink { Id = target.Id, Main = _main, Log = _log, MethodInfo = target.MethodInfo,
            DeclaringClass = target.DeclaringClass, Matches = _ => true, Deliver = (_, _, _, _) => { },
            CaptureNativeVoid = frame => capture(frame), AfterNativeVoid = (context, frame) => after((T)context, frame), NativeVoidFault = fault };
        lock (Gate)
        {
            if (Entries.TryGetValue(target.Address, out var existing))
            {
                if (existing.Target.Abi != target.Abi || existing.Target.MethodInfo != target.MethodInfo
                    || existing.Target.DeclaringClass != target.DeclaringClass || existing.Target.ReturnClass != target.ReturnClass)
                    throw new UnsupportedSignatureException("Native void0 entry cannot share a different exact ABI or method identity.");
                if (existing.NativeInstallFailure is not null) throw new InvalidOperationException(existing.NativeInstallFailure);
                if (existing.NativeVoidBridge is null)
                    throw new UnsupportedSignatureException("Native void0 transport must own the first patch.");
                existing.Sinks.Add(sink); PublishNativeVoidObservers(existing); return Binding.Create(existing, sink);
            }
            if (_engine is not IPreparedNativeDetourEngine prepared)
                throw new UnsupportedSignatureException("Native void0 transport requires Prepare/Configure/Commit.");
            var entry = new Entry { Target = target, Engine = _engine, NativeMain = _main, NativeLog = _log };
            entry.NativeVoidBridge = createBridge() ?? throw new InvalidOperationException("No native void0 bridge.");
            if (entry.NativeVoidBridge.EntryAddress == IntPtr.Zero) throw new UnsupportedSignatureException("Native void0 entry is null.");
            entry.Sinks.Add(sink); PublishNativeVoidObservers(entry); Entries.Add(target.Address, entry);
            try
            {
                var original = prepared.Prepare(target, entry.NativeVoidBridge.EntryAddress);
                if (original == IntPtr.Zero) throw new InvalidOperationException("No original native void0 trampoline.");
                entry.NativeVoidBridge.Configure(original, frame => EnterNativeVoid(entry, frame),
                    (frame, token, flags) => ExitNativeVoid(entry, frame, token, flags));
                prepared.Commit(target); return Binding.Create(entry, sink);
            }
            catch (Exception error)
            {
                sink.Active = false; entry.Sinks.Clear(); PublishNativeVoidObservers(entry);
                entry.NativeInstallFailure = "Native void0 installation remains process-owned without retry: " + error.Message;
                Interlocked.Exchange(ref entry.NativeVoidUnknown, 1); throw;
            }
        }
    }

    private static void PublishNativeVoidObservers(Entry entry)
        => Volatile.Write(ref entry.NativeVoidObservers, entry.Sinks.Where(s => s.Active && s.CaptureNativeVoid is not null).ToArray());

    private static ManagedBoundaryActivity ReadNativeVoidActivity(Entry entry, INativeVoidContextBridge bridge)
    {
        long retained = Math.Max(0, Volatile.Read(ref entry.NativeVoidInvocationCount));
        try
        {
            var native = bridge.ReadActivity();
            if (native.InFlight >= 0)
            {
                if (entry.NativeListenerBridge is { } listener)
                {
                    var children = listener.ReadActivity();
                    retained = Math.Max(retained, Math.Max(Volatile.Read(ref entry.NativeListenerInvocationCount), children.InFlight));
                    if (!children.Known) Interlocked.Exchange(ref entry.NativeVoidUnknown, 1);
                }
                if (!native.Known) Interlocked.Exchange(ref entry.NativeVoidUnknown, 1);
                return new(Math.Max(retained, native.InFlight), native.Known && Volatile.Read(ref entry.NativeVoidUnknown) == 0
                    && Volatile.Read(ref entry.ProbeFault) == 0);
            }
        }
        catch { }
        Interlocked.Exchange(ref entry.NativeVoidUnknown, 1); return new(retained, false);
    }

    private static void NativeVoidFaults(Entry entry, Sink[] observers, NativeVoidFrame frame, string reason)
    {
        // Native workers never acquire Gate or access receiver metadata. The
        // adapter decides which scalar identity faults its own responsibility.
        foreach (var sink in observers) try { sink.NativeVoidFault?.Invoke(frame, reason); } catch { Interlocked.Exchange(ref entry.NativeVoidUnknown, 1); }
    }

    private static nuint EnterNativeVoid(Entry entry, NativeVoidFrame frame)
    {
        Interlocked.Increment(ref entry.NativeCalls); Interlocked.Increment(ref entry.OriginalCalls);
        var observers = Volatile.Read(ref entry.NativeVoidObservers);
        var reserved = Interlocked.Increment(ref entry.NativeVoidInvocationCount);
        if (reserved > NativeVoidInvocationLimit)
        {
            Interlocked.Decrement(ref entry.NativeVoidInvocationCount); Interlocked.Exchange(ref entry.NativeVoidUnknown, 1);
            MaskUntrackedNativeVoid(entry);
            NativeVoidFaults(entry, observers, frame, "Native void0 invocation capacity exhausted; this frame was not observed."); return 0;
        }
        NativeVoidInvocation? invocation = null; nuint token = 0; var registered = false;
        try
        {
            var sequence = Interlocked.Increment(ref entry.NativeVoidSequence);
            if (sequence <= 0) throw new InvalidOperationException("Native void0 invocation sequence exhausted.");
            token = checked((nuint)sequence); invocation = new(entry, token, frame, observers);
            if (!entry.NativeVoidInvocations.TryAdd(token, invocation)) throw new InvalidOperationException("Native void0 invocation token collided.");
            registered = true;
            _nativeVoidPhysicalTop = invocation;
            // Retain the physical probe snapshot before validation or capture.
            // This includes workers, foreign metadata and logical detachment.
            invocation.Probes = EnterProbes(entry, frame.Self, 0, frame.Method);
            if (!frame.IsValid)
            {
                Interlocked.Exchange(ref entry.NativeVoidUnknown, 1);
                NativeVoidFaults(entry, observers, frame, "Invalid native void0 frame."); return token;
            }
            if (Volatile.Read(ref entry.ProbeFault) != 0)
            {
                Interlocked.Exchange(ref entry.NativeVoidUnknown, 1);
                NativeVoidFaults(entry, observers, frame, "Native void0 physical probe failed."); return token;
            }
            if (!entry.NativeMain!.IsMainThread)
            {
                Interlocked.Increment(ref entry.WrongThread);
                NativeVoidFaults(entry, observers, frame, "Native void0 invoked off the verified main thread."); return token;
            }
            if (frame.Method != IntPtr.Zero && frame.Method != entry.Target.MethodInfo)
            { NativeVoidFaults(entry, observers, frame, "Foreign native void0 MethodInfo alias."); return token; }
            foreach (var sink in observers)
            {
                if (!sink.Active) continue;
                try
                {
                    var context = sink.CaptureNativeVoid!(frame);
                    if (context is not null)
                    {
                        try { invocation.Contexts.Add((sink, context)); }
                        catch { context.Dispose(); throw; }
                    }
                }
                catch (Exception error)
                {
                    Interlocked.Exchange(ref entry.NativeVoidUnknown, 1);
                    SafeLog(sink, "Native void0 capture failed; original is preserved.", error);
                    NativeVoidFaults(entry, new[] { sink }, frame, "Native void0 capture failed.");
                }
            }
            return token;
        }
        catch (Exception error)
        {
            Interlocked.Exchange(ref entry.NativeVoidUnknown, 1);
            NativeVoidFaults(entry, observers, frame, "Native void0 dispatch failed; original is preserved.");
            try { entry.NativeLog!.Log(LogLevel.Error, "Native void0 dispatch failed; original is preserved.", error); } catch { }
            // If probes entered, their snapshot must survive until native Exit.
            // Failure before publication has no observation to pretend to pair.
            if (registered) return token;
            Interlocked.Decrement(ref entry.NativeVoidInvocationCount); MaskUntrackedNativeVoid(entry); return 0;
        }
        finally { if (registered) invocation!.Stage = NativeVoidPhysicalStage.OriginalWindow; }
    }

    private static void ExitNativeVoid(Entry entry, NativeVoidFrame frame, nuint token, NativeVoidExitFlags flags)
    {
        if (token == 0) { ExitUntrackedNativeVoid(entry, frame); return; }
        if (!entry.NativeVoidInvocations.TryGetValue(token, out var invocation))
        {
            Interlocked.Exchange(ref entry.NativeVoidUnknown, 1);
            NativeVoidFaults(entry, Volatile.Read(ref entry.NativeVoidObservers), frame, "Unknown native void0 exit token."); return;
        }
        if (invocation.ThreadId != Environment.CurrentManagedThreadId)
        {
            // An impossible cross-thread exit cannot grant root-release authority.
            // Retain the complete record and make activity permanently unknown.
            Interlocked.Exchange(ref entry.NativeVoidUnknown, 1);
            NativeVoidFaults(entry, invocation.Observers, frame, "Native void0 exit crossed its physical calling thread."); return;
        }
        if (!IsCurrentNativeVoidPhysical(entry, invocation)) return;
        invocation.Stage = NativeVoidPhysicalStage.Exiting;
        if (!entry.NativeVoidInvocations.TryRemove(token, out _))
        {
            Interlocked.Exchange(ref entry.NativeVoidUnknown, 1);
            NativeVoidFaults(entry, invocation.Observers, frame, "Native void0 exit token was consumed twice."); return;
        }
        var returned = flags == NativeVoidExitFlags.Returned && SameNativeVoidFrame(frame, invocation.Frame);
        if (!returned)
        {
            Interlocked.Exchange(ref entry.NativeVoidUnknown, 1);
            NativeVoidFaults(entry, invocation.Observers, frame, "Native void0 original exited abnormally or its physical frame changed.");
        }
        try
        {
            if (returned)
                foreach (var item in invocation.Contexts)
                    if (item.Sink.Active)
                        try { item.Sink.AfterNativeVoid!(item.Context, frame); }
                        catch (Exception error)
                        {
                            Interlocked.Exchange(ref entry.NativeVoidUnknown, 1);
                            SafeLog(item.Sink, "Native void0 after observation failed.", error);
                            NativeVoidFaults(entry, new[] { item.Sink }, frame, "Native void0 after observation failed.");
                        }
        }
        finally
        {
            try
            {
                for (var index = invocation.Contexts.Count - 1; index >= 0; --index)
                    try { invocation.Contexts[index].Context.Dispose(); }
                    catch (Exception error)
                    {
                        var sink = invocation.Contexts[index].Sink; Interlocked.Exchange(ref entry.NativeVoidUnknown, 1);
                        SafeLog(sink, "Native void0 context release failed.", error);
                        NativeVoidFaults(entry, new[] { sink }, frame, "Native void0 context release failed.");
                    }
            }
            finally
            {
                try
                {
                    ExitProbes(entry, invocation.Probes, invocation.Frame.Self, 0, invocation.Frame.Method, returned);
                    if (Volatile.Read(ref entry.ProbeFault) != 0)
                    {
                        Interlocked.Exchange(ref entry.NativeVoidUnknown, 1);
                        NativeVoidFaults(entry, invocation.Observers, frame, "Native void0 physical probe failed.");
                    }
                }
                finally
                {
                    try { FinishNativeVoidPhysical(entry, invocation); }
                    finally { Interlocked.Decrement(ref entry.NativeVoidInvocationCount); }
                }
            }
        }
    }
    private static bool SameNativeVoidFrame(NativeVoidFrame actual, NativeVoidFrame original)
        => actual.Version == original.Version && actual.Size == original.Size && actual.Reserved0 == original.Reserved0
            && actual.Reserved1 == original.Reserved1 && actual.Self == original.Self && actual.Method == original.Method && actual.Caller == original.Caller;
}
