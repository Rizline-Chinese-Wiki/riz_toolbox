using System.Security.Cryptography;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.AudioContent;

internal sealed record NativeInactiveControllerActivationGraph(long Controller, long ControllerGameObject,
    long ParentGameObject, long ControllerTransform, long ParentTransform, bool FiveObjectsAlive,
    bool ParentActiveSelf, bool ParentActiveInHierarchy, bool ControllerActiveSelf, bool ControllerActiveInHierarchy,
    long ActualParentTransform, long Parameters, long LevelInformation, int ControllerScene, int ParentScene,
    bool ControllerSceneValidLoaded, bool ParentSceneValidLoaded,
    bool ControllerObjectsAlive = true, bool StagingObjectsAlive = true);
internal sealed record NativeInactiveControllerActivationReceipt(LocalRunSessionIdentity Session, long Controller,
    long Parameters, NativeInactiveControllerActivationGraph Graph, ControllerAwakeLedgerSnapshot Awake,
    ControllerGlobalsSnapshot Globals);

/// <summary>Controlled fault seam. Factory construction always supplies the real
/// backend below with its five existing independently rooted Unity objects.</summary>
internal interface IInactiveControllerActivationBackend
{
    bool CleanupUnknown { get; }
    int RetainedCallArgumentRecords { get; }
    void RequireSupportedBuild();
    NativeInactiveControllerActivationGraph ReadGraph();
    NativeInactiveControllerActivationGraph ReadGraphAfterSceneTransition() => ReadGraph();
    ControllerAwakeLedgerSnapshot ReadAwake(NativeControllerAwakeLedger.CaptureWindow window);
    void CaptureInstalledGlobals(OwnedNativeControllerGlobals globals);
    void DisableClone();
    void DetachClone();
    void ActivateClone();
}

/// <summary>The production implementation holds the exact launch, setter receipt,
/// pre-Instantiate window and globals owner. The controlled seam lets tests exercise
/// mutation ordering and unconfirmed outcomes without claiming native acceptance.</summary>
internal interface IInactiveControllerActivationAuthority
{
    LocalRunSessionIdentity Identity { get; }
    long Controller { get; }
    long Information { get; }
    bool CleanupUnknown { get; }
    bool InstalledGlobalsCaptured { get; }
    void VerifyPending();
    NativeInactiveControllerParameterBindingObservation ReadParameters();
    void NoteActivationAttempt();
    ControllerAwakeLedgerSnapshot ReadAwake();
    ControllerGlobalsSnapshot BaselineGlobals();
    ControllerGlobalsSnapshot InstalledGlobals();
    ControllerGlobalsSnapshot ObserveGlobals();
    void CaptureInstalledGlobals();
    void MarkUnknown(string failure);
}

