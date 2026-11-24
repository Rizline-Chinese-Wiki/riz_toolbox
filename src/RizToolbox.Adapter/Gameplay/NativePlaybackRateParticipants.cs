using RizToolbox.Sdk;
using ClockChange = RizToolbox.Adapter.Gameplay.NativePlaybackClock.NativePlaybackClockRateChange;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>A stable paused source sample. AuthorityRevision must change on hook/root/run authority
/// replacement; SchedulingRevision must change on every exact Pause/Replay/Resume occurrence.
/// Native access supplies these revisions from actual source/occurrence ownership;
/// paused booleans alone cannot identify a scheduling occurrence.</summary>
internal sealed record NativePlaybackRateSample(NativePlaybackBinding Binding, long AuthorityRevision,
    long SchedulingRevision, bool Paused, bool AudioPlaying, bool ProgressPlaying, bool Preparing,
    bool TimeReady, bool LevelOver, bool Crashed, double DspSeconds, float AudioPositionSeconds,
    float ClipLengthSeconds, float Pitch, double ProgressSeconds, float ChartPosition,
    double AppliedOffsetSeconds, bool HasStockOrigin, double StockOriginSeconds);

/// <summary>A captured, independently rooted exact graph with exclusive pitch mutation authority.
/// VerifyAuthority must enforce the main thread, original run, active GetTime return authority,
/// observed schedule fences, and live roots before every read/write. The stock provider
/// implements this access; availability still depends on its current registered source.</summary>
internal interface INativePlaybackRateAccess : IDisposable
{
    void VerifyAuthority();
    NativePlaybackRateSample Read();
    /// <summary>Reserve the continuous writer for the current completed pause occurrence.</summary>
    INativePlaybackPitchWrite PreparePitchWrite();
}

/// <summary>A private continuous writer receipt, captured without mutation. Write records this
/// attempt before entering the setter, including a setter that changes state and then throws.
/// VerifyCurrent must reject every foreign write since capture/last own write, even the same value
/// or an ABA sequence. A production provider requires a complete pitch-writer observer and source
/// fence; comparing current values or a returned setter alone cannot implement this contract.</summary>
internal interface INativePlaybackPitchWrite : IDisposable
{
    void Write(float pitch);
    void VerifyCurrent();
}

/// <summary>Recovery held by the actual completed native writer. Its private
/// continuation permits one original-pitch write after a failed API read; it
/// still verifies the current source, native fences and every other baseline
/// field. A complete independent readback is required to confirm restoration.</summary>
internal interface INativePlaybackPitchRecovery
{
    void RestoreOriginal();
}

/// <summary>Compose the actual audio-pitch participant and the independent media-clock participant
/// through the common value transaction. Factories capture/reserve only; they perform no writes.
/// A source is admitted only while paused, with no pending stock countdown or audio schedule.</summary>
internal static class NativePlaybackRateParticipants
{
    internal static GameplayPreparedControl Prepare(GameplayControlRequest request, NativePlaybackClock clock,
        Func<INativePlaybackRateAccess> captureAccess, Action verifyOrigin)
    {
        if (request.Action != GameplayControlAction.SetSpeed || request.Unit != GameplayControlValueUnit.PlaybackRateMultiplier
            || request.Value is not double requestedRate || !NativePlaybackClock.ValidRate(requestedRate))
            throw new GameplayStateException(ErrorCode.InvalidArgument, "The native rate participants require an explicit positive finite Single playback-rate request.");
        Plan? plan = null;
        var factories = new Func<IGameplayValueParticipant>[]
        {
            () => { plan = Plan.Capture(request, clock, captureAccess()); return plan.AudioParticipant(); },
            () => plan!.ClockParticipant()
        };
        return GameplayPreparedValueControl.Prepare(request, factories, verifyOrigin,
            Math.Max(0.00001, Math.Abs((float)requestedRate - requestedRate)));
    }

    private sealed class Plan
    {
        private readonly INativePlaybackRateAccess _access;
        private readonly ClockChange _change;
        private readonly INativePlaybackPitchWrite _pitchWrite;
        private readonly NativePlaybackRateSample _baseline;
        private int _owners;
        private bool _audioAttempted, _audioConfirmed, _audioRestoreAttempted, _clockAttempted, _disposed;
        private Plan(INativePlaybackRateAccess access, ClockChange change, INativePlaybackPitchWrite pitchWrite, NativePlaybackRateSample baseline)
            => (_access, _change, _pitchWrite, _baseline) = (access, change, pitchWrite, baseline);

