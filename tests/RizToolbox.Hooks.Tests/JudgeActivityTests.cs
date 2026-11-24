using RizToolbox.Runtime.Hooks;

internal static class JudgeActivityTests
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Judge activity lookup requires the exact physical target and does not install missing hooks", ExactLookup),
        ("Native Judge activity includes worker original windows after logical detach", NativeWorker),
        ("Managed inner Judge activity includes nested original and final context cleanup", Inner),
        ("Hold Boolean activity covers original and context release while preserving AL", Hold),
        ("Judge context faults remain unknown after physical activity returns to zero", Faults),
        ("Invalid native statistics and read failures cannot recover into a successful drain", InvalidStatistics)
    };
    private static void Check(bool value, string message = "Judge activity check failed")
    { if (!value) throw new InvalidOperationException(message); }
    private static VerifiedHookTarget Target(ExactHookAbi abi = ExactHookAbi.InstanceInt32SingleVoid)
        => new("judge.activity.fixture", new(Interlocked.Add(ref Addresses.Next, 16)), new(0x123), new(0x321), abi);
    private static NativeJudgeFrame Frame() => new() { AbiVersion = 1, ByteSize = 0x1a0, Rcx = 0x777,
        EntryStackPointer = 0x1238, Rsp = 0x1238, ReturnAddress = 0x12345678 };
    private sealed class Context : IDisposable
    { private readonly Action? _release; internal Context(Action? release = null) => _release = release; public void Dispose() => _release?.Invoke(); }
    private static void ExactLookup()
    {
        using var f = new Fixture(); var target = Target();
        Check(!f.Broker.ReadExactActivity(target).Known && f.Engine.Installs == 0);
        f.Engine.Original = new ExactHookBroker.InstanceJudge((_, _, _, _) => { });
        using var binding = f.Broker.Bind(target, _ => true, (_, _, _, _) => { });
        Check(f.Broker.ReadExactActivity(target) is { Known: true, InFlight: 0 });
        Check(!f.Broker.ReadExactActivity(target, true).Known);
        foreach (var alias in new[] { target with { MethodInfo = new(444) }, target with { DeclaringClass = new(555) },
            target with { Abi = ExactHookAbi.InstanceEnum32SingleVoid } }) Check(!f.Broker.ReadExactActivity(alias).Known);
        binding.Dispose(); Check(binding.ReadActivity().Known && f.Broker.ReadExactActivity(target).Known);
        Check(f.Engine.Installs == 1);
        var nativeTarget = Target(); var bridge = new NativeBridge();
        using var nativeBinding = f.Broker.BindNativeJudgeContext<Context>(nativeTarget, () => bridge, _ => null, (_, _) => { });
        try {
            f.Broker.BindNativeJudgeContext<Context>(nativeTarget with { MethodInfo = new(456) },
                () => throw new Exception("Alias created another native bridge"), _ => null, (_, _) => { });
            throw new Exception("Foreign native method alias was accepted");
        } catch (RizToolbox.Runtime.UnsupportedSignatureException) { }
    }
    private static void NativeWorker()
    {
        using var f = new Fixture(); var target = Target(); var bridge = new NativeBridge();
        using var entered = new ManualResetEventSlim(); using var resume = new ManualResetEventSlim();
        bridge.Original = _ => { entered.Set(); if (!resume.Wait(10000)) throw new TimeoutException(); };
        using var binding = f.Broker.BindNativeJudgeContext<Context>(target, () => bridge,
            _ => throw new Exception("Worker must not capture"), (_, _) => { });
        var worker = Task.Run(() => bridge.Invoke(Frame()));
        try {
            Check(entered.Wait(5000)); Check(binding.ReadActivity() is { Known: true, InFlight: 1 });
            binding.Dispose(); Check(f.Broker.ReadExactActivity(target, true) is { Known: true, InFlight: 1 });
        } finally { resume.Set(); Check(worker.Wait(5000)); }
        Check(binding.ReadActivity() is { Known: true, InFlight: 0 });
    }
    private static void Inner()
    {
        using var f = new Fixture(); var target = Target(ExactHookAbi.InstanceEnum32SingleVoid);
        ExactHookBroker.Binding? binding = null; var original = 0; var releases = 0;
        f.Engine.Original = new ExactHookBroker.InstanceJudge((s, j, d, m) => {
            Check(binding!.ReadActivity() is { Known: true, InFlight: > 0 });
            if (++original == 1) f.Engine.InvokeJudge(s, j, d, m);
        });
        binding = f.Broker.BindRetainedJudgeObservation(target, _ => true,
            (_, _, _) => new Context(() => { Check(binding!.ReadActivity().InFlight == 1); releases++; }), (_, _) => { });
        f.Engine.InvokeJudge(new(1), 2, 3, target.MethodInfo);
        Check(original == 2 && releases == 1 && binding.ReadActivity() is { Known: true, InFlight: 0 }); binding.Dispose();
    }
    private static void Hold()
    {
        using var f = new Fixture(); var target = Target(ExactHookAbi.InstanceBooleanReturn0);
        ExactHookBroker.Binding? binding = null; var releases = 0;
        f.Engine.Original = new ExactHookBroker.InstanceBooleanReturn0((_, _) => { Check(binding!.ReadActivity().InFlight == 1); return 0x80; });
        binding = f.Broker.BindRetainedBooleanObservation(target, _ => true,
            _ => new Context(() => { Check(binding!.ReadActivity().InFlight == 1); releases++; }), _ => { });
        Check(((ExactHookBroker.InstanceBooleanReturn0)f.Engine.Replacement!)(new(1), target.MethodInfo) == 0x80);
        Check(releases == 1 && binding.ReadActivity() is { Known: true, InFlight: 0 }); binding.Dispose();
    }
    private static void Faults()
    {
        foreach (var stage in new[] { "capture", "after", "release" })
        {
            using var f = new Fixture(); var target = Target(); var bridge = new NativeBridge(); var originals = 0;
            bridge.Original = _ => originals++;
            using var binding = f.Broker.BindNativeJudgeContext(target, () => bridge,
                _ => stage == "capture" ? throw new IOException("capture") : new Context(() => { if (stage == "release") throw new IOException("release"); }),
                (_, _) => { if (stage == "after") throw new IOException("after"); });
            bridge.Invoke(Frame()); Check(originals == 1 && binding.ReadActivity() is { Known: false, InFlight: 0 });
            bridge.Invoke(Frame()); Check(originals == 2 && !binding.ReadActivity().Known);
        }
    }
    private static void InvalidStatistics()
    {
        foreach (var stage in new[] { "unknown", "negative", "throw" })
        {
            using var f = new Fixture(); var target = Target(); var bridge = new NativeBridge();
            using var binding = f.Broker.BindNativeJudgeContext<Context>(target, () => bridge, _ => null, (_, _) => { });
            bridge.ActivityKnown = stage != "unknown"; bridge.InFlight = stage == "negative" ? -1 : 0; bridge.ThrowActivity = stage == "throw";
            Check(!binding.ReadActivity().Known);
            bridge.ActivityKnown = true; bridge.InFlight = 0; bridge.ThrowActivity = false;
            Check(!binding.ReadActivity().Known && !f.Broker.ReadExactActivity(target, true).Known);
        }
    }
}