internal sealed class NativeInactiveControllerActivationAuthority : IInactiveControllerActivationAuthority
{
    private readonly OwnedNativeInactiveController _owner;
    private readonly IInactiveControllerActivationBackend _backend;
    private readonly LocalRunLaunchCommitState _launch;
    private readonly NativeInactiveControllerParameterBinding _binding;
    private readonly NativeControllerAwakeLedger.CaptureWindow _window;
    private readonly OwnedNativeControllerGlobals _globals;
    internal NativeInactiveControllerActivationAuthority(OwnedNativeInactiveController owner,
        IInactiveControllerActivationBackend backend, LocalRunLaunchCommitState launch,
        NativeInactiveControllerParameterBinding binding, NativeControllerAwakeLedger.CaptureWindow window,
        OwnedNativeControllerGlobals globals)
        => (_owner, _backend, _launch, _binding, _window, _globals) = (owner, backend, launch, binding, window, globals);
    public LocalRunSessionIdentity Identity => _launch.Identity;
    public long Controller => _launch.Read().Controller;
    public long Information => _launch.Read().Information;
    public bool CleanupUnknown => _globals.CleanupUnknown || _window.CleanupUnknown;
    public bool InstalledGlobalsCaptured => _globals.InstalledCaptureComplete;
    public void VerifyPending()
    {
        _launch.VerifyPendingOwnership(_launch.Registry);
        var state = _launch.Read(); var current = _launch.Registry.Read(_launch.Session);
        if (!ReferenceEquals(_launch.ParameterBinding, _binding)
            || !_binding.IsBoundFor(_launch.Registry, _launch.Claim, _launch.OwnedBundle, _owner)
            || _window.DisposeRequested || _globals.IsClosed || CleanupUnknown
            || state.EarlyTerminal || state.Phase is LocalRunLaunchCommitPhase.Unknown or LocalRunLaunchCommitPhase.CommittedReady
            || current.ControllerIdentity == 0 || current.ControllerIdentity != state.Controller
            || current.ResultCallClaimed || current.ResultSceneIdentity != 0 || current.Result is not null
            || !ReferenceEquals(_launch.Registry.FindProtectedSession(LocalBoundaryKind.ControllerLevelOver, state.Controller), _launch.Session))
            throw new InvalidOperationException("Activation requires the exact attached setter receipt, pending controller and live initialization owners.");
    }
    public NativeInactiveControllerParameterBindingObservation ReadParameters() { VerifyPending(); return _binding.Read(); }
    public void NoteActivationAttempt() { VerifyPending(); _launch.NoteActivationAttempt(); }
    public ControllerAwakeLedgerSnapshot ReadAwake() => _backend.ReadAwake(_window);
    public ControllerGlobalsSnapshot BaselineGlobals() => _globals.BaselineSnapshot();
    public ControllerGlobalsSnapshot InstalledGlobals() => _globals.InstalledSnapshot();
    public ControllerGlobalsSnapshot ObserveGlobals() => _globals.ObserveCurrent();
    public void CaptureInstalledGlobals() => _backend.CaptureInstalledGlobals(_globals);
    public void MarkUnknown(string failure) => _launch.MarkUnknown(failure);
}

internal sealed partial class OwnedNativeInactiveController
{
    private IInactiveControllerActivationBackend? _activationBackend;
    private NativeControllerAwakeLedger.CaptureWindow? _creationAwakeWindow;
    private NativeInactiveControllerActivation? _activation;
    internal bool ActivationAttempted { get { _main.VerifyAccess(); return _activation?.ActivationAttempted == true; } }
    // Factory-only attachment. Existing process-owned root records strongly retain
    // this owner, which then retains the activation, all inputs and native backend.
    internal void AttachActivationBackend(IInactiveControllerActivationBackend backend,
        NativeControllerAwakeLedger.CaptureWindow window)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(backend); ArgumentNullException.ThrowIfNull(window);
        if (_ready || _activationBackend is not null || _destroyRequested || _closed || CleanupUnknown)
            throw new InvalidOperationException("Activation capability must be attached during exact inactive construction.");
        _activationBackend = backend; _creationAwakeWindow = window;
    }
    internal NativeInactiveControllerActivation CreateActivation(LocalRunLaunchCommitState launch,
        NativeInactiveControllerParameterBinding parameterBinding, NativeControllerAwakeLedger.CaptureWindow awakeWindow,
        OwnedNativeControllerGlobals globals)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(launch); ArgumentNullException.ThrowIfNull(parameterBinding);
        ArgumentNullException.ThrowIfNull(awakeWindow); ArgumentNullException.ThrowIfNull(globals);
        if (!_ready || _destroyRequested || _closed || CleanupUnknown || _activation is not null
            || _activationBackend is null || !ReferenceEquals(awakeWindow, _creationAwakeWindow)
            || awakeWindow.DisposeRequested || globals.IsClosed || globals.CleanupUnknown
            || !ReferenceEquals(launch.ParameterBinding, parameterBinding)
            || !parameterBinding.IsBoundFor(launch.Registry, launch.Claim, launch.OwnedBundle, this))
            throw new InvalidOperationException("Activation requires the exact bound owner and its pre-Instantiate Awake window.");
        launch.VerifyPendingOwnership(launch.Registry);
        // Pure managed prevalidation above leaves caller ownership/once permission
        // unchanged on rejection. Retain every dependency before any backend access.
        return _activation = new(_main, _activationBackend,
            new NativeInactiveControllerActivationAuthority(this, _activationBackend, launch, parameterBinding, awakeWindow, globals));
    }
}

