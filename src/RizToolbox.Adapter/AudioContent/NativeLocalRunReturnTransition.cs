using RizToolbox.Runtime;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal sealed record NativeLocalRunReturnTransitionReceipt(LocalRunSessionIdentity Session, long Controller,
    long Manager, long AsyncOperation, NativeLocalRunScene OriginLevel, NativeLocalRunScene ReturnedScene,
    NativeLocalRunScene OriginalMenu);
internal sealed record NativeLocalRunReturnTransitionSnapshot(bool Attempted, bool Completed, bool Closed,
    bool CleanupUnknown, NativeLocalRunScene OriginLevel, NativeLocalRunScene OriginalMenu,
    long AsyncOperation, NativeLocalRunScene? ReturnedScene, string? Failure);

/// <summary>Controlled authority seam. Production uses the exact registry/session
/// owner below and never accepts a constructed scene receipt or target string.</summary>
internal interface ILocalRunReturnTransitionAuthority
{
    LocalRunSessionIdentity Identity { get; }
    long Controller { get; }
    void VerifyReturning(NativeLocalRunSceneTransitionReceipt launch);
    void MarkUnknown(string failure);
}

/// <summary>One genuine return scene request from a completed launch's exact Level
/// to its captured origin name. Completion proves scene unloading only; it cannot
/// close a session, retire a controller, restore globals or release run resources.</summary>
internal sealed class NativeLocalRunReturnTransition
{
    private const int MaximumOwners = 32;
    private static readonly object Gate = new();
    private static readonly HashSet<NativeLocalRunReturnTransition> ProcessOwners = new();
    private readonly IMainThreadDispatcher _main;
    private readonly ILocalRunSceneTransitionBackend _backend;
    private readonly ILocalRunReturnTransitionAuthority _authority;
    private readonly NativeLocalRunSceneTransition _launchTransition;
    private readonly NativeLocalRunSceneTransitionReceipt _launch;
    private readonly LocalRunSessionRegistry? _registry;
    private readonly LocalRunSessionRegistry.SessionHandle? _session;
    private NativeLocalRunReturnTransitionReceipt? _receipt;
    private NativeLocalRunScene? _returnedScene;
    private long _operation;
    private bool _attempted, _busy, _unknown, _closed, _releaseAttempted;
    internal string? Failure { get; private set; }
    internal bool Attempted { get { _main.VerifyAccess(); return _attempted; } }
    internal bool Completed { get { _main.VerifyAccess(); return _receipt is not null && !CleanupUnknown; } }
    internal bool CleanupUnknown { get { _main.VerifyAccess(); return _unknown || _backend.CleanupUnknown; } }
    internal int RootRecords { get { _main.VerifyAccess(); return _backend.RootRecords; } }
    internal int RetainedArgumentRecords { get { _main.VerifyAccess(); return _backend.RetainedArgumentRecords; } }
    internal static int RetainedProcessOwnerCount { get { lock (Gate) return ProcessOwners.Count; } }

    private NativeLocalRunReturnTransition(IMainThreadDispatcher main, ILocalRunSceneTransitionBackend backend,
        ILocalRunReturnTransitionAuthority authority, NativeLocalRunSceneTransition launchTransition,
        NativeLocalRunSceneTransitionReceipt launch, LocalRunSessionRegistry? registry,
        LocalRunSessionRegistry.SessionHandle? session)
        => (_main, _backend, _authority, _launchTransition, _launch, _registry, _session)
            = (main, backend, authority, launchTransition, launch, registry, session);

    /// <summary>The caller must already have called Registry.BeginReturn. This
    /// method never changes a non-returning session into a returning session.</summary>
    internal static NativeLocalRunReturnTransition Begin(NativeRuntime api, IMainThreadDispatcher main,
        AdapterBuildProfile profile, IntPtr module, NativeRuntime.RootHandle currentGameManagerRoot,
        LocalRunSessionRegistry registry, LocalRunSessionRegistry.SessionHandle session,
        NativeLocalRunSceneTransition actualCompletedLaunchTransitionOwner)
    {
        ArgumentNullException.ThrowIfNull(main); main.VerifyAccess(); ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(currentGameManagerRoot);
        ArgumentNullException.ThrowIfNull(registry); ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(actualCompletedLaunchTransitionOwner);
        var launch = actualCompletedLaunchTransitionOwner.VerifyReturnSource(registry, session);
        ValidateLaunch(launch);
        return BeginCore(main, new NativeLocalRunSceneTransitionBackend(api, main, profile, module,
            currentGameManagerRoot, launch.Origin.Name), new Authority(registry, session),
            actualCompletedLaunchTransitionOwner, launch, registry, session, false);
    }

