using RizToolbox.Sdk;

namespace RizToolbox.Examples;

public sealed class LocalCatalogBrowserPlugin : IRizPlugin
{
    public PluginManifest Manifest => new("tools.local-catalog", new(0, 3, 0), new(1, 6, 0),
        MaxSdkExclusive: new(2, 0, 0), RequiredCapabilities: new[]
        {
            AudioContentCapabilityIds.LocalCatalog,
            AudioContentCapabilityIds.LocalCatalogSelection,
            UiInputCapabilityIds.Windows
        });

    public void Enable(IPluginContext context) => LocalCatalogRuntimeProbe.StartBrowser(context);

    public void Disable() { }
}
