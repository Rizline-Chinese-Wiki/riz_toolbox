using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

// Internal read data retains the exact immutable state object for final validation.
// Neither this data nor a copied binding issues source, callback or write authority.
internal sealed record NativePlaybackClockReadout(NativePlaybackClock Clock,
    NativePlaybackClockState State, double DspSeconds, float MediaSeconds)
{
    internal NativePlaybackStockRateProvider? Issuer { get; init; }
    internal NativePlaybackStockRootedSource.ReadbackReceipt? SourceReceipt { get; init; }
}

internal sealed partial class NativePlaybackClock
{
    /// <summary>The registered session reader supplies its independently verified source
    /// sample and exact expected state. TryGetTime remains the callback-only return path.
    /// SDK reads use this separate path and never advance its DSP watermark, publish,
    /// or withdraw the Clock. Session/root/receipt authority is checked by the caller.</summary>
    internal NativePlaybackClockReadout ReadPublished(NativePlaybackClockState expected, double dspSeconds)
    {
        lock (_gate)
        {
            if (_publication is null)
                throw new GameplayStateException(ErrorCode.NotReady, "A native playback read requires an actual publication.");
            if (!ReferenceEquals(_state, expected) || expected.Phase == NativePlaybackPhase.Withdrawn)
                throw new GameplayStateException(ErrorCode.TargetExpired, "The exact playback Clock revision ended during the read.");
            if (!ValidDsp(dspSeconds) || dspSeconds < _lastDspSeconds)
                throw new GameplayStateException(ErrorCode.ObservationUnavailable, "The source DSP sample is nonfinite, negative or older than the Clock watermark.");
            // The registered reader has just checked its actual roots and source
            // receipt. Policy ownership still requires a fresh native read here.
            if (!ReadPublicationPolicy(expected) || !ReferenceEquals(_state, expected))
                throw new GameplayStateException(ErrorCode.TargetExpired, "The expected native publication is no longer current.");
            if (!TryCalculateMediaSeconds(expected, dspSeconds, out var mediaSeconds))
                throw new GameplayStateException(ErrorCode.ObservationUnavailable, "The playback media sample exceeds the finite Single return domain.");
            // The concrete native publisher cannot call back during the pure
            // calculation above. Other publishers may reenter IsCurrent, so
            // retain their second fence and inspect the state after it returns.
            if ((_publication is not NativePlaybackClockPublication && !ReadPublicationPolicy(expected))
                || !ReferenceEquals(_state, expected))
                throw new GameplayStateException(ErrorCode.TargetExpired, "The native publication changed while sampling playback media.");
            return new(this, expected, dspSeconds, mediaSeconds);
        }
    }
    private bool ReadPublicationPolicy(NativePlaybackClockState state)
        => _publication is NativePlaybackClockPublication native
            ? native.IsCurrentOperationPolicy(state) : _publication!.IsCurrent(state);

    internal bool HasExactReadState(NativePlaybackClockState expected)
    {
        lock (_gate) return ReferenceEquals(_state, expected) && _state.Phase != NativePlaybackPhase.Withdrawn;
    }

    private static bool TryCalculateMediaSeconds(NativePlaybackClockState state, double dspSeconds, out float mediaSeconds)
    {
        mediaSeconds = default;
        var delta = dspSeconds - state.DspAnchorSeconds;
        var value = state.Phase switch
        {
            NativePlaybackPhase.Paused => state.MediaAnchorSeconds,
            // Scheduled delay is unscaled until the actual start. Preserve the
            // callback's exact Single-domain calculation, including clip clamp.
            NativePlaybackPhase.Scheduled when delta < 0 => state.MediaAnchorSeconds + delta,
            _ => state.MediaAnchorSeconds + delta * state.Rate
        };
        if (!double.IsFinite(value) || !float.IsFinite((float)value)) return false;
        mediaSeconds = (float)Math.Min(value, state.ClipLengthSeconds);
        return true;
    }
}
