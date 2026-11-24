namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    private sealed record NativeListenerCallbacks(Action<IDisposable, NativeListenerObservation, NativeVoidPhysicalIdentity> Before,
        Action<IDisposable, NativeListenerObservation, bool> After);
    private sealed record NativeListenerRegistration(NativeListenerAdoptionIdentity Identity, Func<NativeListenerContextBridge> Adopt);
    internal sealed class NativeListenerInvocation
    {
        internal readonly NativeVoidInvocation Parent;
        internal readonly NativeListenerFrame Before;
        internal readonly List<(Sink Sink, IDisposable Context, bool Captured)> Contexts = new();
        internal readonly int Thread = Environment.CurrentManagedThreadId;
        internal NativeListenerInvocation(NativeVoidInvocation parent, NativeListenerFrame before) => (Parent, Before) = (parent, before);
    }
    internal (Binding Binding, BoundaryProbeBinding Probe) BindNativeVoidObservationWithListeners<T>(VerifiedHookTarget target,
        Func<NativeVoidFrame, T?> capture, Action<T, NativeVoidFrame> after, Action<NativeVoidFrame, string> fault,
        Action<T, NativeEventMidFrame, NativeVoidPhysicalIdentity> mid,
        Action<T, NativeListenerObservation, NativeVoidPhysicalIdentity> listenerBefore, Action<T, NativeListenerObservation, bool> listenerAfter,
        Action<IntPtr, byte, IntPtr> probeEnter, Action<IntPtr, byte, IntPtr, bool> probeExit) where T : class, IDisposable
    {
        ArgumentNullException.ThrowIfNull(listenerBefore); ArgumentNullException.ThrowIfNull(listenerAfter);
        var result = BindBootstrappedNativeVoidObservation(target, capture, after, fault,
            (context, frame, identity) => mid((T)context, frame, identity), new(probeEnter, probeExit),
            new((context, observation, identity) => listenerBefore((T)context, observation, identity),
                (context, observation, captured) => listenerAfter((T)context, observation, captured)));
        return (result.Binding, result.Probe!);
    }
    // Configured-channel front used by the same production dispatch core. It
    // supplies no first-install Ready and cannot adopt the game front implicitly.
    internal (Binding Binding, BoundaryProbeBinding Probe) BindConfiguredNativeVoidObservationWithListeners<T>(VerifiedHookTarget target,
        NativeVoidAdoptionIdentity parent, Func<NativeBootstrappedVoidBridge> adoptParent,
        NativeListenerAdoptionIdentity lane, Func<NativeListenerContextBridge> adoptListener,
        Func<NativeVoidFrame, T?> capture, Action<T, NativeVoidFrame> after, Action<NativeVoidFrame, string> fault,
        Action<T, NativeEventMidFrame, NativeVoidPhysicalIdentity> mid,
        Action<T, NativeListenerObservation, NativeVoidPhysicalIdentity> before, Action<T, NativeListenerObservation, bool> completed,
        Action<IntPtr, byte, IntPtr> probeEnter, Action<IntPtr, byte, IntPtr, bool> probeExit) where T : class, IDisposable
    {
        ArgumentNullException.ThrowIfNull(before); ArgumentNullException.ThrowIfNull(completed); ArgumentNullException.ThrowIfNull(adoptListener);
        var result = BindConfiguredNativeVoidObservation(target, parent, adoptParent, capture, after, fault,
            (context, frame, identity) => mid((T)context, frame, identity), new(probeEnter, probeExit),
            new((context, frame, identity) => before((T)context, frame, identity), (context, frame, captured) => completed((T)context, frame, captured)),
            new(lane, adoptListener));
        return (result.Binding, result.Probe!);
    }
    private NativeListenerRegistration ListenerRegistration(NativeFirstInstall producer)
    {
        var p = producer.ListenerPublication;
        var identity = new NativeListenerAdoptionIdentity(p.ListenerSlot, p.ListenerEpoch, p.Common.UnityEpoch,
            p.PreEntry, p.PostEntry, p.SourceReturnPc0, p.SourceReturnPc1, p.SourceReturnPc2,
            p.PostTransportReturnPc0, p.PostTransportReturnPc1, p.PostTransportReturnPc2, p.DirectTarget0, p.DirectTarget1);
        return new(identity, () => { producer.VerifyReady(); return NativeListenerContextBridge.AdoptConfigured(producer.HelperPath, identity, _log); });
    }
    private static void EnsureListener(Entry entry, NativeListenerRegistration registration)
    {
        var parent = NativeVoidAdoptedIdentities[entry];
        if (!registration.Identity.IsComplete || registration.Identity.Slot != parent.Slot || registration.Identity.ParentEpoch != parent.Epoch)
            throw new UnsupportedSignatureException("The listener lane belongs to a different configured physical parent.");
        if (entry.NativeListenerBridge is not null)
        {
            if (entry.NativeListenerIdentity != registration.Identity || !entry.NativeListenerBridge.ReadActivity().Known)
                throw new UnsupportedSignatureException("The shared listener lane changed its full native identity.");
            return;
        }
        var bridge = registration.Adopt();
        entry.NativeListenerBridge = bridge; entry.NativeListenerIdentity = registration.Identity;
        if (bridge.Identity != registration.Identity) throw new UnsupportedSignatureException("The adopted listener bridge differs from its lane.");
        bridge.Attach(observation => EnterNativeListener(entry, observation),
            (observation, token) => ExitNativeListener(entry, observation, token),
            reason =>
            {
                Interlocked.Exchange(ref entry.NativeVoidUnknown, 1);
                var parentCall = _nativeVoidPhysicalTop;
                var frame = parentCall is not null && ReferenceEquals(parentCall.Entry, entry)
                    ? parentCall.Frame : new NativeVoidFrame { Version = 1, Size = 40 };
                NativeVoidFaults(entry, Volatile.Read(ref entry.NativeVoidObservers), frame, reason);
            });
    }
    private static bool ListenerParentMatches(Entry entry, NativeListenerFrame frame, NativeVoidInvocation? parent)
        => !_nativeVoidPhysicalCorrupt && _nativeVoidOverflowDepth == 0 && parent is not null
        && ReferenceEquals(_nativeVoidPhysicalTop, parent) && ReferenceEquals(parent.Entry, entry)
        && parent.Stage == NativeVoidPhysicalStage.OriginalWindow && parent.ThreadId == Environment.CurrentManagedThreadId
        && unchecked((ulong)parent.Token) == frame.ParentManagedToken && parent.Frame.Self == frame.Event
        && entry.NativeVoidInvocations.TryGetValue(parent.Token, out var retained) && ReferenceEquals(parent, retained);
    private static nuint EnterNativeListener(Entry entry, NativeListenerObservation observation)
    {
        var frame = observation.Frame; var parent = _nativeVoidPhysicalTop;
        // A physical parent with a null V1 observer can legally have token 0.
        // Do not search ancestors or invent a managed capture in that case.
        if (frame.ParentManagedToken == 0 || entry.NativeMain?.IsMainThread != true
            || !ListenerParentMatches(entry, frame, parent) || parent!.Frame.Method != IntPtr.Zero && parent.Frame.Method != entry.Target.MethodInfo)
            return 0;
        if (!ReadNativeVoidActivity(entry, entry.NativeVoidBridge!).Known) return 0;
        if (Interlocked.Increment(ref entry.NativeListenerInvocationCount) > NativeVoidInvocationLimit)
        { Interlocked.Decrement(ref entry.NativeListenerInvocationCount); ListenerFault(entry, parent, "Listener managed child capacity exhausted."); return 0; }
        var call = new NativeListenerInvocation(parent, frame);
        nuint token = 0; var registered = false;
        try
        {
            var sequence = Interlocked.Increment(ref entry.NativeListenerSequence);
            if (sequence <= 0) throw new InvalidOperationException("Listener managed child sequence exhausted.");
            token = checked((nuint)sequence);
            if (!entry.NativeListenerInvocations.TryAdd(token, call)) throw new InvalidOperationException("Listener managed child token collided.");
            registered = true;
            foreach (var item in parent.Contexts)
            {
                if (!item.Sink.Active || item.Sink.BeforeNativeListener is not { } before) continue;
                var captured = false;
                // Record cleanup responsibility BEFORE entering the observer.
                call.Contexts.Add((item.Sink, item.Context, false));
                try
                {
                    if (!NativeVoidPhysicalIdentity.TryGet<IDisposable>(entry, item.Sink, frame.Event, out var context, out var identity)
                        || identity is null || !ReferenceEquals(context, item.Context))
                        throw new InvalidOperationException("Listener observation has no exact current retained parent.");
                    before(item.Context, observation, identity); captured = true;
                }
                catch (Exception error)
                {
                    ListenerFault(entry, parent, "Listener Before failed; its attempted observation remains unconfirmed.");
                    SafeLog(item.Sink, "Listener Before failed; native original is preserved.", error);
                }
                call.Contexts[^1] = (item.Sink, item.Context, captured);
            }
            return token;
        }
        catch
        {
            ListenerFault(entry, parent, "Listener child registration failed.");
            if (!registered) Interlocked.Decrement(ref entry.NativeListenerInvocationCount);
            return registered ? token : 0;
        }
    }
    private static void ExitNativeListener(Entry entry, NativeListenerObservation observation, nuint token)
    {
        var frame = observation.Frame;
        if (token == 0)
        {
            // Target-read faults have no managed Before. Fault only the exact
            // currently represented parent; never borrow or dispose ancestors.
            if ((frame.Flags & NativeListenerFlags.TargetReadFaulted) != 0 && ListenerParentMatches(entry, frame, _nativeVoidPhysicalTop))
                ListenerFault(entry, _nativeVoidPhysicalTop!, "Listener virtual target read faulted before managed capture.");
            return;
        }
        if (!entry.NativeListenerInvocations.TryGetValue(token, out var call) || call.Thread != Environment.CurrentManagedThreadId
            || !ListenerParentMatches(entry, frame, call.Parent) || !SameListenerChild(call.Before, frame))
        { Interlocked.Exchange(ref entry.NativeVoidUnknown, 1); return; }
        if (!entry.NativeListenerInvocations.TryRemove(token, out _)) { ListenerFault(entry, call.Parent, "Listener child completed twice."); return; }
        try
        {
            foreach (var item in call.Contexts)
                try { item.Sink.AfterNativeListener?.Invoke(item.Context, observation, item.Captured); }
                catch (Exception error)
                { ListenerFault(entry, call.Parent, "Listener completion failed; responsibility remains unknown."); SafeLog(item.Sink, "Listener completion failed.", error); }
        }
        finally { Interlocked.Decrement(ref entry.NativeListenerInvocationCount); }
    }
    private static bool SameListenerChild(NativeListenerFrame before, NativeListenerFrame after)
        => before.Slot == after.Slot && before.Site == after.Site && before.Epoch == after.Epoch
        && before.ParentSequence == after.ParentSequence && before.ChildSequence == after.ChildSequence
        && before.ParentManagedToken == after.ParentManagedToken && before.Event == after.Event && before.List == after.List
        && before.Invokable == after.Invokable && before.Index == after.Index && before.SourceRsp == after.SourceRsp
        && before.ActualTarget == after.ActualTarget && before.SourceReturnPc == after.SourceReturnPc
        && before.ArgRcx == after.ArgRcx && before.ArgRdx == after.ArgRdx && before.ArgR8 == after.ArgR8 && before.ArgR9 == after.ArgR9;
    private static void ListenerFault(Entry entry, NativeVoidInvocation parent, string reason)
    { Interlocked.Exchange(ref entry.NativeVoidUnknown, 1); NativeVoidFaults(entry, parent.Observers, parent.Frame, reason); }
}
