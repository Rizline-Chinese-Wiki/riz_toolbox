using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime.Hooks;
using static Assert;

// Controlled tracker/backend tests only. No Stop hook, engine cancellation,
// native call, IL2CPP object or cancellation-completed receipt is synthesized.
internal static class NativeCoroutineCancellationIdentityTests
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Initialization cancellation identity preserves synchronous enum then actual handle without completion credit", InitializationIdentity),
        ("Initialization cancellation diagnostics retain actual handle on post-return read and root failures", InitializationFailures),
        ("Terminal cancellation identity rejects retired owner generations even after enum and handle address reuse", TerminalIdentity),
        ("Terminal cancellation diagnostics preserve unconfirmed handle and reject scalar matching after faults", TerminalFailures),
        ("Cutout cancellation identity preserves dispatch owner generation and rejects old snapshots after reuse", CutoutIdentity),
        ("Cutout cancellation diagnostics retain failed handle adoption without creating cancellation completion", CutoutFailures)
    };
    private const NativeCoroutineCancellationReferenceKind Enum = NativeCoroutineCancellationReferenceKind.Enumerator;
    private const NativeCoroutineCancellationReferenceKind Handle = NativeCoroutineCancellationReferenceKind.CoroutineHandle;

    private static void InitializationIdentity()
    {
        using var f = new NativeLocalInitializationCoroutineRegression.Fixture();
        using var peer = new NativeLocalInitializationCoroutineRegression.Fixture();
        NativeCoroutineCancellationIdentity synchronous = default;
        f.Backend.Start(InitializationCoroutineRole.Delay, () => {
            Check(f.Owner.TryMatchCancellationReference(Enum, 101, 1001, out synchronous));
            Check(synchronous.Generation == 0 && !synchronous.StartReturned && synchronous.ObservedHandle == 0 && !synchronous.HandleConfirmed);
            Check(!f.Owner.TryMatchCancellationReference(Handle, 101, 5001, out _));
            f.Backend.Move(InitializationCoroutineRole.Delay, 2, 1);
        });
        var reads = f.Backend.Reads; var activity = f.Backend.ActivityReads; var roots = f.Backend.RootRecords;
        Check(f.Owner.TryMatchCancellationReference(Handle, 101, 5001, out var identity));
        Check(identity.StartReturned && identity.HandleConfirmed && identity.ObservedHandle == 5001 && identity.Enumerator == 1001);
        Check(f.Owner.IsCurrentCancellationIdentity(synchronous) && f.Owner.IsCurrentCancellationIdentity(identity));
        Check(!peer.Owner.IsCurrentCancellationIdentity(identity));
        peer.Backend.Start(InitializationCoroutineRole.WaitUntil,
            () => peer.Backend.Move(InitializationCoroutineRole.WaitUntil, -1, 0), 0);
        var nullHandle = peer.Owner.ReadCancellationIdentities().Single();
        Check(nullHandle.StartReturned && nullHandle.NaturalTerminal && nullHandle.ObservedHandle == 0 && !nullHandle.HandleConfirmed);
        Check(!peer.Owner.TryMatchCancellationReference(Handle, 101, 0, out _));
        Check(!f.Owner.TryMatchCancellationReference(Handle, 102, 5001, out _) && !f.Owner.TryMatchCancellationReference(Enum, 101, 5001, out _));
        Check(!f.Owner.TryMatchCancellationReference((NativeCoroutineCancellationReferenceKind)9, 101, 5001, out _));
        Check(!f.Owner.TryMatchCancellationReference(Handle, 101, 0, out _) && !f.Owner.TryMatchCancellationReference(Enum, 0, 1001, out _));
        Check(!f.Owner.IsCurrentCancellationIdentity(identity with { Slot = 1 })
            && !f.Owner.IsCurrentCancellationIdentity(identity with { Generation = 1 })
            && !f.Owner.IsCurrentCancellationIdentity(identity with { ObservedHandle = 5002 }));
        var before = f.Owner.ReadCancellationIdentities(); Equal(1, before.Count);
        Task.Run(() => Throws<InvalidOperationException>(() => f.Owner.TryMatchCancellationReference(Enum, 101, 1001, out _))).GetAwaiter().GetResult();
        Equal(reads, f.Backend.Reads); Equal(activity, f.Backend.ActivityReads); Equal(roots, f.Backend.RootRecords);
        Check(!f.Owner.Read().DelayEnded && !f.Owner.Read().Ended && !identity.NaturalTerminal);
        f.Backend.Start(InitializationCoroutineRole.WaitUntil, coroutine: 5001);
        Equal(1, before.Count); Equal(2, f.Owner.ReadCancellationIdentities().Count); // Existing read-only snapshot remains immutable.
        Check(!f.Owner.TryMatchCancellationReference(Handle, 101, 5001, out _)); // Ambiguous same-owner handle never guesses a role.
        f.Finish(); Check(f.Owner.TryReleaseAfterEnded());
        Check(!f.Owner.IsCurrentCancellationIdentity(identity) && !f.Owner.TryMatchCancellationReference(Handle, 101, 5001, out _));
    }
    private static void InitializationFailures()
    {
        foreach (var rootFault in new[] { false, true }) {
            using var f = new NativeLocalInitializationCoroutineRegression.Fixture();
            f.Backend.Start(InitializationCoroutineRole.Delay, () => {
                if (rootFault) f.Backend.FailCoroutine = true; else f.Backend.FailRead = true;
            }, unchecked((nuint)0x8123456789abcdefUL));
            Check(f.Owner.Read().Unknown && f.Backend.AfterError is not null);
            var value = f.Owner.ReadCancellationIdentities().Single();
            Equal(unchecked((long)0x8123456789abcdefUL), value.ObservedHandle);
            Check(!value.HandleConfirmed && !value.StartReturned && !value.Known && value.Enumerator == 1001);
            var reads = f.Backend.Reads; var roots = f.Backend.RootRecords;
            Check(!f.Owner.TryMatchCancellationReference(Handle, 101, value.ObservedHandle, out _)
                && !f.Owner.TryMatchCancellationReference(Enum, 101, 1001, out _) && !f.Owner.IsCurrentCancellationIdentity(value));
            Equal(reads, f.Backend.Reads); Equal(roots, f.Backend.RootRecords); Equal(0, f.Backend.FreeCalls);
            Check(!f.Owner.Read().DelayEnded && !f.Owner.Read().Ended);
        }
    }
    private static void TerminalIdentity()
    {
        using var f = new NativeLocalTerminalCoroutineRegression.Fixture();
        using var peer = new NativeLocalTerminalCoroutineRegression.Fixture();
        const long enumerator = 7001;
        f.Backend.Factory(TerminalCoroutineRole.Fade, enumerator);
        Check(f.Owner.TryMatchCancellationReference(Enum, 101, enumerator, out var beforeStart));
        Check(!beforeStart.StartReturned && beforeStart.ObservedHandle == 0);
        f.Backend.EnumStart(TerminalCoroutineRole.Fade, enumerator, () => {
            Check(f.Owner.IsCurrentCancellationIdentity(beforeStart));
            Check(!f.Owner.TryMatchCancellationReference(Handle, 101, 5002, out _));
            f.Backend.Move(TerminalCoroutineRole.Fade, enumerator, 1, 1);
        });
        var reads = f.Backend.Reads; var activity = f.Backend.ActivityReads;
        Check(f.Owner.TryMatchCancellationReference(Handle, 101, 5002, out var identity));
        Check(identity.Generation > 0 && identity.HandleConfirmed && identity.StartReturned && !identity.NaturalTerminal);
        Check(f.Owner.IsCurrentCancellationIdentity(beforeStart) && !peer.Owner.IsCurrentCancellationIdentity(identity));
        Check(!f.Owner.TryMatchCancellationReference(Handle, 102, 5002, out _)
            && !f.Owner.TryMatchCancellationReference(Enum, 101, 5002, out _));
        Task.Run(() => Throws<InvalidOperationException>(() => f.Owner.ReadCancellationIdentities())).GetAwaiter().GetResult();
        Equal(reads, f.Backend.Reads); Equal(activity, f.Backend.ActivityReads);
        Equal(0, f.Owner.Read().NaturallyEnded); Check(!f.Owner.TryConfirmObservedEnded());
        f.Backend.Move(TerminalCoroutineRole.Fade, enumerator, -1, 0);
        Equal(1, f.Owner.RecycleNaturallyEnded());
        Check(!f.Owner.IsCurrentCancellationIdentity(identity) && !f.Owner.TryMatchCancellationReference(Handle, 101, 5002, out _));
        Equal(0, f.Owner.ReadCancellationIdentities().Count);
        f.Backend.Factory(TerminalCoroutineRole.Fade, enumerator);
        f.Backend.EnumStart(TerminalCoroutineRole.Fade, enumerator);
        Check(f.Owner.TryMatchCancellationReference(Handle, 101, 5002, out var reused));
        Check(reused.Slot == identity.Slot && reused.Generation > identity.Generation && reused.Enumerator == identity.Enumerator);
        Check(!f.Owner.IsCurrentCancellationIdentity(identity) && f.Owner.IsCurrentCancellationIdentity(reused));
        f.Owner.ObserveFault(f.Backend.Frame(NativeCoroutineKind.MoveNext, enumerator), "controlled external cancellation uncertainty");
        Check(!f.Owner.TryMatchCancellationReference(Handle, 101, 5002, out _) && !f.Owner.IsCurrentCancellationIdentity(reused));
        Equal(5002L, f.Owner.ReadCancellationIdentities().Single().ObservedHandle);
    }
    private static void TerminalFailures()
    {
        foreach (var rootFault in new[] { false, true }) {
            using var f = new NativeLocalTerminalCoroutineRegression.Fixture();
            var enumerator = f.Backend.Factory(TerminalCoroutineRole.Fade);
            f.Backend.EnumStart(TerminalCoroutineRole.Fade, enumerator, () => {
                if (rootFault) f.Backend.FailCoroutine = true;
                else f.Backend.DuringRead = () => throw new IOException("controlled post-return Target/class read");
            });
            Check(f.Owner.Read().Unknown && f.Backend.AfterError is not null);
            var identity = f.Owner.ReadCancellationIdentities().Single();
            Check(identity.ObservedHandle == 5002 && !identity.HandleConfirmed && !identity.StartReturned && !identity.Known);
            var reads = f.Backend.Reads; var roots = f.Backend.Roots;
            Check(!f.Owner.TryMatchCancellationReference(Handle, 101, 5002, out _) && !f.Owner.IsCurrentCancellationIdentity(identity));
            Equal(reads, f.Backend.Reads); Equal(roots, f.Backend.Roots); Equal(0, f.Backend.FreeCalls); Equal(0, f.Owner.Read().NaturallyEnded);
        }
    }
    private static void CutoutIdentity()
    {
        using var f = new CutoutFixture(); using var peer = new CutoutFixture();
        const long enumerator = 8001, handle = 9001;
        NativeCoroutineCancellationIdentity synchronous = default;
        f.Backend.Start(enumerator, handle, () => {
            Check(f.Owner.TryMatchCancellationReference(Enum, 201, enumerator, out synchronous));
            Check(!synchronous.StartReturned && !synchronous.HandleConfirmed && synchronous.ObservedHandle == 0);
            Check(!f.Owner.TryMatchCancellationReference(Handle, 201, handle, out _));
            f.Backend.Move(enumerator, 1, 1);
        });
        var reads = f.Backend.Reads; var activity = f.Backend.ActivityReads;
        Check(f.Owner.TryMatchCancellationReference(Handle, 201, handle, out var identity));
        Check(identity.StartReturned && identity.HandleConfirmed && identity.Generation > 0 && f.Owner.IsCurrentCancellationIdentity(synchronous));
        Check(!peer.Owner.IsCurrentCancellationIdentity(identity) && !f.Owner.TryMatchCancellationReference(Handle, 202, handle, out _));
        Check(!f.Owner.IsCurrentCancellationIdentity(identity with { Generation = identity.Generation + 1 }));
        Task.Run(() => Throws<InvalidOperationException>(() => f.Owner.IsCurrentCancellationIdentity(identity))).GetAwaiter().GetResult();
        Equal(reads, f.Backend.Reads); Equal(activity, f.Backend.ActivityReads); Equal(0, f.Owner.Read().NaturallyEnded);
        Check(!f.Owner.TryConfirmObservedEnded());
        f.Backend.Move(enumerator, -1, 0); Equal(1, f.Owner.RecycleNaturallyEnded());
        Check(!f.Owner.IsCurrentCancellationIdentity(identity)); Equal(0, f.Owner.ReadCancellationIdentities().Count);
        f.Backend.Start(enumerator, handle);
        Check(f.Owner.TryMatchCancellationReference(Handle, 201, handle, out var reused));
        Check(reused.Generation > identity.Generation && reused.Slot == identity.Slot && !f.Owner.IsCurrentCancellationIdentity(identity));
        f.Authority.Faulted = true;
        Check(!f.Owner.IsCurrentCancellationIdentity(reused) && !f.Owner.TryMatchCancellationReference(Enum, 201, enumerator, out _));
        Equal(handle, f.Owner.ReadCancellationIdentities().Single().ObservedHandle);
    }
    private static void CutoutFailures()
    {
        foreach (var rootFault in new[] { false, true }) {
            using var f = new CutoutFixture();
            f.Backend.Start(8001, 9001, () => { if (rootFault) f.Backend.FailHandle = true; else f.Backend.FailRead = true; });
            Check(f.Owner.Read().Unknown && f.Backend.AfterError is not null);
            var identity = f.Owner.ReadCancellationIdentities().Single();
            Check(identity.ObservedHandle == 9001 && !identity.HandleConfirmed && !identity.StartReturned && !identity.Known);
            var reads = f.Backend.Reads; var roots = f.Backend.RootRecords;
            Check(!f.Owner.TryMatchCancellationReference(Handle, 201, 9001, out _) && !f.Owner.IsCurrentCancellationIdentity(identity));
            Equal(reads, f.Backend.Reads); Equal(roots, f.Backend.RootRecords); Equal(0, f.Backend.Frees); Equal(0, f.Owner.Read().NaturallyEnded);
        }
    }
    private sealed class CutoutFixture : IDisposable
    {
        internal readonly MainThreadDispatcher Main = new();
        internal readonly CutoutAuthority Authority = new();
        internal readonly CutoutBackend Backend;
        internal readonly NativeLocalCutoutCoroutineTracker Owner;
        internal CutoutFixture()
        { Backend = new(Authority); Owner = NativeLocalCutoutCoroutineTracker.CaptureBackend(Main, Backend, Authority, CutoutBackend.Module); Authority.Before = false; }
        public void Dispose() => Main.Dispose(); // Preserve uncertain process ownership.
    }
    private sealed class CutoutAuthority : ICutoutCoroutineAuthority
    {
        internal bool Before = true, Faulted; internal long Dispatch;
        internal int Active;
        public bool Unknown => Faulted;
        public long Host => 201;
        public NativeCutoutEventObservation Source { get; } = new(101, 201, 202, 203, 204, 1, true,
            new[] { new NativeCutoutTrigger(301, 401, BitConverter.SingleToInt32Bits(1f)) });
        public void VerifyBeforeCapture() { if (!Before) throw new InvalidOperationException(); }
        public void VerifyObservation() { if (Before || Faulted) throw new InvalidOperationException(); }
        public bool TryGetDispatch(out long generation) { generation = Dispatch; return Active == 1 && generation > 0; }
        public bool TryConfirmSourceIdle() => Active == 0;
        public void ObserveFaultFromAnyThread() { }
    }
    private sealed class CutoutBackend : ICutoutCoroutineBackend
    {
        internal const long Module = 0x20000000;
        private readonly CutoutAuthority _authority;
        private readonly Dictionary<int, long> _slots = new();
        private readonly HashSet<int> _roots = new();
        private readonly Dictionary<long, CutoutCoroutineObservation> _values = new();
        private NativeLocalCutoutCoroutineTracker _owner = null!;
        internal int Reads, ActivityReads, InFlight, Frees;
        internal bool FailHandle, FailRead;
        internal Exception? AfterError;
        public bool CleanupUnknown => false;
        public int RootRecords => _roots.Count;
        internal CutoutBackend(CutoutAuthority authority) => _authority = authority;
        public void Initialize(NativeLocalCutoutCoroutineTracker owner) => _owner = owner;
        public void RetainEnumerator(int slot, CutoutCoroutineRole role, long enumerator) { _slots[slot] = enumerator; _roots.Add(slot); }
        public CutoutCoroutineObservation ReadEnumerator(int slot, CutoutCoroutineRole role)
        { ++Reads; if (FailRead) throw new IOException("controlled post-return Target read"); return _values[_slots[slot]]; }
        public void RetainCoroutine(int slot, long coroutine)
        { _roots.Add(slot + 64); if (FailHandle) throw new IOException("controlled handle retention"); }
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity() { ++ActivityReads; return new(InFlight, true); }
        public void ReleaseEndedRoots(int slot, long enumerator, long generation)
        { Check(_owner.IsRetiringForCleanup(slot, enumerator, generation)); ++Frees; _roots.Remove(slot + 64); _roots.Remove(slot); _slots.Remove(slot); }
        private static NativeCoroutineFrame Frame(NativeCoroutineKind kind, long self, long argument = 0, long caller = 0)
            => new() { Version = 1, Size = 48, Kind = kind, Self = new(self), Enumerator = new(argument), Method = new(1), Caller = new(Module + caller) };
        internal void Start(long enumerator, long handle, Action? original = null)
        {
            ++_authority.Dispatch; ++_authority.Active; ++InFlight;
            _values[enumerator] = new(enumerator, 0, 301, 401, BitConverter.SingleToInt32Bits(1f), 0, 0, 0);
            var frame = Frame(NativeCoroutineKind.Start, _authority.Host, enumerator, 0x38a6b2);
            var context = _owner.CaptureStart(frame) ?? throw new InvalidOperationException("Controlled Start was not captured.");
            try { original?.Invoke(); try { _owner.CompleteStart(context, frame, unchecked((nuint)handle)); } catch (Exception error) { AfterError = error; } }
            finally { context.Dispose(); --InFlight; --_authority.Active; }
        }
        internal void Move(long enumerator, int state, nuint result)
        {
            ++InFlight; var frame = Frame(NativeCoroutineKind.MoveNext, enumerator, caller: 0x999);
            var context = _owner.CaptureMove(CutoutCoroutineRole.DelayedEvent, frame) ?? throw new InvalidOperationException("Controlled Move was not captured.");
            try { _values[enumerator] = _values[enumerator] with { State = state }; _owner.CompleteMove(context, frame, result); }
            finally { context.Dispose(); --InFlight; }
        }
    }
}
