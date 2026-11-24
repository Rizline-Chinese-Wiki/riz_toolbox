using RizToolbox.Runtime;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

/// <summary>Actual bounded invoke implementation with controlled native exports;
/// proves root/argument ordering, not game setters or mutation rollback.</summary>
internal static class VoidPublicationRegression
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Void publication invokes exact instance and static metadata once and consumes all inputs", Success),
        ("Void publication rejects nonvoid byref open-generic and malformed inputs before native entry", MetadataRejections),
        ("Void publication rejects foreign threads before input consumption or process reservation", WrongThread),
        ("Void publication capacity rejection preserves caller input roots and value buffers", Capacity),
        ("Void publication preserves operation exceptions and independently consumes guarded inputs", NativeFailures),
        ("Void publication final guard follows temporary rooting and executes exactly once", FinalGuard),
        ("Void publication rejected final guard prevents dispatch and consumes adopted inputs", GuardRejection),
        ("Void publication invalid metadata never enters the final guard", GuardMetadataRejection),
        // Unknown records intentionally remain process-owned. No reset or retry.
        ("Void publication parameter-root free failure retains bounded responsibility and frees independent receiver", ParameterFreeFailure),
        ("Void publication receiver-root free failure preserves independent parameter and owned-input cleanup", ReceiverFreeFailure),
        ("Void publication unknown owned-argument free retains only its failed argument record without retry", ArgumentFreeFailure)
    };
    private static FakeNative Native(bool isStatic = false)
    {
        var native = new FakeNative { IsStatic = isStatic };
        native.Parameters = new[] { native.Object, native.Int32 }; return native;
    }
    private static A Owned(FakeNative native) => A.Reference(native.OtherObject, native.Api.Root(native.OtherObject));
    private static void Success()
    {
        foreach (var isStatic in new[] { false, true })
        {
            using var native = Native(isStatic); var baseline = NativePublicationCall.RetainedCallCount;
            var reference = Owned(native); var value = A.Value(7);
            native.Api.InvokeVoidWithPublication(native.Method, isStatic ? IntPtr.Zero : native.Instance, reference, value);
            Check(native.Invocations == 1 && native.ActiveRoots == 0 && native.NewRoots == native.FreedRoots);
            Check(NativePublicationCall.RetainedCallCount == baseline);
            Throws<ObjectDisposedException>(() => _ = reference.Pointer); Throws<ObjectDisposedException>(() => _ = value.Pointer);
        }
    }
    private static void MetadataRejections()
    {
        foreach (var kind in new[] { "scalar", "reference", "byref", "generic", "arity", "receiver", "null-method" })
        {
            using var native = Native(); var reference = Owned(native); var value = A.Value(7);
            if (kind == "scalar") native.ReturnType = native.Int32;
            if (kind == "reference") native.ReturnType = native.Object;
            if (kind == "byref") native.ReturnType = native.AddType("System.Void&", 1, true, 0, byRef: true);
            if (kind == "generic") native.GenericMethod = true;
            if (kind == "arity") native.Parameters = Array.Empty<IntPtr>();
            if (kind == "null-method")
                Throws<MissingMethodException>(() => native.Api.InvokeVoidWithPublication(IntPtr.Zero, native.Instance, reference, value));
            else
                Throws<UnsupportedSignatureException>(() => native.Api.InvokeVoidWithPublication(native.Method,
                    kind == "receiver" ? IntPtr.Zero : native.Instance, reference, value));
            Check(native.Invocations == 0 && native.ActiveRoots == 0);
            Throws<ObjectDisposedException>(() => _ = reference.Pointer); Throws<ObjectDisposedException>(() => _ = value.Pointer);
        }
    }
    private static void WrongThread()
    {
        using var native = Native(); var baseline = NativePublicationCall.RetainedCallCount;
        using var reference = Owned(native); using var value = A.Value(7);
        Task.Run(() => Throws<InvalidOperationException>(() => native.Api.InvokeVoidWithPublication(native.Method,
            native.Instance, reference, value))).GetAwaiter().GetResult();
        Check(NativePublicationCall.RetainedCallCount == baseline && native.Invocations == 0 && native.ActiveRoots == 1);
        Check(reference.Pointer == native.OtherObject && value.Pointer != IntPtr.Zero);
        native.Api.InvokeVoidWithPublication(native.Method, native.Instance, reference, value); Check(native.Invocations == 1);
    }
    private static void Capacity()
    {
        using var native = Native(); var baseline = NativePublicationCall.RetainedCallCount;
        var held = new List<NativePublicationCall>(); using var reference = Owned(native); using var value = A.Value(7);
        try
        {
            while (NativePublicationCall.RetainedCallCount < NativePublicationCall.MaximumCalls)
                held.Add(new NativePublicationCall(() => { }, Array.Empty<A>()));
            Throws<InvalidOperationException>(() => native.Api.InvokeVoidWithPublication(native.Method, native.Instance, reference, value));
            Check(native.Invocations == 0 && reference.Pointer == native.OtherObject && value.Pointer != IntPtr.Zero && native.ActiveRoots == 1);
        }
        finally { foreach (var call in held) call.Dispose(); }
        Check(NativePublicationCall.RetainedCallCount == baseline);
        native.Api.InvokeVoidWithPublication(native.Method, native.Instance, reference, value); Check(native.ActiveRoots == 0);
    }
    private static void NativeFailures()
    {
        foreach (var raw in new[] { false, true })
        {
            using var native = Native(); native.ThrowOnInvoke = raw; native.ExceptionOnInvoke = raw ? IntPtr.Zero : new IntPtr(99);
            var baseline = NativePublicationCall.RetainedCallCount; var reference = Owned(native); var value = A.Value(7);
            Throws<InvalidOperationException>(() => native.Api.InvokeVoidWithPublication(native.Method, native.Instance, reference, value));
            Check(native.Invocations == 1 && native.ActiveRoots == 0 && native.NewRoots == native.FreedRoots);
            Check(NativePublicationCall.RetainedCallCount == baseline);
            Throws<ObjectDisposedException>(() => _ = reference.Pointer); Throws<ObjectDisposedException>(() => _ = value.Pointer);
        }
    }
    private static void FinalGuard()
    {
        using var native = Native(); var reference = Owned(native); var value = A.Value(7);
        var baseline = NativePublicationCall.RetainedCallCount; var guards = 0;
        native.Api.InvokeVoidWithPublicationGuarded(native.Method, native.Instance, () =>
        {
            guards++;
            Check(native.Invocations == 0 && native.NewRoots == 3 && native.ActiveRoots == 3);
            Check(reference.Pointer == native.OtherObject && value.Pointer != IntPtr.Zero);
        }, reference, value);
        Check(guards == 1 && native.Invocations == 1 && native.ActiveRoots == 0
            && NativePublicationCall.RetainedCallCount == baseline);
    }
    private static void GuardRejection()
    {
        using var native = Native(); var reference = Owned(native); var value = A.Value(7);
        var baseline = NativePublicationCall.RetainedCallCount; var failure = new IOException("Final guard rejected.");
        Exception? observed = null;
        try { native.Api.InvokeVoidWithPublicationGuarded(native.Method, native.Instance, () => throw failure, reference, value); }
        catch (Exception error) { observed = error; }
        Check(ReferenceEquals(observed, failure) && native.Invocations == 0 && native.ActiveRoots == 0
            && native.NewRoots == native.FreedRoots && NativePublicationCall.RetainedCallCount == baseline);
        Throws<ObjectDisposedException>(() => _ = reference.Pointer); Throws<ObjectDisposedException>(() => _ = value.Pointer);
    }
    private static void GuardMetadataRejection()
    {
        using var native = Native(); native.ReturnType = native.Object;
        var reference = Owned(native); var value = A.Value(7); var guards = 0;
        Throws<UnsupportedSignatureException>(() => native.Api.InvokeVoidWithPublicationGuarded(native.Method,
            native.Instance, () => guards++, reference, value));
        Check(guards == 0 && native.Invocations == 0 && native.ActiveRoots == 0);
        Throws<ObjectDisposedException>(() => _ = reference.Pointer); Throws<ObjectDisposedException>(() => _ = value.Pointer);
    }
    private static void ParameterFreeFailure() => GuardFreeFailure(3);
    private static void ReceiverFreeFailure() => GuardFreeFailure(2);
    private static void GuardFreeFailure(uint handle)
    {
        using var native = Native(); var reference = Owned(native); var value = A.Value(7); native.FailFreeHandle = handle;
        var baseline = NativePublicationCall.RetainedCallCount; var roots = NativePublicationCall.RetainedRootRecordCount;
        Throws<AggregateException>(() => native.Api.InvokeVoidWithPublication(native.Method, native.Instance, reference, value));
        // This fixture removes a handle and then throws: the caller still cannot know
        // whether the native free happened, even though this controlled export did it.
        Check(native.Invocations == 1 && native.ActiveRoots == 0 && native.NewRoots == 3 && native.FreedRoots == 3);
        Check(NativePublicationCall.RetainedCallCount == baseline + 1 && NativePublicationCall.RetainedRootRecordCount == roots + 1);
        var attempts = native.FreeHandleAttempts; GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Check(native.FreeHandleAttempts == attempts);
        Throws<ObjectDisposedException>(() => _ = reference.Pointer); Throws<ObjectDisposedException>(() => _ = value.Pointer);
    }
    private static void ArgumentFreeFailure()
    {
        using var native = Native(); var reference = Owned(native); var value = A.Value(7); native.FailFreeHandle = 1;
        var baseline = NativePublicationCall.RetainedCallCount; var arguments = NativePublicationCall.RetainedArgumentRecordCount;
        Throws<AggregateException>(() => native.Api.InvokeVoidWithPublication(native.Method, native.Instance, reference, value));
        Check(native.Invocations == 1 && native.ActiveRoots == 0 && native.FreedRoots == 3);
        Check(NativePublicationCall.RetainedCallCount == baseline + 1 && NativePublicationCall.RetainedArgumentRecordCount == arguments + 1);
        var attempts = native.FreeHandleAttempts;
        reference.Dispose(); value.Dispose(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Check(native.FreeHandleAttempts == attempts);
    }
    private static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Void publication check failed."); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
