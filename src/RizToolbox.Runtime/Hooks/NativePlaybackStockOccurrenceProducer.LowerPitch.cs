namespace RizToolbox.Runtime.Hooks;

internal sealed partial class NativePlaybackStockOccurrenceProducer
{
    private void RegisterPhysicalLowerPitchChild(Call child)
    {
        var before = child.Before;
        var parent = _calls.Values.SingleOrDefault(value => value.Before.Sequence == before.ParentSequence
            && value.Before.OwnerEpoch == before.OwnerEpoch);
        if (!_group.HasLowerPitch || parent is null || parent.Returned is not null || parent.PhysicalChildren.Count != 0
            || parent.Before.Role != NativePlaybackSourceRole.SetPitch || parent.Before.Kind != NativePlaybackSourceKind.Single1
            || before.Kind != NativePlaybackSourceKind.IcallSingle1 || before.IncomingMethod != IntPtr.Zero
            || before.Self != parent.Before.Self || before.ArgBits != parent.Before.ArgBits
            || before.ThreadId != parent.Before.ThreadId || before.Depth != parent.Before.Depth + 1
            || before.WriterSequence != parent.Before.WriterSequence || before.SourceSequence != parent.Before.SourceSequence
            || before.SchedulingSequence != parent.Before.SchedulingSequence)
            throw new UnsupportedSignatureException("The exact native lower child lost its unique wrapper parent or physical-call identity.");
        child.PhysicalParent = parent; parent.PhysicalChildren.Add(child);
        // It remains in _calls/_responsibilities. It is deliberately absent
        // from Occurrence.Children and from the private writer reservation.
    }

    private void VerifyPhysicalLowerPitchChildren(Call parent)
    {
        if (!_group.HasLowerPitch || parent.Before.Role != NativePlaybackSourceRole.SetPitch) return;
        if (parent.PhysicalChildren.Count != 1)
            throw new UnsupportedSignatureException("The wrapper original did not produce its one exact lower physical child.");
        var child = parent.PhysicalChildren[0];
        if (!ReferenceEquals(child.PhysicalParent, parent) || !child.Before.IsExactLowerPitchChild
            || child.Returned is not { Returned: true, Abnormal: false, Unknown: false }
            || !child.Before.TryReadOperationCompletion(out var exit)
            || exit is not { PhysicalExited: true, Returned: true, Abnormal: false, Unknown: false })
            throw new UnsupportedSignatureException("The wrapper original lacks its exact lower child's normal return and physical-exit receipt.");
        _group.VerifySourceOperationRole(child.Before.Role); exit.VerifyOperationAuthority();
        VerifyPhysicalEnginePitchChildren(child);
    }

    private void RegisterPhysicalEnginePitchChild(Call child)
    {
        var before = child.Before;
        var parent = _calls.Values.SingleOrDefault(value => value.Before.Sequence == before.ParentSequence
            && value.Before.OwnerEpoch == before.OwnerEpoch);
        var scheduled = _group.HasScheduledEnginePitch && parent is not null
            && parent.Before.Role == NativePlaybackSourceRole.PlayScheduled && parent.Before.Kind == NativePlaybackSourceKind.Double1;
        if (!_group.HasEnginePitch || parent is null || parent.Returned is not null || parent.PhysicalChildren.Count != 0
            || (!scheduled && (parent.Before.Role != NativePlaybackSourceRole.LowerSetPitch || parent.Before.Kind != NativePlaybackSourceKind.IcallSingle1))
            || before.Role != NativePlaybackSourceRole.EngineSetPitch || !before.IsExactEnginePitchChild
            || before.Kind != NativePlaybackSourceKind.IcallSingle1 || before.IncomingMethod != IntPtr.Zero
            || before.EngineWrapper != parent.Before.Self || (!scheduled && before.ArgBits != parent.Before.ArgBits)
            || before.ThreadId != parent.Before.ThreadId || before.Depth != parent.Before.Depth + 1
            || before.WriterSequence != parent.Before.WriterSequence || before.SourceSequence != parent.Before.SourceSequence
            || before.SchedulingSequence != parent.Before.SchedulingSequence)
            throw new UnsupportedSignatureException("The native backing child lost its unique lower or scheduled-original parent, rooted wrapper or physical-call identity.");
        // A scheduled parent's argument is a double DSP timestamp. The native
        // original-call window, rather than this timestamp, proves its child's
        // float is the retained source's unchanged current pitch.
        before.VerifyEngineAssociationCurrent();
        child.PhysicalParent = parent; parent.PhysicalChildren.Add(child);
    }

    private void VerifyPhysicalEnginePitchChildren(Call parent, bool requireWrite = false)
    {
        var scheduled = _group.HasScheduledEnginePitch && parent.Before.Role == NativePlaybackSourceRole.PlayScheduled
            && parent.Before.Kind == NativePlaybackSourceKind.Double1;
        if (!_group.HasEnginePitch || (!scheduled && parent.Before.Role != NativePlaybackSourceRole.LowerSetPitch)) return;
        // Unity can reuse an existing playback object without reapplying its
        // fields. A child that did occur still owes its exact normal exit.
        if (scheduled && parent.PhysicalChildren.Count == 0) return;
        // The fixed icall's nonfinite filter can legally return without calling
        // native. A consumed child always keeps its separate completion duty.
        if (!scheduled && parent.PhysicalChildren.Count == 0 && !requireWrite && !float.IsFinite(parent.Before.SingleArgument)) return;
        if (parent.PhysicalChildren.Count != 1)
            throw new UnsupportedSignatureException("The lower or scheduled original did not retain its one actual native backing child.");
        var child = parent.PhysicalChildren[0];
        if (!ReferenceEquals(child.PhysicalParent, parent) || !child.Before.IsExactEnginePitchChild
            || child.Before.EngineWrapper != parent.Before.Self
            || child.Returned is not { Returned: true, Abnormal: false, Unknown: false }
            || !child.Before.TryReadOperationCompletion(out var exit)
            || exit is not { PhysicalExited: true, Returned: true, Abnormal: false, Unknown: false })
            throw new UnsupportedSignatureException("The lower or scheduled original lacks its exact native child's normal return and physical-exit receipt.");
        _group.VerifySourceOperationRole(child.Before.Role); exit.VerifyOperationAuthority();
    }

    private void VerifyReservedLowerPitchChild(NativePlaybackStockSourceHub.WriterReservation reservation)
    {
        if (!_group.HasLowerPitch) return;
        if (!_calls.TryGetValue(reservation.ActualAttempt, out var parent))
            throw new UnsupportedSignatureException("The native writer's wrapper attempt lost its retained physical-call owner.");
        VerifyPhysicalLowerPitchChildren(parent);
        if (_group.HasEnginePitch) VerifyPhysicalEnginePitchChildren(parent.PhysicalChildren.Single(), requireWrite: true);
    }

    private bool RetainsPhysicalCall(Call call)
    {
        if (ReferenceEquals(_resumeCountdown, call.Before)) return true;
        for (Call? retained = call; retained is not null; retained = retained.PhysicalParent)
        {
            if (_issued is not null && _issued.Contains(retained.Before)
                || _writerCurrent is not null && _writerCurrent.Contains(retained.Before)) return true;
            if (_active is { } active && (active.Root.Contains(retained.Before)
                || active.Children.Any(value => value.Contains(retained.Before)))) return true;
        }
        return false;
    }
}
