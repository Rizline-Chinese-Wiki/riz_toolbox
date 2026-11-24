using RizToolbox.Adapter.Gameplay;
using RizToolbox.Core;
using RizToolbox.Core.Diagnostics;
using RizToolbox.Sdk;

if (args.Length == 2 && args[0] == "--mapped-playback-stock-checks")
    return NativePlaybackStockMetadataChecks.RunMapped(args[1]);
if (args.Length == 6 && args[0] == "--mapped-playback-stock-group-checks")
    return NativePlaybackStockMetadataChecks.RunGroupMapped(args[1], args[2], args[3], args[4], args[5]);
if (args.Length == 5 && args[0] == "--mapped-gettime-consumer-operation-checks")
    return NativePlaybackStockMetadataChecks.RunGetTimeConsumerOperation(args[1], args[2], args[3], args[4]);

if (args.Length == 2 && args[0] == "--mapped-clock-code-checks") return ClockCodeProfileChecks.RunMapped(args[1]);
if (args.Length == 5 && args[0] == "--mapped-clock-hook-checks") return ClockCodeProfileChecks.RunHooked(args[1], args[2], args[3], args[4]);

if (args.Length == 1 && args[0] == "--control-observation-checks")
{
    var failures = 0;
    foreach (var check in ControlObservationChecks.All)
    {
        try { check.Run(); Console.WriteLine("PASS " + check.Name); }
        catch (Exception error) { failures++; Console.Error.WriteLine("FAIL " + check.Name + ": " + error); }
    }
    return failures == 0 ? 0 : 1;
}

if (args.Length == 1 && args[0] == "--seek-clock-checks")
{
    var failures = 0;
    foreach (var check in NativePlaybackSeekClockChecks.All)
    {
        try { check.Run(); Console.WriteLine("PASS " + check.Name); }
        catch (Exception error) { failures++; Console.Error.WriteLine("FAIL " + check.Name + ": " + error); }
    }
    return failures == 0 ? 0 : 1;
}

if (args.Length == 1 && args[0] == "--formal-playback-host-checks")
{
    var failures = 0;
    foreach (var check in NativePlaybackStockHostChecks.All)
    {
        try { check.Run(); Console.WriteLine("PASS " + check.Name); }
        catch (Exception error) { failures++; Console.Error.WriteLine("FAIL " + check.Name + ": " + error); }
    }
    return failures == 0 ? 0 : 1;
}

if (args.Length == 1 && args[0] == "--clock-profile-readiness-checks")
{
    var failures = 0;
    foreach (var check in ClockProfileReadinessChecks.All)
    {
        try { check.Run(); Console.WriteLine("PASS " + check.Name); }
        catch (Exception error) { failures++; Console.Error.WriteLine("FAIL " + check.Name + ": " + error); }
    }
    return failures == 0 ? 0 : 1;
}

if (args.Length == 1 && args[0] == "--actual-source-lifetime-checks")
{
    var failures = 0;
    foreach (var check in NativePlaybackActualSourceLifetimeChecks.All)
    {
        try { check.Run(); Console.WriteLine("PASS " + check.Name); }
        catch (Exception error) { failures++; Console.Error.WriteLine("FAIL " + check.Name + ": " + error); }
    }
    return failures == 0 ? 0 : 1;
}

