using System.Security.Cryptography;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.AudioContent;

/// <summary>Real official-export bridge; exact metadata invocation only, no delegate storage offsets or constructed callbacks.</summary>
internal sealed class NativeControllerGlobalsBackend : IControllerGlobalsBackend
{
    private const string Fingerprint = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79";
    private static readonly string[] Types = { "InLevelControl.LevelStartDelegate", "InLevelControl.GetJudgeDelegate",
        "InLevelControl.LevelOverDelegate", "InLevelControl.SetUiColorDelegate", "InLevelControl.ControllerOverDelegate", "System.Func<LevelResultType>" };
    private readonly NativeRuntime _api;
    private readonly IMainThreadDispatcher _main;
    private readonly AdapterBuildProfile _profile;
    private readonly IntPtr _module;
    private readonly Func<IntPtr, NativeRuntime.RootHandle> _allocateRoot;
    private NativeReference? _manager, _owner;
    // Every root created directly by this backend is registered before it can be validated,
    // transferred or used in a temporary scope. Failed frees remain recorded and are never retried.
    private readonly NativeReference?[] _roots = new NativeReference?[64];
    private IntPtr _managerClass, _ownerClass, _controllerClass, _unityObject, _delegateClass;
    private IntPtr _targetGetter, _invocationList, _alive;
    private readonly IntPtr[] _fieldClasses = new IntPtr[6];
    private bool _initialized, _disposeAttempted, _rootCleanupUnknown;
    internal NativeControllerGlobalsBackend(NativeRuntime api, IMainThreadDispatcher main, AdapterBuildProfile profile, IntPtr module,
        Func<IntPtr, NativeRuntime.RootHandle>? allocateRoot = null)
    {
        (_api, _main, _profile, _module) = (api, main, profile, module);
        _allocateRoot = allocateRoot ?? api.Root;
    }

