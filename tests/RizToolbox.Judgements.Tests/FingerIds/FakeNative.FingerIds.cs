using System.Runtime.InteropServices;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

// Independent Int32-list storage atop the already validated graph/read fixture.
// It does not change the JudgeQueues or common support implementations.
sealed partial class FakeNative
{
    internal enum FingerAlias { None, List, Backing, EmptyBacking }
    internal sealed record FingerListState(IntPtr Reference, IntPtr Backing, int Size, int Version, IReadOnlyList<int> Capacity);
    internal sealed record FingerNoteState(IntPtr Reference, IntPtr List, FingerListState? Storage);
    internal sealed record FingerWrite(int Ordinal, string Operation, IntPtr Receiver, int Note, int? Slot, long Requested)
    { internal bool Stored; }
    internal sealed record FingerGrowth(IntPtr OldBacking, IntPtr NewBacking, int OldCapacity, int NewCapacity, IReadOnlyList<int> OldBefore);
    internal readonly List<FingerWrite> FingerWrites = new();
    internal readonly List<FingerGrowth> FingerGrowths = new();
    internal readonly List<IntPtr> FingerNotes = new(), FingerCreatedLists = new();
    internal Action<FingerWrite>? BeforeFingerStore, AfterFingerStore;
    internal Action<IntPtr>? AfterFingerAllocation;
    internal Action<uint, IntPtr>? AfterFingerRoot;
    internal int FailFingerBeforeStore, FailFingerAfterStore;
    internal bool FailFingerAllocation, FingerClearErasesSlots;
    internal bool FingerReceiversRooted = true, FingerArgumentsRooted = true;
    internal int FingerGetValues, FingerBoxCalls;
    private IntPtr _fingerListType, _fingerArrayType, _fingerSystemArrayType, _fingerField, _fingerEmptyArray;
    private IntPtr _fingerConstructor, _fingerClear, _fingerAdd, _fingerRank, _fingerLength, _fingerGet, _fingerSet;
    private readonly Dictionary<IntPtr, int[]> _fingerArrays = new();
    private readonly HashSet<IntPtr> _fingerLists = new();
    private readonly Dictionary<(IntPtr Owner, IntPtr Field), byte[]> _fingerOtherStorage = new();

