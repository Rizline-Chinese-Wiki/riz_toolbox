using RizToolbox.Adapter.Gameplay;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

/// <summary>Correlates one retained LOCAL launch with playback observed by the shared lifecycle.</summary>
internal sealed class LocalRunModification
{
    internal const string Source = AudioContentCapabilityIds.LocalCatalog;

    private readonly GameEventState _lifecycle;
    private readonly RunEffectRecorder _recorder;
    private readonly LocalRunLaunchCommitState _launch;
    private readonly string? _priorRunId;
    private IDisposable? _effect;

    internal LocalRunModification(GameEventState lifecycle, RunEffectRecorder recorder, LocalRunLaunchCommitState launch)
        => (_lifecycle, _recorder, _launch, _priorRunId) = (lifecycle, recorder, launch, lifecycle.RunId);

    internal void Observe()
    {
        if (_effect is not null || !_launch.IsCommittedReady) return;
        var observed = _lifecycle.GameplayObservationBoundary();
        if (!observed.CurrentRun || observed.RunId == _priorRunId) return;

        var graph = _launch.Read();
        if (graph.Phase != LocalRunLaunchCommitPhase.CommittedReady || graph.EarlyTerminal
            || graph.Controller == 0 || graph.Information == 0
            || graph.Controller != observed.Controller || graph.Information != observed.Information) return;

        var session = _launch.Registry.Read(_launch.Session);
        if (session.Phase != LocalRunSessionPhase.Committed || session.ReturnRequested || session.ProtectionRetired
            || session.ResultCallClaimed || session.CleanupAttempted || session.Identity != graph.Identity
            || session.ControllerIdentity != graph.Controller || session.MetadataIdentity != _launch.MetadataIdentity) return;

        var identity = session.Identity;
        _effect = _recorder.BeginObservedRun(new(identity.Song.Namespace, Source, "apply-local-chart", identity.Song.Id,
            RunModificationCategory.LocalContent, RunModificationOutcome.Applied,
            FormattableString.Invariant($"Catalog song {identity.Song.Id} revision {identity.SongRevision}, chart {identity.ChartId}. LOCAL playback uses this content and the host-owned result path."),
            observed.RunId, observed.SessionId, observed.Revision));
    }

    // The coordinator calls this after all of its native return owners retire.
    internal void ReleaseAfterReturn()
    {
        if (_effect is not { } effect) return;
        var session = _launch.Registry.Read(_launch.Session);
        if (session.Identity != _launch.Identity || session.Phase != LocalRunSessionPhase.Closed || !session.ReturnRequested
            || !session.ProtectionRetired || !session.CleanupAttempted || session.OutstandingOwnershipRecords != 0
            || string.IsNullOrWhiteSpace(session.ConsumerEndEvidence)) return;
        _effect = null;
        effect.Dispose();
    }
}
