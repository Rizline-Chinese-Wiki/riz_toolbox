using RizToolbox.Runtime;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

/// <summary>Host observations of the actual Unity frame and native entry/exit accounting.
/// NativeBoundaryStateKnown must include the exact Progress.Update around context and all
/// related native boundaries, including worker entries. A managed handler depth is insufficient.</summary>
internal sealed record LocalRunTerminationBoundarySnapshot(bool AllFiveGatesInstalled, long Frame,
    bool NativeBoundaryStateKnown, int InFlightNativeBoundaries);
internal interface ILocalRunTerminationBoundaryState
{
    bool IsReadyForHost(LocalRunSessionRegistry registry) => false;
    // Called before reading a frame or publishing a queued terminal request. The host bridge
    // must mark the exact current Progress context, or explicitly verify its absence.
    bool MarkTerminal(LocalRunSessionRegistry.SessionHandle session, long exactController);
    LocalRunTerminationBoundarySnapshot Read(LocalRunSessionRegistry.SessionHandle session, long exactController);
}

/// <summary>Host-owned transaction, never a provider callback. Create must publish a failed
/// NativeLocalResultCapture through its exception. Retirement is called once after the
/// registry has verified every old native consumer and completed its own cleanup.</summary>
internal interface ILocalRunTerminationCaptureFactory
{
    long ControllerIdentity { get; }
    NativeLocalResultCapture Create(LocalRunSessionRegistry registry, LocalRunSessionRegistry.SessionHandle session);
    void ReleaseAfterConsumersEnded(LocalRunSessionRegistry registry, LocalRunSessionRegistry.SessionHandle session);
}
internal interface ILocalRunTerminationResultPresentation : IDisposable
{
    // This includes UI callbacks and any retained result consumers; presentation alone is
    // never consumer-end evidence and never authorizes registry.Close.
    bool ConsumersEnded { get; }
}
internal interface ILocalRunTerminationResultSink
{
    // Publish exactly once synchronously before performing any fallible UI work which may
    // leave a view/callback alive. An exception keeps the entire request owned without retry.
    void Present(LocalRunSessionRegistry.SessionHandle session, LocalRunCapturedResult result,
        Action<ILocalRunTerminationResultPresentation> publish);
}
internal interface ILocalRunTerminationCommands
{
    // Implement the explicit host ResultStart/Retry/Back transactions. Returning normally
    // is the host command's responsibility; this layer never supplies a default success.
    void Execute(LocalBoundaryKind boundary, LocalRunSessionRegistry.SessionHandle session, long exactReceiver);
}
internal sealed record LocalRunTerminationWorkSnapshot(LocalRunSessionIdentity Identity, bool Queued,
    long? EnqueuedFrame, bool CaptureAttempted, bool ResultCached, bool PresentationPublished,
    bool Unknown, string? Failure, bool PendingReservation = false, bool Held = false,
    bool ControllerAttached = false, bool EarlyTerminal = false);

