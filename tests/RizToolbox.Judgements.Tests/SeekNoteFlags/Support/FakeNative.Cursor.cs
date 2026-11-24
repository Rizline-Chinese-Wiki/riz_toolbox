#pragma warning disable CS0649 // Reused synthetic fixture fault knobs intentionally retain default values.
using System.Runtime.InteropServices;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

sealed partial class FakeNative
{
    private IntPtr _cursorType, _cursorArrayType;
    internal IntPtr CursorControl, CursorRenderArray, CursorAllArray;
    internal readonly List<(string Field, int Value)> CursorWriteLog = new();
    internal Action<string>? BeforeCursorWrite, AfterCursorWrite;
    internal Action<string, IntPtr>? AfterCursorRead;
    internal bool CursorOwnersRooted = true;
    internal int ForbiddenCursorCalls;
    internal uint CursorRootHandle => _handles.First(x => x.Value == CursorControl).Key;

    internal void ConfigureCursor(bool empty = false, bool alias = false)
    {
        ConfigureSeekChart();
        _cursorType = SeekType("InLevel.NoteControl");
        _cursorArrayType = AddType("InLevel.NoteInformation[]", 29, false, 8);
        CursorControl = SeekObject(_cursorType);
        var information = SeekType("InLevel.LevelInformation"); var controller = SeekType("NormalModeInLevelController");
        var progress = SeekType("InLevel.ProgressControl"); var pause = SeekType("PauseUIControl");
        var noteType = SeekType("InLevel.NoteInformation");
        var noteObjects = new List<IntPtr>();
        foreach (var data in SeekNotes)
        {
            var note = SeekObject(noteType); noteObjects.Add(note);
            SeekField("cursor.note." + noteObjects.Count, noteType, "noteData", _seekNoteType, note, data, 0x28);
        }
        var all = empty ? Array.Empty<IntPtr>() : noteObjects.ToArray();
        var render = empty ? Array.Empty<IntPtr>() : new[] { noteObjects[0], noteObjects[2], noteObjects[4] };
        CursorRenderArray = CursorArray(render); CursorAllArray = alias ? CursorRenderArray : CursorArray(all);
        SeekField("cursor.info.control", information, "noteControl", _cursorType, GameplayInformation, CursorControl, 0x40);
        SeekField("cursor.control.info", _cursorType, "levelInformation", information, CursorControl, GameplayInformation, 0x20);
        SeekField("cursor.render", _cursorType, "notes", _cursorArrayType, CursorControl, CursorRenderArray, 0x28);
        SeekField("cursor.all", _cursorType, "allNotes", _cursorArrayType, CursorControl, CursorAllArray, 0x30);
        SeekField("cursor.has", _cursorType, "_hasNote", Boolean, CursorControl, !empty, 0x44);
        SeekField("cursor.start", _cursorType, "_startIndex", Int32, CursorControl, empty ? 0 : 2, 0x48);
        SeekField("cursor.activeHas", _cursorType, "_activeHasNote", Boolean, CursorControl, false, 0x4c);
        SeekField("cursor.activeStart", _cursorType, "_activeStartIndex", Int32, CursorControl, empty ? 0 : alias ? 3 : 4, 0x50);
        SeekField("controller.pause", controller, "pauseUIControl", pause, GameplayController, GameplayPause, 0xb0);
        foreach (var (key, name, offset) in new[] { ("info.loaded", "loadOver", 0xbe), ("info.over", "levelOver", 0xc0), ("info.crashed", "crashed", 0xc1) })
            SeekField(key, information, name, Boolean, GameplayInformation, key == "info.loaded", offset);
        foreach (var (key, name, offset) in new[] { ("progress.ready", "timeReady", 0x65), ("progress.playing", "isPlaying", 0x42), ("progress.preparing", "prepareToPlay", 0x40) })
            SeekField(key, progress, name, Boolean, GameplayProgress, key == "progress.ready", offset);
        foreach (var (key, name, offset) in new[] { ("pause.paused", "_pausing", 0xb4), ("pause.over", "_over", 0xc0), ("pause.terminated", "_terminated", 0xc1) })
            SeekField(key, pause, name, Boolean, GameplayPause, key == "pause.paused", offset);
        SeekField("pause.time", pause, "_pauseTime", Single, GameplayPause, 0f, 0xbc);
        foreach (var obj in new[] { GameplayPause, CursorControl }) Marshal.WriteIntPtr(obj, ObjectClass(obj));
        Add("il2cpp_class_get_parent", "Ptr1", nameof(CursorClassParent));
        Add("il2cpp_class_is_assignable_from", "Assignable", nameof(CursorAssignable));
        Add("il2cpp_class_instance_size", "ArraySize", nameof(CursorInstanceSize));
        Add("il2cpp_field_get_value", "FieldGet", nameof(CursorRead));
        Add("il2cpp_field_set_value", "FieldSetValue", nameof(CursorWrite));
        GameplayApi = new NativeRuntime(ExportPointer); GameplayApi.BindCurrentThread();
    }
    private IntPtr CursorArray(IntPtr[] notes)
    {
        var value = ReferenceArray(notes.Length); _referenceArrays[value] = notes.ToArray();
        _boxedTypes[value] = _cursorArrayType; Marshal.WriteIntPtr(value, ClassOf(_cursorArrayType)); return value;
    }
    private IntPtr CursorClassParent(IntPtr cls) => cls == ClassOf(_cursorArrayType) ? ClassOf(_referenceArrayType) : ClassParent(cls);
    private bool CursorAssignable(IntPtr target, IntPtr source)
        => target == ClassOf(_referenceArrayType) && source == ClassOf(_cursorArrayType) || GameplayAssignable(target, source);
    private uint CursorInstanceSize(IntPtr cls) => 0x400;
    private void CursorRead(IntPtr owner, IntPtr field, IntPtr output)
    { GameplayReadField(owner, field, output); AfterCursorRead?.Invoke(_fieldMetadata[field].Name, owner); }
    private void CursorWrite(IntPtr owner, IntPtr field, IntPtr input)
    {
        var name = _fieldMetadata[field].Name;
        CursorOwnersRooted &= HasRootFor(owner) && owner == CursorControl;
        BeforeCursorWrite?.Invoke(name);
        var size = _types[_fieldMetadata[field].Type].Size;
        var bytes = new byte[size]; Marshal.Copy(input, bytes, 0, size);
        _gameplayStorage[(owner, field)] = bytes; ++ValueFieldWrites;
        CursorWriteLog.Add((name, size == 1 ? bytes[0] : BitConverter.ToInt32(bytes)));
        AfterCursorWrite?.Invoke(name);
    }
    internal NativeNoteCursorState CursorState()
    {
        byte[] Value(string key) { var row = _gameplayFields[key]; return _gameplayStorage[(row.Owner, row.Field)]; }
        return new(Value("cursor.has")[0] != 0, BitConverter.ToInt32(Value("cursor.start")),
            Value("cursor.activeHas")[0] != 0, BitConverter.ToInt32(Value("cursor.activeStart")));
    }
    internal void CursorReplaceArray(bool all)
    {
        var old = all ? CursorAllArray : CursorRenderArray; var replacement = CursorArray(_referenceArrays[old]);
        SetGameplayField(all ? "cursor.all" : "cursor.render", replacement);
    }
    internal void CursorReplaceSlot()
    { _referenceArrays[CursorAllArray][0] = SeekObject(SeekType("InLevel.NoteInformation")); }
    internal void CursorChangeLayout(string name, int offset)
    {
        var field = _structFields[ClassOf(_cursorType)].Single(f => _fieldMetadata[f].Name == name);
        var prior = _fieldMetadata[field]; _fieldMetadata[field] = (prior.Name, prior.Type, offset);
    }
}