var tests = new (string Name, Action Run)[]
{
    ("clock sources are independent raw snapshots with honest units", IndependentClocks),
    ("DSP and monotonic survive absence of gameplay controller", ClockWithoutController),
    ("clock read cancellation and wrong thread happen before backend access", ReadGuards),
    ("experimental control is explicit and leases are automatically owned", ExperimentalOwnership),
    ("pause and resume use fresh actual pause/play states with readback receipts", PauseResume),
    ("pause and skip do not require the paused-menu predicate while resume and retry do", PauseMenuPredicate),
    ("resume preparation is not mistaken for resumed chart playback", ResumePreparation),
    ("load time-ready over crash and UI transition each refuse control", InvalidStates),
    ("retry requires pause and ends command ownership without fake rollback", Retry),
    ("skip requires playing and ends command ownership without fake rollback", Skip),
    ("speed and seek remain unsupported without backend dispatch", UnsupportedControls),
    ("individual missing control signatures do not disable clock reads or other controls", IndependentControlProbe),
    ("two plugins conflict on control lease and release permits next owner", ExclusiveControl),
    ("controller rebuild expires old lease and creates a new generation", RebuiltController),
    ("controller absence expires ownership and releases retained native roots", MissingController),
    ("cancellation after acquire prevents dispatch and does not modify audit", ExecuteCancellation),
    ("cached scope access is refused after stop and Dispose never auto-resumes", ScopeStop),
    ("dispatch failure preserves unknown-side-effect audit and releases lease", DispatchFailure),
    ("readback failure remains distinct from successful command dispatch", ReadbackFailure),
    ("modification context and snapshot association never invent RunId", ModificationContext),
    ("audit storage is bounded per generation and does not cross rebuilt run", BoundedAudit),
    ("gameplay factory through formal PluginHost preserves ownership and refusal", HostIntegration),
    ("individual thrown clock reads remain isolated from other sources", ClockExceptions),
    ("control callback reentry cannot redispatch or replace active capture roots", Reentry),
    ("module stop during dispatch defers root and backend cleanup until return", StopDuringDispatch),
    ("plugin stop during dispatch returns honest receipt without stale readback", ScopeStopDuringDispatch),
    ("maintenance invalidates absent and rebuilt controllers without plugin reads", Maintenance),
    ("controller replacement invalidates leases even with retained information identity", ControllerReplacement),
    ("shared journal callback preserves successful and unknown-side-effect dispatch evidence", () => {
        var records = new List<RunModificationInput>(); using var f = new Fixture(record: records.Add);
        var lease = Need(f.Controls.Acquire(true)); Need(lease.Execute(GameplayControlAction.Pause));
        f.Backend.DispatchError = new IOException("partial effect"); Error(lease.Execute(GameplayControlAction.Resume), ErrorCode.InternalError);
        Check(records.Count == 2 && records[0].Outcome == RunModificationOutcome.Applied && records[1].Outcome == RunModificationOutcome.InvocationFailedSideEffectsUnknown
            && records.All(x => x.ObservedRunId is null && !x.SubmissionBehaviorVerified), "Journal callback fabricated dispatch success or an unknown run association.");
    }),
    ("throwing shared audit cannot replace a control receipt or invocation failure", () => {
        using var f = new Fixture(record: _ => throw new IOException("audit unavailable")); var lease = Need(f.Controls.Acquire(true)); Need(lease.Execute(GameplayControlAction.Pause));
        f.Backend.DispatchError = new IOException("original invocation"); Error(lease.Execute(GameplayControlAction.Resume), ErrorCode.InternalError);
        Check(f.Backend.Dispatches.Count == 2, "Audit callback skipped or retried a native dispatch.");
    })
};
tests = tests.Concat(ValueControlChecks.All).Concat(ObservedClockChecks.All)
    .Concat(ControlObservationChecks.All).Concat(ControlCommandChecks.All).Concat(ControlOperationDiagnosticChecks.All)
    .Concat(PreparedValueControlChecks.All).Concat(NativePlaybackChecks.All).Concat(NativePlaybackProviderChecks.All)
    .Concat(NativePlaybackOccurrenceProviderChecks.All).Concat(ClockProfileReadinessChecks.All)
    .Concat(NativePlaybackActualSourceLifetimeChecks.All)
    .Concat(NativePlaybackStockHostChecks.All)
    .Concat(NativePlaybackStockHostChecks.FilePreparationChecks)
    .Concat(NativePlaybackSeekClockChecks.All).ToArray();
var failed = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + test.Name + ": " + ex); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} gameplay primitive checks passed.");
return failed == 0 ? 0 : 1;

static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static T Need<T>(Result<T> result) { Check(result.Success && result.Value is not null, result.Error?.Message ?? "Missing value."); return result.Value!; }
static void Error<T>(Result<T> result, ErrorCode code) => Check(!result.Success && result.Error?.KnownCode == code, "Expected " + code + "; got " + result.Error);

