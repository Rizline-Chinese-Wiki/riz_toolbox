using RizToolbox.Runtime;
using RizToolbox.Sdk;
using Fence = RizToolbox.Adapter.Gameplay.NativePlaybackOwnedCallTransport.Fence;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>Diagnostic projection only. No provider or clock accepts this record as evidence.</summary>
internal sealed record NativePlaybackOwnedOccurrenceView(long Occurrence, string Kind, bool Returned,
    bool ActualScheduleReturned, double? ActualScheduleDsp, double BeforeDsp, double AfterDsp,
    float MediaSeconds, float ReadbackPitch, long PitchSequence, long SourceSequence);

/// <summary>Concrete consumer of one private native owned-call producer. It is deliberately
/// obtainable only through an explicit controlled factory. It cannot admit a real game provider:
/// native stock hook coverage, arbitrary external writers and return authority are still absent.</summary>
internal sealed class NativePlaybackOccurrenceRateProvider : INativePlaybackRateProvider
{
    private readonly NativePlaybackOwnedCallProducer _producer;
    private NativePlaybackOccurrenceRateProvider(NativePlaybackOwnedCallProducer producer) => _producer = producer;
    internal static NativePlaybackOccurrenceRateProvider AdoptControlledForTests(NativePlaybackOwnedCallProducer producer)
    {
        ArgumentNullException.ThrowIfNull(producer); return new(producer);
    }
    internal bool ProductionAdmission => false;
    public NativePlaybackRateProviderProbe Probe() => _producer.ProbeControlled();
    public INativePlaybackRateSource Capture(NativePlaybackRateOrigin origin) => _producer.CaptureSource(origin);
}

/// <summary>Executes, observes and coordinates the owned native stub's actual source calls.
/// The native producer, rather than a caller Record/bool, creates every occurrence and writer
/// fence. Its coverage is only this controlled lane, not the game's stock functions or naked
/// NativeRuntime/Unity writes. No production factory or completeness flag is provided.</summary>
internal sealed class NativePlaybackOwnedCallProducer : IDisposable
{
    private const string Coverage = "Controlled native owned-call coverage only: private native Pause/Replay/Resume, exact PlayScheduled argument/Returned, and owned pitch/source setters; actual NativeRuntime DSP/audio/progress readback. Real stock hooks, direct external writer coverage and production GetTime authority are absent.";
    private readonly NativeRuntime _api;
    private readonly NativePlaybackBinding _binding;
    private readonly NativePlaybackRateOrigin _origin;
    private readonly NativePlaybackActualSource _actual;
    private readonly NativePlaybackOwnedCallTransport _transport;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private Fence? _paused;
    private string? _withdrawal;
    private int _activeCalls;
    private bool _disposed, _resourcesReleased, _clockReady;
    private double _lastDsp;
    internal NativePlaybackClock Clock { get; }
    internal NativePlaybackOwnedOccurrenceView? LastOccurrence { get; private set; }
    internal bool ProductionAdmission => false;

    private NativePlaybackOwnedCallProducer(NativeRuntime api, NativePlaybackBinding binding,
        NativePlaybackRateOrigin origin, string ownedNativeLibrary)
    {
        if (!binding.Valid || origin.RunId != binding.RunId || origin.ControllerGeneration != binding.ControllerGeneration
            || origin.Controller != binding.Controller || origin.Information != binding.Information
            || origin.Progress != binding.Progress || origin.Pause == 0)
            throw new GameplayStateException(ErrorCode.InvalidArgument, "The controlled producer requires one exact immutable run/controller/source origin.");
        (_api, _binding, _origin) = (api, binding, origin);
        _actual = new(api, binding, origin.Pause);
        try
        {
            _transport = NativePlaybackOwnedCallTransport.CreateControlledForTests(ownedNativeLibrary, _actual, binding);
            try
            {
                var fence = _transport.Capture(); var initial = Read(_actual, fence);
                RequireReadyPaused(initial);
                Clock = new(binding, initial.DspSeconds, initial.AudioPositionSeconds, initial.Pitch, initial.ClipLengthSeconds);
                _clockReady = true;
            }
            catch { _transport.Dispose(); throw; }
        }
        catch { _actual.Dispose(); throw; }
    }

    internal static NativePlaybackOwnedCallProducer CreateControlledForTests(NativeRuntime api,
        NativePlaybackBinding binding, NativePlaybackRateOrigin origin, string ownedNativeLibrary)
        => new(api, binding, origin, ownedNativeLibrary);

