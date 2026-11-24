using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>The return channel must prove this entire rooted graph and run identity.
/// Raw addresses alone cannot authorize a clock, including after address reuse.</summary>
internal sealed record NativePlaybackBinding(string RunId, long SceneGeneration, long ControllerGeneration,
    long SourceGeneration, long Controller, long Information, long Progress, long Player, long AudioSource,
    long Clip, long GetTimeMethod)
{
    internal bool Valid => !string.IsNullOrWhiteSpace(RunId) && SceneGeneration > 0 && ControllerGeneration > 0
        && SourceGeneration > 0 && Controller != 0 && Information != 0 && Progress != 0 && Player != 0
        && AudioSource != 0 && Clip != 0 && GetTimeMethod != 0;
}

internal enum NativePlaybackPhase { Paused, Playing, Scheduled, Withdrawn }

internal sealed record NativePlaybackClockState(NativePlaybackBinding Binding, long Revision,
    NativePlaybackPhase Phase, double DspAnchorSeconds, double MediaAnchorSeconds, double Rate,
    double ClipLengthSeconds, string? WithdrawalReason = null);

/// <summary>Conditional native policy publication for this exact immutable clock revision.
/// Publish must compare the complete expected policy and read back next before returning.
/// It supplies no run/source authority; that remains with the native access and return broker.</summary>
internal interface INativePlaybackClockPublication
{
    void Publish(NativePlaybackClockState expected, NativePlaybackClockState next);
    bool IsCurrent(NativePlaybackClockState state);
    void Withdraw(string reason);
}

/// <summary>Media seconds for an exact MUGMusicPlayer.GetTime receiver, using an independent DSP
/// anchor. Scaling stock raw GetTime would double-scale AudioSource.time after its drift fallback.
/// This class installs no hook and grants no production capability.</summary>
internal sealed partial class NativePlaybackClock
{
    private readonly object _gate = new();
    private NativePlaybackClockState _state;
    private double _lastDspSeconds;
    private object? _pendingChange;
    private readonly INativePlaybackClockPublication? _publication;

    internal NativePlaybackClock(NativePlaybackBinding binding, double dspSeconds, double mediaSeconds,
        double rate, double clipLengthSeconds, INativePlaybackClockPublication? publication = null)
    {
        if (!binding.Valid || !ValidDsp(dspSeconds) || !ValidRate(rate) || !double.IsFinite(clipLengthSeconds)
            || clipLengthSeconds <= 0 || clipLengthSeconds > float.MaxValue || !ValidPosition(mediaSeconds, clipLengthSeconds))
            throw new GameplayStateException(ErrorCode.InvalidArgument, "A playback clock requires an exact run/source graph, finite DSP/media/clip seconds and a positive finite Single rate.");
        _state = new(binding, 1, NativePlaybackPhase.Paused, dspSeconds, mediaSeconds, (float)rate, clipLengthSeconds);
        _lastDspSeconds = dspSeconds;
        _publication = publication;
    }

    internal NativePlaybackClockState Capture() { lock (_gate) return _state; }

    /// <summary>Only the exact authority-bound callback may consume this result. False requires
    /// withdrawal and accounting for audio state; stock fallback is not synchronization evidence.
    /// The native original exception must never be replaced by this callback.</summary>
    internal bool TryGetTime(NativePlaybackBinding binding, double dspSeconds, out float mediaSeconds)
    {
        lock (_gate)
        {
            mediaSeconds = default;
            if (_state.Binding != binding || _state.Phase == NativePlaybackPhase.Withdrawn) return false;
            if (!PublicationCurrent(_state)) return false;
            if (!AcceptDsp(dspSeconds)) return false;
            if (!TryCalculateMediaSeconds(_state, dspSeconds, out mediaSeconds))
            {
                WithdrawCore("The anchored media clock exceeded the finite Single return domain.");
                return false;
            }
            return true;
        }
    }

    /// <summary>Consume actual post-Pause source time, after native pause and source validation.</summary>
    internal void ObservePause(NativePlaybackBinding binding, double dspSeconds, double actualAudioSeconds, double actualPitch)
    {
        lock (_gate)
        {
            RequireTransition(binding, dspSeconds, actualAudioSeconds, actualPitch);
            Publish(_state with { Revision = NextRevision(), Phase = NativePlaybackPhase.Paused,
                DspAnchorSeconds = dspSeconds, MediaAnchorSeconds = actualAudioSeconds });
        }
    }

