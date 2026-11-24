using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>Stable callback route for one retained physical group. A session owns
/// exactly one final Clock, constructed with its publication while native policy
/// is dormant. Logical successors consume an actual joint-gate handoff.</summary>
internal sealed partial class NativePlaybackStockClockRouter : IDisposable
{
    private readonly NativeRuntime _api;
    private readonly NativePlaybackStockClockRouter _registeredOwner;
    private NativePlaybackInitializationHost? _initialHost;
    private NativePlaybackBinding _binding;
    private NativePlaybackRateOrigin _origin;
    private GameEventState.PlaybackRunAssociation _run;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private PlaybackSourceGroupLease? _group;
    private NativePlaybackStockRootedSource? _source;
    private Session? _session;
    private NativePlaybackGetTimeCallProfile? _callProfile;
    private bool _disposed;
    private object? _sourceRoute;
    private string? _transferFailure;
    private readonly List<NativePlaybackStockRootedSource> _retainedSources = new();
    private readonly HashSet<NativePlaybackSourceObservation> _logicalObservations = new(ReferenceEqualityComparer.Instance);
    internal string? LastFailure => _transferFailure ?? _initialization?.LastFailure ?? _source?.LastFailure;
    internal bool RootsReleased => (_source?.RootsReleased ?? true) && _retainedSources.All(source => source.RootsReleased)
        && !(_initialization?.RootsRetained ?? false) && _retainedInitializers.All(value => !value.RootsRetained)
        && !(_initialHost?.RootsRetained ?? false) && (_freshCapture?.RootsReleased ?? true) && _unreleasedObservedGraphs.Count == 0
        && !(_observedInitialization?.RootsRetained ?? false) && _retainedObservedInitializers.All(value => !value.RootsRetained)
        && (_group?.UnresolvedPlayerRootOwnerCount ?? 0) == 0;
    internal NativePlaybackStockClockRouter(NativeRuntime api, NativePlaybackBinding binding,
        NativePlaybackRateOrigin origin, GameEventState.PlaybackRunAssociation run)
    { run.VerifyCurrent(binding); (_api, _binding, _origin, _run) = (api, binding, origin, run); _registeredOwner = this; }
    internal NativePlaybackStockClockRouter(NativeRuntime api, NativePlaybackInitializationHost host,
        GameEventState.PlaybackRunAssociation run)
    {
        host.VerifyCurrent(api, run); (_api, _initialHost, _origin, _run) = (api, host, host.Origin, run);
        _binding = null!; _registeredOwner = this;
        host.Claim(this, api, run);
    }

    private void VerifyRegisteredOwner()
    {
        if (!ReferenceEquals(_registeredOwner, this))
            throw new GameplayStateException(ErrorCode.TargetExpired, "A copied stock route has no registered physical or logical ownership.");
    }

