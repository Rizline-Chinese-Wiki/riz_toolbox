namespace RizToolbox.Sdk;

/// <summary>Chart coordinates require the existing chart/progress converter. The three seconds domains
/// have separate origins and cannot be exchanged merely because they share a unit.</summary>
public enum GameplayClockDomain { Unknown, ChartCoordinates, GameProgressSeconds, AudioDspSeconds, HostMonotonicSeconds }
public enum GameplayClockObservationState { Unavailable, WarmingUp, ObservedInterval, Expired }

/// <summary>Changing policy begins a new segment. A conversion request may shorten its age limit,
/// but cannot enlarge this policy's lifetime, residual tolerance or observed sample window.</summary>
public sealed record GameplayClockObservationPolicy(double MaxSampleGapSeconds = .5, double MaxAgeSeconds = .5,
    double AbsoluteResidualToleranceSeconds = .002, double MinimumPositiveIntervalSeconds = .000001)
{
    public static GameplayClockObservationPolicy Default { get; } = new();
}

/// <summary>Game/DSP readings were taken somewhere inside the host capture interval. HostMonotonicSeconds
/// is that interval's midpoint, rather than an assertion of simultaneous game and audio reads.</summary>
public sealed record GameplayObservedClockSample(long Revision, double CaptureStartedMonotonicSeconds,
    double CaptureFinishedMonotonicSeconds, double GameProgressSeconds, double AudioDspSeconds,
    double HostMonotonicSeconds);

/// <summary>Three consecutive actual samples describe a recent interpolation model, never a future
/// calibration or measured audio latency. CaptureSkewSeconds and ObservedResidualGameSeconds describe
/// capture envelopes and the middle sample's actual fit residual; unsampled nonlinear behavior is unproved.</summary>
public sealed record GameplayClockObservedInterval(GameplayClockObservationState State, long SegmentId,
    long ControllerGeneration, string? ObservedRunId, GameplayClockObservationPolicy Policy,
    IReadOnlyList<GameplayObservedClockSample> Samples, string Evidence)
{
    public double? GameSecondsPerDspSecond { get; init; }
    public double? GameSecondsPerHostSecond { get; init; }
    public double? GameDspOffsetSeconds { get; init; }
    public double? GameHostOffsetSeconds { get; init; }
    public double CaptureSkewSeconds { get; init; }
    public double ObservedResidualGameSeconds { get; init; }
    public double EstimatedGameUncertaintySeconds { get; init; }
    public double? ExpiresAtHostMonotonicSeconds { get; init; }
}

public sealed partial record GameplayClockSnapshot
{
    public GameplayClockObservedInterval? ObservedInterval { get; init; }
}
public sealed partial record GameplayClockMappingSnapshot
{
    public GameplayClockObservedInterval? ObservedInterval { get; init; }
}

/// <summary>Targets an observed segment and a value inside its current sample endpoints. MaxAgeSeconds
/// is additionally capped by the segment policy. ChartCoordinates are refused by ConvertObserved.</summary>
public sealed record GameplayObservedClockConversionRequest(long ControllerGeneration, string ObservedRunId,
    long SegmentId, double Value, GameplayClockDomain SourceDomain, GameplayClockDomain TargetDomain,
    double MaxAgeSeconds = .5);

/// <summary>EstimatedUncertaintySeconds is expressed in TargetDomain seconds and includes capture skew
/// and observed fit residual. It is not a guaranteed bound on unsampled behavior or a latency measurement.</summary>
public sealed record GameplayObservedClockConversion(GameplayObservedClockConversionRequest Request,
    double ConvertedValue, GameplayClockObservedInterval Interval, double EstimatedUncertaintySeconds,
    string Evidence, bool RuntimeVerified = false);
