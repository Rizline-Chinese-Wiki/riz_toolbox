using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativePlaybackStockRootedSource
{
    // The countdown only carries the unchanged paused policy across the Play
    // return. The next stock operation still has to produce its own readback.
    internal sealed record ResumeCountdown(NativePlaybackSourceObservation Observation, NativePlaybackClockState State);

    internal ResumeCountdown? ObserveResumeCountdownIdentity(NativePlaybackClockState state)
    {
        Open();
        return ReferenceEquals(_resumeCountdown?.State, state) ? _resumeCountdown : null;
    }

    internal void VerifyResumeCountdown(ResumeCountdown countdown)
    {
        Open();
        if (!ReferenceEquals(_resumeCountdown, countdown))
            throw new GameplayStateException(ErrorCode.TargetExpired, "The current paused Clock no longer owns this resume countdown.");
        _producer.VerifyResumeCountdown(countdown.Observation);
    }

    internal double ReadResumeCountdownDsp(ResumeCountdown countdown)
    {
        try
        {
            var (sample, _) = ReadVerified(() => VerifyResumeCountdown(countdown), countdown.Observation.SchedulingSequence);
            var state = countdown.State;
            if (sample.Binding != state.Binding || sample.AudioPlaying || sample.HasStockOrigin || sample.LevelOver || sample.Crashed
                || sample.AudioPositionSeconds != state.MediaAnchorSeconds || sample.Pitch != state.Rate
                || sample.ClipLengthSeconds != state.ClipLengthSeconds)
                throw new GameplayStateException(ErrorCode.ObservationUnavailable, "The audio source changed while the paused Clock was waiting for the stock Resume schedule.");
            return sample.DspSeconds;
        }
        catch (Exception error)
        {
            if (ReferenceEquals(_resumeCountdown, countdown)) Fail(error);
            throw;
        }
    }

    internal sealed partial class ReadbackReceipt
    {
        internal bool MatchesPauseBeforeCountdown(NativePlaybackSourceObservation observation, NativePlaybackClockState state)
        {
            var scheduling = _writer?.SchedulingSequence ?? _execution.SchedulingSequence;
            return Role is NativePlaybackSourceRole.Pause or NativePlaybackSourceRole.ProgressPlay
                && Sample.Binding == state.Binding && Sample.AudioPositionSeconds == state.MediaAnchorSeconds
                && Sample.Pitch == state.Rate && Sample.ClipLengthSeconds == state.ClipLengthSeconds
                && observation.WriterSequence == (_writer?.WriterSequence ?? _execution.WriterSequence)
                && observation.SourceSequence == (_writer?.SourceSequence ?? _execution.SourceSequence)
                && scheduling != ulong.MaxValue && observation.SchedulingSequence == scheduling + 1;
        }

        internal NativePlaybackStockOccurrenceProducer.ExecutionReceipt FreshClockExecution(
            NativePlaybackFreshSourceCapture.Adoption adoption, NativePlaybackStockRootedSource source)
        {
            if (!ReferenceEquals(_owner, source) || !ReferenceEquals(_owner._freshAdoption, adoption) || _writer is not null)
                throw new GameplayStateException(ErrorCode.TargetExpired, "Fresh Clock requires this adopted source's original stock execution.");
            adoption.VerifySource(_owner);
            VerifyCurrent();
            var sample = Read().Sample;
            if (!PhysicalExited || Role is not (NativePlaybackSourceRole.Pause or NativePlaybackSourceRole.ProgressPlay)
                || !sample.Paused || sample.AudioPlaying || sample.ProgressPlaying || sample.Preparing || !sample.TimeReady
                || sample.LevelOver || sample.Crashed || sample.HasStockOrigin)
                throw new GameplayStateException(ErrorCode.NotReady, "Fresh Clock requires the exact physically exited ready Pause and seven-root API baseline.");
            VerifyCurrent();
            return _execution;
        }
    }
}
