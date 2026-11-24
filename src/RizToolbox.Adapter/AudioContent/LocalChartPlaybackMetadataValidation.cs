using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal static class LocalChartPlaybackMetadataValidation
{
    internal static LocalChartPlaybackMetadata? CopyOptional(LocalChartPlaybackMetadata? value)
    {
        if (value is null) return null;
        static bool Text(string? text, bool empty = false) => text is not null && text.Length <= 256
            && (empty || !string.IsNullOrWhiteSpace(text)) && !text.Any(char.IsControl);
        if (value.DifficultyIndex is < 0 or > 2 || value.MaxHit is < 1 or > 1_000_000
            || value.MaxScore is < 1 or > 1_000_000_000 || !Text(value.DifficultyText) || !Text(value.Designer)
            || !Text(value.Illustrator, true) || !float.IsFinite(value.DifficultyRating) || value.DifficultyRating is < 0 or > 1000
            || value.ThemeColor is null || new[] { value.ThemeColor.R, value.ThemeColor.G, value.ThemeColor.B, value.ThemeColor.A }
                .Any(component => !float.IsFinite(component) || component is < 0 or > 1))
            throw new InvalidDataException("LOCAL playback metadata requires difficulty 0..2, positive bounded authored counters, bounded credits and finite normalized color.");
        return value with { ThemeColor = value.ThemeColor with { } };
    }

    internal static bool HasNotes(ValidatedContentLease resource) =>
        resource.Payload is IParsedLocalChart { Source: { Notes: > 0, Lines: > 0 } };

    internal static string? GetPlaybackLineError(ValidatedContentLease resource) =>
        (resource.Payload as IParsedLocalChart)?.Source.PlaybackLineError;

    internal static (LocalNativeMetadataDescriptor Descriptor, int JudgeIndex) Require(LocalPlayRequest request)
    {
        var selection = request.Selection;
        // The snapshot must be the exact chart of the retained selected revision.
        if (selection.Song.Charts.Count(chart => ReferenceEquals(chart, selection.Chart)) != 1
            || request.Resources.Count(resource => ReferenceEquals(resource.Snapshot, selection.Chart.Content)) != 1)
            throw new InvalidDataException("LOCAL playback metadata does not belong to the retained selected chart revision.");
        var chart = request.Resources.Single(resource => ReferenceEquals(resource.Snapshot, selection.Chart.Content));
        if (GetPlaybackLineError(chart) is { } lineError)
            throw new InvalidDataException(lineError);
        var metadata = CopyOptional(selection.Chart.Playback)
            ?? throw new InvalidDataException("This chart has no provider-authored playback metadata; CPU registration alone cannot launch it.");
        if (!HasNotes(chart))
            throw new InvalidDataException("Native LOCAL playback requires the selected retained nonempty parsed chart.");
        return (NativeLocalMetadataFactory.ValidateAndCopy(new(selection.Song.Song, selection.Song.Revision,
            selection.Chart.Id, selection.Song.Title, selection.Song.Artist, selection.Chart.Name,
            metadata.DifficultyText, metadata.Designer, new(metadata.ThemeColor.R, metadata.ThemeColor.G, metadata.ThemeColor.B, metadata.ThemeColor.A),
            metadata.MaxHit, metadata.MaxScore, metadata.DifficultyRating, metadata.Illustrator)), metadata.DifficultyIndex);
    }
}
