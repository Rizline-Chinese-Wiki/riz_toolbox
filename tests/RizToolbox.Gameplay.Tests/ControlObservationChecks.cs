using RizToolbox.Adapter.Gameplay;
using RizToolbox.Core;
using RizToolbox.Core.Diagnostics;
using RizToolbox.Sdk;

internal static class ControlObservationChecks
{
    internal static IEnumerable<(string Name, Action Run)> All => new (string, Action)[]
    {
        ("control observations preserve actual run, per-action support and unavailable outcomes", ActualChanges),
        ("game-state readiness publishes countdown, pause-menu and per-action changes without dispatch", SampledActionReadiness),
        ("the original pause readiness is checked again after the last publication", CommandRechecksReadiness),
        ("shared native observation reports the game's real pause timer and progress preparation fields", NativePauseReadiness),
        ("a failed native pause query leaves other actions and static support observable", IsolatedReadFailure),
        ("value actions require an observed song even on a ready pause menu", ValuesRequireRun),
        ("control observations share samples and register atomic semantic revisions", AtomicSharedSamples),
        ("control observation cancellation, scope stop and subscriber faults prevent late callbacks", ScopedDelivery),
        ("known scene, run and source boundaries expire published state and old write leases", BoundaryRevocation),
        ("clock sampling revokes the control publication on graph loss", () => ClockGraphLoss(ClockOperation.Sample)),
        ("clock publication revokes the control publication on a late graph loss", () => ClockGraphLoss(ClockOperation.Publish)),
        ("chart conversion revokes the control publication on graph loss", () => ClockGraphLoss(ClockOperation.Convert)),
        ("observed conversion revokes the control publication on graph loss", () => ClockGraphLoss(ClockOperation.ConvertObserved)),
        ("a command that loses its controller during preparation expires the shared observation", CommandGraphLoss),
        ("a source change observed between ticks cannot revive the old publication", ObservedSourceReturn),
        ("native observation consumes the sampled controller root without a second inventory scan", SharedNativeController)
    };

    private static void SampledActionReadiness()
    {
        using var f = new Fixture(values: true);
        using var host = CreateHost(f);
        var changes = new List<GameplayControlObservationSnapshot>();
        Enable(host, "readiness", context => Need(Controls(context).Subscribe(changes.Add)));
        f.Backend.CanDo = false;
        f.Backend.PauseError = new(ErrorCode.InvalidGameState, "Countdown active.");
        f.SampleControls();
        var opening = changes.Single();
        GameplayControlAvailability Op(GameplayControlObservationSnapshot observation, GameplayControlAction action)
            => observation.Operations.Single(op => op.Action == action);
        var pause = Op(opening, GameplayControlAction.Pause);
        Check(pause.Supported && pause.StateReadiness?.Error?.Message == "Countdown active."
            && pause.StateReadiness.Error.TargetId == "gameplay.controller/" + opening.State!.ControllerGeneration + "/Pause"
            && Op(opening, GameplayControlAction.Skip).StateReadiness?.Success == true
            && Op(opening, GameplayControlAction.Resume).StateReadiness?.Success == false,
            "The opening countdown, skip and resume predicates were conflated.");
        f.SampleControls();
        Check(changes.Count == 1, "An unchanged refusal advanced the semantic revision.");
        f.Backend.PauseError = null;
        f.SampleControls();
        Check(changes.Count == 2 && Op(changes[1], GameplayControlAction.Pause).StateReadiness?.Success == true,
            "The end of the countdown did not publish while all visible playing flags were unchanged.");
        f.Backend.Playing = false; f.Backend.Paused = true;
        f.SampleControls();
        Check(Op(changes.Last(), GameplayControlAction.Seek).StateReadiness?.Success == false, "A closing pause menu accepted seek.");
        f.Backend.CanDo = true;
        f.SampleControls();
        foreach (var action in new[] { GameplayControlAction.Resume, GameplayControlAction.Retry, GameplayControlAction.SetSpeed, GameplayControlAction.Seek })
            Check(Op(changes.Last(), action).StateReadiness?.Success == true, "A ready pause menu did not publish " + action);
        f.Backend.CaptureError = new GameplayStateException(ErrorCode.TargetMissing, "controller missing");
        f.SampleControls();
        Check(changes.Last().State is null && changes.Last().Operations.All(op => op.StateReadiness is null)
            && f.Backend.Dispatches.Count == 0 && f.Backend.Preparations == 0, "A missing controller retained readiness or an observation dispatched a command.");
    }