    private void Open()
    {
        if (_thread != Environment.CurrentManagedThreadId) throw new GameplayStateException(ErrorCode.WrongThread, "The stock Clock route is owner-thread bound.");
        VerifyRegisteredOwner();
        PumpPhysicalFaults();
        if (_disposed) throw new ObjectDisposedException(nameof(NativePlaybackStockClockRouter));
        if (_transferFailure is not null) throw new GameplayStateException(ErrorCode.TargetExpired, _transferFailure);
    }
    // Nested operation wrappers check the actual registered route only. The
    // operation entry and its native receipt/policy checks read live channels;
    // fault pumping and complete installation audits remain on their own path.
    private void OpenOperation()
    {
        if (_thread != Environment.CurrentManagedThreadId)
            throw new GameplayStateException(ErrorCode.WrongThread, "The stock Clock route is owner-thread bound.");
        VerifyRegisteredOwner();
        if (_disposed) throw new ObjectDisposedException(nameof(NativePlaybackStockClockRouter));
        if (_physicalFailure is not null || !_physicalFaults.IsEmpty || _transferFailure is not null
            || _group is not { IsActive: true })
            throw new GameplayStateException(ErrorCode.TargetExpired,
                _physicalFailure ?? _transferFailure ?? "The registered playback group is unavailable or has a pending native fault.");
    }
    internal void Attach(PlaybackSourceGroupLease group)
    {
        Open();
        if (_group is not null) throw new InvalidOperationException("The Clock route already owns its physical group association.");
        if (_initialHost is { } host) host.VerifyCurrent(_api, _run);
        else _run.VerifyCurrent(_binding);
        group.VerifyCurrent();
        _group = group;
        if (_initialHost is { } initialHost) { initialHost.BindGroup(this, group); group.BindInitialCaptureSource(); return; }
        var route = new object();
        try { _source = CreateSource(_binding, _origin, _run, route); _sourceRoute = route; }
        catch (Exception error)
        {
            _transferFailure = "The first rooted source failed closed: " + error.Message;
            throw;
        }
    }
    private NativePlaybackStockRootedSource CreateSource(NativePlaybackBinding binding, NativePlaybackRateOrigin origin,
        GameEventState.PlaybackRunAssociation run, object route)
        => new(_api, binding, origin, run, _group!,
            receipt => { if (ReferenceEquals(_sourceRoute, route)) _session?.Observe(receipt); },
            reason => { if (ReferenceEquals(_sourceRoute, route)) _session?.Withdraw("The exact rooted stock source withdrew: " + reason); });
    internal void ObserveBefore(NativePlaybackSourceObservation observation)
    {
        VerifyRegisteredOwner();
        if (_thread != Environment.CurrentManagedThreadId)
            throw new GameplayStateException(ErrorCode.WrongThread, "Source responsibility must be routed on its actual owner thread.");
        if (FreshBeforeClosed(observation)) return;
        if (observation.Role == NativePlaybackSourceRole.ProgressClip)
        {
            // Resource-only writes end dependent future captures. Existing
            // current-audio occurrences do not depend on the next resource.
            try { _group!.VerifyIssuedOperationBefore(observation); _initialization?.ObserveResourceBefore(observation); }
            finally { observation.DisposeOperation(); }
            return;
        }
        if (observation.Role == NativePlaybackSourceRole.PlayerConstructor)
        {
            // Native classified the exact watched receiver before any playback
            // fence or callback. A constructor is invalidation, not a stock
            // Pause, initializer child, or new source generation.
            try
            {
                _source?.WithdrawForPlayerConstructor();
                _session?.Withdraw("The registered player's shared constructor entered before its reference store.");
            }
            finally { observation.DisposeOperation(); }
            return;
        }
        if (observation.Role == NativePlaybackSourceRole.SetTime && _source?.OwnsReservedTimeBefore(observation) == true)
        {
            // The same source's actual native reservation identifies this
            // private attempt. Numeric media values or a prepared writer flag
            // cannot distinguish it from an external equal-value setter.
            _logicalObservations.Add(observation); _source.ObserveBefore(observation); return;
        }
        if (observation.Role == NativePlaybackSourceRole.LowerSetPitch && !observation.IsExactLowerPitchChild
            || observation.Role == NativePlaybackSourceRole.EngineSetPitch && !observation.IsExactEnginePitchChild
            || observation.Role == NativePlaybackSourceRole.LowerSetTime && !observation.IsExactLowerTimeChild
            || observation.Role == NativePlaybackSourceRole.SetTime && !HasStockResumeTimeParent(observation))
        {
            ObserveDirectPitchBefore(observation);
            return;
        }
        if (TryObserveDirectPitchChild(observation)) return;
        if (TryObserveDirectTimeChild(observation)) return;
        if (!observation.IsExactStockStop && observation.Role is NativePlaybackSourceRole.AudioPlayHelper or NativePlaybackSourceRole.AudioPlayDelayed
            or NativePlaybackSourceRole.AudioPlay or NativePlaybackSourceRole.AudioStop
            or NativePlaybackSourceRole.AudioStopBoolean or NativePlaybackSourceRole.AudioSetLoop)
        {
            // Only native's exact retained AudioSource watch can issue this.
            // A direct attempt ends old authority, without granting stock origin.
            try
            {
                _source?.WithdrawForDirectAudio();
                _session?.Withdraw("The retained AudioSource entered a direct playback/loop attempt before its original.");
            }
            finally { observation.DisposeOperation(); }
            return;
        }
        if (RouteInitializationBefore(observation)) return;
        if (_source is null) { observation.DisposeOperation(); return; }
        _logicalObservations.Add(observation); _source.ObserveBefore(observation);
    }
    internal void ObserveReturned(NativePlaybackSourceCompletion completion)
    {
        VerifyRegisteredOwner();
        if (ObserveDirectPitchReturned(completion)) return;
        if (_initialization?.ObserveReturned(completion) == true) return;
        foreach (var retained in _retainedInitializers) if (retained.ObserveReturned(completion)) return;
        if (_logicalObservations.Contains(completion.Observation)) _source?.ObserveReturned(completion);
    }
    private NativePlaybackStockRootedSource.ReadbackReceipt Current()
    {
        Open();
        if (_binding is null) throw new GameplayStateException(ErrorCode.NotReady, "The exact initializer has not captured its first rooted graph.");
        _run.VerifyCurrent(_binding); _group!.VerifyCurrent();
        return _source!.Current ?? throw new GameplayStateException(ErrorCode.NotReady, "No exact completed stock occurrence owns the rooted Clock baseline.");
    }
    // Identity selection grants no native read/write authority. The selected
    // receipt supplies its current native fences and actual roots at use.
    private NativePlaybackStockRootedSource.ReadbackReceipt ObserveCurrent()
    {
        OpenOperation();
        var binding = _binding ?? throw new GameplayStateException(ErrorCode.NotReady,
            "The exact initializer has not captured its first rooted graph.");
        _run.VerifyCurrent(binding);
        if (_handoff is not null || InitializationPending)
            throw new GameplayStateException(ErrorCode.NotReady, "The current source has a pending native transition.");
        var source = _source ?? throw new GameplayStateException(ErrorCode.NotReady,
            "The route has no registered rooted source.");
        return source.ObserveCurrentReadbackIdentity() ?? throw new GameplayStateException(ErrorCode.NotReady,
            "No exact completed stock occurrence owns the rooted Clock baseline.");
    }

