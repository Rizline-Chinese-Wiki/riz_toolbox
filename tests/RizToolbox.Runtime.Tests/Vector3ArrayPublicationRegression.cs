using System.Runtime.InteropServices;
using RizToolbox.Runtime;

/// <summary>Runs the production Vector3[] export, copy and publication path over
/// synthetic IL2CPP metadata and real unmanaged array storage.</summary>
internal static class Vector3ArrayPublicationRegression
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Vector3 array copies exact XYZ bits and publishes a pinned output before temporary cleanup", CopyAndPublish),
        ("Vector3 array snapshots caller input before export callbacks can mutate it", InputMutation),
        ("Vector3 array rejects foreign metadata and corrupt storage before publication", Rejections),
        ("Vector3 array stops native exports when caller admission is withdrawn", GuardWithdrawal),
        ("Vector3 array refuses worker access before caller callbacks and native exports", Worker),
        ("Vector3 array keeps publication responsibility after publisher exception", PublisherFailure),
        // Last: unknown native free outcomes intentionally retain the process owner.
        ("Vector3 array preserves published output and unknown empty-root cleanup responsibility", EmptyRootFreeFailure)
    };

    private static NativeVector3ArraySample[] Samples() => new[] {
        new NativeVector3ArraySample(1.25f, -2.5f, BitConverter.Int32BitsToSingle(unchecked((int)0x80000000))),
        new NativeVector3ArraySample(-.125f, 32.5f, 7.75f)
    };
    private static int[] ExpectedBits() => new[] {
        BitConverter.SingleToInt32Bits(1.25f), BitConverter.SingleToInt32Bits(-2.5f), unchecked((int)0x80000000),
        BitConverter.SingleToInt32Bits(-.125f), BitConverter.SingleToInt32Bits(32.5f), BitConverter.SingleToInt32Bits(7.75f)
    };
    private static void CheckCopy(FakeNative native, NativeRuntime.RootHandle root, int[] bits)
    {
        var target = root.Target;
        for (var i = 0; i < bits.Length; i++)
            Check(Marshal.ReadInt32(target, native.ArrayHeaderBytes + i * 4) == bits[i]);
        Check(Marshal.ReadByte(target, native.ArrayHeaderBytes - 1) == 0x5a);
        Check(Marshal.ReadByte(target, native.ArrayHeaderBytes + bits.Length * 4) == 0x5a);
    }
    private static void CopyAndPublish()
    {
        using var native = new FakeNative(); native.ArrayHeaderBytes = 48;
        var api = native.Vector3ArrayCaptureApi(); var baseline = NativePublicationCall.RetainedCallCount;
        NativeRuntime.RootHandle? published = null;
        var output = api.CreateVector3ArrayWithPublication(Samples(), () => { }, root => {
            published = root;
            Check(native.ArrayAllocations == 2 && native.PinnedRoots == 2 && native.ActiveRoots == 2);
            CheckCopy(native, root, ExpectedBits());
        });
        Check(ReferenceEquals(output, published) && native.ActiveRoots == 1 && native.FreedRoots == 1);
        CheckCopy(native, output, ExpectedBits());
        Check(NativePublicationCall.RetainedCallCount == baseline);
        output.Dispose(); Check(native.ActiveRoots == 0 && native.NewRoots == native.FreedRoots);
    }
    private static void InputMutation()
    {
        using var native = new FakeNative(); var api = native.Vector3ArrayCaptureApi();
        var input = Samples(); var before = ExpectedBits();
        native.DuringVectorArrayClass = () => input[0] = new NativeVector3ArraySample(100f, 200f, 300f);
        NativeRuntime.RootHandle? published = null;
        var output = api.CreateVector3ArrayForClassWithPublication(input, native.Vector3ArrayElementClass,
            () => { }, root => { published = root; CheckCopy(native, root, before); });
        Check(ReferenceEquals(output, published)); CheckCopy(native, output, before);
        output.Dispose(); Check(native.ActiveRoots == 0);
    }
    private static void Rejections()
    {
        Reject(n => n.WrongVectorImage = true, 0);
        Reject(n => n.WrongVectorFieldOffset = true, 0);
        Reject(n => n.WrongVectorFieldParent = true, 0);
        Reject(n => n.WrongVectorRank = true, 0);
        Reject(n => n.WrongVectorElement = true, 0);
        Reject(n => n.ArrayElementBytes = 4, 0);
        Reject(n => n.CorruptArrayByteLength = true, 2);
        Reject(n => n.CorruptArrayObjectSize = true, 2);
        Reject(n => n.ReturnWrongArrayClass = true, 1);
        using var native = new FakeNative(); var api = native.Vector3ArrayCaptureApi();
        var notifications = 0;
        Throws<ArgumentException>(() => api.CreateVector3ArrayWithPublication(
            new[] { new NativeVector3ArraySample(float.NaN, 0, 0) }, () => { }, _ => notifications++));
        Throws<ArgumentNullException>(() => api.CreateVector3ArrayWithPublication(Samples(), () => { }, null!));
        Throws<UnsupportedSignatureException>(() => api.CreateVector3ArrayForClassWithPublication(
            Samples(), native.ClassOf(native.Single), () => { }, _ => notifications++));
        Check(notifications == 0 && native.ArrayAllocations == 0 && native.NewRoots == 0);
    }
    private static void Reject(Action<FakeNative> mutate, int expectedAllocations)
    {
        using var native = new FakeNative(); var api = native.Vector3ArrayCaptureApi();
        mutate(native); var baseline = NativePublicationCall.RetainedCallCount; var notifications = 0;
        Throws<UnsupportedSignatureException>(() => api.CreateVector3ArrayForClassWithPublication(
            Samples(), native.Vector3ArrayElementClass, () => { }, _ => notifications++));
        Check(notifications == 0 && native.ArrayAllocations == expectedAllocations
            && native.ActiveRoots == 0 && native.NewRoots == native.FreedRoots
            && NativePublicationCall.RetainedCallCount == baseline);
    }
    private static void GuardWithdrawal()
    {
        using var native = new FakeNative(); var api = native.Vector3ArrayCaptureApi();
        var admitted = true; var exportsAtWithdrawal = -1L;
        native.DuringVectorArrayClass = () => { admitted = false; exportsAtWithdrawal = native.LowerIl2CppCalls; };
        native.BeginLowerIl2CppAudit();
        try
        {
            Throws<InvalidOperationException>(() => api.CreateVector3ArrayWithPublication(Samples(),
                () => { if (!admitted) throw new InvalidOperationException("Source scope withdrawn."); }, _ => throw new Exception("Published after withdrawal.")));
        }
        finally { native.EndLowerIl2CppAudit(); }
        Check(exportsAtWithdrawal > 0 && native.LowerIl2CppCalls == exportsAtWithdrawal
            && native.ArrayAllocations == 0 && native.NewRoots == 0);

        using var afterRoot = new FakeNative(); var rootedApi = afterRoot.Vector3ArrayCaptureApi();
        var rootAdmission = true; var published = 0;
        afterRoot.DuringVectorRootCreation = () => rootAdmission = false;
        Throws<InvalidOperationException>(() => rootedApi.CreateVector3ArrayWithPublication(Samples(),
            () => { if (!rootAdmission) throw new InvalidOperationException("Source scope withdrawn after pinning."); },
            _ => published++));
        Check(published == 0 && afterRoot.ArrayAllocations == 1 && afterRoot.NewRoots == 1
            && afterRoot.FreedRoots == 1 && afterRoot.ActiveRoots == 0);

        using var duringTarget = new FakeNative(); var targetApi = duringTarget.Vector3ArrayCaptureApi();
        var targetAdmission = true; var targetNotifications = 0; var targetCallbacks = 0;
        duringTarget.DuringHandleTarget = _ => { targetCallbacks++; targetAdmission = false; };
        Throws<InvalidOperationException>(() => targetApi.CreateVector3ArrayWithPublication(Samples(),
            () => { if (!targetAdmission) throw new InvalidOperationException("Source scope withdrawn during rooted target lookup."); },
            _ => targetNotifications++));
        Check(targetCallbacks == 1 && targetNotifications == 0
            && duringTarget.ArrayAllocations == 1 && duringTarget.NewRoots == 1
            && duringTarget.FreedRoots == 1 && duringTarget.ActiveRoots == 0);
    }
    private static void Worker()
    {
        using var native = new FakeNative(); var api = native.Vector3ArrayCaptureApi();
        var guards = 0; Exception? failure = null;
        native.BeginLowerIl2CppAudit();
        try
        {
            var worker = new Thread(() => {
                try { api.CreateVector3ArrayWithPublication(Samples(), () => guards++, _ => { }); }
                catch (Exception error) { failure = error; }
            });
            worker.Start(); worker.Join();
        }
        finally { native.EndLowerIl2CppAudit(); }
        Check(failure is InvalidOperationException && guards == 0 && native.LowerIl2CppCalls == 0
            && native.ArrayAllocations == 0 && native.NewRoots == 0);
    }
    private static void PublisherFailure()
    {
        using var native = new FakeNative(); var api = native.Vector3ArrayCaptureApi();
        var calls = NativePublicationCall.RetainedCallCount;
        var unconfirmed = NativePublicationCall.UnconfirmedPublicationCount;
        NativeRuntime.RootHandle? published = null;
        Throws<InvalidOperationException>(() => api.CreateVector3ArrayWithPublication(Samples(), () => { }, root => {
            published = root; throw new InvalidOperationException("Publisher failed after receiving output.");
        }));
        Check(published is not null && native.ActiveRoots == 1 && native.FreedRoots == 1);
        CheckCopy(native, published!, ExpectedBits());
        Check(NativePublicationCall.RetainedCallCount == calls + 1
            && NativePublicationCall.UnconfirmedPublicationCount == unconfirmed + 1);
        published!.Dispose(); Check(native.ActiveRoots == 0 && native.NewRoots == native.FreedRoots);
    }
    private static void EmptyRootFreeFailure()
    {
        using var native = new FakeNative(); var api = native.Vector3ArrayCaptureApi();
        native.FailFreeHandle = 1;
        var calls = NativePublicationCall.RetainedCallCount;
        var roots = NativePublicationCall.RetainedRootRecordCount;
        NativeRuntime.RootHandle? published = null;
        var failure = Catch<AggregateException>(() => api.CreateVector3ArrayWithPublication(
            Samples(), () => { }, root => published = root));
        Check(failure.Data.Contains("NativePublicationCleanupOwner") && published is not null);
        CheckCopy(native, published!, ExpectedBits());
        Check(native.ActiveRoots == 1 && native.FreeHandleAttempts == 1
            && NativePublicationCall.RetainedCallCount == calls + 1
            && NativePublicationCall.RetainedRootRecordCount == roots + 1);
        native.FailFreeHandle = 0; published!.Dispose();
        var attempts = native.FreeHandleAttempts;
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Check(native.ActiveRoots == 0 && native.FreeHandleAttempts == attempts);
    }
    private static void Check(bool condition)
    { if (!condition) throw new InvalidOperationException("Vector3 array publication invariant failed."); }
    private static void Throws<T>(Action action) where T : Exception => _ = Catch<T>(action);
    private static T Catch<T>(Action action) where T : Exception
    {
        try { action(); } catch (T error) { return error; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
}
