using RizToolbox.Runtime;
using RizToolbox.Adapter.Gameplay;

// Reuse only the existing synthetic SEC_IMAGE helper to validate executable
// module/RVA membership. It loads no game and executes no mapped machine code.
internal static partial class ListenerMetadataRegression
{
    internal static readonly (string Name, Action Run)[] LineSetPositionsCases = {
        ("SetPositions exact ABI dispatches the original array with an explicit shorter count", SetPositionsExplicitCount),
        ("SetPositions rejects foreign count ABI and another overload RVA", SetPositionsSignatureRefusal),
        ("SetPositions rejects unreadable enumerated MethodInfo before method exports", SetPositionsUnreadableMethod),
        ("SetPositions rejects a count beyond actual original scratch capacity", SetPositionsCapacity),
        ("SetPositions publishes admission before rechecking callback receiver drift", SetPositionsCallbackDrift),
        ("SetPositions retains entry and original roots after native fields precede an exception", SetPositionsExceptionalFields),
        ("SetPositions refuses worker invocation before guards or native exports", SetPositionsWorker),
        ("SetPositions profile rejects guard reentry and releases its gate on caller withdrawal", SetPositionsProfileGuardReentry),
        ("SetPositions preserves completed dispatch when exact temporary pin cleanup is unknown", SetPositionsCleanup)
    };

    private static void SetPositionsExplicitCount()
    {
        using var image = new Image(); using var n = new FakeNative();
        var api = n.SetPositionsApi(image.Address);
        using (n.SetLineRoot) using (n.SetArrayRoot)
        {
            var signature = api.InspectLineSetPositionsSignature(n.SetLineClass, image.Address, () => { });
            var admission = 0;
            api.InvokeLineSetPositionsWithPublicationAdmission(signature, n.SetLineRoot, n.SetArrayRoot,
                1, () => { }, () => { }, () => { }, () => {
                    Check(n.Invocations == 0 && n.ActiveRoots >= 4); ++admission;
                });
            Check(admission == 1 && n.Invocations == 1 && n.SetAllArgumentsRooted
                && n.SetSavedArray == n.SetArrayObject && n.SetSavedCount == 1 && n.ActiveRoots == 2);
        }
        Check(n.ActiveRoots == 0);
    }

    private static void SetPositionsSignatureRefusal()
    {
        using var image = new Image(); using var n = new FakeNative();
        var api = n.SetPositionsApi(image.Address);
        using (n.SetLineRoot) using (n.SetArrayRoot)
        {
            n.SetParameterOverride = n.Single;
            Throws<UnsupportedSignatureException>(() => api.InspectLineSetPositionsSignature(n.SetLineClass, image.Address, () => { }));
            n.SetParameterOverride = IntPtr.Zero;
            n.SetMethodWrongRva(image.Address);
            Throws<UnsupportedSignatureException>(() => api.InspectLineSetPositionsSignature(n.SetLineClass, image.Address, () => { }));
            Check(n.Invocations == 0);
        }
    }

    private static void SetPositionsCapacity()
    {
        using var image = new Image(); using var n = new FakeNative();
        var api = n.SetPositionsApi(image.Address);
        using (n.SetLineRoot) using (n.SetArrayRoot)
        {
            var signature = api.InspectLineSetPositionsSignature(n.SetLineClass, image.Address, () => { });
            var admission = 0;
            Throws<ArgumentOutOfRangeException>(() => api.InvokeLineSetPositionsWithPublicationAdmission(signature,
                n.SetLineRoot, n.SetArrayRoot, 4, () => { }, () => { }, () => { }, () => ++admission));
            Check(n.Invocations == 0 && admission == 0 && n.SetSavedArray == IntPtr.Zero && n.ActiveRoots == 2);
        }
    }

    private static void SetPositionsUnreadableMethod()
    {
        using var image = new Image(); using var n = new FakeNative();
        var api = n.SetPositionsApi(image.Address);
        using (n.SetLineRoot) using (n.SetArrayRoot)
        {
            n.SetUnreadableMethod = true;
            Throws<UnsupportedSignatureException>(() => api.InspectLineSetPositionsSignature(n.SetLineClass, image.Address, () => { }));
            Check(n.SetMethodNameReads == 0 && n.Invocations == 0);
        }
    }

    private static void SetPositionsCallbackDrift()
    {
        using var image = new Image(); using var n = new FakeNative();
        var api = n.SetPositionsApi(image.Address);
        using (n.SetLineRoot) using (n.SetArrayRoot)
        {
            var signature = api.InspectLineSetPositionsSignature(n.SetLineClass, image.Address, () => { });
            Exception? failure = null;
            try { api.InvokeLineSetPositionsWithPublicationAdmission(signature, n.SetLineRoot, n.SetArrayRoot,
                1, () => { }, () => { }, () => { }, n.SetLineForeignClass); }
            catch (Exception error) { failure = error; }
            Check(failure is UnsupportedSignatureException && n.Invocations == 0
                && failure.Data["LineSetPositionsNativeEntered"] is false
                && ReferenceEquals(failure.Data["LineSetPositionsOriginalLineRoot"], n.SetLineRoot));
        }
    }

