using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>One actual Seek: stop the old Hold epoch, audio, progress/cache targets, game
/// state reconstruction, then the published clock. Reverse recovery keeps the
/// private time continuation alive until all other prepared owners have drained.</summary>
internal static class NativePlaybackSeekParticipants
{
    internal static GameplayPreparedControl Prepare(GameplayControlRequest request,
        INativePlaybackSeekSource source, NativeSeekProgressParticipant progress,
        NativeSeekGameStatePreparation gameState, Action verifyOrigin)
    {
        Plan? plan = null; Audio? audio = null; Clock? clock = null; HoldEpoch? epoch = null;
        try
        {
            if (request.Action != GameplayControlAction.Seek || request.Value is not double value || !double.IsFinite(value)
                || request.Unit is not (GameplayControlValueUnit.GameSeconds or GameplayControlValueUnit.ChartCoordinates)
                || request.ControllerGeneration <= 0 || string.IsNullOrWhiteSpace(request.ObservedRunId))
                throw new GameplayStateException(ErrorCode.InvalidArgument, "Seek needs a finite typed target and the current observed run.");
            verifyOrigin();
            plan = Plan.Capture(request, source, progress);
            audio = new(plan, gameState.VerifyTimeRestoration); clock = new(plan);
            epoch = new(plan, gameState.CaptureHoldEpochParticipant());
            var transaction = GameplayPreparedValueControl.Prepare(request,
                new Func<IGameplayValueParticipant>[]
                {
                    () => epoch, () => audio, () => new ProgressRecovery(progress, gameState), () => gameState, () => clock
                },
                () => { verifyOrigin(); source.VerifyAuthority(); }, progress.ValueTolerance);
            return new SeekControl(transaction, gameState);
        }
        catch (Exception error)
        {
            // This method owns every incoming preparation, even when failure
            // occurs before the common transaction enters its first factory.
            foreach (var owner in new IDisposable?[] { clock, gameState, progress, audio, epoch, plan, source })
                try { owner?.Dispose(); }
                catch (Exception cleanup)
                {
                    error.Data["SeekCleanupOwner/" + owner!.GetType().Name] = owner;
                    error.Data["SeekCleanupFailure/" + owner.GetType().Name] = cleanup;
                }
            throw;
        }
    }

    private sealed class SeekControl : GameplayPreparedControl
    {
        private readonly GameplayPreparedControl _inner;
        private readonly NativeSeekGameStatePreparation _gameState;
        internal SeekControl(GameplayPreparedControl inner, NativeSeekGameStatePreparation gameState)
            => (_inner, _gameState) = (inner, gameState);
        internal override bool CanRestore => _inner.CanRestore;
        internal override double ValueTolerance => _inner.ValueTolerance;
        internal override void Commit() => _inner.Commit();
        internal override GameplayControlConfirmation Confirm() => _inner.Confirm();
        internal override void Restore()
        {
            // Revocation is a pure owner operation. It must happen even when a
            // withdrawn run will reject the common transaction's native guards.
            _gameState.RevokeAllocationAcknowledgement();
            _inner.Restore();
        }
        internal override GameplayControlRestoration ConfirmRestored() => _inner.ConfirmRestored();
        public override void Dispose() => _inner.Dispose();
    }

    private sealed class Plan : IDisposable
    {
        internal readonly INativePlaybackSeekSource Source;
        internal readonly NativeSeekProgressParticipant Progress;
        internal readonly NativePlaybackRateSample Baseline;
        internal readonly NativePlaybackClock.NativePlaybackClockPositionChange Change;
        internal readonly INativePlaybackTimeWrite Writer;
        internal bool AudioAttempted, AudioConfirmed, AudioRestoreAttempted;
        internal bool ClockRestoreAttempted;
        private int _participants;
        private bool _disposed;
        private Plan(INativePlaybackSeekSource source, NativeSeekProgressParticipant progress,
            NativePlaybackRateSample baseline, NativePlaybackClock.NativePlaybackClockPositionChange change,
            INativePlaybackTimeWrite writer)
            => (Source, Progress, Baseline, Change, Writer) = (source, progress, baseline, change, writer);

        internal static Plan Capture(GameplayControlRequest request, INativePlaybackSeekSource source,
            NativeSeekProgressParticipant progress)
        {
            NativePlaybackClock.NativePlaybackClockPositionChange? change = null;
            INativePlaybackTimeWrite? writer = null;
            try
            {
                source.VerifyAuthority(); var baseline = source.Read();
                RequirePaused(baseline);
                if (!baseline.Binding.Valid || baseline.Binding != source.Binding || baseline.Binding.RunId != request.ObservedRunId)
                    throw new GameplayStateException(ErrorCode.TargetExpired, "The actual audio source differs from this Seek run.");
                change = source.Clock.PreparePausedPosition(source.Binding, progress.MediaSeconds);
                if (change.Baseline.Rate != baseline.Pitch || change.Baseline.MediaAnchorSeconds != baseline.AudioPositionSeconds
                    || change.Baseline.ClipLengthSeconds != baseline.ClipLengthSeconds)
                    throw new GameplayStateException(ErrorCode.Conflict, "The paused Clock and actual audio disagree before Seek.");
                writer = source.PrepareTimeWrite();
                var plan = new Plan(source, progress, baseline, change, writer);
                plan.ReadAt(baseline.AudioPositionSeconds, baseline.ProgressSeconds, baseline.ChartPosition);
                return plan;
            }
            catch (Exception error)
            {
                foreach (var owner in new IDisposable?[] { writer, change })
                    try { owner?.Dispose(); }
                    catch (Exception cleanup) { error.Data["SeekClockPreparationCleanup/" + owner!.GetType().Name] = cleanup; }
                throw;
            }
        }

