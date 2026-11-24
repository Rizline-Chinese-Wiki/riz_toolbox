using System.Runtime.InteropServices;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.AudioContent;

/// <summary>Exact, main-thread construction of the game's ResourceReference wrappers.</summary>
/// <remarks>
/// This is intentionally a separate bridge from the LOCAL catalog. A caller must provide an
/// already-created Unity resource pointer; the factory does not invent a resource, infer a
/// generic class from its printed name, or silently substitute AssetRefReference. The boolean
/// is the game's constructor flag (canDestroy), and the returned handle owns both roots until
/// its exact closed-wrapper Release method succeeds. Unknown native cleanup is retained in a
/// bounded process owner and is never retried as if a second no-op proved destruction.
/// </remarks>
internal sealed class NativeResourceReferenceFactory
{
    private const string Sprite = "UnityEngine.Sprite", Audio = "UnityEngine.AudioClip";
    private readonly NativeRuntime _api;
    private readonly IMainThreadDispatcher _main;
    private readonly AdapterBuildProfile _profile;

    internal NativeResourceReferenceFactory(NativeRuntime api, IMainThreadDispatcher main, AdapterBuildProfile profile)
        => (_api, _main, _profile) = (api, main, profile);

    internal NativeResourceReferenceHandle CreateAudio(IntPtr audioClip, bool canDestroy)
        => Create(Audio, "UnityEngine.AudioModule.dll", "musicReference", "ResourceReference<UnityEngine.AudioClip>",
            audioClip, canDestroy);

    internal NativeResourceReferenceHandle CreateSprite(IntPtr sprite, bool canDestroy)
        => Create(Sprite, "UnityEngine.CoreModule.dll", "coverReference", "ResourceReference<UnityEngine.Sprite>",
            sprite, canDestroy);

