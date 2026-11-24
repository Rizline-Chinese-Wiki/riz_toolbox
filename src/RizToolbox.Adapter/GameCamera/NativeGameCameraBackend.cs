using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.GameCamera;

/// <summary>Pinned game-owned camera rig scalars. No menu camera, global camera scan or field writes.</summary>
internal sealed class NativeGameCameraBackend : IGameCameraBackend
{
    internal const string Fingerprint = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79";
    internal const string ValidationEvidence = "Experimental pinned Windows x64 camera rig local position X and local scale X. CameraControl.SetXPosition consumes scale/xPosition and writes its own Transform.localPosition; SetScale multiplies each component of the pinned B vector by scale and writes Transform.localScale, so this scalar is not a uniform zoom. The actual Camera comes from the current LevelInformation.noteJudgeControl._camera, which NoteJudgeControl.Start caches from Camera.main, not InputControl or GameManager.mainCamera. Exact fields, metadata, complete method bodies, and live scene/run ownership are checked for the CameraControl and cached Camera branches of the same current LevelInformation graph. Camera-to-rig transform ancestry is not established by the pinned game methods and is not an admission condition. Position and scale may affect input projection and gameplay. CameraControl.Update rewrites these properties each frame; host tick/camera/render callback order and sustained visibility/restoration remain unverified even with per-tick reapplication.";
    internal static GameCameraScalarProperty LocalX { get; } = new(GameCameraRoles.RigLocalPositionX,
        "local-position-x", "unity-local-unit", null, null,
        "Finite Single only; no game-specific minimum/maximum established. This is a local-space coordinate, not a chart fraction or SI distance.",
        "Current NormalModeInLevelController.LevelInformation.cameraControl.transform.localPosition.x; the same LevelInformation owns the NoteJudgeControl that holds the cached Camera.",
        "CameraControl.SetXPosition@3E3060 writes localPosition=(scale*xPosition,-2.4,0) through Transform.set_localPosition@1561CD0. Getter@1561640. Each scalar write freshly reads Vector3 and preserves Y/Z. CameraControl.Update overwrites X every frame; host/camera/render callback order and sustained visibility are unverified. Position may affect input projection/gameplay.",
        ExternalWritePolicy.AdoptAndReapply);
    internal static GameCameraScalarProperty LocalScaleX { get; } = new(GameCameraRoles.RigLocalScaleX,
        "local-scale-x", "dimensionless", null, null,
        "Finite Single only; no game-specific minimum/maximum established. This is one localScale component, not a uniform zoom or multiplier for the other axes.",
        "Current NormalModeInLevelController.LevelInformation.cameraControl.transform.localScale.x; the same LevelInformation owns the NoteJudgeControl that holds the cached Camera.",
        "CameraControl.SetScale@3E2E00 writes localScale by multiplying each component of the pinned B vector by scale through Transform.set_localScale@1561E10. Getter@1561780. Each scalar write freshly reads Vector3, rechecks current graph ownership and preserves Y/Z. CameraControl.Update overwrites scale every frame; host/camera/render callback order and sustained visibility are unverified. Scale may affect input projection/gameplay.",
        ExternalWritePolicy.AdoptAndReapply);
    public IReadOnlyList<GameCameraScalarProperty> Properties { get; } = Array.AsReadOnly(new[] { LocalX, LocalScaleX });

