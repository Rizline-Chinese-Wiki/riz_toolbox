using System.Text.Json;

namespace RizToolbox.Adapter;

public sealed record TypeBinding(string Assembly, string Namespace, string Name);
public sealed record AdapterBuildProfile(string Id, string GameAssemblySha256, string GameVersion,
    string UnityVersion, string TickType, string TickMethod, IReadOnlyDictionary<string, TypeBinding> Types);

public static class BuildProfiles
{
    public static AdapterBuildProfile Select(string directory, string fingerprint)
    {
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("Adapter profiles directory is missing.");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var profiles = Directory.GetFiles(directory, "*.json").OrderBy(x => x, StringComparer.Ordinal)
            .Select(file => JsonSerializer.Deserialize<AdapterBuildProfile>(File.ReadAllText(file), options)
                ?? throw new InvalidDataException($"Empty adapter profile: {Path.GetFileName(file)}")).ToArray();
        if (profiles.Any(p => string.IsNullOrWhiteSpace(p.Id) || p.GameAssemblySha256?.Length != 64 ||
            !p.GameAssemblySha256.All(Uri.IsHexDigit) || string.IsNullOrWhiteSpace(p.TickType) ||
            string.IsNullOrWhiteSpace(p.TickMethod) || string.IsNullOrWhiteSpace(p.GameVersion) || string.IsNullOrWhiteSpace(p.UnityVersion) ||
            p.Types is null || !p.Types.ContainsKey(p.TickType) || p.Types.Values.Any(t => t is null ||
                string.IsNullOrWhiteSpace(t.Assembly) || t.Namespace is null || string.IsNullOrWhiteSpace(t.Name))))
            throw new InvalidDataException("Invalid adapter profile.");
        if (profiles.GroupBy(p => p.GameAssemblySha256, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw new InvalidDataException("Duplicate adapter build fingerprints.");
        return profiles.SingleOrDefault(p => string.Equals(p.GameAssemblySha256, fingerprint, StringComparison.OrdinalIgnoreCase))
            ?? throw new NotSupportedException($"No validated adapter profile for game build {fingerprint}.");
    }
}
