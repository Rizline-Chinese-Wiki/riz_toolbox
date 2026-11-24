using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativePlaybackClock
{
    /// <summary>Reserves only the clock part of a paused Seek. The composed Seek must
    /// separately write and read back audio/progress/note state before committing this
    /// publication. A position change never issues native setter or source authority.</summary>
    internal NativePlaybackClockPositionChange PreparePausedPosition(NativePlaybackBinding binding,
        double mediaSeconds)
    {
        lock (_gate)
        {
            if (!ValidPosition(mediaSeconds, _state.ClipLengthSeconds)
                || !float.IsFinite((float)mediaSeconds)
                || !ValidPosition((float)mediaSeconds, _state.ClipLengthSeconds))
                throw new GameplayStateException(ErrorCode.InvalidArgument,
                    "The Seek media position must be finite and inside the actual clip in the native Single domain.");
            if (_state.Binding != binding || _state.Phase != NativePlaybackPhase.Paused)
                throw new GameplayStateException(ErrorCode.InvalidGameState,
                    "A position change requires this exact paused playback graph.");
            if (!PublicationCurrent(_state))
                throw new GameplayStateException(ErrorCode.TargetExpired,
                    "The native clock policy no longer owns this paused Seek baseline.");
            // Rate and position reserve the same baseline. Neither may prepare around
            // the other's pending audio writes, even when its numeric value is equal.
            if (_pendingChange is not null)
                throw new GameplayStateException(ErrorCode.Conflict,
                    "Another prepared playback clock change owns this baseline.");
            var ticket = new object();
            _pendingChange = ticket;
            return new(this, ticket, _state, (float)mediaSeconds);
        }
    }

    internal sealed class NativePlaybackClockPositionChange : IDisposable
    {
        private readonly NativePlaybackClock _owner;
        private readonly object _ticket;
        private NativePlaybackClockState? _written, _restored;
        private bool _commitEntered, _restoreEntered, _disposed;
        internal NativePlaybackClockState Baseline { get; }
        internal float RequestedMediaSeconds { get; }

        internal NativePlaybackClockPositionChange(NativePlaybackClock owner, object ticket,
            NativePlaybackClockState baseline, float mediaSeconds)
            => (_owner, _ticket, Baseline, RequestedMediaSeconds) = (owner, ticket, baseline, mediaSeconds);

        internal void VerifyBaseline()
        {
            lock (_owner._gate)
            {
                RequireOpen();
                if (!ReferenceEquals(_owner._state, Baseline) || !_owner.PublicationCurrent(Baseline))
                    throw new GameplayStateException(ErrorCode.Conflict,
                        "The prepared Seek clock baseline was replaced or withdrawn.");
            }
        }

        internal void Commit()
        {
            lock (_owner._gate)
            {
                RequireOpen();
                if (_commitEntered || _restoreEntered)
                    throw new InvalidOperationException("A prepared clock position may commit only once and before restoration.");
                _commitEntered = true;
                VerifyBaseline();
                // Keep the selected rate and the monotonic DSP watermark. Seeking
                // backwards changes media time; it must never rewind the DSP clock.
                var next = Baseline with
                {
                    Revision = _owner.NextRevision(), MediaAnchorSeconds = RequestedMediaSeconds
                };
                _owner.Publish(next);
                _written = next;
            }
        }

        internal bool Confirm()
        {
            lock (_owner._gate)
            {
                RequireOpen();
                return !_restoreEntered && _written is not null
                    && ReferenceEquals(_owner._state, _written) && _owner.PublicationCurrent(_written);
            }
        }

        internal void Restore()
        {
            lock (_owner._gate)
            {
                RequireOpen();
                if (_restoreEntered) throw new InvalidOperationException("A prepared clock position may restore only once.");
                _restoreEntered = true;
                var expected = _written ?? Baseline;
                if (!ReferenceEquals(_owner._state, expected) || !_owner.PublicationCurrent(expected))
                    throw new GameplayStateException(ErrorCode.Conflict,
                        "The Seek clock no longer belongs to this private change; restoration refused.");
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
                    && (_restored with { Revision = Baseline.Revision }) == Baseline
                    && _owner.PublicationCurrent(_restored);
            }
        }

        private void RequireOpen()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(NativePlaybackClockPositionChange));
            if (!ReferenceEquals(_owner._pendingChange, _ticket))
                throw new GameplayStateException(ErrorCode.Conflict,
                    "The playback clock reservation is no longer owned by this position change.");
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