    private static void CommandRechecksReadiness()
    {
        using var f = new Fixture(unsupported: GameplayControlAction.Seek);
        var lease = Need(f.Controls.Acquire(true));
        var sample = Need(f.SampledControlState());
        Check(sample.Operations.Single(op => op.Action == GameplayControlAction.Seek).StateReadiness is null
            && sample.Operations.Single(op => op.Action == GameplayControlAction.Pause).StateReadiness?.Success == true,
            "Unsupported and currently-ready operations were not distinguished.");
        f.Backend.PauseError = new(ErrorCode.InvalidGameState, "A game window owns input.");
        var refused = lease.Execute(GameplayControlAction.Pause);
        Check(!refused.Success && refused.Error?.Message == "A game window owns input." && f.Backend.Dispatches.Count == 0,
            "A command used an earlier readiness result as execution permission.");
    }

    private static void IsolatedReadFailure()
    {
        using var f = new Fixture(values: true);
        f.Backend.PauseReadFailure = new GameplayStateException(ErrorCode.TargetExpired, "Pause UI disappeared.");
        var sample = Need(f.SampledControlState());
        var pause = sample.Operations.Single(op => op.Action == GameplayControlAction.Pause);
        var skip = sample.Operations.Single(op => op.Action == GameplayControlAction.Skip);
        Check(pause.Supported && pause.StateReadiness?.Error?.KnownCode == ErrorCode.TargetExpired
            && skip.StateReadiness?.Success == true && sample.ControllerAvailable && f.Backend.Dispatches.Count == 0,
            "A failed Pause UI read contaminated a healthy observation or unrelated controls.");
    }

    private static void ValuesRequireRun()
    {
        using var f = new Fixture(values: true);
        f.Lifecycle = null;
        f.Backend.Playing = false; f.Backend.Paused = true;
        var sample = Need(f.SampledControlState());
        Check(sample.ObservedRunId is null && sample.Operations.Single(op => op.Action == GameplayControlAction.Resume).StateReadiness?.Success == true,
            "A paused controller without a song identity lost its independent resume check.");
        foreach (var action in new[] { GameplayControlAction.SetSpeed, GameplayControlAction.Seek })
        {
            var op = sample.Operations.Single(item => item.Action == action);
            Check(op.Supported && op.StateReadiness?.Error?.Message.Contains("run identity") == true,
                "A value action reported ready without an observed run.");
        }
    }

    private static void NativePauseReadiness()
    {
        using var native = new FakeNative();
        native.ConfigureGameplay();
        native.SetGameplayField("pause.paused", false);
        native.SetGameplayField("progress.playing", true);
        native.SetGameplayField("pause.time", 2f);
        native.GameplayCanDo = false;
        using var main = new MainThreadDispatcher();
        using var context = new Context("countdown-observer", main);
        using var service = new GameplayService(new GameplayNativeBackend(native.GameplayApi, main,
            observedController: () => native.GameplayApi.Root(native.GameplayController)), main);
        var controls = service.ControlsForPlugin(context);
        GameplayControlAvailability Sample(long tick)
        {
            service.ObserveControlsAfterMaintenance(tick, tick, requested: true);
            return Need(controls.GetState()).Operations.Single(op => op.Action == GameplayControlAction.Pause);
        }
        var initial = Sample(1);
        Check(initial.Supported && initial.StateReadiness?.Error?.Message.Contains("countdown") == true,
            "A metadata-supported native Pause hid the positive pause timer.");
        native.SetGameplayField("pause.time", 0f);
        native.SetGameplayField("progress.preparing", true);
        Check(Sample(2).StateReadiness?.Success == false, "A pending native progress start allowed Pause.");
        native.SetGameplayField("progress.preparing", false);
        Check(Sample(3).StateReadiness?.Success == true && native.GameplayCommandCalls == 0
            && native.AllGameplayReceiversRooted, "The native countdown did not clear read-only with CanDoOperation false.");
    }