    internal NativePlaybackRateProviderProbe ProbeControlled()
    {
        try
        {
            VerifyAvailable();
            if (_activeCalls != 0 || _transport.Capture().Busy)
                return new(false, Coverage + " An owned native call is currently active.", new(ErrorCode.NotReady, "The owned native call has not returned."));
            _actual.VerifyGraph(); return new(true, Coverage);
        }
        catch (Exception error) { return new(false, Coverage + " The controlled producer was withdrawn.", new(ErrorCode.CapabilityUnavailable, error.Message)); }
    }

    internal INativePlaybackRateSource CaptureSource(NativePlaybackRateOrigin origin)
    {
        VerifyAvailable();
        if (origin != _origin) throw new GameplayStateException(ErrorCode.TargetExpired, "The provider capture belongs to another exact run/controller/pause graph.");
        if (_paused is null) throw new GameplayStateException(ErrorCode.NotReady, "No private completed Pause occurrence admits this current controlled source.");
        VerifyCaptured(_paused);
        var source = new Source(this, new(_api, _binding, _origin.Pause), _paused);
        try { RequireReadyPaused(source.Read()); return source; }
        catch { source.Dispose(); throw; }
    }

    internal void Pause() => Run(1, 0);
    internal void ReplayScheduled(float delay) => Run(2, delay);
    internal void ResumeScheduled(float delay) => Run(3, delay);

    private void Run(uint kind, float delay)
    {
        VerifyAvailable();
        if (_activeCalls != 0) throw new GameplayStateException(ErrorCode.Conflict, "A second stock occurrence cannot enter the same owned source call.");
        if (!float.IsFinite(delay) || delay < 0 || kind != 1 && delay < .1f)
            throw new GameplayStateException(ErrorCode.InvalidArgument, "The pinned owned schedule requires a finite Single delay of at least 0.1 seconds.");
        var beforeFence = _transport.Capture(); _transport.Verify(beforeFence);
        var before = Read(_actual, beforeFence); var baselineClock = Clock.Capture();
        _paused = null;
        _activeCalls++;
        try
        {
            var result = _transport.Run(kind, delay); result.Rethrow(); var occurrence = result.Fence;
            if (!occurrence.Returned || occurrence.Kind != kind || occurrence.OccurrenceSequence != checked(beforeFence.OccurrenceSequence + 1)
                || occurrence.SourceSequence != beforeFence.SourceSequence || occurrence.Epoch != beforeFence.Epoch
                || !ReferenceEquals(Clock.Capture(), baselineClock))
                throw new GameplayStateException(ErrorCode.Conflict, "The native owned occurrence did not retain its exact original clock/source fence.");
            var after = Read(_actual, occurrence);
            if (kind == 1)
            {
                if (after.AudioPlaying || after.HasStockOrigin || after.Pitch != baselineClock.Rate
                    || occurrence.PitchSequence != beforeFence.PitchSequence)
                    throw new GameplayStateException(ErrorCode.Conflict, "The actual completed Pause did not retain a stopped origin and the selected pitch/writer fence.");
                Clock.ObservePause(_binding, after.DspSeconds, after.AudioPositionSeconds, after.Pitch);
                _transport.Verify(occurrence); _paused = occurrence;
            }
            else
            {
                // Origin is only a cross-check. The start DSP comes from the private native
                // observer of the exact PlayScheduled argument, never from origin+source time.
                if (!occurrence.ScheduleReturned || occurrence.ScheduleOccurrence != occurrence.OccurrenceSequence
                    || occurrence.ScheduleSequence != checked(beforeFence.ScheduleSequence + 1)
                    || !occurrence.StockPitchReturned || occurrence.StockPitchSequence != checked(beforeFence.PitchSequence + 1)
                    || occurrence.PitchSequence != occurrence.StockPitchSequence || after.Pitch != 1
                    || !after.HasStockOrigin || !double.IsFinite(occurrence.ScheduleDsp)
                    || occurrence.ScheduleDsp < after.DspSeconds
                    || after.StockOriginSeconds != occurrence.ScheduleDsp - (kind == 3 ? after.AudioPositionSeconds : 0)
                    || kind == 2 && after.AudioPositionSeconds != 0
                    || kind == 3 && after.AudioPositionSeconds != before.AudioPositionSeconds)
                    throw new GameplayStateException(ErrorCode.ObservationUnavailable, "The owned native schedule/reset-pitch argument and actual media/origin readback do not agree.");
                var restored = _transport.Write(occurrence, (float)baselineClock.Rate);
                restored.Rethrow(); _transport.Verify(restored.Fence);
                var readback = Read(_actual, restored.Fence);
                if (readback.Pitch != baselineClock.Rate || readback.AudioPositionSeconds != after.AudioPositionSeconds
                    || readback.StockOriginSeconds != after.StockOriginSeconds || readback.HasStockOrigin != after.HasStockOrigin
                    || !ReferenceEquals(Clock.Capture(), baselineClock))
                    throw new GameplayStateException(ErrorCode.Conflict, "The selected pitch was not independently restored/read back at the exact native schedule.");
                Clock.ObserveScheduled(_binding, readback.DspSeconds, occurrence.ScheduleDsp, readback.AudioPositionSeconds, readback.Pitch);
                after = readback; occurrence = restored.Fence;
            }
            LastOccurrence = new(ToLong(occurrence.OccurrenceSequence), kind == 1 ? "Pause" : kind == 2 ? "Replay" : "Resume",
                occurrence.Returned, kind != 1 && occurrence.ScheduleReturned, kind == 1 ? null : occurrence.ScheduleDsp,
                occurrence.BeforeDsp, after.DspSeconds, after.AudioPositionSeconds, after.Pitch,
                ToLong(occurrence.PitchSequence), ToLong(occurrence.SourceSequence));
        }
        catch (Exception error) { Withdraw("The owned native playback occurrence failed; schedule/audio side effects are unknown: " + error.Message); throw; }
        finally { _activeCalls--; ReleaseDeferred(); }
    }