    private void VerifyOperationSource()
    {
        var receipt = ObserveCurrent();
        var binding = _binding; var run = _run; var source = _source; var route = _sourceRoute;
        var consumer = _callProfile;
        VerifyOperationConsumer(consumer);
        _ = receipt.Read(); // Actual roots, native occurrence fences and independent DSP/media readback.
        VerifyOperationConsumer(consumer);
        OpenOperation(); run.VerifyCurrent(binding);
        if (!ReferenceEquals(_binding, binding) || !ReferenceEquals(_run, run)
            || !ReferenceEquals(_source, source) || !ReferenceEquals(_sourceRoute, route)
            || !ReferenceEquals(_callProfile, consumer)
            || !ReferenceEquals(ObserveCurrent(), receipt))
            throw new GameplayStateException(ErrorCode.TargetExpired,
                "The registered route or readback receipt changed during the playback operation.");
    }
    private void VerifyOperationConsumer(NativePlaybackGetTimeCallProfile? consumer, bool verifyConsumerBodies = false)
    {
        OpenOperation();
        if (!ReferenceEquals(_callProfile, consumer))
            throw new GameplayStateException(ErrorCode.TargetExpired, "The running GetTime consumer registration changed.");
        if (_group!.OwnedFixture) return; // This scope has no fixed game consumer.
        if (consumer is null || consumer.MethodInfo.ToInt64() != _binding.GetTimeMethod)
            throw new GameplayStateException(ErrorCode.NotReady, "The formal playback operation has no matching fixed GetTime consumer.");
        if (verifyConsumerBodies) consumer.VerifyPreparedSource(_group.GetTimeLease);
        else consumer.VerifyOperation(_group.GetTimeLease);
        OpenOperation();
    }
    private void VerifySource()
    {
        Open();
        var binding = _binding ?? throw new GameplayStateException(ErrorCode.NotReady,
            "The exact initializer has not captured its first rooted graph.");
        var run = _run;
        var source = _source ?? throw new GameplayStateException(ErrorCode.NotReady,
            "The route has no registered rooted source.");
        run.VerifyCurrent(binding);
        // Select identity only. Read performs the complete receipt, graph,
        // physical group and fresh getter checks before consuming any sample.
        // No successful validation is cached or carried across the read.
        var receipt = source.ObserveCurrentReadbackIdentity()
            ?? throw new GameplayStateException(ErrorCode.NotReady,
                "No exact completed stock occurrence owns the rooted Clock baseline.");
        var consumer = _callProfile;
        VerifyOperationConsumer(consumer, verifyConsumerBodies: true);
        _ = receipt.Read(); // Independent API/fences/run, never Clock or publication.
        // A native getter may reenter and replace the registered route. The
        // complete read cannot authorize a different source selected afterward.
        Open(); run.VerifyCurrent(binding);
        if (!ReferenceEquals(_binding, binding) || !ReferenceEquals(_run, run)
            || !ReferenceEquals(_source, source) || !ReferenceEquals(_callProfile, consumer)
            || !ReferenceEquals(source.ObserveCurrentReadbackIdentity(), receipt))
            throw new GameplayStateException(ErrorCode.TargetExpired,
                "The registered route or readback receipt changed during source verification.");
    }
    internal Session PreparePausedClock(NativePlaybackGetTimeCallProfile? callProfile = null)
    {
        Open();
        _freshAdoption?.VerifyRouter(this);
        if (_handoff is not null || InitializationPending)
            throw new GameplayStateException(ErrorCode.NotReady, "The registered successor source gate has not opened.");
        if (_session is not null) throw new GameplayStateException(ErrorCode.Conflict, "The route already has a registered logical session.");
        _group!.VerifyCurrent();
        var permittedNullCaller = IntPtr.Zero;
        if (_group.OwnedFixture)
        {
            if (callProfile is not null) throw new GameplayStateException(ErrorCode.InvalidArgument, "The owned fixture has no pinned game consumer profile.");
        }
        else
        {
            var caller = IntPtr.Add(_group.Module, NativePlaybackGetTimeCallProfile.ProgressUpdateReturnRva);
            if (callProfile is null || callProfile.MethodInfo.ToInt64() != _binding.GetTimeMethod || !callProfile.AllowsNull(caller.ToInt64()))
                throw new GameplayStateException(ErrorCode.NotReady, "A stock image requires the original private fixed consumer profile attached to the actual GetTime lease.");
            callProfile.VerifyOperation(_group.GetTimeLease);
            permittedNullCaller = caller;
        }
        _callProfile = callProfile;
        var receipt = Current(); var sample = receipt.Read().Sample;
        if (!receipt.PhysicalExited || !IsPausedClockBaseline(receipt, sample))
            throw new GameplayStateException(ErrorCode.NotReady, "Clock admission requires an actual physically completed ready Pause and independent rooted readback.");
        var initial = new NativePlaybackClockState(_binding, _admitted?.AdmittedRevision ?? 1, NativePlaybackPhase.Paused,
            sample.DspSeconds, sample.AudioPositionSeconds, sample.Pitch, sample.ClipLengthSeconds);
        var session = Session.Issue(this, receipt, initial);
        _session = session;
        try { session.Prepare(_group!.GetTimeLease, permittedNullCaller, VerifySource); return session; }
        catch { session.Withdraw("Initial dormant Clock publication failed."); throw; }
    }
    private static bool IsPausedClockBaseline(NativePlaybackStockRootedSource.ReadbackReceipt receipt, NativePlaybackRateSample sample)
        => receipt.Role is NativePlaybackSourceRole.Pause or NativePlaybackSourceRole.ProgressPlay
            && sample.Paused && !sample.AudioPlaying && !sample.ProgressPlaying && !sample.Preparing && sample.TimeReady
            && !sample.LevelOver && !sample.Crashed && !sample.HasStockOrigin;

