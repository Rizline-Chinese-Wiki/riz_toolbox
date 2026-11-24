using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal enum CutoutCoroutineRole { ResetDelay, DelayedEvent }
internal sealed record CutoutCoroutineObservation(long Enumerator, int State, long Trigger, long Events,
    int OffsetBits, long Action, int SecondsBits, long Provider);
internal sealed record NativeLocalCutoutCoroutineSnapshot(int Created, int NaturallyEnded, int LiveRecords,
    int RetiredRecords, int Roots, bool Unknown, string? Failure);
internal interface ICutoutCoroutineBackend
{
    bool CleanupUnknown { get; }
    int RootRecords { get; }
    void Initialize(NativeLocalCutoutCoroutineTracker owner);
    void RetainEnumerator(int slot, CutoutCoroutineRole role, long enumerator);
    CutoutCoroutineObservation ReadEnumerator(int slot, CutoutCoroutineRole role);
    void RetainCoroutine(int slot, long coroutine);
    ExactHookBroker.ManagedBoundaryActivity ReadActivity();
    void ReleaseEndedRoots(int slot, long enumerator, long generation);
}
internal interface ICutoutCoroutineAuthority
{
    bool CoroutineCancellationPending => false;
    bool Unknown { get; }
    long Host { get; }
    NativeCutoutEventObservation Source { get; }
    void VerifyBeforeCapture();
    void VerifyObservation();
    bool TryGetDispatch(out long generation);
    bool TryConfirmSourceIdle();
    void ObserveFaultFromAnyThread();
}
internal sealed class NativeLocalCutoutCoroutineException : InvalidOperationException
{
    internal NativeLocalCutoutCoroutineTracker Responsibility { get; }
    internal NativeLocalCutoutCoroutineException(NativeLocalCutoutCoroutineTracker owner, Exception error)
        : base("Cutout coroutine responsibility is unconfirmed and remains retained.", error) => Responsibility = owner;
}

