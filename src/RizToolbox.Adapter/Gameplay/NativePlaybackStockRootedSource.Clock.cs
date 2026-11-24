using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativePlaybackStockRootedSource
{
    internal sealed partial class ReadbackReceipt
    {
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
