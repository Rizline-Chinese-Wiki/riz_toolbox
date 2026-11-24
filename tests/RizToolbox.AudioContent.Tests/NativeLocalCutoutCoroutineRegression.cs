using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime.Hooks;
using static Assert;

/// <summary>Controlled state-machine and fault seams. These cases do not execute
/// Unity or certify the production native transport, delegate metadata or listeners.</summary>
internal static class NativeLocalCutoutCoroutineRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Cutout coroutine Start requires the actual dispatch generation and exact caller while foreign hosts stay isolated", DispatchAndCaller),
        ("Cutout typed Start and Auto observations publish the real enum before synchronous child Move", SynchronousFirstMove),
        ("Cutout reset Delay accepts zero or negative synchronous natural false and null Coroutine only after completion", SynchronousReset),
        ("Cutout reset Delay supports provider state one with changing seconds and WaitForSeconds state two", DelayBranches),
        ("Cutout event coroutine requires zero to one to natural false and preserves raw byte boolean results", EventStates),
        ("Cutout coroutine fields cannot change to another captured Trigger or change Action outside their native path", FieldIdentity),
        ("Cutout source and native entry activity defer observed-end and recycling before any enum read", PhysicalDrain),
        ("Cutout owned worker faults use only scalar identities and cannot spend a native read", Worker),
        ("Cutout same-enum reentry and missing original after callbacks preserve responsibility", ReentryAndMissingAfter),
        ("Cutout coroutine capacity stops before acquiring the sixty-fifth live record", Capacity),
        ("Cutout natural roots recycle beyond sixty-four starts with raw address reuse and role changes", Reuse),
        ("Cutout withdrawal retains roots while a new native entry drains and resumes without republishing", WithdrawalWait),
        ("Cutout withdrawn main or worker reentry revokes cleanup before any independent root free", WithdrawalReentry),
        ("Cutout unknown independent root free still attempts other known roots once and forbids slot reuse", IndependentFreeFailure),
        ("Cutout final native drain worker faults cannot report successful recycling or make a spent slot reusable", FinalDrainFault),
        ("Cutout source faults inside native enum reads stop publication completion and the next record read", SourceFaultDuringRead),
        ("Cutout activity and root publication failures retain bounded responsibility without cleanup retry", PublicationAndActivity)
    };

    private static void DispatchAndCaller()
    {
        using (var f = new Fixture())
        {
            var reads = f.Backend.Reads;
            var foreign = f.Backend.Frame(NativeCoroutineKind.Start, 999, 4000, Backend.Module + 0x38A6B2);
            Check(f.Owner.CaptureStart(foreign) is null);
            Check(f.Owner.CaptureMove(CutoutCoroutineRole.DelayedEvent, f.Backend.Frame(NativeCoroutineKind.MoveNext, 999)) is null);
            Equal(reads, f.Backend.Reads); Equal(0, f.Backend.Retains); Check(!f.Owner.IsFor(null!));
            f.Backend.Start(CutoutCoroutineRole.DelayedEvent); // No actual StartEvents entry.
            Check(f.Owner.Read().Unknown); Equal(0, f.Backend.Retains); Equal(1, f.Backend.Originals);
        }
        using (var f = new Fixture())
        {
            f.Authority.Dispatch(() => f.Backend.Start(CutoutCoroutineRole.DelayedEvent, caller: Backend.Module + 0x3AE3D5));
            Check(f.Owner.Read().Unknown); Equal(0, f.Backend.Retains); Equal(1, f.Backend.Originals);
        }
        using (var f = new Fixture())
        {
            f.Authority.Dispatch(() => f.Backend.Start(CutoutCoroutineRole.ResetDelay,
                original: _ => f.Authority.Generation++));
            Check(f.Owner.Read().Unknown); Check(f.Backend.AfterError is not null); Equal(1, f.Backend.RootRecords);
        }
        using (var f = new Fixture())
        {
            var frame = f.Backend.Frame(NativeCoroutineKind.Start, f.Authority.Host, 4000, Backend.Module + 0x38A6B2);
            frame.Version = 2;
            f.Authority.Dispatch(() => Throws<InvalidOperationException>(() => f.Owner.CaptureStart(frame)));
            Equal(0, f.Backend.Retains); Check(f.Owner.Read().Unknown);
        }
    }

    private static void SynchronousFirstMove()
    {
        foreach (var alias in new[] { false, true })
        {
            using var f = new Fixture(); var observedPublished = false;
            f.Authority.Dispatch(() => f.Backend.Start(CutoutCoroutineRole.DelayedEvent, alias: alias, original: value => {
                Equal(1, f.Backend.Retains); Equal(1, f.Backend.RootRecords);
                Check(!f.Owner.TryConfirmObservedEnded());
                f.Backend.Move(CutoutCoroutineRole.DelayedEvent, value, 1, 0x80,
                    original: () => observedPublished = f.Backend.InFlight == 2);
            }));
            f.NoErrors(); Check(observedPublished); Equal(2, f.Backend.RootRecords);
            Equal(2, f.Backend.Originals); Equal(0, f.Backend.InFlight); Equal(1L, f.Authority.Entered);
            f.Backend.Move(CutoutCoroutineRole.DelayedEvent, f.Backend.LastEnum, -1, 0);
            f.NoErrors(); Check(f.Owner.TryConfirmObservedEnded()); Equal(1, f.Owner.Read().NaturallyEnded);
            Equal(1, f.Owner.RecycleNaturallyEnded()); Equal(0, f.Backend.RootRecords);
        }
    }

    private static void SynchronousReset()
    {
        foreach (var seconds in new[] { 0f, -1f })
        {
            using var f = new Fixture(seconds); var readInsideOriginal = false;
            f.Authority.Dispatch(() => f.Backend.Start(CutoutCoroutineRole.ResetDelay, coroutine: 0, original: value => {
                f.Backend.Move(CutoutCoroutineRole.ResetDelay, value, -1, 0,
                    original: () => readInsideOriginal = !f.Owner.TryConfirmObservedEnded());
                Equal(0, f.Owner.RecycleNaturallyEnded());
            }));
            f.NoErrors(); Check(readInsideOriginal); Equal(1, f.Backend.RootRecords); Equal(0, f.Backend.CoroutineRetains);
            Check(f.Owner.TryConfirmObservedEnded()); Equal(1, f.Owner.RecycleNaturallyEnded()); Equal(0, f.Backend.RootRecords);
        }
        using var pending = new Fixture(); pending.Authority.Dispatch(() => pending.Backend.Start(CutoutCoroutineRole.ResetDelay, coroutine: 0));
        Check(pending.Owner.Read().Unknown); Check(pending.Backend.AfterError is not null); Equal(1, pending.Backend.RootRecords);
    }

    private static void DelayBranches()
    {
        using (var f = new Fixture())
        {
            f.Authority.Dispatch(() => f.Backend.Start(CutoutCoroutineRole.ResetDelay, original: value =>
                f.Backend.Move(CutoutCoroutineRole.ResetDelay, value, 1, 1, seconds: 2f, provider: 700)));
            var value = f.Backend.LastEnum;
            f.Backend.Move(CutoutCoroutineRole.ResetDelay, value, 1, 0xFF, seconds: 0.5f, provider: 700);
            Check(!f.Owner.TryConfirmObservedEnded());
            f.Backend.Move(CutoutCoroutineRole.ResetDelay, value, -1, 0, seconds: -0.1f, provider: 0);
            f.NoErrors(); Check(f.Owner.TryConfirmObservedEnded()); Equal(1, f.Owner.RecycleNaturallyEnded());
        }
        using (var f = new Fixture())
        {
            f.Authority.Dispatch(() => f.Backend.Start(CutoutCoroutineRole.ResetDelay, original: value =>
                f.Backend.Move(CutoutCoroutineRole.ResetDelay, value, 2, 1, provider: 0)));
            f.Backend.Move(CutoutCoroutineRole.ResetDelay, f.Backend.LastEnum, -1, 0, provider: 0);
            f.NoErrors(); Check(f.Owner.TryConfirmObservedEnded()); Equal(1, f.Owner.RecycleNaturallyEnded());
        }
    }

    private static void EventStates()
    {
        foreach (var stage in new[] { "first-false", "bad-yield", "second-true", "wide-bool" })
        {
            using var f = new Fixture(); var value = f.Start(CutoutCoroutineRole.DelayedEvent);
            if (stage == "first-false") f.Backend.Move(CutoutCoroutineRole.DelayedEvent, value, -1, 0);
            else if (stage == "bad-yield") f.Backend.Move(CutoutCoroutineRole.DelayedEvent, value, 2, 1);
            else if (stage == "wide-bool") f.Backend.Move(CutoutCoroutineRole.DelayedEvent, value, 1, 256);
            else { f.Backend.Move(CutoutCoroutineRole.DelayedEvent, value, 1, 1); f.Backend.Move(CutoutCoroutineRole.DelayedEvent, value, 1, 1); }
            Check(f.Owner.Read().Unknown); Equal(0, f.Owner.Read().NaturallyEnded); Equal(2, f.Backend.RootRecords);
            Equal(0, f.Backend.FreeCalls);
        }
    }

    private static void FieldIdentity()
    {
        foreach (var field in new[] { "captured-other-row", "events", "offset", "enum", "action" })
        {
            using var f = new Fixture(); var role = field == "action" ? CutoutCoroutineRole.ResetDelay : CutoutCoroutineRole.DelayedEvent;
            var value = f.Start(role);
            f.Backend.Move(role, value, role == CutoutCoroutineRole.ResetDelay ? 2 : 1, 1,
                mutate: observed => field switch {
                    "captured-other-row" => observed with { Trigger = 302, Events = 402, OffsetBits = Bits(1f) },
                    "events" => observed with { Events = 999 },
                    "offset" => observed with { OffsetBits = Bits(1.5f) },
                    "enum" => observed with { Enumerator = 999 },
                    _ => observed with { Action = 999 }
                });
            Check(f.Owner.Read().Unknown); Equal(0, f.Owner.Read().NaturallyEnded); Equal(2, f.Backend.RootRecords);
        }
        using var outside = new Fixture(); var token = outside.Start(CutoutCoroutineRole.DelayedEvent);
        outside.Backend.Values[token] = outside.Backend.Values[token] with { State = 1 };
        outside.Backend.Move(CutoutCoroutineRole.DelayedEvent, token, -1, 0);
        Check(outside.Owner.Read().Unknown && outside.Backend.CaptureError is not null);
        using var duplicate = new Fixture(); var live = duplicate.Start(CutoutCoroutineRole.DelayedEvent);
        duplicate.Start(CutoutCoroutineRole.ResetDelay, address: live);
        Check(duplicate.Owner.Read().Unknown); Equal(1, duplicate.Backend.Retains); Equal(2, duplicate.Backend.RootRecords);
    }

    private static void PhysicalDrain()
    {
        using var f = new Fixture(); f.Finish(CutoutCoroutineRole.DelayedEvent);
        var reads = f.Backend.Reads; var activity = f.Backend.ActivityReads;
        f.Authority.ForeignSourceEntries = 1;
        Check(!f.Owner.TryConfirmNativeIdle() && !f.Owner.TryConfirmObservedEnded()); Equal(0, f.Owner.RecycleNaturallyEnded());
        Equal(reads, f.Backend.Reads); Equal(activity, f.Backend.ActivityReads); Equal(0, f.Backend.FreeCalls);
        f.Authority.ForeignSourceEntries = 0; f.Backend.ForeignEntries = 1;
        Check(!f.Owner.TryConfirmObservedEnded()); Equal(0, f.Owner.RecycleNaturallyEnded()); Equal(reads, f.Backend.Reads);
        f.Backend.ForeignEntries = 0; Check(f.Owner.TryConfirmObservedEnded()); Equal(1, f.Owner.RecycleNaturallyEnded());
        Check(f.Authority.SourceIdleReads > 0 && f.Backend.ActivityReads > activity);
    }

    private static void Worker()
    {
        using var f = new Fixture(); var value = f.Start(CutoutCoroutineRole.DelayedEvent); var reads = f.Backend.Reads;
        var activity = f.Backend.ActivityReads; var retains = f.Backend.Retains;
        Task.Run(() => f.Owner.ObserveFault(f.Backend.Frame(NativeCoroutineKind.MoveNext, 9999), "foreign worker")).GetAwaiter().GetResult();
        Check(!f.Owner.Read().Unknown);
        Task.Run(() => f.Owner.ObserveFault(f.Backend.Frame(NativeCoroutineKind.MoveNext, value), "actual owned worker")).GetAwaiter().GetResult();
        Equal(reads, f.Backend.Reads); Equal(activity, f.Backend.ActivityReads); Equal(retains, f.Backend.Retains);
        Equal(1, f.Authority.Faults); Check(f.Owner.Read().Unknown); Equal(2, f.Backend.RootRecords);
        Task.Run(() => Throws<InvalidOperationException>(() => f.Owner.CaptureStart(
            f.Backend.Frame(NativeCoroutineKind.Start, f.Authority.Host, 5000, Backend.Module + 0x38A6B2)))).GetAwaiter().GetResult();
        Equal(reads, f.Backend.Reads);
    }

    private static void ReentryAndMissingAfter()
    {
        using (var f = new Fixture())
        {
            var value = f.Start(CutoutCoroutineRole.DelayedEvent);
            f.Backend.Move(CutoutCoroutineRole.DelayedEvent, value, 1, 1,
                original: () => f.Backend.Move(CutoutCoroutineRole.DelayedEvent, value, 1, 1));
            Check(f.Owner.Read().Unknown && f.Backend.CaptureError is not null); Equal(3, f.Backend.Originals);
        }
        foreach (var stage in new[] { "start", "move" })
        {
            using var f = new Fixture(); var expected = new IOException("Controlled original failed before its after callback.");
            var value = stage == "move" ? f.Start(CutoutCoroutineRole.DelayedEvent) : 0;
            try
            {
                if (stage == "start") f.Authority.Dispatch(() => f.Backend.Start(CutoutCoroutineRole.DelayedEvent, original: _ => throw expected));
                else f.Backend.Move(CutoutCoroutineRole.DelayedEvent, value, 1, 1, original: () => throw expected);
                throw new Exception("The original exception was swallowed.");
            }
            catch (IOException error) { Check(ReferenceEquals(expected, error)); }
            Check(f.Owner.Read().Unknown); Equal(stage == "start" ? 1 : 2, f.Backend.RootRecords); Equal(0, f.Backend.InFlight);
        }
    }

    private static void Capacity()
    {
        using var f = new Fixture();
        for (var index = 0; index < NativeLocalCutoutCoroutineTracker.MaximumRecords; ++index) f.Start(CutoutCoroutineRole.DelayedEvent);
        f.NoErrors(); Equal(64, f.Owner.Read().LiveRecords); Equal(128, f.Backend.RootRecords); Equal(64, f.Backend.Retains);
        f.Start(CutoutCoroutineRole.DelayedEvent); Equal(64, f.Backend.Retains); Equal(65, f.Backend.Originals);
        Check(f.Owner.Read().Unknown); Equal(128, f.Backend.RootRecords);
    }

    private static void Reuse()
    {
        using var f = new Fixture();
        for (var index = 0; index < 130; ++index)
        {
            var role = index % 2 == 0 ? CutoutCoroutineRole.DelayedEvent : CutoutCoroutineRole.ResetDelay;
            f.Finish(role, address: 4001); Check(f.Owner.TryConfirmObservedEnded()); Equal(1, f.Owner.RecycleNaturallyEnded());
            Equal(0, f.Backend.RootRecords); Equal(0, f.Owner.Read().LiveRecords); Equal(index + 1, f.Owner.Read().RetiredRecords);
            Equal(index + 1L, f.Backend.CleanupAdmissions[^1].Generation); Equal(4001L, f.Backend.CleanupAdmissions[^1].Enumerator);
            Equal(0, f.Backend.CleanupAdmissions[^1].Slot);
        }
        f.NoErrors(); Equal(130, f.Owner.Read().Created); Equal(130, f.Owner.Read().NaturallyEnded);
        Equal(260, f.Backend.Frees.Count); Equal(130L, f.Authority.Entered); Equal(f.Authority.Entered, f.Authority.Exited);
        f.Start(CutoutCoroutineRole.DelayedEvent, address: 4001); Check(!f.Owner.TryConfirmObservedEnded());
    }

    private static void WithdrawalWait()
    {
        using var f = new Fixture(); f.Finish(CutoutCoroutineRole.DelayedEvent); f.Backend.ActivityReads = 0;
        f.Backend.DuringActivity = index => { if (index == 3) f.Backend.ForeignEntries = 1; };
        Equal(0, f.Owner.RecycleNaturallyEnded()); Equal(0, f.Backend.FreeCalls); Equal(2, f.Backend.RootRecords);
        Check(!f.Owner.TryConfirmObservedEnded() && !f.Owner.Read().Unknown);
        f.Backend.DuringActivity = null; f.Backend.ForeignEntries = 0;
        Equal(1, f.Owner.RecycleNaturallyEnded()); Equal(0, f.Backend.RootRecords); Check(f.Owner.TryConfirmObservedEnded());
        Equal(1, f.Backend.CleanupAdmissions.Count);
    }

    private static void WithdrawalReentry()
    {
        foreach (var worker in new[] { false, true })
        {
            using var f = new Fixture(); var value = f.Finish(CutoutCoroutineRole.DelayedEvent); f.Backend.ActivityReads = 0;
            f.Backend.DuringActivity = index => {
                if (index != 3) return; var frame = f.Backend.Frame(NativeCoroutineKind.MoveNext, value);
                if (worker) Task.Run(() => f.Owner.ObserveFault(frame, "Withdrawn old enum worker entry")).GetAwaiter().GetResult();
                else Throws<InvalidOperationException>(() => f.Owner.CaptureMove(CutoutCoroutineRole.DelayedEvent, frame));
            };
            Throws<NativeLocalCutoutCoroutineException>(() => f.Owner.RecycleNaturallyEnded());
            Check(f.Owner.Read().Unknown); Equal(0, f.Backend.FreeCalls); Equal(2, f.Backend.RootRecords);
        }
    }

    private static void IndependentFreeFailure()
    {
        using var f = new Fixture(); f.Finish(CutoutCoroutineRole.DelayedEvent); f.Backend.FailRootFree.Add(64);
        Throws<NativeLocalCutoutCoroutineException>(() => f.Owner.RecycleNaturallyEnded());
        Check(f.Backend.Frees.SequenceEqual(new[] { 64, 0 })); Equal(1, f.Backend.RootRecords);
        Check(f.Owner.Read().Unknown); Equal(0, f.Owner.Read().RetiredRecords); Equal(1, f.Owner.Read().LiveRecords);
        Throws<InvalidOperationException>(() => f.Owner.RecycleNaturallyEnded()); Equal(1, f.Backend.FreeCalls);
        var retains = f.Backend.Retains; f.Start(CutoutCoroutineRole.DelayedEvent, address: 4001); Equal(retains, f.Backend.Retains);
    }

    private static void FinalDrainFault()
    {
        using var f = new Fixture(); var value = f.Finish(CutoutCoroutineRole.DelayedEvent);
        f.Backend.DuringActivity = _ => {
            if (f.Backend.FreeCalls == 1) Task.Run(() => f.Owner.ObserveFault(
                f.Backend.Frame(NativeCoroutineKind.MoveNext, value), "Worker in final native drain")).GetAwaiter().GetResult();
        };
        Throws<NativeLocalCutoutCoroutineException>(() => f.Owner.RecycleNaturallyEnded());
        Equal(0, f.Backend.RootRecords); Equal(1, f.Owner.Read().LiveRecords); Equal(0, f.Owner.Read().RetiredRecords);
        Check(f.Owner.Read().Unknown); Throws<InvalidOperationException>(() => f.Owner.RecycleNaturallyEnded()); Equal(1, f.Backend.FreeCalls);
    }

    private static void PublicationAndActivity()
    {
        foreach (var stage in new[] { "enum-root", "coroutine-root", "unknown-activity", "end-query-worker" })
        {
            using var f = new Fixture(); f.Backend.FailEnum = stage == "enum-root"; f.Backend.FailCoroutine = stage == "coroutine-root";
            var value = f.Start(CutoutCoroutineRole.DelayedEvent);
            if (stage == "unknown-activity") { f.Backend.Known = false; Throws<NativeLocalCutoutCoroutineException>(() => f.Owner.TryConfirmObservedEnded()); }
            if (stage == "end-query-worker")
            {
                f.Backend.DuringActivity = _ => Task.Run(() => f.Owner.ObserveFault(
                    f.Backend.Frame(NativeCoroutineKind.MoveNext, value), "Worker during observed-end native stats")).GetAwaiter().GetResult();
                Throws<NativeLocalCutoutCoroutineException>(() => f.Owner.TryConfirmObservedEnded());
            }
            Check(f.Owner.Read().Unknown); Equal(stage == "enum-root" ? 1 : 2, f.Backend.RootRecords); Equal(0, f.Backend.FreeCalls);
        }
        using var main = new MainThreadDispatcher(); var authority = new Authority(2f); var backend = new Backend(main, authority) { FailInitialize = true };
        NativeLocalCutoutCoroutineException? captured = null;
        try { NativeLocalCutoutCoroutineTracker.CaptureBackend(main, backend, authority, Backend.Module); }
        catch (NativeLocalCutoutCoroutineException error) { captured = error; }
        Check(captured is not null && captured.Responsibility.Read().Unknown); Equal(1, authority.Faults);
        Check(!captured!.Responsibility.IsFor(null!)); Equal(0, backend.RootRecords);
    }

    private static void SourceFaultDuringRead()
    {
        using (var f = new Fixture())
        {
            f.Backend.DuringRead = _ => f.Authority.Faulted = true;
            f.Start(CutoutCoroutineRole.DelayedEvent);
            Check(f.Owner.Read().Unknown); Equal(1, f.Backend.RootRecords); Equal(0, f.Backend.CoroutineRetains);
            Equal(1, f.Backend.Reads); Equal(0, f.Owner.Read().NaturallyEnded);
            var retains = f.Backend.Retains; f.Start(CutoutCoroutineRole.DelayedEvent); Equal(retains, f.Backend.Retains);
        }
        using (var f = new Fixture())
        {
            var token = f.Start(CutoutCoroutineRole.DelayedEvent);
            f.Backend.Move(CutoutCoroutineRole.DelayedEvent, token, 1, 1); f.NoErrors();
            var reads = f.Backend.Reads;
            f.Backend.DuringRead = index => { if (index == reads + 2) f.Authority.Faulted = true; };
            f.Backend.Move(CutoutCoroutineRole.DelayedEvent, token, -1, 0);
            Check(f.Owner.Read().Unknown); Equal(0, f.Owner.Read().NaturallyEnded); Equal(2, f.Backend.RootRecords);
        }
        using (var f = new Fixture())
        {
            f.Finish(CutoutCoroutineRole.DelayedEvent); f.Finish(CutoutCoroutineRole.ResetDelay);
            var reads = f.Backend.Reads;
            f.Backend.DuringRead = _ => f.Authority.Faulted = true;
            Throws<NativeLocalCutoutCoroutineException>(() => f.Owner.TryConfirmObservedEnded());
            Equal(reads + 1, f.Backend.Reads); Check(f.Owner.Read().Unknown); Equal(4, f.Backend.RootRecords);
        }
    }

    private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);
    private sealed class Fixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new();
        internal readonly Authority Authority;
        internal readonly Backend Backend;
        internal readonly NativeLocalCutoutCoroutineTracker Owner;
        internal Fixture(float maximumOffset = 2f)
        {
            Authority = new(maximumOffset); Backend = new(Main, Authority);
            Owner = NativeLocalCutoutCoroutineTracker.CaptureBackend(Main, Backend, Authority, NativeLocalCutoutCoroutineRegression.Backend.Module);
            Authority.Before = false;
        }
        internal long Start(CutoutCoroutineRole role, long? address = null)
        { long value = 0; Authority.Dispatch(() => value = Backend.Start(role, address)); return value; }
        internal long Finish(CutoutCoroutineRole role, long? address = null)
        {
            long result = 0;
            Authority.Dispatch(() => result = Backend.Start(role, address, original: value => {
                Backend.Move(role, value, role == CutoutCoroutineRole.ResetDelay ? 2 : 1, 1);
                Backend.Move(role, value, -1, 0);
            }));
            NoErrors(); return result;
        }
        internal void NoErrors() { Check(Backend.CaptureError is null); Check(Backend.AfterError is null); Check(!Owner.Read().Unknown); }
        public void Dispose() => Main.Dispose(); // Active/fault process ownership is intentionally not erased by a fixture.
    }

    private sealed class Authority : ICutoutCoroutineAuthority
    {
        internal bool Before = true, Allowed = true, Faulted;
        internal long Generation, Entered, Exited;
        internal int DispatchDepth, ForeignSourceEntries, SourceIdleReads, DispatchReads, Faults;
        public long Host => 201;
        public bool Unknown => Faulted;
        public NativeCutoutEventObservation Source { get; }
        internal Authority(float maximum)
        {
            var rows = maximum > 1f ? new[] { new NativeCutoutTrigger(301, 401, Bits(maximum)), new NativeCutoutTrigger(302, 402, Bits(1f)) }
                : new[] { new NativeCutoutTrigger(301, 401, Bits(maximum)) };
            Source = new(101, Host, 202, 203, 204, 1, true, Array.AsReadOnly(rows));
        }
        public void VerifyBeforeCapture() { if (!Before) throw new InvalidOperationException("Controlled capture followed activation."); }
        public void VerifyObservation() { if (Before || !Allowed) throw new InvalidOperationException("Controlled exact active source is unavailable."); }
        public bool TryGetDispatch(out long generation) { DispatchReads++; generation = Generation; return DispatchDepth == 1 && generation > 0; }
        public bool TryConfirmSourceIdle() { SourceIdleReads++; return DispatchDepth == 0 && ForeignSourceEntries == 0; }
        public void ObserveFaultFromAnyThread() => Interlocked.Increment(ref Faults);
        internal void Dispatch(Action action)
        {
            Check(DispatchDepth == 0); ++Generation; ++Entered; ++DispatchDepth;
            try { action(); } finally { --DispatchDepth; ++Exited; }
        }
    }

    private sealed class Backend : ICutoutCoroutineBackend
    {
        internal const long Module = 0x20000000;
        private readonly MainThreadDispatcher _main;
        private readonly Authority _authority;
        internal NativeLocalCutoutCoroutineTracker Owner = null!;
        internal readonly Dictionary<long, CutoutCoroutineObservation> Values = new();
        private readonly Dictionary<int, long> _slots = new();
        private readonly bool[] _roots = new bool[128];
        internal readonly HashSet<int> FailRootFree = new();
        internal readonly List<int> Frees = new();
        internal readonly List<(int Slot, long Enumerator, long Generation)> CleanupAdmissions = new();
        internal Action<int>? DuringActivity, DuringRead;
        internal Exception? CaptureError, AfterError;
        internal int Reads, Retains, CoroutineRetains, Originals, InFlight, ForeignEntries, ActivityReads, FreeCalls;
        internal long Next = 4000, LastEnum;
        internal bool Known = true, FailEnum, FailCoroutine, FailInitialize;
        public bool CleanupUnknown { get; private set; }
        public int RootRecords => _roots.Count(value => value);
        internal Backend(MainThreadDispatcher main, Authority authority) => (_main, _authority) = (main, authority);
        public void Initialize(NativeLocalCutoutCoroutineTracker owner)
        { Owner = owner; if (FailInitialize) { CleanupUnknown = true; throw new IOException("Controlled native observer installation outcome is unknown."); } }
        public void RetainEnumerator(int slot, CutoutCoroutineRole role, long enumerator)
        {
            _main.VerifyAccess(); Check(!_roots[slot]); _slots[slot] = enumerator; _roots[slot] = true; Retains++;
            if (FailEnum) { CleanupUnknown = true; throw new IOException("Controlled enum root publication outcome is unknown."); }
        }
        public CutoutCoroutineObservation ReadEnumerator(int slot, CutoutCoroutineRole role)
        { _main.VerifyAccess(); ++Reads; DuringRead?.Invoke(Reads); Check(_roots[slot]); return Values[_slots[slot]]; }
        public void RetainCoroutine(int slot, long coroutine)
        {
            _main.VerifyAccess(); Check(coroutine != 0 && !_roots[slot + 64]); _roots[slot + 64] = true; CoroutineRetains++;
            if (FailCoroutine) { CleanupUnknown = true; throw new IOException("Controlled Coroutine output root outcome is unknown."); }
        }
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity()
        { _main.VerifyAccess(); ++ActivityReads; DuringActivity?.Invoke(ActivityReads); return new(InFlight + ForeignEntries, Known && !CleanupUnknown); }
        public void ReleaseEndedRoots(int slot, long enumerator, long generation)
        {
            _main.VerifyAccess(); Check(Owner.IsRetiringForCleanup(slot, enumerator, generation)); Check(_slots[slot] == enumerator);
            FreeCalls++; CleanupAdmissions.Add((slot, enumerator, generation));
            var errors = new List<Exception>();
            foreach (var index in new[] { slot + 64, slot })
            {
                if (!_roots[index]) continue;
                // Independent free failures do not erase actual stats or the worker
                // flag. This models the cleanup decision, not production GC handles.
                if (Owner.ObservationUnknown || _authority.Unknown || !Known || InFlight + ForeignEntries != 0 || !_authority.TryConfirmSourceIdle())
                { errors.Add(new IOException("Controlled native activity changed before independent free.")); break; }
                Frees.Add(index);
                if (FailRootFree.Contains(index)) { CleanupUnknown = true; errors.Add(new IOException("Controlled independent root free outcome is unknown.")); continue; }
                _roots[index] = false;
            }
            if (errors.Count != 0) { CleanupUnknown = true; throw new AggregateException(errors); }
            _slots.Remove(slot);
        }
        internal NativeCoroutineFrame Frame(NativeCoroutineKind kind, long self, long argument = 0, long caller = 0, bool alias = false)
            => new() { Version = 1, Size = 48, Kind = kind, Self = new(self), Enumerator = new(argument),
                Method = new(alias ? 0x456 : 0x123), Caller = new(caller == 0 ? Module + 0x999 : caller) };
        private void Invoke(Func<NativeLocalCutoutCoroutineTracker.Context?> capture,
            Action<NativeLocalCutoutCoroutineTracker.Context> after, Action? original)
        {
            NativeLocalCutoutCoroutineTracker.Context? context = null; ++InFlight;
            try
            {
                try { context = capture(); } catch (Exception error) { CaptureError ??= error; }
                ++Originals; original?.Invoke();
                if (context is not null) try { after(context); } catch (Exception error) { AfterError ??= error; }
            }
            finally { try { context?.Dispose(); } finally { --InFlight; } }
        }
        internal long Start(CutoutCoroutineRole role, long? address = null, nuint coroutine = 5001,
            Action<long>? original = null, long? caller = null, bool alias = false)
        {
            var value = address ?? ++Next; LastEnum = value;
            var source = _authority.Source.Triggers[0];
            Values[value] = role == CutoutCoroutineRole.DelayedEvent ? new(value, 0, source.Trigger, source.Events, source.OffsetBits, 0, 0, 0)
                : new(value, 0, 0, 0, 0, 6001, Bits(_authority.Source.Triggers.Max(row => BitConverter.Int32BitsToSingle(row.OffsetBits))), 0);
            var frame = Frame(NativeCoroutineKind.Start, _authority.Host, value,
                caller ?? Module + (role == CutoutCoroutineRole.ResetDelay ? 0x38A60C : 0x38A6B2), alias);
            Invoke(() => Owner.CaptureStart(frame), context => Owner.CompleteStart(context, frame, coroutine), () => original?.Invoke(value));
            return value;
        }
        internal void Move(CutoutCoroutineRole role, long value, int state, nuint result,
            float? seconds = null, long? provider = null, Func<CutoutCoroutineObservation, CutoutCoroutineObservation>? mutate = null, Action? original = null)
        {
            var frame = Frame(NativeCoroutineKind.MoveNext, value);
            Invoke(() => Owner.CaptureMove(role, frame), context => Owner.CompleteMove(context, frame, result), () => {
                var next = Values[value] with { State = state };
                if (seconds.HasValue) next = next with { SecondsBits = Bits(seconds.Value) };
                if (provider.HasValue) next = next with { Provider = provider.Value };
                Values[value] = mutate is null ? next : mutate(next); original?.Invoke();
            });
        }
    }
}