    internal static IReadOnlyList<AdapterSymbolRequirement> Requirements { get; } = Array.AsReadOnly(new[] {
        Field("camera.controller.info", "", "InLevelController", "LevelInformation", "InLevel.LevelInformation"),
        Field("camera.info.chart", "InLevel", "LevelInformation", "chart", "InLevel.Chart"),
        Field("camera.info.progress", "InLevel", "LevelInformation", "progressControl", "InLevel.ProgressControl"),
        Field("camera.info.rig", "InLevel", "LevelInformation", "cameraControl", "InLevel.CameraControl"),
        Field("camera.info.judge", "InLevel", "LevelInformation", "noteJudgeControl", "InLevel.NoteJudgeControl"),
        Field("camera.rig.info", "InLevel", "CameraControl", "levelInformation", "InLevel.LevelInformation"),
        Field("camera.judge.info", "InLevel", "NoteJudgeControl", "levelInformation", "InLevel.LevelInformation"),
        Field("camera.judge.camera", "InLevel", "NoteJudgeControl", "_camera", "UnityEngine.Camera"),
        Method("camera.rig.consumer", "Assembly-CSharp.dll", "InLevel", "CameraControl", "SetXPosition", "System.Void"),
        Method("camera.rig.scale-consumer", "Assembly-CSharp.dll", "InLevel", "CameraControl", "SetScale", "System.Void"),
        Method("camera.judge.camera-origin", "Assembly-CSharp.dll", "InLevel", "NoteJudgeControl", "Start", "System.Void"),
        Method("camera.transform", "UnityEngine.CoreModule.dll", "UnityEngine", "Component", "get_transform", "UnityEngine.Transform"),
        Method("camera.local-position.get", "UnityEngine.CoreModule.dll", "UnityEngine", "Transform", "get_localPosition", "UnityEngine.Vector3"),
        Method("camera.local-position.set", "UnityEngine.CoreModule.dll", "UnityEngine", "Transform", "set_localPosition", "System.Void", false, "UnityEngine.Vector3"),
        Method("camera.local-scale.get", "UnityEngine.CoreModule.dll", "UnityEngine", "Transform", "get_localScale", "UnityEngine.Vector3"),
        Method("camera.local-scale.set", "UnityEngine.CoreModule.dll", "UnityEngine", "Transform", "set_localScale", "System.Void", false, "UnityEngine.Vector3"),
        Method("camera.scene.current", "UnityEngine.CoreModule.dll", "UnityEngine.SceneManagement", "SceneManager", "GetActiveScene", "UnityEngine.SceneManagement.Scene", true),
        Method("camera.behaviour.active", "UnityEngine.CoreModule.dll", "UnityEngine", "Behaviour", "get_isActiveAndEnabled", "System.Boolean"),
        Method("camera.controllers.scan", "UnityEngine.CoreModule.dll", "UnityEngine", "Resources", "FindObjectsOfTypeAll", "UnityEngine.Object[]", true, "System.Type")
    });
    private static AdapterSymbolRequirement Field(string id, string ns, string type, string name, string expected)
        => new(id, "Assembly-CSharp.dll", ns, type, AdapterSymbolKind.Field, name) { ExpectedType = expected };
    private static AdapterSymbolRequirement Method(string id, string assembly, string ns, string type, string name,
        string expected, bool isStatic = false, params string[] parameters)
        => new(id, assembly, ns, type, AdapterSymbolKind.Method, name, isStatic, Array.AsReadOnly(parameters)) { ExpectedType = expected };

    private readonly NativeRuntime _api;
    private readonly IMainThreadDispatcher _main;
    private readonly Func<long, long, string, long, bool> _matchesRun;
    private readonly Dictionary<string, NativeExactReadField> _fields = new(StringComparer.Ordinal);
    private readonly IntPtr _controller, _transform, _resourcesFind, _getTransform, _getGo, _getScene, _getActive,
        _isActiveAndEnabled, _getActiveScene, _implicit, _sceneValid, _sceneLoaded, _sceneName, _getId;
    private readonly NativeCameraPositionAccessors _position;
    private readonly NativeCameraScaleAccessors _scale;

