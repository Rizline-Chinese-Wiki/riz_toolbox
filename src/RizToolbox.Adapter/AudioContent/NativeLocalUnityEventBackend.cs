using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;
using System.Security.Cryptography;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.AudioContent
{
/// <summary>Read-only rooted UnityEvent graph and real Invoke/Prepare observations.
/// No Prepare, listener, mutation, cancellation or completion is synthesized.</summary>
internal sealed partial class NativeLocalUnityEventBackend : ICutoutListenerBackend
{
    private const int MaximumRoots = 2048, MaximumRows = 128, MaximumLeaves = 128, MaximumEvents = 32;
    private const string BaseListName = "System.Collections.Generic.List<UnityEngine.Events.BaseInvokableCall>";
    private const string PersistentListName = "System.Collections.Generic.List<UnityEngine.Events.PersistentCall>";
    private readonly NativeRuntime _api;
    private readonly IMainThreadDispatcher _main;
    private readonly AdapterBuildProfile _profile;
    private readonly IntPtr _module;
    private readonly ExactHookBroker _broker;
    private readonly LocalRunLaunchCommitState _launch;
    private readonly NativeLocalCutoutEventSource _source;
    private readonly NativeRuntime.RootHandle?[] _roots = new NativeRuntime.RootHandle?[MaximumRoots];
    private readonly Dictionary<long, int> _rootSlots = new(MaximumRoots);
    private readonly long[] _events = new long[MaximumEvents];
    private readonly NativePublicationArgumentRecords _arguments;
    private NativeLocalUnityEventLedger? _owner;
    private ExactHookBroker.Binding? _invokeBinding, _prepareBinding;
    private ExactHookBroker.BoundaryProbeBinding? _invokeProbe;
    private IntPtr _eventClass, _eventBase, _callsClass, _groupClass, _persistentClass, _argumentClass,
        _baseCallClass, _invokableClass, _actionClass, _delegateClass, _baseListClass, _persistentListClass,
        _stringClass, _objectClass, _delegateArrayClass, _objectArrayClass, _baseGetItem, _persistentGetItem;
    private VerifiedHookTarget? _invoke, _prepare;
    private int _rootAttempts, _eventCount;
    private bool _attempted, _ready;
    public bool CleanupUnknown { get; private set; }
    public int RootRecords { get { _main.VerifyAccess(); return _roots.Count(root => root is not null); } }
    internal int RetainedCallArgumentRecords => _arguments.Count;
    internal NativeLocalUnityEventBackend(NativeRuntime api, IMainThreadDispatcher main, AdapterBuildProfile profile,
        IntPtr module, ExactHookBroker broker, LocalRunLaunchCommitState launch, NativeLocalCutoutEventSource source)
    {
        ArgumentNullException.ThrowIfNull(api); ArgumentNullException.ThrowIfNull(main); ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(broker); ArgumentNullException.ThrowIfNull(launch); ArgumentNullException.ThrowIfNull(source);
        (_api, _main, _profile, _module, _broker, _launch, _source) = (api, main, profile, module, broker, launch, source);
        _arguments = new(main);
    }
    public void Initialize(NativeLocalUnityEventLedger owner)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(owner);
        if (_attempted) throw new InvalidOperationException("UnityEvent source capture and observation install once.");
        _attempted = true; _owner = owner;
        try
        {
            RequireBuild(); ResolveMetadata();
            RequireObservationKnown();
            _source.BorrowUnityEvents(_launch, (_, borrowed) => {
                // Adopt before ObjectClass/graph work. Source retains its own root.
                var actual = Hold(Native(() => borrowed.Target), _eventClass);
                var slot = owner.RegisterEvent(actual);
                if (slot < 0 || slot >= MaximumEvents || _events[slot] != 0 && _events[slot] != actual)
                    throw new InvalidOperationException("UnityEvent registration returned an invalid or reused slot.");
                _events[slot] = actual; _eventCount = Math.Max(_eventCount, slot + 1);
            });
            RequireObservationKnown();
            var invoke = _broker.BindNativeVoidObservationWithListeners(_invoke!,
                frame => owner.ShouldObserveInvoke(frame.Self.ToInt64()) ? owner.CaptureNativeInvoke(frame) : null,
                owner.CompleteNativeInvoke, owner.ObserveInvokeFault,
                (context, frame, identity) => { if (context._record is not null) owner.ObserveInlineList(context, frame, identity); },
                owner.ObserveListenerBefore, owner.ObserveListenerAfter,
                (pointer, _, method) => owner.ProbeEnter(pointer.ToInt64(), method == IntPtr.Zero || method == _invoke!.MethodInfo),
                (pointer, _, _, returned) => owner.ProbeExit(pointer.ToInt64(), returned));
            (_invokeBinding, _invokeProbe) = invoke;
            RequireObservationKnown();
            _prepareBinding = _broker.BindNativeCoroutineObservation(_prepare!, NativeCoroutineKind.Factory,
                owner.CapturePrepare, owner.CompletePrepare, owner.ObservePrepareFault);
            RequireObservationKnown();
            InitializeMutationObservations(owner);
            _ready = true;
        }
        catch { CleanupUnknown = true; throw; }
    }
    private void ResolveMetadata()
    {
        IntPtr EventType(string name) => Native(() => _api.Class("UnityEngine.CoreModule.dll", "UnityEngine.Events", name));
        _eventClass = EventType("UnityEvent"); _eventBase = EventType("UnityEventBase");
        _callsClass = EventType("InvokableCallList"); _groupClass = EventType("PersistentCallGroup");
        _persistentClass = EventType("PersistentCall"); _argumentClass = EventType("ArgumentCache");
        _baseCallClass = EventType("BaseInvokableCall"); _invokableClass = EventType("InvokableCall");
        _actionClass = EventType("UnityAction"); _delegateClass = Native(() => _api.Class("mscorlib.dll", "System", "Delegate"));
        _stringClass = Native(() => _api.Class("mscorlib.dll", "System", "String"));
        _objectClass = Native(() => _api.Class("mscorlib.dll", "System", "Object"));
        Field(_eventBase, "m_Calls", "UnityEngine.Events.InvokableCallList", 0x10);
        Field(_eventBase, "m_PersistentCalls", "UnityEngine.Events.PersistentCallGroup", 0x18);
        Field(_eventBase, "m_CallsDirty", "System.Boolean", 0x20);
        Field(_eventClass, "m_InvokeArray", "System.Object[]", 0x28);
        _objectArrayClass = Native(() => _api.InspectMetadataFieldClass(_eventClass, "m_InvokeArray", "System.Object[]")).FieldClass.Class;
        foreach (var (name, offset) in new[] { ("m_PersistentCalls", 0x10), ("m_RuntimeCalls", 0x18), ("m_ExecutingCalls", 0x20) })
            Field(_callsClass, name, BaseListName, offset);
        Field(_callsClass, "m_NeedsUpdate", "System.Boolean", 0x28);
        _baseListClass = Native(() => _api.InspectMetadataFieldClass(_callsClass, "m_ExecutingCalls", BaseListName)).FieldClass.Class;
        Field(_groupClass, "m_Calls", PersistentListName, 0x10);
        _persistentListClass = Native(() => _api.InspectMetadataFieldClass(_groupClass, "m_Calls", PersistentListName)).FieldClass.Class;
        foreach (var list in new[] { _baseListClass, _persistentListClass })
        { Field(list, "_size", "System.Int32", 0x18); Field(list, "_version", "System.Int32", 0x1C); }
        _baseGetItem = Method(_baseListClass, "get_Item", new[] { "System.Int32" }, "UnityEngine.Events.BaseInvokableCall");
        _persistentGetItem = Method(_persistentListClass, "get_Item", new[] { "System.Int32" }, "UnityEngine.Events.PersistentCall");
        foreach (var (name, type, offset) in new[] {
            ("m_Target", "UnityEngine.Object", 0x10), ("m_TargetAssemblyTypeName", "System.String", 0x18),
            ("m_MethodName", "System.String", 0x20), ("m_Mode", "UnityEngine.Events.PersistentListenerMode", 0x28),
            ("m_Arguments", "UnityEngine.Events.ArgumentCache", 0x30), ("m_CallState", "UnityEngine.Events.UnityEventCallState", 0x38) })
            Field(_persistentClass, name, type, offset);
        foreach (var (name, type, offset) in new[] {
            ("m_ObjectArgument", "UnityEngine.Object", 0x10), ("m_ObjectArgumentAssemblyTypeName", "System.String", 0x18),
            ("m_IntArgument", "System.Int32", 0x20), ("m_FloatArgument", "System.Single", 0x24),
            ("m_StringArgument", "System.String", 0x28), ("m_BoolArgument", "System.Boolean", 0x30) })
            Field(_argumentClass, name, type, offset);
        Field(_invokableClass, "Delegate", "UnityEngine.Events.UnityAction", 0x10);
        foreach (var (name, type, offset) in new[] {
            ("method_ptr", "System.IntPtr", 0x10), ("invoke_impl", "System.IntPtr", 0x18),
            ("m_target", "System.Object", 0x20), ("method", "System.IntPtr", 0x28),
            ("method_code", "System.IntPtr", 0x40), ("method_info", "System.Reflection.MethodInfo", 0x58),
            ("original_method_info", "System.Reflection.MethodInfo", 0x60), ("method_is_virtual", "System.Boolean", 0x70) })
            Field(_delegateClass, name, type, offset);
        var multicast = Native(() => _api.Class("mscorlib.dll", "System", "MulticastDelegate"));
        Field(multicast, "delegates", "System.Delegate[]", 0x78);
        _delegateArrayClass = Native(() => _api.InspectMetadataFieldClass(multicast, "delegates", "System.Delegate[]")).FieldClass.Class;
        Native(() => _api.RequireAssignable(_eventBase, _eventClass)); Native(() => _api.RequireAssignable(multicast, _actionClass));
        _invoke = Native(() => _api.ResolveObservedInstanceVoid0(_module, _eventClass, "Invoke", 0x15642E0));
        _prepare = Native(() => _api.ResolveObservedReferenceReturn0(_module, _callsClass, "PrepareInvoke", _baseListClass, 0x154E1D0));
    }
    public NativeUnityEventGraph ReadGraph(int eventSlot)
    {
        Check();
        try
        {
            if (eventSlot < 0 || eventSlot >= _eventCount || _events[eventSlot] == 0)
                throw new InvalidOperationException("No actual UnityEvent is registered in this slot.");
            var identity = _events[eventSlot]; var value = Pointer(identity);
            var calls = Reference(value, "m_Calls", _callsClass, false);
            var group = Reference(value, "m_PersistentCalls", _groupClass, false);
            var dirty = Read<bool>(value, "m_CallsDirty");
            var update = Read<bool>(Pointer(calls), "m_NeedsUpdate");
            var rowBudget = 0; var leafBudget = 0;
            var persistent = ReadInvokableList(Reference(Pointer(calls), "m_PersistentCalls", _baseListClass, false), ref rowBudget, ref leafBudget);
            var runtime = ReadInvokableList(Reference(Pointer(calls), "m_RuntimeCalls", _baseListClass, false), ref rowBudget, ref leafBudget);
            var executing = ReadInvokableList(Reference(Pointer(calls), "m_ExecutingCalls", _baseListClass, false), ref rowBudget, ref leafBudget);
            var candidatesList = Reference(Pointer(group), "m_Calls", _persistentListClass, false);
            var candidates = ReadCandidates(candidatesList, ref rowBudget, out var candidatesVersion);
            var invokeArray = Reference(value, "m_InvokeArray", _objectArrayClass, true);
            if (invokeArray != 0) RootArray(invokeArray, _objectArrayClass, 128);
            if (Read<IntPtr>(value, "m_Calls").ToInt64() != calls || Read<IntPtr>(value, "m_PersistentCalls").ToInt64() != group
                || Read<bool>(value, "m_CallsDirty") != dirty || Read<bool>(Pointer(calls), "m_NeedsUpdate") != update)
                throw new InvalidOperationException("UnityEvent graph changed during read-only snapshot.");
            RequireObservationKnown();
            return new(identity, calls, group, dirty, update, persistent, runtime, executing,
                candidatesList, candidatesVersion, candidates, invokeArray);
        }
        catch { CleanupUnknown = true; throw; }
    }
    public NativeUnityEventPrepared CapturePrepared(int eventSlot, long returnedList)
    {
        Check();
        try
        {
            // Typed native original result is retained before any graph operation.
            var identity = Hold(new(returnedList), _baseListClass, published: true);
            var rowBudget = 0; var leafBudget = 0;
            var returned = ReadInvokableList(identity, ref rowBudget, ref leafBudget);
            var graph = ReadGraph(eventSlot); RequireObservationKnown();
            return new(graph, returned);
        }
        catch { CleanupUnknown = true; throw; }
    }
    public NativeUnityEventPrepared CaptureInline(int eventSlot, long actualList)
    {
        // The same publication discipline is required for a mid-frame RBP and a
        // native reference return: retain first, inspect only after adoption.
        // The fixed native pre-init producer owns instruction publication;
        // this observation path grants no patching authority.
        return CapturePrepared(eventSlot, actualList);
    }
    public NativeUnityEventDispatchCapture CaptureDispatch(int eventSlot, NativeListenerFrame frame)
    {
        Check();
        try
        {
            if (eventSlot < 0 || eventSlot >= _eventCount || _events[eventSlot] != frame.Event.ToInt64())
                throw new InvalidOperationException("A listener dispatch belongs to a different registered event.");
            // Retain the actual source registers before any list/metadata reads.
            // Their objects need not be the event's latest replacement list.
            var invokable = AdoptDispatchReference(frame.Invokable);
            var list = AdoptDispatchReference(frame.List);
            long arguments = 0;
            if (frame.Site == 2 && frame.ArgRdx != IntPtr.Zero)
                arguments = AdoptDispatchReference(frame.ArgRdx);
            RequireObservationKnown();
            var retainedInvokable = Pointer(invokable); var retainedList = Pointer(list);
            if (ObjectClass(retainedList) != _baseListClass) throw new UnsupportedSignatureException("Actual dispatch list has a different exact class.");
            Native(() => _api.RequireAssignable(_baseCallClass, ObjectClass(retainedInvokable)));
            if (arguments != 0) RootArray(arguments, _objectArrayClass, MaximumLeaves);
            var rows = 0; var leaves = 0;
            var executing = ReadInvokableList(list, ref rows, ref leaves);
            if (frame.Index >= (ulong)executing.Rows.Count || executing.Rows[checked((int)frame.Index)].Object != invokable)
                throw new InvalidOperationException("The actual source invokable differs from its rooted list/index snapshot.");
            RequireObservationKnown();
            return new(executing, executing.Rows[checked((int)frame.Index)], arguments);
        }
        catch { CleanupUnknown = true; throw; }
    }
    private long AdoptDispatchReference(IntPtr pointer)
    {
        // Only the bounded, already-published register references are adopted
        // here. All their roots precede the first Target/class/list inspection.
        // A failed root allocation stops the batch and retains prior records.
        _main.VerifyAccess();
        if (pointer == IntPtr.Zero) throw new InvalidOperationException("A required published dispatch reference is null.");
        var identity = pointer.ToInt64();
        if (_rootSlots.ContainsKey(identity)) return identity;
        if (_rootAttempts >= MaximumRoots) throw new InvalidOperationException("Listener process owner exhausted its root slots.");
        var slot = _rootAttempts++;
        _roots[slot] = _api.Root(pointer);
        _rootSlots.Add(identity, slot);
        return identity;
    }
    private NativeUnityEventList ReadInvokableList(long list, ref int rowBudget, ref int leafBudget)
    {
        var pointer = Pointer(list); var count = Count(pointer); var version = Read<int>(pointer, "_version");
        Spend(ref rowBudget, count, MaximumRows, "UnityEvent rows");
        var rows = new NativeUnityEventInvokable[count]; var identities = new long[count];
        for (var index = 0; index < count; ++index)
        {
            var item = ListItem(pointer, _baseGetItem, index); var actual = ObjectClass(Pointer(item));
            Native(() => _api.RequireAssignable(_baseCallClass, actual)); identities[index] = item;
            rows[index] = ReadInvokable(item, actual, ref leafBudget);
        }
        VerifyList(pointer, _baseGetItem, count, version, identities);
        return new(list, version, Array.AsReadOnly(rows));
    }
    private NativeUnityEventInvokable ReadInvokable(long item, IntPtr actual, ref int leafBudget)
    {
        if (actual != _invokableClass)
            return ReadCachedBooleanInvokable(item, actual, ref leafBudget);
        return ReadDelegateInvokable(item, actual, _actionClass, null, ref leafBudget);
    }
    private NativeUnityEventInvokable ReadDelegateInvokable(long item, IntPtr actual, IntPtr actionClass,
        NativeUnityEventCachedBooleanArgument? argument, ref int leafBudget, long? adoptedAction = null)
    {
        var action = adoptedAction ?? Reference(Pointer(item), "Delegate", actionClass, true);
        if (action == 0)
            return new(item, actual.ToInt64(), true, Array.Empty<NativeUnityEventLeaf>(), CachedBoolean: argument);
        var invocationList = Reference(Pointer(action), "delegates", _delegateArrayClass, true);
        var leaves = new List<NativeUnityEventLeaf>();
        if (invocationList == 0)
        { Spend(ref leafBudget, 1, MaximumLeaves, "UnityEvent delegate leaves"); leaves.Add(ReadLeaf(action, actionClass, argument)); }
        else
        {
            var entries = RootArray(invocationList, _delegateArrayClass, MaximumLeaves);
            Spend(ref leafBudget, entries.Count, MaximumLeaves, "UnityEvent delegate leaves");
            foreach (var entry in entries) leaves.Add(ReadLeaf(entry, actionClass, argument));
        }
        if (argument is null && Read<IntPtr>(Pointer(item), "Delegate").ToInt64() != action
            || Read<IntPtr>(Pointer(action), "delegates").ToInt64() != invocationList)
            throw new InvalidOperationException("The actual invokable delegate chain changed during its rooted snapshot.");
        return new(item, actual.ToInt64(), leaves.Count == 0 || leaves.Any(leaf => leaf.Opaque),
            Array.AsReadOnly(leaves.ToArray()), action, invocationList, argument);
    }
    private IReadOnlyList<NativeUnityEventPersistent> ReadCandidates(long list, ref int budget, out int version)
    {
        var pointer = Pointer(list); var count = Count(pointer); version = Read<int>(pointer, "_version");
        Spend(ref budget, count, MaximumRows, "UnityEvent rows");
        var rows = new NativeUnityEventPersistent[count]; var identities = new long[count];
        for (var index = 0; index < count; ++index)
        {
            var item = ListItem(pointer, _persistentGetItem, index); identities[index] = item;
            var current = Pointer(item);
            if (ObjectClass(current) != _persistentClass) throw new UnsupportedSignatureException("Persistent candidate class changed.");
            var target = Reference(current, "m_Target", IntPtr.Zero, true);
            var targetType = Reference(current, "m_TargetAssemblyTypeName", _stringClass, true);
            var method = Reference(current, "m_MethodName", _stringClass, true);
            var mode = (int)Read<UnityEngine.Events.PersistentListenerMode>(current, "m_Mode");
            var state = (int)Read<UnityEngine.Events.UnityEventCallState>(current, "m_CallState");
            var arguments = Reference(current, "m_Arguments", _argumentClass, true);
            long obj = 0, objType = 0, text = 0; int integer = 0, floatBits = 0; bool boolean = false;
            if (arguments != 0)
            {
                var cache = Pointer(arguments); obj = Reference(cache, "m_ObjectArgument", IntPtr.Zero, true);
                objType = Reference(cache, "m_ObjectArgumentAssemblyTypeName", _stringClass, true);
                text = Reference(cache, "m_StringArgument", _stringClass, true);
                integer = Read<int>(cache, "m_IntArgument"); floatBits = BitConverter.SingleToInt32Bits(Read<float>(cache, "m_FloatArgument"));
                boolean = Read<bool>(cache, "m_BoolArgument");
            }
            rows[index] = new(item, target, target == 0 ? 0 : ObjectClass(Pointer(target)).ToInt64(), targetType, method,
                mode, state, arguments, obj, objType, integer, floatBits, text, boolean);
        }
        VerifyList(pointer, _persistentGetItem, count, version, identities);
        return Array.AsReadOnly(rows);
    }
    private NativeUnityEventLeaf ReadLeaf(long identity, IntPtr expectedActionClass, NativeUnityEventCachedBooleanArgument? argument)
    {
        var pointer = Pointer(identity); var actual = ObjectClass(pointer); Native(() => _api.RequireAssignable(_delegateClass, actual));
        var target = Reference(pointer, "m_target", IntPtr.Zero, true);
        var targetClass = target == 0 ? 0 : ObjectClass(Pointer(target)).ToInt64();
        var method = Read<IntPtr>(pointer, "method").ToInt64(); var methodPtr = Read<IntPtr>(pointer, "method_ptr").ToInt64();
        var invoke = Read<IntPtr>(pointer, "invoke_impl").ToInt64(); var code = Read<IntPtr>(pointer, "method_code").ToInt64();
        var reflected = Reference(pointer, "method_info", IntPtr.Zero, true);
        var original = Reference(pointer, "original_method_info", IntPtr.Zero, true);
        var isVirtual = Read<bool>(pointer, "method_is_virtual");
        var nested = Reference(pointer, "delegates", _delegateArrayClass, true);
        var opaque = true; string? signature = null;
        if (nested != 0)
        {
            // Nested multicast is not reinterpreted as one leaf. Retain its complete
            // bounded array/items, keep it opaque and do not claim resolved calls.
            RootArray(nested, _delegateArrayClass, MaximumLeaves);
        }
        else
        {
            try
            {
                var projected = Native(() => _api.InspectRootedDelegateMethod(_roots[_rootSlots[identity]]!, actual, _module));
                var metadata = projected.Method;
                if (projected.Target.ToInt64() != target || metadata.MethodInfo.ToInt64() != method
                    || projected.MethodPointer.ToInt64() != methodPtr || projected.InvokeImplementation.ToInt64() != invoke
                    || projected.MethodCode.ToInt64() != code || projected.Virtual != isVirtual)
                    throw new InvalidOperationException("Delegate fields changed between its raw snapshot and metadata projection.");
                signature = metadata.DeclaringClass.TypeName + "." + metadata.Name + "(" + string.Join(",", metadata.ParameterTypes)
                    + ")->" + metadata.ReturnType + ";flags=" + metadata.Flags + ";generic=" + metadata.GenericMethod + ";inflated=" + metadata.InflatedMethod;
                // Metadata resolution does not prove receiver adaptation, ABI,
                // virtual dispatch or any listener/producer lifetime ended.
                opaque = !SupportedListenerSignature(metadata, target, isVirtual, actual, expectedActionClass,
                    argument is null ? IntPtr.Zero : new(argument.BooleanClass),
                    argument is null ? null : () => Native(() => _api.InspectMethodParameterClass(metadata.MethodInfo, 0, "System.Boolean")).Class);
            }
            catch (UnsupportedSignatureException) { RequireObservationKnown(); opaque = true; }
        }
        return new(identity, actual.ToInt64(), target, targetClass, method, methodPtr, invoke, code, reflected, original, isVirtual, opaque, signature);
    }
    private IReadOnlyList<long> RootArray(long identity, IntPtr exactArrayClass, int maximum)
    {
        var pointer = Pointer(identity);
        if (ObjectClass(pointer) != exactArrayClass) throw new UnsupportedSignatureException("UnityEvent array class changed.");
        var array = Native(() => _api.Class("mscorlib.dll", "System", "Array"));
        var rank = Method(array, "get_Rank", Array.Empty<string>(), "System.Int32");
        var lengthMethod = Method(array, "get_Length", Array.Empty<string>(), "System.Int32");
        if (Native(() => _api.InvokeValueWithPublication<int>(rank, pointer)) != 1) throw new UnsupportedSignatureException("Only rank1 listener arrays are supported.");
        var count = Native(() => _api.InvokeValueWithPublication<int>(lengthMethod, pointer));
        if (count < 0 || count > maximum) throw new UnsupportedSignatureException("Listener array exceeds its complete bound.");
        var get = Method(array, "GetValue", new[] { "System.Int32" }, "System.Object");
        var rows = new long[count];
        for (var index = 0; index < count; ++index) rows[index] = ListItem(pointer, get, index, allowNull: exactArrayClass == _objectArrayClass);
        return Array.AsReadOnly(rows);
    }
    private long ListItem(IntPtr list, IntPtr get, int index, bool allowNull = false)
    {
        RequireObservationKnown();
        var arguments = new[] { A.Value(index) }; _arguments.Begin(arguments); long result = 0;
        try
        {
            RequireObservationKnown();
            _api.InvokeReferenceWithPublication(get, list,
                pointer => result = Hold(pointer, IntPtr.Zero, allowNull, published: true), arguments);
        }
        catch { _arguments.Fail(); CleanupUnknown = true; throw; }
        // Successful Runtime return has consumed its inputs, even if another
        // observation faults during its final native cleanup. Clear only those
        // confirmed inputs before rejecting all further graph work.
        _arguments.Complete(); RequireObservationKnown(); return result;
    }
    private void VerifyList(IntPtr list, IntPtr get, int count, int version, IReadOnlyList<long> expected)
    {
        if (Read<int>(list, "_version") != version || Count(list) != count)
            throw new InvalidOperationException("Listener list count/version changed during snapshot.");
        for (var index = 0; index < count; ++index)
            if (ListItem(list, get, index) != expected[index]) throw new InvalidOperationException("Listener list element order changed during snapshot.");
        if (Read<int>(list, "_version") != version || Count(list) != count)
            throw new InvalidOperationException("Listener list changed during final element verification.");
    }
    private int Count(IntPtr list)
    {
        var count = Read<int>(list, "_size");
        if (count < 0 || count > MaximumRows) throw new UnsupportedSignatureException("Listener list exceeds its complete bound.");
        return count;
    }
    private static void Spend(ref int spent, int amount, int maximum, string kind)
    {
        if (amount < 0 || spent > maximum - amount) throw new UnsupportedSignatureException(kind + " exceeds its aggregate bound.");
        spent += amount;
    }
    private long Reference(IntPtr owner, string field, IntPtr expected, bool nullable)
    {
        RequireObservationKnown();
        var pointer = _api.ReadWithPublication<IntPtr>(owner, field);
        // Adopt an already-returned reference before the post-operation fault
        // barrier. No Target/class/field work may follow an authority fault.
        return Hold(pointer, expected, nullable, published: true);
    }
    private long Hold(IntPtr pointer, IntPtr expected, bool nullable = false, bool published = false)
    {
        _main.VerifyAccess();
        if (!published) RequireObservationKnown();
        if (pointer == IntPtr.Zero)
        { RequireObservationKnown(); if (nullable) return 0; throw new InvalidOperationException("A required listener graph object is null."); }
        var identity = pointer.ToInt64();
        if (_rootSlots.TryGetValue(identity, out var existing))
        {
            // An independent root already owns this address; no native operation
            // is needed to preserve it when the just-finished read faulted.
            RequireObservationKnown();
            var value = Native(() => _roots[existing]!.Target);
            if (value != pointer || expected != IntPtr.Zero && ObjectClass(value) != expected)
                throw new UnsupportedSignatureException("A retained listener object changed identity or exact class.");
            return identity;
        }
        if (_rootAttempts >= MaximumRoots) throw new InvalidOperationException("Listener process owner exhausted its 2048 root slots.");
        var slot = _rootAttempts++;
        // Publication adoption is the sole necessary native responsibility
        // operation permitted before the post-result fault barrier. Store the
        // actual root immediately, then stop before inspecting its target.
        _roots[slot] = _api.Root(pointer); _rootSlots.Add(identity, slot);
        RequireObservationKnown();
        var retained = Native(() => _roots[slot]!.Target);
        if (retained != pointer || expected != IntPtr.Zero && ObjectClass(retained) != expected)
            throw new UnsupportedSignatureException("Listener graph object differs from its exact class.");
        return identity;
    }
    private IntPtr Pointer(long identity)
    {
        if (identity == 0 || !_rootSlots.TryGetValue(identity, out var slot)) throw new InvalidOperationException("Listener graph has no retained actual object.");
        var pointer = Native(() => _roots[slot]!.Target);
        if (pointer.ToInt64() != identity) throw new TargetExpiredException("Listener root identity changed.");
        return pointer;
    }
    private T Read<T>(IntPtr owner, string field) where T : struct
        => Native(() => _api.ReadWithPublication<T>(owner, field));
    private IntPtr ObjectClass(IntPtr pointer) => Native(() => _api.ObjectClass(pointer));
    private T Native<T>(Func<T> operation)
    { RequireObservationKnown(); var value = operation(); RequireObservationKnown(); return value; }
    private void Native(Action operation)
    { RequireObservationKnown(); operation(); RequireObservationKnown(); }
    public ExactHookBroker.ManagedBoundaryActivity ReadActivity()
    {
        _main.VerifyAccess(); long total = 0;
        foreach (var binding in new[] { _invokeBinding, _prepareBinding }.Concat(_mutationBindings))
        {
            if (!ObservationKnown) return new(total, false);
            var value = binding?.ReadActivity();
            if (!ObservationKnown) return new(total, false);
            if (value is not { Known: true, InFlight: >= 0 } || total > long.MaxValue - value.InFlight)
            { CleanupUnknown = true; return new(0, false); }
            total += value.InFlight;
        }
        if (!ObservationKnown) return new(total, false);
        var probe = _invokeProbe?.ReadActivity();
        if (!ObservationKnown) return new(total, false);
        if (probe is not { Known: true, InFlight: >= 0 }) { CleanupUnknown = true; return new(total, false); }
        // The probe shares Invoke's physical entry. Do not double-count it, but
        // preserve an entry that began after the earlier binding activity read.
        return new(Math.Max(total, probe.InFlight), ObservationKnown);
    }
    private void Field(IntPtr type, string name, string expected, int offset)
        => Native(() => _api.RequireInstanceFieldLayout(type, name, expected, offset));
    private IntPtr Method(IntPtr type, string name, string[] parameters, string returns)
    {
        var method = Native(() => _api.InspectDeclaredMethod(type, name, parameters, returns, _module));
        if (method.Entry is null || method.GenericMethod || (method.Flags & (0x10 | 0x400)) != 0)
            throw new UnsupportedSignatureException("Listener graph read method has no exact supported metadata: " + name);
        return method.MethodInfo;
    }
    private bool ObservationKnown => !CleanupUnknown && !_arguments.CleanupUnknown && _owner is not null && !_owner.ObservationAuthorityUnknown;
    private void RequireObservationKnown()
    {
        if (!ObservationKnown)
            throw new InvalidOperationException("Listener graph observation authority is unknown.");
        if (_ready)
        {
            // Foreign failures can invalidate a shared native patch without
            // touching this event's scalar owner. No idle requirement here:
            // graph reads are legitimate inside the current Invoke/Prepare.
            var invoke = _invokeBinding?.ReadActivity();
            if (!ObservationKnown || invoke is not { Known: true, InFlight: >= 0 })
                throw new InvalidOperationException("Shared Invoke transport is unknown before a listener graph operation.");
            var prepare = _prepareBinding?.ReadActivity();
            if (!ObservationKnown || prepare is not { Known: true, InFlight: >= 0 })
                throw new InvalidOperationException("Shared Prepare transport is unknown before a listener graph operation.");
            foreach (var binding in _mutationBindings)
            {
                var operation = binding.ReadActivity();
                if (!ObservationKnown || operation is not { Known: true, InFlight: >= 0 })
                    throw new InvalidOperationException("Shared mutation transport is unknown before a listener graph operation.");
            }
        }
    }
    private void Check()
    { _main.VerifyAccess(); if (!_ready) throw new InvalidOperationException("Listener graph backend is not initialized."); RequireObservationKnown(); }
    private void RequireBuild()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || _module == IntPtr.Zero
            || _profile.GameAssemblySha256 != WorkshopChartModule.Fingerprint || _profile.UnityVersion != "2022.3.62f2")
            throw new UnsupportedSignatureException("Listener graph requires the pinned Windows x64 Unity build.");
        using var stream = File.OpenRead(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "GameAssembly.dll"));
        using var sha = SHA256.Create();
        if (Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant() != WorkshopChartModule.Fingerprint)
            throw new UnsupportedSignatureException("Listener graph image differs from its pinned profile.");
    }
}
}

// Exact-name Int32 carriers preserve raw enum storage through NativeAbi's strict
// metadata validation. No guessed enum values or execution semantics are defined.
namespace UnityEngine.Events
{
    internal enum PersistentListenerMode : int { }
    internal enum UnityEventCallState : int { }
}
