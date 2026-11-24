using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;
using static Assert;

internal static class NativeTweenTypedEventTests
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Typed Tween event preserves float bits and associates the exact active Play parent", CapturePlay),
        ("Typed Tween event associates an Execute parent by its exact Tween argument", CaptureExecute),
        ("Typed Tween event records abnormal completion while retaining undecided effects", Abnormal),
        ("Typed Tween event ignores foreign global events and faults owned events without a parent", ParentRejection),
        ("Typed Tween event contexts reject double completion disposal and copied observations", PrivateContext),
        ("Typed Tween event permits nullable onTween fields across the complete source set", Nullable),
        ("Typed Tween event binds when every actual source has a null onTween field", AllNullable),
        ("Typed Tween event selects shared event membership through each actual nested Tween parent", Shared),
        ("Typed Tween event faults owned invalid frames before publishing an invocation", InvalidFrames),
        ("Typed Tween event completion requires the retained physical parent and preserves its failure", CompletionParent),
        ("Typed Tween event disposal requires a unique completed outcome before releasing its context", EarlyDisposal),
        ("Typed Tween event rejects combined unknown and skipped native completion outcomes", CompletionFlags),
        ("Typed Tween event completion compares the whole native entry frame", CompletionFrame),
        ("Typed Tween event joins dynamic nullable shared or distinct onTween before its first original call", DynamicMembership),
        ("Typed Tween event rejects an owned event beneath a different retained Tween receiver", EntryParent),
        ("Typed Tween event disposal retains its exact active parent after native return", DisposalParent)
    };

    private static void CapturePlay()
    {
        using var f = new Fixture(); Check(f.Backend.Bound);
        var play = f.Begin(NativeTweenOperation.Play, f.Tween.Target);
        f.Backend.Invoke(0x80000000u, NativeOperationExitFlags.Returned);
        f.End(play);
        var row = f.Work.ReadSourceReplay().TypedEvents!.Single();
        Check(row is { Tween: 1001, Event: 2001, Operation: NativeTweenOperation.Play,
            ValueBits: 0x80000000u, NativeReturned: true, Abnormal: false, Disposed: true, Production: false });
        Check(row.Caller == Fixture.Caller && row.Method == Fixture.Method);
        Check(!f.Work.CanRetireWork() && f.Roots.Freed == 0);
    }

    private static void CaptureExecute()
    {
        using var f = new Fixture();
        var execute = f.Begin(NativeTweenOperation.ExecuteTween, f.Tween.Target);
        f.Backend.Invoke(0x7fc12345, NativeOperationExitFlags.Returned); f.End(execute);
        var row = f.Work.ReadSourceReplay().TypedEvents!.Single();
        Check(row.Operation == NativeTweenOperation.ExecuteTween && row.ValueBits == 0x7fc12345u
            && row.NativeReturned && row.Disposed && !row.Abnormal);
    }

    private static void Abnormal()
    {
        using var f = new Fixture(); var play = f.Begin(NativeTweenOperation.Play, f.Tween.Target);
        f.Backend.Invoke(1u, NativeOperationExitFlags.Abnormal); f.End(play, NativeOperationExitFlags.Abnormal);
        Check(f.Work.ReadSourceReplay().TypedEvents!.Single() is { NativeReturned: false, Abnormal: true, Disposed: true });
        Check(f.Work.Unknown && f.Work.ReadSourceReplay().TypedEvents!.Single().Failure is not null && !f.Work.CanRetireWork());
    }

    private static void ParentRejection()
    {
        using var f = new Fixture();
        Check(f.Backend.Begin(1u, 9999) is null && !f.Work.Unknown);
        var play = f.Begin(NativeTweenOperation.Play, f.Tween.Target);
        Check(f.Backend.Begin(1u, 9999) is null && !f.Work.Unknown); f.End(play);
        Equal(0, f.Work.ReadSourceReplay().TypedEvents!.Count);
        f.Backend.Fault(new() { Self = new(9999) }, "Foreign event fault");
        Check(!f.Work.Unknown);
        using var missing = new Fixture();
        Throws<InvalidOperationException>(() => missing.Backend.Begin(1u));
        Check(missing.Work.Unknown && missing.Work.ReadSourceReplay().TypedEvents!.Count == 0);
    }

    private static void PrivateContext()
    {
        using var f = new Fixture(); var play = f.Begin(NativeTweenOperation.Play, f.Tween.Target);
        var context = f.Backend.Begin(1u);
        using var foreign = new MemoryStream();
        Throws<InvalidOperationException>(() => f.Backend.Complete(foreign, f.Backend.Frame, NativeOperationExitFlags.Returned));
        f.Backend.Complete(context!, f.Backend.Frame, NativeOperationExitFlags.Returned);
        Throws<InvalidOperationException>(() => f.Backend.Complete(context!, f.Backend.Frame, NativeOperationExitFlags.Returned));
        context!.Dispose(); Throws<InvalidOperationException>(context.Dispose);
        f.End(play);
        var copied = f.Work.ReadSourceReplay().TypedEvents!.Single() with { Disposed = false, Abnormal = true };
        Check(copied.Disposed == false && copied.Abnormal && f.Work.ReadSourceReplay().TypedEvents!.Single().Disposed);
    }

    private static void Nullable()
    {
        using var f = new Fixture((1001, 0), (1003, 2003));
        Check(f.Backend.Bound && f.Backend.MembershipCount == 1 && f.Backend.ProcessCapacity == 2 && !f.Work.Unknown);
        var play = f.Begin(NativeTweenOperation.Play, new(1003));
        f.Backend.InvokeEvent(2003, 1u, NativeOperationExitFlags.Returned); f.End(play);
        Check(f.Work.ReadSourceReplay().TypedEvents!.Single() is { Tween: 1003, Event: 2003, Disposed: true });
    }

    private static void AllNullable()
    {
        using var f = new Fixture((1001, 0), (1003, 0));
        Check(f.Backend.Bound && f.Backend.MembershipCount == 0 && f.Backend.ProcessCapacity == 2 && !f.Work.Unknown);
        Check(f.Backend.Begin(1u, 9999) is null && !f.Work.Unknown);
    }

    private static void Shared()
    {
        using var f = new Fixture((1001, 2001), (1003, 2001));
        var outer = f.Begin(NativeTweenOperation.Play, new(1001));
        var outerEvent = f.Backend.Begin(0x7f800000u)!; var outerFrame = f.Backend.Frame;
        var inner = f.Begin(NativeTweenOperation.ExecuteTween, new(1003));
        f.Backend.Invoke(0xff800000u, NativeOperationExitFlags.Returned); f.End(inner);
        f.Backend.Complete(outerEvent, outerFrame, NativeOperationExitFlags.Returned); outerEvent.Dispose(); f.End(outer);
        var rows = f.Work.ReadSourceReplay().TypedEvents!;
        Check(rows.Count == 2 && rows[0].Tween == 1001 && rows[1].Tween == 1003 && rows.All(row => row.Event == 2001)
            && rows[0].Operation == NativeTweenOperation.Play && rows[1].Operation == NativeTweenOperation.ExecuteTween
            && rows[0].OperationSequence != rows[1].OperationSequence && rows.All(row => row.Disposed) && !f.Work.Unknown);
    }

    private static void InvalidFrames()
    {
        foreach (var change in new Action<Backend>[] {
            backend => backend.Frame.Version = 2, backend => backend.Frame.Size = 56,
            backend => backend.Frame.Reserved = 1, backend => backend.Frame.Sequence = 0,
            backend => backend.Frame.Kind = NativeOperationKind.Reference1,
            backend => backend.Frame.Arg1 = new(1), backend => backend.Frame.Method = IntPtr.Zero })
        {
            using var f = new Fixture(); var play = f.Begin(NativeTweenOperation.Play, f.Tween.Target);
            f.Backend.Prepare(1u); change(f.Backend);
            Throws<InvalidOperationException>(() => f.Backend.TypedCapture(f.Backend.Frame));
            Check(f.Work.Unknown && f.Work.ReadSourceReplay().TypedEvents!.Count == 0); f.End(play);
        }
    }

    private static void CompletionParent()
    {
        using var f = new Fixture(); var play = f.Begin(NativeTweenOperation.Play, f.Tween.Target);
        var context = f.Backend.Begin(1u)!; var frame = f.Backend.Frame;
        var nested = f.Begin(NativeTweenOperation.Play, f.Tween.Target);
        Throws<InvalidOperationException>(() => f.Backend.Complete(context, frame, NativeOperationExitFlags.Returned));
        Check(f.Work.Unknown && f.Work.ReadSourceReplay().TypedEvents!.Single().Failure is not null);
        f.End(nested); f.Backend.Complete(context, frame, NativeOperationExitFlags.Returned); context.Dispose(); f.End(play);
    }

    private static void EarlyDisposal()
    {
        using var f = new Fixture(); var play = f.Begin(NativeTweenOperation.Play, f.Tween.Target);
        var context = f.Backend.Begin(1u)!;
        Throws<InvalidOperationException>(context.Dispose);
        Check(f.Work.Unknown && f.Work.ReadSourceReplay().TypedEvents!.Single() is { Disposed: false, NativeReturned: false, Failure: not null });
        f.Backend.Complete(context, f.Backend.Frame, NativeOperationExitFlags.Returned); context.Dispose(); f.End(play);
    }

    private static void CompletionFlags()
    {
        foreach (var flags in new[] { (NativeOperationExitFlags)0, NativeOperationExitFlags.Skipped,
            NativeOperationExitFlags.EnterFaulted, NativeOperationExitFlags.Returned | NativeOperationExitFlags.Abnormal,
            NativeOperationExitFlags.Returned | NativeOperationExitFlags.EnterFaulted,
            NativeOperationExitFlags.Abnormal | NativeOperationExitFlags.Skipped, (NativeOperationExitFlags)16 })
        {
            using var f = new Fixture(); var play = f.Begin(NativeTweenOperation.Play, f.Tween.Target);
            var context = f.Backend.Begin(1u)!;
            Throws<InvalidOperationException>(() => f.Backend.Complete(context, f.Backend.Frame, flags));
            Throws<InvalidOperationException>(context.Dispose);
            Check(f.Work.Unknown && f.Work.ReadSourceReplay().TypedEvents!.Single()
                is { NativeReturned: false, Abnormal: false, Disposed: false, Failure: not null }); f.End(play);
        }
    }

    private static void CompletionFrame()
    {
        using var f = new Fixture(); var play = f.Begin(NativeTweenOperation.Play, f.Tween.Target);
        var context = f.Backend.Begin(1u)!; var changed = f.Backend.Frame;
        changed.Arg0 = new(0x100000001L);
        Throws<InvalidOperationException>(() => f.Backend.Complete(context, changed, NativeOperationExitFlags.Returned));
        Check(f.Work.Unknown && !f.Work.ReadSourceReplay().TypedEvents!.Single().NativeReturned);
        f.Backend.Complete(context, f.Backend.Frame, NativeOperationExitFlags.Returned); context.Dispose(); f.End(play);
    }

    private static void DynamicMembership()
    {
        foreach (var eventIdentity in new long[] { 2003, 2001, 0 })
        {
            using var f = new Fixture();
            var outer = f.Begin(NativeTweenOperation.Play, f.Tween.Target);
            var root = f.Backend.RegisterDynamic(1003, eventIdentity);
            f.Work.AdoptControlledDynamicTypedEventForTests(1003, root);
            var first = f.Begin(NativeTweenOperation.Play, new(1003));
            if (eventIdentity != 0) f.Backend.InvokeEvent(eventIdentity, 0x7fc12345, NativeOperationExitFlags.Returned);
            else Check(f.Backend.Begin(1u, 9999) is null);
            f.End(first); f.End(outer);
            var rows = f.Work.ReadSourceReplay().TypedEvents!;
            Check(!f.Work.Unknown && f.Backend.DynamicBorrowed == 1
                && (eventIdentity == 0 ? rows.Count == 0 : rows.Count == 1
                    && rows[0] is { Tween: 1003, ValueBits: 0x7fc12345, Disposed: true }
                    && rows[0].Event == eventIdentity && rows[0].OperationSequence == (long)first.Frame.Sequence));
        }
    }

    private static void EntryParent()
    {
        using var f = new Fixture((1001, 2001), (1003, 2003));
        var play = f.Begin(NativeTweenOperation.Play, new(1003));
        Throws<InvalidOperationException>(() => f.Backend.Begin(1u, 2001));
        Check(f.Work.Unknown && f.Work.ReadSourceReplay().TypedEvents!.Count == 0); f.End(play);
    }

    private static void DisposalParent()
    {
        using var f = new Fixture(); var play = f.Begin(NativeTweenOperation.Play, f.Tween.Target);
        var context = f.Backend.Begin(1u)!;
        f.Backend.Complete(context, f.Backend.Frame, NativeOperationExitFlags.Returned);
        var nested = f.Begin(NativeTweenOperation.Play, f.Tween.Target);
        Throws<InvalidOperationException>(context.Dispose);
        Check(f.Work.Unknown && f.Work.ReadSourceReplay().TypedEvents!.Single() is { NativeReturned: true, Disposed: false, Failure: not null });
        f.End(nested); context.Dispose(); f.End(play);
    }

    private sealed class Fixture : IDisposable
    {
        internal const long Caller = 0x180395c07, Method = 0x777;
        internal readonly MainThreadDispatcher Main = new();
        internal readonly RootTable Roots;
        internal readonly Backend Backend;
        internal readonly NativeLocalTweenWorkOwner Work;
        internal readonly NativeRuntime.RootHandle Tween, Event;
        private ulong _sequence;
        internal Fixture(params (long Tween, long Event)[] sources)
        {
            if (sources.Length == 0) sources = new[] { (1001L, 2001L) };
            Roots = new(Main); Backend = new(Main, Roots, sources);
            Tween = Roots.Root(new(1001)); Event = Roots.Root(new(2001));
            Work = NativeLocalTweenWorkOwner.CaptureBackend(Main, Backend); Backend.Owner = Work;
        }
        internal NativeLocalTweenWorkOwner.OperationContext Begin(NativeTweenOperation operation, IntPtr arg)
        {
            var frame = operation == NativeTweenOperation.Play
                ? new NativeOperationFrame { Version = 1, Size = 64, Kind = NativeOperationKind.Void0,
                    Self = arg, Method = new(700), Caller = new(900), Sequence = ++_sequence }
                : new NativeOperationFrame { Version = 1, Size = 64, Kind = NativeOperationKind.Reference1,
                    Self = new(1002), Arg0 = arg, Method = new(701), Caller = new(901), Sequence = ++_sequence };
            return Work.CaptureOperation(operation, frame)
                ?? throw new InvalidOperationException("The parent operation was not captured.");
        }
        internal void End(NativeLocalTweenWorkOwner.OperationContext context,
            NativeOperationExitFlags flags = NativeOperationExitFlags.Returned)
        { Work.CompleteOperation(context, context.Frame, flags); context.Dispose(); }
        public void Dispose() => Main.Dispose();
    }

    private sealed class Backend : INativeLocalTweenBackend, INativeTweenTypedEventSource, INativeTweenDynamicTypedEventSource
    {
        private readonly MainThreadDispatcher _main; private readonly RootTable _roots;
        private readonly (long Tween, long Event)[] _sources;
        private readonly Dictionary<long, (NativeRuntime.RootHandle Tween, NativeRuntime.RootHandle? Event)> _dynamic = new();
        internal Backend(MainThreadDispatcher main, RootTable roots, (long Tween, long Event)[] sources)
            => (_main, _roots, _sources) = (main, roots, sources);
        internal NativeLocalTweenWorkOwner Owner = null!;
        internal bool Bound;
        internal int MembershipCount, ProcessCapacity;
        internal int DynamicBorrowed;
        internal Func<NativeOperationFrame, IDisposable?> TypedCapture = null!;
        internal Action<IDisposable, NativeOperationFrame, NativeOperationExitFlags> CompleteCallback = null!;
        internal Action<NativeOperationFrame, string> Fault = null!;
        internal NativeOperationFrame Frame = new();
        public bool CleanupUnknown => false; public int RootRecords => 2; public IntPtr PlayMethodInfo => new(700);
        public void Initialize(NativeLocalTweenWorkOwner owner)
        {
            Owner = owner;
            foreach (var source in _sources) owner.RegisterSource(source.Tween);
            owner.AdoptTypedEvents(this);
            Bound = true;
        }
        public NativeTweenWorkGraph Capture(NativeTweenOperation operation, NativeOperationFrame frame, bool after) => Refresh();
        public NativeTweenWorkGraph Refresh() => new(0, Array.Empty<NativeTweenDelegateObservation>(),
            Array.Empty<NativeTweenInfoObservation>(), Array.Empty<NativeTweenManagerObservation>());
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity() => new(0, true);
        public void BorrowTypedTweenEvents(NativeLocalTweenWorkOwner owner,
            Action<NativeRuntime.RootHandle, NativeExactReadField, NativeRuntime.RootHandle?> adopt)
        {
            _main.VerifyAccess(); var roots = owner.RequireActualTypedEventBorrowing(this);
            Equal(_sources.Length, roots.Count);
            var field = new NativeExactReadField(new(0x98), new(301), new(302), new(303),
                "onTween", "UnityEngine.Events.UnityEvent<System.Single>", false, 0x98, 8, 0);
            foreach (var source in _sources)
                adopt(_roots.Root(new(source.Tween)), field, source.Event == 0 ? null : _roots.Root(new(source.Event)));
        }
        public void BindTypedTweenEvents(NativeLocalTweenWorkOwner owner, int processCapacity,
            Func<NativeOperationFrame, IDisposable?> capture,
            Action<IDisposable, NativeOperationFrame, NativeOperationExitFlags> complete,
            Action<NativeOperationFrame, string> fault)
        {
            MembershipCount = owner.RequireTypedEventMemberships(this, processCapacity).Count;
            Equal(_sources.Count(value => value.Event != 0), MembershipCount); Equal(_sources.Length, processCapacity);
            ProcessCapacity = processCapacity;
            TypedCapture = capture; CompleteCallback = complete; Fault = fault;
        }
        internal NativeRuntime.RootHandle RegisterDynamic(long tween, long eventIdentity)
        {
            var root = _roots.Root(new(tween));
            _dynamic.Add(tween, (root, eventIdentity == 0 ? null : _roots.Root(new(eventIdentity)))); return root;
        }
        public void BorrowDynamicTypedTweenEvents(NativeLocalTweenWorkOwner owner, long tween,
            Action<NativeRuntime.RootHandle, NativeExactReadField, NativeRuntime.RootHandle?> adopt)
        {
            Check(ReferenceEquals(owner, Owner)); _main.VerifyAccess(); ++DynamicBorrowed;
            var field = new NativeExactReadField(new(0x98), new(301), new(302), new(303),
                "onTween", "UnityEngine.Events.UnityEvent<System.Single>", false, 0x98, 8, 0);
            var roots = _dynamic[tween]; adopt(roots.Tween, field, roots.Event);
        }
        internal IDisposable? Begin(uint bits, long eventIdentity = 2001)
        {
            Prepare(bits, eventIdentity);
            return TypedCapture(Frame);
        }
        internal void Prepare(uint bits, long eventIdentity = 2001)
        {
            Frame = new() { Version = 1, Size = 64, Kind = NativeOperationKind.Single1, Self = new(eventIdentity),
                Arg0 = new(unchecked((long)bits)), Method = new(Fixture.Method), Caller = new(Fixture.Caller), Sequence = 7 };
        }
        internal void Complete(IDisposable context, NativeOperationFrame frame, NativeOperationExitFlags flags)
            => CompleteCallback(context, frame, flags);
        internal void Invoke(uint bits, NativeOperationExitFlags flags) => InvokeEvent(2001, bits, flags);
        internal void InvokeEvent(long eventIdentity, uint bits, NativeOperationExitFlags flags)
        { var context = Begin(bits, eventIdentity) ?? throw new InvalidOperationException("No typed context.");
            try { CompleteCallback(context, Frame, flags); } finally { context.Dispose(); } }
    }
    private sealed class RootTable
    {
        private readonly MainThreadDispatcher _main; private readonly Dictionary<uint, IntPtr> _roots = new(); private uint _next;
        internal int Freed; internal RootTable(MainThreadDispatcher main) => _main = main;
        internal NativeRuntime.RootHandle Root(IntPtr value)
        { _main.VerifyAccess(); var key = ++_next; _roots.Add(key, value);
            return new(key, id => { _roots.Remove(id); ++Freed; }, id => _roots.GetValueOrDefault(id), _main.VerifyAccess); }
    }
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("Typed Tween event invariant failed."); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