        internal static Plan Capture(GameplayControlRequest request, NativePlaybackClock clock, INativePlaybackRateAccess access)
        {
            ClockChange? change = null;
            INativePlaybackPitchWrite? pitchWrite = null;
            try
            {
                access.VerifyAuthority();
                var first = access.Read();
                ValidatePaused(first);
                if (first.Binding.RunId != request.ObservedRunId || first.Binding.ControllerGeneration != request.ControllerGeneration)
                    throw new GameplayStateException(ErrorCode.TargetExpired, "The requested rate belongs to another exact run and controller generation.");
                change = clock.PreparePausedRate(first.Binding, request.Value!.Value);
                var baselineClock = change.Baseline;
                if (baselineClock.Rate != first.Pitch || baselineClock.ClipLengthSeconds != first.ClipLengthSeconds
                    || baselineClock.MediaAnchorSeconds != first.AudioPositionSeconds)
                    throw new GameplayStateException(ErrorCode.Conflict, "The clock, actual audio pitch/time, and clip baseline do not agree.");
                pitchWrite = access.PreparePitchWrite();
                var plan = new Plan(access, change, pitchWrite, first);
                plan.VerifyNative(first.Pitch);
                change.VerifyBaseline();
                return plan;
            }
            catch
            {
                // Independent preparation resources must all be attempted, even when a prior
                // cleanup throws. Cleanup cannot replace the actual preparation failure.
                try { change?.Dispose(); } catch { }
                try { pitchWrite?.Dispose(); } catch { }
                try { access.Dispose(); } catch { }
                throw;
            }
        }

        internal IGameplayValueParticipant AudioParticipant() { _owners++; return new Audio(this); }
        internal IGameplayValueParticipant ClockParticipant() { _owners++; return new Clock(this); }

