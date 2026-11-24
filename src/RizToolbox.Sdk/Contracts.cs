namespace RizToolbox.Sdk;

public static class SdkInfo
{
    public static Version Version { get; } = new(1, 3, 0);
}

public sealed record PluginDependency(string Id, Version MinVersion, Version? MaxVersionExclusive = null);

public sealed record PluginManifest(
    string Id,
    Version Version,
    Version MinSdk,
    Version? MaxSdkExclusive = null,
    IReadOnlyList<PluginDependency>? Dependencies = null,
    IReadOnlyList<string>? RequiredCapabilities = null)
{
    /// <summary>Missing, disabled or incompatible optional providers do not prevent activation.</summary>
    public IReadOnlyList<PluginDependency>? OptionalDependencies { get; init; }
    /// <summary>Conflicts are symmetric at runtime even if only one plugin declares the rule.</summary>
    public IReadOnlyList<PluginConflict>? Conflicts { get; init; }
    /// <summary>Orders already requested packages; does not request or require the named package.</summary>
    public IReadOnlyList<string>? LoadAfter { get; init; }
}

public sealed record PluginConflict(string Id, Version? MinVersion = null, Version? MaxVersionExclusive = null);
public sealed record PluginDependencyStatus(string Id, bool Optional, bool Installed, bool VersionCompatible,
    bool Enabled, Version? InstalledVersion, string? Reason = null);

/// <summary>Read-only, main-thread query of the caller's declared hard/optional plugin dependencies.</summary>
public interface IPluginDependencyService
{
    OperationResult<PluginDependencyStatus> Query(string dependencyId);
    IReadOnlyList<PluginDependencyStatus> GetAll();
}

public static class PluginLifecycleCapabilityIds
{
    public const string Dependencies = "riz.plugin-dependencies";
}

public interface IRizPlugin
{
    PluginManifest Manifest { get; }
    void Enable(IPluginContext context);
    void Disable();
}

public interface IPluginContext
{
    string PluginId { get; }
    ILogger Log { get; }
    IMainThreadDispatcher MainThread { get; }
    IEventBus Events { get; }
    /// <summary>JSON configuration isolated to this plugin's directory. Available only while enabled.</summary>
    IPluginConfiguration Configuration => throw new NotSupportedException("This host does not provide plugin configuration.");
    GameBuildDescriptor GameBuild { get; }
    CapabilityDescriptor GetCapability(string capabilityId);
    ServiceLookup<T> GetService<T>(string capabilityId) where T : class;
    /// <summary>Transfers cleanup responsibility to the current enable scope.</summary>
    T Own<T>(T resource) where T : IDisposable;
}

public enum LogLevel { Debug, Info, Warning, Error }

public interface ILogger
{
    void Log(LogLevel level, string message, Exception? exception = null);
}

public interface IEventBus
{
    /// <summary>Higher priorities run first; equal priorities preserve registration order.</summary>
    IDisposable Subscribe<T>(Action<T> handler, int priority = 0);
    void Publish<T>(T message);
}

public interface IMainThreadDispatcher
{
    bool IsMainThread { get; }
    void VerifyAccess();
    Task InvokeAsync(Action action, CancellationToken cancellationToken = default);
}

/// <summary>A public operation succeeds or carries one complete structured failure. Use the factories to create a valid state.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(OperationResultJsonConverter))]
public sealed class OperationResult
{
    public bool Success { get; }
    public OperationError? Error { get; }
    private OperationResult(bool success, OperationError? error) => (Success, Error) = (success, error);
    public static OperationResult Ok() => new(true, null);
    public static OperationResult Fail(OperationError error) => new(false, ErrorCodes.RequireError(error));
    public static OperationResult Fail(ErrorCode code, string message, string? capabilityId = null, string? targetId = null, bool retryable = false)
        => Fail(new(code, message, capabilityId, targetId, retryable));
}

/// <summary>Success preserves the declared value, including null. Failure has the default value and a required structured error.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(OperationResultJsonConverter))]
public sealed class OperationResult<T>
{
    public bool Success { get; }
    public T? Value { get; }
    public OperationError? Error { get; }
    private OperationResult(bool success, T? value, OperationError? error) => (Success, Value, Error) = (success, value, error);
    public static OperationResult<T> Ok(T value) => new(true, value, null);
    public static OperationResult<T> Fail(OperationError error) => new(false, default, ErrorCodes.RequireError(error));
    public static OperationResult<T> Fail(ErrorCode code, string message, string? capabilityId = null, string? targetId = null, bool retryable = false)
        => Fail(new(code, message, capabilityId, targetId, retryable));
}

public sealed record GameBuildDescriptor(
    string GameVersion,
    string UnityVersion,
    string Platform,
    string Fingerprint,
    bool IsKnownBuild = false);

public sealed record CapabilityDescriptor(string Id, bool Available, string? UnavailableReason = null);

public sealed record ServiceLookup<T>(bool Available, T? Service, OperationError? Error) where T : class;

public static class CapabilityIds
{
    public const string Catalog = "riz.catalog";
    public const string Scores = "riz.scores";
    public const string Ui = "riz.ui";
    public const string Resources = "riz.resources";
    public const string Practice = "riz.practice";
    public const string Extensions = "riz.extensions";
    public const string Metadata = "riz.capability-metadata";
}

/// <summary>DifficultyName is the game's string class (for example EZ/HD/IN); Rating is its numeric difficulty.</summary>
public sealed record ChartInfo(string Id, string SongId, string Title, string DifficultyName, double? Rating = null);
/// <summary>RawCompletion preserves the game's value without assuming its unit. Hit/Bad/Miss retain game terminology.</summary>
public sealed record ScoreRecord(
    string ChartId, long Score, double RawCompletion,
    int HitCount, int BadCount, int MissCount, bool IsFullCombo = false, bool IsClear = false,
    DateTimeOffset? PlayedAt = null);
public sealed record RunCompleted(ScoreRecord Score);
/// <summary>A host tick on the Unity main thread. ElapsedSeconds is monotonic time since host initialization.</summary>
public sealed record FrameUpdate(double ElapsedSeconds);

public interface ICatalogService
{
    OperationResult<IReadOnlyList<ChartInfo>> GetCharts();
}

public interface IScoreService
{
    OperationResult<IReadOnlyList<ScoreRecord>> GetBestScoresSnapshot();
}

public sealed record PanelDefinition(string Id, string Title, string Text);
public interface IPanelHandle : IDisposable
{
    string Id { get; }
    OperationResult SetText(string text);
}
public interface IUiService
{
    OperationResult<IPanelHandle> CreatePanel(PanelDefinition definition);
}

/// <summary>ResourceKey and accepted encoded image formats are adapter-defined. Unsupported formats return an explicit failure.</summary>
public sealed record TextureOverrideRequest(string ResourceKey, byte[] EncodedImage, int Priority = 0);
public interface IResourceOverride : IDisposable
{
    string ResourceKey { get; }
}
public interface IResourceService
{
    OperationResult<IResourceOverride> OverrideTexture(TextureOverrideRequest request);
}
