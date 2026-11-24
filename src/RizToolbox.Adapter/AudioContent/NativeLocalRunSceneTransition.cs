using System.Security.Cryptography;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.AudioContent;

internal sealed record NativeLocalRunScene(int Handle, string Name, bool Valid, bool Loaded);
internal sealed record NativeLocalRunSceneTransitionObservation(long Manager, bool CurrentManager, bool Locked,
    long AsyncOperation, NativeLocalRunScene ActiveScene);
internal sealed record NativeLocalRunSceneTransitionReceipt(LocalRunSessionIdentity Session, long Controller,
    long Manager, long AsyncOperation, NativeLocalRunScene Origin, NativeLocalRunScene Destination);
internal sealed record NativeLocalRunSceneTransitionSnapshot(bool Captured, bool Attempted, bool Completed,
    bool Closed, bool CleanupUnknown, NativeLocalRunScene? Origin, long AsyncOperation, string? Failure);

/// <summary>Controlled operation seam; production supplies exact pinned metadata,
/// independently rooted native owners and the real engine AsyncOperation below.</summary>
internal interface ILocalRunSceneTransitionBackend
{
    bool CleanupUnknown { get; }
    int RootRecords { get; }
    int RetainedArgumentRecords { get; }
    void Capture();
    NativeLocalRunSceneTransitionObservation ReadState();
    NativeLocalRunScene ReadScene(int handle);
    void RequestOnce();
    void RetainOperation(long exactOperation);
    long RetainedOperation { get; }
    bool OperationDone();
    void ReleaseRoots();
}
internal interface ILocalRunSceneTransitionAuthority
{
    LocalRunSessionIdentity Identity { get; }
    long Controller { get; }
    void VerifyBeforeCapture();
    void VerifyActivated(NativeInactiveControllerActivationReceipt activation);
    void VerifyPending();
    void MarkUnknown(string failure);
}

/// <summary>Owns only the scene-transition observations. Completion is not run
/// readiness, input/camera balance, return navigation or controller retirement.</summary>
internal sealed class NativeLocalRunSceneTransition : INativeLocalActiveConsumerClosureSource
{
    private const int MaximumOwners = 32;
    private static readonly object Gate = new();
    private static readonly HashSet<NativeLocalRunSceneTransition> ProcessOwners = new();
    private readonly IMainThreadDispatcher _main;
    private readonly ILocalRunSceneTransitionBackend _backend;
    private readonly ILocalRunSceneTransitionAuthority _authority;
    private readonly bool _controlled;
    private NativeLocalRunSceneTransitionObservation? _origin;
    private NativeLocalRunSceneTransitionReceipt? _receipt;
    private bool _captured, _attempted, _busy, _unknown, _closed, _releaseAttempted;
    private long _operation;
    private NativeLocalRunReturnTransition? _returnClaimant;
    internal string? Failure { get; private set; }
    internal bool CleanupUnknown { get { _main.VerifyAccess(); return _unknown || _backend.CleanupUnknown; } }
    internal bool Attempted { get { _main.VerifyAccess(); return _attempted; } }
    internal bool Completed { get { _main.VerifyAccess(); return _receipt is not null && !CleanupUnknown; } }
    /// <summary>Historical immutable native completion receipt. Reading it never
    /// reenters native code or requires a launch to remain PendingCommit.</summary>
    internal NativeLocalRunSceneTransitionReceipt CompletedReceipt
    {
        get
        {
            _main.VerifyAccess();
            if (_receipt is null || CleanupUnknown)
                throw new InvalidOperationException("No confirmed immutable launch transition receipt exists.");
            return _receipt;
        }
    }
    internal NativeLocalRunScene Origin { get { Guard(); return _origin?.ActiveScene ?? throw new InvalidOperationException("No origin scene was captured."); } }
    internal int RootRecords => _backend.RootRecords;
    internal int RetainedArgumentRecords => _backend.RetainedArgumentRecords;

    private NativeLocalRunSceneTransition(IMainThreadDispatcher main, ILocalRunSceneTransitionBackend backend,
        ILocalRunSceneTransitionAuthority authority, bool controlled)
        => (_main, _backend, _authority, _controlled) = (main, backend, authority, controlled);

