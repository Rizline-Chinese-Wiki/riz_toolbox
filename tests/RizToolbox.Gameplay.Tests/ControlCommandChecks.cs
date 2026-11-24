using RizToolbox.Adapter.Gameplay;
using RizToolbox.Sdk;

internal static class ControlCommandChecks
{
    internal static IEnumerable<(string Name, Action Run)> All => new (string, Action)[]
    {
        ("Queued rate commands expose immutable state and reserve the session before probe and capture", ExecutesOnce),
        ("Queue cancellation, scope expiry and stale target cannot dispatch a rate command", CancelAndExpire),
        ("An executing command distinguishes confirmed restoration from unknown and never repeats the write", TerminalOutcomes),
        ("Lease acquisition also refuses reentry from a live capture", AcquireReentry),
        ("Plugin shutdown inside the post-commit graph check restores without confirming", StopDuringValidation),
        ("Deferred backend cleanup preserves the restored command result for queue and immediate execution", DeferredCloseFault),
        ("Subscriber cleanup failure still cancels queued commands and retires independent resources", CloseWithSubscriberFault)
    };

    private static void ExecutesOnce()
    {
        using var f = new Fixture(values: true);
        using var lease = PauseAndAcquire(f);
        var request = Speed(lease);
        var probes = f.Backend.Probes;
        var captures = f.Backend.Captures;
        var command = Need(lease.Submit(request));
        var queued = command.Snapshot;
        Check(queued.State == GameplayControlCommandState.Queued && queued.Receipt is null, "Submission entered live work.");
        Check(Task.Run(() => command.Snapshot).GetAwaiter().GetResult() == queued, "Reading the record required the game thread.");
        Error(lease.Submit(request), ErrorCode.Conflict);
        Error(lease.Execute(request), ErrorCode.Conflict);
        Check(Task.Run(command.Cancel).GetAwaiter().GetResult().Error?.Code == ErrorCode.WrongThread, "Worker cancelled queued work.");
        Check(f.Backend.Probes == probes && f.Backend.Captures == captures, "Queue operations accessed the game.");

        var duringProbe = false;
        var duringCapture = false;
        void Reenter()
        {
            Check(command.Snapshot.State == GameplayControlCommandState.Executing, "Live access ran before publishing Executing.");
            Check(command.Cancel().Error?.Code == ErrorCode.Conflict, "An executing command was cancelled directly.");
            Error(lease.Execute(request), ErrorCode.Conflict);
            Error(lease.Submit(request), ErrorCode.Conflict);
            Error(f.Controls.Acquire(true), ErrorCode.Conflict);
        }
        f.Backend.AvailabilityTransform = operation =>
        {
            if (!duringProbe) { duringProbe = true; Reenter(); }
            return operation;
        };
        f.Backend.OnCapture = () => { duringCapture = true; Reenter(); };
        f.Service.PumpControlCommand();
        f.Service.PumpControlCommand();
        var completed = command.Snapshot;
        Check(duringProbe && duringCapture && f.Backend.ValueCommits == 1, "Reentry changed the number of value commits.");
        Check(completed.State == GameplayControlCommandState.Succeeded && completed.Id == queued.Id
            && completed.Receipt?.Modification.ConfirmedValue == request.Value
            && queued.State == GameplayControlCommandState.Queued, "The record lost its exact immutable command or readback.");
    }

    private static void CancelAndExpire()
    {
        using var f = new Fixture(values: true);
        using var lease = PauseAndAcquire(f);
        var requested = Speed(lease);
        var probes = f.Backend.Probes;
        var explicitCancel = Need(lease.Submit(requested));
        Check(explicitCancel.Cancel().Success && explicitCancel.Cancel().Success, "A queued cancellation did not settle once.");
        using var withdrawal = new CancellationTokenSource();
        var tokenCancel = Need(lease.Submit(requested, withdrawal.Token));
        withdrawal.Cancel();
        f.Service.PumpControlCommand();
        var leaseCancel = Need(lease.Submit(requested));
        lease.Dispose();
        f.Service.PumpControlCommand();
        Check(new[] { explicitCancel, tokenCancel, leaseCancel }.All(command => command.Snapshot.State == GameplayControlCommandState.Cancelled)
            && f.Backend.Probes == probes && f.Backend.ValueCommits == 0, "Cancelled work reached live execution.");

        using var next = PauseAndAcquire(f);
        var stale = Need(next.Submit(Speed(next)));
        f.Lifecycle = f.Lifecycle! with { RunId = "replacement-run", SceneGeneration = 2 };
        f.Service.PumpControlCommand();
        Check(stale.Snapshot.State == GameplayControlCommandState.Rejected && stale.Snapshot.Error?.Code == ErrorCode.TargetExpired
            && f.Backend.ValueCommits == 0, "A queued command wrote into a successor run.");
    }

