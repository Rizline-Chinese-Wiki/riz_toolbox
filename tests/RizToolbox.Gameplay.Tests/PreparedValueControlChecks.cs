using RizToolbox.Adapter.Gameplay;
using RizToolbox.Sdk;

internal static class PreparedValueControlChecks
{
    internal static IEnumerable<(string Name, Action Run)> All => new (string, Action)[]
    {
        ("value executor prepares all participants before first write", PreparationOrder),
        ("value executor preserves preparation exception through reverse cleanup failures", PrepareFailure),
        ("value executor disposes duplicate instances once and rejects duplicate IDs", DuplicateParticipants),
        ("value executor checks all baselines before any write", ChangedPreflight),
        ("value executor reverses attempted setters including write then throw", PartialSetter),
        ("value executor continues remaining restoration after participant failure", RestoreFailure),
        ("value executor lost run authority prevents later reads and writes", LostAuthority),
        ("value executor requires actual finite numeric value and matching unit", NumericReadback),
        ("value executor commit restore and dispose are once-only", OnceOnly),
        ("value executor validates request before any factory", InvalidRequest),
        ("value executor preserves external state changed between commits", BetweenCommits),
        ("value executor checks origin after confirmation callbacks", ConfirmationOrigin),
        ("value executor callback disposal stops later participants", CallbackDisposal),
        ("value executor cleanup releases every resource and preserves success", DisposeResources),
        ("formal gameplay service confirms production value executor and shared audit", ServiceSuccess),
        ("formal gameplay service records independently restored executor failure", ServiceRestored),
        ("formal gameplay service withdraws unproven executor effects", ServiceUnknown)
    };
    private static GameplayControlRequest Request => new(GameplayControlAction.SetSpeed, 7, "executor-run", 2, GameplayControlValueUnit.PlaybackRateMultiplier);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static T Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T error) { return error; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private static GameplayPreparedValueControl Prepare(params Part[] parts)
        => GameplayPreparedValueControl.Prepare(Request, parts.Select(part => (Func<IGameplayValueParticipant>)(() => part)).ToArray(), () => { });
    private static void PreparationOrder()
    {
        var trace = new List<string>(); var a = new Part("audio", trace); var b = new Part("clock", trace, false);
        using var prepared = GameplayPreparedValueControl.Prepare(Request, new Func<IGameplayValueParticipant>[]
        { () => { trace.Add("capture-a"); return a; }, () => { trace.Add("capture-b"); return b; } }, () => { });
        Check(a.Commits == 0 && b.Commits == 0 && trace.SequenceEqual(new[] { "capture-a", "verify-audio", "capture-b", "verify-clock" }), "Prepare wrote or reordered captures.");
        prepared.Commit(); var confirmed = prepared.Confirm();
        Check(confirmed.Observed == true && confirmed.Value == 2 && a.Value == 2 && b.Value == 2, "Executor did not jointly confirm actual states.");
    }
    private static void PrepareFailure()
    {
        var trace = new List<string>(); var a = new Part("a", trace) { DisposeFault = true }; var b = new Part("b", trace) { DisposeFault = true };
        var original = new IOException("preparation source failure");
        var error = Throws<IOException>(() => GameplayPreparedValueControl.Prepare(Request, new Func<IGameplayValueParticipant>[] { () => a, () => b, () => throw original }, () => { }));
        Check(ReferenceEquals(error, original) && trace.TakeLast(2).SequenceEqual(new[] { "dispose-b", "dispose-a" })
            && a.Disposals == 1 && b.Disposals == 1 && a.Commits + b.Commits == 0, "Cleanup replaced original or leaked resources.");
    }
    private static void DuplicateParticipants()
    {
        var same = new Part("a");
        Throws<InvalidOperationException>(() => GameplayPreparedValueControl.Prepare(Request, new Func<IGameplayValueParticipant>[] { () => same, () => same }, () => { }));
        Check(same.Disposals == 1, "Duplicate instance disposed twice.");
        var a = new Part("same"); var b = new Part("same"); Throws<InvalidOperationException>(() => Prepare(a, b));
        Check(a.Disposals == 1 && b.Disposals == 1, "Duplicate ID leaked a capture.");
    }
    private static void ChangedPreflight()
    {
        var a = new Part("a"); var b = new Part("b"); using var prepared = Prepare(a, b); b.Value = 9;
        Throws<InvalidOperationException>(prepared.Commit); Check(a.Commits + b.Commits == 0, "First write preceded verification of all baselines.");
    }
    private static void PartialSetter()
    {
        var trace = new List<string>(); var a = new Part("a", trace); var b = new Part("b", trace) { CommitFault = true };
        using var prepared = Prepare(a, b); Throws<IOException>(prepared.Commit); prepared.Restore();
        Check(trace.Where(x => x.StartsWith("restore-", StringComparison.Ordinal)).SequenceEqual(new[] { "restore-b", "restore-a" })
            && a.Value == 1 && b.Value == 1 && prepared.ConfirmRestored().OriginalStateRestored, "Setter mutation was omitted or restoration order wrong.");
    }
    private static void RestoreFailure()
    {
        var a = new Part("a"); var b = new Part("b") { CommitFault = true, RestoreFault = true };
        using var prepared = Prepare(a, b); Throws<IOException>(prepared.Commit); prepared.Restore();
        Check(a.Value == 1 && b.Value == 2 && a.Restores == 1 && b.Restores == 1 && !prepared.ConfirmRestored().OriginalStateRestored,
            "Restore failure skipped another owner or fabricated readback.");
    }
    private static void LostAuthority()
    {
        var alive = true; var a = new Part("a"); var b = new Part("b"); a.AfterCommit = () => alive = false;
        using var prepared = GameplayPreparedValueControl.Prepare(Request, new Func<IGameplayValueParticipant>[] { () => a, () => b },
            () => { if (!alive) throw new InvalidOperationException("run authority withdrawn"); });
        Throws<InvalidOperationException>(prepared.Commit); var reads = a.Reads + b.Reads; prepared.Restore(); var restored = prepared.ConfirmRestored();
        Check(a.Commits == 1 && b.Commits == 0 && a.Restores + b.Restores == 0 && a.Reads + b.Reads == reads
            && !restored.OriginalStateRestored, "Lost authority permitted access during continuation/recovery.");
    }
    private static void NumericReadback()
    {
        var values = new GameplayControlConfirmation[]
        {
            new(true, "no numeric"), new(true, "unit without value", null, GameplayControlValueUnit.PlaybackRateMultiplier),
            new(true, "nan", double.NaN, GameplayControlValueUnit.PlaybackRateMultiplier),
            new(true, "inf", double.PositiveInfinity, GameplayControlValueUnit.PlaybackRateMultiplier),
            new(true, "wrong unit", 2, GameplayControlValueUnit.GameSeconds),
            new(true, "wrong value", 3, GameplayControlValueUnit.PlaybackRateMultiplier),
            new(false, "not observed", 2, GameplayControlValueUnit.PlaybackRateMultiplier)
        };
        foreach (var value in values)
        {
            var part = new Part("numeric") { Readback = value }; using var prepared = Prepare(part); prepared.Commit();
            Check(prepared.Confirm().Observed == false, "Invalid actual value/unit confirmed: " + value.Evidence);
        }
        var a = new Part("a"); var b = new Part("b", numeric: false); using var positive = Prepare(a, b); positive.Commit();
        Check(positive.Confirm().Observed == true, "A support participant without numeric value disabled joint confirmation.");
    }
    private static void OnceOnly()
    {
        var a = new Part("a"); using var prepared = Prepare(a); prepared.Commit(); Throws<InvalidOperationException>(prepared.Commit);
        prepared.Restore(); Throws<InvalidOperationException>(prepared.Restore); Throws<InvalidOperationException>(prepared.Commit); Throws<InvalidOperationException>(() => prepared.Confirm());
        Check(prepared.ConfirmRestored().OriginalStateRestored && a.Commits == 1 && a.Restores == 1, "Once-only change repeated an action.");
        var b = new Part("b"); using var beforeCommit = Prepare(b); beforeCommit.Restore(); Throws<InvalidOperationException>(beforeCommit.Commit);
        Check(b.Commits == 0 && beforeCommit.ConfirmRestored().OriginalStateRestored, "Restoration before commit revived a transaction.");
    }
    private static void InvalidRequest()
    {
        var captures = 0; var factory = new Func<IGameplayValueParticipant>[] { () => { captures++; return new Part("a"); } };
        foreach (var request in new[] { Request with { ControllerGeneration = 0 }, Request with { ObservedRunId = null }, Request with { Value = double.NaN },
            Request with { Value = double.PositiveInfinity }, Request with { Value = 0 }, Request with { Unit = GameplayControlValueUnit.GameSeconds }, Request with { Action = GameplayControlAction.Pause } })
            Throws<ArgumentException>(() => GameplayPreparedValueControl.Prepare(request, factory, () => { }));
        Throws<ArgumentException>(() => GameplayPreparedValueControl.Prepare(Request, factory, () => { }, double.NaN));
        Check(captures == 0, "Invalid request invoked a factory.");
    }
    private static void BetweenCommits()
    {
        var a = new Part("a"); var b = new Part("b"); a.AfterCommit = () => b.Value = 9;
        using var prepared = Prepare(a, b); Throws<InvalidOperationException>(prepared.Commit); prepared.Restore();
        Check(a.Value == 1 && b.Value == 9 && b.Commits == 0 && !prepared.ConfirmRestored().OriginalStateRestored,
            "External state changed between commits was overwritten or called restored.");
    }
    private static void ConfirmationOrigin()
    {
        var alive = true; var a = new Part("a"); a.OnConfirm = () => alive = false;
        using var prepared = GameplayPreparedValueControl.Prepare(Request, new Func<IGameplayValueParticipant>[] { () => a },
            () => { if (!alive) throw new InvalidOperationException("late origin change"); });
        prepared.Commit(); Throws<InvalidOperationException>(() => prepared.Confirm());
    }
    private static void CallbackDisposal()
    {
        GameplayPreparedValueControl? prepared = null; var a = new Part("a"); var b = new Part("b");
        a.AfterCommit = () => prepared!.Dispose(); prepared = Prepare(a, b);
        Throws<ObjectDisposedException>(prepared.Commit); Check(a.Commits == 1 && b.Commits == 0 && a.Disposals == 1 && b.Disposals == 1, "Disposal allowed a later action."); prepared.Dispose();
    }
    private static void DisposeResources()
    {
        var trace = new List<string>(); var a = new Part("a", trace) { DisposeFault = true }; var b = new Part("b", trace) { DisposeFault = true };
        var prepared = Prepare(a, b); prepared.Commit(); Check(prepared.Confirm().Observed == true, "Joint commit did not confirm.");
        var error = Throws<AggregateException>(prepared.Dispose); prepared.Dispose();
        Check(error.InnerExceptions.Count == 2 && a.Disposals == 1 && b.Disposals == 1 && a.Value == 2 && b.Value == 2
            && trace.TakeLast(2).SequenceEqual(new[] { "dispose-b", "dispose-a" }), "Cleanup stopped early, retried or undid values.");
        Throws<ObjectDisposedException>(prepared.Commit); Throws<ObjectDisposedException>(() => prepared.Confirm());
    }
    private static void ServiceSuccess() => Service(false, false);
    private static void ServiceRestored() => Service(true, false);
    private static void ServiceUnknown() => Service(true, true);
    private static void Service(bool commitFault, bool restoreFault)
    {
        var journal = new List<RunModificationInput>(); using var fixture = new Fixture(record: journal.Add, values: true);
        fixture.Backend.Playing = false; fixture.Backend.Paused = true;
        fixture.Lifecycle = fixture.Lifecycle! with { Phase = RunLifecyclePhase.Paused, Paused = true };
        var audio = new Part("actual-audio"); var clock = new Part("actual-clock", numeric: false) { CommitFault = commitFault, RestoreFault = restoreFault };
        fixture.Backend.PreparedOverride = request => GameplayPreparedValueControl.Prepare(request,
            new Func<IGameplayValueParticipant>[] { () => audio, () => clock }, () =>
            {
                if (fixture.Backend.Identity != 1 || !fixture.Backend.Paused || fixture.Backend.Playing)
                    throw new InvalidOperationException("captured run authority changed");
            });
        var acquired = fixture.Controls.Acquire(true); Check(acquired.Success && acquired.Value is not null, "Control ownership failed.");
        var lease = acquired.Value!;
        var result = lease.Execute(new GameplayControlRequest(GameplayControlAction.SetSpeed, lease.ControllerGeneration, lease.ObservedRunId, 2, GameplayControlValueUnit.PlaybackRateMultiplier));
        var state = fixture.SampledControlState(); Check(state.Success && journal.Count == 1, "Service omitted the executor's shared audit.");
        var modification = state.Value!.Modifications.Single();
        if (!commitFault)
        {
            Check(result.Success && modification.TransactionState == GameplayControlTransactionState.Confirmed && modification.ConfirmedValue == 2
                && journal[0].Outcome == RunModificationOutcome.Applied && audio.Value == 2 && clock.Value == 2, "Service did not expose actual joint confirmation.");
        }
        else if (!restoreFault)
        {
            Check(!result.Success && modification.TransactionState == GameplayControlTransactionState.Restored && journal[0].Outcome == RunModificationOutcome.Restored
                && audio.Value == 1 && clock.Value == 1 && state.Value.Operations.Single(x => x.Action == GameplayControlAction.SetSpeed).Supported,
                "Service omitted separate original-baseline readback or withdrew a restored control.");
        }
        else
        {
            Check(!result.Success && modification.TransactionState == GameplayControlTransactionState.SideEffectsUnknown
                && !state.Value.Operations.Single(x => x.Action == GameplayControlAction.SetSpeed).Supported
                && state.Value.Operations.Single(x => x.Action == GameplayControlAction.Seek).Supported && audio.Value == 1 && clock.Value == 2,
                "Service confirmed unknown effects or withdrew unrelated actions.");
        }
        Check(audio.Disposals == 1 && clock.Disposals == 1 && journal[0].ObservedRunId == "controlled-run"
            && !journal[0].LocalResultBehaviorVerified && !journal[0].SubmissionBehaviorVerified, "Service leaked captures or fabricated result/submission verification.");
    }

    private sealed class Part : IGameplayValueParticipant
    {
        private readonly List<string> _trace;
        private readonly bool _numeric;
        internal double Value = 1;
        internal int Commits, Restores, Reads, Disposals;
        internal bool CommitFault, RestoreFault, DisposeFault;
        internal Action? AfterCommit, OnConfirm;
        internal GameplayControlConfirmation? Readback;
        internal Part(string id, List<string>? trace = null, bool numeric = true) => (Id, _trace, _numeric) = (id, trace ?? new(), numeric);
        public string Id { get; }
        public void VerifyPrepared() { _trace.Add("verify-" + Id); Reads++; if (Value != 1) throw new InvalidOperationException("participant baseline changed"); }
        public void Commit()
        {
            _trace.Add("commit-" + Id); Commits++; Value = 2; AfterCommit?.Invoke();
            if (CommitFault) throw new IOException("participant changed before setter failed");
        }
        public GameplayControlConfirmation Confirm()
        {
            Reads++; OnConfirm?.Invoke();
            return Readback ?? new(Value == 2, "actual participant value read", _numeric ? Value : null,
                _numeric ? GameplayControlValueUnit.PlaybackRateMultiplier : GameplayControlValueUnit.None);
        }
        public void Restore()
        {
            _trace.Add("restore-" + Id); Restores++;
            if (RestoreFault) throw new IOException("controlled restore failure");
            if (Value != 1 && Value != 2) throw new InvalidOperationException("successor participant state");
            Value = 1;
        }
        public GameplayControlRestoration ConfirmRestored() { Reads++; return new(Value == 1, "actual baseline read"); }
        public void Dispose() { _trace.Add("dispose-" + Id); Disposals++; if (DisposeFault) throw new IOException("controlled cleanup failure"); }
    }
}
