using System.Runtime.InteropServices;

namespace RizToolbox.Judgements.Tests.NoteFlags;

sealed partial class FakeNative
{
    internal bool FingerWrongBoxClass, FingerWrongBoxValue;
    private uint FingerRoot(IntPtr target, bool pinned)
    {
        var handle = JudgeRoot(target, pinned); AfterFingerRoot?.Invoke(handle, target); return handle;
    }
    private IntPtr FingerObjectNew(IntPtr cls)
    {
        if (cls != ClassOf(_fingerListType)) throw new InvalidOperationException("fingerIds constructed a non-Int32-list object.");
        ++ObjectAllocations; OnObjectAllocation?.Invoke();
        if (FailFingerAllocation) return IntPtr.Zero;
        var value = SeekObject(_fingerListType); _fingerLists.Add(value); FingerCreatedLists.Add(value);
        LastObject = value; AfterFingerAllocation?.Invoke(value); return value;
    }
    private IntPtr FingerValueBox(IntPtr cls, IntPtr value)
    {
        if (cls != ClassOf(Int32) || value == IntPtr.Zero) throw new InvalidOperationException("fingerIds boxed a non-Int32 argument.");
        ++FingerBoxCalls; return Box(FingerWrongBoxClass ? Single : Int32,
            BitConverter.GetBytes(FingerWrongBoxValue ? unchecked(Marshal.ReadInt32(value) + 1) : Marshal.ReadInt32(value)));
    }
    private FingerWrite FingerBegin(string operation, IntPtr receiver, int? slot, long requested)
    {
        FingerReceiversRooted &= HasRootFor(receiver);
        var row = new FingerWrite(FingerWrites.Count + 1, operation, receiver, FingerNotes.IndexOf(receiver), slot, requested);
        FingerWrites.Add(row); BeforeFingerStore?.Invoke(row);
        if (row.Ordinal == FailFingerBeforeStore) throw new IOException("Synthetic fingerIds native failure before storage.");
        return row;
    }
    private void FingerEnd(FingerWrite row)
    {
        row.Stored = true; AfterFingerStore?.Invoke(row);
        if (row.Ordinal == FailFingerAfterStore) throw new IOException("Synthetic fingerIds native failure after storage.");
    }
    private IntPtr FingerInvoke(IntPtr method, IntPtr receiver, IntPtr arguments, IntPtr exception)
    {
        if (!_gameplayMethods.TryGetValue(method, out var metadata)
            || metadata.Class != ClassOf(_fingerListType) && metadata.Class != ClassOf(_fingerArrayType) && metadata.Class != ClassOf(_fingerSystemArrayType))
            return JudgeQueueInvoke(method, receiver, arguments, exception);
        ++Invocations; Marshal.WriteIntPtr(exception, IntPtr.Zero);
        FingerReceiversRooted &= HasRootFor(receiver);
        if (method == _fingerRank) return Box(Int32, BitConverter.GetBytes(1));
        if (method == _fingerLength) return Box(Int32, BitConverter.GetBytes(_fingerArrays[receiver].Length));
        if (method == _fingerGet)
        {
            ++FingerGetValues; var index = Marshal.ReadInt32(Marshal.ReadIntPtr(arguments));
            return Box(Int32, BitConverter.GetBytes(_fingerArrays[receiver][index]));
        }
        if (method == _fingerSet)
        {
            var boxed = Marshal.ReadIntPtr(arguments); var index = Marshal.ReadInt32(Marshal.ReadIntPtr(arguments, IntPtr.Size));
            if (!_boxedTypes.TryGetValue(boxed, out var type) || type != Int32 || (uint)index >= (uint)_fingerArrays[receiver].Length)
                throw new InvalidOperationException("Official Int32 array SetValue received another boxed type or out-of-capacity index.");
            FingerArgumentsRooted &= HasRootFor(boxed);
            var value = Marshal.ReadInt32(boxed); var row = FingerBegin("SetValue", receiver, index, value);
            FingerSetSlot(receiver, index, value); FingerEnd(row); return IntPtr.Zero;
        }
        if (method == _fingerConstructor)
        {
            var row = FingerBegin("Ctor", receiver, null, 0);
            FingerInitializeList(receiver, _fingerEmptyArray, 0, 0); FingerEnd(row); return IntPtr.Zero;
        }
        if (method != _fingerClear && method != _fingerAdd || !_fingerLists.Contains(receiver))
            throw new InvalidOperationException("Unknown Int32 List native dispatch.");
        var size = FingerScalar(receiver, "_size"); var backing = FingerBacking(receiver); var slots = _fingerArrays[backing];
        if (size < 0 || size > slots.Length) throw new InvalidOperationException("Invalid complete Int32 List storage.");
        var requested = method == _fingerAdd ? Marshal.ReadInt32(Marshal.ReadIntPtr(arguments)) : 0;
        var write = FingerBegin(method == _fingerClear ? "Clear" : "Add", receiver, null, requested);
        if (method == _fingerClear)
        {
            if (FingerClearErasesSlots) for (var index = 0; index < size; ++index) FingerSetSlot(backing, index, 0);
            FingerSetList(receiver, "_size", 0);
        }
        else
        {
            if (size == slots.Length)
            {
                var capacity = slots.Length == 0 ? 4 : checked(slots.Length * 2);
                var values = new int[capacity]; Array.Copy(slots, values, size); var grown = FingerArray(values);
                FingerGrowths.Add(new(backing, grown, slots.Length, capacity, Array.AsReadOnly(slots.ToArray())));
                FingerSetList(receiver, "_items", grown); backing = grown;
            }
            FingerSetSlot(backing, size, requested); FingerSetList(receiver, "_size", size + 1);
        }
        FingerSetList(receiver, "_version", unchecked(FingerScalar(receiver, "_version") + 1));
        FingerEnd(write); return IntPtr.Zero;
    }
    private void FingerSetFieldValue(IntPtr receiver, IntPtr field, IntPtr input)
    {
        var name = _fieldMetadata[field].Name;
        if (!_fingerLists.Contains(receiver) || name is not ("_size" or "_version") || _fieldMetadata[field].Type != Int32
            || _gameplayFieldParents[field] != ClassOf(_fingerListType))
            throw new InvalidOperationException("fingerIds entered an unrelated scalar setter.");
        ++ValueFieldWrites; var value = Marshal.ReadInt32(input); var row = FingerBegin(name, receiver, null, value);
        _gameplayStorage[(receiver, field)] = BitConverter.GetBytes(value); FingerEnd(row);
    }
    private void FingerSetFieldObject(IntPtr receiver, IntPtr field, IntPtr value)
    {
        var finger = field == _fingerField && FingerNotes.Contains(receiver);
        var backing = _fingerLists.Contains(receiver) && _fieldMetadata[field].Name == "_items"
            && _gameplayFieldParents[field] == ClassOf(_fingerListType) && _fieldMetadata[field].Type == _fingerArrayType;
        if (!finger && !backing) throw new InvalidOperationException("fingerIds entered an unrelated reference setter.");
        if (value != IntPtr.Zero && (finger ? !_fingerLists.Contains(value) : !_fingerArrays.ContainsKey(value)))
            throw new InvalidOperationException("fingerIds setter received another exact reference class.");
        ++ObjectFieldWrites; FingerArgumentsRooted &= value == IntPtr.Zero || HasRootFor(value);
        var row = FingerBegin(finger ? "fingerIds" : "_items", receiver, null, value.ToInt64());
        _gameplayStorage[(receiver, field)] = BitConverter.GetBytes(value.ToInt64()); FingerEnd(row);
    }
}
