using RizToolbox.Adapter.Gameplay;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekCanvasCacheChecks
{
    private static void Preparation()
    {
        using var f = new Fixture(); var n = f.Native; var baseline = n.CanvasCacheSnapshot(); var roots = n.ActiveRoots;
        var p = f.Prepare(); p.VerifyPrepared(); var j = p.GetJournal();
        Check(j.Prepared && !j.CommitEntered && !j.AppliedValuesVerified && !j.RestorationEntered
            && !j.HasRetainedResponsibility && !j.SideEffectsUnknown && !j.CompleteSeekPermitted
            && j.AttemptedWrites == 0 && j.AttemptedRestores == 0 && j.Stores.Count == 0,
            "Read-only cache preparation advertised dispatch, storage confirmation, retained failure, or complete Seek.");
        var expected = new[] { 8.5f, 26.75f, 8.5f, 2f, 3f, 2f, 0f, 32f, 0f, 5f, 3f }.Select(Bits).ToArray();
        Check(j.Slots.Count == 11 && j.UniqueSlots.Count == 11 && j.Baseline.SequenceEqual(baseline)
            && j.Expected.SequenceEqual(baseline) && j.Slots.Select(row => row.Ordinal).SequenceEqual(Enumerable.Range(0, 11))
            && j.Slots.Select(row => row.TargetBits).SequenceEqual(expected),
            "Cache preparation dropped complete physical occurrences or recomputed a theoretical/wrong requested target.");
        foreach (var row in j.Slots)
        {
            var group = row.Ordinal < 9 ? row.Ordinal / 3 : row.Ordinal - 6;
            Check(row.Kind == (SeekCanvasCacheKind)group && row.Index == (row.Ordinal < 9 ? row.Ordinal % 3 : -1)
                && row.Owner == (group < 3 ? n.CanvasCacheArray(group).ToInt64() : n.GeometryCanvasControl.ToInt64()),
                "Cache slot lost its actual array owner/index or the exact common scalar receiver.");
        }
        Check(n.CanvasCacheSnapshot().SequenceEqual(baseline) && n.CanvasCacheObservedStores.Count == 0
            && n.CanvasCacheScalarCalls.Count == 0 && n.CanvasSourceRanges.Count == 3
            && n.CanvasSourceRanges.All(row => row.To == 5d)
            && Enumerable.Range(0, 3).Select(n.CanvasCacheArray).All(n.HasRootFor)
            && n.ActiveRoots > roots, "Preparation stored values, re-evaluated projection on verification, or borrowed cache roots.");
        f.UnrelatedState();
    }
    private static void AllSlots()
    {
        using var f = new Fixture(); var p = f.Prepare(); var n = f.Native; var before = p.GetJournal();
        var baseline = before.Baseline.ToArray(); var ranges = n.CanvasSourceRangeAttempts;
        p.Commit(); var committed = p.GetJournal();
        Check(committed.CommitEntered && committed.CommitFinished && !committed.AppliedValuesVerified
            && committed.SideEffectsUnknown && committed.HasRetainedResponsibility
            && committed.AttemptedWrites == 11 && committed.Stores.Count == 11
            && committed.Stores.Select(row => row.Ordinal).SequenceEqual(Enumerable.Range(0, 11))
            && committed.Stores.All(row => row.Direction == SeekCanvasCacheDirection.Apply && row.Attempted
                && row.NativeEntered && row.NativeReturned && row.TargetObserved && row.Readback == row.TargetBits),
            "Actual cache commit skipped/reordered a physical slot or invented native/readback evidence.");
        Check(n.CanvasCacheSnapshot().SequenceEqual(before.Slots.Select(row => row.TargetBits))
            && n.CanvasCacheObservedStores.Select(row => (row.Owner.ToInt64(), row.Index, row.Bits))
                .SequenceEqual(before.UniqueSlots.Select(row => (row.Owner, row.Index, row.TargetBits)))
            && n.CanvasCacheScalarCalls.Select(row => row.Name).SequenceEqual(new[] { "nowTime", "speed" }),
            "Production cache stores differ from actual source order, requested bits, or scalar dependency order.");
        Check(p.Confirm().Observed == true && !p.SideEffectsUnknown && !p.HasRetainedResponsibility
            && !p.GetJournal().CompleteSeekPermitted && n.CanvasSourceRangeAttempts == ranges,
            "Complete cache-only target confirmation failed, recomputed its producers, or granted full Seek.");
        p.Restore(); Check(p.ConfirmRestored().OriginalStateRestored, "Complete actual cache originals failed independent restoration confirmation.");
        var restored = p.GetJournal();
        Check(n.CanvasCacheSnapshot().SequenceEqual(baseline) && restored.RestorationEntered && restored.RestorationFinished
            && restored.RestoredValuesVerified && restored.AttemptedRestores == 11 && restored.Stores.Count == 22
            && restored.Stores.Skip(11).Select(row => row.Ordinal).SequenceEqual(Enumerable.Range(0, 11).Reverse())
            && restored.Stores.Skip(11).All(row => row.Direction == SeekCanvasCacheDirection.Restore
                && row.NativeEntered && row.NativeReturned && row.OriginalObserved && row.Readback == row.TargetBits)
            && !restored.SideEffectsUnknown && !restored.HasRetainedResponsibility,
            "Conditional cache restoration omitted an entered physical slot or failed to close known storage responsibility.");
        Check(!before.CommitEntered && before.Stores.Count == 0 && before.Baseline.SequenceEqual(baseline)
            && committed.Stores.Count == 11 && !committed.RestorationEntered && !committed.AppliedValuesVerified,
            "Later store/confirmation/restoration mutated an earlier immutable journal.");
    }
    private static void Aliases()
    {
        using (var f = new Fixture(n => { n.CanvasCacheEqualTargets(); n.CanvasCacheAlias(1, 0); n.CanvasCacheAlias(2, 0); }))
        {
            var p = f.Prepare(); var before = p.GetJournal();
            Check(before.Slots.Count == 11 && before.UniqueSlots.Count == 5 && before.Baseline.Count == 11
                && before.UniqueSlots.Select(row => row.Ordinal).SequenceEqual(new[] { 0, 1, 2, 9, 10 })
                && before.Slots.Take(9).Select(row => row.Owner).Distinct().Count() == 1,
                "Cross-kind aliases removed complete occurrences, collided distinct indices, or collided scalar kinds.");
            p.Commit(); Check(p.Confirm().Observed == true && p.GetJournal().AttemptedWrites == 5
                && f.Native.CanvasCacheObservedStores.Count == 5, "Equal physical aliases were stored more than once.");
            p.Restore(); Check(p.ConfirmRestored().OriginalStateRestored && p.GetJournal().AttemptedRestores == 5
                && f.Native.CanvasCacheObservedStores.Count == 10, "Equal physical aliases were restored more than once.");
        }
        foreach (var kind in new[] { 1, 2 })
        {
            using var f = new Fixture(n => n.CanvasCacheAlias(kind, 0)); var roots = f.Native.ActiveRoots;
            Reject(() => f.Prepare());
            Check(f.Native.CanvasCacheObservedStores.Count == 0 && f.Native.CanvasCacheScalarCalls.Count == 0
                && f.Native.ActiveRoots == roots, "Conflicting physical cache alias reached storage or leaked construction roots.");
        }
    }
    private static void EmptyAndInvalid()
    {
        using (var f = new Fixture(n => n.CanvasCacheEmpty()))
        {
            var p = f.Prepare(); var baseline = p.GetJournal().Baseline.ToArray(); p.VerifyPrepared(); p.Commit();
            Check(p.Confirm().Observed == true && p.GetJournal().Slots.Select(row => row.Kind)
                    .SequenceEqual(new[] { SeekCanvasCacheKind.NowTime, SeekCanvasCacheKind.Speed })
                && p.GetJournal().Slots.All(row => row.Index == -1) && p.GetJournal().AttemptedWrites == 2
                && f.Native.CanvasCacheScalarCalls.Count == 2 && f.Native.CanvasSourceRangeAttempts == 0,
                "A complete empty actual Canvas source dropped required time/speed storage or invented array/producer work.");
            p.Restore(); Check(p.ConfirmRestored().OriginalStateRestored && f.Native.CanvasCacheSnapshot().SequenceEqual(baseline)
                && p.GetJournal().AttemptedRestores == 2, "Empty-array scalar originals were not restored.");
        }
        foreach (var value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        foreach (var time in new[] { false, true })
        {
            using var f = new Fixture(); var roots = f.Native.ActiveRoots;
            Reject(() => f.Prepare(time ? value : 5, time ? 3 : value));
            Check(f.Native.CanvasSourceRangeAttempts == 0 && f.Native.CanvasCacheObservedStores.Count == 0
                && f.Native.CanvasCacheScalarCalls.Count == 0 && f.Native.ActiveRoots == roots,
                "Nonfinite explicit cache request reached producer/storage or retained known construction roots.");
        }
    }
    private static void RawBits()
    {
        var negativeZero = unchecked((int)0x80000000); var nan = unchecked((int)0xffc12345); const int otherNan = 0x7fc54321;
        using var f = new Fixture(n =>
        {
            n.CanvasCacheSetBits(0, 0, negativeZero); n.CanvasCacheSetBits(1, 1, nan);
            n.CanvasCacheSetBits(2, 2, otherNan); n.CanvasCacheSetBits(3, -1, nan); n.CanvasCacheSetBits(4, -1, negativeZero);
        });
        var baseline = f.Native.CanvasCacheSnapshot(); var p = f.Prepare();
        Check(p.GetJournal().Baseline.SequenceEqual(baseline) && p.GetJournal().Baseline[0] == negativeZero
            && p.GetJournal().Baseline[4] == nan && p.GetJournal().Baseline[8] == otherNan,
            "Preparation canonicalized an original array/scalar NaN payload or signed zero.");
        p.Commit(); Check(p.Confirm().Observed == true, "Nonfinite original cache bits prevented finite explicit target storage.");
        p.Restore(); Check(p.ConfirmRestored().OriginalStateRestored && f.Native.CanvasCacheSnapshot().SequenceEqual(baseline),
            "Actual conditional array/scalar restoration compared NaN numerically or canonicalized original bits.");
        Check(p.GetJournal().Stores.Where(row => row.Direction == SeekCanvasCacheDirection.Restore)
            .All(row => row.TargetBits == baseline[row.Ordinal] && row.Readback == baseline[row.Ordinal] && row.OriginalObserved),
            "Restore evidence lost exact original array/scalar raw bits.");
    }
    private static void FieldAndArrayAbi()
    {
        foreach (var kind in Enumerable.Range(0, 5))
        foreach (var mode in new[] { "offset", "static", "readonly", "literal", "type", "byref", "parent" })
        {
            using var f = new Fixture(n => n.CanvasCacheMetadata(kind, mode)); var roots = f.Native.ActiveRoots;
            Reject(() => f.Prepare());
            Check(f.Native.CanvasCacheObservedStores.Count == 0 && f.Native.CanvasCacheScalarCalls.Count == 0
                && f.Native.ActiveRoots == roots, "Malformed exact cache field reached storage or leaked roots: " + kind + "/" + mode);
        }
        foreach (var mode in new[] { "rank", "element", "width", "length", "bytes", "size", "header-size", "header" })
        {
            using var f = new Fixture(n => n.CanvasSourceFault(n.CanvasCacheArray(1), mode)); var roots = f.Native.ActiveRoots;
            Reject(() => f.Prepare());
            Check(f.Native.CanvasCacheObservedStores.Count == 0 && f.Native.CanvasCacheScalarCalls.Count == 0
                && f.Native.ActiveRoots == roots, "Invalid complete cache array ABI/storage reached a write: " + mode);
        }
        foreach (var length in new[] { 0, 2, 4, 65537 })
        {
            using var f = new Fixture(n => n.CanvasCacheReplace(2, n.CanvasSourceSampleArray(new float[length]))); var roots = f.Native.ActiveRoots;
            var baseline = f.Native.CanvasCacheSnapshot();
            Reject(() => f.Prepare()); Check(f.Native.CanvasCacheObservedStores.Count == 0 && f.Native.ActiveRoots == roots
                && f.Native.CanvasCacheSnapshot().SequenceEqual(baseline),
                "Cache length did not exactly cover all three actual move occurrences: " + length);
        }
    }
    private static void WarmMetadata()
    {
        foreach (var kind in Enumerable.Range(0, 5))
        {
            using var f = new Fixture(); var p = f.Prepare(); f.Native.CanvasCacheMetadata(kind, "offset"); Reject(p.Commit);
            Check(p.GetJournal().AttemptedWrites == 0 && f.Native.CanvasCacheObservedStores.Count == 0,
                "A warm exact cache field descriptor hid current metadata drift: " + kind);
        }
        using (var flags = new Fixture())
        {
            var p = flags.Prepare(); flags.Native.CanvasCacheMetadata(3, "flags"); Reject(p.Commit);
            Check(p.GetJournal().AttemptedWrites == 0, "Saved mutable field flags drift silently replaced the original exact descriptor.");
        }
        foreach (var mode in new[] { "width", "bytes", "header-size", "header" })
        {
            using var f = new Fixture(); var p = f.Prepare(); f.Native.CanvasSourceFault(f.Native.CanvasCacheArray(0), mode); Reject(p.Commit);
            Check(p.GetJournal().AttemptedWrites == 0 && f.Native.CanvasCacheObservedStores.Count == 0,
                "A warm cache array layout hid current ABI/storage drift: " + mode);
        }
        using var scalar = new Fixture(); var q = scalar.Prepare(); scalar.Native.CanvasSourceScalarWidth("System.Single", 8); Reject(q.Commit);
        Check(q.GetJournal().AttemptedWrites == 0 && scalar.Native.CanvasCacheObservedStores.Count == 0,
            "A cached Single descriptor hid fresh native width drift.");
    }
    private static void ReferencesAndSource()
    {
        foreach (var kind in Enumerable.Range(0, 3))
        {
            using var f = new Fixture(); var p = f.Prepare();
            f.Native.CanvasCacheReplace(kind, f.Native.CanvasSourceCloneArray(f.Native.CanvasCacheArray(kind))); Reject(p.Commit);
            Check(p.GetJournal().AttemptedWrites == 0 && f.Native.CanvasCacheObservedStores.Count == 0,
                "Content-equal replacement cache reference silently retargeted independent actual storage.");
        }
        foreach (var mode in new[] { "point", "list", "version", "moves", "transforms", "canvas", "profile", "ease" })
        {
            using var f = new Fixture(); var p = f.Prepare(); var n = f.Native;
            if (mode == "point") n.CanvasSourceSet(n.CanvasSourcePoint(0, true, 0), "value", 123f);
            else if (mode == "list") n.CanvasSourceReplaceList(0, true);
            else if (mode == "version") n.CanvasSourceSet(n.CanvasSourceList(0, true), "_version", 8);
            else if (mode == "moves") n.CanvasSourceSet(n.GeometryCanvasControl, "canvasMoveData", n.CanvasSourceCloneArray(n.CanvasSourceMovesArray));
            else if (mode == "transforms") n.CanvasSourceSet(n.GeometryCanvasControl, "canvasTransforms", n.CanvasSourceCloneArray(n.GeometryCanvasArray));
            else if (mode == "canvas") n.CanvasSourceSet(n.GameplayInformation, "canvasControl", n.CanvasSourceCloneObject(n.GeometryCanvasControl));
            else if (mode == "profile") n.CanvasSourceProfile.WrongMember = "CanvasClass";
            else { n.GeometrySingleSlot(n.CanvasSourceSamples[0], 100, .125f); n.CanvasSourceRemember(); }
            Reject(p.Commit); Check(p.GetJournal().AttemptedWrites == 0 && n.CanvasCacheObservedStores.Count == 0,
                "Changed complete Canvas source/profile reached a target cache store: " + mode);
        }
    }
}
