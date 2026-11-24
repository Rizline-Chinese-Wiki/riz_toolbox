using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using static Assert;

// Controlled native transports, actual reader/slot replay, owner contexts,
// admission leases and death trackers. No game, unregister or Root free runs.
internal static class NativeTweenCompletionTests
{
    internal static readonly (string Name, Action Test)[] Cases = new (string Name, Action Test)[] {
        ("Tween completion borrows a private synchronous interval only after both duplicate source slots actually exit", DuplicateExit),
        ("Tween completion retains one same-pointer occurrence when only its sibling slot exits", OneDuplicateRemains),
        ("Tween completion does not end a ready transient or a copied playing label before actual clear", TransientAndCopy),
        ("Tween completion treats an event-only nullable instant Play as independent work and a legal skipped Play as no work", InstantAndSkipped),
        ("Tween completion refuses an unassociated returned inline child and a typed invocation despite native return and disposal", EffectsRemain),
        ("Tween completion retains setter effects when Update inline activation had no concrete engine transfer owner", SetterRemains),
        ("Tween completion invalidates a borrowed interval on a new journal cutoff and rejects it after the callback", CutoffAndLifetime),
        ("Tween completion requires the same concrete target death independently of its TweenInfo receipt", TargetIsSeparate),
        ("Tween completion freezes private withdrawal ownership before source release and performs no late native reads", FrozenWithdrawal),
        ("Tween completion reborrows the same withdrawn facts after native busy without polling its closed source", WithdrawnRetry),
        ("Tween completion accepts a real completed Awake alias replacement while retaining historical array residue", DetachedAlias),
        ("Tween completion rejects copied diagnostics and a fabricated private interval issuer", NoForgedCompletion)
    }.Concat(NativeTweenTypedListenerEffectTests.Cases).ToArray();

