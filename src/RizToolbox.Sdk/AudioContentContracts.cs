namespace RizToolbox.Sdk;

public static class AudioContentCapabilityIds
{
    public const string Playback = "riz.audio.playback";
    public const string Replacement = "riz.audio.replace";
    public const string Mix = "riz.audio.mix";
    public const string Clock = "riz.audio.clock";
    public const string LocalContent = "riz.content.local";
    public const string LocalContentBatch = "riz.content.local.batch";
    public const string LocalCatalog = "riz.content.local.catalog";
    public const string LocalCatalogSelection = "riz.content.local.selection";
    public const string WorkshopChartReplacement = "riz.content.workshop-chart";
}
public enum ContentFileArea { Package, PluginData }
public sealed record ContentFile(ContentFileArea Area, string RelativePath)
{
    /// <summary>Maximum bytes to read from the opened content file. Must be 1..16 MiB; defaults to 16 MiB.</summary>
    public int MaximumBytes { get; init; } = 16 * 1024 * 1024;
}
public sealed record AudioAsset(string Id);
public sealed record AudioTarget(TargetHandle<AudioAsset> Target, TargetMetadata Metadata, IReadOnlyList<string> Aliases);
public sealed record AudioClockSnapshot(double DspSeconds, double HostMonotonicSeconds);
public enum AudioMixParameter { Volume, Pitch }
public interface IAudioAssetLease : IDisposable
{
    AudioAsset Asset { get; }
    int SampleFrames { get; }
    int Channels { get; }
    int Frequency { get; }
    bool IsValid { get; }
}
/// <summary>Own-source playback. Stop/Dispose stops the source before releasing its clip reference.</summary>
public interface IAudioPlaybackLease : IDisposable
{
    bool IsActive { get; }
    Result Stop();
}
/// <summary>Main-thread only. PCM16 WAV, mono/stereo, 8..192 kHz, at most 16 MiB encoded and 32 MiB decoded.
/// The encoded limit can be lowered with ContentFile.MaximumBytes.
/// Loading is synchronous and cancellable between bounded reads and decoding blocks; it is not streaming.
/// Clip replacement does not promise a restart, sample alignment, latency or unchanged game results.</summary>
public interface IAudioPlaybackService
{
    Result<IAudioAssetLease> LoadWav(ContentFile file, CancellationToken cancellationToken = default);
    Result<IAudioPlaybackLease> Play(AudioAsset clip, float volume = 1, bool loop = false);
}
public interface IAudioReplacementService
{
    Result<IReadOnlyList<AudioTarget>> GetTargets();
    Result<IMutationLease<AudioAsset>> Replace(TargetHandle<AudioAsset> target, AudioAsset clip, int priority = 0);
}
/// <summary>Per-source properties only. Volume [0,1], pitch [0.25,3]. Mixer buses and global volume are not declared.</summary>
public interface IAudioMixService
{
    Result<IMutationLease<float>> Override(TargetHandle<AudioAsset> target, AudioMixParameter parameter, float value, int priority = 0);
}
public interface IAudioClockService { Result<AudioClockSnapshot> Read(); }

public sealed record LocalContentId(string Namespace, string Id);
public sealed record LocalContentDependency(LocalContentId Content, int MinimumFormatVersion = 1);
public enum LocalContentKind { Audio, Cover, Chart }
/// <summary>The namespace is always the caller's plugin ID. Chart format versions require a build-specific parsing boundary.</summary>
public sealed record LocalContentRequest(string Id, LocalContentKind Kind, string Format, int FormatVersion,
    ContentFile File, IReadOnlyList<LocalContentDependency>? Dependencies = null);
public sealed record LocalContentSnapshot(LocalContentId Content, LocalContentKind Kind, string Format, int FormatVersion,
    long Revision, bool AvailableForGameResolution, IReadOnlyList<LocalContentId> Dependencies);
public interface ILocalContentRegistration : IDisposable { LocalContentSnapshot Content { get; } bool IsPublished { get; } }
/// <summary>Main-thread provider registry. Audio and covers are validated completely before publication.
/// Listing metadata does not imply the game can resolve or play a chart. No built-in chart format is currently declared.</summary>
public interface ILocalContentProviderService
{
    Result<ILocalContentRegistration> Register(LocalContentRequest request, CancellationToken cancellationToken = default);
    Result<IReadOnlyList<LocalContentSnapshot>> GetPublished();
}
/// <summary>One atomic publication of validated entries in the caller's namespace.
/// Disposing withdraws all entries together; existing host consumers retain their dependency graph.
/// This is a content-registration group, not a new playable-song catalog entry.</summary>
public interface ILocalContentBatchRegistration : IDisposable
{
    long Revision { get; }
    IReadOnlyList<LocalContentSnapshot> Contents { get; }
    bool IsPublished { get; }
}
/// <summary>Independent additive capability. A batch contains 1..32 requests and may refer
/// forward to entries in the same batch. All validation and quota checks precede publication.
/// Missing dependencies, dependency cycles, cancellation or scope withdrawal publish nothing.</summary>
public interface ILocalContentBatchProviderService
{
    Result<ILocalContentBatchRegistration> RegisterBatch(IReadOnlyList<LocalContentRequest> requests,
        CancellationToken cancellationToken = default);
}
public interface ILocalChartBinding : IDisposable
{
    ulong WorkshopId { get; }
    LocalContentId Content { get; }
    bool IsActive { get; }
}
/// <summary>Experimental Windows build-specific parser boundary for existing workshop IDs.
/// Binds one validated local v0 chart exclusively. The original loader executes once and its failures/null are preserved.
/// This does not add songs to the game catalog or replace the selected workshop song's music/cover.</summary>
public interface ILocalChartReplacementService
{
    Result<ILocalChartBinding> BindWorkshopChart(ulong workshopId, LocalContentId content, bool allowExperimental = false);
}
