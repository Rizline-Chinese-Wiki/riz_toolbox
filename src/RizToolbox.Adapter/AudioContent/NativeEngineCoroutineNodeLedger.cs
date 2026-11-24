using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal sealed record NativeEngineCoroutineNodeSnapshot(long Generation, long StartSequence, long Node, long NativeOwner,
    long Enumerator, ulong EnumeratorHandle, int ActiveExecutions, int OpenCalls, bool StartCompleted,
    bool DetachObserved, bool DetachReturned, bool FinalCleanupObserved, bool InlineCleanupUnproven,
    long ExecuteCalls, long ReleaseCalls, long DetachCalls, uint LastReleaseByte,
    NativeEngineInlineKind? InlineEndpoint = null, bool InlineBranchObserved = false);
internal sealed record NativeEngineCoroutineLedgerSnapshot(long Calls, long Outstanding, long Untracked, bool Unknown,
    bool Pending, bool EngineCancelled, string? Failure, IReadOnlyList<NativeEngineCoroutineNodeSnapshot> Nodes);

/// <summary>Correlates native-owned scalar records; never reads a pointer or frees
/// a root. A Start's synchronous Execute is matched by actual owner and type-2
/// enumerator cache before the Start returns. ReferenceCount is diagnostic, not
/// execution depth. Last Release AL=0 and detach remain separate observed facts;
/// scheduler/inline cleanup/future producer closure is not inferred from them.</summary>
internal sealed class NativeEngineCoroutineNodeLedger
{
    internal const int MaximumCalls = 2048, MaximumNodes = 512;
    private readonly IMainThreadDispatcher _main;
    private readonly List<Call> _calls = new();
    private readonly List<NodeRecord> _nodes = new();
    private readonly Dictionary<long, NodeRecord> _latest = new();
    private Call? _top;
    private long _sequence, _generation, _untracked;
    private int _unknown;
    private string? _failure;
    internal bool Unknown => Volatile.Read(ref _unknown) != 0;
    internal string? FirstFailure => Volatile.Read(ref _failure);
    internal NativeEngineCoroutineNodeLedger(IMainThreadDispatcher main)
    { ArgumentNullException.ThrowIfNull(main); main.VerifyAccess(); _main = main; }

