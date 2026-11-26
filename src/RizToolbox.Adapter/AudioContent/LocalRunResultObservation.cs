using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

/// <summary>Main-thread observation only. The prepared request and registry may retain
/// this state after withdrawal, so it holds no plugin context, callback, or native owner.</summary>
internal sealed class LocalRunResultObservation
{
    private readonly LocalContentId _song;
    private readonly long _revision;
    private readonly string _chartId;
    private LocalRunSessionIdentity? _session;
    private RunFinished? _result;
    private ErrorCode? _failure;
    private string? _failureMessage;
    private bool _released;

    internal LocalRunResultObservation(LocalSongSelectionSnapshot selection)
        => (_song, _revision, _chartId) = (selection.Song.Song, selection.Song.Revision, selection.Chart.Id);

    internal bool TryBind(LocalRunSessionIdentity session)
    {
        // Retained inputs can be used in a native Retry. The first session's
        // observation never transfers to the next launch, even on the same chart.
        if (_released || _session is not null || _failure is not null) return false;
        if (session.Song != _song || session.SongRevision != _revision || session.ChartId != _chartId)
        {
            End(ErrorCode.ObservationUnavailable, "The prepared LOCAL launch did not match this result request.");
            return false;
        }
        _session = session;
        return true;
    }

    internal void Complete(RunFinished result)
    {
        if (!_released && _result is null && _failure is null) _result = result;
    }

    internal void End(ErrorCode code, string message)
    {
        if (_released || _result is not null || _failure is not null) return;
        _failure = code;
        _failureMessage = message;
    }

    internal Result<LocalRunResultSnapshot> Read()
    {
        if (_released) return Result<LocalRunResultSnapshot>.Fail(new(ErrorCode.ScopeExpired,
            "The LOCAL result observation has been released.", AudioContentCapabilityIds.LocalCatalogSelection, _chartId));
        if (_result is not null && _session is { } session)
            return Result<LocalRunResultSnapshot>.Ok(new(session.Song, session.SongRevision, session.ChartId,
                session.SessionId, session.RunGeneration, _result));
        if (_failure is { } failure)
            return Result<LocalRunResultSnapshot>.Fail(new(failure, _failureMessage!, AudioContentCapabilityIds.LocalCatalogSelection, _chartId));
        return Result<LocalRunResultSnapshot>.Fail(new(ErrorCode.NotReady,
            "Awaiting the exact LOCAL result capture.", AudioContentCapabilityIds.LocalCatalogSelection, _chartId, Retryable: true));
    }

    internal void Release()
    {
        _released = true;
        _session = null;
        _result = null;
        _failure = null;
        _failureMessage = null;
    }
}
