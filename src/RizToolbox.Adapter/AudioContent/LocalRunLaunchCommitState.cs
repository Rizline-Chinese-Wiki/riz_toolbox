using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal enum LocalRunLaunchCommitPhase { Held, ControllerBound, Initializing, Initialized, CommittedReady, Unknown }
internal sealed record LocalRunLaunchCommitSnapshot(LocalRunSessionIdentity Identity, LocalRunLaunchCommitPhase Phase,
    long Controller, long Information, long Progress, bool NativeAttempted, bool ActivationAttempted,
    bool EarlyTerminal, string? Failure, bool CoroutineCancellationPending = false);

/// <summary>One immutable readiness publication shared by the existing queue and graph
/// reservations. This is infrastructure ownership, never evidence of a RunStarted event,
/// successful Awake, settings initialization, or permission to activate a game object.</summary>
internal sealed class LocalRunLaunchCommitState
{
    private const int MaximumFailureLength = 768;
    private sealed record LaunchFailure(string Message, bool NativeObservationFault);

    private readonly IMainThreadDispatcher _main;
    private LocalRunLaunchCommitSnapshot _snapshot;
    private LocalRunLaunchCommitSnapshot? _preparedReady;
    private LocalRunTerminationHandler? _handler;
    private NativeLocalRunTerminationBoundaryState? _bridge;
    private NativeLocalResultCapture? _capture;
    private NativeLocalRunLaunchReservation? _coordinator;
    private NativeInactiveControllerParameterBinding? _parameterBinding;
    internal NativeInactiveControllerParameterBinding? ParameterBinding { get { _main.VerifyAccess(); return _parameterBinding; } }
    private bool _preparing;
    private long _observedTerminalReceiver;
    private LaunchFailure? _firstFailure;
    private bool HasNativeObservationFault => Volatile.Read(ref _firstFailure)?.NativeObservationFault == true;
    private int _coroutineCancellationPending;
    private readonly object _cancellationRequestGate = new();
    private long _cancellationRequestVersion;
    internal long CoroutineCancellationRequestVersion => Interlocked.Read(ref _cancellationRequestVersion);
    private bool CancellationPreventsActivation => CoroutineCancellationRequestVersion != 0;
    internal bool CoroutineCancellationPending => Volatile.Read(ref _coroutineCancellationPending) != 0;
    internal LocalRunSessionRegistry Registry { get; }
    internal LocalRunSessionRegistry.SessionHandle Session => Claim.Session;
    internal LocalRunSessionRegistry.CommitClaim Claim { get; }
    internal LocalRunSessionIdentity Identity { get; }
    internal IDisposable OwnedBundle { get; }
    internal long MetadataIdentity { get; }
    internal bool IsCommittedReady => Volatile.Read(ref _snapshot).Phase == LocalRunLaunchCommitPhase.CommittedReady
        && Interlocked.Read(ref _observedTerminalReceiver) == 0 && !HasNativeObservationFault
        && !CancellationPreventsActivation;
    internal bool HasNativeAttempt
    {
        get { _main.VerifyAccess(); return _snapshot.NativeAttempted || Interlocked.Read(ref _observedTerminalReceiver) != 0
            || HasNativeObservationFault || CancellationPreventsActivation || Registry.Read(Session).ControllerIdentity != 0; }
    }
    internal bool CanCancelPureReservation
    {
        get
        {
            _main.VerifyAccess(); var current = Registry.Read(Session); var value = _snapshot;
            return !_preparing && !CancellationPreventsActivation && Interlocked.Read(ref _observedTerminalReceiver) == 0 && !HasNativeObservationFault
                && !value.NativeAttempted && !value.ActivationAttempted && !value.EarlyTerminal
                && value.Phase == LocalRunLaunchCommitPhase.Held && value.Controller == 0
                && current.Identity == Identity && current.MetadataIdentity == MetadataIdentity && current.ControllerIdentity == 0
                && current.Phase == LocalRunSessionPhase.PendingCommit
                && current.ResultSceneIdentity == 0 && current.Result is null && !current.ResultCallClaimed
                && !current.CleanupAttempted && !current.ProtectionRetired;
        }
    }
    internal LocalRunLaunchCommitState(IMainThreadDispatcher main, LocalRunSessionRegistry registry,
        LocalRunSessionRegistry.CommitClaim claim, IDisposable ownedBundle)
    {
        ArgumentNullException.ThrowIfNull(main); main.VerifyAccess(); ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(claim); ArgumentNullException.ThrowIfNull(ownedBundle);
        var current = registry.Read(claim.Session);
        registry.VerifyPendingBundleClaim(claim, current.MetadataIdentity, ownedBundle);
        if (current.ControllerIdentity != 0 || current.ResultSceneIdentity != 0 || current.Result is not null
            || current.ResultCallClaimed || current.ReturnRequested || current.ProtectionRetired || current.CleanupAttempted)
            throw new InvalidOperationException("Launch reservations must precede controller publication and any result/return transaction.");
        (_main, Registry, Claim, OwnedBundle, Identity, MetadataIdentity) = (main, registry, claim, ownedBundle, current.Identity, current.MetadataIdentity);
        _snapshot = new(Identity, LocalRunLaunchCommitPhase.Held, 0, 0, 0, false, false, false, null);
    }
    internal LocalRunLaunchCommitSnapshot Read()
    {
        var value = Volatile.Read(ref _snapshot); var observed = Interlocked.Read(ref _observedTerminalReceiver);
        if (CoroutineCancellationPending) value = value with { CoroutineCancellationPending = true };
        if (Volatile.Read(ref _firstFailure) is { NativeObservationFault: true } fault)
            return value with { Phase = LocalRunLaunchCommitPhase.Unknown, Failure = fault.Message };
        return observed != 0 && !value.EarlyTerminal ? value with { EarlyTerminal = true, NativeAttempted = true,
            Controller = value.Controller == 0 ? observed : value.Controller } : value;
    }
    /// <summary>Called only after the host's root-free classifier has established the
    /// exact session/controller role. This records worker or pre-ready native claims even
    /// though those entries must never run a main-thread transaction handler.</summary>
    internal void ObserveTerminalClaimFromAnyThread(long receiver)
    {
        if (receiver == 0) return;
        var current = Volatile.Read(ref _snapshot);
        if (current.Phase == LocalRunLaunchCommitPhase.CommittedReady && _main.IsMainThread) return;
        Interlocked.CompareExchange(ref _observedTerminalReceiver, receiver, 0);
    }
    internal void ObserveNativeFaultFromAnyThread(string failure) => RememberFailure(failure, nativeObservationFault: true);
    internal void ObserveCoroutineCancellationRequestFromAnyThread()
    {
        lock (_cancellationRequestGate)
        {
            // Never let an old main-thread receipt clear a newly observed
            // worker request. This lock contains managed scalar updates only.
            _cancellationRequestVersion = checked(_cancellationRequestVersion + 1);
            Volatile.Write(ref _coroutineCancellationPending, 1);
        }
    }
    internal bool TryConsumeCancellationCompletion(NativeCoroutineCancellationCompletionReceipt receipt)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(receipt);
        if (!receipt.ValidateForLaunch(this)) return false;
        lock (_cancellationRequestGate)
        {
            if (_cancellationRequestVersion != receipt.RequestVersion || !receipt.MatchesLaunchScalar(this)) return false;
            // This retires one cancellation hold only. No Ready, activation,
            // session state or independent failure/terminal flag is restored.
            Volatile.Write(ref _coroutineCancellationPending, 0); return true;
        }
    }
    internal void AttachCoordinator(NativeLocalRunLaunchReservation coordinator)
    {
        VerifyPendingOwnership(Registry); ArgumentNullException.ThrowIfNull(coordinator);
        if (_coordinator is not null) throw new InvalidOperationException("A launch has only one reservation coordinator.");
        _coordinator = coordinator;
    }
    internal void VerifyPendingOwnership(LocalRunSessionRegistry registry)
    {
        _main.VerifyAccess();
        if (!ReferenceEquals(Registry, registry)) throw new InvalidOperationException("The launch belongs to another registry.");
        registry.VerifyPendingBundleClaim(Claim, MetadataIdentity, OwnedBundle);
        var current = registry.Read(Session);
        if (current.Identity != Identity || current.ReturnRequested || current.ProtectionRetired || current.CleanupAttempted
            || HasNativeObservationFault
            || _snapshot.Phase is LocalRunLaunchCommitPhase.Unknown or LocalRunLaunchCommitPhase.CommittedReady)
            throw new InvalidOperationException("The exact launch is no longer a usable pending reservation.");
    }
    internal void AttachHandler(LocalRunTerminationHandler handler)
    {
        VerifyPendingOwnership(Registry); ArgumentNullException.ThrowIfNull(handler);
        if (_handler is not null) throw new InvalidOperationException("The launch already owns a termination queue reservation.");
        _handler = handler;
    }
    internal void AttachBridge(NativeLocalRunTerminationBoundaryState bridge)
    {
        VerifyPendingOwnership(Registry); ArgumentNullException.ThrowIfNull(bridge);
        if (_bridge is not null) throw new InvalidOperationException("The launch already owns a native graph reservation.");
        _bridge = bridge;
    }
    internal void AttachCapture(NativeLocalResultCapture capture)
    {
        VerifyPendingOwnership(Registry); ArgumentNullException.ThrowIfNull(capture);
        if (_capture is not null || !capture.IsPendingReservationFor(Registry, Claim, OwnedBundle))
            throw new InvalidOperationException("The exact launch requires its own previously reserved actual capture quota.");
        _capture = capture;
    }
    internal void AttachParameterBinding(NativeInactiveControllerParameterBinding binding)
    {
        VerifyPendingOwnership(Registry); ArgumentNullException.ThrowIfNull(binding);
        if (_parameterBinding is not null || _snapshot.ActivationAttempted || _snapshot.Controller == 0
            || _snapshot.EarlyTerminal || Interlocked.Read(ref _observedTerminalReceiver) != 0
            || !binding.IsBoundFor(Registry, Claim, OwnedBundle, binding.ControllerResponsibilityForBinding)
            || binding.SetterBaseline.Controller != _snapshot.Controller
            || Registry.Read(Session).ControllerIdentity != _snapshot.Controller)
            throw new InvalidOperationException("The exact completed inactive setter receipt must be adopted once before activation.");
        _parameterBinding = binding;
    }
    internal void NoteNativeAttempt()
    {
        VerifyPendingOwnership(Registry);
        if (_snapshot.EarlyTerminal || Interlocked.Read(ref _observedTerminalReceiver) != 0
            || _handler is null || !_handler.HasPendingReservation(this) || _bridge is null || !_bridge.HasPendingReservation(this)
            || !_bridge.IsReadyForHost(Registry)
            || _capture is null || !_capture.IsPendingReservationFor(Registry, Claim, OwnedBundle))
            throw new InvalidOperationException("All three real responsibility slots must be reserved before native launch work.");
        Volatile.Write(ref _snapshot, _snapshot with { NativeAttempted = true });
    }
    internal void NoteControllerBound(long exactController)
    {
        VerifyPendingOwnership(Registry); var current = Registry.Read(Session);
        if (exactController == 0 || current.ControllerIdentity != exactController
            || _snapshot.Controller != 0 && _snapshot.Controller != exactController
            || !ReferenceEquals(Registry.FindProtectedSession(LocalBoundaryKind.ControllerLevelOver, exactController), Session))
            throw new InvalidOperationException("The pending controller must have this exact registry role before adoption.");
        Volatile.Write(ref _snapshot, _snapshot with { Controller = exactController, NativeAttempted = true,
            Phase = _snapshot.Phase == LocalRunLaunchCommitPhase.Held ? LocalRunLaunchCommitPhase.ControllerBound : _snapshot.Phase });
    }
    internal void NoteActivationAttempt()
    {
        VerifyPendingOwnership(Registry);
        if (CancellationPreventsActivation || _snapshot.Controller == 0 || _snapshot.ActivationAttempted || _snapshot.EarlyTerminal || Interlocked.Read(ref _observedTerminalReceiver) != 0
            || _handler is null || !_handler.IsPendingPrepared(this) || _bridge is null || !_bridge.HasPendingController(this)
            || !_bridge.IsReadyForHost(Registry)
            || _capture is null || !_capture.IsPendingReservationFor(Registry, Claim, OwnedBundle))
            throw new InvalidOperationException("Activation accounting requires all pre-reserved exact controller responsibility and is once-only.");
        // The actual activation API remains owned by its native coordinator; this flag only
        // prevents an attempted active launch from using the pure/no-native cancellation path.
        Volatile.Write(ref _snapshot, _snapshot with { NativeAttempted = true, ActivationAttempted = true });
    }
    internal void RecordEarlyTerminal(long receiver)
    {
        _main.VerifyAccess(); var current = Registry.Read(Session);
        if (receiver == 0 || current.Identity != Identity || current.ControllerIdentity != receiver
            || !ReferenceEquals(Registry.FindProtectedSession(LocalBoundaryKind.ControllerLevelOver, receiver), Session))
            throw new InvalidOperationException("An early terminal request must name this launch's exact protected controller.");
        _preparedReady = null;
        Interlocked.CompareExchange(ref _observedTerminalReceiver, receiver, 0);
        Volatile.Write(ref _snapshot, _snapshot with { Controller = receiver, NativeAttempted = true, EarlyTerminal = true });
    }
    internal void NoteInitializingGraph(NativeLocalRunTerminationBoundaryState bridge, long information, long progress)
    {
        VerifyPendingOwnership(Registry);
        if (!ReferenceEquals(_bridge, bridge) || _snapshot.Controller == 0 || !_snapshot.ActivationAttempted || _snapshot.EarlyTerminal || Interlocked.Read(ref _observedTerminalReceiver) != 0
            || information == 0 || progress == 0 || information == progress || _snapshot.Controller == information || _snapshot.Controller == progress
            || _snapshot.Information != 0 || _snapshot.Progress != 0)
            throw new InvalidOperationException("Only the adopted initialization bridge may publish one actual initializing graph.");
        Volatile.Write(ref _snapshot, _snapshot with { Phase = LocalRunLaunchCommitPhase.Initializing, Information = information, Progress = progress });
    }
    internal void NoteInitializedGraph(NativeLocalRunTerminationBoundaryState bridge)
    {
        VerifyPendingOwnership(Registry);
        if (!ReferenceEquals(_bridge, bridge) || _snapshot.Phase != LocalRunLaunchCommitPhase.Initializing || _snapshot.EarlyTerminal || Interlocked.Read(ref _observedTerminalReceiver) != 0)
            throw new InvalidOperationException("The full native graph must be verified after the actual initialization returns.");
        Volatile.Write(ref _snapshot, _snapshot with { Phase = LocalRunLaunchCommitPhase.Initialized });
    }
    internal void PrepareCommit()
    {
        VerifyPendingOwnership(Registry);
        if (CancellationPreventsActivation || _preparing || _preparedReady is not null || _snapshot.Phase != LocalRunLaunchCommitPhase.Initialized || _snapshot.EarlyTerminal || Interlocked.Read(ref _observedTerminalReceiver) != 0
            || !_snapshot.ActivationAttempted || _handler is null || !_handler.IsPendingPrepared(this)
            || _bridge is null || !_bridge.IsPendingInitialized(this) || _capture is null
            || !_capture.IsPendingReservationFor(Registry, Claim, OwnedBundle))
            throw new InvalidOperationException("Commit requires the same fully initialized queue, graph and reserved actual-capture responsibility.");
        _preparedReady = _snapshot with { Phase = LocalRunLaunchCommitPhase.CommittedReady };
    }
    internal void CommitPrepared()
    {
        VerifyPendingOwnership(Registry);
        var ready = _preparedReady ?? throw new InvalidOperationException("The shared ready publication must be allocated before commit.");
        if (CancellationPreventsActivation || _preparing || _snapshot.EarlyTerminal || Interlocked.Read(ref _observedTerminalReceiver) != 0 || _snapshot.Phase != LocalRunLaunchCommitPhase.Initialized)
            throw new InvalidOperationException("An unknown or terminal pending launch cannot be published ready.");
        _preparing = true;
        try
        {
            if (_handler is null || !_handler.IsPendingPrepared(this) || _bridge is null || !_bridge.IsPendingInitialized(this)
                || _capture is null || !_capture.IsPendingReservationFor(Registry, Claim, OwnedBundle)
                || CancellationPreventsActivation || !ReferenceEquals(_preparedReady, ready) || _snapshot.EarlyTerminal || Interlocked.Read(ref _observedTerminalReceiver) != 0
                || _snapshot.Phase != LocalRunLaunchCommitPhase.Initialized)
                throw new InvalidOperationException("A prepared launch lost current participant responsibility before commit.");
            // No native calls, participant callbacks or new publication allocations occur
            // between the actual registry transition and this single shared ready token.
            Registry.Commit(Claim);
            Volatile.Write(ref _snapshot, ready);
            _preparedReady = null;
            if (CancellationPreventsActivation || Interlocked.Read(ref _observedTerminalReceiver) != 0 || HasNativeObservationFault)
            {
                MarkUnknown("A native terminal claim raced the shared commit publication; ownership stays held.");
                throw new InvalidOperationException("The committed session cannot be published usable after a concurrent early terminal claim.");
            }
        }
        catch { _preparedReady = null; throw; }
        finally { _preparing = false; }
    }
    internal void MarkUnknown(string failure)
    {
        _main.VerifyAccess(); _preparedReady = null;
        var first = RememberFailure(failure, nativeObservationFault: false);
        Volatile.Write(ref _snapshot, _snapshot with { Phase = LocalRunLaunchCommitPhase.Unknown, Failure = first });
    }

    private string RememberFailure(string failure, bool nativeObservationFault)
    {
        var first = Volatile.Read(ref _firstFailure);
        if (first is null)
        {
            if (string.IsNullOrWhiteSpace(failure)) failure = "Unconfirmed launch responsibility.";
            if (failure.Length > MaximumFailureLength) failure = failure[..MaximumFailureLength];
            var observed = new LaunchFailure(failure, nativeObservationFault);
            first = Interlocked.CompareExchange(ref _firstFailure, observed, null) ?? observed;
        }
        // An earlier main-thread failure keeps its message when a native callback records uncertainty.
        if (nativeObservationFault && !first.NativeObservationFault)
            Interlocked.CompareExchange(ref _firstFailure, first with { NativeObservationFault = true }, first);
        return first.Message;
    }
}
