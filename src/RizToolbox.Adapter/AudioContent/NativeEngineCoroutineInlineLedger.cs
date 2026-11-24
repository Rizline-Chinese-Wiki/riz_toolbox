using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal sealed record NativeEngineCoroutineInlineSnapshot(int Starts, int Outstanding, int Branches,
    bool Unknown, string? Failure);

/// <summary>Associates actual dispatcher-issued side receipts with the existing
/// exact Start/identity/node generation. This closes only that Start's inline
/// cleanup observation; it never closes cancellation or future producers.</summary>
internal sealed class NativeEngineCoroutineInlineLedger
{
    internal const int MaximumStarts = 2048;
    private readonly IMainThreadDispatcher _main;
    private readonly NativeEngineCoroutineIdentityLedger _identities;
    private readonly NativeEngineCoroutineNodeLedger _nodes;
    private readonly List<Context> _contexts = new();
    private int _unknown;
    private string? _failure;
    internal bool Unknown => Volatile.Read(ref _unknown) != 0;
    internal NativeEngineCoroutineInlineLedger(IMainThreadDispatcher main, NativeEngineCoroutineIdentityLedger identities,
        NativeEngineCoroutineNodeLedger nodes)
    {
        ArgumentNullException.ThrowIfNull(main); main.VerifyAccess(); ArgumentNullException.ThrowIfNull(identities); ArgumentNullException.ThrowIfNull(nodes);
        if (!identities.Wraps(nodes)) throw new ArgumentException("Inline observation must use the same exclusive actual identity/node owner.");
        (_main, _identities, _nodes) = (main, identities, nodes);
    }

    internal sealed class Context : IDisposable
    {
        internal readonly NativeEngineCoroutineInlineLedger Owner;
        internal readonly NativeEngineCoroutineIdentityLedger.Call Start;
        internal readonly NativeEngineNodeObserverDispatcher.StartScopeProof Proof;
        internal readonly NativeEngineInlineChannelIdentity Channel;
        internal readonly NativeEngineInlineFrame Entered;
        internal NativeEngineCoroutineNodeLedger.NodeRecord? Node;
        internal NativeEngineInlineFrame Begin, End, Exit;
        internal bool Began, Ended, Exited, Disposed, Failed;
        internal NativeEngineInlineKind? Endpoint;
        internal Context(NativeEngineCoroutineInlineLedger owner, NativeEngineCoroutineIdentityLedger.Call start,
            NativeEngineInlineObserverDispatcher.Observation receipt)
            => (Owner, Start, Proof, Channel, Entered) = (owner, start, receipt.Start, receipt.Identity, receipt.Frame);
        internal bool NativeBranchObserved => Began && Ended && !Failed;
        public void Dispose() => Owner.Release(this);
        internal bool MayConfirm(NativeEngineCoroutineNodeLedger.NodeRecord node)
        {
            var physical = Start.NodeCall;
            var completion = physical?.Completion ?? default;
            return !Owner.Unknown && !Owner._nodes.Unknown && !Owner._identities.Unknown && !Failed
                && ReferenceEquals(Node, node) && ReferenceEquals(node.Inline, this)
                && ReferenceEquals(node.Start, physical) && Owner._nodes.IsLatest(node)
                && Began && Ended && Exited && Disposed && (Endpoint is NativeEngineInlineKind.EndFree or NativeEngineInlineKind.EndRetained)
                && physical is { Completed: true, Disposed: true } && node.Executions == 0 && node.OpenCalls == 0
                && completion.IsValidFor(NativeEngineNodeKind.Start)
                && completion.Flags == (NativeEngineNodeExitFlags.Returned | NativeEngineNodeExitFlags.ReturnByteValid)
                && completion.OutputFlags == (NativeEngineNodeOutputFlags.Requested | NativeEngineNodeOutputFlags.Read)
                && completion.StartOutputNode == IntPtr.Zero && Exit.StartOutputNode == IntPtr.Zero
                && completion.Flags == Exit.StartExitFlags && completion.RawReturn == Exit.RawStartReturn
                && End.RawStartReturn == Exit.RawStartReturn;
        }
        internal bool MatchesStartCompletion(NativeEngineCoroutineNodeLedger.Call physical, NativeEngineNodeCompletion completion)
            => !Failed && Exited && Disposed && ReferenceEquals(Start.NodeCall, physical)
                && completion.IsValidFor(NativeEngineNodeKind.Start) && completion.Flags == Exit.StartExitFlags
                && completion.RawReturn == Exit.RawStartReturn && completion.StartOutputNode == Exit.StartOutputNode
                && completion.OutputFlags == (NativeEngineNodeOutputFlags.Requested | NativeEngineNodeOutputFlags.Read)
                && (!Began || Ended && completion.StartOutputNode == IntPtr.Zero && End.RawStartReturn == completion.RawReturn);
    }

