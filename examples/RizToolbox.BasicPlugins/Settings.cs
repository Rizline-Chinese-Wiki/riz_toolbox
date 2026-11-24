using System.Text.RegularExpressions;
using RizToolbox.Sdk;

namespace RizToolbox.BasicPlugins;

public sealed record SongScoresSettings
{
    public string Filter { get; init; } = "";
    public int Page { get; init; }
    public int RowsPerPage { get; init; } = 8;
}

public sealed record ThemeSettings
{
    public bool Enabled { get; init; } = true;
    public ResourceColor Color { get; init; } = new(.15f, .95f, 1f, 1f);
    public int Priority { get; init; } = 10;
    public bool IncludeMenuButtons { get; init; } = true;
    public string[] TargetKeys { get; init; } = Array.Empty<string>();
}

public sealed record QuickPanelSettings
{
    public bool Visible { get; init; } = true;
    public string WavPath { get; init; } = "audio/owned.wav";
    public float Volume { get; init; } = .2f;
    public bool Loop { get; init; }
}

internal static class SettingsValidation
{
    internal static string? SongScores(SongScoresSettings settings)
        => settings is null || settings.Filter is null || settings.Filter.Length > 128 ||
            settings.Filter.Any(char.IsControl) || settings.Page is < 0 or > 19999 ||
            settings.RowsPerPage is < 1 or > 10
                ? "Filter must contain at most 128 printable characters, page must be 0..19999, and rowsPerPage must be 1..10."
                : null;

    internal static string? Theme(ThemeSettings settings)
    {
        if (settings is null || settings.Color is null ||
            !ValidChannel(settings.Color.R) || !ValidChannel(settings.Color.G) ||
            !ValidChannel(settings.Color.B) || !ValidChannel(settings.Color.A))
            return "Theme color requires four finite RGBA channels in [0,1].";
        if (settings.Priority is < -1000 or > 1000) return "Theme priority must be -1000..1000.";
        if (settings.TargetKeys is null || settings.TargetKeys.Length > 16 ||
            settings.TargetKeys.Any(key => string.IsNullOrWhiteSpace(key) || key.Length > 512 ||
                key.Any(char.IsControl) ||
                (!key.StartsWith("game.ui/", StringComparison.Ordinal) &&
                    !key.StartsWith("game.menu/", StringComparison.Ordinal))))
            return "TargetKeys must contain at most 16 exact declared game.ui/ or game.menu/ color keys.";
        if (settings.TargetKeys.Distinct(StringComparer.Ordinal).Count() != settings.TargetKeys.Length)
            return "TargetKeys must be unique.";
        return null;
    }

    internal static string? QuickPanel(QuickPanelSettings settings)
    {
        if (settings is null || !ValidChannel(settings.Volume)) return "WAV volume must be finite and in [0,1].";
        var path = settings.WavPath;
        if (string.IsNullOrWhiteSpace(path) || path.Length > 256 ||
            !path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            return "WavPath must be a relative .wav name in this plugin's Data storage.";
        foreach (var segment in path.Split('/'))
        {
            if (!Regex.IsMatch(segment, "^[a-zA-Z0-9][a-zA-Z0-9._-]{0,63}$") || segment.EndsWith('.'))
                return "WavPath must use relative ASCII storage segments separated by '/', without traversal or drive names.";
            var device = segment.Split('.')[0].ToUpperInvariant();
            if (device is "CON" or "PRN" or "AUX" or "NUL" || Regex.IsMatch(device, "^(COM|LPT)[0-9]$"))
                return "WavPath contains a reserved storage segment.";
        }
        return null;
    }

    private static bool ValidChannel(float value) => float.IsFinite(value) && value is >= 0 and <= 1;
}
