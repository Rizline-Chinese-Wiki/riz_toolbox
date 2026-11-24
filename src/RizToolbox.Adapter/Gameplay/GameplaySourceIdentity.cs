namespace RizToolbox.Adapter.Gameplay;

/// <summary>Private identity of the captured playback graph. The descriptor's native
/// source epoch is independent of the service's controller generation. Neither the
/// addresses nor this value grant playback authority.</summary>
internal readonly record struct GameplaySourceIdentity(long Progress, long Pause, long Player,
    long AudioSource, long Clip, NativePlaybackRateSourceDescriptor? NativeDescriptor);