    internal NativeGameCameraBackend(NativeRuntime api, IMainThreadDispatcher main, ILogger log,
        Func<long, long, string, long, bool> matchesRun)
    {
        _api = api; _main = main; _matchesRun = matchesRun ?? throw new ArgumentNullException(nameof(matchesRun));
        _main.VerifyAccess(); var module = VerifyLoadedBuild();
        IntPtr Game(string type, string ns = "InLevel") => api.Class("Assembly-CSharp.dll", ns, type);
        IntPtr Unity(string type, string ns = "UnityEngine") => api.Class("UnityEngine.CoreModule.dll", ns, type);
        _controller = Game("NormalModeInLevelController", ""); var baseController = Game("InLevelController", "");
        var info = Game("LevelInformation"); var progress = Game("ProgressControl");
        var rig = Game("CameraControl"); var judge = Game("NoteJudgeControl");
        Add("controller.info", baseController, "LevelInformation", "InLevel.LevelInformation", 0x28);
        Add("info.chart", info, "chart", "InLevel.Chart", 0x88);
        Add("info.progress", info, "progressControl", "InLevel.ProgressControl", 0x28);
        Add("info.rig", info, "cameraControl", "InLevel.CameraControl", 0x78);
        Add("info.judge", info, "noteJudgeControl", "InLevel.NoteJudgeControl", 0x58);
        Add("info.loaded", info, "loadOver", "System.Boolean", 0xBE);
        Add("info.over", info, "levelOver", "System.Boolean", 0xC0);
        Add("info.crashed", info, "crashed", "System.Boolean", 0xC1);
        Add("progress.ready", progress, "timeReady", "System.Boolean", 0x65);
        Add("rig.info", rig, "levelInformation", "InLevel.LevelInformation", 0x20);
        Add("judge.info", judge, "levelInformation", "InLevel.LevelInformation", 0x20);
        Add("judge.camera", judge, "_camera", "UnityEngine.Camera", 0x70);
        var component = Unity("Component"); var gameObject = Unity("GameObject"); var unityObject = Unity("Object");
        var scene = Unity("Scene", "UnityEngine.SceneManagement"); var sceneManager = Unity("SceneManager", "UnityEngine.SceneManagement");
        var behaviour = Unity("Behaviour"); _transform = Unity("Transform");
        _resourcesFind = Bind(Unity("Resources"), "UnityEngine", "Resources", "FindObjectsOfTypeAll", true, "UnityEngine.Object[]", "System.Type");
        _getTransform = Pinned(component, "get_transform", Array.Empty<string>(), "UnityEngine.Transform", module, 0x1536440);
        _position = NativeCameraPositionAccessors.Bind(api, _transform);
        Pinned(_transform, "get_localPosition", Array.Empty<string>(), "UnityEngine.Vector3", module, 0x1561640);
        Pinned(_transform, "set_localPosition", new[] { "UnityEngine.Vector3" }, "System.Void", module, 0x1561CD0);
        _scale = NativeCameraScaleAccessors.Bind(api, _transform);
        Pinned(_transform, "get_localScale", Array.Empty<string>(), "UnityEngine.Vector3", module, 0x1561780);
        Pinned(_transform, "set_localScale", new[] { "UnityEngine.Vector3" }, "System.Void", module, 0x1561E10);
        Pinned(rig, "SetXPosition", Array.Empty<string>(), "System.Void", module, 0x3E3060);
        Pinned(rig, "SetScale", Array.Empty<string>(), "System.Void", module, 0x3E2E00);
        Pinned(judge, "Start", Array.Empty<string>(), "System.Void", module, 0x3FD3E0);
        _getGo = Bind(component, "UnityEngine", "Component", "get_gameObject", false, "UnityEngine.GameObject");
        _getScene = Bind(gameObject, "UnityEngine", "GameObject", "get_scene", false, "UnityEngine.SceneManagement.Scene");
        _getActive = Bind(gameObject, "UnityEngine", "GameObject", "get_activeInHierarchy", false, "System.Boolean");
        _isActiveAndEnabled = Bind(behaviour, "UnityEngine", "Behaviour", "get_isActiveAndEnabled", false, "System.Boolean");
        _getActiveScene = Bind(sceneManager, "UnityEngine.SceneManagement", "SceneManager", "GetActiveScene", true, "UnityEngine.SceneManagement.Scene");
        _implicit = Bind(unityObject, "UnityEngine", "Object", "op_Implicit", true, "System.Boolean", "UnityEngine.Object");
        _getId = Bind(unityObject, "UnityEngine", "Object", "GetInstanceID", false, "System.Int32");
        _sceneValid = Bind(scene, "UnityEngine.SceneManagement", "Scene", "IsValidInternal", true, "System.Boolean", "System.Int32");
        _sceneLoaded = Bind(scene, "UnityEngine.SceneManagement", "Scene", "GetIsLoadedInternal", true, "System.Boolean", "System.Int32");
        _sceneName = Bind(scene, "UnityEngine.SceneManagement", "Scene", "GetNameInternal", true, "System.String", "System.Int32");
    }
    private void Add(string key, IntPtr cls, string name, string type, int offset)
        => _fields.Add(key, _api.InspectExactDeclaredReadField(cls, name, type, false, offset));
    private IntPtr Bind(IntPtr cls, string ns, string type, string name, bool isStatic, string returns, params string[] parameters)
    {
        var method = _api.Method(cls, name, parameters.Length, isStatic, parameters);
        if (!_api.IsMethod(method, ns, type, name) || _api.DescribeReturnType(method) != returns)
            throw new UnsupportedSignatureException("Camera accessor declared receiver or return type differs: " + name);
        return method;
    }
    private IntPtr Pinned(IntPtr cls, string name, string[] parameters, string returns, IntPtr module, long rva)
    {
        var method = _api.InspectDeclaredMethod(cls, name, parameters, returns, module);
        if (method.Entry?.Rva != rva) throw new UnsupportedSignatureException("Camera method differs from its pinned MethodInfo RVA: " + name);
        return method.MethodInfo;
    }

