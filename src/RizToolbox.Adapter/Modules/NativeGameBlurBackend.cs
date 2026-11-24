using System.Runtime.InteropServices;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.Modules;

internal sealed record GameBlurIdentity(string Scene, int SceneHandle, IntPtr Camera, IntPtr Component);

internal interface IGameBlurTarget : IDisposable
{
    GameBlurIdentity Identity { get; }
    bool IsCurrent();
    int Read();
    void Write(int value);
}

internal interface IGameBlurBackend { IGameBlurTarget? Discover(); }

internal sealed class NativeGameBlurBackend : IGameBlurBackend
{
    internal const string Fingerprint = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79";
    private const string Core = "UnityEngine.CoreModule.dll", BlurAssembly = "LeTai.TranslucentImage.dll";
    private const string Unity = "UnityEngine", SceneNamespace = "UnityEngine.SceneManagement";
    private const string BlurNamespace = "LeTai.Asset.TranslucentImage", BlurComponent = "TranslucentImageSource";
    private const int MaxComponents = 64;

    internal static IReadOnlyList<AdapterSymbolRequirement> Requirements { get; } = Array.AsReadOnly(new[] {
        Method("get", BlurAssembly, BlurNamespace, BlurComponent, "get_Downsample", "System.Int32"),
        Method("set", BlurAssembly, BlurNamespace, BlurComponent, "set_Downsample", "System.Void", false, "System.Int32"),
        Method("main", Core, Unity, "Camera", "get_main", "UnityEngine.Camera", true),
        Method("find", Core, Unity, "Resources", "FindObjectsOfTypeAll", "UnityEngine.Object[]", true, "System.Type"),
        Method("owner", Core, Unity, "Component", "get_gameObject", "UnityEngine.GameObject"),
        Method("alive", Core, Unity, "Object", "op_Implicit", "System.Boolean", true, "UnityEngine.Object"),
        Method("active", Core, Unity, "Behaviour", "get_isActiveAndEnabled", "System.Boolean"),
        Method("scene", Core, Unity, "GameObject", "get_scene", "UnityEngine.SceneManagement.Scene"),
        Method("active-scene", Core, SceneNamespace, "SceneManager", "GetActiveScene", "UnityEngine.SceneManagement.Scene", true),
        Method("scene-name", Core, SceneNamespace, "Scene", "GetNameInternal", "System.String", true, "System.Int32"),
        Method("scene-loaded", Core, SceneNamespace, "Scene", "GetIsLoadedInternal", "System.Boolean", true, "System.Int32")
    });

    private static AdapterSymbolRequirement Method(string id, string assembly, string ns, string type, string name,
        string returns, bool isStatic = false, params string[] parameters)
        => new("blur." + id, assembly, ns, type, AdapterSymbolKind.Method, name, isStatic, Array.AsReadOnly(parameters)) { ExpectedType = returns };

    private readonly NativeRuntime _api;
    private readonly IntPtr _camera, _effect, _get, _set, _main, _find, _owner, _alive, _active, _scene, _activeScene, _sceneName, _sceneLoaded;

    internal NativeGameBlurBackend(NativeRuntime api)
    {
        _api = api;
        var module = GetModuleHandle("GameAssembly.dll");
        if (module == IntPtr.Zero) throw new UnsupportedSignatureException("The pinned GameAssembly is unavailable.");
        _camera = api.Class(Core, Unity, "Camera");
        _effect = api.Class(BlurAssembly, BlurNamespace, BlurComponent);
        api.RequireInstanceFieldLayout(_effect, "downsample", "System.Int32", 0x40);
        _get = Bind(Requirements[0], module, 0x38bc50);
        _set = Bind(Requirements[1], module, 0xd23290);
        _main = Bind(Requirements[2]); _find = Bind(Requirements[3]); _owner = Bind(Requirements[4]);
        _alive = Bind(Requirements[5]); _active = Bind(Requirements[6]); _scene = Bind(Requirements[7]);
        _activeScene = Bind(Requirements[8]); _sceneName = Bind(Requirements[9]); _sceneLoaded = Bind(Requirements[10]);
    }

    private IntPtr Bind(AdapterSymbolRequirement row, IntPtr module = default, long? rva = null)
    {
        var cls = _api.Class(row.Assembly, row.Namespace, row.Type);
        var parameters = row.ParameterTypes!.ToArray();
        var method = row.IsStatic
            ? _api.InspectDeclaredStaticMethod(cls, row.Member!, parameters, row.ExpectedType!, module)
            : _api.InspectDeclaredMethod(cls, row.Member!, parameters, row.ExpectedType!, module);
        if (rva.HasValue && method.Entry?.Rva != rva.Value)
            throw new UnsupportedSignatureException("The blur accessor RVA differs from the pinned build: " + row.Member);
        return method.MethodInfo;
    }