    internal static NativeLocalRunSceneTransition Capture(NativeRuntime api, IMainThreadDispatcher main,
        AdapterBuildProfile profile, IntPtr module, NativeRuntime.RootHandle gameManager, LocalRunLaunchCommitState launch)
    {
        ArgumentNullException.ThrowIfNull(main); main.VerifyAccess(); ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(gameManager); ArgumentNullException.ThrowIfNull(launch);
        return CaptureCore(main, new NativeLocalRunSceneTransitionBackend(api, main, profile, module, gameManager),
            new Authority(launch), false);
    }
    internal static NativeLocalRunSceneTransition CaptureBackend(IMainThreadDispatcher main,
        ILocalRunSceneTransitionBackend backend, ILocalRunSceneTransitionAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(main); main.VerifyAccess();
        if (backend is NativeLocalRunSceneTransitionBackend)
            throw new InvalidOperationException("Controlled capture cannot adopt a production native backend.");
        return CaptureCore(main, backend, authority, true);
    }
    private static NativeLocalRunSceneTransition CaptureCore(IMainThreadDispatcher main,
        ILocalRunSceneTransitionBackend backend, ILocalRunSceneTransitionAuthority authority, bool controlled)
    {
        ArgumentNullException.ThrowIfNull(main); main.VerifyAccess(); ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(authority); authority.VerifyBeforeCapture();
        var owner = new NativeLocalRunSceneTransition(main, backend, authority, controlled);
        lock (Gate)
        {
            if (ProcessOwners.Count >= MaximumOwners) throw new InvalidOperationException("Scene-transition responsibility quota is exhausted.");
            ProcessOwners.Add(owner); // Retain backend before its first native root or metadata operation.
        }
        owner._busy = true;
        try
        {
            backend.Capture(); owner._origin = backend.ReadState(); owner.RequireIdleOrigin(owner._origin);
            authority.VerifyBeforeCapture(); owner._captured = true; return owner;
        }
        catch (Exception error) { owner.Fail(error); throw new NativeLocalRunSceneTransitionException(owner, error); }
        finally { owner._busy = false; }
    }

