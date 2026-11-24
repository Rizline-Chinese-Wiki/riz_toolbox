using RizToolbox.Adapter.Judgements;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekGameStatePreparation
{
    private NativeJudgementBackend.SeekHoldEpoch _holdEpoch = null!;
    private HoldEpochParticipant? _epochParticipant;

    // The first participant stops the actual predecessor before any time or
    // game-state writes. Reverse recovery resumes it only after all of those
    // values have been restored. Its epoch retains independent actual roots.
    internal IGameplayValueParticipant CaptureHoldEpochParticipant()
    {
        Registered();
        if (_disposed || _entered || _restoring || _holdEpoch is null)
            throw new InvalidOperationException("The original Hold epoch preparation is no longer transferable.");
        if (_epochParticipant is not null || !_owners.Contains(_holdEpoch))
            throw new InvalidOperationException("The original Hold epoch has already been registered with its transaction.");
        var participant = new HoldEpochParticipant(this, _holdEpoch);
        _epochParticipant = participant; _owners.Remove(_holdEpoch);
        return participant;
    }

    private sealed class HoldEpochParticipant : IGameplayValueParticipant
    {
        private readonly HoldEpochParticipant _registered;
        private readonly NativeSeekGameStatePreparation _owner;
        private readonly NativeJudgementBackend.SeekHoldEpoch _epoch;
        private bool _entered, _restoring, _disposed;
        internal HoldEpochParticipant(NativeSeekGameStatePreparation owner, NativeJudgementBackend.SeekHoldEpoch epoch)
            => (_registered, _owner, _epoch) = (this, owner, epoch);
        public string Id => "native.seek.hold-epoch";
        private void Registered()
        {
            _owner.Registered();
            if (!ReferenceEquals(_registered, this) || !ReferenceEquals(_owner._epochParticipant, this)
                || !ReferenceEquals(_owner._holdEpoch, _epoch))
                throw new InvalidOperationException("The actual Hold epoch belongs to another transaction participant.");
        }
        private void Open()
        {
            Registered();
            if (_disposed) throw new ObjectDisposedException(nameof(HoldEpochParticipant));
        }
        public void VerifyPrepared()
        {
            Open();
            if (_entered || _restoring) throw new InvalidOperationException("The actual Hold epoch was already entered.");
            _epoch.VerifyPrepared();
        }
        public void Commit()
        {
            VerifyPrepared(); _entered = true; _epoch.StopPredecessors();
        }
        internal void VerifyStoppedForReconstruction()
        {
            Open();
            if (!_entered || _restoring)
                throw new InvalidOperationException("The registered transaction has not stopped its predecessor Hold epoch.");
            _epoch.VerifyPredecessorsStopped();
        }
        public GameplayControlConfirmation Confirm()
        {
            Open();
            if (!_entered || _restoring) throw new InvalidOperationException("The actual Hold epoch has not committed.");
            return new(_epoch.VerifyTarget(), "The original Hold receiver completed its controlled Stop and target continuations have their actual observed Start, iterator, input and counter state.");
        }
        public void Restore()
        {
            Open();
            if (_restoring) throw new InvalidOperationException("The actual Hold epoch restoration is once-only.");
            _restoring = true;
            if (!_entered) { _epoch.VerifyPrepared(); return; }
            _epoch.StopTargetHolds();
            if (!_owner.ConfirmRestored().OriginalStateRestored)
                throw new InvalidOperationException("The original Hold iterators cannot restart before actual game-state recovery has read back.");
            _epoch.RestorePredecessors();
        }
        public GameplayControlRestoration ConfirmRestored()
        {
            Open();
            if (!_entered)
            {
                _epoch.VerifyPrepared();
                return new(true, "The unattempted original Hold epoch still has its actual paused baseline.");
            }
            if (!_restoring) throw new InvalidOperationException("Attempt the actual Hold epoch inverse before readback.");
            return new(_epoch.VerifyRestored(), "The original stopped iterators were rescheduled only after audio, progress and game-state recovery, with independent original iterator, input and counter readback.");
        }
        public void Dispose()
        {
            Registered();
            if (_disposed) return;
            _disposed = true; _epoch.Dispose();
        }
    }
}
