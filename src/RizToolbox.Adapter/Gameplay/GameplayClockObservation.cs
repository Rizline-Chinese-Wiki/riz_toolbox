using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>Bounded, request-driven observations. No sampling thread, control dispatch or predicted future clock.</summary>
internal sealed class GameplayClockObservation
{
    private readonly List<GameplayObservedClockSample> _samples = new(3);
    private long _segment, _generation;
    private string? _run, _provenance;
    private string? _mappingProvenance;
    private GameplayClockSourceIdentity? _sourceIdentity;
    private bool? _playing, _paused;
    private double? _offset, _delay;
    private double _lastHostSeen = double.NegativeInfinity;
    private GameplayClockObservationPolicy _policy = GameplayClockObservationPolicy.Default;
    private GameplayClockObservedInterval _current = new(GameplayClockObservationState.Unavailable, 0, 0, null,
        GameplayClockObservationPolicy.Default, Array.Empty<GameplayObservedClockSample>(), "No clock samples have been observed.");

    internal static bool ValidPolicy(GameplayClockObservationPolicy policy) =>
        double.IsFinite(policy.MaxSampleGapSeconds) && policy.MaxSampleGapSeconds > 0
        && double.IsFinite(policy.MaxAgeSeconds) && policy.MaxAgeSeconds > 0
        && double.IsFinite(policy.AbsoluteResidualToleranceSeconds) && policy.AbsoluteResidualToleranceSeconds >= 0
        && double.IsFinite(policy.MinimumPositiveIntervalSeconds) && policy.MinimumPositiveIntervalSeconds > 0
        && policy.MinimumPositiveIntervalSeconds < policy.MaxSampleGapSeconds;

    internal static bool ObservedDomain(GameplayClockDomain domain) => domain is GameplayClockDomain.GameProgressSeconds
        or GameplayClockDomain.AudioDspSeconds or GameplayClockDomain.HostMonotonicSeconds;

    private static double? Seconds(GameplayClockReading? reading) => reading is
        { Available: true, Unit: GameplayClockUnit.Seconds, Value: double value } && double.IsFinite(value) ? value : null;
    private static string MappingProvenance(GameplayClockMappingSnapshot? mapping) => mapping?.BuildFingerprint
        + "/" + mapping?.ProgressTimeInSeconds.Source + "/" + mapping?.ProgressTimeInSeconds.UnitEvidence;

    internal void Reset(string reason)
    {
        _samples.Clear();
        _current = Snapshot(GameplayClockObservationState.Unavailable, reason, newSegment: true);
    }

    private GameplayClockObservedInterval Snapshot(GameplayClockObservationState state, string reason, bool newSegment = false)
        => new(state, newSegment ? ++_segment : _segment, _generation, _run, _policy,
            Array.AsReadOnly(_samples.ToArray()), reason)
        {
            ExpiresAtHostMonotonicSeconds = _samples.Count == 0 ? null
                : _samples[^1].CaptureFinishedMonotonicSeconds + _policy.MaxAgeSeconds
        };

    private void Begin(string reason)
    {
        _samples.Clear();
        _current = Snapshot(GameplayClockObservationState.WarmingUp, reason, newSegment: true);
    }