    internal void Initialize(NativeRuntime.RootHandle gameManager, NativeRuntime.RootHandle inLevelControl)
    {
        _main.VerifyAccess();
        if (_initialized || _manager is not null || _owner is not null) throw new InvalidOperationException("Globals backend initialization is one-shot.");
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || _module == IntPtr.Zero
            || _profile.GameAssemblySha256 != Fingerprint || _profile.UnityVersion != "2022.3.62f2")
            throw new UnsupportedSignatureException("Controller globals require the pinned Windows x64 Unity 2022.3.62f2 build.");
        var file = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "GameAssembly.dll");
        using (var stream = File.OpenRead(file)) using (var sha = SHA256.Create())
            if (Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant() != Fingerprint)
                throw new UnsupportedSignatureException("Controller-global image fingerprint differs from the pinned profile.");
        // Record each newly acquired root immediately; the process-owned coordinator already owns this backend.
        _manager = RootNative(gameManager.Target); _owner = RootNative(inLevelControl.Target);
        _managerClass = _api.Class("Assembly-CSharp.dll", "", "GameManager");
        _ownerClass = _api.Class("Assembly-CSharp.dll", "", "InLevelControl");
        _controllerClass = _api.Class("Assembly-CSharp.dll", "", "NormalModeInLevelController");
        _unityObject = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Object");
        _delegateClass = _api.Class("mscorlib.dll", "System", "Delegate");
        if (_api.ObjectClass(_manager.Target) != _managerClass || _api.ObjectClass(_owner.Target) != _ownerClass
            || _api.DescribeFieldType(_managerClass, "Instance") != "GameManager")
            throw new UnsupportedSignatureException("Controller-global owner classes or singleton metadata changed.");
        _api.RequireInstanceFieldLayout(_managerClass, "inLevelControl", "InLevelControl", 0x38);
        _api.RequireInstanceFieldLayout(_ownerClass, "controllerSet", "System.Boolean", 0x20);
        for (var index = 0; index < Types.Length; index++)
        {
            var field = OwnedNativeControllerGlobals.FieldNames[index];
            _api.RequireInstanceFieldLayout(_ownerClass, field, Types[index], 0x28 + index * IntPtr.Size);
            _fieldClasses[index] = _api.InspectMetadataFieldClass(_ownerClass, field, Types[index]).FieldClass.Class;
            _api.RequireAssignable(_delegateClass, _fieldClasses[index]);
        }
        var target = _api.InspectDeclaredMethod(_delegateClass, "get_Target", Array.Empty<string>(), "System.Object", _module);
        var multicast = _api.Class("mscorlib.dll", "System", "MulticastDelegate");
        var list = _api.InspectDeclaredMethod(multicast, "GetInvocationList", Array.Empty<string>(), "System.Delegate[]", _module);
        if (target.Entry is not { Rva: 0x374D80 } || list.Entry is not { Rva: 0xF70660 })
            throw new UnsupportedSignatureException("Delegate Target/GetInvocationList do not have their verified pinned executable entries.");
        _targetGetter = target.MethodInfo; _invocationList = list.MethodInfo;
        _alive = _api.Method(_unityObject, "op_Implicit", 1, true, new[] { "UnityEngine.Object" });
        if (_api.DescribeReturnType(_alive) != "System.Boolean")
            throw new UnsupportedSignatureException("Unity object liveness return metadata changed.");
        _initialized = true;
        if (!IsCurrentOwner) throw new TargetExpiredException("The supplied roots are not the current live GameManager.inLevelControl owner.");
    }
    public long OwnerIdentity { get { Check(); return _owner!.Target.ToInt64(); } }
    public bool IsCurrentOwner
    {
        get
        {
            Check();
            if (_api.ReadStatic<IntPtr>(_managerClass, "Instance") != _manager!.Target
                || _api.Read<IntPtr>(_manager.Target, "inLevelControl") != _owner!.Target) return false;
            return Alive(_manager.Target) && Alive(_owner.Target);
        }
    }
    public IControllerGlobalsReference RetainController(IntPtr controller)
    {
        Check();
        return RetainValidated(controller, root =>
        {
            var pointer = new IntPtr(root.Identity);
            if (_api.ObjectClass(pointer) != _controllerClass || !Alive(pointer))
                throw new UnsupportedSignatureException("Installed global delegates require the exact live NormalModeInLevelController.");
        });
    }
    public IControllerGlobalsReference? CaptureDelegate(int index)
    {
        CheckIndex(index); var value = _api.Read<IntPtr>(_owner!.Target, OwnedNativeControllerGlobals.FieldNames[index]);
        if (value == IntPtr.Zero) return null;
        return RetainValidated(value, retained => RequireDelegate(index, RequireNative(retained)));
    }
    public void VerifyBaseline(int index, IControllerGlobalsReference? value)
    {
        CheckIndex(index); if (value is null) return;
        using var target = Target(index, RequireNative(value));
        if (target is null) return; // A verified single-cast static/open target is null; never infer a Unity object from it.
        _api.RequireAssignable(_unityObject, _api.ObjectClass(target.Target));
        if (!Alive(target.Target)) throw new TargetExpiredException("The baseline delegate target's Unity object was destroyed.");
    }
    public bool TargetsController(int index, IControllerGlobalsReference value, long controller)
    {
        CheckIndex(index); using var target = Target(index, RequireNative(value));
        return target is not null && target.Target.ToInt64() == controller && _api.ObjectClass(target.Target) == _controllerClass;
    }
    public bool ReadControllerSet() { Check(); return _api.Read<bool>(_owner!.Target, "controllerSet"); }
    public bool WriteDelegateIfSame(int index, long expected, IControllerGlobalsReference? baseline)
    {
        CheckIndex(index); if (baseline is not null) RequireDelegate(index, RequireNative(baseline));
        if (!IsCurrentOwner || ReadDelegateId(index) != expected) return false;
        _api.WriteExact(_owner!.Handle, _ownerClass, OwnedNativeControllerGlobals.FieldNames[index], Types[index],
            A.Ref(baseline is null ? IntPtr.Zero : RequireNative(baseline).Pointer));
        if (ReadDelegateId(index) != (baseline?.Identity ?? 0))
            throw new InvalidOperationException("Restored delegate did not read back the exact baseline identity.");
        return true;
    }
    public bool WriteControllerSetIfSame(bool expected, bool baseline, IReadOnlyList<long> expectedDelegates)
    {
        Check();
        if (expectedDelegates.Count != 6) throw new ArgumentException("All six restored delegate identities are required.");
        if (!IsCurrentOwner || ReadControllerSet() != expected) return false;
        for (var index = 0; index < 6; index++) if (ReadDelegateId(index) != expectedDelegates[index]) return false;
        if (expected != baseline) _api.WriteExact(_owner!.Handle, _ownerClass, "controllerSet", "System.Boolean", A.Value(baseline));
        if (ReadControllerSet() != baseline) throw new InvalidOperationException("controllerSet did not preserve its restored baseline.");
        return true;
    }
    private NativeReference? Target(int index, NativeReference value)
    {
        RequireDelegate(index, value);
        using var list = RootNative(_api.Invoke(_invocationList, value.Pointer));
        if (_api.InspectMetadataClass(_api.ObjectClass(list.Target)).TypeName != "System.Delegate[]"
            || _api.Unbox<int>(_api.CallTyped(list.Target, "get_Rank", Array.Empty<string>())) != 1
            || _api.Unbox<int>(_api.CallTyped(list.Target, "get_Length", Array.Empty<string>())) != 1)
            throw new UnsupportedSignatureException("Only an exact single-cast delegate invocation list is accepted for global ownership.");
        using var item = RootNative(_api.CallTyped(list.Target, "GetValue", new[] { "System.Int32" }, A.Value(0)));
        if (item.Target != value.Pointer) throw new UnsupportedSignatureException("The single invocation list item is not the exact retained delegate.");
        var target = _api.Invoke(_targetGetter, value.Pointer);
        return target == IntPtr.Zero ? null : RootNative(target);
    }
    private long ReadDelegateId(int index) => _api.Read<IntPtr>(_owner!.Target, OwnedNativeControllerGlobals.FieldNames[index]).ToInt64();
    private void RequireDelegate(int index, NativeReference value)
    { if (_api.ObjectClass(value.Pointer) != _fieldClasses[index]) throw new UnsupportedSignatureException("Global delegate differs from its exact field class."); }
    private bool Alive(IntPtr value) => _api.Unbox<bool>(_api.Invoke(_alive, IntPtr.Zero, A.Ref(value)));
    private void CheckIndex(int index) { Check(); if (index is < 0 or >= 6) throw new ArgumentOutOfRangeException(nameof(index)); }
    private void Check()
    {
        _main.VerifyAccess();
        if (!_initialized || _disposeAttempted || _rootCleanupUnknown) throw new InvalidOperationException("Controller-global native backend is not active or root cleanup is unknown.");
    }
    private static NativeReference RequireNative(IControllerGlobalsReference value)
        => value as NativeReference ?? throw new UnsupportedSignatureException("Controller-global reference belongs to another backend.");
    public void Dispose()
    {
        _main.VerifyAccess(); if (_disposeAttempted) throw new InvalidOperationException("Native globals cleanup is one-shot and cannot retry unknown frees.");
        var failures = new List<Exception>(_roots.Length); _disposeAttempted = true;
        for (var index = _roots.Length - 1; index >= 0; index--)
        {
            var root = _roots[index]; if (root is null) continue;
            if (root.FreeAttempted)
            { failures.Add(new InvalidOperationException("A previous native root free is unknown and will not be retried.")); continue; }
            try { root.Dispose(); } catch (Exception error) { failures.Add(error); }
        }
        if (failures.Count != 0) throw new AggregateException("Native global owner roots have unconfirmed cleanup.", failures);
        _owner = null; _manager = null;
    }
    private NativeReference RootNative(IntPtr value)
    {
        _main.VerifyAccess();
        if (_disposeAttempted || _rootCleanupUnknown) throw new InvalidOperationException("Cannot create another native root after uncertain cleanup.");
        var slot = Array.FindIndex(_roots, x => x is null);
        if (slot < 0) throw new InvalidOperationException("Native globals root record capacity is exhausted.");
        var reference = new NativeReference(this, slot); _roots[slot] = reference;
        try { reference.Attach(_allocateRoot(value)); return reference; }
        catch { _roots[slot] = null; throw; } // No RootHandle was returned or transferred when this path executes.
    }
    /// <summary>The same registered-root validation path used by controller and delegate captures.</summary>
    internal IControllerGlobalsReference RetainValidated(IntPtr value, Action<IControllerGlobalsReference> validate)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(validate);
        var root = RootNative(value);
        try { validate(root); return root; }
        catch (Exception failure)
        {
            try { root.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException("Native reference validation failed and its registered root free is unknown.", failure, cleanup); }
            throw;
        }
    }
    internal int OutstandingRootRecords { get { _main.VerifyAccess(); return _roots.Count(x => x is not null); } }
    private sealed class NativeReference : IControllerGlobalsReference
    {
        private readonly NativeControllerGlobalsBackend _owner;
        private readonly int _slot;
        private NativeRuntime.RootHandle? _root;
        internal bool FreeAttempted { get; private set; }
        internal NativeReference(NativeControllerGlobalsBackend owner, int slot) => (_owner, _slot) = (owner, slot);
        internal void Attach(NativeRuntime.RootHandle root) => _root = root;
        internal NativeRuntime.RootHandle Handle
        { get { _owner._main.VerifyAccess(); if (FreeAttempted || _root is null) throw new ObjectDisposedException(nameof(NativeReference)); return _root; } }
        internal IntPtr Target => Handle.Target;
        internal IntPtr Pointer => Target;
        public long Identity => Pointer.ToInt64();
        public void Dispose()
        {
            _owner._main.VerifyAccess();
            if (FreeAttempted)
            { if (_root is not null) throw new InvalidOperationException("Native root free is unknown and will not be retried."); return; }
            FreeAttempted = true;
            try { _root!.Dispose(); _root = null; _owner._roots[_slot] = null; }
            catch { _owner._rootCleanupUnknown = true; throw; }
        }
    }
}
