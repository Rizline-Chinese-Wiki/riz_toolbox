using RizToolbox.Runtime;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

internal static class ExactConstructionRegression
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Exact construction transfers one live object root to its caller", SuccessTransfersRoot),
        ("Exact construction rejects a same-arity different parameter signature before allocation", ExactSignature),
        ("Exact construction consumes arguments on missing or malformed signatures", SignatureCleanup),
        ("Exact construction refuses an inherited parent constructor before allocation", DeclaredConstructor),
        ("Exact construction rejects static abstract nonvoid byref and open constructors before allocation", InvalidMetadata),
        ("Exact construction rejects incompatible argument buffers before allocation and releases temporary roots", InvalidArguments),
        ("Exact construction roots reference arguments across object allocation and invocation", ReferencesAcrossAllocation),
        ("Exact construction releases its new object root after a native constructor exception", NativeExceptionCleanup),
        ("Exact construction releases its new object root after a managed invocation exception", ManagedExceptionCleanup),
        ("Exact construction frees argument roots when native object allocation fails", AllocationFailure),
        ("Exact construction rejects a foreign thread before allocating", ThreadAccess),
        ("Exact construction permits an already inflated closed constructor", InflatedConstructor)
    };

    private static FakeNative Constructor(params Func<FakeNative, IntPtr>[] parameters)
    {
        var native = new FakeNative { ConstructorAvailable = true };
        native.Parameters = parameters.Select(select => select(native)).ToArray();
        return native;
    }

    private static void SuccessTransfersRoot()
    {
        using var native = Constructor(n => n.Int32, n => n.Boolean);
        var number = A.Value(7); var flag = A.Value(false);
        var root = native.Api.NewExact(native.ClassOf(native.Receiver), new[] { "System.Int32", "System.Boolean" }, number, flag);
        Check(native.ObjectAllocations == 1 && native.Invocations == 1 && native.AllConstructorCallsRooted);
        Check(native.ActiveRoots == 1 && root.Target == native.LastObject && native.HasRootFor(root.Target));
        Throws<ObjectDisposedException>(() => _ = number.Pointer);
        Throws<ObjectDisposedException>(() => _ = flag.Pointer);
        root.Dispose(); root.Dispose();
        Check(native.ActiveRoots == 0 && native.NewRoots == native.FreedRoots);
        Throws<TargetExpiredException>(() => _ = root.Target);
    }

    private static void ExactSignature()
    {
        using var native = Constructor(n => n.Int32);
        var argument = A.Value(3f);
        Throws<MissingMethodException>(() => native.Api.NewExact(native.ClassOf(native.Receiver), new[] { "System.Single" }, argument));
        Check(native.ObjectAllocations == 0 && native.Invocations == 0 && native.ActiveRoots == 0);
        Throws<ObjectDisposedException>(() => _ = argument.Pointer);
    }

    private static void SignatureCleanup()
    {
        using var native = Constructor(n => n.Int32);
        var argument = A.Value(3);
        Throws<UnsupportedSignatureException>(() => native.Api.NewExact(native.ClassOf(native.Receiver), Array.Empty<string>(), argument));
        Throws<ObjectDisposedException>(() => _ = argument.Pointer);
        var owned = new CountingLease(); var reference = A.Reference(native.OtherObject, owned);
        Throws<ArgumentNullException>(() => native.Api.NewExact(native.ClassOf(native.Receiver), null!, reference));
        Check(owned.Count == 1);
        Throws<ObjectDisposedException>(() => _ = reference.Pointer);
        Check(native.ObjectAllocations == 0 && native.Invocations == 0 && native.ActiveRoots == 0);
    }

    private static void DeclaredConstructor()
    {
        using var native = Constructor();
        native.ConstructorDeclaringClass = native.ClassOf(native.Object);
        Throws<UnsupportedSignatureException>(() => native.Api.NewExact(native.ClassOf(native.Receiver), Array.Empty<string>()));
        Check(native.ObjectAllocations == 0 && native.Invocations == 0 && native.ActiveRoots == 0);
    }

    private static void InvalidMetadata()
    {
        using (var native = Constructor())
        {
            native.IsStatic = true;
            Throws<MissingMethodException>(() => native.Api.NewExact(native.ClassOf(native.Receiver), Array.Empty<string>()));
            Check(native.ObjectAllocations == 0 && native.Invocations == 0);
        }
        foreach (var invalid in new Action<FakeNative>[] {
            n => n.ConstructorExtraFlags = 0x400,
            n => n.ReturnType = n.Int32,
            n => n.ReturnType = n.AddType("System.Void", 1, true, 0, byRef: true),
            n => n.GenericMethod = true })
        {
            using var native = Constructor(); invalid(native);
            Throws<UnsupportedSignatureException>(() => native.Api.NewExact(native.ClassOf(native.Receiver), Array.Empty<string>()));
            Check(native.ObjectAllocations == 0 && native.Invocations == 0 && native.ActiveRoots == 0);
        }
    }

    private static void InvalidArguments()
    {
        using (var native = Constructor(n => n.Object, n => n.Int32))
        {
            var reference = A.Ref(native.OtherObject); var wrongBuffer = A.Value(1f);
            Throws<UnsupportedSignatureException>(() => native.Api.NewExact(native.ClassOf(native.Receiver),
                new[] { "System.Object", "System.Int32" }, reference, wrongBuffer));
            Check(native.ObjectAllocations == 0 && native.Invocations == 0 && native.ActiveRoots == 0 && native.NewRoots == native.FreedRoots);
            Throws<ObjectDisposedException>(() => _ = reference.Pointer);
            Throws<ObjectDisposedException>(() => _ = wrongBuffer.Pointer);
        }
        using (var native = Constructor(n => n.Receiver))
        {
            Throws<UnsupportedSignatureException>(() => native.Api.NewExact(native.ClassOf(native.Receiver),
                new[] { "Fake.Receiver" }, A.Ref(native.OtherObject)));
            Check(native.ObjectAllocations == 0 && native.Invocations == 0 && native.ActiveRoots == 0 && native.NewRoots == native.FreedRoots);
        }
    }

    private static void ReferencesAcrossAllocation()
    {
        using var native = Constructor(n => n.Object);
        native.OnObjectAllocation = () => Check(native.HasRootFor(native.OtherObject));
        using var root = native.Api.NewExact(native.ClassOf(native.Receiver), new[] { "System.Object" }, A.Ref(native.OtherObject));
        Check(native.ObjectAllocations == 1 && native.Invocations == 1 && native.AllConstructorCallsRooted);
        Check(native.ActiveRoots == 1 && native.HasRootFor(root.Target) && !native.HasRootFor(native.OtherObject));
        root.Dispose(); Check(native.ActiveRoots == 0 && native.NewRoots == native.FreedRoots);
    }

    private static void NativeExceptionCleanup()
    {
        using var native = Constructor(n => n.Object);
        native.ExceptionOnInvoke = new IntPtr(99);
        var owned = new CountingLease(); var argument = A.Reference(native.OtherObject, owned);
        Throws<InvalidOperationException>(() => native.Api.NewExact(native.ClassOf(native.Receiver), new[] { "System.Object" }, argument));
        Check(native.ObjectAllocations == 1 && native.Invocations == 1 && native.AllConstructorCallsRooted);
        Check(native.ActiveRoots == 0 && native.NewRoots == native.FreedRoots && owned.Count == 1);
        Throws<ObjectDisposedException>(() => _ = argument.Pointer);
    }

    private static void ManagedExceptionCleanup()
    {
        using var native = Constructor(n => n.Int32);
        native.ThrowOnInvoke = true;
        var argument = A.Value(9);
        Throws<InvalidOperationException>(() => native.Api.NewExact(native.ClassOf(native.Receiver), new[] { "System.Int32" }, argument));
        Check(native.ObjectAllocations == 1 && native.Invocations == 1 && native.ActiveRoots == 0 && native.NewRoots == native.FreedRoots);
        Throws<ObjectDisposedException>(() => _ = argument.Pointer);
    }

    private static void AllocationFailure()
    {
        using var native = Constructor(n => n.Object);
        native.FailObjectAllocation = true;
        native.OnObjectAllocation = () => Check(native.HasRootFor(native.OtherObject));
        var argument = A.Ref(native.OtherObject);
        Throws<OutOfMemoryException>(() => native.Api.NewExact(native.ClassOf(native.Receiver), new[] { "System.Object" }, argument));
        Check(native.ObjectAllocations == 1 && native.Invocations == 0 && native.ActiveRoots == 0 && native.NewRoots == native.FreedRoots);
        Throws<ObjectDisposedException>(() => _ = argument.Pointer);
    }

    private static void ThreadAccess()
    {
        using var native = Constructor(n => n.Int32);
        var argument = A.Value(9); Exception? failure = null;
        var thread = new Thread(() => {
            try { native.Api.NewExact(native.ClassOf(native.Receiver), new[] { "System.Int32" }, argument); }
            catch (Exception error) { failure = error; }
        });
        thread.Start(); thread.Join();
        Check(failure is InvalidOperationException && native.ObjectAllocations == 0 && native.Invocations == 0 && native.ActiveRoots == 0);
        Throws<ObjectDisposedException>(() => _ = argument.Pointer);
    }

    private static void InflatedConstructor()
    {
        using var native = Constructor(); native.GenericMethod = true; native.InflatedMethod = true;
        using var root = native.Api.NewExact(native.ClassOf(native.Receiver), Array.Empty<string>());
        Check(root.Target == native.LastObject && native.ActiveRoots == 1 && native.Invocations == 1);
    }

    private static void Check(bool condition)
    { if (!condition) throw new InvalidOperationException("Exact-construction invariant failed."); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private sealed class CountingLease : IDisposable
    {
        internal int Count;
        public void Dispose() => Count++;
    }
}
