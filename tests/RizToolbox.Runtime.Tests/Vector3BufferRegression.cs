using System.Runtime.InteropServices;
using RizToolbox.Runtime;

/// <summary>Calls the production rooted Vector3[] reader/writer through fake
/// IL2CPP exports backed by one physical unmanaged array.</summary>
internal static class Vector3BufferRegression
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Vector3 buffer writes only the original array prefix and restores every original bit", PrefixAndRestore),
        ("Vector3 buffer empty prefix observes without entering a store", EmptyPrefix),
        ("Vector3 buffer refuses a stale complete expected array", StaleCompleteExpected),
        ("Vector3 buffer refuses foreign metadata, headers and storage bounds", MetadataAndBounds),
        ("Vector3 buffer refuses source and before-store callback drift", CallbackDrift),
        ("Vector3 buffer refuses read-only and execute-only pages withdrawn before store", ProtectionWithdrawal),
        ("Vector3 buffer records original ownership when admission withdraws after store", GuardWithdrawalAfterStore),
        ("Vector3 buffer refuses worker access before callbacks or native exports", Worker),
        // Unknown frees retain process responsibility; keep these last.
        ("Vector3 buffer preserves completed store observation after unknown temporary free", SuccessfulStoreCleanupUnknown),
        ("Vector3 buffer preserves operation and original ownership after unknown temporary free", FailedOperationCleanupUnknown)
    };

    private static readonly NativeVector3BufferBits[] Original = {
        new(unchecked((int)0x7fc12345), unchecked((int)0x80000000), 0x3f800000),
        new(unchecked((int)0xc0200000), 0x40200000, 0),
        new(unchecked((int)0x7fa54321), unchecked((int)0x80000000), 0x3e800000),
        new(0x41200000, unchecked((int)0xff812345), 0x3f000000)
    };
    private static readonly NativeVector3BufferBits[] Forward = {
        new(0x3f400000, 0x40000000, 0x40400000),
        new(0x40800000, 0x40a00000, 0x40c00000)
    };

    private static void PrefixAndRestore()
    {
        using var native = new FakeNative(); native.ArrayHeaderBytes = 48;
        var (api, original, cls) = native.CaptureVector3Buffer(Original);
        using (original)
        using (var alias = native.AliasVector3BufferRoot())
        {
            var physical = original.Target;
            Check(alias.Target == physical && native.Vector3BufferArray == physical);
            var history = api.ReadRootedLineVector3Buffer(original, cls, 4, () => { });
            Check(!history.AtomicSnapshot && history.Array == physical && history.Class == cls
                && history.ElementClass == native.Vector3ArrayElementClass && history.Length == 4);
            Bits(history.Items, Original);
            var next = api.WriteRootedLineVector3BufferPrefix(original, cls, history, Forward,
                () => { }, () => { }, () => { });
            var expectedForward = Forward.Concat(Original.Skip(2)).ToArray();
            Check(next.StoreEntered && !next.AtomicConditionalWrite && !next.ExternalConsumersKnown
                && next.PrefixLength == 2 && next.Before.Array == physical && next.After.Array == physical
                && alias.Target == physical && native.ArrayAllocations == 1 && native.ActiveRoots == 2);
            Bits(next.Before.Items, Original); Bits(next.After.Items, expectedForward);
            PhysicalBits(native, expectedForward);
            // The completed observation is history. The restore uses its complete
            // after-state as its expected prestate and raw, non-finite old prefix.
            var restored = api.WriteRootedLineVector3BufferPrefix(alias, cls, next.After,
                Original.Take(2).ToArray(), () => { }, () => { }, () => { });
            Check(restored.StoreEntered && restored.Before.Array == physical && restored.After.Array == physical);
            Bits(restored.Before.Items, expectedForward); Bits(restored.After.Items, Original);
            PhysicalBits(native, Original); Bits(history.Items, Original);
            Bits(next.Before.Items, Original); Bits(next.After.Items, expectedForward);
            Check(Marshal.ReadByte(physical, native.ArrayHeaderBytes - 1) == 0x5a
                && Marshal.ReadByte(physical, native.ArrayHeaderBytes + Original.Length * 12) == 0x5a);
        }
        Check(native.ActiveRoots == 0 && native.NewRoots == native.FreedRoots);
    }

    private static void EmptyPrefix()
    {
        using var native = new FakeNative();
        var (api, original, cls) = native.CaptureVector3Buffer(Original);
        using (original)
        {
            var expected = api.ReadRootedLineVector3Buffer(original, cls, 4, () => { });
            var beforeStore = 0; var sourceChecks = 0;
            var observation = api.WriteRootedLineVector3BufferPrefix(original, cls, expected,
                Array.Empty<NativeVector3BufferBits>(), () => { }, () => sourceChecks++, () => beforeStore++);
            Check(!observation.StoreEntered && observation.PrefixLength == 0 && beforeStore == 0
                && sourceChecks == 1 && ReferenceEquals(observation.Before, observation.After));
            Bits(observation.Before.Items, Original); PhysicalBits(native, Original);
            Check(native.ArrayAllocations == 1 && native.ActiveRoots == 1);
        }
        Check(native.ActiveRoots == 0);
    }

    private static void StaleCompleteExpected()
    {
        using var native = new FakeNative();
        var (api, original, cls) = native.CaptureVector3Buffer(Original);
        using (original)
        {
            var expected = api.ReadRootedLineVector3Buffer(original, cls, 4, () => { });
            var foreign = Catch<NativeVector3BufferConflict>(() => api.WriteRootedLineVector3BufferPrefix(
                original, cls, expected with { Array = IntPtr.Zero }, Forward,
                () => { }, () => { }, () => throw new InvalidOperationException("Store callback reached.")));
            Check(foreign.Data["LineVector3BufferStoreEntered"] is false
                && ReferenceEquals(foreign.Data["LineVector3BufferOriginalRoot"], original));
            PhysicalBits(native, Original);
            var drift = Original.ToArray();
            drift[2] = drift[2] with { X = unchecked((int)0x7fa54322) };
            native.WriteVector3BufferItem(2, drift[2]);
            var beforeStore = 0;
            var failure = Catch<NativeVector3BufferConflict>(() => api.WriteRootedLineVector3BufferPrefix(
                original, cls, expected, Forward, () => { }, () => { }, () => beforeStore++));
            Check(beforeStore == 0 && ReferenceEquals(failure.Data["LineVector3BufferOriginalRoot"], original)
                && failure.Data["LineVector3BufferStoreEntered"] is false);
            Check(failure.Data["LineVector3BufferBefore"] is NativeRootedVector3Buffer observed
                && observed.Array == original.Target);
            Bits(expected.Items, Original); PhysicalBits(native, drift);
        }
        Check(native.ActiveRoots == 0);
    }

    private static void MetadataAndBounds()
    {
        RejectRead(n => n.WrongVectorImage = true);
        RejectRead(n => n.WrongVectorFieldOffset = true);
        RejectRead(n => n.WrongVectorRank = true);
        RejectRead(n => n.WrongVectorElement = true);
        RejectRead(n => n.ArrayElementBytes = 4);
        RejectRead(n => n.ReportWrongVector3BufferClass());
        RejectRead(n => n.PatchVector3BufferHeaderClass(n.ClassOf(n.Single)));
        RejectRead(n => n.Vector3BufferHeaderSizeOverride = 8);
        RejectRead(n => n.Vector3BufferHeaderSizeOverride = 4097);
        RejectRead(n => n.Vector3BufferHeaderSizeOverride = 44);
        RejectRead(n => n.CorruptArrayByteLength = true);
        RejectRead(n => n.CorruptArrayObjectSize = true);
        RejectRead(n => n.Vector3BufferLengthExtra = 1);
        RejectRead(_ => { }, maximum: 2);
        using var native = new FakeNative();
        var (api, original, cls) = native.CaptureVector3Buffer(Original);
        using (original)
        {
            Throws<UnsupportedSignatureException>(() => api.ReadRootedLineVector3Buffer(
                original, native.ClassOf(native.Single), 4, () => { }));
            Throws<ArgumentOutOfRangeException>(() => api.ReadRootedLineVector3Buffer(
                original, cls, NativeRuntime.MaximumLineVector3BufferLength + 1, () => { }));
            PhysicalBits(native, Original);
        }
    }

    private static void RejectRead(Action<FakeNative> mutate, int maximum = 4)
    {
        using var native = new FakeNative();
        var (api, original, cls) = native.CaptureVector3Buffer(Original);
        using (original)
        {
            mutate(native);
            Throws<UnsupportedSignatureException>(() => api.ReadRootedLineVector3Buffer(original, cls, maximum, () => { }));
            PhysicalBits(native, Original);
            Check(native.ActiveRoots == 1 && native.NewRoots == native.FreedRoots + 1);
        }
        Check(native.ActiveRoots == 0);
    }

    private static void CallbackDrift()
    {
        using (var native = new FakeNative())
        {
            var (api, original, cls) = native.CaptureVector3Buffer(Original);
            using (original)
            {
                var expected = api.ReadRootedLineVector3Buffer(original, cls, 4, () => { });
                var drift = Original.ToArray(); drift[0] = drift[0] with { Y = 0x40e00000 };
                var sourceChecks = 0; var beforeStore = 0;
                var failure = Catch<NativeVector3BufferConflict>(() => api.WriteRootedLineVector3BufferPrefix(
                    original, cls, expected, Forward, () => { }, () => {
                        if (++sourceChecks == 2) native.WriteVector3BufferItem(0, drift[0]);
                    }, () => beforeStore++));
                Check(sourceChecks == 2 && beforeStore == 0
                    && failure.Data["LineVector3BufferStoreEntered"] is false
                    && ReferenceEquals(failure.Data["LineVector3BufferOriginalRoot"], original));
                PhysicalBits(native, drift); Bits(expected.Items, Original);
            }
        }
        using (var native = new FakeNative())
        {
            var (api, original, cls) = native.CaptureVector3Buffer(Original);
            using (original)
            {
                var expected = api.ReadRootedLineVector3Buffer(original, cls, 4, () => { });
                var drift = Original.ToArray(); drift[1] = drift[1] with { Z = 0x40e00000 };
                var sourceChecks = 0; var beforeStore = 0;
                var failure = Catch<NativeVector3BufferConflict>(() => api.WriteRootedLineVector3BufferPrefix(
                    original, cls, expected, Forward, () => { }, () => sourceChecks++, () => {
                        beforeStore++; native.WriteVector3BufferItem(1, drift[1]);
                    }));
                Check(sourceChecks == 3 && beforeStore == 1
                    && failure.Data["LineVector3BufferStoreEntered"] is false
                    && failure.Data["LineVector3BufferBefore"] is NativeRootedVector3Buffer
                    && ReferenceEquals(failure.Data["LineVector3BufferOriginalRoot"], original));
                PhysicalBits(native, drift); Bits(expected.Items, Original);
            }
        }
    }

    private static void GuardWithdrawalAfterStore()
    {
        using var native = new FakeNative();
        var (api, original, cls) = native.CaptureVector3Buffer(Original);
        using (original)
        {
            var expected = api.ReadRootedLineVector3Buffer(original, cls, 4, () => { });
            var beforeStore = 0;
            var failure = Catch<InvalidOperationException>(() => api.WriteRootedLineVector3BufferPrefix(
                original, cls, expected, Forward,
                () => {
                    if (native.ReadVector3BufferItem(0).X == Forward[0].X)
                        throw new InvalidOperationException("Admission withdrawn after the native store.");
                }, () => { }, () => beforeStore++));
            var before = failure.Data["LineVector3BufferBefore"] as NativeRootedVector3Buffer;
            Check(beforeStore == 1 && failure.Data["LineVector3BufferStoreEntered"] is true
                && ReferenceEquals(failure.Data["LineVector3BufferOriginalRoot"], original)
                && before is not null);
            Bits(before!.Items, Original);
            PhysicalBits(native, Forward.Concat(Original.Skip(2)).ToArray());
        }
    }

    private static void ProtectionWithdrawal()
    {
        foreach (var protection in new uint[] { 0x02, 0x10 }) // PAGE_READONLY, PAGE_EXECUTE
        {
            using var native = new FakeNative();
            var (api, original, cls, page) = native.CapturePageVector3Buffer(Original);
            using (page)
            using (original)
            {
                var expected = api.ReadRootedLineVector3Buffer(original, cls, 4, () => { });
                var beforeStore = 0; uint previous = 0;
                try
                {
                    var failure = Catch<UnsupportedSignatureException>(() => api.WriteRootedLineVector3BufferPrefix(
                        original, cls, expected, Forward, () => { }, () => { }, () => {
                            beforeStore++; previous = page.Protect(protection);
                        }));
                    Check(beforeStore == 1 && previous == 0x04
                        && failure.Data["LineVector3BufferStoreEntered"] is false
                        && ReferenceEquals(failure.Data["LineVector3BufferOriginalRoot"], original)
                        && failure.Data["LineVector3BufferBefore"] is NativeRootedVector3Buffer);
                }
                finally { if (previous != 0) page.Protect(previous); }
                PhysicalBits(native, Original);
                Check(native.ActiveRoots == 1);
            }
            Check(native.ActiveRoots == 0);
        }
    }

    private static void Worker()
    {
        using var native = new FakeNative();
        var (api, original, cls) = native.CaptureVector3Buffer(Original);
        using (original)
        {
            var expected = api.ReadRootedLineVector3Buffer(original, cls, 4, () => { });
            var callbacks = 0; var roots = native.NewRoots; Exception? readFailure = null, writeFailure = null;
            native.BeginLowerIl2CppAudit();
            try
            {
                var worker = new Thread(() => {
                    try { api.ReadRootedLineVector3Buffer(original, cls, 4, () => callbacks++); }
                    catch (Exception error) { readFailure = error; }
                    try { api.WriteRootedLineVector3BufferPrefix(original, cls, expected, Forward,
                        () => callbacks++, () => callbacks++, () => callbacks++); }
                    catch (Exception error) { writeFailure = error; }
                });
                worker.Start(); worker.Join();
            }
            finally { native.EndLowerIl2CppAudit(); }
            Check(readFailure is InvalidOperationException && writeFailure is InvalidOperationException
                && callbacks == 0 && native.LowerIl2CppCalls == 0 && native.LowerWorkerIl2CppCalls == 0
                && native.NewRoots == roots);
            PhysicalBits(native, Original);
        }
    }

    private static void SuccessfulStoreCleanupUnknown()
    {
        using var native = new FakeNative();
        var (api, original, cls) = native.CaptureVector3Buffer(Original);
        using (original)
        {
            var expected = api.ReadRootedLineVector3Buffer(original, cls, 4, () => { });
            var calls = NativePublicationCall.RetainedCallCount;
            var retainedRoots = NativePublicationCall.RetainedRootRecordCount;
            native.FailFreeHandle = checked((uint)(native.NewRoots + 1));
            var failure = Catch<AggregateException>(() => api.WriteRootedLineVector3BufferPrefix(
                original, cls, expected, Forward, () => { }, () => { }, () => { }));
            var before = failure.Data["LineVector3BufferBefore"] as NativeRootedVector3Buffer;
            var completed = failure.Data["LineVector3BufferWriteObservation"] as NativeVector3BufferWriteObservation;
            Check(failure.Data.Contains("NativePublicationCleanupOwner")
                && ReferenceEquals(failure.Data["LineVector3BufferOriginalRoot"], original)
                && failure.Data["LineVector3BufferStoreEntered"] is true
                && before is not null && completed is not null);
            Bits(before!.Items, Original); Bits(completed!.Before.Items, Original);
            Bits(completed.After.Items, Forward.Concat(Original.Skip(2)).ToArray());
            PhysicalBits(native, Forward.Concat(Original.Skip(2)).ToArray());
            Check(NativePublicationCall.RetainedCallCount == calls + 1
                && NativePublicationCall.RetainedRootRecordCount == retainedRoots + 1
                && native.ActiveRoots == 1);
        }
        var attempts = native.FreeHandleAttempts;
        native.FailFreeHandle = 0;
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Check(native.ActiveRoots == 0 && native.FreeHandleAttempts == attempts);
    }

    private static void FailedOperationCleanupUnknown()
    {
        using var native = new FakeNative();
        var (api, original, cls) = native.CaptureVector3Buffer(Original);
        using (original)
        {
            var expected = api.ReadRootedLineVector3Buffer(original, cls, 4, () => { });
            var calls = NativePublicationCall.RetainedCallCount;
            var retainedRoots = NativePublicationCall.RetainedRootRecordCount;
            native.FailFreeHandle = checked((uint)(native.NewRoots + 1));
            var operation = new InvalidOperationException("The source callback withdrew admission.");
            var beforeStore = 0;
            var failure = Catch<AggregateException>(() => api.WriteRootedLineVector3BufferPrefix(
                original, cls, expected, Forward, () => { }, () => { }, () => {
                    beforeStore++; throw operation;
                }));
            var before = failure.Data["LineVector3BufferBefore"] as NativeRootedVector3Buffer;
            Check(beforeStore == 1 && failure.InnerExceptions.Any(e => ReferenceEquals(e, operation))
                && failure.Data.Contains("NativePublicationCleanupOwner")
                && ReferenceEquals(failure.Data["LineVector3BufferOriginalRoot"], original)
                && failure.Data["LineVector3BufferStoreEntered"] is false
                && before is not null
                && !failure.Data.Contains("LineVector3BufferWriteObservation"));
            Bits(before!.Items, Original); PhysicalBits(native, Original);
            Check(NativePublicationCall.RetainedCallCount == calls + 1
                && NativePublicationCall.RetainedRootRecordCount == retainedRoots + 1
                && native.ActiveRoots == 1);
        }
        var attempts = native.FreeHandleAttempts;
        native.FailFreeHandle = 0;
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Check(native.ActiveRoots == 0 && native.FreeHandleAttempts == attempts);
    }

    private static void PhysicalBits(FakeNative native, IReadOnlyList<NativeVector3BufferBits> expected)
    {
        for (var i = 0; i < expected.Count; i++) Check(native.ReadVector3BufferItem(i) == expected[i]);
    }
    private static void Bits(IReadOnlyList<NativeVector3BufferBits> actual,
        IReadOnlyList<NativeVector3BufferBits> expected)
    {
        Check(actual.Count == expected.Count);
        for (var i = 0; i < actual.Count; i++) Check(actual[i] == expected[i]);
    }
    private static void Check(bool condition)
    { if (!condition) throw new InvalidOperationException("Vector3 buffer regression invariant failed."); }
    private static void Throws<T>(Action action) where T : Exception => _ = Catch<T>(action);
    private static T Catch<T>(Action action) where T : Exception
    {
        try { action(); } catch (T error) { return error; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
}
