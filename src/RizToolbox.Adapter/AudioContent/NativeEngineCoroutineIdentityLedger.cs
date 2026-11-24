using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

// Supplied only after an external object/native-owner verifier has established
// this pair. A scalar value is not a first-install or game Ready credential.
internal readonly record struct NativeEngineCoroutineOwnerPair(long ManagedReceiver, long NativeOwner);
internal sealed record NativeEngineWaitUntilChildSource(long ParentIdentityId, long ParentNode, long ParentNodeGeneration,
    ulong ParentExecuteSequence, ulong YieldSequence, long ManagedReceiver, long Enumerator);
internal sealed record NativeEngineCoroutineIdentitySnapshot(long IdentityId, ulong StartSequence, long NativeOwner,
    NativeCoroutineCancellationIdentity Tracker, long Node, long EngineGeneration, bool NativeStartReturned,
    bool HandleUpgraded, bool StartDisposed, NativeEngineWaitUntilChildSource? WaitUntilSource = null)
{
    internal long Enumerator => WaitUntilSource?.Enumerator ?? Tracker.Enumerator;
    internal long ManagedReceiver => WaitUntilSource?.ManagedReceiver ?? Tracker.Receiver;
}
internal sealed record NativeEngineCoroutineIdentityLedgerSnapshot(long Calls, long Outstanding, long Untracked,
    bool Unknown, bool Pending, bool EngineCancelled, string? Failure,
    IReadOnlyList<NativeEngineCoroutineIdentitySnapshot> Identities, NativeEngineCoroutineLedgerSnapshot Nodes);

/// <summary>Wraps one node ledger exclusively. Foreign physical Starts mask
/// outer association without acquiring run responsibility. The verified
/// WaitUntil yield source can associate its engine-owned child's own lifetime.
/// Neither association grants root release, cancellation completion or Ready.</summary>
internal sealed class NativeEngineCoroutineIdentityLedger
{
    internal const int MaximumProviders = 32, MaximumCalls = 2048, MaximumIdentities = 256, MaximumPhysicalDepth = 2048;
    private readonly IMainThreadDispatcher _main;
    private readonly NativeEngineCoroutineNodeLedger _nodes;
    private readonly NativeCoroutineCancellationIdentityProvider[] _providers;
    private readonly NativeEngineCoroutineOwnerPair[] _pairs;
    private readonly List<Call> _calls = new();
    private readonly List<IdentityRecord> _identities = new();
    private readonly Dictionary<(long Node, long Generation), IdentityRecord> _bindings = new();
    private long[] _knownNodes = Array.Empty<long>();
    private Call? _top;
    private int _unknown;
    private long _untracked, _nextIdentity;
    private int _physicalDepth;
    private bool _physicalOverflow;
    private bool _reading;
    private NativeEngineWaitLedger? _yieldWait;
    private NativeLocalInitializationCoroutineTracker? _yieldTracker;
    private string? _failure;
    internal bool Unknown => Volatile.Read(ref _unknown) != 0;
    internal string? FirstFailure => Volatile.Read(ref _failure);
    internal bool Wraps(NativeEngineCoroutineNodeLedger nodes) => ReferenceEquals(_nodes, nodes);

    // Scheduler payloads may identify an already observed native generation.
    // This lookup reads only retained managed records, never providers or native
    // memory, and does not itself certify that a scheduler row has completed.
    internal bool TryGetSchedulerNode(long payload, out IdentityRecord? identity,
        out NativeEngineCoroutineNodeLedger.NodeRecord? node)
    {
        _main.VerifyAccess(); identity = null; node = null;
        if (payload == 0 || Unknown || _nodes.Unknown) return false;
        foreach (var binding in _bindings)
        {
            if (binding.Key.Node != payload) continue;
            var candidate = binding.Value; var actual = candidate.Node;
            if (actual is null || actual.Pointer != payload || actual.Generation != binding.Key.Generation
                || actual.FinalCleanup || actual.ExecuteCalls <= 0 || !_nodes.IsLatest(actual)) continue;
            if (identity is not null || !_identities.Contains(candidate) || !_calls.Contains(candidate.Start)
                || !ReferenceEquals(candidate.Start.Identity, candidate)
                || !ReferenceEquals(actual.Start, candidate.Start.NodeCall)
                || !actual.Start.StartedNodes.Contains(actual)
                || candidate.StartSequence != actual.Start.Frame.Sequence || candidate.NativeOwner != actual.NativeOwner
                || candidate.Start.Frame.Arg0.ToInt64() != actual.NativeOwner
                || candidate.Start.Frame.Arg1.ToInt64() != actual.Enumerator
                || candidate.Enumerator != actual.Enumerator)
            { identity = null; node = null; return false; }
            identity = candidate; node = actual;
        }
        return identity is not null;
    }