    private static void ActualChanges()
    {
        using var f = new Fixture(values: true);
        using var host = CreateHost(f);
        var captures = f.Backend.Captures; var probes = f.Backend.Probes;
        f.SampleControls(requested: false);
        Check(f.Backend.Captures == captures && f.Backend.Probes == probes, "An unobserved service sampled control state.");
        var lease = Need(f.Controls.Acquire(true));
        var seekAvailable = false;
        f.Backend.AvailabilityTransform = operation => operation.Action != GameplayControlAction.Seek ? operation : operation with
        {
            Supported = seekAvailable, Evidence = seekAvailable ? "current source supports seek" : "current source is not ready",
            Error = seekAvailable ? null : new(ErrorCode.CapabilityUnavailable, "Current seek prerequisites are unavailable."),
            ValueUnits = seekAvailable ? new[] { GameplayControlValueUnit.GameSeconds } : Array.Empty<GameplayControlValueUnit>()
        };
        IGameplayControlSubscription? subscription = null;
        var seen = new List<GameplayControlObservationSnapshot>();
        f.SampleControls();
        Enable(host, "observer", context => subscription = Need(Controls(context).Subscribe(seen.Add)));
        var initial = subscription!.InitialSnapshot;
        Check(seen.Count == 0 && initial.State is { } initialState && initialState.ObservedRunId == "controlled-run" && initialState.CurrentOwner == "plugin"
            && !Seek(initial).Supported && initial.Error is null, "Initial observation invented support, ownership or a replay.");
        seekAvailable = true; f.SampleControls(requested: false);
        Check(seen.Count == 1 && Seek(seen[0]).Supported && Seek(seen[0]).ValueUnits.SequenceEqual(new[] { GameplayControlValueUnit.GameSeconds })
            && seen[0].State?.ControllerGeneration == lease.ControllerGeneration, "A real per-action probe change was missed.");
        f.Lifecycle = f.Lifecycle! with { RunId = "next-run" };
        f.SampleControls(requested: false);
        Check(seen.Count == 2 && seen[1].State is { } nextState && nextState.ObservedRunId == "next-run" && nextState.ControllerGeneration > lease.ControllerGeneration
            && nextState.CurrentOwner is null, "A changed observed run kept or automatically reacquired ownership.");
        Error(lease.Execute(GameplayControlAction.Pause), ErrorCode.TargetExpired);
        f.Backend.CaptureError = new GameplayStateException(ErrorCode.TargetMissing, "controller missing");
        f.SampleControls(requested: false); f.SampleControls(requested: false);
        Check(seen.Count == 3 && seen[2].State is null && seen[2].Error?.KnownCode == ErrorCode.TargetMissing && Seek(seen[2]).Supported,
            "Controller unavailability hid the actual operation probe or emitted duplicates.");
        f.Backend.ProbeError = new IOException("probe unavailable");
        f.SampleControls(requested: false); f.SampleControls(requested: false);
        Check(seen.Count == 4 && seen[3].State is null && seen[3].Error?.KnownCode == ErrorCode.TargetMissing
            && seen[3].ProbeError?.KnownCode == ErrorCode.InternalError && seen[3].Operations.Count == 0,
            "A probe failure replaced the state failure or fabricated operation availability.");
        Check(f.Backend.Dispatches.Count == 0, "Read-only observations dispatched a control.");
    }

