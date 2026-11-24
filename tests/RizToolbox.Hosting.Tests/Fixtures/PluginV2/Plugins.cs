using RizToolbox.Sdk;
namespace IsolationFixture;
public class Plugin : IRizPlugin
{
    public static int Constructions;
    public string Proof { get; } = IsolationPrivate.Value.Read();
    public PluginManifest Manifest => new("package.two", new(2, 0, 0), new(1, 0, 0));
    public Plugin() => Constructions++;
    public void Enable(IPluginContext context) { }
    public void Disable() { }
}
