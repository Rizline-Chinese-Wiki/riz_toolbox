using System.Reflection.Emit;
using System.Runtime.InteropServices;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

internal static class BooleanArgumentObservationTests
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Boolean argument metadata rejects enum byte byref extra parameters and nonvoid", Signature),
        ("Boolean argument cdecl entry preserves raw DL and full receiver MethodInfo without mutation", UnmanagedArguments),
        ("Boolean argument original failure preserves exception and releases paired context activity", OriginalFault),
        ("Boolean argument nested worker foreign MethodInfo and mismatched receivers forward without observation", Isolation),
        ("Boolean argument capture after and cleanup failures preserve peers and exact original", ObserverFaults),
        ("Boolean argument in-flight logical detach skips after but releases context before drain", InFlightDetach),
        ("Boolean argument shared binding rejects method aliases and incompatible ABIs", SharedIdentity),
        ("Boolean argument unconfirmed install remains rooted without fabricating observation or retry", FailedInstall)
    };
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject<T>(Action run) where T : Exception
    { try { run(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static VerifiedHookTarget Target() => new("boolean.argument.fixture", new(Interlocked.Add(ref Addresses.Next, 16)),
        new(0x1234), new(0x4321), ExactHookAbi.InstanceBooleanVoid);
    private sealed class Frame : IDisposable
    {
        internal readonly byte Value; internal int Disposals; private readonly Action? _release;
        internal Frame(byte value, Action? release = null) => (Value, _release) = (value, release);
        public void Dispose() { Disposals++; _release?.Invoke(); }
    }
    private static void Signature()
    {
        var returns = new NativeTypeLayout("System.Void", NativeValueKind.Void, 0);
        var value = new NativeTypeLayout("System.Boolean", NativeValueKind.Boolean, 1);
        HookTargetValidation.ValidateBooleanArgumentSignature(returns, new[] { value });
        foreach (var bad in new[] { value with { ByRef = true }, value with { Size = 4 }, value with { Kind = NativeValueKind.Byte },
            value with { Kind = NativeValueKind.Enum }, value with { Name = "OtherBoolean" } })
            Reject<UnsupportedSignatureException>(() => HookTargetValidation.ValidateBooleanArgumentSignature(returns, new[] { bad }));
        Reject<UnsupportedSignatureException>(() => HookTargetValidation.ValidateBooleanArgumentSignature(returns, Array.Empty<NativeTypeLayout>()));
        Reject<UnsupportedSignatureException>(() => HookTargetValidation.ValidateBooleanArgumentSignature(returns, new[] { value, value }));
        Reject<UnsupportedSignatureException>(() => HookTargetValidation.ValidateBooleanArgumentSignature(value, new[] { value }));
        using var f = new Fixture();
        foreach (var abi in new[] { ExactHookAbi.InstanceEnum32Void, ExactHookAbi.InstanceVoid, ExactHookAbi.InstanceBooleanReturn0 })
            Reject<UnsupportedSignatureException>(() => f.Broker.BindRetainedBooleanArgumentObservation(Target() with { Abi = abi }, _ => true,
                (_, v) => new Frame(v), (_, _) => { }));
        Reject<UnsupportedSignatureException>(() => f.Broker.Bind(Target(), _ => true, (_, _, _, _) => { }));
        Check(f.Engine.Installs == 0, "Unsupported ABI reached installation.");
    }
    private static Action<IntPtr, byte, IntPtr> Cdecl(Delegate replacement)
    {
        var args = new[] { typeof(IntPtr), typeof(byte), typeof(IntPtr) };
        var method = new DynamicMethod("boolean_argument_cdecl", typeof(void), args); var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I8, Marshal.GetFunctionPointerForDelegate(replacement).ToInt64()); il.Emit(OpCodes.Conv_I);
        il.EmitCalli(OpCodes.Calli, CallingConvention.Cdecl, typeof(void), args); il.Emit(OpCodes.Ret);
        return (Action<IntPtr, byte, IntPtr>)method.CreateDelegate(typeof(Action<IntPtr, byte, IntPtr>));
    }
    private static void UnmanagedArguments()
    {
        using var f = new Fixture(); var self = new IntPtr(unchecked((long)0x12345678abcdef00));
        var info = new IntPtr(unchecked((long)0x76543210fedcba00)); var target = Target() with { MethodInfo = info };
        var originals = new List<(IntPtr, byte, IntPtr)>(); var captured = new List<Frame>(); var after = new List<byte>();
        f.Engine.Original = new ExactHookBroker.InstanceBooleanVoid((s, v, m) => originals.Add((s, v, m)));
        var binding = f.Broker.BindRetainedBooleanArgumentObservation(target, s => s == self,
            (_, v) => { var frame = new Frame(v); captured.Add(frame); return frame; },
            (frame, v) => { Check(frame.Value == v && frame.Disposals == 0, "Retained raw byte changed or context was freed early."); after.Add(v); });
        var invoke = Cdecl(f.Engine.Replacement!); var values = new byte[] { 0, 1, 0x80, 0xff };
        foreach (var v in values) invoke(self, v, info);
        invoke(self, 1, IntPtr.Zero); // Direct game callers may pass null trailing metadata.
        Check(originals.Count == 5 && originals.Take(4).Select(x => x.Item2).SequenceEqual(values)
            && originals.All(x => x.Item1 == self) && originals.Take(4).All(x => x.Item3 == info) && originals[4].Item3 == IntPtr.Zero
            && captured.All(x => x.Disposals == 1) && after.SequenceEqual(values.Concat(new byte[] { 1 })), "cdecl Boolean ABI or pairing changed.");
        binding.Dispose(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); invoke(self, 0, info);
        Check(originals.Count == 6 && captured.Count == 5 && binding.ReadActivity() is { Known: true, InFlight: 0 }, "Logical detach lost native forwarding or leaked activity.");
        GC.KeepAlive(f.Engine.Replacement);
    }
    private static void OriginalFault()
    {
        using var f = new Fixture(); var target = Target(); var fault = new IOException("actual original"); var fail = true; var calls = 0; var afters = 0;
        var frames = new List<Frame>(); ExactHookBroker.Binding? binding = null;
        f.Engine.Original = new ExactHookBroker.InstanceBooleanVoid((_, _, _) => {
            calls++; Check(binding!.ReadActivity() is { Known: true, InFlight: 1 }, "Original is absent from true in-flight count."); if (fail) throw fault;
        });
        using var owned = binding = f.Broker.BindRetainedBooleanArgumentObservation(target, _ => true,
            (_, v) => { var frame = new Frame(v, () => { Check(binding!.ReadActivity().InFlight == 1, "Context released after drain."); if (fail) throw new IOException("release"); }); frames.Add(frame); return frame; },
            (_, _) => afters++);
        Exception? caught = null; try { ((ExactHookBroker.InstanceBooleanVoid)f.Engine.Replacement!)(new(1), 1, target.MethodInfo); } catch (Exception e) { caught = e; }
        Check(ReferenceEquals(caught, fault) && afters == 0 && frames.Single().Disposals == 1 && binding.ReadActivity().InFlight == 0,
            "Original failure was masked or reported as successful After.");
        fail = false; ((ExactHookBroker.InstanceBooleanVoid)f.Engine.Replacement!)(new(1), 0, target.MethodInfo);
        Check(calls == 2 && afters == 1 && frames.All(x => x.Disposals == 1) && binding.Describe().SuppressedReentries == 0,
            "Exception leaked an entry gate into the next native invocation.");
    }
    private static void Isolation()
    {
        using var f = new Fixture(); var target = Target(); var calls = 0; var captures = 0; var filters = 0; var afters = 0;
        ExactHookBroker.Binding? binding = null; ExactHookBroker.InstanceBooleanVoid? invoke = null;
        f.Engine.Original = new ExactHookBroker.InstanceBooleanVoid((s, v, m) => {
            calls++; if (calls == 1) { Check(binding!.ReadActivity().InFlight == 1, "Missing outer activity."); invoke!(s, v, m); }
        });
        using var owned = binding = f.Broker.BindRetainedBooleanArgumentObservation(target, s => { filters++; return s == new IntPtr(1); },
            (_, v) => { captures++; return new Frame(v); }, (_, _) => afters++);
        invoke = (ExactHookBroker.InstanceBooleanVoid)f.Engine.Replacement!;
        invoke(new(1), 1, target.MethodInfo); var priorFilters = filters;
        Task.Run(() => invoke(new(1), 0, target.MethodInfo)).GetAwaiter().GetResult();
        Check(filters == priorFilters, "Worker invocation accessed native receiver metadata.");
        invoke(new(1), 0, new(0x9999)); Check(filters == priorFilters, "Foreign MethodInfo alias touched receiver metadata.");
        invoke(new(2), 0, target.MethodInfo);
        Check(calls == 5 && captures == 1 && afters == 1 && binding.Describe() is { NativeCalls: 5, OriginalCalls: 5, SuppressedReentries: 1, SuppressedWrongThread: 1 }
            && binding.ReadActivity().InFlight == 0, "Suppressed entries borrowed observation or changed original forwarding.");
    }
    private static void ObserverFaults()
    {
        using var f = new Fixture(); var target = Target(); var calls = 0; var afters = 0; var releaseOrder = new List<int>();
        f.Engine.Original = new ExactHookBroker.InstanceBooleanVoid((_, _, _) => calls++);
        using var a = f.Broker.BindRetainedBooleanArgumentObservation<Frame>(target, _ => true, (_, _) => throw new IOException("capture"), (_, _) => throw new Exception("unreachable"));
        using var b = f.Broker.BindRetainedBooleanArgumentObservation(target, _ => true,
            (_, v) => new Frame(v, () => { releaseOrder.Add(1); throw new IOException("release"); }), (_, _) => throw new IOException("after"));
        using var c = f.Broker.BindRetainedBooleanArgumentObservation(target, _ => true,
            (_, v) => new Frame(v, () => releaseOrder.Add(2)), (_, v) => { Check(v == 0xff, "Observer normalized raw Boolean."); afters++; });
        ((ExactHookBroker.InstanceBooleanVoid)f.Engine.Replacement!)(new(1), 0xff, target.MethodInfo);
        Check(calls == 1 && afters == 1 && releaseOrder.SequenceEqual(new[] { 2, 1 }) && f.Log.Messages.Count == 3
            && c.ReadActivity() is { Known: true, InFlight: 0 }, "Observer fault blocked peers, original, cleanup or activity drain.");
    }
    private static void InFlightDetach()
    {
        using var f = new Fixture(); var target = Target(); ExactHookBroker.Binding? binding = null; var afters = 0; Frame? context = null;
        f.Engine.Original = new ExactHookBroker.InstanceBooleanVoid((_, _, _) => binding!.Dispose());
        binding = f.Broker.BindRetainedBooleanArgumentObservation(target, _ => true,
            (_, v) => context = new(v, () => Check(binding!.ReadActivity().InFlight == 1, "Activity drained before retained release.")), (_, _) => afters++);
        ((ExactHookBroker.InstanceBooleanVoid)f.Engine.Replacement!)(new(1), 1, target.MethodInfo);
        Check(afters == 0 && context is { Disposals: 1 } && binding.ReadActivity().InFlight == 0, "Detach leaked retained context or fabricated successful After.");
    }
    private static void SharedIdentity()
    {
        using var f = new Fixture(); var target = Target(); f.Engine.Original = new ExactHookBroker.InstanceBooleanVoid((_, _, _) => { });
        using var first = f.Broker.BindRetainedBooleanArgumentObservation(target, _ => true, (_, v) => new Frame(v), (_, _) => { });
        using var second = f.Broker.BindRetainedBooleanArgumentObservation(target with { Id = "same-identity" }, _ => true, (_, v) => new Frame(v), (_, _) => { });
        foreach (var alias in new[] { target with { MethodInfo = new(55) }, target with { DeclaringClass = new(66) }, target with { ReturnClass = new(77) } })
            Reject<UnsupportedSignatureException>(() => f.Broker.BindRetainedBooleanArgumentObservation(alias, _ => true, (_, v) => new Frame(v), (_, _) => { }));
        Reject<UnsupportedSignatureException>(() => f.Broker.Bind(target with { Abi = ExactHookAbi.InstanceVoid }, _ => true, (_, _, _, _) => { }));
        Check(f.Engine.Installs == 1, "Method aliases acquired new native patches.");
    }
    private static void FailedInstall()
    {
        using var f = new Fixture(); var target = Target(); f.Engine.Fail = true; var callbacks = 0;
        Action bind = () => f.Broker.BindRetainedBooleanArgumentObservation(target, _ => true,
            (_, v) => { callbacks++; return new Frame(v); }, (_, _) => callbacks++);
        Reject<Exception>(bind); var replacement = f.Engine.Replacement!;
        Cdecl(replacement)(new(1), 1, target.MethodInfo);
        f.Engine.Fail = false; Reject<InvalidOperationException>(bind);
        Check(f.Engine.Installs == 1 && callbacks == 0, "Unknown install fabricated observation or retried a patch."); GC.KeepAlive(replacement);
    }
}