    // Read-only physical-context proof for the engine identity owner. A copied
    // frame or a caller-created Call cannot stand in for the actual stack top.
    internal bool IsCurrentCall(Call call)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(call);
        return !Unknown && ReferenceEquals(call.Owner, this) && ReferenceEquals(_top, call)
            && call.Thread == Environment.CurrentManagedThreadId && !call.Completed && !call.Disposed
            && call.Frame.IsValid && _calls.Contains(call);
    }

    internal sealed class Call : IDisposable
    {
        internal readonly NativeEngineCoroutineNodeLedger Owner;
        internal readonly NativeEngineNodeFrame Frame;
        internal readonly Call? Parent;
        internal readonly int Thread = Environment.CurrentManagedThreadId;
        internal readonly List<NodeRecord> StartedNodes = new();
        internal NodeRecord? Node;
        internal bool Completed, Disposed, ExecuteEntered;
        internal NativeEngineNodeCompletion Completion;
        internal Call(NativeEngineCoroutineNodeLedger owner, NativeEngineNodeFrame frame)
        { Owner = owner; Frame = frame; Parent = owner._top; }
        public void Dispose() => Owner.Release(this);
    }
    internal sealed class NodeRecord
    {
        internal readonly long Generation, Pointer, NativeOwner, Enumerator;
        internal readonly ulong Handle;
        internal readonly Call Start;
        internal int Executions, OpenCalls;
        internal bool DetachObserved, DetachReturned, FinalCleanup, InlineCleanupUnproven;
        internal long ExecuteCalls, ReleaseCalls, DetachCalls;
        internal uint LastReleaseByte;
        internal NativeEngineCoroutineInlineLedger.Context? Inline;
        internal NodeRecord(long generation, long pointer, NativeEngineNodeFrame frame, Call start)
        { Generation = generation; Pointer = pointer; NativeOwner = frame.Owner.ToInt64(); Enumerator = frame.EnumeratorCache.ToInt64(); Handle = frame.EnumeratorHandle; Start = start; }
        internal bool CanObserveNewGeneration => FinalCleanup && Executions == 0 && OpenCalls == 0 && Start.Completed && Start.Disposed;
        internal NativeEngineCoroutineNodeSnapshot Read() => new(Generation, checked((long)Start.Frame.Sequence), Pointer,
            NativeOwner, Enumerator, Handle, Executions, OpenCalls, Start.Completed, DetachObserved, DetachReturned,
            FinalCleanup, InlineCleanupUnproven, ExecuteCalls, ReleaseCalls, DetachCalls, LastReleaseByte,
            Inline?.Endpoint, Inline?.NativeBranchObserved == true);
    }

    internal Call Capture(NativeEngineNodeFrame frame)
    {
        _main.VerifyAccess();
        var call = new Call(this, frame);
        // Responsibility is registered before identity matching, including when
        // the transport has already reported a failed node snapshot.
        if (_calls.Count == MaximumCalls)
        { ++_untracked; Fault("Engine node physical record capacity exhausted."); return call; }
        _calls.Add(call); _top = call;
        try
        {
            if (!frame.IsValid || frame.Sequence <= unchecked((ulong)_sequence))
                throw new InvalidOperationException("Engine node frame is malformed or repeats a physical sequence.");
            _sequence = checked((long)frame.Sequence);
            if (Unknown || frame.Kind == NativeEngineNodeKind.Start) return call;
            if (frame.SnapshotFlags != (NativeEngineNodeSnapshotFlags.Requested | NativeEngineNodeSnapshotFlags.Valid))
                throw new InvalidOperationException("Engine node Before snapshot is unreadable; original responsibility remains.");
            var pointer = frame.Node.ToInt64();
            _latest.TryGetValue(pointer, out var node);
            var start = MatchingStart(call.Parent, frame);
            if (frame.Kind == NativeEngineNodeKind.Execute && start is not null
                && (node is null || !ReferenceEquals(node.Start, start) && node.CanObserveNewGeneration))
            {
                if (_nodes.Count == MaximumNodes) throw new InvalidOperationException("Engine node generation capacity exhausted.");
                node = new(checked(++_generation), pointer, frame, start); _nodes.Add(node); _latest[pointer] = node; start.StartedNodes.Add(node);
            }
            if (node is null) return call; // Foreign, predating attachment; never invent a Start.
            if (start is not null && !ReferenceEquals(node.Start, start))
                throw new InvalidOperationException("A live native node address was reused inside another Start.");
            call.Node = node; ++node.OpenCalls;
            if (frame.EnumeratorType != 2 || frame.EnumeratorCache.ToInt64() != node.Enumerator || frame.EnumeratorHandle != node.Handle
                || frame.Owner.ToInt64() != node.NativeOwner && !(frame.Owner == IntPtr.Zero && node.DetachObserved))
                throw new InvalidOperationException("Native node scalar identity changed within its observed generation.");
            if (node.FinalCleanup) throw new InvalidOperationException("A cleaned native node reentered without a new exact Start.");
            switch (frame.Kind)
            {
                case NativeEngineNodeKind.Execute: ++node.ExecuteCalls; ++node.Executions; call.ExecuteEntered = true; break;
                case NativeEngineNodeKind.Release: ++node.ReleaseCalls; break;
                case NativeEngineNodeKind.Detach: ++node.DetachCalls; node.DetachObserved = true; break;
            }
        }
        catch (Exception error) { Fault(error.Message); }
        return call;
    }
    private static Call? MatchingStart(Call? top, NativeEngineNodeFrame frame)
    {
        if (frame.Owner == IntPtr.Zero || frame.EnumeratorType != 2 || frame.EnumeratorCache == IntPtr.Zero) return null;
        for (var at = top; at is not null; at = at.Parent)
            if (at.Frame.Kind == NativeEngineNodeKind.Start)
                return !at.Completed && at.Frame.Arg0 == frame.Owner && at.Frame.Arg1 == frame.EnumeratorCache ? at : null;
        return null;
    }

    internal void Complete(Call call, NativeEngineNodeFrame frame, NativeEngineNodeCompletion completion)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(call);
        if (!ReferenceEquals(call.Owner,this) || call.Completed || call.Disposed || call.Thread != Environment.CurrentManagedThreadId
            || !ReferenceEquals(_top,call) || !call.Frame.Equals(frame))
        { Fault("Engine node completion crossed its exact thread, frame or parent."); return; }
        call.Completed = true; call.Completion = completion;
        // Release physical execution depth even after an observer faults. It is
        // our exact Execute window, independent of engine refcount or a result.
        if (call.ExecuteEntered) { --call.Node!.Executions; call.ExecuteEntered = false; }
        if (!completion.IsValidFor(frame.Kind) || (completion.Flags & NativeEngineNodeExitFlags.Abnormal) != 0
            || (completion.Flags & NativeEngineNodeExitFlags.EnterFaulted) != 0)
        { Fault("Engine node original or observer completion is unconfirmed."); return; }
        if (frame.Kind == NativeEngineNodeKind.Start)
        {
            if (completion.OutputFlags != (NativeEngineNodeOutputFlags.Requested | NativeEngineNodeOutputFlags.Read))
            { Fault("Engine Start output slot could not be copied safely."); return; }
            var pointer = completion.StartOutputNode.ToInt64();
            if (pointer != 0 && !call.StartedNodes.Any(node => node.Pointer == pointer))
                Fault("Engine Start output has no exact synchronously observed node generation.");
            foreach (var node in call.StartedNodes)
            {
                if (pointer != node.Pointer && !node.FinalCleanup) node.InlineCleanupUnproven = true;
                if (node.Inline is { } inline && !inline.MatchesStartCompletion(call, completion))
                    Fault("Inline side receipt differs from the actual original Start completion.");
            }
            return;
        }
        if (call.Node is null || Unknown) return;
        if (frame.Kind == NativeEngineNodeKind.Detach) call.Node.DetachReturned = true;
        if (frame.Kind == NativeEngineNodeKind.Release)
        {
            call.Node.LastReleaseByte = completion.RawReturn;
            if (completion.RawReturn == 0) call.Node.FinalCleanup = true;
        }
    }
    private void Release(Call call)
    {
        _main.VerifyAccess();
        if (!ReferenceEquals(call.Owner,this) || !call.Completed || call.Disposed || call.Thread != Environment.CurrentManagedThreadId
            || !ReferenceEquals(_top,call))
        { Fault("Engine node cleanup has no completed exact physical scope."); return; }
        call.Disposed = true; _top = call.Parent;
        if (call.Node is not null) --call.Node.OpenCalls;
        if (call.Frame.Kind == NativeEngineNodeKind.Start && !Unknown)
            foreach (var node in call.StartedNodes)
                if (node.Inline?.MayConfirm(node) == true)
                { node.FinalCleanup = true; node.InlineCleanupUnproven = false; }
    }
    internal bool IsLatest(NodeRecord node) => !Unknown && _latest.TryGetValue(node.Pointer, out var actual) && ReferenceEquals(actual, node);
    internal void ObserveInlineFault(string reason) => Fault(reason);
    internal void ObserveFault(NativeEngineNodeFrame frame, string reason)
    { _ = frame; Fault(reason); } // Worker-safe scalar path: never reads nodes/providers.
    private void Fault(string reason)
    { Interlocked.CompareExchange(ref _failure,reason,null); Interlocked.Exchange(ref _unknown,1); }
    internal NativeEngineCoroutineLedgerSnapshot Read()
    {
        _main.VerifyAccess();
        return new(_calls.Count, _untracked + _calls.LongCount(call => !call.Disposed), _untracked,
            Unknown, Unknown || _nodes.Count != 0 || _calls.Any(call => call.Frame.Kind == NativeEngineNodeKind.Start),
            false, FirstFailure, Array.AsReadOnly(_nodes.Select(node => node.Read()).ToArray()));
    }
}
