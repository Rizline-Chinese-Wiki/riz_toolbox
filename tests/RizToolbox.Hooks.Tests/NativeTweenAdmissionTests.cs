using System.Reflection;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

internal static partial class NativeTweenAdmissionTests
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Tween admission wire matches the actual private V1 native layout", Wire),
        ("Tween admission claims independently rooted actual inputs before native entry", ClaimOrder),
        ("Tween admission physical busy retries the same rooted lease without claiming or sealing a DTO", Pending),
        ("Tween admission rejected duplicate unknown and throwing claims retain independent process roots", ClaimFailure),
        ("Tween admission seals monotonically and never exposes root release or native unregister", Seal),
        ("Tween admission refuses changed slot epoch object class flags phase and regressed counters", ChangedReceipt),
        ("Tween admission checks actual global policy before and after each native owner observation", ChangedPolicy),
        ("Tween admission controlled transport cannot create production authority and worker calls stop before native entry", Authority),
        ("Tween dynamic admission shares its backend process root without allocating or owning a second free", SharedRetainedClaim),
        ("Tween shared-root busy claim and seal fault retain the same handle and exact retention responsibility", SharedRetainedFailure),
        ("Tween prepared retirement freezes the full owner set and rejects late claims before touching their root", RetirementEnrollment),
        ("Tween retirement preparation refuses living unsealed or active claims without any free or native unregister", RetirementIncomplete),
        ("Tween real managed Cut commits exact native Retired receipts and frees independent admission handles once", RetirementCommit),
        ("Tween managed Cut drops only a shared admission borrow and rejects foreign or stale batch consumption", RetirementShared),
        ("Tween malformed native retirement tuples retain every handle and reject repeated native commit", RetirementBadReceipt),
        ("Tween retirement records a throwing free before the call and never repeats the attempt", RetirementFreeFailure)
    };
    private static void Wire()
    {
        NativeTweenAdmission.ValidateLayout();
        using var f = new Fixture(); var info = f.Native.Information; var configuration = f.Configuration;
        Check(NativeTweenAdmission.InformationMatches(info, configuration));
        info.Unknown = 1; Check(!NativeTweenAdmission.InformationMatches(info, configuration));
        info = f.Native.Information; info.ConfigurationEpoch = 0; Check(!NativeTweenAdmission.InformationMatches(info, configuration));
        var receipt = f.Native.Current;
        Check(NativeTweenAdmission.ReceiptMatches(receipt, f.Object, f.Class, 3, 41));
        receipt.Phase = NativeTweenAdmissionPhase.Sealed; receipt.IssuersInFlight = 1; receipt.ActiveEntries = 1;
        Check(!NativeTweenAdmission.ReceiptMatches(receipt, f.Object, f.Class, 3, 41));
        Check(!NativeTweenAdmission.CanAdvance(NativeTweenAdmissionPhase.Sealed, NativeTweenAdmissionPhase.Open, false));
    }
    private static void ClaimOrder()
    {
        using var f = new Fixture(); using var borrowed = f.Borrow(); var nativeSawIndependent = false;
        f.Native.OnClaim = () => { nativeSawIndependent = f.ActiveRoots == 2 && f.Admission.RootRecords == 1; };
        var lease = f.Admission.Claim(borrowed, f.Owner);
        Check(nativeSawIndependent && lease.Claimed && !lease.ClaimPending && lease.Slot == 3 && lease.Epoch == 41
            && lease.Root!.Target == f.Object && f.Admission.RootRecords == 1 && f.NewRoots == 2 && f.FreeRoots == 0);
        borrowed.Dispose(); Check(f.ActiveRoots == 1 && lease.Root!.Target == f.Object);
    }
    private static void Pending()
    {
        using var f = new Fixture(); using var borrowed = f.Borrow(); f.Native.ClaimStatus = 6;
        var lease = f.Admission.Claim(borrowed, f.Owner);
        Check(lease.ClaimPending && !lease.Claimed && lease.Epoch == 0 && f.ActiveRoots == 2);
        var pending = lease.Seal();
        Check(pending.Known && pending.Phase == NativeTweenAdmissionPhase.Empty && pending.Epoch == 0 && !pending.IsSealed
            && f.Native.Claims == 2 && f.Native.Seals == 0 && f.NewRoots == 2);
        f.Native.ClaimStatus = 0; f.Native.SealPhase = NativeTweenAdmissionPhase.Sealed;
        var sealedValue = lease.Seal();
        Check(sealedValue.IsSealed && lease.Claimed && !lease.ClaimPending && f.Native.Claims == 3 && f.Native.Seals == 1
            && f.NewRoots == 2 && f.Admission.RootRecords == 1 && f.FreeRoots == 0);
    }
    private static void ClaimFailure()
    {
        foreach (var status in new uint[] { 1, 2, 3, 4, 5 })
        {
            using var f = new Fixture(); using var borrowed = f.Borrow(); f.Native.ClaimStatus = status;
            Throws<UnsupportedSignatureException>(() => f.Admission.Claim(borrowed, f.Owner));
            Check(f.Admission.Unknown && f.Admission.RootRecords == 1 && f.ActiveRoots == 2 && f.FreeRoots == 0 && f.Native.Claims == 1);
            Throws<InvalidOperationException>(() => f.Admission.Claim(borrowed, f.Owner)); Check(f.NewRoots == 2);
        }
        using var throwing = new Fixture(); using var input = throwing.Borrow(); throwing.Native.OnClaim = () => throw new IOException("Unknown native claim outcome.");
        Throws<IOException>(() => throwing.Admission.Claim(input, throwing.Owner));
        Check(throwing.Admission.Unknown && throwing.Admission.RootRecords == 1 && throwing.ActiveRoots == 2 && throwing.FreeRoots == 0);
    }
    private static void Seal()
    {
        using var f = new Fixture(); using var borrowed = f.Borrow(); var lease = f.Admission.Claim(borrowed, f.Owner);
        f.Native.SealStatus = 3; f.Native.SealPhase = NativeTweenAdmissionPhase.Closing; f.Native.Current.IssuersInFlight = 1; f.Native.Current.ActiveEntries = 2;
        var closing = lease.Seal(); Check(closing.Known && !closing.IsSealed && closing.InFlight == 2);
        f.Native.SealStatus = 0; f.Native.SealPhase = NativeTweenAdmissionPhase.Sealed; f.Native.Current.IssuersInFlight = 0;
        var sealedValue = lease.Seal(); Check(sealedValue.IsSealed && sealedValue.ActiveEntries == 2 && f.FreeRoots == 0);
        // Sealed may retain a live Execute; it is not a work-completed receipt.
        Check(!typeof(IDisposable).IsAssignableFrom(typeof(NativeTweenAdmissionLease))
            && typeof(NativeTweenAdmission).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => method.Name.Contains("Unregister", StringComparison.Ordinal)).All(method => method.IsPrivate));
        Check(f.Native.Unregisters == 0 && f.Admission.RootRecords == 1);
    }
    private static void ChangedReceipt()
    {
        foreach (var mode in new[] { "slot", "epoch", "object", "class", "unknown", "reserved", "phase", "counter", "issuers" })
        {
            using var f = new Fixture(); using var borrowed = f.Borrow(); var lease = f.Admission.Claim(borrowed, f.Owner);
            if (mode == "slot") ++f.Native.Current.Slot;
            if (mode == "epoch") ++f.Native.Current.Epoch;
            if (mode == "object") f.Native.Current.Object = new(0x777);
            if (mode == "class") f.Native.Current.ActualClass = new(0x778);
            if (mode == "unknown") f.Native.Current.Unknown = 1;
            if (mode == "reserved") f.Native.Current.Reserved = 1;
            if (mode == "phase") f.Native.Current.Phase = NativeTweenAdmissionPhase.Retired;
            if (mode == "counter") f.Native.Current.AdmittedIssuers = 0;
            if (mode == "issuers") f.Native.Current.IssuersInFlight = 1;
            Throws<UnsupportedSignatureException>(() => lease.Read());
            Check(f.Admission.Unknown && lease.Unknown && f.ActiveRoots == 2 && f.FreeRoots == 0);
        }
    }
    private static void ChangedPolicy()
    {
        foreach (var mode in new[] { "global", "epoch", "class", "field", "read-fault" })
        {
            using var f = new Fixture(); using var borrowed = f.Borrow(); var lease = f.Admission.Claim(borrowed, f.Owner);
            f.Native.OnRead = () => {
                if (mode == "global") f.Native.Information.Unknown = 1;
                if (mode == "epoch") ++f.Native.Information.ConfigurationEpoch;
                if (mode == "class") f.Native.Information.ExactTweenClass = new(0xDEAD);
                if (mode == "field") f.Native.Information.CachedPointerField = new(0xBAD);
                if (mode == "read-fault") ++f.Native.Information.UnknownReads;
            };
            Throws<UnsupportedSignatureException>(() => lease.Read());
            Check(f.Admission.Unknown && lease.Unknown && f.ActiveRoots == 2 && f.FreeRoots == 0 && f.Native.Reads == 1);
        }
        using var ownerA = new Fixture(); using var ownerB = new Fixture(ownerA.Native);
        using var inputA = ownerA.Borrow(); using var inputB = ownerB.Borrow();
        var leaseA = ownerA.Admission.Claim(inputA, ownerA.Owner); var leaseB = ownerB.Admission.Claim(inputB, ownerB.Owner);
        ownerA.Native.OnRead = () => ownerA.Native.Information.Unknown = 1;
        Throws<UnsupportedSignatureException>(() => leaseA.Read());
        var actualReads = ownerA.Native.Reads;
        Throws<UnsupportedSignatureException>(() => leaseB.Read());
        Check(ownerB.Admission.Unknown && leaseB.Unknown && ownerA.Native.Reads == actualReads && ownerB.FreeRoots == 0);
    }
    private static void Authority()
    {
        using var f = new Fixture(); using var borrowed = f.Borrow(); Check(!f.Admission.Production);
        Throws<InvalidOperationException>(() => new NativeTweenAdmissionLease(f.Admission, f.Owner, f.Object, new object()));
        var claims = f.Native.Claims;
        Task.Run(() => Throws<InvalidOperationException>(() => f.Admission.Claim(borrowed, f.Owner))).GetAwaiter().GetResult();
        Check(f.Native.Claims == claims && f.NewRoots == 1 && f.Admission.RootRecords == 0);
    }

    private static void SharedRetainedClaim()
    {
        using var f = new Fixture(); var retained = f.Borrow(); var beforeClaim = false;
        f.Native.OnClaim = () => { beforeClaim = f.NewRoots == 1 && f.Admission.RootRecords == 1
            && f.Admission.IndependentRootRecords == 0 && f.Admission.SharedRootRecords == 1; };
        var lease = f.Admission.ClaimRetainedRoot(retained, f.Owner);
        Check(beforeClaim && ReferenceEquals(lease.Root, retained) && lease.RootRetention == NativeTweenAdmissionRootRetention.SharedRetainedProcessRoot
            && lease.Claimed && !lease.Unknown && f.ActiveRoots == 1 && f.FreeRoots == 0);
        Check(lease.Seal().IsSealed && f.NewRoots == 1 && f.FreeRoots == 0 && retained.Target == f.Object && f.Native.Unregisters == 0);
    }

    private static void SharedRetainedFailure()
    {
        using (var f = new Fixture())
        {
            var retained = f.Borrow(); f.Native.ClaimStatus = 6;
            var lease = f.Admission.ClaimRetainedRoot(retained, f.Owner);
            Check(lease.ClaimPending && !lease.Claimed && ReferenceEquals(lease.Root, retained) && f.NewRoots == 1
                && f.Admission.SharedRootRecords == 1 && f.Admission.IndependentRootRecords == 0);
            Check(lease.Seal().Phase == NativeTweenAdmissionPhase.Empty && f.Native.Seals == 0 && f.NewRoots == 1 && f.FreeRoots == 0);
            f.Native.ClaimStatus = 0; f.Native.SealStatus = 9;
            Throws<UnsupportedSignatureException>(() => lease.Seal());
            Check(f.Admission.Unknown && lease.Unknown && ReferenceEquals(lease.Root, retained) && retained.Target == f.Object
                && f.NewRoots == 1 && f.FreeRoots == 0 && f.Native.Unregisters == 0 && f.Admission.SharedRootRecords == 1);
        }
        using (var f = new Fixture())
        {
            var retained = f.Borrow(); f.Native.ClaimStatus = 3;
            Throws<UnsupportedSignatureException>(() => f.Admission.ClaimRetainedRoot(retained, f.Owner));
            Check(f.Admission.Unknown && f.Admission.SharedRootRecords == 1 && f.Admission.IndependentRootRecords == 0
                && retained.Target == f.Object && f.NewRoots == 1 && f.FreeRoots == 0 && f.Native.Unregisters == 0);
        }
    }

    private sealed class Owner : INativeTweenRetirementCutOwner
    {
        private readonly Action _verify;
        internal NativeTweenRetirementContext? Context;
        internal NativeTweenAdmission.RetirementBatch? Batch;
        internal NativeTweenSource.Withdrawal? Withdrawal;
        internal Owner(Action verify) => _verify = verify;
        public void VerifyTweenAdmissionOwner() => _verify();
        public void VerifyTweenSourceOwner() => _verify();
        public void VerifyTweenRetirementCut(NativeTweenRetirementContext context)
        { _verify(); if (!ReferenceEquals(Context, context) || Batch is null || Withdrawal is null) throw new InvalidOperationException("No prepared owner withdrawal interval."); }
        public NativeTweenAdmission.RetirementBatch BorrowTweenAdmissionRetirementBatch(NativeTweenRetirementContext context)
        { VerifyTweenRetirementCut(context); return Batch!; }
    }
    private sealed class Transport : INativeTweenAdmissionTransport
    {
        internal NativeTweenAdmissionInformation Information;
        internal NativeTweenAdmissionReceipt Current;
        internal uint ClaimStatus, SealStatus;
        internal NativeTweenAdmissionPhase SealPhase = NativeTweenAdmissionPhase.Sealed;
        internal int Claims, Reads, Seals, Unregisters;
        internal Action? OnClaim, OnRead;
        public uint Configure(ref NativeTweenAdmissionConfiguration configuration) => 0;
        public uint GetInfo(ref NativeTweenAdmissionInformation information) { information = Information; return 0; }
        public uint Claim(IntPtr actualTween, ref NativeTweenAdmissionReceipt receipt) { ++Claims; OnClaim?.Invoke(); if (ClaimStatus == 0) receipt = Current; return ClaimStatus; }
        public uint Read(uint slot, ulong epoch, ref NativeTweenAdmissionReceipt receipt) { ++Reads; receipt = Current; OnRead?.Invoke(); return 0; }
        public uint Seal(uint slot, ulong epoch, ref NativeTweenAdmissionReceipt receipt) { ++Seals; Current.Phase = SealPhase; receipt = Current; return SealStatus; }
        public uint Unregister(uint slot, ulong epoch, ref NativeTweenAdmissionReceipt receipt) { ++Unregisters; throw new InvalidOperationException("No completion interval exists."); }
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly NativeThreadAccess _thread = new(); private readonly Dictionary<uint, IntPtr> _roots = new(); private uint _next;
        internal readonly IntPtr Object = new(0x1111), Class = new(0x2222), Field = new(0x3333);
        internal readonly Transport Native; internal readonly Owner Owner;
        internal readonly NativeTweenAdmission Admission;
        internal NativeTweenAdmissionConfiguration Configuration => new() { Version = 1, Size = 32, ExactTweenClass = Class, CachedPointerField = Field, CachedPointerOffset = 16 };
        internal int NewRoots, FreeRoots, TargetReads; internal bool FailFree; internal int ActiveRoots => _roots.Count;
        internal Fixture(Transport? shared = null)
        {
            _thread.BindCurrentThread(); Owner = new(_thread.Verify); Native = shared ?? new();
            if (shared is null)
            {
                Native.Information = new() { Version = 1, Size = 104, Configured = 1, ExactTweenClass = Class, CachedPointerField = Field, CachedPointerOffset = 16, Capacity = 128, ConfigurationEpoch = 7 };
                Native.Current = new() { Version = 1, Size = 96, Slot = 3, Epoch = 41, Phase = NativeTweenAdmissionPhase.Open,
                    Object = Object, ActualClass = Class, CachedNative = new(0x4444), AdmittedIssuers = 1 };
            }
            Admission = NativeTweenAdmission.CreateControlledForTests(Native, Configuration, _thread.Verify, Adopt, root => root.Target);
        }
        internal NativeRuntime.RootHandle Borrow() => Adopt(Object);
        internal NativeRuntime.RootHandle Adopt(IntPtr value)
        { _thread.Verify(); var id = ++_next; _roots.Add(id, value); ++NewRoots; return new(id, key => { ++FreeRoots; if (FailFree) throw new IOException("Ambiguous native root free."); _roots.Remove(key); }, key => { ++TargetReads; return _roots.GetValueOrDefault(key); }, _thread.Verify); }
        public void Dispose() { /* Independent claim roots intentionally remain process-owned. */ }
    }
    private static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Controlled Tween admission invariant failed."); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