    private static void AtomicSharedSamples()
    {
        var performance = new CallbackPerformanceMonitor();
        using var f = new Fixture(); using var host = CreateHost(f, performance);
        var first = new List<GameplayControlObservationSnapshot>(); var second = new List<GameplayControlObservationSnapshot>();
        var nested = new List<GameplayControlObservationSnapshot>();
        IGameplayControlSubscription? initial = null, later = null;
        IGameplayControlService? controls = null;
        IGameplayControlService? peer = null;
        var captures = f.Backend.Captures;
        var probes = f.Backend.Probes;
        Enable(host, "first", context =>
        {
            var currentControls = Controls(context); controls = currentControls;
            initial = Need(currentControls.Subscribe(snapshot =>
            {
                first.Add(snapshot);
                Check(ReferenceEquals(Need(currentControls.GetState()), snapshot.State), "Recursive query did not read the delivered state.");
                later ??= Need(currentControls.Subscribe(nested.Add));
                f.Service.ObserveControlsAfterMaintenance(f.HostTickSequence, f.HostTickSequence + 1, requested: false);
            }));
        });
        Enable(host, "second", context =>
        {
            peer = Controls(context);
            Need(peer.Subscribe(second.Add));
        });
        Check(initial!.InitialSnapshot.Error?.Code == ErrorCode.NotReady, "Registration made its own initial capture.");
        Error(controls!.GetState(), ErrorCode.NotReady);
        Error(peer!.GetState(), ErrorCode.NotReady);
        Check(f.Backend.Captures == captures && f.Backend.Probes == probes, "First queries or subscriptions touched the backend.");
        f.SampleControls(requested: false);
        Check(first.Count == 1 && second.Count == 1 && ReferenceEquals(first[0], second[0]) && nested.Count == 0
            && ReferenceEquals(later?.InitialSnapshot, first[0]) && ReferenceEquals(Need(peer.GetState()), first[0].State)
            && first[0].HostTickSequence == f.HostTickSequence && first[0].ObservedAtMonotonicSeconds == f.HostTickSequence
            && first[0].State?.ObservationRevision == first[0].Revision && f.Backend.Captures == captures + 1 && f.Backend.Probes == probes + 1,
            "Readers or late registration received different state, replayed the current observation, or resampled the backend.");
        var deliveries = performance.GetSnapshot().Callbacks.Where(row => row.Operation.EndsWith("+ControlObservationDelivery", StringComparison.Ordinal));
        Check(deliveries.Sum(row => row.TotalSamples) == 2, "Delivery visited other plugin scopes for each individual subscriber.");
        f.Service.ObserveControlsAfterMaintenance(f.HostTickSequence, f.HostTickSequence + 1, requested: true);
        Check(f.Backend.Captures == captures + 1 && f.Backend.Probes == probes + 1, "An admitted Tick was sampled twice.");
        f.SampleControls(requested: false);
        Check(first.Count == 1 && second.Count == 1 && nested.Count == 0, "An unchanged observation was replayed after nested registration.");
        f.Backend.Paused = true; f.Backend.Playing = false; captures = f.Backend.Captures; probes = f.Backend.Probes;
        f.SampleControls(requested: false);
        Check(first.Count == 2 && second.Count == 2 && nested.Count == 1 && nested[0].Revision > later!.InitialSnapshot.Revision
            && first[1].Revision > initial!.InitialSnapshot.Revision && f.Backend.Captures == captures + 1 && f.Backend.Probes == probes + 1,
            "Later semantic changes were reordered, lost or sampled per subscriber.");
        captures = f.Backend.Captures; probes = f.Backend.Probes;
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Error(controls!.Subscribe(_ => { }, cancelled.Token), ErrorCode.Cancelled);
        Error(controls.Subscribe(null!), ErrorCode.InvalidArgument);
        var wrongThread = Task.Run(() => controls.Subscribe(_ => { })).GetAwaiter().GetResult();
        Error(wrongThread, ErrorCode.WrongThread);
        Check(f.Backend.Captures == captures && f.Backend.Probes == probes, "A registration guard accessed the backend.");
    }

