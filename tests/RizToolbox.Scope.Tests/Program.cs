using RizToolbox.Core;
using RizToolbox.Sdk;

var checks = new (string Name, Action Run)[]
{
    ("Child scopes have unique identities and independent owned UI", () => {
        using var f = new Fixture(); var ui = new Ui(); f.Capabilities.Register<IUiService>(CapabilityIds.Ui, ui);
        var a = f.Context.CreateScope("panel"); var b = f.Context.CreateScope("panel");
        Check(a.ScopeId != b.ScopeId, "Scope identities collide.");
        var panel = a.GetService<IUiService>(CapabilityIds.Ui).Service!.CreatePanel(new("one", "title", "text")).Value!;
        b.GetService<IUiService>(CapabilityIds.Ui).Service!.CreatePanel(new("two", "title", "text"));
        a.Dispose(); Equal(1, ui.Released); Check(!panel.SetText("stale").Success, "Expired panel still works.");
        Check(b.IsActive && !f.Context.Lifetime.IsCancellationRequested, "Sibling or parent stopped.");
        b.Dispose(); b.Dispose(); Equal(2, ui.Released);
    }),
    ("Parent expiry invalidates and cancels all children before resource cleanup", () => {
        using var f = new Fixture(); var a = f.Context.CreateScope("a"); var b = a.CreateScope("b"); var c = f.Context.CreateScope("c");
        a.Own(new Cleanup(() => Check(!a.IsActive && !b.IsActive && !c.IsActive && a.Lifetime.IsCancellationRequested && b.Lifetime.IsCancellationRequested && c.Lifetime.IsCancellationRequested, "Cleanup ran before tree expiration.")));
        f.Host.Disable("probe"); Check(f.Context.Lifetime.IsCancellationRequested, "Parent token not cancelled.");
    }),
    ("Cancellation reentrancy can dispose a sibling without leaving an uncancelled token", () => {
        using var f = new Fixture(); var a = f.Context.CreateScope("a"); var b = f.Context.CreateScope("b"); var released = 0;
        b.Own(new Cleanup(() => released++)); using var registration = a.Lifetime.Register(b.Dispose);
        f.Host.Disable("probe"); Equal(1, released); Check(b.Lifetime.IsCancellationRequested, "Sibling token missed cancellation.");
    }),
    ("Child event handlers are removed when only that child expires", () => {
        using var f = new Fixture(); var child = f.Context.CreateScope("events"); var count = 0;
        child.Events.Subscribe<int>(_ => count++); f.Events.Publish(1); child.Dispose(); f.Events.Publish(2);
        Equal(1, count); Check(!f.Context.Lifetime.IsCancellationRequested, "Child disposal stopped parent.");
    }),
    ("Scene boundary expires scene scopes before ordinary listeners", () => {
        using var f = new Fixture(); var scene = f.Context.CreateScope("scene", ScopeLifetime.Scene); var manual = f.Context.CreateScope("manual");
        var seen = false; f.Context.Events.Subscribe<GameSceneChanged>(_ => { seen = true; Check(!scene.IsActive, "Scene listener retained old scope."); });
        f.Events.Publish(new GameSceneChanged("one", "two")); Check(seen && manual.IsActive, "Scene boundary damaged unrelated scope.");
    }),
    ("A scope created inside the new scene event survives that event", () => {
        using var f = new Fixture(); IPluginScope? current = null;
        f.Context.Events.Subscribe<GameSceneChanged>(_ => current = f.Context.CreateScope("new-scene", ScopeLifetime.Scene));
        f.Events.Publish(new GameSceneChanged("one", "two")); Check(current!.IsActive, "New scene scope expired immediately.");
        var previous = current; f.Events.Publish(new GameSceneChanged("two", "three")); Check(!previous.IsActive && current!.IsActive, "Next scene did not roll scopes.");
    }),
    ("Run scopes expire on finish start and scene boundaries", () => {
        using var f = new Fixture(); var first = f.Context.CreateScope("run", ScopeLifetime.Run);
        f.Events.Publish(new RunFinished("r1", "chart", 1, 0, 0, 0, 0, 0, false, false)); Check(!first.IsActive, "Finish retained run scope.");
        var second = f.Context.CreateScope("run", ScopeLifetime.Run); f.Events.Publish(new RunStarted("r2", "chart")); Check(!second.IsActive, "New start retained previous run.");
        var third = f.Context.CreateScope("run", ScopeLifetime.Run); f.Events.Publish(new GameSceneChanged("level", "menu")); Check(!third.IsActive, "Scene retained run scope.");
    }),
    ("Associated lifecycle ends expire run resources before maximum-priority listeners", () => {
        foreach (var reason in new[] { RunEndReason.Completed, RunEndReason.Failed, RunEndReason.Skipped, RunEndReason.Aborted })
        {
            using var f = new Fixture(); var lifecycle = new LifecycleProbe(Snapshot("session", 10, "run"));
            f.Capabilities.Register<IGameLifecycleService>(GameLifecycleCapabilityIds.Lifecycle, lifecycle);
            IPluginScope? run = null; var released = 0; var legacyResults = 0;
            f.Context.Events.Subscribe<GameLifecycleChange>(_ => Check(!run!.IsActive && run.Lifetime.IsCancellationRequested && released == 1, "Public listener observed a live ended scope."), int.MaxValue);
            f.Context.Events.Subscribe<RunFinished>(_ => legacyResults++);
            run = f.Context.CreateScope("run", ScopeLifetime.Run); run.Own(new Cleanup(() => released++));
            lifecycle.Snapshot = Snapshot("session", 11, "run", RunLifecyclePhase.Finished);
            // Aborts have a verified lifecycle endpoint and no actual game result to fabricate.
            var result = reason == RunEndReason.Aborted ? null : Finish("session", 11, "run", reason);
            f.Events.Publish(Change(GameLifecycleChangeKind.RunEnded, lifecycle.Snapshot, reason, result));
            Check(!run.IsActive && released == 1, "RunEnded retained owned resources for " + reason); Equal(0, legacyResults);
        }
    }),
    ("Lifecycle binding ignores other sessions runs unassociated results and stale revisions", () => {
        using var f = new Fixture(); var lifecycle = new LifecycleProbe(Snapshot("current", 20, "live"));
        f.Capabilities.Register<IGameLifecycleService>(GameLifecycleCapabilityIds.Lifecycle, lifecycle);
        var run = f.Context.CreateScope("run", ScopeLifetime.Run);
        f.Events.Publish(Change(GameLifecycleChangeKind.RunEnded, Snapshot("old-session", 999, "live", RunLifecyclePhase.Finished), RunEndReason.Aborted));
        f.Events.Publish(Change(GameLifecycleChangeKind.RunEnded, Snapshot("current", 19, "live", RunLifecyclePhase.Finished), RunEndReason.Aborted));
        f.Events.Publish(Change(GameLifecycleChangeKind.RunEnded, Snapshot("current", 21, "other", RunLifecyclePhase.Finished), RunEndReason.Aborted));
        var unrelated = Finish("current", 22, null) with { AssociatedWithObservedStart = false };
        f.Events.Publish(Change(GameLifecycleChangeKind.ResultObserved, Snapshot("current", 22, "live"), RunEndReason.Completed, unrelated));
        f.Events.Publish(unrelated);
        f.Events.Publish(Finish("current", 23, "previous"));
        f.Events.Publish(Finish("old-session", 999, "live"));
        f.Events.Publish(Finish("current", 24, "live") with { AssociatedWithObservedStart = false });
        Check(run.IsActive, "An unrelated or late result terminated the current run.");
        f.Events.Publish(Change(GameLifecycleChangeKind.RunEnded, Snapshot("current", 25, "live", RunLifecyclePhase.Finished), RunEndReason.Aborted));
        Check(!run.IsActive, "The matching end was lost after unrelated events.");
    }),
    ("A run scope created by lifecycle start survives its paired legacy start", () => {
        using var f = new Fixture(); var lifecycle = new LifecycleProbe(Snapshot("session", 1, null, RunLifecyclePhase.Loading));
        f.Capabilities.Register<IGameLifecycleService>(GameLifecycleCapabilityIds.Lifecycle, lifecycle);
        var waiting = f.Context.CreateScope("waiting", ScopeLifetime.Run); IPluginScope? current = null;
        f.Context.Events.Subscribe<GameLifecycleChange>(change => {
            if (change.Kind == GameLifecycleChangeKind.RunStarted)
                current = f.Context.CreateScope("current", ScopeLifetime.Run);
        });
        lifecycle.Snapshot = Snapshot("session", 2, "one");
        f.Events.Publish(Change(GameLifecycleChangeKind.RunStarted, lifecycle.Snapshot));
        Check(!waiting.IsActive && current!.IsActive, "Start did not roll preparatory scopes.");
        f.Events.Publish(Start("session", 2, "one")); Check(current!.IsActive, "Paired legacy start expired the newly created scope.");
        f.Events.Publish(Start("session", 3, "one")); Check(current.IsActive, "Same run was treated as a different run.");
        var first = current; lifecycle.Snapshot = Snapshot("session", 4, "two");
        f.Events.Publish(Change(GameLifecycleChangeKind.RunStarted, lifecycle.Snapshot));
        f.Events.Publish(Start("session", 4, "two"));
        Check(!first.IsActive && current!.IsActive, "Retry did not expire only the previous run scope.");
        f.Events.Publish(Finish("session", 5, "one")); Check(current.IsActive, "Late old result expired the retry scope.");
    }),
    ("A new run observed in a valid snapshot expires the prior run without inventing a finish", () => {
        using var f = new Fixture(); var lifecycle = new LifecycleProbe(Snapshot("session", 10, "one"));
        f.Capabilities.Register<IGameLifecycleService>(GameLifecycleCapabilityIds.Lifecycle, lifecycle);
        var run = f.Context.CreateScope("run", ScopeLifetime.Run); var finishes = 0;
        f.Context.Events.Subscribe<RunFinished>(_ => finishes++);
        f.Events.Publish(Change(GameLifecycleChangeKind.SnapshotChanged, Snapshot("session", 11, "two")));
        Check(!run.IsActive, "A scope retained the previous run identity."); Equal(0, finishes);
    }),
    ("Lifecycle scene identity rejects delayed scene events and expires before scene observers", () => {
        using var f = new Fixture(); var lifecycle = new LifecycleProbe(Snapshot("session", 30, "run", scene: 3));
        f.Capabilities.Register<IGameLifecycleService>(GameLifecycleCapabilityIds.Lifecycle, lifecycle);
        var run = f.Context.CreateScope("run", ScopeLifetime.Run);
        f.Events.Publish(new GameSceneChanged("old", "older") { SessionId = "old", Revision = 999, SceneGeneration = 99 });
        f.Events.Publish(new GameSceneChanged("old", "older") { SessionId = "session", Revision = 29, SceneGeneration = 2 });
        Check(run.IsActive, "An old scene event expired the current run.");
        var seen = false;
        f.Context.Events.Subscribe<GameLifecycleChange>(_ => { seen = true; Check(!run.IsActive, "Scene observation preceded run scope expiry."); }, int.MaxValue);
        f.Events.Publish(Change(GameLifecycleChangeKind.SceneChanged, Snapshot("session", 31, "run", scene: 4)));
        Check(seen && !run.IsActive, "Lifecycle scene boundary failed.");
    }),
    ("Known lifecycle withdrawal invalidates scopes instead of downgrading to legacy matching", () => {
        using var f = new Fixture(); var lifecycle = new LifecycleProbe(Snapshot("session", 5, "run"));
        f.Capabilities.Register<IGameLifecycleService>(GameLifecycleCapabilityIds.Lifecycle, lifecycle);
        var run = f.Context.CreateScope("run", ScopeLifetime.Run); var released = 0; run.Own(new Cleanup(() => released++));
        f.Capabilities.SetUnavailable(GameLifecycleCapabilityIds.Lifecycle, "fixture withdrawal");
        Check(!run.IsActive && run.Lifetime.IsCancellationRequested && released == 1, "Withdrawal retained a stale run binding.");
        var unavailable = f.Context.CreateScope("unavailable", ScopeLifetime.Run);
        Check(!unavailable.IsActive, "A declared unavailable lifecycle provider silently enabled legacy mode.");
        f.Capabilities.Register<IGameLifecycleService>(GameLifecycleCapabilityIds.Lifecycle, new LifecycleProbe(Snapshot("replacement", 1, "new")));
        var fresh = f.Context.CreateScope("fresh", ScopeLifetime.Run); Check(fresh.IsActive, "Fresh provider could not bind a new scope.");
        f.Events.Publish(Finish("session", 100, "run")); Check(fresh.IsActive, "Withdrawn provider event affected its replacement.");
    }),
    ("Failed invalid and throwing lifecycle reads return expired scopes with diagnostics", () => {
        foreach (var mode in new[] { "result", "throw", "invalid", "contract" })
        {
            using var f = new Fixture(); var lifecycle = new LifecycleProbe(Snapshot("session", 1, "run"));
            if (mode == "result") lifecycle.Read = _ => Result<GameLifecycleSnapshot>.Fail(new(ErrorCode.ObservationUnavailable, "fixture read failure"));
            if (mode == "throw") lifecycle.Read = _ => throw new IOException("fixture provider failure");
            if (mode == "invalid") lifecycle.Snapshot = lifecycle.Snapshot with { SessionId = "" };
            if (mode == "contract") f.Capabilities.Register<IUiService>(GameLifecycleCapabilityIds.Lifecycle, new Ui());
            else f.Capabilities.Register<IGameLifecycleService>(GameLifecycleCapabilityIds.Lifecycle, lifecycle);
            var run = f.Context.CreateScope("run", ScopeLifetime.Run);
            Check(!run.IsActive && run.Lifetime.IsCancellationRequested, "Unsafe binding accepted " + mode);
            Check(f.Log.Warnings > 0, "Binding failure had no diagnostic for " + mode);
        }
    }),
    ("Scope creation rechecks the snapshot after installing boundaries", () => {
        foreach (var mode in new[] { "later-same", "new-run", "regression", "session", "scene", "read-failure", "withdrawal" })
        {
            using var f = new Fixture(); var lifecycle = new LifecycleProbe(Snapshot("session", 10, "run"));
            f.Capabilities.Register<IGameLifecycleService>(GameLifecycleCapabilityIds.Lifecycle, lifecycle);
            lifecycle.Read = count => {
                if (count == 1) return Result<GameLifecycleSnapshot>.Ok(lifecycle.Snapshot);
                if (mode == "read-failure") return Result<GameLifecycleSnapshot>.Fail(new(ErrorCode.ObservationUnavailable, "fixture second read failure"));
                if (mode == "withdrawal") f.Capabilities.SetUnavailable(GameLifecycleCapabilityIds.Lifecycle, "withdrawn during binding");
                return Result<GameLifecycleSnapshot>.Ok(mode switch {
                    "later-same" => Snapshot("session", 11, "run", RunLifecyclePhase.Paused),
                    "new-run" => Snapshot("session", 11, "retry"),
                    "regression" => Snapshot("session", 9, "run"),
                    "session" => Snapshot("replacement", 11, "run"),
                    "scene" => Snapshot("session", 11, "run", scene: 2),
                    _ => lifecycle.Snapshot
                });
            };
            var run = f.Context.CreateScope("run", ScopeLifetime.Run);
            Equal(mode == "later-same", run.IsActive); Equal(2, lifecycle.Reads);
            if (run.IsActive)
            {
                f.Events.Publish(Finish("session", 10, "run")); Check(run.IsActive, "Older finish defeated the reconciled snapshot revision.");
                f.Events.Publish(Finish("session", 12, "run")); Check(!run.IsActive, "Current matching finish was ignored.");
            }
        }
    }),
    ("Lifecycle scope binding tolerates reentrant same-run snapshots but closes reentrant ends", () => {
        foreach (var ending in new[] { false, true })
        {
            using var f = new Fixture(); var lifecycle = new LifecycleProbe(Snapshot("session", 10, "run"));
            f.Capabilities.Register<IGameLifecycleService>(GameLifecycleCapabilityIds.Lifecycle, lifecycle);
            lifecycle.Read = count => {
                if (count == 2)
                {
                    lifecycle.Snapshot = Snapshot("session", 11, "run", ending ? RunLifecyclePhase.Finished : RunLifecyclePhase.Paused);
                    f.Events.Publish(Change(ending ? GameLifecycleChangeKind.RunEnded : GameLifecycleChangeKind.Paused, lifecycle.Snapshot,
                        ending ? RunEndReason.Aborted : null));
                }
                return Result<GameLifecycleSnapshot>.Ok(lifecycle.Snapshot);
            };
            var run = f.Context.CreateScope("run", ScopeLifetime.Run); Equal(!ending, run.IsActive);
        }
    }),
    ("Expired queued work cannot modify game state", () => {
        using var f = new Fixture(); var child = f.Context.CreateScope("load"); var writes = 0;
        var task = Worker(() => child.MainThread.InvokeAsync(() => writes++)); child.Dispose(); f.Dispatcher.Pump();
        Check(SpinWait.SpinUntil(() => task.IsCompleted, 2000) && task.IsCanceled, "Pending task not cancelled."); Equal(0, writes);
        var late = Worker(() => child.MainThread.InvokeAsync(() => writes++)); Check(late.IsFaulted, "Expired scope accepted late work."); Equal(0, writes);
    }),
    ("Re-enable uses a fresh lifetime and rejects stale child services", () => {
        using var f = new Fixture(); var old = f.Context; var child = old.CreateScope("old");
        f.Host.Disable("probe"); Check(f.Host.Enable("probe").Success, "Re-enable failed.");
        Check(!f.Context.Lifetime.IsCancellationRequested && old.Lifetime.IsCancellationRequested, "Activation token was reused.");
        Throws<ObjectDisposedException>(() => child.CreateScope("late")); Check(f.Context.CreateScope("new").IsActive, "New scope unavailable.");
    }),
    ("Cleanup failures preserve reverse order and remaining releases", () => {
        using var f = new Fixture(); var scope = f.Context.CreateScope("cleanup"); var order = new List<int>();
        scope.Own(new Cleanup(() => order.Add(1))); scope.Own(new Cleanup(() => { order.Add(2); throw new InvalidOperationException("cleanup fault"); }));
        scope.Own(new Cleanup(() => order.Add(3))); scope.Dispose(); Check(order.SequenceEqual(new[] { 3, 2, 1 }), "Cleanup stopped or reordered."); Equal(1, f.Log.Errors);
    }),
    ("Scope disposal requires main thread and validates names", () => {
        using var f = new Fixture(); var child = f.Context.CreateScope("safe"); Exception? error = null;
        var thread = new Thread(() => { try { child.Dispose(); } catch (Exception ex) { error = ex; } }); thread.Start(); thread.Join();
        Check(error is InvalidOperationException && child.IsActive, "Wrong-thread dispose mutated scope.");
        Throws<ArgumentException>(() => f.Context.CreateScope("../bad")); Throws<ArgumentOutOfRangeException>(() => f.Context.CreateScope("bad", (ScopeLifetime)999));
    }),
    ("Cancelled queue heads release capacity before the next frame", () => {
        using var dispatcher = new MainThreadDispatcher(1); using var stop = new CancellationTokenSource(); var writes = 0;
        var first = Worker(() => dispatcher.InvokeAsync(() => writes++, stop.Token));
        var full = Worker(() => dispatcher.InvokeAsync(() => writes++)); Check(full.Exception?.InnerException is DispatcherQueueFullException, "Queue pressure has no typed failure.");
        stop.Cancel(); var next = Worker(() => dispatcher.InvokeAsync(() => writes++)); dispatcher.Pump();
        Check(first.IsCanceled && next.IsCompletedSuccessfully, "Cancelled head blocked next work."); Equal(1, writes);
        Equal(1L, dispatcher.GetSnapshot().Rejected); Equal(1L, dispatcher.GetSnapshot().Cancelled);
    }),
    ("Cancellation after execution starts does not report rollback", () => {
        using var dispatcher = new MainThreadDispatcher(); using var stop = new CancellationTokenSource(); var writes = 0;
        var task = Worker(() => dispatcher.InvokeAsync(() => { stop.Cancel(); writes++; }, stop.Token)); dispatcher.Pump();
        Check(task.IsCompletedSuccessfully, "Running action reported cancellation despite completing."); Equal(1, writes); Equal(0L, dispatcher.GetSnapshot().Cancelled);
    }),
    ("Frame pump is bounded and disposal cancels remaining requests", () => {
        using var dispatcher = new MainThreadDispatcher(); var writes = 0;
        var a = Worker(() => dispatcher.InvokeAsync(() => writes++)); var b = Worker(() => dispatcher.InvokeAsync(() => writes++));
        Equal(1, dispatcher.Pump(1)); Equal(1, writes); Equal(1, dispatcher.PendingCount); dispatcher.Dispose();
        Check(a.IsCompletedSuccessfully && b.IsCanceled, "Shutdown did not settle queued requests.");
    }),
    ("Capability declarations do not activate services and snapshots preserve generations", () => {
        using var f = new Fixture();
        f.Capabilities.DeclareMetadata("probe.service", new(1, 0), CapabilityStability.Experimental,
            new[] { new CapabilityOperationInfo("read", OperationThread.MainThread, ObservationMode.Snapshot, CleanupResponsibility.None, false, new[] { "*" }, "Test operation.") });
        var declared = f.Capabilities.Describe("probe.service").Value!; Check(!declared.Available, "Metadata activated a service.");
        f.Capabilities.Register<IUiService>("probe.service", new Ui()); var ready = f.Capabilities.Describe("probe.service").Value!;
        Check(ready.Available && ready.Generation > declared.Generation && !declared.Available, "Snapshot or generation changed in place.");
        f.Capabilities.SetUnavailable("probe.service", "temporarily absent"); var unavailable = f.Capabilities.Describe("probe.service").Value!;
        Check(!unavailable.Available && unavailable.Generation > ready.Generation && unavailable.Operations.Count == 1, "Withdrawal lost metadata or generation.");
        f.Capabilities.Register<IUiService>("probe.service", new Ui()); Check(f.Capabilities.Describe("probe.service").Value!.Generation > unavailable.Generation, "Replacement reused generation.");
    }),
    ("Plugins query live metadata through an automatically scoped service", () => {
        using var f = new Fixture();
        var provider = f.Context.GetService<ICapabilityMetadataProvider>(CapabilityIds.Metadata).Service!;
        Check(provider.Describe(CapabilityIds.Metadata).Success, "Metadata service missing.");
        Check(!provider.Describe("missing").Success, "Unknown metadata fabricated.");
        f.Host.Disable("probe"); Throws<ObjectDisposedException>(() => provider.GetAll());
    }),
    ("Internal boundaries precede even earlier public maximum-priority listeners", () => {
        using var f = new Fixture(); IPluginScope? scene = null; var seen = false;
        f.Context.Events.Subscribe<GameSceneChanged>(_ => { seen = true; Check(!scene!.IsActive, "Public priority outran expiration."); }, int.MaxValue);
        scene = f.Context.CreateScope("scene", ScopeLifetime.Scene); f.Events.Publish(new GameSceneChanged("a", "b")); Check(seen, "Listener missing.");
    }),
    ("Cancellation token state prevents execution while a later cancellation callback blocks", () => {
        using var dispatcher = new MainThreadDispatcher(); using var stop = new CancellationTokenSource();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); var writes = 0;
        var task = Worker(() => dispatcher.InvokeAsync(() => writes++, stop.Token));
        using var blocking = stop.Token.Register(() => { entered.Set(); release.Wait(); });
        var cancelling = new Thread(stop.Cancel); cancelling.Start();
        try { Check(entered.Wait(2000), "Cancellation barrier did not enter."); dispatcher.Pump(); Equal(0, writes); Check(task.IsCanceled, "Cancelled token executed before callback."); }
        finally { release.Set(); cancelling.Join(); }
    }),
    ("Throwing log sinks cannot interrupt cancellation or resource cleanup", () => {
        using var f = new Fixture(throwingLog: true); var a = f.Context.CreateScope("a"); var b = f.Context.CreateScope("b"); var released = 0;
        using var badCancellation = a.Lifetime.Register(() => throw new InvalidOperationException("callback failure"));
        b.Own(new Cleanup(() => released++)); b.Own(new Cleanup(() => throw new InvalidOperationException("cleanup failure")));
        f.Host.Disable("probe"); Check(a.Lifetime.IsCancellationRequested && b.Lifetime.IsCancellationRequested, "Sink blocked cancellation."); Equal(1, released);
    })
    ,("Structured service results reject stopped and withdrawn views before provider calls", () => {
        using var f = new Fixture(); var service = new StructuredProbe();
        f.Capabilities.Register<IStructuredProbe>("test.result", service);
        var view = f.Context.GetService<IStructuredProbe>("test.result").Service!;
        Check(view.Read().Success && view.Write().Success, "Live structured service did not execute.");
        f.Capabilities.SetUnavailable("test.result", "fixture withdrawal");
        Equal(ErrorCode.ScopeExpired, view.Read().Error!.Code); Equal(ErrorCode.ScopeExpired, view.Write().Error!.Code); Equal(2, service.Calls);
        f.Capabilities.Register<IStructuredProbe>("test.result", service);
        var fresh = f.Context.GetService<IStructuredProbe>("test.result").Service!;
        f.Host.Disable("probe"); Equal(ErrorCode.ScopeExpired, fresh.Read().Error!.Code); Equal(2, service.Calls);
    }),
    ("Structured thread failures do not invoke providers and keep owner access usable", () => {
        using var f = new Fixture(); var service = new StructuredProbe(); f.Capabilities.Register<IStructuredProbe>("test.result", service);
        var view = f.Context.GetService<IStructuredProbe>("test.result").Service!;
        var worker = Task.Run(() => (view.Read(), view.Write())).GetAwaiter().GetResult();
        Equal(ErrorCode.WrongThread, worker.Item1.Error!.Code); Equal(ErrorCode.WrongThread, worker.Item2.Error!.Code); Equal(0, service.Calls);
        Check(view.Read().Success, "Wrong-thread refusal poisoned the main-thread view."); Equal(1, service.Calls);
    })
};
var failures = 0;
foreach (var check in checks)
{
    try { check.Run(); Console.WriteLine("PASS " + check.Name); }
    catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + check.Name + ": " + ex); }
}
Console.WriteLine($"Scope checks: {checks.Length - failures}/{checks.Length} passed.");
return failures == 0 ? 0 : 1;

