namespace RizToolbox.Sdk;

public static class DataCapabilityIds { public const string Read = "riz.data.read"; }
public sealed record DataSnapshotToken(string SessionId, long Revision);
public sealed record DataPageRequest(DataSnapshotToken Token, int Offset = 0, int Count = 128, bool AllowStale = false);
public sealed record DataFieldInfo(string Field, string Unit, string Source, bool IsRaw,
    bool IsNullable = false, string? NullReason = null);
/// <summary>An adapter-verified setting. JsonValue is an ordinary JSON scalar/object, never a native object.</summary>
public sealed record GameSettingSnapshot(string Id, string JsonValue, string Source, string? Unit = null);

/// <summary>A point-in-time revision; IsStale explicitly distinguishes older or invalidated cache from current data.</summary>
public sealed record DataSnapshotHeader(DataSnapshotToken Token, string SourceNamespace,
    DateTimeOffset CapturedAt, DateTimeOffset VerifiedAt, int ChartCount, int BestScoreCount,
    int? SettingCount, bool SupportsCurrentResult, bool IsStale, string? StaleReason);

public sealed class DataPage<T>
{
    public DataSnapshotHeader Header { get; }
    public int Offset { get; }
    public int TotalCount { get; }
    public int? NextOffset => Offset + Items.Count < TotalCount ? Offset + Items.Count : null;
    public IReadOnlyList<T> Items { get; }
    public DataPage(DataSnapshotHeader header, int offset, int totalCount, IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(header); ArgumentNullException.ThrowIfNull(items);
        var copied = items.ToArray();
        if (offset < 0 || totalCount < offset || copied.Length > totalCount - offset)
            throw new ArgumentOutOfRangeException(nameof(offset));
        Header = header; Offset = offset; TotalCount = totalCount; Items = Array.AsReadOnly(copied);
    }
}

/// <summary>All calls require the plugin main thread. Returned DTOs can be copied to background/export work.</summary>
public interface IDataSnapshotService
{
    Result<DataSnapshotHeader> Refresh(CancellationToken cancellationToken = default);
    Result<DataSnapshotHeader> GetHeader(bool allowStale = false);
    Result<DataPage<ChartInfo>> GetCharts(DataPageRequest request, CancellationToken cancellationToken = default);
    Result<DataPage<ScoreRecord>> GetBestScores(DataPageRequest request, CancellationToken cancellationToken = default);
    Result<DataPage<GameSettingSnapshot>> GetSettings(DataPageRequest request, CancellationToken cancellationToken = default);
    Result<RunFinished> GetCurrentResult(DataSnapshotToken token, bool allowStale = false);
    IReadOnlyList<DataFieldInfo> GetFieldDescriptions();
}
