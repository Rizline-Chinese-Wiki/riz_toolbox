using System.Runtime.Loader;

namespace Doorstop;

/// <summary>Unity Doorstop's documented CoreCLR entry point.</summary>
public static class Entrypoint
{
    public static void Start()
    {
        var directory = Path.GetDirectoryName(typeof(Entrypoint).Assembly.Location)!;
        AssemblyLoadContext.Default.Resolving += (_, name) => {
            var file = Path.Combine(directory, name.Name + ".dll");
            if(File.Exists(file))return AssemblyLoadContext.Default.LoadFromAssemblyPath(file);
            return null;
        };
        // Keep the entry method independent of SDK/Core types until resolution is installed.
        typeof(Entrypoint).Assembly.GetType("RizToolbox.Loader.Bootstrap")!
            .GetMethod("Start")!.Invoke(null, null);
    }
}
