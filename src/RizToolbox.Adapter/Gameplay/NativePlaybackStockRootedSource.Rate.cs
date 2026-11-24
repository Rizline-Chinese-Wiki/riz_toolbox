using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativePlaybackStockRootedSource
{
    private PitchWrite? _pitchWriter;

    internal void VerifyCurrentOperationReceipt(ReadbackReceipt receipt)
    {
        Open();
        if (_pitchWriter is { } writer)
        {
            writer.VerifyOperationIdentity();
            if (!ReferenceEquals(writer.ObserveReadbackIdentity(), receipt))
                throw new GameplayStateException(ErrorCode.TargetExpired, "The registered pitch operation owns another readback identity.");
        }
        else if (_timeWriter is { } time)
        {
            time.VerifyOperationIdentity();
            if (!ReferenceEquals(time.ObserveReadbackIdentity(), receipt))
                throw new GameplayStateException(ErrorCode.TargetExpired, "The registered time operation owns another readback identity.");
        }
        else receipt.VerifyOperationIdentity();
    }

    internal PitchWrite PreparePausedPitchWrite(ReadbackReceipt expected, Action? verifySourceOperation = null)
    {
        var operation = EnterOperation();
        var preparing = false;
        try
        {
            BeginValueWriterPreparation(); preparing = true;
            verifySourceOperation?.Invoke();
            expected.VerifyCurrent();
            if (!ReferenceEquals(_current, expected) || !expected.PhysicalExited
                || expected.Role is not (NativePlaybackSourceRole.Pause or NativePlaybackSourceRole.ProgressPlay))
                throw new GameplayStateException(ErrorCode.NotReady, "A pitch writer requires this source's exact physically completed Pause receipt.");
            var baseline = expected.Read().Sample;
            RequirePausedSeek(baseline);
            expected.VerifyOperationIdentity();
            // A baseline getter may dispose or replace the captured source.
            // Do not register a writer that that caller can no longer receive.
            verifySourceOperation?.Invoke();
            return _pitchWriter = new(this, expected, baseline, operation, verifySourceOperation);
        }
        catch { operation.Dispose(); throw; }
        finally { if (preparing) _preparingValueWriter = false; }
    }

    private void RejectPendingPitchReadback(ReadbackReceipt receipt)
    {
        if (_pitchWriter is { PendingRead: true } && ReferenceEquals(_current, receipt))
            throw new GameplayStateException(ErrorCode.NotReady,
                "The completed pitch write has no current API receipt; only its registered private writer may read or restore it.");
    }

    /// <summary>Retains the exact completed native continuation before the first
    /// API read, along with an operation pin over the original source. A failed
    /// getter cannot erase recovery responsibility or authorize a new command.</summary>
    internal sealed class PitchWrite : INativePlaybackPitchWrite, INativePlaybackPitchRecovery, INativePlaybackReleaseGuard
    {
        private readonly NativePlaybackStockRootedSource _owner;
        private readonly PitchWrite _registered;
        private readonly ReadbackReceipt _initial;
        private readonly NativePlaybackRateSample _baseline;
        private readonly IDisposable _operation;
        private readonly Action? _verifySourceOperation;
        private NativePlaybackStockOccurrenceProducer.ContinuationReceipt? _continuation;
        private ReadbackReceipt _current;
        private bool _pendingRead, _disposed, _busy, _restoreAttempted, _restored;

        internal PitchWrite(NativePlaybackStockRootedSource owner, ReadbackReceipt initial,
            NativePlaybackRateSample baseline, IDisposable operation, Action? verifySourceOperation = null)
            => (_owner, _registered, _initial, _current, _baseline, _operation, _verifySourceOperation)
                = (owner, this, initial, initial, baseline, operation, verifySourceOperation);

        internal bool PendingRead => _pendingRead;

        private void VerifyRegistration()
        {
            _owner.VerifyThread();
            if (!ReferenceEquals(_registered, this) || !ReferenceEquals(_owner._pitchWriter, this) || _disposed)
                throw new GameplayStateException(ErrorCode.TargetExpired, "Only this source's registered pitch writer owns its operation or recovery.");
        }

        // Identity selection only; consumers must verify the actual continuation
        // rather than attempting to consume this old receipt while readback waits.
        internal ReadbackReceipt ObserveReadbackIdentity() { VerifyRegistration(); return _current; }

        internal void VerifyOperationIdentity()
        {
            VerifyRegistration(); _owner.Open();
            if (!ReferenceEquals(_owner._current, _current))
                throw new GameplayStateException(ErrorCode.TargetExpired, "Another stock occurrence replaced the prepared pitch writer.");
            _verifySourceOperation?.Invoke();
            try
            {
                if (_continuation is { } continuation) continuation.VerifyCurrent();
                else _initial.VerifyOperationIdentity();
                _owner.Open();
                if (!ReferenceEquals(_owner._current, _current))
                    throw new GameplayStateException(ErrorCode.TargetExpired, "The pitch writer changed during native fence verification.");
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
            if (!SameBaselineExceptPitch(actual.Sample))
                throw new GameplayStateException(ErrorCode.Conflict, "The pitch writer's prepared audio/progress/offset/origin/schedule baseline changed.");
            var expectedPitch = _continuation?.Pitch ?? _baseline.Pitch;
            if (actual.Sample.Pitch != expectedPitch)
                throw new GameplayStateException(ErrorCode.ObservationUnavailable, "The actual API pitch readback differs from the completed native write.");
            if (_pendingRead)
            {
                _current = _initial.Succeed(_owner, _continuation!, actual.Sample, actual.AfterDsp, _owner._issuer);
                _owner._current = _current;
                _owner._selectedRate = actual.Sample.Pitch;
                _pendingRead = false;
            }
            if (_restoreAttempted && expectedPitch == _baseline.Pitch) _restored = true;
            VerifyOperationIdentity();
            return actual.Sample;
        }

        private bool SameBaselineExceptPitch(NativePlaybackRateSample sample)
            => sample.DspSeconds >= _baseline.DspSeconds
                && (sample with { DspSeconds = _baseline.DspSeconds, Pitch = _baseline.Pitch }) == _baseline;

        public void Write(float pitch)
        {
            VerifyRegistration();
            if (_busy) throw new GameplayStateException(ErrorCode.MutationReentrancy, "A pitch write cannot reenter itself.");
            if (_pendingRead || _restoreAttempted)
                throw new GameplayStateException(ErrorCode.Conflict, "An unconfirmed or restored pitch attempt cannot accept another requested write.");
            if (!NativePlaybackClock.ValidRate(pitch))
                throw new GameplayStateException(ErrorCode.InvalidArgument, "The native pitch writer requires a positive finite Single playback rate.");
            _busy = true;
            try
            {
                VerifyCurrent(); _ = Read();
                WriteNativeAndRead(pitch);
            }
            finally { _busy = false; }
        }

        private void WriteNativeAndRead(float pitch)
        {
            try
            {
                _continuation = _initial.BeginPitchWrite(this, _continuation, pitch);
                _pendingRead = true;
            }
            catch (Exception error)
            {
                // No returned continuation means there is no authority to
                // guess a post-store fence or retry an abnormal native call.
                _owner.Fail(error); throw;
            }
            _ = Read(); // A getter failure retains the actual continuation.
        }

        public void RestoreOriginal()
        {
            VerifyRegistration();
            if (_busy) throw new GameplayStateException(ErrorCode.MutationReentrancy, "A running pitch write cannot restore itself.");
            if (_restoreAttempted) throw new InvalidOperationException("The original pitch can be restored only once.");
            _restoreAttempted = true;
            _busy = true;
            try
            {
                VerifyCurrent();
                if (_continuation is null)
                {
                    if (Read().Pitch != _baseline.Pitch)
                        throw new GameplayStateException(ErrorCode.Conflict, "Unwritten pitch differs from its actual baseline.");
                    return;
                }
                VerifyRecoveryBaseline();
                WriteNativeAndRead(_baseline.Pitch);
            }
            finally { _busy = false; }
        }

        private void VerifyRecoveryBaseline()
        {
            VerifyCurrent();
            if (_continuation!.WrittenRole != NativePlaybackSourceRole.SetPitch
                || _continuation.SchedulingSequence != checked((ulong)_baseline.SchedulingRevision)
                || checked((long)_owner._group.Epoch) != _baseline.AuthorityRevision)
                throw new GameplayStateException(ErrorCode.TargetExpired, "The retained pitch continuation no longer owns the original source schedule.");
            if (!_pendingRead)
            {
                // A confirmed write can only be undone while its exact pitch
                // and paused baseline still read back from the retained graph.
                _ = Read();
                return;
            }
            // A completed write with a failed first readback still has its
            // private continuation. Check the non-pitch baseline before the
            // inverse and independently confirm the original pitch afterwards.
            _owner.AcceptDsp(_owner._actual.VerifyPausedPitchRecoveryBaseline(_baseline));
            _owner.AcceptDsp(_owner._actual.ReadDsp());
            VerifyCurrent();
        }

        public void VerifyReleaseAllowed()
        {
            _owner.VerifyThread();
            if (!ReferenceEquals(_registered, this))
                throw new GameplayStateException(ErrorCode.TargetExpired, "A copied pitch writer owns no source operation.");
            if (_disposed) return;
            VerifyRegistration();
            if (_busy) throw new GameplayStateException(ErrorCode.MutationReentrancy, "A running pitch write cannot release its source operation.");
        }

        public void Dispose()
        {
            VerifyReleaseAllowed();
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (_pendingRead || _restoreAttempted && !_restored)
                    _owner.Fail(new GameplayStateException(ErrorCode.ObservationUnavailable,
                        "A completed pitch write ended without actual API readback or confirmed restoration."));
            }
            finally
            {
                _owner._pitchWriter = null; _operation.Dispose();
                if (_owner._disposed) _owner.PumpCompleted();
            }
        }
    }

    internal sealed partial class ReadbackReceipt
    {
        internal NativePlaybackStockOccurrenceProducer.ContinuationReceipt BeginPitchWrite(PitchWrite writer,
            NativePlaybackStockOccurrenceProducer.ContinuationReceipt? predecessor, float pitch)
        {
            writer.VerifyCurrent();
            if (!ReferenceEquals(_owner._pitchWriter, writer))
                throw new GameplayStateException(ErrorCode.TargetExpired, "Only the registered pitch writer may consume this Pause receipt.");
            if (predecessor is null) return WriteNative(_owner._producer, pitch);
            if (_owner._freshAdoption is { } fresh)
                return fresh.WritePitch(_owner, _owner._producer, _execution, predecessor, pitch);
            return predecessor.WritePitch(pitch);
        }
    }
}
