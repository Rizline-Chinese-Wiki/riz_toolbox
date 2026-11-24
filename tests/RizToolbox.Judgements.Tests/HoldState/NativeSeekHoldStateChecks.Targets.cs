using RizToolbox.Adapter.Gameplay;

namespace RizToolbox.Judgements.Tests.NoteFlags;

internal static partial class NativeSeekHoldStateChecks
{
    // Independent storage model: simulate standard List semantics from captured
    // fixture bytes, then compare every slot and scalar without using Confirm.
    private static void ForwardOracle(Fixture f, SeekHoldStateTargets target)
    {
        var arrays = f.Baseline.Lists.GroupBy(row => row.Backing).ToDictionary(group => group.Key, group => group.First().Capacity.ToArray());
        var result = new Dictionary<IntPtr, FakeNative.HoldStateListStorage>(); var growth = 0;
        for (var slot = 0; slot < 2; ++slot)
        {
            var original = f.Baseline.Lists[slot]; if (result.ContainsKey(original.Reference)) continue;
            var items = arrays[original.Backing]; var backing = original.Backing; var size = original.Size;
            var version = unchecked(original.Version + 1); Array.Clear(items, 0, size); size = 0;
            foreach (var reference in Lists(target)[slot])
            {
                if (size == items.Length)
                {
                    var capacity = items.Length == 0 ? 4 : checked(items.Length * 2);
                    Check(growth < f.Native.HoldStateGrowths.Count, "Oracle expected an official Add growth that was not logged.");
                    var change = f.Native.HoldStateGrowths[growth++];
                    Check(change.Before == backing && change.NewCapacity == capacity && change.OldCapacity.SequenceEqual(items),
                        "Official Add growth disagreed with the independent 0-to-4/doubling and complete old-capacity model.");
                    var next = new IntPtr[capacity]; Array.Copy(items, next, size); backing = change.After; items = next; arrays.Add(backing, next);
                }
                items[size++] = new IntPtr(reference); version = unchecked(version + 1);
            }
            result.Add(original.Reference, new(original.Reference, backing, size, version, Array.AsReadOnly(items.ToArray())));
        }
        Check(growth == f.Native.HoldStateGrowths.Count, "A List unexpectedly grew outside the independent forward model.");
        foreach (var array in arrays)
            Check(f.Native.HoldStateCapacity(array.Key).SequenceEqual(array.Value), "An original or intermediate complete backing array differs from the independent oracle.");
        var expected = new FakeNative.HoldStateStorage(f.Baseline.Lists.Select(row => result[row.Reference]).ToArray(),
            f.Baseline.Notes.Select(row =>
            {
                var desired = target.Notes.Single(note => note.Reference == row.Reference.ToInt64());
                return new FakeNative.HoldStateNoteStorage(row.Reference, desired.IsStaticNote, Bits(desired.LastFrameHoldLineLength));
            }).ToArray(), Bits(target.Control.NowTime), Bits(target.Control.Speed), Bits(target.Control.HoldLineLength));
        Check(f.Native.HoldStateEquals(expected), "Committed Hold storage differs from the independent complete-storage oracle.");
    }

    private static void ExplicitTargets()
    {
        using var f = new Fixture(); var target = f.Targets;
        var lists = Lists(target).Select(values => values.ToList()).ToArray(); var notes = target.Notes.ToList();
        var p = f.Prepare(new(lists[0], lists[1], notes, target.Control));
        foreach (var list in lists) { list.Clear(); list.Add(0); } notes.Clear(); notes.Add(new(0, false, float.NaN));
        p.VerifyPrepared(); p.Commit(); ForwardOracle(f, target);
        var expected = Lists(target).SelectMany((values, slot) => new[] { ("Clear", (int?)slot, 0L) }
            .Concat(values.Select(reference => ("Add", (int?)slot, reference))))
            .Concat(f.Native.HoldStateNotes.SelectMany((note, ordinal) =>
            {
                var desired = target.Notes.Single(row => row.Reference == note.ToInt64());
                return new[] { ("isStaticNote", (int?)null, desired.IsStaticNote ? 1L : 0L),
                    ("lastFrameHoldLineLength", (int?)null, (long)Bits(desired.LastFrameHoldLineLength)) };
            }))
            .Concat(new[] { ("_nowTime", (int?)null, (long)Bits(target.Control.NowTime)),
                ("_speed", (int?)null, (long)Bits(target.Control.Speed)), ("_holdLineLength", (int?)null, (long)Bits(target.Control.HoldLineLength)) }).ToArray();
        Check(p.AttemptedWrites == expected.Length && f.Native.HoldStateWrites.Select(row => (row.Operation, row.List, row.Requested)).SequenceEqual(expected)
            && f.Native.HoldStateWrites.All(row => row.Stored) && p.Confirm().Observed == true,
            "Explicit Hold occurrence lists or caller-copied scalar targets were reordered, deduplicated or read too late.");
        Restored(f, p);
    }

