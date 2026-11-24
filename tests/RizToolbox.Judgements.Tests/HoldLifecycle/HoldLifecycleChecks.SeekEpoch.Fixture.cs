using System.Reflection;
using RizToolbox.Adapter;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.Judgements;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class HoldLifecycleChecks
{
    private static readonly List<EpochFixture> UnknownEpochOwners = new();
    private sealed class EpochProfile : ISeekHoldEpochProfile
    {
        private readonly FakeNative _native;
        private readonly JudgementModule.SeekBackendAccess _access;
        private readonly ISeekTargetNativeProfile _source;
        internal Action? OnVerify;
        internal EpochProfile(FakeNative native, ISeekTargetNativeProfile source, JudgementModule.SeekBackendAccess access)
            => (_native, _source, _access) = (native, source, access);
        public IntPtr IteratorClass => _native.HoldIteratorClass;
        public IntPtr FactoryMethod => _native.SeekHoldFactory;
        public IntPtr StartMethod => _native.SeekHoldStart;
        public IntPtr StopMethod => _native.SeekHoldStop;
        public void VerifyCurrent() { _source.VerifyCurrent(); _access.VerifyDrained(); OnVerify?.Invoke(); }
    }

    // The actual module issues its registered access, and the real broker
    // owns Start/Move/all six Stop captures and complete physical finallys.
    // The controlled engine/bridges and profile do not prove pinned game code,
    // engine Detach completion, formal Fresh or production Seek admission.
    private sealed class EpochFixture : IDisposable
    {
        internal readonly FakeNative Native = new();
        internal readonly GameEventState State = new();
        internal readonly MainThreadDispatcher Main = new();
        internal readonly Engine Engine = new();
        internal readonly TestLog Log = new();
        internal readonly NativeJudgementBackend Backend;
        internal readonly ExactHookBroker Broker;
        internal readonly Bridge Start = new(NativeCoroutineKind.Start), Move = new(NativeCoroutineKind.MoveNext);
        internal readonly VerifiedHookTarget StartTarget, MoveTarget;
        internal readonly ExactHookBroker.Binding StartBinding, MoveBinding;
        internal readonly Dictionary<CoroutineStopOperation, StopBridge> Stops = new();
        internal readonly Dictionary<CoroutineStopOperation, VerifiedHookTarget> StopTargets = new();
        internal readonly Dictionary<CoroutineStopOperation, ExactHookBroker.Binding> StopBindings = new();
        internal readonly JudgementModule Module;
        internal readonly JudgementCoordinator Coordinator;
        internal readonly JudgementRunCapture Run;
        internal readonly NativeSeekTargetStatePlan TargetPlan;
        internal readonly JudgementModule.SeekBackendAccess Access;
        internal readonly EpochProfile Profile;
        internal readonly NativeJudgementBackend.SeekHoldEpoch Epoch;
        internal readonly IntPtr[] Predecessors;
        internal bool Retained;
        internal int VerifiedQueueWrites;
        internal bool ComposedGameState;
        private ulong _sequence;
        private readonly int _publicationCalls = NativePublicationCall.RetainedCallCount;
        private readonly int _publicationRoots = NativePublicationCall.RetainedRootRecordCount;
        private readonly int _publicationArguments = NativePublicationCall.RetainedArgumentRecordCount;
        internal EpochFixture(bool autoPlay = true, bool freeHold = false, bool twoHolds = false, IntPtr compositionImage = default)
        {
            ComposedGameState = compositionImage != IntPtr.Zero;
            if (ComposedGameState) Native.ConfigureSeekGameStateGraph(compositionImage);
            Native.ConfigureSeekHoldEpoch(autoPlay, freeHold, twoHolds, reuseSource: ComposedGameState);
            if (ComposedGameState) Native.ConfigureSeekGameStateExports();
            State.SetAvailability(scenes: true, runs: true, pause: true); State.Scene("Play");
            State.Controller(Native.GameplayInformation.ToInt64(), true, false, "controlled-actual-seek-epoch", Native.GameplayController.ToInt64());
            Backend = new(Native.GameplayApi, State); Backend.InitializeHoldLifecycle(Main);
            Broker = new(Engine, Main, Log);
            foreach (var declaration in CoroutineStopProfile.All)
            {
                var operation = declaration.Operation; var bridge = new StopBridge(declaration.Kind);
                var method = operation == CoroutineStopOperation.All ? Native.SeekHoldStop : new(Interlocked.Add(ref _addresses, 16));
                var target = new VerifiedHookTarget("test.seek.hold.stop." + operation, new(Interlocked.Add(ref _addresses, 16)), method,
                    Native.HoldMonoBehaviourClass, ExactHookBroker.OperationAbi(declaration.Kind));
                Stops.Add(operation, bridge); StopTargets.Add(operation, target);
                StopBindings.Add(operation, Broker.BindNativeOperationObservation(target, declaration.Kind, () => bridge,
                    frame => Backend.CaptureHoldStop(operation, frame), Backend.CompleteHoldStop,
                    (frame, reason) => Backend.ObserveHoldStopFault(operation, frame, reason)));
            }
            Backend.CompleteHoldStopInstallation();
            StartTarget = new("test.seek.hold.start", new(Interlocked.Add(ref _addresses, 16)), Native.SeekHoldStart,
                Native.HoldMonoBehaviourClass, ExactHookAbi.CoroutineStartReference1, Native.HoldHandleClass);
            MoveTarget = new("test.seek.hold.move", new(Interlocked.Add(ref _addresses, 16)), new(0x184), Native.HoldIteratorClass,
                ExactHookAbi.InstanceBooleanReturn0);
            Move.Original = frame => { Native.SeekHoldMoveOriginal(frame.Self); return 0xff; };
            Start.Original = frame =>
            {
                Check(Move.Invoke(MoveFrame(frame.Enumerator)) == 0xff, "Controlled actual Start changed its synchronous raw Move result.");
                return unchecked((nuint)Native.HoldCreateHandle().ToInt64());
            };
            MoveBinding = Broker.BindNativeCoroutineObservation(MoveTarget, NativeCoroutineKind.MoveNext, () => Move,
                Backend.CaptureHoldMove, Backend.CompleteHoldMove, Backend.ObserveHoldLifecycleFault);
            StartBinding = Broker.BindNativeCoroutineObservation(StartTarget, NativeCoroutineKind.Start, () => Start,
                Backend.CaptureHoldStart, Backend.CompleteHoldStart, Backend.ObserveHoldLifecycleFault);
            Native.SeekHoldStartOriginal = iterator => unchecked((IntPtr)(nint)Start.Invoke(StartFrame(iterator)));
            Native.SeekHoldStopOriginal = () => Stops[CoroutineStopOperation.All].Invoke(StopFrame());
            var profile = new AdapterBuildProfile("controlled-seek-epoch", new string('0', 64), "controlled", "controlled", "", "", new Dictionary<string, TypeBinding>());
            Module = new(Native.GameplayApi, Main, Log, profile, State, new EventHub(Log), "", "");
            Coordinator = new(Backend, Main, Log, 0, () => 0, () => State.RunId, _ => { });
            Set(Module, "_backend", Backend); Set(Module, "_coordinator", Coordinator);
            Set(Module, "_hold", MoveBinding); Set(Module, "_holdStart", StartBinding);
            var registeredStops = (ExactHookBroker.Binding?[])Get(Module, "_holdStops");
            foreach (var row in StopBindings) registeredStops[(int)row.Key] = row.Value;
            Predecessors = twoHolds ? new[] { Native.SeekHoldCreateIterator(0, false), Native.HoldIterator } : new[] { Native.HoldIterator };
            foreach (var iterator in Predecessors) Start.Invoke(StartFrame(iterator));
            if (ComposedGameState)
            {
                var snapshot = Backend.HoldLifecycle.Snapshot();
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
                { stage = "actual-predecessor-start", snapshot.Unknown, snapshot.Records, snapshot.InFlight,
                    snapshot.FullCoverageKnown, snapshot.Reason, count = Native.JudgeHoldValue,
                    faults = Log.Errors, starts = Start.Originals, moves = Move.Originals }));
            }
            Check(!Backend.HoldLifecycle.Snapshot().Unknown && Native.JudgeHoldValue == Predecessors.Length,
                "The original backend did not capture every yielded predecessor.");
            var association = State.CaptureCurrentPlaybackRun() ?? throw new InvalidOperationException("No actual host run association.");
            Access = Module.CaptureSeekBackend(association); Access.VerifyDrained();
            Run = Backend.CaptureRun() ?? throw new InvalidOperationException("No actual target RunValue.");
            TargetPlan = Run.CaptureSeekTargetStatePlan(Native.TargetProfile, new(1.25), new());
            var independent = Backend.CaptureRun() ?? throw new InvalidOperationException("No independent actual epoch RunValue.");
            Profile = new(Native, Native.TargetProfile, Access);
            Epoch = new(Backend, Access, independent, TargetPlan, Native.TargetProfile,
                (_, _, _) => Profile);
            Set(Backend, "_seekHoldEpoch", Epoch); Epoch.Prepare(); Epoch.VerifyPrepared();
        }
        internal static object Get(object owner, string field) => owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
        private static void Set(object owner, string field, object? value) => owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(owner, value);
        private NativeCoroutineFrame StartFrame(IntPtr iterator) => new()
        { Version = 1, Size = 0x30, Kind = NativeCoroutineKind.Start, Self = Native.HoldJudge, Enumerator = iterator, Method = StartTarget.MethodInfo, Caller = new(0x12345) };
        private NativeCoroutineFrame MoveFrame(IntPtr iterator) => new()
        { Version = 1, Size = 0x30, Kind = NativeCoroutineKind.MoveNext, Self = iterator, Method = MoveTarget.MethodInfo, Caller = new(0x12346) };
        private NativeOperationFrame StopFrame() => new()
        { Version = 1, Size = 64, Kind = CoroutineStopProfile.Get(CoroutineStopOperation.All).Kind, Self = Native.HoldJudge,
            Method = StopTargets[CoroutineStopOperation.All].MethodInfo, Caller = new(0x12347), Sequence = ++_sequence };
        internal void Drained()
        {
            Access.VerifyDrained();
            Check(Start.InFlight == 0 && Move.InFlight == 0 && Stops.Values.All(value => value.InFlight == 0)
                && Start.Enters == Start.Exits && Move.Enters == Move.Exits && Stops.Values.All(value => value.Enters == value.Exits),
                "An actual original or observer physical finally did not drain exactly once.");
        }
        public void Dispose()
        {
            Native.BeforeSeekHoldFactoryMetadata = null; Native.AfterSeekHoldStore = null; Native.TargetProfile.OnVerify = null; Profile.OnVerify = null;
            Native.BeforeSeekHoldStartMetadata = null; Native.AfterSeekHoldFactory = null; Native.AfterTargetFieldRead = null;
            if (ComposedGameState)
            {
                Native.AfterGameStateScalarStore = null; Native.CanvasPositionProfile.OnVerify = null; Native.BeforeCanvasCacheScalar = null;
            }
            if (Retained)
            {
                var roots = Native.ActiveRoots; var frees = Native.FreeHandleAttempts;
                Expect<InvalidOperationException>(Epoch.Dispose); Expect<InvalidOperationException>(Epoch.Dispose);
                Check(Native.ActiveRoots == roots && Native.FreeHandleAttempts == frees && roots > 0,
                    "An unknown original epoch retried release or lost its native roots.");
                UnknownEpochOwners.Add(this); return;
            }
            Drained(); Epoch.Dispose(); TargetPlan.Dispose(); Run.Dispose(); Coordinator.Dispose();
            foreach (var binding in StopBindings.Values) binding.Dispose(); StartBinding.Dispose(); MoveBinding.Dispose(); Backend.Dispose();
            Check(Engine.Installs == 0 && Engine.Prepares == 8 && Engine.Commits == 8 && ExactHookBroker.BooleanInvocationDepth == 0,
                "Epoch execution bypassed the original broker installation or leaked its Boolean depth.");
            Check(Native.ActiveRoots == 0 && Native.NewRoots == Native.FreedRoots
                && (ComposedGameState ? Native.ProducerCompleteRootHistory()
                    : Native.NoteViewFreeLog.Select(row => row.Handle).Distinct().Count() == Native.FreeHandleAttempts),
                "A known complete epoch lost a root or freed it twice.");
            Check(NativePublicationCall.RetainedCallCount == _publicationCalls && NativePublicationCall.RetainedRootRecordCount == _publicationRoots
                && NativePublicationCall.RetainedArgumentRecordCount == _publicationArguments && Native.AllGameplayReceiversRooted
                && Native.GameplayCommandCalls == 0 && Native.GameplayPitchWrites == 0 && Native.TargetForbiddenUpdates == 0
                && Native.JudgeQueueWrites.Count == VerifiedQueueWrites && Native.JudgeWriteViolations.Count == 0
                && (ComposedGameState || Native.FlagWrites.Count == 0 && Native.FingerWrites.Count == 0 && Native.RendererCalls.Count == 0),
                "The epoch leaked publication responsibility or mutated an unrelated gameplay stage.");
            GC.KeepAlive(Module); GC.KeepAlive(Broker); Main.Dispose(); Native.Dispose();
        }
    }
}
