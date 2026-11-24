using System.Runtime.InteropServices;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

/// <summary>Exercises the production broker dispatch core with a controlled bridge.
/// These are managed contract checks, not native SEH/ABI or game cancellation evidence.</summary>
internal static class NativeOperationObservationTests
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Native operation sealed four-kind frame layout and invalid declarations reject before publication", LayoutAndRejections),
        ("Native operation Prepare Configure Commit publishes once and shares exact dual observers", PublicationAndSharing),
        ("Native operation abnormal completion preserves flags and cleanup without returned credit", AbnormalCompletion),
        ("Native operation capture completion cleanup and fault reporter failures isolate captured peers", ObserverFaults),
        ("Native operation capture diagnostics distinguish the outer invocation from its failure", CaptureDiagnostics),
        ("Native operation in-flight detach keeps completion duty and permanent callbacks across GC", DetachAndRoots),
        ("Native operation workers foreign methods and same or cross-target nesting retain exact scopes", IsolationAndReentry),
        ("Native operation malformed frames and broken parent exits retain unknown responsibility", InvalidFramesAndBrokenParents),
        ("Native operation uncertain publication and native activity remain permanently owned without retry", PermanentFailureAndActivity),
        ("Native operation skipped original keeps exact completion and cleanup without original or returned credit", SkippedOriginal),
        ("Native operation combined skipped and returned flags cannot manufacture a successful original", InvalidSkipFlags),
        ("Native operation root credentials exist only in the exact Before observer and retain their original identity", RootCredentials),
        ("Native operation direct-parent proof cannot cross a foreign call whose observer returned no context", RootParentMask)
    };

    private static void Check(bool value, string message = "Native operation observation check failed")
    { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action, string expected)
    {
        Exception? observed = null; try { action(); } catch (Exception error) { observed = error; }
        Check(observed is not null && observed.Message.Contains(expected, StringComparison.OrdinalIgnoreCase),
            "Expected rejection containing '" + expected + "': " + observed);
    }
    private static VerifiedHookTarget Target(NativeOperationKind kind = NativeOperationKind.Reference1)
        => new("operation.controlled", new(Interlocked.Add(ref Addresses.Next, 16)), new(0x123), new(0x321), ExactHookBroker.OperationAbi(kind));
    private static long _sequence;
    private static NativeOperationFrame Frame(VerifiedHookTarget target, NativeOperationKind kind, long self = 11) => new()
    {
        Version = 1, Size = 64, Kind = kind, Self = new(self), Method = target.MethodInfo,
        Arg0 = kind == NativeOperationKind.Void0 ? IntPtr.Zero : new(unchecked((long)0x8123456789abcdefUL)),
        Arg1 = kind == NativeOperationKind.Reference2 ? new(unchecked((long)0xfedcba9876543210UL)) : IntPtr.Zero,
        Caller = new(unchecked((long)0x723456789abcdef0UL)), Sequence = checked((ulong)Interlocked.Increment(ref _sequence))
    };
    private sealed class Scope : IDisposable
    {
        internal readonly NativeOperationFrame Frame;
        internal int Disposals;
        private readonly Action? _release;
        internal Scope(NativeOperationFrame frame, Action? release = null) => (Frame, _release) = (frame, release);
        public void Dispose() { Disposals++; _release?.Invoke(); }
    }
    private sealed class Bridge : INativeOperationContextBridge
    {
        public NativeOperationKind Kind { get; }
        public IntPtr EntryAddress { get; set; } = new(0x4433);
        internal bool Configured, Known = true, ThrowActivity;
        internal long InFlight;
        internal int Originals, Configurations;
        internal Action? ConfigureAction, AfterManagedExit;
        internal Action<NativeOperationFrame>? Original;
        private Func<NativeOperationFrame, nuint> _enter = null!;
        private Action<NativeOperationFrame, nuint, NativeOperationExitFlags> _exit = null!;
        internal Bridge(NativeOperationKind kind = NativeOperationKind.Reference1) => Kind = kind;
        public void Configure(IntPtr original, Func<NativeOperationFrame, nuint> enter,
            Action<NativeOperationFrame, nuint, NativeOperationExitFlags> exit)
        {
            Configurations++; Check(original == new IntPtr(0x4422) && !Configured);
            ConfigureAction?.Invoke(); (_enter, _exit, Configured) = (enter, exit, true);
        }
        public ExactHookBroker.ManagedBoundaryActivity ReadActivity()
        { if (ThrowActivity) throw new IOException("Statistics unavailable"); return new(Interlocked.Read(ref InFlight), Configured && Known); }
        internal nuint Begin(NativeOperationFrame frame)
        {
            Check(Configured); Interlocked.Increment(ref InFlight); nuint token = 0;
            try { token = _enter(frame); Interlocked.Increment(ref Originals); Original?.Invoke(frame); return token; }
            catch { End(frame, token, NativeOperationExitFlags.Abnormal); throw; }
        }
        internal void RawExit(NativeOperationFrame frame, nuint token, NativeOperationExitFlags flags)
            => _exit(frame, token, flags);
        internal void End(NativeOperationFrame frame, nuint token, NativeOperationExitFlags flags = NativeOperationExitFlags.Returned)
        { try { _exit(frame, token, flags); AfterManagedExit?.Invoke(); } finally { Interlocked.Decrement(ref InFlight); } }
        internal void Invoke(NativeOperationFrame frame) { var token = Begin(frame); End(frame, token); }
        internal void Skip(NativeOperationFrame frame, NativeOperationExitFlags flags = NativeOperationExitFlags.Skipped)
        {
            Check(Configured); Interlocked.Increment(ref InFlight);
            try { var token = _enter(frame); _exit(frame, token, flags); }
            finally { Interlocked.Decrement(ref InFlight); }
        }
    }
    private static ExactHookBroker.Binding Bind(Fixture fixture, VerifiedHookTarget target, Bridge bridge,
        Func<NativeOperationFrame, Scope?>? capture = null,
        Action<Scope, NativeOperationFrame, NativeOperationExitFlags>? complete = null,
        Action<NativeOperationFrame, string>? fault = null)
        => fixture.Broker.BindNativeOperationObservation(target, bridge.Kind, () => bridge,
            capture ?? (_ => null), complete ?? ((_, _, _) => { }), fault ?? ((_, _) => { }));

    private static void LayoutAndRejections()
    {
        Check(IntPtr.Size == 8 && Marshal.SizeOf<NativeOperationFrame>() == 64);
        var fields = new[] { nameof(NativeOperationFrame.Version), nameof(NativeOperationFrame.Size), nameof(NativeOperationFrame.Kind),
            nameof(NativeOperationFrame.Reserved), nameof(NativeOperationFrame.Self), nameof(NativeOperationFrame.Arg0),
            nameof(NativeOperationFrame.Arg1), nameof(NativeOperationFrame.Method), nameof(NativeOperationFrame.Caller), nameof(NativeOperationFrame.Sequence) };
        var offsets = new[] { 0, 4, 8, 12, 16, 24, 32, 40, 48, 56 };
        for (var index = 0; index < fields.Length; index++) Check(Marshal.OffsetOf<NativeOperationFrame>(fields[index]).ToInt32() == offsets[index]);
        Check((int)ExactHookAbi.OperationVoid0 == 16 && (int)ExactHookAbi.OperationReference1Void == 17
            && (int)ExactHookAbi.OperationReference2Void == 18 && (int)ExactHookAbi.OperationSingle1Void == 19);
        foreach (var kind in Enum.GetValues<NativeOperationKind>())
        {
            using var f = new Fixture(); var target = Target(kind); var bridge = new Bridge(kind); var frame = Frame(target, kind);
            Check(frame.IsValid && frame.Caller.ToInt64() == unchecked((long)0x723456789abcdef0UL));
            foreach (var invalid in new[] { target with { Address = IntPtr.Zero }, target with { MethodInfo = IntPtr.Zero },
                target with { DeclaringClass = IntPtr.Zero }, target with { ReturnClass = new(1) }, target with { Abi = ExactHookAbi.InstanceVoid } })
                Reject(() => Bind(f, invalid, bridge), "ABI");
            Reject(() => f.Broker.Bind(target, _ => true, (_, _, _, _) => { }), "sealed");
            Reject(() => f.Broker.BindNativeOperationObservation<Scope>(target, kind, () => new Bridge((NativeOperationKind)(((uint)kind + 1) % 4)),
                _ => null, (_, _, _) => { }, (_, _) => { }), "kind");
            Reject(() => Bind(f, target, new(kind) { EntryAddress = IntPtr.Zero }), "kind");
            Check(f.Engine.Prepares == 0 && f.Engine.Commits == 0 && f.Engine.Installs == 0 && !bridge.Configured);
        }
        Reject(() => ExactHookBroker.OperationAbi((NativeOperationKind)4), "Unsupported");
    }

    private static void PublicationAndSharing()
    {
        foreach (var kind in Enum.GetValues<NativeOperationKind>())
        {
            using var f = new Fixture(); var target = Target(kind); var bridge = new Bridge(kind); var order = new List<string>();
            var captures = 0; var completed = 0; var disposed = 0;
            f.Engine.PrepareAction = () => { Check(!bridge.Configured); order.Add("prepare"); };
            bridge.ConfigureAction = () => order.Add("configure");
            f.Engine.CommitAction = () => { Check(bridge.Configured); order.Add("commit"); bridge.Invoke(Frame(target, kind)); };
            Scope Capture(NativeOperationFrame frame)
            {
                Check(frame.IsValid && frame.Kind == kind && frame.Method == target.MethodInfo); captures++;
                return new(frame, () => disposed++);
            }
            void Complete(Scope scope, NativeOperationFrame frame, NativeOperationExitFlags flags)
            { Check(scope.Frame.Equals(frame) && scope.Disposals == 0 && flags == NativeOperationExitFlags.Returned); completed++; }
            using var first = Bind(f, target, bridge, Capture, Complete);
            var peerBroker = new ExactHookBroker(f.Engine, f.Main, f.Log);
            using var second = peerBroker.BindNativeOperationObservation(target, kind,
                () => throw new Exception("Second bridge must not be constructed"), Capture, Complete, (_, _) => { });
            bridge.Invoke(Frame(target, kind));
            Reject(() => Bind(f, target with { MethodInfo = new(456) }, bridge), "identity");
            Check(order.SequenceEqual(new[] { "prepare", "configure", "commit" }) && bridge.Configurations == 1
                && f.Engine.Prepares == 1 && f.Engine.Commits == 1 && f.Engine.Installs == 0 && bridge.Originals == 2
                && captures == 3 && completed == 3 && disposed == 3 && first.Describe().OriginalCalls == 2
                && first.ReadActivity() is { Known: true, InFlight: 0 } && second.ReadActivity() is { Known: true, InFlight: 0 });
            Check(f.Broker.ReadExactActivity(target) is { Known: true, InFlight: 0 }, "Exact physical activity must include the operation lane.");
        }
    }

    private static void AbnormalCompletion()
    {
        foreach (var kind in Enum.GetValues<NativeOperationKind>())
        {
            using var f = new Fixture(); var target = Target(kind); var bridge = new Bridge(kind); var flags = new List<NativeOperationExitFlags>();
            var contexts = new List<Scope>(); var faults = 0; var expected = new IOException("Controlled original abnormal exit");
            using var binding = Bind(f, target, bridge, frame => { var scope = new Scope(frame); contexts.Add(scope); return scope; },
                (scope, frame, value) => { Check(scope.Frame.Equals(frame) && scope.Disposals == 0 && bridge.InFlight == 1); flags.Add(value); }, (_, _) => faults++);
            bridge.Original = _ => throw expected;
            Exception? caught = null; try { bridge.Invoke(Frame(target, kind)); } catch (Exception error) { caught = error; }
            Check(ReferenceEquals(caught, expected) && flags.SequenceEqual(new[] { NativeOperationExitFlags.Abnormal })
                && contexts.Single().Disposals == 1 && faults == 1 && binding.ReadActivity() is { Known: true, InFlight: 0 });
            bridge.Original = null; bridge.Invoke(Frame(target, kind));
            Check(flags.SequenceEqual(new[] { NativeOperationExitFlags.Abnormal, NativeOperationExitFlags.Returned })
                && contexts.Count == 2 && contexts.All(scope => scope.Disposals == 1) && bridge.Originals == 2);
        }
    }

    private static void ObserverFaults()
    {
        foreach (var stage in new[] { "capture", "complete", "dispose", "reporter" })
        {
            using var f = new Fixture(); var target = Target(); var bridge = new Bridge(); var scopes = new List<Scope>();
            var peerCompletes = 0; var peerDisposes = 0; var faults = 0;
            using var broken = Bind(f, target, bridge,
                frame => {
                    if (stage is "capture" or "reporter") throw new IOException("Capture unavailable");
                    var scope = new Scope(frame, () => { if (stage == "dispose") throw new IOException("Cleanup unavailable"); }); scopes.Add(scope); return scope;
                }, (_, _, _) => { if (stage == "complete") throw new IOException("Completion unavailable"); },
                (_, _) => { faults++; if (stage == "reporter") throw new IOException("Fault reporter unavailable"); });
            using var peer = Bind(f, target, bridge, frame => new(frame, () => peerDisposes++), (_, _, _) => peerCompletes++);
            bridge.Invoke(Frame(target, bridge.Kind));
            Check(peerCompletes == 1 && peerDisposes == 1 && scopes.All(scope => scope.Disposals == 1)
                && faults == 1 && bridge.Originals == 1 && broken.ReadActivity() is { Known: false, InFlight: 0 } && !peer.ReadActivity().Known);
            // Sticky unknown inhibits new capture; already captured peers above still completed and released.
            bridge.Invoke(Frame(target, bridge.Kind));
            Check(bridge.Originals == 2 && peerCompletes == 1 && peerDisposes == 1 && faults == 2 && broken.ReadActivity().InFlight == 0);
        }
    }

    private static void DetachAndRoots()
    {
        using var f = new Fixture(); var target = Target(); Bridge? bridge = new(); var complete = 0; var disposed = 0; var captured = 0;
        ExactHookBroker.Binding? first = null, second = null;
        Scope Capture(NativeOperationFrame frame) { captured++; return new(frame, () => { disposed++; Check(first!.ReadActivity().InFlight == 1); }); }
        void Complete(Scope scope, NativeOperationFrame frame, NativeOperationExitFlags flags)
        { Check(scope.Disposals == 0 && scope.Frame.Equals(frame) && flags == NativeOperationExitFlags.Returned); complete++; }
        first = Bind(f, target, bridge, Capture, Complete); second = Bind(f, target, bridge, Capture, Complete);
        var busyChecks = 0;
        bridge.Original = _ => { first.Dispose(); second.Dispose(); Check(first.ReadActivity() is { Known: true, InFlight: 1 }); busyChecks++; };
        bridge.AfterManagedExit = () => { Check(first.ReadActivity() is { Known: true, InFlight: 1 }); busyChecks++; };
        bridge.Invoke(Frame(target, bridge.Kind));
        Check(captured == 2 && complete == 2 && disposed == 2 && busyChecks == 2 && first.ReadActivity() is { Known: true, InFlight: 0 });
        var weak = new WeakReference(bridge); bridge = null; GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Check(weak.IsAlive, "Permanent entry lost its configured bridge after logical detach.");
        var retained = (Bridge)weak.Target!; retained.Original = null; retained.AfterManagedExit = null; retained.Invoke(Frame(target, retained.Kind));
        Check(retained.Originals == 2 && captured == 2 && complete == 2 && disposed == 2 && first.ReadActivity() is { Known: true, InFlight: 0 });
        using var rebound = f.Broker.BindNativeOperationObservation<Scope>(target, retained.Kind,
            () => throw new Exception("Rebind installed again"), _ => null, (_, _, _) => { }, (_, _) => { });
        Check(f.Engine.Prepares == 1 && f.Engine.Commits == 1 && retained.Configurations == 1); first.Dispose(); second.Dispose();
    }

    private static void CaptureDiagnostics()
    {
        foreach (var nested in new[] { false, true })
        {
            using var fixture = new Fixture();
            var target = Target(NativeOperationKind.Void0) with { Id = "TweenInfo.Play" };
            var outerTarget = Target(NativeOperationKind.Void0) with { Id = "TweenManager.Update" };
            var bridge = new Bridge(NativeOperationKind.Void0);
            var outerBridge = new Bridge(NativeOperationKind.Void0);
            var frame = Frame(target, bridge.Kind);
            var outerFrame = Frame(outerTarget, outerBridge.Kind);
            var faultRecords = 0;
            using var binding = Bind(fixture, target, bridge,
                _ => throw new IOException("Controlled source capture failure."),
                fault: (failedFrame, reason) =>
                {
                    Check(failedFrame.Equals(frame) && reason == "Operation capture failed.");
                    faultRecords++;
                });
            using var outerBinding = Bind(fixture, outerTarget, outerBridge);
            if (nested)
            {
                outerBridge.Original = _ => bridge.Invoke(frame);
                outerBridge.Invoke(outerFrame);
            }
            else bridge.Invoke(frame);
            Check(faultRecords == 1 && bridge.Originals == 1 && outerBridge.Originals == (nested ? 1 : 0)
                && binding.ReadActivity() is { Known: false, InFlight: 0 } && outerBinding.ReadActivity().Known,
                "The operation fault changed original, return or outer observer behavior.");
            var expected = "Operation capture failed; original is preserved. "
                + $"Target '{target.Id}', native sequence {frame.Sequence}, observed parent sequence {(nested ? outerFrame.Sequence : 0)}, "
                + $"caller return address 0x{frame.Caller.ToInt64():x}.";
            Check(fixture.Log.Messages.SequenceEqual(new[] { expected }),
                $"The fault report omitted the captured operation identity. Observed {string.Join(", ", fixture.Log.Messages)}");
        }
    }

    private static void IsolationAndReentry()
    {
        using var f = new Fixture(); var a = Target(); var b = Target(NativeOperationKind.Reference2); var ba = new Bridge(); var bb = new Bridge(NativeOperationKind.Reference2);
        var order = new List<string>(); var faults = 0; var scopes = new List<Scope>();
        Scope Capture(NativeOperationFrame frame) { order.Add("capture" + frame.Self); var value = new Scope(frame, () => order.Add("dispose" + frame.Self)); scopes.Add(value); return value; }
        void Complete(Scope scope, NativeOperationFrame frame, NativeOperationExitFlags flags)
        { Check(scope.Frame.Equals(frame) && scope.Disposals == 0 && flags == NativeOperationExitFlags.Returned); order.Add("complete" + frame.Self); }
        using var one = Bind(f, a, ba, Capture, Complete, (_, _) => Interlocked.Increment(ref faults));
        using var two = Bind(f, b, bb, Capture, Complete, (_, _) => Interlocked.Increment(ref faults));
        Task.Run(() => ba.Invoke(Frame(a, ba.Kind, 99))).GetAwaiter().GetResult();
        var foreign = Frame(a, ba.Kind, 88); foreign.Method = new(0x987); ba.Invoke(foreign);
        Check(scopes.Count == 0 && faults == 2 && one.Describe().SuppressedWrongThread == 1 && one.ReadActivity().Known);
        ba.Original = frame => {
            order.Add("original" + frame.Self);
            if (frame.Self != new IntPtr(11)) return;
            ba.Invoke(Frame(a, ba.Kind, 22)); bb.Invoke(Frame(b, bb.Kind, 33));
            var child = Frame(a, ba.Kind, 44); child.Method = new(0x654); ba.Invoke(child);
        };
        bb.Original = frame => order.Add("original" + frame.Self);
        ba.Invoke(Frame(a, ba.Kind));
        Check(order.SequenceEqual(new[] { "capture11", "original11", "capture22", "original22", "complete22", "dispose22",
            "capture33", "original33", "complete33", "dispose33", "original44", "complete11", "dispose11" })
            && scopes.Count == 3 && scopes.All(scope => scope.Disposals == 1) && faults == 3 && ba.Originals == 5 && bb.Originals == 1
            && one.ReadActivity() is { Known: true, InFlight: 0 } && two.ReadActivity() is { Known: true, InFlight: 0 });
        var noMethod = Frame(a, ba.Kind, 55); noMethod.Method = IntPtr.Zero; ba.Invoke(noMethod);
        Check(scopes.Count == 4 && scopes.Last().Disposals == 1 && ba.Originals == 6);
    }

    private static void InvalidFramesAndBrokenParents()
    {
        foreach (var field in new[] { "version", "size", "kind", "reserved", "self", "caller", "sequence0", "sequenceHigh", "arg0", "arg1" })
        {
            using var f = new Fixture(); var target = Target(NativeOperationKind.Void0); var bridge = new Bridge(NativeOperationKind.Void0); var captures = 0;
            using var binding = Bind(f, target, bridge, frame => { captures++; return new(frame); }); var frame = Frame(target, bridge.Kind);
            switch (field) {
                case "version": frame.Version = 2; break; case "size": frame.Size = 65; break; case "kind": frame.Kind = (NativeOperationKind)4; break;
                case "reserved": frame.Reserved = 1; break; case "self": frame.Self = IntPtr.Zero; break; case "caller": frame.Caller = IntPtr.Zero; break;
                case "sequence0": frame.Sequence = 0; break; case "sequenceHigh": frame.Sequence = 1UL << 63; break;
                case "arg0": frame.Arg0 = new(1); break; case "arg1": frame.Arg1 = new(1); break;
            }
            Check(!frame.IsValid); bridge.Invoke(frame);
            Check(captures == 0 && bridge.Originals == 1 && binding.ReadActivity() is { Known: false, InFlight: 0 });
        }
        foreach (var mismatch in new[] { "frame", "token", "flags" })
        {
            using var f = new Fixture(); var target = Target(); var bridge = new Bridge(); Scope? scope = null; var completes = 0;
            using var binding = Bind(f, target, bridge, frame => scope = new(frame), (_, _, _) => completes++);
            var frame = Frame(target, bridge.Kind); var token = bridge.Begin(frame);
            if (mismatch == "flags") bridge.End(frame, token, NativeOperationExitFlags.Returned | NativeOperationExitFlags.EnterFaulted);
            else {
                var changed = frame; changed.Arg0 = new(5);
                bridge.RawExit(mismatch == "frame" ? changed : frame, mismatch == "token" ? token + 1000 : token, NativeOperationExitFlags.Returned);
                Check(completes == 0 && scope!.Disposals == 0 && binding.ReadActivity() is { Known: false, InFlight: 1 });
                bridge.End(frame, token);
            }
            Check(completes == 1 && scope!.Disposals == 1 && binding.ReadActivity() is { Known: false, InFlight: 0 });
        }
        // Deliberately leave the broken outer record responsible on its dedicated thread.
        // Its ThreadStatic stack must never poison the subsequent ordinary test thread.
        Exception? failure = null; var worker = new Thread(() => {
            try {
                using var f = new Fixture(); var a = Target(); var b = Target(NativeOperationKind.Reference2); var ba = new Bridge(); var bb = new Bridge(NativeOperationKind.Reference2);
                Scope? outer = null, inner = null;
                using var one = Bind(f, a, ba, frame => outer = new(frame)); using var two = Bind(f, b, bb, frame => inner = new(frame));
                var af = Frame(a, ba.Kind); var bf = Frame(b, bb.Kind); var at = ba.Begin(af); var bt = bb.Begin(bf);
                ba.End(af, at); Check(outer!.Disposals == 0 && one.ReadActivity() is { Known: false, InFlight: 1 });
                bb.End(bf, bt); Check(inner!.Disposals == 1 && outer.Disposals == 0 && two.ReadActivity() is { Known: true, InFlight: 0 }
                    && one.ReadActivity() is { Known: false, InFlight: 1 });
            } catch (Exception error) { failure = error; }
        });
        worker.Start(); Check(worker.Join(5000), "Controlled parent mismatch thread did not finish."); Check(failure is null, failure?.ToString() ?? "");
    }

    private static void SkippedOriginal()
    {
        foreach (var kind in new[] { NativeOperationKind.Void0, NativeOperationKind.Reference1 })
        {
            using var f = new Fixture(); var target = Target(kind); var bridge = new Bridge(kind);
            Scope? scope = null; var flags = new List<NativeOperationExitFlags>(); var faults = 0;
            using var binding = Bind(f, target, bridge, frame => scope = new(frame),
                (value, frame, outcome) => { Check(ReferenceEquals(value, scope) && value.Disposals == 0 && value.Frame.Equals(frame)); flags.Add(outcome); },
                (_, _) => faults++);
            bridge.Skip(Frame(target, kind));
            Check(bridge.Originals == 0 && scope!.Disposals == 1 && faults == 0
                && flags.SequenceEqual(new[] { NativeOperationExitFlags.Skipped })
                && binding.Describe().NativeCalls == 1 && binding.Describe().OriginalCalls == 0
                && binding.ReadActivity() is { Known: true, InFlight: 0 });
            bridge.Invoke(Frame(target, kind));
            Check(bridge.Originals == 1 && scope!.Disposals == 1 && faults == 0
                && flags.SequenceEqual(new[] { NativeOperationExitFlags.Skipped, NativeOperationExitFlags.Returned })
                && binding.Describe().NativeCalls == 2 && binding.Describe().OriginalCalls == 1
                && binding.ReadActivity() is { Known: true, InFlight: 0 });
        }
    }
    private static void InvalidSkipFlags()
    {
        using var f = new Fixture(); var target = Target(); var bridge = new Bridge(); Scope? scope = null; var faults = 0;
        using var binding = Bind(f, target, bridge, frame => scope = new(frame), (_, _, _) => { }, (_, _) => faults++);
        bridge.Skip(Frame(target, bridge.Kind), NativeOperationExitFlags.Skipped | NativeOperationExitFlags.Returned);
        Check(bridge.Originals == 0 && scope!.Disposals == 1 && faults > 0
            && binding.Describe().OriginalCalls == 0 && binding.ReadActivity() is { Known: false, InFlight: 0 });
    }
    private sealed class OperationSourceOwner : INativeTweenSourceOwner
    {
        private readonly Action _verify;
        internal OperationSourceOwner(Action verify) => _verify = verify;
        public void VerifyTweenSourceOwner() => _verify();
    }
    private static void RootCredentials()
    {
        using var f = new Fixture(); var target = Target(); var bridge = new Bridge(); var frame = Frame(target, bridge.Kind);
        var identity = frame.Arg0; var frees = 0; using var root = new NativeRuntime.RootHandle(1, _ => frees++, _ => identity);
        using var foreign = new NativeRuntime.RootHandle(2, _ => frees++, _ => new(0xBAD));
        var owner = new OperationSourceOwner(f.Main.VerifyAccess); ExactHookBroker.OperationRootReceipt? receipt = null;
        Reject(() => ExactHookBroker.CaptureCurrentOperationRoot(frame, root, owner, production: false), "Before");
        using var binding = Bind(f, target, bridge, actual => {
            receipt = ExactHookBroker.CaptureCurrentOperationRoot(actual, root, owner, production: false);
            receipt.VerifyCurrent(); receipt.VerifyRetained();
            Check(!receipt.Production && ReferenceEquals(receipt.Root, root) && ReferenceEquals(receipt.Owner, owner));
            var changed = actual; ++changed.Sequence;
            Reject(() => ExactHookBroker.CaptureCurrentOperationRoot(changed, root, owner, production: false), "Before");
            Reject(() => ExactHookBroker.CaptureCurrentOperationRoot(actual, foreign, owner, production: false), "reference");
            return new(actual);
        }, (_, actual, _) => {
            receipt!.VerifyRetained(); Reject(receipt.VerifyCurrent, "Before");
            Reject(() => ExactHookBroker.CaptureCurrentOperationRoot(actual, root, owner, production: false), "Before");
        });
        bridge.Original = actual => {
            receipt!.VerifyRetained(); Reject(receipt.VerifyCurrent, "Before");
            Reject(() => ExactHookBroker.CaptureCurrentOperationRoot(actual, root, owner, production: false), "Before");
        };
        bridge.Invoke(frame);
        Check(binding.ReadActivity() is { Known: true, InFlight: 0 } && frees == 0 && receipt is not null);
        receipt!.VerifyRetained(); Reject(receipt.VerifyCurrent, "Before");
        identity = frame.Self; // Another legal frame input cannot replace the original rooted argument.
        Reject(receipt.VerifyRetained, "identity");
        Check(frees == 0);
    }
    private static void RootParentMask()
    {
        using var f = new Fixture(); var outerTarget = Target(); var maskTarget = Target(); var childTarget = Target();
        var outer = new Bridge(); var mask = new Bridge(); var child = new Bridge();
        var outerFrame = Frame(outerTarget, outer.Kind, 101); var maskFrame = Frame(maskTarget, mask.Kind, 202);
        var childFrame = Frame(childTarget, child.Kind, 303); var expected = outerFrame; var masked = false; var captures = 0;
        using var outerBinding = Bind(f, outerTarget, outer, actual => {
            ExactHookBroker.VerifyCurrentOperationParent(actual, null); return new(actual);
        });
        using var maskBinding = Bind(f, maskTarget, mask, _ => null);
        using var childBinding = Bind(f, childTarget, child, actual => {
            ++captures; ExactHookBroker.VerifyCurrentOperationParent(actual, expected);
            Reject(() => ExactHookBroker.VerifyCurrentOperationParent(actual, null), "parent");
            if (masked) Reject(() => ExactHookBroker.VerifyCurrentOperationParent(actual, outerFrame), "parent");
            return new(actual);
        });
        mask.Original = _ => { expected = maskFrame; masked = true; child.Invoke(childFrame); };
        outer.Original = _ => {
            mask.Invoke(maskFrame); expected = outerFrame; masked = false;
            var direct = Frame(childTarget, child.Kind, 304); child.Invoke(direct);
        };
        outer.Invoke(outerFrame);
        Check(captures == 2 && outerBinding.ReadActivity() is { Known: true, InFlight: 0 }
            && maskBinding.ReadActivity() is { Known: true, InFlight: 0 } && childBinding.ReadActivity() is { Known: true, InFlight: 0 });
        Reject(() => ExactHookBroker.VerifyCurrentOperationParent(childFrame, outerFrame), "Before");
    }
    private static void PermanentFailureAndActivity()
    {
        using (var f = new Fixture())
        {
            var target = Target(); var bridge = new Bridge(); using var binding = Bind(f, target, bridge);
            // Native transport can still own work after managed dispatch has already drained.
            bridge.InFlight = 7;
            Check(binding.ReadActivity() is { Known: true, InFlight: 7 }
                && f.Broker.ReadExactActivity(target) is { Known: true, InFlight: 7 });
            bridge.InFlight = 0; Check(binding.ReadActivity() is { Known: true, InFlight: 0 });
        }
        foreach (var stage in new[] { "prepare", "configure", "commit" })
        {
            using var f = new Fixture(); var target = Target(); var bridge = new Bridge(); var captures = 0;
            void Fail() => throw new IOException("Uncertain " + stage);
            if (stage == "prepare") f.Engine.PrepareAction = Fail;
            if (stage == "configure") bridge.ConfigureAction = Fail;
            if (stage == "commit") f.Engine.CommitAction = Fail;
            Reject(() => Bind(f, target, bridge, frame => { captures++; return new(frame); }), "Uncertain");
            Reject(() => Bind(f, target, bridge), "replace");
            var weak = new WeakReference(bridge); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Check(weak.IsAlive && f.Engine.Prepares == 1 && f.Engine.Commits == (stage == "commit" ? 1 : 0)
                && bridge.Configurations == (stage == "prepare" ? 0 : 1) && f.Engine.Installs == 0 && captures == 0
                && !f.Broker.ReadExactActivity(target).Known);
            if (bridge.Configured) { bridge.Invoke(Frame(target, bridge.Kind)); Check(captures == 0 && bridge.Originals == 1); }
        }
        foreach (var stage in new[] { "unknown", "negative", "throw" })
        {
            using var f = new Fixture(); var target = Target(); var bridge = new Bridge(); using var binding = Bind(f, target, bridge);
            bridge.Known = stage != "unknown"; bridge.InFlight = stage == "negative" ? -1 : 3; bridge.ThrowActivity = stage == "throw";
            Check(!binding.ReadActivity().Known); bridge.Known = true; bridge.InFlight = 0; bridge.ThrowActivity = false;
            Check(binding.ReadActivity() is { Known: false, InFlight: 0 });
        }
    }
}