    private static void ActualHolds()
    {
        foreach (var shape in new[] { FakeNative.NoteViewShape.Partial, FakeNative.NoteViewShape.MultipleSegments })
        {
            using var f = new Fixture(shape); var targets = f.Targets; var actual = f.Native.HoldStateNotes;
            Check(actual.Count == (shape == FakeNative.NoteViewShape.MultipleSegments ? 3 : 2)
                && actual.All(note => f.Native.TargetInt(f.Native.HoldStateData(note), "type") == 2),
                "Fixture failed to retain exactly the actual type-two Hold subset.");
            if (shape == FakeNative.NoteViewShape.MultipleSegments)
                Check(actual[0] != actual[^1] && f.Native.HoldStateData(actual[0]) == f.Native.HoldStateData(actual[^1]),
                    "Multiple actual Hold instances must share chart data while retaining separate runtime identities.");
            var p = f.Prepare(targets); f.Run.Dispose(); p.Commit(); ForwardOracle(f, targets);
            Check(p.Confirm().Observed == true && f.Native.HoldStateWrites.Where(row => row.Operation is "isStaticNote" or "lastFrameHoldLineLength")
                .All(row => actual.Contains(row.Receiver)), "Hold writer touched a non-Hold or depended on the disposed caller capture.");
            Restored(f, p);
        }
    }

    private static void InvalidIdentities()
    {
        using var f = new Fixture(); var good = f.Targets; var nonHold = f.Native.TargetNote(1).ToInt64();
        var unknown = f.Native.JudgeForeignTail.ToInt64();
        var invalid = new[]
        {
            good with { HoldNotes = new[] { 0L } }, good with { RunningHoldNotes = new[] { unknown } },
            good with { HoldNotes = new[] { nonHold } }, good with { Notes = good.Notes.Skip(1).ToArray() },
            good with { Notes = good.Notes.Append(new(unknown, false, 0)).ToArray() },
            good with { Notes = good.Notes.Append(new(nonHold, true, 1)).ToArray() },
            good with { Notes = new[] { good.Notes[0], good.Notes[0] } },
            good with { Notes = good.Notes.Select((note, slot) => slot == 0 ? note with { Reference = 0 } : note).ToArray() }
        };
        foreach (var targets in invalid)
        {
            var roots = f.Native.ActiveRoots; ExpectFailure(() => f.Prepare(targets));
            Check(f.Native.HoldStateWrites.Count == 0 && f.Native.ActiveRoots == roots && f.Native.HoldStateEquals(f.Baseline),
                "An invalid actual Hold identity reached native writes or leaked source ownership.");
        }
        using var retry = f.Prepare(); retry.VerifyPrepared();
    }

    private static void InvalidTargets()
    {
        using var f = new Fixture(); var good = f.Targets;
        var invalid = new List<SeekHoldStateTargets>
        {
            good with { HoldNotes = null! }, good with { RunningHoldNotes = null! }, good with { Notes = null! }, good with { Control = null! },
            good with { Notes = good.Notes.Select((note, slot) => slot == 0 ? null! : note).ToArray() },
            good with { HoldNotes = Enumerable.Repeat(good.HoldNotes[0], NativeSeekHoldStateParticipant.MaximumCapacity + 1).ToArray() }
        };
        foreach (var nonfinite in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            invalid.Add(good with { Notes = good.Notes.Select((note, slot) => slot == 0 ? note with { LastFrameHoldLineLength = nonfinite } : note).ToArray() });
            invalid.Add(good with { Control = good.Control with { NowTime = nonfinite } });
            invalid.Add(good with { Control = good.Control with { Speed = nonfinite } });
            invalid.Add(good with { Control = good.Control with { HoldLineLength = nonfinite } });
        }
        foreach (var targets in invalid)
        {
            var roots = f.Native.ActiveRoots; ExpectFailure(() => f.Prepare(targets));
            Check(f.Native.HoldStateWrites.Count == 0 && f.Native.ActiveRoots == roots && f.Native.HoldStateEquals(f.Baseline),
                "A null, oversized or nonfinite explicit Hold target reached native writes or leaked preparation roots.");
        }
    }

