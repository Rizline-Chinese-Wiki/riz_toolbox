using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.AudioContent;

internal interface INativeLocalTweenDeathBackend
{
    void VerifyTweenDeathOwner(NativeLocalTweenWorkOwner owner);
    void BorrowTweenDeathSources(NativeLocalTweenWorkOwner owner, Action<NativeRuntime.RootHandle, NativeTweenAdmissionLease> borrow);
    NativeTweenDeathObservation ReadTweenDeathObservation(NativeRuntime.RootHandle root);
}

internal sealed partial class NativeLocalTweenWorkOwner
{
    private readonly object _tweenDeathIssuer = new();
    private readonly Dictionary<long, (TweenDeathSource Source, NativeLocalTweenDeathTracker Tracker)> _tweenDeaths = new();
    private TweenDeathReturnSource? _tweenDeathReturn;
    private bool _capturingTweenDeaths, _tweenDeathSourcesCaptured, _pumpingTweenDeaths;

    internal sealed class TweenDeathReturnSource
    {
        private readonly object _issuer;
        private readonly NativeLocalTweenWorkOwner _owner;
        private readonly LocalRunLaunchCommitState? _launch;
        private readonly NativeLocalRunLaunchCoordinator? _coordinator;
        internal TweenDeathReturnSource(object issuer, NativeLocalTweenWorkOwner owner)
        {
            if (!ReferenceEquals(issuer, owner._tweenDeathIssuer)) throw new InvalidOperationException("Foreign Tween death Return issuer.");
            (_issuer, _owner, _launch, _coordinator) = (issuer, owner, owner._launch, owner._coordinator);
        }
        internal bool IsFor(NativeLocalTweenWorkOwner owner, LocalRunLaunchCommitState? launch)
            => ReferenceEquals(owner, _owner) && ReferenceEquals(launch, _launch) && ReferenceEquals(_issuer, owner._tweenDeathIssuer)
                && ReferenceEquals(owner._tweenDeathReturn, this) && ReferenceEquals(_coordinator, owner._coordinator) && !owner.Unknown;
        internal void Require()
        {
            _owner._main.VerifyAccess();
            if (!IsFor(_owner, _launch)) throw new InvalidOperationException("Tween death Return belongs to another retained work/launch interval.");
            if (_launch is not null)
            {
                if (!_owner._sealRequested || _coordinator is null) throw new InvalidOperationException("No actual classified source-seal request owns this death interval.");
                _coordinator.RequireTweenAdmissionSealRequest(_owner, _launch);
            }
        }
    }
    internal sealed class TweenDeathSource
    {
        private readonly object _issuer;
        private readonly NativeLocalTweenWorkOwner _owner;
        private readonly INativeLocalTweenDeathBackend _backend;
        private readonly NativeRuntime.RootHandle _root;
        private readonly NativeTweenAdmissionLease _lease;
        private readonly LocalRunLaunchCommitState? _launch;
        private uint? _slot;
        private ulong _epoch;
        internal long Tween { get; }
        internal bool Production => _owner._backend is NativeLocalTweenBackend && _launch is not null && _lease.Authority.Production;
        internal bool Unknown => _owner.Unknown || _lease.Unknown || _lease.Authority.Unknown;
        internal ulong ConfirmedEpoch { get; private set; }
        internal NativeRuntime.RootHandle Root => _root;
        internal TweenDeathSource(object issuer, NativeLocalTweenWorkOwner owner, INativeLocalTweenDeathBackend backend,
            NativeRuntime.RootHandle root, NativeTweenAdmissionLease lease, long tween)
        {
            if (!ReferenceEquals(issuer, owner._tweenDeathIssuer) || !(owner._capturingTweenDeaths || owner._promotingDynamicSource)
                || !ReferenceEquals(backend, owner._backend))
                throw new InvalidOperationException("Only this actual backend borrowing interval issues a rooted Tween death source.");
            (_issuer, _owner, _backend, _root, _lease, _launch, Tween) = (issuer, owner, backend, root, lease, owner._launch, tween);
        }
        internal void RequireRetained()
        {
            _owner._main.VerifyAccess(); _owner.Known(); _backend.VerifyTweenDeathOwner(_owner);
            if (!ReferenceEquals(_issuer, _owner._tweenDeathIssuer) || !ReferenceEquals(_backend, _owner._backend)
                || !ReferenceEquals(_launch, _owner._launch) || !_owner._sources.Contains(Tween) || _root.Target.ToInt64() != Tween
                || _lease.Unknown || _lease.Authority.Unknown || !_lease.RootRecorded || _lease.Root!.Target.ToInt64() != Tween || _lease.Object.ToInt64() != Tween
                || (Production ? !ReferenceEquals(_lease.Owner, _owner)
                    : _lease.Authority.Production || !(ReferenceEquals(_lease.Owner, _backend)
                        || _backend is INativeLocalTweenRetirementBackend && ReferenceEquals(_lease.Owner, _owner))))
                throw new InvalidOperationException("Tween death source lost its exact independent graph/lease roots, backend or private owner.");
            if (_lease.Claimed)
            {
                if (_lease.Epoch == 0 || _slot is { } priorSlot && priorSlot != _lease.Slot || _epoch != 0 && _epoch != _lease.Epoch)
                    throw new InvalidOperationException("The retained actual Tween admission slot/epoch changed.");
                _slot ??= _lease.Slot; _epoch = _lease.Epoch;
            }
        }
        internal void RequireReturn(TweenDeathReturnSource request)
        { RequireRetained(); request.Require(); if (!request.IsFor(_owner, _launch)) throw new InvalidOperationException("Foreign actual Return source."); }
        internal ulong ReadSealedEpoch(bool requireNativeDead = false)
        {
            RequireRetained();
            var observed = _lease.Read(); RequireRetained();
            if (!observed.Known || observed.Object != Tween)
                throw new InvalidOperationException("The actual retained admission lease has no matching death-source identity.");
            if (!_lease.Claimed && observed.Phase == NativeTweenAdmissionPhase.Empty && observed.Epoch == 0) return 0;
            if (observed.Slot != _lease.Slot || observed.Epoch != _lease.Epoch)
                throw new InvalidOperationException("The actual retained admission lease changed its exact source slot/epoch.");
            if (requireNativeDead && observed.CachedNative != 0)
                throw new UnsupportedSignatureException("The confirmed Tween death receipt now has a live native object in its actual admission lease.");
            if (!observed.IsSealed || observed.InFlight != 0 || Production && !_owner._admissionSealed) return 0;
            if (Production && (_owner._sourceJournal?.Production != true || _owner._sourceRootSet?.HasTweenOwnershipAnchor(Tween) != true || _owner._sourceEpoch == 0))
                throw new InvalidOperationException("Production death confirmation has no actual retained source-journal root/epoch.");
            ConfirmedEpoch = observed.Epoch; return ConfirmedEpoch;
        }
        internal NativeTweenDeathObservation? TryReadObservation(bool baseline)
        {
            RequireRetained();
            if (_owner._top is not null && !(baseline && _owner.IsCurrentDynamicSourceBaseline(_root, Tween))
                || !baseline && _owner._reading)
                throw new InvalidOperationException("A physical Tween operation cannot overlap a rooted death observation.");
            if (baseline)
            {
                var initial = _backend.ReadTweenDeathObservation(_root); RequireRetained();
                if (initial.ManagedReceiver != Tween) throw new InvalidOperationException("Baseline reader returned a foreign rooted Tween.");
                _owner.CaptureTweenTargetSources(Tween, _root);
                return initial;
            }
            _owner.PollSourceJournal();
            var before = _owner._backend.ReadActivity(); RequireRetained();
            if (!before.Known || before.InFlight < 0) throw new InvalidOperationException("Tween death operation/source activity became Unknown.");
            if (before.InFlight != 0 || _owner._sourceOperations.Values.Any(value => value.Pairs.Count != 0 || value.Move is not null || value.Transient.Any(item => item is not null)))
                return null;
            var sequence = _owner._sourceReadSequence; var operations = _owner._operationRevision;
            var observed = _backend.ReadTweenDeathObservation(_root); RequireRetained();
            _owner.PollSourceJournal(); var after = _owner._backend.ReadActivity(); RequireRetained();
            if (!after.Known || after.InFlight != 0 || sequence != _owner._sourceReadSequence || operations != _owner._operationRevision
                || _owner._top is not null || _root.Target.ToInt64() != Tween)
                throw new InvalidOperationException("Tween native/source activity changed across the exact rooted death sample.");
            return observed;
        }
        internal bool IsFor(NativeLocalTweenWorkOwner owner, NativeRuntime.RootHandle root, LocalRunLaunchCommitState? launch, ulong epoch)
            => ReferenceEquals(owner, _owner) && ReferenceEquals(root, _root) && ReferenceEquals(launch, _launch)
                && ReferenceEquals(_issuer, owner._tweenDeathIssuer) && owner._tweenDeaths.TryGetValue(Tween, out var retained)
                && ReferenceEquals(retained.Source, this) && ConfirmedEpoch == epoch && epoch != 0 && !Unknown;
        internal void VerifyReceiptCurrent(NativeLocalTweenWorkOwner owner, NativeRuntime.RootHandle root,
            LocalRunLaunchCommitState? launch, ulong epoch)
        {
            // A death receipt is a time-bound observation. Re-read the exact
            // admission lease before every consumption so a recycled slot or
            // changed epoch cannot make an old receipt authoritative.
            if (!IsFor(owner, root, launch, epoch))
                throw new InvalidOperationException("The Tween death receipt no longer belongs to this retained source interval.");
            if (ReadSealedEpoch(requireNativeDead: true) != epoch)
                throw new UnsupportedSignatureException("The Tween death receipt source is not the same currently sealed lease epoch.");
            RequireRetained();
        }
        internal void RetainFault(string failure) => _owner.Fault(failure);
    }
    internal IReadOnlyList<long> RequireActualTweenDeathCapture(NativeLocalTweenBackend backend)
    {
        VerifyTweenSourceOwner();
        if (!ReferenceEquals(_backend, backend) || !_initializing || _ready || !_capturingTweenDeaths || _launch is null
            || _launch.Read().ActivationAttempted || _top is not null)
            throw new InvalidOperationException("Live Tween death baselines must be borrowed from the actual backend before activation.");
        return Array.AsReadOnly(_sources.ToArray());
    }
    internal void CaptureActualTweenDeathSources()
    {
        VerifyTweenSourceOwner();
        if (_backend is not NativeLocalTweenBackend || !_initializing || _ready || _launch is null || _launch.Read().ActivationAttempted)
            throw new InvalidOperationException("Production death-source capture requires this exact retained backend before activation.");
        CaptureTweenDeathSourcesCore();
    }
    internal void CaptureControlledTweenDeathSourcesForTests()
    {
        VerifyTweenSourceOwner();
        if (_backend is NativeLocalTweenBackend || _launch is not null || _coordinator is not null || _operationRevision != 0)
            throw new InvalidOperationException("Controlled death capture cannot adopt a production launch or an already active history.");
        CaptureTweenDeathSourcesCore();
    }
    private void CaptureTweenDeathSourcesCore()
    {
        if (_capturingTweenDeaths || _tweenDeathSourcesCaptured || _backend is not INativeLocalTweenDeathBackend backend)
            throw new InvalidOperationException("This retained backend has no unused actual Tween death borrowing source.");
        _capturingTweenDeaths = true;
        try
        {
            backend.VerifyTweenDeathOwner(this);
            backend.BorrowTweenDeathSources(this, (root, lease) => {
                ArgumentNullException.ThrowIfNull(root); ArgumentNullException.ThrowIfNull(lease);
                var identity = root.Target.ToInt64();
                if (identity == 0 || !_sources.Contains(identity) || _tweenDeaths.ContainsKey(identity))
                    throw new InvalidOperationException("Death source borrowing repeated or changed the exact owned graph root.");
                var source = new TweenDeathSource(_tweenDeathIssuer, this, backend, root, lease, identity);
                var tracker = new NativeLocalTweenDeathTracker(_main, source);
                _tweenDeaths.Add(identity, (source, tracker)); tracker.CaptureLiveBaseline();
            });
            if (!_sources.OrderBy(value => value).SequenceEqual(_tweenDeaths.Keys.OrderBy(value => value)))
                throw new InvalidOperationException("The complete actual Tween source set has no exact rooted live baseline coverage.");
            Known(); _tweenDeathSourcesCaptured = true;
        }
        catch (Exception error) { Fault("Tween death capture: " + error.Message); throw; }
        finally { _capturingTweenDeaths = false; }
    }
    internal void PumpActualTweenDeaths(LocalRunLaunchCommitState launch, NativeLocalRunLaunchCoordinator coordinator)
    {
        VerifyTweenSourceOwner();
        if (_backend is not NativeLocalTweenBackend || !ReferenceEquals(_launch, launch) || !ReferenceEquals(_coordinator, coordinator)
            || !_sealRequested || !_tweenDeathSourcesCaptured)
            throw new InvalidOperationException("Tween death pumping requires the same actual coordinator's classified Return request.");
        coordinator.RequireTweenAdmissionSealRequest(this, launch);
        _tweenDeathReturn ??= new(_tweenDeathIssuer, this); PumpTweenDeathsCore();
        coordinator.RequireTweenAdmissionSealRequest(this, launch);
    }
    internal void RequestControlledTweenDeathReturnForTests()
    {
        VerifyTweenSourceOwner();
        if (_backend is NativeLocalTweenBackend || _launch is not null || _coordinator is not null || !_tweenDeathSourcesCaptured)
            throw new InvalidOperationException("Controlled Return cannot authorize a production death interval.");
        _tweenDeathReturn ??= new(_tweenDeathIssuer, this);
    }
    internal void PumpControlledTweenDeathsForTests()
    {
        VerifyTweenSourceOwner();
        if (_backend is NativeLocalTweenBackend || _launch is not null || _coordinator is not null || !_tweenDeathSourcesCaptured)
            throw new InvalidOperationException("Controlled pumping cannot observe a production death interval.");
        PumpTweenDeathsCore();
    }
    private void PumpTweenDeathsCore()
    {
        if (_pumpingTweenDeaths) throw new InvalidOperationException("Tween death pump reentered its retained observation interval.");
        if (_reading || _top is not null) return;
        _pumpingTweenDeaths = true;
        try { foreach (var value in _tweenDeaths.Values) value.Tracker.Tick(_tweenDeathReturn); Known(); PumpTweenTargetDeaths(); }
        catch (Exception error) { Fault("Tween death pump: " + error.Message); throw; }
        finally { _pumpingTweenDeaths = false; }
    }
    internal IReadOnlyList<NativeLocalTweenDeathSnapshot> ReadTweenDeaths()
    { _main.VerifyAccess(); return Array.AsReadOnly(_tweenDeaths.Values.Select(value => value.Tracker.Read()).ToArray()); }
    internal NativeLocalTweenDeathTracker.DeathReceipt? ReadTweenDeathReceipt(NativeRuntime.RootHandle exactRoot)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(exactRoot);
        var matches = _tweenDeaths.Values.Where(value => ReferenceEquals(value.Source.Root, exactRoot)).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException("Death receipt requires the same exact independent graph root instance.");
        var receipt = matches[0].Tracker.ReadReceipt();
        if (receipt is { } current)
            current.VerifyCurrent(this, exactRoot, _launch, current.SourceEpoch);
        return receipt;
    }
}
