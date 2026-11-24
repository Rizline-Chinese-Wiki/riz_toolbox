using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using static Assert;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

/// <summary>Uses the actual bundle allocation helpers and actual runtime call seams
/// against controlled native exports. Does not create Unity Texture/Sprite/Audio.</summary>
internal static class NativeBundlePublicationArgumentsRegression
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("Bundle reference publication clears recorded inputs only after runtime consumption succeeds", ReferenceSuccess),
        ("Bundle exact constructor publication clears recorded inputs after rooted construction succeeds", ConstructorSuccess),
        ("Bundle allocation admission preserves caller inputs on foreign threads occupied slots and stopped owners", Admission),
        ("Publication input records enforce thread null bound and in-flight admission before accepting ownership", RecordAdmission),
        // Failures retain actual process-owned bundles and runtime responsibility. No reset.
        ("Bundle runtime quota rejection preserves owned string-like root and value buffers before native entry", QuotaFailure),
        ("Bundle temporary native cleanup failure retains output and input records without retrying frees", CleanupFailure)
    };
    private static FakeNative Reference()
    {
        var native = new FakeNative { IsStatic = true }; native.ReturnType = native.Object; native.Result = native.OtherObject;
        native.Parameters = new[] { native.Object, native.Int32 }; return native;
    }
    private static A OwnedInput(FakeNative native) => A.Reference(native.OtherObject, native.Api.Root(native.OtherObject));
    private static void Finish(OwnedNativeLocalPlayBundle owner)
    {
        owner.Dispose(); Check(owner.TryCompleteDestroy()); Check(owner.IsClosed);
    }
    private static void ReferenceSuccess()
    {
        using var main = new MainThreadDispatcher(); using var native = Reference();
        var owner = new OwnedNativeLocalPlayBundle(native.Api, main); var reference = OwnedInput(native); var value = A.Value(7);
        owner.InvokeRoot(3, native.Method, reference, value);
        Equal(1, native.Invocations); Equal(1, owner.DirectRootRecords); Equal(0, owner.RetainedCallArgumentRecords);
        Check(!owner.CleanupUnknown); Throws<ObjectDisposedException>(() => _ = reference.Pointer);
        Throws<ObjectDisposedException>(() => _ = value.Pointer); Finish(owner); Equal(0, native.ActiveRoots);
    }
    private static void ConstructorSuccess()
    {
        using var main = new MainThreadDispatcher(); using var native = new FakeNative { ConstructorAvailable = true };
        native.Parameters = new[] { native.Object, native.Int32 };
        var owner = new OwnedNativeLocalPlayBundle(native.Api, main); var reference = OwnedInput(native); var value = A.Value(7);
        owner.ConstructRoot(4, native.ClassOf(native.Receiver), new[] { "System.Object", "System.Int32" }, reference, value);
        Equal(1, native.Invocations); Check(native.AllConstructorCallsRooted); Equal(1, owner.DirectRootRecords);
        Equal(0, owner.RetainedCallArgumentRecords); Throws<ObjectDisposedException>(() => _ = reference.Pointer);
        Throws<ObjectDisposedException>(() => _ = value.Pointer); Finish(owner); Equal(0, native.ActiveRoots);
    }
    private static void Admission()
    {
        using var main = new MainThreadDispatcher(); using var native = Reference();
        var owner = new OwnedNativeLocalPlayBundle(native.Api, main); using var reference = OwnedInput(native); using var value = A.Value(7);
        Task.Run(() => {
            Throws<InvalidOperationException>(() => owner.InvokeRoot(3, native.Method, reference, value));
            Throws<InvalidOperationException>(() => owner.ConstructRoot(4, native.ClassOf(native.Receiver), Array.Empty<string>(), reference));
        }).GetAwaiter().GetResult();
        Equal(native.OtherObject, reference.Pointer); Check(value.Pointer != IntPtr.Zero);
        Equal(0, native.Invocations); Equal(0, owner.RetainedCallArgumentRecords); Check(!owner.CleanupUnknown);
        owner.InvokeRoot(3, native.Method, A.Ref(native.OtherObject), A.Value(1));
        Throws<InvalidOperationException>(() => owner.InvokeRoot(3, native.Method, reference, value));
        Equal(native.OtherObject, reference.Pointer); Check(value.Pointer != IntPtr.Zero); Equal(1, native.Invocations);
        Finish(owner);
        Throws<InvalidOperationException>(() => owner.ConstructRoot(4, native.ClassOf(native.Receiver), Array.Empty<string>(), reference));
        Equal(native.OtherObject, reference.Pointer); Equal(0, owner.RetainedCallArgumentRecords);
    }
    private static void RecordAdmission()
    {
        using var main = new MainThreadDispatcher(); var records = new NativePublicationArgumentRecords(main);
        using var caller = A.Value(1);
        Task.Run(() => Throws<InvalidOperationException>(() => records.Begin(new[] { caller }))).GetAwaiter().GetResult();
        Throws<ArgumentNullException>(() => records.Begin(null!));
        Throws<ArgumentException>(() => records.Begin(new A[] { null! }));
        Throws<ArgumentException>(() => records.Begin(Enumerable.Repeat(caller, 129).ToArray()));
        Equal(0, records.Count); Check(caller.Pointer != IntPtr.Zero);
        records.Begin(new[] { caller }); Equal(1, records.Count);
        Throws<InvalidOperationException>(() => records.Begin(Array.Empty<A>()));
        Task.Run(() => {
            Throws<InvalidOperationException>(() => records.Complete());
            Throws<InvalidOperationException>(() => records.Fail());
        }).GetAwaiter().GetResult();
        Equal(1, records.Count); Check(!records.CleanupUnknown);
        caller.Dispose(); records.Complete(); Equal(0, records.Count);
        records.Begin(Array.Empty<A>()); records.Fail(); Check(records.CleanupUnknown);
        Throws<InvalidOperationException>(() => records.Complete());
        Throws<InvalidOperationException>(() => records.Begin(Array.Empty<A>()));
    }
    private static void QuotaFailure()
    {
        using var main = new MainThreadDispatcher(); using var native = Reference();
        var owner = new OwnedNativeLocalPlayBundle(native.Api, main); var reference = OwnedInput(native); var value = A.Value(7);
        var reservations = new List<NativePublicationCall>();
        try
        {
            while (NativePublicationCall.RetainedCallCount < NativePublicationCall.MaximumCalls)
                reservations.Add(new NativePublicationCall(main.VerifyAccess, Array.Empty<A>()));
            Throws<InvalidOperationException>(() => owner.InvokeRoot(3, native.Method, reference, value));
        }
        finally { foreach (var reservation in reservations) reservation.Dispose(); }
        Check(owner.CleanupUnknown); Equal(2, owner.RetainedCallArgumentRecords); Equal(0, owner.DirectRootRecords);
        Equal(0, native.Invocations); Equal(native.OtherObject, reference.Pointer); Check(value.Pointer != IntPtr.Zero);
        Equal(1, native.ActiveRoots); owner.Dispose(); Check(!owner.TryCompleteDestroy());
        using var caller = A.Value(2); Throws<InvalidOperationException>(() => owner.InvokeRoot(3, native.Method, caller));
        Check(caller.Pointer != IntPtr.Zero); Equal(0, native.Invocations); Equal(2, owner.RetainedCallArgumentRecords);
        // The process-owned owner retains these unconsumed arguments. No force-free.
    }
    private static void CleanupFailure()
    {
        using var main = new MainThreadDispatcher(); using var native = Reference();
        var owner = new OwnedNativeLocalPlayBundle(native.Api, main); var reference = OwnedInput(native); var value = A.Value(7);
        native.FailFreeHandle = 2; // handle1 is the input's own root; handle2 is the call's parameter guard.
        Throws<AggregateException>(() => owner.InvokeRoot(3, native.Method, reference, value));
        Check(owner.CleanupUnknown); Equal(2, owner.RetainedCallArgumentRecords); Equal(1, owner.DirectRootRecords);
        Equal(1, native.Invocations); var attempts = native.FreeHandleAttempts;
        Throws<ObjectDisposedException>(() => _ = reference.Pointer); Throws<ObjectDisposedException>(() => _ = value.Pointer);
        owner.Dispose(); Check(!owner.TryCompleteDestroy()); Check(!owner.TryCompleteDestroy());
        Equal(attempts, native.FreeHandleAttempts); Equal(2, owner.RetainedCallArgumentRecords); Check(!owner.IsClosed);
    }
}