        internal NativePlaybackRateSample ReadAt(float media, double progress, float chart)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Plan));
            Source.VerifyAuthority(); Writer.VerifyCurrent();
            var current = Source.Read(); RequirePaused(current);
            if (current.Binding != Baseline.Binding || current.AuthorityRevision != Baseline.AuthorityRevision
                || current.Pitch != Baseline.Pitch || current.ClipLengthSeconds != Baseline.ClipLengthSeconds
                || current.AppliedOffsetSeconds != Baseline.AppliedOffsetSeconds || current.DspSeconds < Baseline.DspSeconds
                || current.AudioPositionSeconds != media || current.ProgressSeconds != progress || current.ChartPosition != chart)
                throw new GameplayStateException(ErrorCode.Conflict, "Actual audio, progress and selected rate differ from this Seek stage.");
            Writer.VerifyCurrent(); Source.VerifyAuthority(); return current;
        }
        internal void ReadTarget() => ReadAt(Progress.MediaSeconds, Progress.Coordinates.ProgressSeconds, Progress.Coordinates.ChartCoordinate);
        private static void RequirePaused(NativePlaybackRateSample value)
        {
            if (!value.Paused || value.AudioPlaying || value.ProgressPlaying || value.Preparing || value.HasStockOrigin
                || !value.TimeReady || value.LevelOver || value.Crashed || value.AuthorityRevision <= 0 || value.SchedulingRevision <= 0
                || !double.IsFinite(value.DspSeconds) || value.DspSeconds < 0 || !NativePlaybackClock.ValidRate(value.Pitch)
                || !float.IsFinite(value.AudioPositionSeconds) || value.AudioPositionSeconds < 0
                || !float.IsFinite(value.ClipLengthSeconds) || value.ClipLengthSeconds <= 0 || value.AudioPositionSeconds > value.ClipLengthSeconds)
                throw new GameplayStateException(ErrorCode.InvalidGameState, "Seek requires the actual ready, paused source without a pending playback schedule.");
        }
        internal void Retain() => _participants++;
        internal void Release() { if (--_participants == 0) Dispose(); }
        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            try { Change.Dispose(); }
            finally { try { Writer.Dispose(); } finally { Source.Dispose(); } }
        }
    }

    // Only the dependent time inverse is gated. All other verification,
    // restoration order and preparation-resource ownership remain unchanged.
    private sealed class ProgressRecovery : IGameplayValueParticipant
    {
        private readonly NativeSeekProgressParticipant _inner;
        private readonly NativeSeekGameStatePreparation _gameState;
        internal ProgressRecovery(NativeSeekProgressParticipant inner, NativeSeekGameStatePreparation gameState)
            => (_inner, _gameState) = (inner, gameState);
        public string Id => _inner.Id;
        public void VerifyPrepared() => _inner.VerifyPrepared();
        public void Commit() => _inner.Commit();
        public GameplayControlConfirmation Confirm() => _inner.Confirm();
        public void Restore() => _inner.Restore(_gameState.VerifyTimeRestoration);
        public GameplayControlRestoration ConfirmRestored() => _inner.ConfirmRestored();
        public void Dispose() => _inner.Dispose();
    }

    private sealed class HoldEpoch : IGameplayValueParticipant
    {
        private readonly Plan _plan;
        private readonly IGameplayValueParticipant _inner;
        private bool _disposed;
        internal HoldEpoch(Plan plan, IGameplayValueParticipant inner)
        { _plan = plan; _inner = inner; plan.Retain(); }
        public string Id => _inner.Id;
        public void VerifyPrepared() { Open(); _inner.VerifyPrepared(); }
        public void Commit() { Open(); _inner.Commit(); }
        public GameplayControlConfirmation Confirm() { Open(); return _inner.Confirm(); }
        public void Restore()
        {
            Open();
            // Inverse order means audio/progress were attempted first, but a
            // failed setter does not prove restoration. Restart the old Hold
            // only after independently reading its original time and clock.
            _plan.ReadAt(_plan.Baseline.AudioPositionSeconds, _plan.Baseline.ProgressSeconds, _plan.Baseline.ChartPosition);
            // Source.Read exposes only progress/chart. The retained participant
            // independently reads nowRealTime and nowSyncedTime as well.
            if (!_plan.Progress.ConfirmRestored().OriginalStateRestored)
                throw new GameplayStateException(ErrorCode.Conflict, "Original Hold iterators require all four original progress caches.");
            if (_plan.ClockRestoreAttempted)
            {
                if (!_plan.Change.ConfirmRestored())
                    throw new GameplayStateException(ErrorCode.Conflict, "Original Hold iterators require the restored original playback clock.");
            }
            else _plan.Change.VerifyBaseline();
            _inner.Restore();
        }
        public GameplayControlRestoration ConfirmRestored() { Open(); return _inner.ConfirmRestored(); }
        private void Open() { if (_disposed) throw new ObjectDisposedException(nameof(HoldEpoch)); }
        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            try { _inner.Dispose(); } finally { _plan.Release(); }
        }
    }

    private sealed class Audio : IGameplayValueParticipant
    {
        private readonly Plan _plan;
        private readonly Action _verifyRestoration;
        private bool _disposed;
        internal Audio(Plan plan) : this(plan, static () => { }) { }
        internal Audio(Plan plan, Action verifyRestoration)
        { _plan = plan; _verifyRestoration = verifyRestoration; plan.Retain(); }
        public string Id => "native-seek/audio-time";
        public void VerifyPrepared()
        {
            Open(); _plan.ReadAt(_plan.Baseline.AudioPositionSeconds, _plan.Baseline.ProgressSeconds, _plan.Baseline.ChartPosition);
            _plan.Change.VerifyBaseline();
        }
        public void Commit()
        {
            VerifyPrepared();
            if (_plan.AudioAttempted) throw new InvalidOperationException("A prepared Seek audio write may commit only once.");
            _plan.AudioAttempted = true;
            _plan.Writer.Write(_plan.Progress.MediaSeconds);
            _plan.ReadAt(_plan.Progress.MediaSeconds, _plan.Baseline.ProgressSeconds, _plan.Baseline.ChartPosition);
            _plan.AudioConfirmed = true;
        }
        public GameplayControlConfirmation Confirm()
        {
            Open(); _plan.ReadTarget();
            return new(_plan.AudioConfirmed, "Actual media time, progress and unchanged selected pitch match the prepared Seek target.");
        }
        public void Restore()
        {
            Open();
            if (_plan.AudioRestoreAttempted) throw new InvalidOperationException("Audio Seek restoration is once-only.");
            _plan.AudioRestoreAttempted = true;
            _plan.Source.VerifyAuthority(); _plan.Writer.VerifyCurrent();
            _verifyRestoration();
            _plan.Writer.RestoreOriginal();
            _plan.ReadAt(_plan.Baseline.AudioPositionSeconds, _plan.Baseline.ProgressSeconds, _plan.Baseline.ChartPosition);
        }
        public GameplayControlRestoration ConfirmRestored()
        {
            Open(); _plan.ReadAt(_plan.Baseline.AudioPositionSeconds, _plan.Baseline.ProgressSeconds, _plan.Baseline.ChartPosition);
            return new(!_plan.AudioAttempted || _plan.AudioRestoreAttempted,
                "The original actual audio/progress values and selected pitch were independently read after recovery.");
        }
        private void Open() { if (_disposed) throw new ObjectDisposedException(nameof(Audio)); }
        public void Dispose() { if (_disposed) return; _disposed = true; _plan.Release(); }
    }

    private sealed class Clock : IGameplayValueParticipant
    {
        private readonly Plan _plan; private bool _disposed;
        internal Clock(Plan plan) { _plan = plan; plan.Retain(); }
        public string Id => "native-seek/media-clock";
        public void VerifyPrepared()
        {
            Open(); _plan.Change.VerifyBaseline();
            if (_plan.AudioConfirmed) _plan.ReadTarget();
            else _plan.ReadAt(_plan.Baseline.AudioPositionSeconds, _plan.Baseline.ProgressSeconds, _plan.Baseline.ChartPosition);
        }
        public void Commit() { VerifyPrepared(); _plan.ReadTarget(); _plan.Change.Commit(); }
        public GameplayControlConfirmation Confirm()
        {
            Open(); _plan.ReadTarget();
            return new(_plan.Change.Confirm(), "The conditional native media-clock publication matches the independently read Seek target.");
        }
        public void Restore() { Open(); _plan.ClockRestoreAttempted = true; _plan.Change.Restore(); }
        public GameplayControlRestoration ConfirmRestored()
        {
            Open();
            if (!_plan.ClockRestoreAttempted)
            {
                // Earlier participants can fail before this one is attempted.
                // Its unchanged baseline is still part of full recovery readback.
                _plan.Change.VerifyBaseline();
                return new(true, "Seek failed before clock publication; the exact original paused policy remains current.");
            }
            return new(_plan.Change.ConfirmRestored(), "The original paused Clock baseline was conditionally restored and read back.");
        }
        private void Open() { if (_disposed) throw new ObjectDisposedException(nameof(Clock)); }
        public void Dispose() { if (_disposed) return; _disposed = true; _plan.Release(); }
    }
}
