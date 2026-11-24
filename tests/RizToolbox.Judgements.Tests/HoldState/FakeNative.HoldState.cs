using System.Runtime.InteropServices;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

// One complete source construction; the earlier chart/NoteView/Judge metadata
// and dispatch surfaces remain intact. Only the new Hold state is writable.
sealed partial class FakeNative
{
    internal enum HoldStateAlias { None, List, Backing, EmptyBacking }
    internal sealed record HoldStateListStorage(IntPtr Reference, IntPtr Backing, int Size, int Version, IReadOnlyList<IntPtr> Capacity);
    internal sealed record HoldStateNoteStorage(IntPtr Reference, bool IsStatic, int LengthBits);
    internal sealed record HoldStateStorage(IReadOnlyList<HoldStateListStorage> Lists, IReadOnlyList<HoldStateNoteStorage> Notes,
        int NowBits, int SpeedBits, int LengthBits);
    internal sealed record HoldStateWrite(int Ordinal, string Operation, IntPtr Receiver, int? List, int? Slot, long Requested)
    { internal bool Stored; }
    internal sealed record HoldStateGrowth(IntPtr Before, IntPtr After, IReadOnlyList<IntPtr> OldCapacity, int NewCapacity);
    internal readonly List<IntPtr> HoldStateNotes = new();
    internal readonly List<HoldStateWrite> HoldStateWrites = new();
    internal readonly List<HoldStateGrowth> HoldStateGrowths = new();
    internal Action<HoldStateWrite>? BeforeHoldStateStore, AfterHoldStateStore;
    internal Action<uint, IntPtr>? AfterHoldStateRoot;
    internal int FailHoldStateBefore, FailHoldStateAfter;
    internal bool HoldStateReceiversRooted = true, HoldStateArgumentsRooted = true;
    private readonly Dictionary<IntPtr, int> _holdStateArrays = new();
    private readonly Dictionary<(IntPtr Owner, IntPtr Field), byte[]> _holdStateOther = new();
    internal HoldStateProfileValue HoldStateProfile = null!;
    internal sealed class HoldStateProfileValue : ISeekHoldNativeProfile
    {
        private readonly FakeNative _native; internal Action? OnVerify;
        internal HoldStateProfileValue(FakeNative native) => _native = native;
        public IntPtr ChartClass => _native.TargetProfile.ChartClass;
        public IntPtr ProgressClass => _native.TargetProfile.ProgressClass;
        public IntPtr InformationClass => _native.TargetProfile.InformationClass;
        public IntPtr NoteClass => _native.TargetProfile.NoteClass;
        public IntPtr JudgeClass => _native.TargetProfile.JudgeClass;
        public IntPtr HoldClass => _native.TargetProfile.HoldClass;
        public IntPtr ChallengeTimeClass => _native.TargetProfile.ChallengeTimeClass;
        public IntPtr NoteListClass => _native.ClassOf(_native._judgeListType);
        public IntPtr ClearMethod => _native._judgeQueueClear;
        public IntPtr AddMethod => _native._judgeQueueAdd;
        public void VerifyCurrent() { OnVerify?.Invoke(); _native.TargetProfile.VerifyCurrent(); }
    }
    internal void ConfigureHoldState(NoteViewShape shape = NoteViewShape.Complete, HoldStateAlias alias = HoldStateAlias.None)
    {
        ConfigureTargetState(shape);
        // Introduce a second chart Hold; MultipleSegments then has a third
        // distinct actual Hold sharing this same chart data reference.
        TargetSetChart(0, 2, 0, 8); TargetSetTiming(0, 0, 4);
        if (shape == NoteViewShape.MultipleSegments) TargetSetTiming(5, 0, 4);
        HoldStateNotes.AddRange(_referenceArrays[CursorAllArray].Where(note => TargetInt(SeekValue<IntPtr>(note, "noteData"), "type") == 2));
        var a = HoldStateNotes[0]; var b = HoldStateNotes[1];
        HoldStateSetListStorage(0, 3, int.MinValue, b, a, b, IntPtr.Zero, JudgeForeignTail, a);
        HoldStateSetListStorage(1, 1, int.MaxValue, a, b, IntPtr.Zero, JudgeForeignTail, b);
        for (var ordinal = 0; ordinal < HoldStateNotes.Count; ++ordinal)
        {
            TargetSet(HoldStateNotes[ordinal], "isStaticNote", ordinal % 2 == 0);
            TargetSet(HoldStateNotes[ordinal], "lastFrameHoldLineLength", ordinal == 0 ? BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)) : -7.25f - ordinal);
        }
        if (alias == HoldStateAlias.List)
        {
            TargetHoldLists[1] = TargetHoldLists[0]; TargetSet(TargetHold, "runningHoldNotes", TargetHoldLists[0]);
        }
        else if (alias == HoldStateAlias.Backing)
            TargetSet(TargetHoldLists[1], "_items", SeekValue<IntPtr>(TargetHoldLists[0], "_items"));
        else if (alias == HoldStateAlias.EmptyBacking)
        {
            HoldStateSetListStorage(0, 0, int.MinValue); TargetSet(TargetHoldLists[1], "_items", SeekValue<IntPtr>(TargetHoldLists[0], "_items"));
            TargetSet(TargetHoldLists[1], "_size", 0);
        }
        foreach (var list in TargetHoldLists.Distinct()) _holdStateArrays.TryAdd(SeekValue<IntPtr>(list, "_items"), Array.IndexOf(TargetHoldLists, list));
        Add("il2cpp_runtime_invoke", "RuntimeInvoke", nameof(HoldStateInvoke));
        Add("il2cpp_field_set_value", "FieldSetValue", nameof(HoldStateSetValue));
        Add("il2cpp_field_set_value_object", "FieldSetObject", nameof(HoldStateSetObject));
        Add("il2cpp_gchandle_new", "NewHandle", nameof(HoldStateRoot));
        GameplayApi = new NativeRuntime(ExportPointer); GameplayApi.BindCurrentThread(); HoldStateProfile = new(this);
        HoldStateRememberOther();
    }
    internal HoldStateListStorage HoldStateList(int slot)
    {
        var list = TargetHoldLists[slot]; var backing = SeekValue<IntPtr>(list, "_items");
        return new(list, backing, TargetInt(list, "_size"), TargetInt(list, "_version"), Array.AsReadOnly(_referenceArrays[backing].ToArray()));
    }
    internal HoldStateStorage HoldStateSnapshot() => new(Enumerable.Range(0, 2).Select(HoldStateList).ToArray(),
        HoldStateNotes.Select(note => new HoldStateNoteStorage(note, TargetBoolean(note, "isStaticNote"),
            BitConverter.SingleToInt32Bits(TargetFloat(note, "lastFrameHoldLineLength")))).ToArray(),
        BitConverter.SingleToInt32Bits(TargetFloat(TargetHold, "_nowTime")), BitConverter.SingleToInt32Bits(TargetFloat(TargetHold, "_speed")),
        BitConverter.SingleToInt32Bits(TargetFloat(TargetHold, "_holdLineLength")));
    internal bool HoldStateEquals(HoldStateStorage expected)
    {
        var value = HoldStateSnapshot();
        return value.NowBits == expected.NowBits && value.SpeedBits == expected.SpeedBits && value.LengthBits == expected.LengthBits
            && value.Notes.SequenceEqual(expected.Notes) && value.Lists.Count == expected.Lists.Count
            && value.Lists.Where((list, slot) => list.Reference != expected.Lists[slot].Reference || list.Backing != expected.Lists[slot].Backing
                || list.Size != expected.Lists[slot].Size || list.Version != expected.Lists[slot].Version
                || !list.Capacity.SequenceEqual(expected.Lists[slot].Capacity)).Any() == false;
    }
    internal void HoldStateSetListStorage(int slot, int size, int version, params IntPtr[] capacity)
    {
        var list = TargetHoldLists[slot]; var backing = CursorArray(capacity);
        TargetSet(list, "_items", backing); TargetSet(list, "_size", size); TargetSet(list, "_version", version); _holdStateArrays.TryAdd(backing, slot);
    }
    internal void HoldStateSetSlot(IntPtr array, int slot, IntPtr value) => _referenceArrays[array][slot] = value;
    internal IntPtr[] HoldStateCapacity(IntPtr array) => _referenceArrays[array].ToArray();
    internal IntPtr HoldStateData(IntPtr note) => SeekValue<IntPtr>(note, "noteData");
    internal void HoldStateCapacity(int slot, int capacity)
    {
        var list = HoldStateList(slot); var values = new IntPtr[capacity]; Array.Copy(list.Capacity.ToArray(), values, Math.Min(values.Length, list.Capacity.Count));
        HoldStateSetListStorage(slot, Math.Min(list.Size, capacity), list.Version, values);
    }
    internal void HoldStateRememberOther()
    {
        _holdStateOther.Clear();
        foreach (var row in _gameplayStorage)
        {
            var name = _fieldMetadata[row.Key.Field].Name;
            if (TargetHoldLists.Contains(row.Key.Owner) && name is "_items" or "_size" or "_version"
                || HoldStateNotes.Contains(row.Key.Owner) && name is "isStaticNote" or "lastFrameHoldLineLength"
                || row.Key.Owner == TargetHold && name is "_nowTime" or "_speed" or "_holdLineLength") continue;
            _holdStateOther.Add(row.Key, row.Value.ToArray());
        }
    }
    internal bool HoldStateOtherUnchanged() => _holdStateOther.All(row => _gameplayStorage.TryGetValue(row.Key, out var value) && value.SequenceEqual(row.Value));
    private uint HoldStateRoot(IntPtr pointer, bool pinned)
    { var handle = JudgeRoot(pointer, pinned); AfterHoldStateRoot?.Invoke(handle, pointer); return handle; }
    private HoldStateWrite HoldStateBegin(string operation, IntPtr receiver, int? list, int? slot, long requested)
    {
        HoldStateReceiversRooted &= HasRootFor(receiver);
        var row = new HoldStateWrite(HoldStateWrites.Count + 1, operation, receiver, list, slot, requested);
        HoldStateWrites.Add(row); BeforeHoldStateStore?.Invoke(row);
        if (row.Ordinal == FailHoldStateBefore) throw new IOException("Synthetic Hold native call failed before storage."); return row;
    }
    private void HoldStateEnd(HoldStateWrite row)
    {
        row.Stored = true; AfterHoldStateStore?.Invoke(row);
        if (row.Ordinal == FailHoldStateAfter) throw new IOException("Synthetic Hold native call failed after storage.");
    }
}