    internal Context? Capture(NativeEngineInlineObserverDispatcher.Observation receipt)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(receipt);
        try
        {
            var start = _identities.CaptureInlineStart(receipt);
            if (start is null) return null;
            if (Unknown || _nodes.Unknown || _contexts.Count == MaximumStarts
                || _contexts.Any(context => ReferenceEquals(context.Start, start)))
                throw new InvalidOperationException("Inline Start responsibility is Unknown, repeated or exceeds capacity.");
            var frame = receipt.Frame;
            var context = new Context(this, start, receipt); _contexts.Add(context); // Before the remaining scalar checks.
            if (!frame.IsValid || frame.HasFault || frame.Node != IntPtr.Zero || frame.EnumeratorHandle != 0
                || frame.EnumeratorType != 0 || frame.ReferenceCount != 0 || frame.SitePc != receipt.Identity.Start.Target
                || frame.RelayReturnPc != IntPtr.Zero || frame.StartExitFlags != 0 || frame.RawStartReturn != 0
                || frame.StartOutputNode != IntPtr.Zero)
                throw new InvalidOperationException("Inline StartEntered does not have the native actual-argument scalar shape.");
            return context;
        }
        catch (Exception error) { ObserveFault(receipt.Frame, error.Message); throw; }
    }

    internal void Observe(Context context, NativeEngineInlineObserverDispatcher.Observation receipt)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(receipt);
        try
        {
            if (!ReferenceEquals(context.Owner, this) || !_contexts.Contains(context) || context.Disposed || context.Exited
                || context.Failed || Unknown || !ReferenceEquals(receipt.Start, context.Proof) || receipt.Identity != context.Channel
                || !_identities.IsCurrentInlineStart(context.Start, receipt))
                throw new InvalidOperationException("Inline event cannot borrow another actual Start, owner, channel or completed context.");
            var frame = receipt.Frame;
            if (!frame.IsValid || frame.HasFault || frame.OutputSlot != context.Start.Frame.Arg3
                || frame.StartSequence != context.Entered.StartSequence || frame.ParentStartSequence != context.Entered.ParentStartSequence
                || frame.Depth != context.Entered.Depth || frame.ThreadId != context.Entered.ThreadId)
                throw new InvalidOperationException("Inline event has an abnormal, missing or changed exact Start scope.");
            switch (frame.Kind)
            {
                case NativeEngineInlineKind.Begin: Begin(context, frame); break;
                case NativeEngineInlineKind.EndFree:
                case NativeEngineInlineKind.EndRetained:
                    if (!context.Began || context.Ended || !SameTuple(context.Begin, frame)
                        || !frame.HasSnapshot || frame.StartExitFlags != 0 || frame.StartOutputNode != IntPtr.Zero)
                        throw new InvalidOperationException("Inline End does not match one actual cached Begin tuple.");
                    context.End = frame; context.Ended = true; context.Endpoint = frame.Kind; break;
                case NativeEngineInlineKind.StartExited:
                    if (context.Began != context.Ended || frame.StartExitFlags != (NativeEngineNodeExitFlags.Returned | NativeEngineNodeExitFlags.ReturnByteValid)
                        || frame.SitePc != context.Channel.Start.Target || frame.RelayReturnPc != IntPtr.Zero
                        || (frame.Flags & (NativeEngineInlineFlags.OutputRequested | NativeEngineInlineFlags.OutputRead | NativeEngineInlineFlags.OutputFaulted))
                            != (NativeEngineInlineFlags.OutputRequested | NativeEngineInlineFlags.OutputRead)
                        || context.Began && (!SameTuple(context.Begin, frame) || frame.StartOutputNode != IntPtr.Zero
                            || frame.RawStartReturn != context.End.RawStartReturn))
                        throw new InvalidOperationException("Inline StartExited lacks the matching branch, normal native return or copied output receipt.");
                    context.Exit = frame; context.Exited = true; break;
                default: throw new InvalidOperationException("Inline context received another StartEntered.");
            }
        }
        catch (Exception error) { context.Failed = true; ObserveFault(receipt.Frame, error.Message); throw; }
    }

    private void Begin(Context context, NativeEngineInlineFrame frame)
    {
        var start = context.Start.NodeCall!; var node = context.Start.Identity?.Node;
        if (context.Began || context.Ended || node is null || node.Generation <= 0 || node.Inline is not null
            || !ReferenceEquals(node.Start, start) || !start.StartedNodes.Contains(node) || !_nodes.IsLatest(node)
            || node.FinalCleanup || node.Executions != 0 || node.OpenCalls != 0
            || !frame.HasSnapshot || frame.ReferenceCount != 1 || frame.EnumeratorType != 2
            || frame.Node.ToInt64() != node.Pointer || frame.Owner.ToInt64() != node.NativeOwner
            || frame.EnumeratorCache.ToInt64() != node.Enumerator || frame.EnumeratorHandle != node.Handle
            || frame.Owner != context.Start.Frame.Arg0 || frame.EnumeratorCache != context.Start.Frame.Arg1
            || frame.StartExitFlags != 0 || frame.RawStartReturn != 0 || frame.StartOutputNode != IntPtr.Zero)
            throw new InvalidOperationException("Inline Begin lacks the exact previously observed synchronous Execute generation.");
        context.Begin = frame; context.Node = node; context.Began = true;
        node.Inline = context; node.InlineCleanupUnproven = true;
    }

    private static bool SameTuple(NativeEngineInlineFrame begin, NativeEngineInlineFrame later)
        => begin.Node == later.Node && begin.Owner == later.Owner && begin.EnumeratorHandle == later.EnumeratorHandle
            && begin.EnumeratorCache == later.EnumeratorCache && begin.EnumeratorType == later.EnumeratorType
            && begin.ReferenceCount == later.ReferenceCount && begin.OutputSlot == later.OutputSlot
            && (begin.Flags & (NativeEngineInlineFlags.SnapshotRequested | NativeEngineInlineFlags.SnapshotValid | NativeEngineInlineFlags.SnapshotFaulted))
                == (later.Flags & (NativeEngineInlineFlags.SnapshotRequested | NativeEngineInlineFlags.SnapshotValid | NativeEngineInlineFlags.SnapshotFaulted));
    private void Release(Context context)
    {
        _main.VerifyAccess();
        if (!ReferenceEquals(context.Owner, this) || !_contexts.Contains(context) || context.Disposed || !context.Exited)
        {
            context.Failed = true; ObserveFault(context.Entered, "Inline logical cleanup lacks its exact completed side scope."); return;
        }
        context.Disposed = true; // Original Start still owns Complete/Dispose and the final commit point.
    }
    internal void ObserveFault(NativeEngineInlineFrame frame, string reason)
    {
        Interlocked.CompareExchange(ref _failure, reason, null); Interlocked.Exchange(ref _unknown, 1);
        _nodes.ObserveInlineFault(reason); _identities.ObserveInlineFault(frame, reason);
    }
    internal NativeEngineCoroutineInlineSnapshot Read()
    {
        _main.VerifyAccess();
        return new(_contexts.Count, _contexts.Count(context => !context.Disposed), _contexts.Count(context => context.NativeBranchObserved),
            Unknown || _nodes.Unknown || _identities.Unknown, Volatile.Read(ref _failure));
    }
    internal bool HasCompletedStart(NativeEngineCoroutineIdentitySnapshot selected)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(selected);
        if (Unknown || _nodes.Unknown || _identities.Unknown) return false;
        var matches = _contexts.Where(context => context.Start.Identity is { } identity
            && identity.Id == selected.IdentityId && identity.StartSequence == selected.StartSequence
            && ReferenceEquals(identity.Start, context.Start) && identity.Node is { } node
            && node.Pointer == selected.Node && node.Generation == selected.EngineGeneration
            && ReferenceEquals(node.Start, context.Start.NodeCall)).ToArray();
        return matches.Length == 1 && matches[0] is { Exited: true, Disposed: true, Failed: false } completedContext
            && completedContext.Start is { Completed: true, Disposed: true }
            && completedContext.Start.NodeCall is { Completed: true, Disposed: true } physical
            && completedContext.MatchesStartCompletion(physical, physical.Completion);
    }
}