    public IEnumerable<GameCameraCandidate> Discover(GameCameraRun run, int maximumScanned)
    {
        _main.VerifyAccess(); if (maximumScanned < 1) throw new ArgumentOutOfRangeException(nameof(maximumScanned));
        foreach (var property in Properties)
        {
            // Each candidate transfers a complete independent graph exactly once. Sharing a
            // graph would let one property's retirement release the other property's roots.
            var graph = CaptureRun(run);
            try
            {
                var rigId = _api.InvokeValueWithPublication<int>(_getId, graph.Handle(graph.Rig).Target);
                var cameraId = _api.InvokeValueWithPublication<int>(_getId, graph.Handle(graph.Camera).Target);
                var binding = new NativeBinding(this, graph, property == LocalScaleX);
                var alias = $"game.camera/rig/{rigId}/{property.Property}";
                yield return new(run.RunId + ":" + run.SceneGeneration + ":" + rigId + ":" + cameraId + ":" + property.Property,
                    alias, "camera-rig:" + rigId + ":" + property.Property, run,
                    new(Scene: run.Scene, Role: property.Role, ContentId: run.ChartId, ResourceId: property.Role,
                        Description: $"Current camera rig {rigId} and cached Camera {cameraId} belong to the same validated LevelInformation/run graph. May affect input projection/gameplay."),
                    property, new[] { alias, "game.camera/current/" + property.Property },
                    binding.Read, binding.Write, binding.Current, binding.Current, graph.Retain);
            }
            finally { graph.DisposeUnlessRetained(); }
        }
    }
    private NativeGraph CaptureRun(GameCameraRun run)
    {
        if (!CurrentScene(run.Scene)) throw new TargetExpiredException("The active scene differs from the observed camera run.");
        var candidates = new List<NativeRuntime.RootHandle>();
        try
        {
            using var type = _api.Root(_api.Type(_controller));
            using var array = RootInvocation(_resourcesFind, null, A.Ref(type.Target));
            foreach (var pointer in ReadArray(array, 64))
            {
                var root = _api.Root(pointer);
                try
                {
                    if (_api.ObjectClass(root.Target) == _controller && Loaded(root) && Active(root))
                    { candidates.Add(root); root = null!; }
                }
                finally { root?.Dispose(); }
            }
            if (candidates.Count != 1) throw new InvalidOperationException(candidates.Count == 0
                ? "TargetMissing: no active current NormalMode controller." : "AmbiguousTarget: multiple active current NormalMode controllers.");
            var graph = new NativeGraph(this, run);
            try
            {
                graph.Object(candidates[0].Target, _controller);
                graph.Reference(0, "controller.info");
                if (!_matchesRun(graph.Handle(1).Target.ToInt64(), graph.Handle(0).Target.ToInt64(), run.RunId, run.SceneGeneration))
                    throw new TargetExpiredException("Concrete camera controller/information is not the observed run.");
                graph.Reference(1, "info.chart"); var progress = graph.Reference(1, "info.progress");
                graph.RigControl = graph.Reference(1, "info.rig"); var judge = graph.Reference(1, "info.judge");
                graph.ExpectReference(graph.RigControl, "rig.info", 1); graph.ExpectReference(judge, "judge.info", 1);
                graph.Camera = graph.Reference(judge, "judge.camera");
                graph.Rig = graph.MethodReference(graph.RigControl, _getTransform, _transform);
                graph.CameraTransform = graph.MethodReference(graph.Camera, _getTransform, _transform);
                graph.Expect(1, "info.loaded", true); graph.Expect(1, "info.over", false);
                graph.Expect(1, "info.crashed", false); graph.Expect(progress, "progress.ready", true);
                if (!graph.Current(out var failedCheck)) throw new TargetExpiredException(
                    "The camera rig or cached Camera is not in the current validated scene/run graph; failed check: " + failedCheck + ".");
                return graph;
            }
            catch { graph.Dispose(); throw; }
        }
        finally { foreach (var root in candidates) root.Dispose(); }
    }
    private IEnumerable<IntPtr> ReadArray(NativeRuntime.RootHandle array, int maximum)
    {
        var length = _api.Unbox<int>(_api.Call(array.Target, "get_Length"));
        if (length < 0 || length > maximum) throw new InvalidOperationException("Complete camera controller source exceeds its bound.");
        foreach (var pointer in _api.ArrayItems(array.Target)) if (pointer != IntPtr.Zero) yield return pointer;
    }
    private NativeRuntime.RootHandle RootInvocation(IntPtr method, NativeRuntime.RootHandle? receiver, params NativeRuntime.Arg[] arguments)
    {
        NativeRuntime.RootHandle? result = null;
        try
        {
            _api.InvokeReferenceWithPublication(method, receiver?.Target ?? IntPtr.Zero, pointer => {
                if (pointer != IntPtr.Zero) result = _api.Root(pointer);
            }, arguments);
            return result ?? throw new TargetExpiredException("Camera graph method returned null.");
        }
        catch { result?.Dispose(); throw; }
    }
    private bool Alive(NativeRuntime.RootHandle root)
        => _api.InvokeValueWithPublication<bool>(_implicit, IntPtr.Zero, A.Ref(root.Target));
    private bool Active(NativeRuntime.RootHandle root)
        => Active(root, out _);
    private bool Active(NativeRuntime.RootHandle root, out string failedCheck)
    {
        failedCheck = "";
        if (!Alive(root)) { failedCheck = "object-dead"; return false; }
        using var go = RootInvocation(_getGo, root);
        if (!_api.InvokeValueWithPublication<bool>(_getActive, go.Target))
        { failedCheck = "gameobject-inactive"; return false; }
        if (!_api.InvokeValueWithPublication<bool>(_isActiveAndEnabled, root.Target))
        { failedCheck = "behaviour-disabled"; return false; }
        return true;
    }
    private bool Loaded(NativeRuntime.RootHandle root)
        => Loaded(root, out _);
    private bool Loaded(NativeRuntime.RootHandle root, out string failedCheck)
    {
        failedCheck = "";
        if (!Alive(root)) { failedCheck = "object-dead"; return false; }
        using var go = RootInvocation(_getGo, root);
        var scene = _api.InvokeSceneHandleWithPublication(_getScene, go.Target);
        if (!_api.InvokeValueWithPublication<bool>(_sceneValid, IntPtr.Zero, A.Value(scene)))
        { failedCheck = "object-scene-invalid"; return false; }
        if (!_api.InvokeValueWithPublication<bool>(_sceneLoaded, IntPtr.Zero, A.Value(scene)))
        { failedCheck = "object-scene-unloaded"; return false; }
        return true;
    }
    private bool CurrentScene(string expected)
        => CurrentScene(expected, out _);
    private bool CurrentScene(string expected, out string failedCheck)
    {
        failedCheck = "";
        var scene = _api.InvokeSceneHandleWithPublication(_getActiveScene, IntPtr.Zero);
        if (!_api.InvokeValueWithPublication<bool>(_sceneValid, IntPtr.Zero, A.Value(scene)))
        { failedCheck = "active-scene-invalid"; return false; }
        if (!_api.InvokeValueWithPublication<bool>(_sceneLoaded, IntPtr.Zero, A.Value(scene)))
        { failedCheck = "active-scene-unloaded"; return false; }
        using var name = RootInvocation(_sceneName, null, A.Value(scene));
        if (_api.TextWithPublication(name.Target) != expected)
        { failedCheck = "active-scene-name"; return false; }
        return true;
    }