    private static void TerminalOutcomes()
    {
        foreach (var restore in new[] { true, false })
        {
            using var f = new Fixture(values: true);
            using var lease = PauseAndAcquire(f);
            f.Backend.CommitFaultStage = 1;
            f.Backend.RestoreFails = !restore;
            var command = Need(lease.Submit(Speed(lease)));
            f.Service.PumpControlCommand();
            var attempts = f.Backend.ValueCommits;
            f.Service.PumpControlCommand();
            var terminal = command.Snapshot;
            Check(terminal.State == (restore ? GameplayControlCommandState.Rejected : GameplayControlCommandState.Unknown)
                && terminal.Error?.Code == ErrorCode.InternalError && terminal.Receipt is null && attempts == 1
                && f.Backend.ValueCommits == attempts, "The command falsely succeeded, restored, or replayed.");
            if (restore) continue;
            f.Backend.CaptureError = new GameplayStateException(ErrorCode.TargetMissing, "temporary absence");
            f.SampleControls();
            f.Backend.CaptureError = null;
            f.Lifecycle = f.Lifecycle! with { Phase = RunLifecyclePhase.ObservationLost, Paused = null };
            f.SampleControls();
            using var renewed = PauseAndAcquire(f);
            Error(renewed.Execute(Speed(renewed)), ErrorCode.OperationUnsupported);
            Check(f.Backend.ValueCommits == attempts, "A temporary capture failure reopened an unknown write on the same source.");
            f.Backend.Identity++;
            f.Backend.CommitFaultStage = 0;
            using var replacement = PauseAndAcquire(f);
            Need(replacement.Execute(Speed(replacement)));
            Check(f.Backend.ValueCommits == attempts + 1, "A proven replacement inherited a previous source's refusal.");
        }
    }

    private static void AcquireReentry()
    {
        using var f = new Fixture(values: true);
        var recursive = 0;
        f.Backend.OnCapture = () =>
        {
            recursive++;
            Error(f.Controls.Acquire(true), ErrorCode.Conflict);
        };
        using var lease = PauseAndAcquire(f);
        Check(recursive == 1 && f.Backend.Captures == 1, "Acquisition reentered capture.");
    }

    private static void StopDuringValidation()
    {
        using var f = new Fixture(values: true);
        using var lease = PauseAndAcquire(f);
        var before = f.Backend.Values;
        var stopped = 0;
        f.Backend.OnGraphCheck = () =>
        {
            if (f.Backend.ValueCommits == 0) return;
            f.Backend.OnGraphCheck = null;
            f.Context.Dispose();
            stopped++;
        };
        var command = Need(lease.Submit(Speed(lease)));
        f.Service.PumpControlCommand();
        Check(stopped == 1 && f.Backend.ValueConfirmations == 0, "A retired plugin reached value confirmation after its graph getter returned.");
        Check(command.Snapshot.State == GameplayControlCommandState.Rejected && command.Snapshot.Error?.Code == ErrorCode.TargetExpired
            && f.Backend.Values == before && f.Backend.ValueCommits == 1 && f.Backend.Restorations == 1
            && f.Backend.RestorationReadbacks == 1 && f.Backend.PreparationDisposals == 1,
            "The in-flight write did not preserve its exact restoration responsibility.");
    }

