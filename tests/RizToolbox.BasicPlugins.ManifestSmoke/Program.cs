using System.Text.Json;
using RizToolbox.BasicPlugins;
using RizToolbox.Sdk;

var entries = new IRizPlugin[] { new QuickPanelPlugin(), new SongScoresPlugin(), new ThemePlugin() };
foreach (var plugin in entries)
{
    var type = plugin.GetType();
    var manifest = plugin.Manifest;
    var path = Path.Combine(AppContext.BaseDirectory, "manifests", manifest.Id, "plugin.json");
    using var data = JsonDocument.Parse(File.ReadAllText(path));
    var json = data.RootElement;
    Check(json.GetProperty("id").GetString() == manifest.Id, path, "identifier");
    Check(json.GetProperty("entryType").GetString() == type.FullName, path, "entry type");
    Check(json.GetProperty("entryAssembly").GetString() == type.Assembly.GetName().Name + ".dll", path, "entry assembly");
    Check(manifest.Version == ReadVersion("version") && manifest.MinSdk == ReadVersion("minSdk")
        && manifest.MaxSdkExclusive == ReadVersion("maxSdkExclusive"), path, "versions");
    Check((manifest.Dependencies?.Count ?? 0) == 0 && json.GetProperty("dependencies").GetArrayLength() == 0,
        path, "dependencies");
    Check((manifest.RequiredCapabilities ?? Array.Empty<string>()).OrderBy(x => x, StringComparer.Ordinal)
        .SequenceEqual(json.GetProperty("requiredCapabilities").EnumerateArray().Select(x => x.GetString()).OrderBy(x => x, StringComparer.Ordinal)), path, "capabilities");
    Check(type.Assembly.GetReferencedAssemblies().All(x => x.Name is "RizToolbox.Sdk" || x.Name?.StartsWith("System.", StringComparison.Ordinal) == true), path, "managed-only references");
    Console.WriteLine($"PASS {manifest.Id} manifest and managed references");
    Version ReadVersion(string name) => Version.Parse(json.GetProperty(name).GetString()!);
}
return 0;

static void Check(bool value, string path, string field)
{
    if (!value) throw new InvalidOperationException($"Unexpected {field} in {Path.GetFileName(Path.GetDirectoryName(path))}.");
}
