using System.Runtime.InteropServices;
using System.Security.Cryptography;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

/// <summary>Production typed Start/Move transport and independent roots for the
/// two EventTrigger coroutine sources. Source/caller/generation admission remains
/// with the retained tracker. No listener or whole-run completion is inferred.</summary>
internal sealed partial class NativeLocalCutoutCoroutineBackend : ICutoutCoroutineBackend
{
    private const int Capacity = NativeLocalCutoutCoroutineTracker.MaximumRecords;
    private readonly NativeRuntime _api;
    private readonly IMainThreadDispatcher _main;
    private readonly AdapterBuildProfile _profile;
    private readonly IntPtr _module;
    private readonly ExactHookBroker _broker;
    private readonly NativeLocalCutoutEventSource _source;
    private readonly NativeRuntime.RootHandle?[] _roots = new NativeRuntime.RootHandle?[Capacity * 2];
    private readonly bool[] _freeAttempted = new bool[Capacity * 2];
    private readonly long[] _enumerators = new long[Capacity];
    private readonly CutoutCoroutineRole[] _roles = new CutoutCoroutineRole[Capacity];
    private readonly IntPtr[] _enumClasses = new IntPtr[2];
    private readonly ExactHookBroker.Binding?[] _bindings = new ExactHookBroker.Binding?[3];
    private readonly VerifiedHookTarget?[] _moves = new VerifiedHookTarget?[2];
    private VerifiedHookTarget? _resetAction;
    private NativeCutoutEventObservation? _captured;
    private NativeLocalCutoutCoroutineTracker? _owner;
    private IntPtr _eventClass, _triggerClass, _unityEvent, _actionClass, _providerClass, _coroutineClass;
    private bool _attempted, _ready;
    public bool CleanupUnknown { get; private set; }
    public int RootRecords { get { _main.VerifyAccess(); return _roots.Count(root => root is not null); } }

    internal NativeLocalCutoutCoroutineBackend(NativeRuntime api, IMainThreadDispatcher main, AdapterBuildProfile profile,
        IntPtr module, ExactHookBroker broker, NativeLocalCutoutEventSource source)
    {
        ArgumentNullException.ThrowIfNull(api); ArgumentNullException.ThrowIfNull(main); ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(broker); ArgumentNullException.ThrowIfNull(source);
        (_api, _main, _profile, _module, _broker, _source) = (api, main, profile, module, broker, source);
    }

