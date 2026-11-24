using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal sealed record NativeUnityEventLeaf(long Delegate, long Class, long Target, long TargetClass, long MethodInfo,
    long MethodPointer, long InvokeImplementation, long MethodCode, long ReflectedMethod, long OriginalReflectedMethod,
    bool IsVirtual, bool Opaque, string? Signature);
internal sealed record NativeUnityEventCachedBooleanArgument(long BooleanClass, bool Value, long InvokableParentClass,
    long ActionClass, long DelegateField, long ArgumentField, long ActionInvokeMethod);
internal sealed record NativeUnityEventInvokable(long Object, long Class, bool Opaque, IReadOnlyList<NativeUnityEventLeaf> Leaves,
    long Delegate = 0, long InvocationList = 0, NativeUnityEventCachedBooleanArgument? CachedBoolean = null);
internal sealed record NativeUnityEventList(long Object, int Version, IReadOnlyList<NativeUnityEventInvokable> Rows);
internal sealed record NativeUnityEventPersistent(long Object, long Target, long TargetClass, long TargetTypeName, long MethodName,
    int Mode, int CallState, long Arguments, long ObjectArgument, long ObjectArgumentTypeName, int IntArgument,
    int FloatBits, long StringArgument, bool BoolArgument);
internal sealed record NativeUnityEventGraph(long Event, long Calls, long PersistentGroup, bool CallsDirty, bool NeedsUpdate,
    NativeUnityEventList PersistentCalls, NativeUnityEventList RuntimeCalls, NativeUnityEventList ExecutingCalls,
    long CandidatesList, int CandidatesVersion, IReadOnlyList<NativeUnityEventPersistent> Candidates, long InvokeArray);
internal sealed record NativeUnityEventPrepared(NativeUnityEventGraph Graph, NativeUnityEventList Returned);
internal sealed record NativeUnityEventInline(NativeUnityEventGraph Graph, NativeUnityEventList Executing,
    long NativeToken, long ParentCaller, long MidReturnPc);
internal sealed record NativeUnityEventDispatchCapture(NativeUnityEventList Executing, NativeUnityEventInvokable Invokable, long ArgumentArray);
internal sealed record NativeUnityEventDelegateMutationCapture(NativeUnityEventGraph Graph, NativeUnityEventInvokable Invokable);
internal sealed record NativeUnityEventDispatchSnapshot(ulong ParentSequence, ulong ChildSequence, uint Site, int Index,
    long List, long Invokable, long ActualTarget, long SourceReturnPc, long ParentToken, bool Captured, bool Completed,
    bool NativeReturned, uint Flags, NativeUnityEventDispatchCapture? Observation, bool ConfirmedReturned = false);
internal sealed record NativeUnityEventLedgerEventSnapshot(int Slot, long Event, long Entered, long Exited, long InFlight,
    long Generation, bool Returned, bool ChangedSinceCapture, NativeUnityEventGraph Initial,
    NativeUnityEventGraph? Pre, NativeUnityEventPrepared? IndependentPrepared, NativeUnityEventGraph? Post,
    long PrepareGeneration, long PrepareCaller, long InvokeCaller = 0, long InvokeMethod = 0, bool NativeInvokeObserved = false,
    NativeUnityEventInline? Inline = null, IReadOnlyList<NativeUnityEventDispatchSnapshot>? Dispatches = null,
    long MutationGeneration = 0, IReadOnlyList<NativeUnityEventMutationSnapshot>? Mutations = null);
internal sealed record NativeLocalUnityEventLedgerSnapshot(int Events, long Invocations, long ReturnedInvocations,
    long PreparedInvocations, long NativeInFlight, int Roots, bool Pending, bool MutationClosed, bool Unknown,
    string? Failure, IReadOnlyList<NativeUnityEventLedgerEventSnapshot> EventSnapshots,
    bool InlineExecutionUnobserved = true, bool DispatchPathVerified = false,
    long InvokableDispatches = 0, long ReturnedDispatches = 0, long AbnormalDispatches = 0,
    long ObservedMutations = 0, long ReturnedMutations = 0);
