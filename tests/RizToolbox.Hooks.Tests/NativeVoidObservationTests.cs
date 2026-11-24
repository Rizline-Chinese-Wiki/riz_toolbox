using System.Runtime.InteropServices;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

internal static class NativeVoidObservationTests
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Native void0 frame preserves its full caller and appended sealed ABI layout", Layout),
        ("Native void0 publishes Prepare Configure Commit once and shares exact observers", Publication),
        ("Native void0 rejects ordinary lanes foreign identities and ABI conflicts before patching", Rejections),
        ("Native void0 physical reentry captures each exact receiver after probe enter", Reentry),
        ("Native void0 worker callbacks during Commit never wait for the broker publication gate", WorkerDuringCommit),
        ("Native void0 workers and foreign metadata pair probes without native capture", Isolation),
        ("Native void0 detach preserves retained cleanup and the entered physical probe snapshot", Detach),
        ("Native void0 capture after and Dispose failures preserve original and healthy peers", ObserverFaults),
        ("Native void0 probe faults preserve exact forwarding and mark observation unknown", ProbeFaults),
        ("Native void0 abnormal original releases scopes and reports physical failure without replacement", OriginalFault),
        ("Native void0 activity includes native Exit after all managed callbacks have completed", NativeExitActivity),
        ("Native void0 uncertain Prepare Configure Commit and statistics stay process-owned", Unknown),
        ("Native void0 malformed and changed physical frames never authorize a successful after", InvalidFrames),
        ("Native void0 capacity refuses untracked frames before probe enter while preserving original once", Capacity),
        ("Native void0 concurrent workers retain distinct tokens and paired immutable probes", ConcurrentWorkers),
        ("Native void0 scalar fault reporter failures mark unknown without blocking peers or probes", FaultReporter),
        ("Native void0 native callbacks remain rooted after logical detachment and GC", ProcessRoots),
        ("Native void0 current context is available only inside its physical original window", CurrentStages),
        ("Native void0 physical top grants each same or foreign receiver child only its own token", CurrentReentry),
        ("Native void0 foreign metadata and uncaptured children cannot borrow a parent context", CurrentForeignMasks),
        ("Native void0 current identity belongs only to the matching sink and active binding", CurrentOwnership),
        ("Native void0 another native entry and workers hide unrelated physical contexts", CurrentCrossEntry),
        ("Native void0 untracked capacity overflow masks all parent contexts until paired exit", CurrentOverflowMask),
        ("Native void0 an out-of-order physical exit retains unknown responsibility and cannot expose its parent", CurrentBrokenOrder),
        ("Native void0 private identities expire even when a later call reuses the same retained context and full frame", CurrentIdentityReplay),
        ("Native event mid frame matches the complete fixed native register layout", MidFrameLayout)
    };
    private static void Check(bool value, string message = "Native void0 observation check failed")
    { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action, string expected)
    {
        try { action(); throw new Exception("Expected rejection: " + expected); }
        catch (Exception error) { Check(error.Message.Contains(expected, StringComparison.OrdinalIgnoreCase), error.ToString()); }
    }
    private static VerifiedHookTarget Target() => new("void.controlled", new(Interlocked.Add(ref Addresses.Next, 16)),
        new(0x123), new(0x321), ExactHookAbi.NativeVoid0);
    private static NativeVoidFrame Frame(VerifiedHookTarget target) => new() { Version = 1, Size = 40,
        Self = new(unchecked((long)0x72345678ABCDEF01UL)), Method = target.MethodInfo, Caller = new(unchecked((long)0x7123456789ABCDEFUL)) };
    private sealed class Scope : IDisposable
    {
        private readonly Action? _release; internal int Disposals;
        internal Scope(Action? release = null) => _release = release;
        public void Dispose() { Disposals++; _release?.Invoke(); }
    }
    private sealed class Bridge : INativeVoidContextBridge
    {
        public IntPtr EntryAddress => new(0x4433);
        internal bool Configured, Known = true, ThrowActivity;
        internal long InFlight; internal int Originals;
        internal Action? ConfigureAction, AfterManagedExit;
        internal Action<NativeVoidFrame>? Original;
        private Func<NativeVoidFrame, nuint> _enter = null!;
        private Action<NativeVoidFrame, nuint, NativeVoidExitFlags> _exit = null!;
        public void Configure(IntPtr original, Func<NativeVoidFrame, nuint> enter,
            Action<NativeVoidFrame, nuint, NativeVoidExitFlags> exit)
        { Check(original != IntPtr.Zero && !Configured); ConfigureAction?.Invoke(); (_enter, _exit, Configured) = (enter, exit, true); }
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity()
        { if (ThrowActivity) throw new IOException("Statistics unavailable"); return new(Interlocked.Read(ref InFlight), Configured && Known); }
        internal nuint Begin(NativeVoidFrame frame)
        {
            Check(Configured); Interlocked.Increment(ref InFlight); nuint token = 0;
            try { token = _enter(frame); Interlocked.Increment(ref Originals); Original?.Invoke(frame); return token; }
            catch { End(frame, token, NativeVoidExitFlags.Abnormal); throw; }
        }
        internal void End(NativeVoidFrame frame, nuint token, NativeVoidExitFlags flags = NativeVoidExitFlags.Returned)
        { try { _exit(frame, token, flags); AfterManagedExit?.Invoke(); } finally { Interlocked.Decrement(ref InFlight); } }
        internal void Invoke(NativeVoidFrame frame) { var token = Begin(frame); End(frame, token); }
    }
    private static ExactHookBroker.Binding Bind(Fixture f, VerifiedHookTarget target, Bridge bridge,
        Func<NativeVoidFrame, Scope?>? capture = null, Action<Scope, NativeVoidFrame>? after = null,
        Action<NativeVoidFrame, string>? fault = null)
        => f.Broker.BindNativeVoidObservation(target, () => bridge, capture ?? (_ => null), after ?? ((_, _) => { }), fault ?? ((_, _) => { }));
    private static void Layout()
    {
        Check((int)ExactHookAbi.CoroutineFactoryReference0 == 14 && (int)ExactHookAbi.NativeVoid0 == 15);
        Check(Marshal.SizeOf<NativeVoidFrame>() == 40 && Marshal.OffsetOf<NativeVoidFrame>(nameof(NativeVoidFrame.Self)).ToInt32() == 16
            && Marshal.OffsetOf<NativeVoidFrame>(nameof(NativeVoidFrame.Method)).ToInt32() == 24
            && Marshal.OffsetOf<NativeVoidFrame>(nameof(NativeVoidFrame.Caller)).ToInt32() == 32);
        var frame = Frame(Target()); Check(frame.IsValid && frame.Caller.ToInt64() == unchecked((long)0x7123456789ABCDEFUL));
        foreach (var field in new[] { "version", "size", "reserved0", "reserved1", "self", "caller" })
        {
            var invalid = frame;
            switch (field) { case "version": invalid.Version = 2; break; case "size": invalid.Size = 48; break;
                case "reserved0": invalid.Reserved0 = 1; break; case "reserved1": invalid.Reserved1 = 1; break;
                case "self": invalid.Self = IntPtr.Zero; break; default: invalid.Caller = IntPtr.Zero; break; }
            Check(!invalid.IsValid);
        }
    }
    private static void Publication()
    {
        using var f = new Fixture(); var target = Target(); var bridge = new Bridge(); var order = new List<string>(); var captures = 0;
        f.Engine.PrepareAction = () => order.Add("prepare"); bridge.ConfigureAction = () => order.Add("configure");
        f.Engine.CommitAction = () => { order.Add("commit"); Check(bridge.Configured); bridge.Invoke(Frame(target)); };
        using var first = Bind(f, target, bridge, _ => { captures++; return new(); });
        using var second = f.Broker.BindNativeVoidObservation<Scope>(target, () => throw new Exception("Second bridge"),
            _ => { captures++; return null; }, (_, _) => { }, (_, _) => { });
        bridge.Invoke(Frame(target));
        Check(order.SequenceEqual(new[] { "prepare", "configure", "commit" }) && f.Engine.Prepares == 1 && f.Engine.Commits == 1
            && f.Engine.Installs == 0 && bridge.Originals == 2 && captures == 3 && first.ReadActivity() is { Known: true, InFlight: 0 });
        Check(f.Broker.ReadExactActivity(target) is { Known: true, InFlight: 0 });
    }
    private static void Rejections()
    {
        using var f = new Fixture(); var target = Target(); var bridge = new Bridge();
        foreach (var invalid in new[] { target with { Address = IntPtr.Zero }, target with { MethodInfo = IntPtr.Zero },
            target with { DeclaringClass = IntPtr.Zero }, target with { ReturnClass = new(1) }, target with { Abi = ExactHookAbi.InstanceVoid } })
            Reject(() => Bind(f, invalid, bridge), "ABI");
        Reject(() => f.Broker.Bind(target, _ => true, (_, _, _, _) => { }), "sealed");
        Reject(() => f.Broker.BindRetainedObservation(target, _ => true, _ => new Scope(), _ => { }), "InstanceVoid");
        Reject(() => f.Broker.BindEarlyRetainedObservation(target, _ => true, _ => new Scope(), _ => { }), "exact void");
        Check(f.Engine.Prepares == 0 && f.Engine.Installs == 0 && !bridge.Configured);
        using var native = Bind(f, target, bridge);
        Reject(() => Bind(f, target with { MethodInfo = new(1) }, bridge), "identity");
        Reject(() => Bind(f, target with { DeclaringClass = new(1) }, bridge), "identity");
        Reject(() => f.Broker.Bind(target with { Abi = ExactHookAbi.InstanceVoid }, _ => true, (_, _, _, _) => { }), "different exact hook ABI");
        var ordinaryTarget = Target() with { Abi = ExactHookAbi.InstanceVoid };
        f.Engine.Original = new ExactHookBroker.InstanceVoid((_, _) => { });
        using var ordinary = f.Broker.Bind(ordinaryTarget, _ => true, (_, _, _, _) => { });
        Reject(() => Bind(f, ordinaryTarget with { Abi = ExactHookAbi.NativeVoid0 }, new()), "different exact ABI");
    }
    private static void Reentry()
    {
        using var f = new Fixture(); var target = Target(); var bridge = new Bridge(); var order = new List<string>(); var frames = new List<NativeVoidFrame>();
        using var binding = Bind(f, target, bridge, frame => { frames.Add(frame); order.Add("capture" + frame.Self.ToInt64()); return new(() => order.Add("dispose" + frame.Self.ToInt64())); },
            (_, frame) => order.Add("after" + frame.Self.ToInt64()));
        using var probe = f.Broker.BindBoundaryProbe(target, (self, value, method) => { Check(value == 0 && method == target.MethodInfo); order.Add("enter" + self.ToInt64()); },
            (self, _, _, returned) => { Check(returned); order.Add("exit" + self.ToInt64()); });
        var outer = Frame(target); outer.Self = new(11); var child = outer; child.Self = new(22); child.Caller = new(unchecked((long)0x7234567890ABCDE1UL));
        bridge.Original = frame => { order.Add("original" + frame.Self.ToInt64()); if (frame.Self == new IntPtr(11)) bridge.Invoke(child); };
        bridge.Invoke(outer);
        Check(frames.Count == 2 && frames[0].Self == new IntPtr(11) && frames[1].Self == new IntPtr(22) && frames[1].Caller == child.Caller
            && order.SequenceEqual(new[] { "enter11", "capture11", "original11", "enter22", "capture22", "original22", "after22", "dispose22", "exit22", "after11", "dispose11", "exit11" })
            && bridge.Originals == 2 && binding.ReadActivity() is { Known: true, InFlight: 0 });
    }
    private static void WorkerDuringCommit()
    {
        using var f = new Fixture(); var target = Target(); var bridge = new Bridge(); var enter = 0; var exit = 0; var faults = 0;
        ExactHookBroker.BoundaryProbeBinding? probe = null;
        f.Engine.CommitAction = () => {
            probe = f.Broker.BindBoundaryProbe(target, (_, _, _) => Interlocked.Increment(ref enter), (_, _, _, _) => Interlocked.Increment(ref exit));
            var worker = Task.Run(() => bridge.Invoke(Frame(target))); Check(worker.Wait(5000), "Worker waited for Gate held by Commit."); worker.GetAwaiter().GetResult();
        };
        using var binding = Bind(f, target, bridge, _ => throw new Exception("Worker metadata capture"), fault: (_, _) => Interlocked.Increment(ref faults));
        Check(enter == 1 && exit == 1 && faults == 1 && bridge.Originals == 1 && binding.ReadActivity() is { Known: true, InFlight: 0 }); probe!.Dispose();
    }
    private static void Isolation()
    {
        using var f = new Fixture(); var target = Target(); var bridge = new Bridge(); var captures = 0; var faults = 0; var enters = 0; var exits = 0;
        using var binding = Bind(f, target, bridge, _ => { captures++; return new(); }, fault: (_, _) => Interlocked.Increment(ref faults));
        using var probe = f.Broker.BindBoundaryProbe(target, (_, _, _) => Interlocked.Increment(ref enters), (_, _, _, _) => Interlocked.Increment(ref exits));
        Task.Run(() => bridge.Invoke(Frame(target))).GetAwaiter().GetResult();
        var foreign = Frame(target); foreign.Method = new(0x987); bridge.Invoke(foreign);
        var noMethod = Frame(target); noMethod.Method = IntPtr.Zero; noMethod.Self = new(0x765); bridge.Invoke(noMethod);
        Check(captures == 1 && faults == 2 && enters == 3 && exits == 3 && bridge.Originals == 3
            && binding.ReadActivity() is { Known: true, InFlight: 0 } && probe.ReadActivity() is { Known: true, InFlight: 0 }
            && binding.Describe().SuppressedWrongThread == 1);
    }
    private static void Detach()
    {
        using var f = new Fixture(); var target = Target(); var bridge = new Bridge(); var order = new List<string>(); var afters = 0;
        ExactHookBroker.Binding? binding = null; ExactHookBroker.BoundaryProbeBinding? probe = null;
        binding = Bind(f, target, bridge, _ => new(() => { Check(binding!.ReadActivity().InFlight == 1); order.Add("dispose"); }), (_, _) => afters++);
        probe = f.Broker.BindBoundaryProbe(target, (_, _, _) => order.Add("enter"), (_, _, _, _) => { Check(probe!.ReadActivity().InFlight == 1); order.Add("exit"); });
        bridge.Original = _ => { binding.Dispose(); probe.Dispose(); Check(binding.ReadActivity() is { Known: true, InFlight: 1 }); order.Add("original"); };
        bridge.Invoke(Frame(target)); bridge.Invoke(Frame(target));
        Check(afters == 0 && order.SequenceEqual(new[] { "enter", "original", "dispose", "exit", "original" }) && bridge.Originals == 2
            && binding.ReadActivity() is { Known: true, InFlight: 0 } && probe.ReadActivity() is { Known: true, InFlight: 0 });
    }
    private static void ObserverFaults()
    {
        foreach (var stage in new[] { "capture", "after", "dispose" })
        {
            using var f = new Fixture(); var target = Target(); var bridge = new Bridge(); var healthy = 0; var releases = 0; var faults = 0;
            using var broken = Bind(f, target, bridge, _ => stage == "capture" ? throw new IOException(stage)
                : new(() => { if (stage == "dispose") throw new IOException(stage); }), (_, _) => { if (stage == "after") throw new IOException(stage); }, (_, _) => faults++);
            using var peer = Bind(f, target, bridge, _ => new(() => releases++), (_, _) => healthy++);
            bridge.Invoke(Frame(target)); bridge.Invoke(Frame(target));
            Check(bridge.Originals == 2 && healthy == 2 && releases == 2 && faults == 2 && broken.ReadActivity() is { Known: false, InFlight: 0 }
                && !peer.ReadActivity().Known);
        }
    }
    private static void ProbeFaults()
    {
        foreach (var stage in new[] { "enter", "exit" })
        {
            using var f = new Fixture(); var target = Target(); var bridge = new Bridge(); var captures = 0; var exits = 0;
            using var binding = Bind(f, target, bridge, _ => { captures++; return new(); });
            using var probe = f.Broker.BindBoundaryProbe(target, (_, _, _) => { if (stage == "enter") throw new IOException(stage); },
                (_, _, _, returned) => { Check(returned); exits++; if (stage == "exit") throw new IOException(stage); });
            bridge.Invoke(Frame(target));
            Check(bridge.Originals == 1 && captures == (stage == "enter" ? 0 : 1) && exits == 1 && !binding.ReadActivity().Known && !probe.ReadActivity().Known);
        }
    }
    private static void OriginalFault()
    {
        using var f = new Fixture(); var target = Target(); var bridge = new Bridge(); var expected = new IOException("Original controlled failure");
        var afters = 0; var releases = 0; var faults = 0; var exits = new List<bool>();
        using var binding = Bind(f, target, bridge, _ => new(() => { Check(bridge.InFlight == 1); releases++; }), (_, _) => afters++, (_, _) => faults++);
        using var probe = f.Broker.BindBoundaryProbe(target, (_, _, _) => { }, (_, _, _, returned) => exits.Add(returned));
        bridge.Original = _ => throw expected;
        try { bridge.Invoke(Frame(target)); throw new Exception("Original failure disappeared"); } catch (IOException actual) { Check(ReferenceEquals(actual, expected)); }
        bridge.Original = null; bridge.Invoke(Frame(target));
        Check(bridge.Originals == 2 && afters == 1 && releases == 2 && faults == 1 && exits.SequenceEqual(new[] { false, true })
            && binding.ReadActivity() is { Known: false, InFlight: 0 });
    }
    private static void NativeExitActivity()
    {
        using var f = new Fixture(); var target = Target(); var bridge = new Bridge(); var checks = 0;
        ExactHookBroker.Binding? binding = null; ExactHookBroker.BoundaryProbeBinding? probe = null;
        void Busy() { Check(binding!.ReadActivity() is { Known: true, InFlight: 1 } && probe!.ReadActivity() is { Known: true, InFlight: 1 }
            && f.Broker.ReadExactActivity(target) is { Known: true, InFlight: 1 }); checks++; }
        binding = Bind(f, target, bridge, _ => new(Busy), (_, _) => Busy());
        probe = f.Broker.BindBoundaryProbe(target, (_, _, _) => { }, (_, _, _, _) => Busy());
        bridge.Original = _ => Busy(); bridge.AfterManagedExit = Busy;
        bridge.Invoke(Frame(target)); Check(checks == 5 && binding.ReadActivity() is { Known: true, InFlight: 0 }); binding.Dispose(); probe.Dispose();
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
            Reject(() => Bind(f, target, bridge), "Uncertain"); Reject(() => Bind(f, target, bridge), "without retry");
            Check(f.Engine.Prepares == 1 && f.Engine.Commits == (stage == "commit" ? 1 : 0) && !f.Broker.ReadExactActivity(target).Known);
        }
        foreach (var stage in new[] { "unknown", "negative", "throw" })
        {
            using var f = new Fixture(); var target = Target(); var bridge = new Bridge(); using var binding = Bind(f, target, bridge);
            using var probe = f.Broker.BindBoundaryProbe(target, (_, _, _) => { }, (_, _, _, _) => { });
            bridge.Known = stage != "unknown"; bridge.InFlight = stage == "negative" ? -1 : 3; bridge.ThrowActivity = stage == "throw";
            Check(!binding.ReadActivity().Known && !probe.ReadActivity().Known);
            bridge.Known = true; bridge.InFlight = 0; bridge.ThrowActivity = false;
            Check(binding.ReadActivity() is { Known: false, InFlight: 0 } && !probe.ReadActivity().Known);
        }
    }
    private static void InvalidFrames()
    {
        foreach (var stage in new[] { "entry", "exit", "flags" })
        {
            using var f = new Fixture(); var target = Target(); var bridge = new Bridge(); var captures = 0; var afters = 0; var releases = 0; var faults = 0; var enters = 0; var exits = 0;
            using var binding = Bind(f, target, bridge, _ => { captures++; return new(() => releases++); }, (_, _) => afters++, (_, _) => faults++);
            using var probe = f.Broker.BindBoundaryProbe(target, (_, _, _) => enters++, (_, _, _, returned) => { exits++; Check(returned == (stage == "entry")); });
            var frame = Frame(target); if (stage == "entry") frame.Size = 0;
            var token = bridge.Begin(frame); if (stage == "exit") frame.Caller = new(0x123);
            bridge.End(frame, token, stage == "flags" ? (NativeVoidExitFlags)8 : NativeVoidExitFlags.Returned);
            Check(captures == (stage == "entry" ? 0 : 1) && afters == 0 && releases == captures && faults == 1 && enters == 1 && exits == 1
                && bridge.Originals == 1 && binding.ReadActivity() is { Known: false, InFlight: 0 });
        }
    }
    private static void Capacity()
    {
        using var f = new Fixture(); var target = Target(); var bridge = new Bridge(); var captures = 0; var afters = 0; var releases = 0; var faults = 0; var enters = 0; var exits = 0;
        using var binding = Bind(f, target, bridge, _ => { captures++; return new(() => releases++); }, (_, _) => afters++, (_, _) => faults++);
        using var probe = f.Broker.BindBoundaryProbe(target, (_, _, _) => enters++, (_, _, _, _) => exits++);
        var frame = Frame(target); var tokens = Enumerable.Range(0, ExactHookBroker.NativeVoidInvocationLimit).Select(_ => bridge.Begin(frame)).ToArray();
        Check(tokens.All(token => token != 0) && tokens.Distinct().Count() == tokens.Length && captures == tokens.Length && enters == tokens.Length);
        var overflow = bridge.Begin(frame); Check(overflow == 0 && captures == tokens.Length && enters == tokens.Length && exits == 0 && faults == 1
            && binding.ReadActivity() is { Known: false, InFlight: 4097 });
        bridge.End(frame, overflow); Check(exits == 0, "An unobserved overflow frame fabricated a paired probe exit.");
        foreach (var token in tokens.Reverse()) bridge.End(frame, token);
        Check(bridge.Originals == tokens.Length + 1 && afters == tokens.Length && releases == tokens.Length && exits == tokens.Length
            && binding.ReadActivity() is { Known: false, InFlight: 0 });
    }
    private static void ConcurrentWorkers()
    {
        using var f = new Fixture(); var target = Target(); var bridge = new Bridge(); var enters = 0; var exits = 0; var faults = 0;
        using var binding = Bind(f, target, bridge, _ => throw new Exception("Worker capture"), fault: (_, _) => Interlocked.Increment(ref faults));
        using var probe = f.Broker.BindBoundaryProbe(target, (_, _, _) => Interlocked.Increment(ref enters), (_, _, _, _) => Interlocked.Increment(ref exits));
        Task.WaitAll(Enumerable.Range(0, 8).Select(worker => Task.Run(() => { for (var index = 0; index < 50; ++index) { var frame = Frame(target); frame.Self = new(100 + worker); bridge.Invoke(frame); } })).ToArray());
        Check(bridge.Originals == 400 && enters == 400 && exits == 400 && faults == 400
            && binding.ReadActivity() is { Known: true, InFlight: 0 } && probe.ReadActivity() is { Known: true, InFlight: 0 });
    }
    private static void ProcessRoots()
    {
        using var f = new Fixture(); var target = Target(); var bridge = new Bridge(); var captures = 0;
        var binding = Bind(f, target, bridge, _ => { captures++; return new(); }); binding.Dispose();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); bridge.Invoke(Frame(target));
        using var rebound = f.Broker.BindNativeVoidObservation<Scope>(target, () => throw new Exception("New bridge after GC"), _ => { captures++; return new(); }, (_, _) => { }, (_, _) => { });
        bridge.Invoke(Frame(target)); Check(bridge.Originals == 2 && captures == 1 && f.Engine.Prepares == 1 && rebound.ReadActivity() is { Known: true, InFlight: 0 });
    }
    private static void FaultReporter()
    {
        using var f = new Fixture(); var target = Target(); var bridge = new Bridge(); var faults = 0; var exits = 0;
        using var broken = Bind(f, target, bridge, fault: (_, _) => throw new IOException("Scalar fault reporter failed"));
        using var peer = Bind(f, target, bridge, fault: (_, _) => Interlocked.Increment(ref faults));
        using var probe = f.Broker.BindBoundaryProbe(target, (_, _, _) => { }, (_, _, _, _) => Interlocked.Increment(ref exits));
        Task.Run(() => bridge.Invoke(Frame(target))).GetAwaiter().GetResult();
        Check(bridge.Originals == 1 && faults == 1 && exits == 1 && broken.ReadActivity() is { Known: false, InFlight: 0 }
            && !peer.ReadActivity().Known && !probe.ReadActivity().Known);
    }
    private static void CurrentStages()
    {
        using var f = new Fixture(); var target = Target(); var bridge = new Bridge(); var checks = 0; var expected = Frame(target); Scope? retained = null;
        ExactHookBroker.Binding? binding = null; ExactHookBroker.NativeVoidPhysicalIdentity? identity = null;
        void Closed() { if (binding is not null) { Check(!binding.TryGetCurrentNativeVoidContext<Scope>(expected.Self, out var context, out var current) && context is null && current is null); checks++; } }
        binding = Bind(f, target, bridge, _ => { Closed(); return retained = new(Closed); }, (_, _) => Closed());
        using var probe = f.Broker.BindBoundaryProbe(target, (_, _, _) => Closed(), (_, _, _, _) => Closed());
        bridge.Original = frame => {
            Check(binding.TryGetCurrentNativeVoidContext<Scope>(frame.Self, out var context, out identity) && ReferenceEquals(context, retained));
            Check(identity is not null && identity.Entry == target && identity.Token != 0 && identity.ThreadId == Environment.CurrentManagedThreadId
                && identity.Frame.Equals(expected));
            Check(!binding.TryGetCurrentNativeVoidContext<Scope>(IntPtr.Zero, out _, out _)); checks++;
        };
        bridge.AfterManagedExit = Closed; Closed(); bridge.Invoke(expected); Closed();
        Check(checks == 9 && identity is not null && retained!.Disposals == 1 && binding.ReadActivity() is { Known: true, InFlight: 0 }); binding.Dispose();
    }
    private static void CurrentReentry()
    {
        using var f = new Fixture(); var target = Target(); var bridge = new Bridge(); var outer = Frame(target); var child = outer;
        var calls = 0; var tokenList = new List<nuint>(); var captures = new List<Scope>();
        using var binding = Bind(f, target, bridge, _ => { var scope = new Scope(); captures.Add(scope); return scope; });
        foreach (var same in new[] { true, false })
        {
            child.Self = same ? outer.Self : new(0x7654);
            bridge.Original = frame => {
                Check(binding.TryGetCurrentNativeVoidContext<Scope>(frame.Self, out var context, out var identity));
                Check(identity is not null && context == captures[^1]); tokenList.Add(identity!.Token);
                if (++calls % 2 == 1)
                {
                    var saved = identity.Token; bridge.Invoke(child);
                    Check(binding.TryGetCurrentNativeVoidContext<Scope>(outer.Self, out var parentContext, out var restored)
                        && restored!.Token == saved && ReferenceEquals(parentContext, captures[^2]));
                }
                else if (!same) Check(!binding.TryGetCurrentNativeVoidContext<Scope>(outer.Self, out _, out _));
            };
            bridge.Invoke(outer);
        }
        Check(calls == 4 && captures.Count == 4 && captures.All(scope => scope.Disposals == 1) && tokenList.Distinct().Count() == 4);
    }
    private static void CurrentForeignMasks()
    {
        foreach (var foreignMetadata in new[] { true, false })
        {
            using var f = new Fixture(); var target = Target(); var bridge = new Bridge(); var calls = 0; var outer = Frame(target); var child = outer;
            child.Self = new(0x999); if (foreignMetadata) child.Method = new(0xBAD);
            using var binding = Bind(f, target, bridge, frame => frame.Self == outer.Self ? new() : null);
            bridge.Original = frame => {
                calls++;
                if (frame.Self == outer.Self)
                {
                    Check(binding.TryGetCurrentNativeVoidContext<Scope>(outer.Self, out _, out var parent)); bridge.Invoke(child);
                    Check(binding.TryGetCurrentNativeVoidContext<Scope>(outer.Self, out _, out var current) && current!.Token == parent!.Token);
                }
                else Check(!binding.TryGetCurrentNativeVoidContext<Scope>(outer.Self, out _, out _) && !binding.TryGetCurrentNativeVoidContext<Scope>(child.Self, out _, out _));
            };
            bridge.Invoke(outer); Check(calls == 2 && binding.ReadActivity() is { Known: true, InFlight: 0 });
        }
    }
    private static void CurrentOwnership()
    {
        using var f = new Fixture(); var target = Target(); var bridge = new Bridge(); var outer = Frame(target);
        var firstContext = new Scope(); var secondContext = new Scope();
        using var first = Bind(f, target, bridge, _ => firstContext);
        using var second = Bind(f, target, bridge, _ => secondContext);
        using var empty = Bind(f, target, bridge);
        bridge.Original = frame => {
            Check(first.TryGetCurrentNativeVoidContext<Scope>(frame.Self, out var a, out var ai) && ReferenceEquals(a, firstContext));
            Check(second.TryGetCurrentNativeVoidContext<Scope>(frame.Self, out var b, out var bi) && ReferenceEquals(b, secondContext) && ai!.Token == bi!.Token);
            Check(!empty.TryGetCurrentNativeVoidContext<Scope>(frame.Self, out _, out _));
            first.Dispose(); Check(!first.TryGetCurrentNativeVoidContext<Scope>(frame.Self, out _, out _));
            Check(second.TryGetCurrentNativeVoidContext<Scope>(frame.Self, out _, out _));
        };
        bridge.Invoke(outer); Check(firstContext.Disposals == 1 && secondContext.Disposals == 1);
    }
    private static void CurrentCrossEntry()
    {
        using var f = new Fixture(); var parentTarget = Target(); var childTarget = Target(); var parentBridge = new Bridge(); var childBridge = new Bridge();
        var parentFrame = Frame(parentTarget); var childFrame = Frame(childTarget); var hits = 0;
        using var parent = Bind(f, parentTarget, parentBridge, _ => new());
        using var child = Bind(f, childTarget, childBridge, _ => new());
        childBridge.Original = frame => {
            Check(!parent.TryGetCurrentNativeVoidContext<Scope>(parentFrame.Self, out _, out _));
            Check(child.TryGetCurrentNativeVoidContext<Scope>(frame.Self, out _, out var identity) && identity!.Entry == childTarget); hits++;
        };
        parentBridge.Original = frame => {
            Check(parent.TryGetCurrentNativeVoidContext<Scope>(frame.Self, out _, out var identity)); childBridge.Invoke(childFrame);
            Check(parent.TryGetCurrentNativeVoidContext<Scope>(frame.Self, out _, out var restored) && restored!.Token == identity!.Token);
        };
        parentBridge.Invoke(parentFrame);
        parentBridge.Original = frame => { Check(!parent.TryGetCurrentNativeVoidContext<Scope>(frame.Self, out _, out _)); hits++; };
        Task.Run(() => parentBridge.Invoke(parentFrame)).GetAwaiter().GetResult();
        Check(hits == 2 && parent.ReadActivity() is { Known: true, InFlight: 0 } && child.ReadActivity() is { Known: true, InFlight: 0 });
    }
    private static void CurrentOverflowMask()
    {
        using var f = new Fixture(); var parentTarget = Target(); var saturatedTarget = Target(); var parentBridge = new Bridge(); var saturatedBridge = new Bridge();
        var parentFrame = Frame(parentTarget); var saturatedFrame = Frame(saturatedTarget); var checks = 0;
        using var parent = Bind(f, parentTarget, parentBridge, _ => new());
        using var saturated = Bind(f, saturatedTarget, saturatedBridge);
        var tokens = Enumerable.Range(0, ExactHookBroker.NativeVoidInvocationLimit).Select(_ => saturatedBridge.Begin(saturatedFrame)).ToArray();
        parentBridge.Original = frame => {
            Check(parent.TryGetCurrentNativeVoidContext<Scope>(frame.Self, out _, out _)); checks++;
            var overflow = saturatedBridge.Begin(saturatedFrame); Check(overflow == 0);
            Check(!parent.TryGetCurrentNativeVoidContext<Scope>(frame.Self, out _, out _)); checks++;
            var nestedOverflow = saturatedBridge.Begin(saturatedFrame); Check(nestedOverflow == 0);
            saturatedBridge.End(saturatedFrame, nestedOverflow);
            Check(!parent.TryGetCurrentNativeVoidContext<Scope>(frame.Self, out _, out _)); checks++;
            saturatedBridge.End(saturatedFrame, overflow);
            Check(parent.TryGetCurrentNativeVoidContext<Scope>(frame.Self, out _, out _)); checks++;
        };
        parentBridge.Invoke(parentFrame);
        foreach (var token in tokens.Reverse()) saturatedBridge.End(saturatedFrame, token);
        Check(checks == 4 && parent.ReadActivity() is { Known: true, InFlight: 0 } && saturated.ReadActivity() is { Known: false, InFlight: 0 });
    }
    private static void MidFrameLayout()
    {
        Check(Marshal.SizeOf<NativeEventMidFrame>() == 0x1b0
            && Marshal.OffsetOf<NativeEventMidFrame>(nameof(NativeEventMidFrame.ReturnPc)).ToInt32() == 8
            && Marshal.OffsetOf<NativeEventMidFrame>(nameof(NativeEventMidFrame.EntryRsp)).ToInt32() == 0x10
            && Marshal.OffsetOf<NativeEventMidFrame>(nameof(NativeEventMidFrame.Rflags)).ToInt32() == 0x18
            && Marshal.OffsetOf<NativeEventMidFrame>(nameof(NativeEventMidFrame.Rsi)).ToInt32() == 0x50
            && Marshal.OffsetOf<NativeEventMidFrame>(nameof(NativeEventMidFrame.Xmm0)).ToInt32() == 0xa0
            && Marshal.OffsetOf<NativeEventMidFrame>(nameof(NativeEventMidFrame.Xmm15)).ToInt32() == 0x190
            && Marshal.OffsetOf<NativeEventMidFrame>(nameof(NativeEventMidFrame.Mxcsr)).ToInt32() == 0x1a0
            && Marshal.OffsetOf<NativeEventMidFrame>(nameof(NativeEventMidFrame.Fpcsr)).ToInt32() == 0x1a4);
        var frame = new NativeEventMidFrame { Version = 1, Size = 0x1b0, ReturnPc = new(0x71234567), EntryRsp = 0x1234567887654000,
            Rsp = 0x1234567887654008, Rsi = 0x1234567890ABCDEF, Rbp = 0x234567890ABCDEF1 };
        Check(frame.IsValid && frame.Event == new IntPtr(unchecked((long)frame.Rsi)) && frame.Executing == new IntPtr(unchecked((long)frame.Rbp)));
        frame.Reserved0 = 1; Check(!frame.IsValid); frame.Reserved0 = 0; frame.Reserved1 = 1; Check(!frame.IsValid);
        frame.Reserved1 = 0; frame.Rsp++; Check(!frame.IsValid);
    }
    private static void CurrentBrokenOrder()
    {
        // Corrupt physical ordering deliberately quarantines its calling thread.
        // Keep that impossible native state on a separate dedicated test thread.
        Exception? failure = null;
        var worker = new Thread(() => {
            try
            {
                using var f = new Fixture(); var target = Target(); var bridge = new Bridge(); var frame = Frame(target); var releases = 0;
                using var binding = Bind(f, target, bridge, _ => new(() => releases++));
                var parent = bridge.Begin(frame); var child = bridge.Begin(frame);
                Check(binding.TryGetCurrentNativeVoidContext<Scope>(frame.Self, out _, out var identity) && identity!.Token == child);
                bridge.End(frame, parent);
                Check(!binding.TryGetCurrentNativeVoidContext<Scope>(frame.Self, out _, out _) && releases == 0
                    && binding.ReadActivity() is { Known: false, InFlight: 2 });
                bridge.End(frame, child);
                Check(!binding.TryGetCurrentNativeVoidContext<Scope>(frame.Self, out _, out _) && releases == 1
                    && binding.ReadActivity() is { Known: false, InFlight: 1 });
            }
            catch (Exception error) { failure = error; }
        });
        worker.Start(); Check(worker.Join(10000), "Physical-order test thread did not join.");
        if (failure is not null) throw new InvalidOperationException("Physical-order quarantine failed.", failure);
    }
    private static void CurrentIdentityReplay()
    {
        using var f = new Fixture(); var target = Target(); var bridge = new Bridge(); var reused = new Scope(); var calls = 0;
        ExactHookBroker.NativeVoidPhysicalIdentity? old = null; ExactHookBroker.NativeVoidPhysicalIdentity? latest = null;
        using var binding = Bind(f, target, bridge, _ => reused);
        using var sibling = Bind(f, target, bridge, _ => new());
        bridge.Original = frame => {
            Check(binding.TryGetCurrentNativeVoidContext<Scope>(frame.Self, out var actual, out latest) && ReferenceEquals(actual, reused));
            Check(latest!.IsCurrentFor(reused) && !latest.IsCurrentFor(new Scope()));
            Check(sibling.TryGetCurrentNativeVoidContext<Scope>(frame.Self, out var otherContext, out var otherIdentity)
                && otherIdentity!.IsCurrentFor(otherContext!) && !latest.IsCurrentFor(otherContext!));
            if (++calls == 1) old = latest;
            else
            {
                Check(old is not null && old.Frame.Equals(latest.Frame) && old.Token != latest.Token && !old.IsCurrentFor(reused),
                    "A stale certificate acquired a later generation that reused the same context object.");
                binding.Dispose(); Check(!latest.IsCurrentFor(reused));
            }
        };
        bridge.Invoke(Frame(target)); Check(old is not null && !old.IsCurrentFor(reused));
        bridge.Invoke(Frame(target)); Check(calls == 2 && reused.Disposals == 2 && !latest!.IsCurrentFor(reused));
    }
}
