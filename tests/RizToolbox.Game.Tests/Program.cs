using System.Text.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using RizToolbox.Adapter;
using RizToolbox.Adapter.Modules;
using RizToolbox.Runtime;
using RizToolbox.Core;
using RizToolbox.Sdk;

var checks = new (string Name, Action Run)[]
{
    ("Best-score snapshots reject a manager replaced while GetAllLevel returns", ScoreCaptureManagerChange),
    ("A loading catalog waits for LevelControl even when assets are ready", () => ScoreSourceLoading(false)),
    ("A loading catalog waits for the first level list without publishing empty scores", () => ScoreSourceLoading(true)),
    ("Data revisions reject a LevelControl replaced during traversal and retain only the stale prior page", ScoreRevisionControlChange),
    ("A first data revision waits when the asset source changes during capture", () => ScoreRevisionAssetChange(replaceAssetManager: true)),
    ("A first data revision waits when the asset source loses readiness during capture", () => ScoreRevisionAssetChange(replaceAssetManager: false)),
    ("Run-bound native properties require the exact observed controller information pair even for the same chart", () => {
        var state=Ready();state.Scene("Level");state.Controller(101,true,false,"same-chart",201);
        var run=state.RunId!;var scene=state.SceneGeneration;
        Check(state.MatchesCurrentGameplayRun(101,201,run,scene));
        Check(!state.MatchesCurrentGameplayRun(102,201,run,scene));
        Check(!state.MatchesCurrentGameplayRun(101,202,run,scene));
        Check(!state.MatchesCurrentGameplayRun(101,201,"old-run",scene));
        Check(!state.MatchesCurrentGameplayRun(101,201,run,scene+1));
        state.ObservePause(true);Check(state.MatchesCurrentGameplayRun(101,201,run,scene));
        state.Controller(102,true,false,"same-chart",202);
        Check(!state.MatchesCurrentGameplayRun(101,201,run,scene));
        Check(!state.MatchesCurrentGameplayRun(102,202,run,scene));
        Check(state.MatchesCurrentGameplayRun(102,202,state.RunId!,scene));
    }),
    ("Run-bound native properties stop during observation loss or completion and reject worker use", () => {
        foreach(var boundary in new[]{"unavailable","missing","over","scene"}){
            var state=Ready();state.Scene("Level");state.Controller(101,true,false,"chart",201);var run=state.RunId!;var scene=state.SceneGeneration;
            if(boundary=="unavailable")state.SetAvailability(runs:false);
            if(boundary=="missing")state.Controller(0,false,false,null);
            if(boundary=="over")state.Controller(101,true,true,"chart",201);
            if(boundary=="scene")state.Scene("Other");
            Check(!state.MatchesCurrentGameplayRun(101,201,run,scene));
        }
        var workerState=Ready();workerState.Scene("Level");workerState.Controller(101,true,false,"chart",201);
        Task.Run(()=>Throws<InvalidOperationException>(()=>workerState.MatchesCurrentGameplayRun(101,201,workerState.RunId!,workerState.SceneGeneration))).GetAwaiter().GetResult();
    }),
    ("Same-chart old result cannot end a replacement run awaiting a result or temporarily missing", ResultOriginOldRun),
    ("An exact terminal result return reaches run events without an active result screen", ReturnedResultWithoutScreen),
    ("A rooted terminal result survives loss of the controller between two polls", ResultReturnedBetweenPolls),
    ("Losing the controller with an uncopied original return leaves the run open", () =>
    {
        var state = Ready(); state.Scene("Level"); state.ResultOrigins.Activate();
        state.Controller(101, true, false, "chart", 201);
        try
        {
            Check(state.ResultOrigins.Register(CaptureOrigin(state, 101, 201), new ResultTestRetention(301, 101, 201), out _));
            var changes = Changes(state);
            state.Scene("LevelList"); state.ControllerUnavailable();
            Check(state.FinishTerminalReturn() is null && state.ResultOrigins.PendingCount == 1);
            Check(changes.All(c => c.Kind != GameLifecycleChangeKind.RunEnded));
        }
        finally { state.ResultOrigins.Withdraw("fixture end"); }
    }),
    ("Terminal return delivery requires a completed snapshot for the exact current run", TerminalReturnRequiresExactRun),
    ("Ambiguous completed returns wait for exact result screen identity", TerminalReturnAmbiguity),
    ("A replacement controller sharing LevelInformation requires its own start and result origin", SharedInformationReplacement),
    ("Judgement attribution refuses another controller sharing the current information", JudgementControllerIdentity),
    ("Original-return provenance survives entry run replacement without borrowing the current run", ResultOriginEntryBoundary),
    ("Unknown result origins never fall back to chart matching after over or disappearance", ResultOriginUnknown),
    ("Matching result origin survives revision scene changes and controller teardown", ResultOriginSceneAndTeardown),
    ("Withdrawing result origins releases roots and rejects old in-flight and consumed generations", ResultOriginWithdrawal),
    ("Result identities are bounded rooted and duplicate conflict aware", ResultOriginRetentionBounds),
    ("Result origin capture requires observed start and exact sampled controller information pair", ResultOriginCaptureGuards),
    ("Verified Skip is deferred until after audit and publishes Skipped without inventing a result", () => {
        var state = Ready(); state.Scene("Level"); var start = state.Controller(101, true, false, "chart")!; var changes = Changes(state);
        var queue = new VerifiedRunCancellationQueue(state); var sequence = new List<string>();
        state.Subscribe(change => { if (change.Kind == GameLifecycleChangeKind.RunEnded) sequence.Add("ended"); });
        Check(queue.Enqueue(state.CaptureCancellationToken(101)!, "controlled accepted Skip edge", RunEndReason.Skipped)
            && state.IsPlaying && changes.Count == 0, "Skip observation synchronously closed the run.");
        sequence.Add("SDK dispatch audit"); Equal(1, queue.Drain()); Equal(0, queue.Drain());
        var ended = changes.Single();
        Check(sequence.SequenceEqual(new[] { "SDK dispatch audit", "ended" }) && ended.Kind == GameLifecycleChangeKind.RunEnded
            && ended.EndReason == RunEndReason.Skipped && ended.RelatedRunId == start.RunId && ended.Result is null
            && ended.Observation.Source == LifecycleObservationSource.VerifiedCallback && ended.Observation.SamplingIntervalMilliseconds == 0
            && state.Phase == RunLifecyclePhase.Finished, "Skip was relabeled Aborted, fabricated a result, or lost exact source metadata.");
    }),
    ("Direct terminal evidence accepts only Aborted or Skipped and leaves unsupported reasons untouched", () => {
        foreach (var invalid in new[] { RunEndReason.Unknown, RunEndReason.Completed, RunEndReason.Failed, (RunEndReason)1234 }) {
            var state = Ready(); state.Scene("Level"); state.Controller(101, true, false, "chart", 201); var changes = Changes(state);
            var token = state.CaptureCancellationToken(101)!; var queue = new VerifiedRunCancellationQueue(state);
            Check(!queue.Enqueue(token, "invalid reason fixture", invalid) && queue.Count == 0
                && !state.EndFromVerifiedCallback(token, invalid, "invalid direct reason fixture") && state.IsPlaying
                && state.CaptureCancellationToken(101) == token && changes.Count == 0, "Direct callback fabricated a completion/failure or consumed the run token.");
        }
    }),
    ("One queued terminal token cannot be overwritten by a conflicting Skip or Abort reason", () => {
        foreach (var reason in new[] { RunEndReason.Skipped, RunEndReason.Aborted }) {
            var state = Ready(); state.Scene("Level"); state.Controller(101, true, false, "chart"); var changes = Changes(state);
            var token = state.CaptureCancellationToken(101)!; var queue = new VerifiedRunCancellationQueue(state);
            var other = reason == RunEndReason.Skipped ? RunEndReason.Aborted : RunEndReason.Skipped;
            Check(queue.Enqueue(token, "first actual edge", reason) && queue.Enqueue(token, "duplicate same edge", reason)
                && !queue.Enqueue(token, "conflicting classification", other) && queue.Count == 1, "Terminal reason was overwritten or duplicate work grew the queue.");
            Equal(1, queue.Drain()); Check(changes.Single().EndReason == reason, "Drain changed the first verified native classification.");
        }
    }),
    ("Queued Skip cannot override a later normal result or an already-observed over boundary", () => {
        foreach (var result in new[] { false, true }) {
            var state = Ready(); state.Scene("Level"); state.Controller(101, true, false, "chart"); var changes = Changes(state);
            var queue = new VerifiedRunCancellationQueue(state);
            Check(queue.Enqueue(state.CaptureCancellationToken(101)!, "controlled Skip candidate", RunEndReason.Skipped), "Candidate missing.");
            state.Controller(101, true, true, "chart", 201); if (result) FinishKnown(state, Result("chart", false), 101, 201);
            Equal(0, queue.Drain());
            Check(!changes.Any(x => x.EndReason == RunEndReason.Skipped)
                && (result ? changes.Single(x => x.Kind == GameLifecycleChangeKind.RunEnded).EndReason == RunEndReason.Completed
                    : state.Phase == RunLifecyclePhase.AwaitingResult), "Stale Skip evidence overrode a natural termination boundary.");
        }
    }),
    ("Queued Skip from a previous scene run or revision never ends replacement playback", () => {
        foreach (var boundary in new[] { "scene", "run", "revision", "withdraw" }) {
            var state = Ready(); state.Scene("Level"); state.Controller(101, true, false, "chart"); var changes = Changes(state);
            var queue = new VerifiedRunCancellationQueue(state); var old = state.CaptureCancellationToken(101)!;
            Check(queue.Enqueue(old, "controlled old Skip", RunEndReason.Skipped), "Candidate missing.");
            if (boundary == "scene") state.Scene("Other");
            if (boundary == "run") state.Controller(102, true, false, "chart");
            if (boundary == "revision") state.ObservePause(true);
            if (boundary == "withdraw") queue.Clear();
            Equal(0, queue.Drain()); Check(!changes.Any(x => x.Kind == GameLifecycleChangeKind.RunEnded), "Stale or withdrawn Skip was replayed into current state.");
        }
    }),
    ("Late result after direct Skip remains unassociated and cannot emit a second terminal event", () => {
        var state = Ready(); state.Scene("Level"); state.Controller(101, true, false, "same"); var changes = Changes(state);
        Check(state.EndFromVerifiedCallback(state.CaptureCancellationToken(101)!, RunEndReason.Skipped, "controlled Skip edge"), "Skip did not close the old run.");
        var late = state.Finish(Result("same", false, true));
        Check(late.RunId is null && !late.AssociatedWithObservedStart && changes.Count(x => x.Kind == GameLifecycleChangeKind.RunEnded) == 1
            && changes.Last().Kind == GameLifecycleChangeKind.ResultObserved, "Late result invented another completion for the skipped run.");
        var next = state.Controller(102, true, false, "same")!; var secondLate = state.Finish(Result("same", false, true));
        Check(secondLate.RunId is null && state.IsPlaying && state.RunId == next.RunId
            && changes.Count(x => x.Kind == GameLifecycleChangeKind.RunEnded) == 1, "Old same-chart result ended the new live run.");
    }),
    ("Direct Skip still requires observed start exact token evidence and one terminal edge", () => {
        var state = Ready(); state.Scene("Level"); state.Controller(101, false, false, "chart");
        var invented = new ObservedRunCancellationToken(state.SessionId, "not-started", state.SceneGeneration, state.Revision, 101);
        Check(!state.EndFromVerifiedCallback(invented, RunEndReason.Skipped, "unobserved fixture"), "Skip fabricated an observed start.");
        state.Controller(101, true, false, "chart"); var token = state.CaptureCancellationToken(101)!;
        Check(!state.EndFromVerifiedCallback(token with { Information = 102 }, RunEndReason.Skipped, "foreign object")
            && !state.EndFromVerifiedCallback(token, RunEndReason.Skipped, " ")
            && state.EndFromVerifiedCallback(token, RunEndReason.Skipped, "verified edge fixture")
            && !state.EndFromVerifiedCallback(token, RunEndReason.Skipped, "duplicate"), "Direct Skip bypassed identity/evidence/one-shot requirements.");
    }),
    ("Verified Skipped lifecycle invalidates the matching Run scope before ordinary listeners once", () => {
        using var main = new MainThreadDispatcher(); var logger = new TestLogger(); var events = new EventHub(logger);
        var capabilities = new CapabilityRegistry(new("test", "test", "test", "test")); var state = Ready();
        state.BindPublisher(events, events.Publish);
        capabilities.RegisterScoped<IGameLifecycleService>(GameLifecycleCapabilityIds.Lifecycle, state.ForPlugin);
        state.Scene("Level"); state.Controller(101, true, false, "chart");
        using var host = new PluginHost(capabilities, main, events, logger); IPluginScope? run = null; var listener = 0; var cancelled = 0;
        host.Register(new LifecyclePlugin("skip-scope", context => {
            run = ((IPluginScope)context).CreateScope("skip-run", ScopeLifetime.Run);
            context.Events.Subscribe<GameLifecycleChange>(change => {
                if (change.Kind != GameLifecycleChangeKind.RunEnded) return;
                Check(change.EndReason == RunEndReason.Skipped && !run.IsActive && run.Lifetime.IsCancellationRequested,
                    "Ordinary terminal listener saw a live Run scope or wrong terminal reason."); listener++;
            });
        }));
        Check(host.Enable("skip-scope").Success && run is { IsActive: true }, "Run-scope fixture did not bind to the live run.");
        using var cancellation = run!.Lifetime.Register(() => cancelled++);
        var queue = new VerifiedRunCancellationQueue(state);
        Check(queue.Enqueue(state.CaptureCancellationToken(101)!, "controlled native Skip edge", RunEndReason.Skipped), "Skip fixture was not queued.");
        Equal(1, queue.Drain()); Equal(0, queue.Drain());
        Check(listener == 1 && cancelled == 1 && !run.IsActive, "Skipped lifecycle omitted, delayed, or repeated scope cancellation.");
    }),
    ("Back tracker retains one request without ending a run until a complete terminal edge", () => {
        var state = Ready(); state.Scene("Level"); state.Controller(101, true, false, "chart"); var changes = Changes(state);
        var tracker = new BackCancellationTracker(state.CaptureCancellationToken); var lease = new BackTestLease();
        var identity = new BackCancellationIdentity(state.CaptureCancellationToken(101)!, 201, 301);
        Check(tracker.Accept(identity, lease) && tracker.Count == 1 && lease.Disposals == 0 && state.IsPlaying, "Request acceptance ended playback or lost retention.");
        var edge = tracker.BeginUpdate(201, 301, 101, false) ?? throw new InvalidOperationException("Accepted Back request did not create an update edge.");
        Check(tracker.CompleteUpdate(edge, false, true) is null && tracker.CompleteUpdate(edge, true, false) is null
            && tracker.Count == 1 && !changes.Any(x => x.Kind == GameLifecycleChangeKind.RunEnded), "An incomplete edge fabricated termination.");
        var terminal = tracker.CompleteUpdate(edge, true, true)!; var queue = new VerifiedRunCancellationQueue(state);
        Check(terminal is not null && lease.Disposals == 1 && tracker.Count == 0 && state.IsPlaying && queue.Enqueue(terminal, "controlled Back terminal fixture"), "Completed helper edge lost token or dispatched synchronously.");
        Equal(1, queue.Drain()); Equal(0, queue.Drain());
        Check(changes.Count(x => x.Kind == GameLifecycleChangeKind.RunEnded && x.EndReason == RunEndReason.Aborted) == 1
            && tracker.CompleteUpdate(edge, true, true) is null, "Terminal helper edge repeated its run end.");
    }),
    ("Back pending identity survives normal revisions and terminal edge captures the newest queue token", () => {
        var state = Ready(); state.Scene("Level"); state.Controller(101, true, false, "chart"); var original = state.CaptureCancellationToken(101)!;
        var tracker = new BackCancellationTracker(state.CaptureCancellationToken); var lease = new BackTestLease();
        Check(tracker.Accept(new(original, 201, 301), lease), "Request not retained.");
        state.ObservePause(true); tracker.Prune(); Check(tracker.Count == 1 && lease.Disposals == 0, "Pause baseline revision dropped the accepted request.");
        var edge = tracker.BeginUpdate(201, 301, 101, false)!;
        state.SetAvailability(scenes: false, reason: "independent scene observer fixture"); tracker.Prune();
        var latest = tracker.CompleteUpdate(edge, true, true)!; var queue = new VerifiedRunCancellationQueue(state);
        Check(latest is not null && latest.Revision > original.Revision && latest == state.CaptureCancellationToken(101)
            && queue.Enqueue(latest, "fresh terminal fixture") && queue.Drain() == 1 && lease.Disposals == 1,
            "Terminal edge reused the old Back revision and was lost by the strict cancellation queue.");
    }),
    ("Back duplicate and foreign identities remain bounded and release rejected ownership", () => {
        var state = Ready(); state.Scene("Level"); state.Controller(101, true, false, "chart"); var token = state.CaptureCancellationToken(101)!;
        var tracker = new BackCancellationTracker(state.CaptureCancellationToken); var retained = new BackTestLease(); var duplicate = new BackTestLease();
        Check(tracker.Accept(new(token, 201, 301), retained) && !tracker.Accept(new(token, 201, 301), duplicate), "Duplicate Back request replaced the pending owner.");
        Check(duplicate.Disposals == 1 && retained.Disposals == 0 && tracker.Count == 1
            && tracker.BeginUpdate(202, 301, 101, false) is null && tracker.BeginUpdate(201, 302, 101, false) is null
            && tracker.BeginUpdate(201, 301, 102, false) is null && tracker.BeginUpdate(201, 301, 101, true) is null,
            "Foreign controller/pause/info or already-terminated sample acquired an edge.");
        tracker.Clear(); tracker.Clear(); Equal(1, retained.Disposals);
        var foreign = new BackTestLease(); Check(!tracker.Accept(new(token with { SessionId = "foreign" }, 201, 301), foreign)
            && foreign.Disposals == 1 && tracker.Count == 0, "Foreign session was retained.");
    }),
    ("Back pending roots retire on scene run or completed-result boundaries without inventing cancellation", () => {
        foreach (var boundary in new[] { "scene", "run", "result", "release" }) {
            var state = Ready(); state.Scene("Level"); state.Controller(101, true, false, "chart"); var changes = Changes(state);
            var tracker = new BackCancellationTracker(state.CaptureCancellationToken); var lease = new BackTestLease();
            Check(tracker.Accept(new(state.CaptureCancellationToken(101)!, 201, 301), lease), "Boundary fixture did not retain.");
            var edge = tracker.BeginUpdate(201, 301, 101, false)!;
            if (boundary == "scene") state.Scene("LevelList");
            if (boundary == "run") state.Controller(102, true, false, "chart");
            if (boundary == "result") { state.Controller(101, true, true, "chart", 201); FinishKnown(state, Result("chart", true), 101, 201); }
            if (boundary == "release") tracker.Clear();
            tracker.Prune();
            Check(tracker.Count == 0 && lease.Disposals == 1 && tracker.CompleteUpdate(edge, true, true) is null
                && !changes.Any(x => x.EndReason == RunEndReason.Aborted), "Boundary retained roots or manufactured an Aborted event.");
        }
    }),
    ("Back pending disappearance and a terminated baseline do not synthesize a terminal edge", () => {
        var state = Ready(); state.Scene("Level"); state.Controller(101, true, false, "chart"); var changes = Changes(state);
        var tracker = new BackCancellationTracker(state.CaptureCancellationToken); var lease = new BackTestLease();
        Check(tracker.Accept(new(state.CaptureCancellationToken(101)!, 201, 301), lease), "Request not retained.");
        state.ControllerUnavailable(); tracker.Prune();
        Check(state.Phase == RunLifecyclePhase.ObservationLost && tracker.BeginUpdate(201, 301, 101, true) is null
            && !changes.Any(x => x.Kind == GameLifecycleChangeKind.RunEnded), "Loss or an initial terminated=true sample was relabeled cancellation.");
        tracker.Clear(); Equal(1, lease.Disposals);
    }),
    ("Back late old update cannot consume a new same-chart pending request", () => {
        var state = Ready(); state.Scene("Level"); state.Controller(101, true, false, "chart");
        var tracker = new BackCancellationTracker(state.CaptureCancellationToken); var oldLease = new BackTestLease();
        Check(tracker.Accept(new(state.CaptureCancellationToken(101)!, 201, 301), oldLease), "Old request missing.");
        var old = tracker.BeginUpdate(201, 301, 101, false)!; state.Controller(102, true, false, "chart"); tracker.Prune();
        var next = state.CaptureCancellationToken(102)!; var nextLease = new BackTestLease();
        Check(tracker.Accept(new(next, 201, 301), nextLease) && tracker.CompleteUpdate(old, true, true) is null
            && tracker.Count == 1 && oldLease.Disposals == 1 && nextLease.Disposals == 0, "Old edge consumed replacement ownership.");
        var current = tracker.BeginUpdate(201, 301, 102, false)!;
        Check(tracker.CompleteUpdate(current, true, true) == next && nextLease.Disposals == 1, "New same-chart request lost its own identity.");
    }),
    ("Back no observed start or stale scene token cannot retain roots", () => {
        var state = Ready(); state.Scene("Level"); state.Controller(101, false, false, "chart");
        var tracker = new BackCancellationTracker(state.CaptureCancellationToken); var unstarted = new BackTestLease();
        var invented = new ObservedRunCancellationToken(state.SessionId, "unobserved", state.SceneGeneration, state.Revision, 101);
        Check(!tracker.Accept(new(invented, 201, 301), unstarted) && unstarted.Disposals == 1, "Unobserved start retained a cancellation request.");
        state.Controller(101, true, false, "chart"); var old = state.CaptureCancellationToken(101)!; state.Scene("Other"); var stale = new BackTestLease();
        Check(!tracker.Accept(new(old, 201, 301), stale) && stale.Disposals == 1 && state.IsPlaying, "Stale scene was accepted or ended playback.");
    }),
    ("Back cleanup failure clears ownership and cannot block future requests", () => {
        var state = Ready(); state.Scene("Level"); state.Controller(101, true, false, "chart"); var failures = 0;
        var tracker = new BackCancellationTracker(state.CaptureCancellationToken, _ => failures++);
        var broken = new BackTestLease(() => throw new IOException("cleanup fixture"));
        Check(tracker.Accept(new(state.CaptureCancellationToken(101)!, 201, 301), broken), "Broken cleanup fixture missing.");
        tracker.Clear(); tracker.Clear(); state.Controller(102, true, false, "chart"); var next = new BackTestLease();
        Check(failures == 1 && broken.Disposals == 1 && tracker.Accept(new(state.CaptureCancellationToken(102)!, 202, 302), next), "Cleanup failure retained stale ownership.");
        tracker.Clear(); Equal(1, next.Disposals);
    }),
    ("Accepted cancellation drains once after command audit and before sampled scene changes", () => {
        var state = new GameEventState(); state.Scene("Level"); state.Controller(101, true, false, "chart");
        var token = state.CaptureCancellationToken(101)!; var queue = new VerifiedRunCancellationQueue(state);
        var sequence = new List<string>(); state.Subscribe(change => { if (change.Kind == GameLifecycleChangeKind.RunEnded) sequence.Add("ended"); });
        Check(queue.Enqueue(token, "accepted original Retry") && queue.Enqueue(token, "duplicate") && queue.Count == 1 && state.IsPlaying,
            "Native observation synchronously closed the run or duplicated its pending cancellation.");
        sequence.Add("command audit"); Equal(1, queue.Drain()); state.Scene("ToLevel"); Equal(0, queue.Drain());
        Check(sequence.SequenceEqual(new[] { "command audit", "ended" }) && queue.Count == 0, "Audit/end ordering or one-shot cancellation failed.");
    }),
    ("Queued cancellation is discarded after run replacement or module withdrawal", () => {
        var state = new GameEventState(); state.Scene("Level"); state.Controller(101, true, false, "chart");
        var queue = new VerifiedRunCancellationQueue(state); var first = state.CaptureCancellationToken(101)!;
        Check(queue.Enqueue(first, "old invocation"), "Current token refused."); state.Controller(102, true, false, "chart");
        Equal(0, queue.Drain()); Check(state.IsPlaying, "Old queued cancellation ended replacement playback.");
        Check(queue.Enqueue(state.CaptureCancellationToken(102)!, "accepted"), "Fresh token refused."); queue.Clear(); Equal(0, queue.Drain());
        Check(!queue.Enqueue(first, "stale") && queue.Count == 0 && state.IsPlaying, "Released or stale work was replayed.");
    }),
    ("Verified cancellation tokens reject stale lifecycle revisions and information identities", () => {
        var state = new GameEventState(); state.Scene("Level"); state.Controller(101, true, false, "chart");
        Check(state.CaptureCancellationToken(0) is null && state.CaptureCancellationToken(102) is null, "Unobserved native information acquired a run token.");
        var old = state.CaptureCancellationToken(101)!; state.ObservePause(true);
        Check(!state.AbortFromVerifiedCallback(old, "stale invocation"), "A revised run accepted the old token.");
        var current = state.CaptureCancellationToken(101)!;
        Check(!state.AbortFromVerifiedCallback(current, " ") && state.AbortFromVerifiedCallback(current, "accepted original Retry edge"), "Valid cancellation did not require evidence.");
        Check(!state.AbortFromVerifiedCallback(current, "duplicate") && state.Phase == RunLifecyclePhase.Finished, "Cancellation repeated or stayed open.");
    }),
    ("Verified cancellation cannot end a rebuilt or different-session run", () => {
        var state = new GameEventState(); state.Scene("Level"); state.Controller(101, true, false, "chart");
        var first = state.CaptureCancellationToken(101)!; state.Controller(102, true, false, "chart");
        var replacement = state.RunId;
        Check(!state.AbortFromVerifiedCallback(first, "late old native return") && state.RunId == replacement && state.IsPlaying, "Old native return ended a replacement run.");
        var fresh = state.CaptureCancellationToken(102)!;
        Check(!state.AbortFromVerifiedCallback(fresh with { SessionId = "other" }, "foreign session"), "Foreign session token was accepted.");
        state.Scene("ToLevel"); Check(!state.AbortFromVerifiedCallback(fresh, "late scene return"), "Old scene token was accepted.");
    }),
    ("IL2CPP false ignores nonzero padding in boxed Boolean", () => {
        using var memory = new NativeRuntime.Arg(4);
        Marshal.WriteInt32(memory.Pointer, unchecked((int)0xffffff00));
        Check(Marshal.PtrToStructure<bool>(memory.Pointer), "Regression fixture must expose the old four-byte marshaling bug.");
        Check(!NativeRuntime.ReadValue<bool>(memory.Pointer), "False Boolean was read as true from adjacent padding.");
    }),
    ("IL2CPP true and boolean arguments use one-byte values", () => {
        using var yes = NativeRuntime.Arg.Value(true);
        using var no = NativeRuntime.Arg.Value(false);
        Equal((byte)1, Marshal.ReadByte(yes.Pointer)); Equal((byte)0, Marshal.ReadByte(no.Pointer));
        Check(NativeRuntime.ReadValue<bool>(yes.Pointer) && !NativeRuntime.ReadValue<bool>(no.Pointer), "Boolean round trip failed.");
    }),
    ("Native integer float and pointer reads retain their layout", () => {
        using var integer = NativeRuntime.Arg.Value(-123456);
        using var floating = NativeRuntime.Arg.Value(0.125f);
        using var pointer = NativeRuntime.Arg.Value(new IntPtr(0x12345678));
        Equal(-123456, NativeRuntime.ReadValue<int>(integer.Pointer));
        Equal(0.125f, NativeRuntime.ReadValue<float>(floating.Pointer));
        Equal(new IntPtr(0x12345678), NativeRuntime.ReadValue<IntPtr>(pointer.Pointer));
    }),
    ("Initial scene is baseline and repeated samples are silent", () => {
        var state = new GameEventState();
        Check(state.Scene("Title") is null && state.Scene("Title") is null, "Initial scene replayed.");
        Equal("Title", state.Snapshot().Scene);
    }),
    ("Scene transitions preserve previous and current names", () => {
        var state = new GameEventState(); state.Scene("Title");
        var change = state.Scene("Main");
        Check(change is not null, "Scene transition missing.");
        Equal("Title", change!.PreviousScene); Equal("Main", change.Scene);
        Equal("Main", state.Scene("LevelList")!.PreviousScene);
    }),
    ("Loading controller does not emit start or claim playback", () => {
        var state = new GameEventState();
        Check(state.Controller(10, false, false, "chart-a") is null, "Loading emitted start.");
        Check(!state.IsPlaying && state.RunId is null, "Loading claimed an active run.");
    }),
    ("One verified playback latch produces one start with copied chart identity", () => {
        var state = new GameEventState(); state.Controller(10, false, false, "chart-a");
        var start = state.Controller(10, true, false, "chart-a");
        Check(start is not null && !string.IsNullOrEmpty(start.RunId), "Start missing.");
        Equal("chart-a", start!.ChartId); Check(state.IsPlaying, "Run is not playing.");
        for (var i = 0; i < 20; i++) Check(state.Controller(10, true, false, "chart-a") is null, "Duplicate start.");
        Equal(start.RunId, state.Snapshot().RunId);
    }),
    ("Already finished controller never fabricates a start", () => {
        var state = new GameEventState();
        Check(state.Controller(10, true, true, "chart-a") is null, "Finished controller emitted a false start.");
        Check(!state.IsPlaying && state.RunId is null, "Finished controller created a run identity.");
    }),
    ("Temporarily unavailable information does not create a second run", () => {
        var state = new GameEventState(); var start = state.Controller(10, true, false, "chart-a")!;
        state.ControllerUnavailable(); Check(!state.IsPlaying, "Unavailable controller remains playing.");
        Check(state.Controller(10, true, false, "chart-a") is null, "Temporary missing information repeated the start.");
        Equal(start.RunId, state.RunId);
    }),
    ("Consecutive live controllers create distinct run IDs", () => {
        var state = new GameEventState();
        var first = state.Controller(10, true, false, "same-chart")!;
        var second = state.Controller(20, true, false, "same-chart")!;
        Check(first.RunId != second.RunId, "Two plays shared one ID.");
        Equal(second.RunId, state.CompleteRunId());
    }),
    ("Playback interruption on same information never fabricates a retry", () => {
        var state = new GameEventState();
        var first = state.Controller(10, true, false, "chart-a")!;
        state.Controller(10, false, false, "chart-a");
        var retry = state.Controller(10, true, false, "chart-a");
        Check(retry is null && state.RunId == first.RunId, "Same-information playback interruption fabricated a retry.");
    }),
    ("Over flag stops playing without duplicating start", () => {
        var state = new GameEventState(); var start = state.Controller(10, true, false, "chart-a")!;
        Check(state.Controller(10, true, true, "chart-a") is null, "Over emitted a second start.");
        Check(!state.IsPlaying, "Ended run remains playing."); Equal(start.RunId, state.CompleteRunId());
    }),
    ("Existing result on first observation is not replayed", () => {
        var state = new GameEventState();
        Check(!state.ObserveResult(90), "Pre-existing result replayed.");
        Check(!state.ObserveResult(90), "Repeated baseline result replayed.");
        Check(state.ObserveResult(91), "New result missing.");
    }),
    ("Empty result baseline permits the first later real result", () => {
        var state = new GameEventState();
        Check(!state.ObserveResult(0), "Empty result emitted.");
        Check(state.ObserveResult(90), "First new result missing.");
        Check(!state.ObserveResult(90), "Same result emitted twice.");
    }),
    ("Result screen disappearance and return do not duplicate completion", () => {
        var state = new GameEventState(); state.ObserveResult(0);
        Check(state.ObserveResult(90), "New result missing.");
        Check(!state.ObserveResult(0) && !state.ObserveResult(90), "Reopened result replayed.");
    }),
    ("Completion correlates after gameplay controller is destroyed", () => {
        var state = new GameEventState(); state.ObserveResult(0);
        var start = state.Controller(10, true, false, "chart-a")!;
        state.Controller(0, false, false, null);
        Check(!state.IsPlaying && state.ObserveResult(90), "Completion was lost on teardown.");
        Equal(start.RunId, state.CompleteRunId()); Equal("chart-a", state.ChartId);
    }),
    ("Result ends playback while retaining the started run ID", () => {
        var state = new GameEventState(); state.ObserveResult(0);
        var start = state.Controller(10, true, false, null, 20)!;
        state.Controller(10, true, true, null, 20);
        Check(state.ObserveResult(90), "Result was not observed.");
        var finished = FinishKnown(state, Result(null, false), 10, 20);
        Check(!state.IsPlaying && finished.AssociatedWithObservedStart, "Actual result did not end playback.");
        Equal(start.RunId, state.CompleteRunId());
    }),
    ("Unknown metadata still allows lifecycle tracking", () => {
        var state = new GameEventState(); var start = state.Controller(10, true, false, null)!;
        Check(start.ChartId is null && state.IsPlaying, "Missing metadata blocked lifecycle.");
        Check(Guid.TryParseExact(start.RunId, "N", out _), "Run ID is invalid.");
    }),
    ("Shared session and scene generation are consistent in legacy and new snapshots", () => {
        var state = Ready(); state.Scene("Main"); var transition = state.Scene("Level")!;
        var start = state.Controller(10, true, false, "chart")!; var snapshot = state.LifecycleSnapshot();
        Equal(2L, snapshot.SceneGeneration); Equal(state.SessionId, transition.SessionId); Equal(state.SessionId, start.SessionId);
        Equal(snapshot.SceneGeneration, start.SceneGeneration); Equal(snapshot.Revision, state.Snapshot().Revision);
        Check(state.Scene("Level") is null && state.SceneGeneration == 2, "Second observer duplicated the scene generation.");
    }),
    ("Playback latch requires actual playing and timeReady for the current controller and information", () => {
        var latch = new PlaybackStartLatch(); latch.Observe(10, 20, true, false); Check(!latch.Started, "Playing without ready produced start.");
        latch.Observe(10, 20, false, true); Check(!latch.Started, "Ready without playback produced start.");
        latch.Observe(10, 20, true, true); Check(latch.Started, "Actual playback evidence ignored.");
        latch.Observe(10, 20, false, false); Check(latch.Started, "Pause reset the proven start.");
        latch.Observe(10, 21, false, false); Check(!latch.Started, "A new controller borrowed the same information's latch.");
        latch.Observe(10, 21, true, true); Check(latch.Started, "The replacement controller's real playback did not latch.");
        latch.Observe(11, 21, false, false); Check(!latch.Started, "Retry reused the previous information latch.");
        latch.Observe(11, 0, true, true); Check(!latch.Started, "Playback without a known receiver was admitted.");
    }),
    ("Entering gameplay scene without playback produces no start", () => {
        var state = Ready(); var changes = Changes(state); state.Scene("Level"); state.Controller(10, false, false, "chart");
        Check(!changes.Any(x => x.Kind == GameLifecycleChangeKind.RunStarted) && state.RunId is null, "Scene entry fabricated a run.");
    }),
    ("New information creates a distinct same-chart play identity", () => {
        var state = Ready(); var first = state.Controller(10, true, false, "chart")!;
        state.Controller(20, false, false, "chart"); Check(state.RunId is null, "Old play identity leaked into retry preparation.");
        var next = state.Controller(20, true, false, "chart")!;
        Check(first.RunId != next.RunId && first.ChartId == next.ChartId, "New same-chart play was not distinct.");
    }),
    ("Pause baseline and genuine pause/resume edges keep one RunId", () => {
        var state = Ready(); var changes = Changes(state); var start = state.Controller(10, true, false, "chart")!;
        state.ObservePause(false); state.ObservePause(true); state.ObservePause(true); state.ObservePause(false);
        Equal(1, changes.Count(x => x.Kind == GameLifecycleChangeKind.Paused)); Equal(1, changes.Count(x => x.Kind == GameLifecycleChangeKind.Resumed));
        Equal(start.RunId, state.RunId); Check(state.IsPlaying && state.LifecycleSnapshot().Paused == false, "Resume changed run or playback state.");
    }),
    ("Unavailable pause reports ObservationUnavailable without inventing a resume", () => {
        var state = Ready(); var changes = Changes(state); state.Controller(10, true, false, "chart"); state.ObservePause(false); state.ObservePause(true);
        state.SetAvailability(pause: false, reason: "missing pause object"); state.ObservePause(null);
        var snapshot = state.LifecycleSnapshot(); Check(snapshot.Paused is null && !snapshot.PauseAvailable && snapshot.Phase == RunLifecyclePhase.ObservationLost, "Missing pause still claimed a known pause state.");
        Check(snapshot.Limitations.Any(x => x.Code == ErrorCode.ObservationUnavailable && x.TargetId == "PauseUIControl._pausing"), "Missing pause reason absent.");
        Equal(0, changes.Count(x => x.Kind == GameLifecycleChangeKind.Resumed));
    }),
    ("Scene exit and controller loss do not synthesize an abort", () => {
        var state = Ready(); var changes = Changes(state); var start = state.Controller(10, true, false, "chart")!;
        state.Scene("Main"); state.ControllerUnavailable();
        Check(!changes.Any(x => x.Kind == GameLifecycleChangeKind.RunEnded) && state.RunId == start.RunId && state.Phase == RunLifecyclePhase.ObservationLost, "Disappearance was falsely called cancellation.");
    }),
    ("Over only waits for result while actual result chooses completed failed or skipped", () => {
        foreach (var kind in new[] { RunEndReason.Completed, RunEndReason.Failed, RunEndReason.Skipped }) {
            var state = Ready(); var changes = Changes(state); state.ObserveResult(0); var start = state.Controller(10, true, false, "chart")!;
            state.Controller(10, true, true, "chart", 20); Equal(0, changes.Count(x => x.Kind == GameLifecycleChangeKind.RunEnded));
            Check(state.ObserveResult(90), "Result missing."); var result = FinishKnown(state, Result("chart", kind == RunEndReason.Failed, kind == RunEndReason.Skipped), 10, 20);
            Equal(start.RunId, result.RunId); Equal(kind, result.EndReason); Equal(1, changes.Count(x => x.Kind == GameLifecycleChangeKind.RunEnded));
        }
    }),
    ("Initial historical result does not publish a completion or fabricate a RunId", () => {
        var state = Ready(); var changes = Changes(state);
        Check(!state.ObserveResult(90) && state.CompleteRunId() is null && changes.Count == 0, "Initial result invented history or correlation.");
        Check(state.ObserveResult(91), "New unassociated result missing."); var result = state.Finish(Result("old-chart", true));
        Check(result.RunId is null && !result.AssociatedWithObservedStart && changes.Single().Kind == GameLifecycleChangeKind.ResultObserved, "Unknown result received fake identity or run end.");
    }),
    ("Delayed different-chart result cannot finish the new live run", () => {
        var state = Ready(); state.ObserveResult(0); var start = state.Controller(20, true, false, "current")!;
        Check(state.ObserveResult(90), "New result missing."); var finished = state.Finish(Result("previous", true));
        Check(finished.RunId is null && state.IsPlaying && state.RunId == start.RunId, "Old result corrupted current run.");
    }),
    ("Same-chart late result during active playback is explicitly unassociated", () => {
        var state = Ready(); var changes = Changes(state); state.ObserveResult(0); var start = state.Controller(20, true, false, "same")!;
        Check(state.ObserveResult(90), "Result missing."); var finished = state.Finish(Result("same", true));
        Check(finished.RunId is null && state.IsPlaying && state.RunId == start.RunId && changes.Last().RelatedRunId is null, "Late same-chart result falsely ended current run.");
    }),
    ("Verified abort is distinct and suppressed after observed over/result", () => {
        var state = Ready(); var changes = Changes(state); state.Controller(10, true, false, "chart");
        state.AbortFromVerifiedCallback("test-only verified explicit cancellation callback"); state.AbortFromVerifiedCallback("duplicate");
        Equal(1, changes.Count(x => x.EndReason == RunEndReason.Aborted));
        var completed = Ready(); var ended = Changes(completed); completed.Controller(10, true, false, "chart"); completed.Controller(10, true, true, "chart");
        completed.AbortFromVerifiedCallback("late lifecycle teardown"); Equal(0, ended.Count(x => x.Kind == GameLifecycleChangeKind.RunEnded));
    }),
    ("Atomic initial snapshot subscription has no replay gap and monotonic revisions", () => {
        var state = Ready(); state.Scene("Main"); state.Controller(10, true, false, "chart"); var seen = new List<GameLifecycleChange>();
        var subscription = state.Subscribe(seen.Add).Value!; var initial = subscription.InitialSnapshot;
        Check(initial.RunId == state.RunId && seen.Count == 0, "Subscribe replayed historical start.");
        state.ObservePause(false); state.ObservePause(true); state.Scene("Other");
        Check(seen.Select(x => x.Snapshot.Revision).SequenceEqual(Enumerable.Range(1, seen.Count).Select(i => initial.Revision + i)), "Revision gaps/duplicates after atomic snapshot.");
        Equal(initial.SceneGeneration + 1, seen.Last().Snapshot.SceneGeneration); subscription.Dispose(); subscription.Dispose();
        int count = seen.Count; state.Scene("Final"); Equal(count, seen.Count);
    }),
    ("Raw subscriber exception removes only the offending subscriber", () => {
        var state = Ready(); int valid = 0, broken = 0;
        state.Subscribe(_ => { broken++; throw new InvalidOperationException("fixture"); }); state.Subscribe(_ => valid++);
        state.Scene("Main"); state.Scene("Other"); Equal(1, broken); Equal(2, valid);
    }),
    ("Subscribe during current delivery returns that revision without replaying it", () => {
        var state = Ready(); var events = new EventHub(new TestLogger()); var seen = new List<GameLifecycleChange>(); IGameLifecycleSubscription? nested = null;
        state.BindPublisher(events, change => { if (nested is null) nested = state.Subscribe(seen.Add).Value!; events.Publish(change); });
        state.Scene("Main"); Check(nested is not null && seen.Count == 0, "Registration replayed its atomic initial revision.");
        long initial = nested!.InitialSnapshot.Revision; state.Scene("Other");
        Equal(1, seen.Count); Equal(initial + 1, seen.Single().Snapshot.Revision);
    }),
    ("Nested lifecycle mutations preserve increasing delivery and original legacy metadata", () => {
        var state = Ready(); var seen = new List<GameLifecycleChange>();
        state.Subscribe(change => { if (change.Kind == GameLifecycleChangeKind.RunStarted) state.Scene("Nested"); }); state.Subscribe(seen.Add);
        var start = state.Controller(10, true, false, "chart")!;
        Check(seen.Select(x => x.Snapshot.Revision).SequenceEqual(seen.Select(x => x.Snapshot.Revision).OrderBy(x => x)), "Nested observer delivered newer revision before its predecessor.");
        var started = seen.Single(x => x.Kind == GameLifecycleChangeKind.RunStarted);
        Equal(started.Snapshot.Revision, start.Revision); Equal(started.Snapshot.SceneGeneration, start.SceneGeneration);
    }),
    ("Scoped lifecycle snapshot and callbacks expire automatically after plugin stop", () => {
        using var fixture = new LifecycleFixture(); IGameLifecycleService? service = null; IGameLifecycleSubscription? subscription = null; int delivered = 0;
        fixture.Register("probe", context => { service = fixture.State.ForPlugin(context); subscription = service.Subscribe(_ => delivered++).Value!; });
        fixture.Host.Enable("probe"); long initial = subscription!.InitialSnapshot.Revision; fixture.State.Scene("Main");
        Check(delivered == 1 && service!.GetSnapshot().Value!.Revision == initial + 1, "Scoped lifecycle missed next revision.");
        fixture.Host.Disable("probe"); fixture.State.Scene("Other");
        Equal(1, delivered); Equal(ErrorCode.ScopeExpired, service!.GetSnapshot().Error!.Code); Equal(ErrorCode.ScopeExpired, service.Subscribe(_ => { }).Error!.Code);
    }),
    ("Scoped lifecycle callback fault stops only owner and other subscriptions continue", () => {
        using var fixture = new LifecycleFixture(); int healthy = 0;
        fixture.Register("bad", context => fixture.State.ForPlugin(context).Subscribe(_ => throw new InvalidOperationException("subscriber fault")));
        fixture.Register("good", context => fixture.State.ForPlugin(context).Subscribe(_ => healthy++)); fixture.Host.EnableAll();
        fixture.State.Scene("Main"); fixture.State.Scene("Other");
        Equal(PluginState.Faulted, fixture.Host.Statuses.Single(x => x.Id == "bad").State); Equal(PluginState.Enabled, fixture.Host.Statuses.Single(x => x.Id == "good").State); Equal(2, healthy);
    }),
    ("Main-thread lifecycle queries and mutations refuse worker access", () => {
        var state = Ready(); var code = Task.Run(() => state.GetSnapshot().Error?.Code).GetAwaiter().GetResult(); Equal(ErrorCode.WrongThread, code);
        var changed = Task.Run(() => { try { state.Scene("wrong"); return true; } catch (InvalidOperationException) { return false; } }).GetAwaiter().GetResult(); Check(!changed, "Worker mutated lifecycle state.");
    }),
    ("Candidate diagnostic cache remains bounded and detects changed samples", () => {
        var cache = new BoundedObservationSamples(16);
        for (int i = 0; i < 10000; i++) Check(cache.Changed(new IntPtr(i + 1), "value"), "New observation was missed.");
        Equal(16, cache.Count); Check(!cache.Changed(new IntPtr(10000), "value") && cache.Changed(new IntPtr(10000), "updated"), "Sample dedup/change mismatch.");
        cache.Clear(); Equal(0, cache.Count);
    }),
    ("Lifecycle observation states sampling source and bounded temporal precision", () => {
        var state = Ready(); var changes = Changes(state); state.Controller(10, true, false, "chart");
        Equal(LifecycleObservationSource.SampledGameState, changes.Single().Observation.Source); Equal(250, changes.Single().Observation.SamplingIntervalMilliseconds);
    }),
    ("Profile selection matches the exact fingerprint case insensitively", () => {
        using var files = new Profiles(); files.Write("known.json", Valid());
        var profile = BuildProfiles.Select(files.Path, new string('A', 64));
        Equal("test-build", profile.Id); Equal("GameManager", profile.Types[profile.TickType].Name);
    }),
    ("Unknown build fails closed", () => {
        using var files = new Profiles(); files.Write("known.json", Valid());
        Throws<NotSupportedException>(() => BuildProfiles.Select(files.Path, new string('b', 64)));
    }),
    ("Missing profile directory fails closed", () => {
        Throws<DirectoryNotFoundException>(() => BuildProfiles.Select(System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N")), new string('a', 64)));
    }),
    ("Malformed JSON is refused", () => {
        using var files = new Profiles(); File.WriteAllText(System.IO.Path.Combine(files.Path, "bad.json"), "{ invalid");
        Throws<JsonException>(() => BuildProfiles.Select(files.Path, new string('a', 64)));
    }),
    ("Invalid profile ID is refused", () => {
        using var files = new Profiles(); files.Write("bad.json", Valid() with { Id = " " });
        Throws<InvalidDataException>(() => BuildProfiles.Select(files.Path, new string('a', 64)));
    }),
    ("Nonhex and shortened build fingerprints are refused", () => {
        using var files = new Profiles(); files.Write("bad.json", Valid() with { GameAssemblySha256 = new string('z', 64) });
        Throws<InvalidDataException>(() => BuildProfiles.Select(files.Path, new string('a', 64)));
        files.Write("bad.json", Valid() with { GameAssemblySha256 = "abcd" });
        Throws<InvalidDataException>(() => BuildProfiles.Select(files.Path, new string('a', 64)));
    }),
    ("Tick binding must resolve within the profile", () => {
        using var files = new Profiles(); files.Write("bad.json", Valid() with { TickType = "not-declared" });
        Throws<InvalidDataException>(() => BuildProfiles.Select(files.Path, new string('a', 64)));
    }),
    ("Duplicate build fingerprints are refused even with case differences", () => {
        using var files = new Profiles(); files.Write("a.json", Valid());
        files.Write("b.json", Valid() with { Id = "second", GameAssemblySha256 = new string('A', 64) });
        Throws<InvalidDataException>(() => BuildProfiles.Select(files.Path, new string('a', 64)));
    }),
    ("Missing tick identity fails before native bridge setup", () => {
        using var files = new Profiles(); files.Write("bad.json", Valid() with { TickType = null! });
        Throws<InvalidDataException>(() => BuildProfiles.Select(files.Path, new string('a', 64)));
        files.Write("bad.json", Valid() with { TickMethod = " " });
        Throws<InvalidDataException>(() => BuildProfiles.Select(files.Path, new string('a', 64)));
    }),
    ("Incomplete type bindings fail before native lookup", () => {
        using var files = new Profiles();
        files.Write("bad.json", Valid() with { Types = new Dictionary<string, TypeBinding> { ["manager"] = new("", "", "GameManager") } });
        Throws<InvalidDataException>(() => BuildProfiles.Select(files.Path, new string('a', 64)));
        files.Write("bad.json", Valid() with { Types = new Dictionary<string, TypeBinding> { ["manager"] = null! } });
        Throws<InvalidDataException>(() => BuildProfiles.Select(files.Path, new string('a', 64)));
    }),
    ("Invalid unrelated profile cannot silently poison validated selection", () => {
        using var files = new Profiles(); files.Write("known.json", Valid());
        files.Write("bad.json", Valid() with { Id = "bad", GameAssemblySha256 = "malformed" });
        Throws<InvalidDataException>(() => BuildProfiles.Select(files.Path, new string('a', 64)));
    })
};

var failures = 0;
foreach (var check in checks)
{
    try { check.Run(); Console.WriteLine($"PASS {check.Name}"); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {check.Name}: {ex.Message}"); }
}
Console.WriteLine($"Game adapter checks: {checks.Length - failures}/{checks.Length} passed.");
return failures == 0 ? 0 : 1;

static void Check(bool condition, string message = "Observed run identity check failed.") { if (!condition) throw new InvalidOperationException(message); }
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected '{expected}', got '{actual}'."); }

static void ScoreSourceLoading(bool hasController)
{
    using var main = new MainThreadDispatcher();
    using var native = new FakeNative();
    native.ConfigureScoreCapture();
    var service = new GameDataService(native.ScoreCaptureApi, main, native.ScoreManagerClass);
    var source = native.InitialScoreSource;
    if (hasController) native.ReturnNullLevelList = true;
    else native.SetScoreField(source.Manager, "levelControl", IntPtr.Zero);

    RetryableDataNotReady(service.GetBestScoresSnapshot().Error);
    Equal(hasController ? 1 : 0, native.GetAllLevelCalls);
    Equal(0, native.ActiveRoots);
    native.SetScoreField(source.Manager, "levelControl", source.Control);
    native.ReturnNullLevelList = false;
    var recovered = service.GetBestScoresSnapshot();
    Check(recovered.Success, recovered.Error?.Message ?? "Expected loaded scores.");
    Equal(100, recovered.Value!.Single().Score);
    Equal(0, native.ActiveRoots);
}

static void ScoreCaptureManagerChange()
{
    using var main = new MainThreadDispatcher();
    using var native = new FakeNative();
    native.ConfigureScoreCapture();
    IScoreService scores = new GameDataService(native.ScoreCaptureApi, main, native.ScoreManagerClass);

    native.AfterGetAllLevel = () => native.ScoreManager = native.ReplacementScoreSource.Manager;
    var interrupted = scores.GetBestScoresSnapshot();
    if (interrupted.Success)
        throw new InvalidOperationException($"Published obsolete score '{interrupted.Value!.Single().ChartId}' after the singleton switched.");
    RetryableDataNotReady(interrupted.Error);
    Equal(1, native.GetAllLevelCalls);
    Equal(0, native.ActiveRoots);

    native.AfterGetAllLevel = null;
    var recovered = scores.GetBestScoresSnapshot();
    Check(recovered.Success, recovered.Error?.Message ?? "Replacement capture failed.");
    var score = recovered.Value!.Single();
    Equal("replacement.chart", score.ChartId);
    Equal(200, score.Score);
    Equal(2, native.GetAllLevelCalls);
    Equal(0, native.ActiveRoots);
}

static void ScoreRevisionControlChange()
{
    using var main = new MainThreadDispatcher();
    using var native = new FakeNative();
    native.ConfigureScoreCapture();
    var log = new TestLogger();
    var module = new DataModule(native.ScoreCaptureApi, main, log, native.ScoreManagerClass);
    var registry = new CapabilityRegistry(new("test", "test", "test", new string('a', 64), true), log);
    var registration = new ModuleRegistration(registry, module.Manifest);
    try
    {
        var probe = module.Probe();
        Check(probe.Ready, probe.Reason ?? "Initial probe failed.");
        module.Register(registration);
        registration.Complete();
        var snapshots = registry.GetService<IDataSnapshotService>(DataCapabilityIds.Read).Service!;
        var before = snapshots.GetHeader().Value!;
        var initialPage = new DataPageRequest(before.Token);
        Equal("initial.chart", snapshots.GetBestScores(initialPage).Value!.Items.Single().ChartId);

        using var cancel = new CancellationTokenSource();
        native.AfterLevelItem = cancel.Cancel;
        Equal(ErrorCode.Cancelled, snapshots.Refresh(cancel.Token).Error!.KnownCode);
        Equal(before, snapshots.GetHeader().Value);

        native.SetScoreField(native.InitialScoreSource.BestScore, "score", 101);
        native.AfterLevelItem = () => native.SetScoreField(native.InitialScoreSource.Manager, "levelControl", native.ReplacementScoreSource.Control);
        var interrupted = snapshots.Refresh();
        if (interrupted.Success)
        {
            var published = snapshots.GetBestScores(new(interrupted.Value!.Token)).Value!;
            throw new InvalidOperationException($"Published obsolete score {published.Items.Single().Score} in revision {published.Header.Token.Revision} after LevelControl changed.");
        }
        RetryableDataNotReady(interrupted.Error);
        RetryableDataNotReady(snapshots.GetHeader().Error);
        RetryableDataNotReady(snapshots.GetBestScores(initialPage).Error);
        var stale = snapshots.GetBestScores(initialPage with { AllowStale = true }).Value!;
        Check(stale.Header.IsStale, "The previous page was not marked stale.");
        Equal(before.Token, stale.Header.Token);
        Equal(before.VerifiedAt, stale.Header.VerifiedAt);
        Equal(100, stale.Items.Single().Score);
        Equal(3, native.GetAllLevelCalls);
        Equal(0, native.ActiveRoots);

        native.AfterLevelItem = null;
        var recovered = snapshots.Refresh();
        Check(recovered.Success, recovered.Error?.Message ?? "Replacement refresh failed.");
        var current = recovered.Value!;
        Equal(before.Token with { Revision = before.Token.Revision + 1 }, current.Token);
        Check(!current.IsStale, "A stable replacement source should be current.");
        native.BeginLowerIl2CppAudit();
        try
        {
            var charts = snapshots.GetCharts(new(current.Token)).Value!;
            var scores = snapshots.GetBestScores(new(current.Token)).Value!;
            Equal(current.Token, charts.Header.Token);
            Equal(current.Token, scores.Header.Token);
            Equal("replacement.chart", charts.Items.Single().Id);
            Equal("replacement.chart", scores.Items.Single().ChartId);
            Equal(200, scores.Items.Single().Score);
            Equal(0L, native.LowerIl2CppCalls);
        }
        finally { native.EndLowerIl2CppAudit(); }
        Equal(4, native.GetAllLevelCalls);
        Equal(0, native.ActiveRoots);
    }
    finally { registration.Withdraw("Fixture disposed."); module.Release(); }
}

static void ScoreRevisionAssetChange(bool replaceAssetManager)
{
    using var main = new MainThreadDispatcher();
    using var native = new FakeNative();
    native.ConfigureScoreCapture();
    var source = new GameDataService(native.ScoreCaptureApi, main, native.ScoreManagerClass);
    using var cache = new DataSnapshotService(main, source.CaptureSnapshot);
    IDataSnapshotService snapshots = cache;

    native.AfterGetAllLevel = replaceAssetManager
        ? () => native.SetScoreField(native.InitialScoreSource.Manager, "assetManager", native.ReplacementScoreSource.Assets)
        : () => native.SetScoreField(native.InitialScoreSource.Assets, "loadAssetListOver", false);
    var interrupted = snapshots.Refresh();
    Check(!interrupted.Success, $"Published an initial revision after {(replaceAssetManager ? "asset manager" : "readiness")} changed.");
    RetryableDataNotReady(interrupted.Error);
    RetryableDataNotReady(snapshots.GetHeader(allowStale: true).Error);
    Equal(1, native.GetAllLevelCalls);
    Equal(0, native.ActiveRoots);

    native.AfterGetAllLevel = null;
    native.SetScoreField(native.InitialScoreSource.Assets, "loadAssetListOver", true);
    var recovered = snapshots.Refresh();
    Check(recovered.Success, recovered.Error?.Message ?? "First stable refresh failed.");
    Equal(1L, recovered.Value!.Token.Revision);
    Equal(2, native.GetAllLevelCalls);
}

static void RetryableDataNotReady(OperationError? error)
{
    Check(error is { KnownCode: ErrorCode.NotReady, CapabilityId: DataCapabilityIds.Read, Retryable: true },
        $"Expected retryable NotReady, got '{error}'.");
}
static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}
static GameEventState Ready()
{
    var state = new GameEventState(); state.SetAvailability(scenes: true, runs: true, pause: true); return state;
}
static List<GameLifecycleChange> Changes(GameEventState state)
{
    var result = new List<GameLifecycleChange>();
    var subscribed = state.Subscribe(result.Add); Check(subscribed.Success, "Cannot subscribe lifecycle test probe."); return result;
}
static RunFinished Result(string? chartId, bool crashed, bool skipped = false)
    => new(null, chartId, 1, .5, 2, 3, 4, 5, crashed, skipped);
static RunFinished FinishKnown(GameEventState state, RunFinished result, long information, long controller)
{
    if (!state.ResultOrigins.Active) state.ResultOrigins.Activate();
    var origin = state.CaptureResultOrigin(information, controller) ?? throw new InvalidOperationException("Fixture has no observed result origin.");
    Check(state.ResultOrigins.Register(origin, new ResultTestRetention(900, information, controller), out _), "Fixture failed to register actual-return provenance.");
    var resolved = state.ResultOrigins.Consume(900, out var duplicate, out var reason);
    Check(resolved is not null && !duplicate, "Fixture failed to consume its rooted result.");
    return state.Finish(result, resolved, reason);
}
static ObservedResultOrigin CaptureOrigin(GameEventState state, long information, long controller)
    => state.CaptureResultOrigin(information, controller) ?? throw new InvalidOperationException("Expected captured original-entry run.");
static RunFinished ConsumeResult(GameEventState state, long pointer, RunFinished result)
{
    var origin = state.ResultOrigins.Consume(pointer, out var duplicate, out var reason);
    Check(!duplicate, "Unexpected duplicate fixture result.");
    return state.Finish(result, origin, reason);
}
static void ReturnedResultWithoutScreen()
{
    using var main = new MainThreadDispatcher(); using var native = new FakeNative();
    native.ConfigureGameplay(); native.GameplayActive = false;
    var log = new TestLogger(); var events = new EventHub(log); var extensions = new ExtensionPointRegistry(log);
    var state = Ready(); state.ResultOrigins.Activate(); state.Scene("Level");
    var changes = Changes(state); var delivered = new List<RunFinished>();
    using var listener = events.Subscribe<RunFinished>(delivered.Add);
    Check(extensions.Register(GameExtensionPoints.RunFinished, ExtensionPointPhase.After, "Controlled result event.").Success);
    using var observer = new GameEventObserver(native.GameplayApi, Valid(), main, events, extensions, log,
        observeScenes: false, observeRuns: false, state: state);
    // Use the real run poll with the fake discovery returning only inactive objects.
    var observerType = typeof(GameEventObserver);
    observerType.GetProperty(nameof(GameEventObserver.RunsAvailable), BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(observer, true);
    void SetField(string name, string assembly, string ns, string type)
        => observerType.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(observer, native.GameplayApi.Class(assembly, ns, type));
    SetField("_resources", "UnityEngine.CoreModule.dll", "UnityEngine", "Resources");
    SetField("_controllerType", "Assembly-CSharp.dll", "", "NormalModeInLevelController");
    SetField("_resultType", "Assembly-CSharp.dll", "", "NormalModeInLevelController");
    observer.Poll();
    var run = state.Controller(101, true, false, "chart", 201)!;
    state.Controller(101, false, true, "chart", 201);
    var copied = new RunFinished(null, "chart", 580000, .56, 53, 8, 2, 16, false, false);
    var retention = new ResultTestRetention(301, 101, 201) { TerminalSnapshot = copied };
    try
    {
        Check(state.ResultOrigins.Register(CaptureOrigin(state, 101, 201), retention, out _));
        state.Scene("LevelList");
        observer.Poll(); observer.Poll();
        Check(observer.RunsAvailable, "The controlled observer lost its run availability.");
        Equal(1, delivered.Count);
        var result = delivered.Single(); Equal(run.RunId, result.RunId); Equal(copied.Score, result.Score);
        Equal(copied.RawCompletion, result.RawCompletion); Equal(copied.Perfect, result.Perfect);
        Equal(copied.MaxPerfect, result.MaxPerfect); Equal(copied.Bad, result.Bad); Equal(copied.Miss, result.Miss);
        Equal(RunEndReason.Completed, result.EndReason);
        Check(result.AssociatedWithObservedStart && state.Phase == RunLifecyclePhase.Finished);
        Check(changes.Single(change => change.Kind == GameLifecycleChangeKind.RunEnded).Result == result);
        Equal(0, state.ResultOrigins.PendingCount); Equal(0, retention.Disposals);
        Equal(result, state.ResultOrigins.ReadPublishedResult(301));
        retention.Result = 302;
        Check(state.ResultOrigins.ReadPublishedResult(301) is null);
        Equal(result, state.ResultOrigins.ReadPublishedResult(302));
        Check(state.ResultOrigins.Consume(302, out var duplicate, out _) is null && duplicate);
    }
    finally { state.ResultOrigins.Withdraw("fixture end"); }
    Equal(1, retention.Disposals);
}
static void ResultReturnedBetweenPolls()
{
    using var main = new MainThreadDispatcher();
    using var native = new FakeNative();
    native.ConfigureTerminalResult();
    var api = native.GameplayApi;
    using var resultRoot = api.Root(native.ResultObject);
    using var information = api.Root(native.GameplayInformation);
    using var controller = api.Root(native.GameplayController);
    using var parameters = api.Root(native.ResultParameters);
    var state = Ready();
    state.ResultOrigins.Activate();
    var capture = new ExactResultOriginModule(api, main, new TestLogger(), Valid(), state, "");
    RunFinished? Copy() => capture.CopyTerminalSnapshot(resultRoot, information, controller, parameters);
    var infoId = information.Target.ToInt64();
    var controllerId = controller.Target.ToInt64();
    state.Scene("Level");
    var run = state.Controller(infoId, true, false, "fixture.chart", controllerId)!;
    Check(Copy() is null, "The original result was copied while levelOver was false.");
    native.SetGameplayField("info.over", true);
    native.AfterResultScore = () => native.ResultScore++;
    Check(Copy() is null, "Two different reads of the score were accepted.");
    native.AfterResultScore = () => native.SetGameplayField("result.metadata", IntPtr.Zero);
    Check(Copy() is null, "The result metadata was replaced during the read.");
    native.SetGameplayField("result.metadata", native.ResultMetadata);
    native.AfterResultScore = null;
    native.ResultScore = 580000;
    native.ResultRate = float.NaN;
    Check(Copy() is null, "A non-finite completion value was accepted.");
    native.ResultRate = .75f;
    var copied = Copy();
    Check(copied == new RunFinished(null, "fixture.chart", 580000, .75, 53, 8, 2, 16, false, false),
        "Stable getter, chart or judgement values failed to round-trip.");
    Check(native.AllGameplayReceiversRooted, "A result getter or field was read without rooting its receiver.");
    var changes = Changes(state);
    try
    {
        var record = new ResultTestRetention(resultRoot.Target.ToInt64(), infoId, controllerId) { TerminalSnapshot = copied };
        Check(state.ResultOrigins.Register(CaptureOrigin(state, infoId, controllerId), record, out _));
        Check(state.FinishTerminalReturn() is null, "An active playing observation was superseded prematurely.");
        state.Scene("LevelList");
        state.ControllerUnavailable();
        var finished = state.FinishTerminalReturn();
        Check(finished?.RunId == run.RunId && finished.EndReason == RunEndReason.Completed && finished.Score == 580000,
            "The verified original result was lost when the sampled controller disappeared between polls.");
        Check(state.FinishTerminalReturn() is null && changes.Count(c => c.Kind == GameLifecycleChangeKind.RunEnded) == 1);
    }
    finally { state.ResultOrigins.Withdraw("fixture end"); }
}
static void TerminalReturnRequiresExactRun()
{
    var state = Ready(); state.ResultOrigins.Activate(); state.Scene("Level"); var changes = Changes(state);
    var run = state.Controller(101, true, false, "chart", 201)!;
    var copy = Result("chart", false, skipped: true);
    var returned = new ResultTestRetention(301, 101, 201) { TerminalSnapshot = copy };
    try
    {
        Check(state.ResultOrigins.Register(CaptureOrigin(state, 101, 201), returned, out _));
        state.Scene("LevelList");
        Check(state.FinishTerminalReturn() is null, "A scene change superseded an active playing controller.");
        var other = state.Controller(102, true, false, "chart", 202)!;
        state.Controller(102, false, true, "chart", 202);
        var entry = CaptureOrigin(state, 102, 202);
        Check(state.ResultOrigins.Register(entry, new ResultTestRetention(302, 102, 202), out _));
        Check(state.FinishTerminalReturn() is null && state.ResultOrigins.PendingCount == 2,
            "A stale result or a return missing a completed snapshot ended another run.");
        Check(state.ResultOrigins.Register(entry, new ResultTestRetention(303, 102, 202) { TerminalSnapshot = copy }, out _));
        var completed = state.FinishTerminalReturn();
        Check(completed?.RunId == other.RunId && completed.RunId != run.RunId
            && completed.EndReason == RunEndReason.Skipped && completed.Skipped);
        Check(changes.Count(change => change.Kind == GameLifecycleChangeKind.RunEnded) == 1);
        Check(state.FinishTerminalReturn() is null);
    }
    finally { state.ResultOrigins.Withdraw("fixture end"); }
}
static void TerminalReturnAmbiguity()
{
    var state = Ready(); state.ResultOrigins.Activate(); state.Scene("Level");
    var start = state.Controller(101, true, false, "chart", 201)!;
    state.Controller(101, false, true, "chart", 201);
    var origin = CaptureOrigin(state, 101, 201);
    var failed = Result("chart", true);
    try
    {
        Check(state.ResultOrigins.Register(origin, new ResultTestRetention(301, 101, 201) { TerminalSnapshot = failed }, out _));
        Check(state.ResultOrigins.Register(origin, new ResultTestRetention(302, 101, 201) { TerminalSnapshot = Result("chart", false) }, out _));
        Check(state.FinishTerminalReturn() is null, "Ambiguous terminal returns arbitrarily picked a result.");
        Equal(2, state.ResultOrigins.PendingCount); Equal(RunLifecyclePhase.AwaitingResult, state.Phase);
        var published = ConsumeResult(state, 301, failed);
        Equal(start.RunId, published.RunId); Equal(RunEndReason.Failed, published.EndReason);
        Check(state.FinishTerminalReturn() is null);
    }
    finally { state.ResultOrigins.Withdraw("fixture end"); }
}
static void ResultOriginOldRun()
{
    foreach (var missing in new[] { false, true })
    {
        var state = Ready(); state.ResultOrigins.Activate(); state.Scene("Level");
        var first = state.Controller(101, true, false, "same", 201)!;
        var firstOrigin = CaptureOrigin(state, 101, 201);
        Check(state.ResultOrigins.Register(firstOrigin, new ResultTestRetention(301, 101, 201), out _), "Old return missing.");
        var second = state.Controller(102, true, false, "same", 202)!;
        var secondOrigin = CaptureOrigin(state, 102, 202);
        var changes = Changes(state);
        if (missing) state.ControllerUnavailable(); else state.Controller(102, true, true, "same", 202);
        var phase = state.Phase;
        var old = ConsumeResult(state, 301, Result("same", true));
        Check(old.RunId is null && !old.AssociatedWithObservedStart && state.RunId == second.RunId
            && state.Phase == phase && changes.All(x => x.Kind != GameLifecycleChangeKind.RunEnded),
            "Old A result ended B after B's over/disappearance boundary.");
        Check(state.ResultOrigins.Register(secondOrigin, new ResultTestRetention(302, 102, 202), out _), "New return missing.");
        var current = ConsumeResult(state, 302, Result("same", false));
        Check(current.RunId == second.RunId && current.RunId != first.RunId && current.AssociatedWithObservedStart
            && changes.Count(x => x.Kind == GameLifecycleChangeKind.RunEnded) == 1, "Correct B result did not close exactly B.");
        state.ResultOrigins.Withdraw("fixture end");
    }
}
static void SharedInformationReplacement()
{
    foreach (var temporaryGap in new[] { false, true })
    {
        var state = Ready(); var changes = Changes(state);
        state.ResultOrigins.Activate(); state.Scene("Level");
        var first = state.Controller(101, true, false, "same", 201)!;
        var firstOrigin = CaptureOrigin(state, 101, 201);
        Check(state.ResultOrigins.Register(firstOrigin, new ResultTestRetention(301, 101, 201), out _), "Original result was not registered.");
        if (temporaryGap) state.ControllerUnavailable();

        Check(state.Controller(101, false, false, "same", 202) is null && state.RunId is null
            && state.Phase == RunLifecyclePhase.Loading && state.CaptureResultOrigin(101, 202) is null,
            "The replacement controller inherited the previous run before any verified playback.");
        var second = state.Controller(101, true, false, "same", 202);
        Check(second is not null && second.RunId != first.RunId && state.CaptureResultOrigin(101, 201) is null,
            "Verified playback on a different controller did not issue a distinct run.");
        var secondOrigin = CaptureOrigin(state, 101, 202);
        state.Controller(101, true, true, "same", 202);
        var old = ConsumeResult(state, 301, Result("same", true));
        Check(old.RunId is null && state.Phase == RunLifecyclePhase.AwaitingResult
            && changes.All(change => change.Kind != GameLifecycleChangeKind.RunEnded), "The late original result closed the replacement.");
        Check(state.ResultOrigins.Register(secondOrigin, new ResultTestRetention(302, 101, 202), out _), "Replacement result was not registered.");
        var current = ConsumeResult(state, 302, Result("same", false));
        Check(current.RunId == second!.RunId && current.AssociatedWithObservedStart && state.Phase == RunLifecyclePhase.Finished
            && changes.Count(change => change.Kind == GameLifecycleChangeKind.RunEnded) == 1,
            "The replacement's own result did not close exactly one observed run.");
        state.ResultOrigins.Withdraw("fixture end");
    }
}
static void JudgementControllerIdentity()
{
    var state = Ready(); var run = state.Controller(101, true, false, "same", 201)!;
    Check(state.CaptureJudgementRun(101, 201)?.RunId == run.RunId, "The observed receiver lost judgement provenance.");
    Check(state.CaptureJudgementRun(101, 202) is null, "Judgement from another receiver borrowed the current run before sampling.");
    state.Controller(101, true, true, "same", 201);
    Check(state.CaptureJudgementRun(101, 201)?.RunId == run.RunId && state.CaptureJudgementRun(101, 202) is null,
        "The end-of-run window opened judgement provenance to another receiver.");
}
static void ResultOriginEntryBoundary()
{
    var state = Ready(); state.ResultOrigins.Activate();
    var first = state.Controller(101, true, false, "same", 201)!;
    var entry = CaptureOrigin(state, 101, 201);
    var second = state.Controller(102, true, false, "same", 202)!; // original invocation changed lifecycle
    state.Controller(102, true, true, "same", 202);
    Check(state.ResultOrigins.Register(entry, new ResultTestRetention(301, 101, 201), out _), "Captured entry was overwritten by current state.");
    var actual = state.ResultOrigins.Consume(301, out var duplicate, out var reason);
    Check(!duplicate && actual?.RunId == first.RunId, "Returned result borrowed B's identity.");
    Check(state.Finish(Result("same", false), actual, reason).RunId is null && state.RunId == second.RunId
        && state.Phase == RunLifecyclePhase.AwaitingResult, "Entry A result closed replacement B.");
    state.ResultOrigins.Withdraw("fixture end");
}
static void ResultOriginUnknown()
{
    foreach (var active in new[] { false, true })
        foreach (var missing in new[] { false, true })
        {
            var state = Ready(); if (active) state.ResultOrigins.Activate();
            var run = state.Controller(101, true, false, null, 201)!; var changes = Changes(state);
            if (missing) state.ControllerUnavailable(); else state.Controller(101, true, true, null, 201);
            var current = ConsumeResult(state, 999, Result(null, false));
            Check(current.RunId is null && state.RunId == run.RunId && changes.All(x => x.Kind != GameLifecycleChangeKind.RunEnded),
                "Unknown metadata or missing hook enabled chart-only result association.");
            state.ResultOrigins.Withdraw("fixture end");
        }
}
static void ResultOriginSceneAndTeardown()
{
    var state = Ready(); state.ResultOrigins.Activate(); state.Scene("Level");
    var start = state.Controller(101, true, false, "same", 201)!; var origin = CaptureOrigin(state, 101, 201);
    Check(state.ResultOrigins.Register(origin, new ResultTestRetention(301, 101, 201), out _), "Return missing.");
    state.ObservePause(false); state.ObservePause(true); state.ObservePause(false);
    state.Scene("Result"); state.ControllerUnavailable();
    var finished = ConsumeResult(state, 301, Result("same", false));
    Check(finished.RunId == start.RunId && finished.AssociatedWithObservedStart && state.Phase == RunLifecyclePhase.Finished,
        "A legitimate scene/revision transition lost the captured result origin.");
    state.ResultOrigins.Withdraw("fixture end");
}
static void ResultOriginWithdrawal()
{
    var state = Ready(); state.ResultOrigins.Activate(); state.Controller(101, true, false, "same", 201);
    var origin = CaptureOrigin(state, 101, 201); var retained = new ResultTestRetention(301, 101, 201);
    Check(state.ResultOrigins.Register(origin, retained, out _), "Return missing.");
    state.ResultOrigins.Withdraw("controlled observer withdrawal"); state.Controller(101, true, true, "same", 201);
    Check(retained.Disposals == 1 && ConsumeResult(state, 301, Result("same", false)).RunId is null,
        "Withdrawn origin still associated or leaked retention.");
    state.ResultOrigins.Activate(); var inFlight = new ResultTestRetention(302, 101, 201);
    Check(!state.ResultOrigins.Register(origin, inFlight, out _) && inFlight.Disposals == 1,
        "Old in-flight generation was committed after reinstall.");
    var fresh = CaptureOrigin(state, 101, 201); var current = new ResultTestRetention(303, 101, 201);
    Check(state.ResultOrigins.Register(fresh, current, out _) && ConsumeResult(state, 303, Result("same", false)).AssociatedWithObservedStart,
        "New generation could not associate a fresh original result.");
    state.ResultOrigins.Withdraw("fixture end"); state.ResultOrigins.Withdraw("idempotent");
    Equal(1, current.Disposals); Equal(0, state.ResultOrigins.RetainedCount);
}
static void ResultOriginRetentionBounds()
{
    var state = Ready(); state.ResultOrigins.Activate(); state.Controller(101, true, false, "same", 201);
    var origin = CaptureOrigin(state, 101, 201); var registry = state.ResultOrigins;
    var first = new ResultTestRetention(301, 101, 201);
    Check(registry.Register(origin, first, out _), "First return missing.");
    var duplicate = new ResultTestRetention(301, 101, 201);
    Check(!registry.Register(origin, duplicate, out _) && duplicate.Disposals == 1 && first.Disposals == 0, "Duplicate replaced rooted identity.");
    first.Result = 401; first.Information = 111; first.Controller = 211; // model targets moving while handles remain live
    var moved = registry.Consume(401, out var repeated, out _);
    Check(!repeated && moved?.Information == 111 && moved.Controller == 211, "Registry keyed an obsolete pointer rather than the root target.");
    registry.Consume(401, out repeated, out _); Check(repeated, "Consumed result repeated.");
    var conflict = new ResultTestRetention(401, 999, 999);
    Check(!registry.Register(origin with { RunId = "different" }, conflict, out _) && conflict.Disposals == 1 && registry.Conflicts == 1,
        "Conflicting result origin replaced the previous source.");
    var pending = Enumerable.Range(0, ResultOriginRegistry.MaximumPending).Select(i => new ResultTestRetention(1000 + i, 101, 201)).ToArray();
    foreach (var root in pending) Check(registry.Register(origin, root, out _), "Pending capacity rejected too early.");
    var overflow = new ResultTestRetention(9999, 101, 201);
    Check(!registry.Register(origin, overflow, out _) && overflow.Disposals == 1 && registry.PendingCount == 32,
        "Pending capacity grew or silently evicted a live unconsumed result.");
    foreach (var root in pending) registry.Consume(root.Result, out _, out _);
    Check(registry.PendingCount == 0 && registry.RetainedCount == ResultOriginRegistry.MaximumConsumed && first.Disposals == 1,
        "Consumed tombstones are unbounded or did not release their oldest root.");
    registry.Withdraw("fixture end");
    Check(pending.All(x => x.Disposals == 1) && registry.RetainedCount == 0, "Withdrawal leaked pending/consumed roots.");
}
static void ResultOriginCaptureGuards()
{
    var state = Ready(); state.ResultOrigins.Activate(); state.Controller(101, false, false, "same", 201);
    Check(state.CaptureResultOrigin(101, 201) is null, "Unobserved start acquired a result origin.");
    state.Controller(101, true, false, "same", 201);
    Check(state.CaptureResultOrigin(101, 999) is null && state.CaptureResultOrigin(999, 201) is null, "Foreign receiver graph acquired current origin.");
    var token = state.CaptureCancellationToken(101)!; state.AbortFromVerifiedCallback(token, "controlled abort");
    Check(state.CaptureResultOrigin(101, 201) is null, "Closed run acquired a new result origin.");
    state.ResultOrigins.Withdraw("fixture end");
}
static AdapterBuildProfile Valid() => new("test-build", new string('a', 64), "test", "test", "manager", "Update",
    new Dictionary<string, TypeBinding> { ["manager"] = new("Assembly-CSharp.dll", "", "GameManager") });

sealed class Profiles : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "riz-toolbox-game-checks-" + Guid.NewGuid().ToString("N"));
    public Profiles() => Directory.CreateDirectory(Path);
    public void Write(string name, AdapterBuildProfile profile) => File.WriteAllText(System.IO.Path.Combine(Path, name), JsonSerializer.Serialize(profile));
    public void Dispose() => Directory.Delete(Path, true);
}

sealed class TestLogger : ILogger { public void Log(LogLevel level, string message, Exception? exception = null) { } }
sealed class BackTestLease : IDisposable
{
    private readonly Action? _release; public int Disposals { get; private set; }
    public BackTestLease(Action? release = null) => _release = release;
    public void Dispose() { Disposals++; _release?.Invoke(); }
}
sealed class ResultTestRetention : IResultOriginRetention
{
    public long Result { get; set; }
    public long Information { get; set; }
    public long Controller { get; set; }
    public RunFinished? TerminalSnapshot { get; init; }
    public int Disposals { get; private set; }
    internal ResultTestRetention(long result, long information, long controller) => (Result, Information, Controller) = (result, information, controller);
    public void Dispose() { Disposals++; }
}
sealed class LifecyclePlugin : IRizPlugin
{
    private readonly Action<IPluginContext> _enable;
    public PluginManifest Manifest { get; }
    public LifecyclePlugin(string id, Action<IPluginContext> enable) => (Manifest, _enable) = (new(id, new(1, 0, 0), new(1, 0, 0)), enable);
    public void Enable(IPluginContext context) => _enable(context);
    public void Disable() { }
}
sealed class LifecycleFixture : IDisposable
{
    public PluginHost Host { get; }
    public GameEventState State { get; } = new();
    private readonly MainThreadDispatcher _main = new();
    public LifecycleFixture()
    {
        var logger = new TestLogger(); var events = new EventHub(logger);
        Host = new(new(new("test", "test", "test", "test")), _main, events, logger);
        State.BindPublisher(events, events.Publish);
    }
    public void Register(string id, Action<IPluginContext> enable)
    { var result = Host.Register(new LifecyclePlugin(id, enable)); if (!result.Success) throw new InvalidOperationException(result.Error?.Message); }
    public void Dispose() { Host.Dispose(); _main.Dispose(); }
}
