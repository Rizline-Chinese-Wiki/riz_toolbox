using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime.Hooks;
using static Assert;

internal static class NativeLocalTerminalCoroutineRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Terminal named Cutout publishes its actual factory enum before synchronous first Move and waits for natural false", Cutout),
        ("Terminal Fade factories retain actual independent enums and can be started through the IEnumerator overload", Fade),
        ("Terminal observers ignore unrelated receivers and enum tokens without native object reads", Isolation),
        ("Terminal nested factories are independent and foreign named children cannot borrow outer factory association", Nested),
        ("Terminal named Cutout requires the exact ControllerOver call site and live result claim window", Source),
        ("Terminal factories reject null reused wrong-controller and nonzero-initial-state outputs", InvalidFactory),
        ("Terminal enum worker faults use scalar identity and never read native objects", Worker),
        ("Terminal physical reentry and original exceptions retain exact contexts without false completion", Failures),
        ("Terminal known yield paths reject impossible Cutout and Fade state transitions", States),
        ("Terminal observed-end queries wait for physical cleanup and never revoke future producers", ObservedOnly),
        ("Terminal records stop at sixteen unretired slots before acquiring another root", Capacity),
        ("Terminal unknown roots and activity preserve bounded responsibility without cleanup retry", RootAndActivity),
        ("Terminal unused observers can detach only before activation and reject wrong-thread work", Untouched),
        ("Terminal external launch or source faults stop post-root reads terminal publication and independent cleanup", ExternalAuthorityFault)
    };
    internal sealed class Authority : ITerminalCoroutineAuthority
    {
        internal bool Before = true, Allowed = true, Claim = true; internal int Faults, ExternalFault;
        public bool Unknown => Volatile.Read(ref ExternalFault) != 0;
        public long Controller { get; }
        internal Authority(long controller) => Controller = controller;
        public void VerifyBeforeCapture() { if (!Before) throw new InvalidOperationException("Already activated"); }
        public void VerifyObservation() { if (!Allowed) throw new InvalidOperationException("Wrong protected controller"); }
        public void VerifyResultClaim() { VerifyObservation(); if (!Claim) throw new InvalidOperationException("No result claim"); }
        public void ObserveFaultFromAnyThread() => Interlocked.Increment(ref Faults);
    }
    internal sealed class Fixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new();
        internal readonly Authority Authority;
        internal readonly Backend Backend;
        internal readonly NativeLocalTerminalCoroutineTracker Owner;
        internal Fixture(long controller = 101)
        { Authority = new(controller); Backend = new(Main, controller); Owner = NativeLocalTerminalCoroutineTracker.CaptureBackend(Main, Backend, Authority, Backend.Module); }
        internal void CaptureCutout()
        {
            Authority.Before = false;
            using var result = Owner.BeginControllerOver(); Backend.Named(TerminalCoroutineRole.Cutout); result.Complete();
        }
        public void Dispose()
        {
            try { var state = Owner.Read(); if (!state.Unknown && !state.Closed && state.Created == 0 && !state.ResultAttempted) { Authority.Before = true; Owner.TryReleaseUnusedBeforeActivation(); } }
            finally { Main.Dispose(); }
        }
    }
    internal sealed class Backend : ITerminalCoroutineBackend
    {
        internal const long Module = 0x20000000;
        private readonly MainThreadDispatcher _main;
        internal readonly long Controller;
        internal NativeLocalTerminalCoroutineTracker Owner = null!;
        internal readonly Dictionary<long, TerminalCoroutineObservation> Values = new();
        internal readonly Dictionary<int, long> Slots = new();
        internal long Next = 1000, LastEnum;
        internal int Roots, Reads, Originals, InFlight, Detaches, Retains, ActivityReads, FreeCalls;
        internal bool Known = true, FailEnum, FailCoroutine;
        internal readonly bool[] RootSlots = new bool[32];
        internal readonly HashSet<int> FailRootFree = new();
        internal readonly List<int> Frees = new();
        internal Action<int>? DuringActivity, DuringFree;
        internal Action? DuringRead, DuringRetain;
        internal Exception? CaptureError, AfterError;
        public bool CleanupUnknown { get; private set; }
        public int RootRecords => Roots;
        internal Backend(MainThreadDispatcher main, long controller) => (_main, Controller) = (main, controller);
        public void Initialize(NativeLocalTerminalCoroutineTracker owner) => Owner = owner;
        public string ReadMethodName(long name) { _main.VerifyAccess(); Reads++; return name == 1 ? "Cutout" : name == 2 ? "FadeOutScoreDisplay" : "foreign"; }
        public void RetainEnumerator(int slot, TerminalCoroutineRole role, long enumerator)
        { Retains++; Roots++; RootSlots[slot] = true; Slots[slot] = enumerator; DuringRetain?.Invoke(); if (FailEnum) throw new IOException("Unknown enum root"); }
        public TerminalCoroutineObservation ReadEnumerator(int slot, TerminalCoroutineRole role)
        { _main.VerifyAccess(); Reads++; DuringRead?.Invoke(); return Values[Slots[slot]]; }
        public void RetainCoroutine(int slot, long coroutine) { Roots++; RootSlots[slot+16] = true; if (FailCoroutine) throw new IOException("Unknown Coroutine root"); }
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity() { ++ActivityReads; DuringActivity?.Invoke(ActivityReads); return new(InFlight, Known && !CleanupUnknown); }
        public void DetachAndReleaseUnused() { Detaches++; Check(Roots == 0); }
        public void ReleaseEndedRoots(int slot, long enumerator, long generation)
        {
            Check(Owner.IsRetiringForCleanup(slot, enumerator, generation) && Slots[slot] == enumerator); FreeCalls++;
            foreach (var index in new[] { slot+16, slot })
            {
                if (!RootSlots[index]) continue;
                Owner.RequireEngineRetirementFree(slot, enumerator, generation);
                if (Owner.ObservationAuthorityUnknown || InFlight != 0 || !Known) { CleanupUnknown = true; throw new IOException("Activity changed during free"); }
                Frees.Add(index); DuringFree?.Invoke(index);
                if (FailRootFree.Contains(index)) { CleanupUnknown = true; continue; }
                RootSlots[index] = false; Roots--;
                Owner.RequireEngineRetirementFree(slot, enumerator, generation);
                if (Owner.ObservationAuthorityUnknown) { CleanupUnknown = true; throw new IOException("External authority faulted during a root free"); }
            }
            if (CleanupUnknown) throw new IOException("Unknown independent root free");
            Slots.Remove(slot);
        }
        internal NativeCoroutineFrame Frame(NativeCoroutineKind kind, long self, long argument = 0, long caller = 0)
            => new() { Version = 1, Size = 48, Kind = kind, Self = new(self), Enumerator = new(argument), Method = new(0x123), Caller = new(caller == 0 ? Module + 0x999 : caller) };
        internal void Invoke(Func<NativeLocalTerminalCoroutineTracker.Context?> capture, Action<NativeLocalTerminalCoroutineTracker.Context> after, Action? original = null)
        {
            NativeLocalTerminalCoroutineTracker.Context? context = null; InFlight++;
            try { try { context = capture(); } catch (Exception error) { CaptureError = error; } Originals++; original?.Invoke();
                if (context is not null) try { after(context); } catch (Exception error) { AfterError = error; } }
            finally { try { context?.Dispose(); } finally { InFlight--; } }
        }
        internal long Factory(TerminalCoroutineRole role, long? returned = null, int initialState = 0, long? owner = null, Action? original = null)
        {
            var pointer = returned ?? ++Next; if (pointer != 0) Values[pointer] = new(pointer, owner ?? Controller, initialState);
            var frame = Frame(NativeCoroutineKind.Factory, Controller); LastEnum = pointer;
            Invoke(() => Owner.CaptureFactory(role, frame), context => Owner.CompleteFactory(context, frame, unchecked((nuint)pointer)), original); return pointer;
        }
        internal void Named(TerminalCoroutineRole role, bool firstMove = true, nuint? coroutine = null, long? caller = null, Action? duringMove = null)
        {
            var frame = Frame(NativeCoroutineKind.Start, Controller, role == TerminalCoroutineRole.Cutout ? 1 : 2, caller ?? Module + 0x3BEAAA);
            Invoke(() => Owner.CaptureNamed(frame), context => Owner.CompleteStart(context, frame, coroutine ?? 5001), () => {
                var value = Factory(role); if (firstMove) Move(role, value, 1, 1, duringMove);
            });
        }
        internal void EnumStart(TerminalCoroutineRole role, long enumerator, Action? original = null)
        { var frame = Frame(NativeCoroutineKind.Start, Controller, enumerator); Invoke(() => Owner.CaptureEnumStart(frame), context => Owner.CompleteStart(context, frame, 5002), original); }
        internal void Move(TerminalCoroutineRole role, long enumerator, int state, nuint result, Action? original = null)
        {
            var frame = Frame(NativeCoroutineKind.MoveNext, enumerator);
            Invoke(() => Owner.CaptureMove(role, frame), context => Owner.CompleteMove(context, frame, result), () => { Values[enumerator] = Values[enumerator] with { State = state }; original?.Invoke(); });
        }
    }
    private static void Cutout()
    {
        using var f = new Fixture(); f.CaptureCutout(); var value = f.Backend.LastEnum;
        Check(f.Owner.Read().ResultReturned && f.Owner.Read().CutoutNamedReturned && !f.Owner.TryConfirmObservedEnded()); Equal(2, f.Backend.Roots);
        var waited = false; f.Backend.Move(TerminalCoroutineRole.Cutout, value, -1, 0, () => waited = !f.Owner.TryConfirmObservedEnded());
        Check(waited && f.Owner.TryConfirmObservedEnded()); Equal(1, f.Owner.Read().NaturallyEnded);
        Throws<InvalidOperationException>(() => f.Owner.TryReleaseUnusedBeforeActivation()); Equal(2, f.Backend.Roots);
    }
    private static void Fade()
    {
        using var f = new Fixture(); var value = f.Backend.Factory(TerminalCoroutineRole.Fade);
        f.Backend.EnumStart(TerminalCoroutineRole.Fade, value, () => f.Backend.Move(TerminalCoroutineRole.Fade, value, 1, 0x80));
        f.Backend.Move(TerminalCoroutineRole.Fade, value, 1, 0xff); Check(!f.Owner.TryConfirmObservedEnded());
        f.Backend.Move(TerminalCoroutineRole.Fade, value, -1, 0); Check(f.Owner.TryConfirmObservedEnded()); Equal(2, f.Backend.Roots);
    }
    private static void Isolation()
    {
        using var f = new Fixture(); var frame = f.Backend.Frame(NativeCoroutineKind.Start, 102, 1);
        Check(f.Owner.CaptureNamed(frame) is null); frame.Kind = NativeCoroutineKind.Factory; frame.Enumerator = IntPtr.Zero;
        Check(f.Owner.CaptureFactory(TerminalCoroutineRole.Cutout, frame) is null);
        Check(f.Owner.CaptureMove(TerminalCoroutineRole.Cutout, f.Backend.Frame(NativeCoroutineKind.MoveNext, 2001)) is null);
        Equal(0, f.Backend.Reads); Equal(0, f.Backend.Roots);
    }
    private static void Nested()
    {
        using (var f = new Fixture())
        {
            using (var window = f.Owner.BeginControllerOver())
            {
                f.Backend.Named(TerminalCoroutineRole.Cutout, duringMove: () => {
                    var fade = f.Backend.Factory(TerminalCoroutineRole.Fade); f.Backend.Move(TerminalCoroutineRole.Fade, fade, 1, 1); f.Backend.Move(TerminalCoroutineRole.Fade, fade, -1, 0);
                });
                window.Complete();
            }
            Check(!f.Owner.Read().Unknown && f.Owner.Read().Created == 2 && f.Owner.Read().NaturallyEnded == 1);
        }
        using (var f = new Fixture())
        {
            var outer = f.Backend.Frame(NativeCoroutineKind.Start, f.Backend.Controller, 2);
            var nested = f.Backend.Frame(NativeCoroutineKind.Start, 9999, 0);
            var beforeReads = 0;
            f.Backend.Invoke(() => f.Owner.CaptureNamed(outer), context => f.Owner.CompleteStart(context, outer, 5001), () => {
                beforeReads = f.Backend.Reads;
                f.Backend.Invoke(() => f.Owner.CaptureNamed(nested), context => f.Owner.CompleteStart(context, nested, 5002), () => f.Backend.Factory(TerminalCoroutineRole.Fade));
                Check(f.Backend.Reads == beforeReads + 1); // Only the actual enum was read, never the foreign name.
            });
            Check(f.Owner.Read().Unknown && f.Backend.AfterError is not null); // No child factory can satisfy the outer named Start.
            Equal(1, f.Owner.Read().Created); Equal(1, f.Backend.Roots);
        }
    }
    private static void Source()
    {
        using (var f = new Fixture()) { f.Backend.Named(TerminalCoroutineRole.Cutout); Check(f.Owner.Read().Unknown); Equal(0, f.Backend.Roots); }
        using (var f = new Fixture()) { using var result = f.Owner.BeginControllerOver(); f.Backend.Named(TerminalCoroutineRole.Cutout, caller: Backend.Module + 0x111); Check(f.Owner.Read().Unknown); }
        using (var f = new Fixture()) { f.Authority.Claim = false; Throws<InvalidOperationException>(() => f.Owner.BeginControllerOver()); Check(!f.Owner.Read().ResultAttempted); }
    }
    private static void InvalidFactory()
    {
        foreach (var stage in new[] { "null", "owner", "state" })
        {
            using var f = new Fixture(); f.Backend.Factory(TerminalCoroutineRole.Fade, stage == "null" ? 0 : null, stage == "state" ? 1 : 0, stage == "owner" ? 999 : null);
            Check(f.Owner.Read().Unknown); Equal(stage == "null" ? 0 : 1, f.Backend.Roots);
        }
        using var duplicate = new Fixture(); var value = duplicate.Backend.Factory(TerminalCoroutineRole.Fade); duplicate.Backend.Factory(TerminalCoroutineRole.Fade, value);
        Check(duplicate.Owner.Read().Unknown); Equal(1, duplicate.Backend.Roots);
    }
    private static void Worker()
    {
        using var f = new Fixture(); var value = f.Backend.Factory(TerminalCoroutineRole.Fade); var reads = f.Backend.Reads;
        Task.Run(() => f.Owner.ObserveFault(f.Backend.Frame(NativeCoroutineKind.MoveNext, 9999), "foreign worker")).GetAwaiter().GetResult(); Check(!f.Owner.Read().Unknown);
        Task.Run(() => f.Owner.ObserveFault(f.Backend.Frame(NativeCoroutineKind.MoveNext, value), "owned worker")).GetAwaiter().GetResult();
        Equal(reads, f.Backend.Reads); Check(f.Owner.Read().Unknown); Equal(1, f.Authority.Faults);
    }
    private static void Failures()
    {
        using (var f = new Fixture())
        { var value = f.Backend.Factory(TerminalCoroutineRole.Fade); f.Backend.Move(TerminalCoroutineRole.Fade, value, 0, 1, () => f.Backend.Move(TerminalCoroutineRole.Fade, value, 1, 1)); Check(f.Owner.Read().Unknown && f.Backend.CaptureError is not null); Equal(3, f.Backend.Originals); }
        foreach (var stage in new[] { "factory", "start", "move" })
        {
            using var f = new Fixture(); var expected = new IOException("Terminal original failure"); var value = stage == "factory" ? 0 : f.Backend.Factory(TerminalCoroutineRole.Fade);
            try
            {
                if (stage == "factory") f.Backend.Factory(TerminalCoroutineRole.Fade, original: () => throw expected);
                else if (stage == "start") f.Backend.EnumStart(TerminalCoroutineRole.Fade, value, () => throw expected);
                else f.Backend.Move(TerminalCoroutineRole.Fade, value, -1, 0, () => throw expected);
                throw new Exception("Original exception lost");
            }
            catch (IOException actual) { Check(ReferenceEquals(actual, expected)); }
            Check(f.Owner.Read().Unknown); Equal(stage == "factory" ? 0 : 1, f.Backend.Roots);
        }
    }
    private static void States()
    {
        using (var f = new Fixture()) { f.CaptureCutout(); f.Backend.Move(TerminalCoroutineRole.Cutout, f.Backend.LastEnum, 1, 1); Check(f.Owner.Read().Unknown); }
        using (var f = new Fixture()) { var value = f.Backend.Factory(TerminalCoroutineRole.Fade); f.Backend.Move(TerminalCoroutineRole.Fade, value, 2, 1); Check(f.Owner.Read().Unknown); }
    }
    private static void ObservedOnly()
    {
        using var f = new Fixture(); Check(f.Owner.TryConfirmObservedEnded()); var value = f.Backend.Factory(TerminalCoroutineRole.Fade);
        Check(!f.Owner.TryConfirmObservedEnded()); f.Backend.Move(TerminalCoroutineRole.Fade, value, 1, 1); f.Backend.Move(TerminalCoroutineRole.Fade, value, -1, 0); Check(f.Owner.TryConfirmObservedEnded());
        var another = f.Backend.Factory(TerminalCoroutineRole.Fade); Check(another != value && !f.Owner.TryConfirmObservedEnded());
    }
    private static void Capacity()
    {
        using var f = new Fixture();
        for (var index = 0; index < NativeLocalTerminalCoroutineTracker.MaximumRecords; ++index)
        { var value = f.Backend.Factory(TerminalCoroutineRole.Fade); f.Backend.Move(TerminalCoroutineRole.Fade, value, 1, 1); f.Backend.Move(TerminalCoroutineRole.Fade, value, -1, 0); }
        Check(f.Owner.TryConfirmObservedEnded()); Equal(16, f.Backend.Retains); f.Backend.Factory(TerminalCoroutineRole.Fade);
        Equal(16, f.Backend.Retains); Check(f.Owner.Read().Unknown); Equal(16, f.Backend.Roots);
    }
    private static void RootAndActivity()
    {
        using (var f = new Fixture()) { f.Backend.FailEnum = true; f.Backend.Factory(TerminalCoroutineRole.Fade); Check(f.Owner.Read().Unknown); Equal(1, f.Backend.Roots); Throws<InvalidOperationException>(() => f.Owner.TryReleaseUnusedBeforeActivation()); }
        using (var f = new Fixture()) { f.Backend.FailCoroutine = true; Throws<InvalidOperationException>(() => f.CaptureCutout()); Check(f.Owner.Read().Unknown); Equal(2, f.Backend.Roots); }
        using (var f = new Fixture()) { f.Backend.Known = false; Throws<NativeLocalTerminalCoroutineException>(() => f.Owner.TryConfirmObservedEnded()); f.Backend.Known = true; Check(f.Owner.Read().Unknown); }
    }
    private static void Untouched()
    {
        using var f = new Fixture(); Task.Run(() => Throws<InvalidOperationException>(() => f.Owner.BeginControllerOver())).GetAwaiter().GetResult(); Equal(0, f.Backend.Reads);
        f.Authority.Before = false; Throws<InvalidOperationException>(() => f.Owner.TryReleaseUnusedBeforeActivation()); f.Authority.Before = true;
        Check(f.Owner.TryReleaseUnusedBeforeActivation()); Equal(1, f.Backend.Detaches); Check(f.Owner.Read().Closed);
    }
    private static void ExternalAuthorityFault()
    {
        foreach (var stage in new[] { "root", "read", "stats", "query-first", "false", "free", "final-drain" })
        {
            using var f = new Fixture();
            void WorkerFault() => Task.Run(() => Interlocked.Exchange(ref f.Authority.ExternalFault, 1)).GetAwaiter().GetResult();
            if (stage == "root")
            {
                f.Backend.DuringRetain = WorkerFault; f.Backend.Factory(TerminalCoroutineRole.Fade);
                Check(f.Owner.Read().Unknown); Equal(1, f.Backend.Roots); Equal(0, f.Backend.Reads); Equal(0, f.Owner.Read().NaturallyEnded);
                continue;
            }
            var value = f.Backend.Factory(TerminalCoroutineRole.Fade); f.Backend.EnumStart(TerminalCoroutineRole.Fade, value);
            f.Backend.Move(TerminalCoroutineRole.Fade, value, 1, 1);
            if (stage == "false")
            {
                var previous = f.Backend.Reads;
                f.Backend.DuringRead = () => { if (f.Backend.Reads == previous + 2) WorkerFault(); };
                f.Backend.Move(TerminalCoroutineRole.Fade, value, -1, 0);
                Check(f.Owner.Read().Unknown); Equal(0, f.Owner.Read().NaturallyEnded); Equal(2, f.Backend.Roots);
                continue;
            }
            f.Backend.Move(TerminalCoroutineRole.Fade, value, -1, 0);
            if (stage == "query-first")
            {
                var second = f.Backend.Factory(TerminalCoroutineRole.Fade); f.Backend.Move(TerminalCoroutineRole.Fade, second, 1, 1);
                f.Backend.Move(TerminalCoroutineRole.Fade, second, -1, 0);
            }
            var reads = f.Backend.Reads;
            f.Backend.ActivityReads = 0;
            if (stage is "read" or "query-first") f.Backend.DuringRead = WorkerFault;
            if (stage == "stats") f.Backend.DuringActivity = _ => WorkerFault();
            if (stage == "free") f.Backend.DuringFree = _ => WorkerFault();
            if (stage == "final-drain") f.Backend.DuringActivity = index => { if (index == 3) WorkerFault(); };
            if (stage == "query-first") Throws<NativeLocalTerminalCoroutineException>(() => f.Owner.TryConfirmObservedEnded());
            else Throws<NativeLocalTerminalCoroutineException>(() => f.Owner.RecycleNaturallyEnded());
            Check(f.Owner.Read().Unknown); Equal(0, f.Owner.Read().RetiredRecords);
            Equal(stage == "query-first" ? 2 : 1, f.Owner.Read().LiveRecords);
            if (stage is "read" or "query-first") Equal(reads + 1, f.Backend.Reads);
            if (stage == "stats") Equal(reads, f.Backend.Reads);
            if (stage == "free") { Check(f.Backend.Frees.SequenceEqual(new[] { 16 })); Equal(1, f.Backend.Roots); }
            if (stage == "final-drain") { Equal(0, f.Backend.Roots); Equal(1, f.Backend.FreeCalls); }
            var frees = f.Backend.FreeCalls; Throws<InvalidOperationException>(() => f.Owner.RecycleNaturallyEnded()); Equal(frees, f.Backend.FreeCalls);
        }
    }
}
