using System.Reflection;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

internal static class SingleP4RetirementChecks
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Single P4 controlled retirement succeeds ONLY on verified empty mid, stable Pre/Mid/Post/Dispose graph, stable writer generation, and drained activity", EmptyRetires),
        ("Single P4 controlled retirement rejects missing Mid, graph mutation, writer-generation change, foreign child, active transport, and non-empty Site 0/2 listeners", GatesAndNonEmptyReject),
        ("Single P4 controlled BusyThenRetry rejects late writer-generation, typed activity, or graph mutations between Busy and retry", BusyThenRetryInvalidationsReject),
        ("Single P4 controlled retirement rejects Skipped Play and Skipped ExecuteTween parents with child TypedEventInvocation while clean Skipped controls pass", SkippedParentWithTypedInvocationRejects),
        ("Single P4 controlled retirement allows valid non-empty Site 1 Play transfer across BusyThenRetry while rejecting null EmptyReceipt", Site1PlayTransferBusyThenRetry)
    };

    private static void Check(bool value) { if (!value) throw new InvalidOperationException("Single P4 retirement assertion failed."); }

    private static void EmptyRetires()
    {
        using var f = new RetirementFixture();
        var play = f.BeginPlay();
        f.InvokeEmpty(withMid: true);
        f.EndPlay(play);
        f.ConfirmDeaths();
        f.Owner.PumpControlledTweenRetirementForTests();
        Check(f.Owner.CanRetireWork() && !f.Owner.Unknown && f.Roots.Retired
            && f.Backend.TweenBackendRootsReleased && f.Backend.Cut.Commits == 1 && f.Backend.Cut.Releases == 1);
    }

    private static void GatesAndNonEmptyReject()
    {
        foreach (var mode in new[] { "no-mid", "post-changed", "writer-changed", "foreign-child", "active-transport", "nonempty-site0", "nonempty-site2", "setter" })
        {
            using var f = new RetirementFixture(tweenType: mode == "setter" ? 3 : 12);
            var play = f.BeginPlay();
            if (mode is "nonempty-site0" or "nonempty-site2")
            {
                f.InvokeNonEmpty(mode == "nonempty-site0" ? 0u : 2u);
            }
            else
            {
                f.InvokeEmpty(
                    withMid: mode != "no-mid",
                    mutatePost: mode == "post-changed",
                    incrementWriter: mode == "writer-changed",
                    foreignChild: mode == "foreign-child");
            }
            f.EndPlay(play);
            if (mode == "active-transport") f.Backend.TypedActivity = new(1, true);
            f.ConfirmDeaths();
            f.Owner.PumpControlledTweenRetirementForTests();
            Check(!f.Owner.CanRetireWork() && !f.Owner.Unknown && f.Backend.Cut.Acquires == 0 && f.Backend.Frees == 0);
        }
    }

    private static void BusyThenRetryInvalidationsReject()
    {
        foreach (var mode in new[] { "clean", "writer-gen", "typed-inflight", "typed-unknown", "graph-dirty" })
        {
            using var f = new RetirementFixture();
            var play = f.BeginPlay();
            f.InvokeEmpty(withMid: true);
            f.EndPlay(play);
            f.ConfirmDeaths();

            f.Backend.Cut.Busy = true;
            f.Owner.PumpControlledTweenRetirementForTests();
            Check(!f.Owner.CanRetireWork() && !f.Owner.Unknown && f.Backend.Cut.Acquires == 1
                && f.Backend.Cut.Commits == 0 && !f.Owner.RetiredRootsUnavailable);

            switch (mode)
            {
                case "writer-gen": ++f.Backend.WriterGen; break;
                case "typed-inflight": f.Backend.TypedActivity = new(1, true); break;
                case "typed-unknown": f.Backend.TypedActivity = new(0, false); break;
                case "graph-dirty":
                    var g = f.Backend.EventGraph;
                    f.Backend.EventGraph = g with { CallsDirty = true, RuntimeCalls = new(107, 3012, 3014, 1, 2, new[] { 9002L }) };
                    break;
            }

            f.Backend.Cut.Busy = false;
            f.Owner.PumpControlledTweenRetirementForTests();
            if (mode == "clean")
                Check(f.Owner.CanRetireWork() && !f.Owner.Unknown && f.Backend.Cut.Commits == 1 && f.Roots.Retired);
            else
                Check(!f.Owner.CanRetireWork() && f.Owner.Unknown && f.Backend.Cut.Commits == 0 && f.Backend.Frees == 0 && !f.Roots.Retired);
        }
    }

    private static void SkippedParentWithTypedInvocationRejects()
    {
        // Variant A: Skipped Play parent after executing child TypedEventInvocation
        using (var f = new RetirementFixture())
        {
            var play = f.BeginPlay();
            f.InvokeEmpty(withMid: true);
            var threw = false;
            try { f.Owner.CompleteOperation(play, play.Frame, NativeOperationExitFlags.Skipped); }
            catch (InvalidOperationException) { threw = true; }
            play.Dispose();
            f.Owner.PumpControlledTweenRetirementForTests();
            Check(threw && f.Owner.Unknown && !f.Owner.CanRetireWork()
                && f.Backend.Cut.Commits == 0 && f.Backend.Frees == 0 && !f.Roots.Retired
                && f.Owner.ReadSourceReplay().Occurrences.Count == 1
                && f.Owner.ReadTypedEffects().Single().Pending.Length > 0);
        }

        // Variant B: Skipped ExecuteTween parent after executing child TypedEventInvocation
        using (var f = new RetirementFixture(playing: new long[] { 101, 0, 0, 0 }, working: 1))
        {
            var update = f.Begin(NativeTweenOperation.Update);
            var call = f.Before(update, NativeTweenSourceSite.PlayingExecuteFront, 201, 303, source: 0, count: 4, version: 0);
            f.Publish(call);
            var exec = f.Begin(NativeTweenOperation.ExecuteTween, call.RelayReturnPc.ToInt64() + 5);
            f.InvokeEmpty(withMid: true);
            var threw = false;
            try { f.Owner.CompleteOperation(exec, exec.Frame, NativeOperationExitFlags.Skipped); }
            catch (InvalidOperationException) { threw = true; }
            exec.Dispose();
            f.Owner.PumpControlledTweenRetirementForTests();
            Check(threw && f.Owner.Unknown && !f.Owner.CanRetireWork()
                && f.Backend.Cut.Commits == 0 && f.Backend.Frees == 0 && !f.Roots.Retired
                && f.Owner.ReadSourceReplay().Occurrences.Count == 1
                && f.Owner.ReadTypedEffects().Single().Pending.Length > 0);
        }

        // Positive control: clean Skipped Play and Skipped ExecuteTween without child invocations retire normally
        using (var f = new RetirementFixture(playing: new long[] { 101, 0, 0, 0 }, working: 1))
        {
            var skippedPlay = f.BeginPlay();
            f.End(skippedPlay, NativeOperationExitFlags.Skipped);
            var update = f.Begin(NativeTweenOperation.Update);
            var skippedExec = f.Begin(NativeTweenOperation.ExecuteTween);
            f.End(skippedExec, NativeOperationExitFlags.Skipped);
            f.Clear(update, 0);
            f.EndUpdate(update, new long[4], 0);
            f.ConfirmDeaths();
            f.Owner.PumpControlledTweenRetirementForTests();
            Check(!f.Owner.Unknown && f.Owner.CanRetireWork()
                && f.Backend.Cut.Commits == 1 && f.Backend.Cut.Releases == 1 && f.Roots.Retired);
        }
    }

    private static void Site1PlayTransferBusyThenRetry()
    {
        using (var f = new RetirementFixture(playing: new long[] { 101, 0, 0, 0 }, working: 1))
        {
            var update = f.Begin(NativeTweenOperation.Update);
            var call = f.Before(update, NativeTweenSourceSite.PlayingExecuteFront, 201, 303, source: 0, count: 4, version: 0);
            f.Publish(call);
            var exec = f.Begin(NativeTweenOperation.ExecuteTween, call.RelayReturnPc.ToInt64() + 5);
            f.Backend.PlayConsumer = new(4001, 5001, 101, 700, 0x1803956f0, 0x1803956f0, 101);
            f.Backend.InvokeNonEmptySite1Play(() => {
                var innerPlay = f.BeginPlay();
                f.EndPlay(innerPlay);
            });
            f.End(exec);
            var afterCall = call; afterCall.Stage = NativeTweenSourceStage.After; afterCall.Flags |= NativeTweenSourceFlags.Paired;
            f.Publish(afterCall);
            f.Clear(update, 0);
            f.EndUpdate(update, new long[4], 0);
            f.ConfirmDeaths();

            Check(f.Owner.ReadTypedEffects().Single().Pending.Length == 0
                && f.Owner.ReadTypedExecutingLists().Single().Dispatches.Single().Transfer is not null);

            f.Backend.Cut.Busy = true;
            f.Owner.PumpControlledTweenRetirementForTests();
            Check(!f.Owner.CanRetireWork() && !f.Owner.Unknown && f.Backend.Cut.Acquires == 1
                && f.Backend.Cut.Commits == 0 && !f.Owner.RetiredRootsUnavailable);

            f.Backend.Cut.Busy = false;
            f.Owner.PumpControlledTweenRetirementForTests();
            Check(f.Owner.CanRetireWork() && !f.Owner.Unknown && f.Backend.Cut.Acquires == 2
                && f.Backend.Cut.Commits == 1 && f.Backend.Cut.Releases == 1 && f.Roots.Retired);
        }

        // ProbeD_08: post-Busy EventRoot, WriterGeneration, or TypedEventGraph mutations on a non-empty Site 1 Play transfer must fail closed
        foreach (var mode in new[] { "event-root-zero", "event-root-swap", "writer-gen", "graph-add-listener", "needs-update", "persistent-call-added" })
        {
            using var f = new RetirementFixture(playing: new long[] { 101, 0, 0, 0 }, working: 1);
            var update = f.Begin(NativeTweenOperation.Update);
            var call = f.Before(update, NativeTweenSourceSite.PlayingExecuteFront, 201, 303, source: 0, count: 4, version: 0);
            f.Publish(call);
            var exec = f.Begin(NativeTweenOperation.ExecuteTween, call.RelayReturnPc.ToInt64() + 5);
            f.Backend.PlayConsumer = new(4001, 5001, 101, 700, 0x1803956f0, 0x1803956f0, 101);
            f.Backend.InvokeNonEmptySite1Play(() => {
                var innerPlay = f.BeginPlay();
                f.EndPlay(innerPlay);
            });
            f.End(exec);
            var afterCall = call; afterCall.Stage = NativeTweenSourceStage.After; afterCall.Flags |= NativeTweenSourceFlags.Paired;
            f.Publish(afterCall);
            f.Clear(update, 0);
            f.EndUpdate(update, new long[4], 0);
            f.ConfirmDeaths();

            f.Backend.Cut.Busy = true;
            f.Owner.PumpControlledTweenRetirementForTests();
            Check(!f.Owner.CanRetireWork() && !f.Owner.Unknown && f.Backend.Cut.Acquires == 1
                && f.Backend.Cut.Commits == 0 && !f.Owner.RetiredRootsUnavailable);

            switch (mode)
            {
                case "event-root-zero":
                    f.Backend.SetEventRootTarget(IntPtr.Zero);
                    break;
                case "event-root-swap":
                    f.Backend.SetEventRootTarget(new IntPtr(9999));
                    break;
                case "writer-gen":
                    ++f.Backend.WriterGen;
                    break;
                case "graph-add-listener":
                    f.Backend.EventGraph = f.Backend.EventGraph with {
                        CallsDirty = true,
                        RuntimeCalls = new(107, 3012, 3014, 1, 2, new[] { 9002L })
                    };
                    break;
                case "needs-update":
                    f.Backend.EventGraph = f.Backend.EventGraph with { NeedsUpdate = true };
                    break;
                case "persistent-call-added":
                    f.Backend.EventGraph = f.Backend.EventGraph with {
                        PersistentCalls = new(107, 3011, 3013, 1, 2, new[] { 9001L })
                    };
                    break;
            }

            f.Backend.Cut.Busy = false;
            f.Owner.PumpControlledTweenRetirementForTests();
            Check(!f.Owner.CanRetireWork() && f.Owner.Unknown && f.Backend.Cut.Commits == 0
                && f.Backend.Frees == 0 && !f.Roots.Retired);
        }

        // 1.16 null-empty-receipt regression
        using (var f = new RetirementFixture())
        {
            var play = f.BeginPlay();
            f.InvokeEmpty(withMid: true);
            f.EndPlay(play);
            f.ConfirmDeaths();

            f.Backend.Cut.Busy = true;
            f.Owner.PumpControlledTweenRetirementForTests();
            Check(!f.Owner.CanRetireWork() && !f.Owner.Unknown && f.Backend.Cut.Acquires == 1 && f.Backend.Cut.Commits == 0);

            var typedInvocations = (System.Collections.IList)typeof(NativeLocalTweenWorkOwner)
                .GetField("_typedInvocations", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f.Owner)!;
            var invocation = typedInvocations[0]!;
            invocation.GetType().GetField("EmptyReceipt", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(invocation, null);

            f.Backend.Cut.Busy = false;
            f.Owner.PumpControlledTweenRetirementForTests();
            Check(!f.Owner.CanRetireWork() && f.Owner.Unknown && f.Backend.Cut.Commits == 0
                && f.Backend.Frees == 0 && !f.Roots.Retired);
        }
    }

    private sealed class RetirementFixture : IDisposable
    {
        private static readonly IntPtr PlayMethod = new(700);
        internal readonly MainThreadDispatcher Main = new();
        internal readonly BackendStub Backend;
        internal readonly NativeLocalTweenWorkOwner Owner;
        internal readonly NativeTweenSource Reader;
        internal readonly NativeTweenSourceRootSet Roots;
        private readonly JournalStub _journal = new();
        private ulong _sequence, _transaction;

        internal RetirementFixture(int tweenType = 12, long[]? playing = null, int working = 0)
        {
            Backend = new(Main, tweenType, playing, working);
            Owner = NativeLocalTweenWorkOwner.CaptureBackend(Main, Backend);
            Owner.CaptureControlledTweenDeathSourcesForTests();
            Reader = NativeTweenSource.CreateControlledForTests(_journal, Main.VerifyAccess, Backend.RetainRoot, (r, _) => r.Target);
            Roots = Reader.RetainRootedObjects(Owner, new[] {
                new NativeTweenSourceRootReference(Backend.RetainRoot(new(101)), NativeTweenSourceObjectKind.Tween),
                new NativeTweenSourceRootReference(Backend.RetainRoot(new(201)), NativeTweenSourceObjectKind.Manager),
                new NativeTweenSourceRootReference(Backend.RetainRoot(new(301)), NativeTweenSourceObjectKind.ScheduledList),
                new NativeTweenSourceRootReference(Backend.RetainRoot(new(302)), NativeTweenSourceObjectKind.ReadyStack),
                new NativeTweenSourceRootReference(Backend.RetainRoot(new(303)), NativeTweenSourceObjectKind.TweenArray)
            });
            Owner.AttachSourceJournal(Reader, Roots);
        }

        internal NativeLocalTweenWorkOwner.OperationContext BeginPlay() => Begin(NativeTweenOperation.Play);

        internal NativeLocalTweenWorkOwner.OperationContext Begin(NativeTweenOperation operation, long caller = 900)
            => Owner.CaptureOperation(operation, new() {
                Version = 1, Size = 64, Kind = TweenOperationProfile.Get(operation).Kind,
                Self = new(operation == NativeTweenOperation.Play ? 101L : 201L),
                Arg0 = operation is NativeTweenOperation.ExecuteTween or NativeTweenOperation.AddTweenToWaitingList ? new(101L) : IntPtr.Zero,
                Method = operation == NativeTweenOperation.Play ? PlayMethod : new(700 + (int)operation),
                Caller = new(caller), Sequence = ++_sequence
            })!;

        internal void EndPlay(NativeLocalTweenWorkOwner.OperationContext ctx) => End(ctx);

        internal void End(NativeLocalTweenWorkOwner.OperationContext ctx, NativeOperationExitFlags flags = NativeOperationExitFlags.Returned)
        { Owner.CompleteOperation(ctx, ctx.Frame, flags); ctx.Dispose(); }

        internal void EndUpdate(NativeLocalTweenWorkOwner.OperationContext ctx, long[] playing, int working)
        { Backend.SetPlaying(playing, working); End(ctx); }

        internal NativeTweenSourceRecord Before(NativeLocalTweenWorkOwner.OperationContext context, NativeTweenSourceSite site,
            long container, long array = 0, int source = -1, int destination = -1, long prior = 0, int count = -1, int version = -1)
        {
            var transaction = ++_transaction;
            return new() {
                Version = 1, Size = 192, Site = site, Stage = NativeTweenSourceStage.Before,
                Flags = NativeTweenSourceFlags.Known | NativeTweenSourceFlags.ParentMatched,
                ThreadId = NativeEngineInlinePlatform.CurrentOsThreadId, Depth = 1,
                Operation = context.Operation == NativeTweenOperation.Play ? NativeTweenSourceOperation.Play
                    : context.Operation == NativeTweenOperation.Update ? NativeTweenSourceOperation.Update : NativeTweenSourceOperation.Execute,
                Epoch = 7, UpdateSequence = context.Frame.Sequence, ParentUpdateSequence = 0,
                Transaction = transaction, Occurrence = transaction,
                SourcePc = new(0x100000 + (long)site * 32), RelayReturnPc = new(0x200000 + (long)site * 32),
                Manager = context.Operation == NativeTweenOperation.Play ? IntPtr.Zero : new(201),
                Container = new(container), ArrayBefore = new(array), ArrayAfter = new(array),
                Tween = new(101), Prior = new(prior), SourceIndex = source, DestinationIndex = destination,
                CountBefore = count, CountAfter = count, CapacityBefore = (uint)site <= 12 ? 4 : -1, CapacityAfter = (uint)site <= 12 ? 4 : -1,
                VersionBefore = version, VersionAfter = version
            };
        }

        internal void Publish(NativeTweenSourceRecord row) { _journal.Add(row); Owner.PollSourceJournal(); }

        internal void Clear(NativeLocalTweenWorkOwner.OperationContext update, int index)
        {
            var before = Before(update, NativeTweenSourceSite.PlayingTailClear, 303, 303, source: index, prior: 101, count: 4, version: 0);
            Publish(before);
            var after = before; after.Stage = NativeTweenSourceStage.After; after.Flags |= NativeTweenSourceFlags.Paired;
            Publish(after);
        }

        internal void InvokeEmpty(bool withMid, bool mutatePost = false, bool incrementWriter = false, bool foreignChild = false)
            => Backend.InvokeEmpty(withMid, mutatePost, incrementWriter, foreignChild);

        internal void InvokeNonEmpty(uint site) => Backend.InvokeNonEmpty(site);

        internal void ConfirmDeaths()
        {
            Owner.RequestControlledTweenDeathReturnForTests();
            Backend.Lease.Seal();
            Owner.PumpControlledTweenDeathsForTests();
            Backend.Kill(11); Owner.PumpControlledTweenDeathsForTests();
            Backend.Kill(12); Owner.PumpControlledTweenDeathsForTests();
        }

        public void Dispose() => Main.Dispose();

        internal sealed class BackendStub : INativeLocalTweenBackend, INativeLocalTweenDeathBackend, INativeTweenAdmissionOwner,
            INativeTweenTypedEventSource, INativeTweenTargetDeathBackend, INativeLocalTweenRetirementBackend,
            INativeTweenTypedEventGraphSource, INativeTweenTypedListenerEffectBackend
        {
            private readonly MainThreadDispatcher _main;
            private readonly Dictionary<uint, IntPtr> _handles = new();
            private readonly List<NativeRuntime.RootHandle> _ownedHandles = new();
            private readonly NativeRuntime.RootHandle _tween, _event;
            private readonly NativeTweenAdmission _admission;
            private NativeLocalTweenWorkOwner _owner = null!;
            private Func<NativeOperationFrame, IDisposable?>? _capture;
            private Action<IDisposable, NativeOperationFrame, NativeOperationExitFlags>? _complete;
            private uint _nextHandle;
            private ulong _typedSeq = 800;
            private bool _withdrawn;

            internal readonly AdmissionTransport Admission = new();
            internal readonly CutTransport Cut;
            internal NativeTweenAdmissionLease Lease = null!;
            internal NativeTweenWorkGraph WorkGraph;
            internal NativeTweenDeathObservation TweenObs = new(101, new(222), new(333), 444, true, 10);
            internal NativeSingleEventGraph EventGraph;
            internal NativeTweenTypedPlayConsumer? PlayConsumer;
            internal long WriterGen;
            internal ExactHookBroker.ManagedBoundaryActivity Activity = new(0, true);
            internal ExactHookBroker.ManagedBoundaryActivity TypedActivity = new(0, true);
            internal int Frees;
            public bool CleanupUnknown => false;
            public int RootRecords => _handles.Count;
            public IntPtr PlayMethodInfo => PlayMethod;
            public bool TweenBackendRootsReleased { get; private set; }

            internal BackendStub(MainThreadDispatcher main, int tweenType, long[]? playing = null, int working = 0)
            {
                _main = main;
                Cut = new(this);
                _tween = RetainRoot(new(101));
                _event = RetainRoot(new(107));
                var info = new NativeTweenInfoObservation(101, 222, 0, 0, 105, 106, 107,
                    0, 0, 0, 0, tweenType, false, false, true,
                    new(false, false, 0, "", 0, 0, new(0, 0), new(0, 0, 0), new(0, 0, 0, 0), new(0, 0, 0, 0), new(0, 0, 0, 0), new(0, 0), new(0, 0, 0)));
                WorkGraph = new(0, Array.Empty<NativeTweenDelegateObservation>(), new[] { info },
                    new[] { new NativeTweenManagerObservation(201, 202, 1, 4, working,
                        new(303, 303, 403, working, 0, playing ?? new long[4]),
                        new(301, 401, 501, 0, 1, new long[4]),
                        new(302, 402, 502, 0, 1, new long[4])) });
                EventGraph = new(107, 1101, 1102, false, false,
                    new(107, 3010, 3012, 0, 1, Array.Empty<long>()),
                    new(107, 3011, 3013, 0, 1, Array.Empty<long>()),
                    new(107, 3012, 3014, 0, 1, Array.Empty<long>()),
                    new(107, 3001, 3011, 0, 1, Array.Empty<long>()));
                _admission = NativeTweenAdmission.CreateControlledForTests(Admission, new() {
                    Version = 1, Size = 32, ExactTweenClass = new(222), CachedPointerField = new(333), CachedPointerOffset = 16
                }, main.VerifyAccess, RetainRoot, r => r.Target);
            }

            internal NativeRuntime.RootHandle RetainRoot(IntPtr id)
            {
                var key = ++_nextHandle;
                _handles.Add(key, id);
                var handle = new NativeRuntime.RootHandle(key, k => { _handles.Remove(k); ++Frees; }, k => _handles.GetValueOrDefault(k), _main.VerifyAccess);
                _ownedHandles.Add(handle);
                return handle;
            }

            internal void SetEventRootTarget(IntPtr target)
            {
                var id = (uint)typeof(NativeRuntime.RootHandle)
                    .GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(_event)!;
                _handles[id] = target;
            }

            internal void SetPlaying(long[] values, int count)
            {
                var m = WorkGraph.Managers.Single();
                WorkGraph = WorkGraph with { Managers = new[] { m with { WorkingCount = count, Playing = m.Playing! with { Count = count, CapacityItems = values } } } };
            }

            internal void Kill(int frame) { TweenObs = TweenObs with { Frame = frame, Alive = false, CachedNative = 0 }; Admission.Current.CachedNative = IntPtr.Zero; }

            internal void InvokeEmpty(bool withMid, bool mutatePost, bool incrementWriter, bool foreignChild)
            {
                var seq = ++_typedSeq;
                var frame = new NativeOperationFrame { Version = 1, Size = 64, Kind = NativeOperationKind.Single1, Self = new(107), Method = new(900), Caller = new(0x180395c07), Sequence = seq, Arg0 = new(0x7f800001) };
                TypedActivity = new(1, true);
                var ctx = _capture!(frame)!;
                if (withMid)
                {
                    var mid = new NativeTweenSingleEventFrame { Version = 1, Size = 200, Stage = NativeTweenSingleEventStage.Mid, Site = 3, ThreadId = NativeEngineInlinePlatform.CurrentOsThreadId, Epoch = 9, ParentEpoch = 11, ParentManagedToken = 13, ParentSequence = seq, Event = new(107), List = new(3001), ParentMethod = frame.Method, ParentCaller = frame.Caller, ValueBits = frame.ScalarBits, SourceRsp = 0x10000 };
                    _owner.CaptureTypedExecutingList(ctx, mid, new(107, 3001, 3011, 0, 1, Array.Empty<long>()));
                }
                if (mutatePost) EventGraph = EventGraph with { RuntimeCalls = EventGraph.RuntimeCalls with { Version = 2 } };
                if (incrementWriter) ++WriterGen;
                if (foreignChild)
                {
                    var foreign = new NativeOperationFrame { Version = 1, Size = 64, Kind = NativeOperationKind.Single1, Self = new(9999), Method = new(900), Caller = new(0x180395c07), Sequence = ++_typedSeq, Arg0 = new(0x3f800000) };
                    Check(_capture!(foreign) is null);
                }
                _complete!(ctx, frame, NativeOperationExitFlags.Returned);
                ctx.Dispose();
                TypedActivity = new(0, true);
            }

            internal void InvokeNonEmpty(uint site)
            {
                var seq = ++_typedSeq;
                var list = new NativeSingleExecutingList(107, 3001, 3011, 1, 1, new[] { 4001L });
                EventGraph = EventGraph with { RuntimeCalls = list, ExecutingCalls = list };
                var frame = new NativeOperationFrame { Version = 1, Size = 64, Kind = NativeOperationKind.Single1, Self = new(107), Method = new(900), Caller = new(0x180395c07), Sequence = seq, Arg0 = new(0xffa00001) };
                TypedActivity = new(1, true);
                var ctx = _capture!(frame)!;
                var mid = new NativeTweenSingleEventFrame { Version = 1, Size = 200, Stage = NativeTweenSingleEventStage.Mid, Site = 3, ThreadId = NativeEngineInlinePlatform.CurrentOsThreadId, Epoch = 9, ParentEpoch = 11, ParentManagedToken = 13, ParentSequence = seq, Event = new(107), List = new(3001), ParentMethod = frame.Method, ParentCaller = frame.Caller, ValueBits = frame.ScalarBits, SourceRsp = 0x10000 };
                _owner.CaptureTypedExecutingList(ctx, mid, list);
                var before = new NativeTweenSingleEventFrame { Version = 1, Size = 200, Stage = NativeTweenSingleEventStage.Before, Site = site, ThreadId = NativeEngineInlinePlatform.CurrentOsThreadId, Epoch = 9, ParentEpoch = 11, ParentManagedToken = 13, ParentSequence = seq, Event = new(107), List = new(3001), ParentMethod = frame.Method, ParentCaller = frame.Caller, ValueBits = frame.ScalarBits, ChildSequence = 201, Index = 0, Invokable = new(4001), ActualTarget = new(0x181550000), ArgRcx = new(4001), ArgRdx = site == 2 ? new(7201) : IntPtr.Zero, ArgR8 = new(6001), SourceRsp = 0x10000 };
                var target = new NativeSingleDispatchTarget(site, 4001, before.ActualTarget.ToInt64(), 6001, before.ValueBits, site == 0 ? before.ValueBits : null, false, site == 2 ? 7201L : 0L, site == 2 ? new[] { 7202L } : Array.Empty<long>(), "non-empty", site == 0 ? 7101L : 0L);
                _owner.CaptureTypedDispatch(ctx, before, list, target);
                var ret = before; ret.Stage = NativeTweenSingleEventStage.Returned;
                _owner.CompleteTypedDispatch(ctx, ret);
                _complete!(ctx, frame, NativeOperationExitFlags.Returned);
                ctx.Dispose();
                TypedActivity = new(0, true);
            }

            internal void InvokeNonEmptySite1Play(Action listenerBody)
            {
                var seq = ++_typedSeq;
                var list = new NativeSingleExecutingList(107, 3001, 3011, 1, 1, new[] { 4001L });
                EventGraph = EventGraph with { RuntimeCalls = list, ExecutingCalls = list };
                var frame = new NativeOperationFrame { Version = 1, Size = 64, Kind = NativeOperationKind.Single1, Self = new(107), Method = new(900), Caller = new(0x180395c07), Sequence = seq, Arg0 = new(0x3f800000) };
                TypedActivity = new(1, true);
                var ctx = _capture!(frame)!;
                var mid = new NativeTweenSingleEventFrame { Version = 1, Size = 200, Stage = NativeTweenSingleEventStage.Mid, Site = 3, ThreadId = NativeEngineInlinePlatform.CurrentOsThreadId, Epoch = 9, ParentEpoch = 11, ParentManagedToken = 13, ParentSequence = seq, Event = new(107), List = new(3001), ParentMethod = frame.Method, ParentCaller = frame.Caller, ValueBits = frame.ScalarBits, SourceRsp = 0x10000 };
                _owner.CaptureTypedExecutingList(ctx, mid, list);
                var before = new NativeTweenSingleEventFrame { Version = 1, Size = 200, Stage = NativeTweenSingleEventStage.Before, Site = 1, ThreadId = NativeEngineInlinePlatform.CurrentOsThreadId, Epoch = 9, ParentEpoch = 11, ParentManagedToken = 13, ParentSequence = seq, Event = new(107), List = new(3001), ParentMethod = frame.Method, ParentCaller = frame.Caller, ValueBits = frame.ScalarBits, ChildSequence = 201, Index = 0, Invokable = new(4001), ActualTarget = new(0x18154e5b0), ArgRcx = new(4001), ArgRdx = IntPtr.Zero, ArgR8 = IntPtr.Zero, SourceRsp = 0x10000 };
                var target = new NativeSingleDispatchTarget(1, 4001, before.ActualTarget.ToInt64(), 6001, before.ValueBits, null, false, 0, Array.Empty<long>(), "site1 play");
                _owner.CaptureTypedDispatch(ctx, before, list, target);
                listenerBody();
                var ret = before; ret.Stage = NativeTweenSingleEventStage.Returned;
                _owner.CompleteTypedDispatch(ctx, ret);
                _complete!(ctx, frame, NativeOperationExitFlags.Returned);
                ctx.Dispose();
                TypedActivity = new(0, true);
            }

            public void Initialize(NativeLocalTweenWorkOwner owner) { _owner = owner; owner.RegisterSource(101); Lease = _admission.Claim(_tween, owner); owner.AdoptTypedEvents(this); }
            public NativeTweenWorkGraph Capture(NativeTweenOperation operation, NativeOperationFrame frame, bool after) => WorkGraph;
            public NativeTweenWorkGraph Refresh() => WorkGraph;
            public ExactHookBroker.ManagedBoundaryActivity ReadActivity() => Activity;
            public void VerifyTweenAdmissionOwner() => _main.VerifyAccess();
            public void VerifyTweenDeathOwner(NativeLocalTweenWorkOwner owner) => Check(ReferenceEquals(owner, _owner));
            public void BorrowTweenDeathSources(NativeLocalTweenWorkOwner owner, Action<NativeRuntime.RootHandle, NativeTweenAdmissionLease> borrow) => borrow(_tween, Lease);
            public NativeTweenDeathObservation ReadTweenDeathObservation(NativeRuntime.RootHandle root) => TweenObs;
            public void BorrowTypedTweenEvents(NativeLocalTweenWorkOwner owner, Action<NativeRuntime.RootHandle, NativeExactReadField, NativeRuntime.RootHandle?> adopt)
                => adopt(_tween, new(new(0x98), new(11), new(12), new(13), "onTween", "UnityEngine.Events.UnityEvent<System.Single>", false, 0x98, 8, 0), _event);
            public void BindTypedTweenEvents(NativeLocalTweenWorkOwner owner, int processCapacity, Func<NativeOperationFrame, IDisposable?> capture, Action<IDisposable, NativeOperationFrame, NativeOperationExitFlags> complete, Action<NativeOperationFrame, string> fault)
                => (_capture, _complete) = (capture, complete);
            public NativeTweenTargetBinding BorrowTweenTargetBinding(NativeLocalTweenWorkOwner owner, NativeRuntime.RootHandle tween) => new(_tween, null, null, null, 0);
            public NativeTweenTargetDeathObservation ReadTweenTargetDeath(NativeLocalTweenWorkOwner owner, NativeRuntime.RootHandle target) => throw new NotSupportedException();
            public void VerifyTweenTargetBinding(NativeLocalTweenWorkOwner owner, NativeTweenTargetBinding binding) { }
            public void VerifyTweenCompletionBindingsWithdrawn(NativeLocalTweenWorkOwner owner) => Check(_withdrawn);
            public void VerifyTweenRetirementOwner(NativeLocalTweenWorkOwner owner) => Check(ReferenceEquals(owner, _owner));
            public NativeTweenAdmission.RetirementBatch PrepareTweenRetirement(NativeLocalTweenWorkOwner owner) => _admission.PrepareRetirement(owner);
            public NativeTweenRetirementContext BindTweenRetirementContext(NativeLocalTweenWorkOwner owner) => NativeTweenRetirementContext.CreateControlledForTests(Cut, owner, _main.VerifyAccess);
            public void WithdrawTweenObservationBindings(NativeLocalTweenWorkOwner owner) => _withdrawn = true;
            public void ReleaseTweenBackendRoots(NativeLocalTweenWorkOwner owner, NativeTweenRetirementContext.Cut cut)
            {
                foreach (var handle in _ownedHandles) { cut.VerifyCurrent(); handle.Dispose(); }
                TweenBackendRootsReleased = true;
            }
            public NativeSingleEventGraph ReadTypedEventGraph(NativeLocalTweenWorkOwner owner, NativeRuntime.RootHandle eventRoot) => EventGraph;
            public long ReadTypedEventWriterGeneration(NativeLocalTweenWorkOwner owner, NativeRuntime.RootHandle eventRoot) => WriterGen;
            public ExactHookBroker.ManagedBoundaryActivity ReadTypedEventActivity() => TypedActivity;
            NativeTweenTypedPlayConsumer? INativeTweenTypedListenerEffectBackend.ReadTypedPlayConsumer(
                NativeLocalTweenWorkOwner owner, IDisposable context, NativeTweenSingleEventFrame frame, NativeSingleDispatchTarget target)
            {
                owner.RequireTypedListenerEffectRead(this, context, frame);
                return PlayConsumer is { } consumer && consumer.Invokable == target.Invokable ? consumer : null;
            }
        }

        internal sealed class AdmissionTransport : INativeTweenAdmissionTransport
        {
            internal NativeTweenAdmissionReceipt Current = new() { Version = 1, Size = 96, Slot = 3, Epoch = 41, Phase = NativeTweenAdmissionPhase.Open, Object = new(101), ActualClass = new(222), CachedNative = new(444), AdmittedIssuers = 1 };
            public uint Configure(ref NativeTweenAdmissionConfiguration c) => 0;
            public uint GetInfo(ref NativeTweenAdmissionInformation info) { info = new() { Version = 1, Size = 104, Configured = 1, ExactTweenClass = new(222), CachedPointerField = new(333), CachedPointerOffset = 16, Capacity = 128, ConfigurationEpoch = 7 }; return 0; }
            public uint Claim(IntPtr v, ref NativeTweenAdmissionReceipt r) { r = Current; return 0; }
            public uint Read(uint s, ulong e, ref NativeTweenAdmissionReceipt r) { r = Current; return 0; }
            public uint Seal(uint s, ulong e, ref NativeTweenAdmissionReceipt r) { Current.Phase = NativeTweenAdmissionPhase.Sealed; r = Current; return 0; }
            public uint Unregister(uint s, ulong e, ref NativeTweenAdmissionReceipt r) => 0;
        }

        internal sealed class CutTransport : INativeTweenRetirementContextTransport
        {
            private readonly BackendStub _b;
            private NativeTweenRetirementCutReceipt _current;
            private bool _held;
            private ulong _gen;
            internal bool Busy;
            internal int Acquires, Commits, Releases;
            internal CutTransport(BackendStub b) => _b = b;
            public uint Acquire(ref NativeTweenRetirementRequest req, NativeTweenRetirementClaim[] claims, ref NativeTweenRetirementCutReceipt cut)
            {
                ++Acquires;
                if (Busy) return 3;
                cut = new() { Version = 1, Size = 88, ThreadId = NativeTweenSourceReferences.CurrentThreadId(), Phase = 1, Token = ++_gen, Generation = _gen, ConfigurationEpoch = req.ConfigurationEpoch, SourceEpoch = req.SourceEpoch, JournalCutoff = req.JournalCutoff, ReferenceObserverEpoch = req.ReferenceObserverEpoch, ReferenceDeliveryCutoff = req.ReferenceDeliveryCutoff, ReferenceReaderGeneration = req.ReferenceReaderGeneration, ClaimCount = (uint)claims.Length };
                _current = cut; _held = true; return 0;
            }
            public uint Verify(ref NativeTweenRetirementCutReceipt cut) => _held && cut.Equals(_current) ? 0u : 2u;
            public uint VerifyReferenceRoots(ref NativeTweenRetirementCutReceipt cut) => Verify(ref cut);
            public uint UnregisterBatch(ref NativeTweenRetirementCutReceipt cut, NativeTweenRetirementClaim[] claims, NativeTweenAdmissionReceipt[] receipts)
            { ++Commits; _b.Admission.Current.Phase = NativeTweenAdmissionPhase.Retired; receipts[0] = _b.Admission.Current; return 0; }
            public uint Release(ref NativeTweenRetirementCutReceipt cut) { ++Releases; _held = false; return 0; }
        }

        private sealed class JournalStub : INativeTweenSourceTransport
        {
            private readonly List<NativeTweenSourceRecord> _rows = new();
            internal void Add(NativeTweenSourceRecord row) { row.EventSequence = (ulong)_rows.Count + 1; _rows.Add(row); }
            public uint GetInfo(ref NativeTweenSourceInformation info)
            {
                info = new() {
                    Version = 1, Size = 96, Configured = 1, MetadataBound = 1, BindingCount = 43, JournalCapacity = 8192, Epoch = 7,
                    Events = (ulong)_rows.Count,
                    Transactions = _rows.Select(row => row.Transaction).DefaultIfEmpty().Max(),
                    CompletedTransactions = (ulong)_rows.Count(row => row.Stage == NativeTweenSourceStage.After),
                    PendingTransactions = (ulong)(_rows.Count(row => row.Stage == NativeTweenSourceStage.Before) - _rows.Count(row => row.Stage == NativeTweenSourceStage.After)),
                    RetainedRecords = (ulong)_rows.Count
                };
                return 0;
            }
            public uint ReadRecord(ulong epoch, ulong seq, ref NativeTweenSourceRecord row)
            {
                if (epoch != 7 || seq == 0 || seq > (ulong)_rows.Count) return 3;
                row = _rows[checked((int)seq - 1)];
                return 0;
            }
        }
    }
}
