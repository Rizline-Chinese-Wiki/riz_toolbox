using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal enum NativeEngineCoroutineReceiverRole { Controller, EventTrigger }
internal enum NativeEngineCoroutineReceiverPhase { Live, AwaitingIndependentDeath, ConfirmedDead }
internal sealed record NativeEngineCoroutineReceiverSnapshot(NativeEngineCoroutineReceiverRole Role,
    NativeEngineCoroutineReceiverPhase Phase, NativeUnityCoroutineOwner Retained, long CachedNativeOwner,
    bool ZeroObserved, bool IndependentDeathObserved);
internal sealed record NativeEngineCoroutineReceiverLifetimeSnapshot(bool MayCommit, bool AwaitingIndependentDeath,
    bool Pending, bool Unknown, string? Failure, IReadOnlyList<NativeEngineCoroutineReceiverSnapshot> Receivers);

/// <summary>Two permanent receiver roles, with separate destruction sources.
/// Stable zero is an observation gap until that role's independent proof arrives.
/// Confirmed wrapper death neither closes producers nor releases any ownership.</summary>
internal sealed class NativeEngineCoroutineReceiverLifetime
{
    private readonly IMainThreadDispatcher _main;
    private readonly LocalRunSessionIdentity _session;
    private readonly NativeUnityCoroutineOwner[] _retained;
    private readonly ActiveDestructionIdentity _destruction;
    private readonly NativeCutoutEventObservation _source;
    private readonly long[] _cached = new long[2];
    private readonly bool[] _zero = new bool[2], _death = new bool[2];
    private bool _sourceAlive = true;
    private int _unknown;
    private string? _failure;

    internal NativeEngineCoroutineReceiverLifetime(IMainThreadDispatcher main, LocalRunSessionIdentity session,
        IReadOnlyList<NativeUnityCoroutineOwner> retained, ActiveDestructionIdentity destruction,
        NativeCutoutEventObservation source)
    {
        ArgumentNullException.ThrowIfNull(main); main.VerifyAccess(); ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(retained); ArgumentNullException.ThrowIfNull(destruction);
        ArgumentNullException.ThrowIfNull(source);
        var objects = new[] { destruction.Controller, destruction.CloneGameObject, destruction.CloneTransform,
            destruction.StagingGameObject, destruction.StagingTransform };
        if (retained.Count != 2 || retained.Any(value => value.ManagedReceiver == 0 || value.NativeOwner == 0
                || value.ManagedClass == IntPtr.Zero || value.ManagedReceiver == value.NativeOwner)
            || retained[0].ManagedReceiver == retained[1].ManagedReceiver || retained[0].NativeOwner == retained[1].NativeOwner
            || objects.Any(value => value == 0) || objects.Distinct().Count() != 5
            || destruction.Controller != retained[0].ManagedReceiver || source.Controller != retained[0].ManagedReceiver
            || source.EventTrigger != retained[1].ManagedReceiver || !source.Alive || source.Triggers is null
            || source.Triggers.Count > 32 || source.Triggers.Any(row => row is null || row.Trigger == 0 || row.Events == 0
                || !float.IsFinite(BitConverter.Int32BitsToSingle(row.OffsetBits)))
            || source.GameObject == 0 || source.Transform == 0 || source.List == 0)
            throw new ArgumentException("Two live exact receiver roles and their independent retained sources are required.");
        (_main, _session, _retained, _destruction) = (main, session, retained.ToArray(), destruction);
        _source = source with { Triggers = Array.AsReadOnly(source.Triggers.ToArray()) };
        for (var index = 0; index < 2; ++index) _cached[index] = _retained[index].NativeOwner;
    }

    internal bool Unknown => Volatile.Read(ref _unknown) != 0;
    internal void ObserveReceiver(NativeEngineCoroutineReceiverRole role, NativeUnityCoroutineOwnerObservation value)
    {
        Guard(); var index = Index(role); var old = _retained[index];
        if (value.ManagedReceiver != old.ManagedReceiver || value.ManagedClass != old.ManagedClass
            || value.CachedNativeOwner != 0 && value.CachedNativeOwner != old.NativeOwner)
            Reject("A retained receiver changed its managed identity, class or copied native owner.");
        if (_zero[index] && value.CachedNativeOwner != 0)
            Reject("A retained receiver became live after its cached pointer was observed zero.");
        _cached[index] = value.CachedNativeOwner;
        if (value.CachedNativeOwner == 0) _zero[index] = true;
    }

