using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

// Actual coordinator and dispatcher callbacks over controlled transports. These
// cases issue no production publication, read no game pointers and patch no code.
internal static class NativeEngineRuntimeSubscriptionLifetimeTests
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Engine subscription side capture detach retains all descendants of its actual Start", SideCaptureDetach),
        ("Engine subscription nearest foreign Start masks retired ancestor membership then restores it", NearestStartMask),
        ("Engine subscription registers Start duty before consumer capture can detach and reenter", CaptureDetachReentry),
        ("Engine subscription peer detach before capture preserves already frozen Start membership", PeerDetachBeforeCapture),
        ("Engine subscription null Start context still retains physical descendant duty until exact exit", NullStartContext),
        ("Engine subscription capture failure stays Unknown while retained actual scope drains", FailedStartCapture),
        ("Engine subscription consumer completion and Dispose failures cannot abandon peer cleanup", CleanupFailures),
        ("Engine subscription retired Start membership cannot cross a worker thread", WorkerCannotBorrow),
        ("Engine subscription last actual Dispose retires all four bounded observer slots", RetiredBindingsReleaseCapacity),
        ("Engine subscription retains descendants inside its actual Complete and Dispose closing callbacks", ClosingCallbackReentry),
        ("Engine subscription cannot borrow an outer context after its nearest closing Start context is disposed", ClosingNearestMask)
    };
    private static void Check(bool value, string reason = "Engine subscription lifetime regression failed.")
    { if (!value) throw new InvalidOperationException(reason); }
    private sealed class Logger : ILogger { public void Log(LogLevel level, string message, Exception? error = null) { } }
    private sealed class Scope : IDisposable
    {
        internal readonly NativeEngineNodeKind Kind;
        internal int Completed, Disposed;
        internal Action? OnComplete, OnDispose;
        internal Scope(NativeEngineNodeKind kind = NativeEngineNodeKind.Start) => Kind = kind;
        public void Dispose() { ++Disposed; OnDispose?.Invoke(); }
    }
    private sealed class NodeBridge : INativeEngineNodeContextBridge
    {
        private Func<NativeEngineNodeFrame, nuint>? _enter;
        private Action<NativeEngineNodeFrame, nuint, NativeEngineNodeCompletion>? _exit;
        private long _inFlight;
        internal int Attaches;
        public NativeEngineNodeChannelIdentity Identity { get; }
        internal NodeBridge(NativeEngineNodeKind kind) => Identity = new(kind, (uint)kind + 4,
            new(0x1000 + (int)kind * 0x100), new(0x2000 + (int)kind * 0x100), new(0x3000 + (int)kind * 0x100), 7);
        public void Attach(Func<NativeEngineNodeFrame, nuint> enter, Action<NativeEngineNodeFrame, nuint, NativeEngineNodeCompletion> exit)
        { Check(++Attaches == 1); (_enter, _exit) = (enter, exit); }
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity() => new(Volatile.Read(ref _inFlight), true);
        internal nuint Begin(NativeEngineNodeFrame frame)
        { Interlocked.Increment(ref _inFlight); return _enter!(frame); }
        internal void End(Physical call)
        {
            var kind = call.Frame.Kind;
            _exit!(call.Frame, call.Token, new() { Version = 1, Size = 32,
                Flags = NativeEngineNodeExitFlags.Returned | (kind == NativeEngineNodeKind.Detach ? 0 : NativeEngineNodeExitFlags.ReturnByteValid),
                RawReturn = kind == NativeEngineNodeKind.Detach ? 0u : 0x80u,
                OutputFlags = kind == NativeEngineNodeKind.Start ? NativeEngineNodeOutputFlags.Requested | NativeEngineNodeOutputFlags.Read : 0 });
            Interlocked.Decrement(ref _inFlight);
        }
    }
    private sealed class Physical
    {
        internal readonly NativeEngineNodeFrame Frame;
        internal nuint Token;
        internal Physical(NativeEngineNodeFrame frame) => Frame = frame;
    }
    private sealed class SideBridge : INativeEngineInlineContextBridge
    {
        private Action<NativeEngineInlineFrame>? _observe;
        internal int Attaches;
        public NativeEngineInlineChannelIdentity Identity { get; }
        internal SideBridge(NativeEngineNodeChannelIdentity start) => Identity = new(11, 19,
            NativeEngineInlinePlatform.CurrentOsThreadId, new(0x4000), start,
            new() { SitePc = new(0x5000), RelayReturnPc = new(0x6000) },
            new() { SitePc = new(0x5010), RelayReturnPc = new(0x6010) },
            new() { SitePc = new(0x5020), RelayReturnPc = new(0x6020) });
        public void Attach(Action<NativeEngineInlineFrame> observe) { Check(++Attaches == 1); _observe = observe; }
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity() => new(0, true);
        internal void Send(NativeEngineInlineFrame frame) => _observe!(frame);
    }
    private sealed class Fixture
    {
        internal readonly NodeBridge[] Bridges = Enum.GetValues<NativeEngineNodeKind>().Select(kind => new NodeBridge(kind)).ToArray();
        internal readonly SideBridge Side;
        internal readonly NativeEngineRuntimeCoordinator Runtime;
        private readonly List<Physical> _open = new();
        private ulong _sequence, _event;
        internal Fixture()
        {
            Side = new(Bridges[0].Identity);
            Runtime = NativeEngineRuntimeCoordinator.ShareControlled(new object(), Bridges, new Logger(), Side);
        }
        internal Physical Begin(NativeEngineNodeKind kind = NativeEngineNodeKind.Start)
        {
            var frame = new NativeEngineNodeFrame { Version = 1, Size = 112, Kind = kind,
                Arg0 = new(301), Caller = new(0x8000), Sequence = ++_sequence };
            if (kind == NativeEngineNodeKind.Start)
            { frame.Arg0 = new(101); frame.Arg1 = new(1001); frame.Arg3 = new(0x9000); }
            else
            {
                frame.Node = frame.Arg0; frame.Owner = new(101); frame.EnumeratorHandle = 401;
                frame.EnumeratorCache = new(1001); frame.EnumeratorType = 2; frame.ReferenceCount = 1;
                frame.SnapshotFlags = NativeEngineNodeSnapshotFlags.Requested | NativeEngineNodeSnapshotFlags.Valid;
            }
            var call = new Physical(frame); _open.Add(call);
            call.Token = Bridges[(int)kind].Begin(frame); return call;
        }
        internal void End(Physical call)
        { Bridges[(int)call.Frame.Kind].End(call); _open.Remove(call); }
        internal void Call(NativeEngineNodeKind kind) { var call = Begin(kind); End(call); }
        internal void Send(Physical start, NativeEngineInlineKind kind)
        {
            var starts = _open.Where(call => call.Frame.Kind == NativeEngineNodeKind.Start).ToArray();
            var frame = new NativeEngineInlineFrame { Version = 1, Size = 144, Kind = kind,
                Flags = NativeEngineInlineFlags.ParentMatched, StartSlot = Bridges[0].Identity.Slot, ProbeSlot = Side.Identity.Slot,
                ThreadId = NativeEngineInlinePlatform.CurrentOsThreadId, Depth = checked((uint)starts.Length), Epoch = Side.Identity.Epoch,
                StartSequence = start.Frame.Sequence, ParentStartSequence = starts.Length > 1 ? starts[^2].Frame.Sequence : 0,
                EventSequence = ++_event, Owner = start.Frame.Arg0, EnumeratorCache = start.Frame.Arg1,
                OutputSlot = start.Frame.Arg3, SitePc = Bridges[0].Identity.Target };
            if (kind == NativeEngineInlineKind.StartExited)
            {
                frame.Flags |= NativeEngineInlineFlags.OutputRequested | NativeEngineInlineFlags.OutputRead;
                frame.StartExitFlags = NativeEngineNodeExitFlags.Returned | NativeEngineNodeExitFlags.ReturnByteValid;
                frame.RawStartReturn = 0x80;
            }
            Side.Send(frame);
        }
        internal NativeEngineRuntimeCoordinator.Subscription Bind(List<Scope> scopes,
            Func<NativeEngineNodeFrame, Scope?>? capture = null, Action<NativeEngineNodeFrame, string>? fault = null)
            => Runtime.Bind(frame => capture is null ? Add(scopes, frame.Kind) : capture(frame),
                (scope, _, _) => { ++scope.Completed; scope.OnComplete?.Invoke(); }, fault ?? ((_, _) => { }));
        internal void Drained(bool known = true) => Check(Runtime.ReadActivity() is { InFlight: 0 } activity
            && activity.Known == known && Runtime.Unknown != known && Bridges.All(bridge => bridge.Attaches == 1) && Side.Attaches == 1);
    }
    private static Scope Add(List<Scope> scopes, NativeEngineNodeKind kind)
    { var scope = new Scope(kind); scopes.Add(scope); return scope; }
    private static void Finished(List<Scope> scopes) => Check(scopes.All(scope => scope.Completed == 1 && scope.Disposed == 1));

    private static void SideCaptureDetach()
    {
        var f = new Fixture(); var core = new List<Scope>(); var side = new Scope(); var captured = 0; var observed = 0; var faults = 0;
        NativeEngineRuntimeCoordinator.Subscription? subscription = null;
        subscription = f.Runtime.BindWithInline(frame => Add(core, frame.Kind), (scope, _, _) => ++scope.Completed, (_, _) => ++faults,
            _ => { ++captured; subscription!.Dispose(); return side; }, (_, _) => ++observed, (_, _) => ++faults);
        var start = f.Begin(); f.Send(start, NativeEngineInlineKind.StartEntered);
        foreach (var kind in Enum.GetValues<NativeEngineNodeKind>().Skip(1)) f.Call(kind);
        f.Send(start, NativeEngineInlineKind.StartExited);
        Check(side.Disposed == 1 && core[0].Disposed == 0);
        f.End(start); var before = core.Count;
        foreach (var kind in Enum.GetValues<NativeEngineNodeKind>()) f.Call(kind);
        Check(before == 4 && core.Count == before && captured == 1 && observed == 1 && faults == 0);
        Finished(core); f.Drained(); subscription.Dispose();
    }
    private static void NearestStartMask()
    {
        var f = new Fixture(); var scopes = new List<Scope>(); var subscription = f.Bind(scopes);
        var outer = f.Begin(); subscription.Dispose(); var execute = f.Begin(NativeEngineNodeKind.Execute);
        var inner = f.Begin();
        foreach (var kind in Enum.GetValues<NativeEngineNodeKind>().Skip(1)) f.Call(kind);
        Check(scopes.Count == 2); f.End(inner); f.End(execute);
        f.Call(NativeEngineNodeKind.Release); f.Call(NativeEngineNodeKind.Detach); f.End(outer);
        foreach (var kind in Enum.GetValues<NativeEngineNodeKind>()) f.Call(kind);
        Check(scopes.Select(scope => scope.Kind).SequenceEqual(Enum.GetValues<NativeEngineNodeKind>()));
        Finished(scopes); f.Drained();
    }
    private static void CaptureDetachReentry()
    {
        var f = new Fixture(); var scopes = new List<Scope>(); var nested = false;
        NativeEngineRuntimeCoordinator.Subscription? subscription = null;
        subscription = f.Bind(scopes, frame => {
            if (frame.Kind == NativeEngineNodeKind.Start)
            {
                subscription!.Dispose();
                foreach (var kind in Enum.GetValues<NativeEngineNodeKind>().Skip(1)) f.Call(kind);
                nested = scopes.Count == 3;
            }
            return Add(scopes, frame.Kind);
        });
        var start = f.Begin(); Check(nested && scopes.Count == 4); f.End(start);
        f.Call(NativeEngineNodeKind.Execute); Check(scopes.Count == 4); Finished(scopes); f.Drained();
    }
    private static void PeerDetachBeforeCapture()
    {
        var f = new Fixture(); var first = new List<Scope>(); var second = new List<Scope>();
        NativeEngineRuntimeCoordinator.Subscription? secondBinding = null; var changed = false;
        using var firstBinding = f.Bind(first, frame => {
            if (frame.Kind == NativeEngineNodeKind.Start && !changed)
            { changed = true; secondBinding!.Dispose(); f.Call(NativeEngineNodeKind.Execute); }
            return Add(first, frame.Kind);
        });
        secondBinding = f.Bind(second);
        var start = f.Begin(); f.Call(NativeEngineNodeKind.Detach); f.End(start);
        Check(second.Select(scope => scope.Kind).SequenceEqual(new[] { NativeEngineNodeKind.Execute, NativeEngineNodeKind.Start, NativeEngineNodeKind.Detach }));
        var before = second.Count; f.Call(NativeEngineNodeKind.Start); f.Call(NativeEngineNodeKind.Execute);
        Check(second.Count == before); Finished(first); Finished(second); f.Drained(); secondBinding.Dispose();
    }
    private static void NullStartContext()
    {
        var f = new Fixture(); var scopes = new List<Scope>(); var captures = 0;
        var subscription = f.Bind(scopes, frame => { ++captures; return frame.Kind == NativeEngineNodeKind.Start ? null : Add(scopes, frame.Kind); });
        var start = f.Begin(); subscription.Dispose(); f.Call(NativeEngineNodeKind.Execute); f.Call(NativeEngineNodeKind.Detach); f.End(start);
        f.Call(NativeEngineNodeKind.Execute); Check(captures == 3 && scopes.Count == 2); Finished(scopes); f.Drained();
    }
    private static void FailedStartCapture()
    {
        var f = new Fixture(); var scopes = new List<Scope>(); var faults = 0; var captures = 0;
        NativeEngineRuntimeCoordinator.Subscription? subscription = null;
        subscription = f.Bind(scopes, frame => {
            ++captures;
            if (frame.Kind == NativeEngineNodeKind.Start) { subscription!.Dispose(); throw new InvalidOperationException("Controlled Start capture failure."); }
            return Add(scopes, frame.Kind);
        }, (_, _) => ++faults);
        var start = f.Begin(); f.Call(NativeEngineNodeKind.Execute); f.End(start); f.Call(NativeEngineNodeKind.Execute);
        Check(captures == 2 && faults == 1 && scopes.Count == 1); Finished(scopes); f.Drained(false);
    }
    private static void CleanupFailures()
    {
        var f = new Fixture(); var scopes = new List<Scope>(); var peer = new List<Scope>(); var faults = 0;
        var subscription = f.Bind(scopes, frame => {
            var scope = Add(scopes, frame.Kind);
            scope.OnComplete = () => throw new InvalidOperationException("Controlled completion failure.");
            scope.OnDispose = () => throw new InvalidOperationException("Controlled Dispose failure."); return scope;
        }, (_, _) => ++faults);
        using var other = f.Bind(peer); var start = f.Begin(); subscription.Dispose(); f.End(start);
        Check(faults == 2); Finished(scopes); Finished(peer); var before = scopes.Count;
        f.Call(NativeEngineNodeKind.Execute); Check(scopes.Count == before && peer.Count == 2); Finished(peer); f.Drained(false);
    }
    private static void WorkerCannotBorrow()
    {
        var f = new Fixture(); var scopes = new List<Scope>(); var subscription = f.Bind(scopes);
        var start = f.Begin(); subscription.Dispose();
        Task.Run(() => f.Call(NativeEngineNodeKind.Execute)).GetAwaiter().GetResult();
        Check(scopes.Count == 1); f.Call(NativeEngineNodeKind.Execute); f.End(start);
        Check(scopes.Count == 2); Finished(scopes); f.Drained();
    }
    private static void RetiredBindingsReleaseCapacity()
    {
        var f = new Fixture(); var scopes = new List<Scope>();
        // More subscriptions than a dispatcher's 32 simultaneous sinks proves
        // final physical cleanup removes each retired four-channel membership.
        for (var index = 0; index < 40; ++index)
        {
            var subscription = f.Bind(scopes); var start = f.Begin(); subscription.Dispose();
            f.Call(NativeEngineNodeKind.Detach); f.End(start);
            var before = scopes.Count;
            foreach (var kind in Enum.GetValues<NativeEngineNodeKind>()) f.Call(kind);
            Check(scopes.Count == before);
        }
        Check(scopes.Count == 80); Finished(scopes); f.Drained();
    }
    private static void ClosingCallbackReentry()
    {
        var f = new Fixture(); var scopes = new List<Scope>(); var completeChildren = 0; var disposeChildren = 0;
        NativeEngineRuntimeCoordinator.Subscription? subscription = null;
        subscription = f.Bind(scopes, frame => {
            var scope = Add(scopes, frame.Kind);
            if (frame.Kind == NativeEngineNodeKind.Start)
            {
                scope.OnComplete = () => {
                    subscription!.Dispose();
                    foreach (var kind in Enum.GetValues<NativeEngineNodeKind>().Skip(1)) f.Call(kind);
                    completeChildren = scopes.Count - 1;
                };
                scope.OnDispose = () => {
                    foreach (var kind in Enum.GetValues<NativeEngineNodeKind>().Skip(1)) f.Call(kind);
                    disposeChildren = scopes.Count - 1 - completeChildren;
                };
            }
            return scope;
        });
        var start = f.Begin(); f.End(start);
        Check(completeChildren == 3 && disposeChildren == 3 && scopes.Count == 7);
        foreach (var kind in Enum.GetValues<NativeEngineNodeKind>()) f.Call(kind);
        Check(scopes.Count == 7); Finished(scopes); f.Drained(); subscription.Dispose();
    }
    private static void ClosingNearestMask()
    {
        var f = new Fixture(); var ours = new List<Scope>(); var peers = new List<Scope>();
        Physical? inner = null;
        // Reverse Dispose order ends our inner context before the peer invokes
        // children. The still-open outer context must not mask that boundary.
        using var peer = f.Bind(peers, frame => {
            var scope = Add(peers, frame.Kind);
            if (frame.Kind == NativeEngineNodeKind.Start)
                scope.OnDispose = () => {
                    if (frame.Sequence == inner?.Frame.Sequence)
                    { f.Call(NativeEngineNodeKind.Execute); f.Call(NativeEngineNodeKind.Release); }
                };
            return scope;
        });
        var subscription = f.Bind(ours);
        var outer = f.Begin(); inner = f.Begin(); subscription.Dispose(); f.End(inner);
        Check(ours.Count == 2 && peers.Count == 4);
        f.Call(NativeEngineNodeKind.Execute); f.End(outer);
        Check(ours.Count == 3 && peers.Count == 5); Finished(ours); Finished(peers);
        f.Call(NativeEngineNodeKind.Detach); Check(ours.Count == 3 && peers.Count == 6); f.Drained();
    }
}
