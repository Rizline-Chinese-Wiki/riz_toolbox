using System.Runtime.InteropServices;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

// Uses the real RunValue / NativeRuntime publication path. These exports model
// the official List operations and guarded baseline-storage restoration only.
sealed partial class FakeNative
{
    internal sealed record JudgeQueueWrite(int Ordinal, string Operation, int List,
        IntPtr Receiver, int? Slot, long Requested)
    {
        internal bool Stored;
    }
    internal sealed record JudgeQueueStorage(IntPtr List, IntPtr Backing, int Size, int Version,
        IReadOnlyList<IntPtr> Capacity);
    internal sealed record JudgeQueueGrowth(IntPtr OldBacking, IntPtr NewBacking, int OldCapacity,
        int NewCapacity, IReadOnlyList<IntPtr> OldBefore);
    internal readonly List<JudgeQueueWrite> JudgeQueueWrites = new();
    internal readonly List<JudgeQueueGrowth> JudgeQueueGrowths = new();
    internal Action<JudgeQueueWrite>? BeforeJudgeQueueStore, AfterJudgeQueueStore;
    internal int FailJudgeQueueBeforeStore, FailJudgeQueueAfterStore;
    internal bool JudgeQueueReceiversRooted = true, JudgeQueueArgumentsRooted = true;
    private IntPtr _judgeQueueClear, _judgeQueueAdd, _judgeQueueSetValue;
    private readonly Dictionary<(IntPtr Owner, IntPtr Field), byte[]> _judgeQueueOtherStorage = new();
    private readonly Dictionary<IntPtr, int> _judgeQueueArrayLists = new();

    internal void ConfigureJudgeQueues(NoteViewShape shape = NoteViewShape.Complete,
        JudgeAlias alias = JudgeAlias.None)
    {
        ConfigureJudgeStateView(shape, alias);
        _judgeQueueClear = GameplayMethodAdd(_judgeListType, "Clear", false, Void);
        _judgeQueueAdd = GameplayMethodAdd(_judgeListType, "Add", false, Void, SeekType("InLevel.NoteInformation"));
        _judgeQueueSetValue = GameplayMethodAdd(_cursorArrayType, "SetValue", false, Void, Object, Int32);
        Add("il2cpp_class_from_name", "ClassLookup", nameof(FlagFindClass));
        Add("il2cpp_class_get_methods", "Ptr2", nameof(JudgeQueueMethods));
        Add("il2cpp_class_get_method_from_name", "MethodLookup", nameof(JudgeQueueNamedMethod));
        Add("il2cpp_runtime_invoke", "RuntimeInvoke", nameof(JudgeQueueInvoke));
        Add("il2cpp_field_set_value", "FieldSetValue", nameof(JudgeQueueSetFieldValue));
        Add("il2cpp_field_set_value_object", "FieldSetObject", nameof(JudgeQueueSetFieldObject));
        GameplayApi = new NativeRuntime(ExportPointer); GameplayApi.BindCurrentThread();
        for (var slot = 0; slot < 3; ++slot) _judgeQueueArrayLists.TryAdd(JudgeBacking(slot), slot);
        JudgeQueueRememberOtherStorage();
    }