    /// <summary>Controlled backend/authority only. The origin still comes from an
    /// actual completed controlled launch owner; a constructed receipt is refused.</summary>
    internal static NativeLocalRunReturnTransition BeginBackend(IMainThreadDispatcher main,
        ILocalRunSceneTransitionBackend backend, ILocalRunReturnTransitionAuthority authority,
        NativeLocalRunSceneTransition actualCompletedLaunchTransitionOwner)
    {
        ArgumentNullException.ThrowIfNull(main); main.VerifyAccess();
        ArgumentNullException.ThrowIfNull(actualCompletedLaunchTransitionOwner);
        if (backend is NativeLocalRunSceneTransitionBackend)
            throw new InvalidOperationException("Controlled return cannot adopt a production native backend.");
        var launch = actualCompletedLaunchTransitionOwner.CompletedReceipt; ValidateLaunch(launch);
        return BeginCore(main, backend, authority, actualCompletedLaunchTransitionOwner, launch, null, null, true);
    }

    private static NativeLocalRunReturnTransition BeginCore(IMainThreadDispatcher main,
        ILocalRunSceneTransitionBackend backend, ILocalRunReturnTransitionAuthority authority,
        NativeLocalRunSceneTransition launchTransition, NativeLocalRunSceneTransitionReceipt launch,
        LocalRunSessionRegistry? registry, LocalRunSessionRegistry.SessionHandle? session, bool controlled)
    {
        ArgumentNullException.ThrowIfNull(backend); ArgumentNullException.ThrowIfNull(authority);
        authority.VerifyReturning(launch);
        var owner = new NativeLocalRunReturnTransition(main, backend, authority, launchTransition, launch, registry, session);
        lock (Gate)
        {
            if (ProcessOwners.Count >= MaximumOwners || ProcessOwners.Any(held => ReferenceEquals(held._launchTransition, launchTransition)
                || registry is not null && ReferenceEquals(held._registry, registry) && ReferenceEquals(held._session, session)))
                throw new InvalidOperationException("The exact return already has an owner or the 32-owner return capacity is exhausted.");
            ProcessOwners.Add(owner); // Strong owner precedes native metadata, roots and calls.
        }
        try { launchTransition.ClaimReturn(owner, registry, session, controlled); }
        catch { lock (Gate) ProcessOwners.Remove(owner); throw; } // Pure rejection: no backend work/adoption occurred.
        owner._busy = true;
        try
        {
            backend.Capture(); owner.RequireIdleLevel(backend.ReadState()); authority.VerifyReturning(launch);
            owner._attempted = true; // Even a throwing/rejected native request has an unknown once-only outcome.
            backend.RequestOnce();
            var state = backend.ReadState(); owner.RequireManager(state);
            if (!state.Locked || state.AsyncOperation == 0)
                throw new InvalidOperationException("Return void result did not publish a new locked native AsyncOperation.");
            owner.ObserveScene(state.ActiveScene);
            owner._operation = state.AsyncOperation; backend.RetainOperation(owner._operation);
            if (backend.RetainedOperation != owner._operation)
                throw new InvalidOperationException("The return operation root differs from the actual native publication.");
            authority.VerifyReturning(launch); return owner;
        }
        catch (Exception error) { owner.Fail(error); throw new NativeLocalRunReturnTransitionException(owner, error); }
        finally { owner._busy = false; }
    }