    internal GameplayClockObservedInterval Observe(long revision, long generation, string? run,
        GameplayClockMappingSnapshot? mapping, GameplayClockReading dsp, double start, double end,
        GameplayClockObservationPolicy policy, GameplayClockSourceIdentity? sourceIdentity)
    {
        var mappingProvenance = MappingProvenance(mapping);
        var provenance = mappingProvenance + "/" + dsp.Source;
        var offset = Seconds(mapping?.AppliedOffsetSeconds); var delay = Seconds(mapping?.ChartDelaySeconds);
        var changed = _generation != generation || _run != run || _playing != mapping?.Playing || _paused != mapping?.Paused
            || _policy != policy || _provenance != provenance || _offset != offset || _delay != delay || _sourceIdentity != sourceIdentity;
        _generation = generation; _run = run; _playing = mapping?.Playing; _paused = mapping?.Paused;
        _policy = policy; _provenance = provenance; _mappingProvenance = mappingProvenance;
        _offset = offset; _delay = delay; _sourceIdentity = sourceIdentity;
        if (changed) Begin("Run, controller, playback state, source provenance, offset or observation policy changed.");
        if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end < start || start < _lastHostSeen)
        {
            Reset("Host capture interval is invalid or moved backwards."); return _current;
        }
        _lastHostSeen = end;
        if (string.IsNullOrWhiteSpace(run) || generation <= 0 || _playing != true || _paused != false)
        {
            Reset("A definite playing, unpaused observed run is required; paused game time has no inverse mapping."); return _current;
        }
        var game = Seconds(mapping?.ProgressTimeInSeconds); var audio = Seconds(dsp);
        if (game is null || audio is null)
        {
            Reset("Progress and DSP readings must both be available, finite seconds in their explicit domains."); return _current;
        }
        if (_current.State == GameplayClockObservationState.Expired || _samples.Count > 0
            && end - _samples[^1].CaptureFinishedMonotonicSeconds > _policy.MaxAgeSeconds)
            Begin("The previous observed interval expired; new samples cannot revive it.");
        var sample = new GameplayObservedClockSample(revision, start, end, game.Value, audio.Value, start + (end - start) / 2);
        if (_samples.Count > 0)
        {
            var previous = _samples[^1];
            if (end - previous.CaptureStartedMonotonicSeconds > policy.MaxSampleGapSeconds)
                Begin("The interval between actual captures exceeded MaxSampleGapSeconds.");
            else if (start - previous.CaptureFinishedMonotonicSeconds < policy.MinimumPositiveIntervalSeconds
                || sample.HostMonotonicSeconds - previous.HostMonotonicSeconds < policy.MinimumPositiveIntervalSeconds
                || sample.AudioDspSeconds - previous.AudioDspSeconds < policy.MinimumPositiveIntervalSeconds
                || sample.GameProgressSeconds - previous.GameProgressSeconds < policy.MinimumPositiveIntervalSeconds)
                Begin("A clock stalled, moved backwards, or provided no resolvable positive interval.");
        }
        if (_samples.Count == 3) _samples.RemoveAt(0);
        _samples.Add(sample);
        if (_samples.Count < 3)
            return _current = Snapshot(GameplayClockObservationState.WarmingUp, "Three consecutive positive clock samples are required.");

