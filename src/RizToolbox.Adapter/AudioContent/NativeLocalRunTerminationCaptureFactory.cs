using RizToolbox.Runtime;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

/// <summary>Production factory owning an independent controller root transferred by the
/// host. The actual operation is NativeLocalResultCapture.Create/Capture with NativeRuntime;
/// no fake ControllerOver or copied-score shortcut is substituted.</summary>
internal sealed class NativeLocalRunTerminationCaptureFactory : ILocalRunTerminationCaptureFactory
{
    private const int MaximumFactories = 32;
    private static readonly object ProcessGate = new();
    private static readonly HashSet<NativeLocalRunTerminationCaptureFactory> ProcessOwners = new();
    private readonly NativeRuntime _api;
    private readonly IMainThreadDispatcher _main;
    private readonly AdapterBuildProfile _profile;
    private readonly IntPtr _module;
    private readonly LocalRunSessionRegistry _registry;
    private readonly LocalRunSessionRegistry.SessionHandle _session;
    private readonly LocalRunSessionIdentity _identity;
    private LocalRunLaunchCommitState? _launch;
    private NativeLocalResultCapture? _capture;
    private NativeRuntime.RootHandle? _ownedController;
    private bool _createAttempted, _releaseAttempted, _busy, _cleanupUnknown, _attachAttempted;
    public long ControllerIdentity { get; private set; }
    internal bool OwnsControllerRoot(NativeRuntime.RootHandle root)
    { _main.VerifyAccess(); return root is not null && ReferenceEquals(_ownedController, root); }
    internal bool CleanupUnknown { get { _main.VerifyAccess(); return _cleanupUnknown; } }
    internal static int RetainedProcessFactoryCount { get { lock (ProcessGate) return ProcessOwners.Count; } }
    private NativeLocalRunTerminationCaptureFactory(NativeRuntime api, IMainThreadDispatcher main,
        AdapterBuildProfile profile, IntPtr module, LocalRunLaunchCommitState launch, NativeLocalResultCapture capture)
    {
        (_api, _main, _profile, _module, _registry, _session, _identity, _launch, _capture)
            = (api, main, profile, module, launch.Registry, launch.Session, launch.Identity, launch, capture);
    }

    /// <summary>Uses the same factory quota as the legacy constructor. The capture capacity
    /// must already be reserved before any clone/root can be published. Nothing native is read.</summary>
    internal static NativeLocalRunTerminationCaptureFactory ReservePending(NativeRuntime api, IMainThreadDispatcher main,
        AdapterBuildProfile profile, IntPtr module, LocalRunLaunchCommitState launch, NativeLocalResultCapture reservedCapture)
    {
        ArgumentNullException.ThrowIfNull(api); ArgumentNullException.ThrowIfNull(main); main.VerifyAccess();
        ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(launch); ArgumentNullException.ThrowIfNull(reservedCapture);
        launch.VerifyPendingOwnership(launch.Registry);
        if (!launch.CanCancelPureReservation || !reservedCapture.IsPendingReservationFor(launch.Registry, launch.Claim, launch.OwnedBundle))
            throw new InvalidOperationException("A pending factory requires the same fresh pre-native launch and already reserved actual capture.");
        var owner = new NativeLocalRunTerminationCaptureFactory(api, main, profile, module, launch, reservedCapture);
        lock (ProcessGate)
        {
            if (ProcessOwners.Count >= MaximumFactories || ProcessOwners.Any(value => ReferenceEquals(value._launch, launch)
                || ReferenceEquals(value._capture, reservedCapture)))
                throw new InvalidOperationException("Pending factories share the existing 32-owner quota and cannot adopt the same launch/capture twice.");
            ProcessOwners.EnsureCapacity(ProcessOwners.Count + 1); ProcessOwners.Add(owner);
        }
        return owner;
    }

    internal void AttachPendingController(NativeRuntime.RootHandle ownedIndependentControllerRoot, long exactController)
        => AttachPendingControllerCore(ownedIndependentControllerRoot, exactController, null);

    /// <summary>Controlled backend seam. The production attachment above always constructs
    /// the actual NativeRuntime backend and borrows this factory's adopted independent root.</summary>
    internal void AttachPendingControllerBackend(NativeRuntime.RootHandle ownedIndependentControllerRoot, long exactController,
        INativeLocalResultCaptureBackend backend)
    { ArgumentNullException.ThrowIfNull(backend); AttachPendingControllerCore(ownedIndependentControllerRoot, exactController, backend); }