    internal void BindWaitUntilYieldSource(NativeEngineWaitLedger wait, NativeLocalInitializationCoroutineTracker tracker)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(wait); ArgumentNullException.ThrowIfNull(tracker);
        if (_yieldWait is not null || _calls.Count != 0 || _top is not null || Unknown || !wait.IsForIdentityLedger(this)
            || !_providers.Any(provider => ReferenceEquals(provider.Owner, tracker) && provider.Receiver == tracker.ControllerIdentity))
            throw new InvalidOperationException("WaitUntil yield authorization requires this fresh ledger's exact initialization tracker and Wait dispatcher.");
        (_yieldWait, _yieldTracker) = (wait, tracker);
    }

    internal NativeEngineCoroutineIdentityLedger(IMainThreadDispatcher main, NativeEngineCoroutineNodeLedger nodes,
        IReadOnlyList<NativeCoroutineCancellationIdentityProvider> providers, IReadOnlyList<NativeEngineCoroutineOwnerPair> pairs)
    {
        ArgumentNullException.ThrowIfNull(main); main.VerifyAccess(); ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(providers); ArgumentNullException.ThrowIfNull(pairs);
        if (providers.Count is 0 or > MaximumProviders || pairs.Count is 0 or > MaximumProviders)
            throw new ArgumentOutOfRangeException(nameof(providers));
        (_main, _nodes, _providers, _pairs) = (main, nodes, providers.ToArray(), pairs.ToArray());
        var existing = nodes.Read();
        if (existing.Calls != 0 || existing.Outstanding != 0 || existing.Unknown || existing.Nodes.Count != 0)
            throw new ArgumentException("The identity owner must exclusively wrap a fresh native node ledger.", nameof(nodes));
        for (var index = 0; index < _providers.Length; ++index)
        {
            var value = _providers[index];
            if (value is null || value.Owner is null || value.Receiver == 0 || value.Read is null || value.IsCurrent is null
                || _providers.Take(index).Any(prior => ReferenceEquals(prior.Owner, value.Owner))
                || !_pairs.Any(pair => pair.ManagedReceiver == value.Receiver))
                throw new ArgumentException("Distinct scalar providers require an externally verified receiver/native-owner pair.");
        }
        for (var index = 0; index < _pairs.Length; ++index)
        {
            var pair = _pairs[index];
            if (pair.ManagedReceiver == 0 || pair.NativeOwner == 0 || pair.ManagedReceiver == pair.NativeOwner
                || !_providers.Any(provider => provider.Receiver == pair.ManagedReceiver)
                || _pairs.Take(index).Any(prior => prior.ManagedReceiver == pair.ManagedReceiver || prior.NativeOwner == pair.NativeOwner))
                throw new ArgumentException("Verified managed receiver and native owner must remain distinct one-to-one identities.");
        }
    }

    internal sealed class Call : IDisposable
    {
        internal readonly NativeEngineCoroutineIdentityLedger Owner;
        internal readonly NativeEngineNodeFrame Frame;
        internal readonly Call? Parent;
        internal readonly int Thread = Environment.CurrentManagedThreadId;
        internal readonly bool Relevant;
        internal NativeEngineCoroutineNodeLedger.Call? NodeCall;
        internal IdentityRecord? Identity;
        internal bool Completed, Disposed;
        internal Call(NativeEngineCoroutineIdentityLedger owner, NativeEngineNodeFrame frame, bool relevant)
            => (Owner, Frame, Parent, Relevant) = (owner, frame, owner._top, relevant);
        public void Dispose() => Owner.Release(this);
    }
    internal sealed class IdentityRecord
    {
        internal readonly long Id, NativeOwner;
        internal readonly ulong StartSequence;
        internal readonly Call Start;
        internal readonly NativeCoroutineCancellationIdentityProvider? Provider;
        internal readonly NativeEngineWaitUntilChildSource? WaitUntilSource;
        internal NativeCoroutineCancellationIdentity Tracker;
        internal NativeEngineCoroutineNodeLedger.NodeRecord? Node;
        internal bool NativeStartReturned, HandleUpgraded;
        internal long Enumerator => WaitUntilSource?.Enumerator ?? Tracker.Enumerator;
        internal IdentityRecord(long id, long nativeOwner, Call start, NativeCoroutineCancellationIdentityProvider? provider,
            NativeCoroutineCancellationIdentity tracker, NativeEngineWaitUntilChildSource? waitUntilSource = null)
            => (Id, NativeOwner, StartSequence, Start, Provider, Tracker, WaitUntilSource) =
                (id, nativeOwner, start.Frame.Sequence, start, provider, tracker, waitUntilSource);
        internal NativeEngineCoroutineIdentitySnapshot Read() => new(Id, StartSequence, NativeOwner, Tracker,
            Node?.Pointer ?? 0, Node?.Generation ?? 0, NativeStartReturned, HandleUpgraded, Start.Disposed, WaitUntilSource);
    }

    internal bool IsRelevantFrame(NativeEngineNodeFrame frame)
    {
        // Worker-safe: immutable pairs and an immutable published pointer table.
        var owner = frame.Kind == NativeEngineNodeKind.Start ? frame.Arg0.ToInt64() : frame.Owner.ToInt64();
        return _pairs.Any(pair => pair.NativeOwner == owner)
            || frame.Kind != NativeEngineNodeKind.Start && Volatile.Read(ref _knownNodes).Contains(frame.Node.ToInt64());
    }
    internal void ObserveFault(NativeEngineNodeFrame frame, string reason)
    { if (IsRelevantFrame(frame)) Fault(reason); }

    internal bool IsCurrentDetachCall(Call call)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(call);
        if (Unknown || !Exact(call, call.Frame) || !call.Relevant || call.Completed || call.Disposed
            || call.Frame.Kind != NativeEngineNodeKind.Detach || !_calls.Contains(call)
            || call.NodeCall is not { } physical || !_nodes.IsCurrentCall(physical)
            || !NativeEngineNodeObserverDispatcher.SameFrame(physical.Frame, call.Frame)
            || call.Identity is not { } identity || physical.Node is not { } node
            || !ReferenceEquals(identity.Node, node) || !ReferenceEquals(node.Start, identity.Start.NodeCall)
            || !_identities.Contains(identity) || node.Pointer != call.Frame.Node.ToInt64()
            || node.Generation <= 0 || identity.NativeOwner != node.NativeOwner
            || identity.Enumerator != node.Enumerator || identity.StartSequence != node.Start.Frame.Sequence
            || !_bindings.TryGetValue((node.Pointer, node.Generation), out var bound)
            || !ReferenceEquals(bound, identity))
            return false;
        return true;
    }

    internal Call? CaptureInlineStart(NativeEngineInlineObserverDispatcher.Observation receipt)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(receipt);
        var frame = receipt.Frame; var call = _top;
        if (!receipt.IsCurrent || frame.Kind != NativeEngineInlineKind.StartEntered || call is null
            || !Exact(call, receipt.Start.Frame) || call.Frame.Kind != NativeEngineNodeKind.Start
            || call.Completed || call.Disposed || !receipt.Start.IsFor(call.Frame))
            throw new InvalidOperationException("Inline Start cannot borrow an absent, outer or completed tracker physical Start.");
        if (!call.Relevant) return null; // Preserve the actual foreign Start mask without reading providers.
        if (Unknown || !_calls.Contains(call) || call.Identity is null || call.NodeCall is null
            || !_nodes.IsCurrentCall(call.NodeCall) || !ReferenceEquals(call.Identity.Start, call)
            || !_identities.Contains(call.Identity) || frame.Owner != call.Frame.Arg0
            || frame.EnumeratorCache != call.Frame.Arg1 || frame.OutputSlot != call.Frame.Arg3)
        {
            // The dispatcher already reports this exception. Describe copied scalars
            // and the first retained reasons without re-reading a native object or provider.
            var start = call.Frame;
            var ownerPair = Array.FindIndex(_pairs, pair => pair.NativeOwner == start.Arg0.ToInt64());
            var nodeScopeCurrent = call.NodeCall is { } nodeCall && _nodes.IsCurrentCall(nodeCall);
            throw new InvalidOperationException("Inline Start has no exact current owned identity and node-ledger scope. "
                + $"startSequence={start.Sequence} parentStartSequence={frame.ParentStartSequence} depth={frame.Depth} "
                + $"caller=0x{start.Caller:x} startTarget=0x{receipt.Identity.Start.Target:x} ownerPair={ownerPair} "
                + $"nativeOwner=0x{start.Arg0:x} enumerator=0x{start.Arg1:x} nameArgumentPresent={start.Arg2 != IntPtr.Zero} "
                + $"identityAttached={call.Identity is not null} nodeScopeAttached={call.NodeCall is not null} nodeScopeCurrent={nodeScopeCurrent} "
                + $"identityUnknown={Unknown} nodeUnknown={_nodes.Unknown} "
                + $"identityFailure=[{BoundFailure(Volatile.Read(ref _failure))}] nodeFailure=[{BoundFailure(_nodes.FirstFailure)}]");
        }
        return call;
    }

    private static string BoundFailure(string? failure)
    {
        const int limit = 128;
        if (failure is null) return "none";
        return failure.Length <= limit ? failure : failure[..(limit - 3)] + "...";
    }

    internal bool IsCurrentInlineStart(Call call, NativeEngineInlineObserverDispatcher.Observation receipt)
    {
        _main.VerifyAccess();
        return !Unknown && receipt.IsCurrent && Exact(call, call.Frame) && call.Relevant
            && !call.Completed && !call.Disposed && _calls.Contains(call) && receipt.Start.IsFor(call.Frame)
            && call.NodeCall is not null && _nodes.IsCurrentCall(call.NodeCall)
            && call.Identity is { } identity && ReferenceEquals(identity.Start, call) && _identities.Contains(identity)
            && (identity.Node is null || ReferenceEquals(identity.Node.Start, call.NodeCall)
                && _bindings.TryGetValue((identity.Node.Pointer, identity.Node.Generation), out var binding)
                && ReferenceEquals(binding, identity));
    }

    internal void ObserveInlineFault(NativeEngineInlineFrame frame, string reason)
    {
        // Worker-safe, immutable ownership scalars only. The original four-entry
        // runtime also retains its separate sticky transport fault obligation.
        if (_pairs.Any(pair => pair.NativeOwner == frame.Owner.ToInt64())
            || frame.Node != IntPtr.Zero && Volatile.Read(ref _knownNodes).Contains(frame.Node.ToInt64())) Fault(reason);
    }

    internal Call Capture(NativeEngineNodeFrame frame)
    {
        _main.VerifyAccess();
        if (_physicalOverflow || _physicalDepth == MaximumPhysicalDepth)
        {
            // No physical parent record is admitted past this bound. Keep an
            // irreversible untracked obligation instead of silently borrowing
            // the last retained parent after an overflow. Existing exact
            // contexts may still unwind, but future captures remain blocked.
            _physicalOverflow = true; _untracked = checked(_untracked + 1);
            Fault("Engine/tracker physical mask depth exhausted; untracked responsibility remains permanent.");
            throw new InvalidOperationException("Engine/tracker physical mask depth exhausted.");
        }
        var nearest = NearestStart(_top);
        var known = frame.Kind != NativeEngineNodeKind.Start && Volatile.Read(ref _knownNodes).Contains(frame.Node.ToInt64());
        var relevant = IsRelevantFrame(frame);
        var call = new Call(this, frame, relevant); _top = call; ++_physicalDepth;
        if (!relevant) return call; // A foreign Start still occupies the exact physical parent stack.
        if (_calls.Count == MaximumCalls)
        { ++_untracked; Fault("Engine/tracker physical record capacity exhausted."); return call; }
        _calls.Add(call); // Before node/provider callbacks, including their partial failure.
        try
        {
            if (!frame.IsValid) throw new InvalidOperationException("Engine/tracker frame differs from its transport shape.");
            if (frame.Kind != NativeEngineNodeKind.Start && !known && nearest is { Relevant: false })
                throw new InvalidOperationException("Owned native node has no associated generation inside a foreign Start; scalar responsibility remains.");
            call.NodeCall = _nodes.Capture(frame);
            if (_nodes.Unknown) throw new InvalidOperationException("Native node correlation is Unknown.");
            if (Unknown) return call;
            if (frame.Kind == NativeEngineNodeKind.Start)
            {
                var pair = _pairs.Single(value => value.NativeOwner == frame.Arg0.ToInt64());
                var matches = ReadCurrent(pair.ManagedReceiver).Where(value => value.Identity.Enumerator == frame.Arg1.ToInt64()).ToArray();
                if (_identities.Count == MaximumIdentities) throw new InvalidOperationException("Engine/tracker identity capacity exhausted.");
                if (_yieldWait is { } wait && frame.Caller == wait.WaitUntilChildCaller)
                {
                    if (matches.Length != 0)
                        throw new InvalidOperationException("A nested WaitUntil Start cannot borrow a managed Start identity.");
                    call.Identity = CaptureWaitUntilChild(call, pair, wait);
                    _identities.Add(call.Identity); return call;
                }
                if (matches.Length != 1)
                    throw new InvalidOperationException($"Owned engine Start has no unique current tracker Enumerator identity (matches={matches.Length}).");
                var match = matches[0];
                if (match.Identity.StartReturned)
                    throw new InvalidOperationException("An already returned tracker Start cannot lend its identity to a new native Start.");
                if (_identities.Any(prior => SameKey(prior.Tracker, match.Identity)))
                    throw new InvalidOperationException("A tracker record cannot lend its identity to a second native Start.");
                call.Identity = new(checked(++_nextIdentity), pair.NativeOwner, call, match.Provider, match.Identity);
                _identities.Add(call.Identity); return call;
            }
            var node = call.NodeCall.Node;
            if (node is null) throw new InvalidOperationException("Owned native node has no observed exact Start generation.");
            if (!_bindings.TryGetValue((node.Pointer, node.Generation), out var binding))
            {
                var start = NearestStart(call.Parent);
                if (start?.Identity is null || !ReferenceEquals(node.Start, start.NodeCall))
                    throw new InvalidOperationException("Native node cannot borrow a foreign or outer tracker Start identity.");
                binding = start.Identity;
                if (binding.Node is not null && !ReferenceEquals(binding.Node, node))
                    throw new InvalidOperationException("A tracker Start was associated with multiple native node generations.");
                binding.Node = node; _bindings.Add((node.Pointer, node.Generation), binding);
                PublishKnownNodes();
            }
            call.Identity = binding;
            if (node.NativeOwner != binding.NativeOwner || node.Enumerator != binding.Enumerator)
                throw new InvalidOperationException("Native node and tracker identities no longer agree.");
        }
        catch (Exception error) { Fault(error.Message); }
        return call;
    }

    private IdentityRecord CaptureWaitUntilChild(Call child, NativeEngineCoroutineOwnerPair pair, NativeEngineWaitLedger wait)
    {
        var tracker = _yieldTracker!;
        if (child.Parent is not { } execute || execute.Identity is not { } parent || parent.Node is not { } node
            || !ReferenceEquals(parent.Tracker.Owner, tracker) || parent.Tracker.Receiver != pair.ManagedReceiver
            || parent.Tracker.Slot != (int)InitializationCoroutineRole.WaitUntil || parent.NativeOwner != pair.NativeOwner
            || parent.NativeStartReturned || parent.Tracker.StartReturned || parent.Tracker.NaturalTerminal
            || !_identities.Contains(parent) || !_calls.Contains(execute) || !_nodes.IsLatest(node)
            || !_bindings.TryGetValue((node.Pointer, node.Generation), out var retained) || !ReferenceEquals(retained, parent)
            || _identities.Any(record => record.WaitUntilSource?.ParentIdentityId == parent.Id)
            || !wait.TryReadWaitUntilYieldStart(child, execute, out var sequence))
            throw new InvalidOperationException("Nested WaitUntil Start lacks its exact live parent Execute and Yield scope.");

        var enumerator = child.Frame.Arg1.ToInt64();
        if (!tracker.IsCurrentWaitUntilYield(parent.Enumerator, enumerator) || Unknown || !Exact(child, child.Frame)
            || !wait.TryReadWaitUntilYieldStart(child, execute, out var after) || after != sequence
            || _identities.Any(record => record.WaitUntilSource?.ParentIdentityId == parent.Id))
            throw new InvalidOperationException("Nested WaitUntil Start lacks the exact current managed WaitUntil yield.");

        var source = new NativeEngineWaitUntilChildSource(parent.Id, node.Pointer, node.Generation,
            execute.Frame.Sequence, sequence, pair.ManagedReceiver, enumerator);
        return new(checked(++_nextIdentity), pair.NativeOwner, child, null, default, source);
    }

    internal void Complete(Call call, NativeEngineNodeFrame frame, NativeEngineNodeCompletion completion)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(call);
        if (!Exact(call, frame) || call.Completed || call.Disposed)
        { if (call.Relevant || HasRelevantParent(_top)) Fault("Engine/tracker completion crossed its exact frame, thread or parent."); return; }
        call.Completed = true;
        if (call.NodeCall is not null)
        {
            _nodes.Complete(call.NodeCall, frame, completion);
            if (_nodes.Unknown) Fault("Native node completion is Unknown.");
        }
        if (!call.Relevant) return;
        if (!completion.IsValidFor(frame.Kind) || (completion.Flags & (NativeEngineNodeExitFlags.Abnormal | NativeEngineNodeExitFlags.EnterFaulted)) != 0)
        { Fault("Engine/tracker original or observer completion is unconfirmed."); return; }
        if (frame.Kind == NativeEngineNodeKind.Start && call.Identity is not null) call.Identity.NativeStartReturned = true;
    }
    private void Release(Call call)
    {
        _main.VerifyAccess();
        if (!Exact(call, call.Frame) || !call.Completed || call.Disposed)
        { if (call.Relevant || HasRelevantParent(_top)) Fault("Engine/tracker cleanup has no exact completed scope."); return; }
        if (call.NodeCall is not null)
        {
            call.NodeCall.Dispose();
            if (!call.NodeCall.Disposed || _nodes.Unknown) Fault("Native node scope cleanup is unconfirmed.");
        }
        call.Disposed = true; _top = call.Parent; --_physicalDepth;
        if (call.Relevant) PublishKnownNodes();
    }
    private void PublishKnownNodes()
        => Volatile.Write(ref _knownNodes, _bindings.Where(value => value.Value.Node is { CanObserveNewGeneration: false })
            .Select(value => value.Key.Node).Distinct().ToArray());
    internal void RefreshTrackedIdentities()
    {
        _main.VerifyAccess(); if (Unknown) return;
        if (_nodes.Unknown) { Fault("Native node observation is Unknown during tracker refresh."); return; }
        try
        {
            foreach (var record in _identities)
            {
                if (record.WaitUntilSource is not null) continue;
                // Historical engine generations need not borrow a reused tracker
                // slot. This scalar fact never authorizes tracker/root retirement.
                if (record.Node is { CanObserveNewGeneration: true } && record.Tracker.StartReturned) continue;
                var current = ReadCurrent(record.Tracker.Receiver).Where(value => ReferenceEquals(value.Provider, record.Provider)
                    && value.Identity.Slot == record.Tracker.Slot).ToArray();
                if (current.Length != 1 || !CanUpgrade(record.Tracker, current[0].Identity))
                    throw new InvalidOperationException("Tracked engine identity changed owner, slot, generation, Enumerator or confirmed handle.");
                var next = current[0].Identity;
                if (record.Tracker.ObservedHandle != next.ObservedHandle || record.Tracker.HandleConfirmed != next.HandleConfirmed)
                    record.HandleUpgraded = true;
                record.Tracker = next;
            }
        }
        catch (Exception error) { Fault(error.Message); }
    }
    private (NativeCoroutineCancellationIdentityProvider Provider, NativeCoroutineCancellationIdentity Identity)[] ReadCurrent(long receiver)
    {
        if (_reading) throw new InvalidOperationException("Engine/tracker identity provider reentered its reader.");
        _reading = true;
        try
        {
            var rows = new List<(NativeCoroutineCancellationIdentityProvider Provider, NativeCoroutineCancellationIdentity Identity)>();
            foreach (var provider in _providers.Where(value => value.Receiver == receiver))
            {
                RequireKnown(); var supplied = provider.Read(); RequireKnown();
                if (supplied is null || supplied.Count > MaximumIdentities - rows.Count)
                    throw new InvalidOperationException("Engine/tracker provider snapshot is missing or exceeds capacity.");
                foreach (var identity in supplied.ToArray())
                {
                    RequireKnown();
                    if (!ReferenceEquals(identity.Owner, provider.Owner) || identity.Receiver != receiver || identity.Slot < 0 || identity.Generation < 0)
                        throw new InvalidOperationException("Engine/tracker provider returned a foreign or malformed scalar identity.");
                    if (!identity.Published || identity.Retiring) continue;
                    if (!identity.Known || identity.Enumerator == 0 || identity.HandleConfirmed && (!identity.StartReturned || identity.ObservedHandle == 0)
                        || !provider.IsCurrent(identity))
                        throw new InvalidOperationException("Engine/tracker identity is no longer a current known published record.");
                    RequireKnown();
                    if (rows.Any(prior => ReferenceEquals(prior.Provider.Owner, provider.Owner) && prior.Identity.Slot == identity.Slot))
                        throw new InvalidOperationException("Engine/tracker provider duplicated an owner slot.");
                    rows.Add((provider, identity));
                }
            }
            return rows.ToArray();
        }
        finally { _reading = false; }
    }
    private static bool SameKey(NativeCoroutineCancellationIdentity left, NativeCoroutineCancellationIdentity right)
        => ReferenceEquals(left.Owner, right.Owner) && left.Receiver == right.Receiver && left.Slot == right.Slot && left.Generation == right.Generation;
    private static bool CanUpgrade(NativeCoroutineCancellationIdentity previous, NativeCoroutineCancellationIdentity next)
        => SameKey(previous, next) && next.SameRecord(previous) && (!previous.StartReturned || next.StartReturned)
            && (!previous.NaturalTerminal || next.NaturalTerminal)
            && (!next.HandleConfirmed || next.StartReturned && next.ObservedHandle != 0)
            && (previous.ObservedHandle == next.ObservedHandle || previous.ObservedHandle == 0 && !previous.HandleConfirmed && !previous.StartReturned);
    private bool Exact(Call call, NativeEngineNodeFrame frame)
        => ReferenceEquals(call.Owner, this) && call.Thread == Environment.CurrentManagedThreadId && ReferenceEquals(_top, call)
            && NativeEngineNodeObserverDispatcher.SameFrame(call.Frame, frame);
    private static Call? NearestStart(Call? at)
    { for (; at is not null; at = at.Parent) if (at.Frame.Kind == NativeEngineNodeKind.Start) return at; return null; }
    private static bool HasRelevantParent(Call? at)
    { for (; at is not null; at = at.Parent) if (at.Relevant) return true; return false; }
    private void RequireKnown() { if (Unknown) throw new InvalidOperationException("Engine/tracker observation remains Unknown."); }
    private void Fault(string reason)
    { reason ??= "Engine/tracker observation failed."; Interlocked.CompareExchange(ref _failure, reason.Length > 768 ? reason[..768] : reason, null); Interlocked.Exchange(ref _unknown, 1); }
    internal NativeEngineCoroutineIdentityLedgerSnapshot Read()
    {
        _main.VerifyAccess(); var nodes = _nodes.Read();
        return new(_calls.Count, _untracked + _calls.LongCount(call => !call.Disposed), _untracked,
            Unknown || nodes.Unknown, Unknown || nodes.Unknown || _calls.Count != 0, false, Volatile.Read(ref _failure),
            Array.AsReadOnly(_identities.Select(value => value.Read()).ToArray()), nodes);
    }
}
