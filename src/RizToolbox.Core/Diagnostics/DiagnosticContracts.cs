using RizToolbox.Sdk;

namespace RizToolbox.Core.Diagnostics;

/// <summary>Optional identifiers, copied as strings; never pass native objects or account records.</summary>
public sealed record DiagnosticContext(
    string? PluginId = null,
    string? CapabilityId = null,
    string? TargetId = null,
    string? RunId = null,
    long? SceneGeneration = null,
    string? CorrelationId = null);

public sealed class DiagnosticOptions
{
    public DiagnosticOptions(string rootDirectory) => RootDirectory = rootDirectory;
    public string RootDirectory { get; }
    public string? SessionId { get; init; }
    public LogLevel MinimumLevel { get; init; } = LogLevel.Info;
    public long MaxFileBytes { get; init; } = 1_048_576;
    public int MaxFiles { get; init; } = 4;
    public int MaxSessions { get; init; } = 8;
    public int MaxMessageCharacters { get; init; } = 2048;
    public int MaxExceptionCharacters { get; init; } = 8192;
    public int MaxFields { get; init; } = 16;
    public int MaxFieldValueCharacters { get; init; } = 512;
    public int MaxRepeatKeys { get; init; } = 256;
    public int RepeatBurst { get; init; } = 4;
    public TimeSpan RepeatWindow { get; init; } = TimeSpan.FromSeconds(5);
    public int MaxEventsPerWindow { get; init; } = 200;
    public TimeSpan RateWindow { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan DiskRetryInterval { get; init; } = TimeSpan.FromSeconds(5);
    /// <summary>Pending capacity includes the event currently being processed. Overflow drops the newest event.</summary>
    public int MaxQueuedEvents { get; init; } = 1024;
    /// <summary>Conservative charge for copied event values, including the event currently being processed.</summary>
    public long MaxQueuedBytes { get; init; } = 4_194_304;
    public TimeSpan FlushTimeout { get; init; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan DisposeTimeout { get; init; } = TimeSpan.FromMilliseconds(250);
    /// <summary>Optional compatibility sink. Its rotations are bounded independently and contain mixed sessions.</summary>
    public string? TextLogPath { get; init; }
}

public sealed record DiagnosticSnapshot(
    string SessionId, DateTimeOffset StartedAt, DateTimeOffset SnapshotAt,
    string? DirectoryPath, LogLevel MinimumLevel,
    long Attempted, long Written, long TextWritten, long Filtered,
    long RepeatSuppressed, long RateSuppressed, long DiskFailures,
    long DiskSkipped, long InternalFailures, long Oversized,
    int RetainedFiles, long RetainedBytes, int TrackedRepeatKeys,
    string? LastError, IReadOnlyDictionary<string, long> Levels)
{
    public long Enqueued { get; init; }
    public long Processed { get; init; }
    public long QueueDropped { get; init; }
    public long QueueAbandoned { get; init; }
    public int PendingEvents { get; init; }
    public long PendingBytes { get; init; }
    public int PeakPendingEvents { get; init; }
    public long PeakPendingBytes { get; init; }
    public long LastEnqueuedSequence { get; init; }
    public long LastProcessedSequence { get; init; }
    public long LastSuccessfulFlushSequence { get; init; }
    public long FlushTimeouts { get; init; }
    /// <summary>Concrete pending attempts, including the active attempt; at most MaxQueuedEvents + 2.</summary>
    public int PendingFlushAttempts { get; init; }
    public int PeakPendingFlushAttempts { get; init; }
    public bool AcceptingWrites { get; init; }
    public bool ConsumerCompleted { get; init; }
}

/// <summary>
/// Completed confirms processing and a flush attempt through the captured sequence.
/// SnapshotWritten reports the atomic summary write outcome of that attempt, even if a later attempt succeeds.
/// Calls sharing a pending cutoff share its attempt; after disposal, calls observe the final attempt.
/// Neither value promises that every event reached a sink.
/// </summary>
public sealed record DiagnosticFlushResult(bool Completed, bool SnapshotWritten, long ThroughSequence)
{
    public bool TimedOut { get; init; }
}
