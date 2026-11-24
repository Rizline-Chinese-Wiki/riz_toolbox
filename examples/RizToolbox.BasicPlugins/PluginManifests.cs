using RizToolbox.Sdk;

namespace RizToolbox.BasicPlugins;

internal static class PluginManifests
{
    internal static PluginManifest SongScores { get; } = Create("basic.song-scores",
        CapabilityIds.Catalog, CapabilityIds.Scores, UiInputCapabilityIds.Windows, GameCapabilityIds.Scenes);

    internal static PluginManifest Theme { get; } = Create("basic.theme",
        GameCapabilityIds.Resources, GameCapabilityIds.Scenes);

    internal static PluginManifest QuickPanel { get; } = Create("basic.quick-panel",
        UiInputCapabilityIds.Windows, UiInputCapabilityIds.Actions, AudioContentCapabilityIds.Playback,
        StorageCapabilityIds.Storage, GameCapabilityIds.Scenes);

    private static PluginManifest Create(string id, params string[] capabilities)
        => new(id, new(0, 3, 0), new(1, 2, 0), new(2, 0, 0),
            RequiredCapabilities: Array.AsReadOnly(capabilities));
}
