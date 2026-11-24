using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal enum NativeEngineObserverRetirementPhase { Prepared, Withdrawn, FreeAttempted, Completed, Unknown }
internal sealed record NativeEngineObserverRetirementSnapshot(NativeCoroutineCancellationIdentity Tracker, long NativeOwner,
    long Node, long EngineGeneration, ulong StartSequence, NativeEngineObserverRetirementPhase Phase);

/// <summary>Issues one exact record's observation-root retirement permission from
/// the actual identity ledger. Other running nodes and unpublished factories do
/// not borrow or block that permission. No cancellation/run-end authority exists.</summary>
internal sealed class NativeEngineCoroutineObserverRetirementAuthority
{
    private readonly IMainThreadDispatcher _main;
    private readonly NativeEngineCoroutineIdentityLedger _identities;
    private readonly NativeCoroutineCancellationIdentityProvider[] _providers;
    private readonly Func<ExactHookBroker.ManagedBoundaryActivity> _readActivity;
    private readonly Func<bool> _sourceKnown;
    private readonly Func<NativeEngineCoroutineIdentitySnapshot, bool> _recordReady;
    private readonly Action<string> _fault;
    private readonly NativeEngineCoroutineRuntimeCoordinator? _productionOwner;
    private readonly LocalRunLaunchCommitState? _launch;
    private readonly List<Permit> _permits = new();
    private readonly object _issuer = new();
    private bool _reading, _readingActivity, _readingReadiness;
    private int _unknown;
    internal bool Controlled => _productionOwner is null;
    internal bool Unknown => Volatile.Read(ref _unknown) != 0 || _identities.Unknown || !_sourceKnown();
    internal IReadOnlyList<NativeEngineObserverRetirementSnapshot> ReadPermits()
    { _main.VerifyAccess(); return Array.AsReadOnly(_permits.Select(permit => permit.Read()).ToArray()); }

    private NativeEngineCoroutineObserverRetirementAuthority(IMainThreadDispatcher main,
        NativeEngineCoroutineIdentityLedger identities, IReadOnlyList<NativeCoroutineCancellationIdentityProvider> providers,
        Func<ExactHookBroker.ManagedBoundaryActivity> readActivity, Func<bool> sourceKnown, Action<string> fault,
        NativeEngineCoroutineRuntimeCoordinator? productionOwner, LocalRunLaunchCommitState? launch,
        Func<NativeEngineCoroutineIdentitySnapshot, bool> recordReady)
    {
        ArgumentNullException.ThrowIfNull(main); main.VerifyAccess(); ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(providers); ArgumentNullException.ThrowIfNull(readActivity);
        ArgumentNullException.ThrowIfNull(sourceKnown); ArgumentNullException.ThrowIfNull(fault);
        ArgumentNullException.ThrowIfNull(recordReady);
        if (providers.Count is 0 or > NativeEngineCoroutineIdentityLedger.MaximumProviders
            || providers.Any(provider => provider is null || provider.Owner is null || provider.Receiver == 0
                || provider.Read is null || provider.IsCurrent is null)
            || providers.Where((provider, index) => providers.Take(index).Any(previous => ReferenceEquals(previous.Owner, provider.Owner))).Any())
            throw new ArgumentException("Exact bounded distinct scalar providers are required.");
        (_main, _identities, _providers, _readActivity, _sourceKnown, _fault, _productionOwner, _launch) =
            (main, identities, providers.ToArray(), readActivity, sourceKnown, fault, productionOwner, launch);
        _recordReady = recordReady;
    }
    internal static NativeEngineCoroutineObserverRetirementAuthority ForProduction(IMainThreadDispatcher main,
        NativeEngineCoroutineRuntimeCoordinator owner, LocalRunLaunchCommitState launch,
        NativeEngineCoroutineIdentityLedger identities, IReadOnlyList<NativeCoroutineCancellationIdentityProvider> providers,
        Func<ExactHookBroker.ManagedBoundaryActivity> readActivity, Func<bool> sourceKnown, Action<string> fault,
        Func<NativeEngineCoroutineIdentitySnapshot, bool> recordReady)
    {
        ArgumentNullException.ThrowIfNull(owner); ArgumentNullException.ThrowIfNull(launch);
        if (launch.Read().ActivationAttempted) throw new InvalidOperationException("Production retirement authority binds before activation.");
        return new(main, identities, providers, readActivity, sourceKnown, fault, owner, launch, recordReady);
    }
    // Controlled ledgers/providers exercise this exact permit implementation.
    // They cannot attach to a production launch/tracker or admit native startup.
    internal static NativeEngineCoroutineObserverRetirementAuthority ForControlled(IMainThreadDispatcher main,
        NativeEngineCoroutineIdentityLedger identities, IReadOnlyList<NativeCoroutineCancellationIdentityProvider> providers,
        Func<ExactHookBroker.ManagedBoundaryActivity> readActivity, Func<bool> sourceKnown, Action<string> fault,
        Func<NativeEngineCoroutineIdentitySnapshot, bool>? recordReady = null)
        => new(main, identities, providers, readActivity, sourceKnown, fault, null, null, recordReady ?? (_ => true));

