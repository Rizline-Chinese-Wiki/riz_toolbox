using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.AudioContent;

internal enum TerminalCoroutineRole { Cutout, Fade }
internal readonly record struct TerminalCoroutineObservation(long Enumerator, long Controller, int State);
internal sealed record TerminalCoroutineSnapshot(int Created, int NaturallyEnded, bool ResultAttempted, bool ResultReturned,
    bool CutoutNamedReturned, bool Unknown, bool Closed, int Roots, string? Failure,
    int LiveRecords = 0, int RetiredRecords = 0, bool CutoutNaturallyEnded = false);
internal interface ITerminalCoroutineBackend
{
    bool CleanupUnknown { get; }
    int RootRecords { get; }
    void Initialize(NativeLocalTerminalCoroutineTracker owner);
    string ReadMethodName(long name);
    void RetainEnumerator(int slot, TerminalCoroutineRole role, long enumerator);
    TerminalCoroutineObservation ReadEnumerator(int slot, TerminalCoroutineRole role);
    void RetainCoroutine(int slot, long coroutine);
    ExactHookBroker.ManagedBoundaryActivity ReadActivity();
    void DetachAndReleaseUnused();
    void ReleaseEndedRoots(int slot, long enumerator, long generation);
}
internal interface ITerminalCoroutineAuthority
{
    bool CoroutineCancellationPending => false;
    // Pure scalar authority only; callable by native worker fault paths. Never
    // query a registry, dispatcher, Unity object or root from this property.
    bool Unknown => false;
    long Controller { get; }
    void VerifyBeforeCapture();
    void VerifyObservation();
    void VerifyResultClaim();
    void ObserveFaultFromAnyThread();
}
internal sealed class NativeLocalTerminalCoroutineException : InvalidOperationException
{
    internal NativeLocalTerminalCoroutineTracker Responsibility { get; }
    internal NativeLocalTerminalCoroutineException(NativeLocalTerminalCoroutineTracker owner, Exception error)
        : base("Terminal coroutine observations are unconfirmed; native responsibility remains retained.", error) => Responsibility = owner;
}

