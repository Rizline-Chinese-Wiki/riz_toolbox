namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativePlaybackStockOccurrenceProducer
{
    internal sealed partial class ExecutionReceipt
    {
        // This does not issue a second execution authority. It selects the
        // original retained journal rows after the existing producer verifies
        // its exact occurrence, leaf sequence and current fences.
        internal NativePlaybackSingleReturnBridge.FreshClockBaseline FreshClockBaseline(
            NativePlaybackStockSourceHub source, NativePlaybackSingleReturnBridge.FreshSourceSnapshot graph)
        {
            VerifyCurrent();
            if (Role is not (NativePlaybackSourceRole.Pause or NativePlaybackSourceRole.ProgressPlay)
                || !PhysicalExited || _occurrence.Children.Count != 1
                || _occurrence.Children[0].Before.Role != NativePlaybackSourceRole.PauseLeaf
                || graph.Owner != unchecked((ulong)source.NativeOwner.ToInt64()) || graph.OwnerEpoch != source.Epoch
                || graph.Player != unchecked((ulong)_owner._player.ToInt64())
                || graph.AudioSource != unchecked((ulong)_owner._source.ToInt64())
                || graph.Progress != unchecked((ulong)_owner._progress.ToInt64()))
                throw new UnsupportedSignatureException("Fresh Clock requires this producer's normal physically exited Pause and original source graph.");
            return source.SelectFreshClockBaseline(_occurrence.Root.Before, _occurrence.Children[0].Before,
                WriterSequence, SourceSequence, SchedulingSequence,
                _owner._group.StockOperationLeafCaller(Role, NativePlaybackSourceRole.PauseLeaf));
        }
    }
}

internal sealed partial class NativePlaybackStockSourceHub
{
    internal NativePlaybackSingleReturnBridge.FreshClockBaseline SelectFreshClockBaseline(
        NativePlaybackSourceObservation root, NativePlaybackSourceObservation leaf,
        ulong writer, ulong source, ulong scheduling, IntPtr caller)
    {
        VerifyThread();
        var r = WriterOrigin(root); var l = WriterOrigin(leaf);
        return new() { Version = 1, Size = 96, OwnerEpoch = Epoch,
            RootSequence = r.Sequence, RootToken = r.Token, LeafSequence = l.Sequence, LeafToken = l.Token,
            Writer = writer, Source = source, Scheduling = scheduling, LeafCaller = unchecked((ulong)caller.ToInt64()),
            RootRole = (uint)r.Role, RootSlot = r.Slot, LeafSlot = l.Slot };
    }
}
