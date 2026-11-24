using RizToolbox.Runtime;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

internal static class ValuePublicationRegression
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Publication field reads copy validated scalar and reference tokens before temporary cleanup", FieldSuccess),
        ("Publication value invocation validates and copies boxed Boolean Int32 and Single results", InvokeSuccess),
        ("Publication reads and values reject foreign threads before reserving or consuming native inputs", WrongThread),
        ("Publication value layout and actual boxed-type mismatch reject without unsafe unboxing", ValueRejections),
        ("Publication value preflight reserves boxed-output capacity before a 128-reference invocation", RootBound),
        ("Scene handle publication copies only exact boxed Scene metadata and preserves the general struct guard", SceneHandle),
        ("Scene handle publication rejects wrong thread layout and result type before unsafe unboxing", SceneRejections),
        ("Text publication copies Unicode strings with a bounded temporary root and rejects nonstrings before character reads", TextPublication),
        ("Text publication rejects wrong threads and invalid lengths before copying characters", TextPublicationRejections),
        // Retained unknown roots below are intentional, with no reset or later retry.
        ("Publication field owner free failure retains its process record after copied read", FieldFreeFailure),
        ("Publication value boxed-output free failure still releases every independent receiver and input", BoxFreeFailure),
        ("Text publication retains an unknown root free once instead of returning apparent cleanup success", TextPublicationFreeFailure)
    };
    private static void FieldSuccess()
    {
        using var native = new FakeNative(); native.FieldType = native.Int32; native.FieldBytes = BitConverter.GetBytes(123);
        var baseline = NativePublicationCall.RetainedCallCount;
        Check(native.Api.ReadWithPublication<int>(native.Instance, "field") == 123 && native.ActiveRoots == 0 && native.FieldReads == 1);
        native.FieldType = native.Object; native.FieldBytes = BitConverter.GetBytes(123456L);
        Check(native.Api.ReadWithPublication<IntPtr>(native.Instance, "field") == new IntPtr(123456) && native.ActiveRoots == 0);
        Check(NativePublicationCall.RetainedCallCount == baseline);
    }
    private static void InvokeSuccess()
    {
        using var native = new FakeNative(); var baseline = NativePublicationCall.RetainedCallCount;
        native.ReturnType = native.Boolean; native.Result = native.Box(native.Boolean, new byte[] { 0, 255, 255, 255 });
        Check(!native.Api.InvokeValueWithPublication<bool>(native.Method, native.Instance));
        native.ReturnType = native.Int32; native.Result = native.Box(native.Int32, BitConverter.GetBytes(123));
        Check(native.Api.InvokeValueWithPublication<int>(native.Method, native.Instance) == 123);
        native.ReturnType = native.Single; native.Result = native.Box(native.Single, BitConverter.GetBytes(.625f));
        Check(native.Api.InvokeValueWithPublication<float>(native.Method, native.Instance) == .625f);
        Check(native.Invocations == 3 && native.ActiveRoots == 0 && native.NewRoots == native.FreedRoots);
        Check(NativePublicationCall.RetainedCallCount == baseline);
    }
    private static void WrongThread()
    {
        using var native = new FakeNative(); native.ReturnType = native.Int32; var baseline = NativePublicationCall.RetainedCallCount;
        using var input = A.Value(7);
        Task.Run(() => {
            Throws<InvalidOperationException>(() => native.Api.ReadWithPublication<int>(native.Instance, "field"));
            Throws<InvalidOperationException>(() => native.Api.InvokeValueWithPublication<int>(native.Method, native.Instance, input));
        }).GetAwaiter().GetResult();
        Check(input.Pointer != IntPtr.Zero && native.FieldReads == 0 && native.Invocations == 0 && native.NewRoots == 0);
        Check(NativePublicationCall.RetainedCallCount == baseline);
    }
    private static void ValueRejections()
    {
        using var native = new FakeNative(); native.ReturnType = native.Single;
        Throws<UnsupportedSignatureException>(() => native.Api.InvokeValueWithPublication<int>(native.Method, native.Instance));
        Check(native.Invocations == 0 && native.Unboxes == 0);
        native.ReturnType = native.Object;
        Throws<UnsupportedSignatureException>(() => native.Api.InvokeValueWithPublication<IntPtr>(native.Method, native.Instance));
        Check(native.Invocations == 0);
        native.ReturnType = native.Int32; native.Result = native.Box(native.Single, BitConverter.GetBytes(.5f));
        Throws<UnsupportedSignatureException>(() => native.Api.InvokeValueWithPublication<int>(native.Method, native.Instance));
        Check(native.Invocations == 1 && native.Unboxes == 0 && native.ActiveRoots == 0);
        native.FieldType = native.Single;
        Throws<UnsupportedSignatureException>(() => native.Api.ReadWithPublication<int>(native.Instance, "field"));
        Check(native.FieldReads == 0 && native.ActiveRoots == 0);
    }
    private static void RootBound()
    {
        using var native = new FakeNative(); native.ReturnType = native.Int32; native.Parameters = Enumerable.Repeat(native.Object, 128).ToArray();
        var inputs = Enumerable.Range(0, 128).Select(_ => A.Ref(native.OtherObject)).ToArray();
        Throws<UnsupportedSignatureException>(() => native.Api.InvokeValueWithPublication<int>(native.Method, native.Instance, inputs));
        Check(native.Invocations == 0 && native.NewRoots == 0);
        foreach (var input in inputs) Throws<ObjectDisposedException>(() => _ = input.Pointer);
    }
    private static void SceneHandle()
    {
        using var native = new FakeNative();
        var scene = native.AddStruct("UnityEngine.SceneManagement.Scene", 4, new NativeFieldLayout("m_Handle", "System.Int32", 0));
        native.ReturnType = scene; native.Result = native.Box(scene, BitConverter.GetBytes(-123));
        var before = NativePublicationCall.RetainedCallCount;
        Throws<UnsupportedSignatureException>(() => native.Api.InvokeValueWithPublication<int>(native.Method, native.Instance));
        Check(native.Invocations == 0);
        Check(native.Api.InvokeSceneHandleWithPublication(native.Method, native.Instance) == -123);
        Check(native.Invocations == 1 && native.NewRoots == native.FreedRoots && native.ActiveRoots == 0);
        Check(NativePublicationCall.RetainedCallCount == before);
    }
    private static void TextPublication()
    {
        using var native = new FakeNative(); var value = native.MakeString("场景 Level 🎵");
        var baseline = NativePublicationCall.RetainedCallCount;
        Check(native.Api.TextWithPublication(value) == "场景 Level 🎵");
        Check(native.NewRoots == 1 && native.FreedRoots == 1 && native.ActiveRoots == 0);
        Check(NativePublicationCall.RetainedCallCount == baseline && native.StringCharacterReads == 1);
        Check(native.Api.TextWithPublication(IntPtr.Zero) == "" && native.NewRoots == 1);
        var nonstring = native.Box(native.Int32, BitConverter.GetBytes(1));
        Throws<UnsupportedSignatureException>(() => native.Api.TextWithPublication(nonstring));
        Check(native.StringCharacterReads == 1 && native.StringLengthReads == 1 && native.ActiveRoots == 0);
    }
    private static void TextPublicationRejections()
    {
        using var native = new FakeNative(); var value = native.MakeString("Level");
        Task.Run(() => Throws<InvalidOperationException>(() => native.Api.TextWithPublication(value))).GetAwaiter().GetResult();
        Check(native.NewRoots == 0 && native.StringLengthReads == 0);
        foreach (var length in new[] { -1, 4 * 1024 * 1024 + 1 })
        {
            native.StringLengthOverride = length;
            Throws<UnsupportedSignatureException>(() => native.Api.TextWithPublication(value));
        }
        Check(native.StringCharacterReads == 0 && native.NewRoots == native.FreedRoots && native.ActiveRoots == 0);
    }
    private static void TextPublicationFreeFailure()
    {
        using var native = new FakeNative(); var value = native.MakeString("Level"); native.FailFreeHandle = 1;
        var calls = NativePublicationCall.RetainedCallCount; var roots = NativePublicationCall.RetainedRootRecordCount;
        Throws<AggregateException>(() => native.Api.TextWithPublication(value));
        Check(native.StringCharacterReads == 1 && native.FreeHandleAttempts == 1);
        Check(NativePublicationCall.RetainedCallCount == calls + 1 && NativePublicationCall.RetainedRootRecordCount == roots + 1);
        GC.Collect(); GC.WaitForPendingFinalizers(); Check(native.FreeHandleAttempts == 1);
    }
    private static void SceneRejections()
    {
        using var native = new FakeNative(); native.ReturnType = native.Int32;
        Throws<UnsupportedSignatureException>(() => native.Api.InvokeSceneHandleWithPublication(native.Method, native.Instance));
        Check(native.Invocations == 0 && native.Unboxes == 0);
        var bad = native.AddStruct("UnityEngine.SceneManagement.Scene", 4, new NativeFieldLayout("other", "System.Int32", 0));
        native.ReturnType = bad;
        Throws<UnsupportedSignatureException>(() => native.Api.InvokeSceneHandleWithPublication(native.Method, native.Instance));
        Check(native.Invocations == 0 && native.Unboxes == 0);
        var scene = native.AddStruct("UnityEngine.SceneManagement.Scene", 4, new NativeFieldLayout("m_Handle", "System.Int32", 0));
        native.ReturnType = scene; native.Result = native.Box(native.Int32, BitConverter.GetBytes(4));
        Throws<UnsupportedSignatureException>(() => native.Api.InvokeSceneHandleWithPublication(native.Method, native.Instance));
        Check(native.Invocations == 1 && native.Unboxes == 0 && native.ActiveRoots == 0);
        var roots = native.NewRoots;
        Task.Run(() => Throws<InvalidOperationException>(() => native.Api.InvokeSceneHandleWithPublication(native.Method, native.Instance))).GetAwaiter().GetResult();
        Check(native.Invocations == 1 && native.NewRoots == roots);
    }
    private static void FieldFreeFailure()
    {
        using var native = new FakeNative(); native.FieldType = native.Int32; native.FieldBytes = BitConverter.GetBytes(123); native.FailFreeHandle = 1;
        var baseline = NativePublicationCall.RetainedCallCount; var roots = NativePublicationCall.RetainedRootRecordCount;
        Throws<AggregateException>(() => native.Api.ReadWithPublication<int>(native.Instance, "field"));
        Check(native.FieldReads == 1 && native.FreeHandleAttempts == 1);
        Check(NativePublicationCall.RetainedCallCount == baseline + 1 && NativePublicationCall.RetainedRootRecordCount == roots + 1);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Check(native.FreeHandleAttempts == 1);
    }
    private static void BoxFreeFailure()
    {
        using var native = new FakeNative(); native.ReturnType = native.Int32; native.Parameters = new[] { native.Object };
        native.Result = native.Box(native.Int32, BitConverter.GetBytes(123)); native.FailFreeHandle = 3;
        var baseline = NativePublicationCall.RetainedCallCount; var roots = NativePublicationCall.RetainedRootRecordCount;
        var input = A.Ref(native.OtherObject);
        Throws<AggregateException>(() => native.Api.InvokeValueWithPublication<int>(native.Method, native.Instance, input));
        Check(native.Invocations == 1 && native.Unboxes == 1 && native.FreeHandleAttempts == 3 && native.ActiveRoots == 0);
        Check(NativePublicationCall.RetainedCallCount == baseline + 1 && NativePublicationCall.RetainedRootRecordCount == roots + 1);
        Throws<ObjectDisposedException>(() => _ = input.Pointer); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Check(native.FreeHandleAttempts == 3);
    }
    private static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Value publication check failed."); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
