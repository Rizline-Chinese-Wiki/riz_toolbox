using System.Runtime.InteropServices;
using System.Security.Cryptography;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.AudioContent;

/// <summary>Official-export discovery only. Each backend belongs to a process-owned resolver owner before Initialize.</summary>
internal sealed class NativeControllerPrefabDiscovery : IControllerPrefabBackend
{
    private const string Fingerprint = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79";
    private static readonly string[] CardTypes = { "LevelCardControl", "WorkshopLevelCardControl" };
    private static readonly int[] FieldOffsets = { 0xB8, 0xD8 };
    private const string PrefabField = "normalModeInLevelController";
    private readonly NativeRuntime _api;
    private readonly IMainThreadDispatcher _main;
    private readonly AdapterBuildProfile _profile;
    private readonly IntPtr _module;
    private readonly NativeReference?[] _roots = new NativeReference?[32];
    private readonly NativeReference?[] _sources = new NativeReference?[2];
    private readonly int[] _counts = { -1, -1 };
    private readonly IntPtr[] _cards = new IntPtr[2];
    private IntPtr _controller, _unityObject, _gameObject, _systemType, _find, _alive, _valid, _loaded;
    private IntPtr _active, _getGameObject, _getScene;
    private bool _initialized, _discoveryClosed, _disposeAttempted, _cleanupUnknown;
    internal NativeControllerPrefabDiscovery(NativeRuntime api, IMainThreadDispatcher main, AdapterBuildProfile profile, IntPtr module)
        => (_api, _main, _profile, _module) = (api, main, profile, module);
    public int OutstandingRootRecords { get { _main.VerifyAccess(); return _roots.Count(x => x is not null); } }