    internal bool Tick()
    {
        Guard(); _busy = true;
        try
        {
            _authority.VerifyReturning(_launch);
            var state = _backend.ReadState(); RequireManager(state); ObserveScene(state.ActiveScene);
            if (!_attempted || _operation == 0 || _backend.RetainedOperation != _operation
                || state.AsyncOperation != 0 && state.AsyncOperation != _operation)
                throw new InvalidOperationException("A foreign/unretained operation replaced this exact return.");
            var done = _backend.OperationDone();
            if (state.AsyncOperation != 0)
            {
                if (!state.Locked) throw new InvalidOperationException("Return async publication outlived its scene-manager lock.");
                return false;
            }
            if (!done || state.Locked || _returnedScene is null || state.ActiveScene != _returnedScene)
                throw new InvalidOperationException("The manager cleared return responsibility before the exact native operation and new target completed.");

            // Independently query the original Level by its exact handle. A valid
            // recycled handle with another name cannot establish its unloading.
            var level = _backend.ReadScene(_launch.Destination.Handle);
            if (level.Handle != _launch.Destination.Handle || level.Valid && level.Name != _launch.Destination.Name
                || !level.Valid && level.Loaded)
                throw new InvalidOperationException("The original Level handle changed identity during return verification.");
            // Bracket the unload read with a fresh native manager/active/operation
            // observation, rather than combining unrelated historical snapshots.
            var after = _backend.ReadState(); RequireManager(after); ObserveScene(after.ActiveScene);
            if (after != state || !_backend.OperationDone())
                throw new InvalidOperationException("Native return state changed while original Level unloading was observed.");
            _authority.VerifyReturning(_launch);
            if (level.Loaded) return false;
            _receipt ??= new(_authority.Identity, _authority.Controller, state.Manager, _operation,
                _launch.Destination, _returnedScene, _launch.Origin);
            if (_receipt.ReturnedScene != _returnedScene)
                throw new InvalidOperationException("The completed return target changed its scene handle.");
            return true;
        }
        catch (Exception error) { Fail(error); throw new NativeLocalRunReturnTransitionException(this, error); }
        finally { _busy = false; }
    }

    internal NativeLocalRunReturnTransitionReceipt RequireCompleted()
    {
        if (!Tick() || _receipt is null) throw new InvalidOperationException("The native return transition or original Level unloading is still pending.");
        return _receipt;
    }
    internal NativeLocalRunReturnTransitionReceipt VerifyMenuSource(NativeLocalRunSceneTransition initial,
        LocalRunSessionRegistry registry, LocalRunSessionRegistry.SessionHandle session)
    {
        _main.VerifyAccess();
        if (!ReferenceEquals(_launchTransition, initial) || !ReferenceEquals(_registry, registry) || !ReferenceEquals(_session, session))
            throw new InvalidOperationException("Menu restoration requires this exact production launch/registry/session return owner.");
        return RequireCompleted();
    }
    internal NativeLocalRunReturnTransitionSnapshot Read()
    {
        _main.VerifyAccess(); return new(_attempted, _receipt is not null && !CleanupUnknown, _closed, CleanupUnknown,
            _launch.Destination, _launch.Origin, _operation, _returnedScene, Failure);
    }

    /// <summary>Only independently rooted scene observers are released. No registry
    /// Close/token, globals restoration or run resource cleanup occurs here.</summary>
    internal void ReleaseAfterCompleted()
    {
        Guard(); if (_receipt is null) throw new InvalidOperationException("Return observers cannot release before verified scene completion.");
        _busy = true; _releaseAttempted = true;
        try { _backend.ReleaseRoots(); _closed = true; lock (Gate) ProcessOwners.Remove(this); }
        catch (Exception error) { Fail(error); throw new NativeLocalRunReturnTransitionException(this, error); }
        finally { _busy = false; }
    }

