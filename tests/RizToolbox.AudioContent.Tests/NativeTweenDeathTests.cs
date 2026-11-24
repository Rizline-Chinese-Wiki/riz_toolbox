using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using static Assert;

// Actual work/death owners and Runtime admission lease code with a controlled
// transport and read-only observation backend. Every receipt here is explicitly
// nonproduction. No Unity method, native patch, destroy, unregister or free runs.
internal static class NativeTweenDeathTests
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Tween death retains a preactivation live baseline and confirms two later zero frames after classified Return and actual seal", LiveThenTwoFrames),
        ("Tween death a zero cached pointer without any earlier live observation never issues a death receipt", InitiallyZero),
        ("Tween death sealed source and later scene-style zero observations cannot invent a classified Return source", NoReturnSource),
        ("Tween death Return before actual seal records only its frame threshold while existing live work remains retained", WaitingForSeal),
        ("Tween death repeated samples in the same frame cannot supply the second later-frame confirmation", SameFrame),
        ("Tween death frame regression permanently invalidates the interval and stops further rooted reads", FrameRegression),
        ("Tween death dead to live resurrection revokes a prior private death receipt without freeing its wrapper", Resurrection),
        ("Tween death changed root class official field or live native address retains Unknown responsibility", ChangedIdentity),
        ("Tween death receipt refuses another independent handle even when its copied pointer identity matches", ForeignRoot),
        ("Tween death busy native claim captures live first and binds only the eventual actual sealed lease epoch", PendingClaim),
        ("Tween death actual source epoch changes and alive cached disagreements do not publish confirmation", ChangedSource),
        ("Tween death receipt revalidates the current sealed lease and cannot survive a recycled slot epoch", ReceiptLeaseFreshness),
        ("Tween death receipt rejects native cached-pointer resurrection before any later death pump", ReceiptNativeFreshness),
        ("Tween death worker or reentrant capture cannot spend another native observation", ThreadAndReentry)
    };
    private static void LiveThenTwoFrames()
    {
        using var f = new Fixture(); var baseline = f.State;
        Check(baseline.LiveFrame == 10 && baseline.OriginalNative == 0x4444 && !baseline.Production && baseline.ReturnFrame == -1);
        f.Return(); f.Backend.Lease.Seal();
        f.Dead(11); f.Pump(); Check(f.State.FirstDeathFrame == 11 && f.Receipt is null);
        f.Dead(12); f.Pump(); var receipt = f.Receipt!;
        Check(receipt is not null && !receipt.Production && receipt.IsFor(f.Owner, f.Backend.SourceRoot, null, 41));
        Check(f.State.Phase == NativeLocalTweenDeathPhase.Confirmed && f.State.ConfirmedFrame == 12 && f.State.ReturnFrame == 10);
        Check(f.Backend.Frees == 0 && f.Backend.Native.Unregisters == 0 && !f.Owner.CanRetireWork());
    }
    private static void InitiallyZero()
    {
        using var f = new Fixture(initiallyDead: true); f.Return(); f.Backend.Lease.Seal();
        for (var frame = 11; frame < 15; ++frame) { f.Dead(frame); f.Pump(); }
        Check(!f.State.Unknown && f.State.LiveFrame == -1 && f.State.FirstDeathFrame == -1 && f.Receipt is null);
        Check(f.Backend.Frees == 0 && !f.Owner.CanRetireWork());
    }
    private static void NoReturnSource()
    {
        using var f = new Fixture(); f.Backend.Lease.Seal(); var reads = f.Backend.ObservationReads;
        f.Dead(20); f.Pump(); f.Dead(21); f.Pump();
        Check(f.State.ReturnFrame == -1 && f.Receipt is null && f.Backend.ObservationReads == reads && !f.State.Unknown);
        Throws<InvalidOperationException>(() => f.Owner.PumpActualTweenDeaths(null!, null!));
        Check(f.Backend.Frees == 0 && f.Backend.Native.Unregisters == 0);
    }
    private static void WaitingForSeal()
    {
        using var f = new Fixture(); f.Return(); var reads = f.Backend.ObservationReads;
        f.Dead(11); f.Pump();
        Check(f.State.Phase == NativeLocalTweenDeathPhase.AwaitingSealedSource && f.Backend.ObservationReads == reads && f.Receipt is null);
        f.Backend.Lease.Seal(); f.Pump(); Check(f.State.FirstDeathFrame == 11 && f.Receipt is null);
        f.Dead(12); f.Pump(); Check(f.Receipt is not null && !f.State.Unknown && !f.Owner.CanRetireWork());
    }
    private static void SameFrame()
    {
        using var f = new Fixture(); f.Return(); f.Backend.Lease.Seal();
        f.Dead(10); f.Pump(); Check(f.State.FirstDeathFrame == -1);
        f.Dead(11); f.Pump(); f.Pump(); f.Pump();
        Check(f.State.FirstDeathFrame == 11 && f.State.ConfirmedFrame == -1 && f.Receipt is null);
        f.Dead(12); f.Pump(); Check(f.Receipt is not null);
    }
    private static void FrameRegression()
    {
        using var f = new Fixture(); f.Return(); f.Backend.Lease.Seal(); f.Dead(11); f.Pump(); f.Dead(9);
        Throws<InvalidOperationException>(f.Pump); var reads = f.Backend.ObservationReads;
        Check(f.State.Unknown && f.Receipt is null && f.Owner.Unknown);
        Throws<InvalidOperationException>(f.Pump); Equal(reads, f.Backend.ObservationReads); Equal(0, f.Backend.Frees);
    }
    private static void Resurrection()
    {
        using var f = new Fixture(); f.Confirm(); var receipt = f.Receipt!;
        f.Backend.Observed = f.Backend.Observed with { Frame = 13, Alive = true, CachedNative = 0x4444 };
        Throws<InvalidOperationException>(f.Pump);
        Check(f.State.Unknown && f.Receipt is null && !receipt.IsFor(f.Owner, f.Backend.SourceRoot, null, 41) && f.Backend.Frees == 0);
    }
    private static void ChangedIdentity()
    {
        foreach (var mode in new[] { "root", "class", "field", "native", "receiver" })
        {
            using var f = new Fixture(); f.Return(); f.Backend.Lease.Seal();
            var observation = f.Backend.Observed with { Frame = 11 };
            if (mode == "root") f.Backend.GraphTarget = new(0x9999);
            if (mode == "class") observation = observation with { ManagedClass = new(0x7777) };
            if (mode == "field") observation = observation with { CachedPointerField = new(0x7777) };
            if (mode == "native") observation = observation with { CachedNative = 0x5555 };
            if (mode == "receiver") observation = observation with { ManagedReceiver = 0x9999 };
            f.Backend.Observed = observation; Throws<InvalidOperationException>(f.Pump);
            Check(f.Owner.Unknown && f.State.Unknown && f.Receipt is null && f.Backend.Frees == 0);
        }
    }
    private static void ForeignRoot()
    {
        using var f = new Fixture(); f.Confirm(); var receipt = f.Receipt!;
        var alias = f.Backend.MakeRoot(new(0x1111));
        Check(!receipt.IsFor(f.Owner, alias, null, 41) && !receipt.IsFor(f.Owner, f.Backend.SourceRoot, null, 42));
        Throws<InvalidOperationException>(() => f.Owner.ReadTweenDeathReceipt(alias));
        using var peer = new Fixture(); Check(!receipt.IsFor(peer.Owner, f.Backend.SourceRoot, null, 41));
        Equal(0, f.Backend.Frees);
    }
    private static void PendingClaim()
    {
        using var f = new Fixture(pendingClaim: true); Check(f.Backend.Lease.ClaimPending && f.State.LiveFrame == 10);
        f.Return(); Check(f.State.SourceEpoch == 0 && f.State.ReturnFrame == 10);
        f.Backend.Native.ClaimBusy = false; f.Backend.Lease.Seal();
        f.Dead(11); f.Pump(); f.Dead(12); f.Pump();
        Check(f.Receipt is not null && f.State.SourceEpoch == 41 && f.Backend.Lease.Claimed && !f.Receipt!.Production);
    }
    private static void ChangedSource()
    {
        using (var f = new Fixture())
        {
            f.Return(); f.Backend.Lease.Seal(); f.Dead(11); f.Pump();
            f.Backend.Native.Current.Epoch = 42; f.Dead(12);
            Throws<UnsupportedSignatureException>(f.Pump); Check(f.State.Unknown && f.Receipt is null && f.Backend.Frees == 0);
        }
        using (var f = new Fixture())
        {
            f.Return(); f.Backend.Lease.Seal(); f.Backend.Observed = f.Backend.Observed with { Frame = 11, Alive = false, CachedNative = 0x4444 };
            Throws<InvalidOperationException>(f.Pump); Check(f.State.Unknown && f.Receipt is null);
        }
    }
    private static void ReceiptLeaseFreshness()
    {
        using var f = new Fixture(); f.Confirm(); var receipt = f.Receipt!;
        var observations = f.Backend.ObservationReads;
        var leaseReads = f.Backend.Native.ReadCalls;
        Check(receipt.IsFor(f.Owner, f.Backend.SourceRoot, null, 41));
        Check(f.Backend.Native.ReadCalls == leaseReads + 1 && f.Backend.ObservationReads == observations && !f.Owner.Unknown);

        // Simulate native slot recycling after the receipt was issued. A stale
        // receipt must be checked against that current lease without requiring
        // another frame sample or another death observation.
        f.Backend.Native.Current.Epoch = 42;
        Check(!receipt.IsFor(f.Owner, f.Backend.SourceRoot, null, 41));
        Check(f.Owner.Unknown && f.Backend.Lease.Unknown && f.Backend.Native.Unregisters == 0 && f.Backend.Frees == 0
            && f.Backend.ObservationReads == observations);
    }
    private static void ThreadAndReentry()
    {
        using var f = new Fixture(); f.Return(); f.Backend.Lease.Seal(); var reads = f.Backend.ObservationReads;
        Task.Run(() => Throws<InvalidOperationException>(f.Pump)).GetAwaiter().GetResult(); Equal(reads, f.Backend.ObservationReads);
        f.Backend.DuringObservation = () => Throws<InvalidOperationException>(f.Pump);
        f.Dead(11); f.Pump(); Check(!f.State.Unknown && f.State.FirstDeathFrame == 11);
    }
    private static void ReceiptNativeFreshness()
    {
        using var f = new Fixture(); f.Confirm(); var receipt = f.Receipt!;
        var observations = f.Backend.ObservationReads;
        Check(receipt.IsFor(f.Owner, f.Backend.SourceRoot, null, 41));
        // Keep the same slot, epoch, class and sealed phase. Only the native
        // pointer changes, before another frame tick could sample resurrection.
        f.Backend.Native.Current.CachedNative = new(0x5555);
        Check(!receipt.IsFor(f.Owner, f.Backend.SourceRoot, null, 41));
        Check(f.Owner.Unknown && f.Receipt is null && f.Backend.ObservationReads == observations
            && f.Backend.Native.Unregisters == 0 && f.Backend.Frees == 0);
        f.Backend.Native.Current.CachedNative = IntPtr.Zero;
        Check(!receipt.IsFor(f.Owner, f.Backend.SourceRoot, null, 41) && f.Owner.Unknown);
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new(); internal readonly Backend Backend; internal readonly NativeLocalTweenWorkOwner Owner;
        internal Fixture(bool initiallyDead = false, bool pendingClaim = false)
        { Backend = new(Main, initiallyDead, pendingClaim); Owner = NativeLocalTweenWorkOwner.CaptureBackend(Main, Backend); Owner.CaptureControlledTweenDeathSourcesForTests(); }
        internal NativeLocalTweenDeathSnapshot State => Owner.ReadTweenDeaths().Single();
        internal NativeLocalTweenDeathTracker.DeathReceipt? Receipt => Owner.ReadTweenDeathReceipt(Backend.SourceRoot);
        internal void Return() { Owner.RequestControlledTweenDeathReturnForTests(); Pump(); }
        internal void Dead(int frame)
        {
            Backend.Observed = Backend.Observed with { Frame = frame, Alive = false, CachedNative = 0 };
            Backend.Native.Current.CachedNative = IntPtr.Zero;
        }
        internal void Pump() => Owner.PumpControlledTweenDeathsForTests();
        internal void Confirm() { Return(); Backend.Lease.Seal(); Dead(11); Pump(); Dead(12); Pump(); }
        public void Dispose() => Main.Dispose(); // Native-like independent root/lease responsibilities remain retained.
    }
    private sealed class Backend : INativeLocalTweenBackend, INativeLocalTweenDeathBackend, INativeTweenAdmissionOwner
    {
        private readonly MainThreadDispatcher _main; private NativeLocalTweenWorkOwner _owner = null!;
        private readonly Dictionary<uint, IntPtr> _roots = new(); private uint _next;
        internal readonly NativeRuntime.RootHandle SourceRoot; internal readonly Transport Native; private readonly NativeTweenAdmission _admission;
        internal NativeTweenAdmissionLease Lease = null!;
        internal NativeTweenDeathObservation Observed; internal IntPtr GraphTarget = new(0x1111);
        internal int Frees, ObservationReads; internal Action? DuringObservation;
        internal Backend(MainThreadDispatcher main, bool initiallyDead, bool pendingClaim)
        {
            _main = main; SourceRoot = new(1, _ => ++Frees, _ => GraphTarget, main.VerifyAccess);
            Observed = new(0x1111, new(0x2222), new(0x3333), initiallyDead ? 0 : 0x4444, !initiallyDead, 10);
            Native = new() { ClaimBusy = pendingClaim };
            _admission = NativeTweenAdmission.CreateControlledForTests(Native, new() { Version = 1, Size = 32, ExactTweenClass = new(0x2222),
                CachedPointerField = new(0x3333), CachedPointerOffset = 16 }, main.VerifyAccess, MakeRoot, root => root.Target);
        }
        internal NativeRuntime.RootHandle MakeRoot(IntPtr identity)
        { var id = ++_next; _roots.Add(id, identity); return new(id, key => { ++Frees; _roots.Remove(key); }, key => _roots.GetValueOrDefault(key), _main.VerifyAccess); }
        public bool CleanupUnknown => false; public int RootRecords => 1 + _admission.RootRecords; public IntPtr PlayMethodInfo => new(700);
        public void Initialize(NativeLocalTweenWorkOwner owner) { _owner = owner; owner.RegisterSource(0x1111); Lease = _admission.Claim(SourceRoot, this); }
        public NativeTweenWorkGraph Capture(NativeTweenOperation operation, NativeOperationFrame frame, bool after) => Refresh();
        public NativeTweenWorkGraph Refresh() => new(0, Array.Empty<NativeTweenDelegateObservation>(), Array.Empty<NativeTweenInfoObservation>(), Array.Empty<NativeTweenManagerObservation>());
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity() => new(0, true);
        public void VerifyTweenAdmissionOwner() { _main.VerifyAccess(); if (_owner is null || _owner.ObservationAuthorityUnknown) throw new InvalidOperationException("Controlled source owner is not known."); }
        public void VerifyTweenDeathOwner(NativeLocalTweenWorkOwner owner)
        { VerifyTweenAdmissionOwner(); if (!ReferenceEquals(_owner, owner)) throw new InvalidOperationException("Foreign death owner."); }
        public void BorrowTweenDeathSources(NativeLocalTweenWorkOwner owner, Action<NativeRuntime.RootHandle, NativeTweenAdmissionLease> borrow)
        { VerifyTweenDeathOwner(owner); borrow(SourceRoot, Lease); }
        public NativeTweenDeathObservation ReadTweenDeathObservation(NativeRuntime.RootHandle root)
        { _main.VerifyAccess(); if (!ReferenceEquals(root, SourceRoot)) throw new InvalidOperationException("Foreign independent death root.");
            ++ObservationReads; DuringObservation?.Invoke(); return Observed; }
    }
    private sealed class Transport : INativeTweenAdmissionTransport
    {
        internal bool ClaimBusy; internal int Unregisters, ReadCalls;
        internal NativeTweenAdmissionReceipt Current = new() { Version = 1, Size = 96, Slot = 3, Epoch = 41,
            Phase = NativeTweenAdmissionPhase.Open, Object = new(0x1111), ActualClass = new(0x2222), CachedNative = new(0x4444), AdmittedIssuers = 1 };
        public uint Configure(ref NativeTweenAdmissionConfiguration configuration) => 0;
        public uint GetInfo(ref NativeTweenAdmissionInformation information)
        { information = new() { Version = 1, Size = 104, Configured = 1, ExactTweenClass = new(0x2222), CachedPointerField = new(0x3333),
            CachedPointerOffset = 16, Capacity = 128, ConfigurationEpoch = 7 }; return 0; }
        public uint Claim(IntPtr actualTween, ref NativeTweenAdmissionReceipt receipt)
        { if (ClaimBusy) return 6; receipt = Current; return 0; }
        public uint Read(uint slot, ulong epoch, ref NativeTweenAdmissionReceipt receipt)
        { ++ReadCalls; receipt = Current; return 0; }
        public uint Seal(uint slot, ulong epoch, ref NativeTweenAdmissionReceipt receipt) { Current.Phase = NativeTweenAdmissionPhase.Sealed; receipt = Current; return 0; }
        public uint Unregister(uint slot, ulong epoch, ref NativeTweenAdmissionReceipt receipt)
        { ++Unregisters; throw new InvalidOperationException("Death observations have no unregister authority."); }
    }
}
