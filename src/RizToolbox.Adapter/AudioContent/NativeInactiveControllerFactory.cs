using System.Security.Cryptography;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.AudioContent;

internal sealed record NativeInactiveControllerObservation(long Controller, long ControllerGameObject,
    long ParentGameObject, bool ParentActiveSelf, bool ControllerActiveSelf, bool ControllerActiveInHierarchy,
    bool ParentMatches, long Parameters, long LevelInformation);

/// <summary>Clones only into a newly owned inactive parent. It never activates a controller or edits the source prefab.</summary>
internal sealed class NativeInactiveControllerFactory
{
    private readonly NativeRuntime _api;
    private readonly IMainThreadDispatcher _main;
    private readonly AdapterBuildProfile _profile;
    private readonly IntPtr _module;
    private bool _buildVerified;
    internal NativeInactiveControllerFactory(NativeRuntime api, IMainThreadDispatcher main, AdapterBuildProfile profile, IntPtr module)
        => (_api, _main, _profile, _module) = (api, main, profile, module);

    /// <summary>The caller must retain and revalidate its semantic prefab source in the same main-thread transaction.</summary>
    internal OwnedNativeInactiveController CreateBorrowing(NativeRuntime.RootHandle prefab)
        => CreateCore(prefab, null);

    /// <summary>The same live window is checked before Instantiate and retained by
    /// the owned clone; only this overload can later prepare controlled activation.</summary>
    internal OwnedNativeInactiveController CreateBorrowing(NativeRuntime.RootHandle prefab,
        NativeControllerAwakeLedger.CaptureWindow awakeWindow)
        => CreateCore(prefab, awakeWindow ?? throw new ArgumentNullException(nameof(awakeWindow)));

    private OwnedNativeInactiveController CreateCore(NativeRuntime.RootHandle prefab,
        NativeControllerAwakeLedger.CaptureWindow? awakeWindow)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(prefab); RequireBuild();
        var roots = new InactiveControllerRootRecords(_main);
        NativeRuntime.RootHandle? source = null, sourceGo = null, parent = null, parentTransform = null,
            controller = null, controllerGo = null, controllerTransform = null;
        var unityObject = IntPtr.Zero;
        var nativeAllocationStarted = false;
        var nativeAllocationUnconfirmed = false;
        bool Alive(NativeRuntime.RootHandle? value) => value is not null
            && _api.Unbox<bool>(_api.CallStatic(unityObject, "op_Implicit", A.Ref(value.Target)));
        bool Bool(NativeRuntime.RootHandle owner, string name) => _api.Unbox<bool>(_api.Call(owner.Target, name));
        NativeInactiveControllerObservation Inspect()
        {
            if (!Alive(parent) || !Alive(controller) || !Alive(controllerGo) || !Alive(parentTransform) || !Alive(controllerTransform))
                throw new TargetExpiredException("An owned inactive controller object was destroyed.");
            var observation = new NativeInactiveControllerObservation(controller!.Target.ToInt64(), controllerGo!.Target.ToInt64(),
                parent!.Target.ToInt64(), Bool(parent, "get_activeSelf"), Bool(controllerGo, "get_activeSelf"),
                Bool(controllerGo, "get_activeInHierarchy"), _api.Call(controllerTransform!.Target, "get_parent") == parentTransform!.Target,
                _api.Read<IntPtr>(controller.Target, "LevelParams").ToInt64(), _api.Read<IntPtr>(controller.Target, "LevelInformation").ToInt64());
            if (observation.ParentActiveSelf || observation.ControllerActiveInHierarchy || !observation.ParentMatches)
                throw new InvalidOperationException("Controller escaped its owned inactive parent; activation has not been authorized.");
            return observation;
        }
        void DestroyOwned()
        {
            // No source/sourceGo object is ever disabled or destroyed.
            var failures = new List<Exception>(5);
            foreach (var value in new[] { controllerGo, parent })
                if (value is not null)
                    try { if (Alive(value)) _api.Call(value.Target, "SetActive", A.Value(false)); }
                    catch (Exception error) { failures.Add(error); }
            IntPtr destroy = IntPtr.Zero;
            try { destroy = _api.Method(unityObject, "Destroy", 1, true, new[] { "UnityEngine.Object" }); }
            catch (Exception error) { failures.Add(error); }
            foreach (var value in new[] { controllerGo, parent })
                if (value is not null && destroy != IntPtr.Zero)
                    try { if (Alive(value)) _api.Invoke(destroy, IntPtr.Zero, A.Ref(value.Target)); }
                    catch (Exception error) { failures.Add(error); }
            if (failures.Count != 0) throw new AggregateException("Owned inactive clone destruction request is unconfirmed.", failures);
        }
        bool Destroyed() => !nativeAllocationUnconfirmed && !Alive(controller) && !Alive(controllerGo) && !Alive(parent)
            && !Alive(controllerTransform) && !Alive(parentTransform);
        var owner = new OwnedNativeInactiveController(_main, roots, Inspect, DestroyOwned, Destroyed,
            () => controller is not null ? _api.Root(controller.Target) : throw new TargetExpiredException("No controller root was created."));
        try
        {
            source = roots.Allocate(() => _api.Root(prefab.Target));
            var normal = _api.Class("Assembly-CSharp.dll", "", "NormalModeInLevelController");
            var gameObject = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "GameObject");
            unityObject = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Object");
            var scene = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine.SceneManagement", "Scene");
            if (_api.ObjectClass(source.Target) != normal || !Alive(source))
                throw new UnsupportedSignatureException("Inactive construction requires the exact live NormalMode controller prefab.");
            var baseController = _api.Class("Assembly-CSharp.dll", "", "InLevelController");
            _api.RequireInstanceFieldLayout(baseController, "LevelParams", "InsideLevelParams", 0x20);
            _api.RequireInstanceFieldLayout(baseController, "LevelInformation", "InLevel.LevelInformation", 0x28);
            if (_api.Read<IntPtr>(source.Target, "LevelParams") != IntPtr.Zero || _api.Read<IntPtr>(source.Target, "LevelInformation") != IntPtr.Zero)
                throw new UnsupportedSignatureException("A running or prepared controller cannot be used as a prefab.");
            if (awakeWindow is not null)
                NativeInactiveControllerActivation.RequireZeroAwake(awakeWindow.SnapshotFor(source));
            sourceGo = roots.Allocate(() => _api.Root(_api.Call(source.Target, "get_gameObject")));
            var sceneHandle = _api.Unbox<int>(_api.Call(sourceGo.Target, "get_scene"));
            if (_api.Unbox<bool>(_api.CallStatic(scene, "IsValidInternal", A.Value(sceneHandle))))
                throw new UnsupportedSignatureException("A scene controller instance cannot be cloned as the owned prefab source.");
            var instantiate = _api.InspectDeclaredStaticMethod(unityObject, "Instantiate",
                new[] { "UnityEngine.Object", "UnityEngine.Transform", "System.Boolean" }, "UnityEngine.Object", _module);
            if (instantiate.Entry is not { Rva: 0x153FAA0 })
                throw new UnsupportedSignatureException("Parented non-generic Instantiate has no pinned executable entry at RVA 153FAA0.");

