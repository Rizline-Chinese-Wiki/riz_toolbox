using System.Runtime.InteropServices;

namespace RizToolbox.Judgements.Tests.NoteFlags;

sealed partial class FakeNative
{
    internal Action<HoldStateWrite, string>? DuringHoldStateStore;
    internal readonly List<string> HoldStateWriteViolations = new();

    private IntPtr HoldStateInvoke(IntPtr method, IntPtr receiver, IntPtr arguments, IntPtr exception)
    {
        if (method != _judgeQueueClear && method != _judgeQueueAdd && method != _judgeQueueSetValue)
            return TargetInvoke(method, receiver, arguments, exception);
        ++Invocations; Marshal.WriteIntPtr(exception, IntPtr.Zero);
        if (method == _judgeQueueSetValue)
        {
            if (!_holdStateArrays.TryGetValue(receiver, out var list)
                || !_referenceArrays.TryGetValue(receiver, out var slots) || ObjectClass(receiver) != ClassOf(_cursorArrayType))
                throw HoldStateViolation("SetValue on another backing array");
            var requested = Marshal.ReadIntPtr(arguments);
            var index = Marshal.ReadInt32(Marshal.ReadIntPtr(arguments, IntPtr.Size));
            if ((uint)index >= (uint)slots.Length) throw HoldStateViolation("SetValue outside complete capacity");
            HoldStateArgumentsRooted &= requested == IntPtr.Zero || HasRootFor(requested);
            var row = HoldStateBegin("SetValue", receiver, list, index, requested.ToInt64());
            slots[index] = requested; DuringHoldStateStore?.Invoke(row, "slot"); HoldStateEnd(row); return IntPtr.Zero;
        }
        var selected = Array.IndexOf(TargetHoldLists, receiver);
        if (selected < 0 || ObjectClass(receiver) != ClassOf(_judgeListType))
            throw HoldStateViolation("Clear/Add on another List");
        var size = TargetInt(receiver, "_size"); var oldArray = SeekValue<IntPtr>(receiver, "_items");
        var capacity = _referenceArrays[oldArray];
        if (size < 0 || size > capacity.Length) throw HoldStateViolation("Invalid complete List storage");
        var value = method == _judgeQueueAdd ? Marshal.ReadIntPtr(arguments) : IntPtr.Zero;
        HoldStateArgumentsRooted &= value == IntPtr.Zero || HasRootFor(value);
        if (method == _judgeQueueAdd && (!HoldStateNotes.Contains(value) || ObjectClass(value) != ClassOf(SeekType("InLevel.NoteInformation"))))
            throw HoldStateViolation("Add argument outside actual Hold subset");
        var write = HoldStateBegin(method == _judgeQueueClear ? "Clear" : "Add", receiver, selected, null, value.ToInt64());
        if (method == _judgeQueueClear)
        {
            Array.Clear(capacity, 0, size); DuringHoldStateStore?.Invoke(write, "slots");
            TargetSet(receiver, "_size", 0); DuringHoldStateStore?.Invoke(write, "size");
        }
        else
        {
            if (size == capacity.Length)
            {
                var grown = new IntPtr[capacity.Length == 0 ? 4 : checked(capacity.Length * 2)];
                Array.Copy(capacity, grown, size); var newArray = CursorArray(grown);
                HoldStateGrowths.Add(new(oldArray, newArray, Array.AsReadOnly(capacity.ToArray()), grown.Length));
                _holdStateArrays[newArray] = selected; TargetSet(receiver, "_items", newArray);
                capacity = _referenceArrays[newArray]; DuringHoldStateStore?.Invoke(write, "backing");
            }
            capacity[size] = value; DuringHoldStateStore?.Invoke(write, "slot");
            TargetSet(receiver, "_size", size + 1); DuringHoldStateStore?.Invoke(write, "size");
        }
        TargetSet(receiver, "_version", unchecked(TargetInt(receiver, "_version") + 1));
        DuringHoldStateStore?.Invoke(write, "version"); HoldStateEnd(write); return IntPtr.Zero;
    }

