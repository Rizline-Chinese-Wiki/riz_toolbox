using RizToolbox.Adapter.Judgements;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekJudgeQueuesParticipant
{
    private NativeJudgementBackend.SeekHoldEpoch? _holdEpoch;

    internal void BindHoldEpoch(NativeJudgementBackend.SeekHoldEpoch epoch) => Operation(() =>
    {
        ArgumentNullException.ThrowIfNull(epoch);
        if (_holdEpoch is not null || _entered || _restoring || _pending is not null)
            throw new InvalidOperationException("Judge queues bind their actual Hold epoch once, before any mutation.");
        if (epoch.JudgeIdentity.Notes != _baseline.Identity.Notes
            || epoch.JudgeAddress != _baseline.Identity.NoteJudgeControl)
            throw new InvalidOperationException("The original Hold epoch belongs to another actual Judge graph.");
        if (epoch.ReadOwnedJudgeCount() != _baseline.HoldOnJudgeCount)
            throw new InvalidOperationException("The original Hold epoch has a different prepared counter.");
        RequireOwned(); _holdEpoch = epoch; return 0;
    });

    private void ObserveHoldEpochCount(SeekJudgeRawState actual)
    {
        if (_holdEpoch is null) return;
        if (actual.Identity != _baseline.Identity
            || _holdEpoch.JudgeIdentity.Notes != actual.Identity.Notes
            || _holdEpoch.JudgeAddress != actual.Identity.NoteJudgeControl)
            throw new InvalidOperationException("The bound Hold epoch no longer names this actual Judge graph.");
        var count = _holdEpoch.ReadOwnedJudgeCount(); Local();
        if (actual.HoldOnJudgeCount != count)
            throw new InvalidOperationException("The complete Judge sample disagrees with its actual Hold epoch counter.");
        var second = SeekJudgeQueueState.Copy(_access.Read(), Local); Local();
        if (!SeekJudgeQueueState.Same(actual, second))
            throw new InvalidOperationException("The complete Judge storage changed during the independent epoch counter readback.");
        if (_expected.HoldOnJudgeCount == count) return;
        if (_pending is not null)
            throw new InvalidOperationException("A Hold epoch cannot advance during an unresolved Judge List dispatch.");
        // The epoch alone writes this slot and verifies its phase. Neither the
        // queue values nor any List identity/storage/version are rebased here.
        _baseline = _baseline with { HoldOnJudgeCount = count };
        _expected = _expected with { HoldOnJudgeCount = count };
    }
}
