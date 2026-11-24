using RizToolbox.Runtime;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

internal static class GuardedFieldPublicationRegression
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Guarded field publication dispatches after reference rooting and preserves caller owner", Success),
        ("Guarded field publication metadata and final guard rejection clean adopted inputs", Rejection),
        ("Guarded field publication thread and capacity rejection leave input ownership with caller", Admission),
        ("Guarded field publication retains unknown temporary reference free and original failure", UnknownTemporary),
        ("Guarded field publication retains unknown owned argument free without retry", UnknownArgument)
    };
    private static void Check(bool condition)
    { if (!condition) throw new InvalidOperationException("Guarded field publication check failed."); }
    private static T Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T error) { return error; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static void Success()
    {
        foreach (var reference in new[] { false, true })
        {
            using var n = new FakeNative(); n.FieldType = reference ? n.Object : n.Int32;
            using var owner = n.Api.Root(n.Instance);
            using var arg = reference ? A.Ref(n.OtherObject) : A.Value(17);
            var baseline = NativePublicationCall.RetainedCallCount; var guard = 0;
            n.Api.WriteExactGuarded(owner, n.ClassOf(n.Receiver), "field", reference ? "System.Object" : "System.Int32", arg, () =>
            {
                ++guard; Check(n.FieldWrites == 0 && n.ActiveRoots == (reference ? 2 : 1));
            });
            Check(guard == 1 && n.FieldWrites == 1 && n.ActiveRoots == 1 && owner.Target == n.Instance
                && n.AllFieldOwnersRooted && n.AllFieldReferencesRooted && NativePublicationCall.RetainedCallCount == baseline);
            Throws<ObjectDisposedException>(() => _ = arg.Pointer);
        }
    }
    private static void Rejection()
    {
        foreach (var metadata in new[] { false, true })
        {
            using var n = new FakeNative(); n.FieldType = n.Object;
            using var owner = n.Api.Root(n.Instance); using var arg = A.Ref(n.OtherObject);
            var guard = 0; var failure = new IOException("Guard rejected before field mutation.");
            var baseline = NativePublicationCall.RetainedCallCount;
            Exception? observed = null;
            try { n.Api.WriteExactGuarded(owner, n.ClassOf(n.Receiver), "field", metadata ? "System.String" : "System.Object", arg,
                () => { ++guard; throw failure; }); }
            catch (Exception error) { observed = error; }
            Check(metadata ? observed is UnsupportedSignatureException && guard == 0 : ReferenceEquals(observed, failure) && guard == 1);
            Check(n.FieldWrites == 0 && n.ActiveRoots == 1 && NativePublicationCall.RetainedCallCount == baseline);
            Throws<ObjectDisposedException>(() => _ = arg.Pointer);
        }
    }
    private static void Admission()
    {
        using var n = new FakeNative(); n.FieldType = n.Object;
        using var owner = n.Api.Root(n.Instance);
        using var arg = A.Reference(n.OtherObject, n.Api.Root(n.OtherObject));
        var baseline = NativePublicationCall.RetainedCallCount; var guard = 0;
        Task.Run(() => Throws<InvalidOperationException>(() => n.Api.WriteExactGuarded(owner, n.ClassOf(n.Receiver),
            "field", "System.Object", arg, () => ++guard))).GetAwaiter().GetResult();
        Check(guard == 0 && n.FieldWrites == 0 && arg.Pointer == n.OtherObject && n.ActiveRoots == 2);
        var held = new List<NativePublicationCall>();
        try
        {
            while (NativePublicationCall.RetainedCallCount < NativePublicationCall.MaximumCalls)
                held.Add(new NativePublicationCall(() => { }, Array.Empty<A>()));
            Throws<InvalidOperationException>(() => n.Api.WriteExactGuarded(owner, n.ClassOf(n.Receiver),
                "field", "System.Object", arg, () => ++guard));
            Check(guard == 0 && n.FieldWrites == 0 && arg.Pointer == n.OtherObject && n.ActiveRoots == 2);
        }
        finally { foreach (var call in held) call.Dispose(); }
        Check(NativePublicationCall.RetainedCallCount == baseline);
        n.Api.WriteExactGuarded(owner, n.ClassOf(n.Receiver), "field", "System.Object", arg, () => ++guard);
        Check(guard == 1 && n.FieldWrites == 1 && n.ActiveRoots == 1);
    }
    private static void UnknownTemporary()
    {
        foreach (var nativeFailure in new[] { false, true })
        {
            using var n = new FakeNative(); n.FieldType = n.Object; n.ThrowOnFieldWrite = nativeFailure;
            using var owner = n.Api.Root(n.Instance); using var arg = A.Ref(n.OtherObject);
            n.FailFreeHandle = 2;
            var calls = NativePublicationCall.RetainedCallCount; var roots = NativePublicationCall.RetainedRootRecordCount;
            var error = Throws<AggregateException>(() => n.Api.WriteExactGuarded(owner, n.ClassOf(n.Receiver),
                "field", "System.Object", arg, () => { }));
            Check(n.FieldWrites == 1 && n.ActiveRoots == 1 && owner.Target == n.Instance
                && NativePublicationCall.RetainedCallCount == calls + 1 && NativePublicationCall.RetainedRootRecordCount == roots + 1
                && error.InnerExceptions.Count == (nativeFailure ? 2 : 1));
            if (nativeFailure) Check(error.InnerExceptions[0].Message == "Synthetic native field setter failure.");
            var attempts = n.FreeHandleAttempts; arg.Dispose(); GC.Collect(); GC.WaitForPendingFinalizers();
            Check(n.FreeHandleAttempts == attempts);
        }
    }
    private static void UnknownArgument()
    {
        using var n = new FakeNative(); n.FieldType = n.Object;
        using var owner = n.Api.Root(n.Instance);
        using var arg = A.Reference(n.OtherObject, n.Api.Root(n.OtherObject)); n.FailFreeHandle = 2;
        var calls = NativePublicationCall.RetainedCallCount; var args = NativePublicationCall.RetainedArgumentRecordCount;
        Throws<AggregateException>(() => n.Api.WriteExactGuarded(owner, n.ClassOf(n.Receiver), "field", "System.Object", arg, () => { }));
        Check(n.FieldWrites == 1 && n.ActiveRoots == 1 && NativePublicationCall.RetainedCallCount == calls + 1
            && NativePublicationCall.RetainedArgumentRecordCount == args + 1);
        var attempts = n.FreeHandleAttempts; arg.Dispose(); GC.Collect(); GC.WaitForPendingFinalizers();
        Check(n.FreeHandleAttempts == attempts);
    }
}
