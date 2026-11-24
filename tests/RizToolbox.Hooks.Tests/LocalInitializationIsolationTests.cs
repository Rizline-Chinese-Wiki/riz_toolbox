using System.Reflection;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using Identity = RizToolbox.Runtime.Hooks.ExactHookBroker.LocalInitializationIdentity;
using Args = RizToolbox.Runtime.Hooks.ExactHookBroker.LocalInitializationArguments;

internal static class LocalInitializationIsolationTests
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Initialization sealed authorities reject unpinned images and generic ABI binding", Authorities),
        ("Ordinary Start getter Play Mode Tooltip preserve full arguments and one original", Ordinary),
        ("LOCAL getter claims its direct Start before validation and protects both native tails", LocalSuccess),
        ("Worker getter returns native null without host callbacks while its provisional Start tail stays protected", Worker),
        ("Validation original result and callback failures abort once without fallback or managed escape", Failures),
        ("Unrelated nested Start masks outer initialization and different run getters cannot steal a claimed parent", Nested),
        ("Independent LOCAL Play preserves both bool branches and ordinary child masks Mode authority", Play),
        ("Initialization detach retains current claims and blocks in-flight authority replacement", Detach),
        ("Complete native drain includes observers validation originals static calls and cleanup", Drain),
        ("Initialization frame overflow aborts at the bound instead of attributing an outer parent", Overflow),
        ("Frame creation failures become retained faults before Start Play or getter originals can run", FrameCreationFailure),
        ("Ordinary initialization children mask all terminal Progress Timeline authority and preserve native payloads", TerminalChildMask),
        ("Independent Timeline static originals remain visible to session drain and block owner replacement", StaticDrain),
        ("Typed native initialization prepares configures commits before publishing and shares ordinary Start observers", NativeTransport),
        ("Typed native originals unwind with exact ordinary failure identity and retain claimed LOCAL responsibility", NativeUnwind),
        ("Typed native initialization rejects managed upgrades and retains each uncertain publication phase", NativePublicationFailures),
        ("Each partial initialization install remains process owned and rejects a false complete rebind", PartialInstall),
        ("Native getter provenance exists only in its exact original window and ordinary children mask it", NativeGetterWindow)
    };
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("Sealed initialization invariant failed."); }
    private static T Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T error) { return error; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static readonly IntPtr Start = new(0x901), OrdinaryStart = new(0x902), Controller = new(0x903), Information = new(0x904),
        Progress = new(0x905), OrdinaryProgress = new(0x906), Result = new(0x907), Text = new(0x908), Method = new(0x909);
    private static readonly Identity Token = new("initialization.session", 31, Controller.ToInt64());
    private static void Authorities()
    {
        Throws<UnsupportedSignatureException>(() => ExactHookBroker.LocalInitializationIsolationTargets.Resolve(null!, IntPtr.Zero, "unsupported"));
        using var f = new Fixture();
        foreach (var target in new[] { f.Targets.Getter, f.Targets.Play, f.Targets.Tooltip })
            Throws<UnsupportedSignatureException>(() => f.Broker.Bind(target, _ => true, (_, _, _, _) => { }));
        Check(f.Engine.Installs == 8); // Existing four plus four new entries; Mode installed once.
        Throws<InvalidOperationException>(() => f.Bind());
    }
    private static void Ordinary()
    {
        using var f = new Fixture(); var payload = BitConverter.Int32BitsToSingle(unchecked((int)0xFFC00123));
        f.StartOriginal = receiver => {
            Check(receiver == OrdinaryStart); Check(f.Getter(new(0x999), Information, Method) == Result);
            f.Play(OrdinaryProgress, true, Method); f.Play(OrdinaryProgress, false, Method); f.Mode(-123, Method); f.Tooltip(IntPtr.Zero, payload, Method);
        };
        f.GetterOriginal = (controller, info, method) => { Check(controller == new IntPtr(0x999) && info == Information && method == Method); return Result; };
        f.PlayOriginal = (receiver, _, method) => Check(receiver == OrdinaryProgress && method == Method);
        f.TooltipOriginal = (text, offset, method) => Check(text == IntPtr.Zero && BitConverter.SingleToInt32Bits(offset) == BitConverter.SingleToInt32Bits(payload) && method == Method);
        f.StartCall(OrdinaryStart); Check(f.GetterCalls == 1 && f.PlayCalls == 2 && f.ModeCalls == 1 && f.TooltipCalls == 1 && f.BeforeCalls == 0);
        Check(f.Binding.Read(null) is { InFlight: 0, UnknownInFlight: 0, Known: true });
        var failure = new IOException("Ordinary native original fixture failure."); f.StartOriginal = _ => throw failure;
        Check(ReferenceEquals(failure, Throws<IOException>(() => f.StartCall(OrdinaryStart)))); Check(f.Faults == 0);
        f.GetterOriginal = (_, _, _) => throw failure;
        Check(ReferenceEquals(failure, Throws<IOException>(() => f.Getter(new(0x999), Information))));
        Check(f.Faults == 0 && f.Binding.Read(null) is { InFlight: 0, UnknownInFlight: 0, Known: true });
    }
    private static void LocalSuccess()
    {
        using var f = new Fixture();
        f.Before = args => { Check(args.Identity == Token && args.StartReceiver == Start && args.Controller == Controller && args.Information == Information
            && args.StartMethod == f.Targets.Start.MethodInfo && args.GetterMethod == f.Targets.Getter.MethodInfo); f.Mode(3); f.Tooltip(Text, -0f); return true; };
        f.After = (args, returned) => { Check(returned == Result); Check(f.Binding.Read(args.Identity).InFlight == 2); return true; };
        f.StartOriginal = _ => { Check(f.Getter(Controller, Information) == Result); f.Mode(3); f.Tooltip(Text, float.NaN); f.Instant(); };
        f.StartCall(Start); Check(f.GetterCalls == 1 && f.BeforeCalls == 1 && f.AfterCalls == 1 && f.ModeCalls == 0 && f.TooltipCalls == 0);
        Check(f.InstantCalls == 1 && f.Faults == 0 && f.Binding.Describe().ProvisionalClaims == 1);
        f.Mode(9); f.Tooltip(Text, 1); Check(f.ModeCalls == 1 && f.TooltipCalls == 1);
    }
    private static void Worker()
    {
        using var f = new Fixture(); f.StartOriginal = _ => { Check(f.Getter(Controller, Information) == IntPtr.Zero); f.Mode(3); f.Tooltip(Text, 1); };
        Task.Run(() => f.StartCall(Start)).GetAwaiter().GetResult();
        Check(f.BeforeCalls == 0 && f.AfterCalls == 0 && f.GetterCalls == 0 && f.ModeCalls == 0 && f.TooltipCalls == 0 && f.Faults == 1);
        Check(f.Binding.Read(null) is { InFlight: 0, UnknownInFlight: 0, Known: false });
    }
    private static void Failures()
    {
        foreach (var failure in Enumerable.Range(0, 6))
        {
            using var f = new Fixture();
            f.Before = _ => failure switch { 0 => false, 1 => throw new IOException("Before"), _ => true };
            f.GetterOriginal = (_, _, _) => failure switch { 2 => throw new IOException("Original"), 3 => IntPtr.Zero, _ => Result };
            f.After = (_, _) => failure switch { 4 => false, 5 => throw new IOException("After"), _ => true };
            f.StartOriginal = _ => { Check(f.Getter(Controller, Information) == IntPtr.Zero); f.Mode(3); f.Tooltip(Text, 1); };
            f.StartCall(Start); Check(f.GetterCalls == (failure < 2 ? 0 : 1)); Check(f.ModeCalls == 0 && f.TooltipCalls == 0 && f.Faults == 1);
        }
        using var absent = new Fixture(); Check(absent.Getter(Controller, Information) == IntPtr.Zero); Check(absent.GetterCalls == 0);
        using var mismatch = new Fixture(); mismatch.StartOriginal = _ => { Check(mismatch.Getter(Controller, Information, new(123)) == IntPtr.Zero); mismatch.Mode(3); };
        mismatch.StartCall(Start); Check(mismatch.GetterCalls == 0 && mismatch.ModeCalls == 0);
    }
    private static void Nested()
    {
        using var f = new Fixture();
        f.StartOriginal = receiver => {
            if (receiver == OrdinaryStart) { f.Mode(3); f.Tooltip(Text, 1); return; }
            Check(f.Getter(Controller, Information) == Result); f.StartCall(OrdinaryStart); f.Mode(3); f.Tooltip(Text, 1);
            Check(f.Getter(Controller, Information) == IntPtr.Zero); // Repeat never invokes again.
        };
        f.StartCall(Start); Check(f.GetterCalls == 1 && f.ModeCalls == 1 && f.TooltipCalls == 1 && f.Faults == 1);
        using var wrongRun = new Fixture(); var other = new Identity("other", 32, 0x111);
        wrongRun.Classifier = receiver => receiver == Controller ? Token : receiver == new IntPtr(0x111) ? other : null;
        wrongRun.StartOriginal = _ => { Check(wrongRun.Getter(Controller, Information) == Result); Check(wrongRun.Getter(new(0x111), Information) == IntPtr.Zero); wrongRun.Mode(3); };
        wrongRun.StartCall(Start); Check(wrongRun.GetterCalls == 1 && wrongRun.ModeCalls == 0);
        using var ordinaryGetter = new Fixture();
        ordinaryGetter.GetterOriginal = (receiver, _, _) => { if (receiver != Controller) { ordinaryGetter.Mode(3); ordinaryGetter.Tooltip(Text, 1); } return Result; };
        ordinaryGetter.StartOriginal = _ => { Check(ordinaryGetter.Getter(Controller, Information) == Result); Check(ordinaryGetter.Getter(new(0x111), Information) == Result); ordinaryGetter.Mode(3); };
        ordinaryGetter.StartCall(Start); Check(ordinaryGetter.ModeCalls == 1 && ordinaryGetter.TooltipCalls == 1);
    }
    private static void Play()
    {
        using var f = new Fixture();
        f.PlayOriginal = (receiver, _, _) => {
            if (receiver == Progress) { f.Play(OrdinaryProgress, false); f.Mode(3); f.Tooltip(Text, 1); }
            else f.Mode(3);
        };
        f.Play(Progress, true); f.Play(Progress, false); Check(f.PlayCalls == 4 && f.ModeCalls == 2 && f.TooltipCalls == 2);
        Task.Run(() => f.Play(Progress, true)).GetAwaiter().GetResult(); Check(f.PlayCalls == 6 && f.ModeCalls == 3 && f.BeforeCalls == 0);
        f.StartOriginal = _ => { Check(f.Getter(Controller, Information) == Result); f.Play(OrdinaryProgress, false); f.Mode(3); };
        f.StartCall(Start); Check(f.ModeCalls == 4); // Ordinary Play masks claimed Start; Start tail remains protected.
    }
    private static void Detach()
    {
        using var f = new Fixture(); f.StartOriginal = receiver => {
            Check(f.Getter(Controller, Information) == Result); f.Binding.Dispose(); Throws<InvalidOperationException>(() => f.Bind());
            f.Mode(3); f.Tooltip(Text, 1);
        };
        f.StartCall(Start); Check(f.ModeCalls == 0 && f.TooltipCalls == 0);
        using var rebound = f.Bind(); Check(f.Engine.Installs == 8);
    }
    private static void Drain()
    {
        using var f = new Fixture(); var observed = 0;
        using var observer = f.Broker.Bind(f.Targets.Start, _ => true, (_, _, _, _) => { Check(f.Binding.Read(null).InFlight >= 1); observed++; });
        f.Before = _ => { Check(f.Binding.Read(null).InFlight == 2); return true; };
        f.After = (_, _) => { Check(f.Binding.Read(null).InFlight == 2); return true; };
        f.StartOriginal = _ => { Check(f.Binding.Read(null).InFlight == 1); Check(f.Getter(Controller, Information) == Result); Check(f.Binding.Read(null).InFlight == 1); };
        f.StartCall(Start); Check(observed == 2);
        f.ModeOriginal = (_, _) => Check(f.Binding.Read(null).InFlight >= 1);
        f.TooltipOriginal = (_, _, _) => Check(f.Binding.Read(null).InFlight == 1);
        f.PlayOriginal = (_, _, _) => { Check(f.Binding.Read(null).InFlight == 1); f.Mode(3); };
        f.Mode(3); f.Tooltip(Text, 1); f.Play(OrdinaryProgress, true); Check(f.Binding.Read(null).InFlight == 0);
    }
    private static void Overflow()
    {
        using var f = new Fixture(); var calls = 0;
        f.StartOriginal = _ => { calls++; f.StartCall(OrdinaryStart); };
        f.StartCall(OrdinaryStart); Check(calls == 32 && f.Faults == 1 && f.Binding.Read(null).InFlight == 0 && f.Binding.Read(null).UnknownInFlight == 0);
    }
    private static void FrameCreationFailure()
    {
        foreach (var play in new[] { false, true })
        {
            using var f = new Fixture(); var originals = 0;
            f.StartOriginal = _ => originals++; f.PlayOriginal = (_, _, _) => originals++;
            f.Binding.SetFrameCreationProbe(() => { Check(f.Binding.Read(null).InFlight == 1); throw new IOException("Injected frame allocation failure."); });
            if (play) f.Play(OrdinaryProgress, false); else f.StartCall(OrdinaryStart);
            Check(originals == 0 && f.Faults == 1 && f.Binding.Read(null) is { InFlight: 0, UnknownInFlight: 0, Known: false });
        }
        using var claimed = new Fixture();
        claimed.StartOriginal = _ => {
            claimed.Binding.SetFrameCreationProbe(() => throw new IOException("Injected getter frame allocation failure."));
            Check(claimed.Getter(Controller, Information) == IntPtr.Zero); claimed.Mode(3); claimed.Tooltip(Text, 1);
        };
        claimed.StartCall(Start); Check(claimed.GetterCalls == 0 && claimed.BeforeCalls == 0 && claimed.ModeCalls == 0 && claimed.TooltipCalls == 0 && claimed.Faults == 1);
        Check(claimed.Binding.Read(null) is { InFlight: 0, UnknownInFlight: 0, Known: false });
    }
    private static void TerminalChildMask()
    {
        using var f = new Fixture(); var handle = new ExactHookBroker.TimelineEventHandle(0xFEDCBA9876543210UL);
        var payload = BitConverter.Int32BitsToSingle(unchecked((int)0xFFC01234)); var endCalls = 0;
        f.InstantOriginal = (title, description, icon, priority, offset, clip, method) => {
            Check(title == Text && description == Information && icon == Result && priority == uint.MaxValue && clip == int.MinValue && method == Method
                && BitConverter.SingleToInt32Bits(offset) == BitConverter.SingleToInt32Bits(payload)); return handle;
        };
        f.EndOriginal = (value, offset, method) => { Check(value.Value == handle.Value && method == Method
            && BitConverter.SingleToInt32Bits(offset) == BitConverter.SingleToInt32Bits(payload)); endCalls++; };
        void ChildTail() { Check(f.InstantPayload(Text, Information, Result, uint.MaxValue, payload, int.MinValue, Method).Value == handle.Value); f.End(handle, payload, Method); }
        f.StartOriginal = _ => ChildTail(); f.GetterOriginal = (_, _, _) => { ChildTail(); return Result; }; f.PlayOriginal = (_, _, _) => ChildTail();
        f.UpdateOriginal = (_, _) => {
            Check(f.ProgressBinding.MarkTerminal(new(Token.SessionId, Token.RunGeneration, Token.ControllerIdentity, Progress.ToInt64()))
                == ExactHookBroker.LocalProgressTerminalMark.MarkedCurrentContext);
            f.StartCall(OrdinaryStart); Check(f.Getter(new(0x999), Information) == Result); f.Play(OrdinaryProgress, false);
            Check(f.InstantPayload(Text, Information, Result, uint.MaxValue, payload, int.MinValue, Method).Value == 0); f.End(handle, payload, Method);
        };
        f.Update(Progress); Check(f.InstantCalls == 3 && endCalls == 3 && f.ProgressBinding.Describe().SuppressedTimelineCalls == 2);
    }
    private static void StaticDrain()
    {
        foreach (var instant in new[] { false, true })
        {
            using var f = new Fixture(); using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            void Hold() { entered.Set(); Check(release.Wait(TimeSpan.FromSeconds(10))); }
            f.InstantOriginal = (_, _, _, _, _, _, _) => { Hold(); return new(123); }; f.EndOriginal = (_, _, _) => Hold();
            var worker = Task.Run(() => { if (instant) f.Instant(); else f.End(new(123), 0, Method); });
            Check(entered.Wait(TimeSpan.FromSeconds(10)));
            try
            {
                Check(f.ProgressBinding.Read(new(Token.SessionId, Token.RunGeneration, Token.ControllerIdentity, Progress.ToInt64())).InFlight == 1);
                Check(f.ProgressBinding.Describe().InFlight == 1);
                f.Binding.Dispose(); f.ProgressBinding.Dispose();
                Throws<InvalidOperationException>(() => f.Broker.BindLocalProgressIsolation(f.ProgressTargets, _ => null));
            }
            finally { release.Set(); worker.GetAwaiter().GetResult(); }
            Check(f.ProgressBinding.Describe().InFlight == 0);
        }
    }
    private static void PartialInstall()
    {
        foreach (var phase in Enumerable.Range(0, 4))
        {
            using var f = new Fixture(bind: false);
            var target = new[] { f.Targets.Tooltip, f.Targets.Getter, f.Targets.Play, f.Targets.Start }[phase]; f.Engine.Fail = target.Address;
            var error = Throws<ExactHookBroker.LocalInitializationIsolationCreationException>(() => f.Bind());
            Check(!error.Responsibility.IsActive && !error.Responsibility.Read(null).Known);
            Throws<Exception>(() => f.Bind()); // Unconfirmed transport cannot become a successful retry.
            if (phase >= 1) Check(f.Getter(new(0x999), Information) == IntPtr.Zero || phase > 1);
            if (phase == 3) { f.StartCall(OrdinaryStart); Check(error.Responsibility.Read(null).InFlight == 0); }
        }
    }
    private static void NativeTransport()
    {
        using var f = new Fixture(native: true); var order = new List<string>();
        Check(f.Engine.Prepares == 2 && f.Engine.Commits == 2 && f.Engine.Installs == 6);
        using var observer = f.Broker.Bind(f.Targets.Start, _ => true, (phase, _, _, _) => {
            Check(f.Binding.Read().InFlight >= 1); order.Add(phase.ToString());
        });
        f.Before = _ => { Check(f.Binding.Read().InFlight == 2); order.Add("validate-before"); return true; };
        f.After = (_, returned) => { Check(returned == Result && f.Binding.Read().InFlight == 2); order.Add("validate-after"); return true; };
        f.StartOriginal = _ => { order.Add("start"); Check(f.Getter(Controller, Information) == Result); f.Mode(3); f.Tooltip(Text, 1); };
        f.GetterOriginal = (_, _, _) => { order.Add("getter"); return Result; };
        f.StartCall(Start);
        Check(order.SequenceEqual(new[] { "Before", "start", "validate-before", "getter", "validate-after", "After" }));
        Check(f.ModeCalls == 0 && f.TooltipCalls == 0 && f.Faults == 0 && f.Binding.Read() is { InFlight: 0, Known: true });
        var startBridge = f.Engine.Bridges.Values.Single(bridge => bridge.Kind == NativeInitializationKind.Start);
        startBridge.BeforeEnter = () => Check(f.Binding.Read().InFlight == 1); startBridge.AfterExit = () => Check(f.Binding.Read().InFlight == 1);
        f.StartOriginal = _ => { }; f.StartCall(OrdinaryStart); Check(f.Binding.Read().InFlight == 0);
        f.GetterOriginal = (_, _, _) => Result; Check(f.Getter(new(0x999), Information, Method) == Result);
        f.StartOriginal = _ => { Check(f.Getter(Controller, Information) == IntPtr.Zero); f.Mode(3); f.Tooltip(Text, 1); };
        var before = f.BeforeCalls; Task.Run(() => f.StartCall(Start)).GetAwaiter().GetResult();
        Check(f.BeforeCalls == before && f.Faults == 1 && f.Binding.Read() is { InFlight: 0, Known: false });
    }
    private static void NativeGetterWindow()
    {
        bool Current() => ExactHookBroker.HasCurrentNativeGetter(Token.SessionId, Token.RunGeneration, Controller.ToInt64(), Information.ToInt64());
        using (var f = new Fixture(native: true))
        {
            Check(!Current());
            f.Before = _ => { Check(!Current()); return true; };
            f.After = (_, _) => { Check(!Current()); return true; };
            f.GetterOriginal = (receiver, _, _) => {
                if (receiver != Controller) { Check(!Current()); return Result; }
                Check(Current());
                Check(!ExactHookBroker.HasCurrentNativeGetter("foreign", Token.RunGeneration, Controller.ToInt64(), Information.ToInt64()));
                Check(!ExactHookBroker.HasCurrentNativeGetter(Token.SessionId, Token.RunGeneration + 1, Controller.ToInt64(), Information.ToInt64()));
                Check(!ExactHookBroker.HasCurrentNativeGetter(Token.SessionId, Token.RunGeneration, Controller.ToInt64(), Information.ToInt64() + 1));
                Check(!Task.Run(Current).GetAwaiter().GetResult());
                Check(f.Getter(new(0x999), Information) == Result); Check(Current()); return Result;
            };
            f.StartOriginal = _ => { Check(!Current()); Check(f.Getter(Controller, Information) == Result); Check(!Current()); };
            f.StartCall(Start); Check(!Current() && f.Faults == 0);
        }
        using (var f = new Fixture())
        {
            f.GetterOriginal = (_, _, _) => { Check(!Current()); return Result; };
            f.StartOriginal = _ => { Check(f.Getter(Controller, Information) == Result); };
            f.StartCall(Start); Check(!Current() && f.Faults == 0);
        }
    }
    private static void NativeUnwind()
    {
        using var f = new Fixture(native: true); var error = new IOException("Actual original mock unwind.");
        f.StartOriginal = _ => throw error;
        Check(ReferenceEquals(error, Throws<IOException>(() => f.StartCall(OrdinaryStart))));
        f.GetterOriginal = (_, _, _) => throw error;
        Check(ReferenceEquals(error, Throws<IOException>(() => f.Getter(new(0x999), Information))));
        Check(f.Faults == 0 && f.Binding.Read() is { InFlight: 0, UnknownInFlight: 0, Known: true });
        f.StartOriginal = _ => { f.Getter(Controller, Information); };
        Check(ReferenceEquals(error, Throws<IOException>(() => f.StartCall(Start))));
        Check(f.GetterCalls == 2 && f.AfterCalls == 0 && f.Faults == 2 && f.Binding.Read() is { InFlight: 0, UnknownInFlight: 0, Known: false });
        var mode = f.ModeCalls; f.Mode(3); Check(f.ModeCalls == mode + 1); // Exact TLS frames have left after native unwind.
    }
    private static void NativePublicationFailures()
    {
        using (var managed = new Fixture(bind: false))
        {
            using var observation = managed.Broker.Bind(managed.Targets.Start, _ => true, (_, _, _, _) => { });
            Throws<InvalidOperationException>(() => managed.Bind(native: true));
            Check(managed.Engine.Prepares == 0);
        }
        foreach (var phase in new[] { 1, 2, 3 })
        {
            using var f = new Fixture(bind: false, native: true); f.Engine.FailNativePhase = phase;
            var failure = Throws<ExactHookBroker.LocalInitializationIsolationCreationException>(() => f.Bind(native: true));
            Check(!failure.Responsibility.IsActive && !failure.Responsibility.Read().Known);
            var prepares = f.Engine.Prepares; Throws<Exception>(() => f.Bind(native: true)); Check(f.Engine.Prepares == prepares);
            if (phase == 3) Check(f.Getter(new(0x999), Information) == IntPtr.Zero);
        }
        using var unknown = new Fixture(native: true);
        unknown.Engine.Bridges.Values.First().ReadUnknown = true;
        Check(!unknown.Binding.Read().Known && unknown.Faults > 0);
    }
    private sealed class Engine : INativeDetourEngine, IPreparedNativeDetourEngine
    {
        internal readonly Dictionary<IntPtr, Delegate> Originals = new(), Replacements = new(); internal int Installs; internal IntPtr Fail;
        internal readonly Dictionary<IntPtr, MockBridge> Bridges = new();
        private readonly Dictionary<IntPtr, MockBridge> _prepared = new();
        internal int Prepares, Commits, FailNativePhase;
        public Delegate Install(VerifiedHookTarget target, Delegate replacement)
        { Installs++; Replacements.Add(target.Address, replacement); if (target.Address == Fail) throw new IOException("Visible installation is unknown."); return Originals[target.Address]; }
        public IntPtr Prepare(VerifiedHookTarget target, IntPtr replacement)
        {
            Prepares++; Check(!Replacements.ContainsKey(target.Address));
            var bridge = Bridges[replacement]; _prepared.Add(target.Address, bridge);
            if (FailNativePhase == 1) throw new IOException("Prepare outcome unknown."); return new(target.Address.ToInt64() + 0x90);
        }
        public void Commit(VerifiedHookTarget target)
        {
            Commits++; var bridge = _prepared[target.Address]; Check(bridge.Configured);
            Replacements.Add(target.Address, bridge.Replacement); if (FailNativePhase == 3) throw new IOException("Commit outcome unknown.");
        }
    }
    private sealed class MockBridge : INativeInitializationContextBridge
    {
        private readonly Engine _engine; private readonly VerifiedHookTarget _target;
        private NativeInitializationEnter _enter = null!; private NativeInitializationExit _exit = null!;
        private long _inFlight; internal bool Configured, ReadUnknown; internal Action? BeforeEnter, AfterExit;
        public NativeInitializationKind Kind { get; } public IntPtr EntryAddress { get; }
        internal Delegate Replacement { get; }
        internal MockBridge(Engine engine, VerifiedHookTarget target, NativeInitializationKind kind)
        {
            _engine = engine; _target = target; Kind = kind; EntryAddress = new(target.Address.ToInt64() + 0x80);
            Replacement = kind == NativeInitializationKind.Start
                ? new ExactHookBroker.InstanceVoid((self, method) => Invoke(self, IntPtr.Zero, method))
                : new ExactHookBroker.LocalInitializationReference(Invoke);
        }
        public void Configure(IntPtr original, NativeInitializationEnter enter, NativeInitializationExit exit, Action<string> fault)
        {
            Check(original == new IntPtr(_target.Address.ToInt64() + 0x90) && !Configured);
            if (_engine.FailNativePhase == 2) throw new IOException("Configuration outcome unknown.");
            _enter = enter; _exit = exit; Configured = true;
        }
        private IntPtr Invoke(IntPtr self, IntPtr information, IntPtr method)
        {
            Interlocked.Increment(ref _inFlight); nuint token = 0; var flags = NativeInitializationExitFlags.Abnormal; var returned = IntPtr.Zero;
            try
            {
                BeforeEnter?.Invoke();
                if (_enter(self, information, method, out token) == 0) { flags = NativeInitializationExitFlags.Skipped; return IntPtr.Zero; }
                if (Kind == NativeInitializationKind.Start) ((ExactHookBroker.InstanceVoid)_engine.Originals[_target.Address])(self, method);
                else returned = ((ExactHookBroker.LocalInitializationReference)_engine.Originals[_target.Address])(self, information, method);
                flags = NativeInitializationExitFlags.Returned;
            }
            finally
            {
                try { returned = _exit(self, information, method, returned, token, flags); AfterExit?.Invoke(); }
                finally { Interlocked.Decrement(ref _inFlight); }
            }
            return (flags & NativeInitializationExitFlags.Returned) != 0 ? returned : IntPtr.Zero;
        }
        public NativeInitializationActivity ReadActivity() => new(Interlocked.Read(ref _inFlight), Configured && !ReadUnknown);
    }
    private sealed class Fixture : IDisposable
    {
        private static long NextAddress = 0x8D0000000;
        internal readonly MainThreadDispatcher Main = new(); internal readonly Engine Engine = new(); internal readonly ExactHookBroker Broker;
        internal readonly ExactHookBroker.LocalProgressIsolationTargets ProgressTargets;
        internal readonly ExactHookBroker.LocalInitializationIsolationTargets Targets;
        internal readonly ExactHookBroker.LocalProgressIsolationBinding ProgressBinding;
        internal ExactHookBroker.LocalInitializationIsolationBinding Binding = null!;
        internal Func<IntPtr, Identity?> Classifier = receiver => receiver == Controller ? Token : null;
        internal Func<Args, bool> Before = _ => true; internal Func<Args, IntPtr, bool> After = (_, _) => true;
        internal Action<IntPtr> StartOriginal = _ => { }; internal Func<IntPtr, IntPtr, IntPtr, IntPtr> GetterOriginal = (_, _, _) => Result;
        internal Action<IntPtr, IntPtr> UpdateOriginal = (_, _) => { };
        internal Action<IntPtr, bool, IntPtr> PlayOriginal = (_, _, _) => { }; internal Action<int, IntPtr> ModeOriginal = (_, _) => { };
        internal Action<IntPtr, float, IntPtr> TooltipOriginal = (_, _, _) => { };
        internal ExactHookBroker.StaticTimelineInstantaneous InstantOriginal = (_, _, _, _, _, _, _) => new(123);
        internal ExactHookBroker.StaticTimelineEndRange EndOriginal = (_, _, _) => { };
        internal int GetterCalls, PlayCalls, ModeCalls, TooltipCalls, InstantCalls, BeforeCalls, AfterCalls, Faults;
        internal Fixture(bool bind = true, bool native = false)
        {
            Broker = new(Engine, Main, new Logger()); var address = Interlocked.Add(ref NextAddress, 0x1000);
            VerifiedHookTarget Target(int offset, ExactHookAbi abi) => new("initialization.fixture." + offset, new(address + offset), new(address + offset + 1), new(0x4321), abi,
                abi == ExactHookAbi.LocalInitializationReference1 ? new IntPtr(0x555) : IntPtr.Zero);
            var progress = Target(0, ExactHookAbi.InstanceVoid); var mode = Target(0x100, ExactHookAbi.StaticTimelineModeVoid);
            var instant = Target(0x200, ExactHookAbi.StaticTimelineInstantaneousHandle); var end = Target(0x300, ExactHookAbi.StaticTimelineEndRangeVoid);
            var start = Target(0x400, ExactHookAbi.InstanceVoid); var getter = Target(0x500, ExactHookAbi.LocalInitializationReference1);
            var play = Target(0x600, ExactHookAbi.LocalProgressPlayBooleanVoid); var tooltip = Target(0x700, ExactHookAbi.StaticTimelineTooltipVoid);
            ProgressTargets = (ExactHookBroker.LocalProgressIsolationTargets)typeof(ExactHookBroker.LocalProgressIsolationTargets)
                .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, Enumerable.Repeat(typeof(VerifiedHookTarget), 4).ToArray(), null)!
                .Invoke(new object[] { progress, mode, instant, end });
            Targets = (ExactHookBroker.LocalInitializationIsolationTargets)typeof(ExactHookBroker.LocalInitializationIsolationTargets)
                .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, Enumerable.Repeat(typeof(VerifiedHookTarget), 5).ToArray(), null)!
                .Invoke(new object[] { start, getter, play, mode, tooltip });
            Engine.Originals[progress.Address] = new ExactHookBroker.InstanceVoid((self, method) => UpdateOriginal(self, method));
            Engine.Originals[mode.Address] = new ExactHookBroker.StaticTimelineMode((value, method) => { ModeCalls++; ModeOriginal(value, method); });
            Engine.Originals[instant.Address] = new ExactHookBroker.StaticTimelineInstantaneous((title, description, icon, priority, offset, clip, method) =>
            { InstantCalls++; return InstantOriginal(title, description, icon, priority, offset, clip, method); });
            Engine.Originals[end.Address] = new ExactHookBroker.StaticTimelineEndRange((handle, offset, method) => EndOriginal(handle, offset, method));
            Engine.Originals[start.Address] = new ExactHookBroker.InstanceVoid((self, _) => StartOriginal(self));
            Engine.Originals[getter.Address] = new ExactHookBroker.LocalInitializationReference((self, info, method) => { GetterCalls++; return GetterOriginal(self, info, method); });
            Engine.Originals[play.Address] = new ExactHookBroker.LocalProgressPlay((self, value, method) => { PlayCalls++; PlayOriginal(self, value, method); });
            Engine.Originals[tooltip.Address] = new ExactHookBroker.StaticTimelineTooltip((text, offset, method) => { TooltipCalls++; TooltipOriginal(text, offset, method); });
            ProgressBinding = Broker.BindLocalProgressIsolation(ProgressTargets, receiver => receiver == Progress
                ? new(Token.SessionId, Token.RunGeneration, Token.ControllerIdentity, Progress.ToInt64()) : null);
            if (bind) Binding = Bind(native);
        }
        internal ExactHookBroker.LocalInitializationIsolationBinding Bind(bool native = false) => Broker.BindLocalInitializationIsolation(ProgressBinding, Targets, receiver => Classifier(receiver),
            new(args => { BeforeCalls++; return Before(args); }, (args, result) => { AfterCalls++; return After(args, result); }, (_, _) => Faults++),
            native ? CreateNativeBridge : null);
        private INativeInitializationContextBridge CreateNativeBridge(NativeInitializationKind kind)
        {
            var bridge = new MockBridge(Engine, kind == NativeInitializationKind.Start ? Targets.Start : Targets.Getter, kind);
            Engine.Bridges.Add(bridge.EntryAddress, bridge); return bridge;
        }
        internal void StartCall(IntPtr receiver) => ((ExactHookBroker.InstanceVoid)Engine.Replacements[Targets.Start.Address])(receiver, Targets.Start.MethodInfo);
        internal void Update(IntPtr receiver) => ((ExactHookBroker.InstanceVoid)Engine.Replacements[ProgressTargets.Progress.Address])(receiver, ProgressTargets.Progress.MethodInfo);
        internal IntPtr Getter(IntPtr controller, IntPtr information, IntPtr? method = null)
            => ((ExactHookBroker.LocalInitializationReference)Engine.Replacements[Targets.Getter.Address])(controller, information, method ?? Targets.Getter.MethodInfo);
        internal void Play(IntPtr receiver, bool play, IntPtr? method = null)
            => ((ExactHookBroker.LocalProgressPlay)Engine.Replacements[Targets.Play.Address])(receiver, play, method ?? Targets.Play.MethodInfo);
        internal void Mode(int mode, IntPtr? method = null) => ((ExactHookBroker.StaticTimelineMode)Engine.Replacements[Targets.Mode.Address])(mode, method ?? Targets.Mode.MethodInfo);
        internal void Tooltip(IntPtr text, float offset, IntPtr? method = null) => ((ExactHookBroker.StaticTimelineTooltip)Engine.Replacements[Targets.Tooltip.Address])(text, offset, method ?? Targets.Tooltip.MethodInfo);
        internal void Instant() => ((ExactHookBroker.StaticTimelineInstantaneous)Engine.Replacements[ProgressTargets.Instantaneous.Address])(Text, Text, Text, 1, 0, 0, ProgressTargets.Instantaneous.MethodInfo);
        internal ExactHookBroker.TimelineEventHandle InstantPayload(IntPtr title, IntPtr description, IntPtr icon, uint priority, float offset, int clip, IntPtr method)
            => ((ExactHookBroker.StaticTimelineInstantaneous)Engine.Replacements[ProgressTargets.Instantaneous.Address])(title, description, icon, priority, offset, clip, method);
        internal void End(ExactHookBroker.TimelineEventHandle handle, float offset, IntPtr method)
            => ((ExactHookBroker.StaticTimelineEndRange)Engine.Replacements[ProgressTargets.EndRange.Address])(handle, offset, method);
        public void Dispose() { Binding?.Dispose(); ProgressBinding.Dispose(); Main.Dispose(); }
    }
    private sealed class Logger : RizToolbox.Sdk.ILogger
    { public void Log(RizToolbox.Sdk.LogLevel level, string message, Exception? exception = null) { } }
}