    internal bool IsForProduction(NativeEngineCoroutineRuntimeCoordinator owner, LocalRunLaunchCommitState launch,
        object tracker, long receiver)
    {
        _main.VerifyAccess();
        return !Controlled && ReferenceEquals(_productionOwner, owner) && ReferenceEquals(_launch, launch)
            && owner.IsIssuedObserverRetirementAuthority(this, launch, _identities, _providers)
            && !launch.Read().ActivationAttempted && !Unknown && Owns(tracker, receiver);
    }
    internal bool IsForControlled(object tracker, long receiver)
    { _main.VerifyAccess(); return Controlled && !Unknown && Owns(tracker, receiver); }
    private bool Owns(object tracker, long receiver)
        => _providers.Any(provider => ReferenceEquals(provider.Owner, tracker) && provider.Receiver == receiver);
    internal void ObserveFailure(object tracker, string reason)
    {
        _main.VerifyAccess();
        foreach (var permit in _permits.Where(permit => ReferenceEquals(permit.Tracker.Owner, tracker))) permit.MarkFailure();
        Interlocked.Exchange(ref _unknown, 1);
        try { _fault(reason); } catch { } // Never replace the original cleanup failure.
    }

    internal bool TryBegin(NativeCoroutineCancellationIdentity identity, out Permit? permit)
    {
        _main.VerifyAccess(); permit = null; RequireKnown();
        if (_reading) Reject("Observer retirement reentered its scalar identity reader.");
        if (_permits.Any(previous => SameKey(previous.Tracker, identity)))
            Reject("An exact observer record already owns its one retirement permit.");
        if (!identity.Known || !identity.Published || identity.Retiring || !identity.NaturalTerminal
            || !identity.StartReturned || identity.Enumerator == 0 || !Owns(identity.Owner, identity.Receiver))
            return false;
        if (!ReadIdle()) return false;
        _identities.RefreshTrackedIdentities(); RequireKnown();
        var snapshot = _identities.Read();
        if (snapshot.Unknown || snapshot.Untracked != 0 || snapshot.Nodes.Unknown || snapshot.Nodes.Untracked != 0)
            Reject("Observer retirement has untracked or Unknown engine identity responsibility.");
        if (snapshot.Outstanding != 0 || snapshot.Nodes.Outstanding != 0) return false;
        var matches = snapshot.Identities.Where(record => SameKey(record.Tracker, identity)
            && SameEngineTracker(record.Tracker, identity)).ToArray();
        if (matches.Length != 1) return false; // No Start or missing final native branch remains Pending.
        var selected = matches[0];
        if (!FinalNode(snapshot, selected)) return false;
        if (!ReadRecordReady(selected)) return false;
        var current = ReadCurrent(identity, withdrawn: false);
        if (!SameFrozen(current, identity)) Reject("The actual observer identity changed before retirement publication.");
        if (!ReadIdle() || !ReadRecordReady(selected)) return false;
        if (_permits.Count == NativeEngineCoroutineIdentityLedger.MaximumIdentities)
            Reject("Observer retirement permit capacity exhausted.");
        permit = new(this, _issuer, identity, selected); _permits.Add(permit); return true;
    }