/// <summary>Fixed-capacity process-retained termination queue. LevelOver does no result
/// capture; only a later real frame with known drained native boundaries may invoke the
/// actual ControllerOver once. Provider/module withdrawal never cancels submitted work.
/// This component deliberately neither begins return nor claims native consumer retirement.</summary>
internal sealed class LocalRunTerminationHandler : ILocalRunBoundaryHandler, INativeLocalActiveConsumerClosureSource
{
    internal const int MaximumProcessWork = 32;
    private static readonly object ProcessGate = new();
    private static readonly HashSet<Work> ProcessOwners = new();
    private readonly IMainThreadDispatcher _main;
    private readonly LocalRunSessionRegistry _registry;
    private readonly ILocalRunTerminationBoundaryState _boundaries;
    private readonly ILocalRunTerminationResultSink? _sink;
    private readonly ILocalRunTerminationCommands? _commands;
    private readonly Work?[] _work = new Work?[MaximumProcessWork];
    private bool _busy;
    private readonly Dictionary<LocalRunSessionRegistry.SessionHandle, LocalRunLaunchCommitState> _retiredActiveLaunches = new();
    private sealed class Work
    {
        // Retaining the work also retains its pump and every submitted host callback.
        internal LocalRunTerminationHandler Owner = null!;
        internal LocalRunSessionRegistry.SessionHandle Session = null!;
        internal LocalRunSessionIdentity Identity = null!;
        internal LocalRunLaunchCommitState? Launch;
        internal long Controller;
        internal ILocalRunTerminationCaptureFactory? Factory;
        internal NativeLocalResultCapture? Capture;
        internal LocalRunCapturedResult? Result;
        internal ILocalRunTerminationResultPresentation? Presentation;
        internal bool Queued, CaptureAttempted, PresentAttempted, RetirementAttempted, Unknown, EarlyTerminal;
        internal long EnqueuedFrame;
        internal string? Failure;
    }
    internal LocalRunTerminationHandler(IMainThreadDispatcher main, LocalRunSessionRegistry registry,
        ILocalRunTerminationBoundaryState boundaries, ILocalRunTerminationResultSink? sink,
        ILocalRunTerminationCommands? commands)
    {
        ArgumentNullException.ThrowIfNull(main); main.VerifyAccess();
        ArgumentNullException.ThrowIfNull(registry); ArgumentNullException.ThrowIfNull(boundaries);
        (_main, _registry, _boundaries, _sink, _commands) = (main, registry, boundaries, sink, commands);
    }
    internal static int RetainedProcessWorkCount { get { lock (ProcessGate) return ProcessOwners.Count; } }
    public bool HasPendingWork
    {
        get { _main.VerifyAccess(); return _work.Any(item => item is not null); }
    }
    public void ValidateHost(LocalRunSessionRegistry registry)
    {
        Guard();
        if (!ReferenceEquals(_registry, registry))
            throw new InvalidOperationException("LOCAL admission requires this exact session registry.");
        if (_sink is null)
            throw new InvalidOperationException("LOCAL admission requires an explicit result sink.");
        if (_commands is null)
            throw new InvalidOperationException("LOCAL admission requires explicit host commands.");
        if (!_boundaries.IsReadyForHost(registry))
            throw new InvalidOperationException("LOCAL admission requires verified native Progress/Timeline/Judge bridge readiness for this exact registry.");
    }
    internal bool UsesBoundaryState(ILocalRunTerminationBoundaryState boundary)
    { _main.VerifyAccess(); return ReferenceEquals(_boundaries, boundary); }
    /// <summary>Pure any-thread observation invoked by the sealed controller classifier,
    /// before a worker claim can be suppressed without reaching main-thread Handle.</summary>
    public void ObserveControllerTerminalClaim(LocalRunSessionRegistry.SessionHandle session, long receiver)
    {
        for (var index = 0; index < _work.Length; index++)
        {
            var entry = Volatile.Read(ref _work[index]);
            if (entry is null || !ReferenceEquals(entry.Session, session)) continue;
            var launch = entry.Launch;
            if (launch is not null) launch.ObserveTerminalClaimFromAnyThread(receiver);
            return;
        }
    }

    /// <summary>Reserves the existing bounded work slot before clone/activation. The
    /// shared launch state owns readiness; a reservation does not authorize native capture.</summary>
    internal void ReservePending(LocalRunLaunchCommitState launch)
    {
        Guard(); ArgumentNullException.ThrowIfNull(launch); _busy = true;
        try
        {
            launch.VerifyPendingOwnership(_registry);
            if (_sink is null || _commands is null || !_boundaries.IsReadyForHost(_registry))
                throw new InvalidOperationException("Pending termination requires actual host integration, result sink and commands before clone.");
            var current = _registry.Read(launch.Session);
            if (current.Identity != launch.Identity || current.Phase != LocalRunSessionPhase.PendingCommit
                || current.ControllerIdentity != 0 || current.ReturnRequested || current.ResultCallClaimed || current.ProtectionRetired
                || launch.HasNativeAttempt)
                throw new InvalidOperationException("Termination work must be reserved for the exact empty pending launch before native attempts.");
            var slot = Array.FindIndex(_work, item => item is null);
            if (slot < 0) throw new InvalidOperationException("The bounded LOCAL termination queue is full.");
            var entry = new Work { Owner = this, Session = launch.Session, Identity = launch.Identity, Launch = launch };
            lock (ProcessGate)
            {
                if (ProcessOwners.Count >= MaximumProcessWork || ProcessOwners.Any(item => ReferenceEquals(item.Session, launch.Session)))
                    throw new InvalidOperationException("The exact launch already owns termination work or the process slots are exhausted.");
                ProcessOwners.EnsureCapacity(ProcessOwners.Count + 1);
                ProcessOwners.Add(entry); Volatile.Write(ref _work[slot], entry);
            }
            try { launch.AttachHandler(this); }
            catch (Exception error) { Fail(entry, error); throw; }
        }
        finally { _busy = false; }
    }

