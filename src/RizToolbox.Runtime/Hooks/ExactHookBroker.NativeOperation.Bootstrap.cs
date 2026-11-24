namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    private static readonly Dictionary<Entry, NativeOperationChannelIdentity> OperationAdoptedIdentities = new();

    private Binding BindBootstrappedNativeOperationObservation<T>(VerifiedHookTarget target, NativeOperationKind kind,
        Func<NativeOperationFrame, T?> capture, Action<T, NativeOperationFrame, NativeOperationExitFlags> complete,
        Action<NativeOperationFrame, string> fault) where T : class, IDisposable
    {
        ValidateConfiguredNativeOperationTarget(target, kind);
        var producer = NativeFirstInstall.RequireReady();
        var identity = producer.OperationIdentity(target, kind);
        lock (Gate)
        {
            producer.VerifyReady();
            var binding = BindConfiguredNativeOperationObservation(target, identity,
                () => NativeBootstrappedOperationBridge.AdoptBootstrap(producer.HelperPath, identity, _log), capture, complete, fault);
            try { producer.VerifyReady(); return binding; }
            catch
            {
                // The native slot/observer remains permanently owned. Revoke
                // this attempt's logical delivery, never peers' cleanup duty.
                if (Entries.TryGetValue(target.Address, out var entry)) Interlocked.Exchange(ref entry.OperationUnknown, 1);
                binding.Dispose(); throw;
            }
        }
    }

    // Internal configured-channel reuse is not a FirstInstall authority. The
    // production front above always checks the actual V5 receipt before/after.
    internal Binding BindConfiguredNativeOperationObservation<T>(VerifiedHookTarget target, NativeOperationChannelIdentity identity,
        Func<NativeBootstrappedOperationBridge> adoptBridge, Func<NativeOperationFrame, T?> capture,
        Action<T, NativeOperationFrame, NativeOperationExitFlags> complete,
        Action<NativeOperationFrame, string> fault) where T : class, IDisposable
    {
        ValidateConfiguredNativeOperationTarget(target, identity.Kind); ArgumentNullException.ThrowIfNull(adoptBridge);
        ArgumentNullException.ThrowIfNull(capture); ArgumentNullException.ThrowIfNull(complete); ArgumentNullException.ThrowIfNull(fault);
        if (!identity.IsComplete || identity.Target != target.Address)
            throw new UnsupportedSignatureException("Operation adoption requires the complete exact configured native identity.");
        var sink = new Sink { Id = target.Id, Main = _main, Log = _log, MethodInfo = target.MethodInfo, DeclaringClass = target.DeclaringClass,
            Matches = _ => true, Deliver = (_, _, _, _) => { }, CaptureOperation = frame => capture(frame),
            CompleteOperation = (context, frame, flags) => complete((T)context, frame, flags), OperationFault = fault };
        lock (Gate)
        {
            if (Entries.TryGetValue(target.Address, out var existing))
            {
                if (existing.Target.Abi != target.Abi || existing.Target.MethodInfo != target.MethodInfo
                    || existing.Target.DeclaringClass != target.DeclaringClass || existing.Target.ReturnClass != target.ReturnClass
                    || existing.OperationBridge is not NativeBootstrappedOperationBridge bridge
                    || bridge.Identity != identity || !OperationAdoptedIdentities.TryGetValue(existing, out var actual) || actual != identity)
                    throw new UnsupportedSignatureException("Operation cannot share a different metadata, slot, original, entry or epoch.");
                if (existing.NativeInstallFailure is not null) throw new InvalidOperationException(existing.NativeInstallFailure);
                if (!ReadOperationActivity(existing, bridge).Known)
                    throw new UnsupportedSignatureException("The shared native operation retains unknown physical responsibility.");
                existing.Sinks.Add(sink); PublishOperationObservers(existing); return Binding.Create(existing, sink);
            }
            var entry = new Entry { Target = target, Engine = _engine, NativeMain = _main, NativeLog = _log };
            var adopted = adoptBridge() ?? throw new InvalidOperationException("Missing configured native operation bridge.");
            if (adopted.Identity != identity || adopted.Kind != identity.Kind || adopted.EntryAddress != identity.Entry)
                throw new UnsupportedSignatureException("The adopted operation bridge differs from its actual native channel identity.");
            entry.OperationBridge = adopted; entry.Sinks.Add(sink); PublishOperationObservers(entry);
            Entries.Add(target.Address, entry); OperationAdoptedIdentities.Add(entry, identity);
            try
            {
                adopted.Configure(identity.Original, frame => EnterOperation(entry, frame),
                    (frame, token, flags) => ExitOperation(entry, frame, token, flags));
                if (!ReadOperationActivity(entry, adopted).Known)
                    throw new UnsupportedSignatureException("The operation observer became unknown during Attach publication.");
                return Binding.Create(entry, sink);
            }
            catch (Exception error)
            {
                sink.Active = false; entry.Sinks.Clear(); PublishOperationObservers(entry);
                entry.NativeInstallFailure = "Native operation adoption remains permanently owned without retry: " + error.Message;
                Interlocked.Exchange(ref entry.OperationUnknown, 1); throw;
            }
        }
    }
    private void ValidateConfiguredNativeOperationTarget(VerifiedHookTarget target, NativeOperationKind kind)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(target);
        if (target.Address == IntPtr.Zero || target.MethodInfo == IntPtr.Zero || target.DeclaringClass == IntPtr.Zero
            || target.Abi != OperationAbi(kind) || target.ReturnClass != IntPtr.Zero)
            throw new UnsupportedSignatureException("Operation adoption requires its sealed exact instance void/reference ABI.");
    }
}