    internal sealed class Permit
    {
        private readonly NativeEngineCoroutineObserverRetirementAuthority _authority;
        internal readonly NativeCoroutineCancellationIdentity Tracker;
        private readonly NativeEngineCoroutineIdentitySnapshot _engine;
        private NativeEngineObserverRetirementPhase _phase;
        internal Permit(NativeEngineCoroutineObserverRetirementAuthority authority, object issuer,
            NativeCoroutineCancellationIdentity tracker, NativeEngineCoroutineIdentitySnapshot engine)
        {
            if (!ReferenceEquals(issuer, authority._issuer)) throw new InvalidOperationException("Only the actual engine retirement issuer can create a permit.");
            (_authority, Tracker, _engine) = (authority, tracker, engine);
        }
        internal NativeEngineObserverRetirementSnapshot Read()
        { _authority._main.VerifyAccess(); return new(Tracker, _engine.NativeOwner, _engine.Node, _engine.EngineGeneration, _engine.StartSequence, _phase); }
        internal void MarkWithdrawn(NativeCoroutineCancellationIdentity actual)
        {
            _authority._main.VerifyAccess(); _authority.RequireKnown();
            if (_phase != NativeEngineObserverRetirementPhase.Prepared || !SameFrozen(actual, Tracker)
                || actual.Published || !actual.Retiring)
                Fail("Observer withdrawal did not match its one prepared exact generation.");
            _phase = NativeEngineObserverRetirementPhase.Withdrawn;
        }
        internal bool TryConfirmWithdrawnAndDrained()
        {
            _authority._main.VerifyAccess(); _authority.RequireKnown();
            if (_phase != NativeEngineObserverRetirementPhase.Withdrawn)
                Fail("Only the same withdrawn observer permit can redrain before a free attempt.");
            return Validate();
        }
        internal bool BeginFree()
        {
            if (!TryConfirmWithdrawnAndDrained()) return false;
            _phase = NativeEngineObserverRetirementPhase.FreeAttempted; return true;
        }
        internal void CompleteFree()
        {
            _authority._main.VerifyAccess(); _authority.RequireKnown();
            if (_phase != NativeEngineObserverRetirementPhase.FreeAttempted || !Validate())
                Fail("Observer cleanup lost its exact known idle physical boundary.");
            _phase = NativeEngineObserverRetirementPhase.Completed;
        }
        internal void RequireFreeInProgress()
        {
            _authority._main.VerifyAccess(); _authority.RequireKnown();
            if (_phase != NativeEngineObserverRetirementPhase.FreeAttempted || !Validate())
                Fail("The exact engine retirement boundary changed during an independent root free.");
        }
        internal void Fail(string reason)
        { _phase = NativeEngineObserverRetirementPhase.Unknown; _authority.Reject(reason); }
        internal void MarkFailure()
        { if (_phase != NativeEngineObserverRetirementPhase.Completed) _phase = NativeEngineObserverRetirementPhase.Unknown; }
        private bool Validate()
        {
            if (!_authority.ReadIdle()) return false;
            var current = _authority.ReadCurrent(Tracker, withdrawn: true);
            if (!SameFrozen(current, Tracker)) Fail("A withdrawn observer changed its exact generation or retained identities.");
            var snapshot = _authority._identities.Read();
            if (snapshot.Unknown || snapshot.Untracked != 0 || snapshot.Nodes.Unknown || snapshot.Nodes.Untracked != 0)
                Fail("Withdrawn observer engine responsibility became Unknown.");
            var matches = snapshot.Identities.Where(record => record.IdentityId == _engine.IdentityId && SameKey(record.Tracker, Tracker)).ToArray();
            if (matches.Length != 1 || !SameFrozen(matches[0].Tracker, _engine.Tracker)
                || matches[0].NativeOwner != _engine.NativeOwner || matches[0].Node != _engine.Node
                || matches[0].EngineGeneration != _engine.EngineGeneration || matches[0].StartSequence != _engine.StartSequence
                || !FinalNode(snapshot, matches[0]))
                Fail("The withdrawn observer lost its exact final native generation.");
            if (snapshot.Outstanding != 0 || snapshot.Nodes.Outstanding != 0) return false;
            return _authority.ReadIdle() && _authority.ReadRecordReady(matches[0]);
        }
    }

