using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

internal static class CoroutineObservationTests
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Coroutine transport prepares before publication and agrees on exact Start aliases", Publication),
        ("Coroutine invalid ABI aliases and managed-first patches are rejected before a second bridge", Rejections),
        ("Coroutine original failure releases contexts without successful after or exception replacement", OriginalFault),
        ("Coroutine observer faults retain unknown activity and preserve healthy peers", ObserverFaults),
        ("Coroutine physical reentry captures each receiver before shared ordinary Boolean callbacks", Reentry),
        ("Coroutine worker and foreign metadata notify scalar faults without capturing native receivers", Isolation),
        ("Coroutine logical detach preserves native in-flight activity and paired context cleanup", Detach),
        ("Coroutine uncertain publication and invalid activity remain process-owned without retry", Unknown),
        ("Coroutine Factory uses only its sealed ref0 ABI and forwards full references without transforms", Factory)
    };
    private static void Check(bool value, string message = "Coroutine observation check failed")
    { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action, string expected)
    {
        try { action(); throw new Exception("Expected rejection: " + expected); }
        catch (Exception error) { Check(error.Message.Contains(expected, StringComparison.OrdinalIgnoreCase), error.ToString()); }
    }
    private static VerifiedHookTarget Target(NativeCoroutineKind kind = NativeCoroutineKind.Start)
        => new("coroutine.controlled", new(Interlocked.Add(ref Addresses.Next, 16)), new(0x123), new(0x321),
            kind switch { NativeCoroutineKind.Start => ExactHookAbi.CoroutineStartReference1, NativeCoroutineKind.Factory => ExactHookAbi.CoroutineFactoryReference0, _ => ExactHookAbi.InstanceBooleanReturn0 },
            kind == NativeCoroutineKind.MoveNext ? IntPtr.Zero : new(0x789));
    private static NativeCoroutineFrame Frame(VerifiedHookTarget target, NativeCoroutineKind kind = NativeCoroutineKind.Start)
        => new() { Version = 1, Size = 48, Kind = kind, Self = new(0x777), Enumerator = kind == NativeCoroutineKind.Start ? new(0x888) : IntPtr.Zero,
            Method = target.MethodInfo, Caller = new(0x999) };
    private sealed class Context : IDisposable
    { private readonly Action? _release; internal Context(Action? release = null) => _release = release; public void Dispose() => _release?.Invoke(); }
    private sealed class Bridge : INativeCoroutineContextBridge
    {
        public NativeCoroutineKind Kind { get; }
        public IntPtr EntryAddress => new(0x4433);
        internal bool Configured, Known = true, ThrowActivity;
        internal long InFlight;
        internal Action? ConfigureAction;
        internal Func<NativeCoroutineFrame, nuint> Original = _ => 0;
        private Func<NativeCoroutineFrame, nuint> _enter = null!;
        private Action<NativeCoroutineFrame, nuint, nuint, NativeCoroutineExitFlags> _exit = null!;
        internal Bridge(NativeCoroutineKind kind = NativeCoroutineKind.Start) => Kind = kind;
        public void Configure(IntPtr original, Func<NativeCoroutineFrame, nuint> enter,
            Action<NativeCoroutineFrame, nuint, nuint, NativeCoroutineExitFlags> exit)
        { Check(original != IntPtr.Zero && !Configured); ConfigureAction?.Invoke(); (_enter, _exit, Configured) = (enter, exit, true); }
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity()
        { if (ThrowActivity) throw new IOException("Statistics unavailable"); return new(Interlocked.Read(ref InFlight), Configured && Known); }
        internal nuint Invoke(NativeCoroutineFrame frame)
        {
            Check(Configured); Interlocked.Increment(ref InFlight); nuint token = 0, result = 0; var returned = false;
            try { token = _enter(frame); result = Original(frame); returned = true; return result; }
            finally { try { _exit(frame, token, result, returned ? NativeCoroutineExitFlags.Returned : NativeCoroutineExitFlags.Abnormal); }
                finally { Interlocked.Decrement(ref InFlight); } }
        }
    }
    private static ExactHookBroker.Binding Bind(Fixture f, VerifiedHookTarget target, Bridge bridge,
        Func<NativeCoroutineFrame, Context?>? capture = null, Action<Context, NativeCoroutineFrame, nuint>? after = null,
        Action<NativeCoroutineFrame, string>? fault = null, VerifiedHookTarget? alias = null)
        => f.Broker.BindNativeCoroutineObservation(target, bridge.Kind, () => bridge, capture ?? (_ => null), after ?? ((_, _, _) => { }), fault ?? ((_, _) => { }), alias);
    private static void Publication()
    {
        using var f = new Fixture(); var target = Target(); var alias = target with { MethodInfo = new(0x124) }; var bridge = new Bridge();
        var order = new List<string>(); var captures = 0;
        f.Engine.PrepareAction = () => order.Add("prepare"); bridge.ConfigureAction = () => order.Add("configure");
        f.Engine.CommitAction = () => { order.Add("commit"); Check(bridge.Configured); bridge.Invoke(Frame(target)); };
        using var a = Bind(f, target, bridge, _ => { captures++; return new(); }, alias: alias);
        using var b = f.Broker.BindNativeCoroutineObservation<Context>(target, NativeCoroutineKind.Start,
            () => throw new Exception("Second native patch"), _ => { captures++; return null; }, (_, _, _) => { }, (_, _) => { }, alias);
        foreach (var method in new[] { target.MethodInfo, alias.MethodInfo, IntPtr.Zero })
        { var frame = Frame(target); frame.Method = method; bridge.Invoke(frame); }
        Check(order.SequenceEqual(new[] { "prepare", "configure", "commit" }) && captures == 7 && f.Engine.Prepares == 1 && f.Engine.Commits == 1);
        Reject(() => Bind(f, target, bridge), "agree");
    }
    private static void Rejections()
    {
        using var f = new Fixture(); var target = Target(); var bridge = new Bridge();
        foreach (var invalid in new[] { target with { Address = IntPtr.Zero }, target with { MethodInfo = IntPtr.Zero },
            target with { DeclaringClass = IntPtr.Zero }, target with { ReturnClass = IntPtr.Zero }, target with { Abi = ExactHookAbi.InstanceVoid } })
            Reject(() => Bind(f, invalid, bridge), "ABI");
        foreach (var alias in new[] { target, target with { MethodInfo = new(444), Address = new(555) },
            target with { MethodInfo = new(444), DeclaringClass = new(555) } }) Reject(() => Bind(f, target, bridge, alias: alias), "alias");
        Reject(() => Bind(f, target, new(NativeCoroutineKind.MoveNext)), "ABI");
        Check(f.Engine.Prepares == 0 && !bridge.Configured);
        var move = Target(NativeCoroutineKind.MoveNext);
        f.Engine.Original = new ExactHookBroker.InstanceBooleanReturn0((_, _) => 1);
        using var ordinary = f.Broker.BindRetainedBooleanObservation(move, _ => true, _ => new Context(), _ => { });
        Reject(() => Bind(f, move, new(NativeCoroutineKind.MoveNext)), "first patch");
        Check(f.Engine.Installs == 1 && f.Engine.Prepares == 0);
    }
    private static void OriginalFault()
    {
        using var f = new Fixture(); var target = Target(); var bridge = new Bridge();
        var expected = new IOException("Native original controlled failure"); var afters = 0; var releases = 0; var originals = 0;
        using var binding = Bind(f, target, bridge, _ => new(() => { releases++; Check(bridge.InFlight == 1); }), (_, _, _) => afters++);
        bridge.Original = _ => { originals++; throw expected; };
        try { bridge.Invoke(Frame(target)); throw new Exception("Original failure disappeared"); } catch (IOException actual) { Check(ReferenceEquals(actual, expected)); }
        Check(originals == 1 && afters == 0 && releases == 1 && binding.ReadActivity() is { Known: true, InFlight: 0 });
        bridge.Original = _ => { originals++; return unchecked((nuint)0x76543210abcdef00UL); };
        Check(bridge.Invoke(Frame(target)) == unchecked((nuint)0x76543210abcdef00UL) && originals == 2 && afters == 1 && releases == 2);
    }
    private static void ObserverFaults()
    {
        foreach (var stage in new[] { "capture", "after", "release" })
        {
            using var f = new Fixture(); var target = Target(); var bridge = new Bridge(); var originals = 0; var healthy = 0; var releases = 0;
            bridge.Original = _ => { originals++; return 0; };
            using var broken = Bind(f, target, bridge, _ => stage == "capture" ? throw new IOException("capture")
                : new(() => { if (stage == "release") throw new IOException("release"); }), (_, _, _) => { if (stage == "after") throw new IOException("after"); });
            using var peer = Bind(f, target, bridge, _ => new(() => releases++), (_, _, _) => healthy++);
            bridge.Invoke(Frame(target)); bridge.Invoke(Frame(target));
            Check(originals == 2 && healthy == 2 && releases == 2 && broken.ReadActivity() is { Known: false, InFlight: 0 } && !peer.ReadActivity().Known);
        }
    }
    private static void Reentry()
    {
        using var f = new Fixture(); var target = Target(NativeCoroutineKind.MoveNext); var bridge = new Bridge(NativeCoroutineKind.MoveNext);
        var order = new List<string>(); var originals = 0; var captures = 0; var ordinary = 0;
        using var native = Bind(f, target, bridge, frame => { captures++; order.Add("native" + frame.Self.ToInt64()); return new(); });
        using var shared = f.Broker.BindRetainedBooleanObservation(target, _ => true, _ => { ordinary++; order.Add("ordinary"); return new Context(); }, _ => { });
        bridge.Original = frame => { if (++originals == 1) { frame.Self = new(0x778); Check(bridge.Invoke(frame) == 0xff); } return 0xff; };
        Check(bridge.Invoke(Frame(target, NativeCoroutineKind.MoveNext)) == 0xff && originals == 2 && captures == 2 && ordinary == 1);
        Check(order.SequenceEqual(new[] { "native1911", "ordinary", "native1912" }) && native.ReadActivity() is { Known: true, InFlight: 0 });
    }
    private static void Isolation()
    {
        using var f = new Fixture(); var target = Target(); var bridge = new Bridge(); var captures = 0; var faults = 0; var originals = 0;
        bridge.Original = _ => { Interlocked.Increment(ref originals); return 0x80; };
        using var binding = Bind(f, target, bridge, _ => { captures++; return new(); }, fault: (_, _) => Interlocked.Increment(ref faults));
        Check(Task.Run(() => bridge.Invoke(Frame(target))).GetAwaiter().GetResult() == 0x80);
        var frame = Frame(target); frame.Method = new(0x456); Check(bridge.Invoke(frame) == 0x80);
        Check(captures == 0 && originals == 2 && faults == 2 && binding.ReadActivity().Known);
        frame = Frame(target); frame.Size = 0; bridge.Invoke(frame);
        Check(captures == 0 && originals == 3 && faults == 3 && !binding.ReadActivity().Known);
    }
    private static void Detach()
    {
        using var f = new Fixture(); var target = Target(); var bridge = new Bridge(); var afters = 0; var releases = 0;
        ExactHookBroker.Binding? binding = null;
        binding = Bind(f, target, bridge, _ => new(() => { releases++; Check(binding!.ReadActivity().InFlight == 1); }), (_, _, _) => afters++);
        bridge.Original = _ => { binding.Dispose(); Check(binding.ReadActivity() is { Known: true, InFlight: 1 }); return 1; };
        Check(bridge.Invoke(Frame(target)) == 1 && afters == 0 && releases == 1 && binding.ReadActivity() is { Known: true, InFlight: 0 });
        bridge.Invoke(Frame(target)); Check(releases == 1);
    }
    private static void Unknown()
    {
        foreach (var stage in new[] { "prepare", "configure", "commit" })
        {
            using var f = new Fixture(); var target = Target(); var bridge = new Bridge();
            void Fail() => throw new IOException("Uncertain " + stage);
            if (stage == "prepare") f.Engine.PrepareAction = Fail;
            if (stage == "configure") bridge.ConfigureAction = Fail;
            if (stage == "commit") f.Engine.CommitAction = Fail;
            Reject(() => Bind(f, target, bridge), "Uncertain");
            Reject(() => Bind(f, target, bridge), "without retry"); Check(f.Engine.Prepares == 1);
        }
        foreach (var stage in new[] { "unknown", "negative", "throw" })
        {
            using var f = new Fixture(); var target = Target(); var bridge = new Bridge(); using var binding = Bind(f, target, bridge);
            bridge.Known = stage != "unknown"; bridge.InFlight = stage == "negative" ? -1 : 0; bridge.ThrowActivity = stage == "throw";
            Check(!binding.ReadActivity().Known); bridge.Known = true; bridge.InFlight = 0; bridge.ThrowActivity = false;
            Check(binding.ReadActivity() is { Known: false, InFlight: 0 });
        }
    }
    private static void Factory()
    {
        using var f = new Fixture(); var target = Target(NativeCoroutineKind.Factory); var bridge = new Bridge(NativeCoroutineKind.Factory);
        var originals = 0; var afters = 0; var released = 0; nuint expected = 0;
        bridge.Original = _ => { originals++; return expected; };
        using var binding = Bind(f, target, bridge, frame => { Check(frame.IsValid && frame.Kind == NativeCoroutineKind.Factory && frame.Enumerator == IntPtr.Zero); return new(() => released++); },
            (_, _, result) => { Check(result == expected); afters++; });
        foreach (var value in new[] { (nuint)0, (nuint)1, unchecked((nuint)0x76543210abcdef00UL) })
        { expected = value; Check(bridge.Invoke(Frame(target, NativeCoroutineKind.Factory)) == expected); }
        Check(originals == 3 && afters == 3 && released == 3 && binding.ReadActivity() is { Known: true, InFlight: 0 });
        Reject(() => f.Broker.Bind(target, _ => true, (_, _, _, _) => { }), "sealed");
        Reject(() => Bind(f, target with { ReturnClass = IntPtr.Zero }, bridge), "ABI");
        Reject(() => Bind(f, target with { Abi = ExactHookAbi.InstanceReferenceReturn0 }, bridge), "ABI");
    }
}
