namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekHoldStateParticipant
{
    private enum MutationKind { Clear, Add, CapacitySlot, ListField, Hold, Control }
    private sealed record PendingMutation(MutationKind Kind, int ListSlot, int CapacitySlot, long Value,
        SeekHoldListRestoreField ListField, int Ordinal, SeekHoldNoteField HoldField, SeekHoldControlField ControlField,
        SeekHoldListsState? ListsBefore, Dictionary<long, SeekHoldArrayStorage>? ArraysBefore,
        SeekHoldNoteTarget? HoldBefore, SeekHoldNoteTarget? HoldAfter,
        SeekHoldControlValues? ControlBefore, SeekHoldControlValues? ControlAfter);

    private Exception LostOwnership(string message)
    {
        if (_hasDispatched || _pending is not null) { _unrecognized = true; _retained = true; SideEffectsUnknown = true; }
        return new InvalidOperationException(message);
    }
    private SeekHoldRawState ReadSnapshot()
    {
        Local(); var raw = _access.ReadSnapshot(); Local();
        var copied = SeekHoldState.Copy(raw, Local);
        if (copied.Notes.Count != _references.Length
            || copied.Notes.Where((row, ordinal) => row.Reference != _references[ordinal]).Any())
            throw LostOwnership("The actual type-2 Hold subset identities or NoteView order changed.");
        return copied;
    }
    private SeekHoldListsState ReadLists()
    {
        Local(); var raw = _access.ReadLists(); Local(); return SeekHoldState.Copy(raw, Local);
    }
    private SeekHoldNoteTarget ReadHold(int ordinal)
    {
        Local(); var row = _access.ReadHold(ordinal); Local();
        if (row is null || (uint)ordinal >= (uint)_references.Length || row.Reference != _references[ordinal])
            throw LostOwnership("The selected actual Hold receiver changed.");
        return row with { };
    }
    private SeekHoldControlValues ReadControl()
    {
        Local(); var row = _access.ReadControl(); Local(); return SeekHoldState.Copy(row, Local, false);
    }
    private SeekHoldArrayStorage ReadArray(long reference)
    {
        Local(); var raw = _access.ReadBacking(reference); Local();
        var result = SeekHoldState.Copy(raw, Local);
        if (result.Reference != reference) throw LostOwnership("A retained actual Hold backing receiver changed.");
        return result;
    }
    private bool ArraysMatch(IReadOnlyDictionary<long, SeekHoldArrayStorage> expected)
    {
        foreach (var pair in expected) if (!SeekHoldState.Same(ReadArray(pair.Key), pair.Value)) return false;
        return true;
    }
    private void RequireNoPending()
    {
        if (_pending is not null) throw new InvalidOperationException("An unresolved Hold dispatch retains its exact before/after responsibility.");
    }
    private void RequireListsOwned()
    {
        RequireNoPending();
        if (!SeekHoldState.Same(ReadLists(), _expectedLists) || !ArraysMatch(_expectedArrays))
            throw LostOwnership("Complete actual Hold List or historical backing storage changed outside the recognized sequence; it was not overwritten.");
    }
    private void RequireAllOwned()
    {
        RequireNoPending(); var actual = ReadSnapshot();
        if (actual.Identity != _baseline.Identity || !SeekHoldState.Same(actual.Lists, _expectedLists)
            || !SeekHoldState.Same(actual.Control, _expectedControl) || !SeekHoldState.Same(actual.Notes, _expectedHolds)
            || !ArraysMatch(_expectedArrays))
            throw LostOwnership("The complete actual Hold source changed outside the recognized List and scalar sequence; it was not overwritten.");
    }
    private void DispatchList(MutationKind kind, int listSlot, int capacitySlot = 0, long value = 0,
        SeekHoldListRestoreField field = default)
    {
        RequireListsOwned();
        var pending = new PendingMutation(kind, listSlot, capacitySlot, value, field, 0, default, default,
            _expectedLists, new(_expectedArrays), null, null, null, null);
        var dispatched = false;
        void Enter()
        {
            Local();
            if (dispatched || _pending is not null) throw new InvalidOperationException("The final actual Hold List dispatch guard entered twice.");
            _pending = pending; dispatched = true; _hasDispatched = true;
            if (!_restoring) ++_attempted;
            _retained = true; SideEffectsUnknown = true; Local();
        }
        Local();
        switch (kind)
        {
            case MutationKind.Clear: _access.Clear(listSlot, _expectedLists, Enter); break;
            case MutationKind.Add: _access.Add(listSlot, value, _expectedLists, Enter); break;
            case MutationKind.CapacitySlot:
                _access.RestoreCapacitySlot(listSlot, capacitySlot, value, _expectedLists,
                    _expectedArrays[SeekHoldState.At(_baseline.Lists, listSlot).Backing], Enter); break;
            case MutationKind.ListField: _access.RestoreListField(listSlot, field, value, _expectedLists, Enter); break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
        Local();
        if (!dispatched) throw new InvalidOperationException("No final official Hold List dispatch entry was recorded.");
        ResolvePending(true);
    }
    private void DispatchHold(int ordinal, SeekHoldNoteField field, SeekHoldNoteTarget desired)
    {
        RequireNoPending(); var before = _expectedHolds[ordinal];
        if (!SeekHoldState.Same(ReadHold(ordinal), before)) throw LostOwnership("The selected actual Hold scalars changed outside the recognized sequence.");
        var after = SeekHoldState.With(before, field, desired);
        var pending = new PendingMutation(MutationKind.Hold, 0, 0, 0, default, ordinal, field, default,
            null, null, before, after, null, null);
        var dispatched = false;
        Local(); _access.WriteHold(ordinal, field, after, before, () =>
        {
            Local();
            if (dispatched || _pending is not null) throw new InvalidOperationException("The final actual Hold scalar dispatch guard entered twice.");
            _pending = pending; dispatched = true; _hasDispatched = true;
            if (!_restoring) ++_attempted;
            _retained = true; SideEffectsUnknown = true; Local();
        }); Local();
        if (!dispatched) throw new InvalidOperationException("No final exact Hold scalar setter entry was recorded.");
        ResolvePending(true);
    }
    private void DispatchControl(SeekHoldControlField field, SeekHoldControlValues desired)
    {
        RequireNoPending(); var before = _expectedControl;
        if (!SeekHoldState.Same(ReadControl(), before)) throw LostOwnership("The selected actual HoldControl scalars changed outside the recognized sequence.");
        var after = SeekHoldState.With(before, field, desired);
        var pending = new PendingMutation(MutationKind.Control, 0, 0, 0, default, 0, default, field,
            null, null, null, null, before, after);
        var dispatched = false;
        Local(); _access.WriteControl(field, after, before, () =>
        {
            Local();
            if (dispatched || _pending is not null) throw new InvalidOperationException("The final actual HoldControl scalar dispatch guard entered twice.");
            _pending = pending; dispatched = true; _hasDispatched = true;
            if (!_restoring) ++_attempted;
            _retained = true; SideEffectsUnknown = true; Local();
        }); Local();
        if (!dispatched) throw new InvalidOperationException("No final exact HoldControl setter entry was recorded.");
        ResolvePending(true);
    }
    private void ResolvePending(bool requireAfter)
    {
        var pending = _pending ?? throw new InvalidOperationException("No entered actual Hold dispatch is awaiting readback.");
        if (pending.Kind == MutationKind.Hold)
        {
            var actual = ReadHold(pending.Ordinal);
            if (!requireAfter && SeekHoldState.Same(actual, pending.HoldBefore!)) { _pending = null; return; }
            if (!SeekHoldState.Same(actual, pending.HoldAfter!))
            {
                if (SeekHoldState.Same(actual, pending.HoldBefore!))
                    throw new InvalidOperationException("The entered actual Hold setter still has its exact pre-state instead of the required post-state.");
                throw LostOwnership("The entered actual Hold scalar setter has no exact recognized before/after state.");
            }
            _expectedHolds[pending.Ordinal] = pending.HoldAfter!; _pending = null; return;
        }
        if (pending.Kind == MutationKind.Control)
        {
            var actual = ReadControl();
            if (!requireAfter && SeekHoldState.Same(actual, pending.ControlBefore!)) { _pending = null; return; }
            if (!SeekHoldState.Same(actual, pending.ControlAfter!))
            {
                if (SeekHoldState.Same(actual, pending.ControlBefore!))
                    throw new InvalidOperationException("The entered actual HoldControl setter still has its exact pre-state instead of the required post-state.");
                throw LostOwnership("The entered actual HoldControl setter has no exact recognized before/after state.");
            }
            _expectedControl = pending.ControlAfter!; _pending = null; return;
        }
        ResolveListPending(pending, requireAfter);
    }
    private void ResolveListPending(PendingMutation pending, bool requireAfter)
    {
        var actual = ReadLists(); var before = pending.ListsBefore!; var arraysBefore = pending.ArraysBefore!;
        if (SeekHoldState.Same(actual, before) && ArraysMatch(arraysBefore))
        {
            if (!requireAfter) { _pending = null; return; }
            throw new InvalidOperationException("The entered actual Hold List dispatch still has its exact pre-state instead of the required post-state.");
        }
        var list = SeekHoldState.At(before, pending.ListSlot);
        var arrays = new Dictionary<long, SeekHoldArrayStorage>(arraysBefore);
        SeekHoldListsState after;
        if (pending.Kind == MutationKind.CapacitySlot)
        {
            var original = arrays[SeekHoldState.At(_baseline.Lists, pending.ListSlot).Backing];
            var slots = original.CapacitySlots.ToArray(); slots[pending.CapacitySlot] = pending.Value;
            var changed = original with { CapacitySlots = Array.AsReadOnly(slots) }; arrays[original.Reference] = changed;
            after = before;
            for (var slot = 0; slot < 2; ++slot)
            {
                var attached = SeekHoldState.At(after, slot);
                if (attached.Backing == original.Reference) after = SeekHoldState.With(after, attached with { CapacitySlots = changed.CapacitySlots });
            }
        }
        else if (pending.Kind == MutationKind.ListField)
        {
            list = pending.ListField switch
            {
                SeekHoldListRestoreField.Size => list with { Size = checked((int)pending.Value) },
                SeekHoldListRestoreField.Version => list with { Version = checked((int)pending.Value) },
                SeekHoldListRestoreField.Items => list with { Backing = pending.Value,
                    ArrayClass = arrays[pending.Value].Class, CapacitySlots = arrays[pending.Value].CapacitySlots },
                _ => throw new ArgumentOutOfRangeException()
            };
            after = SeekHoldState.With(before, list);
        }
        else
        {
            var slots = list.CapacitySlots.ToArray();
            if (pending.Kind == MutationKind.Clear)
            {
                Array.Clear(slots, 0, list.Size);
                list = list with { Size = 0, Version = unchecked(list.Version + 1), CapacitySlots = Array.AsReadOnly(slots) };
            }
            else if (list.Size < slots.Length)
            {
                slots[list.Size] = pending.Value;
                list = list with { Size = list.Size + 1, Version = unchecked(list.Version + 1), CapacitySlots = Array.AsReadOnly(slots) };
            }
            else
            {
                var grown = SeekHoldState.At(actual, pending.ListSlot);
                var capacity = slots.Length == 0 ? 4 : slots.Length * 2;
                if (grown.Backing == list.Backing || _knownBackings.Contains(grown.Backing) || arrays.ContainsKey(grown.Backing)
                    || grown.ArrayClass != list.ArrayClass || grown.CapacitySlots.Count != capacity || capacity > MaximumCapacity
                    || !SeekHoldState.Same(ReadArray(list.Backing), SeekHoldState.Array(list)))
                    throw LostOwnership("The entered actual Hold Add has no recognized official fresh growth backing and unchanged prior backing.");
                var grownSlots = new long[capacity]; Array.Copy(slots, grownSlots, list.Size); grownSlots[list.Size] = pending.Value;
                list = list with { Backing = grown.Backing, Size = list.Size + 1,
                    Version = unchecked(list.Version + 1), CapacitySlots = Array.AsReadOnly(grownSlots) };
            }
            after = SeekHoldState.With(before, list); arrays[list.Backing] = SeekHoldState.Array(list);
        }
        if (arrays.Values.Sum(array => (long)array.CapacitySlots.Count) > MaximumTotalRetainedCapacity
            || !SeekHoldState.Same(actual, after) || !ArraysMatch(arrays))
            throw LostOwnership("The entered actual Hold List dispatch has no exact complete before/after state; its native effects remain unknown.");
        _expectedLists = after; _expectedArrays = arrays; _knownBackings.Add(list.Backing); _pending = null;
    }
}
