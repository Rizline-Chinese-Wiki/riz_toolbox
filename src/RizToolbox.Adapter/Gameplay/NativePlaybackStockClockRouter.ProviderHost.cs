using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativePlaybackStockClockRouter
{
    private readonly object _rateHostIssuer = new();
    private RateProviderHost? _rateHost;
    private bool _formalRateHost;

    internal RateProviderHost AdoptFormalRateHost()
    {
        Open();
        if (_rateHost is not null)
            throw new GameplayStateException(ErrorCode.Conflict, "This physical route already registered its stable provider host.");
        if (_group is not { OwnedFixture: false } || _session is not { } session)
            throw new GameplayStateException(ErrorCode.NotReady, "A formal host requires the runtime-issued stock source and its registered session.");
        session.VerifyFormalRateHostCreation(this);
        if (_rateHost is not null)
            throw new GameplayStateException(ErrorCode.Conflict, "Reentrant construction already registered the route's stable host.");
        _formalRateHost = true;
        return _rateHost = RateProviderHost.Issue(this, _rateHostIssuer);
    }

    /// <summary>Issue one stable entry only from this actual registered first
    /// armed fixture session. Logical successors are selected privately below.</summary>
    internal RateProviderHost AdoptControlledRateHost()
    {
        Open();
        if (_rateHost is not null)
            throw new GameplayStateException(ErrorCode.Conflict, "This physical route already registered its stable provider host.");
        if (_group is not { OwnedFixture: true } || _session is not { } session)
            throw new GameplayStateException(ErrorCode.NotReady, "A controlled rate host requires an actual owned-fixture group and its registered armed session.");
        _group.VerifyCurrent(); session.VerifyRateHostCreation(this);
        _rateHost = RateProviderHost.Issue(this, _rateHostIssuer);
        return _rateHost;
    }

    private void VerifyRateHostOwner(RateProviderHost host)
    {
        VerifyRegisteredOwner();
        if (_thread != Environment.CurrentManagedThreadId)
            throw new GameplayStateException(ErrorCode.WrongThread, "The stable stock rate host is owner-thread bound.");
        if (!ReferenceEquals(_rateHost, host))
            throw new GameplayStateException(ErrorCode.TargetExpired, "A copied or foreign host has no private route registration.");
    }

    private NativePlaybackStockRateProvider ResolveRateProvider(RateProviderHost host)
    {
        VerifyRateHostOwner(host); OpenOperation();
        if (_handoff is not null || InitializationPending || _session is not { } session || !session.ReadyForRateHost(this))
            throw new GameplayStateException(ErrorCode.NotReady, "The stable stock host has no currently armed logical session; joint handoff or dormant construction is in progress.");
        // An already registered provider is identity selection only. Its
        // Capture/Probe/read owns the live channel checks; first registration
        // below still executes the complete source/publication audit.
        return session.ProviderForHost(host);
    }

    internal void VerifyRateHostOperation(RateProviderHost host, Session session,
        GameEventState.PlaybackRunAssociation run)
    {
        VerifyRateHostOwner(host); OpenOperation();
        if (!MaintainsReadySession(session, run))
            throw new GameplayStateException(ErrorCode.TargetExpired, "The formal host no longer owns this registered route, run and armed session.");
    }

    // Scheduling/observation only. No source, native receipt, Clock policy or
    // value-operation authority is returned by this local readiness predicate.
    internal bool MaintainsReadySession(Session session, GameEventState.PlaybackRunAssociation observed)
    {
        VerifyRegisteredOwner();
        if (_thread != Environment.CurrentManagedThreadId)
            throw new GameplayStateException(ErrorCode.WrongThread, "Ready maintenance belongs to the actual route thread.");
        return !_disposed && _transferFailure is null && _group is { IsActive: true }
            && _rateHost is not null && _handoff is null && !InitializationPending
            && observed.WithdrawalReason is null && ReferenceEquals(_run, observed) && ReferenceEquals(_session, session)
            && session.ReadyForRateHost(this);
    }

    private NativePlaybackRateSourceDescriptor? ObserveSourceDescriptor(RateProviderHost host)
    {
        VerifyRateHostOwner(host);
        OpenOperation();
        return _session is { } session && MaintainsReadySession(session, _run)
            ? session.ObserveSourceDescriptor(host) : null;
    }

    /// <summary>Stable Module/Service entry; it never accepts a caller resolver,
    /// arbitrary Session, revision or completeness flag. Captures still belong
    /// to the exact concrete provider and final Clock issued for that session.</summary>
    internal sealed class RateProviderHost : INativePlaybackRateProvider,
        INativePlaybackGetTimeHookProvider, INativePlaybackRateProviderLifetime, INativePlaybackRateSourceDescriptorProvider,
        INativePlaybackRateSourceDescriptorObservation, INativePlaybackAvailabilityObservation,
        INativePlaybackClockReadProvider
    {
        private readonly NativePlaybackStockClockRouter _owner;
        private bool _disposed;
        private RateProviderHost(NativePlaybackStockClockRouter owner) => _owner = owner;
        internal static RateProviderHost Issue(NativePlaybackStockClockRouter owner, object issuer)
        {
            if (!ReferenceEquals(owner._rateHostIssuer, issuer))
                throw new GameplayStateException(ErrorCode.TargetExpired, "Only the private route can issue its stable host.");
            return new(owner);
        }
        internal bool ProductionAdmission => false;
        private NativePlaybackStockRateProvider Resolve()
        {
            _owner.VerifyRateHostOwner(this);
            if (_disposed) throw new ObjectDisposedException(nameof(RateProviderHost));
            return _owner.ResolveRateProvider(this);
        }
        public NativePlaybackRateProviderProbe Probe()
        {
            try { return Resolve().Probe(); }
            catch (Exception error)
            {
                return new(false, _owner._formalRateHost
                    ? "The current song's playback support is unavailable."
                    : "Controlled stock fixture host has no current logical session; production admission remains unproven.",
                    new(error is GameplayStateException state ? state.Code : ErrorCode.CapabilityUnavailable, error.Message));
            }
        }
        public INativePlaybackRateSource Capture(NativePlaybackRateOrigin origin) => Resolve().Capture(origin);
        internal INativePlaybackSeekSource CaptureSeek(NativePlaybackRateOrigin origin) => Resolve().CaptureSeek(origin);
        public NativePlaybackRateSourceDescriptor? CaptureSourceDescriptor() => Resolve().CaptureSourceDescriptor();
        public NativePlaybackRateSourceDescriptor? ObserveSourceDescriptor()
        {
            _owner.VerifyRateHostOwner(this);
            if (_disposed) return null;
            return _owner.ObserveSourceDescriptor(this);
        }
        public NativePlaybackRateProviderProbe ObserveRateAvailability()
        {
            try
            {
                return ObserveSourceDescriptor() is not null
                    ? new(true, "Experimental paused rate-control request entry is registered for this observed song. Actual source, Clock and writer authority is verified during preparation.")
                    : new(false, "The registered host has no current armed observed source.",
                        new(ErrorCode.NotReady, "No current armed observed source is registered."));
            }
            catch (Exception error)
            {
                return new(false, "The registered source observation is unavailable.",
                    new(error is GameplayStateException state ? state.Code : ErrorCode.CapabilityUnavailable, error.Message));
            }
        }
        public NativePlaybackRateProviderProbe ObserveSeekAvailability()
            => new(false, "This rate host does not expose a Seek request entry.",
                new(ErrorCode.OperationUnsupported, "Seek requires the complete gameplay host and its target preparation."));
        public NativePlaybackGetTimeHookLease CaptureGetTimeHook() => Resolve().CaptureGetTimeHook();
        public NativePlaybackClockReadout ReadPlaybackClock(NativePlaybackRateOrigin origin) => Resolve().ReadPlaybackClock(origin);
        public void VerifyPlaybackClock(NativePlaybackClockReadout readout)
        {
            var provider = Resolve();
            if (!ReferenceEquals(provider, readout.Issuer))
                throw new GameplayStateException(ErrorCode.TargetExpired, "The stable host changed concrete sessions during the playback read.");
            provider.VerifyPlaybackClock(readout);
        }
        public void VerifyPlaybackClockState(NativePlaybackClockReadout readout)
        {
            _owner.VerifyRateHostOwner(this);
            if (_disposed || readout.Issuer is not { } provider)
                throw new GameplayStateException(ErrorCode.TargetExpired, "The stable playback read host is unavailable.");
            provider.VerifyPlaybackClockStateForHost(this, readout);
        }
        public void PumpCompleted()
        { _owner.VerifyRateHostOwner(this); _owner.PumpCompleted(); }
        public void Dispose()
        {
            // Verify before changing any flag. A worker or cloned host cannot
            // prevent its legitimate owner from retrying the logical release.
            _owner.VerifyRateHostOwner(this);
            _owner.Dispose(); _disposed = true;
        }
    }

    internal sealed partial class Session
    {
        private RateProviderHost? _providerHost;
        internal bool ReadyForRateHost(NativePlaybackStockClockRouter owner)
            => ReferenceEquals(_owner, owner) && ReferenceEquals(owner._session, this) && _armed && !_withdrawn;
        internal NativePlaybackRateSourceDescriptor? ObserveSourceDescriptor(RateProviderHost host)
        {
            Registered();
            _owner.VerifyRateHostOwner(host);
            _owner._run.VerifyCurrent(_initial.Binding);
            if (!_owner.MaintainsReadySession(this, _owner._run)) return null;
            return new(_initial.Binding, _owner._origin.Pause);
        }
        internal void VerifyFormalRateHostCreation(NativePlaybackStockClockRouter owner)
        {
            Registered();
            if (!ReadyForRateHost(owner) || _rateProvider is not null || _owner._group is not { OwnedFixture: false })
                throw new GameplayStateException(ErrorCode.Conflict, "Only this armed stock session can register its sole formal host.");
            // Formal Clock verification already checks the private fresh or
            // transferred publication and its source. No native consumer lies
            // between it and the host's local registration, so repeating the
            // same source/publication transaction adds no authority boundary.
            VerifyFormalClockAuthority();
            Registered();
            if (!ReadyForRateHost(owner) || _rateProvider is not null)
                throw new GameplayStateException(ErrorCode.TargetExpired, "The session changed during formal host verification.");
        }
        internal void VerifyRateHostCreation(NativePlaybackStockClockRouter owner)
        {
            Registered();
            if (!ReadyForRateHost(owner) || _rateProvider is not null)
                throw new GameplayStateException(ErrorCode.Conflict, "Only this actual armed session with untransferred provider ownership can register its stable host.");
            _owner.VerifySource();
            if (!_publication!.IsCurrent(Clock.Capture()))
                throw new GameplayStateException(ErrorCode.TargetExpired, "The stable host requires this session's current exact native publication.");
        }
        internal NativePlaybackStockRateProvider ProviderForHost(RateProviderHost host)
        {
            Registered(); _owner.VerifyRateHostOwner(host);
            if (_rateProvider is null)
            {
                var provider = _owner._formalRateHost
                    ? NativePlaybackStockRateProvider.AdoptFormalSource(this)
                    : NativePlaybackStockRateProvider.AdoptControlledFixture(this);
                _providerHost = host;
                return provider;
            }
            if (!ReferenceEquals(_providerHost, host))
                throw new GameplayStateException(ErrorCode.Conflict, "This session's concrete provider belongs to another logical owner.");
            // Resolve selects the registered provider only. Its immediately
            // following Capture/Probe/read checks current operation authority;
            // returning this identity grants no source or writer authority.
            return _rateProvider;
        }
    }
}
