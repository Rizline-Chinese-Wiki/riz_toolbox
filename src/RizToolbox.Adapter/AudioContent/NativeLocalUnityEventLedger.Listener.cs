using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.AudioContent;

internal sealed partial class NativeLocalUnityEventLedger
{
    internal void ObserveListenerBefore(InvokeContext context, NativeListenerObservation observation,
        ExactHookBroker.NativeVoidPhysicalIdentity identity)
    {
        Guard(); _authority.VerifyObservation();
        var frame = observation.Frame;
        try
        {
            if (_busy || frame.Stage != NativeListenerStage.Before || observation.Machine is null || frame.Site > 2
                || frame.Flags != NativeListenerFlags.None || frame.MachineFrame != IntPtr.Zero
                || !context._native || !context._active || context._after || !ReferenceEquals(_current, context)
                || PhysicalTop != context._receiver || frame.Event.ToInt64() != context._receiver || frame.Invokable == IntPtr.Zero
                || context._record is not { } row || row.Generation != context._generation || row.Inline is null
                || frame.List.ToInt64() != row.Inline.Executing.Object || frame.ParentSequence == 0 || frame.ChildSequence == 0
                || frame.ParentManagedToken != unchecked((ulong)identity.Token) || row.Inline.NativeToken != unchecked((long)identity.Token)
                || !identity.IsCurrentFor(context) || identity.Frame.Caller.ToInt64() != context._nativeCaller
                || identity.Frame.Method.ToInt64() != context._nativeMethod || identity.Entry.Address.ToInt64() != checked(_module + 0x15642e0)
                || frame.SourceReturnPc == IntPtr.Zero || frame.SourceReturnPc != identity.ConfiguredListenerReturnPc(frame.Site)
                || frame.Index > int.MaxValue || row.Dispatches.Count >= MaximumRows
                || row.Dispatches.Any(dispatch => !dispatch.Completed || dispatch.Frame.ChildSequence == frame.ChildSequence))
                throw new InvalidOperationException("Listener Before has no exact current inline-list parent or child identity.");
            VerifyNativeTransport(requireActive: true);
            var dispatch = new DispatchRecord(frame); row.Dispatches.Add(dispatch); _dispatches = checked(_dispatches + 1);
            ObserveListenerEffectsDispatch(context, dispatch);
            _busy = true;
            var captured = _backend.CaptureDispatch(row.Slot, frame); Known(); VerifyNativeTransport(requireActive: true);
            if (!identity.IsCurrentFor(context) || captured.Executing.Object != frame.List.ToInt64()
                || captured.Invokable.Object != frame.Invokable.ToInt64() || frame.Index >= (ulong)captured.Executing.Rows.Count
                || captured.Executing.Rows[checked((int)frame.Index)].Object != frame.Invokable.ToInt64())
                throw new InvalidOperationException("Listener source identity changed during rooted dispatch capture.");
            var executing = Freeze(captured.Executing);
            if (executing.Rows.Count > MaximumRows || executing.Rows.Sum(item => item.Leaves.Count) > MaximumLeaves
                || _backend.RootRecords > MaximumRoots)
                throw new InvalidOperationException("Listener dispatch exceeds bounded graph/root responsibility.");
            dispatch.Observation = new(executing, executing.Rows[checked((int)frame.Index)], captured.ArgumentArray);
            TrackMutationInvokables(row, executing);
            dispatch.Captured = true;
        }
        catch (Exception error) { Fault(error.Message); throw; }
        finally { _busy = false; }
    }

    internal void ObserveListenerAfter(InvokeContext context, NativeListenerObservation observation, bool captured)
    {
        // This path only consumes scalar/copy identities. Do not gate cleanup
        // on Known, read another native object, free roots, or synthesize Before.
        _main.VerifyAccess(); var frame = observation.Frame;
        if (!context._native || !context._active || context._after || !ReferenceEquals(_current, context)
            || PhysicalTop != context._receiver || context._record is not { } row || row.Generation != context._generation
            || frame.Event.ToInt64() != context._receiver)
        { Fault("Listener completion has no exact retained parent generation."); return; }
        var dispatch = row.Dispatches.LastOrDefault(value => value.Frame.ChildSequence == frame.ChildSequence);
        if (dispatch is null || dispatch.Completed || dispatch.Frame.ParentSequence != frame.ParentSequence
            || dispatch.Frame.ParentManagedToken != frame.ParentManagedToken || dispatch.Frame.Site != frame.Site
            || dispatch.Frame.List != frame.List || dispatch.Frame.Invokable != frame.Invokable || dispatch.Frame.Index != frame.Index
            || dispatch.Frame.SourceRsp != frame.SourceRsp || dispatch.Frame.ActualTarget != frame.ActualTarget
            || dispatch.Frame.SourceReturnPc != frame.SourceReturnPc)
        { Fault("Listener completion differs from its actual Before identity."); return; }
        dispatch.Completed = true; dispatch.Flags = (uint)frame.Flags;
        dispatch.NativeReturned = frame.Stage == NativeListenerStage.Returned;
        dispatch.Returned = captured && dispatch.Captured && frame.Stage == NativeListenerStage.Returned && frame.Flags == NativeListenerFlags.None;
        if (dispatch.Returned) _dispatchReturned = checked(_dispatchReturned + 1);
        else { _dispatchAbnormal = checked(_dispatchAbnormal + 1); Fault("Listener original or capture did not complete normally."); }
    }
}
