using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal enum InitializationCoroutineRole { Delay, WaitUntil }
internal readonly record struct InitializationCoroutineObservation(long Enumerator, int State, long Todo, long Predicate, int SecondsBits);
internal sealed record InitializationCoroutineSnapshot(bool Ended, bool Unknown, bool Closed,
    long DelayEnumerator, long WaitEnumerator, bool DelayEnded, bool WaitEnded, int Roots, string? Failure);
internal interface IInitializationCoroutineBackend
{
    bool CleanupUnknown { get; }
    int RootRecords { get; }
    void Initialize(NativeLocalInitializationCoroutineTracker owner);
    void RetainEnumerator(InitializationCoroutineRole role, long enumerator);
    InitializationCoroutineObservation ReadEnumerator(InitializationCoroutineRole role);
    long ReadWaitUntilCurrent(long parent, long predicate, bool retain);
    void RetainCoroutine(InitializationCoroutineRole role, long coroutine);
    ExactHookBroker.ManagedBoundaryActivity ReadActivity();
    void Detach();
    void ReleaseRoots();
    bool SupportsPerRecordRetirement => false;
    void ReleaseEndedRoots(InitializationCoroutineRole role, long enumerator)
        => throw new UnsupportedSignatureException("This backend has no exact per-role root retirement contract.");
}
internal interface IInitializationCoroutineAuthority
{
    bool CoroutineCancellationPending => false;
    long Controller { get; }
    bool Unknown { get; }
    void VerifyBeforeCapture();
    void VerifyGetterWindow();
    void ObserveFaultFromAnyThread(string reason);
}
internal sealed class NativeLocalInitializationCoroutineException : InvalidOperationException
{
    internal NativeLocalInitializationCoroutineTracker Responsibility { get; }
    internal NativeLocalInitializationCoroutineException(NativeLocalInitializationCoroutineTracker owner, Exception error)
        : base("Initialization coroutine observation is unconfirmed; its roots and ownership remain retained.", error) => Responsibility = owner;
}