            // Keep the constructor name in this graph, rather than an inline owned Arg:
            // publication capacity rejection deliberately leaves arguments caller-owned.
            var parentName = roots.Allocate(() => _api.Root(_api.String("RizToolbox.LocalController.Pending." + Guid.NewGuid().ToString("N"))));
            nativeAllocationStarted = true; nativeAllocationUnconfirmed = true;
            roots.AllocatePublished(publish => _api.NewExactWithPublication(gameObject, new[] { "System.String" }, publish,
                A.Ref(parentName.Target)), root => parent = root);
            nativeAllocationUnconfirmed = false;
            _api.Call(parent!.Target, "SetActive", A.Value(false));
            if (Bool(parent, "get_activeSelf") || Bool(parent, "get_activeInHierarchy"))
                throw new InvalidOperationException("The new controller parent did not become inactive.");
            parentTransform = roots.Allocate(() => _api.Root(_api.Call(parent.Target, "get_transform")));
            using (var worldPositionStays = A.Value(false))
            {
                nativeAllocationUnconfirmed = true;
                roots.AllocatePublished(publish => _api.InvokeReferenceWithPublication(instantiate.MethodInfo, IntPtr.Zero,
                    pointer => publish(_api.Root(pointer)), A.Ref(source.Target), A.Ref(parentTransform.Target), worldPositionStays), root => controller = root);
                nativeAllocationUnconfirmed = false;
            }
            if (_api.ObjectClass(controller!.Target) != normal || controller.Target == source.Target || !Alive(controller))
                throw new UnsupportedSignatureException("Parented Instantiate did not produce a separate exact live controller.");
            controllerGo = roots.Allocate(() => _api.Root(_api.Call(controller.Target, "get_gameObject")));
            controllerTransform = roots.Allocate(() => _api.Root(_api.Call(controllerGo.Target, "get_transform")));
            if (controllerGo.Target == sourceGo.Target || controllerGo.Target == parent.Target)
                throw new UnsupportedSignatureException("Cloned controller GameObject is not independent of its source and parent.");
            var observed = Inspect();
            if (observed.Parameters != 0 || observed.LevelInformation != 0)
                throw new InvalidOperationException("The inactive clone already contains gameplay state.");
            if (awakeWindow is not null)
            {
                NativeInactiveControllerActivation.RequireZeroAwake(awakeWindow.SnapshotFor(controller));
                owner.AttachActivationBackend(new NativeInactiveControllerActivationBackend(_api, _main, _profile,
                    _module, controller, controllerGo!, parent!, controllerTransform!, parentTransform!), awakeWindow);
            }
            owner.MarkReady(); return owner;
        }
        catch (Exception failure)
        {
            if (!nativeAllocationStarted)
            {
                try { roots.Dispose(); }
                catch (Exception cleanup) { throw new AggregateException("Inactive prefab admission failed and root cleanup is unknown.", failure, cleanup); }
                throw;
            }
            // Give the caller its bounded cleanup responsibility even if construction only
            // partially completed. No source/native pointer is reused after an unrooted allocation.
            owner.MarkCreationFailed(failure.Message);
            throw new NativeInactiveControllerCreationException(owner, failure);
        }
    }

    private void RequireBuild()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || _module == IntPtr.Zero
            || _profile.GameAssemblySha256 != WorkshopChartModule.Fingerprint || _profile.UnityVersion != "2022.3.62f2")
            throw new UnsupportedSignatureException("Inactive controller construction requires the pinned Windows x64 Unity build.");
        if (_buildVerified) return;
        var file = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "GameAssembly.dll");
        using var stream = File.OpenRead(file); using var sha = SHA256.Create();
        if (Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant() != WorkshopChartModule.Fingerprint)
            throw new UnsupportedSignatureException("Inactive controller image fingerprint differs from the supported profile.");
        _buildVerified = true;
    }
}

