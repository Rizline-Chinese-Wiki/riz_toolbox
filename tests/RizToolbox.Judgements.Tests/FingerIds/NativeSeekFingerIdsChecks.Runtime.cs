using System.Runtime.InteropServices;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekFingerIdsChecks
{
    private static void RuntimeRead()
    {
        using var f = new Fixture(); var expected = f.Baseline[1].Storage!;
        using var root = f.Native.GameplayApi.Root(expected.Backing); var pins = f.Native.PinnedRoots;
        var actual = f.Native.GameplayApi.ReadRootedInt32ArrayWithPublication(root, f.Native.FingerIntArrayClass, 65536);
        Check(actual.Array == expected.Backing && actual.Class == f.Native.FingerIntArrayClass
            && actual.Length == expected.Capacity.Count && actual.Items.SequenceEqual(expected.Capacity)
            && actual.HeaderSize == f.Native.ArrayHeaderBytes && actual.ByteLength == actual.Length * sizeof(int)
            && actual.ObjectSize == actual.HeaderSize + actual.ByteLength && f.Native.PinnedRoots == pins + 1
            && f.Native.FingerGetValues == 0, "The runtime did not pin and copy every original signed Int32 capacity slot directly.");
    }
    private static void RuntimeRejects()
    {
        for (var mode = 0; mode < 8; ++mode)
        {
            using var f = new Fixture(); var backing = f.Baseline[1].Storage!.Backing;
            using var root = f.Native.GameplayApi.Root(backing);
            switch (mode)
            {
                case 0: f.Native.FingerMutateMetadata(FakeNative.FingerMetadataFault.ArrayWidth); break;
                case 1: f.Native.FingerMutateMetadata(FakeNative.FingerMetadataFault.ArrayRank); break;
                case 2: f.Native.FingerHeaderOverride = 7; break;
                case 3: f.Native.FingerLengthOverride = 65537; break;
                case 4: f.Native.CorruptArrayByteLength = true; break;
                case 5: f.Native.CorruptArrayObjectSize = true; break;
                case 6: Marshal.WriteIntPtr(backing, f.Native.ClassOf(f.Native.Object)); break;
                case 7:
                    var observations = 0;
                    f.Native.AfterFingerArrayExport = (operation, array) =>
                    { if (operation == "length" && array == backing && ++observations == 2) f.Native.FingerSetSlot(backing, 5, 8765); };
                    break;
            }
            Expect<UnsupportedSignatureException>(() => f.Native.GameplayApi.ReadRootedInt32ArrayWithPublication(root, f.Native.FingerIntArrayClass, 65536));
            f.ClearCallbacks(); Check(f.Native.FingerWrites.Count == 0 && f.Native.FingerGetValues == 0,
                "An inconsistent complete array observation mutated storage or silently substituted boxed getters: " + mode);
        }
    }
    private static void RuntimeSet()
    {
        using var f = new Fixture(); var backing = f.Baseline[1].Storage!.Backing;
        using var root = f.Native.GameplayApi.Root(backing); var guards = 0;
        f.Native.GameplayApi.SetInt32ArrayElementGuarded(root, f.Native.FingerIntArrayClass, 5, int.MinValue, () => ++guards);
        Check(guards == 1 && f.Native.FingerBoxCalls == 1 && f.Native.FingerArrayCapacity(backing)[5] == int.MinValue
            && f.Native.FingerWrites.Single().Operation == "SetValue" && f.Native.FingerArgumentsRooted,
            "The exact official setter did not receive one rooted boxed Int32 and its explicit capacity index.");
        foreach (var wrongClass in new[] { false, true })
        {
            var writes = f.Native.FingerWrites.Count;
            f.Native.FingerWrongBoxClass = wrongClass; f.Native.FingerWrongBoxValue = !wrongClass;
            Expect<UnsupportedSignatureException>(() => f.Native.GameplayApi.SetInt32ArrayElementGuarded(root, f.Native.FingerIntArrayClass, 4, int.MaxValue, () => ++guards));
            f.ClearCallbacks(); Check(f.Native.FingerWrites.Count == writes && guards == 1,
                "A wrong boxed class/value reached the guard or official SetValue.");
        }
        var before = f.Native.FingerWrites.Count; Calls? atGuard = null;
        Expect<IOException>(() => f.Native.GameplayApi.SetInt32ArrayElementGuarded(root, f.Native.FingerIntArrayClass, 4, 321, () =>
        { atGuard = Calls.Read(f.Native); throw new IOException("Synthetic final guard withdrawal."); }));
        Check(atGuard == Calls.Read(f.Native) && f.Native.FingerWrites.Count == before && f.Native.FingerArrayCapacity(backing)[4] == int.MaxValue,
            "A throwing final managed guard dispatched SetValue or crossed a later high-level native operation.");
    }
    private static void RuntimeUnknownFrees()
    {
        foreach (var box in new[] { false, true })
        {
            using var f = new Fixture(); var backing = f.Baseline[1].Storage!.Backing;
            using var root = f.Native.GameplayApi.Root(backing); var before = PublicationOwners(); uint failed = 0;
            f.Native.AfterFingerRoot = (handle, target) =>
            {
                if (failed != 0) return;
                var record = f.Native.NoteViewRootLog.Single(row => row.Handle == handle);
                if (box ? target == backing || !InMethod("SetInt32ArrayElementGuarded", "NativeRuntime") : target != backing || !record.Pinned) return;
                failed = handle; f.Native.FailFreeHandle = handle;
            };
            var error = box ? Expect<AggregateException>(() => f.Native.GameplayApi.SetInt32ArrayElementGuarded(root, f.Native.FingerIntArrayClass, 5, 7654, () => { }))
                : Expect<AggregateException>(() => f.Native.GameplayApi.ReadRootedInt32ArrayWithPublication(root, f.Native.FingerIntArrayClass, 65536));
            f.ClearCallbacks(); var owner = PublicationOwners().Except(before).Single();
            Check(failed != 0 && Items(Field(owner, "_roots")).Count(value => value is not null) == 1
                && Items(Field(owner, "_failures")).Count(value => value is IOException) == 1
                && f.Native.NoteViewFreeLog.Count(row => row.Handle == failed && row.InjectedFailure) == 1
                && f.Native.FingerWrites.Count == (box ? 1 : 0),
                "Unknown pin/box free lost its exact root/error record or repeated/rolled back an entered setter.");
            var frees = f.Native.FreeHandleAttempts; Expect<InvalidOperationException>(((NativePublicationCall)owner).Dispose);
            Check(f.Native.FreeHandleAttempts == frees, "A process-owned unknown pin/box root was freed twice."); f.RetainPublication(owner, error);
        }
    }
    private static void RuntimeAdmission()
    {
        using var f = new Fixture(); var backing = f.Baseline[1].Storage!.Backing; using var root = f.Native.GameplayApi.Root(backing);
        f.Native.BeginLowerIl2CppAudit();
        try
        {
            Task.Run(() =>
            {
                Expect<InvalidOperationException>(() => f.Native.GameplayApi.ReadRootedInt32ArrayWithPublication(root, f.Native.FingerIntArrayClass, 65536));
                Expect<InvalidOperationException>(() => f.Native.GameplayApi.SetInt32ArrayElementGuarded(root, f.Native.FingerIntArrayClass, 0, 1, () => { }));
            }).GetAwaiter().GetResult();
        }
        finally { f.Native.EndLowerIl2CppAudit(); }
        Check(f.Native.LowerIl2CppCalls == 0, "Wrong-thread Int32 array access reached an export.");
        var reservations = new List<NativePublicationCall>();
        try
        {
            while (NativePublicationCall.RetainedCallCount < NativePublicationCall.MaximumCalls)
                reservations.Add(new NativePublicationCall(() => { }, Array.Empty<NativeRuntime.Arg>()));
            f.Native.BeginLowerIl2CppAudit();
            Expect<InvalidOperationException>(() => f.Native.GameplayApi.ReadRootedInt32ArrayWithPublication(root, f.Native.FingerIntArrayClass, 65536));
            Expect<InvalidOperationException>(() => f.Native.GameplayApi.SetInt32ArrayElementGuarded(root, f.Native.FingerIntArrayClass, 0, 1, () => { }));
            Check(f.Native.LowerIl2CppCalls == 0 && f.Native.FingerWrites.Count == 0, "Saturated publication capacity adopted native input responsibility.");
        }
        finally { f.Native.EndLowerIl2CppAudit(); foreach (var reservation in reservations.AsEnumerable().Reverse()) reservation.Dispose(); }
        Check(root.Target == backing, "Admission rejection consumed the caller-owned array root.");
    }
}