    private static void ValidateLaunch(NativeLocalRunSceneTransitionReceipt launch)
    {
        if (launch.Controller == 0 || launch.Manager == 0 || launch.AsyncOperation == 0
            || !launch.Origin.Valid || !launch.Origin.Loaded || string.IsNullOrWhiteSpace(launch.Origin.Name)
            || launch.Origin.Name.Length > 256 || launch.Origin.Name.Any(char.IsControl) || launch.Origin.Name == "Level"
            || !launch.Destination.Valid || !launch.Destination.Loaded || launch.Destination.Name != "Level"
            || launch.Origin.Handle == launch.Destination.Handle)
            throw new InvalidOperationException("Return requires an actual completed launch from a bounded non-Level origin to a different loaded Level.");
    }
    private void RequireIdleLevel(NativeLocalRunSceneTransitionObservation state)
    {
        RequireManager(state);
        if (state.Locked || state.AsyncOperation != 0 || state.ActiveScene != _launch.Destination)
            throw new InvalidOperationException("Return must begin at this exact completed launch's idle active Level.");
    }
    private void RequireManager(NativeLocalRunSceneTransitionObservation state)
    {
        if (!state.CurrentManager || state.Manager != _launch.Manager || !state.ActiveScene.Valid || !state.ActiveScene.Loaded)
            throw new InvalidOperationException("The current native manager or loaded active scene no longer belongs to the return owner.");
    }
    private void ObserveScene(NativeLocalRunScene scene)
    {
        if (scene == _launch.Destination)
        {
            if (_returnedScene is not null) throw new InvalidOperationException("The active scene returned to Level after the target was already observed.");
            return;
        }
        if (!scene.Valid || !scene.Loaded || scene.Handle == _launch.Destination.Handle || scene.Name != _launch.Origin.Name)
            throw new InvalidOperationException("The return observed a foreign or same-handle target scene.");
        if (_returnedScene is not null && _returnedScene != scene)
            throw new InvalidOperationException("The returned scene name appeared with a changed native handle.");
        _returnedScene ??= scene;
    }
    private void Fail(Exception error)
    {
        if (_unknown) return;
        _unknown = true; Failure = "Native return scene responsibility retained without retry: " + error.GetType().Name;
        try { _authority.MarkUnknown(Failure); } catch { }
    }
    private void Guard()
    {
        _main.VerifyAccess();
        if (_busy || _closed || _releaseAttempted || CleanupUnknown)
            throw new InvalidOperationException("Closed, reentrant or unconfirmed return responsibility cannot be reused.");
    }

    /// <summary>Pure registry admission used by production and real-registry tests.
    /// It neither creates native receivers nor changes the registry return phase.</summary>
    internal sealed class Authority : ILocalRunReturnTransitionAuthority
    {
        private readonly LocalRunSessionRegistry _registry;
        private readonly LocalRunSessionRegistry.SessionHandle _session;
        private readonly LocalRunSessionIdentity _identity;
        private readonly long _controller;
        private bool _unknown;
        internal Authority(LocalRunSessionRegistry registry, LocalRunSessionRegistry.SessionHandle session)
        {
            ArgumentNullException.ThrowIfNull(registry); ArgumentNullException.ThrowIfNull(session);
            var current = registry.Read(session); (_registry, _session, _identity, _controller) = (registry, session, current.Identity, current.ControllerIdentity);
        }
        public LocalRunSessionIdentity Identity => _identity;
        public long Controller => _controller;
        public void VerifyReturning(NativeLocalRunSceneTransitionReceipt launch)
        {
            var current = _registry.Read(_session);
            if (_unknown || _controller == 0 || current.Identity != _identity || launch.Session != _identity || launch.Controller != _controller
                || current.ControllerIdentity != _controller || current.Phase != LocalRunSessionPhase.Returning || !current.ReturnRequested
                || current.ProtectionRetired || current.CleanupAttempted || !string.IsNullOrEmpty(current.Failure)
                || !ReferenceEquals(_registry.FindProtectedSession(LocalBoundaryKind.ControllerLevelOver, _controller), _session))
                throw new InvalidOperationException("The caller must first BeginReturn for this exact protected registry/session/controller.");
        }
        public void MarkUnknown(string failure) => _unknown = true;
    }
}

internal sealed class NativeLocalRunReturnTransitionException : InvalidOperationException
{
    internal NativeLocalRunReturnTransition Responsibility { get; }
    internal NativeLocalRunReturnTransitionException(NativeLocalRunReturnTransition owner, Exception error)
        : base("The LOCAL return scene owner retains every acquired native observer and argument without retry.", error) => Responsibility = owner;
}