    internal JudgeQueueStorage[] JudgeQueueSnapshot() => Enumerable.Range(0, 3).Select(slot =>
        new JudgeQueueStorage(JudgeLists[slot], JudgeBacking(slot), JudgeScalar(slot, "_size"),
            JudgeScalar(slot, "_version"), Array.AsReadOnly(JudgeCapacity(slot)))).ToArray();
    internal long[] JudgeRuntimeReferences => _referenceArrays[CursorAllArray].Select(value => value.ToInt64()).ToArray();
    internal IntPtr[] JudgeQueueArrayCapacity(IntPtr backing) => _referenceArrays[backing].ToArray();
    internal void JudgeQueueSetArraySlot(IntPtr backing, int slot, IntPtr value) => _referenceArrays[backing][slot] = value;
    internal void JudgeQueueCapacity(int list, int capacity, bool sharedEmpty = false)
    {
        var backing = sharedEmpty && capacity == 0 && list != 0 ? JudgeBacking(0) : CursorArray(new IntPtr[capacity]);
        var prior = JudgeCapacity(list);
        if (!sharedEmpty) Array.Copy(prior, _referenceArrays[backing], Math.Min(prior.Length, capacity));
        JudgeSetList(list, "_items", backing);
        JudgeSetList(list, "_size", Math.Min(JudgeScalar(list, "_size"), capacity));
        _judgeQueueArrayLists.TryAdd(backing, list);
    }
    internal bool JudgeQueueStorageEquals(IReadOnlyList<JudgeQueueStorage> expected)
    {
        var actual = JudgeQueueSnapshot();
        return actual.Length == expected.Count && actual.Where((row, slot) =>
            row.List != expected[slot].List || row.Backing != expected[slot].Backing
            || row.Size != expected[slot].Size || row.Version != expected[slot].Version
            || !row.Capacity.SequenceEqual(expected[slot].Capacity)).Any() == false;
    }
    internal long[][] JudgeQueueActive() => Enumerable.Range(0, 3).Select(slot =>
        JudgeCapacity(slot).Take(JudgeScalar(slot, "_size")).Select(value => value.ToInt64()).ToArray()).ToArray();
    internal int JudgeHoldValue => BitConverter.ToInt32(_gameplayStorage[(JudgeOwner, JudgeField(JudgeOwner, "holdOnJudgeCount"))]);
    internal void JudgeQueueForeignSource(string key, object value)
    {
        SetGameplayField(key, value);
        var row = _gameplayFields[key];
        _judgeQueueOtherStorage[(row.Owner, row.Field)] = _gameplayStorage[(row.Owner, row.Field)].ToArray();
    }
    internal void JudgeQueueRememberOtherStorage()
    {
        _judgeQueueOtherStorage.Clear();
        foreach (var row in _gameplayStorage)
            if (!JudgeLists.Contains(row.Key.Owner) || _fieldMetadata[row.Key.Field].Name is not ("_items" or "_size" or "_version"))
                _judgeQueueOtherStorage.Add(row.Key, row.Value.ToArray());
    }
    internal bool JudgeQueueOtherStorageUnchanged() => _judgeQueueOtherStorage.All(row =>
        _gameplayStorage.TryGetValue(row.Key, out var value) && value.SequenceEqual(row.Value));

