using RizToolbox.Adapter.AudioContent;
using RizToolbox.Adapter.Judgements;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class HoldLifecycleChecks
{
    internal static readonly (string Name, Action Run)[] StopCases =
    {
        ("Hold stop six native entries keep requests separate from natural completion", StopEntries),
        ("Hold stop exact references and receiver identities reject foreign values", StopReferences),
        ("Hold stop nested wrappers preserve ancestry and foreign masks", StopNesting),
        ("Hold stop pause and run retirement retain in-flight source roots", StopRetirement),
        ("Hold stop abnormal and detached calls preserve request cleanup", StopExits),
        ("Hold stop old abnormal original cannot fault a new run on the same receiver", StopOldAbnormal),
        ("Hold stop worker and MethodInfo faults read no native receivers", StopIsolation),
        ("Hold stop failed lease retains its original unknown request owner", StopLeaseExhaustion),
        ("Hold stop all covers 257 actual identities without owner substitution", StopLargePopulation)
    };

    // The real broker owns Enter/Exit, physical ancestry, filtering and cleanup.
    // This controlled bridge supplies only an original window and its flags.
    private sealed class StopBridge : INativeOperationContextBridge
    {
        public NativeOperationKind Kind { get; }
        public IntPtr EntryAddress { get; } = new(Interlocked.Add(ref _addresses, 16));
        private Func<NativeOperationFrame, nuint> _enter = null!;
        private Action<NativeOperationFrame, nuint, NativeOperationExitFlags> _exit = null!;
        internal Action<NativeOperationFrame> Original = _ => { };
        internal bool Configured, SkipOriginal;
        internal long InFlight;
        internal int Enters, Originals, Exits;
        internal StopBridge(NativeOperationKind kind) => Kind = kind;
        public void Configure(IntPtr original, Func<NativeOperationFrame, nuint> enter,
            Action<NativeOperationFrame, nuint, NativeOperationExitFlags> exit)
        {
            Check(!Configured && original != IntPtr.Zero, "A Stop channel must be configured exactly once before publication.");
            (_enter, _exit, Configured) = (enter, exit, true);
        }
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity() => new(Interlocked.Read(ref InFlight), Configured);
        internal void Invoke(NativeOperationFrame frame)
        {
            Check(Configured, "An unconfigured Stop bridge was published.");
            Interlocked.Increment(ref InFlight); nuint token = 0; var flags = NativeOperationExitFlags.Abnormal;
            try
            {
                ++Enters; token = _enter(frame);
                if (SkipOriginal) { flags = NativeOperationExitFlags.Skipped; return; }
                ++Originals; Original(frame); flags = NativeOperationExitFlags.Returned;
            }
            finally
            {
                try { ++Exits; _exit(frame, token, flags); }
                finally { Interlocked.Decrement(ref InFlight); }
            }
        }
    }

    private sealed class StopFixture : IDisposable
    {
        internal readonly Fixture Hold;
        internal readonly Engine StopEngine = new();
        internal readonly Dictionary<CoroutineStopOperation, StopBridge> Bridges = new();
        internal readonly Dictionary<CoroutineStopOperation, VerifiedHookTarget> Targets = new();
        internal readonly Dictionary<CoroutineStopOperation, ExactHookBroker.Binding> Bindings = new();
        private readonly Dictionary<IntPtr, IntPtr> _handles = new();
        private ExactHookBroker _broker = null!;
        private ulong _sequence;
        internal StopFixture()
        {
            Hold = new Fixture(beforeCoroutineBindings: Install);
            Hold.Move.Original = _ => 0xff;
            Hold.Start.Original = frame => unchecked((nuint)_handles[frame.Enumerator].ToInt64());
            Check(Hold.Backend.ReadHoldStops().Installed && StopEngine.Prepares == 6 && StopEngine.Commits == 6,
                "The six Stop channels were not ready before the coroutine transports became usable.");
        }
        private void Install(Fixture hold)
        {
            Check(hold.Engine.Prepares == 0 && hold.Engine.Commits == 0,
                "A Start or Move channel was published before all Stop channels.");
            _broker = new(StopEngine, hold.Main, hold.Log);
            foreach (var declaration in CoroutineStopProfile.All)
            {
                var operation = declaration.Operation; var bridge = new StopBridge(declaration.Kind);
                var target = new VerifiedHookTarget("test.hold.stop." + operation,
                    new(Interlocked.Add(ref _addresses, 16)), new(Interlocked.Add(ref _addresses, 16)), new(0x383),
                    ExactHookBroker.OperationAbi(declaration.Kind));
                Bridges.Add(operation, bridge); Targets.Add(operation, target);
                Bindings.Add(operation, _broker.BindNativeOperationObservation(target, declaration.Kind, () => bridge,
                    frame => hold.Backend.CaptureHoldStop(operation, frame), hold.Backend.CompleteHoldStop,
                    (frame, reason) => hold.Backend.ObserveHoldStopFault(operation, frame, reason)));
            }
            hold.Backend.CompleteHoldStopInstallation();
        }
        internal (IntPtr Iterator, IntPtr Handle) Schedule(int noteOrdinal = 2)
        {
            var iterator = Hold.Native.HoldCreateIterator(noteOrdinal); var handle = Hold.Native.HoldCreateHandle();
            _handles.Add(iterator, handle); Hold.Start.Invoke(Hold.StartFrame(iterator));
            Check(Hold.Snapshot().Rows.Any(row => row.Identity.Enumerator == iterator.ToInt64() && row.StartReturned
                && row.Handle == handle.ToInt64()), "An actual native Start did not publish its iterator and returned handle.");
            return (iterator, handle);
        }
        internal NativeOperationFrame Frame(CoroutineStopOperation operation, IntPtr argument = default, IntPtr? receiver = null)
            => new() { Version = 1, Size = 64, Kind = CoroutineStopProfile.Get(operation).Kind,
                Self = receiver ?? Hold.Native.HoldJudge, Arg0 = argument, Method = Targets[operation].MethodInfo,
                Caller = new(0x12347), Sequence = ++_sequence };
        internal void Invoke(CoroutineStopOperation operation, IntPtr argument = default, IntPtr? receiver = null)
            => Bridges[operation].Invoke(Frame(operation, argument, receiver));
        internal NativeCoroutineCancellationSnapshot Requests(int monitor = 0)
            => Hold.Backend.ReadHoldStops().Monitors[monitor].Requests;
        public void Dispose()
        {
            foreach (var binding in Bindings.Values) binding.Dispose();
            Check(StopEngine.Installs == 0 && StopEngine.Prepares == 6 && StopEngine.Commits == 6
                && Bridges.Values.All(bridge => bridge.InFlight == 0 && bridge.Enters == bridge.Exits),
                "Stop observation bypassed native installation or left an unmatched physical call.");
            Hold.Dispose(); GC.KeepAlive(_broker);
        }
    }

    private static NativeCoroutineCancellationOperation CancellationOperation(CoroutineStopOperation operation) => operation switch
    {
        CoroutineStopOperation.Enumerator => NativeCoroutineCancellationOperation.StopEnumerator,
        CoroutineStopOperation.Handle => NativeCoroutineCancellationOperation.StopHandle,
        CoroutineStopOperation.All => NativeCoroutineCancellationOperation.StopAll,
        CoroutineStopOperation.ManagedHandle => NativeCoroutineCancellationOperation.StopManagedHandle,
        CoroutineStopOperation.ManagedEnumerator => NativeCoroutineCancellationOperation.StopManagedEnumerator,
        CoroutineStopOperation.Named => NativeCoroutineCancellationOperation.StopNamed,
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    private static void StopEntries()
    {
        foreach (var declaration in CoroutineStopProfile.All)
        {
            using var f = new StopFixture(); var first = f.Schedule(2); var second = f.Schedule(0);
            var argument = declaration.Operation switch
            {
                CoroutineStopOperation.Enumerator or CoroutineStopOperation.ManagedEnumerator => first.Iterator,
                CoroutineStopOperation.Handle or CoroutineStopOperation.ManagedHandle => first.Handle,
                CoroutineStopOperation.Named => new IntPtr(unchecked((long)0x8abc000000001234UL)),
                _ => IntPtr.Zero
            };
            f.Invoke(declaration.Operation, argument); var requests = f.Requests(); var row = requests.Requests.Single();
            var expected = declaration.Operation == CoroutineStopOperation.All ? 2
                : declaration.Operation == CoroutineStopOperation.Named ? 0 : 1;
            Check(row.Operation == CancellationOperation(declaration.Operation) && row.Arg0 == argument.ToInt64()
                && row.Before.Count == 2 && row.After.Count == 2 && row.Matched.Count == expected
                && row.MatchUnproven == (declaration.Operation == CoroutineStopOperation.Named)
                && row.NativeCompleted && row.NativeReturned && row.Disposed && !row.Abnormal
                && requests.Outstanding == 0 && requests.Pending && !requests.EngineCancelled && !requests.Unknown
                && f.Bridges[declaration.Operation].Originals == 1,
                "A six-entry Stop request lost exact native facts or fabricated engine completion.");
            var lifecycle = f.Hold.Snapshot();
            Check(lifecycle.NaturallyCompleted == 0 && !lifecycle.FullCoverageKnown && lifecycle.RootResponsibilities > 0
                && lifecycle.Rows.Count(value => value.CancellationRequested) == expected,
                "A returned Stop request guessed an individual named match, natural completion or root release.");
            f.Hold.Move.Invoke(f.Hold.MoveFrame(first.Iterator));
            Check(f.Hold.Snapshot().NaturallyCompleted == 0, "A requested iterator's raw true Move became terminal.");
            f.Hold.Move.Original = _ => 0; f.Hold.Move.Invoke(f.Hold.MoveFrame(first.Iterator));
            Check(f.Hold.Snapshot().Rows.Single(value => value.Identity.Enumerator == first.Iterator.ToInt64()).NaturalCompletion
                && !f.Hold.Snapshot().Rows.Single(value => value.Identity.Enumerator == second.Iterator.ToInt64()).NaturalCompletion
                && !f.Requests().EngineCancelled && f.Hold.Native.NoteViewHandlesFor(first.Iterator).Length > 0,
                "Actual false Move did not remain separate from Stop requests and the other retained iterator.");
        }
    }

    private static void StopReferences()
    {
        using var f = new StopFixture(); var first = f.Schedule(2); var second = f.Schedule(4);
        foreach (var pair in new[] {
            (CoroutineStopOperation.Enumerator, first.Handle), (CoroutineStopOperation.Handle, first.Iterator),
            (CoroutineStopOperation.ManagedEnumerator, IntPtr.Zero), (CoroutineStopOperation.ManagedHandle, new IntPtr(0x5eed1234)) })
        {
            f.Invoke(pair.Item1, pair.Item2); var row = f.Requests().Requests.Last();
            Check(row.Captured && row.MatchUnproven && row.Matched.Count == 0 && row.NativeReturned && row.Disposed,
                "Stop borrowed an unrelated/null reference or confused handle and iterator identity.");
        }
        var observed = f.Requests().ObservedRequests;
        f.Invoke(CoroutineStopOperation.All, receiver: new(0x5eed4321));
        Check(f.Requests().ObservedRequests == observed && f.Hold.Snapshot().Rows.All(row => !row.CancellationRequested),
            "A foreign receiver borrowed the current Hold owner's population.");
        f.Invoke(CoroutineStopOperation.Handle, second.Handle); var exact = f.Requests().Requests.Last();
        Check(exact.Matched.Count == 1 && exact.Matched[0].Enumerator == second.Iterator.ToInt64()
            && exact.Matched[0].ObservedHandle == second.Handle.ToInt64()
            && !f.Hold.Snapshot().Rows.Single(row => row.Identity.Enumerator == first.Iterator.ToInt64()).CancellationRequested
            && f.Hold.Snapshot().Rows.Single(row => row.Identity.Enumerator == second.Iterator.ToInt64()).CancellationRequested,
            "Exact returned-handle matching marked another real NoteInformation iterator.");
    }

    private static void StopNesting()
    {
        using var f = new StopFixture(); var first = f.Schedule();
        var outerFrame = f.Frame(CoroutineStopOperation.Enumerator, first.Iterator);
        f.Bridges[CoroutineStopOperation.All].Original = _ => f.Invoke(CoroutineStopOperation.Handle, first.Handle);
        f.Bridges[CoroutineStopOperation.Enumerator].Original = _ =>
        {
            f.Invoke(CoroutineStopOperation.ManagedEnumerator, first.Iterator);
            f.Invoke(CoroutineStopOperation.All, receiver: new(0x5eed9876));
        };
        f.Bridges[CoroutineStopOperation.Enumerator].Invoke(outerFrame); var requests = f.Requests(); var rows = requests.Requests;
        Check(rows.Count == 3 && requests.PhysicalCalls == 4 && requests.ForeignMasks == 1 && requests.Outstanding == 0
            && requests.ObservedRequests == 3 && requests.NativeReturnedRequests == 3 && !requests.Unknown && requests.Pending
            && !requests.EngineCancelled && rows[0].NativeSequence == outerFrame.Sequence
            && rows.Select(row => row.NativeSequence).Distinct().Count() == 3
            && rows.Skip(1).All(row => row.ParentRequestId == rows[0].RequestId && row.RootRequestId == rows[0].RootRequestId)
            && rows.All(row => row.NativeReturned && row.Disposed && row.Matched.Single().Enumerator == first.Iterator.ToInt64())
            && f.Bridges[CoroutineStopOperation.Enumerator].Originals == 1
            && f.Bridges[CoroutineStopOperation.ManagedEnumerator].Originals == 1
            && f.Bridges[CoroutineStopOperation.All].Originals == 1 && f.Bridges[CoroutineStopOperation.Handle].Originals == 1,
            "Nested managed wrappers or a foreign physical mask borrowed, merged or lost an exact native request.");
        Check(f.Hold.Snapshot().NaturallyCompleted == 0 && f.Hold.Snapshot().Rows.Single().CancellationRequests == 3,
            "Physical Stop ancestry became logical cancel completion or counted a foreign mask as an owned request.");
    }

    private static void StopRetirement()
    {
        using (var f = new StopFixture())
        {
            var first = f.Schedule(); var before = f.Hold.Snapshot().Run!;
            f.Bridges[CoroutineStopOperation.All].Original = _ =>
            {
                f.Hold.State.ObservePause(true); f.Hold.Backend.TickHoldLifecycle();
                Check(f.Hold.Snapshot().InFlight > 0 && f.Hold.Snapshot().Run!.SameGeneration(before)
                    && f.Hold.Snapshot().Run!.Revision > before.Revision && f.Requests().Outstanding == 1
                    && f.Hold.Native.NoteViewHandlesFor(first.Iterator).Length > 0,
                    "A Stop observation did not lease its exact source across a same-generation pause.");
            };
            f.Invoke(CoroutineStopOperation.All);
            Check(!f.Requests().Unknown && f.Requests().Requests.Single().Disposed && f.Hold.Snapshot().InFlight == 0,
                "Pause invalidated a stable provider or escaped its request observation lease.");
        }
        using (var f = new StopFixture())
        {
            var first = f.Schedule(); var old = f.Hold.Snapshot().Run!; (IntPtr Iterator, IntPtr Handle) next = default;
            f.Bridges[CoroutineStopOperation.Enumerator].Original = _ =>
            {
                f.Hold.State.Scene("Play-after-stop");
                f.Hold.State.Controller(f.Hold.Native.GameplayInformation.ToInt64(), true, false,
                    "synthetic-hold-stop-next", f.Hold.Native.GameplayController.ToInt64());
                f.Hold.Backend.TickHoldLifecycle();
                Check(f.Hold.Snapshot().InFlight >= 2 && f.Hold.Native.NoteViewHandlesFor(first.Iterator).Length > 0,
                    "Retirement freed a Stop/Move source before their nested physical originals exited.");
                next = f.Schedule(0);
            };
            f.Hold.Move.Original = _ =>
            {
                f.Invoke(CoroutineStopOperation.Enumerator, first.Iterator);
                Check(f.Hold.SynchronousIterator() == first.Iterator.ToInt64(), "A nested Stop displaced the actual Hold Move receiver.");
                return 0;
            };
            f.Hold.Move.Invoke(f.Hold.MoveFrame(first.Iterator)); var monitors = f.Hold.Backend.ReadHoldStops().Monitors;
            Check(monitors.Count == 2 && monitors[0].Run.SameGeneration(old) && monitors[0].Retired
                && monitors[0].Requests.Unknown && monitors[0].Requests.Requests.Single().Disposed
                && monitors[1].Requests.ObservedRequests == 0 && !monitors[1].Run.SameGeneration(old)
                && f.Hold.Native.NoteViewHandlesFor(first.Iterator).Length == 0
                && f.Hold.Snapshot().Rows.Single(row => row.Identity.Enumerator == next.Iterator.ToInt64()) is
                    { CancellationRequested: false, NaturalCompletion: false },
                "An old Stop completion rebound to the new run, retained known retired roots or cancelled a new identity.");
            f.Invoke(CoroutineStopOperation.Handle, next.Handle); monitors = f.Hold.Backend.ReadHoldStops().Monitors;
            var current = monitors.Single(monitor => !monitor.Run.SameGeneration(old));
            Check(monitors.Single(monitor => monitor.Run.SameGeneration(old)).Requests.ObservedRequests == 1
                && current.Requests.ObservedRequests == 1 && !current.Requests.Unknown
                && current.Requests.Requests.Single().Matched.Count == 1
                && current.Requests.Requests.Single().Matched[0].Enumerator == next.Iterator.ToInt64()
                && current.Requests.Requests.Single().Matched[0].ObservedHandle == next.Handle.ToInt64()
                && !current.Requests.EngineCancelled,
                "An old Stop fault poisoned the new epoch or its exact handle request borrowed another iterator.");
            f.Hold.State.Scene("Play-after-stop-retired-again"); f.Hold.Backend.TickHoldLifecycle();
            var retained = f.Hold.Backend.ReadHoldStops().Monitors.Single(monitor => monitor.Run.SameGeneration(old));
            Check(retained.Retired && retained.Requests.Pending && retained.Requests.Unknown
                && retained.Requests.ObservedRequests == 1 && retained.Requests.Requests.Single().Disposed
                && !retained.Requests.EngineCancelled && f.Hold.Snapshot().RootResponsibilities == 0,
                "A retirement Tick discarded the original pending/unknown Stop monitor after its known native roots drained.");
        }
    }

    private static void StopExits()
    {
        using (var f = new StopFixture())
        {
            var first = f.Schedule(); var failure = new IOException("Controlled abnormal Stop original.");
            f.Bridges[CoroutineStopOperation.Handle].Original = _ => throw failure;
            Check(ReferenceEquals(Expect<IOException>(() => f.Invoke(CoroutineStopOperation.Handle, first.Handle)), failure),
                "A Stop observer replaced or swallowed the actual original exception.");
            var requests = f.Requests(); var row = requests.Requests.Single();
            Check(requests.Unknown && requests.Pending && !requests.EngineCancelled && requests.Outstanding == 0
                && row.NativeCompleted && row.Abnormal && !row.NativeReturned && row.Disposed
                && f.Hold.Snapshot().NaturallyCompleted == 0 && f.Hold.Snapshot().RootResponsibilities > 0,
                "An abnormal Stop lost cleanup responsibility or fabricated engine/natural completion.");
        }
        using (var f = new StopFixture())
        {
            var first = f.Schedule(); f.Bridges[CoroutineStopOperation.Enumerator].SkipOriginal = true;
            f.Invoke(CoroutineStopOperation.Enumerator, first.Iterator); var row = f.Requests().Requests.Single();
            Check(f.Requests().Unknown && row.NativeCompleted && row.Disposed && !row.NativeReturned && !row.Abnormal
                && row.Flags == (uint)NativeOperationExitFlags.Skipped && f.Bridges[CoroutineStopOperation.Enumerator].Originals == 0
                && f.Hold.Snapshot().NaturallyCompleted == 0, "A skipped request earned returned-original or terminal credit.");
        }
        using (var f = new StopFixture())
        {
            var first = f.Schedule(); f.Bridges[CoroutineStopOperation.Enumerator].Original = _ =>
            {
                f.Bindings[CoroutineStopOperation.Enumerator].Dispose();
                Check(f.Requests().Outstanding == 1 && f.Hold.Snapshot().InFlight > 0
                    && f.Hold.Native.NoteViewHandlesFor(first.Iterator).Length > 0,
                    "Logical Stop detach released its captured request or live source.");
            };
            f.Invoke(CoroutineStopOperation.Enumerator, first.Iterator); var row = f.Requests().Requests.Single();
            Check(row.NativeCompleted && row.NativeReturned && row.Disposed && !f.Requests().Unknown
                && f.Requests().Outstanding == 0 && f.Hold.Snapshot().InFlight == 0
                && f.Hold.Snapshot().NaturallyCompleted == 0,
                "A detached Operation sink skipped its already captured completion/disposal duty.");
        }
    }

    private static void StopOldAbnormal()
    {
        using var f = new StopFixture(); var first = f.Schedule(); var old = f.Hold.Snapshot().Run!;
        var failure = new IOException("Old Stop failed after a new run started.");
        (IntPtr Iterator, IntPtr Handle) next = default;
        f.Bridges[CoroutineStopOperation.Enumerator].Original = _ =>
        {
            f.Hold.State.Scene("Play-after-abnormal-old-stop");
            f.Hold.State.Controller(f.Hold.Native.GameplayInformation.ToInt64(), true, false,
                "synthetic-next-run-abnormal-stop", f.Hold.Native.GameplayController.ToInt64());
            f.Hold.Backend.TickHoldLifecycle(); next = f.Schedule(0);
            throw failure;
        };
        Check(ReferenceEquals(Expect<IOException>(() => f.Invoke(CoroutineStopOperation.Enumerator, first.Iterator)), failure),
            "An old Stop exception was replaced by its observer.");
        var monitors = f.Hold.Backend.ReadHoldStops().Monitors;
        var retired = monitors.Single(monitor => monitor.Run.SameGeneration(old));
        var current = monitors.Single(monitor => !monitor.Run.SameGeneration(old));
        Check(monitors.Count == 2 && retired.Retired && retired.Requests.Unknown && retired.Requests.Pending
            && retired.Requests.Requests.Single() is { Abnormal: true, NativeReturned: false, Disposed: true }
            && !current.Requests.Unknown && current.Requests.ObservedRequests == 0,
            "An old physical original's transport fault was broadcast to the new epoch sharing its receiver.");
        f.Invoke(CoroutineStopOperation.Handle, next.Handle);
        current = f.Hold.Backend.ReadHoldStops().Monitors.Single(monitor => !monitor.Run.SameGeneration(old));
        Check(!current.Requests.Unknown && current.Requests.Requests.Single().Matched.Count == 1
            && current.Requests.Requests.Single().Matched[0].Enumerator == next.Iterator.ToInt64()
            && current.Requests.Requests.Single().Matched[0].ObservedHandle == next.Handle.ToInt64()
            && !current.Requests.EngineCancelled,
            "The new run lost its exact handle match after the old original faulted.");
    }

    private static void StopIsolation()
    {
        foreach (var worker in new[] { true, false })
        {
            using var f = new StopFixture(); f.Schedule(); var frame = f.Frame(CoroutineStopOperation.All);
            if (!worker) frame.Method = new(0x456789);
            f.Hold.Native.BeginLowerIl2CppAudit();
            try
            {
                if (worker) Task.Run(() => f.Bridges[CoroutineStopOperation.All].Invoke(frame)).GetAwaiter().GetResult();
                else f.Bridges[CoroutineStopOperation.All].Invoke(frame);
            }
            finally { f.Hold.Native.EndLowerIl2CppAudit(); }
            Check(f.Hold.Native.LowerIl2CppCalls == 0 && f.Hold.Native.LowerWorkerIl2CppCalls == 0
                && f.Bridges[CoroutineStopOperation.All].Originals == 1 && f.Requests().ObservedRequests == 0
                && f.Hold.Snapshot().Unknown && f.Hold.Snapshot().NaturallyCompleted == 0,
                "Worker or foreign MethodInfo fault inspected native receivers, captured a request or skipped its original.");
        }
    }

    private static void StopLeaseExhaustion()
    {
        using var f = new StopFixture(); var first = f.Schedule();
        var provider = f.Hold.Backend.HoldLifecycle.GetCancellationProvider(f.Hold.Snapshot().Run!,
            f.Hold.Native.HoldJudge.ToInt64());
        var leases = new List<IDisposable>();
        try
        {
            for (var index = 0; index < HoldCoroutineLifecycle.MaximumInFlight; ++index)
                leases.Add(provider.AcquireObservation());
            f.Invoke(CoroutineStopOperation.Handle, first.Handle);
            var requests = f.Requests();
            Check(requests.Unknown && requests.Pending && requests.ObservedRequests == 0
                && requests.Requests.Count == 0 && !requests.EngineCancelled
                && f.Bridges[CoroutineStopOperation.Handle].Originals == 1
                && f.Hold.Snapshot().InFlight == leases.Count,
                "A failure before Entries.Add lost its unknown ledger or borrowed a physical lease.");
        }
        finally
        {
            for (var index = leases.Count - 1; index >= 0; --index) leases[index].Dispose();
        }
        f.Hold.State.Scene("Play-after-failed-stop-lease"); f.Hold.Backend.TickHoldLifecycle();
        var monitor = f.Hold.Backend.ReadHoldStops().Monitors.Single();
        Check(monitor.Retired && !monitor.HasResponsibility && monitor.Requests.Pending && monitor.Requests.Unknown
            && monitor.Requests.ObservedRequests == 0 && !monitor.Requests.EngineCancelled
            && f.Hold.Snapshot().RootResponsibilities == 0 && f.Hold.Snapshot().InFlight == 0,
            "The original failed-capture monitor was pruned after all actual source roots drained.");
    }

    private sealed class EqualStopOwner
    {
        private readonly object _owner;
        internal EqualStopOwner(object owner) => _owner = owner;
        public override bool Equals(object? other) => ReferenceEquals(other, _owner) || ReferenceEquals(other, this);
        public override int GetHashCode() => _owner.GetHashCode();
    }
    private static void StopLargePopulation()
    {
        using var f = new StopFixture();
        for (var index = 0; index < 257; ++index) f.Schedule(index % f.Hold.Native.FlagNotes.Count);
        f.Invoke(CoroutineStopOperation.All); var requests = f.Requests(); var row = requests.Requests.Single();
        var run = f.Hold.Snapshot().Run!;
        var provider = f.Hold.Backend.HoldLifecycle.GetCancellationProvider(run, f.Hold.Native.HoldJudge.ToInt64());
        Check(row.Before.Count == 257 && row.Matched.Count == 257 && row.After.Count == 257 && !requests.Unknown
            && row.Matched.Select(identity => identity.Enumerator).Distinct().Count() == 257
            && row.Matched.Select(identity => identity.ObservedHandle).Distinct().Count() == 257
            && row.Matched.All(identity => ReferenceEquals(identity.Owner, provider.ScalarProvider.Owner)
                && identity.HandleConfirmed && identity.StartReturned && provider.ScalarProvider.IsCurrent(identity))
            && f.Hold.Snapshot().Rows.Count == 257 && f.Hold.Snapshot().Rows.All(value => value.CancellationRequests == 1)
            && f.Hold.Snapshot().NaturallyCompleted == 0 && !requests.EngineCancelled,
            "StopAll truncated a real Hold population at the old 256 limit or substituted identity owners.");
        var original = row.Matched[0]; var substituted = new EqualStopOwner(original.Owner);
        var forged = original with { Owner = substituted };
        var before = f.Hold.Snapshot().Rows.Sum(value => value.CancellationRequests);
        Check(substituted.Equals(original.Owner) && !ReferenceEquals(substituted, original.Owner)
            && !provider.ScalarProvider.IsCurrent(forged), "A value-equal owner passed an exact provider identity check.");
        Expect<InvalidOperationException>(() => provider.ObserveCancellation(forged));
        Check(f.Hold.Snapshot().Unknown && f.Hold.Snapshot().Rows.Sum(value => value.CancellationRequests) == before,
            "A foreign owner reference gained cancellation credit by equality or changed the actual population.");
    }
}