    public void Initialize(NativeLocalCutoutCoroutineTracker owner)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(owner);
        if (_attempted) throw new InvalidOperationException("Cutout coroutine backend initializes once.");
        _attempted = true; _owner = owner;
        try
        {
            RequireBuild();
            var source = _source.Read();
            if (source.Unknown || source.Source is not { Alive: true } actual || actual.EventTrigger != owner.HostIdentity)
                throw new InvalidOperationException("Cutout coroutine transport requires the actual captured live EventTrigger source.");
            _captured = actual with { Triggers = Array.AsReadOnly(actual.Triggers.ToArray()) };
            var simple = _api.Class("Assembly-CSharp.dll", "", "SimpleCoroutine");
            _eventClass = _api.Class("Assembly-CSharp.dll", "", "EventTrigger");
            _triggerClass = _api.NestedClass(_eventClass, "Trigger");
            _unityEvent = _api.Class("UnityEngine.CoreModule.dll", "UnityEngine.Events", "UnityEvent");
            _enumClasses[0] = _api.NestedClass(simple, "<Delay>d__4");
            _enumClasses[1] = _api.NestedClass(_eventClass, "<StartEventsEnumerator>d__16");
            _actionClass = _api.Class("mscorlib.dll", "System", "Action");
            for (var index = 0; index < _enumClasses.Length; ++index)
            {
                _api.RequireInstanceFieldLayout(_enumClasses[index], "<>1__state", "System.Int32", 0x10);
                _api.RequireInstanceFieldLayout(_enumClasses[index], "<>2__current", "System.Object", 0x18);
                _moves[index] = _api.ResolveExactHook("local.cutout-coroutine.move." + index, _module, _enumClasses[index],
                    "MoveNext", Array.Empty<string>(), index == 0 ? 0x3AF0A0 : 0x39B4D0, ExactHookAbi.InstanceBooleanReturn0);
                // The fixed explicit Reset bodies construct NotSupportedException
                // and call the common raise helper; neither writes iterator state.
                _api.ResolveExactHook("local.cutout-coroutine.reset." + index, _module, _enumClasses[index],
                    "System.Collections.IEnumerator.Reset", Array.Empty<string>(), index == 0 ? 0x3AF250 : 0x39B5A0, ExactHookAbi.InstanceVoid);
            }
            VerifyCode(0x3AF250, 0x40, "5fedf6fbaf76fa2fda445b22667bd7e53381344359dda551f968ba31cd6ecc7b");
            VerifyCode(0x39B5A0, 0x40, "9464093dd95239ed94645e8c6890529d87a3759f879cc9ba49a76bf5bab03926");
            _api.RequireInstanceFieldLayout(_enumClasses[0], "seconds", "System.Single", 0x20);
            _api.RequireInstanceFieldLayout(_enumClasses[0], "todo", "System.Action", 0x28);
            _api.RequireInstanceFieldLayout(_enumClasses[0], "<deltaTimeProvider>5__2", "System.Func<System.Single>", 0x30);
            _providerClass = _api.InspectMetadataFieldClass(_enumClasses[0], "<deltaTimeProvider>5__2", "System.Func<System.Single>").FieldClass.Class;
            _api.RequireInstanceFieldLayout(_enumClasses[1], "trigger", _api.InspectMetadataClass(_triggerClass).TypeName, 0x20);
            _api.RequireInstanceFieldLayout(_triggerClass, "events", "UnityEngine.Events.UnityEvent", 0x10);
            _api.RequireInstanceFieldLayout(_triggerClass, "offset", "System.Single", 0x18);
            var delegateClass = _api.Class("mscorlib.dll", "System", "Delegate");
            var multicast = _api.Class("mscorlib.dll", "System", "MulticastDelegate");
            foreach (var field in new[] { ("method_ptr", "System.IntPtr", 0x10), ("invoke_impl", "System.IntPtr", 0x18),
                ("m_target", "System.Object", 0x20), ("method", "System.IntPtr", 0x28),
                ("method_code", "System.IntPtr", 0x40), ("method_is_virtual", "System.Boolean", 0x70) })
                _api.RequireInstanceFieldLayout(delegateClass, field.Item1, field.Item2, field.Item3);
            _api.RequireInstanceFieldLayout(multicast, "delegates", "System.Delegate[]", 0x78);
            _api.RequireAssignable(multicast, _actionClass); _api.RequireAssignable(multicast, _providerClass);
            _resetAction = _api.ResolveExactHook("local.cutout-coroutine.reset-action", _module, _eventClass,
                "<StartEvents>b__14_1", Array.Empty<string>(), 0x38A700, ExactHookAbi.InstanceVoid);
            var layout = Marshal.PtrToStructure<HookTargetValidation.MethodInfo2022>(_resetAction.MethodInfo);
            if (layout.VirtualMethodPointer != _resetAction.Address || (layout.Flags & 0x40) != 0)
                throw new UnsupportedSignatureException("The exact EventTrigger reset Action is not its declared nonvirtual entry.");
            VerifyCode(0x38A700, 0x10, "b904053c62926849e6cffc4eb7246680105082ac087f21564c410c77257f2ca0");
            var start = _api.ResolveCoroutineStartHook(_module);
            var alias = _api.ResolveCoroutineStartHook(_module, automaticAlias: true); _coroutineClass = start.ReturnClass;
            // Existing physical patches are shared by exact target and native kind.
            _bindings[0] = _broker.BindNativeCoroutineObservation(start, NativeCoroutineKind.Start,
                owner.CaptureStart, owner.CompleteStart, owner.ObserveFault, alias);
            for (var index = 0; index < _moves.Length; ++index)
            {
                var role = (CutoutCoroutineRole)index;
                _bindings[index + 1] = _broker.BindNativeCoroutineObservation(_moves[index]!, NativeCoroutineKind.MoveNext,
                    frame => owner.CaptureMove(role, frame), owner.CompleteMove, owner.ObserveFault);
            }
            _ready = true;
        }
        catch { CleanupUnknown = true; throw; }
    }

    public void RetainEnumerator(int slot, CutoutCoroutineRole role, long enumerator)
    {
        Check(); ValidateSlot(slot, role);
        if (enumerator == 0 || _roots[slot] is not null || _roots[slot + Capacity] is not null || _enumerators[slot] != 0)
            throw new InvalidOperationException("Cutout IEnumerator slot is absent or still occupied.");
        try
        {
            _enumerators[slot] = enumerator; _roles[slot] = role;
            _freeAttempted[slot] = _freeAttempted[slot + Capacity] = false;
            _roots[slot] = _api.RetainExactObject(new(enumerator), _enumClasses[(int)role]);
        }
        catch { CleanupUnknown = true; throw; }
    }
    public CutoutCoroutineObservation ReadEnumerator(int slot, CutoutCoroutineRole role)
    {
        Check(); ValidateSlot(slot, role);
        try
        {
            var value = _roots[slot]?.Target ?? throw new InvalidOperationException("No independently retained Cutout IEnumerator.");
            if (value.ToInt64() != _enumerators[slot] || _roles[slot] != role || _api.ObjectClass(value) != _enumClasses[(int)role])
                throw new UnsupportedSignatureException("The rooted Cutout IEnumerator identity/class/role changed.");
            var state = _api.ReadWithPublication<int>(value, "<>1__state");
            if (role == CutoutCoroutineRole.DelayedEvent)
            {
                var trigger = _api.ReadWithPublication<IntPtr>(value, "trigger");
                if (trigger == IntPtr.Zero || _api.ObjectClass(trigger) != _triggerClass)
                    throw new UnsupportedSignatureException("The delayed event has no exact captured Trigger.");
                var events = _api.ReadWithPublication<IntPtr>(trigger, "events");
                var offset = BitConverter.SingleToInt32Bits(_api.ReadWithPublication<float>(trigger, "offset"));
                if (events == IntPtr.Zero || _api.ObjectClass(events) != _unityEvent
                    || !_captured!.Triggers.Any(row => row.Trigger == trigger.ToInt64() && row.Events == events.ToInt64() && row.OffsetBits == offset))
                    throw new InvalidOperationException("The actual delayed event differs from the preactivation Trigger/events/offset closure.");
                return new(value.ToInt64(), state, trigger.ToInt64(), events.ToInt64(), offset, 0, 0, 0);
            }
            var todo = _api.ReadWithPublication<IntPtr>(value, "todo");
            RequireResetAction(todo);
            var seconds = _api.ReadWithPublication<float>(value, "seconds");
            if (!float.IsFinite(seconds)) throw new UnsupportedSignatureException("Actual reset Delay remaining seconds is not finite.");
            var provider = _api.ReadWithPublication<IntPtr>(value, "<deltaTimeProvider>5__2");
            if (provider != IntPtr.Zero && _api.ObjectClass(provider) != _providerClass)
                throw new UnsupportedSignatureException("Actual Delay provider differs from its closed System.Func<Single> field class.");
            return new(value.ToInt64(), state, 0, 0, 0, todo.ToInt64(), BitConverter.SingleToInt32Bits(seconds), provider.ToInt64());
        }
        catch { CleanupUnknown = true; throw; }
    }
    private void RequireResetAction(IntPtr action)
    {
        if (action == IntPtr.Zero || _api.ObjectClass(action) != _actionClass
            || _api.ReadWithPublication<IntPtr>(action, "m_target").ToInt64() != _captured!.EventTrigger
            || _api.ReadWithPublication<IntPtr>(action, "method") != _resetAction!.MethodInfo
            || _api.ReadWithPublication<IntPtr>(action, "method_ptr") != _resetAction.Address
            || _api.ReadWithPublication<IntPtr>(action, "invoke_impl") != _resetAction.Address
            || _api.ReadWithPublication<IntPtr>(action, "method_code").ToInt64() != _captured.EventTrigger
            || _api.ReadWithPublication<IntPtr>(action, "delegates") != IntPtr.Zero
            || _api.ReadWithPublication<bool>(action, "method_is_virtual"))
            throw new UnsupportedSignatureException("Reset Delay Action is not the exact closed singlecast EventTrigger method/Target.");
    }
    public void RetainCoroutine(int slot, long coroutine)
    {
        Check();
        if (slot < 0 || slot >= Capacity || coroutine == 0 || _enumerators[slot] == 0 || _roots[slot + Capacity] is not null)
            throw new InvalidOperationException("Cutout Coroutine handle slot is absent or already occupied.");
        try { _roots[slot + Capacity] = _api.RetainExactObject(new(coroutine), _coroutineClass); }
        catch { CleanupUnknown = true; throw; }
    }
    public ExactHookBroker.ManagedBoundaryActivity ReadActivity() => ReadActivityCore(true);
    private ExactHookBroker.ManagedBoundaryActivity ReadActivityCore(bool includeCleanupFailure)
    {
        _main.VerifyAccess(); long total = 0;
        foreach (var binding in _bindings)
        {
            if (!includeCleanupFailure) _owner!.RequireCancellationRetirement();
            var value = binding?.ReadActivity();
            if (!includeCleanupFailure) _owner!.RequireCancellationRetirement();
            if (value is not { Known: true, InFlight: >= 0 } || total > long.MaxValue - value.InFlight)
            { CleanupUnknown = true; return new(0, false); }
            total += value.InFlight;
        }
        if (!includeCleanupFailure) _owner!.RequireCancellationRetirement();
        return new(total, _owner is not null && !_owner.ObservationAuthorityUnknown && (!includeCleanupFailure || !CleanupUnknown));
    }
    public void ReleaseEndedRoots(int slot, long enumerator, long generation)
    {
        Check();
        _owner!.RequireCancellationRetirement();
        if (slot < 0 || slot >= Capacity || _enumerators[slot] != enumerator || _roots[slot] is null
            || !_owner!.IsRetiringForCleanup(slot, enumerator, generation))
            throw new InvalidOperationException("Only this exact withdrawn Cutout iterator generation may release its roots.");
        var failures = new List<Exception>(2);
        foreach (var index in new[] { slot + Capacity, slot })
        {
            if (_roots[index] is not { } root) continue;
            try
            {
                _owner.RequireEngineRetirementFree(slot, enumerator, generation);
                _owner.RequireCancellationRetirement();
                var sourceIdle = _source.TryConfirmNativeIdle();
                _owner.RequireCancellationRetirement();
                var activity = ReadActivityCore(false);
                _owner.RequireCancellationRetirement();
                if (_owner.ObservationAuthorityUnknown || _source.Read().Unknown || !sourceIdle || activity is not { Known: true, InFlight: 0 })
                    throw new InvalidOperationException("Cutout native/source activity changed during completed-root cleanup.");
            }
            catch (Exception error) { CleanupUnknown = true; failures.Add(error); break; }
            if (_freeAttempted[index]) { failures.Add(new InvalidOperationException("Unknown Cutout root free cannot retry.")); continue; }
            try { _owner.RequireEngineRetirementFree(slot, enumerator, generation); }
            catch (Exception error) { CleanupUnknown = true; failures.Add(error); break; }
            _owner.RequireCancellationRetirement(); _freeAttempted[index] = true;
            try { root.Dispose(); _roots[index] = null; }
            catch (Exception error) { CleanupUnknown = true; failures.Add(error); }
            try { _owner.RequireEngineRetirementFree(slot, enumerator, generation); _owner.RequireCancellationRetirement(); }
            catch (Exception error) { CleanupUnknown = true; failures.Add(error); break; }
        }
        if (failures.Count != 0)
        { CleanupUnknown = true; throw new AggregateException("Cutout completed roots retain unknown responsibility.", failures); }
        _enumerators[slot] = 0;
    }
    private void ValidateSlot(int slot, CutoutCoroutineRole role)
    {
        if (slot < 0 || slot >= Capacity || role is not (CutoutCoroutineRole.ResetDelay or CutoutCoroutineRole.DelayedEvent))
            throw new ArgumentOutOfRangeException(nameof(slot));
    }
    private void Check()
    { _main.VerifyAccess(); if (!_ready || CleanupUnknown) throw new InvalidOperationException("Cutout coroutine backend is unavailable or unknown."); }
    private void RequireBuild()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || _module == IntPtr.Zero
            || _profile.GameAssemblySha256 != WorkshopChartModule.Fingerprint || _profile.UnityVersion != "2022.3.62f2")
            throw new UnsupportedSignatureException("Cutout coroutines require the pinned Windows x64 Unity build.");
        using var stream = File.OpenRead(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "GameAssembly.dll"));
        using var sha = SHA256.Create();
        if (Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant() != WorkshopChartModule.Fingerprint)
            throw new UnsupportedSignatureException("Cutout coroutine image differs from the pinned profile.");
    }
    private void VerifyCode(int rva, int size, string expected)
    {
        var address = IntPtr.Add(_module, rva);
        HookTargetValidation.VerifyExecutable(address, _module); HookTargetValidation.VerifyReadable(address, (nuint)size);
        var bytes = new byte[size]; Marshal.Copy(address, bytes, 0, size);
        using var sha = SHA256.Create();
        if (!string.Equals(Convert.ToHexString(sha.ComputeHash(bytes)), expected, StringComparison.OrdinalIgnoreCase))
            throw new UnsupportedSignatureException("Pinned Cutout callback/throwing Reset code range changed.");
    }
}