/// <summary>One explicit main-thread native activation transaction. It grants no
/// committed readiness, RunStarted event, result ownership or active retirement.</summary>
internal sealed class NativeInactiveControllerActivation
{
    private readonly IMainThreadDispatcher _main;
    private readonly IInactiveControllerActivationBackend _backend;
    private readonly IInactiveControllerActivationAuthority _authority;
    private NativeInactiveControllerActivationReceipt? _receipt;
    private NativeInactiveControllerActivationGraph? _baseline;
    private ControllerGlobalsSnapshot? _globalBaseline;
    private bool _busy, _attempted, _unknown;
    internal bool ActivationAttempted { get { _main.VerifyAccess(); return _attempted; } }
    internal bool ActivationCompleted { get { _main.VerifyAccess(); return _receipt is not null && !CleanupUnknown; } }
    internal bool CleanupUnknown { get { _main.VerifyAccess(); return _unknown || _backend.CleanupUnknown || _authority.CleanupUnknown; } }
    internal string? Failure { get; private set; }
    internal int RetainedCallArgumentRecords => _backend.RetainedCallArgumentRecords;
    internal NativeInactiveControllerActivation(IMainThreadDispatcher main, IInactiveControllerActivationBackend backend,
        IInactiveControllerActivationAuthority authority)
        => (_main, _backend, _authority) = (main, backend, authority);