    internal void StartLevelOnce(NativeInactiveControllerActivation activation)
    {
        ArgumentNullException.ThrowIfNull(activation);
        StartCore(activation.Read);
    }
    /// <summary>Controlled-backend-only fault seam. Production always reads the
    /// actual activation owner above; a constructed record grants no native permission.</summary>
    internal void StartControlled(Func<NativeInactiveControllerActivationReceipt> readActivation)
    {
        _main.VerifyAccess();
        if (!_controlled) throw new InvalidOperationException("Production transition requires the actual activation owner.");
        StartCore(readActivation);
    }
    private void StartCore(Func<NativeInactiveControllerActivationReceipt> readActivation)
    {
        Guard(); ArgumentNullException.ThrowIfNull(readActivation);
        if (!_captured || _attempted) throw new InvalidOperationException("A captured scene transition can be attempted only once.");
        _busy = true;
        try
        {
            var activation = readActivation(); _authority.VerifyActivated(activation);
            if (activation.Graph.ParentScene != _origin!.ActiveScene.Handle)
                throw new InvalidOperationException("The activation staging graph did not originate in this captured scene.");
            RequireIdleOrigin(_backend.ReadState()); _authority.VerifyPending();
            _attempted = true; // Before the irreversible native call, including unknown entry/outcome.
            _backend.RequestOnce();
            var state = _backend.ReadState(); RequireManager(state);
            if (!state.Locked || state.AsyncOperation == 0)
                throw new InvalidOperationException("Void return did not publish an actual locked new scene AsyncOperation.");
            _operation = state.AsyncOperation;
            _backend.RetainOperation(_operation);
            if (_backend.RetainedOperation != _operation)
                throw new InvalidOperationException("The retained operation differs from the actual published transition.");
            _authority.VerifyPending();
        }
        catch (Exception error) { Fail(error); throw new NativeLocalRunSceneTransitionException(this, error); }
        finally { _busy = false; }
    }
    internal bool Tick()
    {
        Guard(); if (!_attempted) return false;
        _busy = true;
        try
        {
            _authority.VerifyPending();
            var state = _backend.ReadState(); RequireManager(state);
            if (_operation == 0 || _backend.RetainedOperation != _operation
                || state.AsyncOperation != 0 && state.AsyncOperation != _operation)
                throw new InvalidOperationException("A successor or unretained operation replaced the owned transition.");
            var scene = state.ActiveScene; var origin = _origin!.ActiveScene;
            var atOrigin = scene.Handle == origin.Handle && scene.Name == origin.Name;
            var atDestination = scene.Handle != origin.Handle && scene.Name == "Level" && scene.Valid && scene.Loaded;
            if (!atOrigin && !atDestination)
                throw new InvalidOperationException("The transition observed a foreign, invalid or unloaded active scene.");
            var done = _backend.OperationDone();
            if (state.AsyncOperation == 0 && (!done || state.Locked || !atDestination))
                throw new InvalidOperationException("The scene manager cleared responsibility without the exact completed new scene.");
            if (state.AsyncOperation != 0)
            {
                if (!state.Locked) throw new InvalidOperationException("The owned async operation outlived its scene-manager lock.");
                return false;
            }
            _receipt ??= new(_authority.Identity, _authority.Controller, state.Manager, _operation, origin, scene);
            if (_receipt.Destination != scene)
                throw new InvalidOperationException("The completed scene identity changed before its launch receipt was consumed.");
            return true;
        }
        catch (Exception error) { Fail(error); throw new NativeLocalRunSceneTransitionException(this, error); }
        finally { _busy = false; }
    }
    internal NativeLocalRunSceneTransitionReceipt RequireCompleted()
    {
        if (!Tick() || _receipt is null) throw new InvalidOperationException("The real scene transition has not completed.");
        return _receipt;
    }
    internal NativeLocalRunSceneTransitionReceipt VerifyReturnSource(LocalRunSessionRegistry registry,
        LocalRunSessionRegistry.SessionHandle session)
    {
        _main.VerifyAccess();
        if (_controlled || _authority is not Authority authority || !authority.Owns(registry, session))
            throw new InvalidOperationException("Return navigation requires this exact registry/session's actual production launch owner.");
        return CompletedReceipt;
    }
    internal NativeLocalRunSceneTransitionReceipt ClaimReturn(NativeLocalRunReturnTransition owner,
        LocalRunSessionRegistry? registry, LocalRunSessionRegistry.SessionHandle? session, bool controlled)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(owner);
        var receipt = controlled
            ? _controlled ? CompletedReceipt : throw new InvalidOperationException("A controlled return requires a controlled launch owner.")
            : VerifyReturnSource(registry ?? throw new ArgumentNullException(nameof(registry)),
                session ?? throw new ArgumentNullException(nameof(session)));
        if (_returnClaimant is not null) throw new InvalidOperationException("A launch transition can authorize return navigation only once.");
        _returnClaimant = owner;
        return receipt;
    }
    internal NativeLocalRunReturnTransition? OwnedReturnFor(LocalRunLaunchCommitState exactLaunch)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(exactLaunch);
        _ = VerifyReturnSource(exactLaunch.Registry, exactLaunch.Session);
        if (CompletedReceipt.Session != exactLaunch.Identity || CompletedReceipt.Controller != exactLaunch.Read().Controller)
            throw new InvalidOperationException("The retained scene owner belongs to another exact launch.");
        return _returnClaimant;
    }
    public NativeLocalActiveConsumerCheck ReadActiveConsumerClosure(LocalRunLaunchCommitState exactLaunch,
        NativeLocalActiveConsumerObligation obligation)
    {
        if (obligation != NativeLocalActiveConsumerObligation.AsyncConsumers)
            throw new ArgumentOutOfRangeException(nameof(obligation));
        var actualReturn = OwnedReturnFor(exactLaunch);
        if (CleanupUnknown || actualReturn?.CleanupUnknown == true)
            return new(obligation, NativeLocalActiveConsumerProofState.Unknown, "Exact launch/return async owner is Unknown.");
        if (actualReturn is null || !actualReturn.Attempted)
            return new(obligation, NativeLocalActiveConsumerProofState.Pending, "No actual requested return operation has completed.");
        if (!actualReturn.Tick())
            return new(obligation, NativeLocalActiveConsumerProofState.Pending, "The retained native AsyncOperation or original Level unloading is pending.");
        var receipt = actualReturn.VerifyMenuSource(this, exactLaunch.Registry, exactLaunch.Session);
        return new(obligation, NativeLocalActiveConsumerProofState.Proven,
            $"Actual return operation {receipt.AsyncOperation:x} completed; original Level handle {receipt.OriginLevel.Handle} unloaded; exact menu {receipt.ReturnedScene.Handle} is loaded.");
    }
    internal NativeLocalRunSceneTransitionSnapshot Read()
    {
        _main.VerifyAccess();
        return new(_captured, _attempted, _receipt is not null && !CleanupUnknown, _closed, CleanupUnknown,
            _origin?.ActiveScene, _operation, Failure);
    }
    /// <summary>Releases only this observer's independently retained roots after
    /// verified transition completion. It grants no LOCAL consumer-end evidence.</summary>
    internal void ReleaseAfterCompleted()
    {
        Guard(); if (_receipt is null) throw new InvalidOperationException("An in-flight transition cannot release its observations.");
        Release();
    }
    internal void ReleaseBeforeAttempt()
    {
        Guard(); if (!_captured || _attempted) throw new InvalidOperationException("Only an untouched captured snapshot can be released here.");
        Release();
    }
    private void Release()
    {
        _releaseAttempted = true; _busy = true;
        try { _backend.ReleaseRoots(); _closed = true; lock (Gate) ProcessOwners.Remove(this); }
        catch (Exception error) { Fail(error); throw new NativeLocalRunSceneTransitionException(this, error); }
        finally { _busy = false; }
    }
    private void RequireIdleOrigin(NativeLocalRunSceneTransitionObservation state)
    {
        if (!state.CurrentManager || state.Manager == 0 || state.Locked || state.AsyncOperation != 0
            || !state.ActiveScene.Valid || !state.ActiveScene.Loaded || string.IsNullOrEmpty(state.ActiveScene.Name)
            || state.ActiveScene.Name == "Level"
            || _origin is not null && (state.Manager != _origin.Manager || state.ActiveScene != _origin.ActiveScene))
            throw new InvalidOperationException("Scene capture/start requires the same idle current manager and loaded origin distinct from Level.");
    }
    private void RequireManager(NativeLocalRunSceneTransitionObservation state)
    {
        if (!state.CurrentManager || state.Manager != _origin!.Manager || !state.ActiveScene.Valid || !state.ActiveScene.Loaded)
            throw new InvalidOperationException("The actual rooted manager or active loaded scene no longer matches this transition owner.");
    }
    private void Fail(Exception error)
    {
        if (_unknown) return;
        _unknown = true; Failure = "Scene transition responsibility retained: " + error.GetType().Name;
        try { _authority.MarkUnknown(Failure); } catch { }
    }
    private void Guard()
    {
        _main.VerifyAccess();
        if (_busy || _closed || _releaseAttempted || CleanupUnknown)
            throw new InvalidOperationException("Closed, reentrant or unconfirmed scene-transition responsibility cannot be reused.");
    }
    private sealed class Authority : ILocalRunSceneTransitionAuthority
    {
        private readonly LocalRunLaunchCommitState _launch;
        internal Authority(LocalRunLaunchCommitState launch) => _launch = launch;
        internal bool Owns(LocalRunSessionRegistry registry, LocalRunSessionRegistry.SessionHandle session)
            => ReferenceEquals(_launch.Registry, registry) && ReferenceEquals(_launch.Session, session);
        public LocalRunSessionIdentity Identity => _launch.Identity;
        public long Controller => _launch.Read().Controller;
        public void VerifyBeforeCapture()
        {
            VerifyPending(); var state = _launch.Read();
            if (state.ActivationAttempted || state.Controller == 0 || _launch.ParameterBinding is null)
                throw new InvalidOperationException("Origin capture must follow exact inactive binding and precede first activation.");
        }
        public void VerifyActivated(NativeInactiveControllerActivationReceipt activation)
        {
            VerifyPending(); var state = _launch.Read();
            if (!state.ActivationAttempted || state.Controller == 0 || activation.Session != Identity || activation.Controller != state.Controller)
                throw new InvalidOperationException("Scene start requires this exact pending run's actual verified activation.");
        }
        public void VerifyPending()
        {
            _launch.VerifyPendingOwnership(_launch.Registry); var state = _launch.Read();
            if (state.EarlyTerminal || state.Phase is LocalRunLaunchCommitPhase.Unknown or LocalRunLaunchCommitPhase.CommittedReady)
                throw new InvalidOperationException("This transition no longer owns a usable pending launch.");
        }
        public void MarkUnknown(string failure) => _launch.MarkUnknown(failure);
    }
}