static Task Worker(Func<Task> action)
{
    Task? task = null; var thread = new Thread(() => { try { task = action(); } catch (Exception ex) { task = Task.FromException(ex); } });
    thread.Start(); thread.Join(); return task!;
}
static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}."); }
static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
static GameLifecycleSnapshot Snapshot(string session, long revision, string? run, RunLifecyclePhase phase = RunLifecyclePhase.Playing, long scene = 1)
    => new(session, revision, scene, "Level", run, "chart", phase, phase == RunLifecyclePhase.Paused, true, true, true, Array.Empty<OperationError>());
static RunStarted Start(string session, long revision, string run, long scene = 1)
    => new(run, "chart") { SessionId = session, Revision = revision, SceneGeneration = scene };
static RunFinished Finish(string session, long revision, string? run, RunEndReason reason = RunEndReason.Completed, long scene = 1)
    => new(run, "chart", 1, 0, 0, 0, 0, 0, reason == RunEndReason.Failed, reason == RunEndReason.Skipped)
    { SessionId = session, Revision = revision, SceneGeneration = scene, AssociatedWithObservedStart = run is not null, EndReason = reason };
static GameLifecycleChange Change(GameLifecycleChangeKind kind, GameLifecycleSnapshot snapshot, RunEndReason? reason = null, RunFinished? result = null)
    => new(kind, RunLifecyclePhase.Playing, snapshot, new(DateTimeOffset.UtcNow,
        reason == RunEndReason.Aborted ? LifecycleObservationSource.VerifiedCallback : LifecycleObservationSource.SampledGameState, 250, "fixture evidence"), reason, result);

