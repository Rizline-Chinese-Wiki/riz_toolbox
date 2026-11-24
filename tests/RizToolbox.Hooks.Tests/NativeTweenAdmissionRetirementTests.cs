using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

internal static partial class NativeTweenAdmissionTests
{
    private static void RetirementEnrollment()
    {
        using var f = new Fixture(); using var input = f.Borrow();
        var lease = f.Admission.Claim(input, f.Owner); lease.Seal(); f.Native.Current.CachedNative = IntPtr.Zero;
        var batch = f.Admission.PrepareRetirement(f.Owner);
        Check(batch.Claims.Count == 1 && batch.Claims[0].Slot == lease.Slot && batch.Claims[0].Epoch == lease.Epoch
            && batch.ConfigurationEpoch == 7 && !batch.Production && batch.Phase == NativeTweenAdmissionRetirementPhase.Prepared);
        var claims = f.Native.Claims; var roots = f.NewRoots;
        Check(ReferenceEquals(batch, f.Admission.PrepareRetirement(f.Owner)));
        // A disposed input makes a premature Target read throw ObjectDisposed;
        // the closed owner must be rejected before any input dereference.
        var disposedInput = f.Borrow(); disposedInput.Dispose();
        var targetReads = f.TargetReads;
        try { f.Admission.ClaimRetainedRoot(disposedInput, f.Owner); throw new Exception("Closed enrollment was accepted."); }
        catch (ObjectDisposedException) { throw new Exception("The disposed input was touched before enrollment rejection."); }
        catch (InvalidOperationException) { }
        Check(f.Native.Claims == claims && f.NewRoots == roots + 1 && f.Native.Unregisters == 0 && !lease.NativeRetired && f.TargetReads == targetReads);
    }

    private static void RetirementIncomplete()
    {
        foreach (var mode in new[] { "live", "entry", "open" })
        {
            using var f = new Fixture(); using var input = f.Borrow(); var lease = f.Admission.Claim(input, f.Owner);
            if (mode != "open") lease.Seal();
            f.Native.Current.CachedNative = mode == "live" ? new(0x4444) : IntPtr.Zero;
            if (mode == "entry") f.Native.Current.ActiveEntries = 1;
            Throws<InvalidOperationException>(() => f.Admission.PrepareRetirement(f.Owner));
            Check(f.Admission.Unknown && lease.Unknown && f.FreeRoots == 0 && f.Native.Unregisters == 0 && !lease.NativeRetired);
        }
    }

    private static void RetirementCommit()
    {
        var h = new RetirementHarness(shared: false);
        NativeTweenRetirementContext.Cut? retained = null;
        Check(h.Context.WithCut(h.Withdrawal, cut => {
            retained = cut; h.Batch.CommitNative(cut); var reads = h.F.Native.Reads; var targets = h.F.TargetReads;
            Check(h.Lease.NativeRetired && h.Lease.Phase == NativeTweenAdmissionPhase.Retired);
            Throws<InvalidOperationException>(() => h.Lease.Read());
            Check(h.F.Native.Reads == reads && h.F.TargetReads == targets);
            h.Batch.ReleaseRoots(cut); h.Batch.VerifyNativeCommittedIdentity();
            Check(h.Lease.OwnRootFreeAttempted && h.Lease.OwnRootFreeSucceeded && !h.Lease.RootRecorded
                && h.F.Admission.RootRecords == 0 && h.F.FreeRoots == 1);
            Throws<InvalidOperationException>(() => h.Batch.ReleaseRoots(cut));
        }));
        Check(h.Native.Commits == 1 && h.Native.Releases == 1 && h.Batch.Phase == NativeTweenAdmissionRetirementPhase.RootsReleased);
        Throws<InvalidOperationException>(() => h.Batch.CommitNative(retained!));
        Check(h.F.FreeRoots == 1 && h.Native.Commits == 1 && h.F.Native.Unregisters == 0);
    }

    private static void RetirementShared()
    {
        var h = new RetirementHarness(shared: true); var peer = new RetirementHarness(shared: false);
        NativeTweenRetirementContext.Cut? retained = null;
        Check(h.Context.WithCut(h.Withdrawal, cut => {
            retained = cut; Throws<InvalidOperationException>(() => peer.Batch.CommitNative(cut));
            Check(peer.Native.Commits == 0 && peer.F.FreeRoots == 0);
            h.Batch.CommitNative(cut); h.Batch.ReleaseRoots(cut);
        }));
        Check(h.Lease.SharedBorrowReleased && !h.Lease.OwnRootFreeAttempted && h.F.FreeRoots == 0
            && h.Input.Target == h.F.Object && h.F.Admission.SharedRootRecords == 0);
        Throws<InvalidOperationException>(() => peer.Batch.CommitNative(retained!));
        Check(!peer.Batch.Unknown && peer.F.FreeRoots == 0);
        h.Input.Dispose(); Check(h.F.FreeRoots == 1);
    }

    private static void RetirementBadReceipt()
    {
        foreach (var mode in new[] { "epoch", "object", "live", "inflight", "empty" })
        {
            var h = new RetirementHarness(shared: false); h.Native.BadReceipt = mode;
            Throws<UnsupportedSignatureException>(() => h.Context.WithCut(h.Withdrawal, cut => h.Batch.CommitNative(cut)));
            Check(h.Batch.Unknown && h.Lease.Unknown && h.F.Admission.Unknown && h.F.FreeRoots == 0 && h.Native.Commits == 1);
            Throws<InvalidOperationException>(h.Batch.VerifyPrepared);
            Check(h.Native.Commits == 1 && h.F.FreeRoots == 0);
        }
    }

