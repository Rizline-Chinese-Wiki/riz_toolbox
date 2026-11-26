using System.Collections.Concurrent;
using System.Diagnostics;
using RizToolbox.Core.Diagnostics;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;
using RizToolbox.Adapter.Judgements;

namespace RizToolbox.Adapter.Gameplay;

internal enum NativePlaybackStockHostStage
{
    WaitingForInstallation, WaitingForRun, CapturingSource, WaitingForPause,
    WaitingForSuccessorRun, TransferringSource, TransitionUnavailable,
    PreparingClock, ReadyExperimental, Draining, Withdrawn
}

/// <summary>One process-owned physical installation, its first fresh source and
/// actual typed successors. Progress follows the shared observer's Poll; event
/// callbacks never create a run token, capture roots or arm a Clock.</summary>
internal sealed class NativePlaybackStockRateHost : INativePlaybackRateProvider,
    INativePlaybackRateProviderLifetime, INativePlaybackGetTimeHookProvider,
    INativePlaybackRateSourceDescriptorProvider, INativePlaybackRateSourceDescriptorObservation, INativePlaybackAvailabilityObservation,
    INativePlaybackClockReadProvider, INativePlaybackSeekProvider
{
    private static readonly List<NativePlaybackStockRateHost> PendingDrain = new();
    private readonly NativePlaybackStockRateHost _registered;
    private readonly NativeRuntime _api;
    private readonly IMainThreadDispatcher _main;
    private readonly ILogger _log;
    private readonly GameEventState _state;
    private readonly AdapterBuildProfile _profile;
    private readonly string _dobby;
    private readonly ConcurrentQueue<object> _faults = new();
    private NativePlaybackFilePreparation? _filePreparation;
    private NativePlaybackFilePreparation.PreparedFiles? _installationFiles;
    private ExactHookBroker.PlaybackSourceInstallGroup.Preparation? _nativePreparation;
    private bool _advancingPreparation;
    private PlaybackSourceGroupLease? _group;
    private NativeRuntime.NativePlaybackStockTargets? _stock;
    private GameplayClockNativeProfile? _clockProfile;
    private NativePlaybackStockCodeProfile? _sourceProfile;
    private NativePlaybackGetTimeCallProfile? _callProfile;
    private GameEventState.PlaybackRunAssociation? _run;
    private NativePlaybackFreshSourceCapture? _capture;
    private NativePlaybackStockClockRouter? _route;
    private NativePlaybackStockClockRouter.Session? _session;
    private NativePlaybackStockClockRouter.RateProviderHost? _logicalHost;
    private bool _installAttempted, _captureAttempted, _stopped, _disposed, _pumping, _groupReleased;
    private long _nextInstallProbe;
    private long _nextProgressPump;
    private string? _failure;
    private OperationError? _transitionError;
    private GameEventState.PlaybackRunAssociation? _transitionIdleRun;
    private NativePlaybackStockClockRouter.TransitionIdleHint? _transitionIdleHint;
    private OperationError? _transitionIdleError;
    private ulong _finalInitializerReturns, _transitionIdleFinalInitializerReturns;
    private JudgementModule? _judgements;
    internal NativePlaybackStockHostStage Stage { get; private set; } = NativePlaybackStockHostStage.WaitingForInstallation;
    internal string Detail { get; private set; } = "Playback support is waiting for its runtime components.";
    internal bool FrameworkReady => _logicalHost is not null && !_stopped && Stage == NativePlaybackStockHostStage.ReadyExperimental;
    // Managed identity for observation invalidation only. The rate facade may
    // survive several sessions, so the facade itself is not a source stamp.
    internal object? PublishedObservationSource => FrameworkReady && _route is { } route
        && _session is { } session && session.ReadyForRateHost(route) ? session : null;
    internal bool ProductionAdmission => false;
    internal bool SupportsSuccessorSources => !_stopped && _route?.SupportsObservedSuccessors == true;

    internal NativePlaybackStockRateHost(NativeRuntime api, IMainThreadDispatcher main, ILogger log,
        GameEventState state, AdapterBuildProfile profile, string dobby)
        => (_registered, _api, _main, _log, _state, _profile, _dobby) = (this, api, main, log, state, profile, dobby);

    internal void AttachJudgementModule(JudgementModule module)
    {
        VerifyOwner(); ArgumentNullException.ThrowIfNull(module);
        if (_judgements is not null) throw new InvalidOperationException("The playback host's judgement module is once-attached.");
        _judgements = module;
    }

    private void VerifyOwner()
    {
        _main.VerifyAccess();
        if (!ReferenceEquals(_registered, this))
            throw new GameplayStateException(ErrorCode.TargetExpired, "A copied playback host owns no installation or source.");
    }

    private void SetStage(NativePlaybackStockHostStage stage, string detail)
    {
        if (Stage == stage && Detail == detail) return;
        Stage = stage; Detail = detail;
        _log.TryLog(LogLevel.Debug, "Playback host: " + stage + ". " + detail);
    }

    // This is deliberately separate from Probe/Capture. Those can be called by
    // synchronous event subscribers while GameEventState is still mutating.
    public void PumpCompleted() => Pump(prepareSource: true);

    // Dormant maintenance services existing native responsibility only. It
    // must not create a physical group before a control consumer requests it.
    internal void PumpInstallationAndRetirement() => Pump(prepareSource: false);

    private void Pump(bool prepareSource)
    {
        VerifyOwner();
        if (_pumping) return;
        _pumping = true;
        try
        {
            // Relevant native workers suppress managed source observers.
            // Physical fault maintenance cannot wait for async delivery or
            // the slower source-preparation/observation progress interval.
            PumpFaults();
            if (_stopped) { Drain(); return; }
            if (_group is null)
            {
                if (!prepareSource) { CancelFilePreparation(); return; }
                PreparePhysical();
                if (_group is null || _stopped) return;
            }
            var now = Stopwatch.GetTimestamp();
            if (_faults.IsEmpty && now < _nextProgressPump) return;
            _nextProgressPump = now + Math.Max(1, Stopwatch.Frequency / 4);
            if (!prepareSource)
            {
                // Early coverage remains armed. Previously issued ownership
                // continues draining, but a dormant provider may not initiate
                // a new capture, handoff, or Clock publication from this pump.
                if (_route is { } existing) existing.PumpCompleted();
                else _group.PumpCompleted();
                return;
            }
            if (_route is null && _run is null)
            {
                // A committed observed run supplies identity, never native
                // playback authority. In the menu there is no source/Clock to
                // admit: retain native fault and completion maintenance without
                // repeatedly preparing all gameplay evidence for an absent run.
                try { _run = _state.CaptureCurrentPlaybackRun(); }
                catch (GameplayStateException error) when (error.Code == ErrorCode.NotReady)
                {
                    _group.PumpCompleted();
                    SetStage(NativePlaybackStockHostStage.WaitingForRun, "Playback support is waiting for a song to start.");
                    return;
                }
            }
            if (_route is not null && _session is null && _logicalHost is null
                && Stage == NativePlaybackStockHostStage.WaitingForPause)
            {
                // Empty initial waiting is dispatch, never source admission.
                // Keep actual native faults and completion/drain duties moving;
                // a candidate, changed run or pending transition takes the full
                // original validation path below before any Clock or control.
                _route.PumpCompleted();
                PumpFaults();
                if (_stopped) { Drain(); return; }
                GameEventState.PlaybackRunAssociation? currentRun;
                try { currentRun = _state.CaptureCurrentPlaybackRun(); }
                catch (GameplayStateException error) when (error.Code == ErrorCode.NotReady) { currentRun = null; }
                if (ReferenceEquals(currentRun, _run) && currentRun is not null && _route.WaitsForFirstReadback(currentRun))
                    return;
            }
            if (_route is { } idleRoute && _transitionIdleRun is { } idleRun
                && _transitionIdleHint is { } idleHint
                && (Stage is NativePlaybackStockHostStage.TransitionUnavailable
                    or NativePlaybackStockHostStage.WaitingForSuccessorRun))
            {
                // This exact host run was refused for lack of an armed
                // predecessor. Maintain all native completion/fault duties,
                // then inspect only dispatch identities. An ordinary source
                // callback that created no initializer cannot re-run the
                // complete physical group on every frame.
                idleRoute.PumpCompleted();
                PumpFaults();
                if (_stopped) { Drain(); return; }
                GameEventState.PlaybackRunAssociation? idleObserved;
                try { idleObserved = _state.CaptureCurrentPlaybackRun(); }
                catch (GameplayStateException error) when (error.Code == ErrorCode.NotReady)
                { idleObserved = null; }
                if (idleObserved is null)
                {
                    _transitionError = new(ErrorCode.NotReady, "Playback support is waiting for the next song to start.");
                    SetStage(NativePlaybackStockHostStage.WaitingForSuccessorRun, _transitionError.Message);
                    return;
                }
                if (ReferenceEquals(idleObserved, idleRun) && idleHint.StillCurrent(idleRoute)
                    && _finalInitializerReturns == _transitionIdleFinalInitializerReturns)
                {
                    if (Stage == NativePlaybackStockHostStage.WaitingForSuccessorRun && _transitionIdleError is { } unavailable)
                    {
                        _transitionError = unavailable;
                        SetStage(NativePlaybackStockHostStage.TransitionUnavailable, unavailable.Message);
                    }
                    return;
                }
                _transitionIdleRun = null; _transitionIdleHint = null; _transitionIdleError = null;
            }
            if (Stage == NativePlaybackStockHostStage.ReadyExperimental && _route is { } readyRoute
                && _session is { } readySession && _logicalHost is not null)
            {
                // Ready maintenance does not re-create a provider or consume
                // a value. Keep faults/drain current and compare the actual
                // observed association; real transitions take the full path.
                readyRoute.PumpCompleted();
                PumpFaults();
                if (_stopped) { Drain(); return; }
                GameEventState.PlaybackRunAssociation? readyObserved;
                try { readyObserved = _state.CaptureCurrentPlaybackRun(); }
                catch (GameplayStateException error) when (error.Code == ErrorCode.NotReady) { readyObserved = null; }
                if (readyObserved is not null && ReferenceEquals(readyObserved, _run)
                    && readyRoute.MaintainsReadySession(readySession, readyObserved)) return;
            }
            // Every real run still passes all current physical/code checks
            // before capture, adoption, Clock publication or value control.
            _group.VerifyCurrent();
            _sourceProfile!.VerifyCurrent();
            _clockProfile!.VerifyCurrent();
            if (_route is null) _group.PumpCompleted();
            if (_route is null)
            {
                _run!.VerifyHostCurrent();
                if (_capture is null)
                {
                    if (_captureAttempted) throw new GameplayStateException(ErrorCode.TargetExpired, "The first playback source capture already ended.");
                    _captureAttempted = true;
                    SetStage(NativePlaybackStockHostStage.CapturingSource, "Preparing the current song's playback source.");
                    _capture = NativePlaybackFreshSourceCapture.Capture(_api, _run, _stock!, _group);
                }
                if (!_capture.TryIssue()) return; // Keep this same candidate while native calls drain.
                _route = NativePlaybackStockClockRouter.AdoptFresh(_api, _capture, _run, _group);
            }
            _route.PumpCompleted();
            GameEventState.PlaybackRunAssociation observed;
            try { observed = _state.CaptureCurrentPlaybackRun(); }
            catch (GameplayStateException error) when (error.Code == ErrorCode.NotReady)
            {
                _session = null;
                _transitionError = new(ErrorCode.NotReady, "Playback support is waiting for the next song to start.");
                SetStage(NativePlaybackStockHostStage.WaitingForSuccessorRun, _transitionError.Message);
                return;
            }
            try
            {
                // Only the Router can consume a typed predecessor and capture
                // the successor graph. An observed run supplies no source epoch.
                _session = _route.GetOrPrepareObservedSession(observed, _callProfile);
                _run = observed;
                if (_session is null)
                {
                    _transitionError = new(ErrorCode.NotReady, "Preparing the next song's playback source.");
                    SetStage(NativePlaybackStockHostStage.TransferringSource, _transitionError.Message);
                    return;
                }
                if (!_session.ReadyForRateHost(_route))
                {
                    SetStage(NativePlaybackStockHostStage.PreparingClock, "Preparing the paused song's playback clock.");
                    _transitionError = new(ErrorCode.NotReady, Detail);
                    if (!_session.TryArm()) return;
                }
                // The facade stays registered while concrete session/provider
                // identities change; old readouts still fail exact ownership.
                _logicalHost ??= _route.AdoptFormalRateHost();
                // Descriptor observation returns immutable identity data. A
                // concrete value provider is still acquired and physically
                // verified by Capture/Execute, not by frame maintenance.
                _ = _logicalHost.ObserveSourceDescriptor()
                    ?? throw new GameplayStateException(ErrorCode.NotReady, "The prepared session has no current observed source identity.");
                _session.VerifyFormalRateAuthority();
                _transitionError = null;
                SetStage(NativePlaybackStockHostStage.ReadyExperimental,
                    "The playback clock and experimental paused playback-rate control are ready.");
            }
            catch (GameplayStateException error) when (error.Code == ErrorCode.NotReady)
            {
                _transitionError = new(error.Code, error.Message);
                SetStage(NativePlaybackStockHostStage.WaitingForPause, _transitionError.Message);
            }
            catch (GameplayStateException error) when (error.Code == ErrorCode.OperationUnsupported)
            {
                // A branch without an actual initializer/transfer is unavailable
                // for this transition. Keep the physical owner and its drain
                // responsibilities for later real observations; never reinstall.
                _session = null;
                _transitionIdleHint = _route.CaptureUnavailableTransitionIdleHint(observed);
                _transitionIdleRun = _transitionIdleHint is null ? null : observed;
                _transitionIdleFinalInitializerReturns = _finalInitializerReturns;
                _transitionError = new(ErrorCode.OperationUnsupported, error.Message);
                _transitionIdleError = _transitionIdleHint is null ? null : _transitionError;
                SetStage(NativePlaybackStockHostStage.TransitionUnavailable, error.Message);
            }
        }
        catch (Exception error)
        {
            Stop(error.Message);
            _log.Log(LogLevel.Warning, "Optional playback support stopped; ordinary gameplay observation remains available.", error);
        }
        finally { _pumping = false; }
    }

    private void PreparePhysical()
    {
        if (_nativePreparation is { } preparation)
        {
            AdvancePhysicalPreparation(preparation);
            return;
        }
        if (_installAttempted) throw new GameplayStateException(ErrorCode.TargetExpired, "The retained physical playback installation cannot be replaced.");
        if (_filePreparation is { } pending)
        {
            if (!pending.TryTake(out var files)) return;
            _filePreparation = null;
            pending.Dispose();
            try { BeginPhysicalPreparation(files); }
            finally { if (!ReferenceEquals(_installationFiles, files)) files.Dispose(); }
            return;
        }

        var now = Stopwatch.GetTimestamp();
        if (now < _nextInstallProbe) return;
        _nextInstallProbe = now + Stopwatch.Frequency;
        if (_profile.GameAssemblySha256 != GameplayClockNativeProfile.Fingerprint || _profile.UnityVersion != "2022.3.62f2")
            throw new UnsupportedSignatureException("Playback support requires the pinned game build and Unity version.");
        var directory = Path.GetDirectoryName(typeof(NativePlaybackStockRateHost).Assembly.Location)!;
        var getTime = Path.Combine(directory, "RizToolbox.SingleReturn.Prototype.dll");
        var source = Path.Combine(directory, "RizToolbox.PlaybackSourcePrototype.dll");
        if (!File.Exists(getTime) || !File.Exists(source))
        {
            SetStage(NativePlaybackStockHostStage.WaitingForInstallation, "Playback support is waiting for its runtime components.");
            return;
        }
        var module = GameplayClockNativeProfile.RequireMappedModule();
        _filePreparation = new(NativePlaybackExecutableSeal.ModulePath(module), GameplayClockNativeProfile.Fingerprint, getTime, source);
    }

    private void BeginPhysicalPreparation(NativePlaybackFilePreparation.PreparedFiles files)
    {
        _log.Log(LogLevel.Debug, "Playback installation: foundation capture entering.");
        var foundation = NativeFirstInstall.RequireReady().RequirePlaybackGroupAdoptionEvidence();
        _log.Log(LogLevel.Debug, "Playback installation: stock target capture entering.");
        var broker = new ExactHookBroker(_dobby, _main, _log);
        var retained = broker.ReadDormantPlaybackTargets(foundation.Module);
        _stock = retained?.StockIssuer ?? _api.ResolvePlaybackStockTargets(foundation.Module);
        _clockProfile = new GameplayClockNativeProfile(_api, _api.Class("Assembly-CSharp.dll", "InLevel", "LevelInformation"),
            preparedFiles: files);
        _callProfile = new NativePlaybackGetTimeCallProfile(_clockProfile);
        _sourceProfile = new NativePlaybackStockCodeProfile(foundation.Module, preparedFiles: files);
        _log.Log(LogLevel.Debug, "Playback installation: fixed source profiles entering.");
        var targets = retained ?? PlaybackSourceGroupTargets.FromStock(_stock);
        _log.Log(LogLevel.Debug, "Playback installation: fixed source profiles complete.");
        if (_stopped) return;
        files.VerifyHelpers();
        // Bind can retain process addresses even when installation fails. Once
        // entered it must never be retried with another owner or callback set.
        _installAttempted = true;
        var preparation = broker.BeginPlaybackSourceGroupPreparation(targets, files.GetTimePath, files.GetTimeSha256,
            files.SourcePath, files.SourceSha256, ReadClock, fault => _faults.Enqueue(fault), fault => _faults.Enqueue(fault),
            ObserveBefore, ObserveReturned, foundation: foundation);
        if (_stopped) { preparation.Dispose(); return; }
        _installationFiles = files;
        _nativePreparation = preparation;
    }

    private void AdvancePhysicalPreparation(ExactHookBroker.PlaybackSourceInstallGroup.Preparation preparation)
    {
        _advancingPreparation = true;
        try
        {
            _installationFiles!.VerifyHelpers();
            if (!preparation.IsPrepared)
            {
                preparation.PrepareNext();
                return;
            }

            var group = preparation.CommitNext();
            if (group is null)
            {
                if (preparation.WaitingForDormantPlay)
                    SetStage(NativePlaybackStockHostStage.WaitingForInstallation,
                        "Playback support is waiting for the current Play call to return.");
                return;
            }
            _group = group;
            ReleasePhysicalPreparation();
            if (_stopped) return;
            _log.Log(LogLevel.Debug, "Playback installation: physical group complete; attaching source profiles.");
            if (_stopped) return;
            _clockProfile!.AttachGetTimeHook(_group.GetTimeLease);
            _sourceProfile!.AttachSourceGroup(_group, _stock!, requireFreshSourceProtocol: true);
            SetStage(NativePlaybackStockHostStage.WaitingForRun, "Playback support is waiting for a song to start.");
        }
        finally
        {
            _advancingPreparation = false;
            if (_stopped)
            {
                try { ReleasePhysicalPreparation(); }
                finally { Drain(); }
            }
        }
    }

    private void ReleasePhysicalPreparation()
    {
        var preparation = _nativePreparation;
        var files = _installationFiles;
        _nativePreparation = null;
        _installationFiles = null;
        try { preparation?.Dispose(); }
        finally { files?.Dispose(); }
    }

    private void CancelFilePreparation()
    {
        var pending = _filePreparation;
        _filePreparation = null;
        pending?.Dispose();
    }
    private NativePlaybackSingleReturnSample ReadClock()
        => (_route ?? throw new GameplayStateException(ErrorCode.NotReady, "The playback clock has not been created.")).ReadClock();
    private void ObserveBefore(NativePlaybackSourceObservation observation)
    {
        if (_route is { } route) route.ObserveBefore(observation);
        else observation.DisposeOperation(); // Dormant physical ownership grants no logical source responsibility.
    }
    private void ObserveReturned(NativePlaybackSourceCompletion completion)
    {
        // A true/yield MoveNext can be called routinely. Only its actual
        // final false return can introduce a completed Start receipt. This
        // signal schedules full validation; it grants no receipt or source.
        if (completion.Observation.Role == NativePlaybackSourceRole.ProgressStartMoveNext
            && completion.Returned && completion.HasRawReturnByte && completion.RawReturnByte == 0)
        { unchecked { ++_finalInitializerReturns; } }
        _route?.ObserveReturned(completion);
    }
    private void PumpFaults()
    {
        if (!_stopped) _failure ??= _group?.PollOperationNativeFaults();
        while (_faults.TryDequeue(out var fault))
        {
            if (fault is NativePlaybackSingleReturnFault getTime)
            { _route?.OnGetTimeFault(getTime); _failure ??= getTime.Reason; }
            else if (fault is NativePlaybackSourceFault source)
            { _route?.OnSourceFault(source); _failure ??= source.Reason; }
        }
        // Still consume the exact delayed notifications after synchronous
        // withdrawal. They cannot restart preparation or the old session.
        if (_failure is not null && !_stopped) Stop(_failure);
    }

    private OperationError? AvailabilityError()
    {
        if (_disposed || _stopped || _failure is not null || !_faults.IsEmpty)
            return new(ErrorCode.TargetExpired, _failure ?? "The retained playback host has a pending native fault.");
        if (_transitionError is not null) return _transitionError;
        if (_logicalHost is null || _route is null || _session is null || _run is null
            || Stage != NativePlaybackStockHostStage.ReadyExperimental)
            return new(ErrorCode.NotReady, Detail);
        if (_groupReleased || _group is not { IsActive: true })
            return new(ErrorCode.TargetExpired, "The host's registered playback group is unavailable.");
        return null;
    }

    private NativePlaybackStockClockRouter.RateProviderHost Resolve()
    {
        VerifyOwner();
        if (AvailabilityError() is { } error)
            throw new GameplayStateException(error.KnownCode, error.Message);
        var host = _logicalHost!;
        var group = _group!;
        var route = _route!;
        var session = _session!;
        var run = _run!;
        // Select the exact current host/run/session. Its following operation
        // reads actual channels; fault draining and installation audits remain
        // in maintenance and preparation, without hiding a queued fault here.
        run.VerifyHostCurrent();
        route.VerifyRateHostOperation(host, session, run);
        if (_disposed || _stopped || !_faults.IsEmpty || _failure is not null || _transitionError is not null
            || Stage != NativePlaybackStockHostStage.ReadyExperimental || !ReferenceEquals(_logicalHost, host)
            || !ReferenceEquals(_group, group) || !ReferenceEquals(_route, route)
            || !ReferenceEquals(_session, session) || !ReferenceEquals(_run, run))
            throw new GameplayStateException(ErrorCode.TargetExpired, "The playback host changed during operation resolution.");
        return host;
    }
    public NativePlaybackRateProviderProbe Probe()
    {
        try { return Resolve().Probe(); }
        catch (Exception error)
        { return new(false, Detail, new(error is GameplayStateException state ? state.Code : ErrorCode.CapabilityUnavailable, error.Message)); }
    }
    public INativePlaybackRateSource Capture(NativePlaybackRateOrigin origin) => Resolve().Capture(origin);
    public GameEventState GameState { get { VerifyOwner(); return _state; } }
    public INativePlaybackSeekSource CaptureSeek(NativePlaybackRateOrigin origin) => Resolve().CaptureSeek(origin);
    public JudgementModule.SeekBackendAccess CaptureSeekJudgementBackend(GameEventState.PlaybackRunAssociation run)
    {
        VerifyOwner();
        if (_disposed || _stopped) throw new GameplayStateException(ErrorCode.TargetExpired, "Playback support ended.");
        return (_judgements ?? throw new GameplayStateException(ErrorCode.NotReady, "The judgement observer has not been attached.")).CaptureSeekBackend(run);
    }
    public NativePlaybackRateProviderProbe ProbeSeek()
    {
        try
        {
            var host = Resolve();
            var descriptor = host.CaptureSourceDescriptor() ?? throw new GameplayStateException(ErrorCode.NotReady, "The current song has no prepared playback source.");
            var binding = descriptor.Binding;
            using var source = host.CaptureSeek(new(binding.RunId, binding.ControllerGeneration,
                binding.Controller, binding.Information, binding.Progress, descriptor.Pause));
            if (!ReferenceEquals(_state.CaptureCurrentPlaybackRun(), source.Run))
                throw new GameplayStateException(ErrorCode.TargetExpired, "The Seek source belongs to an earlier observed song.");
            source.VerifyAuthority();
            var judgement = CaptureSeekJudgementBackend(source.Run);
            judgement.VerifyCurrent();
            using var writer = source.PrepareTimeWrite();
            writer.VerifyCurrent();
            _clockProfile!.VerifyCurrent();
            NativeSeekGameStatePreparation.VerifyDependencies(_api, _clockProfile, judgement);
            source.VerifyAuthority();
            return new(true, "Experimental paused Seek request entry for the fixed game build. Each requested target must separately prepare the complete audio, progress, note, judgement, hold and visual state transaction; unavailable target-specific work is refused before mutation. Actual game behavior remains unverified.");
        }
        catch (Exception error)
        {
            return new(false, "The current song cannot prepare an experimental Seek request.",
                new(error is GameplayStateException state ? state.Code : ErrorCode.OperationUnsupported, error.Message));
        }
    }
    public NativePlaybackRateSourceDescriptor? CaptureSourceDescriptor() => Resolve().CaptureSourceDescriptor();
    public NativePlaybackRateSourceDescriptor? ObserveSourceDescriptor()
    {
        VerifyOwner();
        if (_disposed || _stopped || Stage != NativePlaybackStockHostStage.ReadyExperimental) return null;
        return _logicalHost?.ObserveSourceDescriptor();
    }
    public NativePlaybackRateProviderProbe ObserveRateAvailability()
    {
        VerifyOwner();
        if (AvailabilityError() is { } error) return new(false, Detail, error);
        return _logicalHost!.ObserveRateAvailability();
    }
    public NativePlaybackRateProviderProbe ObserveSeekAvailability()
    {
        var rate = ObserveRateAvailability();
        if (!rate.Supported || rate.Error is not null) return rate;
        if (_judgements is null || _clockProfile is null || _group is not { HasLowerTime: true, HasProgressResource: true })
            return new(false, "The current host has no registered complete Seek request entry.",
                new(ErrorCode.NotReady, "Seek metadata and judgement observation must be attached before a request."));
        return new(true, "Experimental Seek request entry is registered for the observed song. Each target separately verifies its complete audio, progress, note, judgement, hold and visual transaction during preparation; this availability observation creates no participant.");
    }
    public NativePlaybackClockReadout ReadPlaybackClock(NativePlaybackRateOrigin origin) => Resolve().ReadPlaybackClock(origin);
    public void VerifyPlaybackClock(NativePlaybackClockReadout readout) => Resolve().VerifyPlaybackClock(readout);
    public void VerifyPlaybackClockState(NativePlaybackClockReadout readout)
        => Resolve().VerifyPlaybackClockState(readout);
    internal GameplayClockNativeProfile CaptureReadClockProfile(NativeRuntime api, IntPtr information)
    {
        VerifyOwner();
        if (_disposed || _stopped) throw new GameplayStateException(ErrorCode.TargetExpired, _failure ?? "Playback support ended.");
        if (_advancingPreparation || _group is not { } group || _clockProfile is not { } profile
            || _sourceProfile is not { } source || _stock is not { } targets)
            throw new GameplayStateException(ErrorCode.NotReady, Detail);
        if (!ReferenceEquals(_api, api) || profile.InformationClass != information)
            throw new UnsupportedSignatureException("The retained clock profile belongs to another runtime or LevelInformation class.");
        if (_logicalHost is not null)
        {
            // Once a logical Clock exists, retain the exact session and
            // source receipt checks already used by this provider.
            profile.VerifyOperation(Resolve().CaptureGetTimeHook());
        }
        else
        {
            PumpFaults();
            if (_stopped || _groupReleased || !group.IsActive)
                throw new GameplayStateException(ErrorCode.TargetExpired, _failure ?? "The retained physical playback hook is unavailable.");
            // The installed owner already holds the verified originals. This
            // read brackets current reader bytes with its exact live channel.
            var hook = group.GetTimeLease;
            profile.VerifyOperation(hook);
            source.VerifyReadOnlyBodies(group, targets);
            profile.VerifyReadOnlyBodies();
            profile.VerifyOperation(hook);
        }
        if (_disposed || _stopped || _groupReleased || _failure is not null || !_faults.IsEmpty
            || !ReferenceEquals(_group, group) || !ReferenceEquals(_clockProfile, profile))
            throw new GameplayStateException(ErrorCode.TargetExpired, _failure ?? "The retained clock profile changed during reading.");
        return profile;
    }

    public NativePlaybackGetTimeHookLease CaptureGetTimeHook()
    {
        VerifyOwner();
        if (_disposed || _stopped) throw new GameplayStateException(ErrorCode.TargetExpired, _failure ?? "Playback support ended.");
        if (_logicalHost is not null) return Resolve().CaptureGetTimeHook();
        // Before the first logical Clock exists this is physical installation
        // evidence, so retain its complete preparation audit.
        PumpFaults();
        if (_stopped) throw new GameplayStateException(ErrorCode.TargetExpired, _failure!);
        var group = _group ?? throw new GameplayStateException(ErrorCode.NotReady, Detail);
        group.VerifyCurrent(_stock!); _sourceProfile!.VerifyCurrent(); _clockProfile!.VerifyCurrent();
        var hook = group.GetTimeLease;
        if (!hook.IsActive || !hook.ReadHealth().Known)
            throw new GameplayStateException(ErrorCode.TargetExpired, "The retained physical playback hook is unavailable.");
        group.VerifyCurrent();
        return hook; // Physical identity works before a run or logical Clock exists.
    }

    private void Stop(string reason)
    {
        VerifyOwner(); _stopped = true; _failure ??= reason;
        CancelFilePreparation();
        if (!PendingDrain.Contains(this)) PendingDrain.Add(this);
        SetStage(NativePlaybackStockHostStage.Draining, "Playback support ended; pending calls are being released.");
        // Disposal can reenter through a diagnostic callback. A native step
        // finishes on this thread before the pending owner and files close.
        if (_advancingPreparation) return;
        try { ReleasePhysicalPreparation(); }
        finally { Drain(); }
    }
    private void Drain()
    {
        if (_advancingPreparation) return;
        try
        {
            _route?.Dispose();
            if (_route is null) _capture?.Dispose();
            _route?.PumpCompleted(); _group?.PumpCompleted();
            if (_route is { RootsReleased: false } || _capture is { RootsReleased: false }) return;
            if (_group is { } group && !_groupReleased)
            {
                var source = group.ReadActivity(); var getTime = group.GetTimeLease.ReadActivity();
                if (!source.Known || source.InFlight != 0 || !getTime.Known || getTime.InFlight != 0) return;
                group.Dispose(); _groupReleased = true;
            }
            PendingDrain.Remove(this);
            SetStage(NativePlaybackStockHostStage.Withdrawn, "Playback support is unavailable for this session.");
        }
        catch (Exception error)
        {
            // Unknown retirement keeps this exact owner in the pending list.
            _failure ??= error.Message;
        }
    }
    internal static void PumpRetired()
    {
        foreach (var host in PendingDrain.ToArray())
        {
            if (!host._main.IsMainThread) continue;
            AdapterTickPhase.Run(host._log, "playback-owner-retirement", host, static owner =>
            {
                owner.VerifyOwner();
                owner.Drain();
            });
        }
    }
    public void Dispose()
    {
        VerifyOwner(); _disposed = true;
        Stop("The adapter released playback support.");
    }
}