    internal void ObserveControllerDeath(bool exactSource, bool confirmedThisTick, NativeLocalActiveDestructionSnapshot value)
    {
        Guard(); ArgumentNullException.ThrowIfNull(value);
        if (!exactSource || value.Unknown || value.Closed || value.Session != _session || value.Identity != _destruction)
            Reject("Controller death did not come from this retained exact protected destruction source.");
        if (_death[0] && !value.CloneDestroyed)
            Reject("The independently confirmed controller destruction fact was withdrawn.");
        if (!confirmedThisTick) return;
        if (!value.CloneDestroyed || value.CutoutObservedFrame < 0 || value.CloneConfirmedFrame <= value.CutoutObservedFrame)
            Reject("Controller death requires this source's later-frame clone confirmation.");
        _death[0] = true;
    }

    internal void ObserveEventTriggerDeath(bool exactSource, NativeLocalCutoutEventSourceSnapshot value)
    {
        Guard(); ArgumentNullException.ThrowIfNull(value);
        var next = value.Source;
        if (!exactSource || value.Unknown || value.Session != _session || next is null
            || next.Controller != _source.Controller || next.EventTrigger != _source.EventTrigger
            || next.GameObject != _source.GameObject || next.Transform != _source.Transform
            || next.List != _source.List || next.Version != _source.Version || next.Triggers is null
            || !next.Triggers.SequenceEqual(_source.Triggers))
            Reject("EventTrigger death did not come from its retained exact source identity.");
        var observed = next!;
        if (!_sourceAlive && observed.Alive) Reject("The independently dead cutout source became live again.");
        _sourceAlive = observed.Alive;
        // Alive is the closure's aggregate liveness, so it cannot by itself prove
        // EventTrigger death. ConfirmedDead also requires that receiver's zero.
        if (!observed.Alive) _death[1] = true;
    }

    internal void RequireNewOwnedEntry(long nativeOwner)
    {
        Guard(); var index = Array.FindIndex(_retained, value => value.NativeOwner == nativeOwner);
        if (index < 0) Reject("An owned engine entry has no exact retained native owner role.");
        if (_zero[index] || _death[index])
            Reject("A new owned engine entry arrived after receiver death observation; no late-entry contract is admitted.");
    }

    internal NativeEngineCoroutineReceiverLifetimeSnapshot Read()
    {
        _main.VerifyAccess();
        var values = Enumerable.Range(0, 2).Select(index => new NativeEngineCoroutineReceiverSnapshot(
            (NativeEngineCoroutineReceiverRole)index,
            _zero[index] && _death[index] ? NativeEngineCoroutineReceiverPhase.ConfirmedDead
                : _zero[index] || _death[index] ? NativeEngineCoroutineReceiverPhase.AwaitingIndependentDeath
                : NativeEngineCoroutineReceiverPhase.Live,
            _retained[index], _cached[index], _zero[index], _death[index])).ToArray();
        return new(!Unknown && values.All(value => value.Phase == NativeEngineCoroutineReceiverPhase.Live),
            values.Any(value => value.Phase == NativeEngineCoroutineReceiverPhase.AwaitingIndependentDeath),
            true, Unknown, Volatile.Read(ref _failure), Array.AsReadOnly(values));
    }
    private void Guard()
    { _main.VerifyAccess(); if (Unknown) throw new InvalidOperationException("Receiver lifetime observation is permanently Unknown."); }
    private static int Index(NativeEngineCoroutineReceiverRole role)
        => role is NativeEngineCoroutineReceiverRole.Controller or NativeEngineCoroutineReceiverRole.EventTrigger
            ? (int)role : throw new ArgumentOutOfRangeException(nameof(role));
    private void Reject(string reason)
    { Interlocked.CompareExchange(ref _failure, reason, null); Interlocked.Exchange(ref _unknown, 1); throw new InvalidOperationException(reason); }
}