    private sealed class NativeGraph : IDisposable
    {
        private readonly NativeGameCameraBackend _owner;
        private readonly List<(NativeRuntime.RootHandle Root, IntPtr Class)> _nodes = new();
        private readonly List<(int Owner, string Field, int Target)> _links = new();
        private readonly List<(int Owner, IntPtr Method, int Target)> _methodLinks = new();
        private readonly List<(int Node, string Field, bool Value)> _flags = new();
        private bool _disposed, _retained;
        internal readonly GameCameraRun Run;
        internal int RigControl, Camera, Rig, CameraTransform;
        internal NativeGraph(NativeGameCameraBackend owner, GameCameraRun run) { _owner = owner; Run = run; }
        internal NativeRuntime.RootHandle Handle(int node) => _nodes[node].Root;
        internal int Object(IntPtr pointer, IntPtr expectedClass)
        {
            var root = _owner._api.Root(pointer);
            try { return Adopt(root, expectedClass); }
            catch { root.Dispose(); throw; }
        }
        private int Adopt(NativeRuntime.RootHandle root, IntPtr expectedClass)
        {
            if (_owner._api.ObjectClass(root.Target) != expectedClass) throw new UnsupportedSignatureException("Camera graph reference has a foreign actual class.");
            var index = _nodes.Count; _nodes.Add((root, expectedClass)); return index;
        }
        internal int Reference(int source, string field)
        {
            NativeRuntime.RootHandle? result = null;
            try
            {
                var metadata = _owner._fields[field];
                _owner._api.PublishExactReferenceField(Handle(source), metadata, pointer => { if (pointer != IntPtr.Zero) result = _owner._api.Root(pointer); });
                if (result is null) throw new TargetExpiredException("Camera graph field is null: " + field);
                var node = Adopt(result, metadata.FieldClass); result = null; _links.Add((source, field, node)); return node;
            }
            finally { result?.Dispose(); }
        }
        internal int MethodReference(int source, IntPtr method, IntPtr expectedClass)
        {
            var root = _owner.RootInvocation(method, Handle(source));
            try { var node = Adopt(root, expectedClass); _methodLinks.Add((source, method, node)); return node; }
            catch { root.Dispose(); throw; }
        }
        internal void ExpectReference(int source, string field, int target)
        { if (Handle(Reference(source, field)).Target != Handle(target).Target) throw new TargetExpiredException("Camera owner back-reference differs."); }
        internal void Expect(int node, string field, bool value)
        {
            if (_owner._api.ReadExactField<bool>(Handle(node), _owner._fields[field]) != value)
                throw new TargetExpiredException("The camera run/progress is not ready or is terminal.");
            _flags.Add((node, field, value));
        }
        internal bool Current()
            => Current(out _);
        internal bool Current(out string failedCheck)
        {
            failedCheck = "";
            if (_disposed) { failedCheck = "graph-disposed"; return false; }
            if (!_owner._matchesRun(Handle(1).Target.ToInt64(), Handle(0).Target.ToInt64(), Run.RunId, Run.SceneGeneration))
            { failedCheck = "run-association-entry"; return false; }
            if (!_owner.CurrentScene(Run.Scene, out failedCheck)) return false;
            for (var index = 0; index < _nodes.Count; index++)
                if (_owner._api.ObjectClass(_nodes[index].Root.Target) != _nodes[index].Class)
                { failedCheck = "actual-class-node-" + index; return false; }
            foreach (var node in new[] { 0, RigControl, Camera })
            {
                var role = node == 0 ? "controller" : node == RigControl ? "rig-control" : "camera";
                if (!_owner.Loaded(Handle(node), out failedCheck))
                { failedCheck = role + "." + failedCheck; return false; }
                if (!_owner.Active(Handle(node), out failedCheck))
                { failedCheck = role + "." + failedCheck; return false; }
            }
            foreach (var link in _links)
            {
                NativeRuntime.RootHandle? actual = null;
                try
                {
                    _owner._api.PublishExactReferenceField(Handle(link.Owner), _owner._fields[link.Field], pointer => { if (pointer != IntPtr.Zero) actual = _owner._api.Root(pointer); });
                    if (actual is null || actual.Target != Handle(link.Target).Target)
                    { failedCheck = "field-reference." + link.Field; return false; }
                }
                finally { actual?.Dispose(); }
            }
            foreach (var link in _methodLinks)
            {
                using var actual = _owner.RootInvocation(link.Method, Handle(link.Owner));
                if (actual.Target != Handle(link.Target).Target)
                { failedCheck = link.Owner == RigControl ? "rig-control.transform-reference" : "camera.transform-reference"; return false; }
            }
            foreach (var flag in _flags)
                if (_owner._api.ReadExactField<bool>(Handle(flag.Node), _owner._fields[flag.Field]) != flag.Value)
                { failedCheck = "field-value." + flag.Field; return false; }
            if (!_owner._matchesRun(Handle(1).Target.ToInt64(), Handle(0).Target.ToInt64(), Run.RunId, Run.SceneGeneration))
            { failedCheck = "run-association-exit"; return false; }
            return true;
        }
        internal IDisposable Retain()
        { if (_disposed || _retained) throw new InvalidOperationException("Camera graph already released or transferred."); _retained = true; return this; }
        internal void DisposeUnlessRetained() { if (!_retained) Dispose(); }
        public void Dispose()
        {
            if (_disposed) return; _disposed = true; var errors = new List<Exception>();
            foreach (var node in _nodes.AsEnumerable().Reverse()) try { node.Root.Dispose(); } catch (Exception error) { errors.Add(error); }
            _nodes.Clear(); if (errors.Count != 0) throw new AggregateException("Camera graph root cleanup failed.", errors);
        }
    }
    private sealed class NativeBinding
    {
        private readonly NativeGameCameraBackend _owner; private readonly NativeGraph _graph; private readonly bool _scale;
        internal NativeBinding(NativeGameCameraBackend owner, NativeGraph graph, bool scale) { _owner = owner; _graph = graph; _scale = scale; }
        internal bool Current() { try { return _graph.Current(); } catch { return false; } }
        private void Require() { if (!Current()) throw new TargetExpiredException("Concrete camera run/scene/object ownership changed."); }
        internal float Read()
        {
            Require();
            var value = (_scale ? _owner._scale.ReadScale(_owner._api, _graph.Handle(_graph.Rig))
                : _owner._position.ReadPosition(_owner._api, _graph.Handle(_graph.Rig))).X;
            Require();
            if (!float.IsFinite(value)) throw new UnsupportedSignatureException("Native camera X is not finite."); return value;
        }
        internal void Write(float value)
        {
            if (_scale) _owner._scale.WriteX(_owner._api, _graph.Handle(_graph.Rig), value, Require);
            else _owner._position.WriteX(_owner._api, _graph.Handle(_graph.Rig), value, Require);
        }
    }