    private static void DuplicateExit()
    {
        using var f = new TweenCompletionFixture(playing: new long[] { 101, 101, 0, 0 }, working: 2);
        var update = f.Begin(NativeTweenOperation.Update); f.Clear(update, 1); f.Clear(update, 0); f.EndUpdate(update, new long[4], 0);
        f.ConfirmDeaths(); NativeLocalTweenWorkOwner.CompletionInterval? borrowed = null;
        Check(f.Owner.TryBorrowControlledCompletionIntervalForTests(interval => {
            borrowed = interval; interval.ValidateCurrent(f.Owner, null, null); interval.RequireCutoff(7, 4);
            Check(interval.Sources.SequenceEqual(new long[] { 101 }) && !interval.Production);
        }));
        Check(borrowed is not null && f.Owner.ReadWorkCompletion() is { QueueWork: 2, InstantWork: 0, Unknown: false }
            && f.Owner.ReadWorkCompletion().Pending.Count == 0 && f.Owner.ReadSourceReplay().Occurrences.All(row => row.QueueExited)
            && f.Backend.Frees == 0 && f.Backend.Native.Unregisters == 0);
        Throws<InvalidOperationException>(() => borrowed!.ValidateCurrent(f.Owner, null, null));
    }
    private static void OneDuplicateRemains()
    {
        using var f = new TweenCompletionFixture(playing: new long[] { 101, 101, 0, 0 }, working: 2);
        var update = f.Begin(NativeTweenOperation.Update); f.Clear(update, 1); f.EndUpdate(update, new long[] { 101, 0, 0, 0 }, 1);
        f.ConfirmDeaths(); Check(!f.Borrow());
        Check(f.Owner.ReadWorkCompletion().Pending.Any(text => text.Contains("scheduling alias") || text.Contains("distinct actual source-slot exit"))
            && f.Owner.ReadSourceReplay().Occurrences.Count(row => row.QueueExited) == 1 && !f.Owner.Unknown && f.Backend.Frees == 0);
    }
    private static void TransientAndCopy()
    {
        using (var f = new TweenCompletionFixture(ready: new long[] { 101, 0, 0, 0 }, readyCount: 1))
        {
            f.ReturnAndSeal();
            var update = f.Begin(NativeTweenOperation.Update);
            f.Pair(f.Before(update, NativeTweenSourceSite.ReadyPopFront, 302, 402, source: 0, count: 1, version: 1),
                count: 0, version: 2, raw: 1, flags: NativeTweenSourceFlags.PopSucceeded);
            Check(!f.Borrow());
            Check(f.Owner.ReadSourceReplay().Operations.Single().TransientOccurrences[0] is not null);
            f.Pair(f.Before(update, NativeTweenSourceSite.PlayingInsertFront, 303, 303, destination: 0, count: 4, version: 0,
                occurrence: 1));
            f.Clear(update, 0); f.Backend.SetReady(new long[4], 0, 2); f.EndUpdate(update, new long[4], 0);
            f.ConfirmDeaths(alreadyReturned: true); Check(f.Borrow());
        }
        using (var f = new TweenCompletionFixture(playing: new long[] { 0, 101, 0, 0 }, working: 2))
        {
            var update = f.Begin(NativeTweenOperation.Update);
            f.Pair(f.Before(update, NativeTweenSourceSite.PlayingMoveWrite, 303, 303, source: 1, destination: 0, count: 4, version: 0));
            Check(f.Owner.ReadSourceReplay().Operations.Single().PendingMove && f.Owner.ReadSourceReplay().Occurrences.Single().Locations.Count == 2);
            f.Pair(f.Before(update, NativeTweenSourceSite.PlayingMoveClear, 303, 303, source: 1, prior: 101, count: 4, version: 0));
            f.EndUpdate(update, new long[] { 101, 0, 0, 0 }, 1); f.ConfirmDeaths(); Check(!f.Borrow());
            update = f.Begin(NativeTweenOperation.Update); f.Clear(update, 0); f.EndUpdate(update, new long[4], 0); Check(f.Borrow());
        }
    }
    private static void InstantAndSkipped()
    {
        using var f = new TweenCompletionFixture();
        var play = f.Begin(NativeTweenOperation.Play); f.End(play); f.ConfirmDeaths();
        Check(f.Borrow() && f.Owner.ReadWorkCompletion() is { InstantWork: 1, QueueWork: 0 });
        var skipped = f.Begin(NativeTweenOperation.Play); f.End(skipped, NativeOperationExitFlags.Skipped);
        Check(f.Borrow() && f.Owner.ReadSourceReplay().Operations.Last() is { Skipped: true, Committed: false, Failed: false }
            && f.Owner.ReadSourceReplay().Occurrences.Count == 1 && f.Backend.Frees == 0);
    }
    private static void EffectsRemain()
    {
        using (var f = new TweenCompletionFixture())
        {
            var play = f.Begin(NativeTweenOperation.Play);
            f.Pair(f.Before(play, NativeTweenSourceSite.PlayOnStart, 105)); f.End(play); f.ConfirmDeaths();
            Check(!f.Borrow() && f.Owner.ReadWorkCompletion().Pending.Any(text => text.Contains("effects-ended/transfer")) && !f.Owner.Unknown);
        }
        using (var f = new TweenCompletionFixture(typed: true))
        {
            var play = f.Begin(NativeTweenOperation.Play); f.Backend.InvokeTyped(); f.End(play); f.ConfirmDeaths();
            Check(!f.Borrow() && f.Owner.ReadWorkCompletion().Pending.Any(text => text.Contains("executing-list")));
            var effect = f.Owner.ReadTypedEffects().Single();
            Check(effect is { Tween: 101, Instant: true, Returned: true, Disposed: true } && effect.Occurrence is not null && f.Backend.Frees == 0);
        }
    }
    private static void SetterRemains()
    {
        using var f = new TweenCompletionFixture(playing: new long[] { 101, 0, 0, 0 }, working: 1, tweenType: 3);
        var update = f.Begin(NativeTweenOperation.Update); f.Clear(update, 0); f.EndUpdate(update, new long[4], 0); f.ConfirmDeaths();
        Check(!f.Borrow() && f.Owner.ReadWorkCompletion().Pending.Any(text => text.Contains("setter")) && !f.Owner.Unknown);
    }
    private static void CutoffAndLifetime()
    {
        using var f = new TweenCompletionFixture(); var play = f.Begin(NativeTweenOperation.Play); f.End(play); f.ConfirmDeaths();
        NativeLocalTweenWorkOwner.CompletionInterval? saved = null;
        Check(f.Owner.TryBorrowControlledCompletionIntervalForTests(interval => {
            saved = interval; f.PublishForeignPair();
            Throws<InvalidOperationException>(() => interval.ValidateCurrent(f.Owner, null, null));
        }));
        Throws<InvalidOperationException>(() => saved!.RequireCutoff(7, 0));
        Check(!f.Owner.Unknown && f.Backend.Frees == 0);
    }
    private static void TargetIsSeparate()
    {
        using var f = new TweenCompletionFixture(target: true); var play = f.Begin(NativeTweenOperation.Play); f.End(play);
        f.ConfirmDeaths(killTargets: false); Check(f.Owner.ReadTweenDeathReceipt(f.Backend.Tween) is not null && !f.Borrow());
        Check(f.Owner.ReadWorkCompletion().Pending.Any(text => text.Contains("Concrete target")));
        f.Backend.DeadTargets(13); f.Owner.PumpControlledTweenDeathsForTests(); Check(!f.Borrow());
        f.Backend.DeadTargets(14); f.Owner.PumpControlledTweenDeathsForTests(); Check(f.Borrow());
        Check(f.Owner.ReadTweenTargetDeaths().All(row => row.ConfirmedFrame == 14) && f.Backend.Frees == 0);
    }
    private static void NoForgedCompletion()
    {
        using var f = new TweenCompletionFixture();
        Throws<InvalidOperationException>(() => new NativeLocalTweenWorkOwner.CompletionInterval(new object(), f.Owner));
        var copied = f.Owner.ReadWorkCompletion() with { Pending = Array.Empty<string>(), Unknown = false };
        Check(copied.Pending.Count == 0 && f.Owner.ReadWorkCompletion().Pending.Count != 0);
        Throws<InvalidOperationException>(() => f.Owner.TryBorrowControlledCompletionIntervalForTests(_ => { }));
        Check(f.Backend.Native.Unregisters == 0 && f.Backend.Frees == 0);
    }
    private static void FrozenWithdrawal()
    {
        using var f = new TweenCompletionFixture(); var play = f.Begin(NativeTweenOperation.Play); f.End(play); f.ConfirmDeaths();
        NativeLocalTweenWorkOwner.CompletionInterval? saved = null; NativeTweenSource.Withdrawal? withdrawal = null;
        Check(f.Owner.TryBorrowControlledCompletionIntervalForTests(interval => {
            saved = interval; interval.PrepareWithdrawal(); f.Backend.CloseObservationBindings(); withdrawal = f.Reader.BeginWithdrawal(f.Roots);
            interval.PrepareWithdrawal(withdrawal); interval.VerifyFrozenOwnership(f.Owner, null, null, withdrawal);
            Check(withdrawal.CommitReleaseForTests() && f.Roots.Retired);
            f.Backend.RefuseReads = f.Backend.Native.RefuseReads = true;
            interval.VerifyFrozenOwnership(f.Owner, null, null, withdrawal);
        }));
        Throws<InvalidOperationException>(() => saved!.VerifyFrozenOwnership(f.Owner, null, null, withdrawal!));
        Check(f.Backend.Native.Unregisters == 0);
    }
    private static void WithdrawnRetry()
    {
        using var f = new TweenCompletionFixture(target: true); var play = f.Begin(NativeTweenOperation.Play); f.End(play); f.ConfirmDeaths();
        NativeLocalTweenWorkOwner.FrozenCompletion? prepared = null;
        Check(f.Owner.TryBorrowControlledCompletionIntervalForTests(interval => {
            interval.PrepareWithdrawal(); f.Backend.CloseObservationBindings();
            prepared = interval.PrepareWithdrawal(f.Reader.BeginWithdrawal(f.Roots));
        }));
        f.RefuseJournalReads(); f.Backend.ActivityCount = 1;
        Check(!f.Owner.TryBorrowWithdrawnCompletion(prepared!, _ => throw new InvalidOperationException("Busy cannot enter a cut callback.")) && !f.Owner.Unknown);
        f.Backend.ActivityCount = 0; NativeLocalTweenWorkOwner.CompletionInterval? reborrow = null;
        Check(f.Owner.TryBorrowWithdrawnCompletion(prepared!, interval => {
            reborrow = interval; interval.VerifyFrozenOwnership(f.Owner, null, null, prepared!.Withdrawal);
        }));
        Throws<InvalidOperationException>(() => reborrow!.VerifyFrozenOwnership(f.Owner, null, null, prepared!.Withdrawal));
        f.Backend.Native.Current.CachedNative = new(444);
        Throws<UnsupportedSignatureException>(() => f.Owner.TryBorrowWithdrawnCompletion(prepared!, _ => { }));
        Check(f.Owner.Unknown && f.Backend.Native.Unregisters == 0 && f.Backend.Frees == 0);
    }
    private static void DetachedAlias()
    {
        using var f = new TweenCompletionFixture(playing: new long[] { 101, 0, 0, 0 }, working: 1);
        var update = f.Begin(NativeTweenOperation.Update); f.End(update);
        var awake = f.Begin(NativeTweenOperation.Awake); f.Backend.ReplaceManagerAliases(); f.End(awake);
        f.ConfirmDeaths(); Check(f.Borrow());
        Check(f.Owner.ReadSourceReplay().Occurrences.Single().Locations.Single() is { Manager: 201, Generation: 1 }
            && f.Owner.Read().Current!.Managers.Single().Generation == 2 && f.Backend.Frees == 0);
    }
}

