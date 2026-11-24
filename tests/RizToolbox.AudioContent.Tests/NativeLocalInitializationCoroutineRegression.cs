using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime.Hooks;
using static Assert;

internal static class NativeLocalInitializationCoroutineRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Getter coroutine tracking accepts both native WaitForSeconds state2 and provider countdown state1", Modes),
        ("Getter coroutine tracking publishes the actual IEnumerator before synchronous first MoveNext", Synchronous),
        ("Getter WaitUntil matches the rooted Current only between paired physical moves", CurrentWindow),
        ("Getter WaitUntil rejects changed Current, predicate, class and late loss of authority", ChangedCurrent),
        ("Getter WaitUntil withdraws a child match during cancellation and on the next physical move", WithdrawCurrent),
        ("Getter coroutine tracking ignores unrelated controllers callers and enumerators without native reads", Isolation),
        ("Getter coroutine tracking faults an owned worker using only atomic managed identities", Worker),
        ("Getter coroutine tracking rejects changed initial states delegates and delay bits", InitialMismatch),
        ("Getter coroutine tracking rejects duplicate starts role aliasing and a missing getter window", Duplicate),
        ("Getter coroutine tracking waits for normal false and physical exit rather than state minus1", PhysicalCompletion),
        ("Getter coroutine tracking rejects impossible positive-yield transitions and out-of-band state writes", InvalidTransition),
        ("Getter coroutine tracking detects owned physical reentry without reusing an outer context", Reentry),
        ("Getter coroutine tracking preserves original failure and retains missing-after responsibility", OriginalFailure),
        ("Getter coroutine tracking rejects callback identity changes during original and after completion", ChangedCallbacks),
        ("Getter coroutine tracking contains partial capture and root failures without retrying cleanup", RootFailures),
        ("Getter coroutine tracking permits only synchronous terminal null Coroutine and never pending null", NullHandle),
        ("Getter coroutine tracking retains unknown activity and rejects wrong-thread public operations", ActivityAndThread),
        ("Unused getter coroutine observers detach without granting a natural-completion receipt", Untouched),
        ("Getter coroutine tracking sees external authority faults without its own worker callback", ExternalAuthority),
        ("Getter Start reports the managed caller and enumerator when ownership fails inside the original", FaultDuringStart),
        ("Getter coroutine tracking stops after external authority faults inside reads and activity", ExternalReadAndActivity),
        ("Getter coroutine cleanup stops on external faults during detach and independent root free", ExternalDetachAndFree),
        ("Getter coroutine capture retains its enum root but performs no read after an external fault", ExternalRetain)
    };
    private sealed class Authority : IInitializationCoroutineAuthority
    {
        internal bool Before = true, Window = true; internal int Faults, ExternalFault;
        internal bool Pending;
        public bool CoroutineCancellationPending => Pending;
        public long Controller => 101;
        public bool Unknown => Volatile.Read(ref ExternalFault) != 0;
        public void VerifyBeforeCapture() { if (!Before) throw new InvalidOperationException("Before activation required"); }
        public void VerifyGetterWindow() { if (!Window) throw new InvalidOperationException("Actual getter window required"); }
        public void ObserveFaultFromAnyThread(string reason) => Interlocked.Increment(ref Faults);
    }
    internal sealed class Fixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new();
        private readonly Authority _authority = new();
        internal readonly Backend Backend;
        internal NativeLocalInitializationCoroutineTracker Owner { get; }
        internal Fixture(Action<Backend>? configure = null)
        { Backend = new(Main); configure?.Invoke(Backend); Owner = NativeLocalInitializationCoroutineTracker.CaptureBackend(Main, Backend, _authority, Backend.Module); }
        internal void DenyWindow() => _authority.Window = false;
        internal void SetPending(bool pending) => _authority.Pending = pending;
        internal void FaultExternalAuthority() => Interlocked.Exchange(ref _authority.ExternalFault, 1);
        internal int Faults => _authority.Faults;
        internal void Finish()
        {
            for (var index = 0; index < 2; ++index)
            {
                var role = (InitializationCoroutineRole)index; var state = Owner.Read();
                if ((index == 0 ? state.DelayEnumerator : state.WaitEnumerator) == 0) Backend.Start(role);
                if (Backend.Values[index].State == 0 && index == 0) Backend.Move(role, 2, 1);
                if (Backend.Values[index].State != -1) Backend.Move(role, -1, 0);
            }
            Check(Owner.TryConfirmEnded());
        }
        public void Dispose()
        {
            try
            {
                var state = Owner.Read(); if (state.Unknown || state.Closed) return;
                if (state.DelayEnumerator == 0 && state.WaitEnumerator == 0) Owner.TryReleaseBeforeStart();
                else { Finish(); Check(Owner.TryReleaseAfterEnded()); }
            }
            finally { Main.Dispose(); }
        }
    }
    internal sealed class Backend : IInitializationCoroutineBackend
    {
        internal const long Module = 0x10000000;
        private readonly MainThreadDispatcher _main;
        internal NativeLocalInitializationCoroutineTracker Owner = null!;
        internal InitializationCoroutineObservation[] Values = {
            new(1001, 0, 2001, 0, 0x40466666), new(1002, 0, 2002, 3001, 0)
        };
        internal const long WaitChild = 7001;
        internal long Current = WaitChild, ChildPredicate = 3001;
        internal bool ChildClassMatches = true;
        internal Action? DuringCurrent;
        private long _retainedChild;
        internal int CurrentReads;
        internal readonly bool[] Roots = new bool[5];
        internal bool FailInitialize, FailRetain, FailRead, FailCoroutine, FailFree, Known = true;
        internal int Reads, Originals, FreeCalls, DetachCalls, InFlight, ActivityReads;
        internal readonly int[] RootFreeAttempts = new int[5];
        internal Exception? CaptureError, AfterError;
        internal Action? DuringDetach, DuringRead, DuringActivity, AfterRetain;
        internal Action<int>? DuringFree;
        public bool CleanupUnknown { get; private set; }
        public int RootRecords => Roots.Count(value => value);
        internal Backend(MainThreadDispatcher main) => _main = main;
        public void Initialize(NativeLocalInitializationCoroutineTracker owner)
        { Owner = owner; if (FailInitialize) { CleanupUnknown = true; throw new IOException("Partial install"); } }
        public void RetainEnumerator(InitializationCoroutineRole role, long enumerator)
        { _main.VerifyAccess(); Roots[(int)role] = true; if (FailRetain) throw new IOException("Partial enum root"); Values[(int)role] = Values[(int)role] with { Enumerator = enumerator }; AfterRetain?.Invoke(); }
        public InitializationCoroutineObservation ReadEnumerator(InitializationCoroutineRole role)
        { _main.VerifyAccess(); Reads++; if (FailRead) throw new IOException("Read root"); DuringRead?.Invoke(); return Values[(int)role]; }
        public long ReadWaitUntilCurrent(long parent, long predicate, bool retain)
        {
            _main.VerifyAccess(); ++CurrentReads;
            Check(Roots[1] && parent == Values[1].Enumerator && Values[1].State == 1 && Values[1].Predicate == predicate);
            if (retain && Current != 0) { Check(!Roots[4]); Roots[4] = true; _retainedChild = Current; }
            Check(Roots[4] && ChildClassMatches && _retainedChild == Current && ChildPredicate == predicate);
            DuringCurrent?.Invoke();
            return _retainedChild;
        }
        public void RetainCoroutine(InitializationCoroutineRole role, long coroutine)
        { _main.VerifyAccess(); Roots[(int)role + 2] = true; if (FailCoroutine) throw new IOException("Partial Coroutine root"); }
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity()
        { _main.VerifyAccess(); ActivityReads++; DuringActivity?.Invoke(); return new(InFlight, Known && !CleanupUnknown); }
        public void Detach() { DetachCalls++; DuringDetach?.Invoke(); }
        public void ReleaseRoots()
        {
            FreeCalls++;
            for (var index = Roots.Length - 1; index >= 0; --index)
            {
                if (!Roots[index]) continue;
                if (Owner.ObservationAuthorityUnknown) { CleanupUnknown = true; throw new IOException("External authority lost before root free"); }
                RootFreeAttempts[index]++;
                if (FailFree && index == 1) CleanupUnknown = true;
                else Roots[index] = false;
                DuringFree?.Invoke(index);
                if (Owner.ObservationAuthorityUnknown) { CleanupUnknown = true; throw new IOException("External authority lost after root free"); }
            }
            if (CleanupUnknown) throw new IOException("Unknown independent root free");
        }
        internal NativeCoroutineFrame StartFrame(InitializationCoroutineRole role) => new() { Version = 1, Size = 48, Kind = NativeCoroutineKind.Start,
            Self = new(101), Enumerator = new(1001 + (int)role), Method = new(1), Caller = new(Module + (role == InitializationCoroutineRole.Delay ? 0x3BEFDA : 0x3BF0B2)) };
        internal NativeCoroutineFrame MoveFrame(InitializationCoroutineRole role) => new() { Version = 1, Size = 48, Kind = NativeCoroutineKind.MoveNext,
            Self = new(1001 + (int)role), Method = new(2), Caller = new(Module + 0x1234) };
        internal void Start(InitializationCoroutineRole role, Action? original = null, nuint? coroutine = null)
            => StartRaw(StartFrame(role), original, coroutine ?? (nuint)(5001 + (int)role));
        internal void StartRaw(NativeCoroutineFrame frame, Action? original = null, nuint coroutine = 5001)
        {
            NativeLocalInitializationCoroutineTracker.Context? context = null; InFlight++;
            try
            {
                try { context = Owner.CaptureStart(frame); } catch (Exception error) { CaptureError = error; }
                Originals++; original?.Invoke();
                if (context is not null) try { Owner.CompleteStart(context, frame, coroutine); } catch (Exception error) { AfterError = error; }
            }
            finally { try { context?.Dispose(); } finally { InFlight--; } }
        }
        internal void Move(InitializationCoroutineRole role, int state, nuint result, Action? original = null)
        {
            var frame = MoveFrame(role); NativeLocalInitializationCoroutineTracker.Context? context = null; InFlight++;
            try
            {
                try { context = Owner.CaptureMove(role, frame); } catch (Exception error) { CaptureError = error; }
                Originals++; Values[(int)role] = Values[(int)role] with { State = state }; original?.Invoke();
                if (context is not null) try { Owner.CompleteMove(context, frame, result); } catch (Exception error) { AfterError = error; }
            }
            finally { try { context?.Dispose(); } finally { InFlight--; } }
        }
    }
    private static void Modes()
    {
        foreach (var yield in new[] { 1, 2 })
        {
            using var f = new Fixture(); f.Backend.Start(InitializationCoroutineRole.Delay);
            f.Backend.Move(InitializationCoroutineRole.Delay, yield, 0x80);
            if (yield == 1) { f.Backend.Values[0] = f.Backend.Values[0] with { SecondsBits = 0x3f800000 }; f.Backend.Move(InitializationCoroutineRole.Delay, 1, 0xff); }
            f.Backend.Move(InitializationCoroutineRole.Delay, -1, 0); f.Backend.Start(InitializationCoroutineRole.WaitUntil);
            f.Backend.Move(InitializationCoroutineRole.WaitUntil, 1, 1); f.Backend.Move(InitializationCoroutineRole.WaitUntil, -1, 0);
            Check(f.Owner.TryConfirmEnded()); Equal(5, f.Backend.RootRecords); Check(f.Owner.TryReleaseAfterEnded()); Equal(0, f.Backend.RootRecords);
            Check(f.Owner.TryConfirmEnded()); Equal(1, f.Backend.FreeCalls);
        }
    }
    private static void Synchronous()
    {
        using var f = new Fixture(); var pending = false;
        f.Backend.Start(InitializationCoroutineRole.Delay, () => { Equal(1001L, f.Owner.Read().DelayEnumerator); f.Backend.Move(InitializationCoroutineRole.Delay, 2, 1); pending = !f.Owner.TryConfirmEnded(); });
        f.Backend.Start(InitializationCoroutineRole.WaitUntil, () => f.Backend.Move(InitializationCoroutineRole.WaitUntil, -1, 0));
        Check(pending && !f.Owner.Read().Unknown && f.Owner.Read().WaitEnded); f.Backend.Move(InitializationCoroutineRole.Delay, -1, 0); Check(f.Owner.TryConfirmEnded());
    }
    private static void CurrentWindow()
    {
        const InitializationCoroutineRole wait = InitializationCoroutineRole.WaitUntil;
        using var f = new Fixture();
        f.Backend.Start(wait, () =>
        {
            Check(!f.Owner.IsCurrentWaitUntilYield(1002, Backend.WaitChild));
            f.Backend.Move(wait, 1, 1, () => Check(!f.Owner.IsCurrentWaitUntilYield(1002, Backend.WaitChild)));
            Equal(1, f.Backend.CurrentReads);
            Check(!f.Owner.IsCurrentWaitUntilYield(1001, Backend.WaitChild));
            Check(!f.Owner.IsCurrentWaitUntilYield(1002, 9999));
            Equal(1, f.Backend.CurrentReads);
            Check(f.Owner.IsCurrentWaitUntilYield(1002, Backend.WaitChild));
        });
        Check(f.Owner.IsCurrentWaitUntilYield(1002, Backend.WaitChild));
        var reads = f.Backend.CurrentReads;
        f.Backend.Move(wait, -1, 0, () => Check(!f.Owner.IsCurrentWaitUntilYield(1002, Backend.WaitChild)));
        Check(!f.Owner.IsCurrentWaitUntilYield(1002, Backend.WaitChild)); Equal(reads, f.Backend.CurrentReads);
        f.Finish(); Check(f.Owner.TryReleaseAfterEnded()); Equal(1, f.Backend.RootFreeAttempts[4]);
    }
    private static void WithdrawCurrent()
    {
        const InitializationCoroutineRole wait = InitializationCoroutineRole.WaitUntil;
        using var f = new Fixture(); f.Backend.Start(wait); f.Backend.Move(wait, 1, 1);
        f.SetPending(true); var reads = f.Backend.CurrentReads;
        Check(!f.Owner.IsCurrentWaitUntilYield(1002, Backend.WaitChild)); Equal(reads, f.Backend.CurrentReads);
        f.SetPending(false);
        f.Backend.DuringCurrent = () => f.Backend.Move(wait, -1, 0);
        Check(!f.Owner.IsCurrentWaitUntilYield(1002, Backend.WaitChild)); Check(!f.Owner.Unknown);
    }
    private static void ChangedCurrent()
    {
        const InitializationCoroutineRole wait = InitializationCoroutineRole.WaitUntil;
        foreach (var change in new[] { "predicate", "class", "changed", "null", "initial class", "late" })
        {
            using var f = new Fixture(); f.Backend.Start(wait);
            if (change == "null") f.Backend.Current = 0;
            if (change == "initial class") f.Backend.ChildClassMatches = false;
            f.Backend.Move(wait, 1, 1);
            if (change == "null") { Check(f.Owner.Unknown && !f.Backend.Roots[4]); continue; }
            if (change == "initial class") { Check(f.Owner.Unknown && f.Backend.Roots[4]); continue; }
            if (change == "late")
            {
                f.Backend.DuringCurrent = f.FaultExternalAuthority;
                Check(!f.Owner.IsCurrentWaitUntilYield(1002, Backend.WaitChild));
            }
            else
            {
                if (change == "changed") ++f.Backend.Current;
                if (change == "predicate") ++f.Backend.ChildPredicate;
                if (change == "class") f.Backend.ChildClassMatches = false;
                var failed = false;
                try { f.Owner.IsCurrentWaitUntilYield(1002, Backend.WaitChild); }
                catch (Exception) { failed = true; }
                Check(failed);
            }
            Check(f.Owner.Unknown && f.Backend.Roots[4]); Equal(0, f.Backend.RootFreeAttempts[4]);
        }
    }
    private static void Isolation()
    {
        using var f = new Fixture(); var frame = f.Backend.StartFrame(InitializationCoroutineRole.Delay); frame.Self = new(102); f.Backend.StartRaw(frame);
        frame = f.Backend.StartFrame(InitializationCoroutineRole.Delay); frame.Caller = new(Backend.Module + 0x999); f.Backend.StartRaw(frame);
        var move = f.Backend.MoveFrame(InitializationCoroutineRole.Delay); Check(f.Owner.CaptureMove(InitializationCoroutineRole.Delay, move) is null);
        Equal(0, f.Backend.Reads); Equal(0, f.Backend.RootRecords); Equal(2, f.Backend.Originals); Check(!f.Owner.Read().Unknown);
    }
    private static void Worker()
    {
        using var f = new Fixture(); f.Backend.Start(InitializationCoroutineRole.Delay); var reads = f.Backend.Reads;
        var foreign = f.Backend.MoveFrame(InitializationCoroutineRole.WaitUntil);
        Task.Run(() => f.Owner.ObserveFault(foreign, "Foreign worker")).GetAwaiter().GetResult(); Check(!f.Owner.Read().Unknown);
        var owned = f.Backend.MoveFrame(InitializationCoroutineRole.Delay);
        Task.Run(() => f.Owner.ObserveFault(owned, "Owned worker")).GetAwaiter().GetResult();
        Equal(reads, f.Backend.Reads); Equal(1, f.Faults); Check(f.Owner.Read().Unknown);
    }
    private static void InitialMismatch()
    {
        foreach (var stage in new[] { "state", "seconds", "todo", "predicate" })
        {
            using var f = new Fixture(); var index = stage == "predicate" ? 1 : 0; var value = f.Backend.Values[index];
            f.Backend.Values[index] = stage switch { "state" => value with { State = 1 }, "seconds" => value with { SecondsBits = 0x40400000 }, "todo" => value with { Todo = 0 }, _ => value with { Predicate = 0 } };
            f.Backend.Start((InitializationCoroutineRole)index); Check(f.Owner.Read().Unknown && f.Backend.CaptureError is not null); Equal(1, f.Backend.RootRecords); Equal(1, f.Backend.Originals);
        }
    }
    private static void Duplicate()
    {
        using (var f = new Fixture()) { f.Backend.Start(InitializationCoroutineRole.Delay); f.Backend.Start(InitializationCoroutineRole.Delay); Check(f.Owner.Read().Unknown); Equal(2, f.Backend.RootRecords); }
        using (var f = new Fixture()) { f.Backend.Start(InitializationCoroutineRole.Delay); var frame = f.Backend.StartFrame(InitializationCoroutineRole.WaitUntil); frame.Enumerator = new(1001); f.Backend.StartRaw(frame); Check(f.Owner.Read().Unknown); Equal(2, f.Backend.RootRecords); }
        using (var f = new Fixture()) { f.DenyWindow(); f.Backend.Start(InitializationCoroutineRole.Delay); Check(f.Owner.Read().Unknown); Equal(0, f.Backend.RootRecords); }
    }
    private static void PhysicalCompletion()
    {
        using var f = new Fixture(); f.Backend.Start(InitializationCoroutineRole.Delay); f.Backend.Move(InitializationCoroutineRole.Delay, 2, 1);
        f.Backend.Start(InitializationCoroutineRole.WaitUntil); f.Backend.Move(InitializationCoroutineRole.WaitUntil, -1, 0);
        var waited = false; f.Backend.Move(InitializationCoroutineRole.Delay, -1, 0, () => { waited = !f.Owner.TryConfirmEnded(); Check(!f.Owner.Read().Ended); });
        Check(waited && f.Owner.TryConfirmEnded());
    }
    private static void InvalidTransition()
    {
        using (var f = new Fixture()) { f.Backend.Start(InitializationCoroutineRole.Delay); f.Backend.Move(InitializationCoroutineRole.Delay, 2, 1); f.Backend.Move(InitializationCoroutineRole.Delay, 2, 1); Check(f.Owner.Read().Unknown); }
        using (var f = new Fixture()) { f.Backend.Start(InitializationCoroutineRole.WaitUntil); f.Backend.Values[1] = f.Backend.Values[1] with { State = -1 }; f.Backend.Move(InitializationCoroutineRole.WaitUntil, -1, 0); Check(f.Owner.Read().Unknown); }
    }
    private static void OriginalFailure()
    {
        foreach (var start in new[] { false, true })
        {
            using var f = new Fixture(); var expected = new IOException("Original failure");
            if (!start) f.Backend.Start(InitializationCoroutineRole.Delay);
            try { if (start) f.Backend.Start(InitializationCoroutineRole.Delay, () => throw expected); else f.Backend.Move(InitializationCoroutineRole.Delay, -1, 0, () => throw expected); throw new Exception("Failure lost"); }
            catch (IOException actual) { Check(ReferenceEquals(expected, actual)); }
            Check(f.Owner.Read().Unknown && !f.Owner.Read().DelayEnded); Equal(start ? 1 : 2, f.Backend.Originals); Equal(start ? 1 : 2, f.Backend.RootRecords);
        }
    }
    private static void Reentry()
    {
        using var f = new Fixture(); f.Backend.Start(InitializationCoroutineRole.Delay);
        f.Backend.Move(InitializationCoroutineRole.Delay, 0, 1, () => f.Backend.Move(InitializationCoroutineRole.Delay, 2, 1));
        Check(f.Owner.Read().Unknown && f.Backend.CaptureError is not null); Equal(3, f.Backend.Originals); Equal(2, f.Backend.RootRecords);
    }
    private static void ChangedCallbacks()
    {
        using (var f = new Fixture()) { f.Backend.Start(InitializationCoroutineRole.Delay, () => f.Backend.Values[0] = f.Backend.Values[0] with { Todo = 9001 }); Check(f.Owner.Read().Unknown); Equal(1, f.Backend.RootRecords); }
        using (var f = new Fixture()) { f.Finish(); f.Backend.Values[1] = f.Backend.Values[1] with { Predicate = 9001 }; Throws<NativeLocalInitializationCoroutineException>(() => f.Owner.TryConfirmEnded()); Check(f.Owner.Read().Unknown); }
    }
    private static void RootFailures()
    {
        using (var main = new MainThreadDispatcher())
        {
            var backend = new Backend(main) { FailInitialize = true };
            try { NativeLocalInitializationCoroutineTracker.CaptureBackend(main, backend, new Authority(), Backend.Module); throw new Exception("Failure lost"); }
            catch (NativeLocalInitializationCoroutineException error) { Check(error.Responsibility.Read().Unknown); Equal(0, backend.FreeCalls); }
        }
        foreach (var stage in new[] { "retain", "read", "coroutine" })
        {
            using var f = new Fixture(); f.Backend.FailRetain = stage == "retain"; f.Backend.FailRead = stage == "read"; f.Backend.FailCoroutine = stage == "coroutine";
            f.Backend.Start(InitializationCoroutineRole.Delay); Check(f.Owner.Read().Unknown); var roots = f.Backend.RootRecords;
            Throws<InvalidOperationException>(() => f.Owner.TryReleaseAfterEnded()); Equal(roots, f.Backend.RootRecords); Equal(0, f.Backend.FreeCalls);
        }
        using (var f = new Fixture()) { f.Finish(); f.Backend.FailFree = true; Throws<NativeLocalInitializationCoroutineException>(() => f.Owner.TryReleaseAfterEnded()); Equal(1, f.Backend.RootRecords); Equal(1, f.Backend.FreeCalls); Check(f.Backend.RootFreeAttempts.SequenceEqual(new[] { 1, 1, 1, 1, 0 })); Throws<InvalidOperationException>(() => f.Owner.TryReleaseAfterEnded()); Equal(1, f.Backend.FreeCalls); }
    }
    private static void NullHandle()
    {
        using (var f = new Fixture()) { f.Backend.Start(InitializationCoroutineRole.WaitUntil, () => f.Backend.Move(InitializationCoroutineRole.WaitUntil, -1, 0), 0); Check(!f.Owner.Read().Unknown); Equal(1, f.Backend.RootRecords); f.Finish(); }
        using (var f = new Fixture()) { f.Backend.Start(InitializationCoroutineRole.WaitUntil, coroutine: 0); Check(f.Owner.Read().Unknown); Equal(1, f.Backend.RootRecords); }
    }
    private static void ActivityAndThread()
    {
        using (var f = new Fixture())
        {
            Task.Run(() => Throws<InvalidOperationException>(() => f.Owner.TryConfirmEnded())).GetAwaiter().GetResult(); Equal(0, f.Backend.Reads);
            f.Finish(); f.Backend.InFlight = 1; Check(!f.Owner.TryConfirmEnded()); f.Backend.InFlight = 0; Check(f.Owner.TryConfirmEnded());
            f.Backend.Known = false; Throws<NativeLocalInitializationCoroutineException>(() => f.Owner.TryConfirmEnded()); f.Backend.Known = true;
            Check(f.Owner.Read().Unknown); Throws<NativeLocalInitializationCoroutineException>(() => f.Owner.TryConfirmEnded());
        }
        using (var f = new Fixture()) { f.Finish(); f.Backend.DuringDetach = () => f.Backend.InFlight = 1; Throws<NativeLocalInitializationCoroutineException>(() => f.Owner.TryReleaseAfterEnded()); Equal(0, f.Backend.FreeCalls); Equal(4, f.Backend.RootRecords); }
        using (var f = new Fixture()) { f.Finish(); f.Backend.InFlight = -1; Throws<NativeLocalInitializationCoroutineException>(() => f.Owner.TryConfirmEnded()); Check(f.Owner.Read().Unknown); }
    }
    private static void Untouched()
    { using var f = new Fixture(); Check(f.Owner.TryReleaseBeforeStart()); Check(f.Owner.Read().Closed && !f.Owner.Read().Ended); Equal(1, f.Backend.DetachCalls); Equal(1, f.Backend.FreeCalls); Throws<InvalidOperationException>(() => f.Owner.TryConfirmEnded()); }
    private static void ExternalAuthority()
    {
        using var f = new Fixture(); f.Finish(); var reads = f.Backend.Reads; var activity = f.Backend.ActivityReads;
        Task.Run(f.FaultExternalAuthority).GetAwaiter().GetResult();
        Check(f.Owner.ObservationAuthorityUnknown && !f.Owner.ObservationUnknown);
        Check(f.Owner.Read().Unknown && !f.Owner.Read().Ended && !f.Owner.Read().Closed); Equal(0, f.Faults);
        Throws<InvalidOperationException>(() => f.Owner.TryReleaseAfterEnded());
        Equal(reads, f.Backend.Reads); Equal(activity, f.Backend.ActivityReads); Equal(0, f.Backend.DetachCalls); Equal(0, f.Backend.FreeCalls);
    }
    private static void FaultDuringStart()
    {
        foreach (var role in new[] { InitializationCoroutineRole.Delay, InitializationCoroutineRole.WaitUntil })
        {
            using var f = new Fixture();
            var frame = f.Backend.StartFrame(role);
            int readsBeforeReturn = 0;
            f.Backend.Start(role, () =>
            {
                readsBeforeReturn = f.Backend.Reads;
                f.FaultExternalAuthority();
            }, coroutine: 0x1234);
            var detail = f.Backend.AfterError?.Message ?? "";
            Check(detail.Contains($"role={role} managedCaller=0x{frame.Caller:x} managedReceiver=0x{frame.Self:x} "
                + $"managedEnumerator=0x{frame.Enumerator:x} returnedHandle=0x1234.", StringComparison.Ordinal));
            Check(f.Backend.AfterError?.InnerException is InvalidOperationException && f.Owner.Read().Unknown);
            Equal(detail, f.Owner.Read().Failure ?? "");
            Equal(1, f.Backend.Originals);
            Equal(readsBeforeReturn, f.Backend.Reads);
            Equal(1, f.Backend.RootRecords);
            Equal(0, f.Backend.FreeCalls);
        }
    }
    private static void ExternalReadAndActivity()
    {
        foreach (var duringRead in new[] { false, true })
        {
            using var f = new Fixture(); f.Finish(); var reads = f.Backend.Reads;
            if (duringRead) f.Backend.DuringRead = f.FaultExternalAuthority;
            else f.Backend.DuringActivity = f.FaultExternalAuthority;
            Throws<NativeLocalInitializationCoroutineException>(() => f.Owner.TryConfirmEnded());
            Equal(reads + (duringRead ? 1 : 0), f.Backend.Reads);
            Check(f.Owner.Read().Unknown && !f.Owner.Read().Ended); Equal(4, f.Backend.RootRecords); Equal(0, f.Backend.FreeCalls);
        }
    }
    private static void ExternalDetachAndFree()
    {
        foreach (var duringFree in new[] { false, true })
        {
            using var f = new Fixture(); f.Finish(); var activityAtDetach = -1;
            if (duringFree) f.Backend.DuringFree = _ => f.FaultExternalAuthority();
            else f.Backend.DuringDetach = () => { activityAtDetach = f.Backend.ActivityReads; f.FaultExternalAuthority(); };
            Throws<NativeLocalInitializationCoroutineException>(() => f.Owner.TryReleaseAfterEnded());
            Check(f.Owner.Read().Unknown && !f.Owner.Read().Closed); Equal(1, f.Backend.DetachCalls);
            Equal(duringFree ? 1 : 0, f.Backend.FreeCalls); Equal(duringFree ? 3 : 4, f.Backend.RootRecords);
            Equal(duringFree ? 1 : 0, f.Backend.RootFreeAttempts.Sum());
            if (duringFree) { Check(!f.Backend.Roots[3] && f.Backend.Roots.Take(3).All(value => value)); }
            else Equal(activityAtDetach, f.Backend.ActivityReads);
            var attempts = f.Backend.RootFreeAttempts.Sum();
            Throws<InvalidOperationException>(() => f.Owner.TryReleaseAfterEnded()); Equal(attempts, f.Backend.RootFreeAttempts.Sum());
        }
    }
    private static void ExternalRetain()
    {
        using var f = new Fixture(); f.Backend.AfterRetain = f.FaultExternalAuthority;
        f.Backend.Start(InitializationCoroutineRole.Delay);
        Check(f.Owner.Read().Unknown && f.Backend.CaptureError is not null);
        Equal(1, f.Backend.RootRecords); Equal(0, f.Backend.Reads); Equal(1, f.Backend.Originals); Equal(0, f.Backend.FreeCalls);
    }
}
