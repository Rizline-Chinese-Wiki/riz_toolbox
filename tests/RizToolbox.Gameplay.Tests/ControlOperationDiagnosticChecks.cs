using RizToolbox.Adapter.Gameplay;
using RizToolbox.Core.Diagnostics;
using RizToolbox.Sdk;

internal static class ControlOperationDiagnosticChecks
{
    internal static readonly (string Name, Action Run)[] All =
    {
        ("actual gameplay value controls record observation-only probe capture prepare commit confirm release phases", Success),
        ("actual commit and confirmation failures record separate restoration and baseline readback phases", Recovery),
        ("actual throwing preparation records only reached phases and preserves original failure", PreparationFailure)
    };

    private static CallbackPerformanceMonitor Collector() => new(retainedSamplesPerSeries: 16, maximumSeries: 64,
        callbackBudgetMilliseconds: .000000001, consecutiveLimit: 1);

    private static void Success()
    {
        var performance = Collector();
        var journal = new List<RunModificationInput>();
        using var fixture = new Fixture(values: true, record: journal.Add, performance: performance);
        var lease = Paused(fixture);
        Check(performance.GetSnapshot().Callbacks.Count == 0, "Acquisition or construction fabricated operation samples.");
        var speed = Need(lease.Execute(Request(lease, GameplayControlAction.SetSpeed, 2)));
        Check(speed.Modification.TransactionState == GameplayControlTransactionState.Confirmed && speed.ImmediatePostconditionObserved == true
            && fixture.Backend.Values.GameRate == 2 && fixture.Backend.Values.AudioRate == 2 && fixture.Backend.Values.SchedulerRate == 2,
            "Observation changed the actual speed commit or confirmation.");
        var seek = Need(lease.Execute(Request(lease, GameplayControlAction.Seek, 30)));
        Check(seek.Modification.TransactionState == GameplayControlTransactionState.Confirmed && seek.ImmediatePostconditionObserved == true
            && fixture.Backend.Values.GamePosition == 30 && fixture.Backend.Values.ChartPosition == ValueBackendState.ToChart(30)
            && fixture.Backend.Values.AudioPosition == 30 + fixture.Backend.Values.Offset && !fixture.Backend.Values.Holding,
            "Observation changed the actual seek participants or immediate readback.");
        Check(fixture.Backend.Preparations == 2 && fixture.Backend.ValueCommits == 2 && fixture.Backend.ValueConfirmations == 2
            && fixture.Backend.Restorations == 0 && fixture.Backend.RestorationReadbacks == 0 && fixture.Backend.PreparationDisposals == 2
            && journal.Count == 2 && journal.All(record => record.Outcome == RunModificationOutcome.Applied),
            "Observation retried, restored, leaked preparation, or changed shared audit.");
        Phases(performance, new[] { "SetSpeed", "Seek" }, "probe", "capture", "prepare", "commit", "confirm", "release");
    }

    private static void Recovery()
    {
        foreach (var scenario in new[] { "commit", "confirmation", "restore-throws" })
        {
            var performance = Collector();
            var journal = new List<RunModificationInput>();
            using var fixture = new Fixture(values: true, record: journal.Add, performance: performance);
            var lease = Paused(fixture);
            var baseline = fixture.Backend.Values;
            fixture.Backend.CommitFaultStage = scenario == "confirmation" ? 0 : 2;
            fixture.Backend.ConfirmationFails = scenario == "confirmation";
            fixture.Backend.RestoreThrows = scenario == "restore-throws";
            var result = lease.Execute(Request(lease, GameplayControlAction.SetSpeed, 2));
            var state = Need(fixture.SampledControlState());
            var modification = state.Modifications.Single();
            Check(!result.Success && result.Error is { } failure
                && failure.KnownCode == (scenario == "confirmation" ? ErrorCode.InvalidGameState : ErrorCode.InternalError)
                && failure.Message.Contains(scenario == "confirmation" ? "actual committed value/unit" : "controlled participant failure after stage 2", StringComparison.Ordinal),
                "Performance collection replaced the original failing control outcome.");
            Check(fixture.Backend.Preparations == 1 && fixture.Backend.ValueCommits == 1 && fixture.Backend.PreparationDisposals == 1
                && fixture.Backend.Restorations == 1 && fixture.Backend.ValueConfirmations == (scenario == "confirmation" ? 1 : 0)
                && fixture.Backend.RestorationReadbacks == (scenario == "restore-throws" ? 0 : 1) && journal.Count == 1,
                "Performance collection added a call, skipped actual readback, or repeated cleanup.");
            if (scenario == "restore-throws")
            {
                Check(modification.TransactionState == GameplayControlTransactionState.SideEffectsUnknown
                    && fixture.Backend.Values.AudioRate == 2 && fixture.Backend.Values.GameRate == 2 && fixture.Backend.Values.SchedulerRate == 1
                    && !state.Operations.Single(operation => operation.Action == GameplayControlAction.SetSpeed).Supported
                    && state.Operations.Single(operation => operation.Action == GameplayControlAction.Seek).Supported
                    && journal[0].Outcome == RunModificationOutcome.InvocationFailedSideEffectsUnknown,
                    "Observation changed unknown effects, source ownership, or per-action withdrawal.");
                Phases(performance, new[] { "SetSpeed" }, "probe", "capture", "prepare", "commit", "restore", "release");
            }
            else
            {
                Check(modification.TransactionState == GameplayControlTransactionState.Restored && fixture.Backend.Values == baseline
                    && state.Operations.Single(operation => operation.Action == GameplayControlAction.SetSpeed).Supported
                    && journal[0].Outcome == RunModificationOutcome.Restored,
                    "Observation signed restoration without the actual full-baseline readback or withdrew restored control.");
                Phases(performance, new[] { "SetSpeed" }, scenario == "confirmation"
                    ? new[] { "probe", "capture", "prepare", "commit", "confirm", "restore", "restore-readback", "release" }
                    : new[] { "probe", "capture", "prepare", "commit", "restore", "restore-readback", "release" });
            }
        }
    }