    /// <summary>Pure main-thread ownership preflight before the coordinator adopts a handoff root.
    /// Rejection leaves both the caller's root and this factory unchanged. No Target, Source,
    /// backend method, native call or launch publication occurs here.</summary>
    internal void VerifyPendingControllerHandoff(NativeRuntime.RootHandle ownedIndependentControllerRoot, long exactController)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(ownedIndependentControllerRoot);
        if (_launch is null || _capture is null || _busy || _attachAttempted || _createAttempted || _releaseAttempted
            || _cleanupUnknown || _ownedController is not null || !ReferenceEquals(_launch.Registry, _registry)
            || !ReferenceEquals(_launch.Session, _session) || _launch.Identity != _identity)
            throw new InvalidOperationException("Only the same pending reserved factory may receive one controller handoff.");
        _launch.VerifyPendingOwnership(_registry);
        var current = _registry.Read(_session);
        if (current.Identity != _identity || current.MetadataIdentity != _launch.MetadataIdentity
            || exactController == 0 || current.ControllerIdentity != exactController
            || current.Phase != LocalRunSessionPhase.PendingCommit || current.ReturnRequested || current.ResultCallClaimed
            || current.ResultSceneIdentity != 0 || current.ResultSceneGeneration != 0 || current.Result is not null
            || current.CleanupAttempted || current.ProtectionRetired
            || !_capture.IsPendingReservationFor(_registry, _launch.Claim, _launch.OwnedBundle)
            || !ReferenceEquals(_registry.FindProtectedSession(LocalBoundaryKind.ControllerLevelOver, exactController), _session))
            throw new InvalidOperationException("Controller handoff requires this exact protected pending launch and live reserved capture.");
        lock (ProcessGate)
            if (!ProcessOwners.Contains(this) || ProcessOwners.Any(value => !ReferenceEquals(value, this)
                && ReferenceEquals(value._ownedController, ownedIndependentControllerRoot)))
                throw new InvalidOperationException("The handoff root already belongs to another factory or this reservation is no longer retained.");
    }

    private void AttachPendingControllerCore(NativeRuntime.RootHandle ownedIndependentControllerRoot, long exactController,
        INativeLocalResultCaptureBackend? backend)
    {
        VerifyPendingControllerHandoff(ownedIndependentControllerRoot, exactController);
        var launch = _launch!; var capture = _capture!;
        _attachAttempted = true; _busy = true;
        try
        {
            launch.NoteControllerBound(exactController);
            launch.VerifyPendingOwnership(_registry);
            var current = _registry.Read(_session);
            if (current.Identity != _identity || current.ControllerIdentity != exactController || exactController == 0
                || !ReferenceEquals(_registry.FindProtectedSession(LocalBoundaryKind.ControllerLevelOver, exactController), _session))
                throw new InvalidOperationException("Pending factory controller adoption lost its exact registry role.");
            lock (ProcessGate)
            {
                if (ProcessOwners.Any(value => !ReferenceEquals(value, this) && ReferenceEquals(value._ownedController, ownedIndependentControllerRoot)))
                    throw new InvalidOperationException("Another factory already owns this independent controller root.");
                _ownedController = ownedIndependentControllerRoot; ControllerIdentity = exactController;
            }
            // The root is recorded before this fallible managed attachment, but its Target is
            // still never read. Failure retains the adopted root and cannot become a retry.
            if (backend is null) capture.AttachPending(_api, _profile, _module, _ownedController!);
            else capture.AttachPendingBackend(backend);
        }
        catch (Exception failure)
        {
            _cleanupUnknown = true; launch.MarkUnknown("Pending capture factory attachment is unconfirmed: " + failure.GetType().Name);
            throw;
        }
        finally { _busy = false; }
    }

    internal void CancelPendingFactory()
    {
        _main.VerifyAccess();
        if (_launch is null || _busy || _cleanupUnknown || _attachAttempted || _createAttempted || _releaseAttempted
            || ControllerIdentity != 0 || _ownedController is not null || !_launch.CanCancelPureReservation)
            throw new InvalidOperationException("Only a pure pre-native factory reservation can return its factory slot.");
        _releaseAttempted = true; _capture = null; // Coordinator owns cancellation of the separate capture slot.
        lock (ProcessGate) ProcessOwners.Remove(this);
    }
    internal NativeLocalRunTerminationCaptureFactory(NativeRuntime api, IMainThreadDispatcher main,
        AdapterBuildProfile profile, IntPtr module, LocalRunSessionRegistry registry,
        LocalRunSessionRegistry.SessionHandle session, long exactController, NativeRuntime.RootHandle ownedController)
    {
        ArgumentNullException.ThrowIfNull(api); ArgumentNullException.ThrowIfNull(main); main.VerifyAccess();
        ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(ownedController);
        ArgumentNullException.ThrowIfNull(registry); ArgumentNullException.ThrowIfNull(session);
        if (exactController == 0) throw new ArgumentException("An owned capture factory requires its declared exact controller.", nameof(exactController));
        var current = registry.Read(session);
        if (current.ControllerIdentity != exactController || current.ProtectionRetired || current.CleanupAttempted
            || current.Phase == LocalRunSessionPhase.Closed
            || !ReferenceEquals(registry.FindProtectedSession(LocalBoundaryKind.ControllerLevelOver, exactController), session))
            throw new InvalidOperationException("A native capture factory requires this exact retained, unretired controller session.");
        (_api, _main, _profile, _module, _registry, _session, _identity, ControllerIdentity)
            = (api, main, profile, module, registry, session, current.Identity, exactController);
        // Admission touches only managed exact-session/ownership identities. In particular it must
        // not resolve the caller's root before the native capture's supported-build gate. Capacity
        // and fixed process responsibility are reserved BEFORE adopting the transferred root.
        lock (ProcessGate)
        {
            if (ProcessOwners.Count >= MaximumFactories || ProcessOwners.Any(owner => ReferenceEquals(owner._ownedController, ownedController)))
                throw new InvalidOperationException("Native capture factories exhaust the 32-owner bound or already own this root; caller retains rejected ownership.");
            ProcessOwners.EnsureCapacity(ProcessOwners.Count + 1);
            ProcessOwners.Add(this); _ownedController = ownedController;
        }
    }
    public NativeLocalResultCapture Create(LocalRunSessionRegistry registry, LocalRunSessionRegistry.SessionHandle session)
    {
        _main.VerifyAccess();
        RequireExactSession(registry, session);
        if (_busy || _createAttempted || _releaseAttempted || _cleanupUnknown || _ownedController is null)
            throw new InvalidOperationException("A native termination factory creates once and never repeats an unknown attempt.");
        var current = registry.Read(session);
        if (current.Identity != _identity || current.ControllerIdentity != ControllerIdentity || current.ProtectionRetired || current.CleanupAttempted)
            throw new InvalidOperationException("A native termination factory cannot capture another exact controller.");
        if (_launch is not null && (!_launch.IsCommittedReady || current.Phase != LocalRunSessionPhase.Committed || _capture is null))
            throw new InvalidOperationException("A pending capture factory remains Held until the same launch is committed ready.");
        _createAttempted = true; _busy = true;
        try
        {
            if (_launch is not null) { _capture!.InitializeWhenCommitted(); return _capture; }
            return _capture = NativeLocalResultCapture.Create(_api, _main, _profile, _module, registry, session, _ownedController);
        }
        catch (NativeLocalResultCaptureException failure)
        {
            _capture = failure.Responsibility;
            if (_launch is not null) { _cleanupUnknown = true; _launch.MarkUnknown("Reserved actual capture initialization is unconfirmed."); }
            throw;
        }
        catch
        {
            if (_launch is not null) { _cleanupUnknown = true; _launch.MarkUnknown("Reserved actual capture preparation is unconfirmed."); }
            throw;
        }
        finally { _busy = false; }
    }
    public void ReleaseAfterConsumersEnded(LocalRunSessionRegistry registry, LocalRunSessionRegistry.SessionHandle session)
    {
        _main.VerifyAccess();
        RequireExactSession(registry, session);
        if (_busy || _releaseAttempted) throw new InvalidOperationException("Native factory root cleanup is once-only and cannot reenter an active operation.");
        var current = registry.Read(session);
        if (current.Identity != _identity || ControllerIdentity != 0 && current.ControllerIdentity != ControllerIdentity || current.Phase != LocalRunSessionPhase.Closed
            || !current.ProtectionRetired || !current.CleanupAttempted || string.IsNullOrWhiteSpace(current.ConsumerEndEvidence))
            throw new InvalidOperationException("Native capture factory root requires exact verified consumer retirement.");
        _releaseAttempted = true; _busy = true;
        try
        {
            var failures = new List<Exception>(2);
            if (_capture is { } capture)
                try { capture.Dispose(); _capture = null; } catch (Exception failure) { failures.Add(failure); }
            if (_ownedController is { } controller)
                try { controller.Dispose(); _ownedController = null; } catch (Exception failure) { failures.Add(failure); }
            if (failures.Count != 0)
            {
                _cleanupUnknown = true;
                if (failures.Count == 1) throw failures[0]; // Preserve the legacy single-root exception contract.
                throw new AggregateException("Native factory independently attempted capture/controller cleanup; unknown ownership is never retried.", failures);
            }
            if (_cleanupUnknown) throw new InvalidOperationException("Earlier pending attachment/preparation uncertainty retains factory responsibility and quota.");
            lock (ProcessGate) ProcessOwners.Remove(this);
        }
        catch { _cleanupUnknown = true; throw; }
        finally { _busy = false; }
    }
    private void RequireExactSession(LocalRunSessionRegistry registry, LocalRunSessionRegistry.SessionHandle session)
    {
        if (!ReferenceEquals(_registry, registry) || !ReferenceEquals(_session, session))
            throw new InvalidOperationException("Native capture factory ownership belongs to another exact registry/session handle.");
    }
}
