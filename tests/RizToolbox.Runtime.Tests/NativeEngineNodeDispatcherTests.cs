using System.Runtime.InteropServices;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

// These are controlled scalar/bridge tests. They execute the actual dispatcher
// but do not publish a detour, read a node, execute UnityPlayer or prove engine
// cancellation. Actual native ABI/Dobby evidence belongs to separate fixtures.
internal static class NativeEngineNodeDispatcherTests
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Engine node transport declares four exact native ABIs and copied scalar frame/result layouts", Layout),
        ("Engine node transport shape preserves null inputs unreadable nodes and raw result bytes", Shape),
        ("Engine node dispatcher freezes multi-sink membership and finishes captured duties after detach", SnapshotAndDetach),
        ("Engine node dispatcher pairs one shared nested physical stack across all four typed channels", NestedKinds),
        ("Engine node dispatcher rejects wrong frame parent token and worker completion without losing contexts", ExactCompletion),
        ("Engine node dispatcher isolates sink faults preserves peer cleanup and retains sticky Unknown", ObserverFaults),
        ("Engine node dispatcher records faulted snapshots abnormal originals and malformed completion responsibility", NativeFaults),
        ("Engine node dispatcher supports scalar-only worker observation and does not lend early Attach calls to later sinks", WorkerAndAttach),
        ("Engine node dispatcher keeps native activity Unknown and failed Attach ownership permanent", ActivityAndAttachFailure),
        ("Engine node adoption identity requires exact configured target original entry epoch and clean native information", Identity)
    };
    private static void Check(bool condition, string message = "Controlled engine dispatcher check failed.")
    { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Logger : ILogger { public void Log(LogLevel level, string message, Exception? error = null) { } }
    private sealed class Scope : IDisposable
    {
        internal int Completed, Disposed;
        internal Action? OnDispose;
        public void Dispose() { ++Disposed; OnDispose?.Invoke(); }
    }
    private sealed class Bridge : INativeEngineNodeContextBridge
    {
        private Func<NativeEngineNodeFrame, nuint>? _enter;
        private Action<NativeEngineNodeFrame, nuint, NativeEngineNodeCompletion>? _exit;
        private long _inFlight;
        private ulong _sequence;
        private bool _attached;
        internal bool Known = true, CallDuringAttach, FailAttach;
        internal int Attaches, EarlyCalls;
        public NativeEngineNodeChannelIdentity Identity { get; }
        internal Bridge(NativeEngineNodeKind kind) => Identity = new(kind, 1, new(1001), new(1002), new(1003), 7);
        public void Attach(Func<NativeEngineNodeFrame, nuint> enter, Action<NativeEngineNodeFrame, nuint, NativeEngineNodeCompletion> exit)
        {
            ++Attaches; Check(!_attached); _attached = true; _enter = enter; _exit = exit;
            if (CallDuringAttach) { var call = Begin(); End(call); ++EarlyCalls; }
            if (FailAttach) throw new InvalidOperationException("Controlled Attach uncertainty.");
        }
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity() => new(Volatile.Read(ref _inFlight), Known);
        internal (NativeEngineNodeFrame Frame, nuint Token) Begin(NativeEngineNodeFrame? provided = null)
        {
            var frame = provided ?? Frame(Identity.Kind, ++_sequence);
            Interlocked.Increment(ref _inFlight); return (frame, _enter!(frame));
        }
        internal void TryEnd((NativeEngineNodeFrame Frame, nuint Token) call, NativeEngineNodeFrame? frame = null,
            NativeEngineNodeCompletion? completion = null, nuint? token = null)
            => _exit!(frame ?? call.Frame, token ?? call.Token, completion ?? Done(Identity.Kind));
        internal void End((NativeEngineNodeFrame Frame, nuint Token) call, NativeEngineNodeCompletion? completion = null)
        { TryEnd(call, completion: completion); Interlocked.Decrement(ref _inFlight); }
    }
    private static NativeEngineNodeObserverDispatcher Dispatcher(Bridge bridge) => new(bridge, new Logger());
    private static NativeEngineNodeFrame Frame(NativeEngineNodeKind kind, ulong sequence = 1)
    {
        var frame = new NativeEngineNodeFrame { Version = 1, Size = 112, Kind = kind,
            Arg0 = new(101), Caller = new(301), Sequence = sequence };
        if (kind == NativeEngineNodeKind.Start) { frame.Arg1 = new(102); frame.Arg2 = new(103); frame.Arg3 = new(104); }
        else
        {
            frame.Node = frame.Arg0; frame.Owner = new(105); frame.EnumeratorHandle = 106; frame.EnumeratorCache = new(107);
            frame.EnumeratorType = 2; frame.ReferenceCount = 17;
            frame.SnapshotFlags = NativeEngineNodeSnapshotFlags.Requested | NativeEngineNodeSnapshotFlags.Valid;
            if (kind == NativeEngineNodeKind.Execute) frame.Arg1 = new(108);
        }
        return frame;
    }
    private static NativeEngineNodeCompletion Done(NativeEngineNodeKind kind) => new() {
        Version = 1, Size = 32, Flags = NativeEngineNodeExitFlags.Returned
            | (kind == NativeEngineNodeKind.Detach ? 0 : NativeEngineNodeExitFlags.ReturnByteValid),
        RawReturn = kind == NativeEngineNodeKind.Detach ? 0u : 0xfeu,
        StartOutputNode = kind == NativeEngineNodeKind.Start ? new(401) : IntPtr.Zero,
        OutputFlags = kind == NativeEngineNodeKind.Start ? NativeEngineNodeOutputFlags.Requested | NativeEngineNodeOutputFlags.Read : 0 };
    private static NativeEngineNodeObserverDispatcher.Binding Bind(NativeEngineNodeObserverDispatcher dispatcher, Scope scope,
        Action<NativeEngineNodeFrame>? capture = null, Action<NativeEngineNodeCompletion>? complete = null, Action? fault = null)
        => dispatcher.Bind(frame => { capture?.Invoke(frame); return scope; },
            (context, _, result) => { Check(ReferenceEquals(context, scope)); ++context.Completed; complete?.Invoke(result); }, (_, _) => fault?.Invoke());

    private static void Layout()
    {
        Check(Marshal.SizeOf<NativeEngineNodeFrame>() == 112 && Marshal.SizeOf<NativeEngineNodeCompletion>() == 32
            && Marshal.SizeOf<NativeEngineNodeInformation>() == 112);
        Check(Marshal.OffsetOf<NativeEngineNodeFrame>(nameof(NativeEngineNodeFrame.Arg0)).ToInt32() == 16
            && Marshal.OffsetOf<NativeEngineNodeFrame>(nameof(NativeEngineNodeFrame.Caller)).ToInt32() == 48
            && Marshal.OffsetOf<NativeEngineNodeFrame>(nameof(NativeEngineNodeFrame.Sequence)).ToInt32() == 56
            && Marshal.OffsetOf<NativeEngineNodeFrame>(nameof(NativeEngineNodeFrame.Node)).ToInt32() == 64
            && Marshal.OffsetOf<NativeEngineNodeFrame>(nameof(NativeEngineNodeFrame.EnumeratorHandle)).ToInt32() == 80
            && Marshal.OffsetOf<NativeEngineNodeFrame>(nameof(NativeEngineNodeFrame.ReferenceCount)).ToInt32() == 100
            && Marshal.OffsetOf<NativeEngineNodeCompletion>(nameof(NativeEngineNodeCompletion.StartOutputNode)).ToInt32() == 16
            && Marshal.OffsetOf<NativeEngineNodeCompletion>(nameof(NativeEngineNodeCompletion.OutputFlags)).ToInt32() == 24
            && Marshal.OffsetOf<NativeEngineNodeInformation>(nameof(NativeEngineNodeInformation.SnapshotFaults)).ToInt32() == 96);
        var declarations = new[] { (typeof(NativeEngineNodeProfile.Start), 4, typeof(byte), 0x792240u),
            (typeof(NativeEngineNodeProfile.Execute), 2, typeof(uint), 0x78f840u),
            (typeof(NativeEngineNodeProfile.Release), 1, typeof(byte), 0x78f680u),
            (typeof(NativeEngineNodeProfile.Detach), 1, typeof(void), 0x793840u) };
        for (var index = 0; index != declarations.Length; ++index)
        {
            var row = declarations[index]; var invoke = row.Item1.GetMethod("Invoke")!;
            Check(invoke.ReturnType == row.Item3 && invoke.GetParameters().Length == row.Item2
                && invoke.GetParameters().All(parameter => parameter.ParameterType == typeof(IntPtr))
                && NativeEngineNodeProfile.Rva((NativeEngineNodeKind)index) == row.Item4);
        }
    }
    private static void Shape()
    {
        foreach (var kind in Enum.GetValues<NativeEngineNodeKind>())
        {
            var frame = Frame(kind); Check(frame.IsValid); var done = Done(kind); Check(done.IsValidFor(kind));
            Check(NativeEngineNodeObserverDispatcher.SameFrame(frame, frame));
            var changed = frame; changed.Sequence++; Check(!NativeEngineNodeObserverDispatcher.SameFrame(frame, changed));
            if (kind == NativeEngineNodeKind.Start)
            { frame.Arg0 = frame.Arg1 = frame.Arg2 = frame.Arg3 = IntPtr.Zero; done.StartOutputNode = IntPtr.Zero; }
            else
            {
                frame.Arg0 = frame.Node = frame.Owner = frame.EnumeratorCache = IntPtr.Zero;
                frame.EnumeratorHandle = frame.EnumeratorType = 0; frame.ReferenceCount = 0;
                frame.SnapshotFlags = NativeEngineNodeSnapshotFlags.Requested | NativeEngineNodeSnapshotFlags.Faulted;
            }
            Check(frame.IsValid && done.IsValidFor(kind));
            if (kind != NativeEngineNodeKind.Detach) {
                done.RawReturn = 0xabcdef01;
                Check(done.IsValidFor(kind) == (kind == NativeEngineNodeKind.Execute));
                done.RawReturn = uint.MaxValue;
                Check(done.IsValidFor(kind) == (kind == NativeEngineNodeKind.Execute));
            }
            done = Done(kind); done.Flags |= NativeEngineNodeExitFlags.Abnormal; Check(!done.IsValidFor(kind));
            done = new() { Version = 1, Size = 32, Flags = NativeEngineNodeExitFlags.Abnormal };
            Check(done.IsValidFor(kind)); done.RawReturn = 1; Check(!done.IsValidFor(kind));
        }
        var bad = Frame(NativeEngineNodeKind.Release); bad.Arg1 = new(1); Check(!bad.IsValid);
        bad = Frame(NativeEngineNodeKind.Start); bad.Node = new(1); Check(!bad.IsValid);
        bad = Frame(NativeEngineNodeKind.Execute); bad.EnumeratorType = 0xdead; Check(bad.IsValid); // Value validity belongs to the ledger.
    }
    private static void SnapshotAndDetach()
    {
        var bridge = new Bridge(NativeEngineNodeKind.Execute); var dispatcher = Dispatcher(bridge);
        var first = new Scope(); var second = new Scope(); var late = new Scope(); var changed = false;
        NativeEngineNodeObserverDispatcher.Binding? secondBinding = null, lateBinding = null;
        using var firstBinding = Bind(dispatcher, first, _ => {
            if (changed) return; changed = true; secondBinding!.Dispose(); lateBinding = Bind(dispatcher, late);
        });
        secondBinding = Bind(dispatcher, second);
        var call = bridge.Begin(); Check(dispatcher.ReadActivity() is { Known: true, InFlight: 1 });
        bridge.End(call); Check(first.Completed == 1 && first.Disposed == 1 && second.Completed == 1 && second.Disposed == 1
            && late.Completed == 0 && late.Disposed == 0);
        call = bridge.Begin(); bridge.End(call);
        Check(first.Completed == 2 && second.Completed == 1 && late.Completed == 1 && late.Disposed == 1
            && dispatcher.ReadActivity() is { Known: true, InFlight: 0 });
        lateBinding!.Dispose(); secondBinding.Dispose();
    }
    private static void NestedKinds()
    {
        var bridges = Enum.GetValues<NativeEngineNodeKind>().Select(kind => new Bridge(kind)).ToArray();
        var dispatchers = bridges.Select(Dispatcher).ToArray(); var scopes = bridges.Select(_ => new Scope()).ToArray();
        var order = new List<string>(); var bindings = dispatchers.Select((dispatcher, index) => Bind(dispatcher, scopes[index],
            _ => order.Add("enter" + index), _ => order.Add("exit" + index))).ToArray();
        var calls = bridges.Select(bridge => bridge.Begin()).ToArray();
        for (var index = bridges.Length - 1; index >= 0; --index) bridges[index].End(calls[index]);
        Check(order.SequenceEqual(new[] { "enter0", "enter1", "enter2", "enter3", "exit3", "exit2", "exit1", "exit0" })
            && scopes.All(scope => scope.Completed == 1 && scope.Disposed == 1)
            && dispatchers.All(dispatcher => dispatcher.ReadActivity() is { Known: true, InFlight: 0 }));
        foreach (var binding in bindings) binding.Dispose();
    }
    private static void ExactCompletion()
    {
        var outerBridge = new Bridge(NativeEngineNodeKind.Start); var innerBridge = new Bridge(NativeEngineNodeKind.Execute);
        var outer = Dispatcher(outerBridge); var inner = Dispatcher(innerBridge); var first = new Scope(); var second = new Scope();
        using var a = Bind(outer, first); using var b = Bind(inner, second);
        var outerCall = outerBridge.Begin(); var innerCall = innerBridge.Begin();
        outerBridge.TryEnd(outerCall); Check(first.Completed == 0 && first.Disposed == 0 && outer.Unknown);
        var changed = innerCall.Frame; changed.EnumeratorHandle++;
        innerBridge.TryEnd(innerCall, frame: changed); Check(second.Completed == 0 && second.Disposed == 0 && inner.Unknown);
        var worker = new Thread(() => innerBridge.TryEnd(innerCall)); worker.Start(); worker.Join();
        Check(second.Completed == 0 && second.Disposed == 0);
        innerBridge.TryEnd(innerCall, token: innerCall.Token + 100); Check(second.Disposed == 0);
        innerBridge.End(innerCall); outerBridge.End(outerCall);
        Check(first.Completed == 1 && first.Disposed == 1 && second.Completed == 1 && second.Disposed == 1);
        innerBridge.TryEnd(innerCall); Check(second.Completed == 1 && second.Disposed == 1
            && outer.ReadActivity() is { Known: false, InFlight: 0 } && inner.ReadActivity() is { Known: false, InFlight: 0 });
    }
    private static void ObserverFaults()
    {
        var bridge = new Bridge(NativeEngineNodeKind.Release); var dispatcher = Dispatcher(bridge);
        var first = new Scope { OnDispose = () => throw new InvalidOperationException("Controlled cleanup fault.") };
        var peer = new Scope(); var peerFaults = 0;
        using var a = Bind(dispatcher, first, complete: _ => throw new InvalidOperationException("Controlled completion fault."));
        using var b = dispatcher.Bind<Scope>(_ => throw new InvalidOperationException("Controlled capture fault."), (_, _, _) => { },
            (_, _) => throw new InvalidOperationException("Controlled fault callback failure."));
        using var c = Bind(dispatcher, peer, fault: () => ++peerFaults);
        var call = bridge.Begin(); bridge.End(call);
        Check(first.Completed == 1 && first.Disposed == 1 && peer.Completed == 1 && peer.Disposed == 1
            && dispatcher.ReadActivity() is { Known: false, InFlight: 0 });
        call = bridge.Begin(); bridge.End(call);
        Check(peerFaults > 0 && first.Completed == 1 && first.Disposed == 1 && peer.Completed == 1 && peer.Disposed == 1);
    }
    private static void NativeFaults()
    {
        foreach (var mode in new[] { "snapshot", "abnormal", "output", "malformed" })
        {
            var kind = mode == "output" ? NativeEngineNodeKind.Start : NativeEngineNodeKind.Execute;
            var bridge = new Bridge(kind); var dispatcher = Dispatcher(bridge); var scope = new Scope(); var faults = 0;
            using var binding = Bind(dispatcher, scope, fault: () => ++faults);
            var frame = Frame(kind); var completion = Done(kind);
            if (mode == "snapshot")
            {
                frame.SnapshotFlags = NativeEngineNodeSnapshotFlags.Requested | NativeEngineNodeSnapshotFlags.Faulted;
                frame.Owner = frame.EnumeratorCache = IntPtr.Zero; frame.EnumeratorHandle = frame.EnumeratorType = 0; frame.ReferenceCount = 0;
            }
            else if (mode == "abnormal") completion = new() { Version = 1, Size = 32, Flags = NativeEngineNodeExitFlags.Abnormal };
            else if (mode == "output")
            { completion.OutputFlags = NativeEngineNodeOutputFlags.Requested | NativeEngineNodeOutputFlags.Faulted; completion.StartOutputNode = IntPtr.Zero; }
            else completion.Size = 31;
            var call = bridge.Begin(frame); bridge.End(call, completion);
            Check(scope.Completed == 1 && scope.Disposed == 1 && faults > 0 && dispatcher.ReadActivity() is { Known: false, InFlight: 0 });
        }
    }
    private static void WorkerAndAttach()
    {
        var bridge = new Bridge(NativeEngineNodeKind.Execute) { CallDuringAttach = true };
        var dispatcher = Dispatcher(bridge); Check(bridge.EarlyCalls == 1 && bridge.Attaches == 1);
        var scope = new Scope(); var captures = 0; var captureThread = 0; var completeThread = 0;
        using var binding = Bind(dispatcher, scope, _ => { ++captures; captureThread = Environment.CurrentManagedThreadId; },
            _ => completeThread = Environment.CurrentManagedThreadId);
        Check(captures == 0 && scope.Completed == 0 && scope.Disposed == 0);
        Exception? failure = null;
        var worker = new Thread(() => { try { var call = bridge.Begin(); bridge.End(call); } catch (Exception error) { failure = error; } });
        worker.Start(); worker.Join();
        Check(failure is null && captures == 1 && scope.Completed == 1 && scope.Disposed == 1
            && captureThread == completeThread && captureThread != Environment.CurrentManagedThreadId
            && dispatcher.ReadActivity() is { Known: true, InFlight: 0 });
    }
    private static void ActivityAndAttachFailure()
    {
        var bridge = new Bridge(NativeEngineNodeKind.Detach); var dispatcher = Dispatcher(bridge);
        Check(dispatcher.ReadActivity() is { Known: true, InFlight: 0 }); bridge.Known = false;
        Check(dispatcher.ReadActivity() is { Known: false, InFlight: 0 }); bridge.Known = true;
        Check(dispatcher.ReadActivity() is { Known: false, InFlight: 0 });
        var failed = new Bridge(NativeEngineNodeKind.Release) { FailAttach = true }; var rejected = false;
        try { _ = Dispatcher(failed); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && failed.Attaches == 1);
        // A controlled callback already published before a failed Attach still
        // reaches its retained dispatcher and cannot resurrect Known authority.
        var call = failed.Begin(); failed.End(call); Check(failed.Attaches == 1);
    }
    private static void Identity()
    {
        var identity = new NativeEngineNodeChannelIdentity(NativeEngineNodeKind.Start, 31, new(101), new(102), new(103), 104);
        var info = new NativeEngineNodeInformation { Version = 1, Size = 112, Kind = (uint)identity.Kind, Slot = identity.Slot,
            Target = identity.Target, Original = identity.Original, Entry = identity.Entry, Epoch = identity.Epoch };
        Check(identity.IsComplete && NativeEngineNodeContextBridge.MatchesInformation(info, identity, false)
            && !NativeEngineNodeContextBridge.MatchesInformation(info, identity, true));
        info.Attached = 1; Check(NativeEngineNodeContextBridge.MatchesInformation(info, identity, true));
        foreach (var changed in new[] { identity with { Epoch = 105 }, identity with { Original = new(999) },
            identity with { Target = new(999) }, identity with { Entry = new(999) }, identity with { Slot = 30 },
            identity with { Kind = NativeEngineNodeKind.Execute } })
            Check(!NativeEngineNodeContextBridge.MatchesInformation(info, changed, true));
        info.SnapshotFaults = 1; Check(!NativeEngineNodeContextBridge.MatchesInformation(info, identity, true));
        info.SnapshotFaults = 0; info.OutputFaults = 1; Check(!NativeEngineNodeContextBridge.MatchesInformation(info, identity, true));
        Check(!(identity with { Epoch = 0 }).IsComplete && !(identity with { Slot = 32 }).IsComplete
            && !(identity with { Entry = identity.Target }).IsComplete && !(identity with { Original = identity.Target }).IsComplete
            && !(identity with { Original = IntPtr.Zero }).IsComplete);
    }
}
