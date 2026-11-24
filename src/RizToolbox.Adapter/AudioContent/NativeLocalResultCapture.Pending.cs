using RizToolbox.Runtime;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal sealed partial class NativeLocalResultCapture
{
    private LocalRunSessionRegistry.CommitClaim? _pendingClaim;
    private IDisposable? _pendingBundle;
    private bool _nativeAttempted, _initializationAttempted, _initialized;
    internal bool NativeWorkAttempted { get { _main.VerifyAccess(); return _nativeAttempted; } }
    internal bool IsInitialized { get { _main.VerifyAccess(); return _initialized; } }

    /// <summary>Reserves the existing result-capture process quota before clone/activation.
    /// No backend, source observation, root allocation, result claim or native operation occurs.
    /// The adopted bundle remains registry-owned; this reservation only retains its managed identity.</summary>
    internal static NativeLocalResultCapture ReservePending(IMainThreadDispatcher main,
        LocalRunSessionRegistry registry, LocalRunSessionRegistry.CommitClaim claim, IDisposable ownedBundle)
    {
        main.VerifyAccess(); ArgumentNullException.ThrowIfNull(registry); ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(ownedBundle);
        var entry = registry.Read(claim.Session);
        registry.VerifyPendingBundleClaim(claim, entry.MetadataIdentity, ownedBundle);
        if (entry.ControllerIdentity != 0 || entry.ResultSceneIdentity != 0 || entry.ResultSceneGeneration != 0
            || entry.ReturnRequested || entry.ResultCallClaimed || entry.Result is not null || entry.ProtectionRetired || entry.CleanupAttempted)
            throw new InvalidOperationException("Pending result capacity must be reserved for a fresh exact bundle before a controller is bound.");
        var owner = new NativeLocalResultCapture(main, null, registry, claim.Session, entry)
        { _pendingClaim = claim, _pendingBundle = ownedBundle };
        lock (ProcessGate)
        {
            if (ProcessOwners.Count >= MaximumCaptures || ProcessOwners.Any(value => ReferenceEquals(value._session, claim.Session)))
                throw new InvalidOperationException("This run already owns result capacity or the shared 32-capture process quota is exhausted.");
            ProcessOwners.EnsureCapacity(ProcessOwners.Count + 1); ProcessOwners.Add(owner);
        }
        return owner;
    }

    /// <summary>Pure managed exact-claim query for the host's shared pending launch record.
    /// Attachment alone does not end the reservation; native initialization or cancellation does.</summary>
    internal bool IsPendingReservationFor(LocalRunSessionRegistry registry,
        LocalRunSessionRegistry.CommitClaim claim, IDisposable ownedBundle)
    {
        _main.VerifyAccess();
        return !_closed && !_unknown && !_nativeAttempted && !_initializationAttempted && !_freeAttempted
            && ReferenceEquals(_registry, registry) && ReferenceEquals(_pendingClaim, claim)
            && ReferenceEquals(_pendingBundle, ownedBundle);
    }

    /// <summary>Production attachment records a backend borrowing the caller's rooted controller.
    /// The caller retains that root through initialization. This operation reads no root target,
    /// LevelInformation or source graph and makes no native metadata/capture invocation.</summary>
    internal void AttachPending(NativeRuntime api, AdapterBuildProfile profile, IntPtr module,
        NativeRuntime.RootHandle borrowedController)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(api); ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(borrowedController);
        AttachPendingBackend(new NativeLocalResultCaptureBackend(api, _main, profile, module, borrowedController));
    }

    internal void AttachPendingBackend(INativeLocalResultCaptureBackend backend)
    {
        Guard(false); ArgumentNullException.ThrowIfNull(backend);
        if (_pendingClaim is null || _pendingBundle is null || _closed || _unknown || _freeAttempted
            || _nativeAttempted || _initializationAttempted || _backend is not null)
            throw new InvalidOperationException("A pending result reservation attaches exactly one backend before native initialization.");
        _registry.VerifyPendingBundleClaim(_pendingClaim, _entry.MetadataIdentity, _pendingBundle);
        var current = _registry.Read(_session);
        if (current.Identity != _entry.Identity || current.ControllerIdentity == 0 || current.ResultSceneIdentity != 0
            || current.ResultSceneGeneration != 0 || current.ReturnRequested || current.ResultCallClaimed || current.Result is not null
            || current.ProtectionRetired || current.CleanupAttempted
            || !ReferenceEquals(_registry.FindProtectedSession(LocalBoundaryKind.ControllerRetry, current.ControllerIdentity), _session))
            throw new InvalidOperationException("Result backend attachment requires this exact already-bound pending controller session.");
        _entry = current; _backend = backend;
    }

    /// <summary>Initializes this same reserved owner after actual commit/source readiness.
    /// Pending/returning states are rejected without touching the backend. Once native work begins,
    /// every failure is sticky and neither initialization nor empty-reservation cancellation retries it.</summary>
    internal void InitializeWhenCommitted()
    {
        Guard(false);
        if (_closed || _unknown || _freeAttempted) throw new InvalidOperationException("Closed or unknown result preparation cannot initialize.");
        if (_initialized) return;
        if (_pendingClaim is null || _backend is null || _initializationAttempted)
            throw new InvalidOperationException("Result initialization requires this reservation's one attached pending backend.");
        var current = _registry.Read(_session);
        if (current.Identity != _entry.Identity || current.MetadataIdentity != _entry.MetadataIdentity
            || current.ControllerIdentity != _entry.ControllerIdentity || current.ControllerIdentity == 0
            || current.Phase != LocalRunSessionPhase.Committed || current.ReturnRequested || current.ResultCallClaimed
            || current.Result is not null || current.ProtectionRetired || current.CleanupAttempted
            || !ReferenceEquals(_registry.FindProtectedSession(LocalBoundaryKind.ControllerRetry, current.ControllerIdentity), _session))
            throw new InvalidOperationException("The exact reserved session must be committed and uncaptured before native source initialization.");
        _busy = true; _initializationAttempted = true; _nativeAttempted = true;
        try
        {
            _backend.RequireSupportedBuild(); // Must precede the borrowed root's Target and source reads.
            _source = _backend.ReadSource(); RequireSource(_source);
            _roots[0] = _backend.RetainController(); _roots[1] = _backend.RetainInformation();
            VerifyRootsAndSource(); _initialized = true;
        }
        catch (Exception failure)
        {
            _unknown = true; Failure = failure.Message;
            throw new NativeLocalResultCaptureException(this, failure);
        }
        finally { _busy = false; }
    }

    /// <summary>Releases only an empty, never-entered capture quota slot. It neither retires
    /// the session nor disposes the bundle, backend or caller-owned controller root.</summary>
    internal void CancelEmptyPendingReservation()
    {
        Guard(false);
        if (_closed) return;
        if (_pendingClaim is null || _pendingBundle is null || _unknown || _nativeAttempted || _initializationAttempted
            || _freeAttempted || _invocationAttempted || _roots.Any(root => root is not null))
            throw new InvalidOperationException("Only a never-entered empty pending capture reservation can return its quota.");
        // Returning does not preserve whether Commit occurred earlier. Only PendingCommit proves
        // this session has never crossed commit; a cancelled run otherwise needs its exact Close.
        _registry.VerifyPendingBundleClaim(_pendingClaim, _entry.MetadataIdentity, _pendingBundle);
        var current = _registry.Read(_session);
        if (current.Identity != _entry.Identity || current.Phase != LocalRunSessionPhase.PendingCommit
            || current.ResultCallClaimed || current.Result is not null || current.ReturnRequested
            || current.ResultSceneIdentity != 0 || current.ResultSceneGeneration != 0 || current.ProtectionRetired || current.CleanupAttempted)
            throw new InvalidOperationException("Empty capture cancellation cannot release a committed, returning or result-bearing session.");
        _freeAttempted = true; _closed = true; _backend = null; _pendingBundle = null;
        lock (ProcessGate) ProcessOwners.Remove(this);
    }
}