static void IndependentClocks()
{
    using var f = new Fixture(); f.Backend.MissingClock = "nowRealTime";
    var snapshot = Need(f.Clocks.Read());
    Check(snapshot.GameTime.Value == 12.5 && snapshot.GameSyncedTime.Value == 13.5 && !snapshot.GameRealTime.Available
        && snapshot.AudioDspTime.Value == 200.125 && snapshot.HostMonotonicTime.Value >= 0, "Independent clock failed.");
    Check(snapshot.GameTime.Unit == GameplayClockUnit.RawGameUnits && snapshot.AudioDspTime.Unit == GameplayClockUnit.Seconds
        && snapshot.HostMonotonicTime.Unit == GameplayClockUnit.Seconds && !snapshot.SecondsMappingVerified, "Unproved game-seconds mapping declared.");
    Check(snapshot.Revision == 1 && Need(f.Clocks.Read()).Revision == 2 && snapshot.CaptureFinishedMonotonicSeconds >= snapshot.CaptureStartedMonotonicSeconds,
        "Clock snapshot revision/capture bounds wrong.");
}
static void ClockWithoutController()
{
    using var f = new Fixture(); f.Backend.CaptureError = new GameplayStateException(ErrorCode.TargetMissing, "no active controller");
    var snapshot = Need(f.Clocks.Read());
    Check(!snapshot.GameTime.Available && !snapshot.GameRealTime.Available && !snapshot.GameSyncedTime.Available
        && snapshot.AudioDspTime.Available && snapshot.HostMonotonicTime.Available && snapshot.ObservedRunId is null,
        "Missing gameplay blocked independent host/audio clocks or invented RunId.");
}
static void ReadGuards()
{
    using var f = new Fixture(); using var stop = new CancellationTokenSource(); stop.Cancel();
    Error(f.Clocks.Read(stop.Token), ErrorCode.Cancelled);
    Check(f.Backend.Captures == 0 && f.Backend.DspReads == 0, "Cancelled clock read touched backend.");
    Result<GameplayClockSnapshot>? result = null; var worker = new Thread(() => result = f.Clocks.Read()); worker.Start(); worker.Join();
    Error(result!, ErrorCode.WrongThread); Check(f.Backend.Captures == 0, "Wrong-thread read touched game.");
}
static void ExperimentalOwnership()
{
    using var f = new Fixture(); Error(f.Controls.Acquire(), ErrorCode.OperationUnsupported);
    Check(f.Backend.Captures == 0, "Implicit experimental acquire touched game.");
    var lease = Need(f.Controls.Acquire(true)); Check(f.Context.Resources.Count == 1 && lease.ControllerGeneration == 1, "Lease not scope-owned.");
    lease.Dispose(); lease.Dispose(); Check(f.Backend.Dispatches.Count == 0, "Dispose dispatched a reversible-looking command.");
}
static void PauseResume()
{
    using var f = new Fixture(); var lease = Need(f.Controls.Acquire(true));
    var paused = Need(lease.Execute(GameplayControlAction.Pause));
    Check(paused.Modification.Dispatched && paused.ImmediatePostconditionObserved == true && paused.Experimental && f.Backend.Paused, "Pause dispatch/readback receipt failed.");
    Error(lease.Execute(GameplayControlAction.Pause), ErrorCode.InvalidGameState);
    var resumed = Need(lease.Execute(GameplayControlAction.Resume));
    Check(resumed.ImmediatePostconditionObserved == true && !f.Backend.Paused && f.Backend.Dispatches.SequenceEqual(new[] { GameplayControlAction.Pause, GameplayControlAction.Resume }), "Resume dispatch failed.");
}
static void InvalidStates()
{
    using var f = new Fixture(); var lease = Need(f.Controls.Acquire(true));
    f.Backend.LoadOver = false; Error(lease.Execute(GameplayControlAction.Pause), ErrorCode.InvalidGameState); f.Backend.LoadOver = true;
    f.Backend.TimeReady = false; Error(lease.Execute(GameplayControlAction.Pause), ErrorCode.InvalidGameState); f.Backend.TimeReady = true;
    f.Backend.Over = true; Error(lease.Execute(GameplayControlAction.Pause), ErrorCode.InvalidGameState); f.Backend.Over = false;
    f.Backend.Crashed = true; Error(lease.Execute(GameplayControlAction.Pause), ErrorCode.InvalidGameState); f.Backend.Crashed = false;
    f.Backend.PauseError = new(ErrorCode.InvalidGameState, "countdown, blocker, active window or terminated pause UI");
    Error(lease.Execute(GameplayControlAction.Pause), ErrorCode.InvalidGameState); f.Backend.PauseError = null;
    f.Backend.Playing = false; Error(lease.Execute(GameplayControlAction.Pause), ErrorCode.InvalidGameState);
    Check(f.Backend.Dispatches.Count == 0 && Need(f.SampledControlState()).Modifications.Count == 0, "Invalid state produced side effects or audit.");
}
static void PauseMenuPredicate()
{
    using var f = new Fixture(); var lease = Need(f.Controls.Acquire(true));
    f.Backend.CanDo = false;
    Need(lease.Execute(GameplayControlAction.Pause));
    Error(lease.Execute(GameplayControlAction.Resume), ErrorCode.InvalidGameState);
    Error(lease.Execute(GameplayControlAction.Retry), ErrorCode.InvalidGameState);
    f.Backend.CanDo = true; Need(lease.Execute(GameplayControlAction.Resume));
    f.Backend.CanDo = false; Need(lease.Execute(GameplayControlAction.Skip));
    Check(f.Backend.Dispatches.SequenceEqual(new[] { GameplayControlAction.Pause, GameplayControlAction.Resume, GameplayControlAction.Skip }),
        "Paused-menu predicate blocked a playing command or allowed a refused menu command.");
}
static void ResumePreparation()
{
    using var f = new Fixture(); var lease = Need(f.Controls.Acquire(true));
    Need(lease.Execute(GameplayControlAction.Pause)); f.Backend.ResumeDeferred = true;
    var receipt = Need(lease.Execute(GameplayControlAction.Resume));
    Check(receipt.Modification.Dispatched && receipt.ImmediatePostconditionObserved == false && !f.Backend.Paused && !f.Backend.Playing,
        "Resume preparation was declared playing or the returned native dispatch was hidden.");
    Error(lease.Execute(GameplayControlAction.Pause), ErrorCode.InvalidGameState);
    Error(lease.Execute(GameplayControlAction.Skip), ErrorCode.InvalidGameState);
    f.Backend.Playing = true; Need(lease.Execute(GameplayControlAction.Pause));
    Check(f.Backend.Dispatches.Count == 3, "Countdown rejections dispatched chart commands.");
}
static void Retry()
{
    using var f = new Fixture(); var lease = Need(f.Controls.Acquire(true));
    Error(lease.Execute(GameplayControlAction.Retry), ErrorCode.InvalidGameState);
    Need(lease.Execute(GameplayControlAction.Pause)); var receipt = Need(lease.Execute(GameplayControlAction.Retry));
    Check(receipt.ImmediatePostconditionObserved is null && f.Backend.Dispatches.Last() == GameplayControlAction.Retry, "Retry false full-flow postcondition.");
    Error(lease.Execute(GameplayControlAction.Retry), ErrorCode.TargetExpired); lease.Dispose();
    Check(f.Backend.Dispatches.Count == 2, "Retry lease disposal repeated or reversed command.");
}
static void Skip()
{
    using var f = new Fixture(); var lease = Need(f.Controls.Acquire(true));
    Need(lease.Execute(GameplayControlAction.Pause)); Error(lease.Execute(GameplayControlAction.Skip), ErrorCode.InvalidGameState);
    Need(lease.Execute(GameplayControlAction.Resume)); Need(lease.Execute(GameplayControlAction.Skip));
    Error(lease.Execute(GameplayControlAction.Skip), ErrorCode.TargetExpired); Check(f.Backend.Dispatches.Count == 3, "Skip ran more than once.");
}
static void UnsupportedControls()
{
    using var f = new Fixture(); var lease = Need(f.Controls.Acquire(true));
    Error(lease.Execute(new GameplayControlRequest(GameplayControlAction.SetSpeed, lease.ControllerGeneration, lease.ObservedRunId, 1.25, GameplayControlValueUnit.PlaybackRateMultiplier)), ErrorCode.OperationUnsupported);
    Error(lease.Execute(new GameplayControlRequest(GameplayControlAction.Seek, lease.ControllerGeneration, lease.ObservedRunId, 10, GameplayControlValueUnit.GameSeconds)), ErrorCode.OperationUnsupported);
    Check(f.Backend.Dispatches.Count == 0, "Unsupported speed/seek called native code.");
}
static void IndependentControlProbe()
{
    using var f = new Fixture(GameplayControlAction.Retry);
    Check(Need(f.Clocks.Read()).GameTime.Available, "Missing control signature blocked clocks.");
    var lease = Need(f.Controls.Acquire(true)); Need(lease.Execute(GameplayControlAction.Pause));
    Error(lease.Execute(GameplayControlAction.Retry), ErrorCode.OperationUnsupported);
    Check(Need(f.SampledControlState()).Operations.Single(x => x.Action == GameplayControlAction.Resume).Supported, "One missing method disabled unrelated controls.");
}
static void ExclusiveControl()
{
    using var f = new Fixture(); using var other = new Context("other", f.Main);
    var first = Need(f.Controls.Acquire(true)); var otherView = f.Service.ControlsForPlugin(other);
    Error(otherView.Acquire(true), ErrorCode.Conflict); first.Dispose();
    var second = Need(otherView.Acquire(true)); Need(second.Execute(GameplayControlAction.Pause));
    f.SampleControls();
    Check(Need(otherView.GetState()).CurrentOwner == "other", "Exclusive ownership diagnosis wrong.");
}
static void RebuiltController()
{
    using var f = new Fixture(); var first = Need(f.Controls.Acquire(true)); var generation = first.ControllerGeneration;
    f.Backend.Identity++;
    Error(first.Execute(GameplayControlAction.Pause), ErrorCode.TargetExpired);
    var second = Need(f.Controls.Acquire(true)); Check(second.ControllerGeneration > generation, "Rebuilt run reused old generation.");
    Need(second.Execute(GameplayControlAction.Pause)); Check(f.Backend.Dispatches.Count == 1, "Old lease mutated rebuilt object.");
}
static void MissingController()
{
    using var f = new Fixture(); var lease = Need(f.Controls.Acquire(true));
    f.Backend.CaptureError = new GameplayStateException(ErrorCode.TargetMissing, "controller destroyed");
    Error(f.SampledControlState(), ErrorCode.TargetMissing); Check(f.Backend.CaptureDisposals == f.Backend.Captures, "Retained native capture roots leaked after target loss.");
    f.Backend.CaptureError = null; Error(lease.Execute(GameplayControlAction.Pause), ErrorCode.TargetExpired);
    Need(f.Controls.Acquire(true));
}
static void ExecuteCancellation()
{
    using var f = new Fixture(); var lease = Need(f.Controls.Acquire(true)); using var stop = new CancellationTokenSource(); stop.Cancel();
    var captures = f.Backend.Captures; Error(lease.Execute(GameplayControlAction.Pause, stop.Token), ErrorCode.Cancelled);
    Check(f.Backend.Dispatches.Count == 0 && f.Backend.Captures == captures && Need(f.SampledControlState()).Modifications.Count == 0, "Cancelled operation had game side effects.");
}
static void ScopeStop()
{
    using var f = new Fixture(); var lease = Need(f.Controls.Acquire(true)); Need(lease.Execute(GameplayControlAction.Pause));
    f.Context.Dispose(); Error(lease.Execute(GameplayControlAction.Resume), ErrorCode.ScopeExpired); Error(f.Clocks.Read(), ErrorCode.ScopeExpired);
    Check(f.Backend.Paused && f.Backend.Dispatches.Count == 1, "Stopping plugin auto-resumed or cached operation bypassed scope.");
}
static void DispatchFailure()
{
    using var f = new Fixture(); var lease = Need(f.Controls.Acquire(true)); f.Backend.DispatchError = new Exception("after partial effect");
    Error(lease.Execute(GameplayControlAction.Pause), ErrorCode.InternalError);
    var state = Need(f.SampledControlState()); Check(state.Modifications.Count == 1 && !state.Modifications[0].Dispatched
        && state.Modifications[0].Outcome.Contains("unknown") && state.CurrentOwner is null, "Failed dispatch lacks honest side-effect audit or ownership cleanup.");
}
static void ReadbackFailure()
{
    using var f = new Fixture(); var lease = Need(f.Controls.Acquire(true)); f.Backend.ReadbackError = true;
    var receipt = Need(lease.Execute(GameplayControlAction.Pause));
    Check(receipt.Modification.Dispatched && receipt.ImmediatePostconditionObserved is null && receipt.Evidence.Contains("unavailable"), "Postcondition error hid successful dispatch or claimed successful readback.");
}
static void ModificationContext()
{
    using var f = new Fixture(); Check(Need(f.Clocks.Read()).ObservedRunId is null, "RunId fabricated.");
    var lease = Need(f.Controls.Acquire(true)); var modification = Need(lease.Execute(GameplayControlAction.Pause)).Modification;
    Check(modification.Owner == "plugin" && modification.ObservedRunId is null && modification.ControllerGeneration == lease.ControllerGeneration
        && !modification.LocalResultBehaviorVerified && !modification.SubmissionBehaviorVerified, "Run modification context makes unverified claims.");
}
static void BoundedAudit()
{
    using var f = new Fixture(); var lease = Need(f.Controls.Acquire(true));
    for (var i = 0; i < 270; i++) Need(lease.Execute(i % 2 == 0 ? GameplayControlAction.Pause : GameplayControlAction.Resume));
    Check(Need(f.SampledControlState()).Modifications.Count == 256, "Audit storage is unbounded.");
    f.Backend.Identity++; Check(Need(f.SampledControlState()).Modifications.Count == 0, "Old run modifications crossed rebuilt identity.");
}
static void HostIntegration()
{
    using var f = new Fixture(); var caps = new CapabilityRegistry(new("test", "test", "test", "test")); var events = new EventHub(new Logger());
    using var host = new PluginHost(caps, f.Main, events, new Logger());
    caps.RegisterScoped<IGameplayClockService>(GameplayCapabilityIds.Clock, c => f.Service.ClockForPlugin(c));
    caps.RegisterScoped<IGameplayControlService>(GameplayCapabilityIds.Controls, c => f.Service.ControlsForPlugin(c));
    IGameplayControlLease? lease = null; IGameplayControlService? cached = null;
    host.Register(new Plugin(c =>
    {
        Check(Need(c.GetService<IGameplayClockService>(GameplayCapabilityIds.Clock).Service!.Read()).GameTime.Available, "Scoped clock proxy failed.");
        cached = c.GetService<IGameplayControlService>(GameplayCapabilityIds.Controls).Service!;
        lease = Need(cached.Acquire(true)); Need(lease.Execute(GameplayControlAction.Pause));
    }));
    Check(host.Enable("probe").Success, "Formal host plugin failed."); host.Disable("probe");
    Error(lease!.Execute(GameplayControlAction.Resume), ErrorCode.ScopeExpired);
    Error(cached!.Acquire(true), ErrorCode.ScopeExpired);
    Check(f.Backend.Dispatches.Count == 1, "Formal scoped service bypassed stop guard.");
}
static void ClockExceptions()
{
    using var f = new Fixture(); f.Backend.ThrowClock = "nowTime"; f.Backend.DspError = true;
    var state = Need(f.Clocks.Read());
    Check(!state.GameTime.Available && !state.AudioDspTime.Available && state.GameRealTime.Available
        && state.GameSyncedTime.Available && state.HostMonotonicTime.Available, "Thrown source erased independent clocks.");
}
static void Reentry()
{
    using var f = new Fixture(); var lease = Need(f.Controls.Acquire(true));
    f.Backend.OnDispatch = () =>
    {
        var captures = f.Backend.Captures; var released = f.Backend.CaptureDisposals;
        Error(lease.Execute(GameplayControlAction.Pause), ErrorCode.Conflict);
        Error(f.Controls.GetState(), ErrorCode.Conflict); Error(f.Controls.Acquire(true), ErrorCode.Conflict);
        Error(f.Clocks.Read(), ErrorCode.Conflict);
        Check(f.Backend.Captures == captures && f.Backend.CaptureDisposals == released, "Reentry replaced live roots.");
    };
    Need(lease.Execute(GameplayControlAction.Pause)); f.Backend.OnDispatch = null;
    Need(lease.Execute(GameplayControlAction.Resume)); Check(f.Backend.Dispatches.Count == 2, "Callback reentry redispatched a command.");
}
static void StopDuringDispatch()
{
    using var f = new Fixture(); var lease = Need(f.Controls.Acquire(true));
    f.Backend.OnDispatch = () =>
    {
        var released = f.Backend.CaptureDisposals; f.Service.Dispose();
        Check(f.Backend.CaptureDisposals == released && f.Backend.Disposals == 0, "Stop freed roots/backend before native dispatch returned.");
    };
    var receipt = Need(lease.Execute(GameplayControlAction.Pause));
    Check(receipt.Modification.Dispatched && receipt.ImmediatePostconditionObserved is null && f.Backend.Disposals == 1
        && f.Backend.CaptureDisposals == f.Backend.Captures, "Deferred cleanup or dispatch receipt incorrect.");
    Error(lease.Execute(GameplayControlAction.Resume), ErrorCode.ScopeExpired);
}
static void ScopeStopDuringDispatch()
{
    using var f = new Fixture(); var lease = Need(f.Controls.Acquire(true)); f.Backend.OnDispatch = f.Context.Dispose;
    var receipt = Need(lease.Execute(GameplayControlAction.Pause));
    Check(receipt.Modification.Dispatched && receipt.ImmediatePostconditionObserved is null && receipt.Evidence.Contains("Ownership ended"),
        "Plugin stop hid dispatched command or read stale owner state.");
    Error(lease.Execute(GameplayControlAction.Resume), ErrorCode.ScopeExpired);
}
static void Maintenance()
{
    using var f = new Fixture(); var lease = Need(f.Controls.Acquire(true));
    f.Backend.CaptureError = new GameplayStateException(ErrorCode.TargetMissing, "scene exit"); f.SampleControls();
    Check(f.Backend.Captures == f.Backend.CaptureDisposals, "Idle module did not release roots on scene exit.");
    Error(lease.Execute(GameplayControlAction.Pause), ErrorCode.TargetExpired);
    f.Backend.CaptureError = null; f.SampleControls(); var next = Need(f.Controls.Acquire(true));
    Check(next.ControllerGeneration > lease.ControllerGeneration, "Scene absence/reappearance did not create a new generation.");
}
static void ControllerReplacement()
{
    using var f = new Fixture(); var lease = Need(f.Controls.Acquire(true)); f.Backend.ControllerIdentity++;
    Error(lease.Execute(GameplayControlAction.Pause), ErrorCode.TargetExpired);
    Check(Need(f.Controls.Acquire(true)).ControllerGeneration > lease.ControllerGeneration, "Controller replacement reused an old lease.");
}