internal sealed class NativeInactiveControllerCreationException : InvalidOperationException
{
    internal OwnedNativeInactiveController Responsibility { get; }
    internal NativeInactiveControllerCreationException(OwnedNativeInactiveController responsibility, Exception cause)
        : base("Inactive controller creation failed after native allocation; caller retains explicit cleanup responsibility.", cause)
        => Responsibility = responsibility;
}

/// <summary>Owns only inactive clone objects. Disposal requests destruction; a later poll must confirm it before roots are freed.</summary>
internal sealed partial class OwnedNativeInactiveController : IDisposable
{
    private readonly IMainThreadDispatcher _main;
    private readonly InactiveControllerRootRecords _roots;
    private readonly Func<NativeInactiveControllerObservation> _inspect;
    private readonly Action _destroy;
    private readonly Func<bool> _destroyed;
    private readonly Func<NativeRuntime.RootHandle> _borrow;
    private bool _ready, _destroyRequested, _freeAttempted, _closed, _unknown;
    internal string? Failure { get; private set; }
    internal bool IsClosed => _closed;
    internal bool CleanupUnknown => _unknown || _roots.CleanupUnknown || _activation?.CleanupUnknown == true;
    internal bool DestroyRequested => _destroyRequested;
    internal int RootRecords { get { _main.VerifyAccess(); return _roots.Count; } }
    internal OwnedNativeInactiveController(IMainThreadDispatcher main, InactiveControllerRootRecords roots,
        Func<NativeInactiveControllerObservation> inspect, Action destroy, Func<bool> destroyed, Func<NativeRuntime.RootHandle> borrow)
    {
        (_main, _roots, _inspect, _destroy, _destroyed, _borrow) = (main, roots, inspect, destroy, destroyed, borrow);
        roots.AttachResponsibility(this);
    }
    internal void MarkReady() { _main.VerifyAccess(); _ready = true; }
    internal void MarkCreationFailed(string reason) { _main.VerifyAccess(); _ready = false; Failure = reason; }
    internal NativeInactiveControllerObservation Read()
    {
        _main.VerifyAccess();
        if (!_ready || _destroyRequested || _closed || CleanupUnknown || ActivationAttempted)
            throw new ObjectDisposedException(nameof(OwnedNativeInactiveController));
        return _inspect();
    }
    /// <summary>Returns an independent root; it does not grant activation authority.</summary>
    internal NativeRuntime.RootHandle RetainController() { Read(); return _borrow(); }
    internal void RequestDestroy()
    {
        _main.VerifyAccess(); if (_closed || _destroyRequested) return;
        if (ActivationAttempted)
            throw new InvalidOperationException("An attempted active run cannot use inactive-clone destruction or retirement.");
        _destroyRequested = true; _ready = false;
        try { _destroy(); }
        catch (Exception error) { _unknown = true; Failure = error.Message; throw; }
    }
    internal bool TryCompleteDestroy()
    {
        _main.VerifyAccess(); if (_closed) return true;
        if (ActivationAttempted || !_destroyRequested || CleanupUnknown || _freeAttempted) return false;
        if (!_destroyed()) return false;
        _freeAttempted = true;
        try { _roots.Dispose(); _closed = true; return true; }
        catch (Exception error) { _unknown = true; Failure = error.Message; throw; }
    }
    public void Dispose() => RequestDestroy();
}