    private static void AliasedLists()
    {
        using var f = new Fixture(alias: FakeNative.HoldStateAlias.List); var good = f.Targets;
        Expect<InvalidDataException>(() => f.Prepare(good)); Check(f.Native.HoldStateWrites.Count == 0, "Contradictory targets for one Hold List were written.");
        var target = good with { RunningHoldNotes = good.HoldNotes.ToArray() }; var p = f.Prepare(target); p.Commit(); ForwardOracle(f, target);
        Check(f.Native.HoldStateWrites.Count(row => row.Operation == "Clear") == 1 && f.Native.HoldStateWrites.All(row => row.List != 1)
            && p.Confirm().Observed == true, "An aliased actual Hold List was written twice."); Restored(f, p);
    }

    private static void AliasedBackings()
    {
        using (var f = new Fixture(alias: FakeNative.HoldStateAlias.Backing))
        {
            var roots = f.Native.ActiveRoots; Expect<NotSupportedException>(() => f.Prepare());
            Check(f.Native.HoldStateWrites.Count == 0 && f.Native.ActiveRoots == roots && f.Native.HoldStateEquals(f.Baseline),
                "Two Hold Lists sharing nonempty-capacity backing were admitted.");
        }
        using (var f = new Fixture(alias: FakeNative.HoldStateAlias.EmptyBacking))
        {
            var target = f.Targets; var p = f.Prepare(target); p.Commit(); ForwardOracle(f, target);
            Check(f.Native.HoldStateGrowths.Count == 2 && p.Confirm().Observed == true,
                "Shared zero-capacity backing did not independently grow two actual Lists."); Restored(f, p);
        }
    }

    private static void CapacityGrowth()
    {
        using var f = new Fixture(configure: n => n.HoldStateCapacity(0, 0)); var good = f.Targets;
        var target = good with { HoldNotes = Enumerable.Range(0, 10).Select(slot => good.HoldNotes[slot % good.HoldNotes.Count]).ToArray() };
        var p = f.Prepare(target); p.Commit(); ForwardOracle(f, target);
        Check(f.Native.HoldStateGrowths.Select(row => row.NewCapacity).SequenceEqual(new[] { 4, 8, 16 })
            && f.Native.HoldStateGrowths.All(row => f.Native.NoteViewHandlesFor(row.After).Length != 0) && p.Confirm().Observed == true,
            "Official Hold Add did not retain every 0-to-4, doubling and intermediate backing transition."); Restored(f, p);
    }

    private static void CompleteStorage()
    {
        using var f = new Fixture(); var target = f.Targets with { HoldNotes = Enumerable.Repeat(f.Targets.HoldNotes[0], 6).ToArray() };
        var p = f.Prepare(target); p.Commit(); ForwardOracle(f, target); var forward = p.AttemptedWrites;
        Restored(f, p);
        Check(f.Native.HoldStateWrites.Skip(forward).Any(row => row.Operation == "SetValue" && new IntPtr(row.Requested) == f.Native.JudgeForeignTail)
            && f.Native.HoldStateWrites.Skip(forward).All(row => row.Operation is not ("Clear" or "Add")),
            "Full storage restore skipped a foreign inactive tail reference or replayed List consumers.");
    }

    private static void FloatBits()
    {
        using var f = new Fixture(configure: n =>
        {
            n.TargetSet(n.HoldStateNotes[0], "lastFrameHoldLineLength", BitConverter.Int32BitsToSingle(unchecked((int)0xffc12345)));
            n.TargetSet(n.HoldStateNotes[1], "lastFrameHoldLineLength", BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)));
            n.TargetSet(n.TargetHold, "_nowTime", BitConverter.Int32BitsToSingle(0x7fc54321));
            n.TargetSet(n.TargetHold, "_speed", BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)));
            n.TargetSet(n.TargetHold, "_holdLineLength", BitConverter.Int32BitsToSingle(unchecked((int)0xffc67890)));
        });
        var target = f.Targets with { Control = new(0f, -0f, 0f) }; var p = f.Prepare(target); p.Commit(); ForwardOracle(f, target);
        Check(p.Confirm().Observed == true, "Signed-zero finite explicit values were not observed bit exactly."); Restored(f, p);
    }

    private static void EmptyLists()
    {
        using var f = new Fixture(configure: n => { foreach (var list in n.TargetHoldLists) n.TargetSet(list, "_size", 0); });
        var target = f.Targets with { HoldNotes = Array.Empty<long>(), RunningHoldNotes = Array.Empty<long>() };
        var p = f.Prepare(target); p.Commit(); ForwardOracle(f, target);
        Check(f.Native.HoldStateWrites.Count(row => row.Operation == "Clear") == 2 && f.Native.HoldStateWrites.All(row => row.Operation != "Add"),
            "Empty explicit Hold targets skipped official Clear or appended notes."); Restored(f, p);
    }
}
