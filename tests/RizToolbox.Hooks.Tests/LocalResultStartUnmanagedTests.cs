using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

/// <summary>These checks enter the replacement through a real unmanaged cdecl
/// calli into its reverse-P/Invoke stub. The engine and original delegate remain
/// managed fixtures: this proves neither Dobby patching nor game behavior.
/// Deliberately do not throw an original exception across the unmanaged boundary.
/// </summary>
internal static class LocalResultStartUnmanagedTests
{
    internal static readonly (string Name, Action Run)[] Cases =
    {
        ("LOCAL Start cdecl calli preserves full-width pointers and calls ordinary original once", ContinueThroughUnmanagedEntry),
        ("LOCAL Start cdecl calli catches claimed handler failure and restores original after detach", ClaimThroughUnmanagedEntry),
        ("LOCAL Start cdecl calli protects nested and off-main LOCAL entries while ordinary worker continues", NestedAndWorkerUnmanagedEntries),
    };

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static VerifiedHookTarget Target(IntPtr methodInfo) => new("local.result.start.cdecl.test",
        new IntPtr(Interlocked.Add(ref Addresses.Next, 16)), methodInfo, new(0x4321), ExactHookAbi.InstanceVoid);
    private static ExactHookBroker.LocalResultStartTarget Token(VerifiedHookTarget target)
        => (ExactHookBroker.LocalResultStartTarget)typeof(ExactHookBroker.LocalResultStartTarget)
            .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
                new[] { typeof(VerifiedHookTarget) }, null)!.Invoke(new object[] { target });

    private static Action<IntPtr, IntPtr> CdeclEntry(Delegate replacement)
    {
        // Do not use GetDelegateForFunctionPointer to roundtrip the managed
        // delegate: the runtime can return that original managed delegate.
        var pointer = Marshal.GetFunctionPointerForDelegate(replacement);
        var invoke = new DynamicMethod("local_start_cdecl_entry", typeof(void), new[] { typeof(IntPtr), typeof(IntPtr) });
        var il = invoke.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I8, pointer.ToInt64()); il.Emit(OpCodes.Conv_I);
        il.EmitCalli(OpCodes.Calli, CallingConvention.Cdecl, typeof(void), new[] { typeof(IntPtr), typeof(IntPtr) });
        il.Emit(OpCodes.Ret);
        return (Action<IntPtr, IntPtr>)invoke.CreateDelegate(typeof(Action<IntPtr, IntPtr>));
    }

    private static void ContinueThroughUnmanagedEntry()
    {
        using var f = new Fixture(); var receiver = new IntPtr(unchecked((long)0x12345678ABCDEF00));
        var method = new IntPtr(unchecked((long)0x76543210FEDCBA00)); var target = Target(method);
        var calls = 0; var handlers = 0; var order = new List<string>();
        f.Engine.Original = new ExactHookBroker.InstanceVoid((self, info) =>
        { Check(self == receiver && info == method, "The cdecl entry truncated or swapped pointer arguments."); calls++; order.Add("original"); });
        using var ordinary = f.Broker.Bind(target, _ => true, (phase, _, _, _) => order.Add(phase.ToString()));
        using var policy = f.Broker.BindLocalResultStartPolicy(Token(target), self =>
        { Check(self == receiver, "The unmanaged entry classifier received a changed pointer."); order.Add("classify"); return ExactHookBroker.LocalResultStartDecision.ContinueOriginal; },
            _ => handlers++);
        CdeclEntry(f.Engine.Replacement!)(receiver, method);
        Check(calls == 1 && handlers == 0 && order.SequenceEqual(new[] { "classify", "Before", "original", "After" })
            && policy.DescribePolicy() is { Continued: 1, Skipped: 0, Faults: 0 }
            && policy.DescribeHook().OriginalCalls == 1, "The unmanaged ordinary entry changed call count or phases.");
        GC.KeepAlive(f.Engine.Replacement);
    }

    private static void ClaimThroughUnmanagedEntry()
    {
        using var f = new Fixture(); var receiver = new IntPtr(0x12345); var method = new IntPtr(0x67890);
        var target = Target(method); var calls = 0; var handlers = 0;
        f.Engine.Original = new ExactHookBroker.InstanceVoid((self, info) =>
        { Check(self == receiver && info == method, "Detached original received changed ABI arguments."); calls++; });
        var policy = f.Broker.BindLocalResultStartPolicy(Token(target), _ => ExactHookBroker.LocalResultStartDecision.ClaimLocal,
            _ => { if (++handlers == 2) throw new IOException("Handled inside the claimed policy before returning through cdecl."); });
        var invoke = CdeclEntry(f.Engine.Replacement!);
        invoke(receiver, method); invoke(receiver, method);
        Check(calls == 0 && handlers == 2 && policy.DescribePolicy() is { Claimed: 2, Skipped: 2, Faults: 1 },
            "A claimed handler failure crossed the unmanaged boundary or ran stock.");
        policy.Dispose(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        invoke(receiver, method);
        Check(calls == 1 && handlers == 2 && policy.DescribeHook().NativeCalls == 3 && policy.DescribeHook().OriginalCalls == 1,
            "Logical detach did not restore the retained original through cdecl after GC.");
        GC.KeepAlive(f.Engine.Replacement);
    }

    private static void NestedAndWorkerUnmanagedEntries()
    {
        using var f = new Fixture(); var local = new IntPtr(0x12345); var ordinary = new IntPtr(0x56789); var method = new IntPtr(0x77777);
        var target = Target(method); var calls = 0; var handlers = 0; Exception? workerError = null;
        Action<IntPtr, IntPtr>? invoke = null;
        f.Engine.Original = new ExactHookBroker.InstanceVoid((self, info) =>
        { Check(self == ordinary && info == method, "A protected LOCAL receiver reached original through cdecl."); Interlocked.Increment(ref calls); });
        using var policy = f.Broker.BindLocalResultStartPolicy(Token(target), self => self == local
            ? ExactHookBroker.LocalResultStartDecision.ClaimLocal : ExactHookBroker.LocalResultStartDecision.ContinueOriginal,
            _ => { Check(f.Main.IsMainThread, "An off-main LOCAL handler ran."); handlers++; invoke!(local, method); });
        invoke = CdeclEntry(f.Engine.Replacement!);
        invoke(local, method);
        var worker = new Thread(() =>
        { try { invoke(local, method); invoke(ordinary, method); } catch (Exception error) { workerError = error; } });
        worker.Start(); worker.Join();
        Check(workerError is null && calls == 1 && handlers == 1
            && policy.DescribePolicy() is { Claimed: 2, Skipped: 3, Continued: 1, WrongThreadClaims: 1, SuppressedPolicyReentries: 1 }
            && policy.DescribeHook().NativeCalls == 4 && policy.DescribeHook().OriginalCalls == 1,
            "Unmanaged nested/worker entries escaped protection or blanket suppressed the ordinary receiver.");
        GC.KeepAlive(f.Engine.Replacement);
    }
}
