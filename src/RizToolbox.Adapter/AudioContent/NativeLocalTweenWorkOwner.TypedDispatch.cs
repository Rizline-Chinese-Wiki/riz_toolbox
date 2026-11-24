using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.AudioContent;

internal sealed partial class NativeLocalTweenWorkOwner
{
    private TypedEventInvocation CurrentTypedDispatchParent(IDisposable context, NativeTweenSingleEventFrame frame)
    {
        _main.VerifyAccess();
        if (context is not TypedEventInvocation invocation || !_typedInvocations.Contains(invocation)
            || !ReferenceEquals(_typedEventTop, invocation) || invocation.Disposed || invocation.Returned || invocation.Abnormal
            || !ReferenceEquals(_top, invocation.Parent) || invocation.Parent.Completed || invocation.Parent.Disposed
            || invocation.Frame.Sequence != frame.ParentSequence || invocation.Frame.Self != frame.Event
            || invocation.Frame.ScalarBits != frame.ValueBits || invocation.Frame.Method != frame.ParentMethod || invocation.Frame.Caller != frame.ParentCaller)
            throw new InvalidOperationException("The actual Single mid/child does not belong to this exact retained typed parent.");
        return invocation;
    }
    internal void CaptureTypedExecutingList(IDisposable context, NativeTweenSingleEventFrame frame, NativeSingleExecutingList list)
    {
        Known();
        var invocation = CurrentTypedDispatchParent(context, frame);
        invocation.Executing.Mid(frame, list);
        if (_backend is INativeTweenTypedEventGraphSource graphSource)
        {
            var midGraph = graphSource.ReadTypedEventGraph(this, invocation.Membership.EventRoot);
            var midWriter = graphSource.ReadTypedEventWriterGeneration(this, invocation.Membership.EventRoot);
            if (midGraph.Event != invocation.Membership.Event || midGraph.ExecutingCalls.List != list.List
                || !SingleExecutingListEqual(midGraph.ExecutingCalls, list))
                throw new InvalidOperationException("Typed Single mid event graph differs from its actual rooted executing list.");
            invocation.MidGraph = midGraph;
            invocation.MidWriterGeneration = midWriter;
        }
    }
    internal void CaptureTypedDispatch(IDisposable context, NativeTweenSingleEventFrame frame,
        NativeSingleExecutingList list, NativeSingleDispatchTarget target)
    {
        Known(); var invocation = CurrentTypedDispatchParent(context, frame); PollSourceJournal();
        invocation.Executing.Before(frame, list, target, _sourceOccurrences.Keys);
        BeginTypedListenerEffect(invocation, frame, target);
    }
    internal void CompleteTypedDispatch(IDisposable context, NativeTweenSingleEventFrame frame)
    {
        var invocation = CurrentTypedDispatchParent(context, frame);
        // Cleanup observations survive logical withdrawal and sticky native abnormal state.
        if (!Unknown) PollSourceJournal();
        invocation.Executing.Complete(frame, _sourceOccurrences.Keys);
        CompleteTypedListenerEffect(invocation, frame);
        if (frame.Stage == NativeTweenSingleEventStage.Abnormal || frame.Flags != 0)
            invocation.Failure ??= "Actual Single listener had an abnormal/missing-post/observer-fault outcome.";
    }
    internal void ObserveTypedSourceFault(string reason) => Fault("Typed Single executing source: " + reason);
    internal IReadOnlyList<NativeTweenSingleListSnapshot> ReadTypedExecutingLists()
    {
        _main.VerifyAccess(); return Array.AsReadOnly(_typedInvocations.Select(ReadTypedListenerEffects).ToArray());
    }
}
