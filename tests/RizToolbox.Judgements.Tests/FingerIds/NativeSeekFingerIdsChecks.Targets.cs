using RizToolbox.Adapter.Gameplay;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekFingerIdsChecks
{
    private static void ExplicitTargets()
    {
        using var f = new Fixture(); var expected = f.Targets;
        var values = expected.Select(row => row.FingerIds?.ToList()).ToArray();
        var caller = expected.Select((row, index) => row with { FingerIds = values[index] }).Reverse().ToList();
        var p = f.Prepare(caller);
        Check(f.Native.FingerStorageEquals(f.Baseline) && !f.Native.FingerWrites.Any(row => row.Operation == "fingerIds"),
            "Preparation published scratch or changed actual fingerIds storage.");
        caller.Clear(); foreach (var list in values) { list?.Clear(); list?.Add(-999); }
        p.VerifyPrepared(); p.Commit();
        Check(ActiveEquals(f.Native, expected) && p.Confirm().Observed == true
            && f.Native.FingerReference(0) != IntPtr.Zero && f.Native.FingerReference(2) == IntPtr.Zero
            && f.Native.FingerReference(1) == f.Baseline[1].List && f.Native.FingerScalar(f.Baseline[3].List, "_version") == int.MinValue,
            "Null, signed extrema, duplicate ids, caller copies or original List identities were reduced to another target.");
        var existing = f.Baseline[1].List;
        Check(f.Native.FingerWrites.Where(row => row.Receiver == existing && row.Operation == "Add")
            .Select(row => checked((int)row.Requested)).SequenceEqual(expected[1].FingerIds!), "Existing List.Add lost explicit occurrence order.");
        Restored(f, p);
    }
    private static void NullAndEmpty()
    {
        foreach (var empty in new[] { false, true })
        {
            using var f = new Fixture(FakeNative.NoteViewShape.MultipleSegments); var targets = f.Targets;
            targets[0] = targets[0] with { FingerIds = empty ? Array.Empty<int>() : null };
            targets[^1] = targets[^1] with { FingerIds = empty ? Array.Empty<int>() : null };
            var p = f.Prepare(targets);
            Check(f.Native.FingerReference(0) == IntPtr.Zero && f.Native.FingerReference(targets.Length - 1) == IntPtr.Zero,
                "Scratch construction published either null source before commit.");
            p.Commit();
            Check(ActiveEquals(f.Native, targets) && p.Confirm().Observed == true
                && (empty ? f.Native.FingerReference(0) != IntPtr.Zero
                    && f.Native.FingerReference(targets.Length - 1) != f.Native.FingerReference(0) : f.Native.FingerReference(0) == IntPtr.Zero),
                "Null was converted to empty, empty to null, or independent notes shared a newly constructed scratch List.");
            Restored(f, p);
        }
    }
    private static void InvalidTargets()
    {
        using var f = new Fixture(); var good = f.Targets;
        var invalid = new[] { good[..^1], good.Append(new SeekFingerIdsTarget(long.MaxValue, null)).ToArray(),
            good.Select((row, index) => index == 1 ? good[0] : row).ToArray(),
            good.Select((row, index) => index == 0 ? row with { Reference = 0 } : row).ToArray(),
            good.Select((row, index) => index == 0 ? row with { FingerIds = new int[65537] } : row).ToArray() };
        for (var index = 0; index < invalid.Length; ++index)
        {
            var targets = invalid[index]; var roots = f.Native.ActiveRoots;
            if (index == invalid.Length - 1) Expect<NotSupportedException>(() => f.Prepare(targets));
            else Expect<InvalidDataException>(() => f.Prepare(targets));
            Check(f.Native.ActiveRoots == roots && f.Native.FingerCreatedLists.Count == 0 && f.Native.FingerWrites.Count == 0
                && f.Native.FingerStorageEquals(f.Baseline), "Invalid complete targets allocated/published scratch or leaked preparation roots.");
        }
        using var retry = f.Prepare(); retry.VerifyPrepared();
    }
    private static void ActualRuntimeNotes()
    {
        foreach (var shape in new[] { FakeNative.NoteViewShape.Partial, FakeNative.NoteViewShape.MultipleSegments })
        {
            using var f = new Fixture(shape); var expected = f.Targets; var p = f.Prepare(); f.Run.Dispose(); p.Commit();
            Check(ActiveEquals(f.Native, expected) && p.Confirm().Observed == true,
                "Partial chart coverage or distinct runtime objects sharing chart data lost their independent target/reference lifetime.");
            Restored(f, p); p.Dispose(); Check(f.Native.ActiveRoots == 0, "Independent fingerIds roots outlived all known owners.");
        }
    }
    private static void AliasedLists()
    {
        foreach (var nullTarget in new[] { false, true })
        {
            using var f = new Fixture(alias: FakeNative.FingerAlias.List); var targets = f.Targets;
            var inconsistent = targets.Select(row => row with { }).ToArray();
            inconsistent[2] = inconsistent[2] with { FingerIds = targets[1].FingerIds!.Reverse().ToArray() };
            Expect<InvalidDataException>(() => f.Prepare(inconsistent));
            Check(f.Native.FingerCreatedLists.Count == 0 && f.Native.FingerWrites.Count == 0, "Contradictory aliases allocated scratch or dispatched writes.");
            targets[1] = targets[1] with { FingerIds = nullTarget ? null : new[] { int.MaxValue, -1, int.MaxValue } };
            targets[2] = targets[2] with { FingerIds = targets[1].FingerIds?.ToArray() };
            var p = f.Prepare(targets); p.Commit();
            Check(ActiveEquals(f.Native, targets) && p.Confirm().Observed == true
                && f.Native.FingerWrites.Count(row => row.Receiver == f.Baseline[1].List && row.Operation == "Clear") == (nullTarget ? 0 : 1),
                "The same original List was mutated twice or consistent alias nullness/sequence targets were rejected.");
            Restored(f, p);
        }
    }
    private static void AliasedBackings()
    {
        using (var f = new Fixture(alias: FakeNative.FingerAlias.Backing))
        {
            var roots = f.Native.ActiveRoots; Expect<NotSupportedException>(() => f.Prepare());
            Check(f.Native.FingerCreatedLists.Count == 0 && f.Native.FingerWrites.Count == 0 && f.Native.ActiveRoots == roots,
                "Shared positive-capacity backing was accepted or allocated scratch before alias preflight.");
        }
        using (var f = new Fixture(alias: FakeNative.FingerAlias.EmptyBacking))
        {
            var p = f.Prepare(); p.Commit(); Check(p.Confirm().Observed == true && ActiveEquals(f.Native, f.Targets), "Shared empty Int32 backing could not grow independently.");
            Restored(f, p);
        }
    }
    private static void ClearPolicies()
    {
        foreach (var erase in new[] { false, true })
        {
            using var f = new Fixture(configure: n => n.FingerClearErasesSlots = erase); var targets = f.Targets;
            targets[1] = targets[1] with { FingerIds = Array.Empty<int>() }; var p = f.Prepare(targets); p.Commit();
            var actual = f.Native.FingerStorage(f.Baseline[1].List); var original = f.Baseline[1].Storage!;
            Check(actual.Capacity.Take(original.Size).SequenceEqual(erase ? Enumerable.Repeat(0, original.Size) : original.Capacity.Take(original.Size))
                && actual.Capacity.Skip(original.Size).SequenceEqual(original.Capacity.Skip(original.Size)) && p.Confirm().Observed == true,
                "A legal complete Int32 Clear policy was rejected or its unused tail was changed."); Restored(f, p);
        }
    }
    private static void GrowthAndBound()
    {
        using (var f = new Fixture())
        {
            var targets = f.Targets; targets[1] = targets[1] with { FingerIds = Enumerable.Range(0, 9).Select(index => index % 2 == 0 ? int.MinValue : int.MaxValue).ToArray() };
            var p = f.Prepare(targets); p.Commit(); var growth = f.Native.FingerGrowths.Single(row => row.OldBacking == f.Baseline[1].Storage!.Backing);
            Check(growth.NewCapacity == 12 && f.Native.FingerArrayCapacity(growth.OldBacking).SequenceEqual(growth.OldBefore)
                && f.Native.FingerArrayCapacity(growth.NewBacking).Skip(9).All(value => value == 0) && p.Confirm().Observed == true,
                "Official Int32 Add growth did not preserve its old array and zero new tail."); Restored(f, p);
        }
        using (var f = new Fixture(configure: n => n.FingerCapacity(1, 32769)))
        {
            var targets = f.Targets; targets[1] = targets[1] with { FingerIds = new int[32770] };
            Expect<NotSupportedException>(() => f.Prepare(targets));
            Check(f.Native.FingerCreatedLists.Count == 0 && f.Native.FingerWrites.Count == 0 && f.Native.FingerStorageEquals(f.Baseline),
                "32769→65538 growth was not rejected before scratch allocation or live writes.");
        }
    }
}