    private static IntPtr VerifyLoadedBuild()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) throw new PlatformNotSupportedException("Camera requires the fixed Windows x64 build.");
        var module = GetModuleHandle("GameAssembly.dll"); if (module == IntPtr.Zero) throw new UnsupportedSignatureException("GameAssembly is not loaded.");
        var path = new StringBuilder(32768);
        if (GetModuleFileName(module, path, path.Capacity) == 0) throw new UnsupportedSignatureException("Loaded GameAssembly path unavailable.");
        using var stream = File.OpenRead(path.ToString()); using var hasher = SHA256.Create();
        if (Convert.ToHexString(hasher.ComputeHash(stream)).ToLowerInvariant() != Fingerprint) throw new UnsupportedSignatureException("Loaded camera build fingerprint differs.");
        foreach (var body in Bodies)
        {
            var bytes = new byte[body.Bytes]; Marshal.Copy(IntPtr.Add(module, body.Rva), bytes, 0, bytes.Length);
            if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != body.Hash)
                throw new UnsupportedSignatureException("Loaded complete camera/accessor method body differs at " + body.Rva.ToString("X"));
        }
        return module;
    }
    // Complete method spans through the final runtime-function fragment; not a truncated prologue.
    private static readonly (int Rva, int Bytes, string Hash)[] Bodies = {
        (0x3E3060, 562, "c13729b7c99784a9a692bd7a325db19b5f784e61549a74f65c8013c3a5402c16"),
        (0x3E2E00, 608, "8dbdb6bd01875bd0fe24ae9ef9428d7deb2fb9f411cd504aec4890ea00886b1e"),
        (0x3E32A0, 97, "b3620bbcbe97763df88ed70329fe0fbe3fbeedbfe1003bb26e605acbba1eb447"),
        (0x3FD3E0, 37, "ae86a1bb8dc7cfd0d93f04710752f56474d4b642c707a03cb45f711edd786952"),
        (0x1536440, 51, "b7cebcd24a88da7e3969da2da3697e1b92d80d1a7b2304bb27d3e22bea62fd0e"),
        (0x1561640, 77, "9c491dacfae649cec8d0e6693a0239f5fa71430c39e495720c5e6785439ee034"),
        (0x1561CD0, 66, "ff34c6262b9386729319be565efaca3242b2a52786ea79ee16e09ba2646e6c7a"),
        (0x1561780, 77, "b66b367ce9aed4b21ff93eafcf4030d029cb06609badf7933448a39eb2f598f6"),
        (0x1561E10, 66, "63359448f2b94edab40c898c9220292bb3ecd08702c4e9319c03808e4fdeb04f"),
        (0x15605F0, 66, "9b5cf715d9a42b1b14e7d1a5d1d9ad1695c97ea33ba6144da9c490505ab6ef48")
    };
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern int GetModuleFileName(IntPtr module, StringBuilder path, int size);
}
