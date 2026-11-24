using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativePlaybackStockRootedSource
{
    private TimeWrite? _timeWriter;

    internal bool OwnsReservedTimeBefore(NativePlaybackSourceObservation observation)
    {
        VerifyThread();
        return !_disposed && _fault is null && _timeWriter is not null
            && _producer.OwnsReservedTimeBefore(observation);
    }

    internal TimeWrite PreparePausedTimeWrite(Action? verifySourceOperation = null)
    {
        var scope = EnterOperation();
        var preparing = false;
        try
        {
            BeginValueWriterPreparation(); preparing = true;
            verifySourceOperation?.Invoke();
            var receipt = ObserveCurrentReadbackIdentity()
                ?? throw new GameplayStateException(ErrorCode.NotReady, "Seek needs an actual completed Pause.");
            receipt.VerifyOperationIdentity();
            if (!ReferenceEquals(_current, receipt) || !receipt.ReadOperationPhysicalExit()
                || receipt.Role is not (NativePlaybackSourceRole.Pause or NativePlaybackSourceRole.ProgressPlay))
                throw new GameplayStateException(ErrorCode.NotReady, "Seek requires this exact physically exited Pause receipt.");
            var baseline = receipt.Read().Sample;
            RequirePausedSeek(baseline);
            receipt.VerifyOperationIdentity();
            // Preparation getters may dispose or replace the captured source.
            // Recheck its registered owner before creating a writer that no
            // caller could receive or release after that preparation failed.
            verifySourceOperation?.Invoke();
            return _timeWriter = new(this, receipt, baseline, scope, verifySourceOperation);
        }
        catch { scope.Dispose(); throw; }
        finally { if (preparing) _preparingValueWriter = false; }
    }

    private void RejectPendingTimeReadback(ReadbackReceipt receipt)
    {
        if (_timeWriter is { PendingRead: true } && ReferenceEquals(_current, receipt))
            throw new GameplayStateException(ErrorCode.NotReady,
                "The completed time write has no current API receipt; only its registered private writer may read or restore it.");
    }

    private static void RequirePausedSeek(NativePlaybackRateSample sample)
    {
        if (!sample.Paused || sample.AudioPlaying || sample.ProgressPlaying || sample.Preparing
            || !sample.TimeReady || sample.LevelOver || sample.Crashed || sample.HasStockOrigin)
            throw new GameplayStateException(ErrorCode.InvalidGameState,
                "Seek requires paused, ready audio/progress with no scheduled start or ended run.");
    }

    /// <summary>Holds the actual source operation across all Seek participants.
    /// Native completion is retained before API readback: a transient getter failure
    /// cannot erase the only continuation capable of restoring the audio position.</summary>
    internal sealed class TimeWrite : INativePlaybackTimeWrite
    {
        private readonly NativePlaybackStockRootedSource _owner;
        private readonly TimeWrite _registered;
        private readonly ReadbackReceipt _initial;
        private readonly NativePlaybackRateSample _baseline;
        private readonly IDisposable _operation;
        private readonly Action? _verifySourceOperation;
        private NativePlaybackStockOccurrenceProducer.ContinuationReceipt? _continuation;
        private ReadbackReceipt _current;
        private bool _pendingRead, _disposed, _busy, _restoreAttempted, _restored;

        internal TimeWrite(NativePlaybackStockRootedSource owner, ReadbackReceipt initial,
            NativePlaybackRateSample baseline, IDisposable operation, Action? verifySourceOperation = null)
            => (_owner, _registered, _initial, _current, _baseline, _operation, _verifySourceOperation)
                = (owner, this, initial, initial, baseline, operation, verifySourceOperation);

        internal bool PendingRead => _pendingRead;

        // Selects only this registered writer's current receipt identity. A
        // pending native continuation carries authority until a new actual
        // API read succeeds; the old receipt itself remains unreadable.
        internal ReadbackReceipt ObserveReadbackIdentity() { VerifyRegistration(); return _current; }

        private void VerifyRegistration()
        {
            _owner.VerifyThread();
            if (_disposed || !ReferenceEquals(_registered, this) || !ReferenceEquals(_owner._timeWriter, this))
                throw new GameplayStateException(ErrorCode.TargetExpired, "The time writer is no longer registered with this exact source.");
        }

        internal void VerifyOperationIdentity()
        {
            VerifyRegistration(); _owner.Open();
            // A stock occurrence may replace _current even with identical values.
            if (!ReferenceEquals(_owner._current, _current))
                throw new GameplayStateException(ErrorCode.TargetExpired, "Another stock occurrence replaced the prepared time writer.");
            // The bound source checks its actual route/adoption/Clock at every
            // use, including after a graph getter and before the native store.
            // It selects this writer's successor but cannot issue one itself.
            _verifySourceOperation?.Invoke();
            try
            {
                if (_continuation is { } continuation) continuation.VerifyCurrent();
                else _initial.VerifyOperationIdentity();
                _owner.Open();
                if (!ReferenceEquals(_owner._current, _current))
                    throw new GameplayStateException(ErrorCode.TargetExpired, "The time writer changed during native fence verification.");
            }
            catch (Exception error) { _owner.Fail(error); throw; }
            _verifySourceOperation?.Invoke();
        }

        public void VerifyCurrent()
        {
            VerifyOperationIdentity();
            _owner.VerifyGraph();
            VerifyOperationIdentity();
        }

        internal NativePlaybackRateSample Read()
        {
            VerifyOperationIdentity();
            var actual = _owner.ReadVerified(VerifyOperationIdentity,
                _continuation?.SchedulingSequence ?? checked((ulong)_baseline.SchedulingRevision));
            RequirePausedSeek(actual.Sample);
            if (actual.Sample.Binding != _baseline.Binding || actual.Sample.AuthorityRevision != _baseline.AuthorityRevision
                || actual.Sample.Pitch != _baseline.Pitch || actual.Sample.ClipLengthSeconds != _baseline.ClipLengthSeconds
                || actual.Sample.AppliedOffsetSeconds != _baseline.AppliedOffsetSeconds
                || actual.Sample.DspSeconds < _baseline.DspSeconds)
                throw new GameplayStateException(ErrorCode.Conflict, "Seek changed the prepared source, rate, clip or applied offset.");
            if (_pendingRead)
            {
                // Issue the successor readback only after the actual API succeeded.
                // Before this point the private continuation, not a cached sample,
                // carries recovery responsibility.
                _current = _initial.Succeed(_owner, _continuation!, actual.Sample, actual.AfterDsp, _owner._issuer);
                _owner._current = _current; _pendingRead = false;
            }
            if (_restoreAttempted && actual.Sample.AudioPositionSeconds == _baseline.AudioPositionSeconds) _restored = true;
            VerifyOperationIdentity();
            return actual.Sample;
        }

        public void Write(float mediaSeconds)
        {
            VerifyRegistration();
            if (_busy) throw new GameplayStateException(ErrorCode.MutationReentrancy, "A time write cannot reenter itself.");
            if (_pendingRead || _restoreAttempted)
                throw new GameplayStateException(ErrorCode.Conflict, "An unconfirmed or restored time attempt cannot accept another requested write.");
            if (!float.IsFinite(mediaSeconds) || mediaSeconds < 0 || mediaSeconds > _baseline.ClipLengthSeconds)
                throw new GameplayStateException(ErrorCode.InvalidArgument, "Seek media seconds must be inside the retained clip.");
            _busy = true;
            try
            {
                VerifyCurrent();
                var actual = Read();
                // A later participant can legitimately change progress, so
                // ordinary reads allow it. A new native media store must still
                // start on the original non-media baseline of this writer.
                if ((actual with { DspSeconds = _baseline.DspSeconds, AudioPositionSeconds = _baseline.AudioPositionSeconds,
                    SchedulingRevision = _baseline.SchedulingRevision }) != _baseline)
                    throw new GameplayStateException(ErrorCode.Conflict, "The paused time writer's non-media baseline changed before its native store.");
                var ownedMediaSeconds = _continuation is null ? _baseline.AudioPositionSeconds : _current.Sample.AudioPositionSeconds;
                if (actual.AudioPositionSeconds != ownedMediaSeconds)
                    throw new GameplayStateException(ErrorCode.Conflict, "The paused audio position changed since this time writer last observed it.");
                WriteNativeAndRead(mediaSeconds);
            }
            finally { _busy = false; }
        }

        private void WriteNativeAndRead(float mediaSeconds)
        {
            try
            {
                _continuation = _initial.BeginTimeWrite(this, _continuation, mediaSeconds);
                _pendingRead = true;
            }
            catch (Exception error)
            {
                // No returned private continuation means that a partial native
                // effect cannot be recovered by guessing the new fence.
                _owner.Fail(error); throw;
            }
            _ = Read(); // Deliberately outside the native-failure catch.
        }

        public void RestoreOriginal()
        {
            VerifyRegistration();
            if (_busy) throw new GameplayStateException(ErrorCode.MutationReentrancy, "A running time write cannot restore itself.");
            if (_restoreAttempted) throw new InvalidOperationException("The original media position can be restored only once.");
            _restoreAttempted = true;
            _busy = true;
            try
            {
                VerifyCurrent();
                if (_continuation is null)
                {
                    // No native write returned. Only an unchanged actual baseline
                    // is accepted; there is no private successor from which to write.
                    if (Read().AudioPositionSeconds != _baseline.AudioPositionSeconds)
                        throw new GameplayStateException(ErrorCode.Conflict, "Unwritten Seek audio differs from its captured baseline.");
                    return;
                }
                // The continuation proves only this native write. Independently
                // retain every other paused baseline before the one inverse store.
                _owner.AcceptDsp(_owner._actual.VerifyPausedTimeRecoveryBaseline(_baseline));
                _owner.AcceptDsp(_owner._actual.ReadDsp());
                VerifyCurrent();
                if (!_pendingRead)
                {
                    var actual = Read();
                    if (actual.AudioPositionSeconds == _baseline.AudioPositionSeconds) return;
                    // TimeSeconds is the exact setter argument, not an AudioSource
                    // getter result. Keep recovery for this same private successor's
                    // independently completed, possibly rounded media readback.
                    if (_continuation.WrittenRole != NativePlaybackSourceRole.SetTime
                        || actual.AudioPositionSeconds != _current.Sample.AudioPositionSeconds)
                        throw new GameplayStateException(ErrorCode.Conflict, "Audio no longer matches this time writer's independent completed readback.");
                }
                // The actual continuation permits one inverse even when its first
                // API media read failed. The inverse needs new complete readback.
                WriteNativeAndRead(_baseline.AudioPositionSeconds);
                if (Read().AudioPositionSeconds != _baseline.AudioPositionSeconds)
                    throw new GameplayStateException(ErrorCode.ObservationUnavailable, "The restored audio position did not read back as the original position.");
            }
            finally { _busy = false; }
        }

        public void Dispose()
        {
            _owner.VerifyThread();
            if (!ReferenceEquals(_registered, this))
                throw new GameplayStateException(ErrorCode.TargetExpired, "A copied time writer owns no source operation.");
            if (_disposed) return;
            if (!ReferenceEquals(_owner._timeWriter, this))
                throw new GameplayStateException(ErrorCode.TargetExpired, "A foreign time writer cannot release this source.");
            if (_busy) throw new GameplayStateException(ErrorCode.MutationReentrancy, "A running time write cannot release its retained source.");
            _disposed = true;
            try
            {
                if (_pendingRead || _restoreAttempted && !_restored)
                    _owner.Fail(new GameplayStateException(ErrorCode.ObservationUnavailable,
                        "A completed time write ended without successful actual API readback or restoration."));
            }
            finally { _owner._timeWriter = null; _operation.Dispose(); }
        }
    }

    internal sealed partial class ReadbackReceipt
    {
        internal NativePlaybackStockOccurrenceProducer.ContinuationReceipt BeginTimeWrite(TimeWrite writer,
            NativePlaybackStockOccurrenceProducer.ContinuationReceipt? predecessor, float mediaSeconds)
        {
            writer.VerifyCurrent();
            if (!ReferenceEquals(_owner._timeWriter, writer))
                throw new GameplayStateException(ErrorCode.TargetExpired, "Only the registered time writer may consume this Pause receipt.");
            if (predecessor is not null) return predecessor.WriteTime(mediaSeconds);
            return _writer is { } prior ? prior.WriteTime(mediaSeconds) : _owner._producer.WriteTime(_execution, mediaSeconds);
        }
    }
}
