using RizToolbox.Runtime;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

/// <summary>Controlled publication/cleanup ordering, not a Unity resource allocation or destruction test.</summary>
internal static class NativePublicationRegression
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Reference publication records output before receiver parameter and owned-argument cleanup", ReferenceSuccess),
        ("Reference publication never notifies on native exception slots or invocation exceptions", ReferenceExceptions),
        ("Reference publication rejects scalar void byref and incompatible signatures before native invocation", ReferenceRejections),
        ("Exact publication records a rooted allocation before constructor entry while arguments stay rooted", ExactSuccess),
        ("Exact publication preserves caller output responsibility after native and managed constructor exceptions", ExactConstructorFailures),
        ("Exact publication rejects malformed metadata and argument buffers before allocation or notification", ExactRejections),
        ("Publication seams reject foreign threads before consuming inputs or reserving process responsibility", ThreadAdmission),
        ("Publication fixed 128-argument 129-root bounds and 32-call capacity reject before native work", PublicationBounds),
        ("Ordinary Invoke and NewExact retain their existing output and failure cleanup contracts", OrdinaryCompatibility),
        // Last five preserve unknown responsibility. Never reset, force-free or retry these records.
        ("Publisher exceptions do not implicitly free notified outputs and retain bounded failed receipt responsibility", PublisherFailures),
        ("Reference publication preserves output and independently frees receiver after parameter-root free fails", ReferenceParameterFreeFailure),
        ("Reference publication preserves output and parameter cleanup after receiver-root free fails", ReferenceReceiverFreeFailure),
        ("Publication independently consumes owned arguments and retains only unknown argument-free records", OwnedArgumentFreeFailure),
        ("Exact publication preserves its output and argument guards after constructor receiver-root free fails", ExactReceiverFreeFailure)
    };

    private static FakeNative Reference(params Func<FakeNative, IntPtr>[] parameters)
    {
        var native = new FakeNative(); native.ReturnType = native.Object; native.Result = native.OtherObject;
        native.Parameters = parameters.Select(parameter => parameter(native)).ToArray(); return native;
    }
    private static FakeNative Constructor(params Func<FakeNative, IntPtr>[] parameters)
    {
        var native = new FakeNative { ConstructorAvailable = true };
        native.Parameters = parameters.Select(parameter => parameter(native)).ToArray(); return native;
    }
    private static void ReferenceSuccess()
    {
        using var native = Reference(n => n.Object); var baseline = NativePublicationCall.RetainedCallCount;
        var attached = native.Api.Root(native.OtherObject); var argument = A.Reference(native.OtherObject, attached);
        NativeRuntime.RootHandle? recorded = null; var notifications = 0;
        var result = native.Api.InvokeReferenceWithPublication(native.Method, native.Instance, pointer => {
            recorded = native.Api.Root(pointer); notifications++;
            Check(pointer == native.Result && native.Invocations == 1 && native.FreedRoots == 0);
            Check(argument.Pointer == native.OtherObject && attached.Target == native.OtherObject);
            Check(NativePublicationCall.RetainedCallCount == baseline + 1);
        }, argument);
        Check(result == native.Result && notifications == 1 && recorded!.Target == result);
        Check(native.ActiveRoots == 1 && native.NewRoots == 4 && native.FreedRoots == 3);
        Check(NativePublicationCall.RetainedCallCount == baseline);
        Throws<ObjectDisposedException>(() => _ = argument.Pointer); recorded!.Dispose(); Check(native.ActiveRoots == 0);
    }
    private static void ReferenceExceptions()
    {
        foreach (var rawThrow in new[] { false, true })
        {
            using var native = Reference(n => n.Int32); var baseline = NativePublicationCall.RetainedCallCount;
            native.ThrowOnInvoke = rawThrow; native.ExceptionOnInvoke = rawThrow ? IntPtr.Zero : new IntPtr(99);
            var notifications = 0; var argument = A.Value(1);
            Throws<InvalidOperationException>(() => native.Api.InvokeReferenceWithPublication(native.Method, native.Instance, _ => notifications++, argument));
            Check(notifications == 0 && native.Invocations == 1 && native.ActiveRoots == 0);
            Check(native.NewRoots == native.FreedRoots && NativePublicationCall.RetainedCallCount == baseline);
            Throws<ObjectDisposedException>(() => _ = argument.Pointer);
        }
    }
    private static void ReferenceRejections()
    {
        foreach (var returns in new Func<FakeNative, IntPtr>[] { n => n.Void, n => n.Int32,
            n => n.AddType("System.Object&", 28, false, IntPtr.Size, byRef: true) })
        {
            using var native = Reference(); native.ReturnType = returns(native); var notifications = 0;
            Throws<UnsupportedSignatureException>(() => native.Api.InvokeReferenceWithPublication(native.Method, native.Instance, _ => notifications++));
            Check(notifications == 0 && native.Invocations == 0 && native.NewRoots == 0);
        }
        using (var native = Reference(n => n.Int32))
        {
            var notifications = 0; var argument = A.Ref(native.OtherObject);
            Throws<UnsupportedSignatureException>(() => native.Api.InvokeReferenceWithPublication(native.Method, native.Instance, _ => notifications++, argument));
            Throws<ArgumentNullException>(() => native.Api.InvokeReferenceWithPublication(native.Method, native.Instance, null!));
            Check(notifications == 0 && native.Invocations == 0 && native.ActiveRoots == 0);
            Throws<ObjectDisposedException>(() => _ = argument.Pointer);
        }
    }
    private static void ExactSuccess()
    {
        using var native = Constructor(n => n.Object, n => n.Int32);
        var baseline = NativePublicationCall.RetainedCallCount; var notifications = 0;
        NativeRuntime.RootHandle? recorded = null;
        native.OnObjectAllocation = () => Check(native.HasRootFor(native.OtherObject));
        var reference = A.Ref(native.OtherObject); var number = A.Value(7);
        var returned = native.Api.NewExactWithPublication(native.ClassOf(native.Receiver), new[] { "System.Object", "System.Int32" }, root => {
            recorded = root; notifications++;
            Check(root.Target == native.LastObject && native.HasRootFor(root.Target));
            Check(native.ObjectAllocations == 1 && native.Invocations == 0 && native.FreedRoots == 0);
            Check(reference.Pointer == native.OtherObject && number.Pointer != IntPtr.Zero);
        }, reference, number);
        Check(ReferenceEquals(returned, recorded) && notifications == 1 && native.AllConstructorCallsRooted);
        Check(native.Invocations == 1 && native.ActiveRoots == 1 && native.NewRoots == 3 && native.FreedRoots == 2);
        Check(NativePublicationCall.RetainedCallCount == baseline);
        Throws<ObjectDisposedException>(() => _ = reference.Pointer); Throws<ObjectDisposedException>(() => _ = number.Pointer);
        returned.Dispose(); Check(native.ActiveRoots == 0);
    }
    private static void ExactConstructorFailures()
    {
        foreach (var rawThrow in new[] { false, true })
        {
            using var native = Constructor(); var baseline = NativePublicationCall.RetainedCallCount;
            native.ThrowOnInvoke = rawThrow; native.ExceptionOnInvoke = rawThrow ? IntPtr.Zero : new IntPtr(99);
            NativeRuntime.RootHandle? recorded = null; var notifications = 0;
            Throws<InvalidOperationException>(() => native.Api.NewExactWithPublication(native.ClassOf(native.Receiver), Array.Empty<string>(), root => {
                recorded = root; notifications++; Check(native.Invocations == 0);
            }));
            Check(notifications == 1 && recorded!.Target == native.LastObject && native.ActiveRoots == 1);
            Check(native.Invocations == 1 && native.AllConstructorCallsRooted && NativePublicationCall.RetainedCallCount == baseline);
            recorded!.Dispose(); Check(native.ActiveRoots == 0 && native.NewRoots == native.FreedRoots);
        }
    }
    private static void ExactRejections()
    {
        using (var native = Constructor(n => n.Object, n => n.Int32))
        {
            var notifications = 0; var reference = A.Ref(native.OtherObject); var wrong = A.Value(1f);
            Throws<UnsupportedSignatureException>(() => native.Api.NewExactWithPublication(native.ClassOf(native.Receiver),
                new[] { "System.Object", "System.Int32" }, _ => notifications++, reference, wrong));
            Check(notifications == 0 && native.ObjectAllocations == 0 && native.Invocations == 0 && native.ActiveRoots == 0);
            Throws<ObjectDisposedException>(() => _ = reference.Pointer); Throws<ObjectDisposedException>(() => _ = wrong.Pointer);
        }
        foreach (var invalid in new Action<FakeNative>[] { n => n.ConstructorExtraFlags = 0x400,
            n => n.ReturnType = n.Int32, n => n.GenericMethod = true,
            n => n.ReturnType = n.AddType("System.Void", 1, true, 0, byRef: true),
            n => n.ConstructorDeclaringClass = n.ClassOf(n.Object) })
        {
            using var native = Constructor(); invalid(native); var notifications = 0;
            Throws<UnsupportedSignatureException>(() => native.Api.NewExactWithPublication(native.ClassOf(native.Receiver), Array.Empty<string>(), _ => notifications++));
            Check(notifications == 0 && native.ObjectAllocations == 0 && native.Invocations == 0 && native.ActiveRoots == 0);
        }
        using (var native = Constructor())
        {
            Throws<ArgumentNullException>(() => native.Api.NewExactWithPublication(native.ClassOf(native.Receiver), Array.Empty<string>(), null!));
            Check(native.ObjectAllocations == 0 && native.Invocations == 0);
        }
    }
    private static void ThreadAdmission()
    {
        using var native = Constructor(n => n.Object); var baseline = NativePublicationCall.RetainedCallCount;
        var attached = native.Api.Root(native.OtherObject); var argument = A.Reference(native.OtherObject, attached);
        var notifications = 0; Exception? failure = null;
        var worker = new Thread(() => {
            try
            {
                Throws<InvalidOperationException>(() => native.Api.InvokeReferenceWithPublication(native.Method, native.Instance, _ => notifications++, argument));
                Throws<InvalidOperationException>(() => native.Api.NewExactWithPublication(native.ClassOf(native.Receiver), new[] { "System.Object" }, _ => notifications++, argument));
                Throws<InvalidOperationException>(() => native.Api.InvokeReferenceWithPublication(IntPtr.Zero, IntPtr.Zero, null!, null!));
            }
            catch (Exception error) { failure = error; }
        });
        worker.Start(); worker.Join(); Check(failure is null && notifications == 0);
        Check(native.ObjectAllocations == 0 && native.Invocations == 0 && native.ActiveRoots == 1 && native.FreedRoots == 0);
        Check(argument.Pointer == native.OtherObject && attached.Target == native.OtherObject && NativePublicationCall.RetainedCallCount == baseline);
        argument.Dispose(); Check(native.ActiveRoots == 0 && native.FreeHandleAttempts == 1);
    }
    private static void PublicationBounds()
    {
        using var native = Reference(); var baseline = NativePublicationCall.RetainedCallCount;
        native.Parameters = Enumerable.Repeat(native.Object, 128).ToArray();
        var arguments = Enumerable.Range(0, 128).Select(_ => A.Ref(native.OtherObject)).ToArray();
        NativeRuntime.RootHandle? recorded = null;
        native.Api.InvokeReferenceWithPublication(native.Method, native.Instance, pointer => recorded = native.Api.Root(pointer), arguments);
        Check(native.ActiveRoots == 1 && native.NewRoots == 130 && native.FreedRoots == 129); recorded!.Dispose();
        var extra = Enumerable.Range(0, 129).Select(_ => A.Ref(IntPtr.Zero)).ToArray();
        Throws<UnsupportedSignatureException>(() => native.Api.InvokeReferenceWithPublication(native.Method, native.Instance, _ => { }, extra));
        Check(native.Invocations == 1); foreach (var argument in extra) { Check(argument.Pointer == IntPtr.Zero); argument.Dispose(); }
        var calls = new List<NativePublicationCall>(); var ownerThread = Environment.CurrentManagedThreadId;
        Action verify = () => { if (Environment.CurrentManagedThreadId != ownerThread) throw new InvalidOperationException(); };
        try
        {
            for (var i = baseline; i < NativePublicationCall.MaximumCalls; i++) calls.Add(new(verify, Array.Empty<A>()));
            var callerRoot = native.Api.Root(native.OtherObject); var owned = A.Reference(native.OtherObject, callerRoot);
            var before = native.NewRoots;
            Throws<InvalidOperationException>(() => native.Api.InvokeReferenceWithPublication(native.Method, native.Instance, _ => { }, owned));
            Throws<InvalidOperationException>(() => native.Api.NewExactWithPublication(native.ClassOf(native.Receiver), Array.Empty<string>(), _ => { }));
            Check(native.NewRoots == before && native.Invocations == 1 && native.ObjectAllocations == 0);
            Check(owned.Pointer == native.OtherObject && callerRoot.Target == native.OtherObject); owned.Dispose();
        }
        finally { foreach (var call in calls) call.Dispose(); }
        Check(NativePublicationCall.RetainedCallCount == baseline && native.ActiveRoots == 0);
    }
    private static void OrdinaryCompatibility()
    {
        var baseline = NativePublicationCall.RetainedCallCount;
        using (var native = Reference())
        { Check(native.Api.Invoke(native.Method, native.Instance) == native.Result); Check(native.ActiveRoots == 0); }
        using (var native = Constructor())
        {
            var root = native.Api.NewExact(native.ClassOf(native.Receiver), Array.Empty<string>());
            Check(native.ActiveRoots == 1 && root.Target == native.LastObject); root.Dispose();
        }
        using (var native = Constructor())
        {
            native.ExceptionOnInvoke = new IntPtr(99);
            Throws<InvalidOperationException>(() => native.Api.NewExact(native.ClassOf(native.Receiver), Array.Empty<string>()));
            Check(native.ActiveRoots == 0 && native.NewRoots == native.FreedRoots);
        }
        Check(NativePublicationCall.RetainedCallCount == baseline);
    }
    private static void PublisherFailures()
    {
        using (var native = Reference())
        {
            var baseline = NativePublicationCall.RetainedCallCount; var unconfirmed = NativePublicationCall.UnconfirmedPublicationCount;
            NativeRuntime.RootHandle? recorded = null;
            Throws<IOException>(() => native.Api.InvokeReferenceWithPublication(native.Method, native.Instance, pointer => {
                recorded = native.Api.Root(pointer); throw new IOException("Publisher recorded before throwing.");
            }));
            Check(recorded!.Target == native.Result && native.ActiveRoots == 1 && native.Invocations == 1);
            Check(NativePublicationCall.RetainedCallCount == baseline + 1 && NativePublicationCall.UnconfirmedPublicationCount == unconfirmed + 1);
            recorded.Dispose(); var attempts = native.FreeHandleAttempts; Collect(); Check(native.FreeHandleAttempts == attempts && native.ActiveRoots == 0);
        }
        using (var native = Constructor())
        {
            var baseline = NativePublicationCall.RetainedCallCount; var unconfirmed = NativePublicationCall.UnconfirmedPublicationCount;
            NativeRuntime.RootHandle? recorded = null;
            Throws<IOException>(() => native.Api.NewExactWithPublication(native.ClassOf(native.Receiver), Array.Empty<string>(), root => {
                recorded = root; throw new IOException("Publisher recorded allocation before throwing.");
            }));
            Check(recorded!.Target == native.LastObject && native.ActiveRoots == 1 && native.Invocations == 0);
            Check(NativePublicationCall.RetainedCallCount == baseline + 1 && NativePublicationCall.UnconfirmedPublicationCount == unconfirmed + 1);
            recorded.Dispose(); var attempts = native.FreeHandleAttempts; Collect(); Check(native.FreeHandleAttempts == attempts && native.ActiveRoots == 0);
        }
    }
    private static void ReferenceParameterFreeFailure() => ReferenceRootFreeFailure(2);
    private static void ReferenceReceiverFreeFailure() => ReferenceRootFreeFailure(1);
    private static void ReferenceRootFreeFailure(uint failedHandle)
    {
        using var native = Reference(n => n.Object); native.FailFreeHandle = failedHandle;
        var baseline = NativePublicationCall.RetainedCallCount; var roots = NativePublicationCall.RetainedRootRecordCount;
        NativeRuntime.RootHandle? recorded = null; var argument = A.Ref(native.OtherObject);
        Throws<AggregateException>(() => native.Api.InvokeReferenceWithPublication(native.Method, native.Instance,
            pointer => recorded = native.Api.Root(pointer), argument));
        Check(recorded!.Target == native.Result && native.ActiveRoots == 1 && native.FreeHandleAttempts == 2);
        Check(NativePublicationCall.RetainedCallCount == baseline + 1 && NativePublicationCall.RetainedRootRecordCount == roots + 1);
        Throws<ObjectDisposedException>(() => _ = argument.Pointer);
        native.FailFreeHandle = 0; recorded.Dispose(); var attempts = native.FreeHandleAttempts; Collect();
        Check(native.ActiveRoots == 0 && native.FreeHandleAttempts == attempts);
    }
    private static void OwnedArgumentFreeFailure()
    {
        using var native = Reference(n => n.Object, n => n.Object); native.FailFreeHandle = 2;
        var first = A.Reference(native.OtherObject, native.Api.Root(native.OtherObject));
        var second = A.Reference(native.OtherObject, native.Api.Root(native.OtherObject));
        var baseline = NativePublicationCall.RetainedCallCount; var arguments = NativePublicationCall.RetainedArgumentRecordCount;
        NativeRuntime.RootHandle? recorded = null;
        Throws<AggregateException>(() => native.Api.InvokeReferenceWithPublication(native.Method, native.Instance,
            pointer => recorded = native.Api.Root(pointer), first, second));
        Check(recorded!.Target == native.Result && native.ActiveRoots == 1 && native.FreeHandleAttempts == 5);
        Check(NativePublicationCall.RetainedCallCount == baseline + 1 && NativePublicationCall.RetainedArgumentRecordCount == arguments + 1);
        Throws<ObjectDisposedException>(() => _ = first.Pointer); Throws<ObjectDisposedException>(() => _ = second.Pointer);
        native.FailFreeHandle = 0; recorded.Dispose(); var attempts = native.FreeHandleAttempts; Collect();
        Check(native.FreeHandleAttempts == attempts && native.ActiveRoots == 0);
    }
    private static void ExactReceiverFreeFailure()
    {
        using var native = Constructor(n => n.Object); native.FailFreeHandle = 3;
        var baseline = NativePublicationCall.RetainedCallCount; var roots = NativePublicationCall.RetainedRootRecordCount;
        NativeRuntime.RootHandle? recorded = null; var argument = A.Ref(native.OtherObject);
        Throws<AggregateException>(() => native.Api.NewExactWithPublication(native.ClassOf(native.Receiver), new[] { "System.Object" },
            root => recorded = root, argument));
        Check(recorded!.Target == native.LastObject && native.ActiveRoots == 1 && native.Invocations == 1 && native.AllConstructorCallsRooted);
        Check(native.NewRoots == 3 && native.FreeHandleAttempts == 2);
        Check(NativePublicationCall.RetainedCallCount == baseline + 1 && NativePublicationCall.RetainedRootRecordCount == roots + 1);
        Throws<ObjectDisposedException>(() => _ = argument.Pointer);
        native.FailFreeHandle = 0; recorded.Dispose(); var attempts = native.FreeHandleAttempts; Collect();
        Check(native.FreeHandleAttempts == attempts && native.ActiveRoots == 0);
    }
    private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
    private static void Check(bool condition)
    { if (!condition) throw new InvalidOperationException("Native publication invariant failed."); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
}