        private NativePlaybackRateSample VerifyNative(float expectedPitch)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Plan));
            _access.VerifyAuthority();
            var current = _access.Read();
            ValidatePaused(current);
            if (!SamePreparedState(current, expectedPitch))
                throw new GameplayStateException(ErrorCode.Conflict, "The prepared audio/progress/offset/origin/schedule authority changed.");
            _pitchWrite.VerifyCurrent();
            _access.VerifyAuthority();
            return current;
        }

        // Progress.Update holds its last playing _nowTime during a ready Pause.
        // GetTime reads AudioSource.time when the stock origin is cleared. Its drift
        // threshold applies to the playing DSP estimate. Preserve each paused value independently.
        private bool SamePreparedState(NativePlaybackRateSample current, float expectedPitch)
            => current.Pitch == expectedPitch && current.DspSeconds >= _baseline.DspSeconds
                && (current with { DspSeconds = _baseline.DspSeconds, Pitch = _baseline.Pitch }) == _baseline;

        private static void ValidatePaused(NativePlaybackRateSample sample)
        {
            if (!sample.Binding.Valid || sample.AuthorityRevision <= 0 || sample.SchedulingRevision <= 0
                || !sample.Paused || sample.AudioPlaying || sample.ProgressPlaying || sample.Preparing || sample.HasStockOrigin
                || !sample.TimeReady || sample.LevelOver || sample.Crashed)
                throw new GameplayStateException(ErrorCode.InvalidGameState, "The exact rate participants must be ready and paused with no active playback or pending schedule.");
            if (!double.IsFinite(sample.DspSeconds) || sample.DspSeconds < 0 || !NativePlaybackClock.ValidRate(sample.Pitch)
                || !float.IsFinite(sample.AudioPositionSeconds) || !float.IsFinite(sample.ClipLengthSeconds)
                || sample.ClipLengthSeconds <= 0 || sample.AudioPositionSeconds < 0 || sample.AudioPositionSeconds > sample.ClipLengthSeconds
                || !double.IsFinite(sample.ProgressSeconds) || !float.IsFinite(sample.ChartPosition)
                || !double.IsFinite(sample.AppliedOffsetSeconds) || !double.IsFinite(sample.StockOriginSeconds))
                throw new GameplayStateException(ErrorCode.ObservationUnavailable, "The native rate baseline contains nonfinite or out-of-clip media, progress, offset, or origin values.");
        }

        private void Release()
        {
            if (--_owners != 0 || _disposed) return;
            _disposed = true;
            try { _change.Dispose(); }
            finally
            {
                try { _pitchWrite.Dispose(); }
                finally { _access.Dispose(); }
            }
        }

        private sealed class Audio : IGameplayValueParticipant
        {
            private readonly Plan _plan;
            private bool _disposed;
            internal Audio(Plan plan) => _plan = plan;
            public string Id => "native-playback/audio-pitch";
            public void VerifyPrepared()
            {
                Open();
                _plan.VerifyNative(_plan._baseline.Pitch);
                _plan._change.VerifyBaseline();
            }
            public void Commit()
            {
                VerifyPrepared();
                if (_plan._audioAttempted) throw new InvalidOperationException("The audio pitch may commit only once.");
                _plan._audioAttempted = true;
                _plan._pitchWrite.Write(_plan._change.RequestedRate);
                _plan.VerifyNative(_plan._change.RequestedRate);
                _plan._audioConfirmed = true;
            }
            public GameplayControlConfirmation Confirm()
            {
                Open();
                if (!_plan._audioConfirmed) return new(false, "The audio participant has no confirmed pitch write.");
                var actual = _plan.VerifyNative(_plan._change.RequestedRate);
                return new(true, "The exact paused AudioSource pitch was read back; progress, offset, origin and scheduling authority retain the prepared baseline.",
                    actual.Pitch, GameplayControlValueUnit.PlaybackRateMultiplier);
            }
            public void Restore()
            {
                Open();
                if (_plan._audioRestoreAttempted) throw new InvalidOperationException("The audio pitch may restore only once.");
                _plan._audioRestoreAttempted = true;
                _plan._access.VerifyAuthority();
                if (_plan._audioAttempted && _plan._pitchWrite is INativePlaybackPitchRecovery recovery)
                {
                    _plan._pitchWrite.VerifyCurrent();
                    recovery.RestoreOriginal();
                    return;
                }
                var current = _plan._access.Read();
                ValidatePaused(current);
                _plan._pitchWrite.VerifyCurrent();
                if (_plan.SamePreparedState(current, _plan._baseline.Pitch)) return;
                if (!_plan._audioAttempted || !_plan.SamePreparedState(current, _plan._change.RequestedRate))
                    throw new GameplayStateException(ErrorCode.Conflict, "Audio state is no longer this participant's last attempted pitch and prepared baseline; restoration refused.");
                _plan._access.VerifyAuthority();
                _plan._pitchWrite.VerifyCurrent();
                _plan._pitchWrite.Write(_plan._baseline.Pitch);
            }
            public GameplayControlRestoration ConfirmRestored()
            {
                Open();
                _plan.VerifyNative(_plan._baseline.Pitch);
                return new(!_plan._audioAttempted || _plan._audioRestoreAttempted, "Actual native audio/progress/offset/origin and schedule baseline were independently read after restoration.");
            }
            private void Open() { if (_disposed) throw new ObjectDisposedException(nameof(Audio)); }
            public void Dispose() { if (_disposed) return; _disposed = true; _plan.Release(); }
        }

        private sealed class Clock : IGameplayValueParticipant
        {
            private readonly Plan _plan;
            private bool _disposed;
            internal Clock(Plan plan) => _plan = plan;
            public string Id => "native-playback/media-clock";
            public void VerifyPrepared()
            {
                Open();
                _plan.VerifyNative(_plan._audioConfirmed ? _plan._change.RequestedRate : _plan._baseline.Pitch);
                _plan._change.VerifyBaseline();
            }
            public void Commit()
            {
                VerifyPrepared();
                if (!_plan._audioConfirmed) throw new GameplayStateException(ErrorCode.NotReady, "Clock rate publication requires actual audio pitch readback first.");
                _plan._clockAttempted = true;
                _plan._change.Commit();
            }
            public GameplayControlConfirmation Confirm()
            {
                Open();
                _plan.VerifyNative(_plan._change.RequestedRate);
                return new(_plan._change.Confirm(), "The independently anchored media clock owns the exact paused run/source graph at the read-back audio rate.");
            }
            public void Restore() { Open(); _plan._change.Restore(); }
            public GameplayControlRestoration ConfirmRestored()
            {
                Open();
                if (!_plan._clockAttempted)
                {
                    _plan._change.VerifyBaseline();
                    return new(true, "The uncommitted clock participant independently retains its complete original baseline.");
                }
                return new(_plan._change.ConfirmRestored(), "The private conditional clock change was independently read back at its complete original baseline.");
            }
            private void Open() { if (_disposed) throw new ObjectDisposedException(nameof(Clock)); }
            public void Dispose() { if (_disposed) return; _disposed = true; _plan.Release(); }
        }
    }
}