    /// <summary>Adopts the controller's independently owned factory only on success.
    /// Registry publication and the shared launch claim must identify this exact receiver.</summary>
    internal void AttachPendingController(LocalRunLaunchCommitState launch, ILocalRunTerminationCaptureFactory ownedFactory)
    {
        Guard(); ArgumentNullException.ThrowIfNull(launch); ArgumentNullException.ThrowIfNull(ownedFactory); _busy = true;
        try
        {
            var entry = Find(launch.Session);
            if (!ReferenceEquals(entry.Launch, launch) || entry.Unknown || entry.Factory is not null || entry.Controller != 0)
                throw new InvalidOperationException("Only the existing exact pending work may adopt its controller factory once.");
            launch.VerifyPendingOwnership(_registry); var current = _registry.Read(launch.Session);
            if (current.Identity != entry.Identity || current.Phase != LocalRunSessionPhase.PendingCommit || current.ReturnRequested
                || current.ProtectionRetired || current.ResultCallClaimed || current.ControllerIdentity == 0
                || ownedFactory.ControllerIdentity != current.ControllerIdentity
                || !ReferenceEquals(_registry.FindProtectedSession(LocalBoundaryKind.ControllerLevelOver, current.ControllerIdentity), launch.Session))
                throw new InvalidOperationException("Pending capture factory must belong to this claim's exact published controller.");
            lock (ProcessGate)
            {
                if (ProcessOwners.Any(item => !ReferenceEquals(item, entry) && ReferenceEquals(item.Factory, ownedFactory)))
                    throw new InvalidOperationException("The pending capture factory is already owned by another termination slot.");
                launch.NoteControllerBound(current.ControllerIdentity);
                entry.Controller = current.ControllerIdentity; entry.Factory = ownedFactory;
            }
        }
        finally { _busy = false; }
    }
    internal bool IsPendingPrepared(LocalRunLaunchCommitState launch)
    {
        Guard(); ArgumentNullException.ThrowIfNull(launch);
        var entry = _work.FirstOrDefault(item => item is not null && ReferenceEquals(item.Launch, launch));
        if (entry is null || entry.Unknown || entry.EarlyTerminal || entry.Controller == 0 || entry.Factory is null
            || entry.CaptureAttempted || entry.PresentAttempted || entry.Queued || !ReferenceEquals(launch.Registry, _registry)
            || _sink is null || _commands is null || !_boundaries.IsReadyForHost(_registry)) return false;
        var current = _registry.Read(entry.Session);
        return current.Identity == entry.Identity && current.Phase == LocalRunSessionPhase.PendingCommit
            && current.ControllerIdentity == entry.Controller && !current.ReturnRequested && !current.ResultCallClaimed && !current.ProtectionRetired;
    }
    internal bool HasPendingReservation(LocalRunLaunchCommitState launch)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(launch);
        var entry = _work.FirstOrDefault(item => item is not null && ReferenceEquals(item.Launch, launch));
        if (entry is null || entry.Unknown || !ReferenceEquals(launch.Registry, _registry)
            || launch.Read().Phase == LocalRunLaunchCommitPhase.Unknown) return false;
        var current = _registry.Read(entry.Session);
        return current.Identity == entry.Identity && current.Identity == launch.Identity && current.MetadataIdentity == launch.MetadataIdentity
            && current.Phase == LocalRunSessionPhase.PendingCommit && !current.ReturnRequested && !current.ProtectionRetired && !current.CleanupAttempted;
    }
    internal void CompletePending(LocalRunLaunchCommitState launch)
    {
        Guard(); ArgumentNullException.ThrowIfNull(launch); var entry = Find(launch.Session);
        var current = _registry.Read(launch.Session);
        if (!ReferenceEquals(entry.Launch, launch) || !launch.IsCommittedReady || entry.Unknown || entry.EarlyTerminal
            || entry.Factory is null || entry.Controller == 0 || current.Identity != entry.Identity
            || current.ControllerIdentity != entry.Controller || current.ProtectionRetired)
            throw new InvalidOperationException("Termination completion requires the same shared committed-ready launch and already owned exact controller factory.");
        // No local ready publication: every Handle/Tick continues to read the one shared state.
    }
    internal bool CancelPurePending(LocalRunLaunchCommitState launch)
    {
        Guard(); ArgumentNullException.ThrowIfNull(launch); var entry = Find(launch.Session);
        if (!ReferenceEquals(entry.Launch, launch) || !launch.CanCancelPureReservation || entry.Unknown || entry.EarlyTerminal
            || entry.Controller != 0 || entry.Factory is not null || entry.Capture is not null || entry.Presentation is not null
            || entry.Queued || entry.CaptureAttempted || _registry.Read(entry.Session).ControllerIdentity != 0) return false;
        lock (ProcessGate) { ProcessOwners.Remove(entry); Volatile.Write(ref _work[Array.IndexOf(_work, entry)], null); }
        return true;
    }

    /// <summary>Transfers the factory only on successful registration. No factory/native
    /// call occurs before the fixed responsibility slot has been published.</summary>
    internal void RegisterCommitted(LocalRunSessionRegistry.SessionHandle session, ILocalRunTerminationCaptureFactory ownedFactory)
    {
        Guard(); ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(ownedFactory);
        _busy = true;
        try
        {
            if (_sink is null || _commands is null)
                throw new InvalidOperationException("LOCAL termination admission requires an actual result sink and explicit host commands.");
            if (!_boundaries.IsReadyForHost(_registry))
                throw new InvalidOperationException("LOCAL termination native host integration is not ready.");
            var current = _registry.Read(session);
            if (current.Phase != LocalRunSessionPhase.Committed || current.ReturnRequested || current.ResultCallClaimed
                || current.ProtectionRetired || current.ControllerIdentity == 0 || ownedFactory.ControllerIdentity != current.ControllerIdentity
                || !ReferenceEquals(_registry.FindProtectedSession(LocalBoundaryKind.ControllerLevelOver, current.ControllerIdentity), session))
                throw new InvalidOperationException("Termination admission requires the uncaptured committed exact LOCAL session and controller.");
            var boundary = _boundaries.Read(session, current.ControllerIdentity);
            RequireKnown(boundary);
            if (!boundary.AllFiveGatesInstalled || boundary.InFlightNativeBoundaries != 0)
                throw new InvalidOperationException("Termination responsibility must be registered with all five gates ready and actual native boundaries drained.");
            var slot = Array.FindIndex(_work, item => item is null);
            if (slot < 0) throw new InvalidOperationException("The bounded LOCAL termination queue is full.");
            var entry = new Work { Owner = this, Session = session, Identity = current.Identity,
                Controller = current.ControllerIdentity, Factory = ownedFactory };
            lock (ProcessGate)
            {
                if (ProcessOwners.Count >= MaximumProcessWork || ProcessOwners.Any(item => ReferenceEquals(item.Session, session)
                    || ReferenceEquals(item.Factory, ownedFactory)))
                    throw new InvalidOperationException("This run/factory is already owned or the 32-slot process responsibility bound is exhausted.");
                ProcessOwners.Add(entry); Volatile.Write(ref _work[slot], entry);
            }
        }
        finally { _busy = false; }
    }

    public void Handle(LocalBoundaryKind boundary, LocalRunSessionRegistry.SessionHandle session, long receiver)
    {
        Guard();
        var entry = Find(session);
        if (entry.Unknown && boundary != LocalBoundaryKind.ControllerLevelOver) return;
        if (!ReferenceEquals(_registry.FindProtectedSession(boundary, receiver), session)
            || boundary == LocalBoundaryKind.ControllerLevelOver && entry.Controller != 0 && receiver != entry.Controller)
            throw new InvalidOperationException("A host termination command requires its exact protected receiver.");
        _busy = true;
        try
        {
            // Cleanup Return for the already-owned exact run remains
            // observable after a historical cancellation blocked new Ready.
            // Queue only; this boundary performs no native return operation.
            if (boundary == LocalBoundaryKind.ResultBack && _commands is NativeLocalAudioContentRuntime composition)
            { composition.Execute(boundary, session, receiver); return; }
            if (boundary == LocalBoundaryKind.ResultBack
                && NativeLocalRunLaunchCoordinator.EnqueueActiveReturn(_registry, session, receiver)) return;
            if (entry.Launch is { } launch && !launch.IsCommittedReady)
            {
                // The sealed native LevelOver claim has already latched its current tail
                // before invoking this handler, even while no Progress graph exists yet.
                // Held requests never read a fake frame, execute commands or capture.
                if (boundary == LocalBoundaryKind.ControllerLevelOver)
                {
                    entry.EarlyTerminal = true; launch.RecordEarlyTerminal(receiver);
                }
                return;
            }
            if (boundary == LocalBoundaryKind.ControllerLevelOver)
            {
                // Mark the real currently executing Progress tail before even reading a
                // frame. Rejected/unknown marking still leaves stock suppressed by the gate.
                if (!_boundaries.MarkTerminal(session, entry.Controller))
                    throw new InvalidOperationException("The exact native terminal context could not be marked.");
                // Repeated LevelOver entries still mark their own current native tail.
                // They never requeue or repeat unknown capture/UI operations.
                if (entry.Unknown || entry.Queued || entry.CaptureAttempted) return;
                var state = _boundaries.Read(session, entry.Controller); RequireKnown(state);
                if (!state.AllFiveGatesInstalled) throw new InvalidOperationException("A terminal claim lost its five installed gates.");
                entry.EnqueuedFrame = state.Frame; entry.Queued = true;
                return;
            }
            if (boundary is not (LocalBoundaryKind.ResultStart or LocalBoundaryKind.ControllerRetry
                or LocalBoundaryKind.ResultRetry or LocalBoundaryKind.ResultBack))
                throw new InvalidOperationException("Unknown LOCAL boundary command.");
            (_commands ?? throw new InvalidOperationException("No explicit host command transaction exists.")).Execute(boundary, session, receiver);
        }
        catch (Exception error) { Fail(entry, error); }
        finally { _busy = false; }
    }

    public void Tick()
    {
        Guard(); _busy = true;
        try
        {
            for (var slot = 0; slot < _work.Length; slot++)
            {
                var entry = _work[slot]; if (entry is null || entry.Unknown) continue;
                try
                {
                    var current = _registry.Read(entry.Session);
                    if (current.Identity != entry.Identity || entry.Controller != 0 && current.ControllerIdentity != entry.Controller)
                        throw new InvalidOperationException("The queued termination session changed its exact identity.");
                    if (current.ProtectionRetired)
                    {
                        if (current.Phase != LocalRunSessionPhase.Closed || !current.CleanupAttempted
                            || string.IsNullOrWhiteSpace(current.ConsumerEndEvidence))
                            throw new InvalidOperationException("Registry retirement still has unknown owned cleanup.");
                        if (entry.Presentation is not null && !entry.Presentation.ConsumersEnded) continue;
                        Retire(slot, entry); continue;
                    }
                    if (current.Phase == LocalRunSessionPhase.CleanupUnknown)
                        throw new InvalidOperationException("This LOCAL run has unknown transaction responsibility.");
                    if (entry.Launch is { } launch && !launch.IsCommittedReady) continue;
                    if (entry.Factory is null || entry.Controller == 0)
                        throw new InvalidOperationException("Ready termination work has no already-adopted exact controller factory.");
                    if (!entry.Queued || entry.CaptureAttempted) continue;
                    var state = _boundaries.Read(entry.Session, entry.Controller); RequireKnown(state);
                    if (!state.AllFiveGatesInstalled) throw new InvalidOperationException("Queued termination lost its five installed gates.");
                    if (state.Frame <= entry.EnqueuedFrame || state.InFlightNativeBoundaries != 0) continue;
                    if (current.Phase != LocalRunSessionPhase.Committed || current.ReturnRequested || current.ResultCallClaimed)
                        throw new InvalidOperationException("Queued capture is no longer an uncaptured committed run.");
                    entry.CaptureAttempted = true;
                    entry.Capture = entry.Factory!.Create(_registry, entry.Session)
                        ?? throw new InvalidOperationException("The native capture factory returned no owned capture.");
                    entry.Result = entry.Capture.Capture()
                        ?? throw new InvalidOperationException("The once-only actual result capture returned no cached result.");
                    var cached = _registry.Read(entry.Session).Result;
                    if (entry.Result.Session != entry.Identity || !ReferenceEquals(entry.Result, cached))
                        throw new InvalidOperationException("Actual result cache is not the exact queued session's result.");
                    entry.PresentAttempted = true;
                    var publishing = true;
                    try
                    {
                        _sink!.Present(entry.Session, entry.Result, presentation => {
                            _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(presentation);
                            if (!publishing || !_busy || !entry.PresentAttempted || entry.Presentation is not null)
                                throw new InvalidOperationException("An owned result presentation must publish exactly once synchronously.");
                            entry.Presentation = presentation;
                        });
                    }
                    finally { publishing = false; }
                    if (entry.Presentation is null) throw new InvalidOperationException("The result sink returned without publishing its owned presentation.");
                }
                catch (NativeLocalResultCaptureException error)
                {
                    // Create can fail after rooting objects. Adopt its published owner in the
                    // already-reserved slot, including when native invocation never started.
                    entry.Capture ??= error.Responsibility; Fail(entry, error);
                }
                catch (Exception error) { Fail(entry, error); }
            }
        }
        finally { _busy = false; }
    }
    private void Retire(int slot, Work entry)
    {
        if (entry.RetirementAttempted) throw new InvalidOperationException("Unconfirmed termination cleanup cannot be retried.");
        entry.RetirementAttempted = true;
        var failures = new List<Exception>(3);
        if (entry.Presentation is { } presentation)
            try { presentation.Dispose(); entry.Presentation = null; } catch (Exception error) { failures.Add(error); }
        if (entry.Capture is { } capture)
            try { capture.Dispose(); entry.Capture = null; } catch (Exception error) { failures.Add(error); }
        if (entry.Factory is { } factory)
            try { factory.ReleaseAfterConsumersEnded(_registry, entry.Session); entry.Factory = null; } catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("Termination ownership cleanup is unconfirmed and will not be repeated.", failures);
        if (entry.Launch is { } exactLaunch) _retiredActiveLaunches.Add(entry.Session, exactLaunch);
        lock (ProcessGate) { ProcessOwners.Remove(entry); Volatile.Write(ref _work[slot], null); }
    }
    public NativeLocalActiveConsumerCheck ReadActiveConsumerClosure(LocalRunLaunchCommitState exactLaunch,
        NativeLocalActiveConsumerObligation obligation)
    {
        if (obligation != NativeLocalActiveConsumerObligation.SceneResultConsumers)
            throw new ArgumentOutOfRangeException(nameof(obligation));
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(exactLaunch);
        var entry = Find(exactLaunch.Session); var current = _registry.Read(exactLaunch.Session);
        if (!ReferenceEquals(exactLaunch.Registry, _registry) || !ReferenceEquals(entry.Launch, exactLaunch)
            || entry.Identity != exactLaunch.Identity || current.Identity != entry.Identity
            || current.ControllerIdentity != entry.Controller || !current.ReturnRequested)
            throw new InvalidOperationException("Result consumer facts belong to another exact retained launch/session.");
        if (_busy || entry.Unknown || entry.RetirementAttempted)
            return new(obligation, NativeLocalActiveConsumerProofState.Unknown, "The exact termination/presentation owner is in-flight or Unknown.");
        if (entry.CaptureAttempted && (entry.Result is null || !ReferenceEquals(entry.Result, current.Result))
            || entry.PresentAttempted && entry.Presentation is null)
            return new(obligation, NativeLocalActiveConsumerProofState.Unknown, "The actual result cache/presentation publication is incomplete.");
        if (entry.Presentation is not null && !entry.Presentation.ConsumersEnded)
            return new(obligation, NativeLocalActiveConsumerProofState.Pending, "The actual retained result presentation still owns UI callbacks or cleanup.");
        var state = _boundaries.Read(entry.Session, entry.Controller); RequireKnown(state);
        if (!state.AllFiveGatesInstalled || state.InFlightNativeBoundaries != 0)
            return new(obligation, NativeLocalActiveConsumerProofState.Pending, "Actual scene/result native boundaries have not drained.");
        return new(obligation, NativeLocalActiveConsumerProofState.Proven,
            "The exact termination result is cached, all actually published UI callbacks ended, and all five retained native gates are drained.");
    }
    internal bool TryWithdrawOwnedPresentation(LocalRunLaunchCommitState exactLaunch)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(exactLaunch);
        var entry = Find(exactLaunch.Session); var current = _registry.Read(exactLaunch.Session);
        if (_busy || entry.Unknown || !ReferenceEquals(entry.Launch, exactLaunch) || current.Identity != exactLaunch.Identity
            || !current.ReturnRequested || current.ProtectionRetired || current.CleanupAttempted)
            return false;
        // An actual explicit return already withdrew future UI admission. A
        // live callback must finish before this once-only presentation disposal.
        if (entry.Presentation is null) return true;
        if (_sink is NativeLocalRunResultSink sink)
        {
            var result = sink.Close(exactLaunch.Session);
            if (!result.Success) return false;
        }
        return entry.Presentation is null || entry.Presentation.ConsumersEnded;
    }
    internal bool HasRetiredActiveConsumers(LocalRunLaunchCommitState exactLaunch)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(exactLaunch);
        if (!ReferenceEquals(exactLaunch.Registry, _registry) || _busy) return false;
        return _retiredActiveLaunches.TryGetValue(exactLaunch.Session, out var actual) && ReferenceEquals(actual, exactLaunch)
            && !_work.Any(row => row is not null && ReferenceEquals(row.Session, exactLaunch.Session));
    }
    internal int RetiredActiveReceiptCount { get { _main.VerifyAccess(); return _retiredActiveLaunches.Count; } }
    internal void ConsumeRetiredActiveLaunch(LocalRunLaunchCommitState exactLaunch,
        NativeLocalRunLaunchCoordinator actualOwner)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(exactLaunch); ArgumentNullException.ThrowIfNull(actualOwner);
        if (_busy || !HasRetiredActiveConsumers(exactLaunch))
            throw new InvalidOperationException("No exact completed termination receipt exists for this active launch.");
        actualOwner.VerifyActiveTerminationReceiptConsumption(this, exactLaunch);
        if (!_retiredActiveLaunches.Remove(exactLaunch.Session))
            throw new InvalidOperationException("The completed termination receipt changed before its one final consumption.");
    }
    internal LocalRunTerminationWorkSnapshot Read(LocalRunSessionRegistry.SessionHandle session)
    {
        _main.VerifyAccess(); var entry = Find(session);
        return new(entry.Identity, entry.Queued, entry.Queued ? entry.EnqueuedFrame : null,
            entry.CaptureAttempted, entry.Result is not null, entry.Presentation is not null, entry.Unknown, entry.Failure,
            entry.Launch is not null, entry.Launch is { } launch && !launch.IsCommittedReady,
            entry.Controller != 0 && entry.Factory is not null, entry.EarlyTerminal);
    }
    private Work Find(LocalRunSessionRegistry.SessionHandle session)
        => _work.FirstOrDefault(item => item is not null && ReferenceEquals(item.Session, session))
            ?? throw new InvalidOperationException("The exact LOCAL run has no registered termination responsibility.");
    private static void RequireKnown(LocalRunTerminationBoundarySnapshot state)
    {
        if (state is null || !state.NativeBoundaryStateKnown || state.InFlightNativeBoundaries < 0 || state.Frame < 0)
            throw new InvalidOperationException("The actual native boundary/frame observation is unavailable or unknown.");
    }
    private static void Fail(Work entry, Exception error)
    {
        entry.Unknown = true; entry.Failure = "LOCAL termination responsibility is unconfirmed; no retry or stock fallback: " + error.GetType().Name;
        try { entry.Launch?.MarkUnknown(entry.Failure); } catch { }
    }
    private void Guard()
    {
        _main.VerifyAccess();
        if (_busy) throw new InvalidOperationException("LOCAL termination worker/reentry cannot capture, command or retire in-flight ownership.");
    }
}