    public IGameBlurTarget? Discover()
    {
        var scene = _api.InvokeSceneHandleWithPublication(_activeScene, IntPtr.Zero);
        if (scene == 0 || !_api.InvokeValueWithPublication<bool>(_sceneLoaded, IntPtr.Zero, A.Value(scene))) return null;
        var name = _api.TextWithPublication(_api.Invoke(_sceneName, IntPtr.Zero, A.Value(scene)));
        if (name is not ("MainMenu" or "GameStart")) return null;
        NativeRuntime.RootHandle? camera = Reference(_main, IntPtr.Zero);
        NativeRuntime.RootHandle? component = null;
        try
        {
            if (camera is null || !IsActive(camera.Target, _camera)) return null;
            using var gameObject = Reference(_owner, camera.Target);
            if (gameObject is null || _api.InvokeSceneHandleWithPublication(_scene, gameObject.Target) != scene) return null;
            component = FindEffect(gameObject.Target);
            if (component is null) return null;
            var result = new Target(this, new(name, scene, camera.Target, component.Target), camera, component);
            if (!result.IsCurrent()) return null;
            camera = null; component = null;
            return result;
        }
        catch (AdapterOperationException error) when (error.Error.Code == ErrorCode.AmbiguousTarget)
        {
            throw new AdapterOperationException(error.Error with { TargetId = GamePostProcessingRoles.MainCameraBlurDownsample + "/" + name });
        }
        finally { try { component?.Dispose(); } finally { camera?.Dispose(); } }
    }

    private NativeRuntime.RootHandle? FindEffect(IntPtr gameObject)
    {
        using var type = _api.Root(_api.Type(_effect));
        using var elements = Reference(_find, IntPtr.Zero, A.Ref(type.Target));
        if (elements is null) return null;
        var count = _api.Unbox<int>(_api.Call(elements.Target, "get_Length"));
        if (count is < 0 or > MaxComponents)
            throw new UnsupportedSignatureException("The complete blur component discovery exceeds its bound.");
        NativeRuntime.RootHandle? match = null;
        try
        {
            foreach (var pointer in _api.ArrayItems(elements.Target))
            {
                if (pointer == IntPtr.Zero) continue;
                using var item = _api.Root(pointer);
                if (!IsActive(item.Target, _effect)) continue;
                using var owner = Reference(_owner, item.Target);
                if (owner?.Target != gameObject) continue;
                if (match is not null)
                    throw new AdapterOperationException(new(ErrorCode.AmbiguousTarget, "More than one active blur source belongs to the current main camera."));
                match = _api.RetainExactObject(item.Target, _effect);
            }
            return match;
        }
        catch { match?.Dispose(); throw; }
    }

    private NativeRuntime.RootHandle? Reference(IntPtr method, IntPtr receiver, params A[] args)
    {
        NativeRuntime.RootHandle? result = null;
        try
        {
            _api.InvokeReferenceWithPublication(method, receiver, value => { if (value != IntPtr.Zero) result = _api.Root(value); }, args);
            return result;
        }
        catch { result?.Dispose(); throw; }
    }

    private bool IsActive(IntPtr value, IntPtr cls) => _api.ObjectClass(value) == cls
        && _api.InvokeValueWithPublication<bool>(_alive, IntPtr.Zero, A.Ref(value))
        && _api.InvokeValueWithPublication<bool>(_active, value);

    private sealed class Target : IGameBlurTarget
    {
        private readonly NativeGameBlurBackend _backend;
        private readonly NativeRuntime.RootHandle _camera, _component;
        private bool _disposed;
        public GameBlurIdentity Identity { get; }
        internal Target(NativeGameBlurBackend backend, GameBlurIdentity identity, NativeRuntime.RootHandle camera, NativeRuntime.RootHandle component)
            => (_backend, Identity, _camera, _component) = (backend, identity, camera, component);

        public bool IsCurrent()
        {
            if (_disposed || _camera.Target != Identity.Camera || _component.Target != Identity.Component) return false;
            var b = _backend;
            if (b._api.InvokeSceneHandleWithPublication(b._activeScene, IntPtr.Zero) != Identity.SceneHandle
                || !b._api.InvokeValueWithPublication<bool>(b._sceneLoaded, IntPtr.Zero, A.Value(Identity.SceneHandle))
                || !b.IsActive(_camera.Target, b._camera) || !b.IsActive(_component.Target, b._effect)) return false;
            using var main = b.Reference(b._main, IntPtr.Zero);
            if (main?.Target != _camera.Target) return false;
            using var cameraOwner = b.Reference(b._owner, _camera.Target);
            using var effectOwner = b.Reference(b._owner, _component.Target);
            return cameraOwner is not null && effectOwner?.Target == cameraOwner.Target
                && b._api.InvokeSceneHandleWithPublication(b._scene, cameraOwner.Target) == Identity.SceneHandle;
        }

        private void RequireCurrent()
        {
            if (!IsCurrent()) throw new TargetExpiredException("The active main camera or blur source was replaced.");
        }

        public int Read()
        {
            RequireCurrent();
            var value = _backend._api.InvokeValueWithPublication<int>(_backend._get, _component.Target);
            RequireCurrent();
            return value;
        }

        public void Write(int value)
        {
            if (value is < GamePostProcessingModule.Minimum or > GamePostProcessingModule.Maximum)
                throw new ArgumentOutOfRangeException(nameof(value));
            RequireCurrent();
            using var gameObject = _backend.Reference(_backend._owner, _camera.Target);
            using var effect = gameObject is null ? null : _backend.FindEffect(gameObject.Target);
            if (effect?.Target != Identity.Component)
                throw new TargetExpiredException("The camera's unique active blur source changed before the write.");
            _backend._api.InvokeVoidWithPublicationGuarded(_backend._set, _component.Target, RequireCurrent, A.Value(value));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _component.Dispose(); } finally { _camera.Dispose(); }
        }
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
}