    internal NativePlaybackSingleReturnSample ReadClock()
    {
        // Native dormant entry never calls this, even with real observers and an
        // original in flight. Once Arm commits the final Clock already exists.
        OpenOperation();
        var session = _session ?? throw new GameplayStateException(ErrorCode.NotReady, "The actual native reader has no registered Clock session.");
        var state = session.ReadFinalSnapshot();
        var binding = _binding; var run = _run; var source = _source; var route = _sourceRoute;
        var consumer = _callProfile;
        VerifyOperationConsumer(consumer);
        var receipt = ObserveCurrent();
        var sample = receipt.Read().Sample;
        VerifyOperationConsumer(consumer);
        // No new getter follows this fence check. A healthy fixed consumer
        // cannot authorize a superseded sample, route or Clock snapshot.
        receipt.VerifyOperationIdentity();
        session.Registered();
        run.VerifyCurrent(binding);
        if (!ReferenceEquals(_binding, binding) || !ReferenceEquals(_run, run)
            || !ReferenceEquals(_source, source) || !ReferenceEquals(_sourceRoute, route)
            || !ReferenceEquals(_callProfile, consumer)
            || !ReferenceEquals(ObserveCurrent(), receipt) || !session.Clock.HasExactReadState(state)
            || sample.Binding != state.Binding)
            throw new GameplayStateException(ErrorCode.TargetExpired, "The source or final Clock changed during the native playback read.");
        return new(NativePlaybackClockPublication.Identity(binding), state.Revision,
            NativePlaybackClockPublication.Phase(state), sample.DspSeconds);
    }
    internal void PumpCompleted()
    {
        if (_thread != Environment.CurrentManagedThreadId) throw new GameplayStateException(ErrorCode.WrongThread, "Actual Clock/source drain must be read on its owner thread.");
        VerifyRegisteredOwner();
        PumpPhysicalFaults();
        _group?.PumpCompleted(); // Construction may have thrown before assigning _source.
        PumpDirectPitchCompleted();
        if (_disposed && _source is { RootsReleased: false } source)
        {
            var activity = _group!.GetTimeLease.ReadActivity();
            if (activity.Known && activity.InFlight == 0) source.Dispose();
        }
        _source?.PumpCompleted();
        foreach (var retained in _retainedSources)
        {
            if (_disposed && !retained.RootsReleased)
            {
                var activity = _group!.GetTimeLease.ReadActivity();
                if (activity.Known && activity.InFlight == 0) retained.Dispose();
            }
            retained.PumpCompleted();
        }
        PumpInitialization();
        foreach (var retained in _retainedObservedInitializers.ToArray())
        {
            retained.Dispose();
            if (!retained.RootsRetained) _retainedObservedInitializers.Remove(retained);
        }
        _freshAdoption?.Pump(this);
    }
    public void Dispose()
    {
        if (_thread != Environment.CurrentManagedThreadId) throw new GameplayStateException(ErrorCode.WrongThread, "Dispose must retain roots until the actual owner thread can drain them.");
        VerifyRegisteredOwner();
        if (_disposed) { PumpCompleted(); return; }
        _session?.Withdraw("The logical stock Clock route was disposed.");
        _freshAdoption?.Close(this, "The fresh route was disposed.");
        if (_observedInitialization is { } observed)
        {
            _observedInitialization = null;
            _retainedObservedInitializers.Add(observed); observed.Dispose();
        }
        var initializationHold = _freshInitializationRoots; _freshInitializationRoots = null;
        initializationHold?.Dispose();
        _disposed = true;
        PumpCompleted();
    }

