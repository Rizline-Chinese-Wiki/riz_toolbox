using System.Text.Json;
using RizToolbox.Adapter;
using RizToolbox.Adapter.Judgements;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class HoldLifecycleChecks
{
    internal static readonly (string Name, Action Run)[] Cases =
    {
        ("Hold lifecycle publishes Start before synchronous Move and survives pause revision", SynchronousAndPause),
        ("Hold lifecycle missing and duplicate Start never become complete coverage", MissingAndDuplicate),
        ("Hold lifecycle abnormal Move preserves original exception and unknown roots", Abnormal),
        ("Hold lifecycle run retirement defers in-flight roots without rebinding", RetireInFlight),
        ("Hold lifecycle cancellation requests never fabricate natural completion", Cancellation),
        ("Hold lifecycle worker and foreign MethodInfo faults touch no native metadata", Isolation),
        ("Hold lifecycle changed iterator note cannot rebind a retained identity", ChangedNote),
        ("Hold lifecycle unknown root free keeps owner and never retries", UnknownFree)
    };
    private static long _addresses = 0x79000000;
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static T Expect<T>(Action action) where T : Exception
    {
        try { action(); } catch (T error) { return error; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }
    private sealed class TestLog : ILogger
    {
        internal readonly List<string> Errors = new();
        internal readonly List<string> HoldSamples = new();
        public void Log(LogLevel level, string message, Exception? exception = null)
        {
            if (level == LogLevel.Error) Errors.Add(message);
            const string prefix = "Hold lifecycle sample: ";
            if (message.StartsWith(prefix, StringComparison.Ordinal)) HoldSamples.Add(message[prefix.Length..]);
        }
    }
    private sealed class Engine : INativeDetourEngine, IPreparedNativeDetourEngine
    {
        internal int Prepares, Commits, Installs;
        internal readonly Dictionary<IntPtr, IntPtr> Prepared = new();
        internal Action<VerifiedHookTarget>? DuringCommit;
        public Delegate Install(VerifiedHookTarget target, Delegate replacement)
        { ++Installs; throw new InvalidOperationException("Lifecycle tests require the native first-patch transport."); }
        public IntPtr Prepare(VerifiedHookTarget target, IntPtr replacement)
        {
            ++Prepares; Check(replacement != IntPtr.Zero && !Prepared.ContainsKey(target.Address), "A native target was prepared twice or with a null replacement.");
            Prepared.Add(target.Address, replacement); return new(target.Address.ToInt64() + 0x100000);
        }
        public void Commit(VerifiedHookTarget target) { ++Commits; DuringCommit?.Invoke(target); }
    }
    private sealed class Bridge : INativeCoroutineContextBridge
    {
        public NativeCoroutineKind Kind { get; }
        public IntPtr EntryAddress { get; } = new(Interlocked.Add(ref _addresses, 16));
        private Func<NativeCoroutineFrame, nuint> _enter = null!;
        private Action<NativeCoroutineFrame, nuint, nuint, NativeCoroutineExitFlags> _exit = null!;
        internal Func<NativeCoroutineFrame, nuint> Original = _ => 0;
        internal bool Configured;
        internal long InFlight;
        internal int Originals, Enters, Exits;
        internal Bridge(NativeCoroutineKind kind) => Kind = kind;
        public void Configure(IntPtr original, Func<NativeCoroutineFrame, nuint> enter,
            Action<NativeCoroutineFrame, nuint, nuint, NativeCoroutineExitFlags> exit)
        {
            Check(!Configured && original != IntPtr.Zero, "Native bridge configuration must precede publication exactly once.");
            (_enter, _exit, Configured) = (enter, exit, true);
        }
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity() => new(Interlocked.Read(ref InFlight), Configured);
        internal nuint Invoke(NativeCoroutineFrame frame)
        {
            Check(Configured, "Unconfigured native bridge invoked."); Interlocked.Increment(ref InFlight);
            nuint token = 0, result = 0; var returned = false;
            try { ++Enters; token = _enter(frame); ++Originals; result = Original(frame); returned = true; return result; }
            finally
            {
                try { ++Exits; _exit(frame, token, result, returned ? NativeCoroutineExitFlags.Returned : NativeCoroutineExitFlags.Abnormal); }
                finally { Interlocked.Decrement(ref InFlight); }
            }
        }
    }
    private sealed class Fixture : IDisposable
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
        internal bool RetainStorage;
        internal Fixture(bool startDuringPublication = false, bool synchronousMove = false,
            Action<Fixture>? beforeCoroutineBindings = null)
        {
            Native.ConfigureHoldLifecycle(); State.SetAvailability(scenes: true, runs: true, pause: true); State.Scene("Play");
            State.Controller(Native.GameplayInformation.ToInt64(), true, false, "synthetic-hold-lifecycle-only", Native.GameplayController.ToInt64());
            Backend = new(Native.GameplayApi, State); Backend.InitializeHoldLifecycle(Main);
            Broker = new(Engine, Main, Log);
            StartTarget = new("test.hold.start", new(Interlocked.Add(ref _addresses, 16)), new(0x183), new(0x283),
                ExactHookAbi.CoroutineStartReference1, Native.HoldHandleClass);
            MoveTarget = new("test.hold.move", new(Interlocked.Add(ref _addresses, 16)), new(0x184), Native.HoldIteratorClass,
                ExactHookAbi.InstanceBooleanReturn0);
            Move.Original = frame =>
            {
                var row = Snapshot().Rows.Single();
                if (synchronousMove && !row.StartReturned)
                    Check(row.StartObserved && Snapshot().InFlight == 2, "Synchronous Move did not observe the pending Start entry.");
                return 0xff;
            };
            Start.Original = frame =>
            {
                if (synchronousMove)
                    Check(Move.Invoke(MoveFrame(frame.Enumerator)) == 0xff, "Start did not forward its synchronous first Move result.");
                return unchecked((nuint)Native.HoldHandle.ToInt64());
            };
            beforeCoroutineBindings?.Invoke(this);
            MoveBinding = Broker.BindNativeCoroutineObservation(MoveTarget, NativeCoroutineKind.MoveNext, () => Move,
                Backend.CaptureHoldMove, Backend.CompleteHoldMove, Backend.ObserveHoldLifecycleFault);
            if (startDuringPublication) Engine.DuringCommit = target =>
            {
                if (target.Address != StartTarget.Address) return;
                Check(Start.Configured && Move.Configured, "A Start escaped before both transport bridges were configured.");
                Start.Invoke(StartFrame());
            };
            StartBinding = Broker.BindNativeCoroutineObservation(StartTarget, NativeCoroutineKind.Start, () => Start,
                Backend.CaptureHoldStart, Backend.CompleteHoldStart, Backend.ObserveHoldLifecycleFault);
            Engine.DuringCommit = null;
        }
        internal NativeCoroutineFrame StartFrame(IntPtr? iterator = null) => new()
        {
            Version = 1, Size = 0x30, Kind = NativeCoroutineKind.Start, Self = Native.HoldJudge,
            Enumerator = iterator ?? Native.HoldIterator, Method = StartTarget.MethodInfo, Caller = new(0x12345)
        };
        internal NativeCoroutineFrame MoveFrame(IntPtr? iterator = null) => new()
        {
            Version = 1, Size = 0x30, Kind = NativeCoroutineKind.MoveNext, Self = iterator ?? Native.HoldIterator,
            Method = MoveTarget.MethodInfo, Caller = new(0x12346)
        };
        internal HoldCoroutineLifecycleSnapshot Snapshot() => Backend.HoldLifecycle.Snapshot();
        internal long SynchronousIterator() => ((HoldCoroutineContext)typeof(NativeJudgementBackend)
            .GetField("_holdContext", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(Backend)!).Current();
        public void Dispose()
        {
            StartBinding.Dispose(); MoveBinding.Dispose(); Backend.Dispose();
            Check(Start.InFlight == 0 && Move.InFlight == 0 && ExactHookBroker.BooleanInvocationDepth == 0,
                "A physical native frame or Boolean depth escaped its paired Exit.");
            Check(Engine.Installs == 0 && Engine.Prepares == 2 && Engine.Commits == 2,
                "The test bypassed native Prepare/Configure/Commit or patched a target more than once.");
            Check(Native.FieldWrites == 0 && Native.GameplayCommandCalls == 0 && Native.GameplayPitchWrites == 0
                && Native.FlagViolations.Count == 0 && Native.NoteViewDispatchViolations.Count == 0,
                "Lifecycle observation dispatched a setter, game command or unselected synthetic method.");
            Check(Native.ActiveRoots == 0 && Native.NewRoots == Native.FreedRoots
                && Native.NoteViewFreeLog.Select(row => row.Handle).Distinct().Count() == Native.FreeHandleAttempts,
                "A known native root escaped cleanup or an unknown free was retried.");
            Main.Dispose(); if (!RetainStorage) Native.Dispose();
        }
    }
    private static void SynchronousAndPause()
    {
        using var f = new Fixture(startDuringPublication: true, synchronousMove: true);
        var first = f.Snapshot(); var row = first.Rows.Single();
        Check(f.Start.Originals == 1 && f.Move.Originals == 1 && row.StartObserved && row.StartReturned
            && row.MoveCalls == 1 && row.YieldedMoves == 1 && !row.NaturalCompletion && !first.Unknown
            && row.Identity.Owner == f.Native.HoldJudge.ToInt64() && row.Identity.Enumerator == f.Native.HoldIterator.ToInt64()
            && row.Identity.Note == f.Native.FlagNotes[2].ToInt64() && !first.FullCoverageKnown,
            "Actual scheduled iterator identity or synchronous noncanonical true result was lost.");
        var before = first.Run!;
        f.Move.Original = _ =>
        {
            f.State.ObservePause(true); f.Backend.TickHoldLifecycle(); var paused = f.Snapshot().Run!;
            Check(paused.SceneGeneration == before.SceneGeneration && paused.RunId == before.RunId && paused.Revision > before.Revision,
                "Pause did not preserve the same generation while advancing its revision.");
            return 0xff;
        };
        Check(f.Move.Invoke(f.MoveFrame()) == 0xff, "Raw AL was changed by lifecycle observation.");
        var pausedResult = f.Snapshot();
        Check(!pausedResult.Unknown && pausedResult.Rows.Single().YieldedMoves == 2 && pausedResult.InFlight == 0,
            "An old-revision in-flight Move could not finish after a same-generation pause.");
        var samplingRevision = f.State.Revision; var sourceReads = 0;
        f.Native.AfterNoteViewFieldRead = (name, owner) =>
        {
            if (name != "holdNoteInformation" || owner != f.Native.HoldIterator || ++sourceReads != 2) return;
            f.Native.AfterNoteViewFieldRead = null; f.State.ObservePause(false);
        };
        f.Move.Original = _ =>
        {
            Check(f.State.Revision > samplingRevision && f.Snapshot().Run!.Revision == f.State.Revision,
                "Begin did not refresh a same-generation revision advanced while sampling its source.");
            return 0xff;
        };
        f.Move.Invoke(f.MoveFrame());
        Check(sourceReads >= 2 && !f.Snapshot().Unknown && f.Snapshot().Rows.Single().YieldedMoves == 3,
            "A source-sampling pause revision incorrectly rejected the actual same-generation Move.");
        f.Move.Original = _ => 0; Check(f.Move.Invoke(f.MoveFrame()) == 0, "False Move result was changed.");
        var ended = f.Snapshot();
        Check(ended.NaturallyCompleted == 1 && ended.Rows.Single().NaturalCompletion && !ended.FullCoverageKnown
            && ended.RootResponsibilities > 0 && f.Native.NoteViewHandlesFor(f.Native.HoldIterator).Length > 0,
            "False Move must mark this iterator complete while preserving its rooted identity until run retirement.");
    }
    private static void MissingAndDuplicate()
    {
        using (var f = new Fixture())
        {
            f.Move.Original = _ => 0xff; Check(f.Move.Invoke(f.MoveFrame()) == 0xff, "Orphan Move result changed.");
            var snapshot = f.Snapshot(); var row = snapshot.Rows.Single();
            Check(snapshot.Unknown && !snapshot.FullCoverageKnown && !row.StartObserved && row.MoveObserved
                && !row.NaturalCompletion && snapshot.RootResponsibilities > 0,
                "A Move without observed Start was treated as fully known or left its capture unowned.");
        }
        using (var f = new Fixture())
        {
            f.Start.Invoke(f.StartFrame()); f.Start.Invoke(f.StartFrame()); var snapshot = f.Snapshot();
            Check(f.Start.Originals == 2 && snapshot.Records == 1 && snapshot.Unknown && snapshot.NaturallyCompleted == 0,
                "Repeated scheduling silently reused an identity or suppressed a native original.");
        }
    }
    private static void Abnormal()
    {
        using (var f = new Fixture())
        {
            var diagnostics = new HoldLifecycleDiagnostics(f.Log);
            f.Start.Invoke(f.StartFrame()); diagnostics.Sample(f.Backend.HoldLifecycle);
            var failure = new IOException("Controlled abnormal Hold original.");
            f.Move.Original = _ => throw failure;
            var actual = Expect<IOException>(() => f.Move.Invoke(f.MoveFrame())); var snapshot = f.Snapshot();
            Check(ReferenceEquals(actual, failure) && f.Move.Originals == 1 && snapshot.Unknown && snapshot.NaturallyCompleted == 0
                && snapshot.InFlight == 0 && snapshot.RootResponsibilities > 0 && !snapshot.FullCoverageKnown,
                "Abnormal native exit replaced/retried the original exception or fabricated terminal/idle evidence.");
            diagnostics.Sample(f.Backend.HoldLifecycle); diagnostics.Sample(f.Backend.HoldLifecycle);
            Check(f.Log.HoldSamples.Count == 2, "The first clean start and first fault each need exactly one sample.");
            using var report = JsonDocument.Parse(f.Log.HoldSamples[1]);
            var fault = report.RootElement;
            Check(fault.GetProperty("Fault").GetBoolean() && fault.GetProperty("Reason").GetString() is { Length: > 0 }
                && fault.GetProperty("StartReturnedAndExitedSequence").ValueKind == JsonValueKind.Null
                && fault.GetProperty("NaturalExitSequence").ValueKind == JsonValueKind.Null,
                "A fault sample was indistinguishable from natural completion.");
        }
        using (var f = new Fixture())
        {
            f.Start.Invoke(f.StartFrame());
            f.Move.Original = _ =>
            {
                f.MoveBinding.Dispose();
                Check(f.Snapshot().InFlight == 1 && f.SynchronousIterator() == f.Native.HoldIterator.ToInt64()
                    && f.Native.NoteViewHandlesFor(f.Native.HoldIterator).Length > 0,
                    "Binding withdrawal freed a live physical iterator or its synchronous attribution.");
                return 0;
            };
            Check(f.Move.Invoke(f.MoveFrame()) == 0, "Binding withdrawal changed the physical original result.");
            var snapshot = f.Snapshot();
            Check(snapshot.Unknown && snapshot.InFlight == 0 && snapshot.NaturallyCompleted == 0
                && snapshot.RootResponsibilities > 0, "A withdrawn after-observer fabricated confirmed natural completion.");
        }
    }
    private static void RetireInFlight()
    {
        using var f = new Fixture(); f.Start.Invoke(f.StartFrame()); var old = f.Snapshot().Run!;
        f.Move.Original = _ =>
        {
            f.State.Scene("Play-next");
            f.State.Controller(f.Native.GameplayInformation.ToInt64(), true, false, "synthetic-hold-lifecycle-next", f.Native.GameplayController.ToInt64());
            f.Backend.TickHoldLifecycle();
            Check(f.Snapshot().InFlight == 1 && f.Native.NoteViewHandlesFor(f.Native.HoldIterator).Length > 0,
                "Run retirement freed a receiver while its native original was still using it.");
            return 0;
        };
        Check(f.Move.Invoke(f.MoveFrame()) == 0, "Retiring an in-flight run changed its original result.");
        var retired = f.Snapshot();
        Check(retired.InFlight == 0 && retired.Rows.All(value => value.Identity.Run.SceneGeneration == old.SceneGeneration)
            && retired.Rows.All(value => value.Retired) && retired.RootResponsibilities == 0
            && f.Native.NoteViewHandlesFor(f.Native.HoldIterator).Length == 0,
            "An old in-flight scope rebound to the new run or its known roots did not drain on exit.");
    }
    private static void Cancellation()
    {
        using var f = new Fixture(); f.Start.Invoke(f.StartFrame());
        f.Backend.HoldLifecycle.ObserveCancellation(f.Native.HoldJudge.ToInt64(), f.Native.HoldIterator.ToInt64());
        f.Backend.HoldLifecycle.ObserveCancellation(f.Native.HoldJudge.ToInt64(), f.Native.HoldIterator.ToInt64());
        var requested = f.Snapshot(); var row = requested.Rows.Single();
        Check(requested.PendingCancellation == 1 && row.CancellationRequested && row.CancellationRequests == 2
            && !row.NaturalCompletion && requested.RootResponsibilities > 0 && !requested.FullCoverageKnown,
            "Repeated stop requests fabricated iterator termination or global idle.");
        f.Move.Original = _ => 0xff; f.Move.Invoke(f.MoveFrame());
        Check(!f.Snapshot().Rows.Single().NaturalCompletion, "A yielded iterator became complete after a cancellation request.");
        f.Move.Original = _ => 0; f.Move.Invoke(f.MoveFrame());
        Check(f.Snapshot().NaturallyCompleted == 1 && f.Snapshot().Rows.Single().CancellationRequests == 2,
            "Actual false Move completion was not separate from prior cancellation requests.");
    }
    private static void Isolation()
    {
        using var f = new Fixture(); f.Start.Invoke(f.StartFrame()); f.Move.Original = _ => 0xff;
        f.Native.BeginLowerIl2CppAudit();
        try
        {
            Check(Task.Run(() => f.Move.Invoke(f.MoveFrame())).GetAwaiter().GetResult() == 0xff,
                "Worker dispatch changed the native result.");
            var foreign = f.MoveFrame(); foreign.Method = new(0x456789);
            Check(f.Move.Invoke(foreign) == 0xff, "Foreign MethodInfo alias changed the native result.");
        }
        finally { f.Native.EndLowerIl2CppAudit(); }
        Check(f.Native.LowerIl2CppCalls == 0 && f.Native.LowerWorkerIl2CppCalls == 0
            && f.Move.Originals == 2 && f.Snapshot().Unknown && f.Snapshot().Rows.Single().MoveCalls == 0,
            "Worker/foreign metadata fault read native receivers, skipped originals or fabricated captured Move calls.");
    }
    private static void ChangedNote()
    {
        using var f = new Fixture(); f.Start.Invoke(f.StartFrame()); var note = f.Snapshot().Rows.Single().Identity.Note;
        f.Native.HoldReplaceNote(f.Native.HoldIterator, 0);
        f.Move.Original = _ =>
        {
            Check(f.SynchronousIterator() == f.Native.HoldIterator.ToInt64(),
                "Lifecycle identity rejection removed the actual synchronous Hold iterator attribution.");
            return 0xff;
        };
        Check(f.Move.Invoke(f.MoveFrame()) == 0xff, "Changed identity changed the original result.");
        var snapshot = f.Snapshot();
        Check(snapshot.Unknown && snapshot.Records == 1 && snapshot.Rows.Single().Identity.Note == note
            && snapshot.NaturallyCompleted == 0 && !snapshot.FullCoverageKnown,
            "A retained iterator silently rebound to another actual NoteInformation.");
    }
    private static readonly List<Fixture> UnknownOwners = new();
    private static void UnknownFree()
    {
        using var f = new Fixture(); f.Start.Invoke(f.StartFrame());
        var target = f.Native.HoldIterator; var handle = f.Native.NoteViewHandlesFor(target).Single();
        f.Native.FailFreeHandle = handle; f.RetainStorage = true; UnknownOwners.Add(f);
        f.State.Scene("Play-retired"); f.Backend.TickHoldLifecycle(); var snapshot = f.Snapshot();
        Check(snapshot.Unknown && snapshot.UnknownRootFrees == 1 && snapshot.RootResponsibilities == 1
            && !snapshot.Accepting && f.Native.NoteViewFreeLog.Count(row => row.InjectedFailure && row.Handle == handle && row.Target == target) == 1,
            "Unknown native free lost its exact single handle/target attempt or persistent cleanup owner.");
        var attempts = f.Native.FreeHandleAttempts; f.Backend.TickHoldLifecycle(); f.Backend.Dispose(); f.Backend.Dispose();
        Check(attempts == f.Native.FreeHandleAttempts && f.Snapshot().UnknownRootFrees == 1,
            "A later tick/withdrawal retried an unknown native free.");
    }
}