    /// <summary>Consume a verified immediate start. This does not call AudioSource or set pitch.</summary>
    internal void ObservePlaying(NativePlaybackBinding binding, double dspSeconds, double actualAudioSeconds, double actualPitch)
    {
        lock (_gate)
        {
            RequireTransition(binding, dspSeconds, actualAudioSeconds, actualPitch);
            Publish(_state with { Revision = NextRevision(), Phase = NativePlaybackPhase.Playing,
                DspAnchorSeconds = dspSeconds, MediaAnchorSeconds = actualAudioSeconds });
        }
    }

    /// <summary>Replay/Resume force pitch=1 in the pinned image. The owner must reapply and read back
    /// this clock's rate before admitting the completed schedule. Resume uses saved source time;
    /// Replay uses its actual reset source time. startDsp is the DspTimeService schedule.</summary>
    internal void ObserveScheduled(NativePlaybackBinding binding, double dspSeconds, double startDspSeconds,
        double actualAudioSeconds, double actualPitch)
    {
        lock (_gate)
        {
            RequireTransition(binding, dspSeconds, actualAudioSeconds, actualPitch);
            if (!ValidDsp(startDspSeconds) || startDspSeconds < dspSeconds)
            {
                WithdrawCore("The completed playback schedule has no finite future DSP start.");
                throw new GameplayStateException(ErrorCode.InvalidGameState, _state.WithdrawalReason!);
            }
            Publish(_state with { Revision = NextRevision(), Phase = NativePlaybackPhase.Scheduled,
                DspAnchorSeconds = startDspSeconds, MediaAnchorSeconds = actualAudioSeconds });
        }
    }

    internal void Withdraw(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Withdrawal requires actual evidence.", nameof(reason));
        lock (_gate) WithdrawCore(reason);
    }

    /// <summary>Read-only preparation reserves a private conditional change. It cannot admit native
    /// writes or schedules. Disposing this resource never rolls a clock back.</summary>
    internal NativePlaybackClockRateChange PreparePausedRate(NativePlaybackBinding binding, double requestedRate)
    {
        lock (_gate)
        {
            if (!ValidRate(requestedRate)) throw new GameplayStateException(ErrorCode.InvalidArgument, "Playback rate must remain positive and finite in the native Single domain.");
            if (_state.Binding != binding || _state.Phase != NativePlaybackPhase.Paused)
                throw new GameplayStateException(ErrorCode.InvalidGameState, "A rate change requires this exact paused playback graph.");
            if (!PublicationCurrent(_state)) throw new GameplayStateException(ErrorCode.TargetExpired, "The native clock policy no longer belongs to this exact paused baseline.");
            if (_pendingChange is not null) throw new GameplayStateException(ErrorCode.Conflict, "Another prepared playback clock change owns this baseline.");
            var ticket = new object();
            _pendingChange = ticket;
            return new(this, ticket, _state, (float)requestedRate);
        }
    }

    private void RequireTransition(NativePlaybackBinding binding, double dspSeconds, double audioSeconds, double actualPitch)
    {
        if (_state.Phase == NativePlaybackPhase.Withdrawn || _state.Binding != binding)
            throw new GameplayStateException(ErrorCode.TargetExpired, "The playback transition does not belong to the current exact clock source and run.");
        if (!PublicationCurrent(_state)) throw new GameplayStateException(ErrorCode.TargetExpired, "The native clock policy no longer belongs to this transition.");
        if (_pendingChange is not null)
        {
            WithdrawCore("A native playback transition raced a prepared value baseline.");
            throw new GameplayStateException(ErrorCode.Conflict, _state.WithdrawalReason!);
        }
        if (!AcceptDsp(dspSeconds) || !ValidPosition(audioSeconds, _state.ClipLengthSeconds)
            || !ValidRate(actualPitch) || (float)actualPitch != _state.Rate)
        {
            if (_state.Phase != NativePlaybackPhase.Withdrawn)
                WithdrawCore("A completed native playback transition has invalid media seconds or did not preserve the selected audio pitch.");
            throw new GameplayStateException(ErrorCode.InvalidGameState, _state.WithdrawalReason!);
        }
    }

    private bool AcceptDsp(double dspSeconds)
    {
        if (!ValidDsp(dspSeconds) || dspSeconds < _lastDspSeconds)
        {
            WithdrawCore("The DspTimeService clock became nonfinite, negative, or regressed.");
            return false;
        }
        _lastDspSeconds = dspSeconds;
        return true;
    }