/// <summary>Only guards retirement of already naturally ended observer roots.
/// Missing native final cleanup stays Pending, including inline cleanup. This is
/// unrelated to cancellation completion or whole-run consumer retirement.</summary>
internal static class NativeEngineCoroutineObserverRetirement
{
    internal static bool MayRetire(ExactHookBroker.ManagedBoundaryActivity activity,
        NativeEngineCoroutineIdentityLedgerSnapshot value, IReadOnlyList<NativeCoroutineCancellationIdentity> current)
    {
        if (!activity.Known || activity.InFlight != 0 || value.Unknown || value.Outstanding != 0 || value.Untracked != 0
            || value.Nodes.Unknown || value.Nodes.Outstanding != 0 || value.Nodes.Untracked != 0
            || value.Nodes.Nodes.Count != value.Identities.Count)
            return false;
        foreach (var row in current.Where(row => row.Published && !row.Retiring))
        {
            if (!row.Known || !row.NaturalTerminal || !row.StartReturned) return false;
            var matches = value.Identities.Where(identity => ReferenceEquals(identity.Tracker.Owner, row.Owner)
                && identity.Tracker.Receiver == row.Receiver && identity.Tracker.Slot == row.Slot
                && identity.Tracker.Generation == row.Generation && identity.Tracker.Enumerator == row.Enumerator
                && row.SameRecord(identity.Tracker) && identity.Tracker.StartReturned
                && identity.Tracker.ObservedHandle == row.ObservedHandle && identity.Tracker.HandleConfirmed == row.HandleConfirmed).ToArray();
            if (matches.Length != 1) return false;
        }
        foreach (var identity in value.Identities)
        {
            if (!HasRetainedSource(value, identity) || !HasFinalNode(value, identity)) return false;
        }
        return true;
    }

    internal static bool HasRetainedSource(NativeEngineCoroutineIdentityLedgerSnapshot state, NativeEngineCoroutineIdentitySnapshot identity)
    {
        if (identity.WaitUntilSource is not { } source) return identity.Tracker.Enumerator != 0;
        var parents = state.Identities.Where(parent => parent.IdentityId == source.ParentIdentityId).ToArray();
        return source.ParentIdentityId > 0 && source.ParentNode != 0 && source.ParentNodeGeneration > 0
            && source.YieldSequence > 0 && source.Enumerator != 0 && source.ManagedReceiver != 0
            && parents.Length == 1 && parents[0] is { WaitUntilSource: null } parent
            && parent.NativeOwner == identity.NativeOwner && parent.Node == source.ParentNode
            && parent.EngineGeneration == source.ParentNodeGeneration && parent.Tracker.Receiver == source.ManagedReceiver
            && parent.Tracker.Slot == (int)InitializationCoroutineRole.WaitUntil && parent.Tracker.Enumerator != 0
            && parent.Tracker.Enumerator != source.Enumerator && parent.StartSequence < source.ParentExecuteSequence
            && source.ParentExecuteSequence < identity.StartSequence;
    }

    internal static bool YieldChildrenFinal(NativeEngineCoroutineIdentityLedgerSnapshot state, NativeEngineCoroutineIdentitySnapshot parent)
        => state.Identities.Where(identity => identity.WaitUntilSource?.ParentIdentityId == parent.IdentityId)
            .All(child => HasRetainedSource(state, child) && HasFinalNode(state, child));

    private static bool HasFinalNode(NativeEngineCoroutineIdentityLedgerSnapshot state, NativeEngineCoroutineIdentitySnapshot identity)
    {
        if (identity.Node == 0 || identity.EngineGeneration <= 0 || identity.NativeOwner == 0 || identity.Enumerator == 0
            || identity.StartSequence == 0 || identity.StartSequence > (ulong)long.MaxValue || !identity.StartDisposed || !identity.NativeStartReturned)
            return false;
        var matches = state.Nodes.Nodes.Where(node => node.Node == identity.Node && node.Generation == identity.EngineGeneration
            && node.NativeOwner == identity.NativeOwner && node.Enumerator == identity.Enumerator
            && node.StartSequence == checked((long)identity.StartSequence)).ToArray();
        return matches.Length == 1 && matches[0].FinalCleanupObserved && matches[0].StartCompleted
            && matches[0].ActiveExecutions == 0 && matches[0].OpenCalls == 0
            && (identity.WaitUntilSource is null || !matches[0].InlineCleanupUnproven);
    }
}
