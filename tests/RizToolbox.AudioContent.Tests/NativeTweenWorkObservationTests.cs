using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;
using static Assert;

// Controlled graph observations through the real ExactHookBroker operation
// callbacks and production Tween owner. The Runtime suite separately exercises
// official field/array publication. No game method, gate or receipt is invented.
internal static partial class NativeTweenWorkObservationTests
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Tween production owner consumes actual broker entry and normal After around an ordered queue append", OrderedAppend),
        ("Tween repeated same-target appends retain separate admission sequences and mark later attribution ambiguous", DuplicateAdmissions),
        ("Tween queue append cannot be inferred from a matching total count after reordered members", ReorderedAppend),
        ("Tween isOver false actual Add no-op retains its native operation without inventing a work item", AlreadyPlaying),
        ("Tween onComplete-style nested Play Add and Execute retain physical parents and independent histories", CompletionReentry),
        ("Tween failed Before retains completion duty and does not reread after authority becomes Unknown", BeforeFailure),
        ("Tween failed final graph keeps normal original return distinct from unconfirmed work transition", AfterFailure),
        ("Tween abnormal original exit has no fabricated After graph or completion proof", AbnormalOriginal),
        ("Tween full capacity snapshots preserve duplicate and higher playing slots beyond stale working count", CompleteCapacity),
        ("Tween host pump observes manager generation changes while preserving earlier work and graphs", ManagerGeneration),
        ("Tween a normal Execute followed by absence or source data loss does not seal future admission", AbsenceDoesNotSeal),
        ("Tween wrong native completion identity cannot dispose an owned physical generation", WrongCompletion),
        ("Tween unrelated shared-manager Update frames do not exhaust local work history after its actual slots drain", ForeignUpdateScope),
        ("Tween actual paired graph equality includes fixedStart and all captured effect-value float bits", EffectValueEquality),
        ("Tween production Capture retains both published inputs before its first Target can fail", ProductionInputAdoption),
        ("Tween three native skipped lanes finish only their exact scalar calls without original After or admission", SkippedOriginal),
        ("Tween skipped Execute retains existing queue records and does not issue a work completion", SkippedExecuteRetainsWork),
        ("Tween skipped nested Add preserves its Play parent without confirming a queue append", SkippedNestedAdd),
        ("Tween controlled observations and copied sealed snapshots cannot confer production admission authority", ControlledSealHasNoAuthority)
    };
    private static void OrderedAppend()
    {
        using var f = new Fixture(); var before = f.Owner.Read().Current!;
        f.Call(NativeTweenOperation.Play, () => f.Call(NativeTweenOperation.AddTweenToWaitingList, () => f.AppendScheduled(101)));
        var state = f.Owner.Read(); var admission = state.Admissions.Single();
        Check(!state.Unknown && admission.ConfirmedAppend && !admission.Ambiguous && admission.PlayObservation is not null);
        Equal(0, before.Managers.Single().Scheduled!.Count); Equal(1, state.Current!.Managers.Single().Scheduled!.Count);
        Check(state.Operations.All(row => row.Captured && row.Completed && row.NativeReturned && row.Disposed));
        Equal(2, f.Backend.Originals); Equal(1, admission.Locations.Count);
        Check(!f.Owner.TrySeal() && !state.AdmissionSealed && state.Pending);
    }
    private static void DuplicateAdmissions()
    {
        using var f = new Fixture();
        for (var index = 0; index < 2; ++index) f.Call(NativeTweenOperation.AddTweenToWaitingList, () => f.AppendScheduled(101));
        var state = f.Owner.Read(); Equal(2, state.Admissions.Count);
        Check(state.Admissions.All(row => row.ConfirmedAppend && row.Ambiguous && row.Locations.Count == 2));
        Check(state.Admissions.Select(row => row.Admission).Distinct().Count() == 2
            && state.Admissions.Select(row => row.NativeSequence).Distinct().Count() == 2);
        Check(state.Admissions.Select(row => row.AdmittedAt!.Index).SequenceEqual(new[] { 0, 1 }));
    }
    private static void ReorderedAppend()
    {
        using var f = new Fixture(); f.Backend.Graph = Graph(new long[] { 103, 104 }); f.Owner.Tick();
        f.Call(NativeTweenOperation.AddTweenToWaitingList, () => f.Backend.Graph = Graph(new long[] { 104, 103, 101 }, version: 2));
        var admission = f.Owner.Read().Admissions.Single();
        Check(!admission.ConfirmedAppend && admission.Ambiguous && f.Owner.Read().Pending && !f.Owner.Read().Unknown);
    }
    private static void AlreadyPlaying()
    {
        using var f = new Fixture(); f.Backend.Graph = Graph(isOver: false); f.Owner.Tick();
        f.Call(NativeTweenOperation.AddTweenToWaitingList);
        var state = f.Owner.Read(); Equal(0, state.Admissions.Count); Equal(1, state.Operations.Count);
        Check(state.Operations.Single().NativeReturned && !state.Unknown && state.Pending);
    }
    private static void CompletionReentry()
    {
        using var f = new Fixture(); f.Call(NativeTweenOperation.AddTweenToWaitingList, () => f.AppendScheduled(101));
        f.Call(NativeTweenOperation.Update, () => {
            f.Backend.Graph = Graph(playing: new long[] { 101, 0, 0, 0 }, working: 0);
            f.Call(NativeTweenOperation.ExecuteTween, () => {
                // A Replay/Over path may set isOver while onComplete reenters.
                f.Call(NativeTweenOperation.Play, () => f.Call(NativeTweenOperation.AddTweenToWaitingList, () => f.AppendScheduled(101)));
                f.Backend.Graph = f.Backend.Graph with { Managers = f.Backend.Graph.Managers.Select(manager => manager with {
                    Playing = manager.Playing! with { CapacityItems = new long[] { 0, 0, 0, 0 } } }).ToArray() };
            });
        });
        var state = f.Owner.Read(); Equal(2, state.Admissions.Count); Check(!state.Unknown);
        var execute = state.Operations.Single(row => row.Operation == NativeTweenOperation.ExecuteTween);
        var play = state.Operations.Single(row => row.Operation == NativeTweenOperation.Play);
        Equal(execute.Observation, play.Parent!.Value);
        Check(state.Admissions.All(row => row.Ambiguous) && state.Admissions[1].PlayObservation == play.Observation);
        Check(execute.Before!.Managers.Single().Playing!.CapacityItems[0] == 101);
    }
    private static void BeforeFailure()
    {
        using var f = new Fixture(); f.Backend.FailBefore = true;
        f.Call(NativeTweenOperation.AddTweenToWaitingList, () => f.AppendScheduled(101));
        var state = f.Owner.Read(); var call = state.Operations.Single();
        Check(state.Unknown && !call.Captured && call.Completed && call.NativeReturned && call.Disposed && call.After is null);
        Equal(1, f.Backend.BeforeReads); Equal(0, f.Backend.AfterReads); Equal(1, f.Backend.Originals);
        var reads = f.Backend.RefreshReads; f.Owner.Tick(); Equal(reads, f.Backend.RefreshReads);
    }
    private static void AfterFailure()
    {
        using var f = new Fixture(); f.Backend.FailAfter = true;
        f.Call(NativeTweenOperation.AddTweenToWaitingList, () => f.AppendScheduled(101));
        var state = f.Owner.Read(); var call = state.Operations.Single();
        Check(state.Unknown && call.Captured && call.Before is not null && call.Completed && call.NativeReturned && call.After is null);
        Equal(1, f.Backend.AfterReads); Check(state.Pending && !f.Owner.TrySeal());
    }
    private static void AbnormalOriginal()
    {
        using var f = new Fixture();
        Throws<IOException>(() => f.Call(NativeTweenOperation.AddTweenToWaitingList, () => throw new IOException("controlled original failed")));
        var state = f.Owner.Read(); var call = state.Operations.Single();
        Check(state.Unknown && call.Completed && !call.NativeReturned && call.After is null && call.Disposed);
        Equal(0, f.Backend.AfterReads);
    }
    private static void CompleteCapacity()
    {
        using var f = new Fixture(); f.Call(NativeTweenOperation.AddTweenToWaitingList, () => f.AppendScheduled(101));
        var mutable = new List<long> { 0, 101, 101, 0 };
        f.Backend.Graph = Graph(playing: mutable, working: 0); f.Owner.Tick();
        var held = f.Owner.Read(); mutable.Clear();
        Equal(4, held.Current!.Managers.Single().Playing!.CapacityItems.Count);
        Equal(2, held.Admissions.Single().Locations.Count);
        Check(held.Admissions.Single().Locations.All(location => location.Kind == NativeTweenLocationKind.Playing)
            && held.Admissions.Single().Ambiguous);
    }
    private static void ManagerGeneration()
    {
        using var f = new Fixture(); f.Call(NativeTweenOperation.AddTweenToWaitingList, () => f.AppendScheduled(101));
        f.Call(NativeTweenOperation.Awake, () => f.Backend.Graph = Graph(new long[] { 101 }, generation: 2));
        var state = f.Owner.Read(); var admission = state.Admissions.Single();
        Equal(1L, admission.ManagerGeneration); Equal(2L, state.Current!.Managers.Single().Generation);
        Check(admission.Ambiguous && state.Pending && !state.Unknown);
        Equal(1L, state.Operations.First().After!.Managers.Single().Generation);
    }
    private static void AbsenceDoesNotSeal()
    {
        using var f = new Fixture(); f.Call(NativeTweenOperation.AddTweenToWaitingList, () => f.AppendScheduled(101));
        f.Call(NativeTweenOperation.ExecuteTween, () => f.Backend.Graph = Graph());
        var state = f.Owner.Read(); Check(state.Admissions.Single().ObservedAbsentAfterExecute);
        Check(state.Pending && !state.AdmissionSealed && !f.Owner.TrySeal());
        // Managed references remain owned even if the Unity-facing target disappears.
        f.Backend.Graph = Graph() with { Tweens = new[] { Info() with { Target = 0 } } }; f.Owner.Tick();
        Equal(1, f.Owner.Read().Admissions.Count); Check(!f.Owner.TrySeal());
    }
    private static void WrongCompletion()
    {
        using var f = new Fixture(); var frame = f.Backend.Frame(NativeTweenOperation.Play);
        var context = f.Owner.CaptureOperation(NativeTweenOperation.Play, frame)!;
        var wrong = frame; wrong.Sequence++;
        f.Owner.CompleteOperation(context, wrong, NativeOperationExitFlags.Returned); context.Dispose();
        var state = f.Owner.Read(); Check(state.Unknown && !state.Operations.Single().Completed && !state.Operations.Single().Disposed);
        Equal(0, f.Backend.AfterReads);
    }
    private static void ForeignUpdateScope()
    {
        using var f = new Fixture();
        for (var index = 0; index < 40; ++index) f.Call(NativeTweenOperation.Update);
        Equal(0, f.Owner.Read().Operations.Count);
        f.Call(NativeTweenOperation.AddTweenToWaitingList, () => f.AppendScheduled(101));
        f.Call(NativeTweenOperation.Update, () => f.Backend.Graph = Graph());
        for (var index = 0; index < 40; ++index) f.Call(NativeTweenOperation.Update);
        var state = f.Owner.Read(); Equal(2, state.Operations.Count); Equal(1, state.Admissions.Count);
        Check(state.Pending && !state.Unknown && !f.Owner.TrySeal()); Equal(82, f.Backend.Originals);
    }
    private static void EffectValueEquality()
    {
        var before = Graph(); var info = before.Tweens.Single(); var values = info.EffectValues;
        var same = before with { Tweens = new[] { info with { EffectValues = values with { } } } };
        Check(NativeLocalTweenBackend.SameGraph(before, same));
        foreach (var changed in new[] {
            values with { StartValue = unchecked((int)0x80000000) }, values with { Target = 1 },
            values with { Target2 = new(0, 1) }, values with { Target3 = new(0, 0, 1) },
            values with { TargetColor = new(0, 0, 0, 1) }, values with { FixedStart = new(0, 1, 0, 0) },
            values with { StartColor = new(1, 0, 0, 0) }, values with { StartValue2 = new(1, 0) },
            values with { StartValue3 = new(1, 0, 0) }, values with { MessageText = "actual source changed" },
            values with { AutoPlay = true }, values with { UseFixedStart = true } })
            Check(!NativeLocalTweenBackend.SameGraph(before, before with { Tweens = new[] { info with { EffectValues = changed } } }));
        var nanA = values with { FixedStart = new(unchecked((int)0x7fc00001), 0, 0, 0) };
        var nanB = values with { FixedStart = new(unchecked((int)0x7fc00002), 0, 0, 0) };
        Check(!NativeLocalTweenBackend.SameGraph(before with { Tweens = new[] { info with { EffectValues = nanA } } },
            before with { Tweens = new[] { info with { EffectValues = nanB } } }));
    }
    private static void SkippedOriginal()
    {
        foreach (var operation in new[] { NativeTweenOperation.Play, NativeTweenOperation.AddTweenToWaitingList, NativeTweenOperation.ExecuteTween })
        {
            using var f = new Fixture(); f.Backend.Skip.Add(operation);
            f.Call(operation, () => throw new InvalidOperationException("A native skipped original must not execute."));
            var state = f.Owner.Read(); var call = state.Operations.Single();
            Check(!state.Unknown && call.Captured && call.Completed && call.Skipped && !call.NativeReturned && call.Disposed);
            Equal((uint)NativeOperationExitFlags.Skipped, call.ExitFlags); Check(call.Before is not null && call.After is null);
            Equal(0, f.Backend.Originals); Equal(0, f.Backend.AfterReads); Equal(0, state.Admissions.Count);
        }
    }
    private static void SkippedExecuteRetainsWork()
    {
        using var f = new Fixture(); f.Call(NativeTweenOperation.AddTweenToWaitingList, () => f.AppendScheduled(101));
        f.Backend.Skip.Add(NativeTweenOperation.ExecuteTween); var afterReads = f.Backend.AfterReads;
        f.Call(NativeTweenOperation.ExecuteTween, () => f.Backend.Graph = Graph());
        var state = f.Owner.Read(); var admission = state.Admissions.Single();
        Check(!state.Unknown && state.Operations.Last().Skipped && admission.Locations.Count == 1 && !admission.ObservedAbsentAfterExecute);
        Equal(afterReads, f.Backend.AfterReads); Check(state.Pending && !f.Owner.CanRetireWork());
    }
    private static void SkippedNestedAdd()
    {
        using var f = new Fixture(); f.Backend.Skip.Add(NativeTweenOperation.AddTweenToWaitingList);
        f.Call(NativeTweenOperation.Play, () => f.Call(NativeTweenOperation.AddTweenToWaitingList, () => f.AppendScheduled(101)));
        var state = f.Owner.Read(); Equal(2, state.Operations.Count); Equal(0, state.Admissions.Count);
        var parent = state.Operations[0]; var child = state.Operations[1];
        Check(parent.NativeReturned && !parent.Skipped && parent.After is not null
            && child.Skipped && child.After is null && child.Parent == parent.Observation && !state.Unknown);
        Equal(1, f.Backend.Originals);
    }
    private static void ControlledSealHasNoAuthority()
    {
        using var f = new Fixture();
        var copied = f.Owner.Read() with { AdmissionSealed = true, Pending = false };
        Check(copied.AdmissionSealed && !f.Owner.Read().AdmissionSealed && !f.Owner.TrySeal() && !f.Owner.CanRetireWork());
        Throws<InvalidOperationException>(() => f.Owner.VerifyTweenAdmissionOwner());
        Throws<InvalidOperationException>(() => f.Owner.RequestAdmissionSeal(null!, null!));
        Check(!f.Owner.Read().Unknown && !f.Owner.TrySeal());
    }
    private static NativeTweenInfoObservation Info(bool isOver = true)
        => new(101, 102, 103, 104, 105, 106, 107, BitConverter.SingleToInt32Bits(1), BitConverter.SingleToInt32Bits(.1f), 0, 15, 3, false, false, isOver,
            new(false, false, 0, "", 0, 0, new(0, 0), new(0, 0, 0), new(0, 0, 0, 0), new(0, 0, 0, 0),
                new(0, 0, 0, 0), new(0, 0), new(0, 0, 0)));
    private static NativeTweenWorkGraph Graph(IReadOnlyList<long>? scheduled = null, int version = 1,
        IReadOnlyList<long>? playing = null, int working = 0, long generation = 1, bool isOver = true)
    {
        scheduled ??= Array.Empty<long>(); playing ??= new long[] { 0, 0, 0, 0 };
        var queue = new NativeTweenContainerObservation(301, 401, 501, scheduled.Count, version, scheduled.Concat(new long[] { 0, 0 }).ToArray());
        var ready = new NativeTweenContainerObservation(302, 402, 502, 0, 1, new long[] { 0, 0 });
        return new(801, new[] { new NativeTweenDelegateObservation(801, 0, 802, 201, 803, true) }, new[] { Info(isOver) },
            new[] { new NativeTweenManagerObservation(201, 202, generation, 4, working,
                new(303, 303, 503, working, 0, playing), queue, ready) });
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new();
        internal readonly Backend Backend;
        internal readonly NativeLocalTweenWorkOwner Owner;
        internal Fixture() { Backend = new(Main); Owner = NativeLocalTweenWorkOwner.CaptureBackend(Main, Backend); }
        internal void Call(NativeTweenOperation operation, Action? original = null) => Backend.Call(operation, original);
        internal void AppendScheduled(long tween)
        {
            var old = Backend.Graph.Managers.Single().Scheduled!;
            var next = old.CapacityItems.Take(old.Count).Append(tween).Concat(new long[] { 0, 0 }).ToArray();
            Backend.Graph = Backend.Graph with { Managers = Backend.Graph.Managers.Select(manager => manager with {
                Scheduled = old with { Count = old.Count + 1, Version = old.Version + 1, CapacityItems = next } }).ToArray() };
        }
        public void Dispose() => Main.Dispose();
    }
    private sealed class Backend : INativeLocalTweenBackend, INativeDetourEngine, IPreparedNativeDetourEngine
    {
        private static long _module = 0x5f0000000;
        private readonly long _identity;
        private readonly IMainThreadDispatcher _main;
        private readonly ExactHookBroker _broker;
        internal ExactHookBroker Broker => _broker;
        private readonly Dictionary<NativeTweenOperation, Bridge> _bridges = new();
        private readonly Dictionary<NativeTweenOperation, ExactHookBroker.Binding> _bindings = new();
        private ulong _sequence;
        internal NativeTweenWorkGraph Graph = NativeTweenWorkObservationTests.Graph();
        internal bool FailBefore, FailAfter;
        internal readonly HashSet<NativeTweenOperation> Skip = new();
        internal int BeforeReads, AfterReads, SharedManagerReads, RefreshReads, Originals;
        public bool CleanupUnknown => false;
        public int RootRecords { get; private set; } = 8;
        public IntPtr PlayMethodInfo => new(700);
        internal Backend(IMainThreadDispatcher main) { _main = main; _identity = Interlocked.Add(ref _module, 0x10000000); _broker = new(this, main, new Logger()); }
        public void Initialize(NativeLocalTweenWorkOwner owner)
        {
            owner.RegisterSource(101);
            foreach (var row in TweenOperationProfile.All)
            {
                var operation = row.Operation; var bridge = new Bridge(row.Kind); _bridges.Add(operation, bridge);
                var target = new VerifiedHookTarget("tween-work.fixture." + operation, new(_identity + row.Rva), new(700 + (int)operation),
                    new(900 + (int)operation), ExactHookBroker.OperationAbi(row.Kind));
                _bindings.Add(operation, _broker.BindNativeOperationObservation(target, row.Kind, () => bridge,
                    frame => owner.CaptureOperation(operation, frame), owner.CompleteOperation,
                    (frame, reason) => owner.ObserveFault(operation, frame, reason)));
            }
        }
        public NativeTweenWorkGraph Capture(NativeTweenOperation operation, NativeOperationFrame frame, bool after)
        {
            _main.VerifyAccess(); RootRecords += 2;
            if (after) { ++AfterReads; if (FailAfter) throw new IOException("controlled final rooted graph failed"); }
            else { ++BeforeReads; if (FailBefore) throw new IOException("controlled before rooted graph failed"); }
            return Graph;
        }
        public NativeTweenManagerObservation CaptureSharedUpdate(NativeOperationFrame frame, bool after)
        {
            _main.VerifyAccess(); ++SharedManagerReads;
            return Graph.Managers.Single(row => row.Object == frame.Self.ToInt64());
        }
        public NativeTweenWorkGraph Refresh() { _main.VerifyAccess(); ++RefreshReads; return Graph; }
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity()
        {
            long total = 0; foreach (var binding in _bindings.Values) { var activity = binding.ReadActivity(); if (!activity.Known) return new(total, false); total += activity.InFlight; }
            return new(total, true);
        }
        internal NativeOperationFrame Frame(NativeTweenOperation operation) => new() { Version = 1, Size = 64,
            Kind = TweenOperationProfile.Get(operation).Kind, Self = new(operation == NativeTweenOperation.Play ? 101 : 201),
            Arg0 = TweenOperationProfile.Get(operation).Kind == NativeOperationKind.Reference1 ? new(101) : IntPtr.Zero,
            Method = new(700 + (int)operation), Caller = new(999), Sequence = ++_sequence };
        internal void Call(NativeTweenOperation operation, Action? original)
            => _bridges[operation].Call(Frame(operation), () => { ++Originals; original?.Invoke(); }, Skip.Contains(operation));
        internal void CallUpdate(Action<NativeOperationFrame> original)
        {
            var frame = Frame(NativeTweenOperation.Update);
            _bridges[NativeTweenOperation.Update].Call(frame, () => { ++Originals; original(frame); });
        }
        public Delegate Install(VerifiedHookTarget target, Delegate replacement) => throw new InvalidOperationException("Only the controlled native context bridge is used.");
        public IntPtr Prepare(VerifiedHookTarget target, IntPtr replacement) => new(1234);
        public void Commit(VerifiedHookTarget target) { }
    }
    private sealed class Bridge : INativeOperationContextBridge
    {
        private Func<NativeOperationFrame, nuint> _enter = null!;
        private Action<NativeOperationFrame, nuint, NativeOperationExitFlags> _exit = null!;
        private long _inflight;
        internal Bridge(NativeOperationKind kind) => Kind = kind;
        public NativeOperationKind Kind { get; }
        public IntPtr EntryAddress => new(1235);
        public void Configure(IntPtr original, Func<NativeOperationFrame, nuint> enter,
            Action<NativeOperationFrame, nuint, NativeOperationExitFlags> exit) => (_enter, _exit) = (enter, exit);
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity() => new(Interlocked.Read(ref _inflight), true);
        internal void Call(NativeOperationFrame frame, Action original, bool skip = false)
        {
            Interlocked.Increment(ref _inflight); nuint token = 0; var returned = false;
            try { token = _enter(frame); if (!skip) { original(); returned = true; } }
            finally { try { _exit(frame, token, skip ? NativeOperationExitFlags.Skipped
                : returned ? NativeOperationExitFlags.Returned : NativeOperationExitFlags.Abnormal); }
                finally { Interlocked.Decrement(ref _inflight); } }
        }
    }
    private sealed class Logger : ILogger { public void Log(LogLevel level, string message, Exception? error = null) { } }
}
