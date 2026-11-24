using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;
using static Assert;

// Controlled roots/graphs and journal, actual owner/context methods and broker
// physical Invoke/mid identity. No production launch, IL2CPP acceptance or native
// source publication is synthesized by these cases.
internal static class NativeTweenChildEventTests
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Tween child preactivation adoption publishes full slots before first actual Invoke generation", RegisteredBeforeEntry),
        ("Tween child shared event deduplicates the slot and independent root while retaining both field memberships", SharedMembership),
        ("Tween child actual empty Invoke ends effects only after inline post Dispose physical exit and source CALL After", EmptyChildEffects),
        ("Tween child runtime listener addition remains unaccounted despite an initially empty asset and source return", AddedListenerEffects),
        ("Tween child repeated actual Invoke contexts preserve distinct private source and effect generations", RepeatedGeneration),
        ("Tween child graph failure retains its independently adopted root and consumed slot responsibility", AdoptionFailure),
        ("Tween child zero argument adoption refuses onTween float fields before listener root allocation", FloatIsSeparate),
        ("Tween child registration refuses an already active physical Invoke and preserves existing event ownership", ActiveAdoption),
        ("Tween child copied diagnostics and foreign issuers cannot mint membership or completed effects", PrivateIssuer),
        ("Tween child registered calls and mutation receivers reuse actual Prepare and mutation ledgers", PrepareAndMutation),
        ("Tween controlled dynamic source appends child ownership only at an idle listener boundary", DynamicAdoption),
        ("Tween dynamic child registration refuses an active Invoke and retains its source responsibility", ActiveDynamicAdoption),
        ("Tween first Before from an exact active listener dispatch adopts new child events while its parent Invoke remains active", FirstListenerBefore),
        ("Tween first listener Add and Execute Before adopt their exact new manager and keep ordered admission ownership", FirstListenerManagerBefore),
        ("Tween current listener dispatch receipts reject reuse after dispatch completion and nested foreign Invoke masks", ListenerReceiptBoundaries),
        ("Tween first Before retains Unknown when its new child reuses an already active event generation", FirstListenerActiveChild)
    };

    private static void RegisteredBeforeEntry()
    {
        using var f = new Fixture(); f.Adopt();
        var registrations = f.Listeners.ReadTweenChildRegistrations();
        Equal(2, registrations.Count); Equal(3, f.Listeners.Read().Events);
        Check(registrations.All(row => row.RootAdopted && row.SlotRetained && row.GraphCaptured && !row.Failed && !row.Production));
        Check(f.Listeners.ReadOriginalTriggerEventIdentities().SequenceEqual(new long[] { 101 }));
        f.Events.DuringInvoke = receiver => {
            var child = f.Listeners.Read().EventSnapshots.Single(row => row.Event == receiver);
            Check(child.Generation == 1 && child.InFlight == 1 && child.Pre is not null && child.Initial.Event == receiver);
            f.Events.Inline(receiver);
        };
        f.Events.Call(201);
        var row = f.Listeners.Read().EventSnapshots.Single(value => value.Event == 201);
        Check(row.Entered == 1 && row.Exited == 1 && row.Returned && row.Generation == 1 && row.Inline is not null);
        Check(f.Listeners.ReadListenerEffects().CompletedEmptyInvocations == 1 && f.Listeners.ReadTweenChildInvocations().Count == 0);
    }
    private static void SharedMembership()
    {
        using var f = new Fixture(shared: true); f.Adopt();
        var memberships = f.Work.ReadChildEventMemberships(); var registrations = f.Listeners.ReadTweenChildRegistrations();
        Equal(2, memberships.Count); Check(memberships.Select(row => row.Role).Distinct().Count() == 2);
        Equal(2, f.Listeners.Read().Events); Equal(1, f.Events.ChildRoots);
        Check(registrations.Select(row => row.Slot).Distinct().Count() == 1 && memberships.Select(row => row.EventSlot).Distinct().Count() == 1);
        Check(f.Listeners.ReadOriginalTriggerEventIdentities().SequenceEqual(new long[] { 101 }));
    }
    private static void EmptyChildEffects()
    {
        using var f = new Fixture(); f.Adopt();
        f.Events.DuringInvoke = receiver => {
            f.Events.Inline(receiver);
            var child = f.Listeners.ReadTweenChildInvocations().Single();
            Check(child.NativeObserved && !child.AfterObserved && !child.ContextDisposed && !child.PhysicalExited && !child.EffectsEnded);
        };
        var source = f.BeginSource(); f.Events.Call(201, source.RelayReturnPc.ToInt64() + 5, associate: true);
        var returned = f.Listeners.ReadTweenChildInvocations().Single();
        Check(returned.AfterObserved && returned.ContextDisposed && returned.PhysicalExited && !returned.EffectsEnded);
        Equal(1, f.Listeners.ReadListenerEffects().CompletedEmptyInvocations);
        f.EndSource(source);
        Check(f.Listeners.ReadTweenChildInvocations().Single().EffectsEnded);
        Check(f.Work.ReadSourceReplay().InlineEvents!.Single().EffectsEnded && !f.Work.ReadSourceReplay().Production && !f.Work.CanRetireWork());
        Equal(0, f.Roots.Freed);
    }
    private static void AddedListenerEffects()
    {
        using var f = new Fixture(); f.Adopt();
        f.AddListener();
        Check(f.Listeners.Read().EventSnapshots.Single(row => row.Event == 201).Initial.ExecutingCalls.Rows.Count == 0);
        f.Events.DuringInvoke = f.Events.Inline;
        var source = f.BeginSource(); f.Events.Call(201, source.RelayReturnPc.ToInt64() + 5, associate: true); f.EndSource(source);
        var child = f.Listeners.ReadTweenChildInvocations().Single();
        Check(child.AfterObserved && child.ContextDisposed && child.PhysicalExited && !child.EffectsEnded);
        Check(f.Listeners.ReadListenerEffects().Invocations.Single().State == NativeUnityEventListenerEffectState.Unaccounted);
        Check(!f.Work.ReadSourceReplay().InlineEvents!.Single().EffectsEnded && !f.Work.CanRetireWork());
    }
    private static void RepeatedGeneration()
    {
        using var f = new Fixture(); f.Adopt(); f.Events.DuringInvoke = f.Events.Inline;
        for (var index = 0; index < 2; ++index)
        {
            var source = f.BeginSource(); f.Events.Call(201, source.RelayReturnPc.ToInt64() + 5, associate: true); f.EndSource(source);
        }
        var children = f.Listeners.ReadTweenChildInvocations();
        Check(children.Select(row => row.Generation).SequenceEqual(new long[] { 1, 2 }));
        Check(children.Select(row => row.Transaction).Distinct().Count() == 2 && children.All(row => row.EffectsEnded));
        Check(f.Work.ReadSourceReplay().InlineEvents!.All(row => row.EffectsEnded));
    }
    private static void AdoptionFailure()
    {
        using var f = new Fixture(); f.Events.FailGraph = 201;
        Throws<IOException>(f.Adopt);
        var registration = f.Listeners.ReadTweenChildRegistrations().Single();
        Check(registration is { RootAdopted: true, SlotRetained: true, GraphCaptured: false, Failed: true, Slot: 1 });
        Check(f.Work.Unknown && f.Listeners.Unknown && f.Events.ChildRoots == 1 && f.Events.SlotIdentity(1) == 201 && f.Roots.Freed == 0);
        Equal(2, f.Listeners.Read().Events);
    }
    private static void FloatIsSeparate()
    {
        using var f = new Fixture(); f.Tweens.WrongFloatField = true;
        Throws<UnsupportedSignatureException>(f.Adopt);
        Check(f.Work.Unknown && f.Events.ChildRoots == 0 && f.Listeners.ReadTweenChildRegistrations().Count == 0 && f.Roots.Freed == 0);
    }
    private static void ActiveAdoption()
    {
        using var f = new Fixture(); f.Events.DuringInvoke = _ => Throws<InvalidOperationException>(f.Adopt);
        f.Events.Call(101);
        Check(f.Work.Unknown && f.Events.ChildRoots == 0 && f.Listeners.Read().Events == 1 && f.Listeners.ReadTweenChildRegistrations().Count == 0);
    }
    private static void PrivateIssuer()
    {
        using var f = new Fixture(); f.Adopt(); f.AddListener(); f.Events.DuringInvoke = f.Events.Inline;
        var source = f.BeginSource(); f.Events.Call(201, source.RelayReturnPc.ToInt64() + 5, associate: true); f.EndSource(source);
        var copied = f.Listeners.ReadTweenChildInvocations().Single() with { EffectsEnded = true, Production = true };
        Check(copied.EffectsEnded && !f.Listeners.ReadTweenChildInvocations().Single().EffectsEnded);
        Check(!f.Work.ReadSourceReplay().InlineEvents!.Single().EffectsEnded);
        Throws<InvalidOperationException>(() => new NativeLocalTweenWorkOwner.ChildEventMembership(new object(), f.Work, f.Listeners,
            f.Tweens.Tween, f.Tweens.StartField, f.Tweens.Start, 1001, 201, NativeTweenChildEventRole.OnStart));
        Throws<InvalidOperationException>(() => new NativeLocalTweenWorkOwner.InlineEventSource(new object(), f.Work, copied));
        Equal(0, f.Roots.Freed); Check(!f.Work.CanRetireWork());
    }
    private static void PrepareAndMutation()
    {
        using var f = new Fixture(); f.Adopt();
        var graph = f.Events.Graph(201); f.Events.Prepare(graph.Calls, graph.ExecutingCalls.Object);
        var prepared = f.Listeners.Read().EventSnapshots.Single(row => row.Event == 201);
        Check(prepared.IndependentPrepared is not null && prepared.PrepareGeneration == 1 && prepared.IndependentPrepared.Graph.Event == 201);
        f.AddListener();
        var row = f.Listeners.Read().EventSnapshots.Single(value => value.Event == 201);
        Check(row.Mutations!.Single().ConfirmedReturned && row.MutationGeneration == 1 && row.Mutations[0].Before!.RuntimeCalls.Rows.Count == 0
            && row.Mutations[0].After!.RuntimeCalls.Rows.Count == 1 && !f.Listeners.Unknown);
    }

    private static void DynamicAdoption()
    {
        using var f = new Fixture(); f.Adopt();
        var before = f.Work.ReadChildEventMemberships();
        f.AdoptDynamic();
        var after = f.Work.ReadChildEventMemberships();
        Equal(2, after.Count - before.Count);
        Check(after.Where(row => row.Tween == 2002).Select(row => row.Role).OrderBy(value => value)
            .SequenceEqual(new[] { NativeTweenChildEventRole.OnStart, NativeTweenChildEventRole.OnComplete }));
        Check(f.Listeners.ReadTweenChildRegistrations().Where(row => row.Tween == 2002)
            .All(row => row.RootAdopted && row.SlotRetained && row.GraphCaptured && !row.Failed && !row.Production));
        Check(f.Work.Read().Sources == 2 && f.Roots.Freed == 0 && !f.Work.CanRetireWork());
    }

    private static void ActiveDynamicAdoption()
    {
        using var f = new Fixture(); f.Adopt();
        f.Events.DuringInvoke = _ => Throws<InvalidOperationException>(f.AdoptDynamic);
        f.Events.Call(101);
        Check(f.Work.Unknown && f.Work.Read().Sources == 2
            && !f.Listeners.ReadTweenChildRegistrations().Any(row => row.Tween == 2002) && f.Roots.Freed == 0);
    }

    private static void FirstListenerBefore()
    {
        using var f = new Fixture(); f.Adopt(); f.Tweens.UseDistinctDynamicEvents();
        var initial = f.Listeners.Read().EventSnapshots.Single(row => row.Event == 101).Initial;
        f.Events.DuringInvoke = receiver => f.Events.ControlledDispatch(receiver, () => {
            var frame = f.Tweens.DynamicFrame(NativeTweenOperation.Play);
            var child = f.Work.CaptureOperation(NativeTweenOperation.Play, frame)!;
            Check(child is { Captured: true, Completed: false, ForeignMask: false } && child.DynamicSource is not null);
            Check(!f.Work.Unknown && !f.Listeners.Unknown && f.Work.Read().Sources == 2
                && f.Work.ReadTweenDeaths().Single().Phase == NativeLocalTweenDeathPhase.LiveObserved);
            var registrations = f.Listeners.ReadTweenChildRegistrations().Where(row => row.Tween == 2002).ToArray();
            Check(registrations.Length == 2 && registrations.All(row => row.RootAdopted && row.SlotRetained && row.GraphCaptured));
            Check(f.Listeners.Read().EventSnapshots.Single(row => row.Event == 101) is { InFlight: 1, Returned: false }
                && ReferenceEquals(initial, f.Listeners.Read().EventSnapshots.Single(row => row.Event == 101).Initial));
            f.Work.CompleteOperation(child, frame, NativeOperationExitFlags.Returned); child.Dispose();
        });
        f.Events.Call(101);
        Check(!f.Work.Unknown && !f.Listeners.Unknown && f.Work.Read().Operations.Single() is { Completed: true, Disposed: true }
            && f.Listeners.Read().Events == 5 && f.Roots.Freed == 0);
    }

    private static void ListenerReceiptBoundaries()
    {
        using var f = new Fixture(); f.Adopt();
        NativeLocalUnityEventLedger.TweenDynamicSource? receipt = null;
        var frame = f.Tweens.DynamicFrame(NativeTweenOperation.Play);
        f.Events.DuringInvoke = receiver => {
            if (receiver != 101) return;
            Check(f.Listeners.TryCaptureDynamicTweenSource(f.Work, null, frame) is null);
            f.Events.ControlledDispatch(receiver, () => {
                receipt = f.Listeners.TryCaptureDynamicTweenSource(f.Work, null, frame);
                Check(receipt?.IsCurrentFor(f.Listeners, f.Work, frame) == true);
                f.Events.DuringInvoke = nested => {
                    Check(nested == 999 && !receipt!.IsCurrentFor(f.Listeners, f.Work, frame)
                        && f.Listeners.TryCaptureDynamicTweenSource(f.Work, null, frame) is null);
                    Check(f.Work.CaptureOperation(NativeTweenOperation.Play, frame) is null);
                };
                f.Events.Call(999);
                Check(receipt!.IsCurrentFor(f.Listeners, f.Work, frame));
            });
            Check(!receipt!.IsCurrentFor(f.Listeners, f.Work, frame)
                && f.Listeners.TryCaptureDynamicTweenSource(f.Work, null, frame) is null);
        };
        f.Events.Call(101);
        Check(!receipt!.IsCurrentFor(f.Listeners, f.Work, frame) && !f.Work.Unknown && !f.Listeners.Unknown
            && f.Work.Read().Sources == 1 && f.Work.Read().Operations.Count == 0 && f.Roots.Freed == 0);
    }

    private static void FirstListenerManagerBefore()
    {
        foreach (var operation in new[] { NativeTweenOperation.AddTweenToWaitingList, NativeTweenOperation.ExecuteTween })
        {
            using var f = new Fixture(); f.Adopt(); f.Tweens.UseDistinctDynamicEvents();
            f.Events.DuringInvoke = receiver => f.Events.ControlledDispatch(receiver, () => {
                var frame = f.Tweens.DynamicFrame(operation);
                var child = f.Work.CaptureOperation(operation, frame)!;
                Check(child is { Captured: true, Completed: false, ForeignMask: false } && child.DynamicSource is not null
                    && f.Work.Read() is { Sources: 2, Managers: 1, Unknown: false });
                Check(f.SourceRoots.HasTweenOwnershipAnchor(2002) && f.SourceRoots.TryGetAnchorKind(new(501), out var role)
                    && role == NativeTweenSourceObjectKind.Manager && f.Tweens.ManagerBorrows == 1);
                if (operation == NativeTweenOperation.AddTweenToWaitingList) f.Tweens.AppendDynamic();
                f.Work.CompleteOperation(child, frame, NativeOperationExitFlags.Returned); child.Dispose();
            });
            f.Events.Call(101);
            Check(!f.Work.Unknown && !f.Listeners.Unknown && f.Roots.Freed == 0
                && f.Work.ReadChildEventMemberships().Count(row => row.Tween == 2002) == 2);
            if (operation == NativeTweenOperation.AddTweenToWaitingList)
                Check(f.Work.Read().Admissions.Single() is { Tween: 2002, Manager: 501, ConfirmedAppend: true, Ambiguous: false });
            else Check(f.Work.ReadSourceReplay().Operations.Single() is { Operation: NativeTweenOperation.ExecuteTween, Committed: true, Failed: false });
        }
    }

    private static void FirstListenerActiveChild()
    {
        using var f = new Fixture(); f.Adopt(); f.Tweens.DynamicStart = f.Roots.Root(new(101));
        f.Events.DuringInvoke = receiver => f.Events.ControlledDispatch(receiver, () => {
            var frame = f.Tweens.DynamicFrame(NativeTweenOperation.Play);
            var child = f.Work.CaptureOperation(NativeTweenOperation.Play, frame)!;
            Check(f.Work.Unknown && f.Listeners.Unknown && f.Work.Read().Sources == 2
                && f.Listeners.ReadTweenChildRegistrations().Last() is { Tween: 2002, RootAdopted: true, Failed: true, GraphCaptured: false });
            f.Work.CompleteOperation(child, frame, NativeOperationExitFlags.Returned); child.Dispose();
        });
        f.Events.Call(101);
        Check(f.Roots.Freed == 0 && f.Listeners.Read().EventSnapshots.Single(row => row.Event == 101).Initial.Event == 101);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new(); internal readonly RootTable Roots;
        internal readonly ListenerBackend Events; internal readonly TweenBackend Tweens;
        internal readonly NativeLocalUnityEventLedger Listeners; internal readonly NativeLocalTweenWorkOwner Work;
        private readonly Journal _journal = new(); private readonly NativeTweenSource _reader;
        internal readonly NativeTweenSourceRootSet SourceRoots;
        private NativeLocalTweenWorkOwner.OperationContext? _play; private ulong _sequence, _transaction;
        internal Fixture(bool shared = false)
        {
            Roots = new(Main); Events = new(Main, Roots); Listeners = NativeLocalUnityEventLedger.CaptureBackend(Main, Events, new Authority(), Events.Module);
            Tweens = new(Main, Roots, shared); Work = NativeLocalTweenWorkOwner.CaptureBackend(Main, Tweens); Events.Work = Work;
            _reader = NativeTweenSource.CreateControlledForTests(_journal, Main.VerifyAccess, Roots.Root, (root, _) => root.Target);
            var roots = _reader.RetainRootedObjects(Work, new[] { new NativeTweenSourceRootReference(Tweens.Tween, NativeTweenSourceObjectKind.Tween),
                new NativeTweenSourceRootReference(Tweens.Start, NativeTweenSourceObjectKind.UnityEvent),
                new NativeTweenSourceRootReference(Tweens.Complete, NativeTweenSourceObjectKind.UnityEvent) });
            SourceRoots = roots; Work.AttachSourceJournal(_reader, roots);
        }
        internal void Adopt() => Work.AdoptControlledTweenChildEventsForTests(Listeners);
        internal void AdoptDynamic() => Work.AdoptControlledDynamicTweenChildEventsForTests(Listeners, 2002, Tweens.DynamicTween);
        internal NativeTweenSourceRecord BeginSource()
        {
            var frame = new NativeOperationFrame { Version = 1, Size = 64, Kind = NativeOperationKind.Void0, Self = new(1001), Method = new(700), Caller = new(900), Sequence = ++_sequence };
            _play = Work.CaptureOperation(NativeTweenOperation.Play, frame)!;
            var row = new NativeTweenSourceRecord { Version = 1, Size = 192, Site = NativeTweenSourceSite.PlayOnStart,
                Stage = NativeTweenSourceStage.Before, Flags = NativeTweenSourceFlags.Known | NativeTweenSourceFlags.ParentMatched,
                ThreadId = NativeEngineInlinePlatform.CurrentOsThreadId, Depth = 1, Operation = NativeTweenSourceOperation.Play,
                Epoch = 7, UpdateSequence = frame.Sequence, Transaction = ++_transaction, Occurrence = _transaction,
                SourcePc = new(0x3957e8), RelayReturnPc = new(0x50000 + (long)_transaction * 32), Tween = new(1001), Container = new(201),
                SourceIndex = -1, DestinationIndex = -1, CountBefore = -1, CountAfter = -1, CapacityBefore = -1, CapacityAfter = -1, VersionBefore = -1, VersionAfter = -1 };
            _journal.Add(row); Work.PollSourceJournal(); return row;
        }
        internal void EndSource(NativeTweenSourceRecord source)
        {
            source.Stage = NativeTweenSourceStage.After; source.Flags |= NativeTweenSourceFlags.Paired; source.RelayReturnPc = new(source.RelayReturnPc.ToInt64() + 8);
            _journal.Add(source); Work.PollSourceJournal(); Work.CompleteOperation(_play!, _play!.Frame, NativeOperationExitFlags.Returned); _play.Dispose(); _play = null;
        }
        internal void AddListener()
        {
            var frame = new NativeOperationFrame { Version = 1, Size = 64, Kind = UnityEventOperationProfile.Get(UnityEventOperation.AddListener).Kind,
                Self = new(201), Arg0 = new(501), Method = new(702), Caller = new(703), Sequence = ++_sequence };
            var mutation = Listeners.CaptureMutation(UnityEventOperation.AddListener, frame)!;
            var graph = Events.Graph(201); var listener = new NativeUnityEventInvokable(501, 502, true, Array.Empty<NativeUnityEventLeaf>());
            Events.SetGraph(graph with { RuntimeCalls = graph.RuntimeCalls with { Version = 2, Rows = new[] { listener } },
                ExecutingCalls = graph.ExecutingCalls with { Version = 2, Rows = new[] { listener } } });
            Listeners.CompleteMutation(mutation, frame, NativeOperationExitFlags.Returned); mutation.Dispose();
        }
        public void Dispose() => Main.Dispose();
    }
    private sealed class Authority : ICutoutListenerAuthority
    {
        public bool Unknown => false;
        public void VerifyBeforeCapture() { }
        public void VerifyObservation() { }
        public bool TryConfirmSourceIdle() => true;
        public void ObserveFaultFromAnyThread() { }
    }
    private sealed class RootTable
    {
        private readonly MainThreadDispatcher _main; private readonly Dictionary<uint, IntPtr> _roots = new(); private uint _next;
        internal int Freed; internal RootTable(MainThreadDispatcher main) => _main = main;
        internal NativeRuntime.RootHandle Root(IntPtr value)
        { _main.VerifyAccess(); var key = ++_next; _roots.Add(key, value); return new(key, id => { _roots.Remove(id); ++Freed; }, id => _roots.GetValueOrDefault(id), _main.VerifyAccess); }
    }
    private sealed class TweenBackend : INativeLocalTweenBackend, INativeTweenChildEventSource,
        INativeLocalTweenDynamicChildEventBackend, INativeLocalTweenDynamicSourceBackend,
        INativeLocalTweenDeathBackend, INativeTweenAdmissionOwner, INativeLocalTweenDynamicManagerSourceBackend
    {
        private readonly MainThreadDispatcher _main;
        private readonly RootTable _roots;
        private readonly NativeTweenAdmission _admission;
        private NativeLocalTweenWorkOwner _owner = null!;
        private NativeTweenAdmissionLease? _lease;
        private ulong _dynamicSequence;
        private NativeTweenWorkGraph _graph;
        private readonly NativeRuntime.RootHandle _manager;
        internal int ManagerBorrows;
        internal readonly NativeRuntime.RootHandle Tween, DynamicTween, Start, Complete;
        internal NativeRuntime.RootHandle DynamicStart, DynamicComplete;
        internal readonly NativeExactReadField StartField = new(new(0x88), new(301), new(302), new(303), "onStart", "UnityEngine.Events.UnityEvent", false, 0x88, 8, 0);
        internal bool WrongFloatField;
        internal TweenBackend(MainThreadDispatcher main, RootTable roots, bool shared)
        {
            _main = main; _roots = roots; Tween = roots.Root(new(1001)); DynamicTween = roots.Root(new(2002));
            Start = roots.Root(new(201)); Complete = roots.Root(new(shared ? 201 : 202));
            DynamicStart = Start; DynamicComplete = Complete;
            _manager = roots.Root(new(501));
            _graph = new(0, Array.Empty<NativeTweenDelegateObservation>(), new[] { TweenInfo(1001), TweenInfo(2002) }, Array.Empty<NativeTweenManagerObservation>());
            _admission = NativeTweenAdmission.CreateControlledForTests(new AdmissionTransport(),
                new() { Version = 1, Size = 32, ExactTweenClass = new(0x2222), CachedPointerField = new(0x3333), CachedPointerOffset = 16 },
                main.VerifyAccess, roots.Root, root => root.Target);
        }
        public bool CleanupUnknown => false; public int RootRecords => 4; public IntPtr PlayMethodInfo => new(700);
        public void Initialize(NativeLocalTweenWorkOwner owner) { _owner = owner; owner.RegisterSource(1001); }
        public NativeTweenWorkGraph Capture(NativeTweenOperation operation, NativeOperationFrame frame, bool after)
        {
            if (operation is NativeTweenOperation.AddTweenToWaitingList or NativeTweenOperation.ExecuteTween && _graph.Managers.Count == 0)
                _graph = _graph with { Managers = new[] { new NativeTweenManagerObservation(501, 502, 1, 4, 0,
                    new(701, 701, 801, 0, 0, new long[4]), new(702, 802, 902, 0, 1, new long[4]), new(703, 803, 903, 0, 1, new long[4])) } };
            return Refresh();
        }
        public NativeTweenWorkGraph Refresh() => _graph;
        internal void AppendDynamic()
        { var manager = _graph.Managers.Single(); _graph = _graph with { Managers = new[] { manager with {
            Scheduled = manager.Scheduled! with { Count = 1, Version = 2, CapacityItems = new long[] { 2002, 0, 0, 0 } } } } }; }
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity() => new(0, true);
        public void BorrowTweenChildEvents(NativeLocalTweenWorkOwner owner, Action<NativeRuntime.RootHandle, NativeExactReadField, NativeRuntime.RootHandle> adopt)
        {
            _main.VerifyAccess();
            var first = WrongFloatField ? StartField with { Name = "onTween", TypeName = "UnityEngine.Events.UnityEvent<System.Single>", Offset = 0x98 } : StartField;
            adopt(Tween, first, Start); adopt(Tween, StartField with { Field = new(0x90), Name = "onComplete", Offset = 0x90 }, Complete);
        }
        public void BorrowDynamicTweenChildEvents(NativeLocalTweenWorkOwner owner, long tween,
            Action<NativeRuntime.RootHandle, NativeExactReadField, NativeRuntime.RootHandle> adopt)
        {
            _main.VerifyAccess();
            if (tween != 2002) throw new InvalidOperationException("Foreign controlled dynamic Tween.");
            adopt(DynamicTween, StartField, DynamicStart);
            adopt(DynamicTween, StartField with { Field = new(0x90), Name = "onComplete", Offset = 0x90 }, DynamicComplete);
        }
        internal void UseDistinctDynamicEvents() { DynamicStart = _roots.Root(new(601)); DynamicComplete = _roots.Root(new(602)); }
        internal NativeOperationFrame DynamicFrame(NativeTweenOperation operation) => new() { Version = 1, Size = 64,
            Kind = TweenOperationProfile.Get(operation).Kind, Self = new(operation == NativeTweenOperation.Play ? 2002 : 501),
            Arg0 = operation == NativeTweenOperation.Play ? IntPtr.Zero : new(2002), Method = new(700 + (int)operation), Caller = new(999), Sequence = ++_dynamicSequence };
        public NativeTweenDynamicSourceLease AdoptDynamicSource(NativeLocalTweenWorkOwner owner, long tween)
        { VerifyTweenAdmissionOwner(); Check(ReferenceEquals(owner, _owner) && tween == 2002 && _lease is null);
            _lease = _admission.ClaimRetainedRoot(DynamicTween, this); return new(DynamicTween, _lease); }
        public NativeTweenCurrentManagerSourceRoots BorrowCurrentManagerSourceRoots(NativeLocalTweenWorkOwner owner, NativeOperationFrame frame)
        { Check(ReferenceEquals(owner, _owner) && frame.Self.ToInt64() == 501 && frame.Arg0.ToInt64() == 2002); ++ManagerBorrows; return new(_manager, DynamicTween); }
        public void VerifyTweenAdmissionOwner() { _main.VerifyAccess(); if (_owner.ObservationAuthorityUnknown) throw new InvalidOperationException("Controlled dynamic listener source is Unknown."); }
        public void VerifyTweenDeathOwner(NativeLocalTweenWorkOwner owner) { VerifyTweenAdmissionOwner(); Check(ReferenceEquals(owner, _owner)); }
        public void BorrowTweenDeathSources(NativeLocalTweenWorkOwner owner, Action<NativeRuntime.RootHandle, NativeTweenAdmissionLease> borrow) => throw new NotSupportedException();
        public NativeTweenDeathObservation ReadTweenDeathObservation(NativeRuntime.RootHandle root)
        { Check(ReferenceEquals(root, DynamicTween)); return new(2002, new(0x2222), new(0x3333), 0x4444, true, 1); }
        private static NativeTweenInfoObservation TweenInfo(long tween) => new(tween, 0x2222, 0, 0, 201, 202, 0,
            BitConverter.SingleToInt32Bits(1), 0, 0, 0, 0, false, false, true,
            new(false, false, 0, "", 0, 0, new(0, 0), new(0, 0, 0), new(0, 0, 0, 0), new(0, 0, 0, 0),
                new(0, 0, 0, 0), new(0, 0), new(0, 0, 0)));
    }
    private sealed class ListenerBackend : ICutoutListenerBackend, INativeTweenChildEventBackend, INativeDetourEngine,
        IPreparedNativeDetourEngine, INativeTweenDynamicListenerSourceBackend
    {
        private static long _nextModule = 0x7B0000000;
        private readonly MainThreadDispatcher _main; private readonly RootTable _roots; private readonly ExactHookBroker _broker;
        private readonly Bridge _bridge = new(); private readonly VerifiedHookTarget _target;
        private ExactHookBroker.Binding? _binding; private ExactHookBroker.BoundaryProbeBinding? _probe;
        private NativeLocalUnityEventLedger _owner = null!; private readonly Dictionary<int, long> _slots = new();
        private readonly Dictionary<long, NativeUnityEventGraph> _graphs = new(); private readonly Dictionary<long, NativeRuntime.RootHandle> _childRoots = new();
        private int _prepareInFlight;
        private ulong _dispatchSequence;
        internal readonly long Module; internal NativeLocalTweenWorkOwner Work = null!; internal Action<long>? DuringInvoke;
        internal long FailGraph; internal int ChildRoots => _childRoots.Count;
        public bool CleanupUnknown => false; public int RootRecords => 1 + _childRoots.Count;
        internal ListenerBackend(MainThreadDispatcher main, RootTable roots)
        { _main = main; _roots = roots; Module = Interlocked.Add(ref _nextModule, 0x10000000); _broker = new(this, main, new TestLogger());
            _target = new("tween.child.fixture", new(Module + 0x15642e0), new(0x123), new(0x456), ExactHookAbi.NativeVoid0); }
        public void Initialize(NativeLocalUnityEventLedger owner)
        {
            _owner = owner; _slots.Add(owner.RegisterEvent(101), 101); _graphs.Add(101, EmptyGraph(101));
            _binding = _broker.BindNativeVoidObservation(_target, () => _bridge,
                frame => { if (!owner.ShouldObserveInvoke(frame.Self.ToInt64())) return null; var context = owner.CaptureNativeInvoke(frame);
                    if (_bridge.Associate) owner.ObserveControlledRegisteredTweenChildEntryForTests(Work, context, frame); return context; },
                owner.CompleteNativeInvoke, owner.ObserveInvokeFault);
            _probe = _broker.BindBoundaryProbe(_target, (self, _, method) => owner.ProbeEnter(self.ToInt64(), method == _target.MethodInfo),
                (self, _, _, returned) => owner.ProbeExit(self.ToInt64(), returned));
        }
        public long AdoptBorrowedTweenChildEventRoot(NativeLocalUnityEventLedger owner, NativeLocalTweenWorkOwner.ChildEventMembership membership)
        {
            membership.RequireFor(owner); Check(ReferenceEquals(owner, _owner) && !membership.Production);
            var identity = membership.BorrowEventRoot(owner).Target.ToInt64();
            if (!_childRoots.ContainsKey(identity)) _childRoots.Add(identity, _roots.Root(new(identity)));
            return identity;
        }
        public void RetainTweenChildEventSlot(NativeLocalUnityEventLedger owner, NativeLocalTweenWorkOwner.ChildEventMembership membership, int slot, long identity)
        { membership.RequireFor(owner); Check(_childRoots.ContainsKey(identity)); _slots[slot] = identity; _graphs.TryAdd(identity, EmptyGraph(identity)); }
        internal long SlotIdentity(int slot) => _slots[slot];
        internal NativeUnityEventGraph Graph(long receiver) => _graphs[receiver];
        internal void SetGraph(NativeUnityEventGraph graph) => _graphs[graph.Event] = graph;
        public NativeUnityEventGraph ReadGraph(int eventSlot)
        { _main.VerifyAccess(); var receiver = _slots[eventSlot]; if (receiver == FailGraph) throw new IOException("Controlled child graph failed after root/slot retention."); return _graphs[receiver]; }
        public NativeUnityEventPrepared CaptureInline(int eventSlot, long actualList)
        { var graph = ReadGraph(eventSlot); Check(graph.ExecutingCalls.Object == actualList); return new(graph, graph.ExecutingCalls); }
        public NativeUnityEventPrepared CapturePrepared(int eventSlot, long returnedList) => CaptureInline(eventSlot, returnedList);
        public NativeUnityEventGraph CaptureMutation(int eventSlot, UnityEventOperation operation, NativeOperationFrame frame) => ReadGraph(eventSlot);
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity()
        { var invoke = _binding?.ReadActivity() ?? new(0, false); var probe = _probe?.ReadActivity() ?? new(0, false);
            return new(Math.Max(invoke.InFlight, probe.InFlight) + _prepareInFlight, invoke.Known && probe.Known); }
        public ExactHookBroker.NativeVoidPhysicalIdentity? BorrowCurrentDynamicListenerIdentity(
            NativeLocalUnityEventLedger owner, NativeLocalUnityEventLedger.InvokeContext context)
        {
            Check(ReferenceEquals(owner, _owner));
            return _binding!.TryGetCurrentNativeVoidContext<NativeLocalUnityEventLedger.InvokeContext>(new(context._receiver), out var retained, out var identity)
                && ReferenceEquals(retained, context) ? identity : null;
        }
        internal void ControlledDispatch(long receiver, Action original)
        {
            // The parent Invoke and mid identity are the real managed broker
            // path. This one dispatch record is explicitly controlled: it is
            // never a production native listener-site publication certificate.
            Check(_binding!.TryGetCurrentNativeVoidContext<NativeLocalUnityEventLedger.InvokeContext>(new(receiver), out var context, out var physical));
            Check(context!._record is not null && physical!.IsCurrentFor(context));
            Inline(receiver); var row = context._record!;
            var invokable = new NativeUnityEventInvokable(801, 802, true, Array.Empty<NativeUnityEventLeaf>());
            var list = new NativeUnityEventList(901, 1, new[] { invokable });
            var frame = new NativeListenerFrame { Version = 1, Size = 176, ParentSequence = 1,
                ChildSequence = ++_dispatchSequence, ParentManagedToken = unchecked((ulong)physical!.Token),
                Event = new(receiver), Invokable = new(invokable.Object), List = new(list.Object), Stage = NativeListenerStage.Before };
            var dispatch = new NativeLocalUnityEventLedger.DispatchRecord(frame) { Captured = true, Observation = new(list, invokable, 0) };
            row.Dispatches.Add(dispatch);
            try { original(); } finally { dispatch.Completed = dispatch.Returned = dispatch.NativeReturned = true; }
        }
        internal void Inline(long receiver)
        {
            Check(_binding!.TryGetCurrentNativeVoidContext<NativeLocalUnityEventLedger.InvokeContext>(new(receiver), out var context, out var identity));
            var frame = new NativeEventMidFrame { Version = 1, Size = 0x1b0, ReturnPc = new(Module + 0x1564420), EntryRsp = 0x10008,
                Rsp = 0x10010, Rsi = (ulong)receiver, Rbp = (ulong)_graphs[receiver].ExecutingCalls.Object };
            _owner.ObserveInlineList(context!, frame, identity!);
        }
        internal void Call(long receiver, long caller = 0x71234567, bool associate = false)
        { _bridge.Associate = associate; var frame = new NativeVoidFrame { Version = 1, Size = 40, Self = new(receiver), Method = _target.MethodInfo, Caller = new(caller) };
            _bridge.Call(frame, () => DuringInvoke?.Invoke(receiver)); _bridge.Associate = false; }
        internal void Prepare(long calls, long returned)
        {
            var frame = new NativeCoroutineFrame { Version = 1, Size = 48, Kind = NativeCoroutineKind.Factory, Self = new(calls), Method = new(900), Caller = new(901) };
            ++_prepareInFlight; var context = _owner.CapturePrepare(frame)!;
            try { _owner.CompletePrepare(context, frame, (nuint)returned); context.Dispose(); } finally { --_prepareInFlight; }
        }
        public Delegate Install(VerifiedHookTarget target, Delegate replacement) => throw new NotSupportedException();
        public IntPtr Prepare(VerifiedHookTarget target, IntPtr replacement) => new(0x123456);
        public void Commit(VerifiedHookTarget target) { }
        private static NativeUnityEventGraph EmptyGraph(long receiver) => new(receiver, receiver + 1000, receiver + 2000, false, false,
            new(receiver + 3000, 1, Array.Empty<NativeUnityEventInvokable>()), new(receiver + 4000, 1, Array.Empty<NativeUnityEventInvokable>()),
            new(receiver + 5000, 1, Array.Empty<NativeUnityEventInvokable>()), receiver + 6000, 1, Array.Empty<NativeUnityEventPersistent>(), 0);
    }
    private sealed class Bridge : INativeVoidContextBridge
    {
        private Func<NativeVoidFrame, nuint> _enter = null!; private Action<NativeVoidFrame, nuint, NativeVoidExitFlags> _exit = null!;
        private long _active; internal bool Associate;
        public IntPtr EntryAddress => new(0x123451);
        public void Configure(IntPtr original, Func<NativeVoidFrame, nuint> enter, Action<NativeVoidFrame, nuint, NativeVoidExitFlags> exit) => (_enter, _exit) = (enter, exit);
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity() => new(Interlocked.Read(ref _active), true);
        internal void Call(NativeVoidFrame frame, Action original)
        { Interlocked.Increment(ref _active); var token = _enter(frame); var returned = false;
            try { original(); returned = true; } finally { try { _exit(frame, token, returned ? NativeVoidExitFlags.Returned : NativeVoidExitFlags.Abnormal); } finally { Interlocked.Decrement(ref _active); } } }
    }
    private sealed class Journal : INativeTweenSourceTransport
    {
        private NativeTweenSourceInformation _info = new() { Version = 1, Size = 96, Configured = 1, MetadataBound = 1, BindingCount = 43, JournalCapacity = 8192, Epoch = 7 };
        private readonly Dictionary<ulong, NativeTweenSourceRecord> _records = new();
        internal void Add(NativeTweenSourceRecord row)
        { row.EventSequence = ++_info.Events; _records.Add(row.EventSequence, row); _info.RetainedRecords = _info.Events;
            _info.Transactions = Math.Max(_info.Transactions, row.Transaction); if (row.Stage == NativeTweenSourceStage.After) ++_info.CompletedTransactions;
            _info.PendingTransactions = _info.Transactions - _info.CompletedTransactions; }
        public uint GetInfo(ref NativeTweenSourceInformation information) { information = _info; return 0; }
        public uint ReadRecord(ulong epoch, ulong eventSequence, ref NativeTweenSourceRecord record)
            => _records.TryGetValue(eventSequence, out record) && epoch == _info.Epoch ? 0u : 3u;
    }
    private sealed class AdmissionTransport : INativeTweenAdmissionTransport
    {
        private NativeTweenAdmissionReceipt _receipt = new() { Version = 1, Size = 96, Slot = 4, Epoch = 41,
            Phase = NativeTweenAdmissionPhase.Open, Object = new(2002), ActualClass = new(0x2222), CachedNative = new(0x4444), AdmittedIssuers = 1 };
        public uint Configure(ref NativeTweenAdmissionConfiguration configuration) => 0;
        public uint GetInfo(ref NativeTweenAdmissionInformation information)
        { information = new() { Version = 1, Size = 104, Configured = 1, ExactTweenClass = new(0x2222), CachedPointerField = new(0x3333),
            CachedPointerOffset = 16, Capacity = 128, ConfigurationEpoch = 7 }; return 0; }
        public uint Claim(IntPtr actualTween, ref NativeTweenAdmissionReceipt receipt) { receipt = _receipt; return 0; }
        public uint Read(uint slot, ulong epoch, ref NativeTweenAdmissionReceipt receipt) { receipt = _receipt; return 0; }
        public uint Seal(uint slot, ulong epoch, ref NativeTweenAdmissionReceipt receipt)
        { _receipt.Phase = NativeTweenAdmissionPhase.Sealed; receipt = _receipt; return 0; }
        public uint Unregister(uint slot, ulong epoch, ref NativeTweenAdmissionReceipt receipt) => throw new NotSupportedException();
    }
    private sealed class TestLogger : ILogger { public void Log(LogLevel level, string message, Exception? exception = null) { } }
}