internal sealed partial class TweenCompletionFixture : IDisposable
{
    internal readonly MainThreadDispatcher Main = new();
    internal readonly CompletionBackend Backend;
    internal readonly NativeLocalTweenWorkOwner Owner;
    internal readonly NativeTweenSource Reader;
    internal readonly NativeTweenSourceRootSet Roots;
    private readonly CompletionJournal _journal = new();
    private ulong _sequence, _transaction;
    internal TweenCompletionFixture(long[]? playing = null, int working = 0, long[]? ready = null, int readyCount = 0,
        bool typed = false, bool target = false, int tweenType = 12, bool initiallyDeadTarget = false,
        Action<CompletionBackend>? prepare = null)
    {
        Backend = new(Main, playing, working, ready, readyCount, typed, target, tweenType, initiallyDeadTarget);
        prepare?.Invoke(Backend);
        Owner = NativeLocalTweenWorkOwner.CaptureBackend(Main, Backend); Owner.CaptureControlledTweenDeathSourcesForTests();
        Reader = NativeTweenSource.CreateControlledForTests(_journal, Main.VerifyAccess, Backend.SourceRoot, (root, _) => root.Target);
        Roots = Reader.RetainRootedObjects(Owner, new[] {
            (101L, NativeTweenSourceObjectKind.Tween), (201L, NativeTweenSourceObjectKind.Manager),
            (301L, NativeTweenSourceObjectKind.ScheduledList), (302L, NativeTweenSourceObjectKind.ReadyStack),
            (303L, NativeTweenSourceObjectKind.TweenArray), (401L, NativeTweenSourceObjectKind.TweenArray),
            (402L, NativeTweenSourceObjectKind.TweenArray), (105L, NativeTweenSourceObjectKind.UnityEvent),
            (106L, NativeTweenSourceObjectKind.UnityEvent)
        }.Select(row => new NativeTweenSourceRootReference(Backend.Root(new(Backend.Identity(row.Item1))), row.Item2)).ToArray());
        Owner.AttachSourceJournal(Reader, Roots);
    }
    internal NativeLocalTweenWorkOwner.OperationContext Begin(NativeTweenOperation operation, long caller = 999)
    {
        var frame = new NativeOperationFrame { Version = 1, Size = 64, Kind = TweenOperationProfile.Get(operation).Kind,
            Self = new(Backend.Identity(operation == NativeTweenOperation.Play ? 101 : 201)),
            Arg0 = operation is NativeTweenOperation.ExecuteTween or NativeTweenOperation.AddTweenToWaitingList ? new(Backend.Identity(101)) : IntPtr.Zero,
            Method = operation == NativeTweenOperation.Play ? Backend.PlayMethodInfo : new(700 + (int)operation), Caller = new(caller), Sequence = ++_sequence };
        return Owner.CaptureOperation(operation, frame)!;
    }
    internal void End(NativeLocalTweenWorkOwner.OperationContext context, NativeOperationExitFlags flags = NativeOperationExitFlags.Returned)
    { Owner.CompleteOperation(context, context.Frame, flags); context.Dispose(); }
    internal void EndUpdate(NativeLocalTweenWorkOwner.OperationContext context, long[] playing, int working)
    { Backend.SetPlaying(playing, working); End(context); }
    internal NativeTweenSourceRecord Before(NativeLocalTweenWorkOwner.OperationContext context, NativeTweenSourceSite site,
        long container, long array = 0, int source = -1, int destination = -1, long prior = 0, int count = -1,
        int version = -1, ulong? occurrence = null)
    {
        var transaction = ++_transaction; var snapshot = (uint)site <= 12;
        var parents = new List<NativeLocalTweenWorkOwner.OperationContext>();
        for (var parent = context.Parent; parent is not null; parent = parent.Parent)
            if (parent.Operation is NativeTweenOperation.Update or NativeTweenOperation.Play or NativeTweenOperation.ExecuteTween) parents.Add(parent);
        return new() { Version = 1, Size = 192, Site = site, Stage = NativeTweenSourceStage.Before,
            Flags = NativeTweenSourceFlags.Known | NativeTweenSourceFlags.ParentMatched, ThreadId = NativeEngineInlinePlatform.CurrentOsThreadId,
            Depth = (uint)parents.Count + 1, Operation = context.Operation == NativeTweenOperation.Play ? NativeTweenSourceOperation.Play
                : context.Operation == NativeTweenOperation.Update ? NativeTweenSourceOperation.Update : NativeTweenSourceOperation.Execute,
            Epoch = 7, UpdateSequence = context.Frame.Sequence, ParentUpdateSequence = parents.FirstOrDefault()?.Frame.Sequence ?? 0,
            Transaction = transaction, Occurrence = occurrence ?? transaction, SourcePc = new(0x100000 + (long)site * 32),
            RelayReturnPc = new(0x200000 + (long)site * 32), Manager = context.Operation == NativeTweenOperation.Play ? IntPtr.Zero : new(201),
            Container = new(Backend.Identity(container)), ArrayBefore = new(Backend.Identity(array)), ArrayAfter = new(Backend.Identity(array)), Tween = new(Backend.Identity(101)), Prior = new(Backend.Identity(prior)),
            SourceIndex = source, DestinationIndex = destination, CountBefore = count, CountAfter = count,
            CapacityBefore = snapshot ? 4 : -1, CapacityAfter = snapshot ? 4 : -1, VersionBefore = version, VersionAfter = version };
    }
    internal void Pair(NativeTweenSourceRecord before, int? count = null, int? version = null, ulong raw = 0, NativeTweenSourceFlags flags = 0)
    { Publish(before); var after = before; after.Stage = NativeTweenSourceStage.After; after.Flags |= NativeTweenSourceFlags.Paired | flags;
        after.CountAfter = count ?? before.CountBefore; after.VersionAfter = version ?? before.VersionBefore; after.RawResult = raw;
        Publish(after); }
    internal void Publish(NativeTweenSourceRecord row) { _journal.Add(row); Owner.PollSourceJournal(); }
    internal void Clear(NativeLocalTweenWorkOwner.OperationContext update, int index)
        => Pair(Before(update, NativeTweenSourceSite.PlayingTailClear, 303, 303, source: index, prior: 101, count: 4, version: 0));
    internal void PublishForeignPair()
        => PublishUnpolledPair(999, 888);
    internal void PublishUnobservedOwnedPair()
        => PublishUnpolledPair(101, 105);
    private void PublishUnpolledPair(long tween, long receiver)
    { var row = new NativeTweenSourceRecord { Version = 1, Size = 192, Site = NativeTweenSourceSite.PlayOnStart,
        Flags = NativeTweenSourceFlags.Known | NativeTweenSourceFlags.ParentMatched, ThreadId = NativeEngineInlinePlatform.CurrentOsThreadId,
        Depth = 1, Operation = NativeTweenSourceOperation.Play, Epoch = 7, UpdateSequence = ++_sequence, Transaction = ++_transaction,
        SourcePc = new(0x10000), RelayReturnPc = new(0x20000), Container = new(receiver), Tween = new(tween),
        SourceIndex = -1, DestinationIndex = -1, CountBefore = -1, CountAfter = -1, CapacityBefore = -1, CapacityAfter = -1, VersionBefore = -1, VersionAfter = -1 };
        _journal.Add(row); row.Stage = NativeTweenSourceStage.After; row.Flags |= NativeTweenSourceFlags.Paired; _journal.Add(row); }
    internal void ReturnAndSeal() { Owner.RequestControlledTweenDeathReturnForTests(); Backend.Lease.Seal(); Owner.PumpControlledTweenDeathsForTests(); }
    internal void ConfirmDeaths(bool killTargets = true, bool alreadyReturned = false)
    { if (!alreadyReturned) ReturnAndSeal(); Backend.DeadTween(11); if (killTargets) Backend.DeadTargets(11); Owner.PumpControlledTweenDeathsForTests();
        Backend.DeadTween(12); if (killTargets) Backend.DeadTargets(12); Owner.PumpControlledTweenDeathsForTests(); }
    internal bool Borrow() => Owner.TryBorrowControlledCompletionIntervalForTests(interval => interval.ValidateCurrent(Owner, null, null));
    internal void RefuseJournalReads() => _journal.RefuseReads = true;
    internal void AfterNextJournalInformationRead(Action callback) => _journal.AfterNextInformation = callback;
    public void Dispose() => Main.Dispose();

