using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using Command = RizToolbox.Runtime.Hooks.ExactHookBroker.LocalRunCommand;
using Decision = RizToolbox.Runtime.Hooks.ExactHookBroker.LocalRunCommandDecision;

internal static class LocalRunCommandPolicyTests
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("LOCAL command authorities reject unknown operations and builds before native access", Authority),
        ("Each LOCAL command preserves ordinary observers ABI and exception identity but never falls back after claim", OrdinaryAndClaimed),
        ("Each LOCAL command rejects classifier faults unknown decisions and wrong MethodInfo before stock", InvalidClassification),
        ("Each LOCAL command keeps reentrant claims protected through detach and isolates worker entries", ReentryAndWorker),
        ("Each LOCAL command shares one exact patch and rejects competing coordinators and method aliases", ExclusiveOwnership),
        ("LOCAL Retry Back and LevelOver commands retain independent simultaneous policy scopes", CrossCommand),
        ("LOCAL command binding and logical release reject foreign threads without consuming ownership", MainThreadOwnership),
        ("Each LOCAL command real cdecl entry preserves full width pointers and suppresses failing LOCAL handlers", CdeclEntries)
    };
    private static readonly Command[] Commands = Enum.GetValues<Command>();
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("LOCAL command boundary invariant failed."); }
    private static T Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T error) { return error; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static VerifiedHookTarget Target(Command command) => new("local.command.test." + command,
        new(Interlocked.Add(ref Addresses.Next, 16)), new(0x222), new(0x4321), ExactHookAbi.InstanceVoid);
    // Only the fixture bypasses the sealed metadata authority. This does not verify
    // game metadata, Dobby patching, save isolation, or a native run transaction.
    private static ExactHookBroker.LocalRunCommandTarget Token(VerifiedHookTarget target, Command command)
        => (ExactHookBroker.LocalRunCommandTarget)typeof(ExactHookBroker.LocalRunCommandTarget)
            .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
                new[] { typeof(VerifiedHookTarget), typeof(Command) }, null)!.Invoke(new object[] { target, command });
    private static void Authority()
    {
        const string supported = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79";
        Throws<UnsupportedSignatureException>(() => ExactHookBroker.LocalRunCommandTarget.Resolve(null!, new(1), supported, (Command)90));
        foreach (var command in Commands)
        {
            Throws<UnsupportedSignatureException>(() => ExactHookBroker.LocalRunCommandTarget.Resolve(null!, new(1), "unsupported", command));
            Throws<ArgumentNullException>(() => ExactHookBroker.LocalRunCommandTarget.Resolve(null!, new(1), supported, command));
        }
        Check(typeof(ExactHookBroker.LocalRunCommandTarget).GetConstructors().Length == 0);
    }
    private static void OrdinaryAndClaimed()
    {
        foreach (var command in Commands)
        {
            using var f = new Fixture(); var target = Target(command); var order = new List<string>();
            var calls = 0; var handled = 0; var claim = false; var fail = false;
            var originalError = new IOException("Original exception remains original.");
            f.Engine.Original = new ExactHookBroker.InstanceVoid((self, info) => {
                Check(self == new IntPtr(0x111) && info == new IntPtr(0x222)); calls++; order.Add("original");
                if (fail) throw originalError;
            });
            using var observer = f.Broker.Bind(target, _ => true, (phase, _, _, _) => order.Add(phase.ToString()));
            using var retained = f.Broker.BindRetainedObservation(target, _ => true,
                self => { order.Add("retain"); return new RetainedContext(self, () => order.Add("release")); }, _ => order.Add("post"));
            using var gate = f.Broker.BindLocalRunCommandPolicy(Token(target, command), _ => {
                order.Add("classify"); return claim ? Decision.ClaimLocal : Decision.ContinueOriginal;
            }, _ => { handled++; throw new IOException("Next bundle preparation failed."); });
            f.Engine.InvokeVoid();
            Check(order.SequenceEqual(new[] { "classify", "Before", "retain", "original", "post", "After", "release" }));
            order.Clear(); claim = true; f.Engine.InvokeVoid();
            Check(order.SequenceEqual(new[] { "classify" }) && calls == 1 && handled == 1);
            claim = false; fail = true; Check(ReferenceEquals(Throws<IOException>(f.Engine.InvokeVoid), originalError));
            fail = false; f.Engine.InvokeVoid();
            Check(calls == 3 && handled == 1 && gate.DescribePolicy() is { Continued: 3, Claimed: 1, Skipped: 1, Faults: 1 }
                && gate.DescribePolicy().Command == command && gate.DescribeHook().OriginalCalls == 3
                && f.Log.Messages.Any(x => x.Contains("stock " + command + " remains suppressed")));
        }
    }
    private static void InvalidClassification()
    {
        foreach (var command in Commands)
        {
            using var f = new Fixture(); var target = Target(command); var calls = 0; var handled = 0; var mode = 0;
            f.Engine.Original = new ExactHookBroker.InstanceVoid((_, _) => calls++);
            using var gate = f.Broker.BindLocalRunCommandPolicy(Token(target, command), _ => mode switch {
                0 => throw new IOException("Identity lookup failed."), 1 => (Decision)71, _ => Decision.ContinueOriginal
            }, _ => handled++);
            f.Engine.InvokeVoid(); mode = 1; f.Engine.InvokeVoid(); mode = 2;
            var entry = (ExactHookBroker.InstanceVoid)f.Engine.Replacement!;
            entry(new(0x111), new(0x333)); entry(new(0x111), IntPtr.Zero);
            Check(calls == 1 && handled == 0 && gate.DescribePolicy() is { Classified: 3, Continued: 1, Faults: 3, Skipped: 3 });
        }
    }
    private static void ReentryAndWorker()
    {
        foreach (var command in Commands)
        {
            using var f = new Fixture(); var target = Target(command); var calls = 0; var handled = 0; var claim = false;
            f.Engine.Original = new ExactHookBroker.InstanceVoid((_, _) => Interlocked.Increment(ref calls));
            ExactHookBroker.LocalRunCommandBinding? gate = null;
            gate = f.Broker.BindLocalRunCommandPolicy(Token(target, command), _ => claim ? Decision.ClaimLocal : Decision.ContinueOriginal,
                _ => { handled++; gate!.Dispose(); f.Engine.InvokeVoid(); throw new IOException("Failed after detach."); });
            Exception? workerFailure = null;
            void Worker() { var worker = new Thread(() => { try { f.Engine.InvokeVoid(); } catch (Exception error) { workerFailure = error; } }); worker.Start(); worker.Join(); }
            Worker(); claim = true; Worker();
            Check(calls == 1 && handled == 0 && workerFailure is null && gate.DescribePolicy().WrongThreadClaims == 1);
            f.Engine.InvokeVoid(); Check(calls == 1 && handled == 1);
            f.Engine.InvokeVoid(); gate.Dispose();
            Check(calls == 2 && gate.DescribePolicy() is { Claimed: 2, Skipped: 3, Faults: 1, SuppressedPolicyReentries: 1 });
        }
    }
    private static void ExclusiveOwnership()
    {
        foreach (var command in Commands)
        {
            using var f = new Fixture(); var target = Target(command); var calls = 0;
            f.Engine.Original = new ExactHookBroker.InstanceVoid((_, _) => calls++);
            using var gate = f.Broker.BindLocalRunCommandPolicy(Token(target, command), _ => Decision.ClaimLocal, _ => { });
            Throws<InvalidOperationException>(() => f.Broker.BindLocalRunCommandPolicy(Token(target, command), _ => Decision.ContinueOriginal, _ => { }));
            Throws<UnsupportedSignatureException>(() => f.Broker.Bind(target with { MethodInfo = new(9) }, _ => true, (_, _, _, _) => { }));
            using var ordinary = f.Broker.Bind(target with { Id = "same.method.alias" }, _ => true, (_, _, _, _) => { });
            f.Engine.InvokeVoid(); gate.Dispose(); f.Engine.InvokeVoid();
            Check(calls == 1 && f.Engine.Installs == 1);
        }
    }
    private static void CrossCommand()
    {
        var fixtures = Commands.Select(_ => new Fixture()).ToArray(); var gates = new List<ExactHookBroker.LocalRunCommandBinding>();
        var calls = 0; var handled = new int[Commands.Length];
        try
        {
            for (var i = 0; i < Commands.Length; i++)
            {
                var index = i; fixtures[i].Engine.Original = new ExactHookBroker.InstanceVoid((_, _) => calls++);
                gates.Add(fixtures[i].Broker.BindLocalRunCommandPolicy(Token(Target(Commands[i]), Commands[i]), _ => Decision.ClaimLocal,
                    _ => { handled[index]++; if (index + 1 < Commands.Length) fixtures[index + 1].Engine.InvokeVoid(); else fixtures[0].Engine.InvokeVoid(); }));
            }
            fixtures[0].Engine.InvokeVoid();
            Check(calls == 0 && handled.All(value => value == 1) && gates[0].DescribePolicy().SuppressedPolicyReentries == 1);
            gates[1].Dispose(); fixtures[1].Engine.InvokeVoid(); fixtures[^1].Engine.InvokeVoid();
            Check(calls == 2 && handled.SequenceEqual(Enumerable.Range(0, Commands.Length).Select(index => index == 0 || index == Commands.Length - 1 ? 2 : 1)));
        }
        finally { foreach (var gate in gates) gate.Dispose(); foreach (var fixture in fixtures) fixture.Dispose(); }
    }
    private static void MainThreadOwnership()
    {
        foreach (var command in Commands)
        {
            using var f = new Fixture(); var token = Token(Target(command), command);
            f.Engine.Original = new ExactHookBroker.InstanceVoid((_, _) => { });
            Task.Run(() => Throws<InvalidOperationException>(() => f.Broker.BindLocalRunCommandPolicy(token, _ => Decision.ClaimLocal, _ => { }))).GetAwaiter().GetResult();
            Check(f.Engine.Installs == 0);
            using var gate = f.Broker.BindLocalRunCommandPolicy(token, _ => Decision.ClaimLocal, _ => { });
            Task.Run(() => Throws<InvalidOperationException>(() => gate.Dispose())).GetAwaiter().GetResult();
            f.Engine.InvokeVoid(); Check(gate.DescribePolicy().Claimed == 1 && gate.DescribeHook().OriginalCalls == 0);
        }
    }
    private static void CdeclEntries()
    {
        foreach (var command in Commands)
        {
            using var f = new Fixture(); var self = new IntPtr(unchecked((long)0x12345678ABCDEF00));
            var method = new IntPtr(unchecked((long)0x76543210FEDCBA00)); var target = Target(command) with { MethodInfo = method };
            var calls = 0; var handled = 0; var claim = false;
            f.Engine.Original = new ExactHookBroker.InstanceVoid((receiver, info) => { Check(receiver == self && info == method); calls++; });
            using var gate = f.Broker.BindLocalRunCommandPolicy(Token(target, command), receiver => {
                Check(receiver == self); return claim ? Decision.ClaimLocal : Decision.ContinueOriginal;
            }, _ => { handled++; throw new IOException("Caught inside the reverse P/Invoke boundary."); });
            var pointer = Marshal.GetFunctionPointerForDelegate(f.Engine.Replacement!);
            var dynamic = new DynamicMethod("local_command_cdecl", typeof(void), new[] { typeof(IntPtr), typeof(IntPtr) });
            var il = dynamic.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I8, pointer.ToInt64()); il.Emit(OpCodes.Conv_I);
            il.EmitCalli(OpCodes.Calli, CallingConvention.Cdecl, typeof(void), new[] { typeof(IntPtr), typeof(IntPtr) }); il.Emit(OpCodes.Ret);
            var entry = (Action<IntPtr, IntPtr>)dynamic.CreateDelegate(typeof(Action<IntPtr, IntPtr>));
            entry(self, method); claim = true; entry(self, method); gate.Dispose(); entry(self, method);
            Check(calls == 2 && handled == 1 && gate.DescribePolicy() is { Claimed: 1, Faults: 1, Skipped: 1 } && f.Engine.Installs == 1);
            GC.KeepAlive(f.Engine.Replacement); GC.KeepAlive(f.Engine.Original);
        }
    }
}
