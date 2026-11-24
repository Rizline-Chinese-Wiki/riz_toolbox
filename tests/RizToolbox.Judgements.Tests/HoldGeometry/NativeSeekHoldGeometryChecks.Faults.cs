using RizToolbox.Adapter.Gameplay;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekHoldGeometryChecks
{
    private static void NativeFaults()
    {
        foreach (var mode in new[] { "before", "after", "callback" })
        {
            using var f = new Fixture(); var p = f.Prepare(Empty());
            if (mode == "before") f.Native.FailGeometryBefore = 1;
            else if (mode == "after") f.Native.FailGeometryAfter = 1;
            else f.Native.AfterGeometryCall = _ => throw new IOException("Synthetic geometry callback fault.");
            ExpectFailure(() => Execute(f, p)); var j = p.GetJournal();
            Check(!j.ExecutionFinished && j.SideEffectsUnknown && j.HasRetainedResponsibility
                && j.Commands.Count == 1 && j.Commands[0].NativeEntered && j.Commands[0].Before is not null,
                "A partial native/callback failure lost its immutable journal or exact owner.");
            Check(f.Native.GeometryCalls.Count == 1 && f.Native.GeometryCalls[0].Stored == (mode != "before"),
                "Geometry retried a partially entered command or invented native storage success.");
            var calls = Calls.Read(f.Native); ExpectFailure(p.Execute);
            Check(Calls.Read(f.Native) == calls, "A failed once-only sequence entered exports again.");
        }
    }
    private static void SourceChanges()
    {
        using (var f = new Fixture())
        {
            var p = f.Prepare(Empty()); var transform = f.Native.GeometryNoteTransforms[0];
            var old = f.Native.GeometryTransforms[transform];
            f.Native.GeometrySetTransform(transform, old with { Position = old.Position with { X = old.Position.X + 1 } });
            ExpectFailure(p.Execute);
            Check(f.Native.GeometryCalls.Count == 0 && p.AttemptedCommands == 0,
                "A changed prepared Transform reached native dispatch.");
        }
        using var changed = new Fixture(); var q = changed.Prepare(Empty());
        changed.Native.AfterGeometryStore = call =>
        {
            var root = changed.Native.GeometryUiRoot; var value = changed.Native.GeometryTransforms[root];
            changed.Native.GeometrySetTransform(root, value with { Position = value.Position with { X = value.Position.X + 10 } });
        };
        ExpectFailure(() => Execute(changed, q));
        Check(changed.Native.GeometryCalls.Count == 1 && q.GetJournal().SideEffectsUnknown,
            "A callback changed an upstream parent but later commands still executed.");
    }
    private static void ImmutableJournal()
    {
        using var f = new Fixture(); var a = f.Native.HoldStateNotes[0].ToInt64(); var seed = new[] { a };
        var p = f.Prepare(new(-1, 2, new(seed, Array.Empty<long>()))); var original = p.GetJournal();
        var transforms = original.Baseline!.Transforms.ToArray(); seed[0] = long.MaxValue;
        var result = Execute(f, p);
        Check(!original.ExecutionEntered && original.Commands.Count == 0
            && original.Baseline.Transforms.SequenceEqual(transforms) && original.Plan!.Request.Seed!.HoldNotes[0] == a,
            "Caller mutation or later execution rewrote an earlier immutable journal.");
        var before = result.Commands.Select(x => x.Before).ToArray(); var again = p.GetJournal();
        Check(result.Commands.Select(x => x.Before).SequenceEqual(before)
            && again.Plan!.HoldNotes.SequenceEqual(result.Plan!.HoldNotes), "Repeated journal reads changed captured state.");
    }
    private static void SingleArrays()
    {
        foreach (var bits in new[] { unchecked((int)0x80000000), 0x7fc12345, unchecked((int)0xffc54321) })
        {
            using var f = new Fixture(); f.Native.GeometrySingleSlot(f.Native.GeometryFloorArray, 0, BitConverter.Int32BitsToSingle(bits));
            using var root = f.Native.GameplayApi.Root(f.Native.GeometryFloorArray);
            var value = f.Native.GameplayApi.ReadRootedGeometrySingleArray(root, f.Native.GeometryFloorClass, 65536);
            Check(value.Array == f.Native.GeometryFloorArray && value.Length == 1 && Bits(value.Items[0]) == bits,
                "Single vector observation canonicalized NaN payload or signed zero.");
        }
    }
    private static void InvalidArrays()
    {
        foreach (var fault in Enum.GetValues<FakeNative.GeometryArrayFault>())
        {
            using var f = new Fixture();
            if (fault == FakeNative.GeometryArrayFault.HeaderClass) f.Native.GeometryBreakArrayHeader();
            else f.Native.GeometryArrayError = fault;
            using var root = f.Native.GameplayApi.Root(f.Native.GeometryFloorArray);
            ExpectFailure(() => f.Native.GameplayApi.ReadRootedGeometrySingleArray(root, f.Native.GeometryFloorClass, 65536));
            Check(f.Native.GeometryCalls.Count == 0, "Malformed Single storage reached a geometry setter.");
        }
        using var changed = new Fixture(); var lengths = 0;
        changed.Native.AfterGeometryArrayExport = (name, array) =>
        {
            if (name == "length" && array == changed.Native.GeometryFloorArray && ++lengths == 2)
                changed.Native.GeometrySingleSlot(array, 0, 19f);
        };
        using var held = changed.Native.GameplayApi.Root(changed.Native.GeometryFloorArray);
        ExpectFailure(() => changed.Native.GameplayApi.ReadRootedGeometrySingleArray(held, changed.Native.GeometryFloorClass, 65536));
        Check(lengths >= 2 && changed.Native.GeometryCalls.Count == 0, "Changing second-pass slots were accepted.");
    }
    private static void InvalidMetadata()
    {
        using (var bad = new Fixture(configure: n => n.GeometryMetadata(n.GeometryLineFor(0), "endLineInformation", 0x60)))
        { ExpectFailure(() => bad.Prepare()); Check(bad.Native.GeometryCalls.Count == 0, "Changed field metadata reached dispatch."); }
        using (var method = new Fixture())
        {
            var p = method.Prepare(Empty()); method.Native.GeometryAliasMethod("SetParent", "set_localPosition");
            ExpectFailure(p.Execute); Check(method.Native.GeometryCalls.Count == 0 && p.AttemptedCommands == 0,
                "A different official setter MethodInfo reached native dispatch.");
        }
        using var row = new Fixture(configure: n => n.GeometryNullEaseRow());
        ExpectFailure(() => row.Prepare()); Check(row.Native.GeometryCalls.Count == 0,
            "A malformed nonempty ease array was silently treated as uninitialized.");
    }
    private static void Reentry()
    {
        using var f = new Fixture(); var preparing = false;
        f.Native.GeometryProfile.OnVerify = () =>
        {
            if (preparing) return; preparing = true; var before = Calls.Read(f.Native);
            Expect<InvalidOperationException>(() => f.Prepare(Empty()));
            Check(before == Calls.Read(f.Native), "Same RunValue preparation reentry reached native exports.");
        };
        var p = f.Prepare(Empty()); f.Native.GeometryProfile.OnVerify = null;
        Check(preparing, "The preparation reentry callback was not exercised.");
        var executing = false; f.Native.BeforeGeometryCall = _ =>
        {
            if (executing) return; executing = true; var before = Calls.Read(f.Native);
            Expect<InvalidOperationException>(p.Execute);
            Check(before == Calls.Read(f.Native), "Executing the same sequence recursively reached exports.");
        };
        Execute(f, p); Check(executing, "The actual execution callback reentry was not exercised.");
    }
    private static void CallbackDispose()
    {
        using var f = new Fixture(); var p = f.Prepare(Empty());
        f.Native.AfterGeometryStore = _ => p.Dispose();
        ExpectFailure(() => Execute(f, p)); var j = p.GetJournal();
        Check(j.Disposed && !j.ExecutionFinished && j.SideEffectsUnknown && j.HasRetainedResponsibility
            && j.Commands[0].NativeEntered && f.Native.GeometryCalls.Count == 1,
            "Callback withdrawal erased entered responsibility or allowed later commands.");
    }
    private static void ThreadAndGuard()
    {
        using (var f = new Fixture())
        {
            var p = f.Prepare(Empty()); var before = Calls.Read(f.Native);
            Task.Run(() => Expect<InvalidOperationException>(p.Execute)).GetAwaiter().GetResult();
            Check(before == Calls.Read(f.Native), "Worker-thread rejection occurred after native exports.");
        }
        using var guarded = new Fixture(); var q = guarded.Prepare(Empty()); var fired = false;
        guarded.Native.AfterGeometryRoot = (_, target) =>
        {
            if (!fired && target == guarded.Native.GeometryEndTransforms[0]
                && InMethod("InvokeCore") && InMethod("DispatchParent")) { fired = true; q.Dispose(); }
        };
        ExpectFailure(q.Execute);
        Check(fired && q.AttemptedCommands == 0 && guarded.Native.GeometryCalls.Count == 0,
            "The final managed guard did not reject withdrawal before actual native dispatch.");
    }
    private static void UnknownFree()
    {
        using var f = new Fixture(); var p = f.Prepare(Empty());
        var handle = f.Native.NoteViewHandlesFor(f.Native.GeometryUiRoot).Last(); f.Native.FailFreeHandle = handle;
        var error = ExpectFailure(p.Dispose); var attempts = f.Native.FreeHandleAttempts;
        Check(ReferenceEquals(error.Data["SeekHoldGeometryEffectsOwner"], p) && p.HasRetainedResponsibility,
            "Unknown root cleanup lost its exact public owner.");
        ExpectFailure(p.Dispose); Check(attempts == f.Native.FreeHandleAttempts, "Unknown native free was retried."); f.Retain(p, error);
        var sourceOwner = new object(); var construction = new IOException("Synthetic source construction with unknown cleanup.");
        construction.Data["SeekHoldGeometryCleanupOwner"] = sourceOwner;
        construction.Data["SeekHoldGeometryCleanupFailure"] = new IOException("Unknown source free.");
        var rejected = Expect<IOException>(() => NativeSeekHoldGeometryCommands.Prepare(() => throw construction, Empty()));
        var retained = rejected.Data["SeekHoldGeometryEffectsOwner"] as NativeSeekHoldGeometryCommands;
        Check(retained is not null && ReferenceEquals(rejected.Data["SeekHoldGeometrySourceCleanupOwner"], sourceOwner),
            "Construction failure replaced the original cleanup owner with a nonexistent effects owner.");
        ExpectFailure(retained!.Dispose); ExpectFailure(retained.Dispose);
    }
}