    private static void SharedNativeController()
    {
        using var native = new FakeNative();
        native.ConfigureGameplay();
        using var main = new MainThreadDispatcher();
        using var context = new Context("sampled-controller", main);
        using var observed = native.GameplayApi.Root(native.GameplayController);
        var sampled = true;
        var rootRequests = 0;
        using (var service = new GameplayService(new GameplayNativeBackend(native.GameplayApi, main, observedController: () =>
        {
            rootRequests++;
            return sampled ? native.GameplayApi.Root(observed.Target) : null;
        }), main))
        {
            var controls = service.ControlsForPlugin(context);
            // There is no full scan result to fall back to. Only the shared
            // observer supplies this capture's still-owned controller.
            native.GameplayControllers = IntPtr.Zero;
            service.ObserveControlsAfterMaintenance(1, 1, requested: true);
            var result = Need(controls.GetState());
            Check(result.ControllerAvailable && result.Paused && rootRequests == 1 && native.AllGameplayReceiversRooted,
                "Observation rescanned or read an unrooted controller.");
            var invocations = native.Invocations;
            Check(ReferenceEquals(result, Need(controls.GetState())) && native.Invocations == invocations,
                "Reading a published state repeated an IL2CPP invocation.");
            service.ObserveControlsAfterMaintenance(2, 1.1, requested: true);
            Check(rootRequests == 1, "A second Tick sampled before the interval was due.");
            Check(!controls.Acquire(true).Success && native.GameplayCommandCalls == 0,
                "The observed controller was accepted as current write authority.");
            sampled = false;
            service.ObserveControlsAfterMaintenance(3, 2, requested: true);
            Error(controls.GetState(), ErrorCode.NotReady);
            Check(rootRequests == 2 && native.ActiveRoots == 1,
                "A missing sampled controller fell back to inventory discovery or leaked roots.");
        }
        Check(native.ActiveRoots == 1, "Service disposal released a root owned by the shared sampler.");
    }

    private static void BoundaryRevocation()
    {
        using var f = new Fixture(values: true);
        using var host = CreateHost(f);
        f.ControlSource = new object();
        f.SampleControls();
        var old = Need(f.Controls.GetState());
        var request = new GameplayControlRequest(GameplayControlAction.Pause, old.ControllerGeneration, old.ObservedRunId);
        using var lease = Need(f.Controls.Acquire(true));
        var captures = f.Backend.Captures;
        var probes = f.Backend.Probes;
        f.Lifecycle = f.Lifecycle! with { SceneGeneration = f.Lifecycle!.SceneGeneration + 1 };
        Error(f.Controls.GetState(), ErrorCode.TargetExpired);
        Check(f.Backend.Captures == captures && f.Backend.Probes == probes, "Invalidating a query touched native state.");
        Error(lease.Execute(request), ErrorCode.TargetExpired);
        Check(f.Backend.Dispatches.Count == 0, "An old scene lease wrote through the new graph.");
        f.SampleControls();
        Check(Need(f.Controls.GetState()).ControllerGeneration > old.ControllerGeneration, "A scene transition reused the control generation.");

        var peerCalls = 0;
        var changedSource = false;
        Enable(host, "source-observer", context => Need(Controls(context).Subscribe(_ =>
        {
            if (changedSource) return;
            changedSource = true;
            f.ControlSource = new object();
            Error(f.Controls.GetState(), ErrorCode.TargetExpired);
        })));
        Enable(host, "peer-observer", context => Need(Controls(context).Subscribe(_ => peerCalls++)));
        f.Backend.Paused = true;
        f.SampleControls();
        Check(changedSource && peerCalls == 0, "A peer received a publication after its source had ended.");
        f.SampleControls();
        Check(peerCalls == 1 && Need(f.Controls.GetState()).Paused && !old.Paused,
            "The successor was not sampled or an older immutable snapshot changed.");

        f.Lifecycle = f.Lifecycle with { RunId = "another-run" };
        Error(f.Controls.GetState(), ErrorCode.TargetExpired);
        f.SampleControls();
        Check(Need(f.Controls.GetState()).ObservedRunId == "another-run" && f.Backend.DspReads == 0
            && f.Backend.Preparations == 0, "Publication did not follow the new run or started a clock/control operation.");
    }

    private enum ClockOperation { Sample, Publish, Convert, ConvertObserved }

