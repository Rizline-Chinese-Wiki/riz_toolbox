using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativePlaybackStockClockRouter
{
    // Each direct audio attempt retains its physical subtree through the helper
    // journal. A lower-pitch parent may call engine-pitch, and a time wrapper
    // must call its single lower-time child before its original returns.
    private sealed class DirectPitchCall
    {
        internal readonly NativePlaybackSourceObservation Before;
        internal NativePlaybackSourceCompletion? Returned;
        internal DirectPitchCall? Child;
        internal DirectPitchCall? Parent;
        internal DirectPitchCall(NativePlaybackSourceObservation before) => Before = before;
    }
    private readonly Dictionary<NativePlaybackSourceObservation, DirectPitchCall> _directPitchCalls = new();

    private void ObserveDirectPitchBefore(NativePlaybackSourceObservation observation)
    {
        _group!.VerifyIssuedOperationBefore(observation);
        if (!_directPitchCalls.TryAdd(observation, new(observation)))
            throw new UnsupportedSignatureException("A direct audio Before cannot be delivered twice.");
        _source?.WithdrawForDirectAudio();
        _session?.Withdraw("The retained AudioSource entered a direct audio attempt before its original.");
    }

    private bool HasStockResumeTimeParent(NativePlaybackSourceObservation observation)
    {
        _group!.VerifyIssuedOperationBefore(observation);
        if (observation.Kind != NativePlaybackSourceKind.Single1 || observation.IncomingMethod != IntPtr.Zero
            || observation.Caller != _group.StockOperationLeafCaller(NativePlaybackSourceRole.Resume, NativePlaybackSourceRole.SetTime))
            return false;
        // Resume's retained root and fixed original CALL identify the wrapper.
        // Its lower child remains a separate physical duty in the producer.
        return _logicalObservations.Any(parent => !parent.IsReleased
            && parent.Role == NativePlaybackSourceRole.Resume && parent.ParentSequence == 0
            && parent.Sequence == observation.ParentSequence && parent.OwnerEpoch == observation.OwnerEpoch
            && parent.ThreadId == observation.ThreadId && observation.Depth == parent.Depth + 1);
    }

    private bool TryObserveDirectTimeChild(NativePlaybackSourceObservation observation)
    {
        if (!observation.IsExactLowerTimeChild) return false;
        var parent = _directPitchCalls.Values.SingleOrDefault(value => value.Before.Sequence == observation.ParentSequence
            && value.Before.OwnerEpoch == observation.OwnerEpoch);
        if (parent is null) return false;
        _group!.VerifyIssuedOperationBefore(observation);
        try
        {
            var origin = parent.Before;
            if (!_group.HasLowerTime || parent.Returned is not null || parent.Child is not null
                || origin.Role != NativePlaybackSourceRole.SetTime || origin.Kind != NativePlaybackSourceKind.Single1
                || observation.Kind != NativePlaybackSourceKind.IcallSingle1 || observation.IncomingMethod != IntPtr.Zero
                || observation.Self != origin.Self || observation.ArgBits != origin.ArgBits
                || observation.ThreadId != origin.ThreadId || observation.Depth != origin.Depth + 1
                || observation.WriterSequence != origin.WriterSequence || observation.SourceSequence != origin.SourceSequence
                || observation.SchedulingSequence != origin.SchedulingSequence)
                throw new UnsupportedSignatureException("The direct time child lost its unique actual wrapper and Scheduling identity.");
            var child = new DirectPitchCall(observation) { Parent = parent };
            parent.Child = child;
            _directPitchCalls.Add(observation, child);
            return true;
        }
        catch
        {
            // This is the group's actual issued child. A rejected association
            // grants no parent link, but must still request its own native
            // journal drain after PhysicalExit; Bridge retains it until then.
            observation.DisposeOperation();
            throw;
        }
    }

    private bool TryObserveDirectPitchChild(NativePlaybackSourceObservation observation)
    {
        if (!observation.IsExactEnginePitchChild) return false;
        var parent = _directPitchCalls.Values.SingleOrDefault(value => value.Before.Sequence == observation.ParentSequence
            && value.Before.OwnerEpoch == observation.OwnerEpoch);
        if (parent is null) return false;
        _group!.VerifyIssuedOperationBefore(observation); observation.VerifyEngineAssociationCurrent();
        var origin = parent.Before;
        if (parent.Returned is not null || parent.Child is not null || origin.Role != NativePlaybackSourceRole.LowerSetPitch
            || origin.IsExactLowerPitchChild || observation.EngineWrapper != origin.Self || observation.ArgBits != origin.ArgBits
            || observation.ThreadId != origin.ThreadId || observation.Depth != origin.Depth + 1
            || observation.WriterSequence != origin.WriterSequence || observation.SourceSequence != origin.SourceSequence
            || observation.SchedulingSequence != origin.SchedulingSequence)
            throw new UnsupportedSignatureException("The direct icall's native child lost its actual private parent and wrapper association.");
        var child = new DirectPitchCall(observation) { Parent = parent }; parent.Child = child;
        _directPitchCalls.Add(observation, child); return true;
    }

    private bool ObserveDirectPitchReturned(NativePlaybackSourceCompletion completion)
    {
        if (!_directPitchCalls.TryGetValue(completion.Observation, out var call)) return false;
        _group!.VerifyIssuedOperationReturn(completion);
        if (call.Returned is not null) throw new UnsupportedSignatureException("A direct audio return was delivered twice.");
        call.Returned = completion;
        var engineParent = call.Before.Role == NativePlaybackSourceRole.LowerSetPitch && _group.HasEnginePitch;
        var timeParent = call.Before.Role == NativePlaybackSourceRole.SetTime && _group.HasLowerTime;
        if (completion.Returned && !completion.Abnormal && (engineParent || timeParent))
        {
            if (engineParent && call.Child is null && !float.IsFinite(call.Before.SingleArgument)) return true;
            if (call.Child is not { Returned: { Returned: true, Abnormal: false, Unknown: false } } child
                || !child.Before.TryReadOperationCompletion(out var exit)
                || exit is not { PhysicalExited: true, Returned: true, Abnormal: false, Unknown: false })
                throw new UnsupportedSignatureException("The direct audio return has no complete exact native-child physical exit.");
            exit.VerifyOperationAuthority();
        }
        return true;
    }

    private void PumpDirectPitchCompleted()
    {
        foreach (var call in _directPitchCalls.Values.ToArray())
        {
            if (call.Parent is not null) continue;
            if (call.Before.IsReleased) { _directPitchCalls.Remove(call.Before); continue; }
            if (!call.Before.TryReadOperationCompletion(out var exit) || exit is not { PhysicalExited: true }) continue;
            if (call.Child is { } child)
            {
                if (!child.Before.TryReadOperationCompletion(out var childExit) || childExit is not { PhysicalExited: true }) continue;
                child.Before.DisposeOperation();
                if (!child.Before.IsReleased) continue;
                _directPitchCalls.Remove(child.Before);
            }
            call.Before.DisposeOperation();
            if (call.Before.IsReleased) _directPitchCalls.Remove(call.Before);
        }
    }
}
