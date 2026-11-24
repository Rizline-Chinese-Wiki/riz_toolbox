namespace RizToolbox.Sdk;

public static class GamePostProcessingCapabilityIds
{
    public const string PostProcessing = "riz.game.post-processing";
}

public static class GamePostProcessingRoles
{
    public const string MainCameraBlurDownsample = "game.post-processing.main-camera.blur-downsample";
    public const string DownsampleProperty = "downsample";
}

/// <summary>Experimental integer exponent for the menu main camera's blur texture downsampling.</summary>
public sealed record GameBlurDownsampleTarget(TargetHandle<int> Target, string Scene, int Minimum, int Maximum);

/// <summary>Bound means an active priority layer. Target is absent while waiting, ambiguous or stopped.</summary>
public sealed record GameBlurBindingSnapshot(long Revision, VisualBindingState State, TargetQuery Query, int Value,
    int Priority, TargetHandle<int>? Target, OperationError? Error);

public interface IGameBlurBindingIntent : IDisposable
{
    /// <summary>Last immutable observation. Reading it does not discover targets or apply an override.</summary>
    GameBlurBindingSnapshot LastSnapshot { get; }
    /// <summary>Permanently releases this intent on the host thread.</summary>
    Result Stop();
}

public interface IGamePostProcessingService
{
    Result<GameBlurDownsampleTarget> GetDownsampleTarget();
    /// <summary>Overrides only this target generation. The current plugin scope owns the lease.</summary>
    Result<IMutationLease<int>> OverrideDownsample(TargetHandle<int> target, int value, int priority = 0);
    /// <summary>Queues an override for a stable blur role or key in one supported scene. The plugin scope owns the intent.</summary>
    Result<IGameBlurBindingIntent> BindDownsample(TargetQuery query, int value, int priority = 0);
}
