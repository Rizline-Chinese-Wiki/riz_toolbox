namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    // Identity of an already configured native channel, not a first-install
    // receipt. Only the production front below can authorize a game adoption.
    // Info56 exposes the other fields; MidReturnPc comes from that caller's
    // publication and is checked again on the actual native Mid callback.
    internal readonly record struct NativeVoidAdoptionIdentity(uint Slot, IntPtr Target, IntPtr Original,
        IntPtr Entry, ulong Epoch, IntPtr MidReturnPc);
    private static readonly Dictionary<Entry, NativeVoidAdoptionIdentity> NativeVoidAdoptedIdentities = new();

    /// <summary>Prepares the retained observer and physical probe together before
    /// the one native Attach publication. No callback can see an observer whose
    /// required physical probe has not yet been prepared.</summary>
    internal (Binding Binding, BoundaryProbeBinding Probe) BindNativeVoidObservationWithMid<T>(VerifiedHookTarget target,
        Func<NativeVoidFrame, T?> capture, Action<T, NativeVoidFrame> after, Action<NativeVoidFrame, string> fault,
        Action<T, NativeEventMidFrame, NativeVoidPhysicalIdentity> mid,
        Action<IntPtr, byte, IntPtr> probeEnter, Action<IntPtr, byte, IntPtr, bool> probeExit) where T : class, IDisposable
    {
        ArgumentNullException.ThrowIfNull(mid); ArgumentNullException.ThrowIfNull(probeEnter); ArgumentNullException.ThrowIfNull(probeExit);
        var result = BindBootstrappedNativeVoidObservation(target, capture, after, fault,
            (context, frame, identity) => mid((T)context, frame, identity), new(probeEnter, probeExit));
        return (result.Binding, result.Probe!);
    }
    private (Binding Binding, BoundaryProbeBinding? Probe) BindBootstrappedNativeVoidObservation<T>(VerifiedHookTarget target,
        Func<NativeVoidFrame, T?> capture, Action<T, NativeVoidFrame> after, Action<NativeVoidFrame, string> fault,
        Action<IDisposable, NativeEventMidFrame, NativeVoidPhysicalIdentity>? mid, BoundaryProbe? probe,
        NativeListenerCallbacks? listener = null) where T : class, IDisposable
    {
        ValidateConfiguredNativeVoidTarget(target);
        var producer = NativeFirstInstall.RequireReady();
        if (target.Address != producer.UnityTarget)
            throw new UnsupportedSignatureException("The native first-install receipt does not own this exact UnityEvent target.");
        lock (Gate)
        {
            producer.VerifyReady();
            var identity = new NativeVoidAdoptionIdentity(producer.VoidSlot, producer.UnityTarget, producer.UnityOriginal,
                producer.UnityEntry, producer.UnityEpoch, producer.MidReturnPc);
            var result = BindConfiguredNativeVoidObservation(target, identity,
                () => NativeBootstrappedVoidBridge.AdoptBootstrap(producer.HelperPath, producer.VoidSlot,
                    producer.UnityTarget, producer.UnityOriginal, producer.UnityEpoch, _log),
                capture, after, fault, mid, probe, listener, listener is null ? null : ListenerRegistration(producer));
            try { producer.VerifyReady(); return result; }
            catch
            {
                // Publication is permanent. Revoke only this call's logical
                // delivery and retain unknown native responsibility for peers.
                if (Entries.TryGetValue(target.Address, out var entry)) Interlocked.Exchange(ref entry.NativeVoidUnknown, 1);
                try { result.Binding.Dispose(); }
                finally { result.Probe?.Dispose(); }
                throw;
            }
        }
    }

    /// <summary>Adopts an actual configured channel without publishing native
    /// instructions. The identity is checked against the bridge's native read;
    /// it grants no game first-install permission. Production callers enter only
    /// through the receipt-checked front above.</summary>
    internal (Binding Binding, BoundaryProbeBinding Probe) BindConfiguredNativeVoidObservationWithMid<T>(VerifiedHookTarget target,
        NativeVoidAdoptionIdentity identity, Func<NativeBootstrappedVoidBridge> adoptBridge,
        Func<NativeVoidFrame, T?> capture, Action<T, NativeVoidFrame> after, Action<NativeVoidFrame, string> fault,
        Action<T, NativeEventMidFrame, NativeVoidPhysicalIdentity> mid,
        Action<IntPtr, byte, IntPtr> probeEnter, Action<IntPtr, byte, IntPtr, bool> probeExit) where T : class, IDisposable
    {
        ArgumentNullException.ThrowIfNull(mid); ArgumentNullException.ThrowIfNull(probeEnter); ArgumentNullException.ThrowIfNull(probeExit);
        var result = BindConfiguredNativeVoidObservation(target, identity, adoptBridge, capture, after, fault,
            (context, frame, physical) => mid((T)context, frame, physical), new(probeEnter, probeExit));
        return (result.Binding, result.Probe!);
    }

    private void ValidateConfiguredNativeVoidTarget(VerifiedHookTarget target)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(target);
        if (target.Address == IntPtr.Zero || target.MethodInfo == IntPtr.Zero || target.DeclaringClass == IntPtr.Zero
            || target.Abi != ExactHookAbi.NativeVoid0 || target.ReturnClass != IntPtr.Zero)
            throw new UnsupportedSignatureException("Native void0 bootstrap adoption requires its sealed exact instance void0 ABI.");
    }

    private (Binding Binding, BoundaryProbeBinding? Probe) BindConfiguredNativeVoidObservation<T>(VerifiedHookTarget target,
        NativeVoidAdoptionIdentity identity, Func<NativeBootstrappedVoidBridge> adoptBridge,
        Func<NativeVoidFrame, T?> capture, Action<T, NativeVoidFrame> after, Action<NativeVoidFrame, string> fault,
        Action<IDisposable, NativeEventMidFrame, NativeVoidPhysicalIdentity>? mid, BoundaryProbe? probe,
        NativeListenerCallbacks? listener = null, NativeListenerRegistration? registration = null) where T : class, IDisposable
    {
        ValidateConfiguredNativeVoidTarget(target); ArgumentNullException.ThrowIfNull(adoptBridge);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(after); ArgumentNullException.ThrowIfNull(fault);
        if (identity.Slot >= 32 || identity.Target != target.Address || identity.Original == IntPtr.Zero
            || identity.Entry == IntPtr.Zero || identity.Epoch == 0 || identity.MidReturnPc == IntPtr.Zero)
            throw new UnsupportedSignatureException("Configured native adoption requires the full nonzero channel identity.");
        var sink = new Sink { Id = target.Id, Main = _main, Log = _log, MethodInfo = target.MethodInfo,
            DeclaringClass = target.DeclaringClass, Matches = _ => true, Deliver = (_, _, _, _) => { },
            CaptureNativeVoid = frame => capture(frame), AfterNativeVoid = (context, frame) => after((T)context, frame),
            NativeVoidFault = fault, ObserveNativeVoidMid = mid,
            BeforeNativeListener = listener?.Before, AfterNativeListener = listener?.After };
        lock (Gate)
        {
            if (Entries.TryGetValue(target.Address, out var existing))
            {
                var sharedBridge = existing.NativeVoidBridge as NativeBootstrappedVoidBridge;
                if (existing.Target.Abi != target.Abi || existing.Target.MethodInfo != target.MethodInfo
                    || existing.Target.DeclaringClass != target.DeclaringClass || existing.Target.ReturnClass != target.ReturnClass
                    || sharedBridge is null || !NativeVoidAdoptedIdentities.TryGetValue(existing, out var sharedIdentity)
                    || sharedIdentity != identity || !MatchesConfiguredNativeVoidBridge(sharedBridge, identity))
                    throw new UnsupportedSignatureException("The bootstrap entry cannot share a different exact ABI, metadata or transport.");
                if (existing.NativeInstallFailure is not null) throw new InvalidOperationException(existing.NativeInstallFailure);
                if (!ReadNativeVoidActivity(existing, sharedBridge).Known)
                    throw new UnsupportedSignatureException("The shared native bootstrap entry has unknown physical responsibility.");
                if (registration is not null) EnsureListener(existing, registration);
                var addedProbe = probe is null ? null : new BoundaryProbeBinding(existing, probe, _main);
                if (probe is not null) Volatile.Write(ref existing.Probes, existing.Probes.Append(probe).ToArray());
                existing.Sinks.Add(sink); PublishNativeVoidObservers(existing);
                return (Binding.Create(existing, sink), addedProbe);
            }
            var entry = new Entry { Target = target, Engine = _engine, NativeMain = _main, NativeLog = _log };
            var bridge = adoptBridge() ?? throw new InvalidOperationException("No configured native bootstrap bridge.");
            if (!MatchesConfiguredNativeVoidBridge(bridge, identity))
                throw new UnsupportedSignatureException("The adopted native bridge differs from the exact configured channel identity.");
            entry.NativeVoidBridge = bridge; entry.Sinks.Add(sink); PublishNativeVoidObservers(entry);
            BoundaryProbeBinding? ownedProbe = null;
            if (probe is not null) { entry.Probes = new[] { probe }; ownedProbe = new(entry, probe, _main); }
            Entries.Add(target.Address, entry);
            NativeVoidAdoptedIdentities.Add(entry, identity);
            try
            {
                if (registration is not null) EnsureListener(entry, registration);
                bridge.ConfigureMid(frame => ObserveBootstrappedNativeVoidMid(entry, frame, identity.MidReturnPc));
                bridge.Configure(identity.Original, frame => EnterNativeVoid(entry, frame),
                    (frame, token, flags) => ExitNativeVoid(entry, frame, token, flags));
                return (Binding.Create(entry, sink), ownedProbe);
            }
            catch (Exception error)
            {
                sink.Active = false; entry.Sinks.Clear(); PublishNativeVoidObservers(entry);
                entry.NativeInstallFailure = "Native bootstrap observation remains process-owned without retry: " + error.Message;
                Interlocked.Exchange(ref entry.NativeVoidUnknown, 1); throw;
            }
        }
    }
    private static bool MatchesConfiguredNativeVoidBridge(NativeBootstrappedVoidBridge bridge, NativeVoidAdoptionIdentity identity)
        => bridge.Slot == identity.Slot && bridge.TargetAddress == identity.Target && bridge.OriginalAddress == identity.Original
            && bridge.EntryAddress == identity.Entry && bridge.Epoch == identity.Epoch;
    private static void ObserveBootstrappedNativeVoidMid(Entry entry, NativeEventMidFrame frame, IntPtr expectedReturnPc)
    {
        var invocation = _nativeVoidPhysicalTop;
        if (invocation is null || !ReferenceEquals(invocation.Entry, entry) || entry.NativeMain?.IsMainThread != true
            || invocation.Stage != NativeVoidPhysicalStage.OriginalWindow) return;
        if (!frame.IsValid || frame.ReturnPc != expectedReturnPc || frame.Event != invocation.Frame.Self)
        {
            Interlocked.Exchange(ref entry.NativeVoidUnknown, 1);
            NativeVoidFaults(entry, invocation.Observers, invocation.Frame, "Native mid frame differs from its fixed current parent."); return;
        }
        foreach (var item in invocation.Contexts)
        {
            if (!item.Sink.Active || item.Sink.ObserveNativeVoidMid is not { } observe) continue;
            try
            {
                if (!NativeVoidPhysicalIdentity.TryGet<IDisposable>(entry, item.Sink, frame.Event, out var context, out var identity)
                    || !ReferenceEquals(context, item.Context) || identity is null)
                    throw new InvalidOperationException("Native mid observation has no exact current retained context.");
                observe(item.Context, frame, identity);
            }
            catch (Exception error)
            {
                Interlocked.Exchange(ref entry.NativeVoidUnknown, 1);
                SafeLog(item.Sink, "Native mid observation failed; original continuation is preserved.", error);
                NativeVoidFaults(entry, new[] { item.Sink }, invocation.Frame, "Native mid observation failed.");
            }
        }
    }
}