sealed class Fixture : IDisposable
{
    public MainThreadDispatcher Dispatcher { get; } = new();
    public CaptureLog Log { get; } = new();
    public CapabilityRegistry Capabilities { get; } = new(new("test", "test", "test", "test"));
    public EventHub Events { get; }
    public PluginHost Host { get; }
    public IScopedPluginContext Context { get; private set; } = null!;
    public Fixture(bool throwingLog = false)
    {
        ILogger sink = throwingLog ? new ThrowingLog() : Log;
        Events = new(sink); Host = new(Capabilities, Dispatcher, Events, sink);
        Host.Register(new Probe(context => Context = (IScopedPluginContext)context));
        CheckActivation(Host.Enable("probe"));
    }
    private static void CheckActivation(OperationResult result) { if (!result.Success) throw new InvalidOperationException(result.Error?.Message); }
    public void Dispose() { Host.Dispose(); Dispatcher.Dispose(); }
}
sealed class Probe : IRizPlugin
{
    private readonly Action<IPluginContext> _enable;
    public Probe(Action<IPluginContext> enable) => _enable = enable;
    public PluginManifest Manifest { get; } = new("probe", new(1, 0), new(1, 0));
    public void Enable(IPluginContext context) => _enable(context);
    public void Disable() { }
}
sealed class CaptureLog : ILogger { public int Errors, Warnings; public void Log(LogLevel level, string message, Exception? exception = null) { if (level == LogLevel.Error) Errors++; if (level == LogLevel.Warning) Warnings++; } }
sealed class ThrowingLog : ILogger { public void Log(LogLevel level, string message, Exception? exception = null) => throw new IOException("diagnostic sink failure"); }
sealed class Cleanup : IDisposable { private Action? _release; public Cleanup(Action release) => _release = release; public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke(); }
sealed class LifecycleProbe : IGameLifecycleService
{
    public GameLifecycleSnapshot Snapshot; public Func<int, Result<GameLifecycleSnapshot>>? Read; public int Reads;
    public LifecycleProbe(GameLifecycleSnapshot snapshot) => Snapshot = snapshot;
    public Result<GameLifecycleSnapshot> GetSnapshot() { Reads++; return Read?.Invoke(Reads) ?? Result<GameLifecycleSnapshot>.Ok(Snapshot); }
    public Result<IGameLifecycleSubscription> Subscribe(Action<GameLifecycleChange> handler) => throw new InvalidOperationException("Scope expiry must use EventHub's boundary phase, not an ordinary lifecycle subscriber.");
}
public interface IStructuredProbe { Result<int> Read(); Result Write(); }
public sealed class StructuredProbe : IStructuredProbe
{
    public int Calls; public Result<int> Read() { Calls++; return Result<int>.Ok(7); } public Result Write() { Calls++; return Result.Ok(); }
}
sealed class Ui : IUiService
{
    public int Released;
    public OperationResult<IPanelHandle> CreatePanel(PanelDefinition definition) => OperationResult<IPanelHandle>.Ok(new Panel(definition.Id, () => Released++));
}
sealed class Panel : IPanelHandle
{
    private Action? _release;
    public Panel(string id, Action release) { Id = id; _release = release; }
    public string Id { get; }
    public OperationResult SetText(string text) => _release is not null ? OperationResult.Ok() : OperationResult.Fail(ErrorCode.ScopeExpired,"Panel scope expired.");
    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
