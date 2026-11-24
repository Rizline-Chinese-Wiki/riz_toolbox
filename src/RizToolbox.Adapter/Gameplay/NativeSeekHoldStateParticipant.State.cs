namespace RizToolbox.Adapter.Gameplay;

internal static class SeekHoldState
{
    internal static SeekHoldListStorage At(SeekHoldListsState value, int slot) => slot switch
    {
        0 => value.HoldNotes, 1 => value.RunningHoldNotes,
        _ => throw new ArgumentOutOfRangeException(nameof(slot))
    };
    internal static SeekHoldListsState With(SeekHoldListsState value, SeekHoldListStorage replacement)
        => value with
        {
            HoldNotes = value.HoldNotes.Reference == replacement.Reference ? replacement : value.HoldNotes,
            RunningHoldNotes = value.RunningHoldNotes.Reference == replacement.Reference ? replacement : value.RunningHoldNotes
        };
    internal static bool Same(float first, float second)
        => BitConverter.SingleToInt32Bits(first) == BitConverter.SingleToInt32Bits(second);
    internal static bool Same(SeekHoldNoteTarget first, SeekHoldNoteTarget second)
        => first.Reference == second.Reference && first.IsStaticNote == second.IsStaticNote
            && Same(first.LastFrameHoldLineLength, second.LastFrameHoldLineLength);
    internal static bool Same(SeekHoldControlValues first, SeekHoldControlValues second)
        => Same(first.NowTime, second.NowTime) && Same(first.Speed, second.Speed) && Same(first.HoldLineLength, second.HoldLineLength);
    internal static bool Same(SeekHoldArrayStorage first, SeekHoldArrayStorage second)
        => first.Reference == second.Reference && first.Class == second.Class && first.CapacitySlots.SequenceEqual(second.CapacitySlots);
    internal static bool Same(SeekHoldListStorage first, SeekHoldListStorage second)
        => first.Reference == second.Reference && first.Class == second.Class && first.Backing == second.Backing
            && first.ArrayClass == second.ArrayClass && first.Size == second.Size && first.Version == second.Version
            && first.CapacitySlots.SequenceEqual(second.CapacitySlots);
    internal static bool Same(SeekHoldListsState first, SeekHoldListsState second)
        => Same(first.HoldNotes, second.HoldNotes) && Same(first.RunningHoldNotes, second.RunningHoldNotes);
    internal static SeekHoldArrayStorage Array(SeekHoldListStorage list) => new(list.Backing, list.ArrayClass, list.CapacitySlots);
    internal static bool Same(IReadOnlyList<SeekHoldNoteTarget> first, IReadOnlyList<SeekHoldNoteTarget> second)
        => first.Count == second.Count && !first.Where((row, ordinal) => !Same(row, second[ordinal])).Any();
    internal static long[] Copy(IReadOnlyList<long> input, Action guard)
    {
        ArgumentNullException.ThrowIfNull(input);
        guard(); var count = input.Count; guard();
        if (count is < 0 or > NativeSeekHoldStateParticipant.MaximumCapacity)
            throw new InvalidDataException("A complete Hold sequence exceeds its bound.");
        var result = new long[count];
        for (var index = 0; index < count; ++index) { guard(); result[index] = input[index]; guard(); }
        guard(); if (input.Count != count) throw new InvalidOperationException("A complete Hold sequence changed while being copied."); guard();
        return result;
    }
    internal static SeekHoldArrayStorage Copy(SeekHoldArrayStorage input, Action guard)
    {
        guard();
        if (input is null || input.Reference == 0 || input.Class == 0 || input.CapacitySlots is null)
            throw new InvalidDataException("The complete retained Hold backing is absent.");
        var slots = Copy(input.CapacitySlots, guard); guard();
        return input with { CapacitySlots = System.Array.AsReadOnly(slots) };
    }
    internal static SeekHoldListsState Copy(SeekHoldListsState input, Action guard)
    {
        guard(); if (input is null) throw new InvalidDataException("The two actual Hold Lists are absent.");
        SeekHoldListStorage CopyList(SeekHoldListStorage value)
        {
            guard();
            if (value is null || value.Reference == 0 || value.Class == 0 || value.Backing == 0
                || value.ArrayClass == 0 || value.CapacitySlots is null
                || value.Size is < 0 or > NativeSeekHoldStateParticipant.MaximumCapacity)
                throw new InvalidDataException("An actual Hold List storage is absent or outside its bound.");
            var slots = Copy(value.CapacitySlots, guard);
            if (value.Size > slots.Length) throw new InvalidDataException("A Hold List size exceeds its complete backing capacity.");
            guard(); return value with { CapacitySlots = System.Array.AsReadOnly(slots) };
        }
        var result = input with { HoldNotes = CopyList(input.HoldNotes), RunningHoldNotes = CopyList(input.RunningHoldNotes) };
        var first = result.HoldNotes; var second = result.RunningHoldNotes;
        if (first.Reference == second.Reference && !Same(first, second)
            || first.Backing == second.Backing && !Same(Array(first), Array(second)))
            throw new InvalidDataException("Aliased actual Hold storage returned contradictory complete state.");
        guard(); return result;
    }
    internal static SeekHoldNoteTarget[] Copy(IReadOnlyList<SeekHoldNoteTarget> input, Action guard, bool targets)
    {
        ArgumentNullException.ThrowIfNull(input);
        guard(); var count = input.Count; guard();
        if (count is < 0 or > NativeSeekHoldStateParticipant.MaximumCapacity)
            throw new InvalidDataException("The complete actual Hold collection exceeds its bound.");
        var result = new SeekHoldNoteTarget[count]; var seen = new HashSet<long>();
        for (var ordinal = 0; ordinal < count; ++ordinal)
        {
            guard(); var row = input[ordinal]; guard();
            if (row is null || row.Reference == 0 || !seen.Add(row.Reference)
                || targets && !float.IsFinite(row.LastFrameHoldLineLength))
                throw new InvalidDataException("Actual Hold identities must be present and unique; explicit scalar targets must be finite.");
            result[ordinal] = row with { };
        }
        guard(); if (input.Count != count) throw new InvalidOperationException("The complete actual Hold input changed during copying."); guard();
        return result;
    }
    internal static SeekHoldControlValues Copy(SeekHoldControlValues input, Action guard, bool targets)
    {
        guard();
        if (input is null || targets && (!float.IsFinite(input.NowTime) || !float.IsFinite(input.Speed) || !float.IsFinite(input.HoldLineLength)))
            throw new InvalidDataException("HoldControl values are required; explicit scalar targets must be finite.");
        guard(); return input with { };
    }
    internal static SeekHoldRawState Copy(SeekHoldRawState input, Action guard)
    {
        guard();
        if (input is null || input.Identity is null || input.Identity.Notes is null || input.Identity.Notes.Chart is null
            || input.Identity.Notes.Chart.Run is null || input.Identity.HoldControl == 0)
            throw new InvalidDataException("The exact actual Hold source identity is absent.");
        var lists = Copy(input.Lists, guard); var notes = Copy(input.Notes, guard, false); var control = Copy(input.Control, guard, false);
        guard(); return input with { Lists = lists, Notes = System.Array.AsReadOnly(notes), Control = control };
    }
    internal static SeekHoldNoteTarget With(SeekHoldNoteTarget value, SeekHoldNoteField field, SeekHoldNoteTarget desired) => field switch
    {
        SeekHoldNoteField.IsStaticNote => value with { IsStaticNote = desired.IsStaticNote },
        SeekHoldNoteField.LastFrameHoldLineLength => value with { LastFrameHoldLineLength = desired.LastFrameHoldLineLength },
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };
    internal static SeekHoldControlValues With(SeekHoldControlValues value, SeekHoldControlField field, SeekHoldControlValues desired) => field switch
    {
        SeekHoldControlField.NowTime => value with { NowTime = desired.NowTime },
        SeekHoldControlField.Speed => value with { Speed = desired.Speed },
        SeekHoldControlField.HoldLineLength => value with { HoldLineLength = desired.HoldLineLength },
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };
}
