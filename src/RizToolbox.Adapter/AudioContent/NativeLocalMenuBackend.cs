using System.Security.Cryptography;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.AudioContent;

internal sealed class NativeLocalMenuBackend : INativeLocalMenuBackend
{
    private readonly NativeRuntime _api; private readonly IMainThreadDispatcher _main;
    private readonly AdapterBuildProfile _profile; private readonly IntPtr _module;
    private readonly ExactHookBroker _broker; private readonly NativeRuntime.RootHandle _borrowedManager;
    // Manager, camera/particle component, two GOs, normal/back lock.
    private readonly NativeRuntime.RootHandle?[] _roots = new NativeRuntime.RootHandle?[7];
    private readonly bool[] _freeAttempted = new bool[7];
    private readonly ExactHookBroker.Binding?[] _bindings = new ExactHookBroker.Binding?[4];
    private readonly ExactHookBroker.BoundaryProbeBinding?[] _probes = new ExactHookBroker.BoundaryProbeBinding?[4];
    private readonly VerifiedHookTarget?[] _targets = new VerifiedHookTarget?[4];
    private readonly NativePublicationArgumentRecords _arguments;
    private IntPtr _manager, _camera, _particle, _go, _multi, _touch, _bottom, _alive, _getGo, _active;
    private bool _captured, _bindAttempted, _releaseAttempted;
    private NativeLocalMenuLedger? _owner;
    public bool CleanupUnknown { get; private set; }
    public int RootRecords { get { _main.VerifyAccess(); return _roots.Count(item => item is not null); } }
    internal NativeLocalMenuBackend(NativeRuntime api, IMainThreadDispatcher main, AdapterBuildProfile profile,
        IntPtr module, ExactHookBroker broker, NativeRuntime.RootHandle borrowedManager)
    {
        ArgumentNullException.ThrowIfNull(api); ArgumentNullException.ThrowIfNull(main); ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(broker); ArgumentNullException.ThrowIfNull(borrowedManager);
        (_api, _main, _profile, _module, _broker, _borrowedManager) = (api, main, profile, module, broker, borrowedManager);
        _arguments = new(main);
    }
    public NativeMenuObservation Capture()
    {
        _main.VerifyAccess(); if (_captured || _roots.Any(item => item is not null)) throw new InvalidOperationException("Menu backend capture is once-only.");
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || _module == IntPtr.Zero
            || _profile.GameAssemblySha256 != WorkshopChartModule.Fingerprint || _profile.UnityVersion != "2022.3.62f2")
            throw new UnsupportedSignatureException("Menu handoff requires the pinned Windows x64 Unity build.");
        using (var stream = File.OpenRead(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "GameAssembly.dll")))
        using (var sha = SHA256.Create())
            if (Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant() != WorkshopChartModule.Fingerprint)
                throw new UnsupportedSignatureException("Menu image differs from its pinned profile.");
        try
        {
            _manager = _api.Class("Assembly-CSharp.dll", "", "GameManager");
            _particle = _api.Class("Assembly-CSharp.dll", "", "BackgroundParticleControl");
            _multi = _api.Class("Assembly-CSharp.dll", "", "MultiLock");
            _touch = _api.Class("Assembly-CSharp.dll", "", "TouchBlocker"); _bottom = _api.Class("Assembly-CSharp.dll", "", "BottomBar");
            _camera = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Camera");
            _go = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "GameObject");
            var component = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Component");
            _alive = Method(_api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Object"), "op_Implicit", new[] { "UnityEngine.Object" }, "System.Boolean", true, 0x15409B0);
            _getGo = Method(component, "get_gameObject", Array.Empty<string>(), "UnityEngine.GameObject", false, 0x1536400);
            _active = Method(_go, "get_activeSelf", Array.Empty<string>(), "System.Boolean", false, 0x1538EF0);
            _api.RequireInstanceFieldLayout(_manager, "mainCamera", "UnityEngine.Camera", 0x40);
            _api.RequireInstanceFieldLayout(_manager, "mainParticle", "BackgroundParticleControl", 0x48);
            _api.RequireInstanceFieldLayout(_multi, "count", "System.Int32", 0x10);
            _api.RequireStaticFieldLayout(_touch, "normalLock", "MultiLock", 0);
            _api.RequireStaticFieldLayout(_bottom, "<BackLock>k__BackingField", "MultiLock", 0x20);
            _targets[0] = _api.ResolveExactHook("local.menu.SetActive", _module, _go, "SetActive", new[] { "System.Boolean" }, 0x1538AB0, ExactHookAbi.InstanceBooleanVoid);
            _targets[1] = _api.ResolveExactHook("local.menu.Lock", _module, _multi, "Lock", Array.Empty<string>(), 0x38B2D0, ExactHookAbi.InstanceVoid);
            _targets[2] = _api.ResolveExactHook("local.menu.Unlock", _module, _multi, "Unlock", Array.Empty<string>(), 0x38B350, ExactHookAbi.InstanceVoid);
            _targets[3] = _api.ResolveExactHook("local.menu.UnlockAll", _module, _multi, "UnlockAll", Array.Empty<string>(), 0x38B310, ExactHookAbi.InstanceVoid);
            Root(0, _borrowedManager.Target, _manager);
            Root(1, _api.ReadWithPublication<IntPtr>(_roots[0]!.Target, "mainCamera"), _camera);
            Root(2, _api.ReadWithPublication<IntPtr>(_roots[0]!.Target, "mainParticle"), _particle);
            _api.InvokeReferenceWithPublication(_getGo, _roots[1]!.Target, value => Root(3, value, _go));
            _api.InvokeReferenceWithPublication(_getGo, _roots[2]!.Target, value => Root(4, value, _go));
            Root(5, _api.ReadStatic<IntPtr>(_touch, "normalLock"), _multi);
            Root(6, _api.ReadStatic<IntPtr>(_bottom, "<BackLock>k__BackingField"), _multi);
            _captured = true; return Read();
        }
        catch { CleanupUnknown = true; throw; }
    }
    private IntPtr Method(IntPtr type, string name, string[] args, string returns, bool isStatic, long rva)
    {
        var method = isStatic ? _api.InspectDeclaredStaticMethod(type, name, args, returns, _module)
            : _api.InspectDeclaredMethod(type, name, args, returns, _module);
        if (method.Entry?.Rva != rva || method.GenericMethod || method.InflatedMethod || (method.Flags & 0x400) != 0
            || ((method.Flags & 0x10) != 0) != isStatic) throw new UnsupportedSignatureException("Menu method metadata changed: " + name);
        return method.MethodInfo;
    }
    private void Root(int slot, IntPtr value, IntPtr type)
    {
        if (_roots[slot] is not null) throw new InvalidOperationException("Menu root slot is already occupied.");
        _roots[slot] = _api.Root(value); // Record before class/liveness reads that may fail.
        if (_api.ObjectClass(_roots[slot]!.Target) != type) throw new UnsupportedSignatureException("Menu object differs from its exact class.");
    }
    private bool Alive(IntPtr value) => _api.InvokeValueWithPublication<bool>(_alive, IntPtr.Zero, A.Ref(value));
    public NativeMenuObservation Read()
    {
        _main.VerifyAccess(); if (!_captured || CleanupUnknown) throw new InvalidOperationException("Menu native observations are not confirmed.");
        try
        {
            var manager = _roots[0]!.Target;
            NativeMenuObject Item(int componentSlot, int goSlot, string field)
            {
                var item = _roots[componentSlot]!.Target; var go = _roots[goSlot]!.Target;
                var alive = Alive(item) && Alive(go);
                return new(item.ToInt64(), go.ToInt64(), _api.ReadWithPublication<IntPtr>(manager, field) == item,
                    alive, alive && _api.InvokeValueWithPublication<bool>(_active, go));
            }
            var normal = _roots[5]!.Target; var back = _roots[6]!.Target;
            return new(manager.ToInt64(), _api.ReadStatic<IntPtr>(_manager, "Instance") == manager && Alive(manager),
                Item(1, 3, "mainCamera"), Item(2, 4, "mainParticle"),
                new(normal.ToInt64(), _api.ReadStatic<IntPtr>(_touch, "normalLock") == normal, _api.ReadWithPublication<int>(normal, "count")),
                new(back.ToInt64(), _api.ReadStatic<IntPtr>(_bottom, "<BackLock>k__BackingField") == back, _api.ReadWithPublication<int>(back, "count")));
        }
        catch { CleanupUnknown = true; throw; }
    }
    public void Bind(NativeLocalMenuLedger owner)
    {
        _main.VerifyAccess(); if (!_captured || _bindAttempted) throw new InvalidOperationException("Menu observations bind once after capture.");
        _bindAttempted = true; _owner = owner;
        for (var index = 0; index < 4; ++index)
        {
            var operation = (LocalMenuOperation)index; var target = _targets[index]!;
            _bindings[index] = index == 0
                ? _broker.BindRetainedBooleanArgumentObservation(target, self => owner.Matches(operation, self.ToInt64()),
                    (self, value) => owner.CaptureInvocation(operation, self.ToInt64(), value), (frame, _) => owner.CompleteInvocation(frame))
                : _broker.BindEarlyRetainedObservation(target, self => owner.Matches(operation, self.ToInt64()),
                    self => owner.CaptureInvocation(operation, self.ToInt64(), 0), owner.CompleteInvocation);
            _probes[index] = _broker.BindBoundaryProbe(target,
                (self, _, method) => owner.ProbeEnter(operation, self.ToInt64(), method == IntPtr.Zero || method == target.MethodInfo),
                (self, _, _, returned) => owner.ProbeExit(operation, self.ToInt64(), returned));
        }
    }
    public ExactHookBroker.ManagedBoundaryActivity ReadActivity()
    {
        _main.VerifyAccess(); long count = 0;
        for (var index = 0; index < 4; ++index)
        {
            if (_bindings[index] is null || _probes[index] is null) return new(count, false);
            var normal = _bindings[index]!.ReadActivity(); var probe = _probes[index]!.ReadActivity();
            if (!normal.Known || !probe.Known || normal.InFlight < 0 || probe.InFlight < 0) return new(count, false);
            count = checked(count + Math.Max(normal.InFlight, probe.InFlight));
        }
        return new(count, !CleanupUnknown);
    }
    public void Invoke(LocalMenuOperation operation, int slot, byte value)
    {
        _main.VerifyAccess(); if (CleanupUnknown || value > 1 || slot < 0 || slot > 3
            || operation == LocalMenuOperation.SetActive != (slot < 2)) throw new InvalidOperationException("Invalid menu mutation role.");
        var args = operation == LocalMenuOperation.SetActive ? new[] { A.Value(value != 0) } : Array.Empty<A>();
        _arguments.Begin(args);
        try { _api.InvokeVoidWithPublication(_targets[(int)operation]!.MethodInfo, _roots[slot + 3]!.Target, args); _arguments.Complete(); }
        catch { if (_arguments.Count != 0) _arguments.Fail(); CleanupUnknown = true; throw; }
    }
    public void ReleaseObservers()
    {
        _main.VerifyAccess(); if (_releaseAttempted || CleanupUnknown || _arguments.CleanupUnknown) throw new InvalidOperationException("Menu observer release is unknown or already attempted.");
        _releaseAttempted = true;
        try
        {
            for (var index = 3; index >= 0; --index) { _bindings[index]?.Dispose(); _probes[index]?.Dispose(); }
            if (ReadActivity() is not { Known: true, InFlight: 0 } || _owner?.Read().Unknown != false)
                throw new InvalidOperationException("Menu physical callbacks did not drain cleanly after logical detach.");
            var failures = new List<Exception>();
            for (var slot = 6; slot >= 0; --slot)
                if (_roots[slot] is { } root && !_freeAttempted[slot])
                { _freeAttempted[slot] = true; try { root.Dispose(); _roots[slot] = null; } catch (Exception error) { failures.Add(error); } }
            if (failures.Count != 0) throw new AggregateException(failures);
        }
        catch { CleanupUnknown = true; throw; }
    }
}