    private static void DeferredCloseFault()
    {
        foreach (var queued in new[] { false, true })
        {
            var log = new CleanupLog();
            using var f = new Fixture(values: true, log: log);
            using var lease = PauseAndAcquire(f);
            var before = f.Backend.Values;
            f.Backend.DisposeError = new IOException("backend retirement failed");
            f.Backend.OnValueCommit = f.Service.Dispose;
            if (queued)
            {
                var command = Need(lease.Submit(Speed(lease)));
                f.Service.PumpControlCommand();
                Check(command.Snapshot.State == GameplayControlCommandState.Rejected && command.Snapshot.Error?.Code == ErrorCode.TargetExpired,
                    "Deferred cleanup replaced the terminal command outcome.");
            }
            else Error(lease.Execute(Speed(lease)), ErrorCode.TargetExpired);
            f.Service.PumpControlCommand();
            f.Service.Dispose();
            Check(f.Backend.ValueCommits == 1 && f.Backend.Values == before && f.Backend.RestorationReadbacks == 1
                && f.Backend.PreparationDisposals == 1 && f.Backend.CaptureDisposals == f.Backend.Captures && f.Backend.Disposals == 1,
                "Reentrant shutdown lost restoration, skipped release, or replayed cleanup.");
            Check(log.Errors.Count == 1 && ReferenceEquals(log.Errors[0], f.Backend.DisposeError),
                "The deferred cleanup exception was lost or reported twice.");
        }
    }

    private static void CloseWithSubscriberFault()
    {
        var log = new CleanupLog();
        using var f = new Fixture(values: true, log: log);
        var events = new FaultingEvents();
        f.Context.TestEvents = events;
        using var lease = PauseAndAcquire(f);
        using var first = Need(f.Controls.Subscribe(_ => { }));
        using var second = Need(f.Controls.Subscribe(_ => { }));
        var command = Need(lease.Submit(Speed(lease)));
        f.Service.Dispose();
        f.Service.PumpControlCommand();
        Check(command.Snapshot.State == GameplayControlCommandState.Cancelled && f.Backend.ValueCommits == 0,
            "Shutdown left a queued command without a terminal outcome.");
        Check(events.Disposals == 2 && f.Backend.Disposals == 1 && f.Backend.CaptureDisposals == f.Backend.Captures,
            "A subscriber's cleanup fault skipped independent resources.");
        Check(log.Errors.Count == 1 && log.Errors[0].Message == "subscriber retirement failed",
            "The subscription cleanup failure was not attributed.");
    }

    private sealed class CleanupLog : ILogger
    {
        internal readonly List<Exception> Errors = new();
        public void Log(LogLevel level, string message, Exception? error = null)
        {
            if (level == LogLevel.Error && error is not null) Errors.Add(error);
            throw new IOException("diagnostic sink unavailable");
        }
    }

    private sealed class FaultingEvents : IEventBus
    {
        internal int Disposals;
        public IDisposable Subscribe<T>(Action<T> handler, int priority = 0) => new Subscription(this);
        public void Publish<T>(T message) { }

        private sealed class Subscription : IDisposable
        {
            private readonly FaultingEvents _bus;
            internal Subscription(FaultingEvents bus) => _bus = bus;
            public void Dispose()
            {
                if (++_bus.Disposals == 1) throw new IOException("subscriber retirement failed");
            }
        }
    }

    private static IGameplayControlLease PauseAndAcquire(Fixture f)
    {
        f.Backend.Paused = true;
        f.Backend.Playing = false;
        f.Lifecycle = f.Lifecycle! with { Phase = RunLifecyclePhase.Paused, Paused = true };
        return Need(f.Controls.Acquire(true));
    }

    private static GameplayControlRequest Speed(IGameplayControlLease lease) => new(GameplayControlAction.SetSpeed,
        lease.ControllerGeneration, lease.ObservedRunId, 1.5, GameplayControlValueUnit.PlaybackRateMultiplier);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Error<T>(Result<T> result, ErrorCode expected) => Check(!result.Success && result.Error?.Code == expected,
        $"Expected {expected}. Observed {result.Error?.Code}.");
    private static T Need<T>(Result<T> result) => result.Success ? result.Value! : throw new InvalidOperationException(result.Error?.Message);
}