    internal NativeInactiveControllerActivationReceipt ActivateOnce()
    {
        Guard(); if (_attempted) throw new InvalidOperationException("Native activation is once-only, including an unknown outcome.");
        _busy = true;
        try
        {
            _backend.RequireSupportedBuild(); RequirePending();
            var parameters = _authority.ReadParameters();
            _baseline = _backend.ReadGraph(); RequireGraph(_baseline, parameters.Parameters, attached: true, active: null);
            if (_baseline.Controller != parameters.Controller || _baseline.ControllerGameObject != parameters.InactiveController.ControllerGameObject
                || _baseline.ParentGameObject != parameters.InactiveController.ParentGameObject)
                throw new InvalidOperationException("Activation roots differ from the actual bound inactive clone.");
            RequireZeroAwake(_authority.ReadAwake());
            _globalBaseline = _authority.BaselineGlobals(); RequireUnchangedGlobals(); RequirePending();
            // All queue/graph/capture admission and activation accounting precede the
            // very first mutation. No action below is retried or undone on failure.
            _authority.NoteActivationAttempt(); _attempted = true;
            _backend.DisableClone();
            RequireGraph(_backend.ReadGraph(), parameters.Parameters, attached: true, active: false);
            RequireZeroAwake(_authority.ReadAwake()); RequireUnchangedGlobals(); RequirePending();
            // Awake passes this clone GO to DontDestroyOnLoadScene. Detach while
            // self-disabled so the eventual first Awake receives a real root GO.
            _backend.DetachClone();
            RequireGraph(_backend.ReadGraph(), parameters.Parameters, attached: false, active: false);
            RequireZeroAwake(_authority.ReadAwake()); RequireUnchangedGlobals(); RequirePending();
            _backend.ActivateClone();
            var graph = _backend.ReadGraph(); RequireGraph(graph, parameters.Parameters, attached: false, active: true);
            RequireMovedScene(graph);
            var awake = RequireOneAwake(_authority.ReadAwake());
            _authority.CaptureInstalledGlobals();
            if (!_authority.InstalledGlobalsCaptured || _authority.CleanupUnknown)
                throw new InvalidOperationException("The six actual Awake globals were not adopted with exact controller Targets.");
            var installed = _authority.InstalledGlobals(); RequireInstalledGlobals(installed);
            var globals = _authority.ObserveGlobals(); RequireInstalledGlobals(globals);
            if (!globals.DelegateIdentities.SequenceEqual(installed.DelegateIdentities))
                throw new InvalidOperationException("Current globals changed from the exact retained Awake delegate Targets before publication.");
            RequirePending();
            return _receipt = new(_authority.Identity, graph.Controller, graph.Parameters, graph, awake, globals);
        }
        catch (Exception error)
        {
            // Metadata/temporary-root failures can also be unknown before a setter.
            // The existing clone process owner retains every dependency either way.
            if (_attempted || _backend.CleanupUnknown || _authority.CleanupUnknown) Fail(error);
            throw;
        }
        finally { _busy = false; }
    }
    internal NativeInactiveControllerActivationReceipt Read()
    {
        Guard(); if (_receipt is null) throw new InvalidOperationException("No completed native activation observation exists.");
        _busy = true;
        try
        {
            _backend.RequireSupportedBuild();
            var graph = _backend.ReadGraph(); RequireGraph(graph, _receipt.Parameters, attached: false, active: true);
            RequireMovedScene(graph); var awake = RequireOneAwake(_authority.ReadAwake());
            var globals = _authority.ObserveGlobals(); RequireInstalledGlobals(globals);
            if (!globals.DelegateIdentities.SequenceEqual(_receipt.Globals.DelegateIdentities))
                throw new InvalidOperationException("The adopted Awake globals changed after activation.");
            return _receipt = _receipt with { Graph = graph, Awake = awake, Globals = globals };
        }
        catch (Exception error) { Fail(error); throw; }
        finally { _busy = false; }
    }
    /// <summary>The staging parent belongs to the origin scene. Its two Unity
    /// objects may die only after this exact completed transition; the three
    /// persistent clone objects and all activation evidence remain mandatory.</summary>
    internal NativeInactiveControllerActivationReceipt ReadAfterSceneTransition(NativeLocalRunSceneTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);
        return ReadAfterSceneTransitionCore(transition.RequireCompleted);
    }
    internal NativeInactiveControllerActivationReceipt ReadAfterControlledSceneTransition(
        Func<NativeLocalRunSceneTransitionReceipt> requireCompleted)
    {
        _main.VerifyAccess();
        if (_backend is NativeInactiveControllerActivationBackend)
            throw new InvalidOperationException("Production activation requires the actual completed scene-transition owner.");
        return ReadAfterSceneTransitionCore(requireCompleted);
    }
    private NativeInactiveControllerActivationReceipt ReadAfterSceneTransitionCore(
        Func<NativeLocalRunSceneTransitionReceipt> requireCompleted)
    {
        Guard(); ArgumentNullException.ThrowIfNull(requireCompleted);
        if (_receipt is null) throw new InvalidOperationException("No completed activation exists for scene handoff.");
        _busy = true;
        try
        {
            var transition = requireCompleted();
            if (transition.Session != _receipt.Session || transition.Controller != _receipt.Controller
                || transition.Origin.Handle != _baseline!.ParentScene || transition.Destination.Handle == transition.Origin.Handle
                || transition.Destination.Name != "Level" || !transition.Destination.Valid || !transition.Destination.Loaded)
                throw new InvalidOperationException("Only this activation's exact completed origin-to-Level transition permits staging expiry.");
            _backend.RequireSupportedBuild(); var graph = _backend.ReadGraphAfterSceneTransition();
            if (graph.StagingObjectsAlive)
            {
                RequireGraph(graph, _receipt.Parameters, attached: false, active: true); RequireMovedScene(graph);
            }
            else
            {
                if (graph.FiveObjectsAlive || !graph.ControllerObjectsAlive || graph.ParentSceneValidLoaded || graph.ParentScene != 0
                    || graph.Controller != _baseline.Controller || graph.ControllerGameObject != _baseline.ControllerGameObject
                    || graph.ControllerTransform != _baseline.ControllerTransform || graph.ParentGameObject != _baseline.ParentGameObject
                    || graph.ParentTransform != _baseline.ParentTransform || graph.ActualParentTransform != 0
                    || graph.ParentActiveSelf || graph.ParentActiveInHierarchy || !graph.ControllerActiveSelf || !graph.ControllerActiveInHierarchy
                    || graph.Parameters != _receipt.Parameters || graph.Controller != _authority.Controller
                    || !graph.ControllerSceneValidLoaded || graph.ControllerScene != _receipt.Graph.ControllerScene
                    || graph.LevelInformation != 0 && graph.LevelInformation != _authority.Information)
                    throw new InvalidOperationException("Origin unload may expire only staging objects; the exact active persistent clone must survive.");
            }
            if (graph.ControllerScene == transition.Destination.Handle)
                throw new InvalidOperationException("The persisted controller unexpectedly moved into the disposable destination scene.");
            var awake = RequireOneAwake(_authority.ReadAwake()); var globals = _authority.ObserveGlobals(); RequireInstalledGlobals(globals);
            if (!globals.DelegateIdentities.SequenceEqual(_receipt.Globals.DelegateIdentities))
                throw new InvalidOperationException("Awake delegate ownership changed during scene transition.");
            return _receipt = _receipt with { Graph = graph, Awake = awake, Globals = globals };
        }
        catch (Exception error) { Fail(error); throw; }
        finally { _busy = false; }
    }
    private void RequirePending()
        => _authority.VerifyPending();
    private void RequireGraph(NativeInactiveControllerActivationGraph graph, long parameters, bool attached, bool? active)
    {
        var baseline = _baseline ?? graph;
        var ids = new[] { graph.Controller, graph.ControllerGameObject, graph.ParentGameObject, graph.ControllerTransform, graph.ParentTransform };
        if (!graph.FiveObjectsAlive || ids.Any(value => value == 0) || ids.Distinct().Count() != 5
            || graph.Controller != baseline.Controller || graph.ControllerGameObject != baseline.ControllerGameObject
            || graph.ParentGameObject != baseline.ParentGameObject || graph.ControllerTransform != baseline.ControllerTransform
            || graph.ParentTransform != baseline.ParentTransform || graph.Controller != _authority.Controller
            || graph.ParentActiveSelf || graph.ParentActiveInHierarchy || graph.Parameters != parameters
            || graph.ActualParentTransform != (attached ? graph.ParentTransform : 0)
            || !graph.ControllerSceneValidLoaded || !graph.ParentSceneValidLoaded
            || active.HasValue && (graph.ControllerActiveSelf != active.Value || graph.ControllerActiveInHierarchy != active.Value)
            || !active.HasValue && graph.ControllerActiveInHierarchy)
            throw new InvalidOperationException("The actual five-object activation graph, active state or exact root-parent relation differs.");
        if (!active.GetValueOrDefault() && graph.LevelInformation != 0)
            throw new InvalidOperationException("The pre-activation controller already contains LevelInformation.");
        if (active == true && graph.LevelInformation != 0 && graph.LevelInformation != _authority.Information)
            throw new InvalidOperationException("Activation observed information outside the actual pre-reserved initialization graph.");
    }
    private void RequireMovedScene(NativeInactiveControllerActivationGraph graph)
    {
        if (graph.ControllerScene == graph.ParentScene || graph.ParentScene != _baseline!.ParentScene)
            throw new InvalidOperationException("The root clone did not actually leave its retained parent's scene after Awake persistence.");
    }
    internal static void RequireZeroAwake(ControllerAwakeLedgerSnapshot awake)
    {
        if (!awake.TimingVerifiable || !awake.ExactNormalClone || awake.NativeCalls != 0 || awake.OriginalCalls != 0
            || awake.SuppressedReentries != 0 || awake.SuppressedWrongThread != 0 || awake.WindowBefore != 0 || awake.WindowAfter != 0
            || awake.CloneBefore != 0 || awake.CloneAfter != 0 || awake.InFlight != 0 || awake.Failures.Count != 0 || awake.CloneRows.Count != 0)
            throw new InvalidOperationException("The same pre-Instantiate Awake window must remain exactly zero before first activation.");
    }
    private ControllerAwakeLedgerSnapshot RequireOneAwake(ControllerAwakeLedgerSnapshot awake)
    {
        if (!awake.TimingVerifiable || !awake.ExactNormalClone || awake.CloneIdentity != _baseline!.Controller
            || awake.NativeCalls != 1 || awake.OriginalCalls != 1 || awake.WindowBefore != 1 || awake.WindowAfter != 1
            || awake.CloneBefore != 1 || awake.CloneAfter != 1 || awake.InFlight != 0 || awake.SuppressedReentries != 0
            || awake.SuppressedWrongThread != 0 || awake.Failures.Count != 0 || awake.CloneRows.Count != 1
            || awake.CloneRows[0] is not { OriginalReturned: true, ContextFinished: true } row
            || row.ReceiverIdentity != _baseline.Controller || row.BeforeOrder <= 0 || row.AfterOrder <= row.BeforeOrder
            || row.BeforeFrame < 0 || row.AfterFrame < row.BeforeFrame)
            throw new InvalidOperationException("Actual first activation did not produce one complete exact-controller Awake original and finally.");
        return awake;
    }
    private void RequireUnchangedGlobals()
    {
        var current = _authority.ObserveGlobals();
        if (!current.IsCurrentOwner || current.OwnerIdentity != _globalBaseline!.OwnerIdentity
            || current.ControllerSet != _globalBaseline.ControllerSet || current.DelegateIdentities.Count != 6
            || !current.DelegateIdentities.SequenceEqual(_globalBaseline.DelegateIdentities))
            throw new InvalidOperationException("Controller globals changed before the first actual activation.");
    }
    private void RequireInstalledGlobals(ControllerGlobalsSnapshot current)
    {
        if (!current.IsCurrentOwner || current.OwnerIdentity != _globalBaseline!.OwnerIdentity || !current.ControllerSet
            || current.DelegateIdentities.Count != 6 || current.DelegateIdentities.Any(identity => identity == 0))
            throw new InvalidOperationException("The exact current manager did not retain complete Awake delegate publication.");
    }
    private void Fail(Exception error)
    {
        _unknown = true; Failure = "Native activation remains retained without retry: " + error.GetType().Name;
        try { _authority.MarkUnknown(Failure); } catch { }
    }
    private void Guard()
    {
        _main.VerifyAccess();
        if (_busy || CleanupUnknown) throw new InvalidOperationException("Activation cannot reenter or retry unknown native responsibility.");
    }
}