    private void WithdrawCore(string reason)
    {
        if (_state.Phase == NativePlaybackPhase.Withdrawn) return;
        _state = _state with { Revision = NextRevision(), Phase = NativePlaybackPhase.Withdrawn, WithdrawalReason = reason };
        try { _publication?.Withdraw(reason); }
        catch (Exception error) { _state = _state with { WithdrawalReason = reason + " Native withdrawal is unknown: " + error.Message }; }
    }

    private bool PublicationCurrent(NativePlaybackClockState state)
    {
        if (_publication is null) return true;
        try { if (ReadPublicationPolicy(state) && ReferenceEquals(_state, state)) return true; }
        catch (Exception error) { WithdrawCore("The native clock policy readback failed: " + error.Message); return false; }
        WithdrawCore("The native clock policy revision/source changed or was withdrawn.");
        return false;
    }

    private void Publish(NativePlaybackClockState next)
    {
        var expected = _state;
        if (_publication is not null)
        {
            try
            {
                _publication.Publish(expected, next);
                if (!ReferenceEquals(_state, expected) || !ReadPublicationPolicy(next))
                    throw new GameplayStateException(ErrorCode.Conflict, "The native clock policy did not read back at the requested exact revision.");
            }
            catch (Exception error)
            {
                WithdrawCore("Native clock policy publication failed; synchronization is unknown: " + error.Message);
                throw;
            }
        }
        _state = next;
    }

    private long NextRevision() => checked(_state.Revision + 1);
    private static bool ValidDsp(double value) => double.IsFinite(value) && value >= 0;
    private static bool ValidPosition(double value, double length) => double.IsFinite(value) && value >= 0 && value <= length;
    internal static bool ValidRate(double value) => double.IsFinite(value) && value > 0 && float.IsFinite((float)value) && (float)value > 0;

    internal sealed class NativePlaybackClockRateChange : IDisposable
    {
        private readonly NativePlaybackClock _owner;
        private readonly object _ticket;
        private NativePlaybackClockState? _written, _restored;
        private bool _commitEntered, _restoreEntered, _disposed;
        internal NativePlaybackClockState Baseline { get; }
        internal float RequestedRate { get; }

        internal NativePlaybackClockRateChange(NativePlaybackClock owner, object ticket, NativePlaybackClockState baseline, float requestedRate)
            => (_owner, _ticket, Baseline, RequestedRate) = (owner, ticket, baseline, requestedRate);

        internal void VerifyBaseline()
        {
            lock (_owner._gate)
            {
                RequireOpen();
                if (!ReferenceEquals(_owner._state, Baseline) || !_owner.PublicationCurrent(Baseline))
                    throw new GameplayStateException(ErrorCode.Conflict, "The prepared clock baseline was replaced or withdrawn.");
            }
        }

        internal void Commit()
        {
            lock (_owner._gate)
            {
                RequireOpen();
                if (_commitEntered) throw new InvalidOperationException("A prepared clock rate may commit only once.");
                _commitEntered = true;
                VerifyBaseline();
                var next = Baseline with { Revision = _owner.NextRevision(), Rate = RequestedRate };
                _owner.Publish(next);
                _written = next;
            }
        }

        internal bool Confirm()
        {
            lock (_owner._gate)
            {
                RequireOpen();
                return _written is not null && ReferenceEquals(_owner._state, _written) && _owner.PublicationCurrent(_written);
            }
        }

        internal void Restore()
        {
            lock (_owner._gate)
            {
                RequireOpen();
                if (_restoreEntered) throw new InvalidOperationException("A prepared clock rate may restore only once.");
                _restoreEntered = true;
                if (!ReferenceEquals(_owner._state, _written ?? Baseline))
                    throw new GameplayStateException(ErrorCode.Conflict, "The clock is no longer the state written by this private change; restoration refused.");
                var next = Baseline with { Revision = _owner.NextRevision() };
                _owner.Publish(next);
                _restored = next;
            }
        }

        internal bool ConfirmRestored()
        {
            lock (_owner._gate)
            {
                RequireOpen();
                return _restoreEntered && _restored is not null && ReferenceEquals(_owner._state, _restored)
                    && (_restored with { Revision = Baseline.Revision }) == Baseline && _owner.PublicationCurrent(_restored);
            }
        }

        private void RequireOpen()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(NativePlaybackClockRateChange));
            if (!ReferenceEquals(_owner._pendingChange, _ticket))
                throw new GameplayStateException(ErrorCode.Conflict, "The playback clock reservation is no longer owned by this change.");
        }

        public void Dispose()
        {
            lock (_owner._gate)
            {
                if (_disposed) return;
                _disposed = true;
                if (ReferenceEquals(_owner._pendingChange, _ticket)) _owner._pendingChange = null;
            }
        }
    }
}