    internal sealed partial class CompletionBackend : INativeLocalTweenBackend, INativeLocalTweenDeathBackend, INativeTweenAdmissionOwner,
        INativeTweenTypedEventSource, INativeTweenTargetDeathBackend, INativeTweenCompletionWithdrawalBackend,
        INativeTweenTypedEventGraphSource
    {
        private readonly MainThreadDispatcher _main; private NativeLocalTweenWorkOwner _owner = null!;
        private readonly Dictionary<uint, IntPtr> _roots = new(); private uint _next;
        private readonly NativeTweenAdmission _admission;
        private readonly NativeRuntime.RootHandle? _event, _component, _gameObject, _transform;
        private Func<NativeOperationFrame, IDisposable?>? _capture;
        private Action<IDisposable, NativeOperationFrame, NativeOperationExitFlags>? _complete;
        internal readonly NativeRuntime.RootHandle Tween;
        internal readonly CompletionAdmission Native = new();
        internal NativeTweenAdmissionLease Lease = null!;
        internal NativeTweenWorkGraph Graph;
        internal NativeTweenDeathObservation TweenObserved = new(101, new(222), new(333), 444, true, 10);
        internal readonly Dictionary<long, NativeTweenTargetDeathObservation> Targets = new();
        internal bool ChangedBinding, RefuseReads; internal int Frees, Reads; internal long ActivityCount; internal Action? DuringTargetRead;
        internal NativeSingleEventGraph? TypedEventGraph;
        internal Func<NativeSingleEventGraph>? ReadTypedEventGraphOverride;
        internal long TypedEventWriterGeneration;
        internal ExactHookBroker.ManagedBoundaryActivity TypedEventActivity = new(0, true);
        private ulong _typedEmptySequence = 800;
        private bool _observersClosed;
        internal Action? DuringObservationWithdrawal;
        private readonly NativeExactReadField _field = new(new(0x98), new(11), new(12), new(13), "onTween", "UnityEngine.Events.UnityEvent<System.Single>", false, 0x98, 8, 0);
        internal CompletionBackend(MainThreadDispatcher main, long[]? playing, int working, long[]? ready, int readyCount,
            bool typed, bool target, int tweenType, bool initiallyDeadTarget)
        {
            _main = main; CutNative = new(this); Tween = Root(new(101)); _event = typed ? Root(new(107)) : null;
            if (typed)
                TypedEventGraph = new(107, 1101, 1102, false, false,
                    new(107, 3010, 3012, 0, 1, Array.Empty<long>()),
                    new(107, 3011, 3013, 0, 1, Array.Empty<long>()),
                    new(107, 3012, 3014, 0, 1, Array.Empty<long>()),
                    new(107, 3001, 3011, 0, 1, Array.Empty<long>()));
            if (target)
            { _component = Root(new(501)); _gameObject = Root(new(502)); _transform = Root(new(503));
                foreach (var root in new[] { _component, _gameObject, _transform })
                    Targets.Add(root.Target.ToInt64(), new(root.Target.ToInt64(), new(700 + root.Target.ToInt64()), new(333),
                        initiallyDeadTarget ? 0 : 1000 + root.Target.ToInt64(), !initiallyDeadTarget, 10)); }
            var info = new NativeTweenInfoObservation(101, 222, target ? 501 : 0, 0, 105, 106, typed ? 107 : 0,
                0, 0, 0, 0, tweenType, false, false, true,
                new(false, false, 0, "", 0, 0, new(0, 0), new(0, 0, 0), new(0, 0, 0, 0), new(0, 0, 0, 0), new(0, 0, 0, 0), new(0, 0), new(0, 0, 0)));
            Graph = new(0, Array.Empty<NativeTweenDelegateObservation>(), new[] { info }, new[] { new NativeTweenManagerObservation(201, 202, 1, 4, working,
                new(303, 303, 403, working, 0, playing ?? new long[4]), new(301, 401, 501, 0, 1, new long[4]),
                new(302, 402, 502, readyCount, 1, ready ?? new long[4])) });
            _admission = NativeTweenAdmission.CreateControlledForTests(Native, new() { Version = 1, Size = 32, ExactTweenClass = new(222),
                CachedPointerField = new(333), CachedPointerOffset = 16 }, main.VerifyAccess, AdmissionRoot, root => root.Target);
        }
        internal NativeRuntime.RootHandle Root(IntPtr identity)
            => RetainRoot(identity, "Backend");
        internal NativeRuntime.RootHandle SourceRoot(IntPtr identity) => RetainRoot(identity, "Source");
        private NativeRuntime.RootHandle AdmissionRoot(IntPtr identity) => RetainRoot(identity, "Admission");
        public bool CleanupUnknown => false; public int RootRecords => _roots.Count; public IntPtr PlayMethodInfo => ActualPlayMethod;
        public void Initialize(NativeLocalTweenWorkOwner owner) { _owner = owner; owner.RegisterSource(Identity(101)); Lease = _admission.Claim(Tween, owner); owner.AdoptTypedEvents(this); }
        public NativeTweenWorkGraph Capture(NativeTweenOperation operation, NativeOperationFrame frame, bool after) => Graph;
        public NativeTweenWorkGraph Refresh() => RefuseReads ? throw new InvalidOperationException("No late native graph reads.") : Graph;
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity() => RefuseReads ? throw new InvalidOperationException("No late native activity reads.") : new(ActivityCount, true);
        public void VerifyTweenAdmissionOwner() { _main.VerifyAccess(); if (_owner is null || _owner.ObservationAuthorityUnknown) throw new InvalidOperationException("Controlled completion source Unknown."); }
        public void VerifyTweenDeathOwner(NativeLocalTweenWorkOwner owner) { VerifyTweenAdmissionOwner(); Check(ReferenceEquals(owner, _owner)); }
        public void BorrowTweenDeathSources(NativeLocalTweenWorkOwner owner, Action<NativeRuntime.RootHandle, NativeTweenAdmissionLease> borrow) { VerifyTweenDeathOwner(owner); borrow(Tween, Lease); }
        public NativeTweenDeathObservation ReadTweenDeathObservation(NativeRuntime.RootHandle root)
        { if (RefuseReads) throw new InvalidOperationException("No late concrete Tween death reads."); Check(ReferenceEquals(root, Tween)); return TweenObserved; }
        public void BorrowTypedTweenEvents(NativeLocalTweenWorkOwner owner, Action<NativeRuntime.RootHandle, NativeExactReadField, NativeRuntime.RootHandle?> adopt) => adopt(Tween, _field, _event);
        public void BindTypedTweenEvents(NativeLocalTweenWorkOwner owner, int capacity, Func<NativeOperationFrame, IDisposable?> capture,
            Action<IDisposable, NativeOperationFrame, NativeOperationExitFlags> complete, Action<NativeOperationFrame, string> fault) => (_capture, _complete) = (capture, complete);
        internal void CloseObservationBindings()
        { _capture = null; _complete = null; _observersClosed = true; DuringObservationWithdrawal?.Invoke(); }
        public void VerifyTweenCompletionBindingsWithdrawn(NativeLocalTweenWorkOwner owner)
        { Check(ReferenceEquals(owner, _owner) && _observersClosed && _capture is null && _complete is null); }
        internal void InvokeTyped()
        { var frame = new NativeOperationFrame { Version = 1, Size = 64, Kind = NativeOperationKind.Single1, Self = new(107), Method = new(900), Caller = new(901), Sequence = 800, Arg0 = new(0x3f800000) };
            var context = _capture!(frame)!; _complete!(context, frame, NativeOperationExitFlags.Returned); context.Dispose(); }
        internal void InvokeTypedEmpty(bool withMid = true, Action? betweenPreAndMid = null, Action? betweenMidAndPost = null,
            Action? betweenPostAndDispose = null, bool foreignChild = false, bool abnormal = false, NativeSingleExecutingList? midListOverride = null)
        {
            var seq = ++_typedEmptySequence;
            var frame = new NativeOperationFrame { Version = 1, Size = 64, Kind = NativeOperationKind.Single1,
                Self = new(107), Method = new(900), Caller = new(0x180395c07), Sequence = seq, Arg0 = new(0x7f800001) };
            TypedEventActivity = new(1, true);
            var context = _capture!(frame)!;
            betweenPreAndMid?.Invoke();
            if (withMid)
            {
                var mid = new NativeTweenSingleEventFrame {
                    Version = 1, Size = 200, Stage = NativeTweenSingleEventStage.Mid, Site = 3,
                    ThreadId = NativeEngineInlinePlatform.CurrentOsThreadId, Epoch = 9, ParentEpoch = 11,
                    ParentManagedToken = 13, ParentSequence = seq, Event = new(107), List = new(3001),
                    ParentMethod = frame.Method, ParentCaller = frame.Caller, ValueBits = frame.ScalarBits,
                    SourceRsp = 0x10000
                };
                _owner.CaptureTypedExecutingList(context, mid, midListOverride ?? new NativeSingleExecutingList(107, 3001, 3011, 0, 1, Array.Empty<long>()));
            }
            betweenMidAndPost?.Invoke();
            if (foreignChild)
            {
                var foreign = new NativeOperationFrame { Version = 1, Size = 64, Kind = NativeOperationKind.Single1,
                    Self = new(9999), Method = new(900), Caller = new(0x180395c07), Sequence = ++_typedEmptySequence, Arg0 = new(0x3f800000) };
                Check(_capture!(foreign) is null);
            }
            _complete!(context, frame, abnormal ? NativeOperationExitFlags.Abnormal : NativeOperationExitFlags.Returned);
            betweenPostAndDispose?.Invoke();
            context.Dispose();
            TypedEventActivity = new(0, true);
        }
        NativeSingleEventGraph INativeTweenTypedEventGraphSource.ReadTypedEventGraph(NativeLocalTweenWorkOwner owner, NativeRuntime.RootHandle eventRoot)
        {
            if (RefuseReads) throw new InvalidOperationException("No late typed event graph reads.");
            Check(ReferenceEquals(owner, _owner) && _event is not null && ReferenceEquals(eventRoot, _event));
            return ReadTypedEventGraphOverride?.Invoke() ?? TypedEventGraph!;
        }
        long INativeTweenTypedEventGraphSource.ReadTypedEventWriterGeneration(NativeLocalTweenWorkOwner owner, NativeRuntime.RootHandle eventRoot)
        {
            Check(ReferenceEquals(owner, _owner) && _event is not null && ReferenceEquals(eventRoot, _event));
            return TypedEventWriterGeneration;
        }
        ExactHookBroker.ManagedBoundaryActivity INativeTweenTypedEventGraphSource.ReadTypedEventActivity() => TypedEventActivity;
        public NativeTweenTargetBinding BorrowTweenTargetBinding(NativeLocalTweenWorkOwner owner, NativeRuntime.RootHandle root)
        { owner.RequireTweenTargetBaseline(root); Check(ReferenceEquals(root, Tween)); return new(Tween, _component, _gameObject, _transform, 600); }
        public NativeTweenTargetDeathObservation ReadTweenTargetDeath(NativeLocalTweenWorkOwner owner, NativeRuntime.RootHandle root)
        { if (RefuseReads) throw new InvalidOperationException("No late concrete target reads."); ++Reads; DuringTargetRead?.Invoke(); return Targets[root.Target.ToInt64()]; }
        public void VerifyTweenTargetBinding(NativeLocalTweenWorkOwner owner, NativeTweenTargetBinding binding)
        { if (RefuseReads) throw new InvalidOperationException("No late concrete target binding reads.");
            Check(ReferenceEquals(owner, _owner) && ReferenceEquals(binding.TweenRoot, Tween)); if (ChangedBinding) throw new InvalidOperationException("Concrete target changed or reparented."); }
        internal void DeadTween(int frame) { TweenObserved = TweenObserved with { Frame = frame, Alive = false, CachedNative = 0 }; Native.Current.CachedNative = IntPtr.Zero; }
        internal void DeadTargets(int frame) { foreach (var key in Targets.Keys.ToArray()) Targets[key] = Targets[key] with { Frame = frame, Alive = false, CachedNative = 0 }; }
        internal void SetPlaying(long[] values, int count) { var manager = Graph.Managers.Single(); Graph = Graph with { Managers = new[] { manager with { WorkingCount = count, Playing = manager.Playing! with { Count = count, CapacityItems = values } } } }; }
        internal void SetReady(long[] values, int count, int version) { var manager = Graph.Managers.Single(); Graph = Graph with { Managers = new[] { manager with { Ready = manager.Ready! with { Count = count, Version = version, CapacityItems = values } } } }; }
        internal void ReplaceManagerAliases() { var manager = Graph.Managers.Single(); Graph = Graph with { Managers = new[] { manager with { Generation = 2,
            WorkingCount = 0, Playing = new(1303, 1303, 403, 0, 0, new long[4]), Scheduled = new(1301, 1401, 501, 0, 1, new long[4]),
            Ready = new(1302, 1402, 502, 0, 1, new long[4]) } } }; }
    }
    internal sealed class CompletionAdmission : INativeTweenAdmissionTransport
    {
        internal int Unregisters;
        internal bool RefuseReads;
        internal NativeTweenAdmissionReceipt Current = new() { Version = 1, Size = 96, Slot = 3, Epoch = 41, Phase = NativeTweenAdmissionPhase.Open,
            Object = new(101), ActualClass = new(222), CachedNative = new(444), AdmittedIssuers = 1 };
        public uint Configure(ref NativeTweenAdmissionConfiguration configuration) => 0;
        public uint GetInfo(ref NativeTweenAdmissionInformation information) { if (RefuseReads) throw new InvalidOperationException("No late native admission information reads."); information = new() { Version = 1, Size = 104, Configured = 1,
            ExactTweenClass = new(222), CachedPointerField = new(333), CachedPointerOffset = 16, Capacity = 128, ConfigurationEpoch = 7 }; return 0; }
        public uint Claim(IntPtr value, ref NativeTweenAdmissionReceipt receipt) { receipt = Current; return 0; }
        public uint Read(uint slot, ulong epoch, ref NativeTweenAdmissionReceipt receipt) { if (RefuseReads) throw new InvalidOperationException("No late native lease reads."); receipt = Current; return 0; }
        public uint Seal(uint slot, ulong epoch, ref NativeTweenAdmissionReceipt receipt) { Current.Phase = NativeTweenAdmissionPhase.Sealed; receipt = Current; return 0; }
        public uint Unregister(uint slot, ulong epoch, ref NativeTweenAdmissionReceipt receipt) { ++Unregisters; throw new InvalidOperationException("Completion facts do not authorize unregister."); }
    }
    private sealed class CompletionJournal : INativeTweenSourceTransport
    {
        private readonly List<NativeTweenSourceRecord> _rows = new();
        internal bool RefuseReads;
        internal bool BreakNextSequence;
        internal Action? AfterNextInformation;
        internal void Add(NativeTweenSourceRecord row) { row.EventSequence = (ulong)_rows.Count + 1; _rows.Add(row); }
        public uint GetInfo(ref NativeTweenSourceInformation information) { if (RefuseReads) throw new InvalidOperationException("Closed source cannot be polled by a withdrawn facts retry."); information = new() { Version = 1, Size = 96, Configured = 1,
            MetadataBound = 1, BindingCount = 43, JournalCapacity = 8192, Epoch = 7, Events = (ulong)_rows.Count,
            Transactions = _rows.Select(row => row.Transaction).DefaultIfEmpty().Max(), CompletedTransactions = (ulong)_rows.Count(row => row.Stage == NativeTweenSourceStage.After),
            PendingTransactions = (ulong)(_rows.Count(row => row.Stage == NativeTweenSourceStage.Before) - _rows.Count(row => row.Stage == NativeTweenSourceStage.After)), RetainedRecords = (ulong)_rows.Count };
            var callback = AfterNextInformation; AfterNextInformation = null; callback?.Invoke(); return 0; }
        public uint ReadRecord(ulong epoch, ulong sequence, ref NativeTweenSourceRecord row)
        { if (RefuseReads) throw new InvalidOperationException("Closed source cannot reread a journal row."); if (epoch != 7 || sequence == 0 || sequence > (ulong)_rows.Count) return 3;
            row = _rows[checked((int)sequence - 1)]; if (BreakNextSequence) ++row.EventSequence; return 0; }
    }
}
