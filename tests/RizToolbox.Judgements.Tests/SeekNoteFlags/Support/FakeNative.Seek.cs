#pragma warning disable CS0649 // Reused synthetic fixture fault knobs intentionally retain default values.
using System.Runtime.InteropServices;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

// Synthetic metadata/export storage. The actual NativeRuntime and retained
// NativeJudgementBackend reader run against this storage; no image is loaded.
sealed partial class FakeNative
{
    private readonly Dictionary<IntPtr, object[]> _seekLists = new();
    private IntPtr _seekChartType, _seekLineType, _seekNoteType, _seekKeyType, _seekCoroutineType;
    private IntPtr _seekLineListType, _seekNoteListType, _seekSingleListType, _seekKeyListType, _seekGo;
    internal IntPtr SeekChart, SeekLines;
    internal readonly List<IntPtr> SeekNotes = new();
    internal int SeekConversions, SeekListItems;
    internal Action? DuringSeekConversion;
    internal Action<string, IntPtr>? AfterSeekInvocation;
    internal bool TruncateSeekNotes;
    internal ISeekChartNativeProfile SeekProfile = null!;
    internal int SeekProfileVerifications => ((SeekProfileValue)SeekProfile).Verifications;

    private sealed class SeekProfileValue : ISeekChartNativeProfile
    {
        public IntPtr ChartClass { get; }
        public IntPtr ProgressClass { get; }
        internal int Verifications;
        internal SeekProfileValue(IntPtr chart, IntPtr progress) => (ChartClass, ProgressClass) = (chart, progress);
        public void VerifyCurrent() => ++Verifications;
    }
    private IntPtr SeekType(string name, bool scalar = false)
        => _types.FirstOrDefault(p => p.Value.Name == name).Key is var found && found != IntPtr.Zero
            ? found : AddType(name, scalar ? 8 : 18, scalar, scalar ? 4 : 8);
    private IntPtr SeekObject(IntPtr type)
    {
        // The renderer fixture sends real LineInformation instances through
        // the exact native SetPositions seam, which stores at 0x88 and 0x90.
        // Give that existing type enough physical storage before any graph is
        // built. Other synthetic object allocations retain their old shape.
        var bytes = _types[type].Name == "InLevel.LineInformation" ? 0x98
            : _types[type].Name == "InLevel.LineControl" ? 0x88 : 16;
        var value = Box(type, new byte[bytes]); Marshal.WriteIntPtr(value, ClassOf(type)); return value;
    }
    private void SeekField(string key, IntPtr type, string name, IntPtr fieldType, IntPtr owner, object value, int offset)
    {
        var cls = ClassOf(type);
        var field = _structFields.TryGetValue(cls, out var fields)
            ? fields.FirstOrDefault(f => _fieldMetadata[f].Name == name) : IntPtr.Zero;
        if (field == IntPtr.Zero)
        {
            GameplayField(key, type, name, fieldType, owner, value);
            field = _gameplayFields[key].Field;
        }
        else
        {
            _gameplayFields[key] = (owner, field); SetGameplayField(key, value);
        }
        _fieldMetadata[field] = (name, fieldType, offset);
    }
    private IntPtr SeekList(IntPtr type, IntPtr elementType, params object[] values)
    {
        var value = SeekObject(type); _seekLists[value] = values;
        if (!_gameplayMethods.Values.Any(m => m.Class == ClassOf(type) && m.Name == "get_Count"))
        {
            GameplayMethodAdd(type, "get_Count", false, Int32);
            GameplayMethodAdd(type, "get_Item", false, elementType, Int32);
        }
        return value;
    }
    private IntPtr SeekNote(int type, float time, float end = 0)
    {
        var note = SeekObject(_seekNoteType); SeekNotes.Add(note); var key = "seek.note." + SeekNotes.Count;
        var extra = SeekList(_seekSingleListType, Single, type == 2 ? new object[] { end, 0f, 0f } : Array.Empty<object>());
        SeekField(key + ".type", _seekNoteType, "type", Int32, note, type, 0x10);
        SeekField(key + ".time", _seekNoteType, "time", Single, note, time, 0x14);
        SeekField(key + ".floor", _seekNoteType, "floorPosition", Single, note, 0f, 0x18);
        SeekField(key + ".extra", _seekNoteType, "otherInformations", _seekSingleListType, note, extra, 0x20);
        return note;
    }
    private IntPtr SeekLine(int ordinal, params IntPtr[] notes)
    {
        var line = SeekObject(_seekLineType);
        var list = SeekList(_seekNoteListType, _seekNoteType, notes.Cast<object>().ToArray());
        SeekField("seek.line." + ordinal, _seekLineType, "notes", _seekNoteListType, line, list, 0x18);
        return line;
    }
    internal void ConfigureSeekChart()
    {
        ConfigureGameplay();
        var controller = SeekType("NormalModeInLevelController"); var information = SeekType("InLevel.LevelInformation");
        var progress = SeekType("InLevel.ProgressControl"); var owner = SeekType("InLevel.NoteJudgeControl");
        var outer = SeekType("InLevel.GetJudgeControl"); var noteInfo = SeekType("InLevel.NoteInformation");
        _seekCoroutineType = SeekType("InLevel.<CheckHold>d__32");
        _seekChartType = SeekType("InLevel.Chart"); _seekLineType = SeekType("InLevel.Line");
        _seekNoteType = SeekType("InLevel.Note"); _seekKeyType = SeekType("InLevel.KeyPoint");
        _seekLineListType = SeekType("System.Collections.Generic.List<InLevel.Line>");
        _seekNoteListType = SeekType("System.Collections.Generic.List<InLevel.Note>");
        _seekKeyListType = SeekType("System.Collections.Generic.List<InLevel.KeyPoint>");
        _seekSingleListType = SeekType("System.Collections.Generic.List<System.Single>");
        foreach (var obj in new[] { GameplayController, GameplayInformation, GameplayProgress })
            Marshal.WriteIntPtr(obj, ObjectClass(obj));
        SeekChart = SeekObject(_seekChartType);
        SeekLines = SeekList(_seekLineListType, _seekLineType,
            SeekLine(0, SeekNote(0, 0), SeekNote(1, 2), SeekNote(2, 2, 6)),
            SeekLine(1, SeekNote(0, 4), SeekNote(0, 6)));
        SeekField("seek.chart.lines", _seekChartType, "lines", _seekLineListType, SeekChart, SeekLines, 0x40);
        SeekField("seek.chart.bpm", _seekChartType, "bPM", Single, SeekChart, 120f, 0x30);
        SeekField("seek.chart.shifts", _seekChartType, "bpmShifts", _seekKeyListType, SeekChart,
            SeekList(_seekKeyListType, _seekKeyType), 0x38);
        SeekField("seek.info.chart", information, "chart", _seekChartType, GameplayInformation, SeekChart, 0x88);
        SeekField("info.progress", information, "progressControl", progress, GameplayInformation, GameplayProgress, 0x28);
        SeekField("progress.info", progress, "levelInformation", information, GameplayProgress, GameplayInformation, 0x20);
        SeekField("seek.info.owner", information, "noteJudgeControl", owner, GameplayInformation, IntPtr.Zero, 0x58);
        SeekField("seek.info.outer", information, "getJudge", outer, GameplayInformation, IntPtr.Zero, 0x60);
        SeekField("seek.owner.info", owner, "levelInformation", information, IntPtr.Zero, IntPtr.Zero, 0x20);
        SeekField("seek.outer.info", outer, "levelInformation", information, IntPtr.Zero, IntPtr.Zero, 0x20);
        SeekField("seek.noteInfo.data", noteInfo, "noteData", _seekNoteType, IntPtr.Zero, IntPtr.Zero, 0x28);
        SeekField("seek.coroutine.owner", _seekCoroutineType, "<>4__this", owner, IntPtr.Zero, IntPtr.Zero, 0x20);
        SeekField("seek.coroutine.note", _seekCoroutineType, "holdNoteInformation", noteInfo, IntPtr.Zero, IntPtr.Zero, 0x28);
        foreach (var name in new[] { "_bad", "_miss", "_perfect", "_early", "_late", "_maxPerfect", "_challengeTimeMiss" })
            SeekField("seek.counter." + name, controller, name, Int32, GameplayController, 0, 0x100);
        // Metadata for the empty list is still required by the production reader.
        SeekField("seek.key.time", _seekKeyType, "time", Single, IntPtr.Zero, 0f, 0x10);
        SeekField("seek.key.value", _seekKeyType, "value", Single, IntPtr.Zero, 1f, 0x14);
        SeekField("seek.key.ease", _seekKeyType, "easeType", Int32, IntPtr.Zero, 0, 0x18);
        SeekField("seek.key.floor", _seekKeyType, "floorPosition", Single, IntPtr.Zero, 0f, 0x1c);
        var go = SeekType("UnityEngine.GameObject"); _seekGo = SeekObject(go);
        var scene = SeekType("UnityEngine.SceneManagement.Scene");
        GameplayMethodAdd(controller, "get_gameObject", false, go);
        GameplayMethodAdd(go, "get_scene", false, Int32);
        GameplayMethodAdd(scene, "IsValidInternal", true, Boolean, Int32);
        GameplayMethodAdd(scene, "GetIsLoadedInternal", true, Boolean, Int32);
        GameplayMethodAdd(progress, "ChartTimeToRealTime", false, Single, _gameplayDouble);
        Add("il2cpp_class_get_nested_types", "Ptr2", nameof(SeekNested));
        Add("il2cpp_runtime_invoke", "RuntimeInvoke", nameof(SeekInvoke));
        GameplayApi = new NativeRuntime(ExportPointer); GameplayApi.BindCurrentThread();
        SeekProfile = new SeekProfileValue(ClassOf(_seekChartType), ClassOf(progress));
    }
    private IntPtr SeekNested(IntPtr parent, IntPtr iterator)
    {
        if (GameplayTypeName(parent) != "InLevel.NoteJudgeControl" || Marshal.ReadIntPtr(iterator) != IntPtr.Zero) return IntPtr.Zero;
        Marshal.WriteIntPtr(iterator, new IntPtr(1)); return ClassOf(_seekCoroutineType);
    }
    private T SeekValue<T>(IntPtr owner, string name) where T : struct
    {
        var field = _structFields[ObjectClass(owner)].Single(f => _fieldMetadata[f].Name == name);
        var bytes = _gameplayStorage[(owner, field)];
        if (typeof(T) == typeof(float)) return (T)(object)BitConverter.ToSingle(bytes);
        return (T)(object)new IntPtr(BitConverter.ToInt64(bytes));
    }
    internal void SeekBpmSegments()
    {
        var points = new List<object>();
        foreach (var (time, value, floor) in new[] { (0f, 1f, 0f), (4f, 2f, 2f) })
        {
            var point = SeekObject(_seekKeyType); var key = "seek.key." + points.Count;
            SeekField(key + ".time", _seekKeyType, "time", Single, point, time, 0x10);
            SeekField(key + ".value", _seekKeyType, "value", Single, point, value, 0x14);
            SeekField(key + ".ease", _seekKeyType, "easeType", Int32, point, 0, 0x18);
            SeekField(key + ".floor", _seekKeyType, "floorPosition", Single, point, floor, 0x1c);
            points.Add(point);
        }
        SetGameplayField("seek.chart.shifts", SeekList(_seekKeyListType, _seekKeyType, points.ToArray()));
    }
    internal void SeekReplaceChart()
    {
        var replacement = SeekObject(_seekChartType);
        SeekField("seek.replacement.lines", _seekChartType, "lines", _seekLineListType, replacement, SeekLines, 0x40);
        SeekField("seek.replacement.bpm", _seekChartType, "bPM", Single, replacement, SeekValue<float>(SeekChart, "bPM"), 0x30);
        SeekField("seek.replacement.shifts", _seekChartType, "bpmShifts", _seekKeyListType, replacement,
            SeekValue<IntPtr>(SeekChart, "bpmShifts"), 0x38);
        SetGameplayField("seek.info.chart", replacement);
    }
    private float SeekConvert(double coordinate)
    {
        // Fixed method scalar oracle for synthetic storage only. Production calls
        // the existing ChartTimeToRealTime method through NativeRuntime.CallTyped.
        var chart = SeekValue<IntPtr>(GameplayInformation, "chart");
        var bpm = SeekValue<float>(chart, "bPM"); var shifts = _seekLists[SeekValue<IntPtr>(chart, "bpmShifts")];
        if (shifts.Length == 0) return (float)(coordinate * 60d / bpm);
        var index = 0;
        while (index < shifts.Length && SeekValue<float>((IntPtr)shifts[index], "time") <= coordinate) ++index;
        var point = (IntPtr)shifts[Math.Max(0, index - 1)];
        return (float)((coordinate - SeekValue<float>(point, "time")) / SeekValue<float>(point, "value")
            / bpm * 60d + SeekValue<float>(point, "floorPosition"));
    }
    private IntPtr SeekInvoke(IntPtr method, IntPtr instance, IntPtr arguments, IntPtr exception)
    {
        if (IsArrayMethod(method)) return GameplayInvoke(method, instance, arguments, exception);
        var row = _gameplayMethods[method];
        Marshal.WriteIntPtr(exception, IntPtr.Zero);
        AllGameplayReceiversRooted &= instance == IntPtr.Zero || HasRootFor(instance);
        IntPtr result;
        switch (row.Name)
        {
            case "get_gameObject": result = _seekGo; break;
            case "get_scene": result = Box(Int32, BitConverter.GetBytes(1)); break;
            case "IsValidInternal": case "GetIsLoadedInternal": result = Box(Boolean, new byte[] { 1 }); break;
            case "get_Count": result = Box(Int32, BitConverter.GetBytes(_seekLists[instance].Length)); break;
            case "get_Item":
                ++SeekListItems;
                var ordinal = Marshal.ReadInt32(Marshal.ReadIntPtr(arguments));
                if (TruncateSeekNotes && ObjectClass(instance) == ClassOf(_seekNoteListType) && ordinal == 1)
                { Marshal.WriteIntPtr(exception, new IntPtr(99)); return IntPtr.Zero; }
                result = _seekLists[instance][ordinal] is float single ? Box(Single, BitConverter.GetBytes(single))
                    : (IntPtr)_seekLists[instance][ordinal];
                break;
            case "ChartTimeToRealTime":
                ++SeekConversions;
                result = Box(Single, BitConverter.GetBytes(SeekConvert(Marshal.PtrToStructure<double>(Marshal.ReadIntPtr(arguments)))));
                var action = DuringSeekConversion; DuringSeekConversion = null; action?.Invoke(); break;
            default: return GameplayInvoke(method, instance, arguments, exception);
        }
        ++Invocations; AfterSeekInvocation?.Invoke(row.Name, instance); return result;
    }
}
