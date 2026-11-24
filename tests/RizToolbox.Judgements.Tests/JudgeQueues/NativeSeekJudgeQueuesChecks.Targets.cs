using RizToolbox.Adapter.Gameplay;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekJudgeQueuesChecks
{
    private static void ExplicitTargets()
    {
        using var f = new Fixture(); var expected = f.Targets;
        var caller = Lists(expected).Select(row => row.ToList()).ToArray();
        var p = f.Prepare(new(caller[0], caller[1], caller[2]));
        foreach (var list in caller) { list.Clear(); list.Add(0); }
        p.VerifyPrepared(); p.Commit();
        var expectedCalls = Lists(expected).SelectMany((rows, slot) =>
            new[] { (Operation: "Clear", List: slot, Requested: 0L) }
                .Concat(rows.Select(note => (Operation: "Add", List: slot, Requested: note)))).ToArray();
        Check(p.AttemptedWrites == expectedCalls.Length && ActiveEquals(f.Native, expected)
            && f.Native.JudgeQueueWrites.Select(row => (row.Operation, row.List, row.Requested)).SequenceEqual(expectedCalls)
            && f.Native.JudgeQueueWrites.All(row => row.Stored && row.Receiver == f.Native.JudgeLists[row.List])
            && p.Confirm().Observed == true,
            "Explicit queue targets were reordered, deduplicated, read from caller storage late or sent to another receiver.");
        for (var slot = 0; slot < 3; ++slot)
            Check(f.Native.JudgeBacking(slot) == f.Baseline[slot].Backing
                && f.Native.JudgeScalar(slot, "_version") == unchecked(f.Baseline[slot].Version + Lists(expected)[slot].Count + 1)
                && f.Native.JudgeCapacity(slot).Skip(Math.Max(f.Baseline[slot].Size, Lists(expected)[slot].Count))
                    .SequenceEqual(f.Baseline[slot].Capacity.Skip(Math.Max(f.Baseline[slot].Size, Lists(expected)[slot].Count))),
                "A forward queue write changed backing identity or failed signed version wraparound.");
        var committed = f.Native.JudgeQueueWrites.Count; Restored(f, p);
        Check(f.Native.JudgeQueueWrites.Skip(committed).All(row => row.Operation is "SetValue" or "_items" or "_size" or "_version")
            && f.Native.JudgeQueueWrites.Skip(committed).Any(row => row.Operation == "SetValue"),
            "Baseline restoration did not use guarded official complete-storage restoration.");
    }
    private static void IndependentRuntimeNotes()
    {
        foreach (var shape in new[] { FakeNative.NoteViewShape.Partial, FakeNative.NoteViewShape.MultipleSegments })
        {
            using var f = new Fixture(shape); var targets = f.Targets;
            if (shape == FakeNative.NoteViewShape.MultipleSegments)
            {
                var r = f.Native.JudgeRuntimeReferences;
                targets = targets with { Notes = new[] { r[^1], r[0], r[^1], r[0] } };
                Check(r[^1] != r[0] && f.Native.NoteViewAllCount == 6,
                    "The multiple-instance fixture did not preserve distinct runtime references.");
            }
            var p = f.Prepare(targets); f.Run.Dispose(); p.Commit();
            Check(ActiveEquals(f.Native, targets) && p.Confirm().Observed == true,
                "Partial chart coverage, shared chart data or caller RunValue disposal revoked independent queue targets.");
            Restored(f, p); p.Dispose(); Check(f.Native.ActiveRoots == 0, "Independent queue roots survived all known owners.");
        }
    }
    private static void InvalidTargets()
    {
        using var f = new Fixture(); var good = f.Targets; var r = f.Native.JudgeRuntimeReferences;
        var invalid = new[]
        {
            good with { Notes = null! }, good with { TapWaiting = new[] { 0L } },
            good with { DragWaiting = new[] { long.MaxValue } },
            good with { Notes = new[] { f.Native.JudgeForeignTail.ToInt64() } },
            good with { Notes = Enumerable.Repeat(r[0], NativeSeekJudgeStateView.MaximumCapacity + 1).ToArray() }
        };
        foreach (var targets in invalid)
        {
            var roots = f.Native.ActiveRoots;
            try { using var unexpected = f.Prepare(targets); throw new Exception("Invalid Judge target was admitted."); }
            catch (ArgumentException) { }
            catch (InvalidDataException) { }
            Check(f.Native.JudgeQueueWrites.Count == 0 && f.Native.ActiveRoots == roots
                && f.Native.JudgeQueueStorageEquals(f.Baseline),
                "Invalid targets reached a native writer or leaked preparation roots.");
        }
        using var retry = f.Prepare(); retry.VerifyPrepared();
    }
    private static void AliasedLists()
    {
        using var f = new Fixture(alias: FakeNative.JudgeAlias.List); var notes = f.Targets.Notes;
        var inconsistent = f.Targets with { Notes = notes, TapWaiting = notes.Reverse().ToArray() };
        Expect<InvalidDataException>(() => f.Prepare(inconsistent));
        Check(f.Native.JudgeQueueWrites.Count == 0, "Contradictory targets for one actual List reached native writes.");
        var expected = f.Targets with { Notes = notes, TapWaiting = notes.ToArray() };
        var p = f.Prepare(expected); p.Commit();
        Check(ActiveEquals(f.Native, expected) && p.AttemptedWrites == notes.Count + 2
            && f.Native.JudgeQueueWrites.Count(row => row.Operation == "Clear") == 2
            && f.Native.JudgeQueueWrites.All(row => row.List != 1) && p.Confirm().Observed == true,
            "One aliased List was written twice or occurrence-identical alias targets were rejected.");
        Restored(f, p);
    }
    private static void AliasedBackings()
    {
        using (var f = new Fixture(alias: FakeNative.JudgeAlias.Backing))
        {
            var roots = f.Native.ActiveRoots; Expect<NotSupportedException>(() => f.Prepare());
            Check(f.Native.JudgeQueueWrites.Count == 0 && f.Native.ActiveRoots == roots
                && f.Native.JudgeQueueStorageEquals(f.Baseline),
                "Distinct mutable Lists sharing positive-capacity backing were admitted or leaked roots.");
        }
        using (var f = new Fixture(configure: n =>
        {
            for (var slot = 0; slot < 3; ++slot) n.JudgeQueueCapacity(slot, 0, sharedEmpty: true);
        }))
        {
            var p = f.Prepare(); p.Commit();
            Check(p.Confirm().Observed == true && f.Native.JudgeQueueGrowths.Count == 2,
                "Distinct Lists sharing an empty array could not independently grow from explicit targets.");
            Restored(f, p);
        }
    }
    private static void CapacityGrowth()
    {
        using var f = new Fixture(); var r = f.Native.JudgeRuntimeReferences;
        var targets = f.Targets with { Notes = Enumerable.Range(0, 9).Select(index => r[index % r.Length]).ToArray() };
        var p = f.Prepare(targets); p.Commit();
        Check(ActiveEquals(f.Native, targets) && f.Native.JudgeQueueGrowths.Count == 1
            && f.Native.JudgeCapacity(0).Length == 12 && f.Native.JudgeCapacity(0).Skip(9).All(value => value == IntPtr.Zero)
            && p.Confirm().Observed == true, "Official Add growth did not preserve capacity doubling and zero new tail.");
        var growth = f.Native.JudgeQueueGrowths.Single();
        Check(f.Native.JudgeQueueArrayCapacity(growth.OldBacking).SequenceEqual(growth.OldBefore)
            && growth.OldCapacity == 6 && growth.NewCapacity == 12
            && f.Native.NoteViewHandlesFor(growth.NewBacking).Length != 0,
            "Growth modified its old backing or the new array was not independently retained on readback.");
        var committed = f.Native.JudgeQueueWrites.Count; Restored(f, p);
        Check(f.Native.JudgeQueueWrites.Skip(committed).Any(row => row.Operation == "_items"),
            "A grown queue did not restore the original backing through the official reference setter.");
    }
    private static void GrowthBound()
    {
        using var f = new Fixture(configure: n => n.JudgeQueueCapacity(0, 32769));
        var targets = f.Targets with { Notes = Enumerable.Repeat(f.Native.JudgeRuntimeReferences[0], 32770).ToArray() };
        var roots = f.Native.ActiveRoots; Expect<NotSupportedException>(() => f.Prepare(targets));
        Check(f.Native.JudgeQueueWrites.Count == 0 && f.Native.JudgeQueueGrowths.Count == 0
            && f.Native.ActiveRoots == roots && f.Native.JudgeQueueStorageEquals(f.Baseline),
            "32769→65538 growth was not rejected before Clear/Add or leaked preparation roots.");
    }
    private static void EmptyQueues()
    {
        using var f = new Fixture(configure: n => { for (var slot = 0; slot < 3; ++slot) n.JudgeSetList(slot, "_size", 0); });
        var targets = new SeekJudgeQueueTargets(Array.Empty<long>(), Array.Empty<long>(), Array.Empty<long>());
        var p = f.Prepare(targets); p.Commit();
        Check(p.AttemptedWrites == 3 && ActiveEquals(f.Native, targets) && p.Confirm().Observed == true
            && f.Native.JudgeQueueSnapshot().Where((row, slot) => !row.Capacity.SequenceEqual(f.Baseline[slot].Capacity)).Any() == false,
            "Clear of a size-zero queue modified its nonzero tail or skipped its official version transition.");
        Restored(f, p);
    }
}
