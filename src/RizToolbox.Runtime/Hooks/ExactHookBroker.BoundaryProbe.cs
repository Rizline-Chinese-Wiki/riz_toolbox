using RizToolbox.Sdk;

namespace RizToolbox.Runtime.Hooks;

internal sealed partial class ExactHookBroker
{
    // Ownership ledgers need their state capture before public Before delivery.
    // Existing retained observers keep their established Before -> retain order.
    internal Binding BindEarlyRetainedObservation<T>(VerifiedHookTarget target, Func<IntPtr, bool> matches,
        Func<IntPtr, T> capture, Action<T> after) where T : class, IDisposable
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(matches); ArgumentNullException.ThrowIfNull(capture); ArgumentNullException.ThrowIfNull(after);
        if (target.Abi != ExactHookAbi.InstanceVoid || target.MethodInfo == IntPtr.Zero || target.DeclaringClass == IntPtr.Zero)
            throw new UnsupportedSignatureException("Early retained capture requires an exact void instance target.");
        return BindInternal(target, matches, (_, _, _, _) => { },
            self => capture(self) ?? throw new InvalidOperationException("Early retained context is null."), null, null,
            afterRetained: context => after((T)context), captureBeforeDelivery: true);
    }
    // These callbacks run on every calling thread, including suppressed reentry.
    // They may only record managed scalar identities/counters. No Unity/IL2CPP
    // access, user delivery, native mutation or exception may cross this lane.
    internal sealed record BoundaryProbe(Action<IntPtr, byte, IntPtr> Enter,
        Action<IntPtr, byte, IntPtr, bool> Exit);
    internal sealed class BoundaryProbeBinding : IDisposable
    {
        private readonly Entry _entry; private readonly BoundaryProbe _probe;
        private readonly IMainThreadDispatcher _main; private bool _disposed;
        internal BoundaryProbeBinding(Entry entry, BoundaryProbe probe, IMainThreadDispatcher main)
            => (_entry, _probe, _main) = (entry, probe, main);
        internal ManagedBoundaryActivity ReadActivity()
        {
            var count = Interlocked.Read(ref _entry.VoidInFlight);
            var known = _entry.NativeInstallFailure is null && Volatile.Read(ref _entry.ProbeFault) == 0;
            if (_entry.NativeVoidBridge is { } bridge)
            { var native = ReadNativeVoidActivity(_entry, bridge); count = Math.Max(count, native.InFlight); known &= native.Known; }
            return new(count, known);
        }
        public void Dispose()
        {
            _main.VerifyAccess(); if (_disposed) return;
            lock (Gate) { Volatile.Write(ref _entry.Probes, _entry.Probes.Where(item => !ReferenceEquals(item, _probe)).ToArray()); _disposed = true; }
        }
    }
    internal BoundaryProbeBinding BindBoundaryProbe(VerifiedHookTarget target,
        Action<IntPtr, byte, IntPtr> enter, Action<IntPtr, byte, IntPtr, bool> exit)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(enter); ArgumentNullException.ThrowIfNull(exit);
        if (target.Abi is not (ExactHookAbi.InstanceVoid or ExactHookAbi.InstanceBooleanVoid or ExactHookAbi.NativeVoid0))
            throw new UnsupportedSignatureException("Boundary probes require the exact void or void(Boolean) transport.");
        lock (Gate)
        {
            if (!Entries.TryGetValue(target.Address, out var entry) || entry.NativeInstallFailure is not null
                || entry.Target.MethodInfo != target.MethodInfo || entry.Target.DeclaringClass != target.DeclaringClass
                || entry.Target.Abi != target.Abi || entry.Target.ReturnClass != target.ReturnClass)
                throw new UnsupportedSignatureException("A probe requires an already-owned exact physical entry.");
            var probe = new BoundaryProbe(enter, exit); var binding = new BoundaryProbeBinding(entry, probe, _main);
            Volatile.Write(ref entry.Probes, entry.Probes.Append(probe).ToArray()); return binding;
        }
    }
    private static BoundaryProbe[] EnterProbes(Entry entry, IntPtr self, byte value, IntPtr method)
    {
        // Immutable snapshot pairs every entered probe with its exit, including
        // logical detach during original. No per-invocation array allocation.
        var probes = Volatile.Read(ref entry.Probes);
        foreach (var probe in probes)
            try { probe.Enter(self, value, method); } catch { Interlocked.Exchange(ref entry.ProbeFault, 1); }
        return probes;
    }
    private static void ExitProbes(Entry entry, BoundaryProbe[] probes, IntPtr self, byte value, IntPtr method, bool returned)
    {
        for (var index = probes.Length - 1; index >= 0; --index)
            try { probes[index].Exit(self, value, method, returned); } catch { Interlocked.Exchange(ref entry.ProbeFault, 1); }
    }
}