/// <summary>Actual NativeRuntime operations over the factory's existing five-root
/// graph. New reads and all three one-shot mutations use bounded publication.</summary>
internal sealed class NativeInactiveControllerActivationBackend : IInactiveControllerActivationBackend
{
    private readonly NativeRuntime _api;
    private readonly IMainThreadDispatcher _main;
    private readonly AdapterBuildProfile _profile;
    private readonly IntPtr _module;
    private readonly NativeRuntime.RootHandle _controller, _gameObject, _parent, _transform, _parentTransform;
    private readonly NativePublicationArgumentRecords _arguments;
    private IntPtr _normalClass, _goClass, _transformClass, _alive, _activeSelf, _activeHierarchy, _parentGetter,
        _gameObjectGetter, _transformGetter, _scene, _validScene, _loadedScene, _setActive, _setParent;
    private bool _verified, _disableAttempted, _detachAttempted, _activateAttempted;
    public bool CleanupUnknown { get; private set; }
    public int RetainedCallArgumentRecords => _arguments.Count;
    internal NativeInactiveControllerActivationBackend(NativeRuntime api, IMainThreadDispatcher main, AdapterBuildProfile profile,
        IntPtr module, NativeRuntime.RootHandle controller, NativeRuntime.RootHandle gameObject, NativeRuntime.RootHandle parent,
        NativeRuntime.RootHandle transform, NativeRuntime.RootHandle parentTransform)
    {
        (_api, _main, _profile, _module, _controller, _gameObject, _parent, _transform, _parentTransform)
            = (api, main, profile, module, controller, gameObject, parent, transform, parentTransform);
        _arguments = new(main);
    }
    public void RequireSupportedBuild()
    {
        _main.VerifyAccess();
        if (CleanupUnknown || _arguments.CleanupUnknown) throw new InvalidOperationException("Native activation cleanup/outcome is unknown.");
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || _module == IntPtr.Zero
            || _profile.GameAssemblySha256 != WorkshopChartModule.Fingerprint || _profile.UnityVersion != "2022.3.62f2")
            throw new UnsupportedSignatureException("Controller activation requires the pinned Windows x64 Unity build.");
        if (_verified) return;
        var file = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "GameAssembly.dll");
        using (var stream = File.OpenRead(file)) using (var sha = SHA256.Create())
            if (Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant() != WorkshopChartModule.Fingerprint)
                throw new UnsupportedSignatureException("Controller activation image fingerprint differs from its profile.");
        Observe(() => {
            _normalClass = _api.Class("Assembly-CSharp.dll", "", "NormalModeInLevelController");
            _goClass = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "GameObject");
            _transformClass = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Transform");
            var component = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Component");
            var unityObject = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Object");
            var scene = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine.SceneManagement", "Scene");
            _alive = Method(unityObject, "op_Implicit", new[] { "UnityEngine.Object" }, "System.Boolean", isStatic: true);
            _activeSelf = Method(_goClass, "get_activeSelf", Array.Empty<string>(), "System.Boolean");
            _activeHierarchy = Method(_goClass, "get_activeInHierarchy", Array.Empty<string>(), "System.Boolean");
            _parentGetter = Method(_transformClass, "get_parent", Array.Empty<string>(), "UnityEngine.Transform");
            _gameObjectGetter = Method(component, "get_gameObject", Array.Empty<string>(), "UnityEngine.GameObject", rva: 0x1536400);
            _transformGetter = Method(_goClass, "get_transform", Array.Empty<string>(), "UnityEngine.Transform");
            _scene = Method(_goClass, "get_scene", Array.Empty<string>(), "UnityEngine.SceneManagement.Scene");
            _validScene = Method(scene, "IsValidInternal", new[] { "System.Int32" }, "System.Boolean", isStatic: true, rva: 0x155A8B0);
            _loadedScene = Method(scene, "GetIsLoadedInternal", new[] { "System.Int32" }, "System.Boolean", isStatic: true, rva: 0x155A7F0);
            _setActive = Method(_goClass, "SetActive", new[] { "System.Boolean" }, "System.Void", rva: 0x1538AB0);
            _setParent = Method(_transformClass, "SetParent", new[] { "UnityEngine.Transform", "System.Boolean" }, "System.Void", rva: 0x1560D80);
            var declaring = _api.Class("Assembly-CSharp.dll", "", "InLevelController");
            _api.RequireInstanceFieldLayout(declaring, "LevelParams", "InsideLevelParams", 0x20);
            _api.RequireInstanceFieldLayout(declaring, "LevelInformation", "InLevel.LevelInformation", 0x28);
            _verified = true; return true;
        });
    }
    private IntPtr Method(IntPtr declaring, string name, string[] parameters, string returns, bool isStatic = false, long? rva = null)
    {
        var method = isStatic ? _api.InspectDeclaredStaticMethod(declaring, name, parameters, returns, _module)
            : _api.InspectDeclaredMethod(declaring, name, parameters, returns, _module);
        if (method.Entry is null || rva.HasValue && method.Entry.Rva != rva.Value || method.GenericMethod || method.InflatedMethod
            || ((method.Flags & 0x10) != 0) != isStatic || (method.Flags & 0x400) != 0)
            throw new UnsupportedSignatureException("Activation method has no exact non-generic executable metadata: " + name);
        return method.MethodInfo;
    }
    public NativeInactiveControllerActivationGraph ReadGraph()
        => ReadGraphCore(false);
    public NativeInactiveControllerActivationGraph ReadGraphAfterSceneTransition()
        => ReadGraphCore(true);
    private NativeInactiveControllerActivationGraph ReadGraphCore(bool allowExpiredStaging)
    {
        RequireSupportedBuild();
        return Observe<NativeInactiveControllerActivationGraph>(() => {
            var controller = _controller.Target; var go = _gameObject.Target; var parent = _parent.Target;
            var transform = _transform.Target; var parentTransform = _parentTransform.Target;
            if (_api.ObjectClass(controller) != _normalClass || _api.ObjectClass(go) != _goClass || _api.ObjectClass(parent) != _goClass
                || _api.ObjectClass(transform) != _transformClass || _api.ObjectClass(parentTransform) != _transformClass)
                throw new UnsupportedSignatureException("Activation object classes differ from the five exact retained native roots.");
            var alive = new[] { controller, go, parent, transform, parentTransform }
                .Select(value => _api.InvokeValueWithPublication<bool>(_alive, IntPtr.Zero, A.Ref(value))).ToArray();
            if (!alive[0] || !alive[1] || !alive[3] || !allowExpiredStaging && alive.Any(value => !value)
                || alive[2] != alive[4])
                throw new TargetExpiredException("A clone object or an unpaired staging Unity object was destroyed.");
            var stagingAlive = alive[2];
            if (Reference(_gameObjectGetter, controller) != go || Reference(_gameObjectGetter, transform) != go
                || Reference(_transformGetter, go) != transform
                || stagingAlive && (Reference(_gameObjectGetter, parentTransform) != parent || Reference(_transformGetter, parent) != parentTransform))
                throw new InvalidOperationException("The actual controller/GO/transform back references differ from the owned graph.");
            var childScene = _api.InvokeSceneHandleWithPublication(_scene, go);
            var parentScene = stagingAlive ? _api.InvokeSceneHandleWithPublication(_scene, parent) : 0;
            return new(controller.ToInt64(), go.ToInt64(), parent.ToInt64(), transform.ToInt64(), parentTransform.ToInt64(), stagingAlive,
                stagingAlive && _api.InvokeValueWithPublication<bool>(_activeSelf, parent), stagingAlive && _api.InvokeValueWithPublication<bool>(_activeHierarchy, parent),
                _api.InvokeValueWithPublication<bool>(_activeSelf, go), _api.InvokeValueWithPublication<bool>(_activeHierarchy, go),
                Reference(_parentGetter, transform).ToInt64(), _api.ReadWithPublication<IntPtr>(controller, "LevelParams").ToInt64(),
                _api.ReadWithPublication<IntPtr>(controller, "LevelInformation").ToInt64(), childScene, parentScene,
                SceneLoaded(childScene), stagingAlive && SceneLoaded(parentScene), true, stagingAlive);
        });
    }
    private bool SceneLoaded(int handle) => Value<bool>(_validScene, A.Value(handle))
        && Value<bool>(_loadedScene, A.Value(handle));
    private IntPtr Reference(IntPtr method, IntPtr receiver)
    {
        var result = IntPtr.Zero;
        _api.InvokeReferenceWithPublication(method, receiver, value => result = value);
        return result; // Identity comparison only; the five owned roots remain held.
    }
    public ControllerAwakeLedgerSnapshot ReadAwake(NativeControllerAwakeLedger.CaptureWindow window)
    { RequireSupportedBuild(); return Observe(() => window.SnapshotFor(_controller)); }
    public void CaptureInstalledGlobals(OwnedNativeControllerGlobals globals)
    { RequireSupportedBuild(); Observe(() => { globals.CaptureInstalledFor(_controller); return true; }); }
    public void DisableClone()
    {
        RequireSupportedBuild(); if (_disableAttempted) throw new InvalidOperationException("Clone disable is one-shot.");
        _disableAttempted = true; Invoke(_setActive, _gameObject.Target, A.Value(false));
    }
    public void DetachClone()
    {
        RequireSupportedBuild(); if (!_disableAttempted || _detachAttempted) throw new InvalidOperationException("Root detachment is one-shot after disable.");
        _detachAttempted = true; Invoke(_setParent, _transform.Target, A.Ref(IntPtr.Zero), A.Value(false));
    }
    public void ActivateClone()
    {
        RequireSupportedBuild(); if (!_detachAttempted || _activateAttempted) throw new InvalidOperationException("Root clone activation is one-shot after detach.");
        _activateAttempted = true; Invoke(_setActive, _gameObject.Target, A.Value(true));
    }
    private void Invoke(IntPtr method, IntPtr receiver, params A[] arguments)
    {
        _arguments.Begin(arguments);
        try { _api.InvokeVoidWithPublication(method, receiver, arguments); _arguments.Complete(); }
        catch { _arguments.Fail(); CleanupUnknown = true; throw; }
    }
    private T Value<T>(IntPtr method, params A[] arguments) where T : struct
    {
        _arguments.Begin(arguments);
        try { var value = _api.InvokeValueWithPublication<T>(method, IntPtr.Zero, arguments); _arguments.Complete(); return value; }
        catch { _arguments.Fail(); CleanupUnknown = true; throw; }
    }
    private T Observe<T>(Func<T> read)
    { try { return read(); } catch { CleanupUnknown = true; throw; } }
}