    private static void PreparationFailure()
    {
        var performance = Collector();
        var journal = new List<RunModificationInput>();
        using var fixture = new Fixture(values: true, record: journal.Add, performance: performance);
        var lease = Paused(fixture);
        var baseline = fixture.Backend.Values;
        fixture.Backend.OnPrepare = () => throw new IOException("actual preparation failure");
        var result = lease.Execute(Request(lease, GameplayControlAction.SetSpeed, 2));
        Check(!result.Success && result.Error is { KnownCode: ErrorCode.InternalError, Message: "actual preparation failure" }
            && fixture.Backend.Values == baseline && fixture.Backend.Preparations == 1 && fixture.Backend.ValueCommits == 0
            && fixture.Backend.ValueConfirmations == 0 && fixture.Backend.Restorations == 0 && fixture.Backend.RestorationReadbacks == 0
            && fixture.Backend.PreparationDisposals == 1 && journal.Count == 0,
            "Throwing preparation lost its exception, mutated state, fabricated audit, or skipped its owned cleanup.");
        Phases(performance, new[] { "SetSpeed" }, "probe", "capture", "prepare");
        Check(Need(fixture.SampledControlState()).Operations.Single(operation => operation.Action == GameplayControlAction.SetSpeed).Supported,
            "Observed preparation failure became unknown side effects or withdrew control authority.");
    }

    private static IGameplayControlLease Paused(Fixture fixture)
    {
        fixture.Backend.Playing = false; fixture.Backend.Paused = true;
        fixture.Lifecycle = fixture.Lifecycle! with { Phase = RunLifecyclePhase.Paused, Paused = true };
        return Need(fixture.Controls.Acquire(true));
    }

    private static GameplayControlRequest Request(IGameplayControlLease lease, GameplayControlAction action, double value)
        => new(action, lease.ControllerGeneration, lease.ObservedRunId, value, action == GameplayControlAction.SetSpeed
            ? GameplayControlValueUnit.PlaybackRateMultiplier : GameplayControlValueUnit.GameSeconds);

    private static void Phases(CallbackPerformanceMonitor performance, string[] actions, params string[] phases)
    {
        var snapshot = performance.GetSnapshot();
        var expected = actions.SelectMany(action => phases.Select(phase => action + "." + phase)).Order(StringComparer.Ordinal);
        Check(snapshot.Callbacks.Select(row => row.Operation).Order(StringComparer.Ordinal).SequenceEqual(expected),
            "Timing rows were omitted, conflated confirmation with restoration, or fabricated unreached phases.");
        Check(snapshot.Callbacks.All(row => row.Owner == "gameplay-control" && row.TotalSamples == 1 && row.WindowSamples == 1
            && double.IsFinite(row.MaximumMilliseconds) && row.MaximumMilliseconds >= 0 && row.TotalAllocatedBytes >= 0
            && row.OverBudget == 0 && row.ConsecutiveOverBudget == 0) && snapshot.DroppedSeriesSamples == 0,
            "Actual phase timing enforced callback budgets or changed bounded collector accounting.");
    }

    private static T Need<T>(Result<T> result)
    {
        Check(result.Success && result.Value is not null, result.Error?.Message ?? "Missing actual service result.");
        return result.Value!;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