/// <summary>Observes two real StartEvents coroutine sources. Natural enum completion
/// is local evidence only: UnityEvent listeners and future producers remain owned.</summary>
internal sealed partial class NativeLocalCutoutCoroutineTracker
{
    internal const int MaximumRecords = 64;
    private static readonly object Gate = new();
    private static readonly HashSet<NativeLocalCutoutCoroutineTracker> Owners = new();
    private readonly IMainThreadDispatcher _main;
    private readonly ICutoutCoroutineBackend _backend;
    private readonly ICutoutCoroutineAuthority _authority;
    private readonly LocalRunLaunchCommitState? _launch;
    private readonly long _host, _delayCaller, _eventCaller;
    private readonly Record[] _records;
    private NativeEngineCoroutineObserverRetirementAuthority? _engineRetirement;
    private int _fault, _created, _completed, _live, _retired;
    private bool _ready, _recycling;
    private string? _failure;
    private sealed class Record
    {
        internal readonly int Slot;
        internal readonly Context Start, Move;
        internal long Enumerator, Generation, Dispatch, ObservedHandle;
        internal CutoutCoroutineRole Role;
        internal bool Reserved, Published, StartReturned, Terminal, Retiring, FreeAttempted, HandleConfirmed;
        internal CutoutCoroutineObservation? Current;
        internal NativeEngineCoroutineObserverRetirementAuthority.Permit? Retirement;
        internal Record(NativeLocalCutoutCoroutineTracker owner, int slot)
        { Slot = slot; Start = new(owner, slot, false); Move = new(owner, slot, true); }
        internal void Reserve(CutoutCoroutineRole role, long dispatch, long enumerator)
        {
            if (Reserved || Generation == long.MaxValue) throw new InvalidOperationException("Cutout coroutine slot generation exhausted.");
            ++Generation; Role = role; Dispatch = dispatch; Interlocked.Exchange(ref Enumerator, enumerator);
            ObservedHandle = 0;
            Current = null; StartReturned = Terminal = Retiring = FreeAttempted = Published = HandleConfirmed = false; Reserved = true;
            Retirement = null;
        }
    }
    internal sealed class Context : IDisposable
    {
        private readonly NativeLocalCutoutCoroutineTracker _owner;
        internal readonly int Slot;
        internal readonly bool IsMove;
        internal bool Active, After;
        internal long Generation;
        internal Context(NativeLocalCutoutCoroutineTracker owner, int slot, bool move) => (_owner, Slot, IsMove) = (owner, slot, move);
        internal void Begin(long generation)
        { if (Active) throw new InvalidOperationException("Cutout coroutine context reentered."); Active = true; After = false; Generation = generation; }
        public void Dispose()
        {
            _owner._main.VerifyAccess(); var record = _owner._records[Slot];
            if (!Active || !After || !record.Reserved || record.Generation != Generation)
                _owner.Fault("Cutout coroutine original or context cleanup did not pair with its exact generation.");
            Active = false;
        }
    }
    private NativeLocalCutoutCoroutineTracker(IMainThreadDispatcher main, ICutoutCoroutineBackend backend,
        ICutoutCoroutineAuthority authority, LocalRunLaunchCommitState? launch, long module)
    {
        (_main, _backend, _authority, _launch, _host) = (main, backend, authority, launch, authority.Host);
        _delayCaller = checked(module + 0x38A60C); _eventCaller = checked(module + 0x38A6B2);
        _records = Enumerable.Range(0, MaximumRecords).Select(index => new Record(this, index)).ToArray();
    }
    internal static NativeLocalCutoutCoroutineTracker Capture(NativeRuntime api, IMainThreadDispatcher main,
        AdapterBuildProfile profile, IntPtr module, ExactHookBroker broker, LocalRunLaunchCommitState launch,
        NativeLocalCutoutEventSource source)
        => CaptureCore(main, new NativeLocalCutoutCoroutineBackend(api, main, profile, module, broker, source), new Authority(launch, source), launch, module.ToInt64());
    internal static NativeLocalCutoutCoroutineTracker CaptureBackend(IMainThreadDispatcher main, ICutoutCoroutineBackend backend,
        ICutoutCoroutineAuthority authority, long module)
    {
        if (backend is NativeLocalCutoutCoroutineBackend) throw new InvalidOperationException("A controlled authority cannot adopt a production coroutine backend.");
        return CaptureCore(main, backend, authority, null, module);
    }
    private static NativeLocalCutoutCoroutineTracker CaptureCore(IMainThreadDispatcher main, ICutoutCoroutineBackend backend,
        ICutoutCoroutineAuthority authority, LocalRunLaunchCommitState? launch, long module)
    {
        ArgumentNullException.ThrowIfNull(main); main.VerifyAccess(); ArgumentNullException.ThrowIfNull(backend); ArgumentNullException.ThrowIfNull(authority);
        authority.VerifyBeforeCapture();
        if (authority.Host == 0 || module <= 0) throw new InvalidOperationException("Actual EventTrigger host and pinned module required.");
        var owner = new NativeLocalCutoutCoroutineTracker(main, backend, authority, launch, module);
        lock (Gate)
        {
            if (Owners.Count >= 32 || launch is not null && Owners.Any(item => ReferenceEquals(item._launch, launch)))
                throw new InvalidOperationException("Cutout coroutine observation owner capacity exhausted or already captured.");
            Owners.Add(owner);
        }
        try
        {
            backend.Initialize(owner); authority.VerifyBeforeCapture(); var activity = backend.ReadActivity();
            if (owner.Unknown || activity is not { Known: true, InFlight: 0 }) throw new InvalidOperationException("Cutout coroutine installation has unknown or active native entries.");
            owner._ready = true; return owner;
        }
        catch (Exception error) { owner.Fault(error.Message); throw new NativeLocalCutoutCoroutineException(owner, error); }
    }
    internal long HostIdentity => _host;
    internal bool ObservationUnknown => Volatile.Read(ref _fault) != 0;
    internal bool ObservationAuthorityUnknown => ObservationUnknown || _authority.Unknown;
    internal bool Unknown => ObservationUnknown || _backend.CleanupUnknown || _authority.Unknown || _engineRetirement?.Unknown == true;
    internal bool CoroutineCancellationPending => _authority.CoroutineCancellationPending;
    internal void RequireCancellationRetirement()
    { _main.VerifyAccess(); if (CoroutineCancellationPending) throw new InvalidOperationException("Cancellation requests retain Cutout coroutine roots."); }
    internal bool IsFor(LocalRunLaunchCommitState launch) { _main.VerifyAccess(); return _launch is not null && ReferenceEquals(_launch, launch) && !Unknown; }
    internal void AttachEngineRetirementAuthority(NativeEngineCoroutineRuntimeCoordinator owner,
        NativeEngineCoroutineObserverRetirementAuthority authority)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(authority);
        if (_launch is null || _engineRetirement is not null || !IsFor(_launch)
            || !authority.IsForProduction(owner, _launch, this, _host))
            throw new InvalidOperationException("Cutout retirement binds its exact production engine once before activation.");
        _engineRetirement = authority;
    }
    internal void AttachControlledEngineRetirementAuthority(NativeEngineCoroutineObserverRetirementAuthority authority)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(authority);
        if (_launch is not null || _backend is NativeLocalCutoutCoroutineBackend || _engineRetirement is not null
            || !authority.IsForControlled(this, _host))
            throw new InvalidOperationException("Controlled retirement cannot bind production cutout observations.");
        _engineRetirement = authority;
    }
    internal NativeLocalCutoutCoroutineSnapshot Read()
    { _main.VerifyAccess(); return new(_created, _completed, _live, _retired, _backend.RootRecords, Unknown, Volatile.Read(ref _failure)); }
    internal IReadOnlyList<NativeCoroutineCancellationIdentity> ReadCancellationIdentities()
    {
        _main.VerifyAccess();
        return Array.AsReadOnly(_records.Where(record => record.Reserved).Select(CancellationIdentity).ToArray());
    }
    internal bool TryMatchCancellationReference(NativeCoroutineCancellationReferenceKind kind, long receiver, long argument,
        out NativeCoroutineCancellationIdentity identity)
    {
        _main.VerifyAccess(); identity = default;
        if (!_ready || _recycling || Unknown) return false;
        var found = false;
        foreach (var record in _records) {
            var candidate = CancellationIdentity(record);
            if (!candidate.MatchesReference(kind, receiver, argument)) continue;
            if (found) { identity = default; return false; }
            identity = candidate; found = true;
        }
        if (Unknown) { identity = default; return false; }
        return found;
    }
    internal bool IsCurrentCancellationIdentity(NativeCoroutineCancellationIdentity identity)
    {
        _main.VerifyAccess();
        return ReferenceEquals(identity.Owner, this) && identity.Slot >= 0 && identity.Slot < _records.Length
            && CancellationIdentity(_records[identity.Slot]).SameRecord(identity) && !Unknown;
    }
    private NativeCoroutineCancellationIdentity CancellationIdentity(Record record)
        => new(this, _host, record.Slot, record.Generation, Interlocked.Read(ref record.Enumerator), Interlocked.Read(ref record.ObservedHandle),
            record.HandleConfirmed, record.StartReturned, record.Reserved && record.Published, record.Retiring,
            record.Terminal, _ready && !Unknown);
    private void Fault(string failure)
    { Interlocked.CompareExchange(ref _failure, failure.Length > 768 ? failure[..768] : failure, null); Interlocked.Exchange(ref _fault, 1); _authority.ObserveFaultFromAnyThread(); }
    internal void ObserveFault(NativeCoroutineFrame frame, string failure)
    {
        var self = frame.Self.ToInt64();
        if (self == _host) { Fault(failure); return; }
        if (self == 0) return;
        foreach (var record in _records)
            if (Volatile.Read(ref record.Reserved) && Interlocked.Read(ref record.Enumerator) == self) { Fault(failure); return; }
    }
    internal Context? CaptureStart(NativeCoroutineFrame frame)
    {
        _main.VerifyAccess(); if (frame.Self.ToInt64() != _host) return null;
        try
        {
            Guard(); _authority.VerifyObservation();
            if (_recycling || !frame.IsValid || frame.Kind != NativeCoroutineKind.Start || frame.Enumerator == IntPtr.Zero
                || !_authority.TryGetDispatch(out var dispatch) || dispatch <= 0)
                throw new InvalidOperationException("Cutout coroutine Start has no exact physical source window.");
            var caller = frame.Caller.ToInt64();
            var role = caller == _delayCaller ? CutoutCoroutineRole.ResetDelay : caller == _eventCaller ? CutoutCoroutineRole.DelayedEvent
                : throw new InvalidOperationException("Untracked IEnumerator Start targets the actual EventTrigger host.");
            var identity = frame.Enumerator.ToInt64();
            if (_records.Any(record => record.Reserved && record.Enumerator == identity) || _created == int.MaxValue)
                throw new InvalidOperationException("Cutout coroutine enum was reused or its sequence exhausted.");
            var record = _records.FirstOrDefault(item => !item.Reserved) ?? throw new InvalidOperationException("Cutout coroutine record capacity exhausted.");
            record.Reserve(role, dispatch, identity); ++_created; ++_live;
            _backend.RetainEnumerator(record.Slot, role, identity); Guard();
            var value = _backend.ReadEnumerator(record.Slot, role); RequireFields(record, value, true);
            if (value.State != 0 || role == CutoutCoroutineRole.ResetDelay && value.Provider != 0)
                throw new InvalidOperationException("Cutout coroutine output is not the actual fresh state0 iterator.");
            Guard(); record.Current = value; record.Start.Begin(record.Generation); Volatile.Write(ref record.Published, true); return record.Start;
        }
        catch (Exception error) { Fault(error.Message); throw; }
    }
    internal void CompleteStart(Context context, NativeCoroutineFrame frame, nuint coroutine)
    {
        _main.VerifyAccess();
        try
        {
            var record = RequireContext(context, false, frame.Enumerator.ToInt64());
            Interlocked.Exchange(ref record.ObservedHandle, unchecked((long)coroutine));
            Guard();
            if (frame.Self.ToInt64() != _host || !_authority.TryGetDispatch(out var dispatch) || dispatch != record.Dispatch)
                throw new InvalidOperationException("Cutout Start returned outside its exact source generation.");
            RequireCurrent(record, _backend.ReadEnumerator(record.Slot, record.Role));
            if (coroutine == 0 && !record.Terminal) throw new InvalidOperationException("Pending Cutout iterator has no real Coroutine output.");
            if (coroutine != 0) _backend.RetainCoroutine(record.Slot, unchecked((long)coroutine));
            Guard(); record.HandleConfirmed = coroutine != 0; record.StartReturned = true; context.After = true;
        }
        catch (Exception error) { Fault(error.Message); throw; }
    }
    internal Context? CaptureMove(CutoutCoroutineRole role, NativeCoroutineFrame frame)
    {
        _main.VerifyAccess(); var self = frame.Self.ToInt64();
        var record = _records.FirstOrDefault(item => item.Reserved && item.Enumerator == self && self != 0);
        if (record is null) return null;
        try
        {
            Guard(); _authority.VerifyObservation();
            if (_recycling || !record.Published || record.Retiring || record.Terminal || record.Role != role
                || !frame.IsValid || frame.Kind != NativeCoroutineKind.MoveNext)
                throw new InvalidOperationException("Owned Cutout iterator reentered, changed role or advanced after completion/withdrawal.");
            RequireCurrent(record, _backend.ReadEnumerator(record.Slot, role)); Guard();
            record.Move.Begin(record.Generation); return record.Move;
        }
        catch (Exception error) { Fault(error.Message); throw; }
    }
    internal void CompleteMove(Context context, NativeCoroutineFrame frame, nuint result)
    {
        _main.VerifyAccess();
        try
        {
            Guard(); var record = RequireContext(context, true, frame.Self.ToInt64()); var previous = record.Current!;
            var value = _backend.ReadEnumerator(record.Slot, record.Role); RequireFields(record, value, false);
            if (result > byte.MaxValue) throw new InvalidOperationException("Cutout MoveNext result exceeds raw byte ABI.");
            if (record.Role == CutoutCoroutineRole.DelayedEvent)
            {
                if (result == 0 ? previous.State != 1 || value.State != -1 : previous.State != 0 || value.State != 1)
                    throw new InvalidOperationException("Delayed event did not follow its observed yield/Invoke/natural false path.");
            }
            else
            {
                var seconds = BitConverter.Int32BitsToSingle(value.SecondsBits);
                if (result == 0)
                {
                    if (value.State != -1 || value.Provider != 0 || previous.State == 0 && seconds > 0
                        || previous.State == 1 && seconds > 0 || previous.State is not (0 or 1 or 2))
                        throw new InvalidOperationException("Reset Delay did not naturally complete its actual known branch.");
                }
                else if (previous.State == 0 ? value.State is not (1 or 2) || seconds <= 0
                    : previous.State != 1 || value.State != 1 || seconds <= 0)
                    throw new InvalidOperationException("Reset Delay returned true outside the provider/wait yield paths.");
                if (result != 0 && (value.State == 1 ? value.Provider == 0 : value.Provider != 0)
                    || previous.State is 0 or 2 && previous.SecondsBits != value.SecondsBits
                    || previous.State == 1 && result != 0 && previous.Provider != value.Provider)
                    throw new InvalidOperationException("Reset Delay seconds/provider changed outside the observed provider branch.");
            }
            Guard(); record.Current = value;
            if (result == 0)
            {
                if (_completed == int.MaxValue) throw new InvalidOperationException("Cutout completion count exhausted.");
                record.Terminal = true; ++_completed;
            }
            context.After = true;
        }
        catch (Exception error) { Fault(error.Message); throw; }
    }
    private Record RequireContext(Context context, bool move, long enumerator)
    {
        var record = _records[context.Slot];
        if (!context.Active || context.After || context.IsMove != move || record.Generation != context.Generation
            || !record.Reserved || record.Retiring || !record.Published || record.Enumerator != enumerator
            || !ReferenceEquals(context, move ? record.Move : record.Start)) throw new InvalidOperationException("Cutout callback has no exact paired context generation.");
        return record;
    }
    private void RequireFields(Record record, CutoutCoroutineObservation value, bool initial)
    {
        if (value.Enumerator != record.Enumerator) throw new InvalidOperationException("Rooted Cutout iterator identity changed.");
        var source = _authority.Source;
        if (source.EventTrigger != _host) throw new InvalidOperationException("The actual Cutout source host changed.");
        if (record.Role == CutoutCoroutineRole.DelayedEvent)
        {
            if (!source.Triggers.Any(row => row.Trigger == value.Trigger && row.Events == value.Events && row.OffsetBits == value.OffsetBits)
                || (double)BitConverter.Int32BitsToSingle(value.OffsetBits) <= 1e-6 || value.Action != 0 || value.Provider != 0
                || record.Current is { } current && (current.Trigger != value.Trigger || current.Events != value.Events || current.OffsetBits != value.OffsetBits))
                throw new InvalidOperationException("Delayed event differs from its real captured Trigger/events/offset.");
        }
        else if (value.Trigger != 0 || value.Events != 0 || value.Action == 0 || !float.IsFinite(BitConverter.Int32BitsToSingle(value.SecondsBits))
            || initial && (source.Triggers.Count == 0 || value.SecondsBits != BitConverter.SingleToInt32Bits(source.Triggers.Max(row => BitConverter.Int32BitsToSingle(row.OffsetBits))))
            || record.Current is { } current && value.Action != current.Action)
            throw new InvalidOperationException("Reset Delay differs from its actual reset Action and finite source Max offset.");
    }
    private void RequireCurrent(Record record, CutoutCoroutineObservation value)
    { RequireFields(record, value, false); Guard(); if (record.Current != value) throw new InvalidOperationException("Cutout iterator fields changed outside a paired native MoveNext."); }
    internal bool TryConfirmNativeIdle()
    {
        Guard(); if (CoroutineCancellationPending) return false; _authority.VerifyObservation(); if (CoroutineCancellationPending) return false;
        if (!_authority.TryConfirmSourceIdle()) { Guard(); return false; }
        Guard(); if (CoroutineCancellationPending) return false; var value = _backend.ReadActivity(); Guard(); if (CoroutineCancellationPending) return false;
        if (!value.Known || value.InFlight < 0) { Fault("Cutout coroutine native activity became unknown."); throw new InvalidOperationException(_failure); }
        return value.InFlight == 0;
    }
    internal bool TryConfirmObservedEnded()
    {
        Guard(); if (CoroutineCancellationPending) return false;
        try
        {
            if (_recycling || !TryConfirmNativeIdle() || _records.Any(record => record.Start.Active || record.Move.Active)) return false;
            foreach (var record in _records)
            {
                if (!record.Reserved) continue;
                if (!record.StartReturned || !record.Terminal || record.Retiring) return false;
                RequireCurrent(record, _backend.ReadEnumerator(record.Slot, record.Role));
                if (CoroutineCancellationPending) return false;
            }
            Guard(); return TryConfirmNativeIdle();
        }
        catch (Exception error) { Fault(error.Message); throw new NativeLocalCutoutCoroutineException(this, error); }
    }
    internal bool IsRetiringForCleanup(int slot, long enumerator, long generation)
    {
        _main.VerifyAccess(); if (slot < 0 || slot >= _records.Length) return false;
        var record = _records[slot]; return _recycling && !Unknown && !CoroutineCancellationPending && record.Reserved && record.Retiring && !record.Published
            && record.FreeAttempted && record.Enumerator == enumerator && record.Generation == generation;
    }
    internal void RequireEngineRetirementFree(int slot, long enumerator, long generation)
    {
        _main.VerifyAccess(); RequireCancellationRetirement();
        if (_engineRetirement is null)
        {
            if (_launch is not null) throw new InvalidOperationException("Production root cleanup has no engine retirement authority.");
            return;
        }
        if (slot < 0 || slot >= _records.Length) throw new ArgumentOutOfRangeException(nameof(slot));
        var record = _records[slot];
        if (ObservationAuthorityUnknown || !_recycling || !record.Reserved || !record.Retiring || record.Published
            || !record.FreeAttempted || record.Enumerator != enumerator || record.Generation != generation || record.Retirement is null)
            throw new InvalidOperationException("An independent Cutout root lost its exact retirement scope.");
        record.Retirement.RequireFreeInProgress();
    }
    internal int RecycleNaturallyEnded()
    {
        Guard(); if (CoroutineCancellationPending) return 0; if (_recycling) throw new InvalidOperationException("Cutout coroutine cleanup cannot reenter.");
        if (_launch is not null && _engineRetirement is null)
            throw new InvalidOperationException("Production cutout retirement requires the exact engine authority.");
        if (_records.Any(record => record.Start.Active || record.Move.Active) || !TryConfirmNativeIdle()) return 0;
        _recycling = true; var released = 0;
        try
        {
            foreach (var record in _records)
            {
                if (!record.Reserved || !record.StartReturned || !record.Terminal) continue;
                if (!TryConfirmNativeIdle()) break;
                RequireCurrent(record, _backend.ReadEnumerator(record.Slot, record.Role)); Guard(); if (CoroutineCancellationPending) break;
                if (!record.Retiring)
                {
                    if (_engineRetirement is not null
                        && !_engineRetirement.TryBegin(CancellationIdentity(record), out record.Retirement)) continue;
                    record.Retiring = true; Volatile.Write(ref record.Published, false);
                    record.Retirement?.MarkWithdrawn(CancellationIdentity(record));
                }
                if (!TryConfirmNativeIdle()) { RequireCancellationRetirement(); break; }
                if (record.Retirement is not null && !record.Retirement.TryConfirmWithdrawnAndDrained()) break;
                if (record.FreeAttempted || _retired == int.MaxValue) throw new InvalidOperationException("Unknown root free cannot retry or overflow retirement.");
                if (record.Retirement is not null && !record.Retirement.BeginFree()) break;
                RequireCancellationRetirement(); record.FreeAttempted = true; _backend.ReleaseEndedRoots(record.Slot, record.Enumerator, record.Generation); RequireCancellationRetirement();
                record.Retirement?.CompleteFree();
                if (!TryConfirmNativeIdle()) throw new InvalidOperationException("Cutout calls changed during completed-root cleanup.");
                Interlocked.Exchange(ref record.Enumerator, 0); Volatile.Write(ref record.Reserved, false); record.Retiring = false;
                --_live; ++_retired; ++released;
            }
            return released;
        }
        catch (Exception error)
        {
            _engineRetirement?.ObserveFailure(this, error.Message); Fault(error.Message);
            throw new NativeLocalCutoutCoroutineException(this, error);
        }
        finally { _recycling = false; }
    }
    private void Guard() { _main.VerifyAccess(); if (!_ready || Unknown) throw new InvalidOperationException("Cutout coroutine owner is unavailable or unknown."); }
    private sealed class Authority : ICutoutCoroutineAuthority
    {
        private readonly LocalRunLaunchCommitState _launch;
        private readonly NativeLocalCutoutEventSource _source;
        internal Authority(LocalRunLaunchCommitState launch, NativeLocalCutoutEventSource source)
        { ArgumentNullException.ThrowIfNull(launch); ArgumentNullException.ThrowIfNull(source); _launch = launch; _source = source; }
        public NativeCutoutEventObservation Source => _source.Read().Source ?? throw new InvalidOperationException("No actual Cutout event closure.");
        public bool Unknown => !_source.IsFor(_launch) || _launch.Read().Phase == LocalRunLaunchCommitPhase.Unknown;
        public bool CoroutineCancellationPending => _launch.CoroutineCancellationPending;
        public long Host => Source.EventTrigger;
        public void VerifyBeforeCapture()
        {
            _launch.VerifyPendingOwnership(_launch.Registry); var value = _launch.Read();
            if (!_source.IsFor(_launch) || value.ActivationAttempted || value.Phase != LocalRunLaunchCommitPhase.ControllerBound || value.Controller != Source.Controller)
                throw new InvalidOperationException("Cutout coroutine observations must capture this real event source before activation.");
        }
        public void VerifyObservation()
        {
            var state = _launch.Read(); var current = _launch.Registry.Read(_launch.Session);
            if (!_source.IsFor(_launch) || !state.ActivationAttempted || state.Phase == LocalRunLaunchCommitPhase.Unknown
                || current.ControllerIdentity != Source.Controller || current.ProtectionRetired || current.CleanupAttempted
                || current.Phase is LocalRunSessionPhase.Closed or LocalRunSessionPhase.CleanupUnknown
                || !ReferenceEquals(_launch.Registry.FindProtectedSession(LocalBoundaryKind.ControllerLevelOver, current.ControllerIdentity), _launch.Session))
                throw new InvalidOperationException("Cutout coroutine observations require the same exact activated protected run.");
        }
        public bool TryGetDispatch(out long generation) => _source.TryGetCurrentDispatch(out generation);
        public bool TryConfirmSourceIdle() => _source.TryConfirmNativeIdle();
        public void ObserveFaultFromAnyThread() => _launch.ObserveNativeFaultFromAnyThread("Cutout coroutine observation is unknown.");
    }
}
