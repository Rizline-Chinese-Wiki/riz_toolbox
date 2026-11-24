namespace RizToolbox.Sdk;

public static class BackgroundPlayCapability
{
    public const string Id = "riz.window.background-play";
}

/// <summary>Opt in to background chart playback on the current Windows game window.
/// The request belongs to the calling plugin scope and ends when the scope expires.</summary>
public interface IBackgroundPlayService
{
    Result EnableForScope();
}