    private static void RetirementFreeFailure()
    {
        var h = new RetirementHarness(shared: false); h.F.FailFree = true;
        Throws<InvalidOperationException>(() => h.Context.WithCut(h.Withdrawal, cut => { h.Batch.CommitNative(cut); h.Batch.ReleaseRoots(cut); }));
        Check(h.Batch.Unknown && h.Lease.NativeRetired && h.Lease.OwnRootFreeAttempted && !h.Lease.OwnRootFreeSucceeded
            && h.Lease.RootRecorded && h.F.FreeRoots == 1 && h.Native.Commits == 1);
        Throws<InvalidOperationException>(h.Batch.VerifyPrepared);
        Check(h.F.FreeRoots == 1 && h.Native.Commits == 1);
    }

    private sealed class RetirementHarness
    {
        internal readonly Fixture F = new();
        internal readonly NativeRuntime.RootHandle Input;
        internal readonly NativeTweenAdmissionLease Lease;
        internal readonly NativeTweenAdmission.RetirementBatch Batch;
        internal readonly NativeTweenSource.Withdrawal Withdrawal;
        internal readonly RetirementTransport Native;
        internal readonly NativeTweenRetirementContext Context;
        internal RetirementHarness(bool shared)
        {
            Input = F.Borrow(); Lease = shared ? F.Admission.ClaimRetainedRoot(Input, F.Owner) : F.Admission.Claim(Input, F.Owner);
            Lease.Seal(); F.Native.Current.CachedNative = IntPtr.Zero;
            Batch = F.Admission.PrepareRetirement(F.Owner);
            var reader = NativeTweenSource.CreateControlledForTests(new RetirementSourceTransport(), F._thread.Verify, F.Adopt, (root, _) => root.Target);
            var roots = reader.RetainRootedObjects(F.Owner, new[] { new NativeTweenSourceRootReference(Input, NativeTweenSourceObjectKind.Tween) });
            Withdrawal = reader.BeginWithdrawal(roots);
            Native = new(F.Native); Context = NativeTweenRetirementContext.CreateControlledForTests(Native, F.Owner, F._thread.Verify);
            (F.Owner.Batch, F.Owner.Context, F.Owner.Withdrawal) = (Batch, Context, Withdrawal);
        }
    }
    private sealed class RetirementSourceTransport : INativeTweenSourceTransport
    {
        public uint GetInfo(ref NativeTweenSourceInformation information)
        { information = new() { Version = 1, Size = 96, Configured = 1, MetadataBound = 1, BindingCount = 43, JournalCapacity = 8192, Epoch = 9 }; return 0; }
        public uint ReadRecord(ulong epoch, ulong sequence, ref NativeTweenSourceRecord record) => 1;
    }
    private sealed class RetirementTransport : INativeTweenRetirementContextTransport
    {
        private readonly Transport _admission;
        private NativeTweenRetirementCutReceipt _current;
        internal int Commits, Releases; internal string? BadReceipt;
        internal RetirementTransport(Transport admission) => _admission = admission;
        public uint Acquire(ref NativeTweenRetirementRequest request, NativeTweenRetirementClaim[] claims, ref NativeTweenRetirementCutReceipt cut)
        {
            cut = _current = new() { Version = 1, Size = 88, Phase = 1, ThreadId = NativeTweenSourceReferences.CurrentThreadId(),
                Token = 42, Generation = 1, ClaimCount = (uint)claims.Length, ConfigurationEpoch = request.ConfigurationEpoch,
                SourceEpoch = request.SourceEpoch, JournalCutoff = request.JournalCutoff, ReferenceObserverEpoch = request.ReferenceObserverEpoch,
                ReferenceDeliveryCutoff = request.ReferenceDeliveryCutoff, ReferenceReaderGeneration = request.ReferenceReaderGeneration };
            return 0;
        }
        public uint Verify(ref NativeTweenRetirementCutReceipt cut) => cut.Equals(_current) ? 0u : 2u;
        public uint VerifyReferenceRoots(ref NativeTweenRetirementCutReceipt cut) => Verify(ref cut);
        public uint UnregisterBatch(ref NativeTweenRetirementCutReceipt cut, NativeTweenRetirementClaim[] claims, NativeTweenAdmissionReceipt[] receipts)
        {
            if (Verify(ref cut) != 0 || Commits != 0) return 2; ++Commits;
            for (var index = 0; index < receipts.Length; ++index)
            {
                var receipt = _admission.Current; receipt.Phase = NativeTweenAdmissionPhase.Retired;
                if (BadReceipt == "epoch") ++receipt.Epoch;
                if (BadReceipt == "object") receipt.Object = new(0x7777);
                if (BadReceipt == "live") receipt.CachedNative = new(0x8888);
                if (BadReceipt == "inflight") receipt.ActiveEntries = 1;
                if (BadReceipt == "empty") receipt = default;
                receipts[index] = receipt;
            }
            _admission.Current.Phase = NativeTweenAdmissionPhase.Retired; _admission.Current.Object = IntPtr.Zero;
            return 0;
        }
        public uint Release(ref NativeTweenRetirementCutReceipt cut) { ++Releases; return Verify(ref cut); }
    }
}
