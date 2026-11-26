using System.Text.Json;
using RizToolbox.Sdk;

namespace RizToolbox.Diagnostics;

public sealed class StockJudgementProbe : IRizPlugin
{
    private const string Id = "probe.r24.stock-judgement";
    private const string ChartId = "chart.PastelLines.RekuMochizuki.0.EZ";
    private const string Prefix = "StockJudgementProbe POST_RESUME ";
    private Activation? _active;

    public PluginManifest Manifest => new(Id, new(1, 0, 0), new(1, 5, 0), new(2, 0, 0),
        RequiredCapabilities: new[] { GameLifecycleCapabilityIds.Lifecycle, JudgementCapabilityIds.Observe });

    public void Enable(IPluginContext context)
    {
        context.MainThread.VerifyAccess();
        _active = new Activation(context.Log);
        var lifecycle = context.GetService<IGameLifecycleService>(GameLifecycleCapabilityIds.Lifecycle).Service;
        var judgements = context.GetService<IGameplayJudgementService>(JudgementCapabilityIds.Observe).Service;
        if (lifecycle?.Subscribe(_active.ObserveLifecycle).Success is not true ||
            judgements?.Subscribe(_active.ObserveJudgement).Success is not true)
            throw new InvalidOperationException("The stock judgement probe could not subscribe to both observers.");
    }

    public void Disable()
    {
        _active?.Stop();
        _active = null;
    }

    private sealed record RunIdentity(string SessionId, string RunId, long SceneGeneration);

    private sealed class Activation
    {
        private readonly ILogger _log;
        private RunIdentity? _run;
        private long _resumedRevision;
        private bool _recorded;
        private bool _stopped;

        internal Activation(ILogger log) => _log = log;

        internal void ObserveLifecycle(GameLifecycleChange change)
        {
            if (_stopped) return;
            var snapshot = change.Snapshot;
            if (change.Kind == GameLifecycleChangeKind.RunEnded || snapshot.Phase == RunLifecyclePhase.Finished ||
                snapshot.Scene != "Level" || snapshot.ChartId != ChartId || snapshot.SceneGeneration <= 0 ||
                !BoundedIdentity(snapshot.SessionId) || snapshot.RunId is not { } runId || !BoundedIdentity(runId))
            {
                Reset();
                return;
            }

            var run = new RunIdentity(snapshot.SessionId, runId, snapshot.SceneGeneration);
            if (_run != run)
            {
                Reset();
                _run = run;
            }

            if (!snapshot.ScenesAvailable || !snapshot.RunsAvailable || !snapshot.PauseAvailable ||
                snapshot.Phase != RunLifecyclePhase.Playing || snapshot.Paused != false)
            {
                _resumedRevision = 0;
                return;
            }

            if (!_recorded && change.Kind == GameLifecycleChangeKind.Resumed &&
                change.PreviousPhase == RunLifecyclePhase.Paused && change.RelatedRunId == runId && snapshot.Revision > 0)
                _resumedRevision = snapshot.Revision;
        }

        internal void ObserveJudgement(GameplayJudgementObservation sample)
        {
            if (_stopped || _recorded || _resumedRevision <= 0 || _run is not { } run ||
                !sample.OriginalReturned || sample.Invocation <= 0 || sample.SceneGeneration != run.SceneGeneration ||
                sample.LifecycleRevisionAtEntry < _resumedRevision || !BoundedIdentity(sample.SourceId) ||
                sample.RuntimeNote is not { Sequence: > 0 } note ||
                note.SessionId != run.SessionId || note.ObservedRunId != run.RunId) return;

            _recorded = true;
            _log.Log(LogLevel.Info, Prefix + JsonSerializer.Serialize(new
            {
                sessionId = run.SessionId,
                runId = run.RunId,
                chartId = ChartId,
                sceneGeneration = run.SceneGeneration,
                resumedRevision = _resumedRevision,
                lifecycleRevisionAtEntry = sample.LifecycleRevisionAtEntry,
                invocation = sample.Invocation,
                runtimeNoteSequence = note.Sequence,
                sourceId = sample.SourceId,
                originalReturned = sample.OriginalReturned
            }));
        }

        internal void Stop()
        {
            _stopped = true;
            Reset();
        }

        private void Reset()
        {
            _run = null;
            _resumedRevision = 0;
            _recorded = false;
        }

        private static bool BoundedIdentity(string? value) => value is { Length: > 0 and <= 64 } &&
            !string.IsNullOrWhiteSpace(value);
    }
}
