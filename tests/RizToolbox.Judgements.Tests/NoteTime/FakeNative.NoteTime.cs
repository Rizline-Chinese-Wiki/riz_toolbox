using System.Runtime.InteropServices;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

// Official field exports backed by real unmanaged argument buffers. Only the
// captured actual note's head and raw type-2 Hold tail are writable; ordinary
// tail storage, all chart/type/source fields and every consumer remain outside
// this writer's authority.
sealed partial class FakeNative
{
    internal sealed record NoteTimeWrite(int Ordinal, IntPtr Owner, string Field, int BeforeBits, int Bits)
    { internal bool Stored; }
    internal enum NoteTimeMetadataFault { Static, InitOnly, Literal, WrongType, WrongWidth, WrongOffset, WrongParent }
    internal readonly List<NoteTimeWrite> NoteTimeWrites = new();
    internal readonly List<string> NoteTimeViolations = new();
    internal Action<NoteTimeWrite>? BeforeNoteTimeWrite, AfterNoteTimeWrite;
    internal int FailNoteTimeBefore, FailNoteTimeAfter;
    internal bool NoteTimeReceiversRooted = true;
    private readonly Dictionary<(IntPtr Owner, IntPtr Field), string> _noteTimeSelected = new();
    private readonly Dictionary<IntPtr, int> _noteTimeRawTypes = new();
    private Dictionary<(IntPtr Owner, IntPtr Field), byte[]> _noteTimeBaseline = new();
    internal readonly List<(IntPtr Owner, string Field)> NoteTimeSlots = new();

    internal void ConfigureNoteTime(NoteViewShape shape = NoteViewShape.Complete)
    {
        ConfigureTargetState(shape);
        // Deliberately stale selected caches ensure all forward/inverse fault
        // tests exercise an actual changed scalar, not coincidentally equal bits.
        for (var index = 0; index < NoteViewAllCount; ++index) TargetSetTiming(index, 55f + index, 66f + index);
        Add("il2cpp_field_get_flags", "Flags", nameof(NoteTimeFieldFlags));
        Add("il2cpp_field_set_value", "FieldSetValue", nameof(NoteTimeSetValue));
        Add("il2cpp_field_set_value_object", "FieldSetObject", nameof(NoteTimeRejectObject));
        GameplayApi = new NativeRuntime(ExportPointer); GameplayApi.BindCurrentThread(); NoteTimeRemember();
    }
    internal void NoteTimeRemember()
    {
        _noteTimeSelected.Clear(); _noteTimeRawTypes.Clear(); NoteTimeSlots.Clear();
        foreach (var note in _referenceArrays[CursorAllArray])
        {
            var type = TargetInt(SeekValue<IntPtr>(note, "noteData"), "type"); _noteTimeRawTypes.Add(note, type);
            AddSlot(note, "realTime"); if (type == 2) AddSlot(note, "holdEndRealTime");
        }
        _noteTimeBaseline = _gameplayStorage.ToDictionary(x => x.Key, x => x.Value.ToArray());
        void AddSlot(IntPtr owner, string name)
        { _noteTimeSelected.Add((owner, TargetField(owner, name)), name); NoteTimeSlots.Add((owner, name)); }
    }
    internal int NoteTimeBits(IntPtr owner, string name)
        => BitConverter.ToInt32(_gameplayStorage[(owner, TargetField(owner, name))]);
    internal int NoteTimeOriginalBits(IntPtr owner, string name)
        => BitConverter.ToInt32(_noteTimeBaseline[(owner, TargetField(owner, name))]);
    internal bool NoteTimeBaselineMatches() => _noteTimeBaseline.All(x =>
        _gameplayStorage.TryGetValue(x.Key, out var value) && value.SequenceEqual(x.Value));
    internal bool NoteTimeOtherMatches() => _noteTimeBaseline.Where(x => !_noteTimeSelected.ContainsKey(x.Key)).All(x =>
        _gameplayStorage.TryGetValue(x.Key, out var value) && value.SequenceEqual(x.Value));
    internal void NoteTimeChangeMetadata(string name, NoteTimeMetadataFault fault)
    {
        var field = TargetField(TargetNote(0), name); var old = _fieldMetadata[field];
        switch (fault)
        {
            case NoteTimeMetadataFault.Static: _flagMetadataFlags[field] = 0x10; break;
            case NoteTimeMetadataFault.InitOnly: _flagMetadataFlags[field] = 0x20; break;
            case NoteTimeMetadataFault.Literal: _flagMetadataFlags[field] = 0x40; break;
            case NoteTimeMetadataFault.WrongType: _fieldMetadata[field] = (old.Name, Int32, old.Offset); break;
            case NoteTimeMetadataFault.WrongWidth: _fieldMetadata[field] = (old.Name, AddType("System.Single", 12, true, 8), old.Offset); break;
            case NoteTimeMetadataFault.WrongOffset: _fieldMetadata[field] = (old.Name, old.Type, old.Offset + 4); break;
            case NoteTimeMetadataFault.WrongParent: _gameplayFieldParents[field] = ClassOf(_judgeType); break;
        }
    }
    private uint NoteTimeFieldFlags(IntPtr field)
        => _flagMetadataFlags.TryGetValue(field, out var flags) ? flags : JudgeFieldFlags(field);
    private void NoteTimeSetValue(IntPtr owner, IntPtr field, IntPtr input)
    {
        ++ValueFieldWrites;
        var allowed = _noteTimeSelected.TryGetValue((owner, field), out var name);
        var metadata = _fieldMetadata[field];
        if (!allowed || metadata.Type != Single || metadata.Offset != (name == "realTime" ? 0x34 : 0x38)
            || (name == "holdEndRealTime" && _noteTimeRawTypes[owner] != 2)
            || (_flagMetadataFlags.TryGetValue(field, out var flags) && (flags & 0x70) != 0))
        {
            NoteTimeViolations.Add("value owner=" + owner + " field=" + metadata.Name);
            throw new InvalidOperationException("Only captured actual Single realTime and type-2 Hold holdEndRealTime may be stored.");
        }
        NoteTimeReceiversRooted &= HasRootFor(owner);
        var bytes = new byte[4]; Marshal.Copy(input, bytes, 0, bytes.Length);
        var row = new NoteTimeWrite(NoteTimeWrites.Count + 1, owner, name!,
            BitConverter.ToInt32(_gameplayStorage[(owner, field)]), BitConverter.ToInt32(bytes));
        NoteTimeWrites.Add(row); BeforeNoteTimeWrite?.Invoke(row);
        if (row.Ordinal == FailNoteTimeBefore) throw new IOException("Synthetic note-time before-storage failure.");
        _gameplayStorage[(owner, field)] = bytes; row.Stored = true; AfterNoteTimeWrite?.Invoke(row);
        if (row.Ordinal == FailNoteTimeAfter) throw new IOException("Synthetic note-time after-storage failure.");
    }
    private void NoteTimeRejectObject(IntPtr owner, IntPtr field, IntPtr input)
    {
        ++ObjectFieldWrites; NoteTimeViolations.Add("reference owner=" + owner + " field=" + field);
        throw new InvalidOperationException("Note-time writes never change references, Transform, renderers or consumer lists.");
    }
}
