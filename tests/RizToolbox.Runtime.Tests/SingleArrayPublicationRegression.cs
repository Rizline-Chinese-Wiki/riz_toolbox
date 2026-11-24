using System.Runtime.InteropServices;
using RizToolbox.Runtime;

/// <summary>Exercises the real array/header copy path with synthetic exported metadata.</summary>
internal static class SingleArrayPublicationRegression
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Single-array publication transfers validated copied output before empty temporary cleanup", Success),
        ("Single-array publication rejects corrupt storage invalid inputs and wrong threads without publishing", Rejections),
        // Last: retain the unknown empty-root free record and process slot without retry/reset.
        ("Single-array publication preserves caller output when empty-root free has an unknown outcome", EmptyFreeFailure)
    };
    private static void Success()
    {
        using var native = new FakeNative(); var baseline = NativePublicationCall.RetainedCallCount;
        var samples = new[] { -.5f, 0f, .25f, 1f }; NativeRuntime.RootHandle? recorded = null;
        var returned = native.Api.CreateSingleArrayForClassWithPublication(samples, native.ClassOf(native.Single), root => {
            recorded = root; Check(native.ArrayAllocations == 2 && native.ActiveRoots == 2 && native.FreedRoots == 0);
            Check(root.Target == native.LastArray); CheckCopy(native, root, samples);
        });
        Check(ReferenceEquals(returned, recorded) && native.ActiveRoots == 1 && native.FreedRoots == 1);
        Check(NativePublicationCall.RetainedCallCount == baseline); CheckCopy(native, returned, samples);
        returned.Dispose(); Check(native.ActiveRoots == 0 && native.NewRoots == native.FreedRoots);
    }
    private static void Rejections()
    {
        using (var native = new FakeNative { CorruptArrayObjectSize = true })
        {
            var baseline = NativePublicationCall.RetainedCallCount; var notifications = 0;
            Throws<UnsupportedSignatureException>(() => native.Api.CreateSingleArrayForClassWithPublication(new[] { 1f }, native.ClassOf(native.Single), _ => notifications++));
            Check(notifications == 0 && native.ArrayAllocations == 2 && native.ActiveRoots == 0 && native.NewRoots == native.FreedRoots);
            Check(NativePublicationCall.RetainedCallCount == baseline);
        }
        using (var native = new FakeNative())
        {
            var notifications = 0; var baseline = NativePublicationCall.RetainedCallCount;
            Throws<ArgumentException>(() => native.Api.CreateSingleArrayForClassWithPublication(new[] { float.NaN }, native.ClassOf(native.Single), _ => notifications++));
            Throws<ArgumentNullException>(() => native.Api.CreateSingleArrayForClassWithPublication(new[] { 0f }, native.ClassOf(native.Single), null!));
            Exception? failed = null;
            var worker = new Thread(() => {
                try { Throws<InvalidOperationException>(() => native.Api.CreateSingleArrayForClassWithPublication(null!, IntPtr.Zero, null!)); }
                catch (Exception error) { failed = error; }
            });
            worker.Start(); worker.Join();
            Check(failed is null && notifications == 0 && native.ArrayAllocations == 0 && native.NewRoots == 0);
            Check(NativePublicationCall.RetainedCallCount == baseline);
        }
    }
    private static void EmptyFreeFailure()
    {
        using var native = new FakeNative(); native.FailFreeHandle = 1;
        var baseline = NativePublicationCall.RetainedCallCount; var roots = NativePublicationCall.RetainedRootRecordCount;
        var samples = new[] { .125f, -.25f }; NativeRuntime.RootHandle? recorded = null;
        Throws<AggregateException>(() => native.Api.CreateSingleArrayForClassWithPublication(samples, native.ClassOf(native.Single), root => recorded = root));
        Check(recorded is not null && recorded.Target == native.LastArray && native.ActiveRoots == 1);
        CheckCopy(native, recorded!, samples); Check(native.FreeHandleAttempts == 1);
        Check(NativePublicationCall.RetainedCallCount == baseline + 1 && NativePublicationCall.RetainedRootRecordCount == roots + 1);
        native.FailFreeHandle = 0; recorded!.Dispose(); var attempts = native.FreeHandleAttempts;
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Check(native.ActiveRoots == 0 && native.FreeHandleAttempts == attempts);
    }
    private static void CheckCopy(FakeNative native, NativeRuntime.RootHandle root, float[] expected)
    {
        var actual = new float[expected.Length]; Marshal.Copy(IntPtr.Add(root.Target, native.ArrayHeaderBytes), actual, 0, actual.Length);
        Check(actual.SequenceEqual(expected));
    }
    private static void Check(bool condition)
    { if (!condition) throw new InvalidOperationException("Single-array publication invariant failed."); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
}