        var a = _samples[0]; var b = _samples[1]; var c = _samples[2];
        var gameDelta = c.GameProgressSeconds - a.GameProgressSeconds;
        var dspRate = gameDelta / (c.AudioDspSeconds - a.AudioDspSeconds);
        var hostRate = gameDelta / (c.HostMonotonicSeconds - a.HostMonotonicSeconds);
        var dspOffset = a.GameProgressSeconds - dspRate * a.AudioDspSeconds;
        var hostOffset = a.GameProgressSeconds - hostRate * a.HostMonotonicSeconds;
        var skew = _samples.Sum(s => s.CaptureFinishedMonotonicSeconds - s.CaptureStartedMonotonicSeconds);
        var residual = Math.Max(Math.Abs(b.GameProgressSeconds - (a.GameProgressSeconds + (b.AudioDspSeconds - a.AudioDspSeconds) * dspRate)),
            Math.Abs(b.GameProgressSeconds - (a.GameProgressSeconds + (b.HostMonotonicSeconds - a.HostMonotonicSeconds) * hostRate)));
        var captureUncertainty = Math.Max(dspRate, hostRate) * skew;
        if (!double.IsFinite(dspRate) || !double.IsFinite(hostRate) || !double.IsFinite(dspOffset)
            || !double.IsFinite(hostOffset) || !double.IsFinite(residual + captureUncertainty)
            || residual > policy.AbsoluteResidualToleranceSeconds + captureUncertainty)
        {
            Begin("Observed clocks do not fit one interval within its capture envelope and absolute residual tolerance; no control action is inferred.");
            _samples.Add(sample);
            return _current = Snapshot(GameplayClockObservationState.WarmingUp, _current.Evidence);
        }
        return _current = Snapshot(GameplayClockObservationState.ObservedInterval,
            "Linear interpolation of three actual same-run samples only. Capture skew and actual fit residual are uncertainty estimates, not latency or a guarantee of unsampled behavior.") with
        {
            GameSecondsPerDspSecond = dspRate, GameSecondsPerHostSecond = hostRate,
            GameDspOffsetSeconds = dspOffset, GameHostOffsetSeconds = hostOffset,
            CaptureSkewSeconds = skew, ObservedResidualGameSeconds = residual,
            EstimatedGameUncertaintySeconds = residual + captureUncertainty
        };
    }

    internal GameplayClockObservedInterval Inspect(double now)
    {
        if (!double.IsFinite(now) || now < 0 || now < _lastHostSeen)
        {
            Reset("Host monotonic time is unavailable or moved backwards while checking the observed interval."); return _current;
        }
        _lastHostSeen = now;
        if (_samples.Count > 0 && now > _samples[^1].CaptureFinishedMonotonicSeconds + _policy.MaxAgeSeconds)
            _current = Snapshot(GameplayClockObservationState.Expired, "The segment policy's MaxAgeSeconds elapsed; this model cannot be revived by a request.");
        return _current;
    }

    internal Result<GameplayObservedClockConversion> Convert(GameplayObservedClockConversionRequest request,
        long generation, string? run, bool playing, bool paused, double now,
        GameplayClockMappingSnapshot? currentMapping, GameplayClockSourceIdentity? sourceIdentity)
    {
        if (generation != _generation || run != _run || !playing || paused
            || _playing is bool sampledPlaying && playing != sampledPlaying
            || _paused is bool sampledPaused && paused != sampledPaused)
        {
            Reset("Current run, controller or playback state differs from the observed segment.");
            return Fail(ErrorCode.TargetExpired, _current.Evidence);
        }
        var interval = Inspect(now);
        if (interval.State != GameplayClockObservationState.ObservedInterval)
            return Fail(ErrorCode.ObservationUnavailable, interval.State + ": " + interval.Evidence);
        if (request.SegmentId != interval.SegmentId)
            return Fail(ErrorCode.TargetExpired, "The requested observed segment has ended.");
        var a = interval.Samples[0]; var b = interval.Samples[^1];
        if (now - b.CaptureFinishedMonotonicSeconds > Math.Min(request.MaxAgeSeconds, interval.Policy.MaxAgeSeconds))
            return Fail(ErrorCode.ObservationUnavailable, "The interval exceeds the request's stricter maximum age.");
        var currentGame = Seconds(currentMapping?.ProgressTimeInSeconds);
        if (currentGame is null || currentMapping?.Playing != true || currentMapping.Paused != false)
        {
            Reset("Current progress seconds or playback mapping cannot be verified without extending the observed history.");
            return Fail(ErrorCode.ObservationUnavailable, _current.Evidence);
        }
        if (_sourceIdentity != sourceIdentity || _mappingProvenance != MappingProvenance(currentMapping)
            || _offset != Seconds(currentMapping.AppliedOffsetSeconds) || _delay != Seconds(currentMapping.ChartDelaySeconds)
            || currentGame.Value < b.GameProgressSeconds
            || currentGame.Value - b.GameProgressSeconds > Math.Max(0, now - b.HostMonotonicSeconds)
                * interval.GameSecondsPerHostSecond!.Value + interval.EstimatedGameUncertaintySeconds + _policy.AbsoluteResidualToleranceSeconds)
        {
            Reset("Current progress/chart/pause source, provenance, offset/delay or actual progress continuity differs from the observed segment.");
            return Fail(ErrorCode.TargetExpired, _current.Evidence);
        }
        double Value(GameplayObservedClockSample sample, GameplayClockDomain domain) => domain switch
        {
            GameplayClockDomain.GameProgressSeconds => sample.GameProgressSeconds,
            GameplayClockDomain.AudioDspSeconds => sample.AudioDspSeconds,
            GameplayClockDomain.HostMonotonicSeconds => sample.HostMonotonicSeconds,
            _ => throw new InvalidOperationException("Only observed seconds domains may reach interpolation.")
        };
        var low = Value(a, request.SourceDomain); var high = Value(b, request.SourceDomain);
        if (request.Value < low || request.Value > high)
            return Fail(ErrorCode.InvalidArgument, "The source value is outside the actual observed sample endpoints; extrapolation is refused.");
        var fraction = (request.Value - low) / (high - low);
        var targetLow = Value(a, request.TargetDomain); var targetHigh = Value(b, request.TargetDomain);
        var converted = targetLow + fraction * (targetHigh - targetLow);
        var uncertainty = interval.EstimatedGameUncertaintySeconds * ((targetHigh - targetLow) / (b.GameProgressSeconds - a.GameProgressSeconds));
        if (request.TargetDomain != GameplayClockDomain.GameProgressSeconds) uncertainty += interval.CaptureSkewSeconds;
        if (!double.IsFinite(converted) || !double.IsFinite(uncertainty))
            return Fail(ErrorCode.ObservationUnavailable, "The observed conversion could not retain finite value and uncertainty.");
        return Result<GameplayObservedClockConversion>.Ok(new(request, converted, interval, uncertainty,
            "ObservedInterval only; anchored interpolation inside actual endpoints. Fresh source validation adds no history sample; no playback control, future extrapolation or measured latency."));
    }

    private static Result<GameplayObservedClockConversion> Fail(ErrorCode code, string reason)
        => Result<GameplayObservedClockConversion>.Fail(new(code, reason, GameplayCapabilityIds.Clock, "gameplay.clock/observed"));
}
