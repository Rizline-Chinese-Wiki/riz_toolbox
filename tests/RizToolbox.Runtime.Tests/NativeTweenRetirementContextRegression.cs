using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

internal static class NativeTweenRetirementContextRegression
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Tween native cut accepts its actual empty complete claim group once and expires after synchronous release", EmptyGroup),
        ("Tween native cut treats a real no-mutation busy acquisition as pending and preserves its same owner", Busy),
        ("Tween native cut rejects worker access before native transport or source borrower reads", Worker),
        ("Tween native cut rejects forged native receipt and changed request without issuing release authority", BadReceipt),
        ("Tween native cut releases a captured loan after callback failure and retains Unknown without retry", CallbackFailure),
        ("Tween native cut retains Unknown when native loan release itself throws after a committed batch", ReleaseFailure),
        ("Tween native cut rechecks its exact owner interval before using an otherwise valid native token", OwnerRevoked)
    };
    private static void EmptyGroup()
    {
        using var f = new Fixture(); NativeTweenRetirementContext.Cut? saved = null;
        Check(f.Context.WithCut(f.Withdrawal, cut => {
            saved = cut; cut.VerifySourceWithdrawal(f.Withdrawal); cut.VerifyReferenceRoots(f.References);
            Check(!cut.Production && !cut.Committed && cut.SourceEpoch == 7 && cut.ReferenceObserverEpoch == 11);
            f.Batch.CommitNative(cut); Check(cut.Committed);
            Throws<InvalidOperationException>(() => cut.UnregisterBatch(f.Batch));
            f.Withdrawal.CommitRelease(cut); Check(f.Withdrawal.Committed); f.Batch.ReleaseRoots(cut);
            cut.VerifySourceWithdrawal(f.Withdrawal); cut.VerifyAdmissionBatch(f.Batch);
        }));
        Check(f.Native.Commits == 1 && f.Native.Releases == 1 && f.Frees == 1 && !f.Context.Unknown);
        Throws<InvalidOperationException>(saved!.VerifyCurrent);
        Throws<InvalidOperationException>(() => f.Context.WithCut(f.Withdrawal, _ => { }));
        Check(f.Native.Acquires == 1);
    }
    private static void Busy()
    {
        using var f = new Fixture(); f.Native.Busy = true; var invoked = false;
        Check(!f.Context.WithCut(f.Withdrawal, _ => invoked = true));
        Check(!invoked && !f.Context.Unknown && f.Native.Releases == 0);
        f.Native.Busy = false;
        Check(f.Context.WithCut(f.Withdrawal, cut => f.Batch.CommitNative(cut)));
        Check(f.Native.Acquires == 2 && f.Native.Commits == 1 && f.Native.Releases == 1);
    }
    private static void Worker()
    {
        using var f = new Fixture();
        Task.Run(() => Throws<InvalidOperationException>(() => f.Context.WithCut(f.Withdrawal, _ => { }))).GetAwaiter().GetResult();
        Check(f.Native.Acquires == 0 && !f.Context.Unknown);
        Check(f.Context.WithCut(f.Withdrawal, cut => {
            Task.Run(() => Throws<InvalidOperationException>(cut.VerifyCurrent)).GetAwaiter().GetResult();
            f.Batch.CommitNative(cut);
        }));
    }
    private static void BadReceipt()
    {
        foreach (var mode in new[] { "token", "thread", "source", "reference", "count", "request" })
        {
            using var f = new Fixture(); f.Native.Bad = mode; var invoked = false;
            Throws<UnsupportedSignatureException>(() => f.Context.WithCut(f.Withdrawal, _ => invoked = true));
            Check(f.Context.Unknown && !invoked && f.Native.Commits == 0 && f.Frees == 0);
        }
    }
    private static void CallbackFailure()
    {
        using var f = new Fixture();
        Throws<InvalidOperationException>(() => f.Context.WithCut(f.Withdrawal, cut => {
            f.Batch.CommitNative(cut); throw new InvalidOperationException("actual callback failure");
        }));
        Check(f.Context.Unknown && f.Native.Commits == 1 && f.Native.Releases == 1 && f.Frees == 0);
        Throws<InvalidOperationException>(() => f.Context.WithCut(f.Withdrawal, _ => { }));
        Check(f.Native.Acquires == 1);
    }
    private static void ReleaseFailure()
    {
        using var f = new Fixture(); f.Native.ReleaseThrows = true;
        NativeTweenRetirementContext.Cut? saved = null;
        Throws<InvalidOperationException>(() => f.Context.WithCut(f.Withdrawal, cut => { saved = cut; f.Batch.CommitNative(cut); }));
        Check(f.Context.Unknown && f.Native.Commits == 1 && f.Native.Releases == 1);
        Throws<InvalidOperationException>(saved!.VerifyCurrent);
        Throws<InvalidOperationException>(() => f.Context.WithCut(f.Withdrawal, _ => { }));
        Check(f.Native.Acquires == 1 && f.Native.Releases == 1);
    }
    private static void OwnerRevoked()
    {
        using var f = new Fixture();
        Throws<InvalidOperationException>(() => f.Context.WithCut(f.Withdrawal, cut => {
            f.Revoked = true; cut.VerifyCurrent();
        }));
        Check(f.Context.Unknown && f.Native.Commits == 0 && f.Native.Releases == 1 && f.Frees == 0);
    }
    private sealed class Fixture : INativeTweenRetirementCutOwner, IDisposable
    {
        private readonly int _thread = Environment.CurrentManagedThreadId;
        internal readonly NativeTweenRetirementContext Context;
        internal readonly NativeTweenSource.Withdrawal Withdrawal;
        internal readonly NativeTweenAdmission.RetirementBatch Batch;
        internal readonly NativeTweenSourceReferences References;
        internal readonly CutTransport Native = new();
        internal int Frees; internal bool Revoked;
        internal Fixture()
        {
            var admission = NativeTweenAdmission.CreateControlledForTests(new AdmissionTransport(),
                new() { Version = 1, Size = 32, ExactTweenClass = new(0x222), CachedPointerField = new(0x333), CachedPointerOffset = 16 },
                Verify, Root, root => root.Target);
            Batch = admission.PrepareRetirement(this);
            var reader = NativeTweenSource.CreateControlledForTests(new SourceTransport(), Verify, Root, (root, _) => root.Target);
            References = NativeTweenSourceReferences.CreateControlledForTests(new ReferenceTransport(), 7, Verify, Root,
                (root, _) => root.Target, _ => { }); reader.BindControlledReferencesForTests(References);
            var roots = reader.RetainRootedObjects(this, new[] { new NativeTweenSourceRootReference(Root(new(0x1001)), NativeTweenSourceObjectKind.Tween) });
            Withdrawal = reader.BeginWithdrawal(roots); Context = NativeTweenRetirementContext.CreateControlledForTests(Native, this, Verify);
        }
        private NativeRuntime.RootHandle Root(IntPtr value)
            => new(1, _ => ++Frees, _ => value, Verify);
        private void Verify() { if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Wrong actual fixture thread."); }
        public void VerifyTweenAdmissionOwner() => Verify();
        public void VerifyTweenSourceOwner() => Verify();
        public void VerifyTweenRetirementCut(NativeTweenRetirementContext context)
        { Verify(); if (Revoked || !ReferenceEquals(context, Context)) throw new InvalidOperationException("Foreign or revoked cut owner."); }
        public NativeTweenAdmission.RetirementBatch BorrowTweenAdmissionRetirementBatch(NativeTweenRetirementContext context)
        { VerifyTweenRetirementCut(context); return Batch; }
        public void Dispose() { }
    }
    private sealed class AdmissionTransport : INativeTweenAdmissionTransport
    {
        private NativeTweenAdmissionConfiguration _configuration;
        public uint Configure(ref NativeTweenAdmissionConfiguration configuration) { _configuration = configuration; return 0; }
        public uint GetInfo(ref NativeTweenAdmissionInformation value)
        { value = new() { Version = 1, Size = 104, Configured = 1, ExactTweenClass = _configuration.ExactTweenClass,
            CachedPointerField = _configuration.CachedPointerField, CachedPointerOffset = 16, Capacity = 128, ConfigurationEpoch = 9 }; return 0; }
        public uint Claim(IntPtr value, ref NativeTweenAdmissionReceipt receipt) => throw new InvalidOperationException("No claim belongs to this empty fixture owner.");
        public uint Read(uint slot, ulong epoch, ref NativeTweenAdmissionReceipt receipt) => throw new InvalidOperationException("No claim read belongs to this empty fixture owner.");
        public uint Seal(uint slot, ulong epoch, ref NativeTweenAdmissionReceipt receipt) => throw new InvalidOperationException("No seal belongs to this empty fixture owner.");
        public uint Unregister(uint slot, ulong epoch, ref NativeTweenAdmissionReceipt receipt) => throw new InvalidOperationException("Only the actual cut batch may unregister.");
    }
    private sealed class SourceTransport : INativeTweenSourceTransport
    {
        public uint GetInfo(ref NativeTweenSourceInformation value)
        { value = new() { Version = 1, Size = 96, Configured = 1, MetadataBound = 1, BindingCount = 43, JournalCapacity = 8192, Epoch = 7 }; return 0; }
        public uint ReadRecord(ulong epoch, ulong sequence, ref NativeTweenSourceRecord value) => throw new InvalidOperationException("No source row was issued.");
    }
    private sealed class ReferenceTransport : INativeTweenSourceReferencesTransport
    {
        public uint Attach(ref NativeTweenSourceReferencesObserver observer) => 0;
        public uint GetInfo(ref NativeTweenSourceReferencesInformation value)
        { value = new() { Version = 1, Size = 104, Attached = 1, ScopeCapacity = 128, SourceEpoch = 7, ObserverEpoch = 11 }; return 0; }
        public uint ReadCurrentNotice(IntPtr notice, ulong observer, ulong scope, ulong delivery, IntPtr copy, IntPtr origin, IntPtr references, uint capacity)
            => throw new InvalidOperationException("No callback was borrowed.");
    }
    private sealed class CutTransport : INativeTweenRetirementContextTransport
    {
        internal bool Busy, ReleaseThrows; internal string? Bad;
        internal int Acquires, Commits, Releases;
        private NativeTweenRetirementCutReceipt _current;
        public uint Acquire(ref NativeTweenRetirementRequest request, NativeTweenRetirementClaim[] claims, ref NativeTweenRetirementCutReceipt cut)
        {
            ++Acquires; if (Busy) return 3;
            cut = new() { Version = 1, Size = 88, ThreadId = NativeTweenSourceReferences.CurrentThreadId(), Phase = 1,
                Token = 1, Generation = 1, ConfigurationEpoch = request.ConfigurationEpoch, SourceEpoch = request.SourceEpoch,
                JournalCutoff = request.JournalCutoff, ReferenceObserverEpoch = request.ReferenceObserverEpoch,
                ReferenceDeliveryCutoff = request.ReferenceDeliveryCutoff, ReferenceReaderGeneration = request.ReferenceReaderGeneration,
                ClaimCount = (uint)claims.Length };
            if (Bad == "token") cut.Token = 0;
            if (Bad == "thread") ++cut.ThreadId;
            if (Bad == "source") ++cut.SourceEpoch;
            if (Bad == "reference") ++cut.ReferenceObserverEpoch;
            if (Bad == "count") ++cut.ClaimCount;
            if (Bad == "request") ++request.ConfigurationEpoch;
            _current = cut; return 0;
        }
        public uint Verify(ref NativeTweenRetirementCutReceipt cut) => cut.Equals(_current) ? 0u : 2u;
        public uint VerifyReferenceRoots(ref NativeTweenRetirementCutReceipt cut) => Verify(ref cut);
        public uint UnregisterBatch(ref NativeTweenRetirementCutReceipt cut, NativeTweenRetirementClaim[] claims, NativeTweenAdmissionReceipt[] receipts)
        { if (Verify(ref cut) != 0 || claims.Length != 0 || receipts.Length != 0) return 2; ++Commits; return 0; }
        public uint Release(ref NativeTweenRetirementCutReceipt cut)
        { ++Releases; if (ReleaseThrows) throw new InvalidOperationException("Actual native release threw."); return Verify(ref cut); }
    }
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("Tween cut invariant failed."); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
