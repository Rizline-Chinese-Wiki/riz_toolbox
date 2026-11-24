using System.Text;
using System.Text.Json;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter;

/// <summary>Adapter-owned ordinary DTO input captured synchronously on Unity's main thread.</summary>
internal sealed record DataSnapshotInput(IReadOnlyList<ChartInfo> Charts, IReadOnlyList<ScoreRecord> BestScores,
    bool SupportsCurrentResult = false, RunFinished? CurrentResult = null,
    IReadOnlyList<GameSettingSnapshot>? Settings = null, string SourceNamespace = "rizline.official");
internal sealed record DataSnapshotLimits(int MaximumEntries = 20000, long MaximumBytes = 8 * 1024 * 1024,
    int MaximumPageSize = 256, int RetainedRevisions = 2);

/// <summary>Immutable, bounded revision cache. Refresh never sends native game reads to a worker thread.</summary>
internal sealed class DataSnapshotService : IDataSnapshotService, IDisposable
{
    private readonly IMainThreadDispatcher _main;
    private readonly Func<CancellationToken, Result<DataSnapshotInput>> _capture;
    // Reads the observer's already-copied result. Page reads never resample the game.
    private readonly Func<(bool Available, RunFinished? Result)>? _observeResult;
    private readonly DataSnapshotLimits _limits;
    private readonly string _session = Guid.NewGuid().ToString("N");
    private readonly List<Snapshot> _snapshots = new();
    private long _revision, _invalidation;
    private string? _staleReason = "Game data has not been captured.";
    private bool _disposed, _refreshing;
    private sealed record Snapshot(DataSnapshotToken Token, DataSnapshotInput Data, DateTimeOffset CapturedAt,
        DateTimeOffset VerifiedAt);
    internal DataSnapshotService(IMainThreadDispatcher main, Func<CancellationToken, Result<DataSnapshotInput>> capture,
        DataSnapshotLimits? limits = null, Func<(bool Available, RunFinished? Result)>? observeResult = null)
    {
        _main = main ?? throw new ArgumentNullException(nameof(main)); _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _limits = limits ?? new();
        _observeResult = observeResult;
        if (_limits.MaximumEntries < 1 || _limits.MaximumEntries > 100000 || _limits.MaximumBytes < 128 ||
            _limits.MaximumBytes > 64 * 1024 * 1024 || _limits.MaximumPageSize < 1 || _limits.MaximumPageSize > 4096 ||
            _limits.RetainedRevisions < 1 || _limits.RetainedRevisions > 8)
            throw new ArgumentOutOfRangeException(nameof(limits));
    }