    private NativeCoroutineCancellationIdentity ReadCurrent(NativeCoroutineCancellationIdentity identity, bool withdrawn)
    {
        RequireKnown(); if (_reading) Reject("Observer retirement scalar provider reentered."); _reading = true;
        try
        {
            var providers = _providers.Where(provider => ReferenceEquals(provider.Owner, identity.Owner) && provider.Receiver == identity.Receiver).ToArray();
            if (providers.Length != 1) Reject("Observer retirement has no exact retained scalar provider.");
            var rows = providers[0].Read(); RequireKnown();
            if (rows is null || rows.Count > NativeEngineCoroutineIdentityLedger.MaximumIdentities)
                Reject("Observer retirement scalar provider snapshot is missing or unbounded.");
            var matches = rows!.Where(row => SameKey(row, identity)).ToArray();
            if (matches.Length != 1 || !SameFrozen(matches[0], identity)
                || withdrawn && (matches[0].Published || !matches[0].Retiring)
                || !withdrawn && (!matches[0].Known || !matches[0].Published || matches[0].Retiring
                    || !providers[0].IsCurrent(matches[0])))
                Reject("Observer retirement scalar provider changed the exact current or withdrawn generation.");
            RequireKnown(); return matches[0];
        }
        finally { _reading = false; }
    }
    private bool ReadIdle()
    {
        RequireKnown(); if (_launch?.CoroutineCancellationPending == true) return false;
        if (_readingActivity) Reject("Observer retirement native activity reader reentered.");
        _readingActivity = true;
        try
        {
            var value = _readActivity(); RequireKnown();
            if (!value.Known || value.InFlight < 0) Reject("Observer retirement native activity became Unknown.");
            return value.InFlight == 0 && _launch?.CoroutineCancellationPending != true;
        }
        finally { _readingActivity = false; }
    }
    private bool ReadRecordReady(NativeEngineCoroutineIdentitySnapshot selected)
    {
        RequireKnown();
        if (_readingReadiness) Reject("Observer retirement readiness reader reentered.");
        _readingReadiness = true;
        try
        {
            var ready = _recordReady(selected) && _identities.Read().Identities
                .Where(identity => identity.WaitUntilSource?.ParentIdentityId == selected.IdentityId).All(_recordReady);
            RequireKnown();
            return ready;
        }
        finally { _readingReadiness = false; }
    }
    private static bool FinalNode(NativeEngineCoroutineIdentityLedgerSnapshot snapshot, NativeEngineCoroutineIdentitySnapshot selected)
    {
        if (selected.Node == 0 || selected.EngineGeneration <= 0 || selected.StartSequence == 0
            || selected.StartSequence > (ulong)long.MaxValue || !selected.NativeStartReturned || !selected.StartDisposed
            || !selected.Tracker.StartReturned) return false;
        var nodes = snapshot.Nodes.Nodes.Where(node => node.Node == selected.Node && node.Generation == selected.EngineGeneration
            && node.NativeOwner == selected.NativeOwner && node.Enumerator == selected.Tracker.Enumerator
            && node.StartSequence == checked((long)selected.StartSequence)).ToArray();
        return nodes.Length == 1 && nodes[0].FinalCleanupObserved && nodes[0].StartCompleted
            && nodes[0].ActiveExecutions == 0 && nodes[0].OpenCalls == 0
            && NativeEngineCoroutineObserverRetirement.YieldChildrenFinal(snapshot, selected);
    }
    private static bool SameKey(NativeCoroutineCancellationIdentity left, NativeCoroutineCancellationIdentity right)
        => ReferenceEquals(left.Owner, right.Owner) && left.Receiver == right.Receiver && left.Slot == right.Slot && left.Generation == right.Generation;
    private static bool SameFrozen(NativeCoroutineCancellationIdentity left, NativeCoroutineCancellationIdentity right)
        => SameKey(left, right) && left.Enumerator == right.Enumerator && left.ObservedHandle == right.ObservedHandle
            && left.HandleConfirmed == right.HandleConfirmed && left.StartReturned == right.StartReturned
            && left.NaturalTerminal == right.NaturalTerminal;
    // A fully final historical engine generation intentionally stops refreshing
    // its provider. Natural terminal may be observed later by MoveNext; its
    // actual tracker predicate is checked separately, without rewriting history.
    private static bool SameEngineTracker(NativeCoroutineCancellationIdentity left, NativeCoroutineCancellationIdentity right)
        => SameKey(left, right) && left.Enumerator == right.Enumerator && left.ObservedHandle == right.ObservedHandle
            && left.HandleConfirmed == right.HandleConfirmed && left.StartReturned == right.StartReturned;
    private void RequireKnown()
    {
        _main.VerifyAccess();
        if (!Controlled && (_launch is null || !_productionOwner!.IsIssuedObserverRetirementAuthority(this, _launch, _identities, _providers)))
            Reject("Production retirement authority was not published by its actual engine coordinator.");
        if (Unknown) Reject("Observer retirement authority is permanently Unknown.");
    }
    private void Reject(string reason)
    {
        Interlocked.Exchange(ref _unknown, 1); foreach (var permit in _permits) permit.MarkFailure();
        _fault(reason); throw new InvalidOperationException(reason);
    }
}