sealed class Fixture : IDisposable
{
    public MainThreadDispatcher Main = new(); public Backend Backend; public GameplayService Service; public Context Context;
    public IGameplayClockService Clocks; public IGameplayControlService Controls;
    public GameLifecycleSnapshot? Lifecycle;
    internal object? ControlSource;
    internal long HostTickSequence;
    public Fixture(GameplayControlAction? unsupported = null, Action<RunModificationInput>? record = null, bool values = false,
        Func<double>? monotonicSeconds = null, Func<long, long, string, long, bool>? matchesCurrentGameplayRun = null,
        CallbackPerformanceMonitor? performance = null, ILogger? log = null)
    {
        if (values) Lifecycle = new("controlled-session", 1, 1, "NormalMode", "controlled-run", "chart", RunLifecyclePhase.Playing, false, true, true, true, Array.Empty<OperationError>());
        Backend = new(unsupported, values);
        Service = new(Backend, Main, () => Lifecycle, record, monotonicSeconds, matchesCurrentGameplayRun,
            controlSource: () => ControlSource, performance: performance, log: log);
        Context = new("plugin", Main);
        Clocks = Service.ClockForPlugin(Context); Controls = Service.ControlsForPlugin(Context);
    }
    internal void SampleControls(bool requested = true)
        => Service.ObserveControlsAfterMaintenance(++HostTickSequence, HostTickSequence, requested);
    internal Result<GameplayControlSnapshot> SampledControlState()
    {
        SampleControls();
        return Controls.GetState();
    }
    public void Dispose() { Context.Dispose(); Service.Dispose(); Main.Dispose(); }
}
sealed class Backend : IGameplayBackend
{
    private readonly GameplayControlAction? _unsupported;
    internal readonly bool ValuesSupported;
    internal ValueBackendState Values = ValueBackendState.Initial;
    internal bool AudioReady = true, GameReady = true, RestoreFails, RestoreThrows, WrongConfirmationUnit, NonfiniteConfirmation, ConfirmationFails, MappingFails, WrongConversionUnit;
    internal int CommitFaultStage, Preparations, ValueCommits, ValueConfirmations, Restorations, RestorationReadbacks, PreparationDisposals, Conversions;
    internal Action? OnPrepare, OnValueCommit, OnValueConfirm, OnRestore, OnReadMapping, OnConvert;
    internal Func<GameplayControlRequest, GameplayPreparedControl>? PreparedOverride;
    internal string? NonfiniteClock;
    internal bool NonfiniteDsp, NonfiniteMapping;
    public long Identity = 1, ControllerIdentity = 1; public bool LoadOver = true, TimeReady = true, Playing = true, Paused, Over, Crashed, CanDo = true, ReadbackError, DspError, ResumeDeferred;
    public OperationError? PauseError;
    public Exception? PauseReadFailure;
    public string? MissingClock, ThrowClock; public Exception? CaptureError, DispatchError, DisposeError; public Action? OnDispatch, OnCapture, OnGraphCheck;
    public int Captures, CaptureDisposals, DspReads, Disposals, Probes; public List<GameplayControlAction> Dispatches = new();
    internal Func<GameplayControlAvailability, GameplayControlAvailability>? AvailabilityTransform;
    internal Exception? ProbeError;
    public Backend(GameplayControlAction? unsupported, bool values = false) => (_unsupported, ValuesSupported) = (unsupported, values);
    public GameplayProbe Probe()
    {
        Probes++; if (ProbeError is not null) throw ProbeError;
        return new(Enum.GetValues<GameplayControlAction>().Select(action =>
            new GameplayControlAvailability(action, action != _unsupported && (ValuesSupported || action is not (GameplayControlAction.SetSpeed or GameplayControlAction.Seek)),
                "controlled backend seam", new(ErrorCode.OperationUnsupported, "Operation unsupported."))
            { ValueUnits = action == GameplayControlAction.SetSpeed ? new[] { GameplayControlValueUnit.PlaybackRateMultiplier }
                : action == GameplayControlAction.Seek ? new[] { GameplayControlValueUnit.GameSeconds, GameplayControlValueUnit.ChartCoordinates } : Array.Empty<GameplayControlValueUnit>() })
            .Select(operation => AvailabilityTransform?.Invoke(operation) ?? operation).ToArray(), new Dictionary<string, string?>());
    }
    public GameplayCapture Capture() { if (CaptureError is not null) throw CaptureError; Captures++; OnCapture?.Invoke(); return new CaptureValue(this); }
    public GameplayClockReading ReadDsp() { DspReads++; if (DspError) throw new Exception("audio unavailable"); return new(true, NonfiniteDsp ? double.PositiveInfinity : 200.125, GameplayClockUnit.Seconds, "AudioSettings.dspTime"); }
    public void Dispose() { Disposals++; if (DisposeError is { } error) throw error; }
    private sealed class CaptureValue : GameplayCapture
    {
        private readonly Backend _owner; private bool _disposed;
        public CaptureValue(Backend owner) => _owner = owner;
        internal override long Identity => _owner.Identity;
        internal override long ControllerIdentity => _owner.ControllerIdentity;
        internal override bool CurrentGraphMatches { get { _owner.OnGraphCheck?.Invoke(); return true; } }
        internal override bool LoadOver => _owner.LoadOver;
        internal override bool TimeReady => _owner.TimeReady;
        internal override bool Playing => _owner.Playing;
        internal override bool Paused => _owner.Paused;
        internal override bool LevelOver => _owner.Over;
        internal override bool Crashed => _owner.Crashed;
        internal override bool CanDoOperation => _owner.CanDo;
        internal override OperationError? PauseReadinessError =>
            _owner.PauseReadFailure is { } error ? throw error : _owner.PauseError;
        internal override GameplayClockReading ReadClock(string field)
        {
            if (field == _owner.ThrowClock) throw new Exception("clock read failed");
            if (field == _owner.NonfiniteClock) return new(true, double.NaN, GameplayClockUnit.RawGameUnits, field);
            if (_owner.ValuesSupported && field != _owner.MissingClock)
                return new(true, field == "nowTime" ? _owner.Values.ChartPosition : _owner.Values.GamePosition,
                    field == "nowTime" ? GameplayClockUnit.ChartCoordinates : GameplayClockUnit.Seconds, field) { UnitEvidence = "controlled piecewise BPM consumer" };
            return field == _owner.MissingClock ? new(false, null, GameplayClockUnit.RawGameUnits, field, new(ErrorCode.ObservationUnavailable, "missing field"))
                : new(true, field == "nowTime" ? 12.5 : field == "nowRealTime" ? 13 : 13.5, GameplayClockUnit.RawGameUnits, field);
        }
        internal override GameplayPreparedControl Prepare(GameplayControlRequest request)
            => _owner.PreparedOverride?.Invoke(request) ?? (_owner.ValuesSupported && request.Action is GameplayControlAction.SetSpeed or GameplayControlAction.Seek
                ? ValuePreparation.Prepare(_owner, request) : base.Prepare(request));
        internal override GameplayTimeConversion ConvertTime(GameplayTimeConversionRequest request)
        {
            if (!_owner.ValuesSupported) return base.ConvertTime(request);
            _owner.Conversions++;
            _owner.OnConvert?.Invoke();
            var converted = request.SourceUnit == request.TargetUnit ? request.Value
                : request.SourceUnit == GameplayClockUnit.Seconds ? ValueBackendState.ToChart(request.Value) : ValueBackendState.ToSeconds(request.Value);
            return new(request, new(true, converted, _owner.WrongConversionUnit ? GameplayClockUnit.RawGameUnits : request.TargetUnit, "controlled actual piecewise conversion"), "Controlled consumer conversion; no DSP/host calibration.");
        }
        internal override GameplayClockMappingSnapshot? ReadMapping(GameplayClockReading game)
        {
            if (!_owner.ValuesSupported) return null;
            _owner.OnReadMapping?.Invoke();
            if (_owner.MappingFails) throw new InvalidOperationException("mapping metadata unavailable");
            return new(new(true, _owner.NonfiniteMapping ? double.NaN : ValueBackendState.ToSeconds(game.Value!.Value), GameplayClockUnit.Seconds, "controlled ChartTimeToRealTime"),
                new(true, _owner.Values.GamePosition, GameplayClockUnit.Seconds, "controlled progress"),
                new(true, _owner.Values.Offset, GameplayClockUnit.Seconds, "controlled applied offset"),
                new(true, .35, GameplayClockUnit.Seconds, "controlled chart delay"), Playing, Paused, "Controlled sample; no cross-clock rates or latency are verified.");
        }
        internal override void Dispatch(GameplayControlAction action)
        {
            _owner.Dispatches.Add(action); _owner.OnDispatch?.Invoke(); if (_owner.DispatchError is not null) throw _owner.DispatchError;
            if (action == GameplayControlAction.Pause) { _owner.Paused = true; _owner.Playing = false; }
            if (action == GameplayControlAction.Resume) { _owner.Paused = false; _owner.Playing = !_owner.ResumeDeferred; }
        }
        internal override bool? Postcondition(GameplayControlAction action)
        { if (_owner.ReadbackError) throw new Exception("readback failed"); return action switch { GameplayControlAction.Pause => _owner.Paused && !_owner.Playing, GameplayControlAction.Resume => !_owner.Paused && _owner.Playing, _ => null }; }
        public override void Dispose() { if (_disposed) return; _disposed = true; _owner.CaptureDisposals++; }
    }
}
sealed class Logger : ILogger { public void Log(LogLevel level, string message, Exception? exception = null) { } }
sealed class Context : IPluginContext, IMainThreadDispatcher, IDisposable
{
    private readonly MainThreadDispatcher _main; private bool _active = true;
    public List<IDisposable> Resources = new(); public string PluginId { get; }
    public Context(string id, MainThreadDispatcher main) { PluginId = id; _main = main; }
    public ILogger Log { get; } = new Logger(); public IMainThreadDispatcher MainThread => this;
    internal IEventBus? TestEvents;
    public IEventBus Events => TestEvents ?? throw new NotSupportedException(); public GameBuildDescriptor GameBuild => new("test", "test", "test", "test");
    public CapabilityDescriptor GetCapability(string id) => throw new NotSupportedException();
    public ServiceLookup<T> GetService<T>(string id) where T : class => throw new NotSupportedException();
    public T Own<T>(T resource) where T : IDisposable { VerifyAccess(); Resources.Add(resource); return resource; }
    public bool IsMainThread => _main.IsMainThread;
    public void VerifyAccess() { _main.VerifyAccess(); if (!_active) throw new ObjectDisposedException(PluginId); }
    public Task InvokeAsync(Action action, CancellationToken token = default) => _main.InvokeAsync(() => { VerifyAccess(); action(); }, token);
    public void Dispose() { _main.VerifyAccess(); if (!_active) return; _active = false; foreach (var item in Resources.AsEnumerable().Reverse()) item.Dispose(); }
}
sealed class Plugin : IRizPlugin
{
    private readonly Action<IPluginContext> _enable;
    public Plugin(Action<IPluginContext> enable) => _enable = enable;
    public PluginManifest Manifest { get; } = new("probe", new(1, 0), new(1, 0));
    public void Enable(IPluginContext context) => _enable(context); public void Disable() { }
}
