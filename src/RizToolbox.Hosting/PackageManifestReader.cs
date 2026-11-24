using System.Text.Json;
using System.Text.RegularExpressions;
using RizToolbox.Sdk;

namespace RizToolbox.Hosting;

internal static class PackageManifestReader
{
    public static PluginPackageMetadata Read(string path)
    {
        if (!File.Exists(path)) throw new PackageException("manifest-missing", "Package requires plugin.json.");
        if (new FileInfo(path).Length > 65536) throw new PackageException("manifest-invalid", "plugin.json exceeds 64 KiB.");
        using var json = JsonDocument.Parse(File.ReadAllBytes(path), new() { MaxDepth = 16 });
        var root = json.RootElement;
        CheckProperties(root, "id", "version", "entryAssembly", "entryType", "minSdk", "maxSdkExclusive", "dependencies", "requiredCapabilities", "optionalDependencies", "conflicts", "loadAfter");
        string id = Id(Required(root, "id"));
        var version = ParseVersion(Required(root, "version"));
        var minSdk = ParseVersion(Required(root, "minSdk"));
        var maxSdk = OptionalVersion(root, "maxSdkExclusive");
        if (maxSdk is not null && maxSdk <= minSdk) Invalid("SDK range is empty.");
        if (SdkInfo.Version < minSdk || (maxSdk is not null && SdkInfo.Version >= maxSdk))
            throw new PackageException("sdk-incompatible", $"Package does not support SDK {SdkInfo.Version}.");
        var dependencies = new List<PluginDependency>();
        if (root.TryGetProperty("dependencies", out var deps))
        {
            if (deps.ValueKind != JsonValueKind.Array) Invalid("dependencies must be an array.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var dependency in deps.EnumerateArray())
            {
                CheckProperties(dependency, "id", "minVersion", "maxVersionExclusive");
                var depId = Id(Required(dependency, "id"));
                if (!seen.Add(depId)) Invalid($"Dependency '{depId}' is repeated.");
                if (depId == id) Invalid("A package cannot depend on itself.");
                var minimum = ParseVersion(Required(dependency, "minVersion"));
                var maximum = OptionalVersion(dependency, "maxVersionExclusive");
                if (maximum is not null && maximum <= minimum) Invalid($"Dependency '{depId}' has an empty range.");
                dependencies.Add(new(depId, minimum, maximum));
            }
        }
        var capabilities = new List<string>();
        var optional = ReadOptionalDependencies(root, id);
        if (optional.Any(x => dependencies.Any(hard => hard.Id == x.Id))) Invalid("A dependency cannot be both required and optional.");
        var conflicts = ReadConflicts(root, id);
        var loadAfter = new List<string>();
        if (root.TryGetProperty("loadAfter", out var after))
        {
            if (after.ValueKind != JsonValueKind.Array) Invalid("loadAfter must be an array.");
            foreach (var reference in after.EnumerateArray())
            {
                var target = Id(Text(reference, "loadAfter"));
                if (target == id || loadAfter.Contains(target, StringComparer.Ordinal)) Invalid("loadAfter reference is repeated or refers to itself.");
                loadAfter.Add(target);
            }
        }
        if (root.TryGetProperty("requiredCapabilities", out var caps))
        {
            if (caps.ValueKind != JsonValueKind.Array) Invalid("requiredCapabilities must be an array.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var capability in caps.EnumerateArray())
            {
                var value = Text(capability, "capability");
                if (!seen.Add(value)) Invalid($"Capability '{value}' is repeated.");
                capabilities.Add(value);
            }
        }
        return new(new PluginManifest(id, version, minSdk, maxSdk, Array.AsReadOnly(dependencies.ToArray()),
            Array.AsReadOnly(capabilities.ToArray())) { OptionalDependencies = Array.AsReadOnly(optional), Conflicts = Array.AsReadOnly(conflicts), LoadAfter = Array.AsReadOnly(loadAfter.ToArray()) },
            Required(root, "entryAssembly"), Required(root, "entryType"));
    }

    public static bool Equivalent(PluginManifest? actual, PluginManifest expected)
    {
        if (actual is null || actual.Id != expected.Id || actual.Version != expected.Version ||
            actual.MinSdk != expected.MinSdk || actual.MaxSdkExclusive != expected.MaxSdkExclusive) return false;
        var actualDeps = actual.Dependencies ?? Array.Empty<PluginDependency>();
        var expectedDeps = expected.Dependencies ?? Array.Empty<PluginDependency>();
        if (actualDeps.Count != expectedDeps.Count || actualDeps.Any(x => x is null)) return false;
        if (!actualDeps.OrderBy(x => x.Id, StringComparer.Ordinal).SequenceEqual(expectedDeps.OrderBy(x => x.Id, StringComparer.Ordinal))) return false;
        if (!SameById(actual.OptionalDependencies, expected.OptionalDependencies, x => x.Id) ||
            !SameById(actual.Conflicts, expected.Conflicts, x => x.Id) ||
            !(actual.LoadAfter ?? Array.Empty<string>()).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual((expected.LoadAfter ?? Array.Empty<string>()).OrderBy(x => x, StringComparer.Ordinal))) return false;
        var actualCaps = actual.RequiredCapabilities ?? Array.Empty<string>();
        var expectedCaps = expected.RequiredCapabilities ?? Array.Empty<string>();
        return actualCaps.Count == expectedCaps.Count && actualCaps.OrderBy(x => x, StringComparer.Ordinal)
            .SequenceEqual(expectedCaps.OrderBy(x => x, StringComparer.Ordinal));
    }

    private static bool SameById<T>(IReadOnlyList<T>? actual, IReadOnlyList<T>? expected, Func<T, string> key)
        => !(actual ?? Array.Empty<T>()).Any(x => x is null) && (actual ?? Array.Empty<T>()).OrderBy(key, StringComparer.Ordinal)
            .SequenceEqual((expected ?? Array.Empty<T>()).OrderBy(key, StringComparer.Ordinal));

    private static PluginDependency[] ReadOptionalDependencies(JsonElement root, string owner)
    {
        var result = new List<PluginDependency>();
        if (!root.TryGetProperty("optionalDependencies", out var values)) return result.ToArray();
        if (values.ValueKind != JsonValueKind.Array) Invalid("optionalDependencies must be an array.");
        foreach (var value in values.EnumerateArray())
        {
            CheckProperties(value, "id", "minVersion", "maxVersionExclusive");
            var id = Id(Required(value, "id")); var min = ParseVersion(Required(value, "minVersion")); var max = OptionalVersion(value, "maxVersionExclusive");
            if (id == owner || result.Any(x => x.Id == id) || (max is not null && max <= min)) Invalid("Optional dependency is repeated, refers to itself or has an empty range.");
            result.Add(new(id, min, max));
        }
        return result.ToArray();
    }

    private static PluginConflict[] ReadConflicts(JsonElement root, string owner)
    {
        var result = new List<PluginConflict>();
        if (!root.TryGetProperty("conflicts", out var values)) return result.ToArray();
        if (values.ValueKind != JsonValueKind.Array) Invalid("conflicts must be an array.");
        foreach (var value in values.EnumerateArray())
        {
            CheckProperties(value, "id", "minVersion", "maxVersionExclusive");
            var id = Id(Required(value, "id")); var min = OptionalVersion(value, "minVersion"); var max = OptionalVersion(value, "maxVersionExclusive");
            if (id == owner || result.Any(x => x.Id == id) || (min is not null && max is not null && max <= min)) Invalid("Conflict is repeated, refers to itself or has an empty range.");
            result.Add(new(id, min, max));
        }
        return result.ToArray();
    }

    private static void CheckProperties(JsonElement element, params string[] allowed)
    {
        if (element.ValueKind != JsonValueKind.Object) Invalid("Manifest values must be JSON objects.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name)) Invalid($"Property '{property.Name}' is repeated.");
            if (!allowed.Contains(property.Name, StringComparer.Ordinal)) Invalid($"Unknown property '{property.Name}'.");
        }
    }

    private static string Required(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) ? Text(value, name) : throw new PackageException("manifest-invalid", $"Required property '{name}' is missing.");
    private static string Text(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())) Invalid($"'{name}' must be a nonempty string.");
        return value.GetString()!;
    }
    private static string Id(string value)
    {
        if (!Regex.IsMatch(value, "^[a-z0-9][a-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)) Invalid("Plugin IDs must use lowercase ASCII letters, digits, dots, underscores or hyphens (maximum 128 characters).");
        return value;
    }
    private static Version ParseVersion(string value)
        => Version.TryParse(value, out var version) ? version : throw new PackageException("manifest-invalid", $"Invalid version '{value}'.");
    private static Version? OptionalVersion(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) ? ParseVersion(Text(value, name)) : null;
    private static void Invalid(string message) => throw new PackageException("manifest-invalid", message);
}
