namespace RizToolbox.Sdk;

public static class GameCapabilityIds
{
    public const string Resources = "riz.game-resources";
    public const string Scenes = "riz.scenes";
    public const string Runs = "riz.runs";
}

/// <summary>Active scene names observed on the Unity main thread.</summary>
public sealed record GameSceneChanged(string PreviousScene, string Scene)
{
    public string? SessionId { get; init; }
    public long Revision { get; init; }
    public long SceneGeneration { get; init; }
}
/// <summary>A new live gameplay object observed by the adapter. ChartId can be unavailable.</summary>
public sealed record RunStarted(string RunId, string? ChartId)
{
    public string? SessionId { get; init; }
    public long Revision { get; init; }
    public long SceneGeneration { get; init; }
}
/// <summary>Copied from a newly observed InsideLevelResult, preserving the game's individual counters.</summary>
public sealed record RunFinished(string? RunId, string? ChartId, long Score, double RawCompletion,
    int Perfect, int MaxPerfect, int Bad, int Miss, bool Crashed, bool Skipped)
{
    public string? SessionId { get; init; }
    public long Revision { get; init; }
    public long SceneGeneration { get; init; }
    public bool AssociatedWithObservedStart { get; init; }
    public RunEndReason EndReason { get; init; }
}
public sealed record GameStateSnapshot(string Scene, string? RunId, string? ChartId, bool IsPlaying)
{
    public string? SessionId { get; init; }
    public long Revision { get; init; }
    public long SceneGeneration { get; init; }
    public bool? Paused { get; init; }
}
public interface IGameStateService
{
    OperationResult<GameStateSnapshot> GetSnapshot();
}

public static class GameExtensionPoints
{
    public static ExtensionPoint<GameSceneChanged> SceneChanged { get; } = new("game.scene.changed", new(1, 0, 0));
    public static ExtensionPoint<RunStarted> RunStarted { get; } = new("game.run.started", new(1, 0, 0));
    public static ExtensionPoint<RunFinished> RunFinished { get; } = new("game.run.finished", new(1, 0, 0));
}

public sealed record GameResourceTarget(string Key, string Kind, string Description);
public sealed record ResourceColor(float R, float G, float B, float A = 1);
public sealed record GameColorOverrideRequest(string ResourceKey, ResourceColor Color, int Priority = 0);

/// <summary>Adapter-discovered real game targets. All methods require the plugin's main thread.</summary>
public interface IGameResourceService
{
    OperationResult<IReadOnlyList<GameResourceTarget>> GetTargets();
    OperationResult<IResourceOverride> OverrideColor(GameColorOverrideRequest request);
}
