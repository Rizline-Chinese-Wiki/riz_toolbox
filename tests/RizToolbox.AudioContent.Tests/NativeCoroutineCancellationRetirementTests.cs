using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime.Hooks;
using static Assert;

/// <summary>Actual production trackers with controlled scalar authority and
/// backend reads/roots. The shared production retirement guard is called around
/// each controlled free. No Ready is forged, no native/GC free or Stop executes.</summary>
internal static class NativeCoroutineCancellationRetirementTests
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Cancellation Pending blocks initialization end and release while ordinary observations continue", InitializationAdmission),
        ("Cancellation arriving in initialization activity or enum reads prevents cached Ready evidence", InitializationReads),
        ("Cancellation during initialization detach or first independent free retains remaining roots", InitializationCleanup),
        ("Cancellation Pending blocks Cutout retirement without changing physical activity or natural observations", CutoutAdmission),
        ("Cancellation in Cutout source reads withdrawal and first free blocks successful slot recycling", CutoutCleanup),
        ("Cancellation Pending blocks terminal end and unused cleanup while factory and Move observations continue", TerminalAdmission),
        ("Cancellation in terminal reads withdrawal and first free retains generation responsibility", TerminalCleanup),
        ("Cancellation worker scalar and unused-detach transitions cannot authorize further root release", WorkerAndUnused),
        ("Cancellation Pending permits pure terminal source observation while retaining every cleanup root", TerminalSourceObservation),
        ("Terminal source-only observation still rejects in-flight unreadable or changed exact enumerators", TerminalSourceObservationFailures),
        ("Cancellation Pending permits only exact natural initialization observation for return", InitializationReturnObservation)
    };
    private static void Check(bool value, string message = "Cancellation retirement assertion failed.")
    { if (!value) throw new InvalidOperationException(message); }

    private static void InitializationAdmission()
    {
        using var f = new InitFixture(); f.Authority.SetPending(); var reads = f.Inner.Reads; var activities = f.Inner.ActivityReads;
        Check(!f.Owner.TryReleaseBeforeStart() && !f.Owner.TryConfirmEnded()); Equal(reads, f.Inner.Reads); Equal(activities, f.Inner.ActivityReads);
        f.Finish(); Check(!f.Owner.Unknown && !f.Owner.TryConfirmEnded() && !f.Owner.TryReleaseAfterEnded() && !f.Owner.Read().Ended);
        Equal(4, f.Inner.RootRecords); Equal(0, f.Inner.FreeCalls); Equal(0, f.Inner.DetachCalls);
        Check(f.Owner.Read().DelayEnded && f.Owner.Read().WaitEnded && f.Inner.ReadActivity() is { Known: true, InFlight: 0 });
        Check(f.Owner.ReadCancellationIdentities().All(identity => identity.Known && identity.NaturalTerminal));
    }
    private static void InitializationReads()
    {
        foreach (var phase in new[] { "activity", "enum" })
        {
            using var f = new InitFixture(); f.Finish(); Check(f.Owner.TryConfirmEnded()); var reads = f.Inner.Reads;
            if (phase == "activity") f.Inner.DuringActivity = f.Authority.SetPending; else f.Inner.DuringRead = f.Authority.SetPending;
            Check(!f.Owner.TryConfirmEnded() && !f.Owner.Read().Ended && !f.Owner.Unknown);
            Equal(reads + (phase == "enum" ? 1 : 0), f.Inner.Reads);
            Check(!f.Owner.TryReleaseAfterEnded()); Equal(0, f.Inner.FreeCalls); Equal(0, f.Inner.DetachCalls);
        }
    }
    private static void InitializationReturnObservation()
    {
        using(var f=new InitFixture())
        {
            f.Authority.SetPending();Check(!f.Owner.TryConfirmNaturalEndForReturn());f.Finish();
            Check(f.Owner.TryConfirmNaturalEndForReturn()&&!f.Owner.Read().Ended&&!f.Owner.TryConfirmEnded()
                &&!f.Owner.TryReleaseAfterEnded()&&f.Inner.FreeCalls==0&&f.Inner.RootRecords==4);
            f.Inner.InFlight=1;Check(!f.Owner.TryConfirmNaturalEndForReturn());f.Inner.InFlight=0;
            Check(f.Owner.TryConfirmNaturalEndForReturn());
        }
        using(var f=new InitFixture())
        {
            f.Authority.SetPending();f.Finish();f.Inner.Known=false;
            Throws<NativeLocalInitializationCoroutineException>(()=>f.Owner.TryConfirmNaturalEndForReturn());
            Check(f.Owner.Unknown&&f.Inner.FreeCalls==0&&f.Inner.RootRecords==4);
        }
    }
    private static void InitializationCleanup()
    {
        foreach (var phase in new[] { "detach", "free" })
        {
            using var f = new InitFixture(); f.Finish();
            if (phase == "detach") f.Inner.DuringDetach = f.Authority.SetPending; else f.Inner.DuringFree = _ => f.Authority.SetPending();
            Throws<NativeLocalInitializationCoroutineException>(() => f.Owner.TryReleaseAfterEnded());
            Check(f.Owner.Unknown && !f.Owner.Read().Closed && !f.Owner.Read().Ended);
            Equal(phase == "detach" ? 4 : 3, f.Inner.RootRecords); Equal(phase == "detach" ? 0 : 1, f.Inner.RootFreeAttempts.Sum());
            var frees = f.Inner.RootFreeAttempts.Sum(); Check(!f.Owner.TryReleaseAfterEnded()); Equal(frees, f.Inner.RootFreeAttempts.Sum());
        }
    }
    private static void CutoutAdmission()
    {
        using var f = new CutoutFixture(); f.Authority.SetPending(); f.Finish();
        var reads = f.Backend.Reads; var activities = f.Backend.Activities;
        Check(!f.Owner.TryConfirmNativeIdle() && !f.Owner.TryConfirmObservedEnded()); Equal(0, f.Owner.RecycleNaturallyEnded());
        Equal(reads, f.Backend.Reads); Equal(activities, f.Backend.Activities); Equal(2, f.Backend.RootRecords); Equal(0, f.Backend.Frees);
        Check(!f.Owner.Unknown && f.Owner.Read().NaturallyEnded == 1 && f.Backend.ReadActivity() is { Known: true, InFlight: 0 }
            && f.Owner.ReadCancellationIdentities().Single().NaturalTerminal);
    }
    private static void CutoutCleanup()
    {
        foreach (var phase in new[] { "source", "enum", "withdraw", "free" })
        {
            using var f = new CutoutFixture(); f.Finish(); var activity = f.Backend.Activities; var reads = f.Backend.Reads;
            if (phase == "source") f.Authority.DuringIdle = f.Authority.SetPending;
            if (phase == "enum") f.Backend.DuringRead = f.Authority.SetPending;
            if (phase == "withdraw") f.Backend.DuringActivity = count => { if (count == activity + 3) f.Authority.SetPending(); };
            if (phase == "free") f.Backend.DuringFree = f.Authority.SetPending;
            if (phase is "withdraw" or "free") Throws<NativeLocalCutoutCoroutineException>(() => f.Owner.RecycleNaturallyEnded());
            else Equal(0, f.Owner.RecycleNaturallyEnded());
            var value = f.Owner.Read(); Equal(0, value.RetiredRecords); Equal(1, value.LiveRecords);
            Equal(phase == "free" ? 1 : 2, f.Backend.RootRecords); Equal(phase == "free" ? 1 : 0, f.Backend.Frees);
            Check(value.Unknown == (phase is "withdraw" or "free"));
            if (phase == "source") Equal(reads, f.Backend.Reads);
            if (!value.Unknown) Equal(0, f.Owner.RecycleNaturallyEnded());
        }
    }
    private static void TerminalAdmission()
    {
        using var f = new TerminalFixture(); f.Authority.SetPending(); Check(!f.Owner.TryReleaseUnusedBeforeActivation());
        f.Finish(); var reads = f.Inner.Reads; var activities = f.Inner.ActivityReads;
        Check(!f.Owner.TryConfirmObservedEnded()); Equal(0, f.Owner.RecycleNaturallyEnded());
        Equal(reads, f.Inner.Reads); Equal(activities, f.Inner.ActivityReads);
        Check(!f.Owner.Unknown && f.Owner.Read().NaturallyEnded == 1 && f.Inner.Roots == 2 && f.Inner.FreeCalls == 0
            && f.Inner.ReadActivity() is { Known: true, InFlight: 0 } && f.Owner.ReadCancellationIdentities().Single().NaturalTerminal);
    }
    private static void TerminalSourceObservation()
    {
        using var f=new TerminalFixture(); f.Authority.SetPending(); f.Finish();
        var roots=f.Inner.Roots; var frees=f.Inner.FreeCalls;
        Check(!f.Owner.TryConfirmObservedEnded() && f.Owner.TryConfirmObservedEndedForSourceClosure());
        Check(!f.Owner.Unknown && f.Owner.CoroutineCancellationPending && f.Owner.RecycleNaturallyEnded()==0
            && !f.Owner.TryReleaseUnusedBeforeActivation() && f.Inner.Roots==roots && f.Inner.FreeCalls==frees,
            "Independent death-source observation must not retire cancellation or allow a root free.");
    }
    private static void TerminalSourceObservationFailures()
    {
        using(var f=new TerminalFixture())
        {
            f.Authority.SetPending(); f.Finish(); f.Inner.InFlight=1;
            Check(!f.Owner.TryConfirmObservedEndedForSourceClosure() && f.Inner.FreeCalls==0);
            f.Inner.InFlight=0; Check(f.Owner.TryConfirmObservedEndedForSourceClosure());
        }
        foreach(var failure in new[]{"unknown","state","receiver"})
        {
            using var f=new TerminalFixture();f.Authority.SetPending();f.Finish();
            if(failure=="unknown") f.Inner.Known=false;
            else
            {
                var key=f.Inner.Values.Keys.Single();var row=f.Inner.Values[key];
                f.Inner.Values[key]=failure=="state"?row with{State=1}:row with{Controller=999};
            }
            Throws<NativeLocalTerminalCoroutineException>(()=>f.Owner.TryConfirmObservedEndedForSourceClosure());
            Check(f.Owner.Unknown && f.Inner.FreeCalls==0 && f.Inner.Roots==2);
        }
    }
    private static void TerminalCleanup()
    {
        foreach (var phase in new[] { "activity", "enum", "withdraw", "free" })
        {
            using var f = new TerminalFixture(); f.Finish(); var activity = f.Inner.ActivityReads;
            if (phase == "activity") f.Inner.DuringActivity = _ => f.Authority.SetPending();
            if (phase == "enum") f.Inner.DuringRead = f.Authority.SetPending;
            if (phase == "withdraw") f.Inner.DuringActivity = count => { if (count == activity + 2) f.Authority.SetPending(); };
            if (phase == "free") f.Inner.DuringFree = _ => f.Authority.SetPending();
            if (phase is "withdraw" or "free") Throws<NativeLocalTerminalCoroutineException>(() => f.Owner.RecycleNaturallyEnded());
            else Equal(0, f.Owner.RecycleNaturallyEnded());
            var value = f.Owner.Read(); Equal(0, value.RetiredRecords); Equal(1, value.LiveRecords);
            Equal(phase == "free" ? 1 : 2, f.Inner.Roots); Equal(phase == "free" ? 1 : 0, f.Inner.Frees.Count);
            Check(value.Unknown == (phase is "withdraw" or "free"));
        }
    }
    private static void WorkerAndUnused()
    {
        using (var f = new InitFixture())
        {
            f.Inner.DuringDetach = f.Authority.SetPending;
            Throws<NativeLocalInitializationCoroutineException>(() => f.Owner.TryReleaseBeforeStart());
            Check(f.Owner.Unknown && !f.Owner.Read().Closed && f.Inner.FreeCalls == 0);
        }
        using (var f = new TerminalFixture())
        {
            f.Backend.DuringDetach = f.Authority.SetPending;
            Throws<NativeLocalTerminalCoroutineException>(() => f.Owner.TryReleaseUnusedBeforeActivation());
            Check(f.Owner.Unknown && !f.Owner.Read().Closed && f.Inner.Detaches == 1 && f.Inner.Roots == 0);
        }
        using (var f = new CutoutFixture())
        {
            f.Finish(); var reads = f.Backend.Reads; var activities = f.Backend.Activities;
            Task.Run(f.Authority.SetPending).GetAwaiter().GetResult();
            Check(!f.Owner.Unknown && f.Owner.CoroutineCancellationPending && !f.Owner.TryConfirmNativeIdle()); Equal(0, f.Owner.RecycleNaturallyEnded());
            Equal(reads, f.Backend.Reads); Equal(activities, f.Backend.Activities); Equal(0, f.Backend.Frees);
        }
    }

    private class PendingAuthority
    {
        private int _pending;
        public bool CoroutineCancellationPending => Volatile.Read(ref _pending) != 0;
        internal void SetPending() => Interlocked.Exchange(ref _pending, 1);
    }
    private sealed class InitAuthority : PendingAuthority, IInitializationCoroutineAuthority
    {
        public long Controller => 101;
        public bool Unknown => false;
        public void VerifyBeforeCapture() { }
        public void VerifyGetterWindow() { }
        public void ObserveFaultFromAnyThread(string reason) { }
    }
    private sealed class InitFixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new(); internal readonly InitAuthority Authority = new();
        internal readonly NativeLocalInitializationCoroutineRegression.Backend Inner;
        internal readonly NativeLocalInitializationCoroutineTracker Owner;
        internal InitFixture()
        { Inner = new(Main); Owner = NativeLocalInitializationCoroutineTracker.CaptureBackend(Main, new InitBackend(Inner), Authority, NativeLocalInitializationCoroutineRegression.Backend.Module); }
        internal void Finish()
        {
            Inner.Start(InitializationCoroutineRole.Delay); Inner.Move(InitializationCoroutineRole.Delay, 2, 1); Inner.Move(InitializationCoroutineRole.Delay, -1, 0);
            Inner.Start(InitializationCoroutineRole.WaitUntil); Inner.Move(InitializationCoroutineRole.WaitUntil, -1, 0);
            Check(Inner.CaptureError is null && Inner.AfterError is null);
        }
        public void Dispose() => Main.Dispose();
    }
    private sealed class InitBackend : IInitializationCoroutineBackend
    {
        private readonly NativeLocalInitializationCoroutineRegression.Backend _inner;
        internal InitBackend(NativeLocalInitializationCoroutineRegression.Backend inner) => _inner = inner;
        public bool CleanupUnknown => _inner.CleanupUnknown;
        public int RootRecords => _inner.RootRecords;
        public void Initialize(NativeLocalInitializationCoroutineTracker owner) => _inner.Initialize(owner);
        public void RetainEnumerator(InitializationCoroutineRole role, long enumerator) => _inner.RetainEnumerator(role, enumerator);
        public InitializationCoroutineObservation ReadEnumerator(InitializationCoroutineRole role) => _inner.ReadEnumerator(role);
        public long ReadWaitUntilCurrent(long parent, long predicate, bool retain) => _inner.ReadWaitUntilCurrent(parent, predicate, retain);
        public void RetainCoroutine(InitializationCoroutineRole role, long coroutine) => _inner.RetainCoroutine(role, coroutine);
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity() => _inner.ReadActivity();
        public void Detach() => _inner.Detach();
        public void ReleaseRoots()
        {
            ++_inner.FreeCalls;
            for (var index = _inner.Roots.Length - 1; index >= 0; --index)
            {
                if (!_inner.Roots[index]) continue;
                _inner.Owner.RequireCancellationRetirement(); ++_inner.RootFreeAttempts[index]; _inner.Roots[index] = false;
                _inner.DuringFree?.Invoke(index); _inner.Owner.RequireCancellationRetirement();
            }
        }
    }
    private sealed class TerminalAuthority : PendingAuthority, ITerminalCoroutineAuthority
    {
        public long Controller => 101;
        public void VerifyBeforeCapture() { }
        public void VerifyObservation() { }
        public void VerifyResultClaim() { }
        public void ObserveFaultFromAnyThread() { }
    }
    private sealed class TerminalFixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new(); internal readonly TerminalAuthority Authority = new();
        internal readonly NativeLocalTerminalCoroutineRegression.Backend Inner;
        internal readonly TerminalBackend Backend;
        internal readonly NativeLocalTerminalCoroutineTracker Owner;
        internal TerminalFixture()
        { Inner = new(Main, 101); Backend = new(Inner); Owner = NativeLocalTerminalCoroutineTracker.CaptureBackend(Main, Backend, Authority, NativeLocalTerminalCoroutineRegression.Backend.Module); }
        internal void Finish()
        {
            var value = Inner.Factory(TerminalCoroutineRole.Fade); Inner.EnumStart(TerminalCoroutineRole.Fade, value);
            Inner.Move(TerminalCoroutineRole.Fade, value, 1, 1); Inner.Move(TerminalCoroutineRole.Fade, value, -1, 0);
            Check(Inner.CaptureError is null && Inner.AfterError is null);
        }
        public void Dispose() => Main.Dispose();
    }
    private sealed class TerminalBackend : ITerminalCoroutineBackend
    {
        private readonly NativeLocalTerminalCoroutineRegression.Backend _inner;
        internal Action? DuringDetach;
        internal TerminalBackend(NativeLocalTerminalCoroutineRegression.Backend inner) => _inner = inner;
        public bool CleanupUnknown => _inner.CleanupUnknown;
        public int RootRecords => _inner.RootRecords;
        public void Initialize(NativeLocalTerminalCoroutineTracker owner) => _inner.Initialize(owner);
        public string ReadMethodName(long name) => _inner.ReadMethodName(name);
        public void RetainEnumerator(int slot, TerminalCoroutineRole role, long enumerator) => _inner.RetainEnumerator(slot, role, enumerator);
        public TerminalCoroutineObservation ReadEnumerator(int slot, TerminalCoroutineRole role) => _inner.ReadEnumerator(slot, role);
        public void RetainCoroutine(int slot, long coroutine) => _inner.RetainCoroutine(slot, coroutine);
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity() => _inner.ReadActivity();
        public void DetachAndReleaseUnused() { _inner.DetachAndReleaseUnused(); DuringDetach?.Invoke(); }
        public void ReleaseEndedRoots(int slot, long enumerator, long generation)
        {
            Check(_inner.Owner.IsRetiringForCleanup(slot, enumerator, generation)); ++_inner.FreeCalls;
            foreach (var index in new[] { slot + 16, slot })
            {
                if (!_inner.RootSlots[index]) continue;
                _inner.Owner.RequireCancellationRetirement(); _inner.Frees.Add(index); _inner.RootSlots[index] = false; --_inner.Roots;
                _inner.DuringFree?.Invoke(index); _inner.Owner.RequireCancellationRetirement();
            }
            _inner.Slots.Remove(slot);
        }
    }
    private sealed class CutoutAuthority : PendingAuthority, ICutoutCoroutineAuthority
    {
        internal long Dispatch; internal bool Active; internal Action? DuringIdle;
        public bool Unknown => false;
        public long Host => 201;
        public NativeCutoutEventObservation Source { get; } = new(101, 201, 202, 203, 204, 1, true,
            Array.AsReadOnly(new[] { new NativeCutoutTrigger(301, 401, BitConverter.SingleToInt32Bits(1f)) }));
        public void VerifyBeforeCapture() { }
        public void VerifyObservation() { }
        public bool TryGetDispatch(out long generation) { generation = Dispatch; return Active; }
        public bool TryConfirmSourceIdle() { DuringIdle?.Invoke(); return !Active; }
        public void ObserveFaultFromAnyThread() { }
    }
    private sealed class CutoutFixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new(); internal readonly CutoutAuthority Authority = new();
        internal readonly CutoutBackend Backend; internal readonly NativeLocalCutoutCoroutineTracker Owner;
        internal CutoutFixture() { Backend = new(Authority); Owner = NativeLocalCutoutCoroutineTracker.CaptureBackend(Main, Backend, Authority, CutoutBackend.Module); }
        internal void Finish() { Backend.Start(); Backend.Move(1, 1); Backend.Move(-1, 0); }
        public void Dispose() => Main.Dispose();
    }
    private sealed class CutoutBackend : ICutoutCoroutineBackend
    {
        internal const long Module = 0x20000000;
        private readonly CutoutAuthority _authority; private NativeLocalCutoutCoroutineTracker _owner = null!;
        private CutoutCoroutineObservation _value = new(8001, 0, 301, 401, BitConverter.SingleToInt32Bits(1f), 0, 0, 0);
        private readonly HashSet<int> _roots = new(); internal int Reads, Activities, Frees, InFlight;
        internal Action? DuringRead, DuringFree; internal Action<int>? DuringActivity;
        internal CutoutBackend(CutoutAuthority authority) => _authority = authority;
        public bool CleanupUnknown => false;
        public int RootRecords => _roots.Count;
        public void Initialize(NativeLocalCutoutCoroutineTracker owner) => _owner = owner;
        public void RetainEnumerator(int slot, CutoutCoroutineRole role, long enumerator) { Check(slot == 0 && enumerator == 8001); _roots.Add(slot); }
        public CutoutCoroutineObservation ReadEnumerator(int slot, CutoutCoroutineRole role) { ++Reads; DuringRead?.Invoke(); return _value; }
        public void RetainCoroutine(int slot, long coroutine) => _roots.Add(slot + 64);
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity() { ++Activities; DuringActivity?.Invoke(Activities); return new(InFlight, true); }
        public void ReleaseEndedRoots(int slot, long enumerator, long generation)
        {
            Check(_owner.IsRetiringForCleanup(slot, enumerator, generation));
            foreach (var index in new[] { slot + 64, slot })
            {
                if (!_roots.Contains(index)) continue;
                _owner.RequireCancellationRetirement(); _roots.Remove(index); ++Frees; DuringFree?.Invoke(); _owner.RequireCancellationRetirement();
            }
        }
        private static NativeCoroutineFrame Frame(NativeCoroutineKind kind, long self, long argument = 0, long caller = 0)
            => new() { Version = 1, Size = 48, Kind = kind, Self = new(self), Enumerator = new(argument), Method = new(1), Caller = new(Module + caller) };
        internal void Start()
        {
            ++_authority.Dispatch; _authority.Active = true; ++InFlight;
            var frame = Frame(NativeCoroutineKind.Start, 201, 8001, 0x38a6b2); var context = _owner.CaptureStart(frame)!;
            try { _owner.CompleteStart(context, frame, 9001); }
            finally { context.Dispose(); --InFlight; _authority.Active = false; }
        }
        internal void Move(int state, nuint result)
        {
            ++InFlight; var frame = Frame(NativeCoroutineKind.MoveNext, 8001, caller: 0x999); var context = _owner.CaptureMove(CutoutCoroutineRole.DelayedEvent, frame)!;
            try { _value = _value with { State = state }; _owner.CompleteMove(context, frame, result); }
            finally { context.Dispose(); --InFlight; }
        }
    }
}