    private NativeResourceReferenceHandle Create(string resourceType, string resourceAssembly, string ownerField,
        string expectedClosedType, IntPtr resource, bool canDestroy)
    {
        _main.VerifyAccess();
        if (resource == IntPtr.Zero) throw new TargetExpiredException("Cannot wrap a null Unity resource.");
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || _profile.GameAssemblySha256 != WorkshopChartModule.Fingerprint
            || _profile.UnityVersion != "2022.3.62f2")
            throw new UnsupportedSignatureException("ResourceReference construction requires the pinned Windows x64 Unity 2022.3.62f2 build.");
        using var reservation = NativeResourceReferenceHandle.Reserve();
        // Reflection allocates BCL objects, so establish resource ownership before it runs.
        var resourceRoot = _api.Root(resource);
        try
        {
            var definition = _api.Class("Assembly-CSharp.dll", "", "ResourceReference`1");
            var argument = _api.Class(resourceAssembly, "UnityEngine", resourceType["UnityEngine.".Length..]);
            if (_api.ObjectClass(resourceRoot.Target) != argument)
                throw new UnsupportedSignatureException("ResourceReference requires the exact Unity resource class.");
            var unityObject = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Object");
            if (!_api.Unbox<bool>(_api.CallStatic(unityObject, "op_Implicit", A.Ref(resourceRoot.Target))))
                throw new TargetExpiredException("ResourceReference cannot wrap a destroyed Unity resource.");
            var owner = _api.Class("Assembly-CSharp.dll", "", "InsideLevelParams");
            var contractName = "IResourceReference<" + resourceType + ">";
            var contract = _api.InspectMetadataFieldClass(owner, ownerField, contractName).FieldClass.Class;
            var closed = _api.ReflectSingleReferenceGenericClass(definition, argument, expectedClosedType, contract);
            _api.InspectDeclaredMethod(closed.Class, ".ctor", new[] { resourceType, "System.Boolean" }, "System.Void",
                GetModuleHandle("GameAssembly.dll"));
            var getter = _api.InspectDeclaredMethod(closed.Class, "get_Resource", Array.Empty<string>(), resourceType,
                GetModuleHandle("GameAssembly.dll"));
            var release = _api.InspectDeclaredMethod(closed.Class, "Release", Array.Empty<string>(), "System.Void",
                GetModuleHandle("GameAssembly.dll"));

            var wrapperRoot = _api.NewExact(closed.Class, new[] { resourceType, "System.Boolean" },
                A.Ref(resourceRoot.Target), A.Value(canDestroy));
            try
            {
                if (_api.ObjectClass(wrapperRoot.Target) != closed.Class)
                    throw new UnsupportedSignatureException("ResourceReference constructor returned a different runtime class.");
                var result = new NativeResourceReferenceHandle(_api, _main, wrapperRoot, resourceRoot,
                    resourceType, argument, getter.MethodInfo, release.MethodInfo, canDestroy, reservation);
                reservation.Transfer(result);
                return result;
            }
            catch { wrapperRoot.Dispose(); throw; }
        }
        catch { resourceRoot.Dispose(); throw; }
    }

    private static IntPtr GetModuleHandle(string name)
    {
        return OperatingSystem.IsWindows() ? GetModuleHandleNative(name) : IntPtr.Zero;
    }

    [DllImport("kernel32", EntryPoint = "GetModuleHandleW", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleNative(string name);
}

/// <summary>Owns one exact closed wrapper and the resource root it was constructed around.</summary>
internal sealed class NativeResourceReferenceHandle : IDisposable
{
    private const int MaximumLiveHandles = 64;
    private static int _reserved;
    private static readonly HashSet<NativeResourceReferenceHandle> ProcessRoots = new();
    private readonly NativeRuntime _api;
    private readonly IMainThreadDispatcher _main;
    private readonly string _resourceType;
    private readonly IntPtr _resourceClass;
    private readonly IntPtr _getter, _release;
    private NativeRuntime.RootHandle? _wrapper, _resource;
    private bool _released;
    private bool _releaseStarted;
    private Exception? _cleanupFailure;
    private readonly Reservation _reservation;

    internal NativeResourceReferenceHandle(NativeRuntime api, IMainThreadDispatcher main, NativeRuntime.RootHandle wrapper,
        NativeRuntime.RootHandle resource, string resourceType, IntPtr resourceClass, IntPtr getter, IntPtr release, bool canDestroy,
        Reservation reservation)
        => (_api, _main, _wrapper, _resource, _resourceType, _resourceClass, _getter, _release, CanDestroy, _reservation) =
            (api, main, wrapper, resource, resourceType, resourceClass, getter, release, canDestroy, reservation);

    internal bool CanDestroy { get; }
    internal bool IsReleased => _released;
    internal bool CleanupFailed => _cleanupFailure is not null;
    internal string ResourceType => _resourceType;
    internal IntPtr Pointer => !_releaseStarted && _wrapper is not null ? _wrapper.Target : throw new ObjectDisposedException(nameof(NativeResourceReferenceHandle));

    /// <summary>Returns a new root for the exact resource returned by get_Resource.</summary>
    internal NativeRuntime.RootHandle BorrowResource()
    {
        _main.VerifyAccess();
        if (_releaseStarted || _wrapper is null) throw new ObjectDisposedException(nameof(NativeResourceReferenceHandle));
        var value = _api.Invoke(_getter, _wrapper.Target);
        if (value == IntPtr.Zero || _api.ObjectClass(value) != _resourceClass)
            throw new UnsupportedSignatureException("ResourceReference getter returned an unexpected resource class.");
        return _api.Root(value);
    }

    public void Dispose()
    {
        _main.VerifyAccess();
        if (_cleanupFailure is not null)
            throw new InvalidOperationException("ResourceReference cleanup outcome is unknown; roots remain process-owned and native Release will not be retried.", _cleanupFailure);
        if (_releaseStarted) return;
        _releaseStarted = true;
        try
        {
            _api.Invoke(_release, _wrapper!.Target);
            _released = true;
        }
        catch (Exception error)
        {
            // Native Release sets its released flag before Destroy. A second call can return
            // successfully without finishing the first destruction, so retry is unsafe evidence.
            _cleanupFailure = error;
            throw;
        }
        // A failed free must not prevent the other independent root from being attempted.
        var failures = new List<Exception>();
        try { _wrapper!.Dispose(); _wrapper = null; } catch (Exception error) { failures.Add(error); }
        try { _resource!.Dispose(); _resource = null; } catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0)
        {
            _cleanupFailure = new AggregateException("ResourceReference root cleanup outcome is unknown.", failures);
            throw _cleanupFailure;
        }
        ProcessRoots.Remove(this);
        _reservation.Complete();
    }

    internal static Reservation Reserve()
    {
        if (Interlocked.Increment(ref _reserved) > MaximumLiveHandles)
        {
            Interlocked.Decrement(ref _reserved);
            throw new InvalidOperationException("ResourceReference live/failed cleanup capacity is exhausted; process restart is required if cleanup failed.");
        }
        return new Reservation();
    }

    internal sealed class Reservation : IDisposable
    {
        private bool _transferred, _completed;
        internal void Transfer(NativeResourceReferenceHandle owner)
        {
            if (_transferred || _completed) throw new InvalidOperationException("ResourceReference reservation is no longer available.");
            ProcessRoots.Add(owner);
            _transferred = true;
        }
        internal void Complete()
        {
            if (_completed) return;
            _completed = true;
            Interlocked.Decrement(ref _reserved);
        }
        public void Dispose() { if (!_transferred) Complete(); }
    }
}
