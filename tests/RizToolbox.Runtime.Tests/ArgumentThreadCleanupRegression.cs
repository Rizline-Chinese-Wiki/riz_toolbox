using RizToolbox.Runtime;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

/// <summary>A refused foreign-thread cleanup must leave attached native ownership recoverable.</summary>
internal static class ArgumentThreadCleanupRegression
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Wrong-thread Arg.Dispose preserves its native root and pointer for owner-thread cleanup", DirectDispose),
        ("Wrong-thread Invoke cleanup preserves its attached native argument root", Invocation),
        ("Wrong-thread NewExact cleanup preserves its attached native argument root", Construction)
    };

    private static void DirectDispose() => CheckOwnerCleanup((_, argument) => argument.Dispose());

    private static void Invocation() => CheckOwnerCleanup((native, argument) =>
        native.Api.Invoke(native.Method, native.Instance, argument));

    private static void Construction() => CheckOwnerCleanup((native, argument) =>
        native.Api.NewExact(native.ClassOf(native.Receiver), new[] { "System.Object" }, argument));

    private static void CheckOwnerCleanup(Action<FakeNative, A> workerAction)
    {
        using var native = new FakeNative { ConstructorAvailable = true };
        native.Parameters = new[] { native.Object };
        // This extra managed reference is cleanup insurance for a failing regression;
        // ownership still belongs to the argument and is released through it below.
        using var attachedRoot = native.Api.Root(native.OtherObject);
        using var argument = A.Reference(native.OtherObject, attachedRoot);
        Exception? failure = null;
        var worker = new Thread(() => {
            try { workerAction(native, argument); }
            catch (Exception error) { failure = error; }
        });
        worker.Start(); worker.Join();
        Check(failure is InvalidOperationException);
        Check(native.Invocations == 0 && native.ObjectAllocations == 0);
        Check(native.ActiveRoots == 1 && native.FreedRoots == 0);
        Check(argument.Pointer == native.OtherObject && attachedRoot.Target == native.OtherObject);
        argument.Dispose(); argument.Dispose();
        Check(native.ActiveRoots == 0 && native.NewRoots == 1 && native.FreedRoots == 1);
        Throws<ObjectDisposedException>(() => _ = argument.Pointer);
        Throws<TargetExpiredException>(() => _ = attachedRoot.Target);
    }

    private static void Check(bool condition)
    { if (!condition) throw new InvalidOperationException("Argument thread-cleanup invariant failed."); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
}