/// <summary>All direct roots remain process-owned until confirmed destruction and one successful independent free pass.</summary>
internal sealed partial class InactiveControllerRootRecords : IDisposable
{
    private const int MaximumOwners = 32, MaximumRoots = 8;
    private static readonly object ProcessGate = new();
    private static readonly HashSet<InactiveControllerRootRecords> ProcessOwners = new();
    private readonly IMainThreadDispatcher _main;
    private readonly NativeRuntime.RootHandle?[] _roots = new NativeRuntime.RootHandle?[MaximumRoots];
    private OwnedNativeInactiveController? _responsibility;
    private int _count;
    private bool _attempted, _publishing, _allocationUnconfirmed, _cleanupUnknown;
    internal bool CleanupUnknown => _cleanupUnknown || _allocationUnconfirmed;
    internal int Count => _roots.Count(x => x is not null);
    internal InactiveControllerRootRecords(IMainThreadDispatcher main)
    {
        _main = main; _main.VerifyAccess();
        lock (ProcessGate)
        {
            if (ProcessOwners.Count >= MaximumOwners) throw new InvalidOperationException("Inactive-controller process root quota is exhausted.");
            ProcessOwners.Add(this);
        }
    }
    internal NativeRuntime.RootHandle Allocate(Func<NativeRuntime.RootHandle> create)
    {
        _main.VerifyAccess(); if (_attempted) throw new ObjectDisposedException(nameof(InactiveControllerRootRecords));
        if (_publishing || _allocationUnconfirmed) throw new InvalidOperationException("An in-flight or unconfirmed publication cannot accept another allocation.");
        if (_count >= MaximumRoots) throw new InvalidOperationException("Inactive-controller construction exceeds eight root records.");
        var root = create(); _roots[_count++] = root; return root;
    }
    /// <summary>Reserves fixed storage before allocation and records the output before
    /// any caller notification or runtime temporary cleanup. A failed call retains its
    /// root records and process slot; a no-op later cannot turn that failure into proof.</summary>
    internal void AllocatePublished(Action<Action<NativeRuntime.RootHandle>> create, Action<NativeRuntime.RootHandle> notify)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(create); ArgumentNullException.ThrowIfNull(notify);
        if (_attempted) throw new ObjectDisposedException(nameof(InactiveControllerRootRecords));
        if (_publishing || _allocationUnconfirmed) throw new InvalidOperationException("An in-flight or unconfirmed publication cannot be entered again.");
        if (_count >= MaximumRoots) throw new InvalidOperationException("Inactive-controller construction exceeds eight root records.");
        var slot = _count++; var published = false;
        _publishing = true; _allocationUnconfirmed = true;
        try
        {
            create(root => {
                _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(root);
                if (!_publishing || _attempted || published) throw new InvalidOperationException("Allocation publication must be synchronous and occur once.");
                _roots[slot] = root; published = true; // record before notifying
                notify(root);
            });
            if (!published) throw new InvalidOperationException("Allocation returned without publishing its output responsibility.");
            _allocationUnconfirmed = false;
        }
        finally { _publishing = false; }
    }
    internal void AttachResponsibility(OwnedNativeInactiveController responsibility)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(responsibility);
        if (_attempted || _responsibility is not null) throw new InvalidOperationException("Inactive-controller responsibility is already attached or closed.");
        _responsibility = responsibility;
    }
    public void Dispose()
    {
        _main.VerifyAccess(); if (_attempted) { if (CleanupUnknown) throw new InvalidOperationException("Unknown inactive-controller frees are never retried."); return; }
        if (_publishing || _allocationUnconfirmed) throw new InvalidOperationException("Inactive-controller roots cannot be released while allocation is in flight or unconfirmed.");
        var failures = new List<Exception>(MaximumRoots); _attempted = true;
        for (var i = _count - 1; i >= 0; i--)
            if (_roots[i] is { } root) try { root.Dispose(); _roots[i] = null; } catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) { _cleanupUnknown = true; throw new AggregateException("Inactive-controller root cleanup is unknown.", failures); }
        lock (ProcessGate) ProcessOwners.Remove(this);
        _responsibility = null;
    }
}