    private InvalidOperationException HoldStateViolation(string reason)
    { HoldStateWriteViolations.Add(reason); return new InvalidOperationException("Hold writer escaped fixture surface: " + reason); }

    private bool HoldStateWritable(IntPtr owner, IntPtr field, bool reference)
    {
        var (name, type, offset) = _fieldMetadata[field]; var parent = _gameplayFieldParents[field];
        if (TargetHoldLists.Contains(owner))
            return parent == ClassOf(_judgeListType) && (reference
                ? name == "_items" && type == _cursorArrayType && offset == 0x10
                : name is "_size" or "_version" && type == Int32 && offset == (name == "_size" ? 0x18 : 0x1c));
        if (reference) return false;
        if (owner == TargetHold)
            return parent == ClassOf(_targetHoldType) && type == Single
                && offset == (name == "_nowTime" ? 0x38 : name == "_speed" ? 0x3c : name == "_holdLineLength" ? 0x40 : -1);
        return HoldStateNotes.Contains(owner) && parent == ClassOf(SeekType("InLevel.NoteInformation"))
            && (name == "isStaticNote" && type == Boolean && offset == 0x54
                || name == "lastFrameHoldLineLength" && type == Single && offset == 0x50);
    }

    private void HoldStateSetValue(IntPtr owner, IntPtr field, IntPtr input)
    {
        if (!HoldStateWritable(owner, field, false)) throw HoldStateViolation("value field " + _fieldMetadata[field].Name);
        ++ValueFieldWrites; var width = _fieldMetadata[field].Type == Boolean ? 1 : 4;
        var bytes = new byte[width]; Marshal.Copy(input, bytes, 0, width);
        var row = HoldStateBegin(_fieldMetadata[field].Name, owner,
            TargetHoldLists.Contains(owner) ? Array.IndexOf(TargetHoldLists, owner) : null, null,
            width == 1 ? bytes[0] : BitConverter.ToInt32(bytes));
        _gameplayStorage[(owner, field)] = bytes; DuringHoldStateStore?.Invoke(row, "field"); HoldStateEnd(row);
    }

    private void HoldStateSetObject(IntPtr owner, IntPtr field, IntPtr input)
    {
        if (!HoldStateWritable(owner, field, true)) throw HoldStateViolation("reference field " + _fieldMetadata[field].Name);
        ++ObjectFieldWrites; HoldStateArgumentsRooted &= HasRootFor(input);
        if (!_holdStateArrays.ContainsKey(input) || ObjectClass(input) != ClassOf(_cursorArrayType))
            throw HoldStateViolation("_items argument outside retained Hold backings");
        var row = HoldStateBegin("_items", owner, Array.IndexOf(TargetHoldLists, owner), null, input.ToInt64());
        _gameplayStorage[(owner, field)] = BitConverter.GetBytes(input.ToInt64());
        DuringHoldStateStore?.Invoke(row, "field"); HoldStateEnd(row);
    }

    internal void HoldStateMetadata(IntPtr owner, string name, string? type = null, int? offset = null, bool remove = false)
    {
        var field = TargetField(owner, name); var prior = _fieldMetadata[field];
        if (remove) _structFields[ObjectClass(owner)].Remove(field);
        else _fieldMetadata[field] = (prior.Name, type is null ? prior.Type : SeekType(type, type == "System.Single"), offset ?? prior.Offset);
    }

    internal void HoldStateForeignSource(string key, object value)
    {
        SetGameplayField(key, value); var row = _gameplayFields[key];
        _holdStateOther[(row.Owner, row.Field)] = _gameplayStorage[(row.Owner, row.Field)].ToArray();
    }

    internal void HoldStateReplaceBacking(int slot, int? capacity = null)
    {
        var current = HoldStateList(slot); var values = new IntPtr[capacity ?? current.Capacity.Count];
        Array.Copy(current.Capacity.ToArray(), values, Math.Min(values.Length, current.Capacity.Count));
        HoldStateSetListStorage(slot, Math.Min(current.Size, values.Length), current.Version, values);
    }
}