    /// <summary>A service-external writer in the controlled owned lane. It executes the actual
    /// setter, so even equal-value/ABA writes get native Before attempts. Direct game/Unity writes
    /// bypassing this lane are not covered and are a remaining production dependency.</summary>
    internal void WritePitchOutsideService(float pitch)
    {
        VerifyThread(); if (_disposed) throw new ObjectDisposedException(nameof(NativePlaybackOwnedCallProducer));
        if (!NativePlaybackClock.ValidRate(pitch)) throw new GameplayStateException(ErrorCode.InvalidArgument, "The owned external pitch must be positive and finite.");
        _actual.VerifyGraph(); _activeCalls++;
        try
        {
            var result = _transport.Write(_transport.Capture(), pitch); result.Rethrow();
            _transport.Verify(result.Fence);
            var actual = Read(_actual, result.Fence);
            if (actual.Pitch != pitch) throw new GameplayStateException(ErrorCode.ObservationUnavailable, "The actual service-external owned pitch setter did not read back its requested value.");
            if (actual.Pitch != Clock.Capture().Rate) Withdraw("An actual service-external owned pitch write changed the selected clock rate.");
        }
        catch (Exception error) { Withdraw("The actual service-external owned pitch write failed: " + error.Message); throw; }
        finally { _activeCalls--; ReleaseDeferred(); }
    }

    internal void SetClipOutsideService(IntPtr clip)
    {
        VerifyThread(); if (_disposed) throw new ObjectDisposedException(nameof(NativePlaybackOwnedCallProducer));
        _actual.VerifyGraph(); _activeCalls++;
        try
        {
            var result = _transport.SetClip(clip);
            Withdraw("An actual owned AudioSource clip setter entered; the original source fence is no longer admitted.");
            result.Rethrow();
        }
        catch (Exception error) { Withdraw("The actual owned source setter failed: " + error.Message); throw; }
        finally { _activeCalls--; ReleaseDeferred(); }
    }

