using System.Reflection;
using RizToolbox.Sdk;

namespace IsolationFixture;

public class Plugin : IRizPlugin
{
    public static int Constructions;
    public string Proof { get; } = IsolationPrivate.Value.Read();
    public virtual PluginManifest Manifest => new("package.one", new(1, 0, 0), new(1, 0, 0));
    public Plugin() => Constructions++;
    public void Enable(IPluginContext context) { }
    public void Disable() { }
}
public class ManifestMismatch : Plugin
{
    public override PluginManifest Manifest => base.Manifest with { Id = "different.id" };
}
public class VersionMismatch : Plugin
{
    public override PluginManifest Manifest => base.Manifest with { Version = new(9, 0, 0) };
}
public class DependencyMismatch : Plugin
{
    public override PluginManifest Manifest => base.Manifest with { Dependencies = new[] { new PluginDependency("different.id", new(1, 0, 0)) } };
}
public class CapabilityMismatch : Plugin
{
    public override PluginManifest Manifest => base.Manifest with { RequiredCapabilities = new[] { "different.capability" } };
}
public class Throwing : Plugin { public Throwing() => throw new InvalidOperationException("fixture constructor failure"); }
public class DiscoveryConstructorPlugin : Plugin
{
    public DiscoveryConstructorPlugin()
    {
        var marker = Environment.GetEnvironmentVariable("RIZ_HOSTING_CONSTRUCTOR_PROBE");
        if (marker is not null) File.AppendAllText(marker, "constructed\n");
    }
}
public class DynamicDefaultDependency : Plugin
{
    public DynamicDefaultDependency() => Assembly.Load(new AssemblyName("HostPrivateOnly"));
}
public class WrongType { }
public abstract class AbstractPlugin : Plugin { }
internal class InternalPlugin : Plugin { }
public class GenericPlugin<T> : Plugin { }
public class NoDefaultConstructor : Plugin { public NoDefaultConstructor(int _) { } }
public class WaitingRequiredPlugin : Plugin
{
    public override PluginManifest Manifest => base.Manifest with { Dependencies = new[] { new PluginDependency("absent.plugin", new(1, 0, 0)) } };
}
public class ExtendedManifestPlugin : Plugin
{
    public override PluginManifest Manifest => base.Manifest with
    {
        OptionalDependencies = new[] { new PluginDependency("package.two", new(2, 0, 0), new(3, 0, 0)) },
        Conflicts = new[] { new PluginConflict("mutually.exclusive", new(1, 0, 0), new(2, 0, 0)) },
        LoadAfter = new[] { "package.two" }
    };
}
