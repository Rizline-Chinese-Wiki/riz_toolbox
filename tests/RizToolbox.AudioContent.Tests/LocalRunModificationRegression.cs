using RizToolbox.Adapter;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Core;
using RizToolbox.Sdk;
using static Assert;

internal static partial class NativeLocalRunTerminationBoundaryStateRegression
{
    internal static readonly (string Name, Action Test)[] RunModificationCases = {
        ("LOCAL modification needs committed content and exact observed controller and information", ModificationNeedsObservedGraph),
        ("LOCAL modification refuses an earlier run or playback already ended before audit", ModificationRefusesStaleRun),
        ("LOCAL modification survives cached result and releases to its original run after exact return", ModificationOwnedReturn),
        ("LOCAL retry records a new run with the same retained catalog content", ModificationRetriesWithNewRun)
    };

    private static void InitializeForModification(PendingFixture fixture)
    {
        fixture.AttachController();
        fixture.Launch.NoteActivationAttempt();
        fixture.Bridge.AttachInitializingGraph(fixture.Launch, fixture.Information);
        fixture.Backend.PendingControllerInitialized = true;
    }

    private static void ModificationNeedsObservedGraph()
    {
        using var fixture = new PendingFixture();
        using var audit = new ModificationFixture(fixture.Main);
        var local = audit.Track(fixture);
        var controller = fixture.Controller;
        var information = fixture.Information.Target.ToInt64();
        local.Observe();
        InitializeForModification(fixture);
        audit.State.Scene("Level");
        audit.State.Controller(information, false, false, "chart", controller);
        local.Observe();
        Check(Pump(fixture, () => { }));
        local.Observe();
        Equal(0, audit.Read().Records.Count);

        // Either different rooted object must defeat an identical chart identifier.
        audit.State.Controller(information, true, false, "chart", controller + 1000);
        local.Observe();
        audit.State.Controller(information + 1000, true, false, "chart", controller);
        local.Observe();
        Equal(0, audit.Read().Records.Count);

        using var duringObservation = Need(audit.State.Subscribe(change =>
        {
            if (change.Kind == GameLifecycleChangeKind.RunStarted) local.Observe();
        }));
        var started = audit.State.Controller(information, true, false, "chart", controller)!;
        Equal(0, audit.Read().Records.Count);
        local.Observe();
        audit.State.ControllerUnavailable();
        local.Observe();
        audit.State.Controller(information, true, false, "chart", controller);
        local.Observe();

        var snapshot = audit.Read(started.RunId);
        var entry = snapshot.Records.Single();
        Equal(started.RunId, entry.RunId);
        Equal(audit.State.SessionId, entry.SessionId);
        Equal("native-termination.pending", entry.Owner);
        Equal("song", entry.TargetId);
        Equal(RunModificationCategory.LocalContent, entry.Category);
        Equal(RunModificationOutcome.EffectActivated, entry.Outcome);
        Equal(RunModificationAssociation.ExplicitObservedRun, entry.Association);
        Equal(entry.EffectId, snapshot.ActiveEffects.Single().EffectId);
        Check(entry.Detail.Contains("song revision 1, chart chart", StringComparison.Ordinal));
        Check(!entry.LocalResultBehaviorVerified && !entry.SubmissionBehaviorVerified);
        fixture.Retire();
        local.ReleaseAfterReturn();
    }

    private static void ModificationRefusesStaleRun()
    {
        using var fixture = new PendingFixture();
        using var audit = new ModificationFixture(fixture.Main);
        var information = fixture.Information.Target.ToInt64();
        audit.State.Scene("Level");
        var earlier = audit.State.Controller(information, true, false, "chart", fixture.Controller)!;
        var local = audit.Track(fixture);
        InitializeForModification(fixture);
        Check(Pump(fixture, () => { }));
        audit.State.ObservePause(true); // A later revision still belongs to the same earlier run.
        local.Observe();
        Equal(0, audit.Read(earlier.RunId).Records.Count);
        audit.State.Controller(information + 1000, true, false, "chart", fixture.Controller + 1000);
        var observed = audit.State.Controller(information, true, false, "chart", fixture.Controller)!;
        Check(observed.RunId != earlier.RunId);
        audit.State.AbortFromVerifiedCallback("Fixture accepted an exact terminal boundary.");
        local.Observe();
        fixture.Retire();
        local.ReleaseAfterReturn();
        var snapshot = audit.Read();
        Equal(0, snapshot.Records.Count);
        Equal(0, snapshot.ActiveEffects.Count);
    }

