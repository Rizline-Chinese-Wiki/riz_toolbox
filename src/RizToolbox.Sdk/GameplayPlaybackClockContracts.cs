namespace RizToolbox.Sdk;

public enum GameplayPlaybackPhase { Paused, Playing, Scheduled }

/// <summary>A current sample of the authority-bound playback policy. MediaSeconds is the
/// MUGMusicPlayer media domain. DspSeconds is the separate DspTimeService domain sampled by
/// that source, not an alias for AudioSettings.dspTime or host monotonic seconds.
/// ClockRevision is scoped by the enclosing snapshot's run and controller generation.
/// Scheduled media before the start follows the stock unscaled delay.</summary>
public sealed record GameplayPlaybackClockSample(long ClockRevision, GameplayPlaybackPhase Phase,
    double DspSeconds, double MediaSeconds, double PlaybackRate);

/// <summary>Independent optional playback information. An unavailable reading has no sample.
/// This value carries observation data only and grants no native call or write authority.</summary>
public sealed record GameplayPlaybackClockReading(bool Available, GameplayPlaybackClockSample? Sample,
    string Evidence, OperationError? Error = null);

public sealed partial record GameplayClockSnapshot
{
    public GameplayPlaybackClockReading Playback { get; init; } = new(false, null,
        "No authority-bound native playback reader supplied a sample.",
        new(ErrorCode.ObservationUnavailable, "The native playback reader is unavailable.",
            GameplayCapabilityIds.Clock, "gameplay.clock/playback"));
}