    private static void SetPositionsExceptionalFields()
    {
        using var image = new Image(); using var n = new FakeNative();
        var api = n.SetPositionsApi(image.Address);
        using (n.SetLineRoot) using (n.SetArrayRoot)
        {
            var signature = api.InspectLineSetPositionsSignature(n.SetLineClass, image.Address, () => { });
            n.SetExceptionAfterFields = true;
            Exception? failure = null;
            try { api.InvokeLineSetPositionsWithPublicationAdmission(signature, n.SetLineRoot, n.SetArrayRoot,
                2, () => { }, () => { }, () => { }, () => { }); }
            catch (Exception error) { failure = error; }
            Check(failure is InvalidOperationException && n.Invocations == 1
                && n.SetSavedArray == n.SetArrayObject && n.SetSavedCount == 2
                && failure.Data["LineSetPositionsNativeEntered"] is true
                && failure.Data["LineSetPositionsReturned"] is false
                && ReferenceEquals(failure.Data["LineSetPositionsOriginalArrayRoot"], n.SetArrayRoot));
        }
    }

    private static void SetPositionsWorker()
    {
        using var image = new Image(); using var n = new FakeNative();
        var api = n.SetPositionsApi(image.Address);
        using (n.SetLineRoot) using (n.SetArrayRoot)
        {
            var signature = api.InspectLineSetPositionsSignature(n.SetLineClass, image.Address, () => { });
            Exception? failure = null; var guards = 0;
            n.BeginLowerIl2CppAudit();
            try
            {
                var worker = new Thread(() => {
                    try { api.InvokeLineSetPositionsWithPublicationAdmission(signature, n.SetLineRoot, n.SetArrayRoot,
                        1, () => ++guards, () => ++guards, () => ++guards, () => ++guards); }
                    catch (Exception error) { failure = error; }
                });
                worker.Start(); worker.Join();
            }
            finally { n.EndLowerIl2CppAudit(); }
            Check(failure is InvalidOperationException && guards == 0 && n.LowerIl2CppCalls == 0 && n.Invocations == 0);
        }
    }

    private static void SetPositionsCleanup()
    {
        using var image = new Image(); using var n = new FakeNative();
        var api = n.SetPositionsApi(image.Address);
        using (n.SetLineRoot) using (n.SetArrayRoot)
        {
            var signature = api.InspectLineSetPositionsSignature(n.SetLineClass, image.Address, () => { });
            Exception? failure = null;
            try { api.InvokeLineSetPositionsWithPublicationAdmission(signature, n.SetLineRoot, n.SetArrayRoot,
                1, () => { }, () => { }, () => { }, () => n.FailFreeHandle = n.SetArrayTemporaryPin); }
            catch (Exception error) { failure = error; }
            Check(failure is AggregateException && n.Invocations == 1 && n.ActiveRoots == 2
                && n.SetSavedArray == n.SetArrayObject && n.SetSavedCount == 1
                && failure.Data["LineSetPositionsNativeEntered"] is true
                && failure.Data["LineSetPositionsReturned"] is true
                && failure.Data["NativePublicationCleanupOwner"] is not null);
        }
        var attempts = n.FreeHandleAttempts;
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Check(n.ActiveRoots == 0 && n.FreeHandleAttempts == attempts);
    }

    private static void SetPositionsProfileGuardReentry()
    {
        // Only exercise the managed gate, never admit a fake module/body.
        // The guard always throws before any module or metadata operation.
        var type = typeof(NativeSeekLineSetPositionsNativeProfile);
        var profile = (NativeSeekLineSetPositionsNativeProfile)
            System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
        type.GetField("_thread", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(profile, Environment.CurrentManagedThreadId);
        var marker = new InvalidOperationException("Caller withdrew before module verification.");
        Exception? outer = null, nested = null;
        var callbacks = 0; var nestedCallbacks = 0;
        try
        {
            profile.VerifyCurrent(() => {
                ++callbacks;
                try { profile.VerifyCurrent(() => ++nestedCallbacks); }
                catch (Exception error) { nested = error; }
                throw marker;
            });
        }
        catch (Exception error) { outer = error; }
        Check(ReferenceEquals(outer, marker) && nested is InvalidOperationException
            && callbacks == 1 && nestedCallbacks == 0);
        // A caller withdrawal must release the gate without permanently
        // faulting otherwise unobserved profile metadata.
        Exception? subsequent = null;
        try { profile.VerifyCurrent(() => throw marker); }
        catch (Exception error) { subsequent = error; }
        Check(ReferenceEquals(subsequent, marker));
    }
}
