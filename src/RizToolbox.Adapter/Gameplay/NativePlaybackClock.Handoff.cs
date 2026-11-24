using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativePlaybackClock
{
    private NativePlaybackClock(NativePlaybackClockState initial, NativePlaybackClockPublication publication)
    {
        if (!initial.Binding.Valid || initial.Revision < 1 || initial.Phase != NativePlaybackPhase.Paused
            || !ValidDsp(initial.DspAnchorSeconds) || !ValidRate(initial.Rate) || initial.Rate != (float)initial.Rate
            || !double.IsFinite(initial.ClipLengthSeconds) || initial.ClipLengthSeconds <= 0 || initial.ClipLengthSeconds > float.MaxValue
            || !ValidPosition(initial.MediaAnchorSeconds, initial.ClipLengthSeconds) || initial.WithdrawalReason is not null)
            throw new InvalidOperationException("The admitted baseline is outside the exact finite paused Clock domain.");
        _state = initial; _lastDspSeconds = initial.DspAnchorSeconds; _publication = publication;
    }
    internal static NativePlaybackClock CreatePrepared(NativePlaybackClockState initial,
        NativePlaybackClockPublication publication)
    {
        if (initial.Revision != 1)
            throw new InvalidOperationException("Only the first prepared Clock owns revision one.");
        publication.VerifyDormant(initial);
        return new(initial, publication);
    }
    internal static NativePlaybackClock CreateAdmitted(NativePlaybackClockState initial,
        NativePlaybackClockPublication publication, NativePlaybackSingleReturnBridge.LogicalHandoff transfer)
    {
        // Revision is consumed from the registered opaque native transfer, never
        // accepted as a caller's expected-epoch/revision initialization grant.
        if (initial.Revision <= 1 || transfer.AdmittedRevision != initial.Revision || initial.Phase != NativePlaybackPhase.Paused)
            throw new InvalidOperationException("The admitted Clock baseline differs from its actual native handoff.");
        publication.VerifyDormant(initial);
        return new(initial, publication);
    }
}