    private static void ClockGraphLoss(ClockOperation operation)
    {
        var hostReads = 0;
        Action? onHostRead = null;
        using var f = new Fixture(values: true, monotonicSeconds: () => { onHostRead?.Invoke(); return ++hostReads; });
        using var lease = Need(f.Controls.Acquire(true));
        var original = Need(f.SampledControlState());
        var controller = f.Backend.ControllerIdentity;
        void ReplaceController() => f.Backend.ControllerIdentity = controller + 1;
        if (operation == ClockOperation.Publish)
            onHostRead = () => { onHostRead = ReplaceController; };
        else if (operation == ClockOperation.Convert)
            f.Backend.OnConvert = ReplaceController;
        else
            f.Backend.OnReadMapping = ReplaceController;

        switch (operation)
        {
            case ClockOperation.Convert:
                Error(f.Clocks.Convert(new(original.ControllerGeneration, original.ObservedRunId!, 1,
                    GameplayClockUnit.Seconds, GameplayClockUnit.ChartCoordinates)), ErrorCode.TargetExpired);
                break;
            case ClockOperation.ConvertObserved:
                Error(f.Clocks.ConvertObserved(new(original.ControllerGeneration, original.ObservedRunId!, 1, 1,
                    GameplayClockDomain.GameProgressSeconds, GameplayClockDomain.AudioDspSeconds)), ErrorCode.TargetExpired);
                break;
            default:
                var clock = Need(f.Clocks.Read());
                Check(!clock.GameTime.Available && clock.AudioDspTime.Available, "A mixed clock sample escaped.");
                break;
        }

        // A new controller can occupy the original slot before the host's next observation.
        f.Backend.ControllerIdentity = controller;
        onHostRead = null;
        f.Backend.OnReadMapping = null;
        f.Backend.OnConvert = null;
        var captures = f.Backend.Captures;
        Error(f.Controls.GetState(), ErrorCode.TargetExpired);
        Error(lease.Execute(GameplayControlAction.Pause), ErrorCode.TargetExpired);
        Check(f.Backend.Captures == captures && f.Backend.Dispatches.Count == 0, "Old state reacquired a controller.");
        var successor = Need(f.SampledControlState());
        Check(successor.ControllerGeneration > original.ControllerGeneration && successor.ObservationRevision > original.ObservationRevision,
            "The successor reused the old controller generation or observation.");
    }

    private static void CommandGraphLoss()
    {
        using var f = new Fixture(values: true);
        f.Backend.Playing = false;
        f.Backend.Paused = true;
        using var lease = Need(f.Controls.Acquire(true));
        var original = Need(f.SampledControlState());
        var controller = f.Backend.ControllerIdentity;
        f.Backend.OnPrepare = () => f.Backend.ControllerIdentity++;
        var request = new GameplayControlRequest(GameplayControlAction.SetSpeed, lease.ControllerGeneration, lease.ObservedRunId,
            1.5, GameplayControlValueUnit.PlaybackRateMultiplier);
        Error(lease.Execute(request), ErrorCode.TargetExpired);
        f.Backend.ControllerIdentity = controller;
        Error(f.Controls.GetState(), ErrorCode.TargetExpired);
        var successor = Need(f.SampledControlState());
        Check(f.Backend.ValueCommits == 0 && successor.ControllerGeneration > original.ControllerGeneration,
            "A rejected preparation committed a value or let the successor reuse its generation.");
    }

    private static void ObservedSourceReturn()
    {
        using var f = new Fixture(values: true);
        var source = new object();
        f.ControlSource = source;
        var original = Need(f.SampledControlState());
        f.ControlSource = null;
        Error(f.Controls.GetState(), ErrorCode.TargetExpired);
        f.ControlSource = source;
        Error(f.Controls.GetState(), ErrorCode.TargetExpired);
        f.Service.ObserveControlsAfterMaintenance(++f.HostTickSequence, original.ObservedAtMonotonicSeconds + .001, requested: true);
        var successor = Need(f.Controls.GetState());
        Check(successor.ObservationRevision > original.ObservationRevision && successor.HostTickSequence == f.HostTickSequence,
            "Observed source loss did not force a fresh sample before the polling interval.");
    }