    public void Initialize()
    {
        _main.VerifyAccess();
        if (_initialized || _disposeAttempted) throw new InvalidOperationException("Prefab discovery initialization is one-shot.");
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || _module == IntPtr.Zero
            || _module != GetModuleHandle("GameAssembly.dll") || _profile.GameAssemblySha256 != Fingerprint || _profile.UnityVersion != "2022.3.62f2")
            throw new UnsupportedSignatureException("Prefab resolution requires the loaded pinned Windows x64 Unity 2022.3.62f2 image.");
        var image = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "GameAssembly.dll");
        using (var stream = File.OpenRead(image)) using (var sha = SHA256.Create())
            if (Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant() != Fingerprint)
                throw new UnsupportedSignatureException("Prefab resolution image differs from its fixed build profile.");
        _controller = _api.Class("Assembly-CSharp.dll", "", "NormalModeInLevelController");
        _unityObject = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Object");
        _gameObject = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "GameObject");
        _systemType = _api.Class("mscorlib.dll", "System", "Type");
        var resources = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Resources");
        var component = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Component");
        var behaviour = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Behaviour");
        var scene = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine.SceneManagement", "Scene");
        _find = Method(resources, "FindObjectsOfTypeAll", true, "UnityEngine.Object[]", "System.Type");
        _alive = Method(_unityObject, "op_Implicit", true, "System.Boolean", "UnityEngine.Object");
        _active = Method(behaviour, "get_isActiveAndEnabled", false, "System.Boolean");
        _getGameObject = Method(component, "get_gameObject", false, "UnityEngine.GameObject");
        _getScene = Method(_gameObject, "get_scene", false, "UnityEngine.SceneManagement.Scene");
        _valid = Method(scene, "IsValidInternal", true, "System.Boolean", "System.Int32");
        _loaded = Method(scene, "GetIsLoadedInternal", true, "System.Boolean", "System.Int32");
        for (var source = 0; source < _cards.Length; source++)
        {
            var cls = _api.Class("Assembly-CSharp.dll", "", CardTypes[source]); _cards[source] = cls;
            _api.RequireInstanceFieldLayout(cls, PrefabField, "NormalModeInLevelController", FieldOffsets[source]);
            if (_api.InspectMetadataFieldClass(cls, PrefabField, "NormalModeInLevelController").FieldClass.Class != _controller)
                throw new UnsupportedSignatureException("Card prefab field does not have the exact NormalModeInLevelController class.");
            _api.RequireAssignable(behaviour, cls);
        }
        _initialized = true;
    }
    public int DiscoverSource(int source)
    {
        CheckSource(source);
        if (_discoveryClosed || _counts[source] >= 0) throw new InvalidOperationException("Each source discovery array can be captured once.");
        using var type = Root(_api.Type(_cards[source]));
        _api.RequireAssignable(_systemType, _api.ObjectClass(type.Pointer));
        var array = Root(_api.Invoke(_find, IntPtr.Zero, A.Ref(type.Pointer))); _sources[source] = array;
        var arrayType = _api.InspectMetadataClass(_api.ObjectClass(array.Pointer)).TypeName;
        var rank = _api.Unbox<int>(_api.CallTyped(array.Pointer, "get_Rank", Array.Empty<string>()));
        VerifyArrayShape(source, arrayType, rank);
        var count = _api.Unbox<int>(_api.CallTyped(array.Pointer, "get_Length", Array.Empty<string>()));
        if (count < 0 || count > NativeControllerPrefabResolver.MaximumScannedElements)
            throw new ControllerPrefabScanLimitException("One prefab card source exceeds the complete 4096-element scan bound.");
        _counts[source] = count; return count;
    }
    internal static void VerifyArrayShape(int source, string actualType, int rank)
    {
        if (source is < 0 or >= 2) throw new ArgumentOutOfRangeException(nameof(source));
        // Resources(Type) declares Object[] but this game's native implementation returns
        // a covariant exact requested-card array, including an empty array before scene load.
        // Every non-null element is still checked against the exact requested card class.
        if (rank != 1 || (actualType != "UnityEngine.Object[]" && actualType != CardTypes[source] + "[]"))
            throw new UnsupportedSignatureException("Resources discovery array contract differs: source=" + CardTypes[source]
                + "; actualType=" + actualType + "; rank=" + rank + "; required=Object[] or exact source-card[]/rank1.");
    }
    public IControllerPrefabReference? RetainCard(int source, int index)
    {
        CheckSource(source);
        if (_discoveryClosed || index < 0 || index >= _counts[source] || _sources[source] is not { } array)
            throw new InvalidOperationException("Card index is outside the captured complete source array.");
        var value = _api.CallTyped(array.Pointer, "GetValue", new[] { "System.Int32" }, A.Value(index));
        return value == IntPtr.Zero ? null : Root(value);
    }
    public bool IsActiveLoadedCard(int source, IControllerPrefabReference card)
    {
        CheckSource(source); var value = Native(card);
        if (_api.ObjectClass(value.Pointer) != _cards[source])
            throw new UnsupportedSignatureException("A discovery array element is not the exact requested card class.");
        if (!Alive(value.Pointer) || !_api.Unbox<bool>(_api.Invoke(_active, value.Pointer))) return false;
        using var gameObject = Root(_api.Invoke(_getGameObject, value.Pointer));
        if (_api.ObjectClass(gameObject.Pointer) != _gameObject || !Alive(gameObject.Pointer))
            throw new UnsupportedSignatureException("The card owner has no exact live GameObject.");
        var scene = _api.Unbox<int>(_api.Invoke(_getScene, gameObject.Pointer));
        return _api.Unbox<bool>(_api.Invoke(_valid, IntPtr.Zero, A.Value(scene)))
            && _api.Unbox<bool>(_api.Invoke(_loaded, IntPtr.Zero, A.Value(scene)));
    }
    public IControllerPrefabReference? RetainPrefab(int source, IControllerPrefabReference card)
    {
        CheckSource(source); var owner = Native(card);
        if (_api.ObjectClass(owner.Pointer) != _cards[source]) throw new UnsupportedSignatureException("Card prefab owner has the wrong exact class.");
        var value = _api.Read<IntPtr>(owner.Pointer, PrefabField);
        return value == IntPtr.Zero ? null : Root(value);
    }
    public void VerifyCardPrefab(int source, IControllerPrefabReference card, IControllerPrefabReference prefab)
    {
        CheckSource(source); var value = Native(prefab);
        if (!IsActiveLoadedCard(source, card)) throw new TargetExpiredException("The retained card is no longer active in a valid loaded scene.");
        if (_api.ObjectClass(value.Pointer) != _controller || !Alive(value.Pointer))
            throw new UnsupportedSignatureException("The retained prefab is not the exact live NormalModeInLevelController.");
        if (_api.Read<IntPtr>(Native(card).Pointer, PrefabField) != value.Pointer)
            throw new TargetExpiredException("The retained card's prefab field changed.");
    }
    public void CompleteDiscovery()
    {
        Check(); if (_discoveryClosed) throw new InvalidOperationException("Discovery source cleanup is one-shot.");
        _discoveryClosed = true; var failures = new List<Exception>(2);
        for (var source = _sources.Length - 1; source >= 0; source--)
            if (_sources[source] is { } array)
                try { array.Dispose(); _sources[source] = null; } catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("Prefab discovery arrays have unknown root cleanup.", failures);
    }
    private IntPtr Method(IntPtr cls, string name, bool isStatic, string returns, params string[] parameters)
    {
        var method = _api.Method(cls, name, parameters.Length, isStatic, parameters);
        if (_api.DescribeReturnType(method) != returns) throw new UnsupportedSignatureException("Prefab discovery method return changed: " + name);
        return method;
    }
    private bool Alive(IntPtr value) => _api.Unbox<bool>(_api.Invoke(_alive, IntPtr.Zero, A.Ref(value)));
    private NativeReference Native(IControllerPrefabReference value)
        => value is NativeReference root && ReferenceEquals(root.Owner, this) ? root
            : throw new UnsupportedSignatureException("Prefab reference belongs to another discovery backend.");
    private void CheckSource(int source) { Check(); if (source is < 0 or >= 2) throw new ArgumentOutOfRangeException(nameof(source)); }
    private void Check()
    {
        _main.VerifyAccess();
        if (!_initialized || _disposeAttempted || _cleanupUnknown) throw new InvalidOperationException("Prefab discovery backend is inactive or cleanup remains unknown.");
    }
    private NativeReference Root(IntPtr value)
    {
        _main.VerifyAccess(); if (_disposeAttempted || _cleanupUnknown) throw new InvalidOperationException("Cannot create another root after uncertain prefab cleanup.");
        var slot = Array.FindIndex(_roots, x => x is null);
        if (slot < 0) throw new InvalidOperationException("Bounded prefab discovery root records are exhausted.");
        var reference = new NativeReference(this, slot); _roots[slot] = reference;
        try { reference.Attach(_api.Root(value)); return reference; }
        catch { _roots[slot] = null; throw; }
    }
    public void Dispose()
    {
        _main.VerifyAccess(); if (_disposeAttempted) throw new InvalidOperationException("Unknown prefab root frees will not be retried.");
        var failures = new List<Exception>(_roots.Length); _disposeAttempted = true;
        for (var slot = _roots.Length - 1; slot >= 0; slot--)
        {
            var root = _roots[slot]; if (root is null) continue;
            if (root.FreeAttempted) { failures.Add(new InvalidOperationException("A previous prefab root free is unknown.")); continue; }
            try { root.Dispose(); } catch (Exception error) { failures.Add(error); }
        }
        if (failures.Count != 0) throw new AggregateException("Prefab roots retain unconfirmed cleanup responsibility.", failures);
    }
    private sealed class NativeReference : IControllerPrefabReference
    {
        internal readonly NativeControllerPrefabDiscovery Owner;
        private readonly int _slot;
        private NativeRuntime.RootHandle? _root;
        internal bool FreeAttempted { get; private set; }
        internal NativeReference(NativeControllerPrefabDiscovery owner, int slot) => (Owner, _slot) = (owner, slot);
        internal void Attach(NativeRuntime.RootHandle root) => _root = root;
        internal IntPtr Pointer
        { get { Owner._main.VerifyAccess(); if (FreeAttempted || _root is null) throw new ObjectDisposedException(nameof(NativeReference)); return _root.Target; } }
        public long Identity => Pointer.ToInt64();
        public void Dispose()
        {
            Owner._main.VerifyAccess();
            if (FreeAttempted) { if (_root is not null) throw new InvalidOperationException("Unknown prefab root free is not retried."); return; }
            FreeAttempted = true;
            try { _root!.Dispose(); _root = null; Owner._roots[_slot] = null; }
            catch { Owner._cleanupUnknown = true; throw; }
        }
    }
    [DllImport("kernel32", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr GetModuleHandle(string moduleName);
}
