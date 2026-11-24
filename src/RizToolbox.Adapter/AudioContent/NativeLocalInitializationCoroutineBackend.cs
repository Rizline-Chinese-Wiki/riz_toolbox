using System.Runtime.InteropServices;
using System.Security.Cryptography;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal sealed class NativeLocalInitializationCoroutineBackend : IInitializationCoroutineBackend
{
    private readonly NativeRuntime _api; private readonly IMainThreadDispatcher _main;
    private readonly AdapterBuildProfile _profile; private readonly IntPtr _module; private readonly ExactHookBroker _broker;
    private const int WaitUntilCurrentRoot = 4;
    private readonly NativeRuntime.RootHandle?[] _roots = new NativeRuntime.RootHandle?[5];
    private readonly bool[] _freeAttempted = new bool[5];
    private readonly long[] _enumerators = new long[2];
    private readonly IntPtr[] _enumClasses = new IntPtr[2];
    private readonly ExactHookBroker.Binding?[] _bindings = new ExactHookBroker.Binding?[3];
    private readonly VerifiedHookTarget?[] _targets = new VerifiedHookTarget?[3];
    private VerifiedHookTarget _delayTodo = null!, _waitPredicate = null!, _waitTodo = null!;
    private IntPtr _action, _predicate, _closure, _controllerClass, _waitUntilClass;
    private NativeExactReadField _waitCurrentField = null!;
    private NativeLocalInitializationCoroutineTracker? _owner;
    private bool _attempted, _ready, _detachAttempted, _freeBegun;
    public bool CleanupUnknown { get; private set; }
    public int RootRecords { get { _main.VerifyAccess(); return _roots.Count(root => root is not null); } }
    public bool SupportsPerRecordRetirement => true;
    internal NativeLocalInitializationCoroutineBackend(NativeRuntime api, IMainThreadDispatcher main,
        AdapterBuildProfile profile, IntPtr module, ExactHookBroker broker)
    {
        ArgumentNullException.ThrowIfNull(api); ArgumentNullException.ThrowIfNull(main); ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(broker);
        (_api, _main, _profile, _module, _broker) = (api, main, profile, module, broker);
    }
    public void Initialize(NativeLocalInitializationCoroutineTracker owner)
    {
        _main.VerifyAccess(); if (_attempted) throw new InvalidOperationException("Coroutine metadata and bindings initialize once.");
        _attempted = true; _owner = owner;
        try
        {
            if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || _module == IntPtr.Zero
                || _profile.GameAssemblySha256 != WorkshopChartModule.Fingerprint || _profile.UnityVersion != "2022.3.62f2")
                throw new UnsupportedSignatureException("Initialization coroutines require the pinned Windows x64 build.");
            using (var stream = File.OpenRead(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "GameAssembly.dll")))
            using (var sha = SHA256.Create())
                if (Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant() != WorkshopChartModule.Fingerprint)
                    throw new UnsupportedSignatureException("Coroutine image differs from the pinned profile.");
            var simple = Native(() => _api.Class("Assembly-CSharp.dll", "", "SimpleCoroutine"));
            _controllerClass = Native(() => _api.Class("Assembly-CSharp.dll", "", "NormalModeInLevelController"));
            _closure = Native(() => _api.NestedClass(_controllerClass, "<>c"));
            var closureName = Native(() => _api.InspectMetadataClass(_closure)).TypeName;
            Native(() => _api.RequireStaticFieldLayout(_closure, "<>9", closureName, 0));
            _enumClasses[0] = Native(() => _api.NestedClass(simple, "<Delay>d__4"));
            _enumClasses[1] = Native(() => _api.NestedClass(simple, "<WaitUntil>d__8"));
            for (var index = 0; index < 2; ++index)
            {
                Native(() => _api.RequireInstanceFieldLayout(_enumClasses[index], "<>1__state", "System.Int32", 0x10));
                Native(() => _api.RequireInstanceFieldLayout(_enumClasses[index], "<>2__current", "System.Object", 0x18));
                Native(() => _api.RequireInstanceFieldLayout(_enumClasses[index], "todo", "System.Action", 0x28));
                _targets[index + 1] = Native(() => _api.ResolveExactHook("local.initial-coroutine.move." + index, _module, _enumClasses[index], "MoveNext",
                    Array.Empty<string>(), index == 0 ? 0x3AF0A0 : 0x3B2D70, ExactHookAbi.InstanceBooleanReturn0));
            }
            Native(() => _api.RequireInstanceFieldLayout(_enumClasses[0], "seconds", "System.Single", 0x20));
            Native(() => _api.RequireInstanceFieldLayout(_enumClasses[0], "<deltaTimeProvider>5__2", "System.Func<System.Single>", 0x30));
            Native(() => _api.RequireInstanceFieldLayout(_enumClasses[1], "predicate", "System.Func<System.Boolean>", 0x20));
            _waitUntilClass = Native(() => _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "WaitUntil"));
            _waitCurrentField = Native(() => _api.InspectExactDeclaredReadField(_enumClasses[1], "<>2__current", "System.Object", false, 0x18));
            Native(() => _api.RequireInstanceFieldLayout(_waitUntilClass, "m_Predicate", "System.Func<System.Boolean>", 0x10));
            _action = Native(() => _api.Class("mscorlib.dll", "System", "Action"));
            _predicate = Native(() => _api.InspectMetadataFieldClass(_enumClasses[1], "predicate", "System.Func<System.Boolean>")).FieldClass.Class;
            // Named official field metadata plus inspected constructor writes; no guessed
            // Delegate offsets and no reflection invocation creates additional targets.
            var delegateClass = Native(() => _api.Class("mscorlib.dll", "System", "Delegate"));
            var multicast = Native(() => _api.Class("mscorlib.dll", "System", "MulticastDelegate"));
            foreach (var field in new[] { ("method_ptr", "System.IntPtr", 0x10), ("invoke_impl", "System.IntPtr", 0x18),
                ("m_target", "System.Object", 0x20), ("method", "System.IntPtr", 0x28), ("method_code", "System.IntPtr", 0x40), ("method_is_virtual", "System.Boolean", 0x70) })
                Native(() => _api.RequireInstanceFieldLayout(delegateClass, field.Item1, field.Item2, field.Item3));
            Native(() => _api.RequireInstanceFieldLayout(multicast, "delegates", "System.Delegate[]", 0x78));
            Native(() => _api.RequireAssignable(multicast, _action)); Native(() => _api.RequireAssignable(multicast, _predicate));
            _delayTodo = Native(() => _api.ResolveExactHook("local.initial-coroutine.delay.action", _module, _controllerClass,
                "<GetInsideLevelParams>b__114_0", Array.Empty<string>(), 0x3C1930, ExactHookAbi.InstanceVoid));
            _waitPredicate = Native(() => _api.ResolveExactHook("local.initial-coroutine.wait.predicate", _module, _controllerClass,
                "<GetInsideLevelParams>b__114_1", Array.Empty<string>(), 0x3C1960, ExactHookAbi.InstanceBooleanReturn0));
            _waitTodo = Native(() => _api.ResolveExactHook("local.initial-coroutine.wait.action", _module, _closure,
                "<GetInsideLevelParams>b__114_2", Array.Empty<string>(), 0x3B1B60, ExactHookAbi.InstanceVoid));
            foreach (var target in new[] { _delayTodo, _waitPredicate, _waitTodo })
            {
                var layout = Native(() => Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(target.MethodInfo));
                if (layout.VirtualMethodPointer != target.Address || (layout.Flags & 0x40) != 0)
                    throw new UnsupportedSignatureException("Closed getter delegates require their exact nonvirtual direct entry.");
            }
            _targets[0] = Native(() => _api.ResolveCoroutineStartHook(_module)); var alias = Native(() => _api.ResolveCoroutineStartHook(_module, true));
            RequireObservationKnown();
            _bindings[0] = _broker.BindNativeCoroutineObservation(_targets[0]!, NativeCoroutineKind.Start, owner.CaptureStart, owner.CompleteStart, owner.ObserveFault, alias);
            RequireObservationKnown();
            for (var index = 0; index < 2; ++index)
            {
                var role = (InitializationCoroutineRole)index;
                RequireObservationKnown();
                _bindings[index + 1] = _broker.BindNativeCoroutineObservation(_targets[index + 1]!, NativeCoroutineKind.MoveNext,
                    frame => owner.CaptureMove(role, frame), owner.CompleteMove, owner.ObserveFault);
                RequireObservationKnown();
            }
            _ready = true;
        }
        catch { CleanupUnknown = true; throw; }
    }
    public void RetainEnumerator(InitializationCoroutineRole role, long enumerator)
    {
        Check(); var index = (int)role; if (_roots[index] is not null) throw new InvalidOperationException("IEnumerator root slot already occupied.");
        try
        {
            _enumerators[index] = enumerator; RequireObservationKnown();
            _roots[index] = _api.RetainExactObject(new(enumerator), _enumClasses[index]);
            // Store the newly acquired responsibility before a late fault check.
            RequireObservationKnown();
        }
        catch { CleanupUnknown = true; throw; }
    }
    public InitializationCoroutineObservation ReadEnumerator(InitializationCoroutineRole role)
    {
        Check(); var index = (int)role;
        try
        {
            var value = Native(() => _roots[index]?.Target ?? throw new InvalidOperationException("No independent IEnumerator root."));
            if (value.ToInt64() != _enumerators[index] || Native(() => _api.ObjectClass(value)) != _enumClasses[index])
                throw new UnsupportedSignatureException("The independently rooted IEnumerator changed identity or class.");
            var controller = _owner!.ControllerIdentity;
            var todo = Read<IntPtr>(value, "todo");
            var predicate = index == 0 ? IntPtr.Zero : Read<IntPtr>(value, "predicate");
            if (index == 0) RequireDelegate(todo, _action, new(controller), _delayTodo);
            else
            {
                RequireDelegate(predicate, _predicate, new(controller), _waitPredicate);
                var closure = Native(() => _api.ReadStatic<IntPtr>(_closure, "<>9"));
                Native(() => HookTargetValidation.VerifyReadable(closure, 16));
                if (Native(() => Marshal.ReadIntPtr(closure)) != _closure) throw new UnsupportedSignatureException("The actual cached release closure changed.");
                RequireDelegate(todo, _action, closure, _waitTodo);
            }
            return new(value.ToInt64(), Read<int>(value, "<>1__state"), todo.ToInt64(), predicate.ToInt64(),
                index == 0 ? BitConverter.SingleToInt32Bits(Read<float>(value, "seconds")) : 0);
        }
        catch { CleanupUnknown = true; throw; }
    }
    public long ReadWaitUntilCurrent(long parent, long predicate, bool retain)
    {
        Check();
        try
        {
            const int slot = (int)InitializationCoroutineRole.WaitUntil;
            var owner = Native(() => _roots[slot]?.Target ?? IntPtr.Zero);
            if (parent == 0 || predicate == 0 || owner.ToInt64() != parent || parent != _enumerators[slot]
                || Native(() => _api.ObjectClass(owner)) != _enumClasses[slot] || Read<int>(owner, "<>1__state") != 1
                || Read<IntPtr>(owner, "predicate").ToInt64() != predicate)
                throw new UnsupportedSignatureException("WaitUntil.Current has no matching state-one iterator and predicate.");
            if (retain)
            {
                if (_roots[WaitUntilCurrentRoot] is not null) throw new InvalidOperationException("WaitUntil.Current was already retained.");
                // Adopt the reference returned by the rooted iterator before inspecting
                // its concrete type or invoking another ownership guard.
                Native(() => _api.PublishExactReferenceField(_roots[slot]!, _waitCurrentField, current =>
                {
                    if (current != IntPtr.Zero) _roots[WaitUntilCurrentRoot] = _api.Root(current);
                }));
            }
            var child = Native(() => _roots[WaitUntilCurrentRoot]?.Target ?? IntPtr.Zero);
            if (child == IntPtr.Zero || Native(() => _api.ObjectClass(child)) != _waitUntilClass
                || Read<IntPtr>(child, "m_Predicate").ToInt64() != predicate || Read<IntPtr>(owner, "<>2__current") != child)
                throw new UnsupportedSignatureException("The rooted UnityEngine.WaitUntil or its original predicate differs from Current.");
            return child.ToInt64();
        }
        catch { CleanupUnknown = true; throw; }
    }
    private void RequireDelegate(IntPtr value, IntPtr exactClass, IntPtr target, VerifiedHookTarget method)
    {
        Native(() => HookTargetValidation.VerifyReadable(value, 16));
        if (Native(() => Marshal.ReadIntPtr(value)) != exactClass
            || Read<IntPtr>(value, "m_target") != target
            || Read<IntPtr>(value, "method") != method.MethodInfo
            || Read<IntPtr>(value, "method_ptr") != method.Address
            || Read<IntPtr>(value, "invoke_impl") != method.Address
            || Read<IntPtr>(value, "method_code") != target
            || Read<IntPtr>(value, "delegates") != IntPtr.Zero
            || Read<bool>(value, "method_is_virtual"))
            throw new UnsupportedSignatureException("Coroutine callback differs from its exact closed single-cast declared method and target.");
    }
    public void RetainCoroutine(InitializationCoroutineRole role, long coroutine)
    {
        Check(); var index = (int)role + 2;
        if (_roots[index] is not null) throw new InvalidOperationException("Coroutine handle slot already occupied.");
        try
        {
            RequireObservationKnown(); _roots[index] = _api.RetainExactObject(new(coroutine), _targets[0]!.ReturnClass);
            RequireObservationKnown();
        }
        catch { CleanupUnknown = true; throw; }
    }
    public ExactHookBroker.ManagedBoundaryActivity ReadActivity()
        => ReadActivityCore(true);
    private ExactHookBroker.ManagedBoundaryActivity ReadActivityCore(bool includeCleanupFailure)
    {
        _main.VerifyAccess(); long total = 0;
        foreach (var binding in _bindings)
        {
            if (_owner is null || _owner.ObservationAuthorityUnknown || includeCleanupFailure && CleanupUnknown) return new(total, false);
            if (!includeCleanupFailure) _owner.RequireCancellationRetirement();
            var value = binding?.ReadActivity();
            if (!includeCleanupFailure) _owner.RequireCancellationRetirement();
            if (_owner.ObservationAuthorityUnknown) return new(total, false);
            if (value is not { Known: true, InFlight: >= 0 } || total > long.MaxValue - value.InFlight)
            { CleanupUnknown = true; return new(0, false); }
            total += value.InFlight;
        }
        if (!includeCleanupFailure) _owner!.RequireCancellationRetirement();
        return new(total, _owner is not null && !_owner.ObservationAuthorityUnknown && (!includeCleanupFailure || !CleanupUnknown));
    }
    public void Detach()
    {
        Check(); if (_detachAttempted) throw new InvalidOperationException("Coroutine logical detach is once-only.");
        _owner!.RequireCancellationRetirement();
        _detachAttempted = true;
        try { foreach (var binding in _bindings) { _owner.RequireCancellationRetirement(); Native(() => binding!.Dispose()); _owner.RequireCancellationRetirement(); } }
        catch { CleanupUnknown = true; throw; }
    }
    public void ReleaseRoots()
    {
        _main.VerifyAccess(); if (!_detachAttempted || _freeBegun || CleanupUnknown || _owner!.Unknown)
            throw new InvalidOperationException("Only known detached observation roots may release once.");
        _owner.RequireCancellationRetirement();
        _freeBegun = true; var failures = new List<Exception>();
        for (var index = _roots.Length - 1; index >= 0; --index)
        {
            if (_roots[index] is not { } root) continue;
            try { RequireCleanupIdle(); }
            catch (Exception error) { CleanupUnknown = true; failures.Add(error); break; }
            if (_freeAttempted[index]) { failures.Add(new InvalidOperationException("Unknown root free is never retried.")); continue; }
            _owner.RequireCancellationRetirement(); _freeAttempted[index] = true;
            try { root.Dispose(); _roots[index] = null; } catch (Exception error) { CleanupUnknown = true; failures.Add(error); }
            // Local free failure does not invalidate another independent root.
            // Launch/worker authority loss or changed activity always stops here.
            try { RequireCleanupIdle(); }
            catch (Exception error) { CleanupUnknown = true; failures.Add(error); break; }
        }
        if (failures.Count != 0) { CleanupUnknown = true; throw new AggregateException(failures); }
    }
    public void ReleaseEndedRoots(InitializationCoroutineRole role, long enumerator)
    {
        Check(); var slot = (int)role;
        if (slot is < 0 or > 1 || _enumerators[slot] != enumerator
            || !_owner!.IsRetiringForCleanup(role, enumerator) || _roots[slot] is null)
            throw new InvalidOperationException("Initialization cleanup requires the exact withdrawn per-role generation.");
        var failures = new List<Exception>(2);
        var ownedSlots = role == InitializationCoroutineRole.WaitUntil ? new[] { WaitUntilCurrentRoot, slot + 2, slot } : new[] { slot + 2, slot };
        foreach (var index in ownedSlots)
        {
            if (_roots[index] is not { } root) continue;
            try { _owner.RequireEngineRetirementFree(role, enumerator); RequireCleanupIdle(); }
            catch (Exception error) { CleanupUnknown = true; failures.Add(error); break; }
            if (_freeAttempted[index]) { failures.Add(new InvalidOperationException("Unknown role root free cannot retry.")); continue; }
            try { _owner.RequireEngineRetirementFree(role, enumerator); }
            catch (Exception error) { CleanupUnknown = true; failures.Add(error); break; }
            _owner.RequireCancellationRetirement(); _freeAttempted[index] = true;
            try { root.Dispose(); _roots[index] = null; }
            catch (Exception error) { CleanupUnknown = true; failures.Add(error); }
            try { _owner.RequireEngineRetirementFree(role, enumerator); RequireCleanupIdle(); }
            catch (Exception error) { CleanupUnknown = true; failures.Add(error); break; }
        }
        if (failures.Count != 0) { CleanupUnknown = true; throw new AggregateException("Initialization role cleanup is unconfirmed and cannot repeat.", failures); }
    }
    private void Check()
    { _main.VerifyAccess(); if (!_ready || _detachAttempted || _freeBegun) throw new InvalidOperationException("Coroutine backend is not active."); RequireObservationKnown(); }
    private T Read<T>(IntPtr value, string field) where T : struct => Native(() => _api.ReadWithPublication<T>(value, field));
    private T Native<T>(Func<T> operation)
    { RequireObservationKnown(); var result = operation(); RequireObservationKnown(); return result; }
    private void Native(Action operation)
    { RequireObservationKnown(); operation(); RequireObservationKnown(); }
    private void RequireObservationKnown()
    {
        if (CleanupUnknown || _owner is null || _owner.ObservationAuthorityUnknown)
            throw new InvalidOperationException("Initialization coroutine observation authority is unknown.");
    }
    private void RequireCleanupIdle()
    {
        if (_owner is null || _owner.ObservationAuthorityUnknown)
            throw new InvalidOperationException("Initialization coroutine external authority changed during cleanup.");
        _owner.RequireCancellationRetirement();
        var activity = ReadActivityCore(false);
        _owner.RequireCancellationRetirement();
        if (_owner.ObservationAuthorityUnknown || activity is not { Known: true, InFlight: 0 })
            throw new InvalidOperationException("Initialization coroutine activity changed during observer cleanup.");
    }
}