/// <summary>Observes actual Cutout/Fade factories, named/enum starts and MoveNext.
/// A zero current activity count does not revoke future producers. This owner
/// never signs a whole-run consumers-ended receipt or frees running records.</summary>
internal sealed partial class NativeLocalTerminalCoroutineTracker
{
    internal const int MaximumRecords = 16;
    private static readonly object Gate = new();
    private static readonly HashSet<NativeLocalTerminalCoroutineTracker> Owners = new();
    private readonly IMainThreadDispatcher _main;
    private readonly ITerminalCoroutineBackend _backend;
    private readonly ITerminalCoroutineAuthority _authority;
    private readonly LocalRunLaunchCommitState? _launch;
    private readonly long _controller, _cutoutCaller;
    private readonly Record[] _records;
    private NativeEngineCoroutineObserverRetirementAuthority? _engineRetirement;
    private readonly Context[] _named;
    private readonly ResultWindow _result;
    private Context? _currentNamed;
    private int _count, _created, _completed, _retired, _fault;
    private bool _ready, _closed, _recycling, _resultAttempted, _resultReturned, _cutoutNamedAttempted, _cutoutNamedReturned, _cutoutNaturallyEnded;
    private string? _failure;
    internal sealed class Record
    {
        internal readonly int Slot;
        internal TerminalCoroutineRole Role;
        internal long Enumerator, ObservedHandle;
        internal long Generation;
        internal bool Reserved, Published, Retiring, FreeAttempted, RequiredCutout;
        internal bool FactoryReturned, StartAttempted, StartReturned, Terminal, HandleConfirmed;
        internal int ExpectedState;
        internal readonly Context Factory, Start, Move;
        internal NativeEngineCoroutineObserverRetirementAuthority.Permit? Retirement;
        internal Record(NativeLocalTerminalCoroutineTracker owner, int slot)
        { Slot = slot; Factory = new(owner, 1, this); Start = new(owner, 2, this); Move = new(owner, 3, this); }
        internal void Reserve(TerminalCoroutineRole role)
        {
            if (Reserved || Generation == long.MaxValue) throw new InvalidOperationException("Terminal record slot is occupied or its generation is exhausted.");
            ++Generation; Role = role; Reserved = true; Published = Retiring = FreeAttempted = RequiredCutout = false;
            Retirement = null;
            Enumerator = ObservedHandle = 0; FactoryReturned = StartAttempted = StartReturned = Terminal = HandleConfirmed = false; ExpectedState = 0;
        }
    }
    internal sealed class Context : IDisposable
    {
        private readonly NativeLocalTerminalCoroutineTracker _owner;
        internal readonly int Kind; // named 0, factory 1, enum start 2, move 3
        private readonly Record? _fixed;
        internal Record? _record;
        internal TerminalCoroutineRole Role;
        internal Context? Parent;
        internal bool Active, After, Mask;
        internal long Generation;
        internal Context(NativeLocalTerminalCoroutineTracker owner, int kind, Record? record = null)
            => (_owner, Kind, _fixed) = (owner, kind, record);
        internal void Begin(TerminalCoroutineRole role)
        { if (Active) throw new InvalidOperationException("An owned terminal coroutine physically reentered its active record."); Role = role; _record = _fixed; Generation = _fixed?.Generation ?? 0; After = false; Mask = false; Active = true; }
        public void Dispose()
        {
            _owner._main.VerifyAccess();
            if (!Active) { _owner.Fault("Terminal context release has no paired entry."); return; }
            if (_record is { } record && (!record.Reserved || record.Generation != Generation)) _owner.Fault("Terminal context outlived its exact slot generation.");
            if (Kind == 0)
            {
                if (!ReferenceEquals(_owner._currentNamed, this)) _owner.Fault("Named coroutine frames exited out of physical order.");
                _owner._currentNamed = Parent; Parent = null;
            }
            Active = false;
            if (!After) _owner.Fault("Terminal coroutine original or observation did not return normally.");
        }
    }
    internal sealed class ResultWindow : IDisposable
    {
        private readonly NativeLocalTerminalCoroutineTracker _owner;
        internal bool Active, Completed;
        internal ResultWindow(NativeLocalTerminalCoroutineTracker owner) => _owner = owner;
        internal void Complete()
        {
            _owner._main.VerifyAccess(); _owner.Guard();
            if (!Active || Completed || !_owner._cutoutNamedReturned)
                throw new InvalidOperationException("Actual ControllerOver returned without its paired named Cutout factory/start.");
            Completed = true; _owner._resultReturned = true;
        }
        public void Dispose()
        { _owner._main.VerifyAccess(); if (!Active || !Completed) _owner.Fault("Actual ControllerOver invocation did not complete its tracked Cutout publication."); Active = false; }
    }
    private NativeLocalTerminalCoroutineTracker(IMainThreadDispatcher main, ITerminalCoroutineBackend backend,
        ITerminalCoroutineAuthority authority, LocalRunLaunchCommitState? launch, long module)
    {
        (_main, _backend, _authority, _launch, _controller, _cutoutCaller) = (main, backend, authority, launch, authority.Controller, checked(module + 0x3BEAAA));
        _records = Enumerable.Range(0, MaximumRecords).Select(index => new Record(this, index)).ToArray();
        _named = Enumerable.Range(0, 8).Select(_ => new Context(this, 0)).ToArray(); _result = new(this);
    }
    internal static NativeLocalTerminalCoroutineTracker Capture(NativeRuntime api, IMainThreadDispatcher main,
        AdapterBuildProfile profile, IntPtr module, ExactHookBroker broker, LocalRunLaunchCommitState launch)
        => CaptureCore(main, new NativeLocalTerminalCoroutineBackend(api, main, profile, module, broker), new Authority(launch), launch, module.ToInt64());
    internal static NativeLocalTerminalCoroutineTracker CaptureBackend(IMainThreadDispatcher main, ITerminalCoroutineBackend backend,
        ITerminalCoroutineAuthority authority, long module)
    {
        if (backend is NativeLocalTerminalCoroutineBackend) throw new InvalidOperationException("Controlled terminal capture cannot adopt a production backend.");
        return CaptureCore(main, backend, authority, null, module);
    }
    private static NativeLocalTerminalCoroutineTracker CaptureCore(IMainThreadDispatcher main, ITerminalCoroutineBackend backend,
        ITerminalCoroutineAuthority authority, LocalRunLaunchCommitState? launch, long module)
    {
        ArgumentNullException.ThrowIfNull(main); main.VerifyAccess(); ArgumentNullException.ThrowIfNull(backend); ArgumentNullException.ThrowIfNull(authority);
        authority.VerifyBeforeCapture(); if (authority.Controller == 0 || module <= 0) throw new InvalidOperationException("Actual terminal controller/module required.");
        var owner = new NativeLocalTerminalCoroutineTracker(main, backend, authority, launch, module);
        lock (Gate) { if (Owners.Count >= 32) throw new InvalidOperationException("Terminal coroutine owner capacity exhausted."); Owners.Add(owner); }
        try { backend.Initialize(owner); authority.VerifyBeforeCapture(); var activity = backend.ReadActivity(); if (owner.Unknown || activity is not { Known: true, InFlight: 0 }) throw new InvalidOperationException("Pre-capture terminal activity has not drained."); owner._ready = true; return owner; }
        catch (Exception error) { owner.Fault(error.Message); throw new NativeLocalTerminalCoroutineException(owner, error); }
    }
    internal bool IsFor(LocalRunLaunchCommitState launch) => ReferenceEquals(_launch, launch) && _launch is not null && !Unknown && !_closed;
    internal void AttachEngineRetirementAuthority(NativeEngineCoroutineRuntimeCoordinator owner,
        NativeEngineCoroutineObserverRetirementAuthority authority)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(authority);
        if (_launch is null || _engineRetirement is not null || !IsFor(_launch)
            || !authority.IsForProduction(owner, _launch, this, _controller))
            throw new InvalidOperationException("Terminal retirement binds its exact production engine once before activation.");
        _engineRetirement = authority;
    }
    internal void AttachControlledEngineRetirementAuthority(NativeEngineCoroutineObserverRetirementAuthority authority)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(authority);
        if (_launch is not null || _backend is NativeLocalTerminalCoroutineBackend || _engineRetirement is not null
            || !authority.IsForControlled(this, _controller))
            throw new InvalidOperationException("Controlled retirement cannot bind production terminal observations.");
        _engineRetirement = authority;
    }
    internal bool IsFor(LocalRunSessionRegistry registry, LocalRunSessionRegistry.SessionHandle session)
        => _launch is not null && ReferenceEquals(_launch.Registry, registry) && ReferenceEquals(_launch.Session, session) && !Unknown && !_closed;
    internal long ControllerIdentity => _controller;
    internal bool IsControlled => _launch is null;
    internal bool ObservationUnknown => Volatile.Read(ref _fault) != 0;
    internal bool ObservationAuthorityUnknown => ObservationUnknown || _authority.Unknown;
    internal bool Unknown => ObservationAuthorityUnknown || _backend.CleanupUnknown || _engineRetirement?.Unknown == true;
    internal bool CoroutineCancellationPending => _authority.CoroutineCancellationPending;
    internal void RequireCancellationRetirement()
    { _main.VerifyAccess(); if (CoroutineCancellationPending) throw new InvalidOperationException("Cancellation requests retain terminal coroutine roots."); }
    internal TerminalCoroutineSnapshot Read()
    { _main.VerifyAccess(); return new(_created, _completed, _resultAttempted, _resultReturned, _cutoutNamedReturned, Unknown, _closed, _backend.RootRecords, Volatile.Read(ref _failure), _count, _retired, _cutoutNaturallyEnded); }
    internal IReadOnlyList<NativeCoroutineCancellationIdentity> ReadCancellationIdentities()
    {
        _main.VerifyAccess();
        return Array.AsReadOnly(_records.Where(record => record.Reserved).Select(CancellationIdentity).ToArray());
    }
    internal bool TryMatchCancellationReference(NativeCoroutineCancellationReferenceKind kind, long receiver, long argument,
        out NativeCoroutineCancellationIdentity identity)
    {
        _main.VerifyAccess(); identity = default;
        if (!_ready || _closed || _recycling || Unknown) return false;
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
        => new(this, _controller, record.Slot, record.Generation, Interlocked.Read(ref record.Enumerator), Interlocked.Read(ref record.ObservedHandle),
            record.HandleConfirmed, record.StartReturned, record.Reserved && record.Published, record.Retiring || _closed,
            record.Terminal, _ready && !_closed && !Unknown);
    private void Fault(string reason)
    { Interlocked.CompareExchange(ref _failure, reason.Length > 768 ? reason[..768] : reason, null); Interlocked.Exchange(ref _fault, 1); _authority.ObserveFaultFromAnyThread(); }
    internal void ObserveFault(NativeCoroutineFrame frame, string reason)
    {
        var self = frame.Self.ToInt64();
        if (self == _controller) { Fault(reason); return; }
        if (self == 0) return;
        for (var index = 0; index < _records.Length; ++index)
            if (Volatile.Read(ref _records[index].Reserved) && Interlocked.Read(ref _records[index].Enumerator) == self) { Fault(reason); return; }
    }
    internal ResultWindow BeginControllerOver()
    {
        Guard(); _authority.VerifyResultClaim();
        if (_resultAttempted || _result.Active) throw new InvalidOperationException("Actual ControllerOver observation is once-only.");
        _resultAttempted = true; _result.Active = true; return _result;
    }
    internal Context? CaptureNamed(NativeCoroutineFrame frame)
    {
        _main.VerifyAccess(); if (_closed) return null;
        if (frame.Self.ToInt64() != _controller)
        {
            // A foreign physical child masks the outer named factory association.
            // Only scalar identity/frame state is touched; its name is never read.
            if (_currentNamed is null) return null;
            var mask = _named.FirstOrDefault(item => !item.Active);
            if (mask is null) { Fault("Named coroutine mask depth exhausted."); return null; }
            mask.Begin(default); mask.Mask = true; mask.Parent = _currentNamed; _currentNamed = mask; return mask;
        }
        try
        {
            Guard(); if (_recycling) throw new InvalidOperationException("An owned named coroutine reentered observer cleanup.");
            _authority.VerifyObservation(); RequireFrame(frame, NativeCoroutineKind.Start);
            var name = _backend.ReadMethodName(frame.Enumerator.ToInt64()); Guard();
            if (name is not ("Cutout" or "FadeOutScoreDisplay")) { Fault("An untracked named coroutine targets the owned controller."); return null; }
            var role = name == "Cutout" ? TerminalCoroutineRole.Cutout : TerminalCoroutineRole.Fade;
            if (role == TerminalCoroutineRole.Cutout && (!_result.Active || _cutoutNamedAttempted || frame.Caller.ToInt64() != _cutoutCaller))
                throw new InvalidOperationException("Named Cutout is outside the exact ControllerOver source and once-only capture window.");
            if (role == TerminalCoroutineRole.Cutout) _cutoutNamedAttempted = true;
            var context = _named.FirstOrDefault(item => !item.Active) ?? throw new InvalidOperationException("Named coroutine depth exhausted.");
            context.Begin(role); context.Parent = _currentNamed; _currentNamed = context; return context;
        }
        catch (Exception error) { Fault(error.Message); throw; }
    }
    internal Context? CaptureFactory(TerminalCoroutineRole role, NativeCoroutineFrame frame)
    {
        _main.VerifyAccess(); if (_closed || frame.Self.ToInt64() != _controller) return null;
        try
        {
            Guard(); if (_recycling) throw new InvalidOperationException("An owned factory reentered observer cleanup.");
            _authority.VerifyObservation(); RequireFrame(frame, NativeCoroutineKind.Factory);
            if (_count >= _records.Length || _created == int.MaxValue) throw new InvalidOperationException("Terminal enum record capacity exhausted.");
            var record = _records.First(item => !item.Reserved); record.Reserve(role); ++_count; ++_created;
            if (_currentNamed is { Mask: false, _record: null } parent && parent.Role == role)
            { parent._record = record; parent.Generation = record.Generation; record.RequiredCutout = role == TerminalCoroutineRole.Cutout; }
            record.Factory.Begin(role); return record.Factory;
        }
        catch (Exception error) { Fault(error.Message); throw; }
    }
    internal void CompleteFactory(Context context, NativeCoroutineFrame frame, nuint result)
    {
        _main.VerifyAccess();
        try
        {
            Guard(); var record = RequireContext(context, 1); if (frame.Self.ToInt64() != _controller || result == 0) throw new InvalidOperationException("Owned factory returned no actual enum.");
            var pointer = unchecked((long)result);
            if (_records.Any(item => item.Reserved && Interlocked.Read(ref item.Enumerator) == pointer)) throw new InvalidOperationException("Terminal factories reused the same enum instance.");
            Interlocked.Exchange(ref record.Enumerator, pointer); _backend.RetainEnumerator(record.Slot, record.Role, pointer); Guard();
            var value = _backend.ReadEnumerator(record.Slot, record.Role); RequireEnum(record, value);
            if (value.State != 0) throw new InvalidOperationException("Factory output is not a fresh state0 enum.");
            record.ExpectedState = 0; record.FactoryReturned = true; Volatile.Write(ref record.Published, true); context.After = true;
        }
        catch (Exception error) { Fault(error.Message); throw; }
    }
    internal Context? CaptureEnumStart(NativeCoroutineFrame frame)
    {
        _main.VerifyAccess(); if (_closed || frame.Self.ToInt64() != _controller) return null;
        var record = FindEnum(frame.Enumerator.ToInt64()); if (record is null) { RejectRetiringIdentity(frame.Enumerator.ToInt64()); return null; }
        try
        {
            Guard(); RequireFrame(frame, NativeCoroutineKind.Start);
            if (!record.FactoryReturned || record.StartAttempted || record.Terminal) throw new InvalidOperationException("Owned enum started twice or before factory publication.");
            record.StartAttempted = true; record.Start.Begin(record.Role); return record.Start;
        }
        catch (Exception error) { Fault(error.Message); throw; }
    }
    internal void CompleteStart(Context context, NativeCoroutineFrame frame, nuint coroutine)
    {
        _main.VerifyAccess();
        if (context.Mask)
        {
            if (!context.Active || context.After || context.Kind != 0 || frame.Self.ToInt64() == _controller)
                Fault("Foreign named coroutine mask was not paired to its actual child.");
            else context.After = true;
            return;
        }
        try
        {
            var record = RequireContext(context, context.Kind);
            Interlocked.Exchange(ref record.ObservedHandle, unchecked((long)coroutine));
            Guard();
            if (context.Kind is not (0 or 2) || frame.Self.ToInt64() != _controller || !record.FactoryReturned || record.StartReturned)
                throw new InvalidOperationException("Coroutine Start did not pair with its exact factory output.");
            if (context.Kind == 0) { if (!ReferenceEquals(_currentNamed, context) || record.StartAttempted) throw new InvalidOperationException("Named Start/factory ownership changed."); record.StartAttempted = true; }
            else if (frame.Enumerator.ToInt64() != record.Enumerator) throw new InvalidOperationException("Enum Start identity changed.");
            RequireEnum(record, _backend.ReadEnumerator(record.Slot, record.Role));
            if (coroutine == 0 && !record.Terminal) throw new InvalidOperationException("Pending terminal coroutine has no actual Coroutine handle.");
            if (coroutine != 0) _backend.RetainCoroutine(record.Slot, unchecked((long)coroutine));
            Guard();
            record.HandleConfirmed = coroutine != 0; record.StartReturned = true; context.After = true;
            if (context.Kind == 0 && record.Role == TerminalCoroutineRole.Cutout) _cutoutNamedReturned = true;
        }
        catch (Exception error) { Fault(error.Message); throw; }
    }
    internal Context? CaptureMove(TerminalCoroutineRole role, NativeCoroutineFrame frame)
    {
        _main.VerifyAccess(); var record = FindEnum(frame.Self.ToInt64()); if (_closed) return null;
        if (record is null) { RejectRetiringIdentity(frame.Self.ToInt64()); return null; }
        try
        {
            Guard(); RequireFrame(frame, NativeCoroutineKind.MoveNext);
            if (record.Role != role || !record.FactoryReturned || record.Terminal) throw new InvalidOperationException("Terminal enum role/publication/completion changed.");
            var value = _backend.ReadEnumerator(record.Slot, record.Role); RequireEnum(record, value);
            if (value.State != record.ExpectedState) throw new InvalidOperationException("Terminal enum state changed outside paired native MoveNext.");
            record.Move.Begin(role); return record.Move;
        }
        catch (Exception error) { Fault(error.Message); throw; }
    }
    internal void CompleteMove(Context context, NativeCoroutineFrame frame, nuint result)
    {
        _main.VerifyAccess();
        try
        {
            Guard(); var record = RequireContext(context, 3); var value = _backend.ReadEnumerator(record.Slot, record.Role); RequireEnum(record, value);
            if (frame.Self.ToInt64() != record.Enumerator || result > byte.MaxValue) throw new InvalidOperationException("Terminal MoveNext identity or raw-byte result changed.");
            if (result == 0)
            {
                if (value.State != -1 || record.ExpectedState != 1)
                    throw new InvalidOperationException("Terminal coroutine did not naturally complete through its known path.");
                if (_completed == int.MaxValue) throw new InvalidOperationException("Terminal completion sequence exhausted.");
                record.Terminal = true; ++_completed;
                if (record.RequiredCutout) _cutoutNaturallyEnded = true;
            }
            else if (value.State != 1 || record.Role == TerminalCoroutineRole.Cutout && record.ExpectedState != 0)
                throw new InvalidOperationException("Terminal coroutine did not return its known yield state.");
            record.ExpectedState = value.State; context.After = true;
        }
        catch (Exception error) { Fault(error.Message); throw; }
    }
    private Record? FindEnum(long identity) => identity == 0 ? null : _records.FirstOrDefault(record => Volatile.Read(ref record.Published) && Interlocked.Read(ref record.Enumerator) == identity);
    private void RejectRetiringIdentity(long identity)
    {
        if (identity != 0 && _records.Any(record => record.Reserved && record.Retiring && Interlocked.Read(ref record.Enumerator) == identity))
            Fault("A withdrawn terminal enum reentered during observer-root retirement.");
    }
    private Record RequireContext(Context context, int kind)
    {
        var record = context._record;
        if (!context.Active || context.After || context.Kind != kind || record is null || !record.Reserved || record.Retiring || record.Role != context.Role || record.Generation != context.Generation
            || !ReferenceEquals(_records[record.Slot], record)) throw new InvalidOperationException("Terminal callback is not paired to its exact preallocated context.");
        return record;
    }
    private void RequireEnum(Record record, TerminalCoroutineObservation value)
    { Guard(); if (value.Enumerator != record.Enumerator || value.Controller != _controller) throw new InvalidOperationException("Actual terminal enum/controller identity changed."); }
    private static void RequireFrame(NativeCoroutineFrame frame, NativeCoroutineKind kind)
    { if (!frame.IsValid || frame.Kind != kind) throw new InvalidOperationException("Invalid native terminal coroutine frame."); }
    internal bool TryConfirmObservedEnded()
        => TryConfirmObservedEndedCore(requireCancellationRetired: true);

    // Pure retained-record observation used to freeze independent destruction
    // sources before cancellation can retire. It grants no root cleanup permit,
    // Ready/activation authority, or future producer closure.
    internal bool TryConfirmObservedEndedForSourceClosure()
        => TryConfirmObservedEndedCore(requireCancellationRetired: false);

    private bool TryConfirmObservedEndedCore(bool requireCancellationRetired)
    {
        Guard(); if (requireCancellationRetired && CoroutineCancellationPending) return false;
        try
        {
            var activity = _backend.ReadActivity(); if (requireCancellationRetired && CoroutineCancellationPending) return false; if (Unknown || !activity.Known || activity.InFlight < 0) throw new InvalidOperationException("Terminal native activity is unknown.");
            if (_recycling || activity.InFlight != 0 || _result.Active || _currentNamed is not null || _records.Any(record => record.Reserved && (record.Retiring || record.Factory.Active || record.Start.Active || record.Move.Active))) return false;
            if (_resultAttempted && (!_resultReturned || !_cutoutNamedReturned || !_cutoutNaturallyEnded)) return false;
            for (var index = 0; index < _records.Length; ++index)
            {
                var record = _records[index]; if (!record.Reserved) continue; if (!record.FactoryReturned || !record.Terminal) return false;
                var value = _backend.ReadEnumerator(index, record.Role); RequireEnum(record, value);
                if (requireCancellationRetired && CoroutineCancellationPending) return false;
                if (value.State != -1) throw new InvalidOperationException("Terminal enum changed its natural completed state.");
            }
            if (Unknown) throw new InvalidOperationException("Terminal observation authority changed during completed-record verification.");
            return !requireCancellationRetired || !CoroutineCancellationPending; // Observed records only: future producers remain installed.
        }
        catch (Exception error) { Fault(error.Message); throw new NativeLocalTerminalCoroutineException(this, error); }
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
            throw new InvalidOperationException("An independent terminal root lost its exact retirement scope.");
        record.Retirement.RequireFreeInProgress();
    }
    internal int RecycleNaturallyEnded()
    {
        Guard(); if (CoroutineCancellationPending) return 0; if (_recycling) throw new InvalidOperationException("Terminal observer root retirement cannot reenter.");
        if (_launch is not null && _engineRetirement is null)
            throw new InvalidOperationException("Production terminal retirement requires the exact engine authority.");
        if (_result.Active || _currentNamed is not null || _records.Any(record => record.Factory.Active || record.Start.Active || record.Move.Active)) return 0;
        _recycling = true; var released = 0;
        try
        {
            _authority.VerifyObservation(); if (CoroutineCancellationPending) return 0;
            for (var index = 0; index < _records.Length; ++index)
            {
                var record = _records[index];
                if (!record.Reserved || !record.FactoryReturned || !record.Terminal) continue;
                var before = _backend.ReadActivity();
                if (CoroutineCancellationPending) break;
                if (Unknown || !before.Known || before.InFlight < 0) throw new InvalidOperationException("Terminal activity is unknown before record withdrawal.");
                if (before.InFlight != 0) break;
                var value = _backend.ReadEnumerator(index, record.Role); RequireEnum(record, value);
                if (CoroutineCancellationPending) break;
                if (value.State != -1) throw new InvalidOperationException("Terminal enum changed its natural completed state before cleanup.");
                if (!record.Retiring)
                {
                    if (_engineRetirement is not null
                        && !_engineRetirement.TryBegin(CancellationIdentity(record), out record.Retirement)) continue;
                    record.Retiring = true; Volatile.Write(ref record.Published, false);
                    record.Retirement?.MarkWithdrawn(CancellationIdentity(record));
                }
                var after = _backend.ReadActivity();
                RequireCancellationRetirement();
                if (Unknown || !after.Known || after.InFlight < 0) throw new InvalidOperationException("Terminal activity changed while withdrawing record identity.");
                if (after.InFlight != 0) break; // Roots stay held; a later frame may redrain without retrying a free.
                if (record.Retirement is not null && !record.Retirement.TryConfirmWithdrawnAndDrained()) break;
                if (record.FreeAttempted || _retired == int.MaxValue) throw new InvalidOperationException("Unknown root free cannot retry or overflow its retirement sequence.");
                if (record.Retirement is not null && !record.Retirement.BeginFree()) break;
                RequireCancellationRetirement(); record.FreeAttempted = true;
                _backend.ReleaseEndedRoots(index, record.Enumerator, record.Generation);
                record.Retirement?.CompleteFree();
                RequireCancellationRetirement();
                var completed = _backend.ReadActivity();
                RequireCancellationRetirement();
                if (Unknown || completed is not { Known: true, InFlight: 0 }) throw new InvalidOperationException("Terminal calls changed during root cleanup; slot remains unavailable.");
                Interlocked.Exchange(ref record.Enumerator, 0); record.Reserved = false; record.Retiring = false;
                --_count; ++_retired; ++released;
            }
            return released;
        }
        catch (Exception error)
        {
            _engineRetirement?.ObserveFailure(this, error.Message); Fault(error.Message);
            throw new NativeLocalTerminalCoroutineException(this, error);
        }
        finally { _recycling = false; }
    }
    internal bool TryReleaseUnusedBeforeActivation()
    {
        Guard(); if (CoroutineCancellationPending) return false; _authority.VerifyBeforeCapture(); if (CoroutineCancellationPending) return false;
        if (_created != 0 || _resultAttempted || _currentNamed is not null) throw new InvalidOperationException("Active terminal records cannot use unused-observer cleanup.");
        var activity = _backend.ReadActivity();
        if (CoroutineCancellationPending) return false;
        if (Unknown) throw new InvalidOperationException("Terminal observation authority changed before unused cleanup.");
        if (activity is not { Known: true, InFlight: 0 }) return false;
        try { RequireCancellationRetirement(); _backend.DetachAndReleaseUnused(); RequireCancellationRetirement(); if (Unknown || _backend.RootRecords != 0) throw new InvalidOperationException("Unused terminal cleanup is unconfirmed."); _closed = true; lock (Gate) Owners.Remove(this); return true; }
        catch (Exception error) { Fault(error.Message); throw new NativeLocalTerminalCoroutineException(this, error); }
    }
    private void Guard()
    { _main.VerifyAccess(); if (!_ready || _closed || Unknown) throw new InvalidOperationException("Terminal coroutine owner is unavailable or unknown."); }
    private sealed class Authority : ITerminalCoroutineAuthority
    {
        private readonly LocalRunLaunchCommitState _launch;
        internal Authority(LocalRunLaunchCommitState launch) { ArgumentNullException.ThrowIfNull(launch); _launch = launch; Controller = launch.Read().Controller; }
        public bool Unknown => _launch.Read().Phase == LocalRunLaunchCommitPhase.Unknown;
        public bool CoroutineCancellationPending => _launch.CoroutineCancellationPending;
        public long Controller { get; }
        public void VerifyBeforeCapture()
        { _launch.VerifyPendingOwnership(_launch.Registry); var value = _launch.Read(); if (value.Controller != Controller || value.ActivationAttempted || value.Phase != LocalRunLaunchCommitPhase.ControllerBound || _launch.ParameterBinding is null) throw new InvalidOperationException("Terminal observations must be installed after setter and before activation."); }
        public void VerifyObservation()
        {
            var value = _launch.Read(); var current = _launch.Registry.Read(_launch.Session);
            if (!value.ActivationAttempted || value.Controller != Controller || value.Phase == LocalRunLaunchCommitPhase.Unknown || current.ProtectionRetired
                || current.Phase == LocalRunSessionPhase.Closed || current.ControllerIdentity != Controller)
                throw new InvalidOperationException("Terminal observations require the same protected activated controller.");
        }
        public void VerifyResultClaim()
        { VerifyObservation(); var current = _launch.Registry.Read(_launch.Session); if (current.Phase != LocalRunSessionPhase.ResultCapturing || !current.ResultCallClaimed) throw new InvalidOperationException("ControllerOver requires this session's actual result capture claim."); }
        public void ObserveFaultFromAnyThread() => _launch.ObserveNativeFaultFromAnyThread("Terminal coroutine observation is unknown.");
    }
}