internal sealed class NativeLocalRunSceneTransitionException : InvalidOperationException
{
    internal NativeLocalRunSceneTransition Responsibility { get; }
    internal NativeLocalRunSceneTransitionException(NativeLocalRunSceneTransition owner, Exception error)
        : base("The LOCAL scene transaction retains all acquired native responsibility.", error) => Responsibility = owner;
}

internal sealed class NativeLocalRunSceneTransitionBackend : ILocalRunSceneTransitionBackend
{
    private readonly NativeRuntime _api;
    private readonly IMainThreadDispatcher _main;
    private readonly AdapterBuildProfile _profile;
    private readonly IntPtr _module;
    private readonly NativeRuntime.RootHandle _borrowedManager;
    private readonly string _targetScene;
    // Manager, scene manager, owned AsyncOperation, transient scene-name, target string.
    private readonly NativeRuntime.RootHandle?[] _roots = new NativeRuntime.RootHandle?[5];
    private readonly bool[] _freeAttempted = new bool[5];
    private readonly NativePublicationArgumentRecords _arguments;
    private IntPtr _managerClass, _sceneManagerClass, _asyncClass, _alive, _getActiveScene, _getName,
        _valid, _loaded, _startNextScene, _done;
    private bool _captured, _requested, _releaseAttempted;
    public bool CleanupUnknown { get; private set; }
    public int RootRecords { get { _main.VerifyAccess(); return _roots.Count(root => root is not null); } }
    public int RetainedArgumentRecords => _arguments.Count;
    public long RetainedOperation { get { Check(); return _roots[2]?.Target.ToInt64() ?? 0; } }
    internal NativeLocalRunSceneTransitionBackend(NativeRuntime api, IMainThreadDispatcher main,
        AdapterBuildProfile profile, IntPtr module, NativeRuntime.RootHandle manager, string targetScene = "Level")
    {
        if (string.IsNullOrWhiteSpace(targetScene) || targetScene.Length > 256 || targetScene.Any(char.IsControl))
            throw new ArgumentException("The exact captured target scene name must be nonempty and bounded.", nameof(targetScene));
        (_api, _main, _profile, _module, _borrowedManager, _targetScene) = (api, main, profile, module, manager, targetScene);
        _arguments = new(main);
    }
    public void Capture()
    {
        _main.VerifyAccess();
        if (_captured || _roots.Any(root => root is not null) || CleanupUnknown || _releaseAttempted)
            throw new InvalidOperationException("Native scene capture is one-shot.");
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || _module == IntPtr.Zero
            || _profile.GameAssemblySha256 != WorkshopChartModule.Fingerprint || _profile.UnityVersion != "2022.3.62f2")
            throw new UnsupportedSignatureException("Scene transition requires the pinned Windows x64 Unity build.");
        var file = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "GameAssembly.dll");
        using (var stream = File.OpenRead(file)) using (var sha = SHA256.Create())
            if (Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant() != WorkshopChartModule.Fingerprint)
                throw new UnsupportedSignatureException("Scene-transition image fingerprint differs from its profile.");
        Observe(() => {
            _managerClass = _api.Class("Assembly-CSharp.dll", "", "GameManager");
            _sceneManagerClass = _api.Class("Assembly-CSharp.dll", "Rizline", "SceneManager");
            _asyncClass = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "AsyncOperation");
            var unitySceneManager = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine.SceneManagement", "SceneManager");
            var scene = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine.SceneManagement", "Scene");
            var unityObject = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Object");
            _alive = Method(unityObject, "op_Implicit", new[] { "UnityEngine.Object" }, "System.Boolean", true, 0x15409B0);
            _startNextScene = Method(_sceneManagerClass, "StartNextScene",
                new[] { "System.String", "System.Single", "System.Func<System.Boolean>" }, "System.Void", false, 0x402170);
            _getActiveScene = Method(unitySceneManager, "GetActiveScene", Array.Empty<string>(), "UnityEngine.SceneManagement.Scene", true, 0x1559790);
            _getName = Method(scene, "GetNameInternal", new[] { "System.Int32" }, "System.String", true, 0x155A830);
            _valid = Method(scene, "IsValidInternal", new[] { "System.Int32" }, "System.Boolean", true, 0x155A8B0);
            _loaded = Method(scene, "GetIsLoadedInternal", new[] { "System.Int32" }, "System.Boolean", true, 0x155A7F0);
            _done = Method(_asyncClass, "get_isDone", Array.Empty<string>(), "System.Boolean", false, 0x1534390);
            if (_api.DescribeFieldType(_managerClass, "Instance") != "GameManager")
                throw new UnsupportedSignatureException("GameManager.Instance metadata changed.");
            _api.RequireInstanceFieldLayout(_managerClass, "sceneManager", "Rizline.SceneManager", 0x20);
            _api.RequireInstanceFieldLayout(_sceneManagerClass, "_lock", "System.Boolean", 0x30);
            _api.RequireInstanceFieldLayout(_sceneManagerClass, "_nextScene", "System.String", 0x38);
            _api.RequireInstanceFieldLayout(_sceneManagerClass, "_transTime", "System.Single", 0x48);
            _api.RequireInstanceFieldLayout(_sceneManagerClass, "_waitPredicator", "System.Func<System.Boolean>", 0x50);
            _api.RequireInstanceFieldLayout(_sceneManagerClass, "_async", "UnityEngine.AsyncOperation", 0x58);
            Root(0, _borrowedManager.Target);
            Root(1, _api.ReadWithPublication<IntPtr>(_roots[0]!.Target, "sceneManager"));
            Root(4, _api.String(_targetScene)); _captured = true;
            return true;
        });
    }
    private IntPtr Method(IntPtr declaring, string name, string[] parameters, string returns, bool isStatic, long rva)
    {
        var target = isStatic ? _api.InspectDeclaredStaticMethod(declaring, name, parameters, returns, _module)
            : _api.InspectDeclaredMethod(declaring, name, parameters, returns, _module);
        if (target.Entry?.Rva != rva || target.GenericMethod || target.InflatedMethod
            || ((target.Flags & 0x10) != 0) != isStatic || (target.Flags & 0x400) != 0)
            throw new UnsupportedSignatureException("Scene transition metadata/RVA changed: " + name);
        return target.MethodInfo;
    }
    public NativeLocalRunSceneTransitionObservation ReadState()
    {
        Check(); return Observe<NativeLocalRunSceneTransitionObservation>(() => {
            var manager = _roots[0]!.Target; var scenes = _roots[1]!.Target;
            if (_api.ObjectClass(manager) != _managerClass || _api.ObjectClass(scenes) != _sceneManagerClass)
                throw new UnsupportedSignatureException("Retained scene owners differ from exact native classes.");
            var current = _api.ReadStatic<IntPtr>(_managerClass, "Instance") == manager
                && _api.ReadWithPublication<IntPtr>(manager, "sceneManager") == scenes
                && Value<bool>(_alive, IntPtr.Zero, A.Ref(manager)) && Value<bool>(_alive, IntPtr.Zero, A.Ref(scenes));
            var handle = _api.InvokeSceneHandleWithPublication(_getActiveScene, IntPtr.Zero);
            var scene = ReadSceneCore(handle);
            if (_api.InvokeSceneHandleWithPublication(_getActiveScene, IntPtr.Zero) != handle)
                throw new InvalidOperationException("The native active scene changed during the bounded observation.");
            return new(scenes.ToInt64(), current, _api.ReadWithPublication<bool>(scenes, "_lock"),
                _api.ReadWithPublication<IntPtr>(scenes, "_async").ToInt64(), scene);
        });
    }
    public NativeLocalRunScene ReadScene(int handle)
    {
        Check(); return Observe(() => ReadSceneCore(handle));
    }
    private NativeLocalRunScene ReadSceneCore(int handle)
    {
        var valid = Value<bool>(_valid, IntPtr.Zero, A.Value(handle));
        var loaded = valid && Value<bool>(_loaded, IntPtr.Zero, A.Value(handle));
        var name = valid ? SceneName(handle) : "";
        // A recycled/unloaded handle must not combine an earlier name with a later
        // loaded state. The return transaction also brackets this with active reads.
        if (Value<bool>(_valid, IntPtr.Zero, A.Value(handle)) != valid
            || valid && Value<bool>(_loaded, IntPtr.Zero, A.Value(handle)) != loaded)
            throw new InvalidOperationException("The exact scene handle changed while its state was observed.");
        return new(handle, name, valid, loaded);
    }
    private string SceneName(int handle)
    {
        if (_roots[3] is not null) throw new InvalidOperationException("An earlier scene-name publication remains owned.");
        var args = new[] { A.Value(handle) }; _arguments.Begin(args);
        try
        {
            _api.InvokeReferenceWithPublication(_getName, IntPtr.Zero, value => Root(3, value), args);
            _arguments.Complete(); var name = _api.TextWithPublication(_roots[3]!.Target);
            Free(3); _freeAttempted[3] = false; return name;
        }
        catch { if (_arguments.Count != 0) _arguments.Fail(); CleanupUnknown = true; throw; }
    }
    public void RequestOnce()
    {
        Check(); if (_requested) throw new InvalidOperationException("The exact native scene request is once-only.");
        _requested = true;
        var args = new[] { A.Ref(_roots[4]!.Target), A.Value(3f), A.Ref(IntPtr.Zero) };
        _arguments.Begin(args);
        try
        {
            var manager = _roots[1]!.Target;
            _api.InvokeVoidWithPublication(_startNextScene, manager, args); _arguments.Complete();
            if (_api.ReadWithPublication<IntPtr>(manager, "_nextScene") != _roots[4]!.Target
                || BitConverter.SingleToInt32Bits(_api.ReadWithPublication<float>(manager, "_transTime")) != BitConverter.SingleToInt32Bits(3f)
                || _api.ReadWithPublication<IntPtr>(manager, "_waitPredicator") != IntPtr.Zero)
                throw new InvalidOperationException("The actual native scene request did not preserve its captured target/3/null arguments.");
        }
        catch { if (_arguments.Count != 0) _arguments.Fail(); CleanupUnknown = true; throw; }
    }
    public void RetainOperation(long exactOperation)
    {
        Check(); Observe(() => {
            if (!_requested || exactOperation == 0 || _roots[2] is not null
                || _api.ReadWithPublication<IntPtr>(_roots[1]!.Target, "_async").ToInt64() != exactOperation)
                throw new InvalidOperationException("Only the actual just-published operation can be retained once.");
            Root(2, new(exactOperation));
            if (_api.ObjectClass(_roots[2]!.Target) != _asyncClass
                || _api.ReadWithPublication<IntPtr>(_roots[1]!.Target, "_async") != _roots[2]!.Target)
                throw new InvalidOperationException("The adopted operation changed class or native ownership during retention.");
            return true;
        });
    }
    public bool OperationDone()
    {
        Check(); return Observe(() => _roots[2] is not null
            ? Value<bool>(_done, _roots[2]!.Target) : throw new InvalidOperationException("No actual operation was retained."));
    }
    public void ReleaseRoots()
    {
        _main.VerifyAccess(); if (_releaseAttempted || CleanupUnknown || _arguments.CleanupUnknown)
            throw new InvalidOperationException("Unconfirmed native scene roots cannot be freed or retried.");
        _releaseAttempted = true; var failures = new List<Exception>(5);
        for (var i = _roots.Length - 1; i >= 0; i--)
            if (_roots[i] is not null) try { Free(i); } catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) { CleanupUnknown = true; throw new AggregateException("Scene observation roots have unknown frees.", failures); }
    }
    private void Root(int slot, IntPtr pointer)
    {
        if (_roots[slot] is not null || _freeAttempted[slot]) throw new InvalidOperationException("Scene root publication slot is already consumed.");
        _roots[slot] = _api.Root(pointer); // Fixed slot exists before allocation; no validation precedes recording.
    }
    private void Free(int slot)
    {
        if (_freeAttempted[slot]) throw new InvalidOperationException("A scene root free is never retried.");
        _freeAttempted[slot] = true; _roots[slot]!.Dispose(); _roots[slot] = null;
    }
    private T Value<T>(IntPtr method, IntPtr receiver, params A[] arguments) where T : struct
    {
        _arguments.Begin(arguments);
        try { var value = _api.InvokeValueWithPublication<T>(method, receiver, arguments); _arguments.Complete(); return value; }
        catch { _arguments.Fail(); CleanupUnknown = true; throw; }
    }
    private T Observe<T>(Func<T> read) { try { return read(); } catch { CleanupUnknown = true; throw; } }
    private void Check()
    {
        _main.VerifyAccess(); if (!_captured || _releaseAttempted || CleanupUnknown || _arguments.CleanupUnknown)
            throw new InvalidOperationException("Native scene observation ownership is unavailable or unknown.");
    }
}
