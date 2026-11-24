using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using static Assert;

/// <summary>Controlled broker transport only; not Dobby, IL2CPP receiver, clone or Awake runtime acceptance.</summary>
internal static class NativeControllerAwakeLedgerRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Awake window proves zero before a clone and paired exact-root observations after original", Paired),
        ("Awake window captures before clone identity is known and reports only the rooted queried clone", UnknownIdentity),
        ("Awake original exception propagates once and leaves timing explicitly unpaired", OriginalFailure),
        ("Awake worker pass-through is detected by broker deltas without native metadata capture", Worker),
        ("Awake nested original calls remain unchanged and make suppressed observation unverifiable", Reentry),
        ("Awake window caps 32 retained calls and never claims complete timing after overflow", Overflow),
        ("Awake scope disposal during original defers receiver cleanup through retained finally", DisposeDuringOriginal),
        ("Awake frame and retain failures cannot escape into original-call control", CaptureFailures),
        ("Awake frame callback disposal cannot release the receiver before original", DisposeDuringFrame),
        ("Awake root allocator disposal cannot release newly returned responsibility before finally", DisposeDuringAllocation),
        ("Awake window admission and wrong-thread disposal preserve existing owned roots", ScopeAdmission),
        ("Awake ledger detach during original still releases the retained context once", DetachDuringOriginal),
        ("Awake unknown root free attempts independent roots once and retains failed capacity", UnknownFree)
    };

    private static void Paired()
    {
        using var f = new AwakeFixture(); using var clone = f.Caller(); using var window = f.Ledger.BeginCaptureWindow("clone-timing");
        var before = window.SnapshotFor(clone); Check(before.TimingVerifiable); Equal(0, before.CloneBefore); Equal(0L, before.NativeCalls);
        f.Engine.Original = (_, _) => { f.Frame = 11; Equal(0, f.Frees.Count); };
        f.Engine.Invoke(new(77)); var after = window.SnapshotFor(clone);
        Check(after.TimingVerifiable); Equal(1, after.CloneBefore); Equal(1, after.CloneAfter);
        Equal(1L, after.NativeCalls); Equal(1L, after.OriginalCalls); Equal(1, f.Engine.Calls);
        var row = after.CloneRows.Single(); Check(row.BeforeOrder < row.AfterOrder && row.ContextFinished);
        Equal(10, row.BeforeFrame); Equal(11, row.AfterFrame); Equal(0, f.CallerFrees);
        window.Dispose(); Equal(1, f.Frees.Count); Equal(new IntPtr(77), clone.Target); Equal(0, f.CallerFrees);
    }
    private static void UnknownIdentity()
    {
        using var f = new AwakeFixture(); using var window = f.Ledger.BeginCaptureWindow("before-clone-return");
        f.Engine.Invoke(new(88)); f.Engine.Invoke(new(77));
        using var clone = f.Caller(); var snapshot = window.SnapshotFor(clone);
        Check(snapshot.TimingVerifiable); Equal(2, snapshot.WindowBefore); Equal(2, snapshot.WindowAfter);
        Equal(1, snapshot.CloneBefore); Equal(1, snapshot.CloneAfter); Equal(77L, snapshot.CloneRows.Single().ReceiverIdentity);
        using var otherClass = f.Caller(88); Check(!window.SnapshotFor(otherClass).TimingVerifiable);
        Equal(2, window.RootRecords);
    }
    private static void OriginalFailure()
    {
        using var f = new AwakeFixture(); using var window = f.Ledger.BeginCaptureWindow("throwing-original"); using var clone = f.Caller();
        var failure = new IOException("Original failure must propagate."); f.Engine.Original = (_, _) => throw failure;
        try { f.Engine.Invoke(new(77)); throw new Exception("Original exception disappeared."); }
        catch (IOException actual) { Check(ReferenceEquals(failure, actual)); }
        var snapshot = window.SnapshotFor(clone); Check(!snapshot.TimingVerifiable);
        Equal(1, snapshot.CloneBefore); Equal(0, snapshot.CloneAfter); Equal(0, snapshot.InFlight);
        Equal(1, f.Engine.Calls); Equal(0, f.Frees.Count);
    }
    private static void Worker()
    {
        using var f = new AwakeFixture(); using var window = f.Ledger.BeginCaptureWindow("worker"); using var clone = f.Caller();
        Task.Run(() => f.Engine.Invoke(new(77))).GetAwaiter().GetResult();
        var snapshot = window.SnapshotFor(clone); Check(!snapshot.TimingVerifiable);
        Equal(1L, snapshot.NativeCalls); Equal(1L, snapshot.OriginalCalls); Check(snapshot.SuppressedWrongThread > 0);
        Equal(0, f.Retains); Equal(0, f.MatchCalls); Equal(0, f.FrameReads); Equal(1, f.Engine.Calls);
    }
    private static void Reentry()
    {
        using var f = new AwakeFixture(); using var window = f.Ledger.BeginCaptureWindow("nested"); using var clone = f.Caller();
        f.Engine.Original = (_, _) => { if (f.Engine.Calls == 1) f.Engine.Invoke(new(77)); };
        f.Engine.Invoke(new(77)); var snapshot = window.SnapshotFor(clone);
        Check(!snapshot.TimingVerifiable); Equal(2, f.Engine.Calls); Equal(2L, snapshot.NativeCalls);
        Equal(1L, snapshot.SuppressedReentries); Equal(1, snapshot.CloneBefore); Equal(1, snapshot.CloneAfter); Equal(1, f.Retains);
    }
    private static void Overflow()
    {
        using var f = new AwakeFixture(); using var window = f.Ledger.BeginCaptureWindow("bounded"); using var clone = f.Caller();
        for (var i = 0; i < 33; i++) f.Engine.Invoke(new(77));
        var snapshot = window.SnapshotFor(clone); Check(!snapshot.TimingVerifiable);
        Equal(33, f.Engine.Calls); Equal(32, window.RootRecords); Equal(32, snapshot.CloneBefore); Equal(32, f.Retains);
        window.Dispose(); Equal(32, f.Frees.Count); Check(f.Frees.Distinct().Count() == 32);
    }
    private static void DisposeDuringOriginal()
    {
        using var f = new AwakeFixture(); var window = f.Ledger.BeginCaptureWindow("in-flight");
        f.Engine.Original = (_, _) => { window.Dispose(); Equal(1, window.RootRecords); Equal(0, f.Frees.Count); };
        f.Engine.Invoke(new(77)); Equal(1, f.Frees.Count); Equal(0, window.RootRecords); window.Dispose();
        using var next = f.Ledger.BeginCaptureWindow("next"); Equal(0, next.RootRecords);
    }
    private static void CaptureFailures()
    {
        using (var f = new AwakeFixture())
        {
            using var clone = f.Caller(); using var window = f.Ledger.BeginCaptureWindow("frame-fault");
            f.ReadFrame = () => throw new IOException("Frame unavailable."); f.Engine.Invoke(new(77));
            Check(!window.SnapshotFor(clone).TimingVerifiable); Equal(1, f.Engine.Calls); Equal(1, window.RootRecords);
        }
        using (var f = new AwakeFixture())
        {
            using var clone = f.Caller(); using var window = f.Ledger.BeginCaptureWindow("retain-fault");
            f.Allocate = _ => throw new IOException("No root returned."); f.Engine.Invoke(new(77));
            Check(!window.SnapshotFor(clone).TimingVerifiable); Equal(1, f.Engine.Calls); Equal(0, window.RootRecords);
            Equal(0, window.SnapshotFor(clone).InFlight);
        }
        using (var f = new AwakeFixture())
        {
            using var clone = f.Caller(); using var window = f.Ledger.BeginCaptureWindow("filter-fault");
            f.Match = _ => throw new IOException("Class lookup failed."); f.Engine.Invoke(new(77));
            Check(!window.SnapshotFor(clone).TimingVerifiable); Equal(1, f.Engine.Calls); Equal(0, f.Retains);
        }
    }
    private static void DisposeDuringFrame()
    {
        using var f = new AwakeFixture(); var window = f.Ledger.BeginCaptureWindow("frame-close");
        f.ReadFrame = () => { window.Dispose(); Equal(0, f.Frees.Count); return 10; };
        f.Engine.Original = (_, _) => { Equal(1, window.RootRecords); Equal(0, f.Frees.Count); };
        f.Engine.Invoke(new(77)); Equal(1, f.Frees.Count); Equal(0, window.RootRecords);
    }
    private static void DisposeDuringAllocation()
    {
        using var f = new AwakeFixture(); var window = f.Ledger.BeginCaptureWindow("allocator-close");
        f.Allocate = pointer => { window.Dispose(); Equal(0, f.Frees.Count); return f.NewRoot(pointer); };
        f.Engine.Original = (_, _) => { Equal(1, window.RootRecords); Equal(0, f.Frees.Count); };
        f.Engine.Invoke(new(77)); Equal(1, f.Frees.Count); Equal(0, window.RootRecords);
    }
    private static void ScopeAdmission()
    {
        using var f = new AwakeFixture(); using var window = f.Ledger.BeginCaptureWindow("one"); using var clone = f.Caller();
        Throws<InvalidOperationException>(() => f.Ledger.BeginCaptureWindow("two"));
        f.Engine.Invoke(new(77));
        Task.Run(() => {
            Throws<InvalidOperationException>(() => window.Dispose());
            Throws<InvalidOperationException>(() => window.SnapshotFor(clone));
            Throws<InvalidOperationException>(() => f.Ledger.Dispose());
        }).GetAwaiter().GetResult();
        Equal(0, f.Frees.Count); Check(window.SnapshotFor(clone).TimingVerifiable);
    }
    private static void DetachDuringOriginal()
    {
        using var f = new AwakeFixture(); var window = f.Ledger.BeginCaptureWindow("detach");
        f.Engine.Original = (_, _) => { f.Ledger.Dispose(); Equal(0, f.Frees.Count); };
        f.Engine.Invoke(new(77)); Equal(1, f.Frees.Count); Equal(0, window.RootRecords);
        f.Engine.Original = null; f.Engine.Invoke(new(77)); Equal(2, f.Engine.Calls); Equal(1, f.Retains);
    }
    private static void UnknownFree()
    {
        using var f = new AwakeFixture(); var window = f.Ledger.BeginCaptureWindow("unknown-free");
        f.Engine.Invoke(new(77)); f.Engine.Invoke(new(77)); f.FailFreeId = 1;
        Throws<AggregateException>(() => window.Dispose()); Check(window.CleanupUnknown); Equal(1, window.RootRecords);
        Equal("2,1", string.Join(",", f.Frees));
        Throws<InvalidOperationException>(() => window.Dispose());
        Throws<InvalidOperationException>(() => f.Ledger.BeginCaptureWindow("forbidden"));
        Equal("2,1", string.Join(",", f.Frees)); // Independent success was freed and failed free was never retried.
    }

    private sealed class AwakeFixture : IDisposable
    {
        private static long _nextAddress = 0x71600000;
        internal readonly MainThreadDispatcher Main = new();
        internal readonly Engine Engine = new();
        internal readonly List<uint> Frees = new();
        internal readonly NativeControllerAwakeLedger Ledger;
        internal int Frame = 10, FrameReads, Retains, MatchCalls, CallerFrees;
        internal uint FailFreeId;
        private uint _nextRoot;
        internal Func<IntPtr, bool>? Match;
        internal Func<int>? ReadFrame;
        internal Func<IntPtr, NativeRuntime.RootHandle>? Allocate;
        internal AwakeFixture()
        {
            var target = new VerifiedHookTarget("controlled.awake", new(Interlocked.Add(ref _nextAddress, 16)),
                new(0x111), new(0x222), ExactHookAbi.InstanceVoid);
            Ledger = NativeControllerAwakeLedger.BindCore(Main, new ExactHookBroker(Engine, Main, new Logger()), target,
                pointer => { MatchCalls++; return Match?.Invoke(pointer) ?? pointer != IntPtr.Zero; },
                pointer => pointer == new IntPtr(77),
                pointer => { Retains++; return Allocate?.Invoke(pointer) ?? NewRoot(pointer); },
                () => { FrameReads++; return ReadFrame?.Invoke() ?? Frame; });
        }
        internal NativeRuntime.RootHandle NewRoot(IntPtr pointer)
        {
            var id = ++_nextRoot;
            return new(id, value => { Frees.Add(value); if (FailFreeId == value) throw new IOException("Unknown root free."); }, _ => pointer, Main.VerifyAccess);
        }
        internal NativeRuntime.RootHandle Caller(int identity = 77) => new(1000, _ => CallerFrees++, _ => new IntPtr(identity), Main.VerifyAccess);
        public void Dispose() { Ledger.Dispose(); Main.Dispose(); }
    }
    private sealed class Engine : INativeDetourEngine
    {
        private ExactHookBroker.InstanceVoid? _replacement;
        internal Action<IntPtr, IntPtr>? Original;
        internal int Calls;
        public Delegate Install(VerifiedHookTarget target, Delegate replacement)
        {
            _replacement = (ExactHookBroker.InstanceVoid)replacement;
            return new ExactHookBroker.InstanceVoid((receiver, method) => { Calls++; Original?.Invoke(receiver, method); });
        }
        internal void Invoke(IntPtr receiver) => _replacement!(receiver, new IntPtr(0x111));
    }
}
