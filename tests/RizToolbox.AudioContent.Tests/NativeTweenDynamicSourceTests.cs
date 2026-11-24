using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using static Assert;

/// <summary>Controlled first-call adoption through the production owner state
/// machine. Roots and admission leases are controlled; no game or helper runs.</summary>
internal static class NativeTweenDynamicSourceTests
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Tween first dynamic Play Before shares its exact root and installs live death ownership before the original", Promote),
        ("Tween dynamic first-call adoption is idempotent across repeated stable graph ticks", Idempotent),
        ("Tween an unqueued diagnostic object is never upgraded to an owned dynamic source", Unqueued),
        ("Tween queue membership alone never upgrades a foreign shared-manager object", QueueWithoutCall),
        ("Tween shared-manager Add Execute and Play beneath Update retain their foreign ownership", ForeignCalls),
        ("Tween failed first-call dynamic admission retains unknown responsibility and stops later graph reads", FailedPromotion),
        ("Tween first dynamic Add Before owns its exact subsequent manager append", AddCandidate),
        ("Tween first dynamic Execute Before owns an unqueued input from an owned Tween parent", FirstExecute),
        ("Tween busy first-call admission stays pending on the same retained root until idle retry", PendingAdmission),
        ("Tween first dynamic Play source rows are covered by an owned journal anchor before original execution", FirstJournalRows),
        ("Tween first Add to a new exact manager shares the current receiver root without adopting foreign graph items", FirstManager),
        ("Tween an earlier unowned Play cannot be repaired by a later first-call source adoption", MissingEarlierPlay),
        ("Tween promoted source matches later Play Add and Execute operations while the old source set remains unchanged", FuturePlay)
    };

    private static void Promote()
    {
        using var f = new Fixture(); Equal(1, f.Owner.Read().Sources);
        var parent = f.Begin(NativeTweenOperation.Play, 1001);
        var child = f.Begin(NativeTweenOperation.Play, 2002);
        var state = f.Owner.Read();
        Check(state is { Sources: 2, Unknown: false } && !child.Completed && f.Backend.RefreshReads == 1);
        var death = f.Owner.ReadTweenDeaths().Single();
        Check(death.Tween == 2002 && death.Phase == NativeLocalTweenDeathPhase.LiveObserved && !death.Unknown);
        Equal(1, f.Backend.DynamicClaims);
        Check(ReferenceEquals(f.Backend.DynamicRoot, f.Backend.DynamicLeaseRoot));
        f.End(child); f.End(parent);
    }

    private static void Idempotent()
    {
        using var f = new Fixture(); f.QueueDynamicCandidate(2002, queued: true);
        var reads = f.Backend.RefreshReads;
        f.Owner.Tick(); f.Owner.Tick();
        Check(f.Backend.RefreshReads == reads + 2 && f.Backend.DynamicClaims == 1
            && f.Owner.Read().Sources == 2 && f.Owner.ReadTweenDeaths().Single().Tween == 2002);
    }

    private static void Unqueued()
    {
        using var f = new Fixture(); f.Backend.Graph = f.Backend.GraphWithDynamic(3003, queued: false);
        f.Owner.Tick();
        Check(f.Owner.Read() is { Sources: 1, Unknown: false } && f.Backend.DynamicClaims == 0
            && f.Owner.CaptureOperation(NativeTweenOperation.Play, f.Backend.Frame(NativeTweenOperation.Play, 3003)) is null);
    }

    private static void ForeignCalls()
    {
        using var f = new Fixture(); f.Backend.Graph = f.Backend.GraphWithDynamic(2002, queued: true);
        var update = f.Begin(NativeTweenOperation.Update, 0);
        foreach (var operation in new[] { NativeTweenOperation.Play, NativeTweenOperation.AddTweenToWaitingList, NativeTweenOperation.ExecuteTween })
        {
            var mask = f.Owner.CaptureOperation(operation, f.Backend.Frame(operation, 2002));
            Check(mask is { ForeignMask: true, Before: null, Captured: false });
            f.End(mask!);
        }
        f.End(update); f.Owner.Tick();
        Check(f.Owner.Read() is { Sources: 1, Unknown: false } && f.Backend.Native.ClaimCalls == 0);

        var owned = f.Begin(NativeTweenOperation.Play, 1001); update = f.Begin(NativeTweenOperation.Update, 0);
        var foreign = f.Owner.CaptureOperation(NativeTweenOperation.ExecuteTween, f.Backend.Frame(NativeTweenOperation.ExecuteTween, 2002));
        Check(foreign is { ForeignMask: true });
        var nested = f.Owner.CaptureOperation(NativeTweenOperation.Play, f.Backend.Frame(NativeTweenOperation.Play, 2002));
        Check(nested is { ForeignMask: true } && f.Backend.Native.ClaimCalls == 0);
        f.End(nested!); f.End(foreign!); f.End(update); f.End(owned);
        Check(f.Owner.Read() is { Sources: 1, Unknown: false } && f.Owner.Read().Operations.All(row => row.Frame.Self.ToInt64() != 2002));
    }

    private static void QueueWithoutCall()
    {
        using var f = new Fixture(); f.Backend.Graph = f.Backend.GraphWithDynamic(2002, queued: true);
        f.Owner.Tick(); f.Owner.Tick();
        Check(f.Owner.Read() is { Sources: 1, Unknown: false } && f.Backend.DynamicClaims == 0 && f.Backend.Native.ClaimCalls == 0);
    }

    private static void FailedPromotion()
    {
        using var f = new Fixture(); f.Backend.Native.ClaimStatus = 9;
        var parent = f.Begin(NativeTweenOperation.Play, 1001);
        var child = f.Begin(NativeTweenOperation.Play, 2002);
        var reads = f.Backend.RefreshReads;
        Check(f.Owner.Read().Unknown && f.Owner.Read().Sources == 1 && f.Backend.DynamicClaims == 0
            && f.Backend.Native.ClaimCalls == 1 && f.Backend.Freed == 0);
        f.Owner.Tick(); Check(f.Backend.RefreshReads == reads && f.Backend.Native.ClaimCalls == 1);
        f.End(child); f.End(parent);
    }

    private static void AddCandidate()
    {
        using var f = new Fixture();
        var parent = f.Begin(NativeTweenOperation.Play, 1001);
        var child = f.Begin(NativeTweenOperation.AddTweenToWaitingList, 2002);
        Check(f.Owner.Read() is { Sources: 2, Unknown: false } && f.Owner.Read().Admissions.Count == 0);
        f.Backend.Graph = f.Backend.GraphWithDynamic(2002, queued: true);
        f.End(child); f.End(parent);
        Check(f.Owner.Read() is { Sources: 2, Unknown: false } && f.Backend.DynamicClaims == 1
            && f.Owner.ReadTweenDeaths().Single().Tween == 2002
            && f.Owner.Read().Admissions.Single() is { Tween: 2002, ConfirmedAppend: true, Ambiguous: false });
    }

    private static void FirstExecute()
    {
        using var f = new Fixture(); var parent = f.Begin(NativeTweenOperation.Play, 1001);
        var execute = f.Begin(NativeTweenOperation.ExecuteTween, 2002);
        Check(f.Owner.Read() is { Sources: 2, Unknown: false } && !execute.Completed
            && f.Owner.ReadTweenDeaths().Single().Phase == NativeLocalTweenDeathPhase.LiveObserved);
        f.End(execute); f.End(parent);
    }

    private static void PendingAdmission()
    {
        using var f = new Fixture(); f.Backend.Native.ClaimStatus = 6;
        var parent = f.Begin(NativeTweenOperation.Play, 1001); var child = f.Begin(NativeTweenOperation.Play, 2002);
        Check(f.Owner.Read() is { Sources: 2, Unknown: false } && f.Backend.DynamicLease is { ClaimPending: true, Claimed: false }
            && ReferenceEquals(f.Backend.DynamicRoot, f.Backend.DynamicLeaseRoot) && f.Backend.ClaimAttempts == 0);
        f.End(child); f.End(parent); f.Backend.Native.ClaimStatus = 0; f.Owner.Tick();
        Check(!f.Owner.Read().Unknown && f.Backend.DynamicLease is { Claimed: true, ClaimPending: false }
            && f.Backend.DynamicClaims == 1 && f.Backend.ClaimAttempts == 0 && f.Backend.Freed == 0);
    }

    private static void FirstJournalRows()
    {
        using var f = new Fixture(journal: true); var parent = f.Begin(NativeTweenOperation.Play, 1001);
        var child = f.Begin(NativeTweenOperation.Play, 2002);
        f.PublishPlayEvent(child, parent);
        Check(f.Owner.Read() is { Sources: 2, Unknown: false } && f.Owner.ReadSourceReplay().InlineEvents!.Single() is {
            Tween: 2002, Site: NativeTweenSourceSite.PlayOnStart, NativeCallReturned: true });
        f.End(child); f.End(parent);
        Check(!f.Owner.Read().Unknown && f.Owner.ReadSourceReplay().Operations.All(row => row.Committed && !row.Failed));
    }

    private static void FuturePlay()
    {
        using var f = new Fixture(); f.QueueDynamicCandidate(2002, queued: true); f.Owner.Tick();
        var priorCount = f.Owner.Read().Operations.Count;
        var context = f.Owner.CaptureOperation(NativeTweenOperation.Play, f.Backend.Frame(NativeTweenOperation.Play, 2002));
        Check(context is not null && f.Owner.Read().Operations.Last().Frame.Self.ToInt64() == 2002);
        f.Owner.CompleteOperation(context!, context!.Frame, NativeOperationExitFlags.Returned); context.Dispose();
        foreach (var operation in new[] { NativeTweenOperation.AddTweenToWaitingList, NativeTweenOperation.ExecuteTween })
        {
            context = f.Owner.CaptureOperation(operation, f.Backend.Frame(operation, 2002));
            Check(context is not null);
            f.Owner.CompleteOperation(context!, context!.Frame, NativeOperationExitFlags.Returned); context.Dispose();
        }
        var operations = f.Owner.Read().Operations;
        Check(!f.Owner.Read().Unknown && operations.Count == priorCount + 3
            && operations.Skip(priorCount).All(row => row.Completed && row.NativeReturned && row.Disposed));
    }

    private static void FirstManager()
    {
        using var f = new Fixture(journal: true);
        var parent = f.Begin(NativeTweenOperation.Play, 1001);
        var addFrame = f.Backend.Frame(NativeTweenOperation.AddTweenToWaitingList, 2002); addFrame.Self = new(501);
        var add = f.Owner.CaptureOperation(NativeTweenOperation.AddTweenToWaitingList, addFrame)!;
        Check(add is { ForeignMask: false, Captured: true, Completed: false } && !f.Owner.Read().Unknown
            && f.Owner.Read() is { Sources: 2, Managers: 2 } && f.Backend.ManagerBorrows == 1);
        Check(f.ReaderRoots!.HasTweenOwnershipAnchor(2002)
            && f.ReaderRoots.TryGetAnchorKind(new(501), out var kind) && kind == NativeTweenSourceObjectKind.Manager
            && !f.ReaderRoots.HasTweenOwnershipAnchor(3003));
        f.Backend.AppendNewManager(2002); f.End(add); f.End(parent);
        var admission = f.Owner.Read().Admissions.Single();
        Check(!f.Owner.Read().Unknown && admission is { Manager: 501, Tween: 2002, ConfirmedAppend: true }
            && f.Backend.ClaimAttempts == 0 && f.Backend.Freed == 0);
    }

    private static void MissingEarlierPlay()
    {
        using var f = new Fixture(journal: true, diagnosticDynamicRoot: true);
        Check(f.ReaderRoots!.HasRetainedObject(2002) && !f.ReaderRoots.HasTweenOwnershipAnchor(2002));
        f.PublishUnownedPlayEvent();
        Check(!f.Owner.Read().Unknown && f.Owner.Read().Sources == 1 && f.Owner.ReadSourceReplay().Operations.Count == 0);
        var parent = f.Begin(NativeTweenOperation.Play, 1001);
        var child = f.Begin(NativeTweenOperation.Play, 2002);
        Check(f.Owner.Unknown && f.Owner.Read().Sources == 2 && f.Owner.ReadTweenDeaths().Count == 0
            && !f.ReaderRoots.HasTweenOwnershipAnchor(2002) && f.Backend.DynamicLease is { Claimed: true }
            && f.Backend.Freed == 0);
        f.End(child); f.End(parent);
        var reads = f.Backend.RefreshReads; f.Owner.Tick();
        Check(f.Backend.RefreshReads == reads && f.Backend.ClaimAttempts == 0 && f.Backend.Freed == 0);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new(); internal readonly Backend Backend;
        internal readonly NativeLocalTweenWorkOwner Owner;
        private readonly SourceTransport? _journal;
        internal NativeTweenSourceRootSet? ReaderRoots;
        internal Fixture(bool journal = false, bool diagnosticDynamicRoot = false)
        {
            Backend = new(Main); Owner = NativeLocalTweenWorkOwner.CaptureBackend(Main, Backend);
            if (!journal) return;
            _journal = new();
            var reader = NativeTweenSource.CreateControlledForTests(_journal, Main.VerifyAccess,
                value => new NativeRuntime.RootHandle((uint)value.ToInt64(), _ => ++Backend.Freed, _ => value, Main.VerifyAccess), (root, _) => root.Target);
            var references = new[] {
                (1001L, NativeTweenSourceObjectKind.Tween), (201L, NativeTweenSourceObjectKind.Manager),
                (102L, NativeTweenSourceObjectKind.UnityEvent), (103L, NativeTweenSourceObjectKind.UnityEvent),
                (104L, NativeTweenSourceObjectKind.SingleUnityEvent), (303L, NativeTweenSourceObjectKind.TweenArray),
                (301L, NativeTweenSourceObjectKind.ScheduledList), (401L, NativeTweenSourceObjectKind.TweenArray),
                (302L, NativeTweenSourceObjectKind.ReadyStack), (402L, NativeTweenSourceObjectKind.TweenArray)
            }.Select(row => new NativeTweenSourceRootReference(new NativeRuntime.RootHandle((uint)row.Item1,
                _ => ++Backend.Freed, _ => new(row.Item1), Main.VerifyAccess), row.Item2)).ToArray();
            if (diagnosticDynamicRoot) references = references.Append(new NativeTweenSourceRootReference(
                Backend.RetainedRoot(2002), NativeTweenSourceObjectKind.Tween, OwnershipAnchor: false)).ToArray();
            ReaderRoots = reader.RetainRootedObjects(Owner, references);
            Owner.AttachSourceJournal(reader, ReaderRoots);
        }
        internal NativeLocalTweenWorkOwner.OperationContext Begin(NativeTweenOperation operation, long tween)
            => Owner.CaptureOperation(operation, Backend.Frame(operation, tween))
                ?? throw new InvalidOperationException("The exact controlled dynamic operation was not captured.");
        internal void End(NativeLocalTweenWorkOwner.OperationContext context)
        { Owner.CompleteOperation(context, context.Frame, NativeOperationExitFlags.Returned); context.Dispose(); }
        internal void QueueDynamicCandidate(long tween, bool queued, bool setGraph = true)
        {
            if (setGraph) Backend.Graph = Backend.GraphWithDynamic(tween, queued);
            var parent = Begin(NativeTweenOperation.Play, 1001);
            var play = Begin(NativeTweenOperation.Play, tween); End(play); End(parent);
        }
        internal void PublishPlayEvent(NativeLocalTweenWorkOwner.OperationContext child, NativeLocalTweenWorkOwner.OperationContext parent)
        {
            var before = new NativeTweenSourceRecord { Version = 1, Size = 192, Site = NativeTweenSourceSite.PlayOnStart,
                Stage = NativeTweenSourceStage.Before, Flags = NativeTweenSourceFlags.Known | NativeTweenSourceFlags.ParentMatched,
                ThreadId = NativeEngineInlinePlatform.CurrentOsThreadId, Depth = 2, Operation = NativeTweenSourceOperation.Play,
                Epoch = 9, UpdateSequence = child.Frame.Sequence, ParentUpdateSequence = parent.Frame.Sequence, Transaction = 1,
                SourcePc = new(0x10000), RelayReturnPc = new(0x20000), Container = new(102), Tween = new(2002),
                SourceIndex = -1, DestinationIndex = -1, CountBefore = -1, CountAfter = -1,
                CapacityBefore = -1, CapacityAfter = -1, VersionBefore = -1, VersionAfter = -1 };
            _journal!.Add(before); Owner.PollSourceJournal();
            var after = before; after.Stage = NativeTweenSourceStage.After; after.Flags |= NativeTweenSourceFlags.Paired;
            _journal.Add(after); Owner.PollSourceJournal();
        }
        internal void PublishUnownedPlayEvent()
        {
            var before = new NativeTweenSourceRecord { Version = 1, Size = 192, Site = NativeTweenSourceSite.PlayOnStart,
                Stage = NativeTweenSourceStage.Before, Flags = NativeTweenSourceFlags.Known | NativeTweenSourceFlags.ParentMatched,
                ThreadId = NativeEngineInlinePlatform.CurrentOsThreadId, Depth = 1, Operation = NativeTweenSourceOperation.Play,
                Epoch = 9, UpdateSequence = 700, Transaction = 1, SourcePc = new(0x10000), RelayReturnPc = new(0x20000),
                Container = new(102), Tween = new(2002), SourceIndex = -1, DestinationIndex = -1,
                CountBefore = -1, CountAfter = -1, CapacityBefore = -1, CapacityAfter = -1, VersionBefore = -1, VersionAfter = -1 };
            _journal!.Add(before); var after = before; after.Stage = NativeTweenSourceStage.After; after.Flags |= NativeTweenSourceFlags.Paired;
            _journal.Add(after); Owner.PollSourceJournal();
        }
        public void Dispose() => Main.Dispose();
    }

    private sealed class Backend : INativeLocalTweenBackend, INativeLocalTweenDynamicSourceBackend,
        INativeLocalTweenDeathBackend, INativeTweenAdmissionOwner, INativeLocalTweenDynamicManagerSourceBackend
    {
        private readonly MainThreadDispatcher _main;
        private readonly Dictionary<long, NativeRuntime.RootHandle> _roots = new();
        private NativeLocalTweenWorkOwner _owner = null!;
        private readonly NativeTweenAdmission _admission;
        private ulong _sequence;
        internal readonly Transport Native = new();
        internal NativeTweenWorkGraph Graph;
        internal NativeRuntime.RootHandle? DynamicRoot, DynamicLeaseRoot;
        internal NativeTweenAdmissionLease? DynamicLease;
        internal int DynamicClaims, ClaimAttempts, Freed, RefreshReads, ManagerBorrows;
        internal Backend(MainThreadDispatcher main)
        {
            _main = main;
            _roots.Add(1001, new NativeRuntime.RootHandle(1, _ => ++Freed, _ => new IntPtr(1001), main.VerifyAccess));
            _roots.Add(2002, new NativeRuntime.RootHandle(2, _ => ++Freed, _ => new IntPtr(2002), main.VerifyAccess));
            _roots.Add(201, new NativeRuntime.RootHandle(3, _ => ++Freed, _ => new IntPtr(201), main.VerifyAccess));
            _roots.Add(501, new NativeRuntime.RootHandle(4, _ => ++Freed, _ => new IntPtr(501), main.VerifyAccess));
            Graph = InitialGraph();
            _admission = NativeTweenAdmission.CreateControlledForTests(Native,
                new() { Version = 1, Size = 32, ExactTweenClass = new(0x2222), CachedPointerField = new(0x3333), CachedPointerOffset = 16 },
                main.VerifyAccess, MakeRoot, root => root.Target);
        }
        public bool CleanupUnknown => false;
        internal NativeRuntime.RootHandle RetainedRoot(long identity) => _roots[identity];
        public int RootRecords => 2 + _admission.RootRecords;
        public IntPtr PlayMethodInfo => new(700);
        public void Initialize(NativeLocalTweenWorkOwner owner) { _owner = owner; owner.RegisterSource(1001); }
        public NativeTweenWorkGraph Capture(NativeTweenOperation operation, NativeOperationFrame frame, bool after)
        {
            var tween = operation == NativeTweenOperation.Play ? frame.Self.ToInt64()
                : operation is NativeTweenOperation.AddTweenToWaitingList or NativeTweenOperation.ExecuteTween ? frame.Arg0.ToInt64() : 0;
            if (tween != 0 && Graph.Tweens.All(row => row.Object != tween)) Graph = GraphWithDynamic(tween, queued: false);
            if (operation is NativeTweenOperation.AddTweenToWaitingList or NativeTweenOperation.ExecuteTween
                && Graph.Managers.All(row => row.Object != frame.Self.ToInt64()))
                Graph = Graph with { Managers = Graph.Managers.Append(new NativeTweenManagerObservation(frame.Self.ToInt64(), 202, 1, 4, 0,
                    new(603, 603, 503, 0, 0, new long[4]), new(601, 701, 501, 0, 1, new long[4]),
                    new(602, 702, 502, 0, 1, new long[4]))).ToArray(),
                    Tweens = Graph.Tweens.Append(Info(3003, true)).ToArray() };
            return Graph;
        }
        public NativeTweenWorkGraph Refresh()
        {
            _main.VerifyAccess(); ++RefreshReads;
            if (DynamicLease is not null) { _ = DynamicLease.Read(); DynamicClaims = DynamicLease.Claimed ? 1 : 0; }
            return Graph;
        }
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity() => new(0, true);
        public NativeTweenCurrentManagerSourceRoots BorrowCurrentManagerSourceRoots(NativeLocalTweenWorkOwner owner, NativeOperationFrame frame)
        {
            VerifyTweenAdmissionOwner(); Check(ReferenceEquals(owner, _owner)); ++ManagerBorrows;
            return new(_roots[frame.Self.ToInt64()], _roots[frame.Arg0.ToInt64()]);
        }
        internal void AppendNewManager(long tween)
        {
            Graph = Graph with { Managers = Graph.Managers.Select(manager => manager.Object == 501
                ? manager with { Scheduled = manager.Scheduled! with { Count = 1, Version = 2, CapacityItems = new long[] { tween, 0, 0, 0 } } }
                : manager).ToArray() };
        }
        public void VerifyTweenAdmissionOwner()
        {
            _main.VerifyAccess();
            if (_owner is null || _owner.ObservationAuthorityUnknown) throw new InvalidOperationException("Controlled dynamic owner is not known.");
        }
        public NativeTweenDynamicSourceLease AdoptDynamicSource(NativeLocalTweenWorkOwner owner, long tween)
        {
            VerifyTweenAdmissionOwner();
            if (!ReferenceEquals(owner, _owner) || tween != 2002 || !_roots.TryGetValue(tween, out var root))
                throw new InvalidOperationException("Dynamic Tween is not the exact pre-rooted fixture graph object.");
            var lease = _admission.ClaimRetainedRoot(root, this);
            DynamicLease = lease;
            DynamicRoot = root; DynamicLeaseRoot = lease.Root;
            if (lease.Claimed) ++DynamicClaims;
            return new(root, lease);
        }
        public void VerifyTweenDeathOwner(NativeLocalTweenWorkOwner owner)
        { VerifyTweenAdmissionOwner(); if (!ReferenceEquals(owner, _owner)) throw new InvalidOperationException("Foreign dynamic death owner."); }
        public void BorrowTweenDeathSources(NativeLocalTweenWorkOwner owner, Action<NativeRuntime.RootHandle, NativeTweenAdmissionLease> borrow)
            => throw new NotSupportedException("Dynamic promotion supplies its own exact root and lease.");
        public NativeTweenDeathObservation ReadTweenDeathObservation(NativeRuntime.RootHandle root)
        {
            _main.VerifyAccess();
            if (!ReferenceEquals(root, DynamicRoot) || root.Target.ToInt64() != 2002)
                throw new InvalidOperationException("Foreign dynamic death root.");
            return new(2002, new(0x2222), new(0x3333), 0x4444, true, 10);
        }
        internal NativeTweenWorkGraph GraphWithDynamic(long tween, bool queued)
        {
            var manager = Graph.Managers.Single();
            var scheduled = queued ? new long[] { tween, 0, 0, 0 } : new long[4];
            var infos = Graph.Tweens.Where(row => row.Object != tween).Append(Info(tween, alive: true) with { IsOver = true }).ToArray();
            return Graph with { Tweens = infos, Managers = new[] { manager with {
                Scheduled = manager.Scheduled! with { Count = queued ? 1 : 0, Version = queued ? 2 : 1, CapacityItems = scheduled } } } };
        }
        internal NativeOperationFrame Frame(NativeTweenOperation operation, long tween)
            => new() { Version = 1, Size = 64, Kind = TweenOperationProfile.Get(operation).Kind,
                Self = new(operation == NativeTweenOperation.Play ? tween : 201),
                Arg0 = TweenOperationProfile.Get(operation).Kind == NativeOperationKind.Reference1 ? new(tween) : IntPtr.Zero,
                Method = new(700 + (int)operation), Caller = new(999), Sequence = ++_sequence };
        private NativeRuntime.RootHandle MakeRoot(IntPtr identity)
        {
            ++ClaimAttempts;
            var root = new NativeRuntime.RootHandle((uint)(100 + ClaimAttempts), _ => ++Freed, _ => identity, _main.VerifyAccess);
            return root;
        }
        private static NativeTweenWorkGraph InitialGraph()
            => new(801, Array.Empty<NativeTweenDelegateObservation>(), new[] { Info(1001, true) }, new[] {
                new NativeTweenManagerObservation(201, 202, 1, 4, 1,
                    new(303, 303, 503, 1, 0, new long[] { 1001, 0, 0, 0 }),
                    new(301, 401, 501, 0, 1, new long[4]), new(302, 402, 502, 0, 1, new long[4])) });
        private static NativeTweenInfoObservation Info(long tween, bool alive)
            => new(tween, 0x2222, 100, 101, 102, 103, 104, BitConverter.SingleToInt32Bits(1),
                BitConverter.SingleToInt32Bits(.1f), 0, 15, 3, false, false, !alive,
                new(false, false, 0, "", 0, 0, new(0, 0), new(0, 0, 0), new(0, 0, 0, 0),
                    new(0, 0, 0, 0), new(0, 0, 0, 0), new(0, 0), new(0, 0, 0)));
    }

    private sealed class SourceTransport : INativeTweenSourceTransport
    {
        private readonly List<NativeTweenSourceRecord> _records = new();
        internal void Add(NativeTweenSourceRecord record)
        { record.EventSequence = (ulong)_records.Count + 1; _records.Add(record); }
        public uint GetInfo(ref NativeTweenSourceInformation information)
        {
            information = new() { Version = 1, Size = 96, Configured = 1, MetadataBound = 1,
                BindingCount = NativeTweenSource.BindingCount, JournalCapacity = NativeTweenSource.JournalCapacity,
                Epoch = 9, Events = (ulong)_records.Count, Transactions = _records.Count == 0 ? 0UL : 1UL,
                CompletedTransactions = _records.Count > 1 ? 1UL : 0UL, PendingTransactions = _records.Count == 1 ? 1UL : 0UL,
                RetainedRecords = (ulong)_records.Count };
            return 0;
        }
        public uint ReadRecord(ulong epoch, ulong eventSequence, ref NativeTweenSourceRecord record)
        {
            if (epoch != 9 || eventSequence == 0 || eventSequence > (ulong)_records.Count) return 1;
            record = _records[checked((int)eventSequence - 1)]; return 0;
        }
    }

    private sealed class Transport : INativeTweenAdmissionTransport
    {
        internal uint ClaimStatus; internal int ClaimCalls;
        private NativeTweenAdmissionReceipt _receipt = new() { Version = 1, Size = 96, Slot = 4, Epoch = 41,
            Phase = NativeTweenAdmissionPhase.Open, Object = new(2002), ActualClass = new(0x2222), CachedNative = new(0x4444), AdmittedIssuers = 1 };
        public uint Configure(ref NativeTweenAdmissionConfiguration configuration) => 0;
        public uint GetInfo(ref NativeTweenAdmissionInformation information)
        {
            information = new() { Version = 1, Size = 104, Configured = 1, ExactTweenClass = new(0x2222),
                CachedPointerField = new(0x3333), CachedPointerOffset = 16, Capacity = 128, ConfigurationEpoch = 7 };
            return 0;
        }
        public uint Claim(IntPtr actualTween, ref NativeTweenAdmissionReceipt receipt)
        { ++ClaimCalls; if (ClaimStatus != 0) return ClaimStatus; receipt = _receipt; return 0; }
        public uint Read(uint slot, ulong epoch, ref NativeTweenAdmissionReceipt receipt) { receipt = _receipt; return 0; }
        public uint Seal(uint slot, ulong epoch, ref NativeTweenAdmissionReceipt receipt)
        { _receipt.Phase = NativeTweenAdmissionPhase.Sealed; receipt = _receipt; return 0; }
        public uint Unregister(uint slot, ulong epoch, ref NativeTweenAdmissionReceipt receipt)
            => throw new NotSupportedException("Dynamic promotion has no root-free authority.");
    }
}
