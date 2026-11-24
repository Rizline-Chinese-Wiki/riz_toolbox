using RizToolbox.Adapter.AudioContent;
using RizToolbox.Adapter;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using System.Reflection;
using System.Runtime.InteropServices;
using static Assert;

/// <summary>Controlled algorithm seams, not Unity/game acceptance. Fault owners
/// intentionally retain their bounded process slots; production quotas are unchanged.</summary>
internal static class NativeLocalActiveDestructionRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Active destruction adopts the exact five named factory root handles without transferring their ownership", NamedFactoryRoots),
        ("Active destruction waits for required natural Cutout and a later actual Unity frame", LaterFrame),
        ("Active destruction accepts staging already dead after origin scene unload", StagingAlreadyDead),
        ("Active destruction destroys only its verified empty staging parent once and waits another frame", StagingDestroy),
        ("Active destruction waits for native and Cutout event entries before any Unity reads or mutation", Drain),
        ("Active destruction keeps partial clone death pending and rejects identity or liveness resurrection", ChangedIdentity),
        ("Active destruction retains native read and frame failures without retrying cleanup", ReadFaults),
        ("Active destruction rejects foreign staging children and retains unknown Destroy outcomes", DestroyFaults),
        ("Active destruction releases known independent observer roots once after all five Unity objects die", IndependentFrees),
        ("Active destruction stops independent frees when native ownership faults during an activity read", FreeActivityFault),
        ("Active destruction worker and reentry rejection do not spend another native operation", ThreadAndReentry),
        ("Active destruction capture faults retain acquired roots and controlled owners never claim production IsFor", CaptureFault),
        ("Active native backend refuses shared launch faults before root or Unity access", NativeSharedFaultBeforeRead),
        ("Active native backend stops after a root target or real value invocation faults shared launch", NativeSharedFaultDuringRead),
        ("Active native staging validation cannot reach Destroy after a shared fault inside its actual liveness invocation", NativeSharedFaultBeforeDestroy),
        ("Active native root cleanup separates local free failure from shared authority failure", NativeSharedFaultDuringFree)
    };
    private static void NamedFactoryRoots()
    {
        using var main = new MainThreadDispatcher(); var records = new InactiveControllerRootRecords(main);
        var freed = new List<uint>(); var handles = new List<NativeRuntime.RootHandle>();
        var owner = new OwnedNativeInactiveController(main, records,
            () => new(106, 107, 104, false, true, false, true, 0, 0), () => { }, () => true,
            () => throw new Exception("No legacy controller-only export is used."));
        for (uint id = 1; id <= 8; ++id)
        { var captured = id; handles.Add(records.Allocate(() => new(captured, value => freed.Add(value), _ => new(100 + captured), main.VerifyAccess))); }
        var borrowed = new List<(ActiveDestructionObject Role, NativeRuntime.RootHandle Root)>();
        records.BorrowActiveDestructionRoots((role, root) => borrowed.Add((role, root)));
        Equal(5, borrowed.Count); Equal(0, freed.Count); Equal(8, owner.RootRecords);
        var order = new[] { 5, 6, 7, 3, 4 };
        for (var index = 0; index < order.Length; ++index)
        { Equal((ActiveDestructionObject)index, borrowed[index].Role); Check(ReferenceEquals(handles[order[index]], borrowed[index].Root)); }
        owner.RequestDestroy(); Check(owner.TryCompleteDestroy()); Equal(8, freed.Count);
        Throws<InvalidOperationException>(() => records.BorrowActiveDestructionRoots((_, _) => { }));
    }
    private static void LaterFrame()
    {
        using var f = new Fixture(); f.Backend.Live = f.Backend.Live with { ControllerAlive = false, CloneGameObjectAlive = false, CloneTransformAlive = false };
        f.Authority.Activated = true; Check(!f.Owner.Tick()); Equal(1, f.Backend.LivenessReads); // Capture only: no Cutout yet.
        f.Authority.Natural = true; Check(!f.Owner.Tick()); Equal(10, f.Owner.Read().CutoutObservedFrame);
        Check(!f.Owner.Tick()); Equal(1, f.Backend.LivenessReads); Equal(-1, f.Owner.Read().CloneConfirmedFrame);
        f.Backend.FrameValue = 11; Check(f.Owner.Tick()); Check(f.Owner.Read().CloneDestroyed);
        Check(!f.Owner.Read().FiveObjectsDestroyed); Equal(0, f.Backend.Destroys);
        Throws<InvalidOperationException>(() => f.Owner.ReleaseObservationRootsAfterConfirmed()); Equal(0, f.Backend.Releases);
    }
    private static void StagingAlreadyDead()
    {
        using var f = new Fixture(); f.Authority.Activated = f.Authority.Natural = true;
        f.Backend.Live = f.Backend.Live with { StagingGameObjectAlive = false, StagingTransformAlive = false };
        Check(!f.Owner.Tick()); f.Backend.FrameValue++;
        f.Backend.KillClone(); Check(f.Owner.Tick()); var state = f.Owner.Read();
        Check(state.CloneDestroyed && state.FiveObjectsDestroyed && !state.StagingDestroyAttempted);
        f.Owner.RequestStagingDestroyOnce(); f.Owner.RequestStagingDestroyOnce(); Equal(0, f.Backend.Destroys);
        f.Owner.ReleaseObservationRootsAfterConfirmed(); Check(f.Owner.Read().Closed); Equal(0, f.Owner.Read().Roots);
    }
    private static void StagingDestroy()
    {
        using var f = new Fixture(); f.ConfirmClone();
        f.Owner.RequestStagingDestroyOnce(); Equal(1, f.Backend.Destroys); Equal(1, f.Backend.StagingChecks);
        var frame = f.Owner.Read().StagingDestroyFrame;
        f.Owner.RequestStagingDestroyOnce(); Equal(1, f.Backend.Destroys);
        f.Backend.KillStaging(); Check(f.Owner.Tick()); Check(!f.Owner.Read().FiveObjectsDestroyed);
        f.Backend.FrameValue++; Check(f.Owner.Tick()); Check(f.Owner.Read().FiveObjectsDestroyed);
        Check(f.Owner.Read().ConfirmedFrame > frame); Equal(0, f.Backend.CloneDestroyRequests);
    }
    private static void Drain()
    {
        using var f = new Fixture(); f.Authority.Activated = f.Authority.Natural = true; f.Authority.Drained = false;
        var frames = f.Backend.FrameReads; var reads = f.Backend.LivenessReads;
        Check(!f.Owner.Tick()); Equal(frames, f.Backend.FrameReads); Equal(reads, f.Backend.LivenessReads);
        Check(!f.Owner.Read().Unknown); Equal(-1, f.Owner.Read().CutoutObservedFrame);
        f.Authority.Drained = true; Check(!f.Owner.Tick()); f.Backend.FrameValue++; f.Backend.KillClone(); Check(f.Owner.Tick());
        frames = f.Backend.FrameReads; reads = f.Backend.LivenessReads;
        f.Authority.Drained = false; f.Owner.RequestStagingDestroyOnce(); Equal(0, f.Backend.Destroys);
        Equal(frames, f.Backend.FrameReads); Equal(reads, f.Backend.LivenessReads); Equal(0, f.Backend.StagingChecks);
        Check(!f.Owner.Read().Unknown); Check(!f.Owner.Read().StagingDestroyAttempted);
        f.Authority.Drained = true; f.Owner.RequestStagingDestroyOnce(); Equal(1, f.Backend.Destroys);
    }
    private static void ChangedIdentity()
    {
        using (var f = new Fixture())
        {
            f.Authority.Activated = f.Authority.Natural = true; f.Owner.Tick(); f.Backend.FrameValue++;
            f.Backend.Live = f.Backend.Live with { ControllerAlive = false }; Check(!f.Owner.Tick()); Equal(-1, f.Owner.Read().CloneConfirmedFrame);
            f.Backend.FrameValue++; f.Backend.Live = f.Backend.Live with { ControllerAlive = true };
            Throws<NativeLocalActiveDestructionException>(() => f.Owner.Tick()); Check(f.Owner.Read().Unknown); Equal(5, f.Owner.Read().Roots);
        }
        using (var f = new Fixture())
        {
            f.Authority.Activated = f.Authority.Natural = true; f.Owner.Tick(); f.Backend.FrameValue++;
            f.Backend.Live = f.Backend.Live with { Identity = f.Backend.Live.Identity with { CloneTransform = 999 } };
            Throws<NativeLocalActiveDestructionException>(() => f.Owner.Tick()); Equal(0, f.Backend.Destroys); Check(f.Owner.Read().Unknown);
        }
    }
    private static void ReadFaults()
    {
        foreach (var stage in new[] { "read", "frame-backwards", "frame-mismatch", "activity" })
        {
            using var f = new Fixture(); f.Authority.Activated = f.Authority.Natural = true; f.Owner.Tick(); f.Backend.FrameValue++;
            if (stage == "read") f.Backend.ThrowRead = true;
            if (stage == "frame-backwards") f.Backend.FrameValue = 9;
            if (stage == "frame-mismatch") f.Authority.FrameAdjustment = 1;
            if (stage == "activity") f.Authority.DuringReadiness = () => f.Authority.Fault = true;
            Throws<NativeLocalActiveDestructionException>(() => f.Owner.Tick()); Check(f.Owner.Read().Unknown);
            var reads = f.Backend.LivenessReads; Throws<InvalidOperationException>(() => f.Owner.Tick()); Equal(reads, f.Backend.LivenessReads);
            Equal(5, f.Owner.Read().Roots); Equal(0, f.Backend.Destroys); Equal(0, f.Backend.Releases);
        }
    }
    private static void DestroyFaults()
    {
        using (var f = new Fixture())
        {
            f.ConfirmClone(); f.Backend.ForeignChild = true;
            Throws<NativeLocalActiveDestructionException>(() => f.Owner.RequestStagingDestroyOnce());
            Check(f.Owner.Read().Unknown); Equal(0, f.Backend.Destroys); Check(!f.Owner.Read().StagingDestroyAttempted);
        }
        using (var f = new Fixture())
        {
            f.ConfirmClone(); f.Backend.ThrowDestroy = true;
            Throws<NativeLocalActiveDestructionException>(() => f.Owner.RequestStagingDestroyOnce());
            Check(f.Owner.Read().Unknown && f.Owner.Read().StagingDestroyAttempted); Equal(1, f.Backend.Destroys);
            Throws<InvalidOperationException>(() => f.Owner.RequestStagingDestroyOnce()); Equal(1, f.Backend.Destroys);
        }
    }
    private static void IndependentFrees()
    {
        using var f = new Fixture(); f.ConfirmFive(); f.Backend.FailFree = 3;
        Throws<NativeLocalActiveDestructionException>(() => f.Owner.ReleaseObservationRootsAfterConfirmed());
        Check(f.Backend.Frees.SequenceEqual(new[] { 4, 3, 2, 1, 0 })); Equal(1, f.Owner.Read().Roots);
        Check(f.Owner.Read().Unknown); Throws<InvalidOperationException>(() => f.Owner.ReleaseObservationRootsAfterConfirmed()); Equal(1, f.Backend.Releases);
    }
    private static void FreeActivityFault()
    {
        using var f = new Fixture(); f.ConfirmFive();
        f.Backend.BeforeFree = index => { if (index == 3) f.Authority.DuringReadiness = () => f.Authority.Fault = true; };
        Throws<NativeLocalActiveDestructionException>(() => f.Owner.ReleaseObservationRootsAfterConfirmed());
        Check(f.Backend.Frees.SequenceEqual(new[] { 4 })); Equal(4, f.Owner.Read().Roots); Check(f.Owner.Read().Unknown);
        Equal(1, f.Backend.Releases); Throws<InvalidOperationException>(() => f.Owner.ReleaseObservationRootsAfterConfirmed());
    }
    private static void ThreadAndReentry()
    {
        using var f = new Fixture(); f.Authority.Activated = f.Authority.Natural = true;
        var reads = f.Backend.LivenessReads;
        Task.Run(() => { Throws<InvalidOperationException>(() => f.Owner.Tick()); Throws<InvalidOperationException>(() => f.Owner.RequestStagingDestroyOnce()); })
            .GetAwaiter().GetResult(); Equal(reads, f.Backend.LivenessReads);
        var rejected = 0; f.Backend.DuringFrame = () => { Throws<InvalidOperationException>(() => f.Owner.Tick()); rejected++; };
        Check(!f.Owner.Tick()); Equal(1, rejected); Check(!f.Owner.Read().Unknown);
        f.Backend.DuringFrame = null;
    }
    private static void CaptureFault()
    {
        using var main = new MainThreadDispatcher(); var backend = new Backend { FailCaptureAfterRoots = true };
        var authority = new Authority(backend); NativeLocalActiveDestructionException? captured = null;
        try { NativeLocalActiveDestructionTracker.CaptureBackend(main, backend, authority); }
        catch (NativeLocalActiveDestructionException error) { captured = error; }
        Check(captured is not null); Equal(2, captured!.Responsibility.Read().Roots); Check(captured.Responsibility.Read().Unknown);
        Equal(0, backend.Releases); Check(!captured.Responsibility.IsFor(null!));
    }
    private static void NativeSharedFaultBeforeRead()
    {
        using var f = new NativeBarrierFixture(); f.FaultFromWorker();
        Throws<InvalidOperationException>(() => f.Backend.ReadLiveness());
        Throws<InvalidOperationException>(() => f.Backend.Frame());
        Throws<InvalidOperationException>(() => f.Backend.VerifyStagingCanDestroy());
        Throws<InvalidOperationException>(() => f.Backend.DestroyStagingOnce());
        Equal(0, f.RootReads); Equal(0, f.Native.Invocations); Equal(5, f.Backend.RootRecords);
    }
    private static void NativeSharedFaultDuringRead()
    {
        using (var f = new NativeBarrierFixture())
        {
            f.OnRoot = f.FaultFromWorker;
            Throws<InvalidOperationException>(() => f.Backend.ReadLiveness());
            Equal(1, f.RootReads); Equal(0, f.Native.Invocations); Equal(5, f.Backend.RootRecords);
            Throws<InvalidOperationException>(() => f.Backend.ReadLiveness()); Equal(1, f.RootReads);
        }
        using (var f = new NativeBarrierFixture())
        {
            f.OnInvoke = f.FaultFromWorker;
            Throws<InvalidOperationException>(() => f.Backend.ReadLiveness());
            Equal(1, f.RootReads); Equal(1, f.Native.Invocations); Equal(5, f.Backend.RootRecords);
            // Successful Runtime cleanup consumed its inputs before the late
            // shared-fault barrier rejected every subsequent Unity operation.
            Equal(0, f.Backend.RetainedArgumentRecords); Equal(0, f.Native.ActiveRoots);
            Throws<InvalidOperationException>(() => f.Backend.ReadLiveness()); Equal(1, f.Native.Invocations);
        }
    }
    private static void NativeSharedFaultBeforeDestroy()
    {
        using var f = new NativeBarrierFixture(); f.OnInvoke = f.FaultFromWorker;
        Throws<InvalidOperationException>(() => f.Backend.DestroyStagingOnce());
        Equal(1, f.Native.Invocations); Equal(1, f.RootReads); Equal(5, f.Backend.RootRecords);
        Check(!f.Field<bool>("_destroyAttempted")); Equal(0, f.Backend.RetainedArgumentRecords);
        Throws<InvalidOperationException>(() => f.Backend.DestroyStagingOnce()); Equal(1, f.Native.Invocations);
    }
    private static void NativeSharedFaultDuringFree()
    {
        using (var f = new NativeBarrierFixture())
        {
            var probes = 0;
            Throws<AggregateException>(() => f.Backend.ReleaseObservationRoots(() => { probes++; f.FaultFromWorker(); return true; }));
            Equal(1, probes); Equal(0, f.Frees.Count); Equal(5, f.Backend.RootRecords);
        }
        using (var f = new NativeBarrierFixture())
        {
            f.OnFree = _ => f.FaultFromWorker(); var probes = 0;
            Throws<AggregateException>(() => f.Backend.ReleaseObservationRoots(() => { probes++; return true; }));
            Check(f.Frees.SequenceEqual(new[] { 4 })); Equal(1, probes); Equal(4, f.Backend.RootRecords);
            Throws<InvalidOperationException>(() => f.Backend.ReleaseObservationRoots(() => true)); Equal(1, f.Frees.Count);
        }
        using (var f = new NativeBarrierFixture())
        {
            f.OnFree = index => { if (index == 0) f.FaultFromWorker(); };
            Throws<AggregateException>(() => f.Backend.ReleaseObservationRoots(() => true));
            Check(f.Frees.SequenceEqual(new[] { 4, 3, 2, 1, 0 })); Equal(0, f.Backend.RootRecords);
            // Successful individual frees do not turn a late external fault into
            // a successful cleanup transaction, including its final iteration.
            Check(f.Backend.CleanupUnknown); Throws<InvalidOperationException>(() => f.Backend.ReleaseObservationRoots(() => true)); Equal(5, f.Frees.Count);
        }
        using (var f = new NativeBarrierFixture())
        {
            f.OnFree = index => { if (index == 3) throw new IOException("Only this independent root has an unknown free."); };
            Throws<AggregateException>(() => f.Backend.ReleaseObservationRoots(() => true));
            Check(f.Frees.SequenceEqual(new[] { 4, 3, 2, 1, 0 })); Equal(1, f.Backend.RootRecords);
            Check(f.Backend.CleanupUnknown); Throws<InvalidOperationException>(() => f.Backend.ReleaseObservationRoots(() => true)); Equal(5, f.Frees.Count);
        }
    }
    /// <summary>Exercises the production backend methods with a real registry and
    /// NativeRuntime publication path. Only private initialization metadata/roots
    /// are supplied by this fixture; it is not native metadata/game acceptance.</summary>
    private sealed class NativeBarrierFixture : IDisposable
    {
        private static long _nextIdentity = 93_000_000;
        internal readonly MainThreadDispatcher Main = new();
        internal readonly FakeNative Native = new();
        internal readonly LocalRunLaunchCommitState Launch;
        internal readonly NativeLocalActiveDestructionBackend Backend;
        internal int RootReads;
        internal Action? OnRoot, OnInvoke;
        internal Action<int>? OnFree;
        internal readonly List<int> Frees = new();
        internal NativeBarrierFixture()
        {
            var registry = new LocalRunSessionRegistry(Main); var bundle = new EmptyLease();
            // Unknown fixtures intentionally retain their process registry owner.
            // Use a distinct block for metadata and all five wrapper identities;
            // never clear production ownership or recycle a retained address.
            var identity = Interlocked.Add(ref _nextIdentity, 16);
            var session = registry.RegisterPrepared(new(new("active.native-barrier", "song"), 1, "chart"), identity, bundle);
            Launch = new(Main, registry, registry.BeginCommit(session), bundle);
            Native.IsStatic = true; Native.ReturnType = Native.Boolean; Native.Parameters = new[] { Native.Object };
            var api = Native.CreateActiveDestructionBarrierApi(() => OnInvoke?.Invoke());
            var profile = new AdapterBuildProfile("test", "", "test", "test", "test", "test", new Dictionary<string, TypeBinding>());
            Backend = new(api, Main, profile, IntPtr.Zero, null!, Launch);
            Set("_initialized", true); Set("_controllerClass", Native.ClassOf(Native.String));
            Set("_goClass", Native.ClassOf(Native.String)); Set("_transformClass", Native.ClassOf(Native.String));
            Set("_alive", Native.Method); Set("_frame", Native.Method);
            var roots = Field<NativeRuntime.RootHandle?[]>("_roots"); var identities = Field<long[]>("_identities");
            for (var index = 0; index < 5; ++index)
            {
                var captured = index; var pointer = new IntPtr(identity + index + 1); identities[index] = pointer.ToInt64();
                roots[index] = new((uint)(index + 1), _ => { Frees.Add(captured); OnFree?.Invoke(captured); },
                    _ => { RootReads++; OnRoot?.Invoke(); return pointer; }, Main.VerifyAccess);
            }
        }
        internal void FaultFromWorker() => Task.Run(() => Launch.ObserveNativeFaultFromAnyThread("Controlled worker observation fault.")).GetAwaiter().GetResult();
        internal T Field<T>(string name) => (T)typeof(NativeLocalActiveDestructionBackend).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Backend)!;
        private void Set(string name, object value) => typeof(NativeLocalActiveDestructionBackend).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Backend, value);
        public void Dispose() { Native.Dispose(); Main.Dispose(); }
        private sealed class EmptyLease : IDisposable { public void Dispose() { } }
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new();
        internal readonly Backend Backend = new();
        internal readonly Authority Authority;
        internal readonly NativeLocalActiveDestructionTracker Owner;
        internal Fixture() { Authority = new(Backend); Owner = NativeLocalActiveDestructionTracker.CaptureBackend(Main, Backend, Authority); Check(!Owner.IsFor(null!)); }
        internal void ConfirmClone()
        {
            Authority.Activated = Authority.Natural = true; Check(!Owner.Tick()); Backend.FrameValue++; Backend.KillClone(); Check(Owner.Tick());
        }
        internal void ConfirmFive() { ConfirmClone(); Backend.KillStaging(); Check(Owner.Tick()); Check(Owner.Read().FiveObjectsDestroyed); }
        public void Dispose()
        {
            try
            {
                var state = Owner.Read(); if (state.Unknown || state.Closed) return;
                Backend.ThrowRead = Backend.ThrowDestroy = Backend.ForeignChild = false; Backend.DuringFrame = null;
                Authority.Drained = Authority.Natural = Authority.Activated = true; Authority.FrameAdjustment = 0; Authority.DuringReadiness = null;
                Backend.FrameValue++; Owner.Tick(); Backend.FrameValue++; Backend.KillClone(); Backend.KillStaging(); Owner.Tick();
                Backend.FrameValue++; Owner.Tick(); Owner.ReleaseObservationRootsAfterConfirmed();
            }
            finally { Main.Dispose(); }
        }
    }
    private sealed class Authority : IActiveDestructionAuthority
    {
        private readonly Backend _backend;
        internal bool Activated, Natural, Drained = true, Known = true, Fault;
        internal int FrameAdjustment;
        internal Action? DuringReadiness;
        internal Authority(Backend backend) => _backend = backend;
        public LocalRunSessionIdentity Identity { get; } = new("active-destruction-fixture", new("destruction.fixture", "song"), 1, "chart", 1);
        public long Controller => 101;
        public bool Unknown => Fault;
        public void VerifyBeforeCapture() { if (Activated || Fault) throw new InvalidOperationException("Capture must precede actual activation."); }
        public void VerifyObservation() { if (!Activated || Fault) throw new InvalidOperationException("Controlled run protection unavailable."); }
        public ActiveDestructionReadiness ReadReadiness()
        { DuringReadiness?.Invoke(); return new(Known, Drained, Natural, Drained ? _backend.FrameValue + FrameAdjustment : -1); }
        public void MarkUnknown(string failure) => Fault = true;
    }
    private sealed class Backend : IActiveDestructionBackend
    {
        internal ActiveDestructionLiveness Live = new(new(101, 102, 103, 104, 105), true, true, true, true, true);
        internal int FrameValue = 10, FrameReads, LivenessReads, Destroys, StagingChecks, Releases, CloneDestroyRequests;
        internal bool ThrowRead, ThrowDestroy, ForeignChild, FailCaptureAfterRoots;
        internal int FailFree = -1;
        internal Action? DuringFrame;
        internal Action<int>? BeforeFree;
        internal readonly List<int> Frees = new();
        private readonly bool[] _roots = new bool[5];
        public bool CleanupUnknown { get; private set; }
        public int RootRecords => _roots.Count(value => value);
        public void Initialize()
        {
            Array.Fill(_roots, true);
            if (FailCaptureAfterRoots) { Array.Fill(_roots, false); _roots[0] = _roots[1] = true; CleanupUnknown = true; throw new IOException("Controlled publication failed after two roots."); }
        }
        public int Frame() { FrameReads++; DuringFrame?.Invoke(); return FrameValue; }
        public ActiveDestructionLiveness ReadLiveness()
        { LivenessReads++; if (ThrowRead) { CleanupUnknown = true; throw new IOException("Controlled liveness observation failed."); } return Live; }
        public void VerifyStagingCanDestroy()
        { StagingChecks++; if (ForeignChild) throw new InvalidOperationException("A successor adopted a staging child."); }
        public void DestroyStagingOnce()
        { Destroys++; if (ThrowDestroy) { CleanupUnknown = true; throw new IOException("Controlled unknown native staging Destroy outcome."); } }
        public void ReleaseObservationRoots(Func<bool> mayFreeNext)
        {
            Releases++; var failures = new List<Exception>();
            for (var index = 4; index >= 0; --index)
            {
                if (!_roots[index]) continue; BeforeFree?.Invoke(index);
                if (!mayFreeNext()) { failures.Add(new InvalidOperationException("Controlled activity changed before independent free.")); break; }
                Frees.Add(index); if (index == FailFree) { failures.Add(new IOException("Controlled free failed.")); CleanupUnknown = true; continue; }
                _roots[index] = false;
            }
            if (failures.Count != 0) { CleanupUnknown = true; throw new AggregateException(failures); }
        }
        internal void KillClone() => Live = Live with { ControllerAlive = false, CloneGameObjectAlive = false, CloneTransformAlive = false };
        internal void KillStaging() => Live = Live with { StagingGameObjectAlive = false, StagingTransformAlive = false };
    }
}

sealed partial class FakeNative
{
    internal NativeRuntime CreateActiveDestructionBarrierApi(Action invoked)
    {
        // Keep this real unmanaged callback strongly owned with the fake exports.
        // It returns a real boxed Boolean through Runtime's publication/unbox path.
        var callback = new NativeRuntime.RuntimeInvoke((_, _, _, exception) => {
            Invocations++; invoked(); Marshal.WriteIntPtr(exception, IntPtr.Zero);
            return Box(Boolean, new byte[] { 0 });
        });
        _exports["active-destruction-barrier-invoke"] = callback;
        var api = new NativeRuntime(name => name == "il2cpp_runtime_invoke"
            ? Marshal.GetFunctionPointerForDelegate(callback) : ExportPointer(name));
        api.BindCurrentThread(); return api;
    }
}
