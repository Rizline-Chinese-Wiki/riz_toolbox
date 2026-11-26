namespace RizToolbox.Sdk;

/// <summary>A chart within a local song; Id is local to the song, Content references validated chart data.</summary>
public sealed record LocalSongChartRequest(string Id, string Name, LocalContentId Content)
{
    public LocalChartPlaybackMetadata? Playback { get; init; }
}
/// <summary>Provider-authored chart metadata, like the game's workshop chart
/// metadata. MaxHit/MaxScore are declared values, not a framework scoring formula.
/// DifficultyIndex 0/1/2 selects the pinned native JudgeDataList entry. This DTO
/// supplies content only; it cannot grant native startup or play permission.</summary>
public sealed record LocalChartPlaybackMetadata(int DifficultyIndex, int MaxHit, int MaxScore,
    string DifficultyText, string Designer, ResourceColor ThemeColor,
    float DifficultyRating = 0, string Illustrator = "");
/// <summary>One complete CPU catalog item. Id is always placed in the caller's plugin namespace.
/// Audio, cover and 1..16 charts must already be validated and published by the content registry.</summary>
public sealed record LocalSongRequest(string Id, string Title, string Artist, LocalContentId Audio,
    LocalContentId Cover, IReadOnlyList<LocalSongChartRequest> Charts);
public sealed record LocalSongChartSnapshot(string Id, string Name, LocalContentSnapshot Content)
{
    public LocalChartPlaybackMetadata? Playback { get; init; }
    /// <summary>Registration verified authored playback metadata and notes on lines with usable point segments for this revision.
    /// RequestPlay checks the retained selection and the current game state when called.</summary>
    public bool HasPlaybackContent { get; init; }
}
/// <summary>Song registration alone grants no native playback admission. Use the selected chart's
/// HasPlaybackContent to present a play request and its RequestPlay receipt for the actual outcome.</summary>
public sealed record LocalSongSnapshot(LocalContentId Song, string Title, string Artist, long Revision,
    LocalContentSnapshot Audio, LocalContentSnapshot Cover, IReadOnlyList<LocalSongChartSnapshot> Charts,
    bool AvailableForGamePlayback);
/// <summary>One immutable catalog revision; metadata listing does not imply native game playback.</summary>
public sealed record LocalCatalogSnapshot(long Revision, IReadOnlyList<LocalSongSnapshot> Songs);
public interface ILocalSongRegistration : IDisposable
{
    LocalSongSnapshot Snapshot { get; }
    bool IsPublished { get; }
}
/// <summary>Main-thread CPU song registry, independently queried through riz.content.local.catalog.
/// Withdrawal removes the item from future reads; existing selected/prepared consumers retain its exact resources.
/// Metadata and resource references are bounded immutable copies; resources share the existing content budget.</summary>
public interface ILocalCatalogProviderService
{
    Result<ILocalSongRegistration> RegisterSong(LocalSongRequest request, CancellationToken cancellationToken = default);
    Result<LocalCatalogSnapshot> Read();
}
public sealed record LocalSongSelectionSnapshot(LocalSongSnapshot Song, LocalSongChartSnapshot Chart, long Generation);

/// <summary>The captured result for the exact LOCAL launch. LocalSessionId and RunGeneration
/// identify the host's LOCAL transaction. Result preserves the copied counters and any
/// separately established lifecycle fields.</summary>
public sealed record LocalRunResultSnapshot(LocalContentId Song, long SongRevision, string ChartId,
    string LocalSessionId, long RunGeneration, RunFinished Result);

/// <summary>A main-thread observation owned by the requesting scope. A captured copy
/// remains readable after the native run retires, until the handle or scope is released.</summary>
public interface ILocalRunResultHandle : IDisposable
{
    /// <summary>NotReady while capture can still occur, TargetExpired when the launch ends without
    /// a result, ObservationUnavailable when result observation fails, and ScopeExpired after release.</summary>
    Result<LocalRunResultSnapshot> Read();
}

public interface ILocalCatalogSelection : IDisposable
{
    LocalSongSelectionSnapshot Snapshot { get; }
    bool IsCurrent { get; }
    /// <summary>Requests native playback only through an adapted host boundary.
    /// ContentInvalid includes the offending chart path when a note has no usable line segment.
    /// Missing production admission returns CapabilityUnavailable; an independent
    /// CPU-only catalog returns OperationUnsupported. No stock content is substituted.</summary>
    Result RequestPlay(CancellationToken cancellationToken = default);
    /// <summary>Requests the same admitted LOCAL play with an observation of this launch's exact result.
    /// A refused request returns its failure without a handle. The handle observes the first launch only,
    /// and disposal ends observation without cancelling gameplay. Release unused handles to free capacity.</summary>
    Result<ILocalRunResultHandle> RequestPlayWithResult(CancellationToken cancellationToken = default)
        => Result<ILocalRunResultHandle>.Fail(new(ErrorCode.OperationUnsupported,
            "This LOCAL selection does not support request result observation.", AudioContentCapabilityIds.LocalCatalogSelection));
}
/// <summary>Main-thread, per-enable-scope selection, queried through riz.content.local.selection.
/// A successful selection replaces that view's previous generation. Failed/cancelled selection preserves it.
/// Provider/content withdrawal or reconstruction invalidates new requests; old prepared host consumers retain resources.</summary>
public interface ILocalCatalogSelectionService
{
    Result<ILocalCatalogSelection> Select(LocalContentId song, string chartId, CancellationToken cancellationToken = default);
    /// <summary>Select the exact published snapshot obtained from the catalog or registration.
    /// A withdrawn or replaced row returns TargetExpired and preserves the current selection.</summary>
    Result<ILocalCatalogSelection> SelectRevision(LocalSongSnapshot song, string chartId, CancellationToken cancellationToken = default);
    Result<LocalSongSelectionSnapshot?> ReadCurrent();
}
