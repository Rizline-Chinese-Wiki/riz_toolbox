using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

/// <summary>Real controlled RootHandle frees and synchronous callback lifetimes.
/// The transport is a fixture, so these results confer no production native cut.</summary>
internal static class NativeTweenSourceLifetimeRegression
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Source withdrawal freezes one reader cutoff and frees only its independently owned handles once", InitialRoots),
        ("Source operation-root withdrawal invalidates its credential without freeing the backend handle", OperationBorrow),
        ("Two source readers retain one hub generation until the actual last borrowing holder withdraws", SharedReaders),
        ("Source AfterCommitted is pending until the actual native scope has ended", NativeScopePending),
        ("Late rootless source scopes cannot re-enroll a withdrawn run or lend old references to a new reader", LateScopes),
        ("Released synchronous source generations cannot be revived by reusing their original address", AddressReuse),
        ("Source-root Free reentry may borrow a fresh generation without reviving the removed holder", FreeReentry),
        ("Source own-root Free exceptions leave sticky unknown responsibility and never retry", OwnFreeFailure),
        ("Hub last-holder Free exceptions remain permanently unknown without a second free attempt", HubFreeFailure),
        ("Unknown source callbacks never earn a withdrawal or free receipt", UnknownCallback),
        ("Source release closes every old root-set read before invoking its first external Free callback", ReleaseReadMask),
        ("A later root set on the same reader cannot inherit a previously frozen native scope borrower", LateRootSet),
        ("Source release consumes the actual current committed Cut and rejects saved or uncommitted cuts", ActualCut)
    };

    private static void InitialRoots()
    {
        using var f = new Fixture(); var (reader, roots) = f.Reader(); var own = roots.Entries.Select(row => row.Root!).ToArray();
        f.Source.Add(f.Row(1)); var transaction = reader.BeginWithdrawal(roots); var rootsBefore = f.Native.RootAttempts;
        Check(transaction.JournalCutoff == 1 && !transaction.Committed && !roots.EnrollmentOpen && !roots.Retired);
        Reject<InvalidOperationException>(() => transaction.CommitReleaseForTests());
        Check(!transaction.Unknown && f.Native.FreeAttempts == 0);
        f.Source.Add(f.Row(2));
        var batch = reader.ReadAvailable(roots, 0, 8);
        Check(batch.LastSequence == 1 && batch.Records.Count == 1 && reader.ReadAvailable(roots, 1, 8).Records.Count == 0);
        Reject<InvalidOperationException>(() => reader.PromoteCurrentOperationTweenRoot(roots, f.Native.Root(new(0x8000)), f.Frame(new(0x8000))));
        Check(transaction.CommitReleaseForTests() && transaction.Committed && roots.Retired && roots.RootRecords == 0
            && f.Native.Freed == own.Length && own.All(IsDisposed));
        Reject<InvalidOperationException>(() => transaction.CommitReleaseForTests());
        var reads = f.Source.Reads;
        Reject<InvalidOperationException>(() => reader.ReadAvailable(roots, 1, 8));
        Check(f.Source.Reads == reads && !roots.HasRetainedObject(f.Native.Tween.ToInt64()) && f.Native.RootAttempts == rootsBefore + 1);
    }

    private static void OperationBorrow()
    {
        using var f = new Fixture(); var (reader, roots) = f.Reader(); var identity = new IntPtr(0x8000); var backendRoot = f.Native.Root(identity);
        reader.PromoteCurrentOperationTweenRoot(roots, backendRoot, f.Frame(identity));
        var entry = roots.Entries.Single(row => row.Identity == identity); var anchor = entry.OperationAnchor!;
        var transaction = reader.BeginWithdrawal(roots);
        Check(transaction.CommitReleaseForTests() && f.Native.Freed == 3 && backendRoot.Target == identity
            && entry.BorrowWithdrawn && !entry.FreeAttempted && entry.Root is null);
        Reject<TargetExpiredException>(() => { _ = anchor.Root; });
        Reject<UnsupportedSignatureException>(() => anchor.VerifyRetained(reader, roots, identity));
    }

    private static void SharedReaders()
    {
        using var f = new Fixture(); var (left, leftRoots) = f.Reader(); var (right, rightRoots) = f.Reader();
        f.CompleteScope(1, f.Native.NewArray);
        var a = leftRoots.Entries.Single(row => row.Identity == f.Native.NewArray);
        var b = rightRoots.Entries.Single(row => row.Identity == f.Native.NewArray); var shared = a.Root!; var rightBorrow = b.ReferenceBorrow!;
        Check(ReferenceEquals(a.Root, b.Root) && a.ReferenceBorrow!.Generation == b.ReferenceBorrow!.Generation && f.Native.Freed == 0);
        var first = left.BeginWithdrawal(leftRoots); Check(first.CommitReleaseForTests());
        Check(f.Native.Freed == 5 && shared.Target == f.Native.NewArray && f.Native.Owner.RootRecords == 1 && rightRoots.RootRecords == 4);
        Check(right.ReadAvailable(rightRoots, 0, 8).Records.Count == 0);
        var second = right.BeginWithdrawal(rightRoots); Check(second.CommitReleaseForTests());
        Check(f.Native.Freed == 9 && f.Native.Owner.RootRecords == 0 && f.Native.Owner.FreedRootRecords == 3 && IsDisposed(shared));
        Reject<TargetExpiredException>(() => rightBorrow.VerifyRetained(shared));
    }

    private static void NativeScopePending()
    {
        using var f = new Fixture(); var (reader, roots) = f.Reader(); f.CompleteScope(1, f.Native.NewArray);
        var transaction = reader.BeginWithdrawal(roots);
        f.Native.Native.Info.ScopesBegun = 1; f.Native.Native.Info.ActiveScopes = 1; f.Native.Native.Info.RetainedScopes = 1;
        Check(!transaction.CommitReleaseForTests() && !transaction.Unknown && !transaction.Committed && f.Native.Freed == 0
            && f.Native.Owner.PendingNativeScopeRecords == 1);
        f.Native.Native.Info.ActiveScopes = 0; f.Native.Native.Info.RetainedScopes = 0;
        f.Native.Native.Info.ScopesEnded = f.Native.Native.Info.ScopesCompleted = 1;
        Check(transaction.CommitReleaseForTests() && transaction.Committed && f.Native.Freed == 6);
    }

    private static void LateScopes()
    {
        using var f = new Fixture(); var (reader, roots) = f.Reader();
        Check(f.Native.Deliver(1, NativeTweenSourceReferencePhase.References,
            f.Native.Ref(f.Native.Manager, NativeTweenSourceReferenceRole.Manager), f.Native.Ref(f.Native.NewArray, NativeTweenSourceReferenceRole.Array)) == 0);
        var transaction = reader.BeginWithdrawal(roots); var expected = roots.RootRecords;
        Check(!transaction.CommitReleaseForTests() && f.Native.Freed == 0);
        Check(f.Native.Deliver(1, NativeTweenSourceReferencePhase.BeforeCommitted) == 0);
        Check(f.Native.Deliver(1, NativeTweenSourceReferencePhase.References,
            new[] { f.Native.Ref(f.Native.Array, NativeTweenSourceReferenceRole.Array) }, NativeTweenSourceStage.After) == 0);
        Check(f.Native.Deliver(1, NativeTweenSourceReferencePhase.AfterCommitted) == 0 && roots.RootRecords == expected);
        Check(transaction.CommitReleaseForTests()); var attempts = f.Native.RootAttempts; var targets = f.Native.TargetReads;
        // No reader exists when this native scope enters. Only TLS, source role
        // and scalar origin are checked; no game pointer is dereferenced.
        Check(f.Native.Deliver(2, NativeTweenSourceReferencePhase.References,
            f.Native.Ref(f.Native.Manager, NativeTweenSourceReferenceRole.Manager), f.Native.Ref(f.Native.NewArray, NativeTweenSourceReferenceRole.Array)) == 0);
        Check(f.Native.RootAttempts == attempts && f.Native.TargetReads == targets);
        var (next, nextRoots) = f.Reader(); var nextRecords = nextRoots.RootRecords; attempts = f.Native.RootAttempts;
        Check(f.Native.Deliver(2, NativeTweenSourceReferencePhase.BeforeCommitted) == 0);
        Check(f.Native.Deliver(2, NativeTweenSourceReferencePhase.References,
            new[] { f.Native.Ref(f.Native.NewArray, NativeTweenSourceReferenceRole.Array) }, NativeTweenSourceStage.After) == 0);
        Check(f.Native.Deliver(2, NativeTweenSourceReferencePhase.AfterCommitted) == 0
            && nextRoots.RootRecords == nextRecords && f.Native.RootAttempts == attempts);
        var closed = next.BeginWithdrawal(nextRoots); Check(closed.CommitReleaseForTests() && !f.Native.Owner.Unknown);
    }

    private static void AddressReuse()
    {
        using var f = new Fixture(); var (reader, roots) = f.Reader(); f.CompleteScope(1, f.Native.NewArray);
        var capture = f.Native.Receipts[0].References.Single(row => row.Identity == f.Native.NewArray);
        var firstRoot = capture.Root!; var generation = capture.RootGeneration;
        Check(reader.BeginWithdrawal(roots).CommitReleaseForTests());
        Reject<TargetExpiredException>(() => { _ = capture.Root; });
        var (next, nextRoots) = f.Reader(); f.CompleteScope(2, f.Native.NewArray);
        var current = f.Native.Receipts.First(row => row.ScopeId == 2).References.Single(row => row.Identity == f.Native.NewArray);
        Check(current.RootGeneration > generation && !ReferenceEquals(current.Root, firstRoot) && current.Root!.Target == f.Native.NewArray);
        Reject<TargetExpiredException>(() => { _ = capture.Root; });
        Check(next.BeginWithdrawal(nextRoots).CommitReleaseForTests() && !f.Native.Owner.Unknown);
    }

    private static void FreeReentry()
    {
        using var f = new Fixture(); var (reader, roots) = f.Reader(); f.CompleteScope(1, f.Native.NewArray);
        var oldCapture = f.Native.Receipts[0].References.Single(row => row.Identity == f.Native.NewArray); var oldGeneration = oldCapture.RootGeneration;
        var closing = reader.BeginWithdrawal(roots); var next = f.Reader(); var entered = false;
        f.Native.OnFree = identity => {
            if (identity != f.Native.NewArray || entered) return; entered = true;
            f.CompleteScope(2, f.Native.NewArray);
        };
        Check(closing.CommitReleaseForTests() && entered && !f.Native.Owner.Unknown);
        var fresh = f.Native.Receipts.First(row => row.ScopeId == 2).References.Single(row => row.Identity == f.Native.NewArray);
        Check(fresh.RootGeneration > oldGeneration && fresh.Root!.Target == f.Native.NewArray && next.Roots.HasRetainedObject(f.Native.NewArray.ToInt64()));
        Reject<TargetExpiredException>(() => { _ = oldCapture.Root; });
        f.Native.OnFree = null; Check(next.Reader.BeginWithdrawal(next.Roots).CommitReleaseForTests());
    }

    private static void OwnFreeFailure()
    {
        using var f = new Fixture(); var (reader, roots) = f.Reader(); var transaction = reader.BeginWithdrawal(roots);
        f.Native.OnFree = _ => throw new IOException("Controlled source-owned Free outcome is unknown.");
        Reject<IOException>(() => transaction.CommitReleaseForTests());
        Check(transaction.Unknown && reader.Unknown && !transaction.Committed && f.Native.FreeAttempts == 1 && f.Native.Freed == 0);
        f.Native.OnFree = null; Reject<InvalidOperationException>(() => transaction.CommitReleaseForTests());
        Check(f.Native.FreeAttempts == 1 && roots.Entries.Count(row => row.FreeAttempted) == 1);
    }

    private static void HubFreeFailure()
    {
        using var f = new Fixture(); var (reader, roots) = f.Reader(); f.CompleteScope(1, f.Native.NewArray);
        var capture = f.Native.Receipts[0].References.Single(row => row.Identity == f.Native.NewArray);
        var transaction = reader.BeginWithdrawal(roots);
        f.Native.OnFree = value => { if (value == f.Native.NewArray) throw new IOException("Controlled hub Free outcome is unknown."); };
        Reject<IOException>(() => transaction.CommitReleaseForTests());
        var attempts = f.Native.FreeAttempts;
        Check(f.Native.Owner.Unknown && transaction.Unknown && reader.Unknown && !transaction.Committed && f.Native.Owner.FreeAttemptRecords == 3);
        Reject<TargetExpiredException>(() => { _ = capture.Root; });
        f.Native.OnFree = null; Reject<InvalidOperationException>(() => transaction.CommitReleaseForTests());
        Check(f.Native.FreeAttempts == attempts);
    }

    private static void UnknownCallback()
    {
        using var f = new Fixture(); var (reader, roots) = f.Reader();
        f.Native.Consume = receipt => { foreach (var row in f.Readers) row.ObserveSynchronousReferences(receipt); throw new IOException("Unknown source consumer."); };
        Check(f.Native.Deliver(1, NativeTweenSourceReferencePhase.References,
            f.Native.Ref(f.Native.Manager, NativeTweenSourceReferenceRole.Manager), f.Native.Ref(f.Native.NewArray, NativeTweenSourceReferenceRole.Array)) == 5);
        Reject<InvalidOperationException>(() => reader.BeginWithdrawal(roots));
        Check(reader.Unknown && f.Native.Owner.Unknown && f.Native.FreeAttempts == 0 && roots.RootRecords == 4);
    }

    private static void ReleaseReadMask()
    {
        using var f = new Fixture(); var (reader, roots) = f.Reader(); var transaction = reader.BeginWithdrawal(roots); var calls = 0;
        f.Native.OnFree = _ => {
            ++calls; var reads = f.Source.Reads; var targetReads = f.Native.TargetReads;
            Check(roots.Releasing && !roots.HasRetainedObject(f.Native.Tween.ToInt64()) && !roots.HasTweenOwnershipAnchor(f.Native.Tween.ToInt64()));
            Reject<InvalidOperationException>(() => reader.ReadAvailable(roots, 0, 8));
            Check(f.Source.Reads == reads && f.Native.TargetReads == targetReads);
        };
        Check(transaction.CommitReleaseForTests() && calls == 3 && roots.Retired && !roots.Releasing && !reader.Unknown);
    }

    private static void LateRootSet()
    {
        using var f = new Fixture(); var (reader, firstRoots) = f.Reader();
        Check(f.Native.Deliver(1, NativeTweenSourceReferencePhase.References,
            f.Native.Ref(f.Native.Manager, NativeTweenSourceReferenceRole.Manager), f.Native.Ref(f.Native.NewArray, NativeTweenSourceReferenceRole.Array)) == 0);
        var laterRoots = f.Retain(reader);
        Check(f.Native.Deliver(1, NativeTweenSourceReferencePhase.BeforeCommitted) == 0);
        Check(f.Native.Deliver(1, NativeTweenSourceReferencePhase.References,
            new[] { f.Native.Ref(f.Native.NewArray, NativeTweenSourceReferenceRole.Array) }, NativeTweenSourceStage.After) == 0);
        Check(f.Native.Deliver(1, NativeTweenSourceReferencePhase.AfterCommitted) == 0);
        Check(firstRoots.RootRecords == 4 && laterRoots.RootRecords == 3 && !laterRoots.HasRetainedObject(f.Native.NewArray.ToInt64()));
        Check(reader.BeginWithdrawal(firstRoots).CommitReleaseForTests());
        Check(reader.BeginWithdrawal(laterRoots).CommitReleaseForTests() && !reader.Unknown && f.Native.Owner.RootRecords == 0);
    }

    private static void ActualCut()
    {
        using var f = new Fixture(); var (reader, roots) = f.Reader(); f.CompleteScope(1, f.Native.NewArray);
        var admissionNative = new AdmissionTransport(f.Native.Tween); var admission = NativeTweenAdmission.CreateControlledForTests(admissionNative,
            new NativeTweenAdmissionConfiguration { Version = 1, Size = 32, ExactTweenClass = new(0x2222), CachedPointerField = new(0x3333), CachedPointerOffset = 16 },
            f.Native.VerifyAccess, f.Native.Root, root => root.Target);
        var lease = admission.Claim(f.Native.Root(f.Native.Tween), f.CutOwner); Check(lease.Seal().IsSealed);
        var batch = admission.PrepareRetirement(f.CutOwner); f.CutOwner.Batch = batch;
        var transport = new CutTransport(); var context = NativeTweenRetirementContext.CreateControlledForTests(transport, f.CutOwner, f.Native.VerifyAccess);
        f.CutOwner.Context = context; var transaction = reader.BeginWithdrawal(roots); NativeTweenRetirementContext.Cut? saved = null;
        var frees = 0;
        Check(context.WithCut(transaction, cut => {
            saved = cut; Reject<InvalidOperationException>(() => transaction.CommitRelease(cut));
            Check(!transaction.Unknown && f.Native.FreeAttempts == 0);
            batch.CommitNative(cut);
            f.Native.OnFree = _ => { ++frees; cut.VerifySourceWithdrawal(transaction); cut.VerifyReferenceRoots(f.Native.Owner); };
            transaction.CommitRelease(cut); cut.VerifySourceWithdrawal(transaction);
            Check(transaction.Committed && roots.Retired && f.Native.Owner.RootRecords == 0);
        }));
        Check(frees == 6 && transport.Releases == 1 && transport.Commits == 1 && !context.Unknown && !reader.Unknown);
        Reject<InvalidOperationException>(() => saved!.VerifySourceWithdrawal(transaction));
        Reject<InvalidOperationException>(() => transaction.CommitRelease(saved!));
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly NativeTweenSourceReferencesRegression.Fixture Native = new();
        internal readonly SourceTransport Source = new();
        internal readonly List<NativeTweenSource> Readers = new();
        private readonly Owner _owner;
        internal Owner CutOwner => _owner;
        internal Fixture()
        {
            Native.ConsumerRegistration.Withdraw(); _owner = new(Native.VerifyAccess);
            Native.Consume = receipt => { foreach (var reader in Readers.ToArray()) reader.ObserveSynchronousReferences(receipt); };
        }
        internal (NativeTweenSource Reader, NativeTweenSourceRootSet Roots) Reader()
        {
            var reader = NativeTweenSource.CreateControlledForTests(Source, Native.VerifyAccess, Native.Root, (root, _) => root.Target);
            reader.BindControlledReferencesForTests(Native.Owner); Readers.Add(reader);
            return (reader, Retain(reader));
        }
        internal NativeTweenSourceRootSet Retain(NativeTweenSource reader)
            => reader.RetainRootedObjects(_owner, new[] {
                new NativeTweenSourceRootReference(Native.Root(Native.Manager), NativeTweenSourceObjectKind.Manager),
                new NativeTweenSourceRootReference(Native.Root(Native.Tween), NativeTweenSourceObjectKind.Tween),
                new NativeTweenSourceRootReference(Native.Root(Native.Array), NativeTweenSourceObjectKind.TweenArray) });
        internal void CompleteScope(ulong scope, IntPtr array)
        {
            Check(Native.Deliver(scope, NativeTweenSourceReferencePhase.References,
                Native.Ref(Native.Manager, NativeTweenSourceReferenceRole.Manager), Native.Ref(array, NativeTweenSourceReferenceRole.Array)) == 0);
            Check(Native.Deliver(scope, NativeTweenSourceReferencePhase.BeforeCommitted) == 0);
            Check(Native.Deliver(scope, NativeTweenSourceReferencePhase.References,
                new[] { Native.Ref(Native.Tween, NativeTweenSourceReferenceRole.Tween) }, NativeTweenSourceStage.After) == 0);
            Check(Native.Deliver(scope, NativeTweenSourceReferencePhase.AfterCommitted) == 0);
        }
        internal NativeOperationFrame Frame(IntPtr tween) => new() { Version = 1, Size = 64, Kind = NativeOperationKind.Void0,
            Self = tween, Caller = new(0x6000), Sequence = 103 };
        internal NativeTweenSourceRecord Row(ulong sequence) => new() { Version = 1, Size = 192,
            Site = NativeTweenSourceSite.ReadyPopFront, Stage = NativeTweenSourceStage.Before,
            Flags = NativeTweenSourceFlags.Known | NativeTweenSourceFlags.ParentMatched,
            ThreadId = NativeTweenSourceReferences.CurrentThreadId(), Depth = 1, Epoch = 7, UpdateSequence = 101, Transaction = 1001,
            EventSequence = sequence, SourcePc = new(0x5000), RelayReturnPc = new(0x6000), Manager = Native.Manager,
            Tween = Native.Tween, ArrayBefore = Native.Array, ArrayAfter = Native.Array };
        public void Dispose() => Native.Dispose();
    }
    internal sealed class Owner : INativeTweenRetirementCutOwner
    {
        private readonly Action _verify;
        internal Owner(Action verify) => _verify = verify;
        public void VerifyTweenSourceOwner() => _verify();
        public void VerifyTweenAdmissionOwner() => _verify();
        internal NativeTweenAdmission.RetirementBatch? Batch;
        internal NativeTweenRetirementContext? Context;
        public void VerifyTweenRetirementCut(NativeTweenRetirementContext context)
        { _verify(); if (!ReferenceEquals(context, Context)) throw new InvalidOperationException("Foreign controlled cut context."); }
        public NativeTweenAdmission.RetirementBatch BorrowTweenAdmissionRetirementBatch(NativeTweenRetirementContext context)
        { VerifyTweenRetirementCut(context); return Batch ?? throw new InvalidOperationException("No controlled actual batch."); }
    }
    private sealed class AdmissionTransport : INativeTweenAdmissionTransport
    {
        private readonly NativeTweenAdmissionInformation _info = new() { Version = 1, Size = 104, Configured = 1,
            ExactTweenClass = new(0x2222), CachedPointerField = new(0x3333), CachedPointerOffset = 16, Capacity = 128, ConfigurationEpoch = 7 };
        private NativeTweenAdmissionReceipt _current;
        internal AdmissionTransport(IntPtr tween) => _current = new() { Version = 1, Size = 96, Slot = 3, Epoch = 41,
            Phase = NativeTweenAdmissionPhase.Open, Object = tween, ActualClass = new(0x2222), AdmittedIssuers = 1 };
        public uint Configure(ref NativeTweenAdmissionConfiguration value) => 0;
        public uint GetInfo(ref NativeTweenAdmissionInformation value) { value = _info; return 0; }
        public uint Claim(IntPtr tween, ref NativeTweenAdmissionReceipt receipt) { receipt = _current; return 0; }
        public uint Read(uint slot, ulong epoch, ref NativeTweenAdmissionReceipt receipt) { receipt = _current; return 0; }
        public uint Seal(uint slot, ulong epoch, ref NativeTweenAdmissionReceipt receipt)
        { _current.Phase = NativeTweenAdmissionPhase.Sealed; receipt = _current; return 0; }
        public uint Unregister(uint slot, ulong epoch, ref NativeTweenAdmissionReceipt receipt) => throw new InvalidOperationException("Use actual batch cut.");
    }
    private sealed class CutTransport : INativeTweenRetirementContextTransport
    {
        internal int Releases, Commits;
        private bool _active;
        public uint Acquire(ref NativeTweenRetirementRequest request, NativeTweenRetirementClaim[] claims, ref NativeTweenRetirementCutReceipt cut)
        {
            if (_active) return 3; _active = true;
            cut = new() { Version = 1, Size = 88, ThreadId = NativeTweenSourceReferences.CurrentThreadId(), Phase = 1, Token = 17, Generation = 19,
                ConfigurationEpoch = request.ConfigurationEpoch, SourceEpoch = request.SourceEpoch, JournalCutoff = request.JournalCutoff,
                ReferenceObserverEpoch = request.ReferenceObserverEpoch, ReferenceDeliveryCutoff = request.ReferenceDeliveryCutoff,
                ReferenceReaderGeneration = request.ReferenceReaderGeneration, ClaimCount = checked((uint)claims.Length) }; return 0;
        }
        public uint Verify(ref NativeTweenRetirementCutReceipt cut) => _active ? 0u : 3u;
        public uint UnregisterBatch(ref NativeTweenRetirementCutReceipt cut, NativeTweenRetirementClaim[] claims, NativeTweenAdmissionReceipt[] receipts)
        {
            if (!_active) return 3; ++Commits;
            for (var index = 0; index < claims.Length; ++index) receipts[index] = new() { Version = 1, Size = 96,
                Phase = NativeTweenAdmissionPhase.Retired, Slot = claims[index].Slot, Epoch = claims[index].Epoch,
                Object = claims[index].Object, ActualClass = claims[index].ActualClass, AdmittedIssuers = 1 };
            return 0;
        }
        public uint Release(ref NativeTweenRetirementCutReceipt cut) { ++Releases; _active = false; return 0; }
        public uint VerifyReferenceRoots(ref NativeTweenRetirementCutReceipt cut) => _active ? 0u : 3u;
    }
    private sealed class SourceTransport : INativeTweenSourceTransport
    {
        internal NativeTweenSourceInformation Info = new() { Version = 1, Size = 96, Configured = 1, MetadataBound = 1,
            BindingCount = 43, JournalCapacity = 8192, Epoch = 7 };
        private readonly Dictionary<ulong, NativeTweenSourceRecord> _records = new();
        internal int Reads;
        internal void Add(NativeTweenSourceRecord row) { _records.Add(row.EventSequence, row); Info.Events = row.EventSequence; Info.RetainedRecords = row.EventSequence; }
        public uint GetInfo(ref NativeTweenSourceInformation information) { ++Reads; information = Info; return 0; }
        public uint ReadRecord(ulong epoch, ulong sequence, ref NativeTweenSourceRecord record)
        { ++Reads; return epoch == 7 && _records.TryGetValue(sequence, out record) ? 0u : 3u; }
    }
    private static bool IsDisposed(NativeRuntime.RootHandle root)
    { try { _ = root.Target; return false; } catch (TargetExpiredException) { return true; } }
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("Source reader/root lifetime invariant failed."); }
    private static void Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
