using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal sealed record LocalRunBoundaryCounters(LocalBoundaryKind Boundary, long NativeCalls, long OriginalCalls,
    long Classified, long Continued, long Claimed, long Skipped, long Faults, long WrongThreadClaims, long SuppressedReentries);
internal sealed record LocalRunProtectionSnapshot(bool AllGatesInstalled, bool AcceptingNewRuns, bool StopRequested,
    bool Stopped, bool CleanupUnknown, bool HandlerWorkPending, bool HandlerWorkUnknown, int UnretiredSessions, int RetainedBindings, string? Failure,
    IReadOnlyList<LocalRunBoundaryCounters> Boundaries);
internal interface ILocalRunProtectionDiagnostics { LocalRunProtectionSnapshot Read(); }
internal sealed record LocalRunBoundaryActivity(long InFlight, bool Known);
internal interface ILocalRunBoundaryBinding : IDisposable
{
    LocalRunBoundaryCounters Read();
    // Missing integration must stay unverified, never a default successful drain.
    LocalRunBoundaryActivity ReadActivity() => new(0, false);
}
internal interface ILocalRunBoundaryTransport
{
    ILocalRunBoundaryBinding Bind(LocalBoundaryKind boundary, Func<long, bool> classify, Action<long> handle);
}
/// <summary>Host transaction callbacks, not provider callbacks. Tick must continue even
/// after the adapter module withdraws while any submitted consumers still need the host.</summary>
internal interface ILocalRunBoundaryHandler
{
    void ObserveControllerTerminalClaim(LocalRunSessionRegistry.SessionHandle session, long receiver) { }
    void ValidateHost(LocalRunSessionRegistry registry)
        => throw new InvalidOperationException("The handler has not verified this host's native boundary integration and transaction dependencies.");
    // Includes delayed bundle destruction, globals and UI/callback responsibility after
    // registry.Close. False means the handler has no remaining owned work or unknown cleanup.
    bool HasPendingWork { get; }
    void Handle(LocalBoundaryKind boundary, LocalRunSessionRegistry.SessionHandle session, long receiver);
    void Tick();
}

internal sealed class LocalRunProtectionCreationException : InvalidOperationException
{
    internal LocalRunProtectionHost Responsibility { get; }
    internal LocalRunProtectionCreationException(LocalRunProtectionHost owner, Exception error)
        : base(LocalRunFailureDiagnostics.Describe("LOCAL gate installation failed; bounded process responsibility is retained without retry", error), error)
        => Responsibility = owner;
}