internal interface ICutoutListenerBackend
{
    bool CleanupUnknown { get; }
    int RootRecords { get; }
    void Initialize(NativeLocalUnityEventLedger owner);
    NativeUnityEventGraph ReadGraph(int eventSlot);
    NativeUnityEventPrepared CapturePrepared(int eventSlot, long returnedList);
    NativeUnityEventPrepared CaptureInline(int eventSlot, long actualList)
        => throw new UnsupportedSignatureException("This backend has no actual inline-list adoption path.");
    NativeUnityEventDispatchCapture CaptureDispatch(int eventSlot, NativeListenerFrame frame)
        => throw new UnsupportedSignatureException("This backend has no actual listener dispatch adoption path.");
    NativeUnityEventGraph CaptureMutation(int eventSlot, UnityEventOperation operation, NativeOperationFrame frame)
        => throw new UnsupportedSignatureException("This backend has no actual mutation-argument adoption path.");
    NativeUnityEventDelegateMutationCapture CaptureDelegateMutation(int eventSlot, UnityEventOperation operation, NativeOperationFrame frame)
        => throw new UnsupportedSignatureException("This backend has no exact delegate-writer argument adoption path.");
    NativeUnityEventDelegateMutationCapture ReadDelegateMutation(int eventSlot, UnityEventOperation operation, NativeOperationFrame frame)
        => throw new UnsupportedSignatureException("This backend has no actual final delegate-chain observation path.");
    ExactHookBroker.ManagedBoundaryActivity ReadActivity();
}
internal interface ICutoutListenerAuthority
{
    bool Unknown { get; }
    void VerifyBeforeCapture();
    void VerifyObservation();
    bool TryConfirmSourceIdle();
    void ObserveFaultFromAnyThread();
}
internal sealed class NativeLocalUnityEventLedgerException : InvalidOperationException
{
    internal NativeLocalUnityEventLedger Responsibility { get; }
    internal NativeLocalUnityEventLedgerException(NativeLocalUnityEventLedger owner, Exception error)
        : base("UnityEvent listener observation is unconfirmed; acquired native responsibility remains retained.", error) => Responsibility = owner;
}