    private static void ModificationOwnedReturn()
    {
        using var fixture = new PendingFixture();
        using var audit = new ModificationFixture(fixture.Main);
        var local = audit.Track(fixture);
        InitializeForModification(fixture);
        Check(Pump(fixture, () => { }));
        audit.State.Scene("Level");
        var started = audit.State.Controller(fixture.Information.Target.ToInt64(), true, false, "chart", fixture.Controller)!;
        local.Observe();
        var activation = audit.Read(started.RunId).Records.Single();

        fixture.Handler.Handle(LocalBoundaryKind.ControllerLevelOver, fixture.Session, fixture.Controller);
        fixture.Backend.FrameValue++;
        fixture.Handler.Tick();
        Equal(LocalRunSessionPhase.ResultReady, fixture.Host.Sessions.Read(fixture.Session).Phase);
        local.ReleaseAfterReturn();
        Equal(activation.EffectId, audit.Read().ActiveEffects.Single().EffectId);
        fixture.Host.Sessions.MarkProviderWithdrawn(fixture.Session);
        fixture.Host.Sessions.BeginReturn(fixture.Session);
        audit.State.ControllerUnavailable();
        audit.State.Scene("MainMenu");
        local.ReleaseAfterReturn();
        Equal(1, audit.Read().ActiveEffects.Count);

        audit.State.Scene("Level");
        var official = audit.State.Controller(fixture.Controller + 2001, true, false, "chart", fixture.Controller + 2000)!;
        Equal(0, audit.Read(official.RunId).Records.Count);
        fixture.Retire();
        local.ReleaseAfterReturn();
        local.ReleaseAfterReturn();
        var old = audit.Read(started.RunId);
        Equal(2, old.Records.Count);
        var release = old.Records.Last();
        Equal(RunModificationOutcome.EffectReleased, release.Outcome);
        Equal(RunModificationAssociation.ExplicitObservedRun, release.Association);
        Equal(activation.EffectId, release.EffectId);
        Check(!release.LocalResultBehaviorVerified && !release.SubmissionBehaviorVerified);
        Equal(0, old.ActiveEffects.Count);
        Equal(0, audit.Read(official.RunId).Records.Count);
    }

    private static void ModificationRetriesWithNewRun()
    {
        using var first = new PendingFixture();
        using var audit = new ModificationFixture(first.Main);
        var initial = audit.Track(first);
        InitializeForModification(first);
        Check(Pump(first, () => { }));
        audit.State.Scene("Level");
        var firstRun = audit.State.Controller(first.Information.Target.ToInt64(), true, false, "chart", first.Controller)!;
        initial.Observe();
        first.Retire();
        initial.ReleaseAfterReturn();

        using var retry = new PendingFixture();
        var next = audit.Track(retry);
        InitializeForModification(retry);
        Check(Pump(retry, () => { }));
        Equal(first.Launch.Identity.Song, retry.Launch.Identity.Song);
        Equal(first.Launch.Identity.SongRevision, retry.Launch.Identity.SongRevision);
        Equal(first.Launch.Identity.ChartId, retry.Launch.Identity.ChartId);
        next.Observe();
        Equal(0, audit.Read().ActiveEffects.Count);
        var secondRun = audit.State.Controller(retry.Information.Target.ToInt64(), true, false, "chart", retry.Controller)!;
        next.Observe();
        var second = audit.Read(secondRun.RunId).Records.Single();
        Equal(RunModificationOutcome.EffectActivated, second.Outcome);
        Check(firstRun.RunId != second.RunId && audit.Read(firstRun.RunId).Records.First().EffectId != second.EffectId);
        retry.Retire();
        next.ReleaseAfterReturn();
    }

    private sealed class ModificationFixture : IDisposable
    {
        internal GameEventState State { get; } = new();
        private readonly RunModificationJournal _journal;
        private readonly RunEffectRecorder _recorder;
        private readonly Context _context;
        private readonly IRunModificationService _view;

        internal ModificationFixture(IMainThreadDispatcher main)
        {
            State.SetAvailability(scenes: true, runs: true);
            State.Scene("MainMenu");
            _journal = new(main, new Logger(), State);
            _recorder = new(_journal.Record, _journal.BeginEffect, _journal.BeginObservedRunEffect);
            _context = new("consumer", main);
            _view = _journal.ForPlugin(_context);
        }

        internal LocalRunModification Track(PendingFixture fixture) => new(State, _recorder, fixture.Launch);
        internal RunModificationJournalSnapshot Read(string? runId = null)
            => Need(_view.Read(new(RunId: runId, Source: LocalRunModification.Source)));

        public void Dispose()
        {
            _context.Dispose();
            _journal.Dispose();
        }
    }
}