/// <summary>Process-retained owner for the exact LOCAL pre-original gates. Provider/module
/// withdrawal stops admission; it never releases a gate still needed by an unretired run.
/// Native consumer-end validation belongs to the run adapter, not this count/ownership layer.</summary>
internal sealed class LocalRunProtectionHost : ILocalRunProtectionDiagnostics
{
    private const int MaximumHosts = 32;
    private static readonly object Gate = new();
    private static readonly HashSet<LocalRunProtectionHost> ProcessOwners = new();
    private static readonly LocalBoundaryKind[] Boundaries = {
        LocalBoundaryKind.ResultStart, LocalBoundaryKind.ControllerRetry, LocalBoundaryKind.ResultRetry, LocalBoundaryKind.ResultBack,
        LocalBoundaryKind.ControllerLevelOver
    };
    private readonly IMainThreadDispatcher _main;
    private readonly ILogger _log;
    private readonly ILocalRunBoundaryBinding?[] _bindings = new ILocalRunBoundaryBinding?[Boundaries.Length];
    private readonly bool[] _freeAttempted = new bool[Boundaries.Length];
    private ILocalRunBoundaryHandler? _handler;
    private bool _allInstalled, _accepting, _stopRequested, _stopped, _unknown, _handlerFaulted, _ticking, _handlerWorkPending, _handlerWorkUnknown;
    private int _handling, _classifying;
    private long _claimObservationFaults;
    private LocalBoundaryKind? _currentBoundary;
    private LocalRunSessionRegistry.SessionHandle? _currentSession;
    private string? _failure;
    private sealed record RetirementGuard(long Controller, long ResultScene);
    private RetirementGuard? _retirementGuard;
    private NativeLocalAudioContentRuntime? _composition;
    internal LocalRunSessionRegistry Sessions { get; }
    internal static int RetainedProcessHostCount { get { lock (Gate) return ProcessOwners.Count; } }
    private LocalRunProtectionHost(IMainThreadDispatcher main, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(main); ArgumentNullException.ThrowIfNull(log); main.VerifyAccess();
        (_main, _log) = (main, log); Sessions = new(main);
        lock (Gate)
        {
            if (ProcessOwners.Count >= MaximumHosts) throw new InvalidOperationException("LOCAL gate hosts exhaust the 32-owner live/unknown capacity.");
            ProcessOwners.Add(this);
        }
    }
    internal static LocalRunProtectionHost Create(IMainThreadDispatcher main, ILogger log, ILocalRunBoundaryTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var owner = new LocalRunProtectionHost(main, log);
        try
        {
            for (var index = 0; index < Boundaries.Length; index++)
            {
                var boundary = Boundaries[index];
                owner._bindings[index] = transport.Bind(boundary,
                    receiver => owner.Classify(boundary, receiver), receiver => owner.Handle(boundary, receiver))
                    ?? throw new InvalidOperationException("LOCAL boundary transport returned no binding.");
            }
            owner._allInstalled = true;
            // No admission before a complete launch/result transaction handler is supplied.
            return owner;
        }
        catch (Exception error)
        {
            owner._stopRequested = true; owner._unknown = true;
            owner._failure = LocalRunFailureDiagnostics.Describe("A gate installation outcome is unknown", error);
            try { owner._log.Log(LogLevel.Error, owner._failure, error); } catch { }
            owner.DetachKnownBindings();
            throw new LocalRunProtectionCreationException(owner, error);
        }
    }
    internal void InstallHandler(ILocalRunBoundaryHandler handler)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(handler);
        if (!_allInstalled || _stopped || _unknown || _stopRequested || _handling != 0 || _retirementGuard is not null || Sessions.UnretiredSessionCount != 0
            || _handler is not null && !ReferenceEquals(_handler, handler))
            throw new InvalidOperationException("A complete idle host can install one stable run transaction handler before admission.");
        handler.ValidateHost(Sessions);
        if (handler.HasPendingWork) throw new InvalidOperationException("A new handler must not bring unadopted work from another host.");
        Volatile.Write(ref _handler, handler); _accepting = true;
    }
    internal void RetainComposition(NativeLocalAudioContentRuntime composition)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(composition);
        if (_stopped || _stopRequested || _unknown || _handler is not null
            || _composition is not null && !ReferenceEquals(_composition, composition))
            throw new InvalidOperationException("This exact host already retained a composition or is unavailable.");
        _composition = composition; // Survives a partial native initialization and module withdrawal.
    }
    internal LocalRunSessionRegistry.SessionHandle RegisterPrepared(LocalRunSessionDescriptor descriptor, long metadataIdentity, IDisposable bundle)
    {
        _main.VerifyAccess();
        if (!_allInstalled || !_accepting || _stopRequested || _stopped || _unknown || _handlerFaulted || _handler is null || _retirementGuard is not null)
            throw new InvalidOperationException("LOCAL launch admission requires all installed gates and an active host transaction handler.");
        _handler.ValidateHost(Sessions);
        if (Read().CleanupUnknown)
            throw new InvalidOperationException("LOCAL admission is unavailable while retained native transaction responsibility is unknown.");
        if (_handling != 0)
        {
            var current = _currentSession is null ? null : Sessions.Read(_currentSession);
            if (_handling != 1 || _currentBoundary is not (LocalBoundaryKind.ControllerRetry or LocalBoundaryKind.ResultRetry)
                || current is null || (_currentBoundary == LocalBoundaryKind.ControllerRetry
                    ? current.Phase != LocalRunSessionPhase.Committed : current.Phase != LocalRunSessionPhase.ResultReady)
                || descriptor.Song != current.Identity.Song || descriptor.SongRevision != current.Identity.SongRevision || descriptor.ChartId != current.Identity.ChartId)
                throw new InvalidOperationException("An in-flight retry may prepare only its exact retained song revision/chart; other nested admission is rejected.");
        }
        return Sessions.RegisterPrepared(descriptor, metadataIdentity, bundle);
    }
    private bool Classify(LocalBoundaryKind boundary, long receiver)
    {
        Interlocked.Increment(ref _classifying);
        try
        {
            var retiring = Volatile.Read(ref _retirementGuard);
            if (retiring is not null && receiver != 0 && (boundary is LocalBoundaryKind.ControllerRetry or LocalBoundaryKind.ControllerLevelOver
                ? receiver == retiring.Controller : receiver == retiring.ResultScene)) return true;
            var session = Sessions.FindProtectedSession(boundary, receiver);
            if (session is null) return false;
            if (boundary == LocalBoundaryKind.ControllerLevelOver)
            {
                try { Volatile.Read(ref _handler)?.ObserveControllerTerminalClaim(session, receiver); }
                catch { Interlocked.Increment(ref _claimObservationFaults); }
            }
            return true;
        }
        finally { Interlocked.Decrement(ref _classifying); }
    }
    private void Handle(LocalBoundaryKind boundary, long receiver)
    {
        _main.VerifyAccess();
        if (_retirementGuard is not null) throw new InvalidOperationException("LOCAL retirement issuance cannot reenter a boundary handler; stock remains suppressed.");
        var session = Sessions.FindProtectedSession(boundary, receiver)
            ?? throw new InvalidOperationException("A claimed LOCAL receiver no longer has an exact protected host session.");
        if (_handler is null || _handlerFaulted) throw new InvalidOperationException("LOCAL transaction handling is unavailable; stock remains suppressed.");
        if (_handling != 0) throw new InvalidOperationException("Nested LOCAL host command transactions are rejected without stock fallback.");
        _handling++; _currentBoundary = boundary; _currentSession = session;
        try { _handler.Handle(boundary, session, receiver); }
        finally { _currentBoundary = null; _currentSession = null; _handling--; }
    }
    internal bool RequestStop()
    {
        _main.VerifyAccess(); _accepting = false; _stopRequested = true;
        NativeNeverActivatedRetirement.RequestCancellationForHost(this);
        return TryCompleteStop();
    }
    /// <summary>Real current entry/exit counts from every required native void binding.
    /// This is a drain observation, not evidence that future native consumers have ended.</summary>
    internal LocalRunBoundaryActivity ReadNativeBoundaryActivity()
    {
        _main.VerifyAccess();
        if (!_allInstalled || _stopped || _unknown || Interlocked.Read(ref _claimObservationFaults) != 0 || _bindings.Any(binding => binding is null)) return new(0, false);
        long inFlight = 0;
        foreach (var binding in _bindings)
        {
            var activity = binding!.ReadActivity();
            if (!activity.Known || activity.InFlight < 0) return new(inFlight, false);
            inFlight = checked(inFlight + activity.InFlight);
        }
        return new(inFlight, true);
    }
    /// <summary>Only the sealed never-activated transaction may provide the native verifier:
    /// it must prove no native callable source was published, zero Awake/globals changes and
    /// delayed destruction of all owned Unity objects. Counts alone never grant retirement.
    /// The short identity guard keeps a concurrent/reentrant old call suppressed while
    /// registry classification is retired before its roots are independently released.</summary>
    internal bool TryRetireNeverActivated(LocalRunSessionRegistry.SessionHandle session,
        ILocalRunConsumersEndedVerifier verifier, out bool closed)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(verifier); closed = false;
        if (!_ticking || _retirementGuard is not null || _handling != 0 || Volatile.Read(ref _classifying) != 0) return false;
        var snapshot = Sessions.Read(session);
        if (snapshot.Phase == LocalRunSessionPhase.Closed) { closed = true; return true; }
        if (!snapshot.ReturnRequested || snapshot.ResultSceneIdentity != 0 || snapshot.ResultSceneGeneration != 0
            || snapshot.Result is not null || snapshot.ResultCallClaimed || snapshot.ControllerIdentity == 0) return false;
        Volatile.Write(ref _retirementGuard, new(snapshot.ControllerIdentity, 0));
        try
        {
            var activity = ReadNativeBoundaryActivity();
            if (!activity.Known || activity.InFlight != 0 || Volatile.Read(ref _classifying) != 0) return false;
            var proof = Sessions.VerifyConsumersEnded(session, verifier);
            if (proof is null) return false;
            activity = ReadNativeBoundaryActivity();
            if (!activity.Known || activity.InFlight != 0 || Volatile.Read(ref _classifying) != 0) return false;
            closed = Sessions.Close(session, proof); return true;
        }
        finally { Volatile.Write(ref _retirementGuard, null); }
    }
    /// <summary>Active retirement only accepts the actual launch's issued
    /// verifier. Keep the exact controller/result identities suppressed through
    /// the final native drain and registry classification withdrawal.</summary>
    internal bool TryRetireActive(LocalRunSessionRegistry.SessionHandle session,
        NativeLocalActiveConsumersEndedVerifier verifier, out bool closed)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(verifier); closed = false;
        if (!verifier.IsFor(this, session) || !_ticking || _unknown || _handlerFaulted || _handlerWorkUnknown
            || _retirementGuard is not null || _handling != 0 || Volatile.Read(ref _classifying) != 0) return false;
        var snapshot = Sessions.Read(session);
        if (snapshot.Phase == LocalRunSessionPhase.Closed) { closed = true; return true; }
        if (!snapshot.ReturnRequested || snapshot.ControllerIdentity == 0 || snapshot.CleanupAttempted
            || snapshot.ProtectionRetired || snapshot.Phase != LocalRunSessionPhase.Returning) return false;
        Volatile.Write(ref _retirementGuard, new(snapshot.ControllerIdentity, snapshot.ResultSceneIdentity));
        try
        {
            var activity = ReadNativeBoundaryActivity();
            if (!activity.Known || activity.InFlight != 0 || Volatile.Read(ref _classifying) != 0) return false;
            var proof = Sessions.VerifyConsumersEnded(session, verifier);
            if (proof is null) return false;
            activity = ReadNativeBoundaryActivity();
            if (!activity.Known || activity.InFlight != 0 || Volatile.Read(ref _classifying) != 0
                || !verifier.ReverifyBeforeClose(this, session)) return false;
            activity = ReadNativeBoundaryActivity();
            if (!activity.Known || activity.InFlight != 0 || Volatile.Read(ref _classifying) != 0) return false;
            closed = Sessions.Close(session, proof); return true;
        }
        finally { Volatile.Write(ref _retirementGuard, null); }
    }
    internal bool TryCompleteStop()
    {
        _main.VerifyAccess(); if (_stopped) return true;
        if (!_stopRequested || _unknown || _handlerWorkUnknown || _composition?.Unknown == true
            || Interlocked.Read(ref _claimObservationFaults) != 0 || _retirementGuard is not null || _handling != 0 || Volatile.Read(ref _classifying) != 0 || Sessions.UnretiredSessionCount != 0
            || NativeNeverActivatedRetirement.HasPendingWorkForHost(this)
            || NativeLocalRunTerminationBoundaryState.HasPendingWorkForHost(this)
            || NativeLocalRunLaunchCoordinator.HasPendingWorkForHost(this)) return false;
        if (_handler is not null)
        {
            try { _handlerWorkPending = _handler.HasPendingWork; }
            catch (Exception error) { FailHandler(error); return false; }
            if (_handlerWorkPending) return false;
        }
        DetachKnownBindings();
        if (_unknown) return false;
        _stopped = true; _allInstalled = false;
        lock (Gate) ProcessOwners.Remove(this);
        return true;
    }
    private void DetachKnownBindings()
    {
        _allInstalled = false;
        for (var index = _bindings.Length - 1; index >= 0; index--)
        {
            if (_bindings[index] is not { } binding || _freeAttempted[index]) continue;
            _freeAttempted[index] = true;
            try { binding.Dispose(); _bindings[index] = null; }
            catch (Exception error) { _unknown = true; _failure ??= LocalRunFailureDiagnostics.Describe("LOCAL gate detach is unknown", error); }
        }
    }
    private void Tick()
    {
        _main.VerifyAccess(); if (_stopped || _ticking || _handling != 0 || Volatile.Read(ref _classifying) != 0) return;
        _ticking = true;
        try
        {
            if (_handler is not null && !_handlerFaulted)
            {
                try { _handler.Tick(); _handlerWorkPending = _handler.HasPendingWork; }
                catch (Exception error)
                {
                    FailHandler(error);
                }
            }
            NativeNeverActivatedRetirement.TickForHost(this);
            NativeLocalRunTerminationBoundaryState.TickForHost(this);
            NativeLocalRunLaunchCoordinator.TickForHost(this);
            TryCompleteStop();
        }
        finally { _ticking = false; }
    }
    private void FailHandler(Exception error)
    {
        _handlerFaulted = true; _handlerWorkUnknown = true; _accepting = false; _stopRequested = true;
        _failure ??= LocalRunFailureDiagnostics.Describe("LOCAL host transaction work is unconfirmed", error);
        try { _log.Log(LogLevel.Error, _failure + "; exact protection and outstanding responsibility remain retained without retry.", error); } catch { }
    }
    internal static void TickProcessOwners()
    {
        LocalRunProtectionHost[] snapshot;
        lock (Gate) snapshot = ProcessOwners.ToArray();
        foreach (var owner in snapshot)
        {
            if (!owner._main.IsMainThread) continue;
            try { owner.Tick(); }
            catch (Exception error) { owner.FailHandler(error); }
        }
    }
    public LocalRunProtectionSnapshot Read()
    {
        _main.VerifyAccess();
        var retirement = NativeNeverActivatedRetirement.ReadWorkForHost(this);
        var boundaryUnknown = NativeLocalRunTerminationBoundaryState.HasUnknownWorkForHost(this, out var boundaryFailure);
        boundaryUnknown |= NativeLocalRunLaunchCoordinator.HasUnknownWorkForHost(this);
        boundaryUnknown |= _composition?.Unknown == true;
        boundaryUnknown |= Interlocked.Read(ref _claimObservationFaults) != 0;
        return new(_allInstalled, _accepting && !_stopRequested && !_unknown && !_handlerFaulted && retirement.CleanupUnknown == 0 && !boundaryUnknown, _stopRequested, _stopped, _unknown || _handlerWorkUnknown || retirement.CleanupUnknown != 0 || boundaryUnknown,
            _handlerWorkPending || retirement.Pending != 0 || NativeLocalRunTerminationBoundaryState.HasPendingWorkForHost(this)
                || NativeLocalRunLaunchCoordinator.HasPendingWorkForHost(this), _handlerWorkUnknown || retirement.CleanupUnknown != 0 || boundaryUnknown,
            Sessions.UnretiredSessionCount, _bindings.Count(binding => binding is not null),
            _failure ?? boundaryFailure ?? (_composition?.Unknown == true ? _composition.Failure : null),
            Array.AsReadOnly(_bindings.Where(binding => binding is not null).Select(binding => binding!.Read()).ToArray()));
    }
}
