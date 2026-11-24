using RizToolbox.Sdk;

namespace RizToolbox.BackgroundPlay;

public sealed class BackgroundPlayPlugin : IRizPlugin
{
    public PluginManifest Manifest { get; } = new("riz.background-play", new(0, 3, 0), new(1, 2, 0),
        RequiredCapabilities: new[] { BackgroundPlayCapability.Id, GameLifecycleCapabilityIds.Lifecycle });

    public void Enable(IPluginContext context)
    {
        var lookup = context.GetService<IBackgroundPlayService>(BackgroundPlayCapability.Id);
        var service = lookup.Service ?? throw new InvalidOperationException(lookup.Error?.Message);
        var result = service.EnableForScope();
        if (!result.Success) throw new InvalidOperationException(result.Error!.Message);
        context.Log.Log(LogLevel.Info, "Background play enabled for this game window.");
    }

    public void Disable() { }
}
