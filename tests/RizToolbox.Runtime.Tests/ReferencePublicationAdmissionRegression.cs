using RizToolbox.Runtime;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

internal static class ReferencePublicationAdmissionRegression
{
    internal static readonly (string Name, Action Run)[] Cases =
    {
        ("Reference admission records dispatch only after complete metadata and temporary rooting", Ordering),
        ("Reference admission rejects metadata and final guard before original dispatch without attempt", BeforeDispatch),
        ("Reference admission publishes a returned output before a withdrawing post-call guard", AfterDispatch),
        ("Reference admission preserves original exception and unknown temporary cleanup responsibility", Failures)
    };
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("Reference admission invariant failed."); }
    private static T Expect<T>(Action action) where T : Exception
    { try { action(); } catch (T error) { return error; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static FakeNative Reference(bool parameter = false)
    {
        var value = new FakeNative(); value.Api.BindCurrentThread(); value.ReturnType = value.Object;
        value.Result = value.OtherObject; value.Parameters = parameter ? new[] { value.Object } : Array.Empty<IntPtr>(); return value;
    }
    private static void Ordering()
    {
        using var n = Reference(true); var baseline = NativePublicationCall.RetainedCallCount;
        var guards = 0; var dispatch = 0; var publication = 0; NativeRuntime.RootHandle? output = null;
        var result = n.Api.InvokeReferenceWithPublicationAdmission(n.Method, n.Instance, pointer =>
        { Check(dispatch == 1 && n.Invocations == 1 && publication++ == 0); output = n.Api.Root(pointer); },
            () => ++guards, () =>
            { Check(guards > 2 && n.Invocations == 0 && n.HasRootFor(n.Instance) && n.HasRootFor(n.OtherObject)); ++dispatch; },
            A.Ref(n.OtherObject));
        Check(result == n.OtherObject && dispatch == 1 && publication == 1 && output!.Target == n.OtherObject
            && n.ActiveRoots == 1 && NativePublicationCall.RetainedCallCount == baseline);
        output!.Dispose(); Check(n.ActiveRoots == 0 && n.NewRoots == n.FreedRoots);
    }
    private static void BeforeDispatch()
    {
        using (var n = Reference())
        {
            var dispatch = 0; var publication = 0; var argument = A.Value(1);
            Expect<UnsupportedSignatureException>(() => n.Api.InvokeReferenceWithPublicationAdmission(n.Method, n.Instance,
                _ => ++publication, () => { }, () => ++dispatch, argument));
            Check(dispatch == 0 && publication == 0 && n.Invocations == 0 && n.ActiveRoots == 0);
            Expect<ObjectDisposedException>(() => _ = argument.Pointer);
        }
        using (var n = Reference())
        {
            var failure = new IOException("Controlled last prepared guard failure."); var dispatch = 0; var guards = 0;
            var actual = Expect<IOException>(() => n.Api.InvokeReferenceWithPublicationAdmission(n.Method, n.Instance, _ => throw new Exception(),
                () => { ++guards; if (n.ActiveRoots != 0) throw failure; }, () => ++dispatch));
            Check(ReferenceEquals(actual, failure) && guards > 1 && dispatch == 0 && n.Invocations == 0
                && n.ActiveRoots == 0 && n.NewRoots == n.FreedRoots);
        }
    }
    private static void AfterDispatch()
    {
        using var n = Reference(); var failure = new IOException("Controlled withdrawn post-invoke source.");
        var dispatch = 0; var publications = 0; NativeRuntime.RootHandle? output = null;
        var baseline = NativePublicationCall.RetainedCallCount;
        var actual = Expect<IOException>(() => n.Api.InvokeReferenceWithPublicationAdmission(n.Method, n.Instance,
            pointer => { output = n.Api.Root(pointer); ++publications; }, () => { if (n.Invocations != 0) throw failure; }, () => ++dispatch));
        Check(ReferenceEquals(actual, failure) && dispatch == 1 && n.Invocations == 1 && publications == 1
            && output!.Target == n.OtherObject && n.ActiveRoots == 1 && NativePublicationCall.RetainedCallCount == baseline);
        output!.Dispose(); Check(n.ActiveRoots == 0);
    }
    private static readonly List<FakeNative> UnknownOwners = new();
    private static void Failures()
    {
        using (var n = Reference())
        {
            n.ExceptionOnInvoke = new(99); var dispatch = 0; var publications = 0;
            Expect<InvalidOperationException>(() => n.Api.InvokeReferenceWithPublicationAdmission(n.Method, n.Instance,
                _ => ++publications, () => { }, () => ++dispatch));
            Check(n.Invocations == 1 && dispatch == 1 && publications == 0 && n.ActiveRoots == 0);
        }
        var native = Reference(); var baseline = NativePublicationCall.RetainedCallCount;
        var temporary = NativePublicationCall.RetainedRootRecordCount; var dispatches = 0; var published = 0;
        native.FailFreeHandle = 1; NativeRuntime.RootHandle? output = null; UnknownOwners.Add(native);
        var error = Expect<AggregateException>(() => native.Api.InvokeReferenceWithPublicationAdmission(native.Method, native.Instance,
            pointer => { output = native.Api.Root(pointer); ++published; }, () => { }, () => ++dispatches));
        // This fake removes the native handle before throwing. The unknown
        // cleanup record must stay retained even though only the output is
        // still physically rooted; a later Dispose cannot prove that free.
        Check(error.Data["NativePublicationCleanupOwner"] is NativePublicationCall && dispatches == 1 && published == 1
            && native.Invocations == 1 && native.ActiveRoots == 1 && NativePublicationCall.RetainedCallCount == baseline + 1
            && NativePublicationCall.RetainedRootRecordCount == temporary + 1 && native.FreeHandleAttempts == 1);
        output!.Dispose(); Check(native.ActiveRoots == 0 && native.FreeHandleAttempts == 2);
        var attempts = native.FreeHandleAttempts;
        Expect<InvalidOperationException>(((NativePublicationCall)error.Data["NativePublicationCleanupOwner"]!).Dispose);
        Check(native.FreeHandleAttempts == attempts && NativePublicationCall.RetainedRootRecordCount == temporary + 1);

        var failed = Reference(); failed.ThrowOnInvoke = true; failed.FailFreeHandle = 1; UnknownOwners.Add(failed);
        var failedDispatches = 0; var failedPublications = 0;
        var combined = Expect<AggregateException>(() => failed.Api.InvokeReferenceWithPublicationAdmission(failed.Method, failed.Instance,
            _ => ++failedPublications, () => { }, () => ++failedDispatches));
        Check(combined.InnerExceptions.Count == 2 && combined.InnerExceptions[0] is InvalidOperationException
            && combined.InnerExceptions[0].Message == "Synthetic managed invocation failure."
            && combined.InnerExceptions[1] is IOException && combined.Data["NativePublicationCleanupOwner"] is NativePublicationCall
            && failed.Invocations == 1 && failedDispatches == 1 && failedPublications == 0 && failed.ActiveRoots == 0
            && failed.FreeHandleAttempts == 1 && NativePublicationCall.RetainedCallCount == baseline + 2
            && NativePublicationCall.RetainedRootRecordCount == temporary + 2);
    }
}
