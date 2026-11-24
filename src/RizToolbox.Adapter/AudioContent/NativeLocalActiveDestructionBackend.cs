using System.Security.Cryptography;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.AudioContent;

/// <summary>Pinned native liveness and empty-staging destruction over five actual
/// independently rooted factory objects. Never destroys controller/clone objects.</summary>
internal sealed class NativeLocalActiveDestructionBackend : IActiveDestructionBackend
{
    private readonly NativeRuntime _api;
    private readonly IMainThreadDispatcher _main;
    private readonly AdapterBuildProfile _profile;
    private readonly IntPtr _module;
    private readonly OwnedNativeInactiveController _factory;
    private readonly LocalRunLaunchCommitState _launch;
    private readonly NativeRuntime.RootHandle?[] _roots = new NativeRuntime.RootHandle?[5];
    private readonly long[] _identities = new long[5];
    private readonly bool[] _freeAttempted = new bool[5];
    private readonly NativePublicationArgumentRecords _arguments;
    private IntPtr _controllerClass, _goClass, _transformClass, _alive, _frame, _componentGameObject,
        _goTransform, _parent, _childCount, _activeSelf, _activeHierarchy, _destroy;
    private bool _initialized, _initializing, _destroyAttempted, _releaseAttempted;
    public bool CleanupUnknown { get; private set; }
    public int RootRecords { get { _main.VerifyAccess(); return _roots.Count(root => root is not null); } }
    internal int RetainedArgumentRecords => _arguments.Count;
    internal NativeLocalActiveDestructionBackend(NativeRuntime api, IMainThreadDispatcher main,
        AdapterBuildProfile profile, IntPtr module, OwnedNativeInactiveController factory, LocalRunLaunchCommitState launch)
    {
        (_api, _main, _profile, _module, _factory, _launch) = (api, main, profile, module, factory, launch);
        _arguments = new(main);
    }
    public void Initialize()
    {
        _main.VerifyAccess();
        if (_initialized || _initializing || CleanupUnknown || _roots.Any(root => root is not null))
            throw new InvalidOperationException("Active destruction backend initialization is once-only.");
        _initializing = true;
        try
        {
            if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || _module == IntPtr.Zero
                || _profile.GameAssemblySha256 != WorkshopChartModule.Fingerprint || _profile.UnityVersion != "2022.3.62f2")
                throw new UnsupportedSignatureException("Active destruction observations require the pinned Windows x64 Unity build.");
            var file = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "GameAssembly.dll");
            using (var stream = File.OpenRead(file)) using (var sha = SHA256.Create())
                if (Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant() != WorkshopChartModule.Fingerprint)
                    throw new UnsupportedSignatureException("Active destruction image fingerprint differs from the supported profile.");
            _controllerClass = Native(() => _api.Class("Assembly-CSharp.dll", "", "NormalModeInLevelController"));
            _goClass = Native(() => _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "GameObject"));
            _transformClass = Native(() => _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Transform"));
            var unityObject = Native(() => _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Object"));
            var component = Native(() => _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Component"));
            var time = Native(() => _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Time"));
            _alive = Method(unityObject, "op_Implicit", new[] { "UnityEngine.Object" }, "System.Boolean", true, 0x15409B0);
            _frame = Method(time, "get_frameCount", Array.Empty<string>(), "System.Int32", true, 0x155EFD0);
            _componentGameObject = Method(component, "get_gameObject", Array.Empty<string>(), "UnityEngine.GameObject", false, 0x1536400);
            _goTransform = Method(_goClass, "get_transform", Array.Empty<string>(), "UnityEngine.Transform", false, 0x1539010);
            _parent = Method(_transformClass, "get_parent", Array.Empty<string>(), "UnityEngine.Transform", false, 0x1560470);
            _childCount = Method(_transformClass, "get_childCount", Array.Empty<string>(), "System.Int32", false, 0x1561340);
            _activeSelf = Method(_goClass, "get_activeSelf", Array.Empty<string>(), "System.Boolean", false, 0x1538EF0);
            _activeHierarchy = Method(_goClass, "get_activeInHierarchy", Array.Empty<string>(), "System.Boolean", false, 0x1538EB0);
            _destroy = Method(unityObject, "Destroy", new[] { "UnityEngine.Object" }, "System.Void", true, 0x153E680);
            Native(() => _factory.BorrowActiveDestructionRoots(_launch, AdoptRoot));
            if (_roots.Any(root => root is null) || _identities.Any(identity => identity == 0) || _identities.Distinct().Count() != 5
                || _identities[0] != _launch.Read().Controller)
                throw new InvalidOperationException("The actual factory failed to publish five distinct independently rooted objects.");
            _initialized = true;
            var live = ReadLiveness();
            if (!live.ControllerAlive || !live.CloneGameObjectAlive || !live.CloneTransformAlive
                || !live.StagingGameObjectAlive || !live.StagingTransformAlive)
                throw new TargetExpiredException("A factory object was not live during pre-activation adoption.");
            VerifyCaptureGraph();
        }
        catch { CleanupUnknown = true; throw; }
        finally { _initializing = false; }
    }
    private void AdoptRoot(ActiveDestructionObject role, NativeRuntime.RootHandle borrowed)
    {
        _main.VerifyAccess(); var slot = (int)role;
        if (!_initializing || slot < 0 || slot >= _roots.Length || _roots[slot] is not null || _freeAttempted[slot])
            throw new InvalidOperationException("The preallocated named factory-root slot is unavailable.");
        var pointer = Native(() => borrowed.Target);
        RequireObservationKnown();
        _roots[slot] = _api.Root(pointer); // Record immediately, before class/identity validation.
        _identities[slot] = pointer.ToInt64();
        RequireObservationKnown();
        if (Pointer(slot) != pointer || Native(() => borrowed.Target) != pointer || Native(() => _api.ObjectClass(pointer)) != ClassFor(slot))
            throw new UnsupportedSignatureException("An independently adopted factory root changed identity or exact class.");
    }
    private IntPtr ClassFor(int slot) => slot switch { 0 => _controllerClass, 1 or 3 => _goClass, 2 or 4 => _transformClass,
        _ => throw new ArgumentOutOfRangeException(nameof(slot)) };
    private IntPtr Method(IntPtr declaring, string name, string[] parameters, string returns, bool isStatic, long rva)
    {
        var method = Native(() => isStatic ? _api.InspectDeclaredStaticMethod(declaring, name, parameters, returns, _module)
            : _api.InspectDeclaredMethod(declaring, name, parameters, returns, _module));
        if (method.Entry?.Rva != rva || method.GenericMethod || method.InflatedMethod
            || ((method.Flags & 0x10) != 0) != isStatic || (method.Flags & 0x400) != 0)
            throw new UnsupportedSignatureException("Active destruction method metadata/RVA changed: " + name);
        return method.MethodInfo;
    }
    public int Frame() { Check(); return Observe(() => Value<int>(_frame, IntPtr.Zero)); }
    public ActiveDestructionLiveness ReadLiveness()
    {
        Check(); return Observe<ActiveDestructionLiveness>(() => {
            // Unity-dead wrappers are still IL2CPP objects held by our handles.
            // Do not dereference their native Component/Transform/Scene APIs.
            var live = new bool[5];
            for (var index = 0; index < _roots.Length; ++index)
            {
                var pointer = Pointer(index);
                if (pointer.ToInt64() != _identities[index] || Native(() => _api.ObjectClass(pointer)) != ClassFor(index))
                    throw new UnsupportedSignatureException("A retained factory wrapper changed exact identity or class.");
                live[index] = Value<bool>(_alive, IntPtr.Zero, A.Ref(pointer));
            }
            return new(new(_identities[0], _identities[1], _identities[2], _identities[3], _identities[4]),
                live[0], live[1], live[2], live[3], live[4]);
        });
    }
    private void VerifyCaptureGraph()
    {
        var controller = Pointer(0); var go = Pointer(1); var transform = Pointer(2);
        var staging = Pointer(3); var stagingTransform = Pointer(4);
        if (Reference(_componentGameObject, controller) != go || Reference(_componentGameObject, transform) != go
            || Reference(_goTransform, go) != transform || Reference(_componentGameObject, stagingTransform) != staging
            || Reference(_goTransform, staging) != stagingTransform || Reference(_parent, transform) != stagingTransform
            || Reference(_parent, stagingTransform) != IntPtr.Zero || Value<int>(_childCount, stagingTransform) != 1
            || Value<bool>(_activeSelf, staging) || Value<bool>(_activeHierarchy, staging) || Value<bool>(_activeHierarchy, go))
            throw new InvalidOperationException("The complete five-object factory graph is not this exact inactive clone under its empty staging root.");
    }
    public void VerifyStagingCanDestroy()
    {
        Check(); Observe(() => {
            var observed = ReadLiveness();
            if (!observed.CloneDead || !observed.StagingGameObjectAlive || !observed.StagingTransformAlive)
                throw new InvalidOperationException("Staging destruction requires the three actual clone wrappers dead and both staging objects live.");
            var go = Pointer(3); var transform = Pointer(4);
            if (Reference(_componentGameObject, transform) != go || Reference(_goTransform, go) != transform
                || Reference(_parent, transform) != IntPtr.Zero || Value<int>(_childCount, transform) != 0
                || Value<bool>(_activeSelf, go) || Value<bool>(_activeHierarchy, go))
                throw new InvalidOperationException("Staging is no longer this inactive unattached empty object; a successor may own its children or activation.");
            return true;
        });
    }
    public void DestroyStagingOnce()
    {
        Check(); if (_destroyAttempted) throw new InvalidOperationException("The staging Destroy request is once-only, including unknown native outcomes.");
        // Revalidate immediately before mutation. Never call inactive-owner cleanup:
        // that path would also destroy the active controller and its original roots.
        VerifyStagingCanDestroy(); RequireObservationKnown(); _destroyAttempted = true;
        var arguments = new[] { A.Ref(Pointer(3)) }; _arguments.Begin(arguments);
        try { RequireObservationKnown(); _api.InvokeVoidWithPublication(_destroy, IntPtr.Zero, arguments); }
        catch { _arguments.Fail(); CleanupUnknown = true; throw; }
        _arguments.Complete(); RequireObservationKnown();
    }
    public void ReleaseObservationRoots(Func<bool> mayFreeNext)
    {
        Check(); ArgumentNullException.ThrowIfNull(mayFreeNext);
        if (_releaseAttempted) throw new InvalidOperationException("Independent active observation-root cleanup cannot repeat.");
        _releaseAttempted = true; var errors = new List<Exception>(5);
        for (var index = _roots.Length - 1; index >= 0; --index)
        {
            if (_roots[index] is not { } root) continue;
            // A local root-free failure does not prevent known independent roots'
            // one attempt. A native/authority fault or new in-flight call does.
            bool safe;
            try
            {
                RequireObservationKnown(includeCleanupFailure: false);
                safe = mayFreeNext();
                RequireObservationKnown(includeCleanupFailure: false);
            }
            catch (Exception error) { errors.Add(error); break; }
            if (!safe) { errors.Add(new InvalidOperationException("Native destruction authority changed during root cleanup.")); break; }
            if (_freeAttempted[index]) { errors.Add(new InvalidOperationException("Unknown observation-root free cannot retry.")); continue; }
            _freeAttempted[index] = true;
            try { root.Dispose(); _roots[index] = null; } catch (Exception error) { CleanupUnknown = true; errors.Add(error); }
            // The final root has no next iteration whose pre-check could notice
            // a concurrent shared fault. A local failed free still permits known
            // independent roots; external authority loss always stops this pass.
            try
            {
                RequireObservationKnown(includeCleanupFailure: false);
                if (!mayFreeNext()) throw new InvalidOperationException("Native destruction authority changed after an independent root free.");
                RequireObservationKnown(includeCleanupFailure: false);
            }
            catch (Exception error) { errors.Add(error); break; }
        }
        if (errors.Count != 0) { CleanupUnknown = true; throw new AggregateException("Independent active destruction roots retain unknown cleanup responsibility.", errors); }
    }
    private IntPtr Reference(IntPtr method, IntPtr receiver)
    {
        var result = IntPtr.Zero; Native(() => _api.InvokeReferenceWithPublication(method, receiver, value => result = value));
        return result; // Identity comparison only; every expected object is already independently rooted.
    }
    private T Value<T>(IntPtr method, IntPtr receiver, params A[] arguments) where T : struct
    {
        RequireObservationKnown();
        _arguments.Begin(arguments);
        T value;
        try { RequireObservationKnown(); value = _api.InvokeValueWithPublication<T>(method, receiver, arguments); }
        catch { _arguments.Fail(); CleanupUnknown = true; throw; }
        _arguments.Complete(); RequireObservationKnown(); return value;
    }
    private IntPtr Pointer(int slot) => Native(() => _roots[slot]!.Target);
    private T Native<T>(Func<T> operation)
    { RequireObservationKnown(); var value = operation(); RequireObservationKnown(); return value; }
    private void Native(Action operation)
    { RequireObservationKnown(); operation(); RequireObservationKnown(); }
    private void RequireObservationKnown(bool includeCleanupFailure = true)
    {
        _main.VerifyAccess();
        if (_launch.Read().Phase == LocalRunLaunchCommitPhase.Unknown || _arguments.CleanupUnknown || includeCleanupFailure && CleanupUnknown)
            throw new InvalidOperationException("Active destruction shared launch authority is unknown.");
    }
    private T Observe<T>(Func<T> read) { try { return read(); } catch { CleanupUnknown = true; throw; } }
    private void Check()
    {
        _main.VerifyAccess();
        if (!_initialized || _releaseAttempted || CleanupUnknown || _arguments.CleanupUnknown)
            throw new InvalidOperationException("Active destruction native ownership is unavailable or unknown.");
        RequireObservationKnown();
    }
}