/// <summary>Tracks only the two actual IEnumerator instances created by this
/// controller's getter. Natural false plus native drain proves their observations
/// ended, never that every controller/audio/Unity consumer has ended.</summary>
internal sealed class NativeLocalInitializationCoroutineTracker
{
    private static readonly object Gate = new();
    private static readonly HashSet<NativeLocalInitializationCoroutineTracker> Owners = new();
    private readonly IMainThreadDispatcher _main;
    private readonly IInitializationCoroutineBackend _backend;
    private readonly IInitializationCoroutineAuthority _authority;
    private readonly LocalRunLaunchCommitState? _launch;
    private readonly bool _controlled;
    private readonly long _controller, _delayCaller, _waitCaller;
    private readonly Record[] _records;
    private long _waitUntilYield;
    private NativeEngineCoroutineObserverRetirementAuthority? _engineRetirement;
    private int _fault;
    private bool _initialized, _ended, _closed, _releaseAttempted, _recycling;
    private string? _failure;
    private sealed class Record
    {
        internal long Enumerator, ObservedHandle;
        internal bool Attempted, Published, StartReturned, Terminal, HandleConfirmed;
        internal bool Retiring, FreeAttempted, Retired;
        internal NativeEngineCoroutineObserverRetirementAuthority.Permit? Retirement;
        internal int ExpectedState;
        internal InitializationCoroutineObservation Baseline;
        internal readonly Context Start, Move;
        internal Record(NativeLocalInitializationCoroutineTracker owner, InitializationCoroutineRole role)
        { Start = new(owner, role, false); Move = new(owner, role, true); }
    }
    internal sealed class Context : IDisposable
    {
        private readonly NativeLocalInitializationCoroutineTracker _owner;
        internal readonly InitializationCoroutineRole Role;
        internal readonly bool IsMove;
        internal bool Active, After;
        internal Context(NativeLocalInitializationCoroutineTracker owner, InitializationCoroutineRole role, bool move)
            => (_owner, Role, IsMove) = (owner, role, move);
        internal void Begin()
        { if (Active) throw new InvalidOperationException("The same coroutine physically reentered before its previous exit."); Active = true; After = false; }
        public void Dispose()
        {
            _owner._main.VerifyAccess();
            if (!Active) { _owner.Fault("A coroutine context was released without a matching entry."); return; }
            Active = false;
            if (!After) _owner.Fault("A captured coroutine did not normally return through its paired observation.");
        }
    }
    private NativeLocalInitializationCoroutineTracker(IMainThreadDispatcher main, IInitializationCoroutineBackend backend,
        IInitializationCoroutineAuthority authority, LocalRunLaunchCommitState? launch, bool controlled, long module)
    {
        (_main, _backend, _authority, _launch, _controlled, _controller) = (main, backend, authority, launch, controlled, authority.Controller);
        _delayCaller = checked(module + 0x3BEFDA); _waitCaller = checked(module + 0x3BF0B2);
        _records = new[] { new Record(this, InitializationCoroutineRole.Delay), new Record(this, InitializationCoroutineRole.WaitUntil) };
    }
    internal static NativeLocalInitializationCoroutineTracker Capture(NativeRuntime api, IMainThreadDispatcher main,
        AdapterBuildProfile profile, IntPtr module, ExactHookBroker broker, LocalRunLaunchCommitState launch)
        => CaptureCore(main, new NativeLocalInitializationCoroutineBackend(api, main, profile, module, broker), new Authority(launch), launch, false, module.ToInt64());
    internal static NativeLocalInitializationCoroutineTracker CaptureBackend(IMainThreadDispatcher main,
        IInitializationCoroutineBackend backend, IInitializationCoroutineAuthority authority, long module)
    {
        if (backend is NativeLocalInitializationCoroutineBackend)
            throw new InvalidOperationException("Controlled coroutine capture cannot adopt a production backend.");
        return CaptureCore(main, backend, authority, null, true, module);
    }
    private static NativeLocalInitializationCoroutineTracker CaptureCore(IMainThreadDispatcher main,
        IInitializationCoroutineBackend backend, IInitializationCoroutineAuthority authority, LocalRunLaunchCommitState? launch, bool controlled, long module)
    {
        ArgumentNullException.ThrowIfNull(main); main.VerifyAccess(); ArgumentNullException.ThrowIfNull(backend); ArgumentNullException.ThrowIfNull(authority);
        authority.VerifyBeforeCapture(); if (authority.Unknown || authority.Controller == 0 || module <= 0) throw new InvalidOperationException("Known actual controller and module identities are required.");
        var owner = new NativeLocalInitializationCoroutineTracker(main, backend, authority, launch, controlled, module);
        lock (Gate) { if (Owners.Count >= 32) throw new InvalidOperationException("Initialization coroutine owner capacity is exhausted."); Owners.Add(owner); }
        try
        {
            backend.Initialize(owner); owner.RequireObservationKnown(); authority.VerifyBeforeCapture(); owner.RequireObservationKnown();
            var activity = backend.ReadActivity(); owner.RequireObservationKnown();
            if (activity is not { Known: true, InFlight: 0 }) throw new InvalidOperationException("Pre-capture coroutine calls have not drained.");
            owner._initialized = true; return owner;
        }
        catch (Exception error) { owner.Fault(error.Message); throw new NativeLocalInitializationCoroutineException(owner, error); }
    }
    internal bool IsFor(LocalRunLaunchCommitState launch) => !_controlled && ReferenceEquals(_launch, launch) && !Unknown && !_closed;
    internal bool IsControlled => _controlled;
    internal void AttachEngineRetirementAuthority(NativeEngineCoroutineRuntimeCoordinator owner,
        NativeEngineCoroutineObserverRetirementAuthority authority)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(authority);
        if (_controlled || _launch is null || _engineRetirement is not null || !IsFor(_launch)
            || !_backend.SupportsPerRecordRetirement || !authority.IsForProduction(owner, _launch, this, _controller))
            throw new InvalidOperationException("Initialization retirement requires its exact production engine and per-role backend, once before activation.");
        _engineRetirement = authority;
    }
    internal void AttachControlledEngineRetirementAuthority(NativeEngineCoroutineObserverRetirementAuthority authority)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(authority);
        if (!_controlled || _launch is not null || _backend is NativeLocalInitializationCoroutineBackend
            || _engineRetirement is not null || !_backend.SupportsPerRecordRetirement || !authority.IsForControlled(this, _controller))
            throw new InvalidOperationException("Controlled per-role retirement cannot bind production initialization observations.");
        _engineRetirement = authority;
    }
    internal long ControllerIdentity => _controller;
    internal bool ObservationUnknown => Volatile.Read(ref _fault) != 0;
    // Pure managed authority state, also available to the backend after activity
    // reads and each independent root free. Local free failure is kept separate.
    internal bool ObservationAuthorityUnknown => ObservationUnknown || _authority.Unknown;
    internal bool Unknown => ObservationAuthorityUnknown || _backend.CleanupUnknown || _engineRetirement?.Unknown == true;
    internal bool CoroutineCancellationPending => _authority.CoroutineCancellationPending;
    // Unity starts the yielded CustomYieldInstruction before the outer managed
    // StartCoroutine returns. Only the completed 0 -> 1 MoveNext owns Current.
    internal bool IsCurrentWaitUntilYield(long parentEnumerator, long childEnumerator)
    {
        _main.VerifyAccess();
        var record = _records[(int)InitializationCoroutineRole.WaitUntil];
        bool Eligible() => _initialized && !_closed && !_releaseAttempted && !Unknown && !CoroutineCancellationPending
            && record.Published && !record.Terminal && !record.Retiring && !record.Move.Active && record.Move.After
            && record.ExpectedState == 1 && record.Enumerator == parentEnumerator && _waitUntilYield == childEnumerator;
        if (parentEnumerator == 0 || childEnumerator == 0 || childEnumerator == parentEnumerator || !Eligible()) return false;
        try
        {
            var current = _backend.ReadWaitUntilCurrent(parentEnumerator, record.Baseline.Predicate, retain: false);
            if (current != childEnumerator) throw new InvalidOperationException("The retained WaitUntil differs from its parent's Current.");
            return Eligible();
        }
        catch (Exception error) { Fault(error.Message); throw; }
    }
    internal void RequireCancellationRetirement()
    { _main.VerifyAccess(); if (CoroutineCancellationPending) throw new InvalidOperationException("Cancellation requests retain initialization coroutine roots."); }
    internal InitializationCoroutineSnapshot Read()
    {
        _main.VerifyAccess(); return new(_ended && !Unknown && !CoroutineCancellationPending, Unknown, _closed,
            Interlocked.Read(ref _records[0].Enumerator), Interlocked.Read(ref _records[1].Enumerator), _records[0].Terminal,
            _records[1].Terminal, _backend.RootRecords, Volatile.Read(ref _failure));
    }
    internal IReadOnlyList<NativeCoroutineCancellationIdentity> ReadCancellationIdentities()
    {
        _main.VerifyAccess();
        return Array.AsReadOnly(_records.Select((record, slot) => CancellationIdentity(record, slot)).Where(value => value.Enumerator != 0).ToArray());
    }
    internal bool TryMatchCancellationReference(NativeCoroutineCancellationReferenceKind kind, long receiver, long argument,
        out NativeCoroutineCancellationIdentity identity)
    {
        _main.VerifyAccess(); identity = default;
        if (!_initialized || _closed || _releaseAttempted || Unknown) return false;
        var found = false;
        for (var slot = 0; slot < _records.Length; ++slot) {
            var candidate = CancellationIdentity(_records[slot], slot);
            if (!candidate.MatchesReference(kind, receiver, argument)) continue;
            if (found) { identity = default; return false; } // Ambiguous handle is never assigned to a guessed role.
            identity = candidate; found = true;
        }
        if (Unknown) { identity = default; return false; }
        return found;
    }
    internal bool IsCurrentCancellationIdentity(NativeCoroutineCancellationIdentity identity)
    {
        _main.VerifyAccess();
        return ReferenceEquals(identity.Owner, this) && identity.Slot >= 0 && identity.Slot < _records.Length
            && CancellationIdentity(_records[identity.Slot], identity.Slot).SameRecord(identity) && !Unknown;
    }
    private NativeCoroutineCancellationIdentity CancellationIdentity(Record record, int slot)
        => new(this, _controller, slot, 0, Interlocked.Read(ref record.Enumerator), Interlocked.Read(ref record.ObservedHandle),
            record.HandleConfirmed, record.StartReturned, record.Published, record.Retiring || record.Retired || _releaseAttempted || _closed,
            record.Terminal, _initialized && !_closed && !_releaseAttempted && !Unknown);
    private void Fault(string reason)
    {
        // Disposing the failed callback also reports a missing return. Keep the
        // original cause visible after that cleanup and across worker faults.
        var bounded = reason.Length > 768 ? reason[..768] : reason;
        var first = Interlocked.CompareExchange(ref _failure, bounded, null) ?? bounded;
        Interlocked.Exchange(ref _fault, 1);
        _authority.ObserveFaultFromAnyThread(first);
    }
    internal void ObserveFault(NativeCoroutineFrame frame, string reason)
    {
        // Runs on arbitrary native threads. No dispatcher, root or object metadata reads.
        var self = frame.Self.ToInt64();
        if (frame.Kind == NativeCoroutineKind.Start ? self == _controller
            : self != 0 && (self == Interlocked.Read(ref _records[0].Enumerator) || self == Interlocked.Read(ref _records[1].Enumerator)))
            Fault(reason);
    }
    internal Context? CaptureStart(NativeCoroutineFrame frame)
    {
        _main.VerifyAccess(); if (_closed || frame.Self.ToInt64() != _controller) return null;
        var caller = frame.Caller.ToInt64();
        if (caller != _delayCaller && caller != _waitCaller) return null;
        try
        {
            Guard(); if (!frame.IsValid || frame.Kind != NativeCoroutineKind.Start) throw new InvalidOperationException("Invalid physical Start frame.");
            _authority.VerifyGetterWindow(); Guard(); var role = caller == _delayCaller ? InitializationCoroutineRole.Delay : InitializationCoroutineRole.WaitUntil;
            var record = _records[(int)role];
            if (record.Attempted || frame.Enumerator == IntPtr.Zero) throw new InvalidOperationException("This getter coroutine was already started or has no enumerator.");
            record.Attempted = true; Interlocked.Exchange(ref record.Enumerator, frame.Enumerator.ToInt64());
            if (_records[1 - (int)role].Enumerator == record.Enumerator) throw new InvalidOperationException("Two coroutine roles cannot alias the same IEnumerator.");
            _backend.RetainEnumerator(role, record.Enumerator); Guard();
            record.Baseline = _backend.ReadEnumerator(role); Guard(); RequireStable(record, record.Baseline);
            if (record.Baseline.State != 0 || record.Baseline.Todo == 0
                || (role == InitializationCoroutineRole.Delay ? record.Baseline.SecondsBits != 0x40466666 || record.Baseline.Predicate != 0 : record.Baseline.Predicate == 0))
                throw new UnsupportedSignatureException("Getter coroutine initial state, delegates or 3.1f delay differ.");
            record.ExpectedState = 0; record.Start.Begin(); record.Published = true; return record.Start;
        }
        catch (Exception error) { Fault(error.Message); throw; }
    }
    internal void CompleteStart(Context context, NativeCoroutineFrame frame, nuint coroutine)
    {
        _main.VerifyAccess();
        try
        {
            var record = RequireContext(context, false, frame.Enumerator.ToInt64());
            // Save the actual return before Guard/root/Target checks can fail.
            // This scalar is diagnostic responsibility until retention succeeds.
            Interlocked.Exchange(ref record.ObservedHandle, unchecked((long)coroutine));
            try { Guard(); }
            catch (InvalidOperationException error)
            {
                // The engine Start may have faulted during the original call. Keep
                // the managed caller and enumerator available for that same run.
                throw new InvalidOperationException($"{error.Message} Getter Start returned: role={context.Role} "
                    + $"managedCaller=0x{frame.Caller:x} managedReceiver=0x{frame.Self:x} "
                    + $"managedEnumerator=0x{frame.Enumerator:x} returnedHandle=0x{coroutine:x}.", error);
            }
            if (frame.Self.ToInt64() != _controller) throw new InvalidOperationException("Start receiver changed before its after callback.");
            var value = _backend.ReadEnumerator(context.Role); Guard(); RequireStable(record, value);
            if (coroutine == 0 && !record.Terminal) throw new InvalidOperationException("A pending IEnumerator has no actual Coroutine handle.");
            if (coroutine != 0) { _backend.RetainCoroutine(context.Role, unchecked((long)coroutine)); Guard(); record.HandleConfirmed = true; }
            record.StartReturned = true; context.After = true;
        }
        catch (Exception error) { Fault(error.Message); throw; }
    }
    internal Context? CaptureMove(InitializationCoroutineRole role, NativeCoroutineFrame frame)
    {
        _main.VerifyAccess(); var record = _records[(int)role];
        if (_closed || frame.Self.ToInt64() != Interlocked.Read(ref record.Enumerator)) return null;
        if (role == InitializationCoroutineRole.WaitUntil) _waitUntilYield = 0;
        try
        {
            Guard(); if (!frame.IsValid || frame.Kind != NativeCoroutineKind.MoveNext) throw new InvalidOperationException("Invalid physical MoveNext frame.");
            if (!record.Published || record.Terminal) throw new InvalidOperationException("A completed or unpublished getter IEnumerator was advanced again.");
            var value = _backend.ReadEnumerator(role); Guard(); RequireStable(record, value);
            if (value.State != record.ExpectedState) throw new InvalidOperationException("IEnumerator state changed outside its paired MoveNext.");
            record.Move.Begin(); return record.Move;
        }
        catch (Exception error) { Fault(error.Message); throw; }
    }
    internal void CompleteMove(Context context, NativeCoroutineFrame frame, nuint result)
    {
        _main.VerifyAccess();
        try
        {
            Guard(); var record = RequireContext(context, true, frame.Self.ToInt64()); var value = _backend.ReadEnumerator(context.Role); Guard(); RequireStable(record, value);
            if (result > byte.MaxValue) throw new InvalidOperationException("MoveNext result exceeds its raw byte ABI.");
            if (result == 0)
            {
                if (value.State != -1 || (context.Role == InitializationCoroutineRole.Delay && record.ExpectedState == 0))
                    throw new InvalidOperationException("MoveNext did not follow the known natural completion path.");
                record.Terminal = true;
            }
            else if (context.Role == InitializationCoroutineRole.WaitUntil
                ? record.ExpectedState != 0 || value.State != 1
                : record.ExpectedState == 0 ? value.State is not (1 or 2) : record.ExpectedState != 1 || value.State != 1)
                throw new InvalidOperationException("MoveNext returned true without a known state transition.");
            if (context.Role == InitializationCoroutineRole.WaitUntil && result != 0)
            {
                var current = _backend.ReadWaitUntilCurrent(record.Enumerator, value.Predicate, retain: true);
                Guard();
                if (current == 0 || current == record.Enumerator || current == _records[0].Enumerator)
                    throw new InvalidOperationException("WaitUntil.MoveNext returned an absent or aliased Current.");
                _waitUntilYield = current;
            }
            record.ExpectedState = value.State; context.After = true;
        }
        catch (Exception error) { Fault(error.Message); throw; }
    }
    private Record RequireContext(Context context, bool move, long enumerator)
    {
        var record = _records[(int)context.Role];
        if (!context.Active || context.After || context.IsMove != move || !ReferenceEquals(context, move ? record.Move : record.Start)
            || record.Enumerator != enumerator) throw new InvalidOperationException("Coroutine after callback is not paired to its exact recorded entry.");
        return record;
    }
    private static void RequireStable(Record record, InitializationCoroutineObservation value)
    {
        if (value.Enumerator != record.Enumerator || (record.Baseline.Todo != 0
            && (value.Todo != record.Baseline.Todo || value.Predicate != record.Baseline.Predicate)))
            throw new InvalidOperationException("The rooted IEnumerator or its callback identities changed.");
    }
    internal bool TryConfirmEnded()
    {
        _main.VerifyAccess(); if (CoroutineCancellationPending) return false;
        if (_closed) { if (Unknown || !_ended) throw new InvalidOperationException("Coroutine tracker was closed without confirmed completion."); return true; }
        try
        {
            Guard(); var activity = _backend.ReadActivity(); Guard(); if (CoroutineCancellationPending) return false;
            if (!activity.Known || activity.InFlight < 0) throw new InvalidOperationException("Coroutine activity became unknown or invalid.");
            if (activity.InFlight != 0 || _records.Any(record => record.Start.Active || record.Move.Active)) return false;
            foreach (var record in _records)
                if (!record.StartReturned || !record.Terminal) return false;
            for (var index = 0; index < 2; ++index)
            { if (_records[index].Retired) continue; Guard(); var value = _backend.ReadEnumerator((InitializationCoroutineRole)index); Guard(); if (CoroutineCancellationPending) return false; RequireStable(_records[index], value); if (value.State != -1) throw new InvalidOperationException("Completed IEnumerator changed its terminal state."); }
            Guard(); if (CoroutineCancellationPending) return false; _ended = true; return true;
        }
        catch (Exception error) { Fault(error.Message); throw new NativeLocalInitializationCoroutineException(this, error); }
    }
    // Return may observe natural iterator completion before the cancellation
    // hold can retire. This writes no _ended/retirement state and grants no free.
    internal bool TryConfirmNaturalEndForReturn()
    {
        _main.VerifyAccess();
        if (_closed) { RequireObservationKnown(); return _ended; }
        try
        {
            Guard(); var activity=_backend.ReadActivity(); Guard();
            if (!activity.Known || activity.InFlight<0) throw new InvalidOperationException("Return source coroutine activity is unknown.");
            if (_recycling || _releaseAttempted || activity.InFlight!=0
                || _records.Any(record=>!record.StartReturned||!record.Terminal||record.Start.Active||record.Move.Active||record.Retiring)) return false;
            for(var index=0;index<_records.Length;++index)
            {
                var record=_records[index];if(record.Retired)continue;
                var enumerator=record.Enumerator;var value=_backend.ReadEnumerator((InitializationCoroutineRole)index);Guard();
                if(record.Enumerator!=enumerator||record.Start.Active||record.Move.Active||record.Retiring||!record.StartReturned||!record.Terminal)
                    throw new InvalidOperationException("Return source iterator changed its exact completed generation.");
                RequireStable(record,value);
                if(value.State!=-1)throw new InvalidOperationException("Return source iterator changed its natural terminal state.");
            }
            var after=_backend.ReadActivity();Guard();
            if(!after.Known||after.InFlight<0)throw new InvalidOperationException("Return source activity changed during verification.");
            return after.InFlight==0&&!_records.Any(record=>record.Start.Active||record.Move.Active||record.Retiring);
        }
        catch(Exception error){Fault(error.Message);throw new NativeLocalInitializationCoroutineException(this,error);}
    }
    // Only releases independent observation roots. No controller/resource destruction
    // or complete-run consumers-ended authority is provided by this method.
    internal bool TryReleaseAfterEnded()
    {
        _main.VerifyAccess(); if (CoroutineCancellationPending) return false;
        if (_closed) { RequireObservationKnown(); return true; } Guard();
        if (_launch is not null && _engineRetirement is null)
            throw new InvalidOperationException("Production initialization retirement requires the exact engine authority.");
        if (_engineRetirement is not null) return TryRetireEndedRecords();
        if (_releaseAttempted) throw new InvalidOperationException("Unknown coroutine observer cleanup cannot retry.");
        if (!TryConfirmEnded()) return false;
        if (CoroutineCancellationPending) return false; _releaseAttempted = true;
        try
        {
            _backend.Detach(); Guard(); RequireCancellationRetirement(); var activity = _backend.ReadActivity(); Guard(); RequireCancellationRetirement(); if (activity is not { Known: true, InFlight: 0 })
                throw new InvalidOperationException("Coroutine calls changed while logically detaching.");
            RequireCancellationRetirement(); _backend.ReleaseRoots(); RequireCancellationRetirement(); if (Unknown || _backend.RootRecords != 0) throw new InvalidOperationException("Coroutine observation root cleanup is unconfirmed.");
            _closed = true; lock (Gate) Owners.Remove(this); return true;
        }
        catch (Exception error) { Fault(error.Message); throw new NativeLocalInitializationCoroutineException(this, error); }
    }
    private bool TryRetireEndedRecords()
    {
        if (_recycling || _releaseAttempted) throw new InvalidOperationException("Initialization record retirement cannot reenter or retry an unknown free.");
        _recycling = true;
        try
        {
            for (var index = 0; index < _records.Length; ++index)
            {
                var record = _records[index];
                if (record.Retired || !record.StartReturned || !record.Terminal || record.Start.Active || record.Move.Active) continue;
                Guard(); RequireCancellationRetirement();
                var activity = _backend.ReadActivity(); Guard(); RequireCancellationRetirement();
                if (!activity.Known || activity.InFlight < 0) throw new InvalidOperationException("Initialization retirement activity is Unknown.");
                if (activity.InFlight != 0) return false;
                var value = _backend.ReadEnumerator((InitializationCoroutineRole)index); Guard(); RequireStable(record, value);
                if (value.State != -1) throw new InvalidOperationException("Initialization iterator lost its natural terminal state.");
                if (!record.Retiring)
                {
                    if (!_engineRetirement!.TryBegin(CancellationIdentity(record, index), out record.Retirement)) continue;
                    record.Retiring = true; Volatile.Write(ref record.Published, false);
                    record.Retirement!.MarkWithdrawn(CancellationIdentity(record, index));
                }
                activity = _backend.ReadActivity(); Guard(); RequireCancellationRetirement();
                if (!activity.Known || activity.InFlight < 0) throw new InvalidOperationException("Initialization activity changed after withdrawal.");
                if (activity.InFlight != 0 || !record.Retirement!.TryConfirmWithdrawnAndDrained()) return false;
                if (record.FreeAttempted) throw new InvalidOperationException("Unknown initialization role free cannot retry.");
                if (!record.Retirement.BeginFree()) return false;
                record.FreeAttempted = true;
                _backend.ReleaseEndedRoots((InitializationCoroutineRole)index, record.Enumerator);
                record.Retirement.CompleteFree(); Guard(); RequireCancellationRetirement();
                record.Retired = true; Interlocked.Exchange(ref record.Enumerator, 0);
            }
            if (_records.Any(record => !record.Retired)) return false;
            _ended = true; _releaseAttempted = true;
            _backend.Detach(); Guard(); RequireCancellationRetirement();
            var after = _backend.ReadActivity(); Guard(); RequireCancellationRetirement();
            if (after is not { Known: true, InFlight: 0 } || _backend.RootRecords != 0)
                throw new InvalidOperationException("Initialization observation detachment is unconfirmed.");
            _closed = true; lock (Gate) Owners.Remove(this); return true;
        }
        catch (Exception error)
        {
            _engineRetirement?.ObserveFailure(this, error.Message); Fault(error.Message);
            throw new NativeLocalInitializationCoroutineException(this, error);
        }
        finally { _recycling = false; }
    }
    internal bool IsRetiringForCleanup(InitializationCoroutineRole role, long enumerator)
    {
        _main.VerifyAccess(); var record = _records[(int)role];
        return _recycling && !Unknown && !CoroutineCancellationPending && record.Retiring && !record.Published
            && record.FreeAttempted && !record.Retired && record.Enumerator == enumerator;
    }
    internal void RequireEngineRetirementFree(InitializationCoroutineRole role, long enumerator)
    {
        _main.VerifyAccess(); RequireCancellationRetirement();
        if (_engineRetirement is null)
        {
            if (_launch is not null) throw new InvalidOperationException("Production root cleanup has no engine retirement authority.");
            return; // Existing controlled backend contract, without a production permit.
        }
        var index = (int)role;
        if (index is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(role));
        var record = _records[index];
        if (ObservationAuthorityUnknown || !_recycling || !record.Retiring || record.Published
            || !record.FreeAttempted || record.Retired || record.Enumerator != enumerator || record.Retirement is null)
            throw new InvalidOperationException("An independent initialization root lost its exact retirement scope.");
        record.Retirement.RequireFreeInProgress();
    }
    internal bool TryReleaseBeforeStart()
    {
        _main.VerifyAccess(); if (CoroutineCancellationPending) return false;
        if (_closed) { RequireObservationKnown(); return true; } Guard();
        if (_releaseAttempted || _records.Any(record => record.Attempted)) throw new InvalidOperationException("A started or uncertain coroutine cannot use pre-start cleanup.");
        _authority.VerifyBeforeCapture(); Guard(); if (CoroutineCancellationPending) return false;
        var before = _backend.ReadActivity(); Guard(); if (CoroutineCancellationPending || before is not { Known: true, InFlight: 0 }) return false;
        _releaseAttempted = true;
        try
        {
            _backend.Detach(); Guard(); RequireCancellationRetirement(); var activity = _backend.ReadActivity(); Guard(); RequireCancellationRetirement(); if (activity is not { Known: true, InFlight: 0 })
                throw new InvalidOperationException("Pre-start coroutine calls changed during detach.");
            RequireCancellationRetirement(); _backend.ReleaseRoots(); RequireCancellationRetirement(); if (Unknown || _backend.RootRecords != 0) throw new InvalidOperationException("Pre-start observation cleanup is unconfirmed.");
            _closed = true; lock (Gate) Owners.Remove(this); return true;
        }
        catch (Exception error) { Fault(error.Message); throw new NativeLocalInitializationCoroutineException(this, error); }
    }
    private void Guard()
    { _main.VerifyAccess(); if (!_initialized || _closed || Unknown) throw new InvalidOperationException("Initialization coroutine ownership is not active or is unknown."); }
    private void RequireObservationKnown()
    { if (Unknown) throw new InvalidOperationException("Initialization coroutine observation authority became unknown."); }
    private sealed class Authority : IInitializationCoroutineAuthority
    {
        private readonly LocalRunLaunchCommitState _state;
        internal Authority(LocalRunLaunchCommitState state) { ArgumentNullException.ThrowIfNull(state); _state = state; Controller = state.Read().Controller; }
        public long Controller { get; }
        public bool Unknown => _state.Read().Phase == LocalRunLaunchCommitPhase.Unknown;
        public bool CoroutineCancellationPending => _state.CoroutineCancellationPending;
        public void VerifyBeforeCapture()
        {
            _state.VerifyPendingOwnership(_state.Registry); var value = _state.Read();
            if (value.Controller != Controller || value.ActivationAttempted || value.Phase != LocalRunLaunchCommitPhase.ControllerBound || _state.ParameterBinding is null)
                throw new InvalidOperationException("Coroutine capture must follow the actual setter and precede activation.");
        }
        public void VerifyGetterWindow()
        {
            _state.VerifyPendingOwnership(_state.Registry); var value = _state.Read();
            if (value.Controller != Controller || !value.ActivationAttempted || value.Phase != LocalRunLaunchCommitPhase.Initializing || value.EarlyTerminal
                || !ExactHookBroker.HasCurrentNativeGetter(value.Identity.SessionId, value.Identity.RunGeneration, Controller, value.Information))
                throw new InvalidOperationException("Getter coroutine capture requires the actual pending initialization window.");
        }
        public void ObserveFaultFromAnyThread(string reason)
            => _state.ObserveNativeFaultFromAnyThread("Initialization coroutine observation: " + reason);
    }
}