    internal void Withdraw(string reason)
    {
        VerifyThread(); if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Withdrawal requires an actual reason.", nameof(reason));
        if (_withdrawal is not null) return;
        _withdrawal = reason; _paused = null; Clock.Withdraw(reason);
    }
    private void VerifyCaptured(Fence pause)
    {
        VerifyAvailable();
        if (_activeCalls != 0 || !ReferenceEquals(_paused, pause) || !pause.Returned || pause.Kind != 1)
            throw new GameplayStateException(ErrorCode.TargetExpired, "The private completed Pause occurrence is no longer this source's current authority.");
        _transport.Verify(pause, pitch: false); _actual.VerifyGraph();
        if (Clock.Capture().Binding != _binding || Clock.Capture().Phase != NativePlaybackPhase.Paused)
            throw new GameplayStateException(ErrorCode.TargetExpired, "The controlled source no longer owns this exact paused clock.");
    }
    private NativePlaybackRateSample Read(NativePlaybackActualSource actual, Fence fence)
    {
        try
        {
            _transport.Verify(fence); var sample = actual.Read(ToLong(fence.Epoch), ToLong(fence.OccurrenceSequence)); _transport.Verify(fence);
            if (!double.IsFinite(sample.DspSeconds) || sample.DspSeconds < _lastDsp || !NativePlaybackClock.ValidRate(sample.Pitch)
                || !float.IsFinite(sample.AudioPositionSeconds) || !float.IsFinite(sample.ClipLengthSeconds)
                || sample.ClipLengthSeconds <= 0 || sample.AudioPositionSeconds < 0 || sample.AudioPositionSeconds > sample.ClipLengthSeconds
                || !double.IsFinite(sample.ProgressSeconds) || !float.IsFinite(sample.ChartPosition) || !double.IsFinite(sample.AppliedOffsetSeconds))
                throw new GameplayStateException(ErrorCode.ObservationUnavailable, "The actual owned source DSP/media/pitch/progress sample is invalid or DSP regressed.");
            _lastDsp = sample.DspSeconds; return sample;
        }
        catch (Exception error)
        {
            if (_clockReady) Withdraw("An actual owned source read failed; clock synchronization is unknown: " + error.Message);
            throw;
        }
    }
    private static void RequireReadyPaused(NativePlaybackRateSample sample)
    {
        if (!sample.Paused || sample.AudioPlaying || sample.ProgressPlaying || sample.Preparing || sample.HasStockOrigin
            || !sample.TimeReady || sample.LevelOver || sample.Crashed)
            throw new GameplayStateException(ErrorCode.InvalidGameState, "The controlled source must be ready and paused with no pending stock origin/schedule.");
    }
    private void VerifyAvailable()
    {
        VerifyThread();
        if (_disposed || _withdrawal is not null) throw new GameplayStateException(ErrorCode.TargetExpired, _withdrawal ?? "The owned playback producer was disposed.");
    }
    private void VerifyThread()
    {
        if (_thread != Environment.CurrentManagedThreadId) throw new GameplayStateException(ErrorCode.WrongThread, "The owned playback producer is main-thread bound.");
    }
    private static long ToLong(ulong value) => checked((long)value);
    private void ReleaseDeferred()
    {
        if (!_disposed || _resourcesReleased || _activeCalls != 0) return;
        _resourcesReleased = true;
        try { _transport.Dispose(); } finally { _actual.Dispose(); }
    }
    public void Dispose()
    {
        VerifyThread(); if (_disposed) return;
        Withdraw("The controlled native owned-call producer was disposed."); _disposed = true; ReleaseDeferred();
    }

    private sealed class Source : INativePlaybackRateSource
    {
        private readonly NativePlaybackOwnedCallProducer _owner;
        private readonly NativePlaybackActualSource _actual;
        private readonly Fence _pause;
        private bool _disposed;
        internal Source(NativePlaybackOwnedCallProducer owner, NativePlaybackActualSource actual, Fence pause)
            => (_owner, _actual, _pause) = (owner, actual, pause);
        public NativePlaybackBinding Binding => _owner._binding;
        public NativePlaybackClock Clock => _owner.Clock;
        public void VerifyAuthority()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Source));
            _owner.VerifyCaptured(_pause); _actual.VerifyGraph();
        }
        public NativePlaybackRateSample Read()
        {
            VerifyAuthority(); var fence = _owner._transport.Capture();
            var sample = _owner.Read(_actual, fence); VerifyAuthority(); return sample;
        }
        public INativePlaybackPitchWrite PreparePitchWrite()
        {
            VerifyAuthority(); return new PitchWrite(this, _owner._transport.Capture());
        }
        public void Dispose() { _owner.VerifyThread(); if (_disposed) return; _disposed = true; _actual.Dispose(); }
        private sealed class PitchWrite : INativePlaybackPitchWrite
        {
            private readonly Source _source;
            private Fence _fence;
            private bool _disposed;
            internal PitchWrite(Source source, Fence fence) => (_source, _fence) = (source, fence);
            public void VerifyCurrent()
            {
                if (_disposed) throw new ObjectDisposedException(nameof(PitchWrite));
                _source.VerifyAuthority(); _source._owner._transport.Verify(_fence);
            }
            public void Write(float pitch)
            {
                VerifyCurrent();
                if (!NativePlaybackClock.ValidRate(pitch)) throw new GameplayStateException(ErrorCode.InvalidArgument, "A native pitch attempt must be positive and finite.");
                var owner = _source._owner; owner._activeCalls++;
                try
                {
                    var result = owner._transport.Write(_fence, pitch);
                    _fence = result.Fence; // Preserve native Before ownership before rethrowing a partial setter failure.
                    result.Rethrow();
                }
                finally { owner._activeCalls--; owner.ReleaseDeferred(); }
            }
            public void Dispose() { _disposed = true; }
        }
    }
}