    internal void Invalidate(string reason)
    {
        _main.VerifyAccess(); if (_disposed) return;
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("An invalidation reason is required.", nameof(reason));
        _invalidation++;
        _staleReason = reason;
    }
    public Result<DataSnapshotHeader> Refresh(CancellationToken cancellationToken = default)
    {
        var error = Verify(); if (error is not null) return Result<DataSnapshotHeader>.Fail(error);
        if (cancellationToken.IsCancellationRequested) return Failed<DataSnapshotHeader>(ErrorCode.Cancelled, "Snapshot capture was cancelled.");
        if (_refreshing) return Failed<DataSnapshotHeader>(ErrorCode.Conflict, "A game data capture is already in progress.", true);
        var stamp = _invalidation;
        _refreshing = true;
        try
        {
            var resultAtStart = ReadObservedResult();
            var captured = _capture(cancellationToken);
            if (PublicationError(stamp, cancellationToken) is { } interrupted) return Result<DataSnapshotHeader>.Fail(interrupted);
            if (!captured.Success)
            {
                var failure = captured.Error!;
                if (failure.KnownCode != ErrorCode.Cancelled) _staleReason = failure.Message;
                return Result<DataSnapshotHeader>.Fail(failure);
            }
            var input = captured.Value!;
            if (input is not null && resultAtStart is { } observed)
                input = input with { SupportsCurrentResult = observed.Available, CurrentResult = observed.Result };
            var copied = CopyAndValidate(input!, cancellationToken);
            var resultAtEnd = ReadObservedResult();
            var current = _snapshots.LastOrDefault();
            var unchanged = current is not null && Equivalent(current.Data, copied);
            if (PublicationError(stamp, cancellationToken) is { } blocked) return Result<DataSnapshotHeader>.Fail(blocked);
            if (resultAtStart != resultAtEnd)
            {
                const string reason = "The observed result changed during data capture.";
                Invalidate(reason);
                return Failed<DataSnapshotHeader>(ErrorCode.NotReady, reason, true);
            }
            var now = DateTimeOffset.UtcNow;
            if (unchanged)
            {
                current = current! with { VerifiedAt = now }; _snapshots[^1] = current;
            }
            else
            {
                current = new(new(_session, checked(++_revision)), copied, now, now); _snapshots.Add(current);
                while (_snapshots.Count > _limits.RetainedRevisions) _snapshots.RemoveAt(0);
            }
            _staleReason = null;
            return Result<DataSnapshotHeader>.Ok(Header(current));
        }
        catch (Exception failure)
        {
            if (PublicationError(stamp, cancellationToken) is { } interrupted) return Result<DataSnapshotHeader>.Fail(interrupted);
            if (failure is OperationCanceledException) return Failed<DataSnapshotHeader>(ErrorCode.Cancelled, "Snapshot capture was cancelled.");
            var (code, message) = failure switch
            {
                SnapshotQuotaException => (ErrorCode.QuotaExceeded, failure.Message),
                InvalidDataException => (ErrorCode.ContentInvalid, failure.Message),
                JsonException => (ErrorCode.ContentInvalid, "Snapshot JSON is invalid: " + failure.Message),
                _ => (ErrorCode.ServiceUnavailable, "Snapshot provider failed: " + failure.Message)
            };
            _staleReason = message;
            return Failed<DataSnapshotHeader>(code, message, code == ErrorCode.ServiceUnavailable);
        }
        finally { _refreshing = false; }
    }
    private OperationError? PublicationError(long stamp, CancellationToken token)
    {
        if (_disposed) return new(ErrorCode.ScopeExpired, "Snapshot module was released.", DataCapabilityIds.Read);
        if (token.IsCancellationRequested) return new(ErrorCode.Cancelled, "Snapshot capture was cancelled.", DataCapabilityIds.Read);
        if (_invalidation != stamp) return new(ErrorCode.NotReady, _staleReason!, DataCapabilityIds.Read, Retryable: true);
        return null;
    }
    private (bool Available, RunFinished? Result)? ReadObservedResult()
    {
        if (_observeResult is null) return null;
        var observed = _observeResult();
        return (observed.Available, observed.Available ? observed.Result : null);
    }
    private void VerifyObservedResult(Snapshot current)
    {
        if (_observeResult is null || _staleReason is not null) return;
        try
        {
            if (ReadObservedResult() != (current.Data.SupportsCurrentResult, current.Data.CurrentResult))
                Invalidate("The observed result changed after this data revision was captured.");
        }
        catch (Exception error) { Invalidate("Current result observation is unavailable: " + error.Message); }
    }
    public Result<DataSnapshotHeader> GetHeader(bool allowStale = false)
    {
        var error = Verify(); if (error is not null) return Result<DataSnapshotHeader>.Fail(error);
        var snapshot = _snapshots.LastOrDefault();
        if (snapshot is null) return Failed<DataSnapshotHeader>(ErrorCode.NotReady, _staleReason ?? "No data revision exists.", true);
        VerifyObservedResult(snapshot);
        if (!allowStale && _staleReason is not null) return Failed<DataSnapshotHeader>(ErrorCode.NotReady, _staleReason, true);
        return Result<DataSnapshotHeader>.Ok(Header(snapshot));
    }
    public Result<DataPage<ChartInfo>> GetCharts(DataPageRequest request, CancellationToken cancellationToken = default)
        => Page(request, s => s.Data.Charts, cancellationToken);
    public Result<DataPage<ScoreRecord>> GetBestScores(DataPageRequest request, CancellationToken cancellationToken = default)
        => Page(request, s => s.Data.BestScores, cancellationToken);
    public Result<DataPage<GameSettingSnapshot>> GetSettings(DataPageRequest request, CancellationToken cancellationToken = default)
        => Page(request, s => s.Data.Settings, cancellationToken);
    public Result<RunFinished> GetCurrentResult(DataSnapshotToken token, bool allowStale = false)
    {
        var found = Resolve(token, allowStale); if (!found.Success) return Result<RunFinished>.Fail(found.Error!);
        var data = found.Value!.Data;
        if (!data.SupportsCurrentResult) return Failed<RunFinished>(ErrorCode.OperationUnsupported, "No verified current-result source was supplied to this module.");
        return data.CurrentResult is null ? Failed<RunFinished>(ErrorCode.NotReady, "No actual current result has been observed.", true)
            : Result<RunFinished>.Ok(data.CurrentResult);
    }
    private Result<DataPage<T>> Page<T>(DataPageRequest request, Func<Snapshot, IReadOnlyList<T>?> select, CancellationToken token)
    {
        if (token.IsCancellationRequested) return Failed<DataPage<T>>(ErrorCode.Cancelled, "Snapshot page was cancelled.");
        if (request is null || request.Offset < 0 || request.Count < 1 || request.Count > _limits.MaximumPageSize)
            return Failed<DataPage<T>>(ErrorCode.InvalidArgument, $"Offset must be nonnegative and count must be 1–{_limits.MaximumPageSize}.");
        var found = Resolve(request.Token, request.AllowStale); if (!found.Success) return Result<DataPage<T>>.Fail(found.Error!);
        var snapshot = found.Value!; var items = select(snapshot);
        if (items is null) return Failed<DataPage<T>>(ErrorCode.OperationUnsupported, "This data section has no verified source in the current module.");
        if (request.Offset > items.Count) return Failed<DataPage<T>>(ErrorCode.InvalidArgument, "Offset exceeds the revision's item count.");
        var count = Math.Min(request.Count, items.Count - request.Offset);
        var copied = new T[count];
        for (var i = 0; i < count; i++)
        { if (token.IsCancellationRequested) return Failed<DataPage<T>>(ErrorCode.Cancelled, "Snapshot page was cancelled."); copied[i] = items[request.Offset + i]; }
        return Result<DataPage<T>>.Ok(new(Header(snapshot), request.Offset, items.Count, copied));
    }
    private Result<Snapshot> Resolve(DataSnapshotToken? token, bool allowStale)
    {
        var error = Verify(); if (error is not null) return Result<Snapshot>.Fail(error);
        if (token is null || string.IsNullOrWhiteSpace(token.SessionId) || token.Revision < 1)
            return Failed<Snapshot>(ErrorCode.InvalidArgument, "A valid session/revision token is required.");
        var found = _snapshots.FirstOrDefault(s => s.Token == token);
        if (found is null) return Failed<Snapshot>(ErrorCode.TargetExpired, "This data revision is absent, expired, or belongs to another session.");
        VerifyObservedResult(_snapshots[^1]);
        if (!allowStale && (_staleReason is not null || found != _snapshots[^1]))
            return Failed<Snapshot>(ErrorCode.NotReady, _staleReason ?? "A newer data revision exists; request stale data explicitly or refresh the token.", true);
        return Result<Snapshot>.Ok(found);
    }
    private DataSnapshotHeader Header(Snapshot snapshot)
    {
        var old = snapshot.Token != _snapshots[^1].Token;
        var reason = _staleReason ?? (old ? "A newer captured revision exists." : null);
        return new(snapshot.Token, snapshot.Data.SourceNamespace, snapshot.CapturedAt, snapshot.VerifiedAt,
            snapshot.Data.Charts.Count, snapshot.Data.BestScores.Count, snapshot.Data.Settings?.Count,
            snapshot.Data.SupportsCurrentResult, reason is not null, reason);
    }
    private DataSnapshotInput CopyAndValidate(DataSnapshotInput input, CancellationToken token)
    {
        if (input is null || input.Charts is null || input.BestScores is null) throw new InvalidDataException("Snapshot input or required collections are null.");
        if ((long)input.Charts.Count + input.BestScores.Count + (input.Settings?.Count ?? 0) > _limits.MaximumEntries)
            throw new SnapshotQuotaException("Snapshot entry count exceeds the module limit.");
        Text(input.SourceNamespace, "source namespace", 256, true);
        var charts = new List<ChartInfo>(input.Charts.Count); var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chart in input.Charts)
        {
            token.ThrowIfCancellationRequested(); if (chart is null) throw new InvalidDataException("Null chart entry.");
            Text(chart.Id, "chart ID", 4096, true); Text(chart.SongId, "song ID", 4096, true);
            Text(chart.Title, "title", 65536); Text(chart.DifficultyName, "difficulty name", 4096);
            if (chart.Rating is double rating && !double.IsFinite(rating)) throw new InvalidDataException("Nonfinite chart rating.");
            if (!ids.Add(chart.Id)) throw new InvalidDataException("Duplicate chart ID: " + chart.Id);
            charts.Add(chart with { });
        }
        var scores = new List<ScoreRecord>(input.BestScores.Count); var scoreIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var score in input.BestScores)
        {
            token.ThrowIfCancellationRequested(); if (score is null) throw new InvalidDataException("Null score entry.");
            if (!ids.Contains(score.ChartId) || !scoreIds.Add(score.ChartId)) throw new InvalidDataException("Score ID is missing from the catalog or duplicated.");
            if (!double.IsFinite(score.RawCompletion)) throw new InvalidDataException("Nonfinite raw completion.");
            scores.Add(score with { });
        }
        List<GameSettingSnapshot>? settings = null;
        if (input.Settings is not null)
        {
            settings = new(); var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var setting in input.Settings)
            {
                token.ThrowIfCancellationRequested(); if (setting is null) throw new InvalidDataException("Null setting entry.");
                Text(setting.Id, "setting ID", 4096, true); Text(setting.Source, "setting source", 4096, true); Text(setting.JsonValue, "setting JSON", 65536, true);
                if (!keys.Add(setting.Id)) throw new InvalidDataException("Duplicate setting ID.");
                using var parsed = JsonDocument.Parse(setting.JsonValue, new JsonDocumentOptions { MaxDepth = 16 });
                settings.Add(setting with { });
            }
        }
        var result = input.CurrentResult;
        if (result is not null)
        {
            if (result.RunId is not null) Text(result.RunId, "result run ID", 4096); if (result.ChartId is not null) Text(result.ChartId, "result chart ID", 4096);
            if (!double.IsFinite(result.RawCompletion)) throw new InvalidDataException("Nonfinite result completion.");
        }
        var copied = new DataSnapshotInput(Array.AsReadOnly(charts.OrderBy(c => c.Id, StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(scores.OrderBy(s => s.ChartId, StringComparer.Ordinal).ToArray()), input.SupportsCurrentResult || result is not null,
            result is null ? null : result with { }, settings is null ? null : Array.AsReadOnly(settings.OrderBy(s => s.Id, StringComparer.Ordinal).ToArray()), input.SourceNamespace);
        using var stream = new QuotaStream(_limits.MaximumBytes, token);
        JsonSerializer.Serialize(stream, copied); // Enforce actual UTF-8 JSON byte count without retaining a second large byte buffer.
        return copied;
    }
    private static void Text(string? value, string field, int maximum, bool required = false)
    {
        if (value is null || value.Length > maximum || required && string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"Snapshot {field} is null, missing or exceeds its length limit.");
    }
    private static bool Equivalent(DataSnapshotInput a, DataSnapshotInput b)
        => a.SourceNamespace == b.SourceNamespace && a.Charts.SequenceEqual(b.Charts) && a.BestScores.SequenceEqual(b.BestScores)
            && a.SupportsCurrentResult == b.SupportsCurrentResult && a.CurrentResult == b.CurrentResult
            && (a.Settings is null ? b.Settings is null : b.Settings is not null && a.Settings.SequenceEqual(b.Settings));
    private OperationError? Verify()
    {
        if (!_main.IsMainThread) return new(ErrorCode.WrongThread, "Snapshot operations require the Unity main thread.", DataCapabilityIds.Read);
        return _disposed ? new(ErrorCode.ScopeExpired, "Snapshot module was released.", DataCapabilityIds.Read) : null;
    }
    private static Result<T> Failed<T>(ErrorCode code, string message, bool retryable = false)
        => Result<T>.Fail(new(code, message, DataCapabilityIds.Read, Retryable: retryable));
    public IReadOnlyList<DataFieldInfo> GetFieldDescriptions()
    {
        _main.VerifyAccess(); if (_disposed) throw new ObjectDisposedException(nameof(DataSnapshotService));
        return Fields;
    }
    private static readonly IReadOnlyList<DataFieldInfo> Fields = Array.AsReadOnly(new[] {
        new DataFieldInfo("ChartInfo.Id", "opaque stable ID", "Official ChartInfo.id", true),
        new DataFieldInfo("ChartInfo.SongId", "opaque stable ID", "Official Level.id", true),
        new DataFieldInfo("ChartInfo.Title", "display text", "Official Level.musicInfo.musicName", true),
        new DataFieldInfo("ChartInfo.DifficultyName", "game class name", "Official ChartInfo.level", true),
        new DataFieldInfo("ChartInfo.Rating", "game difficulty", "Official ChartInfo.difficulty", true, true, "Source may omit a numeric rating."),
        new DataFieldInfo("ScoreRecord.ChartId", "opaque stable chart ID", "Best-score slot owner ChartInfo.id", true),
        new DataFieldInfo("ScoreRecord.Score", "game score points", "Official best score", true),
        new DataFieldInfo("ScoreRecord.RawCompletion", "game raw completion units", "Official best score completeRate", true),
        new DataFieldInfo("ScoreRecord.HitCount", "game hit count", "Official bestScore.hit; not reinterpreted as single-run Perfect", true),
        new DataFieldInfo("ScoreRecord.BadCount", "count", "Official bestScore.bad", true),
        new DataFieldInfo("ScoreRecord.MissCount", "count", "Official bestScore.miss", true),
        new DataFieldInfo("ScoreRecord.IsFullCombo", "Boolean", "Official bestScore.isFullCombo", true),
        new DataFieldInfo("ScoreRecord.IsClear", "Boolean", "Official bestScore.isClear", true),
        new DataFieldInfo("ScoreRecord.PlayedAt", "UTC timestamp", "Unavailable in the current native best-score source", false, true, "Never synthesized from capture time."),
        new DataFieldInfo("RunFinished.RunId", "host run identity", "Observed lifecycle correlation supplied by the module", false, true, "No started run may be associated; never synthesized from scores."),
        new DataFieldInfo("RunFinished.ChartId", "opaque stable chart ID", "Actual InsideLevelResult.metadata, when resolved", true, true, "Metadata may not map to the official catalog."),
        new DataFieldInfo("RunFinished.Score", "game score points", "Actual InsideLevelResult.get_Score", true),
        new DataFieldInfo("RunFinished.RawCompletion", "game raw completion units", "Actual InsideLevelResult.get_RawCompleteRate", true),
        new DataFieldInfo("RunFinished.Perfect", "count", "Actual InsideLevelResult.perfect", true),
        new DataFieldInfo("RunFinished.MaxPerfect", "count", "Actual InsideLevelResult.maxPerfect; independent from Perfect", true),
        new DataFieldInfo("RunFinished.Bad", "count", "Actual InsideLevelResult.bad", true),
        new DataFieldInfo("RunFinished.Miss", "count", "Actual InsideLevelResult.miss", true),
        new DataFieldInfo("RunFinished.Crashed", "Boolean", "Actual InsideLevelResult.crashed", true),
        new DataFieldInfo("RunFinished.Skipped", "Boolean", "Actual InsideLevelResult.skip", true),
        new DataFieldInfo("GameSettingSnapshot.Id", "adapter setting ID", "Adapter-verified source supplied by the module", false),
        new DataFieldInfo("GameSettingSnapshot.JsonValue", "per setting Unit", "Adapter-verified game setting; absent source is unsupported", true),
        new DataFieldInfo("GameSettingSnapshot.Source", "provenance text", "Supplying adapter module", false),
        new DataFieldInfo("GameSettingSnapshot.Unit", "unit label", "Supplying adapter module", false, true, "The game may not define a unit for this setting.") });
    public void Dispose()
    {
        _main.VerifyAccess(); if (_disposed) return; _disposed = true; _snapshots.Clear(); _staleReason = "Snapshot module was released.";
    }
    private sealed class SnapshotQuotaException : Exception { internal SnapshotQuotaException(string message) : base(message) { } }
    private sealed class QuotaStream : Stream
    {
        private readonly long _maximum; private readonly CancellationToken _token; private long _written;
        internal QuotaStream(long maximum, CancellationToken token) { _maximum = maximum; _token = token; }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        { _token.ThrowIfCancellationRequested(); if (buffer.Length > _maximum - _written) throw new SnapshotQuotaException("Snapshot UTF-8 byte count exceeds the module limit."); _written += buffer.Length; }
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => _written; public override long Position { get => _written; set => throw new NotSupportedException(); }
        public override void Flush() { } public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
    }
}