/// <summary>Real Invoke and Prepare observations, never an invocation/mutation API.
/// Pre, actual prepared output and post are distinct immutable snapshots. Without
/// mutation and listener-operation owners even equal snapshots remain Pending.</summary>
internal sealed partial class NativeLocalUnityEventLedger
{
    internal const int MaximumEvents = 32, MaximumRows = 128, MaximumLeaves = 128, MaximumRoots = 2048;
    private const int MaximumDepth = 32;
    private static readonly object Gate = new();
    private static readonly HashSet<NativeLocalUnityEventLedger> Owners = new();
    private readonly IMainThreadDispatcher _main;
    private readonly ICutoutListenerBackend _backend;
    private readonly ICutoutListenerAuthority _authority;
    private readonly LocalRunLaunchCommitState? _launch;
    private readonly long _module;
    private readonly EventRecord[] _events;
    private readonly InvokeContext[] _masks;
    private readonly long[] _physical = new long[MaximumDepth];
    private InvokeContext? _current;
    private int _count, _fault, _physicalDepth;
    private bool _initializing, _ready, _busy;
    private long _returned, _prepared;
    private long _dispatches, _dispatchReturned, _dispatchAbnormal;
    private string? _failure;
    internal sealed class EventRecord
    {
        internal readonly int Slot;
        internal readonly InvokeContext Invoke;
        internal readonly PrepareContext Prepare;
        internal long Event, Entered, Exited, InFlight, Generation, PrepareGeneration, PrepareCaller, InvokeCaller, InvokeMethod;
        internal NativeUnityEventGraph? Initial, Pre, Post;
        internal NativeUnityEventPrepared? Prepared;
        internal NativeUnityEventInline? Inline;
        internal readonly List<DispatchRecord> Dispatches = new();
        internal bool Returned, Changed, NativeInvokeObserved;
        internal long MutationGeneration;
        internal readonly List<MutationRecord> Mutations = new();
        internal MutationReceiverIdentity[] MutationReceivers = Array.Empty<MutationReceiverIdentity>();
        internal long[] MutationInvokables = Array.Empty<long>();
        internal readonly Dictionary<long, long> MutationInvokableClasses = new();
        internal EventRecord(NativeLocalUnityEventLedger owner, int slot)
        { Slot = slot; Invoke = new(owner, this); Prepare = new(owner, this); }
    }
    internal sealed class DispatchRecord
    {
        internal readonly NativeListenerFrame Frame;
        internal NativeUnityEventDispatchCapture? Observation;
        internal bool Captured, Completed, Returned, NativeReturned;
        internal uint Flags;
        internal DispatchRecord(NativeListenerFrame frame) => Frame = frame;
        internal NativeUnityEventDispatchSnapshot Snapshot() => new(Frame.ParentSequence, Frame.ChildSequence, Frame.Site, checked((int)Frame.Index),
            Frame.List.ToInt64(), Frame.Invokable.ToInt64(), Frame.ActualTarget.ToInt64(), Frame.SourceReturnPc.ToInt64(),
            unchecked((long)Frame.ParentManagedToken), Captured, Completed, NativeReturned, Flags, Observation, Returned);
    }
    internal sealed class InvokeContext : IDisposable
    {
        private readonly NativeLocalUnityEventLedger _owner;
        internal readonly EventRecord? _record;
        internal InvokeContext? _parent;
        internal long _generation, _receiver, _nativeCaller, _nativeMethod;
        internal bool _active, _after, _native;
        internal InvokeContext(NativeLocalUnityEventLedger owner, EventRecord? record) => (_owner, _record) = (owner, record);
        internal void Begin(long receiver)
        {
            if (_active) throw new InvalidOperationException("The same UnityEvent Invoke context reentered.");
            _receiver = receiver; _generation = _record?.Generation ?? 0; _after = false; _active = true;
            _native = false; _nativeCaller = _nativeMethod = 0;
            _parent = _owner._current; _owner._current = this;
        }
        public void Dispose()
        {
            _owner._main.VerifyAccess();
            if (!_active || !_after || !ReferenceEquals(_owner._current, this)
                || _record is not null && _record.Generation != _generation)
                _owner.Fault("UnityEvent Invoke did not normally exit its exact physical generation.");
            if (ReferenceEquals(_owner._current, this)) _owner._current = _parent;
            _parent = null; _active = false;
            _owner.ObserveListenerEffectsContextDisposed(this);
        }
    }
    internal sealed class PrepareContext : IDisposable
    {
        private readonly NativeLocalUnityEventLedger _owner;
        internal readonly EventRecord _record;
        internal long _generation, _receiver, _caller, _method;
        internal bool _active, _after;
        internal PrepareContext(NativeLocalUnityEventLedger owner, EventRecord record) => (_owner, _record) = (owner, record);
        public void Dispose()
        {
            _owner._main.VerifyAccess();
            if (!_active || !_after || _record.PrepareGeneration != _generation)
                _owner.Fault("UnityEvent Prepare original/after did not pair with its independent generation.");
            _active = false;
        }
    }
    private NativeLocalUnityEventLedger(IMainThreadDispatcher main, ICutoutListenerBackend backend,
        ICutoutListenerAuthority authority, LocalRunLaunchCommitState? launch, long module)
    {
        (_main, _backend, _authority, _launch, _module) = (main, backend, authority, launch, module);
        _events = Enumerable.Range(0, MaximumEvents).Select(index => new EventRecord(this, index)).ToArray();
        _masks = Enumerable.Range(0, MaximumDepth).Select(_ => new InvokeContext(this, null)).ToArray();
    }
    internal static NativeLocalUnityEventLedger Capture(NativeRuntime api, IMainThreadDispatcher main, AdapterBuildProfile profile,
        IntPtr module, ExactHookBroker broker, LocalRunLaunchCommitState launch, NativeLocalCutoutEventSource source)
        => CaptureCore(main, new NativeLocalUnityEventBackend(api, main, profile, module, broker, launch, source),
            new Authority(launch, source), launch, module.ToInt64());
    internal static NativeLocalUnityEventLedger CaptureBackend(IMainThreadDispatcher main, ICutoutListenerBackend backend,
        ICutoutListenerAuthority authority, long module)
    {
        if (backend is NativeLocalUnityEventBackend) throw new InvalidOperationException("Controlled listener capture cannot adopt a production backend.");
        return CaptureCore(main, backend, authority, null, module);
    }
    private static NativeLocalUnityEventLedger CaptureCore(IMainThreadDispatcher main, ICutoutListenerBackend backend,
        ICutoutListenerAuthority authority, LocalRunLaunchCommitState? launch, long module)
    {
        ArgumentNullException.ThrowIfNull(main); main.VerifyAccess(); ArgumentNullException.ThrowIfNull(backend); ArgumentNullException.ThrowIfNull(authority);
        authority.VerifyBeforeCapture(); if (module <= 0) throw new InvalidOperationException("A pinned native listener module is required.");
        var owner = new NativeLocalUnityEventLedger(main, backend, authority, launch, module);
        lock (Gate)
        {
            if (Owners.Count >= 32 || launch is not null && Owners.Any(value => ReferenceEquals(value._launch, launch)))
                throw new InvalidOperationException("Listener process responsibility capacity exhausted or launch already captured.");
            Owners.Add(owner);
        }
        owner._initializing = owner._busy = true;
        try
        {
            backend.Initialize(owner); owner.Known();
            for (var index = 0; index < owner._count; ++index)
            { owner._events[index].Initial = owner.ReadGraph(index); owner.Known(); }
            owner.CheckBudget(); authority.VerifyBeforeCapture();
            var activity = backend.ReadActivity(); owner.Known();
            if (!activity.Known || activity.InFlight != 0) throw new InvalidOperationException("Listener installation has unknown or active physical entries.");
            owner._ready = true; return owner;
        }
        catch (Exception error) { owner.Fault(error.Message); throw new NativeLocalUnityEventLedgerException(owner, error); }
        finally { owner._initializing = owner._busy = false; }
    }
    internal int RegisterEvent(long identity)
    {
        _main.VerifyAccess(); Known();
        if (!_initializing || identity == 0) throw new InvalidOperationException("Only preactivation independent event adoption may register an identity.");
        for (var index = 0; index < _count; ++index) if (_events[index].Event == identity) return index;
        if (_count == MaximumEvents) throw new InvalidOperationException("Distinct UnityEvent capacity exhausted.");
        var slot = _count++; Interlocked.Exchange(ref _events[slot].Event, identity); return slot;
    }
    internal bool ObservationAuthorityUnknown => Volatile.Read(ref _fault) != 0 || _authority.Unknown;
    internal bool Unknown => ObservationAuthorityUnknown || _backend.CleanupUnknown;
    internal bool IsFor(LocalRunLaunchCommitState launch) { _main.VerifyAccess(); return _launch is not null && ReferenceEquals(_launch, launch) && !Unknown; }
    private EventRecord? Find(long receiver) => receiver == 0 ? null : _events.FirstOrDefault(row => Interlocked.Read(ref row.Event) == receiver);
    private long PhysicalTop => _physicalDepth > 0 && _physicalDepth <= MaximumDepth ? _physical[_physicalDepth - 1] : 0;
    // Every-thread probes touch only fixed managed slots and atomic scalars. The
    // main-thread physical stack also masks foreign calls suppressed by the broker.
    internal void ProbeEnter(long receiver, bool exactMethod)
    {
        var row = Find(receiver);
        if (row is not null)
        {
            var entered = Interlocked.Increment(ref row.Entered); var active = Interlocked.Increment(ref row.InFlight);
            if (entered <= 0 || active != 1 || !_main.IsMainThread || !exactMethod || _busy) Fault("Owned UnityEvent worker, metadata mismatch or physical reentry.");
        }
        if (!_main.IsMainThread) return;
        if (_physicalDepth >= MaximumDepth) { ++_physicalDepth; Fault("UnityEvent physical child mask depth exhausted."); return; }
        _physical[_physicalDepth++] = receiver;
    }
    internal void ProbeExit(long receiver, bool returned)
    {
        var row = Find(receiver);
        if (row is not null)
        {
            var exited = Interlocked.Increment(ref row.Exited); var active = Interlocked.Decrement(ref row.InFlight);
            if (exited <= 0 || active < 0 || !returned) Fault("Owned UnityEvent original did not return through its paired physical exit.");
        }
        if (!_main.IsMainThread) return;
        if (_physicalDepth <= 0) { Fault("UnityEvent probe exited without a physical entry."); return; }
        if (_physicalDepth <= MaximumDepth)
        { if (_physical[_physicalDepth - 1] != receiver) Fault("UnityEvent physical entries exited out of order."); _physical[_physicalDepth - 1] = 0; }
        --_physicalDepth;
        ObserveListenerEffectsPhysicalExit(receiver);
    }
    internal bool ShouldObserveInvoke(long receiver)
    { _main.VerifyAccess(); return Find(receiver) is not null || _current is not null || ShouldObserveTweenInlineInvoke(receiver); }
    internal InvokeContext CaptureInvoke(long receiver)
    {
        Guard(); _authority.VerifyObservation();
        try
        {
            if (_busy || PhysicalTop != receiver) throw new InvalidOperationException("Listener capture has no exact main-thread physical Invoke entry.");
            var row = Find(receiver);
            if (row is null)
            {
                var mask = _masks.FirstOrDefault(item => !item._active) ?? throw new InvalidOperationException("Foreign UnityEvent context capacity exhausted.");
                mask.Begin(receiver); return mask;
            }
            if (Interlocked.Read(ref row.InFlight) != 1) throw new InvalidOperationException("The same event is physically active more than once.");
            row.Generation = Interlocked.Read(ref row.Entered); row.Returned = false; row.Post = null;
            row.Inline = null;
            if (row.Dispatches.Any(dispatch => !dispatch.Completed)) throw new InvalidOperationException("A prior UnityEvent dispatch still has physical responsibility.");
            row.Dispatches.Clear();
            row.InvokeCaller = row.InvokeMethod = 0; row.NativeInvokeObserved = false;
            row.Invoke.Begin(receiver); BeginListenerEffectsInvocation(row.Invoke); _busy = true;
            row.Pre = ReadGraph(row.Slot); CheckBudget(); Known(); return row.Invoke;
        }
        catch (Exception error) { Fault(error.Message); throw; }
        finally { _busy = false; }
    }
    internal InvokeContext CaptureNativeInvoke(NativeVoidFrame frame)
    {
        Guard();
        if (!frame.IsValid) { Fault("UnityEvent native Invoke has an invalid physical frame."); throw new InvalidOperationException(_failure); }
        VerifyNativeTransport(requireActive: true);
        var context = CaptureInvoke(frame.Self.ToInt64());
        context._native = true; context._nativeCaller = frame.Caller.ToInt64(); context._nativeMethod = frame.Method.ToInt64();
        if (context._record is { } row)
        {
            row.InvokeCaller = context._nativeCaller; row.InvokeMethod = context._nativeMethod; row.NativeInvokeObserved = true;
        }
        ObserveListenerEffectsNativeEntry(context);
        ObserveActualTweenInlineEntry(context, frame);
        ObserveRegisteredTweenChildEntry(context, frame);
        return context;
    }
    internal void CompleteNativeInvoke(InvokeContext context, NativeVoidFrame frame)
    {
        Guard();
        if (!context._native || !frame.IsValid || frame.Self.ToInt64() != context._receiver
            || frame.Caller.ToInt64() != context._nativeCaller || frame.Method.ToInt64() != context._nativeMethod)
        { Fault("UnityEvent native Invoke exit differs from its actual entry frame."); throw new InvalidOperationException(_failure); }
        VerifyNativeTransport(requireActive: true);
        CompleteInvoke(context);
        ObserveListenerEffectsNativeAfter(context);
    }
    internal void ObserveInvokeFault(NativeVoidFrame frame, string failure)
    {
        // Called on native workers too: fixed managed identities and an atomic
        // launch fault only. Foreign events acquire no ownership through this lane.
        if (Find(frame.Self.ToInt64()) is not null) Fault(failure);
    }
    private void VerifyNativeTransport(bool requireActive)
    {
        // A previous foreign/observer failure can make the shared transport
        // unknown without touching this event's scalar fault. Check that real
        // transport before any native receiver graph read, including after.
        var activity = _backend.ReadActivity(); Known();
        if (!activity.Known || activity.InFlight < 0 || requireActive && activity.InFlight == 0)
        { Fault("UnityEvent native transport is unknown or no longer physically active."); throw new InvalidOperationException(_failure); }
    }
    internal void ObserveInlineList(InvokeContext context, NativeEventMidFrame frame,
        ExactHookBroker.NativeVoidPhysicalIdentity identity)
    {
        Guard(); _authority.VerifyObservation();
        try
        {
            if (_busy || !frame.IsValid || frame.Event.ToInt64() != context._receiver || frame.Executing == IntPtr.Zero
                || frame.ReturnPc != (identity.ConfiguredMidReturnPc != IntPtr.Zero ? identity.ConfiguredMidReturnPc : new IntPtr(checked(_module + 0x1564420)))
                || !context._native || !context._active || context._after
                || !ReferenceEquals(_current, context) || PhysicalTop != context._receiver || context._record is not { } row
                || row.Generation != context._generation || row.Inline is not null
                || identity.Entry.Address.ToInt64() != checked(_module + 0x15642E0) || identity.Entry.Abi != ExactHookAbi.NativeVoid0
                || identity.Token == 0 || !identity.IsCurrentFor(context) || identity.ThreadId != Environment.CurrentManagedThreadId
                || identity.Frame.Self.ToInt64() != context._receiver || identity.Frame.Caller.ToInt64() != context._nativeCaller
                || identity.Frame.Method.ToInt64() != context._nativeMethod)
                throw new InvalidOperationException("Inline executing list has no exact current native parent context and fixed mid return.");
            VerifyNativeTransport(requireActive: true); _busy = true;
            var observed = _backend.CaptureInline(row.Slot, frame.Executing.ToInt64()); Known();
            VerifyNativeTransport(requireActive: true);
            if (!identity.IsCurrentFor(context))
                throw new InvalidOperationException("Inline parent capability expired during actual list adoption.");
            var graph = Freeze(observed.Graph); var executing = Freeze(observed.Returned); Validate(graph, row.Event);
            if (executing.Object != frame.Executing.ToInt64() || graph.ExecutingCalls.Object != executing.Object
                || !ListEqual(executing, graph.ExecutingCalls))
                throw new InvalidOperationException("Observed inline list differs from the actual rooted executing snapshot.");
            CheckBudget(row.Slot, graph); Known();
            TrackMutationReceivers(row, graph); TrackMutationInvokables(row, executing);
            row.Inline = new(graph, executing, unchecked((long)identity.Token), context._nativeCaller, frame.ReturnPc.ToInt64());
            ObserveListenerEffectsInline(context, row.Inline);
        }
        catch (Exception error) { Fault(error.Message); throw; }
        finally { _busy = false; }
    }
    internal void CompleteInvoke(InvokeContext context)
    {
        Guard();
        try
        {
            if (!context._active || context._after || !ReferenceEquals(_current, context) || PhysicalTop != context._receiver)
                throw new InvalidOperationException("Invoke after has no exact physical retained context.");
            if (context._record is { } row)
            {
                if (row.Generation != context._generation || row.Pre is null || row.Dispatches.Any(dispatch => !dispatch.Completed))
                    throw new InvalidOperationException("Invoke returned without its actual pre-snapshot generation.");
                _busy = true; row.Post = ReadGraph(row.Slot); CheckBudget(); Known();
                row.Changed |= !GraphEqual(row.Initial!, row.Post); row.Returned = true; _returned = checked(_returned + 1);
            }
            context._after = true;
        }
        catch (Exception error) { Fault(error.Message); throw; }
        finally { _busy = false; }
    }
    // Stock UnityEvent.Invoke inlines Prepare. These are independent real calls,
    // with their actual caller copied verbatim, never stock dispatcher proof.
    internal PrepareContext? CapturePrepare(NativeCoroutineFrame frame)
    {
        _main.VerifyAccess();
        var receiver = frame.Self.ToInt64();
        var matches = _events.Take(_count).Where(row => HasCalls(row, receiver)).ToArray();
        if (matches.Length == 0) return null;
        try
        {
            Guard(); _authority.VerifyObservation();
            if (matches.Length != 1 || _busy || !frame.IsValid || frame.Kind != NativeCoroutineKind.Factory)
                throw new InvalidOperationException("Prepare has no unique rooted calls receiver or valid physical frame.");
            VerifyNativeTransport(requireActive: true);
            var row = matches[0];
            if (row.Prepare._active || row.PrepareGeneration == long.MaxValue)
                throw new InvalidOperationException("Independent Prepare reentered or exhausted its generation.");
            var context = row.Prepare; context._generation = ++row.PrepareGeneration; context._receiver = receiver;
            context._caller = frame.Caller.ToInt64(); context._method = frame.Method.ToInt64();
            context._active = true; context._after = false; return context;
        }
        catch (Exception error) { Fault(error.Message); throw; }
    }
    internal void CompletePrepare(PrepareContext context, NativeCoroutineFrame frame, nuint result)
    {
        Guard();
        try
        {
            var row = context._record;
            if (!context._active || context._after || row.PrepareGeneration != context._generation || !frame.IsValid
                || frame.Kind != NativeCoroutineKind.Factory || frame.Method.ToInt64() != context._method
                || frame.Self.ToInt64() != context._receiver || frame.Caller.ToInt64() != context._caller || result == 0)
                throw new InvalidOperationException("Prepare output is not paired to this independent generation and actual return.");
            VerifyNativeTransport(requireActive: true);
            _busy = true;
            var value = _backend.CapturePrepared(row.Slot, unchecked((long)result)); Known();
            VerifyNativeTransport(requireActive: true);
            var graph = Freeze(value.Graph); var returned = Freeze(value.Returned); Validate(graph, row.Event);
            if (graph.Calls != context._receiver || returned.Object != unchecked((long)result) || returned.Object != graph.ExecutingCalls.Object
                || !ListEqual(returned, graph.ExecutingCalls)) throw new InvalidOperationException("Original Prepare did not return this exact executing list snapshot.");
            row.Prepared = new(graph, returned); row.PrepareCaller = context._caller;
            TrackMutationReceivers(row, graph); TrackMutationInvokables(row, returned);
            CheckBudget(row.Slot, graph); Known(); _prepared = checked(_prepared + 1); context._after = true;
        }
        catch (Exception error) { Fault(error.Message); throw; }
        finally { _busy = false; }
    }
    internal void ObservePrepareFault(NativeCoroutineFrame frame, string failure)
    {
        var receiver = frame.Self.ToInt64();
        foreach (var row in _events)
            if (row.Event != 0 && HasCalls(row, receiver)) { Fault(failure); return; }
    }
    internal bool TryConfirmNativeIdle()
    {
        Guard(); _authority.VerifyObservation();
        if (!_authority.TryConfirmSourceIdle()) { Known(); return false; }
        var activity = _backend.ReadActivity(); Known();
        if (!activity.Known || activity.InFlight < 0) { Fault("UnityEvent physical entry activity is unknown."); throw new InvalidOperationException(_failure); }
        return activity.InFlight == 0 && _physicalDepth == 0 && _current is null
            && _mutationCurrent is null
            && !_events.Any(row => Interlocked.Read(ref row.InFlight) != 0 || row.Invoke._active || row.Prepare._active
                || row.Dispatches.Any(dispatch => !dispatch.Completed) || row.Mutations.Any(mutation => !mutation.Completed));
    }
    internal NativeLocalUnityEventLedgerSnapshot Read()
    {
        _main.VerifyAccess(); var rows = new List<NativeUnityEventLedgerEventSnapshot>(_count); long entered = 0, active = 0;
        for (var index = 0; index < _count; ++index)
        {
            var row = _events[index]; entered += Interlocked.Read(ref row.Entered);
            active += Interlocked.Read(ref row.InFlight) + (row.Prepare._active ? 1 : 0) + row.Dispatches.Count(dispatch => !dispatch.Completed)
                + row.Mutations.Count(mutation => !mutation.Completed);
            if (row.Initial is not null) rows.Add(new(index, row.Event, Interlocked.Read(ref row.Entered), Interlocked.Read(ref row.Exited),
                Interlocked.Read(ref row.InFlight), row.Generation, row.Returned, row.Changed, row.Initial, row.Pre, row.Prepared, row.Post,
                row.PrepareGeneration, row.PrepareCaller, row.InvokeCaller, row.InvokeMethod, row.NativeInvokeObserved, row.Inline,
                Array.AsReadOnly(row.Dispatches.Select(dispatch => dispatch.Snapshot()).ToArray()), row.MutationGeneration,
                Array.AsReadOnly(row.Mutations.Select(mutation => mutation.Snapshot()).ToArray())));
        }
        active += Interlocked.Read(ref _typedMutationsInFlight);
        return new(_count, entered, _returned, _prepared, active, _backend.RootRecords, true, false, Unknown,
            Volatile.Read(ref _failure), rows.AsReadOnly(), InvokableDispatches: _dispatches,
            ReturnedDispatches: _dispatchReturned, AbnormalDispatches: _dispatchAbnormal,
            ObservedMutations: _mutationCount, ReturnedMutations: _mutationReturned);
    }
    private static bool HasCalls(EventRecord row, long receiver) => receiver != 0 && (row.Initial?.Calls == receiver
        || row.Pre?.Calls == receiver || row.Post?.Calls == receiver || row.Prepared?.Graph.Calls == receiver);
    private NativeUnityEventGraph ReadGraph(int slot)
    {
        VerifyNativeTransport(requireActive: false);
        var value = _backend.ReadGraph(slot); Known(); VerifyNativeTransport(requireActive: false);
        var graph = Freeze(value); Validate(graph, _events[slot].Event); TrackMutationReceivers(_events[slot], graph); return graph;
    }
    private void CheckBudget(int changedSlot = -1, NativeUnityEventGraph? changed = null)
    {
        var rows = 0; var leaves = 0;
        foreach (var record in _events.Take(_count))
        {
            var graph = record.Slot == changedSlot ? changed : record.Post ?? record.Prepared?.Graph ?? record.Pre ?? record.Initial;
            if (graph is null) continue;
            rows = checked(rows + graph.Candidates.Count);
            foreach (var list in Lists(graph)) { rows = checked(rows + list.Rows.Count); leaves = checked(leaves + list.Rows.Sum(row => row.Leaves.Count)); }
        }
        if (rows > MaximumRows || leaves > MaximumLeaves || _backend.RootRecords > MaximumRoots)
            throw new InvalidOperationException("Complete listener snapshot exceeds fixed row/leaf/root responsibility bounds.");
    }
    private static IEnumerable<NativeUnityEventList> Lists(NativeUnityEventGraph graph)
    { yield return graph.PersistentCalls; yield return graph.RuntimeCalls; yield return graph.ExecutingCalls; }
    private static void Validate(NativeUnityEventGraph graph, long identity)
    {
        if (graph.Event != identity || graph.Calls == 0 || graph.PersistentGroup == 0 || graph.CandidatesList == 0
            || graph.CandidatesVersion < 0 || graph.Candidates.Any(row => row is null || row.Object == 0))
            throw new InvalidOperationException("Listener graph has no complete actual event/calls/persistent identity.");
        foreach (var list in Lists(graph))
            if (list.Object == 0 || list.Version < 0 || list.Rows.Any(row => row is null || row.Object == 0 || row.Class == 0
                || row.Leaves.Any(leaf => leaf is null || leaf.Delegate == 0 || leaf.Class == 0)))
                throw new InvalidOperationException("Listener list/delegate graph is incomplete.");
    }
    private static NativeUnityEventGraph Freeze(NativeUnityEventGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph); ArgumentNullException.ThrowIfNull(graph.Candidates);
        return graph with { PersistentCalls = Freeze(graph.PersistentCalls), RuntimeCalls = Freeze(graph.RuntimeCalls),
            ExecutingCalls = Freeze(graph.ExecutingCalls), Candidates = Array.AsReadOnly(graph.Candidates.ToArray()) };
    }
    private static NativeUnityEventList Freeze(NativeUnityEventList list)
    {
        ArgumentNullException.ThrowIfNull(list); ArgumentNullException.ThrowIfNull(list.Rows);
        return list with { Rows = Array.AsReadOnly(list.Rows.Select(FreezeInvokable).ToArray()) };
    }
    private static NativeUnityEventInvokable FreezeInvokable(NativeUnityEventInvokable row)
    {
        ArgumentNullException.ThrowIfNull(row); ArgumentNullException.ThrowIfNull(row.Leaves);
        if (row.CachedBoolean is { } argument && (argument.BooleanClass == 0 || argument.InvokableParentClass == 0
            || argument.ActionClass == 0 || argument.DelegateField == 0 || argument.ArgumentField == 0
            || argument.ActionInvokeMethod == 0
            || argument.DelegateField == argument.ArgumentField
            || row.Leaves.Any(leaf => leaf is null || !leaf.Opaque && (leaf.Class != argument.ActionClass
                || leaf.Target == 0 || leaf.MethodInfo == 0))
            || !row.Opaque && (row.Delegate == 0 || row.Leaves.Count == 0 || row.Leaves.Any(leaf => leaf.Opaque))))
            throw new InvalidOperationException("Cached Boolean listener has no complete closed-class argument/delegate provenance.");
        // The argument is an immutable scalar record. Native lists and delegate
        // leaves still need their own copy at every retained observation.
        return row with { Leaves = Array.AsReadOnly(row.Leaves.ToArray()) };
    }
    private static bool ListEqual(NativeUnityEventList first, NativeUnityEventList second)
        => first.Object == second.Object && first.Version == second.Version && first.Rows.Count == second.Rows.Count
            && first.Rows.Zip(second.Rows).All(pair => pair.First.Object == pair.Second.Object && pair.First.Class == pair.Second.Class
                && pair.First.Opaque == pair.Second.Opaque && pair.First.Delegate == pair.Second.Delegate
                && pair.First.InvocationList == pair.Second.InvocationList && pair.First.CachedBoolean == pair.Second.CachedBoolean
                && pair.First.Leaves.SequenceEqual(pair.Second.Leaves));
    private static bool GraphEqual(NativeUnityEventGraph first, NativeUnityEventGraph second)
        => first.Event == second.Event && first.Calls == second.Calls && first.PersistentGroup == second.PersistentGroup
            && first.CallsDirty == second.CallsDirty && first.NeedsUpdate == second.NeedsUpdate && first.InvokeArray == second.InvokeArray
            && first.CandidatesList == second.CandidatesList && first.CandidatesVersion == second.CandidatesVersion
            && first.Candidates.SequenceEqual(second.Candidates) && ListEqual(first.PersistentCalls, second.PersistentCalls)
            && ListEqual(first.RuntimeCalls, second.RuntimeCalls) && ListEqual(first.ExecutingCalls, second.ExecutingCalls);
    private void Fault(string failure)
    { Interlocked.CompareExchange(ref _failure, failure.Length > 768 ? failure[..768] : failure, null); Interlocked.Exchange(ref _fault, 1); _authority.ObserveFaultFromAnyThread(); }
    private void Known()
    {
        const string message = "Listener/source authority became unknown during observation.";
        if (ObservationAuthorityUnknown)
        {
            var failure = Volatile.Read(ref _failure);
            if (failure is not null)
                throw new InvalidOperationException($"{message} First ledger failure: {failure}");
            // The launch snapshot is managed state. Leave native source/activity reads to the existing observation path.
            if (_launch?.Read() is { Phase: LocalRunLaunchCommitPhase.Unknown } launch)
                throw new InvalidOperationException($"{message} Launch phase is Unknown. {launch.Failure}");
            throw new InvalidOperationException($"{message} The retained listener/source association is unknown.");
        }
        if (_backend.CleanupUnknown)
            throw new InvalidOperationException($"{message} Listener backend cleanup is unknown.");
    }
    private void Guard() { _main.VerifyAccess(); if (!_ready) throw new InvalidOperationException("Listener ledger is not initialized."); Known(); }
    private sealed class Authority : ICutoutListenerAuthority
    {
        private readonly LocalRunLaunchCommitState _launch;
        private readonly NativeLocalCutoutEventSource _source;
        internal Authority(LocalRunLaunchCommitState launch, NativeLocalCutoutEventSource source) => (_launch, _source) = (launch, source);
        public bool Unknown => !_source.IsFor(_launch) || _launch.Read().Phase == LocalRunLaunchCommitPhase.Unknown;
        public void VerifyBeforeCapture()
        {
            _launch.VerifyPendingOwnership(_launch.Registry); var value = _launch.Read();
            if (Unknown || value.ActivationAttempted || value.Phase != LocalRunLaunchCommitPhase.ControllerBound
                || _source.Read().Source?.Controller != value.Controller) throw new InvalidOperationException("Listener capture requires the exact actual source before activation.");
        }
        public void VerifyObservation()
        {
            var value = _launch.Read(); var session = _launch.Registry.Read(_launch.Session);
            if (Unknown || !value.ActivationAttempted || value.Controller == 0 || session.Identity != _launch.Identity
                || session.ControllerIdentity != value.Controller || _source.Read().Source?.Controller != value.Controller
                || session.ProtectionRetired || session.CleanupAttempted || session.Phase is LocalRunSessionPhase.Closed or LocalRunSessionPhase.CleanupUnknown
                || !ReferenceEquals(_launch.Registry.FindProtectedSession(LocalBoundaryKind.ControllerLevelOver, value.Controller), _launch.Session))
                throw new InvalidOperationException("Listener observation requires the same activated protected run.");
        }
        public bool TryConfirmSourceIdle() => _source.TryConfirmNativeIdle();
        public void ObserveFaultFromAnyThread() => _launch.ObserveNativeFaultFromAnyThread("UnityEvent listener observation is unknown.");
    }
}
