using RizToolbox.Adapter.Judgements;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>The process-owned playback host exposes Seek through the same exact
/// source and run as rate control. Preparation still validates the requested
/// target's complete audio, progress, judge and renderer work before writing.</summary>
internal interface INativePlaybackSeekProvider
{
    NativePlaybackRateProviderProbe ProbeSeek();
    GameEventState GameState { get; }
    INativePlaybackSeekSource CaptureSeek(NativePlaybackRateOrigin origin);
    JudgementModule.SeekBackendAccess CaptureSeekJudgementBackend(GameEventState.PlaybackRunAssociation run);
}

internal interface INativePlaybackSeekSource : IDisposable
{
    NativePlaybackBinding Binding { get; }
    NativePlaybackClock Clock { get; }
    GameEventState.PlaybackRunAssociation Run { get; }
    void VerifyAuthority();
    NativePlaybackRateSample Read();
    INativePlaybackTimeWrite PrepareTimeWrite();
}

/// <summary>A real continuous SetTime writer. A successful native write retains
/// its continuation even if a later API read fails; only that private continuation
/// may restore the old media position. Dispose never restores automatically.</summary>
internal interface INativePlaybackTimeWrite : IDisposable
{
    void VerifyCurrent();
    void Write(float mediaSeconds);
    void RestoreOriginal();
}
