namespace RizToolbox.Adapter;

internal sealed partial class GameEventState
{
    // The caller runs after the observer's Poll has returned. The existing
    // capture still rejects reentry during the actual Controller mutation.
    internal PlaybackRunAssociation CaptureCurrentPlaybackRun()
        => CapturePlaybackRun(_information, _controller);
}
