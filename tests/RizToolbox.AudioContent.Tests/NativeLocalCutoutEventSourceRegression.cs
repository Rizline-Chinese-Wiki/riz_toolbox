using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;
using static Assert;

/// <summary>Controlled source backend plus actual InstanceVoid broker/probes.
/// Does not assert real prefab/listener layout or native-original unwind.</summary>
internal static class NativeLocalCutoutEventSourceRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Cutout source copies its bounded real-source observation before observer installation", InitialCapture),
        ("Cutout StartEvents return and expected Unity destruction retain unsealed pending sources", DispatchAndDestruction),
        ("Cutout peer receiver does not acquire this exact source identity", Peer),
        ("Cutout worker dispatch records unknown through pure probes without source reads", Worker),
        ("Cutout suppressed synchronous native reentry remains observable and unknown", Reentry),
        ("Cutout original exception propagates and leaves source observation unconfirmed", OriginalFailure),
        ("Cutout known active native boundary defers source reads until paired exit", InFlight),
        ("Cutout list version reference and scalar changes cannot be presented as the original closure", ChangedClosure),
        ("Cutout source rejects incomplete or overbound capture while retaining failure responsibility", InvalidCapture),
        ("Cutout source snapshot is pure and detached from a mutable backend list", ImmutableRead),
        ("Cutout unknown native activity never retries source observations", UnknownActivity),
        ("Cutout MethodInfo mismatch faults exact source while stock original still runs once", MethodMismatch),
        ("Cutout idle confirmation reads actual boundary activity without refreshing Unity sources", IdleConfirmation),
        ("Cutout activity observation rechecks a worker fault before any Unity source refresh", WorkerDuringActivity),
        ("Cutout dispatch window is physical, scalar and gives each paired source call a new generation", DispatchWindow),
        ("Cutout dispatch window rejects source reentry, worker and unknown activity without native object reads", FaultedWindow),
        ("Cutout source rejects refresh output after a concurrent worker fault and never refreshes again", WorkerDuringRefresh),
        ("Cutout UI accepts RectTransform only for the GameObject transform result", CutoutTransformClass)
    };
    private static void CutoutTransformClass()
    {
        IntPtr transform = new(1), rect = new(2), unrelated = new(3);
        Check(NativeLocalCutoutEventSourceBackend.MatchesRootClass(3, transform, transform, rect));
        Check(NativeLocalCutoutEventSourceBackend.MatchesRootClass(3, rect, transform, rect));
        Check(!NativeLocalCutoutEventSourceBackend.MatchesRootClass(3, unrelated, transform, rect));
        Check(!NativeLocalCutoutEventSourceBackend.MatchesRootClass(3, IntPtr.Zero, transform, IntPtr.Zero));
        for (var slot = 0; slot < 69; ++slot)
            if (slot != 3)
                Check(!NativeLocalCutoutEventSourceBackend.MatchesRootClass(slot, rect, unrelated, rect));
    }
    private static void InitialCapture()
    {
        using var f = new Fixture(); var value = f.Owner.Read();
        Equal(1, f.Backend.Captures); Equal(1, f.Backend.Binds); Equal(0, f.Backend.Refreshes);
        Check(value.Pending && !value.Unknown && !value.DispatchAttempted); Equal(9, value.RootRecords);
        Equal(unchecked((int)0x80000000), value.Source!.Triggers[0].OffsetBits);
    }
    private static void DispatchAndDestruction()
    {
        using var f = new Fixture(); f.Backend.Call(20);
        var value = f.Owner.Read(); Check(value.Pending && value.DispatchAttempted && value.UnsealedDispatchSource && !value.Unknown);
        Equal(1L, value.Entered); Equal(1L, value.Exited); Equal(0L, value.InFlight); Equal(1, f.Backend.Originals);
        f.Backend.Current = f.Backend.Current with { Alive = false }; f.Owner.Tick();
        value = f.Owner.Read(); Check(!value.Source!.Alive && value.Pending && value.UnsealedDispatchSource && !value.Unknown);
        Equal(9, value.RootRecords); Equal(0, f.Backend.Frees);
    }
    private static void Peer()
    {
        using var f = new Fixture(); f.Backend.Call(21); f.Owner.Tick();
        var value = f.Owner.Read(); Check(!value.DispatchAttempted && !value.Unknown && value.Pending); Equal(0L, value.Entered);
        Equal(1, f.Backend.Originals);
    }
    private static void Worker()
    {
        using var f = new Fixture(); var reads = f.Backend.Refreshes;
        Task.Run(() => f.Backend.Call(20)).GetAwaiter().GetResult();
        var value = f.Owner.Read(); Check(value.Unknown && value.DispatchAttempted && value.Pending);
        Equal(1L, value.Entered); Equal(1L, value.Exited); Equal(0L, value.InFlight); Equal(reads, f.Backend.Refreshes);
        f.Owner.Tick(); Equal(reads, f.Backend.Refreshes); Equal(1, f.Backend.Originals);
        Task.Run(() => Throws<InvalidOperationException>(() => f.Owner.Read())).GetAwaiter().GetResult();
    }
    private static void Reentry()
    {
        using var f = new Fixture(); var nested = false;
        f.Backend.DuringOriginal = () => { if (!nested) { nested = true; f.Backend.Call(20); } };
        f.Backend.Call(20); var value = f.Owner.Read(); Check(value.Unknown && value.Pending);
        Equal(2L, value.Entered); Equal(2L, value.Exited); Equal(0L, value.InFlight); Equal(2, f.Backend.Originals);
    }
    private static void OriginalFailure()
    {
        using var f = new Fixture(); var expected = new IOException("Exact original failure");
        f.Backend.DuringOriginal = () => throw expected;
        try { f.Backend.Call(20); throw new Exception("Original failure disappeared."); }
        catch (IOException error) { Check(ReferenceEquals(expected, error)); }
        var value = f.Owner.Read(); Check(value.Unknown && value.Pending && value.DispatchAttempted); Equal(0L, value.InFlight);
        Equal(1, f.Backend.Originals);
    }
    private static void InFlight()
    {
        using var f = new Fixture();
        f.Backend.DuringOriginal = () => {
            var value = f.Owner.Read(); Equal(1L, value.InFlight); Check(value.Pending && !value.Unknown);
            f.Owner.Tick(); Equal(0, f.Backend.Refreshes);
        };
        f.Backend.Call(20); f.Owner.Tick(); Equal(1, f.Backend.Refreshes); Check(!f.Owner.Read().Unknown);
    }
    private static void ChangedClosure()
    {
        using var f = new Fixture(); f.Backend.Current = f.Backend.Current with { Version = 2 };
        f.Owner.Tick(); Check(f.Owner.Read().Unknown); var reads = f.Backend.Refreshes;
        f.Backend.Current = f.Backend.Current with { Version = 1 }; f.Owner.Tick(); Equal(reads, f.Backend.Refreshes);
    }
    private static void InvalidCapture()
    {
        using var main = new MainThreadDispatcher(); var backend = new Backend(main);
        backend.Current = backend.Current with { Triggers = Array.AsReadOnly(Enumerable.Range(0, 33).Select(_ => new NativeCutoutTrigger(40, 50, 0)).ToArray()) };
        NativeLocalCutoutEventSourceException? failure = null;
        try { NativeLocalCutoutEventSource.CaptureBackend(main, backend); }
        catch (NativeLocalCutoutEventSourceException error) { failure = error; }
        Check(failure is not null && failure.Responsibility.Read().Unknown); Equal(0, backend.Binds); Equal(0, backend.Frees);
    }
    private static void ImmutableRead()
    {
        using var f = new Fixture(); var copied = f.Owner.Read().Source!;
        f.Backend.Rows[0] = new(999, 999, 7);
        Equal(40L, copied.Triggers[0].Trigger); Equal(40L, f.Owner.Read().Source!.Triggers[0].Trigger); Equal(0, f.Backend.Refreshes);
        f.Owner.Tick(); Check(f.Owner.Read().Unknown); Equal(40L, copied.Triggers[0].Trigger);
    }
    private static void UnknownActivity()
    {
        using var f = new Fixture(); f.Backend.ActivityKnown = false; f.Owner.Tick();
        Check(f.Owner.Read().Unknown); Equal(0, f.Backend.Refreshes);
        f.Backend.ActivityKnown = true; f.Owner.Tick(); Equal(0, f.Backend.Refreshes);
    }
    private static void MethodMismatch()
    {
        using var f = new Fixture(); f.Backend.Call(20, new(0xABC));
        Check(f.Owner.Read().Unknown && f.Owner.Read().DispatchAttempted); Equal(1, f.Backend.Originals);
    }
    private static void IdleConfirmation()
    {
        using var f = new Fixture(); Check(f.Owner.TryConfirmNativeIdle()); Equal(0, f.Backend.Refreshes);
        f.Backend.DuringOriginal = () => Check(!f.Owner.TryConfirmNativeIdle());
        f.Backend.Call(20); Check(f.Owner.TryConfirmNativeIdle()); Equal(0, f.Backend.Refreshes);
        f.Backend.ActivityKnown = false; Throws<InvalidOperationException>(() => f.Owner.TryConfirmNativeIdle());
        Check(f.Owner.Read().Unknown); Equal(0, f.Backend.Refreshes);
    }
    private static void WorkerDuringActivity()
    {
        using var f = new Fixture();
        f.Backend.OnActivity = () => { f.Backend.OnActivity = null; Task.Run(() => f.Backend.Call(20)).GetAwaiter().GetResult(); };
        f.Owner.Tick(); Check(f.Owner.Read().Unknown); Equal(0, f.Backend.Refreshes); Equal(1, f.Backend.Originals);
        Throws<InvalidOperationException>(() => f.Owner.TryConfirmNativeIdle());
    }
    private static void DispatchWindow()
    {
        using var f = new Fixture(); Check(!f.Owner.TryGetCurrentDispatch(out var none)); Equal(0L, none);
        long generation = 0;
        f.Backend.DuringOriginal = () => { Check(f.Owner.TryGetCurrentDispatch(out generation)); Equal(1L, generation); };
        f.Backend.Call(20); Check(!f.Owner.TryGetCurrentDispatch(out none)); Equal(0L, none);
        f.Backend.DuringOriginal = () => { Check(f.Owner.TryGetCurrentDispatch(out generation)); Equal(2L, generation); };
        f.Backend.Call(20); Equal(0, f.Backend.Refreshes);
        f.Backend.DuringOriginal = () => Check(!f.Owner.TryGetCurrentDispatch(out _)); f.Backend.Call(21);
        Equal(2L, f.Owner.Read().Entered); Equal(2L, f.Owner.Read().Exited);
    }
    private static void FaultedWindow()
    {
        using (var f = new Fixture())
        {
            f.Backend.DuringOriginal = () => { f.Owner.ProbeEnter(20, true); Check(!f.Owner.TryGetCurrentDispatch(out _)); f.Owner.ProbeExit(20, true); };
            f.Backend.Call(20); Check(f.Owner.Read().Unknown); Equal(0, f.Backend.Refreshes);
        }
        using (var f = new Fixture())
        {
            Task.Run(() => f.Backend.Call(20)).GetAwaiter().GetResult(); Check(!f.Owner.TryGetCurrentDispatch(out _)); Equal(0, f.Backend.Refreshes);
        }
    }
    private static void WorkerDuringRefresh()
    {
        using var f = new Fixture();
        f.Backend.Current = f.Backend.Current with { Alive = false };
        f.Backend.OnRefresh = () => Task.Run(() => f.Backend.Call(20)).GetAwaiter().GetResult();
        f.Owner.Tick();
        var state = f.Owner.Read();
        Check(state.Unknown && state.Pending && state.Source!.Alive);
        Equal(1, f.Backend.Refreshes); Equal(1, f.Backend.Originals); Equal(9, state.RootRecords);
        f.Backend.OnRefresh = null; f.Owner.Tick(); Equal(1, f.Backend.Refreshes);
        Throws<InvalidOperationException>(() => f.Owner.TryConfirmNativeIdle());
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new();
        internal readonly Backend Backend;
        internal readonly NativeLocalCutoutEventSource Owner;
        internal Fixture() { Backend = new(Main); Owner = NativeLocalCutoutEventSource.CaptureBackend(Main, Backend); }
        // Deliberately no fake release API: production source remains Pending.
        public void Dispose() => Main.Dispose();
    }
    private sealed class Backend : INativeCutoutEventSourceBackend, INativeDetourEngine
    {
        private static long Next = 0x6C0000000;
        private readonly MainThreadDispatcher _main;
        private readonly ExactHookBroker _broker;
        private readonly VerifiedHookTarget _target;
        private ExactHookBroker.InstanceVoid? _replacement;
        private ExactHookBroker.Binding? _binding;
        private ExactHookBroker.BoundaryProbeBinding? _probe;
        internal readonly List<NativeCutoutTrigger> Rows = new() { new(40, 50, unchecked((int)0x80000000)), new(41, 51, BitConverter.SingleToInt32Bits(.25f)) };
        internal NativeCutoutEventObservation Current;
        internal int Captures, Binds, Refreshes, Originals, Frees;
        internal bool ActivityKnown = true;
        internal Action? DuringOriginal;
        internal Action? OnActivity;
        internal Action? OnRefresh;
        public bool CleanupUnknown => false;
        public int RootRecords => 9;
        internal Backend(MainThreadDispatcher main)
        {
            _main = main; _broker = new(this, main, new Logger());
            _target = new("cutout.source.fixture", new(Interlocked.Add(ref Next, 0x1000)), new(0x123), new(0x456), ExactHookAbi.InstanceVoid);
            Current = new(10, 20, 30, 31, 35, 1, true, Rows);
        }
        public NativeCutoutEventObservation Capture() { _main.VerifyAccess(); Captures++; return Current; }
        public void Bind(NativeLocalCutoutEventSource owner)
        {
            Binds++; _binding = _broker.Bind(_target, _ => false, (_, _, _, _) => { });
            _probe = _broker.BindBoundaryProbe(_target,
                (self, _, method) => owner.ProbeEnter(self.ToInt64(), method == _target.MethodInfo),
                (self, _, _, returned) => owner.ProbeExit(self.ToInt64(), returned));
        }
        public NativeCutoutEventObservation Refresh() { _main.VerifyAccess(); Refreshes++; OnRefresh?.Invoke(); return Current; }
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity()
        {
            var value = _binding?.ReadActivity() ?? new(0, false); var probe = _probe?.ReadActivity() ?? new(0, false);
            OnActivity?.Invoke();
            return new(Math.Max(value.InFlight, probe.InFlight), value.Known && probe.Known && ActivityKnown);
        }
        public Delegate Install(VerifiedHookTarget target, Delegate replacement)
        {
            _replacement = (ExactHookBroker.InstanceVoid)replacement;
            return new ExactHookBroker.InstanceVoid((_, _) => { Originals++; DuringOriginal?.Invoke(); });
        }
        internal void Call(long receiver, IntPtr? method = null) => _replacement!(new(receiver), method ?? _target.MethodInfo);
    }
    private sealed class Logger : ILogger
    { public void Log(LogLevel level, string message, Exception? error = null) { } }
}