    internal void ConfigureFingerIds(NoteViewShape shape = NoteViewShape.Complete, FingerAlias alias = FingerAlias.None,
        bool reuseSource = false)
    {
        if (!reuseSource) ConfigureJudgeQueues(shape);
        _fingerListType = reuseSource ? _targetFingerType : AddType("System.Collections.Generic.List<System.Int32>", 21, false, IntPtr.Size);
        _fingerArrayType = AddType("System.Int32[]", 29, false, IntPtr.Size);
        _fingerSystemArrayType = SeekType("System.Array");
        _fingerEmptyArray = FingerArray(Array.Empty<int>());
        _fingerConstructor = GameplayMethodAdd(_fingerListType, ".ctor", false, Void);
        _fingerClear = GameplayMethodAdd(_fingerListType, "Clear", false, Void);
        _fingerAdd = GameplayMethodAdd(_fingerListType, "Add", false, Void, Int32);
        _fingerRank = GameplayMethodAdd(_fingerArrayType, "get_Rank", false, Int32);
        _fingerLength = GameplayMethodAdd(_fingerArrayType, "get_Length", false, Int32);
        _fingerGet = GameplayMethodAdd(_fingerArrayType, "GetValue", false, Object, Int32);
        _fingerSet = GameplayMethodAdd(_fingerSystemArrayType, "SetValue", false, Void, Object, Int32);
        FingerNotes.AddRange(_referenceArrays[CursorAllArray]);
        var lists = new[] { IntPtr.Zero,
            FingerNewList(3, -17, int.MinValue, 7, int.MinValue, 123, int.MaxValue, -99),
            FingerNewList(2, int.MinValue, -7, int.MaxValue, int.MinValue, 9),
            FingerNewList(0, int.MaxValue, -3, 123, 99), FingerNewList(1, -1, 0) };
        if (alias == FingerAlias.List) lists[2] = lists[1];
        if (alias == FingerAlias.Backing) FingerSetList(lists[2], "_items", FingerBacking(lists[1]));
        if (alias == FingerAlias.EmptyBacking)
            for (var index = 1; index < lists.Length; ++index)
            { FingerSetList(lists[index], "_items", _fingerEmptyArray); FingerSetList(lists[index], "_size", 0); }
        for (var index = 0; index < FingerNotes.Count; ++index)
        {
            // The retained common note accessor validates these official
            // descriptors; this fixture still rejects every Boolean setter.
            for (var slot = 0; slot < FlagNames.Length; ++slot)
                if (!reuseSource) SeekField("finger.unselected.flag." + index + "." + slot, SeekType("InLevel.NoteInformation"), FlagNames[slot],
                    Boolean, FingerNotes[index], index % 2 == slot % 2, FlagOffsets[slot]);
            var key = "finger.note." + index;
            SeekField(key, SeekType("InLevel.NoteInformation"), "fingerIds", _fingerListType,
                FingerNotes[index], index < lists.Length ? lists[index] : IntPtr.Zero, 0x58);
            _fingerField = _gameplayFields[key].Field;
        }
        ConfigureFingerExports();
        GameplayApi = new NativeRuntime(ExportPointer); GameplayApi.BindCurrentThread();
        FingerRememberOtherStorage();
    }
    private IntPtr FingerArray(int[] slots)
    {
        var value = GameplayAllocate(checked(ArrayHeaderBytes + slots.Length * sizeof(int)));
        for (var index = 0; index < ArrayHeaderBytes; ++index) Marshal.WriteByte(value, index, 0);
        Marshal.WriteIntPtr(value, ClassOf(_fingerArrayType)); _boxedTypes[value] = _fingerArrayType;
        _fingerArrays[value] = slots.ToArray();
        if (slots.Length != 0) Marshal.Copy(slots, 0, IntPtr.Add(value, ArrayHeaderBytes), slots.Length);
        return value;
    }
    private IntPtr FingerNewList(int size, int version, params int[] slots)
    {
        var value = SeekObject(_fingerListType); _fingerLists.Add(value);
        FingerInitializeList(value, FingerArray(slots), size, version); return value;
    }
    private void FingerInitializeList(IntPtr list, IntPtr backing, int size, int version)
    {
        var key = "finger.list." + _gameplayFields.Count;
        SeekField(key + ".items", _fingerListType, "_items", _fingerArrayType, list, backing, 0x10);
        SeekField(key + ".size", _fingerListType, "_size", Int32, list, size, 0x18);
        SeekField(key + ".version", _fingerListType, "_version", Int32, list, version, 0x1c);
    }
    private IntPtr FingerListField(string name) => _structFields[ClassOf(_fingerListType)].Single(field => _fieldMetadata[field].Name == name);
    internal IntPtr FingerReference(int note) => new(BitConverter.ToInt64(_gameplayStorage[(FingerNotes[note], _fingerField)]));
    internal IntPtr FingerIntArrayClass => ClassOf(_fingerArrayType);
    internal IntPtr FingerIntListClass => ClassOf(_fingerListType);
    internal IntPtr FingerBacking(IntPtr list) => new(BitConverter.ToInt64(_gameplayStorage[(list, FingerListField("_items"))]));
    internal int FingerScalar(IntPtr list, string name) => BitConverter.ToInt32(_gameplayStorage[(list, FingerListField(name))]);
    internal int[] FingerArrayCapacity(IntPtr backing) => _fingerArrays[backing].ToArray();
    internal FingerListState FingerStorage(IntPtr list) => new(list, FingerBacking(list), FingerScalar(list, "_size"),
        FingerScalar(list, "_version"), Array.AsReadOnly(FingerArrayCapacity(FingerBacking(list))));
    internal FingerNoteState[] FingerSnapshot() => FingerNotes.Select((note, index) =>
    { var list = FingerReference(index); return new FingerNoteState(note, list, list == IntPtr.Zero ? null : FingerStorage(list)); }).ToArray();
    internal static bool FingerSame(FingerListState? a, FingerListState? b) => a is null ? b is null : b is not null
        && a.Reference == b.Reference && a.Backing == b.Backing && a.Size == b.Size && a.Version == b.Version && a.Capacity.SequenceEqual(b.Capacity);
    internal bool FingerStorageEquals(IReadOnlyList<FingerNoteState> baseline) => FingerSnapshot()
        .Where((row, index) => row.Reference != baseline[index].Reference || row.List != baseline[index].List || !FingerSame(row.Storage, baseline[index].Storage)).Any() == false;
    internal void FingerSetList(IntPtr list, string name, object value) => _gameplayStorage[(list, FingerListField(name))] = value switch
    { int integer => BitConverter.GetBytes(integer), IntPtr pointer => BitConverter.GetBytes(pointer.ToInt64()), _ => throw new ArgumentException("Synthetic finger storage type.") };
    internal void FingerSetReference(int note, IntPtr value) => _gameplayStorage[(FingerNotes[note], _fingerField)] = BitConverter.GetBytes(value.ToInt64());
    internal void FingerSetSlot(IntPtr backing, int slot, int value)
    { _fingerArrays[backing][slot] = value; Marshal.WriteInt32(backing, checked(ArrayHeaderBytes + slot * sizeof(int)), value); }
    internal void FingerReplaceBacking(IntPtr list, int? capacity = null)
    {
        var old = FingerArrayCapacity(FingerBacking(list)); var values = new int[capacity ?? old.Length];
        Array.Copy(old, values, Math.Min(old.Length, values.Length)); FingerSetList(list, "_items", FingerArray(values));
    }
    internal void FingerCapacity(int note, int capacity)
    {
        var list = FingerReference(note); FingerReplaceBacking(list, capacity);
        FingerSetList(list, "_size", Math.Min(FingerScalar(list, "_size"), capacity));
    }
    internal void FingerForeignSource(string key, object value)
    {
        SetGameplayField(key, value); var row = _gameplayFields[key];
        _fingerOtherStorage[(row.Owner, row.Field)] = _gameplayStorage[(row.Owner, row.Field)].ToArray();
    }
    internal void FingerRememberOtherStorage()
    {
        _fingerOtherStorage.Clear();
        foreach (var row in _gameplayStorage)
            if (row.Key.Field != _fingerField && (!_fingerLists.Contains(row.Key.Owner)
                || _fieldMetadata[row.Key.Field].Name is not ("_items" or "_size" or "_version")))
                _fingerOtherStorage[row.Key] = row.Value.ToArray();
    }
    internal bool FingerOtherStorageUnchanged() => _fingerOtherStorage.All(row => _gameplayStorage.TryGetValue(row.Key, out var value) && value.SequenceEqual(row.Value));
}
