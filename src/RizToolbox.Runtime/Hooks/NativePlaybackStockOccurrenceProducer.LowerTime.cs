namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativePlaybackStockOccurrenceProducer
{
    private void RegisterPhysicalLowerTimeChild(Call child)
    {
        var before = child.Before;
        // ObserveBefore authenticated the issued child against this group's
        // native current owner/token. The hub also rejects mixed child flags.
        // The parent's retained Before was authenticated on its own entry.
        var parent = _calls.Values.SingleOrDefault(value => value.Before.Sequence == before.ParentSequence
            && value.Before.OwnerEpoch == before.OwnerEpoch);
        if (!_group.HasLowerTime || parent is null || parent.Returned is not null || parent.PhysicalChildren.Count != 0
            || parent.Before.Role != NativePlaybackSourceRole.SetTime || parent.Before.Kind != NativePlaybackSourceKind.Single1
            || !before.IsExactLowerTimeChild || before.ParentSequence == 0 || before.IncomingMethod != IntPtr.Zero
            || before.Self != parent.Before.Self || before.ArgBits != parent.Before.ArgBits || before.ArgBits > uint.MaxValue
            || before.ThreadId != parent.Before.ThreadId || before.Depth != parent.Before.Depth + 1
            || before.WriterSequence != parent.Before.WriterSequence || before.SourceSequence != parent.Before.SourceSequence
            || before.SchedulingSequence != parent.Before.SchedulingSequence)
            throw new UnsupportedSignatureException("The exact lower-time child lost its unique SetTime parent or physical-call identity.");
        child.PhysicalParent = parent;
        parent.PhysicalChildren.Add(child);
        // The wrapper consumes Scheduling once. Its child inherits all three
        // fences and keeps its own physical-exit duty in _responsibilities.
        // It never enters Occurrence.Children or consumes a second reservation.
    }

    private void VerifyPhysicalLowerTimeChildren(Call parent)
    {
        if (!_group.HasLowerTime || parent.Before.Role != NativePlaybackSourceRole.SetTime) return;
        // Every raw32 value, including NaN payloads, requires the real child.
        // Its normal return and native finally exit precede wrapper acceptance.
        if (parent.Before.Kind != NativePlaybackSourceKind.Single1 || parent.PhysicalChildren.Count != 1)
            throw new UnsupportedSignatureException("The SetTime original did not produce its one exact lower-time physical child.");
        var child = parent.PhysicalChildren[0];
        if (!ReferenceEquals(child.PhysicalParent, parent) || !child.Before.IsExactLowerTimeChild
            || child.Returned is not { Returned: true, Abnormal: false, Unknown: false }
            || !child.Before.TryReadOperationCompletion(out var exit)
            || exit is not { PhysicalExited: true, Returned: true, Abnormal: false, Unknown: false })
            throw new UnsupportedSignatureException("The SetTime original lacks its exact lower-time child's normal return and physical-exit receipt.");
        _group.VerifySourceOperationRole(child.Before.Role); exit.VerifyOperationAuthority();
    }

    private void VerifyReservedWriter(NativePlaybackStockSourceHub.WriterReservation reservation)
    {
        if (reservation.Role == NativePlaybackSourceRole.SetPitch)
        { VerifyReservedLowerPitchChild(reservation); return; }
        if (reservation.Role != NativePlaybackSourceRole.SetTime || !_group.HasLowerTime
            || !_calls.TryGetValue(reservation.ActualAttempt, out var parent))
            throw new UnsupportedSignatureException("The time writer lost its actual wrapper/lower-time completion owner.");
        VerifyPhysicalLowerTimeChildren(parent);
    }
}
