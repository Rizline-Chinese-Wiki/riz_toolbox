using RizToolbox.Sdk;

namespace RizModProject;

/// <summary>A read-only scene probe. The host owns its subscriptions for each activation.</summary>
public sealed class Plugin : IRizPlugin
{
    public PluginManifest Manifest => new("wiki.hello", new(0, 1, 0), new(1, 3, 0),
        MaxSdkExclusive: new(2, 0, 0), RequiredCapabilities: new[] { GameCapabilityIds.Scenes });

    public void Enable(IPluginContext context)
    {
        context.MainThread.VerifyAccess();
        var scenes = context.GetService<IGameStateService>(GameCapabilityIds.Scenes);
        if (!scenes.Available || scenes.Service is null)
            throw new InvalidOperationException(scenes.Error?.Message);
        var state = scenes.Service.GetSnapshot();
        if (!state.Success || state.Value is null) throw new InvalidOperationException(state.Error?.Message);
        context.Log.Log(LogLevel.Info, $"{context.PluginId}: current scene {state.Value.Scene}");
        context.Events.Subscribe<GameSceneChanged>(changed =>
            context.Log.Log(LogLevel.Info, $"{context.PluginId}: scene {changed.PreviousScene} -> {changed.Scene}"));
    }

    public void Disable() { }
}
