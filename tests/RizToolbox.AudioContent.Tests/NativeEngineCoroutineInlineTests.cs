using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

// Controlled bridges execute the actual runtime dispatchers, coordinator and
// Adapter identity/node/inline ledgers. No native capability, game publication,
// Unity pointer access, cancellation completion or root release is performed.
internal static class NativeEngineCoroutineInlineTests
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Inline side ABI1 preserves 144 scalar offsets and independent config observer information layouts", Layout),
        ("Inline Begin EndFree native Start return and original Dispose confirm only the exact generation", EndFree),
        ("Inline EndRetained proves script cleanup without claiming native memory freed or cancellation", EndRetained),
        ("Inline native endpoint alone never clears final cleanup before both real Start completion and Dispose", CompletionWindows),
        ("Inline null output raw AL and referencecount alone never sign a missing cleanup branch", NoBranch),
        ("Inline endpoint AL must match native side exit and the actual original completion", ReturnMismatch),
        ("Inline cached owner handle node and outputslot changes remain Unknown with retained responsibility", TupleMismatch),
        ("Inline missing End abnormal native exit and unreadable snapshots cannot clear cleanup obligation", MissingAndAbnormal),
        ("Inline Begin cannot borrow a still active Execute scope or invent an unobserved node", ExecuteScope),
        ("Inline side events require the unique current actual Start rather than copied stale receipts", ActualProof),
        ("Inline nested foreign Start masks outer association and Start only depth spans other engine kinds", PhysicalParents),
        ("Inline same receiver Start rejection reports its own nested scope and missing tracker match", MissingIdentityDiagnostic),
        ("Inline Start rejection preserves bounded first node and identity failures", NodeFailureDiagnostic),
        ("Inline finalized native address reuse retains old generation proof independently", Generations),
        ("Inline duplicate Begin End or exit cannot erase the original physical responsibility", Duplicates),
        ("Inline worker records cannot borrow a main thread physical Start or run provider reads", Worker),
        ("Inline actual runtime owner shares one side family across consumers and freezes detached contexts", SharingAndDetach),
        ("Inline membership survives disposal inside the already admitted typed Start capture", TypedCaptureDetach),
        ("Inline typed capture peer detach and new Bind preserve the original admitted membership", TypedCaptureMembership),
        ("Inline missing capability refusal leaves core channels usable and rejects late or different family adoption", CapabilityAndFamily),
        ("Inline native activity and callback faults make the shared runtime permanently Unknown", StickyFaults),
        ("Inline configuration information requires exact helper epoch Start target and clean fault counters", Information),
        ("Inline cancellation readiness requires actual side exit and both original completion and disposal", CompletedStartProof)
    };
    private static void Check(bool value, string reason = "Controlled inline completion regression failed.")
    { if (!value) throw new InvalidOperationException(reason); }
    private static void Reject(Action action)
    { try { action(); } catch (InvalidOperationException) { return; } catch (NotSupportedException) { return; } throw new InvalidOperationException("Expected inline ownership refusal."); }
    private sealed class Logger : ILogger
    {
        internal readonly ConcurrentQueue<(string Message, Exception? Error)> Errors = new();
        public void Log(LogLevel level, string message, Exception? error = null)
        {
            if (level == LogLevel.Error) Errors.Enqueue((message, error));
        }
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
        internal Physical Begin(NativeEngineNodeFrame frame)
        { Interlocked.Increment(ref _inFlight); return new(this, frame, _enter!(frame)); }
        internal void End(Physical call, NativeEngineNodeCompletion completion)
        { _exit!(call.Frame, call.Token, completion); Interlocked.Decrement(ref _inFlight); }
    }
    private sealed class Physical
    {
        internal readonly NodeBridge Bridge;
        internal readonly NativeEngineNodeFrame Frame;
        internal readonly nuint Token;
        internal Physical(NodeBridge bridge, NativeEngineNodeFrame frame, nuint token) => (Bridge, Frame, Token) = (bridge, frame, token);
    }
    private sealed class SideBridge : INativeEngineInlineContextBridge
    {
        private Action<NativeEngineInlineFrame>? _observe;
        internal bool Known = true;
        internal int Attaches;
        public NativeEngineInlineChannelIdentity Identity { get; }
        internal SideBridge(NativeEngineNodeChannelIdentity start, uint slot = 11) => Identity = new(slot, 19,
            NativeEngineInlinePlatform.CurrentOsThreadId, new(0x4000), start,
            new() { SitePc = new(0x5000), RelayReturnPc = new(0x6000) },
            new() { SitePc = new(0x5010), RelayReturnPc = new(0x6010) },
            new() { SitePc = new(0x5020), RelayReturnPc = new(0x6020) });
        public void Attach(Action<NativeEngineInlineFrame> observe) { Check(++Attaches == 1); _observe = observe; }
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity() => new(0, Known);
        internal void Send(NativeEngineInlineFrame frame) => _observe!(frame);
    }
    private sealed class Provider
    {
        internal readonly object Owner = new();
        internal NativeCoroutineCancellationIdentity Row;
        internal int Reads;
        internal Provider() => Reset(1, 1001);
        internal void Reset(long generation, long enumerator) => Row = new(Owner, 101, 0, generation, enumerator,
            0, false, false, true, false, false, true);
        internal NativeCoroutineCancellationIdentityProvider Description => new(Owner, 101,
            () => { ++Reads; return new[] { Row }; }, value => Row.SameRecord(value));
    }
    private sealed class Harness : IDisposable
    {
        internal const long NativeOwner = 0x12340000;
        internal readonly object Key = new();
        internal readonly MainThreadDispatcher Main = new();
        internal readonly Provider Provider = new();
        internal readonly Logger Log = new();
        internal readonly NodeBridge[] Bridges = Enum.GetValues<NativeEngineNodeKind>().Select(value => new NodeBridge(value)).ToArray();
        internal readonly SideBridge Side;
        internal readonly NativeEngineRuntimeCoordinator Runtime;
        internal readonly NativeEngineCoroutineNodeLedger Nodes;
        internal readonly NativeEngineCoroutineIdentityLedger Identities;
        internal readonly NativeEngineCoroutineInlineLedger Inline;
        internal readonly NativeEngineRuntimeCoordinator.Subscription Subscription;
        internal NativeEngineInlineObserverDispatcher.Observation? LastReceipt;
        internal NativeEngineCoroutineInlineLedger.Context? LastContext;
        internal NativeEngineCoroutineIdentityLedger.Call? LastCall;
        private readonly List<Physical> _open = new();
        private readonly HashSet<ulong> _sideOpen = new();
        private readonly Dictionary<ulong, NativeEngineInlineFrame> _begins = new();
        private ulong _nextNode, _nextEvent;
        internal Harness(bool bind = true)
        {
            Side = new(Bridges[0].Identity);
            Runtime = NativeEngineRuntimeCoordinator.ShareControlled(Key, Bridges, Log, Side);
            Nodes = new(Main); Identities = new(Main, Nodes, new[] { Provider.Description }, new[] { new NativeEngineCoroutineOwnerPair(101, NativeOwner) });
            Inline = new(Main, Identities, Nodes);
            Subscription = bind ? Runtime.BindWithInline(frame => LastCall = Identities.Capture(frame), Identities.Complete, Identities.ObserveFault,
                receipt => { LastReceipt = receipt; return LastContext = Inline.Capture(receipt); },
                (context, receipt) => { LastReceipt = receipt; Inline.Observe(context, receipt); }, Inline.ObserveFault)
                : Runtime.Bind<NativeEngineCoroutineIdentityLedger.Call>(_ => null, (_, _, _) => { }, (_, _) => { });
        }
        internal Physical Start(long owner = NativeOwner, long enumerator = 1001, long nameArgument = 0, long caller = 0x8000)
        {
            var frame = new NativeEngineNodeFrame { Version = 1, Size = 112, Kind = NativeEngineNodeKind.Start,
                Arg0 = new(owner), Arg1 = new(enumerator), Arg2 = new(nameArgument), Arg3 = new(0x9000),
                Caller = new(caller), Sequence = ++_nextNode };
            var call = Bridges[0].Begin(frame); _open.Add(call); return call;
        }
        internal Physical Execute(long node = 301, long owner = NativeOwner, long enumerator = 1001)
        {
            var frame = new NativeEngineNodeFrame { Version = 1, Size = 112, Kind = NativeEngineNodeKind.Execute,
                Arg0 = new(node), Node = new(node), Owner = new(owner), EnumeratorHandle = 401, EnumeratorCache = new(enumerator),
                EnumeratorType = 2, ReferenceCount = 1, Caller = new(0x8000), Sequence = ++_nextNode,
                SnapshotFlags = NativeEngineNodeSnapshotFlags.Requested | NativeEngineNodeSnapshotFlags.Valid };
            var call = Bridges[1].Begin(frame); _open.Add(call); return call;
        }
        internal void Return(Physical call, uint raw = 0x80, long output = 0, bool abnormal = false)
        {
            var completion = Done(call.Frame.Kind, raw, output);
            if (abnormal) completion = new() { Version = 1, Size = 32, Flags = NativeEngineNodeExitFlags.Abnormal };
            call.Bridge.End(call, completion); _open.Remove(call);
        }
        internal NativeEngineInlineFrame Event(Physical start, NativeEngineInlineKind kind, uint raw = 0x80,
            long output = 0, uint? depth = null, ulong? parent = null)
        {
            var starts = _open.Where(call => call.Frame.Kind == NativeEngineNodeKind.Start).ToArray();
            var frame = _begins.TryGetValue(start.Frame.Sequence, out var begin) && kind != NativeEngineInlineKind.StartEntered ? begin : default;
            frame.Version = 1; frame.Size = 144; frame.Kind = kind; frame.Flags |= NativeEngineInlineFlags.ParentMatched;
            frame.StartSlot = Bridges[0].Identity.Slot; frame.ProbeSlot = Side.Identity.Slot;
            frame.ThreadId = NativeEngineInlinePlatform.CurrentOsThreadId; frame.Depth = depth ?? checked((uint)starts.Length);
            frame.Epoch = Side.Identity.Epoch; frame.StartSequence = start.Frame.Sequence;
            frame.ParentStartSequence = parent ?? (starts.Length > 1 ? starts[^2].Frame.Sequence : 0);
            frame.EventSequence = ++_nextEvent; frame.Owner = start.Frame.Arg0;
            frame.EnumeratorCache = start.Frame.Arg1; frame.OutputSlot = start.Frame.Arg3;
            frame.SitePc = Bridges[0].Identity.Target; frame.RelayReturnPc = IntPtr.Zero;
            frame.StartExitFlags = 0; frame.RawStartReturn = 0; frame.StartOutputNode = IntPtr.Zero;
            if (kind == NativeEngineInlineKind.Begin)
            {
                frame.Flags |= NativeEngineInlineFlags.SnapshotRequested | NativeEngineInlineFlags.SnapshotValid;
                frame.Node = new(301); frame.EnumeratorHandle = 401; frame.EnumeratorType = 2; frame.ReferenceCount = 1;
            }
            if (kind is NativeEngineInlineKind.Begin or NativeEngineInlineKind.EndFree or NativeEngineInlineKind.EndRetained)
            {
                var site = kind == NativeEngineInlineKind.Begin ? Side.Identity.Begin
                    : kind == NativeEngineInlineKind.EndFree ? Side.Identity.EndFree : Side.Identity.EndRetained;
                frame.SitePc = site.SitePc; frame.RelayReturnPc = site.RelayReturnPc;
                if (kind != NativeEngineInlineKind.Begin) frame.RawStartReturn = raw;
            }
            if (kind == NativeEngineInlineKind.StartExited)
            {
                frame.Flags |= NativeEngineInlineFlags.OutputRequested | NativeEngineInlineFlags.OutputRead;
                frame.StartExitFlags = NativeEngineNodeExitFlags.Returned | NativeEngineNodeExitFlags.ReturnByteValid;
                frame.RawStartReturn = raw; frame.StartOutputNode = new(output);
            }
            return frame;
        }
        internal void Send(NativeEngineInlineFrame frame)
        {
            if (frame.Kind == NativeEngineInlineKind.StartEntered) _sideOpen.Add(frame.StartSequence);
            if (frame.Kind == NativeEngineInlineKind.Begin) _begins[frame.StartSequence] = frame;
            Side.Send(frame);
            if (frame.Kind == NativeEngineInlineKind.StartExited) _sideOpen.Remove(frame.StartSequence);
        }
        internal Physical Begin()
        { var start = Start(); Send(Event(start, NativeEngineInlineKind.StartEntered)); var execute = Execute(); Return(execute); Send(Event(start, NativeEngineInlineKind.Begin)); return start; }
        internal void Finish(Physical start, NativeEngineInlineKind endpoint = NativeEngineInlineKind.EndFree, uint raw = 0x80)
        { Send(Event(start, endpoint, raw)); Send(Event(start, NativeEngineInlineKind.StartExited, raw)); Return(start, raw); }
        internal NativeEngineCoroutineNodeSnapshot One() => Nodes.Read().Nodes.Single();
        public void Dispose()
        {
            foreach (var call in _open.AsEnumerable().Reverse().ToArray())
            {
                if (_sideOpen.Contains(call.Frame.Sequence)) Send(Event(call, NativeEngineInlineKind.StartExited));
                Return(call);
            }
            Subscription.Dispose(); Main.Dispose();
        }
    }
    private static NativeEngineNodeCompletion Done(NativeEngineNodeKind kind, uint raw = 0x80, long output = 0) => new() {
        Version = 1, Size = 32, Flags = NativeEngineNodeExitFlags.Returned | (kind == NativeEngineNodeKind.Detach ? 0 : NativeEngineNodeExitFlags.ReturnByteValid),
        RawReturn = kind == NativeEngineNodeKind.Detach ? 0 : raw, StartOutputNode = new(output),
        OutputFlags = kind == NativeEngineNodeKind.Start ? NativeEngineNodeOutputFlags.Requested | NativeEngineNodeOutputFlags.Read : 0 };

    private static void MissingIdentityDiagnostic()
    {
        using var f = new Harness();
        var parent = f.Start();
        f.Send(f.Event(parent, NativeEngineInlineKind.StartEntered));
        Check(f.LastContext is not null && f.Provider.Reads == 1 && f.Log.Errors.IsEmpty);

        var foreign = f.Start(owner: 999, enumerator: 2002);
        f.Send(f.Event(foreign, NativeEngineInlineKind.StartEntered));
        Check(f.LastContext is null && f.Provider.Reads == 1 && f.Log.Errors.IsEmpty);
        f.Send(f.Event(foreign, NativeEngineInlineKind.StartExited));
        f.Return(foreign);
        Check(!f.Runtime.Unknown);

        var child = f.Start(enumerator: 2002, nameArgument: 27, caller: 0x8123);
        var reads = f.Provider.Reads;
        f.Send(f.Event(child, NativeEngineInlineKind.StartEntered));
        var fault = f.Log.Errors.Single();
        var detail = fault.Error?.Message ?? string.Empty;
        Check(fault.Message == "Native inline Start capture failed." && fault.Error is InvalidOperationException);
        Check(detail.Contains($"startSequence={child.Frame.Sequence} parentStartSequence={parent.Frame.Sequence} depth=2", StringComparison.Ordinal)
            && detail.Contains("caller=0x8123 startTarget=0x1000 ownerPair=0", StringComparison.Ordinal)
            && detail.Contains("nativeOwner=0x12340000 enumerator=0x7d2 nameArgumentPresent=True", StringComparison.Ordinal)
            && detail.Contains("identityAttached=False nodeScopeAttached=True nodeScopeCurrent=True", StringComparison.Ordinal)
            && detail.Contains("identityFailure=[Owned engine Start has no unique current tracker Enumerator identity (matches=0).] nodeFailure=[none]", StringComparison.Ordinal));
        Check(reads == 2 && f.Provider.Reads == reads && f.Runtime.Unknown && f.Inline.Unknown
            && f.Identities.Read() is { Unknown: true, Pending: true, Outstanding: 2, Identities.Count: 1 });

        f.Send(f.Event(child, NativeEngineInlineKind.StartExited));
        f.Return(child);
        f.Send(f.Event(parent, NativeEngineInlineKind.StartExited));
        f.Return(parent);
        Check(!f.Runtime.ReadActivity().Known && f.Inline.Unknown && f.Provider.Reads == reads);
    }

    private static void NodeFailureDiagnostic()
    {
        using var f = new Harness();
        const string reasonPrefix = "First controlled node failure ";
        f.Nodes.ObserveFault(default, reasonPrefix + new string('x', 900));
        var start = f.Start();
        f.Send(f.Event(start, NativeEngineInlineKind.StartEntered));
        var fault = f.Log.Errors.Single();
        var detail = fault.Error?.Message ?? string.Empty;
        Check(fault.Message == "Native inline Start capture failed." && fault.Error is InvalidOperationException);
        Check(detail.Length <= 768 && detail.Contains("identityUnknown=True nodeUnknown=True", StringComparison.Ordinal)
            && detail.Contains("identityFailure=[Native node correlation is Unknown.] nodeFailure=[" + reasonPrefix, StringComparison.Ordinal)
            && detail.EndsWith("...]", StringComparison.Ordinal));
        Check(f.Provider.Reads == 0 && f.Runtime.Unknown && f.Identities.Unknown && f.Nodes.Unknown);
        f.Send(f.Event(start, NativeEngineInlineKind.StartExited));
        f.Return(start);
        Check(!f.Runtime.ReadActivity().Known);
    }

    private static void Layout()
    {
        Check(Marshal.SizeOf<NativeEngineInlineFrame>() == 144 && Marshal.SizeOf<NativeEngineInlineConfiguration>() == 88
            && Marshal.SizeOf<NativeEngineInlineInformation>() == 160 && Marshal.SizeOf<NativeEngineInlineSite>() == 16);
        var offsets = new (string Name, int Offset)[] { (nameof(NativeEngineInlineFrame.Kind), 8), (nameof(NativeEngineInlineFrame.Flags), 12),
            (nameof(NativeEngineInlineFrame.StartSlot), 16), (nameof(NativeEngineInlineFrame.ProbeSlot), 20), (nameof(NativeEngineInlineFrame.ThreadId), 24),
            (nameof(NativeEngineInlineFrame.Depth), 28), (nameof(NativeEngineInlineFrame.Epoch), 32), (nameof(NativeEngineInlineFrame.StartSequence), 40),
            (nameof(NativeEngineInlineFrame.ParentStartSequence), 48), (nameof(NativeEngineInlineFrame.EventSequence), 56), (nameof(NativeEngineInlineFrame.Node), 64),
            (nameof(NativeEngineInlineFrame.Owner), 72), (nameof(NativeEngineInlineFrame.EnumeratorHandle), 80), (nameof(NativeEngineInlineFrame.EnumeratorCache), 88),
            (nameof(NativeEngineInlineFrame.EnumeratorType), 96), (nameof(NativeEngineInlineFrame.ReferenceCount), 100), (nameof(NativeEngineInlineFrame.SitePc), 104),
            (nameof(NativeEngineInlineFrame.RelayReturnPc), 112), (nameof(NativeEngineInlineFrame.OutputSlot), 120), (nameof(NativeEngineInlineFrame.StartExitFlags), 128),
            (nameof(NativeEngineInlineFrame.RawStartReturn), 132), (nameof(NativeEngineInlineFrame.StartOutputNode), 136) };
        foreach (var pair in offsets) Check(Marshal.OffsetOf<NativeEngineInlineFrame>(pair.Name).ToInt32() == pair.Offset);
        Check(Marshal.SizeOf<NativeEngineNodeFrame>() == 112 && Marshal.SizeOf<NativeEngineNodeCompletion>() == 32);
    }
    private static void EndFree()
    {
        using var f = new Harness(); var start = f.Begin(); var before = f.One(); f.Finish(start);
        Check(!f.Nodes.Unknown && !f.Identities.Unknown && !f.Inline.Unknown && f.Runtime.ReadActivity() is { Known: true, InFlight: 0 });
        Check(f.One() is { FinalCleanupObserved: true, InlineCleanupUnproven: false, InlineBranchObserved: true, InlineEndpoint: NativeEngineInlineKind.EndFree });
        Check(f.Nodes.Read() is { Outstanding: 0, Pending: true, EngineCancelled: false }
            && f.Inline.Read() is { Starts: 1, Outstanding: 0, Branches: 1 } && before.InlineCleanupUnproven && !before.FinalCleanupObserved);
    }
    private static void EndRetained()
    {
        using var f = new Harness(); var start = f.Begin(); f.Finish(start, NativeEngineInlineKind.EndRetained);
        Check(!f.Nodes.Unknown && f.One() is { FinalCleanupObserved: true, InlineEndpoint: NativeEngineInlineKind.EndRetained }
            && !f.Nodes.Read().EngineCancelled && f.Identities.Read().Pending);
    }
    private static void CompletedStartProof()
    {
        using (var f=new Harness())
        {
            var start=f.Begin();
            var selected=f.Identities.Read().Identities.Single();
            Check(!f.Inline.HasCompletedStart(selected));
            f.Send(f.Event(start,NativeEngineInlineKind.EndFree));
            f.Send(f.Event(start,NativeEngineInlineKind.StartExited));
            Check(!f.Inline.HasCompletedStart(selected), "Exited/disposed side observation cannot stand in for the still-open original Start.");
            f.Return(start); selected=f.Identities.Read().Identities.Single();
            Check(f.Inline.HasCompletedStart(selected));
            Check(!f.Inline.HasCompletedStart(selected with { EngineGeneration=selected.EngineGeneration+1 }));
        }
        using (var f=new Harness())
        {
            var start=f.Start();f.Send(f.Event(start,NativeEngineInlineKind.StartEntered));
            var execute=f.Execute();f.Return(execute);
            f.Send(f.Event(start,NativeEngineInlineKind.StartExited,output:301));f.Return(start,output:301);
            var selected=f.Identities.Read().Identities.Single();
            Check(!f.Nodes.Unknown && f.Inline.HasCompletedStart(selected),
                "A normally returned node has a real completed side context even though no inline-cleanup Begin was taken.");
        }
    }
    private static void CompletionWindows()
    {
        using var f = new Harness(); var start = f.Begin(); f.Send(f.Event(start, NativeEngineInlineKind.EndFree));
        Check(!f.One().FinalCleanupObserved && f.One().InlineCleanupUnproven);
        f.Send(f.Event(start, NativeEngineInlineKind.StartExited));
        Check(!f.One().FinalCleanupObserved && !f.LastContext!.Start.Completed && f.Inline.Read().Outstanding == 0);
        f.Return(start); Check(!f.Nodes.Unknown && f.One().FinalCleanupObserved);
        // A peer captured at the actual physical Start observes the interval
        // after normal Complete and before the retained Adapter scope Dispose.
        using var second = new Harness(); var completedWindow = false; var disposedWindow = false;
        using var peer = second.Runtime.Bind(frame => frame.Kind == NativeEngineNodeKind.Start
                ? new Scope { OnDispose = () => { disposedWindow = true; Check(!second.One().FinalCleanupObserved); } } : null,
            (_, _, _) => { completedWindow = true; Check(second.LastContext!.Start.Completed
                && !second.LastContext.Start.Disposed && !second.One().FinalCleanupObserved); }, (_, _) => { });
        var s = second.Begin(); second.Finish(s);
        Check(completedWindow && disposedWindow && !second.Nodes.Unknown && second.One().FinalCleanupObserved);
    }
    private static void NoBranch()
    {
        using (var f = new Harness())
        {
            var s = f.Start(); f.Send(f.Event(s, NativeEngineInlineKind.StartEntered)); var e = f.Execute(); f.Return(e, 0);
            f.Send(f.Event(s, NativeEngineInlineKind.StartExited, 0)); f.Return(s, 0);
            Check(!f.Nodes.Unknown && f.One() is { FinalCleanupObserved: false, InlineCleanupUnproven: true, InlineBranchObserved: false });
        }
        using (var f = new Harness())
        {
            var s = f.Start(); f.Send(f.Event(s, NativeEngineInlineKind.StartEntered)); var e = f.Execute(); f.Return(e);
            var b = f.Event(s, NativeEngineInlineKind.Begin); b.ReferenceCount = 0; f.Send(b);
            Check(f.Nodes.Unknown && !f.One().FinalCleanupObserved);
        }
    }
    private static void ReturnMismatch()
    {
        using (var f = new Harness())
        {
            var s = f.Begin(); f.Send(f.Event(s, NativeEngineInlineKind.EndFree, 0x7f));
            f.Send(f.Event(s, NativeEngineInlineKind.StartExited, 0x80)); f.Return(s, 0x80);
            Check(f.Inline.Unknown && f.Nodes.Unknown && !f.One().FinalCleanupObserved);
        }
        using (var f = new Harness())
        {
            var s = f.Begin(); f.Send(f.Event(s, NativeEngineInlineKind.EndFree, 0x80));
            f.Send(f.Event(s, NativeEngineInlineKind.StartExited, 0x80)); f.Return(s, 0x81);
            Check(f.Nodes.Unknown && !f.One().FinalCleanupObserved && f.One().InlineCleanupUnproven);
        }
    }
    private static void TupleMismatch()
    {
        for (var index = 0; index < 6; ++index)
        {
            using var f = new Harness(); var s = f.Begin(); var end = f.Event(s, NativeEngineInlineKind.EndFree);
            switch (index) { case 0: end.Node += 1; break; case 1: end.Owner += 1; break; case 2: end.EnumeratorHandle++; break;
                case 3: end.EnumeratorCache += 1; break; case 4: end.OutputSlot += 1; break; default: end.ReferenceCount++; break; }
            f.Send(end); f.Send(f.Event(s, NativeEngineInlineKind.StartExited)); f.Return(s);
            Check(f.Nodes.Unknown && !f.One().FinalCleanupObserved);
        }
    }
    private static void MissingAndAbnormal()
    {
        for (var index = 0; index < 3; ++index)
        {
            using var f = new Harness(); var s = f.Begin();
            if (index != 0) f.Send(f.Event(s, NativeEngineInlineKind.EndFree));
            var exit = f.Event(s, NativeEngineInlineKind.StartExited);
            if (index == 0) exit.Flags |= NativeEngineInlineFlags.MissingEnd;
            else if (index == 1) { exit.Flags |= NativeEngineInlineFlags.Abnormal; exit.StartExitFlags = NativeEngineNodeExitFlags.Abnormal; }
            else { exit.Flags |= NativeEngineInlineFlags.SnapshotFaulted; exit.Flags &= ~NativeEngineInlineFlags.SnapshotValid; }
            f.Send(exit); f.Return(s, abnormal: index == 1);
            Check(f.Runtime.Unknown && f.Inline.Unknown && !f.One().FinalCleanupObserved);
        }
    }
    private static void ExecuteScope()
    {
        using (var f = new Harness())
        {
            var s = f.Start(); f.Send(f.Event(s, NativeEngineInlineKind.StartEntered)); var e = f.Execute();
            f.Send(f.Event(s, NativeEngineInlineKind.Begin)); Check(f.Inline.Unknown && !f.One().FinalCleanupObserved); f.Return(e);
        }
        using (var f = new Harness())
        {
            var s = f.Start(); f.Send(f.Event(s, NativeEngineInlineKind.StartEntered)); f.Send(f.Event(s, NativeEngineInlineKind.Begin));
            Check(f.Inline.Unknown && f.Nodes.Read().Nodes.Count == 0);
        }
    }
    private static void ActualProof()
    {
        using var f = new Harness(); var s = f.Start(); f.Send(f.Event(s, NativeEngineInlineKind.StartEntered));
        var old = f.LastReceipt!; Check(!old.IsCurrent);
        var e = f.Execute(); f.Return(e);
        Reject(() => f.Inline.Capture(old)); Check(f.Inline.Unknown);
        f.Send(f.Event(s, NativeEngineInlineKind.Begin));
        Check(!f.One().FinalCleanupObserved);
    }
    private static void PhysicalParents()
    {
        using (var f = new Harness())
        {
            var outer = f.Start(); f.Send(f.Event(outer, NativeEngineInlineKind.StartEntered));
            var e = f.Execute(); var foreign = f.Start(owner: 999, enumerator: 888);
            f.Send(f.Event(foreign, NativeEngineInlineKind.StartEntered));
            f.Send(f.Event(foreign, NativeEngineInlineKind.StartExited)); f.Return(foreign); f.Return(e);
            f.Send(f.Event(outer, NativeEngineInlineKind.Begin)); f.Finish(outer);
            Check(!f.Nodes.Unknown && f.One().FinalCleanupObserved && f.Inline.Read().Starts == 1);
        }
        using (var f = new Harness())
        {
            var outer = f.Start(); f.Send(f.Event(outer, NativeEngineInlineKind.StartEntered));
            var foreign = f.Start(owner: 999, enumerator: 888); f.Send(f.Event(foreign, NativeEngineInlineKind.StartEntered));
            f.Send(f.Event(outer, NativeEngineInlineKind.Begin, depth: 1, parent: 0));
            Check(f.Inline.Unknown && f.Nodes.Read().Nodes.Count == 0);
        }
    }
    private static void Generations()
    {
        using var f = new Harness(); var first = f.Begin(); f.Finish(first); var old = f.One();
        f.Provider.Reset(2, 1002); var second = f.Start(enumerator: 1002); f.Send(f.Event(second, NativeEngineInlineKind.StartEntered));
        var e = f.Execute(enumerator: 1002); f.Return(e); f.Send(f.Event(second, NativeEngineInlineKind.Begin)); f.Finish(second);
        var rows = f.Nodes.Read().Nodes;
        Check(!f.Nodes.Unknown && rows.Count == 2 && rows[0].Generation < rows[1].Generation
            && rows.All(row => row.FinalCleanupObserved && !row.InlineCleanupUnproven) && old.Enumerator == 1001 && rows[1].Enumerator == 1002);
    }
    private static void Duplicates()
    {
        for (var index = 0; index < 2; ++index)
        {
            using var f = new Harness(); var s = f.Begin();
            if (index == 0) f.Send(f.Event(s, NativeEngineInlineKind.Begin));
            else { f.Send(f.Event(s, NativeEngineInlineKind.EndFree)); f.Send(f.Event(s, NativeEngineInlineKind.EndRetained)); }
            f.Send(f.Event(s, NativeEngineInlineKind.StartExited)); f.Return(s);
            Check(f.Inline.Unknown && !f.One().FinalCleanupObserved && f.Nodes.Read().Pending);
        }
    }
    private static void Worker()
    {
        using var f = new Harness(); var s = f.Begin(); var reads = f.Provider.Reads; var frame = f.Event(s, NativeEngineInlineKind.EndFree);
        Task.Run(() => f.Side.Send(frame)).GetAwaiter().GetResult();
        Check(f.Runtime.Unknown && f.Provider.Reads == reads && !f.One().FinalCleanupObserved);
    }
    private sealed class Scope : IDisposable
    { internal int Observed, Disposed; internal Action? OnDispose; public void Dispose() { ++Disposed; OnDispose?.Invoke(); } }
    private static void SharingAndDetach()
    {
        using var f = new Harness(); Check(ReferenceEquals(f.Runtime,
            NativeEngineRuntimeCoordinator.ShareControlled(f.Key, f.Bridges, new Logger(), f.Side)) && f.Side.Attaches == 1 && f.Bridges.All(value => value.Attaches == 1));
        var core = new Scope(); var side = new Scope(); var faults = 0;
        using var peer = f.Runtime.BindWithInline(_ => core, (_, _, _) => ++core.Observed, (_, _) => ++faults,
            _ => side, (scope, _) => ++scope.Observed, (_, _) => ++faults);
        var start = f.Begin(); f.Subscription.Dispose(); peer.Dispose(); f.Finish(start);
        Check(!f.Runtime.Unknown && f.One().FinalCleanupObserved && core.Observed == 2 && core.Disposed == 2
            && side.Observed == 3 && side.Disposed == 1 && faults == 0);
    }
    private static void CapabilityAndFamily()
    {
        var key = new object(); var bridges = Enum.GetValues<NativeEngineNodeKind>().Select(value => new NodeBridge(value)).ToArray();
        var runtime = NativeEngineRuntimeCoordinator.ShareControlled(key, bridges, new Logger());
        Reject(runtime.RequireInlineObservation); Check(!runtime.Unknown && !runtime.InlineAvailable && runtime.ReadActivity().Known);
        var late = new SideBridge(bridges[0].Identity); Reject(() => NativeEngineRuntimeCoordinator.ShareControlled(key, bridges, new Logger(), late));
        Check(late.Attaches == 0 && !runtime.Unknown);
        using var f = new Harness(); var different = new SideBridge(f.Bridges[0].Identity, 12);
        Reject(() => NativeEngineRuntimeCoordinator.ShareControlled(f.Key, f.Bridges, new Logger(), different));
        Check(different.Attaches == 0 && !f.Runtime.Unknown);
    }
    private static void TypedCaptureDetach()
    {
        using var f = new Harness(bind: false); var core = new Scope(); var side = new Scope();
        var sideCaptured = 0; NativeEngineRuntimeCoordinator.Subscription? binding = null;
        binding = f.Runtime.BindWithInline(_ => { binding!.Dispose(); return core; },
            (_, _, _) => ++core.Observed, (_, _) => { },
            _ => { ++sideCaptured; return side; }, (scope, _) => ++scope.Observed, (_, _) => { });
        var start = f.Start(); f.Send(f.Event(start, NativeEngineInlineKind.StartEntered));
        f.Send(f.Event(start, NativeEngineInlineKind.StartExited)); f.Return(start);
        Check(!f.Runtime.Unknown && sideCaptured == 1 && core.Observed == 1 && core.Disposed == 1
            && side.Observed == 1 && side.Disposed == 1);
        start = f.Start(); f.Send(f.Event(start, NativeEngineInlineKind.StartEntered));
        f.Send(f.Event(start, NativeEngineInlineKind.StartExited)); f.Return(start);
        Check(!f.Runtime.Unknown && sideCaptured == 1 && core.Disposed == 1);
        binding.Dispose();
    }
    private static void TypedCaptureMembership()
    {
        using var f = new Harness(bind: false); var first = new Scope(); var second = new Scope(); var late = new Scope();
        var firstSide = 0; var secondSide = 0; var lateSide = 0; var changed = false;
        NativeEngineRuntimeCoordinator.Subscription? secondBinding = null, lateBinding = null;
        using var firstBinding = f.Runtime.BindWithInline(_ => {
            if (!changed)
            {
                changed = true; secondBinding!.Dispose();
                lateBinding = f.Runtime.BindWithInline(_ => late, (_, _, _) => ++late.Observed, (_, _) => { },
                    _ => { ++lateSide; return new Scope(); }, (_, _) => { }, (_, _) => { });
            }
            return first;
        }, (_, _, _) => ++first.Observed, (_, _) => { }, _ => { ++firstSide; return new Scope(); }, (_, _) => { }, (_, _) => { });
        secondBinding = f.Runtime.BindWithInline(_ => second, (_, _, _) => ++second.Observed, (_, _) => { },
            _ => { ++secondSide; return new Scope(); }, (_, _) => { }, (_, _) => { });
        var start = f.Start(); f.Send(f.Event(start, NativeEngineInlineKind.StartEntered));
        f.Send(f.Event(start, NativeEngineInlineKind.StartExited)); f.Return(start);
        Check(!f.Runtime.Unknown && first.Observed == 1 && second.Observed == 1 && late.Observed == 0
            && firstSide == 1 && secondSide == 1 && lateSide == 0);
        start = f.Start(); f.Send(f.Event(start, NativeEngineInlineKind.StartEntered));
        f.Send(f.Event(start, NativeEngineInlineKind.StartExited)); f.Return(start);
        Check(!f.Runtime.Unknown && first.Observed == 2 && second.Observed == 1 && late.Observed == 1
            && firstSide == 2 && secondSide == 1 && lateSide == 1);
        secondBinding.Dispose(); lateBinding!.Dispose();
    }
    private static void StickyFaults()
    {
        using (var f = new Harness())
        {
            f.Side.Known = false; Check(!f.Runtime.ReadActivity().Known && f.Runtime.Unknown);
            f.Side.Known = true; Check(!f.Runtime.ReadActivity().Known); Reject(f.Runtime.RequireInlineObservation);
        }
        using (var f = new Harness())
        {
            var start = f.Begin(); var wrong = f.Event(start, NativeEngineInlineKind.EndFree); wrong.Epoch++;
            f.Send(wrong); Check(f.Runtime.Unknown && !f.One().FinalCleanupObserved);
        }
    }
    private static void Information()
    {
        var start = new NodeBridge(NativeEngineNodeKind.Start); var identity = new SideBridge(start.Identity).Identity;
        var information = new NativeEngineInlineInformation { Version = 1, Size = 160, Slot = identity.Slot,
            Configured = 1, MainThreadId = identity.MainThreadId, Epoch = identity.Epoch, StartEpoch = identity.Start.Epoch,
            StartTarget = identity.Start.Target, Entry = identity.Entry };
        Check(NativeEngineInlineContextBridge.MatchesInformation(information, identity, false));
        information.Attached = 1; Check(NativeEngineInlineContextBridge.MatchesInformation(information, identity, true));
        var copy = information; copy.StartEpoch++; Check(!NativeEngineInlineContextBridge.MatchesInformation(copy, identity, true));
        copy = information; copy.StartTarget += 1; Check(!NativeEngineInlineContextBridge.MatchesInformation(copy, identity, true));
        copy = information; copy.Faults++; Check(!NativeEngineInlineContextBridge.MatchesInformation(copy, identity, true));
        copy = information; copy.UnmatchedStarts++; Check(!NativeEngineInlineContextBridge.MatchesInformation(copy, identity, true));
        copy = information; copy.Reserved = 1; Check(!NativeEngineInlineContextBridge.MatchesInformation(copy, identity, true));
    }
}