    private static void ScopedDelivery()
    {
        using var f = new Fixture(); using var host = CreateHost(f); using var cancellation = new CancellationTokenSource();
        var bad = 0; var good = 0; var stopped = 0; var cancelled = 0;
        IPluginScope? child = null; IGameplayControlService? scoped = null, cancellable = null;
        Enable(host, "bad", context => Need(Controls(context).Subscribe(_ => { bad++; throw new IOException("observer failed"); })));
        Enable(host, "good", context =>
        {
            child = ((IScopedPluginContext)context).CreateScope("control-watch"); scoped = Controls(child);
            Need(scoped.Subscribe(_ => good++));
        });
        Enable(host, "stop-first", context => Need(Controls(context).Subscribe(_ =>
        {
            host.Disable("stopped"); Task.Run(cancellation.Cancel).GetAwaiter().GetResult();
        })));
        Enable(host, "stopped", context => Need(Controls(context).Subscribe(_ => stopped++)));
        Enable(host, "cancelled", context =>
        {
            cancellable = Controls(context); Need(cancellable.Subscribe(_ => cancelled++, cancellation.Token));
        });
        f.Backend.Paused = true; f.Backend.Playing = false; f.SampleControls(requested: false);
        Check(bad == 1 && good == 1 && stopped == 0 && cancelled == 0
            && host.Statuses.Single(x => x.Id == "bad").State == PluginState.Faulted
            && host.Statuses.Single(x => x.Id == "good").State == PluginState.Enabled,
            "A callback fault spread to peers or a stopped/cancelled subscriber received late delivery.");
        f.Backend.CanDo = false; f.SampleControls(requested: false);
        Check(bad == 1 && good == 2 && stopped == 0 && cancelled == 0, "A stopped observer revived or a healthy observer stopped.");
        child!.Dispose(); Error(scoped!.Subscribe(_ => { }), ErrorCode.ScopeExpired); host.Disable("stop-first");
        using var finalCancellation = new CancellationTokenSource();
        Need(cancellable!.Subscribe(_ => cancelled++, finalCancellation.Token));
        Task.Run(finalCancellation.Cancel).GetAwaiter().GetResult();
        var captures = f.Backend.Captures; var probes = f.Backend.Probes;
        f.Backend.CanDo = true; f.SampleControls(requested: false);
        Check(good == 2 && cancelled == 0 && f.Backend.Captures == captures && f.Backend.Probes == probes,
            "Expired scope/token observers kept polling or received callbacks after cancellation.");
    }

    private static GameplayControlAvailability Seek(GameplayControlObservationSnapshot snapshot)
        => snapshot.Operations.Single(x => x.Action == GameplayControlAction.Seek);
    private static IGameplayControlService Controls(IPluginContext context)
        => context.GetService<IGameplayControlService>(GameplayCapabilityIds.Controls).Service ?? throw new InvalidOperationException("Control service missing.");
    private static PluginHost CreateHost(Fixture fixture, CallbackPerformanceMonitor? performance = null)
    {
        var capabilities = new CapabilityRegistry(new("test", "test", "test", "test"));
        capabilities.RegisterScoped<IGameplayControlService>(GameplayCapabilityIds.Controls, fixture.Service.ControlsForPlugin);
        var events = new EventHub(new Logger()) { Performance = performance };
        return new PluginHost(capabilities, fixture.Main, events, new Logger());
    }
    private static void Enable(PluginHost host, string id, Action<IPluginContext> enable)
    { host.Register(new ObserverPlugin(id, enable)); Check(host.Enable(id).Success, "Observer plugin failed to enable: " + id); }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static T Need<T>(Result<T> result)
    { Check(result.Success && result.Value is not null, result.Error?.Message ?? "Missing result."); return result.Value!; }
    private static void Error<T>(Result<T> result, ErrorCode code)
        => Check(!result.Success && result.Error?.KnownCode == code, "Expected " + code + "; got " + result.Error);
    private sealed class ObserverPlugin : IRizPlugin
    {
        private readonly Action<IPluginContext> _enable;
        public PluginManifest Manifest { get; }
        internal ObserverPlugin(string id, Action<IPluginContext> enable) => (Manifest, _enable) = (new(id, new(1, 0), new(1, 0)), enable);
        public void Enable(IPluginContext context) => _enable(context);
        public void Disable() { }
    }
}