    internal sealed partial class Session
    {
        private readonly NativePlaybackStockClockRouter _owner;
        private readonly NativePlaybackStockRootedSource.ReadbackReceipt _baseline;
        private readonly NativePlaybackClockState _initial;
        private NativePlaybackClockPublication? _publication;
        private NativePlaybackClock? _finalClock;
        private bool _armed, _withdrawn;
        internal bool ProductionAdmission => false;
        private Session(NativePlaybackStockClockRouter owner, NativePlaybackStockRootedSource.ReadbackReceipt baseline, NativePlaybackClockState initial)
            => (_owner, _baseline, _initial) = (owner, baseline, initial);
        internal static Session Issue(NativePlaybackStockClockRouter owner, NativePlaybackStockRootedSource.ReadbackReceipt baseline, NativePlaybackClockState initial)
        {
            if (owner._session is not null || !ReferenceEquals(owner.Current(), baseline))
                throw new GameplayStateException(ErrorCode.Conflict, "Only the actual route can issue its first current source session.");
            return new(owner, baseline, initial);
        }
        internal void Registered()
        {
            _owner.OpenOperation();
            if (!ReferenceEquals(_owner._session, this) || _withdrawn)
                throw new GameplayStateException(ErrorCode.TargetExpired, "A copied, withdrawn, or superseded Clock session is not registered.");
        }
        internal void Prepare(NativePlaybackGetTimeHookLease hook, IntPtr caller, Action verifySource)
        {
            _owner.Open(); Registered();
            if (_publication is not null) throw new InvalidOperationException("The initial publication may be constructed only once.");
            _baseline.VerifyCurrent();
            _publication = _owner._freshAdoption is { } fresh
                ? NativePlaybackClockPublication.PrepareFresh(hook, _initial, caller, verifySource, this, fresh, _baseline,
                    group: _owner._group, verifyOperationSource: _owner.VerifyOperationSource)
                : _owner._admitted is { } transfer
                ? NativePlaybackClockPublication.PrepareTransferred(hook, _initial, caller, verifySource, transfer,
                    group: _owner._group, verifyOperationSource: _owner.VerifyOperationSource)
                : NativePlaybackClockPublication.PrepareDormant(hook, _initial, caller, verifySource,
                    group: _owner._group, verifyOperationSource: _owner.VerifyOperationSource);
        }
        internal bool TryArm()
        {
            _owner.Open(); Registered();
            if (_armed) throw new GameplayStateException(ErrorCode.Conflict, "The final Clock is already armed.");
            _baseline.VerifyCurrent();
            if (!_publication!.TryPrepareDormant()) return false;
            _publication.VerifyDormant(_initial);
            // This slot is assigned once, before the real native activation gate.
            _finalClock ??= _owner._admitted is { } transfer
                ? NativePlaybackClock.CreateAdmitted(_initial, _publication, transfer)
                : NativePlaybackClock.CreatePrepared(_initial, _publication);
            if (!_publication.TryArm(this)) return false;
            _armed = true;
            if (_owner._freshAdoption is null) _owner._group!.BindLogicalHandoff();
            if (!_publication.IsCurrent(_finalClock.Capture())) { Withdraw("Final Clock arm readback mismatch."); throw new InvalidOperationException("Clock arm failed readback."); }
            return true;
        }
        internal NativePlaybackClock Clock
        { get { Registered(); if (!_armed) throw new GameplayStateException(ErrorCode.NotReady, "The final Clock is still dormant."); return _finalClock!; } }
        internal void VerifyFinalClock(NativePlaybackClockPublication publication, NativePlaybackClockState initial)
        {
            Registered();
            if (_finalClock is null || !ReferenceEquals(_publication, publication) || !ReferenceEquals(_initial, initial)
                || _finalClock.Capture() != initial)
                throw new GameplayStateException(ErrorCode.TargetExpired, "Arm requires this session's unique final Clock and exact initial publication.");
        }
        internal void VerifyActivationBaseline(bool afterCommit)
        {
            Registered();
            var consumer = _owner._callProfile;
            _owner.VerifyOperationConsumer(consumer);
            var sample = _baseline.Read().Sample;
            _owner.VerifyOperationConsumer(consumer);
            var physicalExit = _baseline.ReadOperationPhysicalExit();
            Registered();
            // DSP may advance during Pause. The published media anchor, pitch
            // and clip length must still match the exact completed Pause.
            if (!physicalExit || !IsPausedClockBaseline(_baseline, sample) || sample.Binding != _initial.Binding
                || sample.AudioPositionSeconds != _initial.MediaAnchorSeconds || sample.Pitch != _initial.Rate
                || sample.ClipLengthSeconds != _initial.ClipLengthSeconds)
                throw new GameplayStateException(afterCommit ? ErrorCode.InvalidGameState : ErrorCode.NotReady,
                    "The actual paused position, pitch, clip length or playback state changed before Clock activation could be confirmed.");
        }
        internal NativePlaybackClockState ReadFinalSnapshot()
        {
            Registered();
            return _finalClock?.Capture() ?? throw new GameplayStateException(ErrorCode.NotReady, "No final Clock exists; native gate must still be dormant.");
        }
        internal void Observe(NativePlaybackStockRootedSource.ReadbackReceipt receipt)
        {
            Registered();
            if (!_armed) { Withdraw("A stock transition replaced the dormant baseline."); return; }
            if (receipt.Role is NativePlaybackSourceRole.Pause or NativePlaybackSourceRole.ProgressPlay)
            {
                var sample = receipt.Read().Sample;
                Clock.ObservePause(_initial.Binding, sample.DspSeconds, sample.AudioPositionSeconds, sample.Pitch);
            }
            else
            {
                var (sample, startDsp) = receipt.ReadScheduledBaseline();
                Clock.ObserveScheduled(_initial.Binding, sample.DspSeconds, startDsp,
                    sample.AudioPositionSeconds, sample.Pitch);
            }
        }
        internal void Withdraw(string reason)
        {
            if (_owner._thread != Environment.CurrentManagedThreadId)
                throw new GameplayStateException(ErrorCode.WrongThread, "Clock withdrawal must run on its owner thread; physical callbacks enqueue their exact fault.");
            if (!ReferenceEquals(_owner._session, this)) return; // Stale copied cleanup owns no successor.
            if (_withdrawn) return; _withdrawn = true;
            if (_finalClock is not null) _finalClock.Withdraw(reason);
            else _publication?.Withdraw(reason);
        }
    }
}