    private IntPtr JudgeQueueMethods(IntPtr cls, IntPtr iterator)
    {
        var value = cls == ClassOf(_judgeListType) || cls == ClassOf(_cursorArrayType)
            ? GameplayMethods(cls, iterator) : FlagMethods(cls, iterator);
        AfterJudgeMethodResolution?.Invoke("enumerate", cls);
        return value;
    }
    private IntPtr JudgeQueueNamedMethod(IntPtr cls, string name, int count)
    {
        var value = cls == ClassOf(_judgeListType) || cls == ClassOf(_cursorArrayType)
            ? _gameplayMethods.FirstOrDefault(row => row.Value.Class == cls && row.Value.Name == name
                && row.Value.Parameters.Length == count).Key
            : FlagNamedMethod(cls, name, count);
        AfterJudgeMethodResolution?.Invoke(name, cls);
        return value;
    }
    private JudgeQueueWrite JudgeQueueBegin(string operation, int list, IntPtr receiver, int? slot, long requested)
    {
        JudgeQueueReceiversRooted &= HasRootFor(receiver);
        var row = new JudgeQueueWrite(JudgeQueueWrites.Count + 1, operation, list, receiver, slot, requested);
        JudgeQueueWrites.Add(row); BeforeJudgeQueueStore?.Invoke(row);
        if (row.Ordinal == FailJudgeQueueBeforeStore)
            throw new IOException("Synthetic Judge queue native write failed before storage.");
        return row;
    }
    private void JudgeQueueEnd(JudgeQueueWrite row)
    {
        row.Stored = true; AfterJudgeQueueStore?.Invoke(row);
        if (row.Ordinal == FailJudgeQueueAfterStore)
            throw new IOException("Synthetic Judge queue native write failed after storage.");
    }
    private IntPtr JudgeQueueInvoke(IntPtr method, IntPtr receiver, IntPtr arguments, IntPtr exception)
    {
        if (method != _judgeQueueClear && method != _judgeQueueAdd && method != _judgeQueueSetValue)
            return FlagInvoke(method, receiver, arguments, exception);
        ++Invocations; Marshal.WriteIntPtr(exception, IntPtr.Zero);
        if (method == _judgeQueueSetValue)
        {
            var list = _judgeQueueArrayLists.TryGetValue(receiver, out var sourceList) ? sourceList : -1;
            if (!_referenceArrays.TryGetValue(receiver, out var slots) || ObjectClass(receiver) != ClassOf(_cursorArrayType))
                throw new InvalidOperationException("Judge restoration escaped the exact NoteInformation backing array.");
            var value = Marshal.ReadIntPtr(arguments);
            var index = Marshal.ReadInt32(Marshal.ReadIntPtr(arguments, IntPtr.Size));
            if ((uint)index >= (uint)slots.Length) throw new InvalidOperationException("Judge array restoration escaped capacity.");
            JudgeQueueArgumentsRooted &= value == IntPtr.Zero || HasRootFor(value);
            var row = JudgeQueueBegin("SetValue", list, receiver, index, value.ToInt64());
            slots[index] = value; JudgeQueueEnd(row); return IntPtr.Zero;
        }
        var selected = Array.IndexOf(JudgeLists, receiver);
        if (selected < 0 || ObjectClass(receiver) != ClassOf(_judgeListType))
            throw new InvalidOperationException("A Judge queue operation escaped a retained exact List receiver.");
        var size = JudgeScalar(selected, "_size");
        var backing = _referenceArrays[JudgeBacking(selected)];
        var requested = method == _judgeQueueAdd ? Marshal.ReadIntPtr(arguments) : IntPtr.Zero;
        JudgeQueueArgumentsRooted &= requested == IntPtr.Zero || HasRootFor(requested);
        if (method == _judgeQueueAdd && (requested == IntPtr.Zero || ObjectClass(requested) != ClassOf(SeekType("InLevel.NoteInformation"))))
            throw new InvalidOperationException("A Judge Add argument escaped the exact non-null runtime-note class.");
        if (size < 0 || size > backing.Length)
            throw new InvalidOperationException("An official synthetic List write has invalid complete storage.");
        var write = JudgeQueueBegin(method == _judgeQueueClear ? "Clear" : "Add", selected, receiver, null, requested.ToInt64());
        if (method == _judgeQueueClear)
        {
            Array.Clear(backing, 0, size); JudgeSetList(selected, "_size", 0);
        }
        else
        {
            if (size == backing.Length)
            {
                var oldBacking = JudgeBacking(selected);
                var newCapacity = backing.Length == 0 ? 4 : checked(backing.Length * 2);
                var grown = new IntPtr[newCapacity]; Array.Copy(backing, grown, size);
                var newBacking = CursorArray(grown);
                JudgeQueueGrowths.Add(new(oldBacking, newBacking, backing.Length, newCapacity, Array.AsReadOnly(backing.ToArray())));
                _judgeQueueArrayLists[newBacking] = selected;
                JudgeSetList(selected, "_items", newBacking); backing = _referenceArrays[newBacking];
            }
            backing[size] = requested; JudgeSetList(selected, "_size", size + 1);
        }
        JudgeSetList(selected, "_version", unchecked(JudgeScalar(selected, "_version") + 1));
        JudgeQueueEnd(write); return IntPtr.Zero;
    }
    private bool JudgeQueueWritableField(IntPtr owner, IntPtr field, bool reference)
    {
        var name = _fieldMetadata[field].Name;
        return JudgeLists.Contains(owner) && _gameplayFieldParents[field] == ClassOf(_judgeListType)
            && (reference ? name == "_items" && _fieldMetadata[field].Type == _cursorArrayType && _fieldMetadata[field].Offset == 0x10
                : name is "_size" or "_version" && _fieldMetadata[field].Type == Int32
                    && _fieldMetadata[field].Offset == (name == "_size" ? 0x18 : 0x1c));
    }
    private void JudgeQueueSetFieldValue(IntPtr owner, IntPtr field, IntPtr input)
    {
        if (!JudgeQueueWritableField(owner, field, false)) { JudgeRejectValueWrite(owner, field, input); return; }
        ++ValueFieldWrites; var requested = Marshal.ReadInt32(input);
        var row = JudgeQueueBegin(_fieldMetadata[field].Name, Array.IndexOf(JudgeLists, owner), owner, null, requested);
        _gameplayStorage[(owner, field)] = BitConverter.GetBytes(requested); JudgeQueueEnd(row);
    }
    private void JudgeQueueSetFieldObject(IntPtr owner, IntPtr field, IntPtr input)
    {
        if (!JudgeQueueWritableField(owner, field, true)) { JudgeRejectObjectWrite(owner, field, input); return; }
        ++ObjectFieldWrites; JudgeQueueArgumentsRooted &= input == IntPtr.Zero || HasRootFor(input);
        if (!_referenceArrays.ContainsKey(input) || ObjectClass(input) != ClassOf(_cursorArrayType))
            throw new InvalidOperationException("Judge backing restoration received another reference class.");
        var row = JudgeQueueBegin("_items", Array.IndexOf(JudgeLists, owner), owner, null, input.ToInt64());
        _gameplayStorage[(owner, field)] = BitConverter.GetBytes(input.ToInt64()); JudgeQueueEnd(row);
    }
}
