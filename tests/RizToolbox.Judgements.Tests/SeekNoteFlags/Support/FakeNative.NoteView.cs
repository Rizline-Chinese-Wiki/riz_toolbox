#pragma warning disable CS0649 // Reused synthetic fixture fault knobs intentionally retain default values.
using System.Runtime.InteropServices;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

// Extends the seven byte-preserved support files with actual declared-field,
// collection and array storage for the production RunValue note-view entry.
sealed partial class FakeNative
{
    internal enum NoteViewShape { Complete, Partial, MultipleSegments }
    internal sealed record NoteViewCall(string Type, string Name, IntPtr Receiver, int? Index);
    internal sealed record NoteViewFreeRecord(uint Handle, IntPtr Target, bool InjectedFailure);
    internal sealed record NoteViewRootRecord(uint Handle, IntPtr Target, bool Pinned);
    private IntPtr _noteViewPointType, _noteViewPointListType, _noteViewLineType, _noteViewGroupType;
    internal readonly List<IntPtr> NoteViewChartLines = new(), NoteViewRuntimeNotes = new();
    internal readonly List<IntPtr[]> NoteViewPoints = new();
    internal readonly List<IntPtr> NoteViewGroups = new();
    internal readonly List<(string Name, IntPtr Owner)> NoteViewFieldLog = new();
    internal readonly List<NoteViewCall> NoteViewDispatchLog = new();
    internal readonly List<NoteViewFreeRecord> NoteViewFreeLog = new();
    internal readonly List<NoteViewRootRecord> NoteViewRootLog = new();
    internal readonly List<string> NoteViewDispatchViolations = new();
    internal int NoteViewMethodResolutions, NoteViewObjectClassCalls, NoteViewInvocations, NoteViewFieldMetadataEnumerations;
    internal Action<string, IntPtr>? AfterNoteViewFieldRead;
    internal Action<IntPtr>? AfterNoteViewObjectClass;
    internal Action<NoteViewCall>? AfterNoteViewInvocation;
    private readonly Dictionary<IntPtr, bool> _noteViewStaticLines = new();

    internal void ConfigureNoteView(NoteViewShape shape)
    {
        ConfigureCursor();
        _noteViewPointType = SeekType("InLevel.LinePoint");
        _noteViewPointListType = SeekType("System.Collections.Generic.List<InLevel.LinePoint>");
        _noteViewLineType = SeekType("InLevel.LineInformation");
        _noteViewGroupType = SeekType("InLevel.LinesGroupInformation");
        NoteViewChartLines.AddRange(_seekLists[SeekLines].Cast<IntPtr>());
        for (var line = 0; line < NoteViewChartLines.Count; ++line)
        {
            var points = new IntPtr[3];
            var times = line == 0 ? new[] { 0f, 3f, 10f } : new[] { 0f, 5f, 10f };
            for (var ordinal = 0; ordinal < points.Length; ++ordinal)
            {
                points[ordinal] = SeekObject(_noteViewPointType);
                SeekField("noteview.point." + line + "." + ordinal, _noteViewPointType, "time", Single,
                    points[ordinal], times[ordinal], 0x10);
            }
            NoteViewPoints.Add(points);
            NoteViewGroups.Add(SeekObject(_noteViewGroupType));
            var list = SeekList(_noteViewPointListType, _noteViewPointType, points.Cast<object>().ToArray());
            SeekField("noteview.line.points." + line, _seekLineType, "linePoints", _noteViewPointListType,
                NoteViewChartLines[line], list, 0x10);
        }
        NoteViewRuntimeNotes.AddRange(_referenceArrays[CursorAllArray]);
        var dynamicFirst = NoteViewRuntimeLine(0, 0, false);
        var staticFirst = NoteViewRuntimeLine(0, 0, true);
        var staticSecond = NoteViewRuntimeLine(1, 0, true);
        var dynamicSecond = NoteViewRuntimeLine(1, 1, false);
        var runtimeLines = new[] { dynamicFirst, staticFirst, dynamicFirst, staticSecond, dynamicSecond };
        for (var slot = 0; slot < runtimeLines.Length; ++slot)
            SeekField("noteview.note.line." + slot, SeekType("InLevel.NoteInformation"), "lineInformation", _noteViewLineType,
                NoteViewRuntimeNotes[slot], runtimeLines[slot], 0x20);
        if (shape == NoteViewShape.MultipleSegments)
        {
            // A second actual runtime instance shares one chart Note reference.
            // The view joins references independently of creation-time intervals.
            var note = SeekObject(SeekType("InLevel.NoteInformation"));
            SeekField("noteview.extra.data", SeekType("InLevel.NoteInformation"), "noteData", _seekNoteType,
                note, SeekNotes[0], 0x28);
            SeekField("noteview.extra.line", SeekType("InLevel.NoteInformation"), "lineInformation", _noteViewLineType,
                note, NoteViewRuntimeLine(0, 1, false), 0x20);
            NoteViewRuntimeNotes.Add(note);
        }
        var all = NoteViewRuntimeNotes.Where((_, index) => shape != NoteViewShape.Partial || index != 4).ToArray();
        var render = all.Where(note => !_noteViewStaticLines[SeekValue<IntPtr>(note, "lineInformation")]).ToArray();
        CursorAllArray = CursorArray(all);
        CursorRenderArray = CursorArray(render);
        SetGameplayField("cursor.all", CursorAllArray);
        SetGameplayField("cursor.render", CursorRenderArray);

        Add("il2cpp_class_get_methods", "Ptr2", nameof(NoteViewMethods));
        Add("il2cpp_class_get_method_from_name", "MethodLookup", nameof(NoteViewNamedMethod));
        Add("il2cpp_runtime_invoke", "RuntimeInvoke", nameof(NoteViewInvoke));
        Add("il2cpp_field_get_value", "FieldGet", nameof(NoteViewReadField));
        Add("il2cpp_object_get_class", "Ptr1", nameof(NoteViewObjectClass));
        Add("il2cpp_class_get_fields", "Ptr2", nameof(NoteViewFields));
        Add("il2cpp_gchandle_new", "NewHandle", nameof(NoteViewRoot));
        Add("il2cpp_gchandle_free", "FreeHandle", nameof(NoteViewFree));
        GameplayApi = new NativeRuntime(ExportPointer);
        GameplayApi.BindCurrentThread();
    }

    private IntPtr NoteViewRuntimeLine(int chartLine, int segment, bool isStatic)
    {
        var line = SeekObject(_noteViewLineType);
        var key = "noteview.segment." + _noteViewStaticLines.Count;
        _noteViewStaticLines.Add(line, isStatic);
        SeekField(key + ".start", _noteViewLineType, "startLinePoint", _noteViewPointType,
            line, NoteViewPoints[chartLine][segment], 0x30);
        SeekField(key + ".end", _noteViewLineType, "endLinePoint", _noteViewPointType,
            line, NoteViewPoints[chartLine][segment + 1], 0x38);
        SeekField(key + ".group", _noteViewLineType, "linesGroup", _noteViewGroupType,
            line, NoteViewGroups[chartLine], 0x60);
        SeekField(key + ".static", _noteViewLineType, "lineIsStatic", Boolean, line, isStatic, 0x20);
        return line;
    }
    private void NoteViewSetReference(IntPtr owner, string name, IntPtr value)
    {
        var field = _structFields[ObjectClass(owner)].Single(field => _fieldMetadata[field].Name == name);
        _gameplayStorage[(owner, field)] = BitConverter.GetBytes(value.ToInt64());
    }
    internal void NoteViewWrongLine()
    {
        var line = SeekValue<IntPtr>(NoteViewRuntimeNotes[0], "lineInformation");
        NoteViewSetReference(line, "startLinePoint", NoteViewPoints[1][0]);
        NoteViewSetReference(line, "endLinePoint", NoteViewPoints[1][1]);
    }
    internal void NoteViewForeignNote()
        => NoteViewSetReference(NoteViewRuntimeNotes[0], "noteData", SeekNote(0, 3));
    internal void NoteViewSwapAllSlots()
    {
        var slots = _referenceArrays[CursorAllArray];
        (slots[0], slots[^1]) = (slots[^1], slots[0]);
    }
    internal IntPtr NoteViewExtraList(int chartNote) => SeekValue<IntPtr>(SeekNotes[chartNote], "otherInformations");
    internal uint[] NoteViewHandlesFor(IntPtr target)
        => _handles.Where(row => row.Value == target).Select(row => row.Key).OrderBy(id => id).ToArray();
    internal int NoteViewAllCount => _referenceArrays[CursorAllArray].Length;
    internal IntPtr NoteViewLinesClass => ClassOf(_seekLineListType);
    internal bool NoteViewInitialStorageValid(NoteViewShape shape)
        => NoteViewChartLines.Count == 2 && SeekNotes.Count == 5 && NoteViewPoints.All(points => points.Length == 3)
            && _seekLists[SeekLines].Length == 2 && NoteViewAllCount == (shape == NoteViewShape.Partial ? 4 : shape == NoteViewShape.MultipleSegments ? 6 : 5)
            && _referenceArrays[CursorRenderArray].All(_referenceArrays[CursorAllArray].Contains)
            && _referenceArrays[CursorAllArray].All(note => NoteViewRuntimeNotes.Contains(note));

    private void NoteViewRejectDispatch(string operation)
    {
        NoteViewDispatchViolations.Add(operation);
        throw new InvalidOperationException("Forbidden note-view synthetic dispatch: " + operation);
    }
    private bool NoteViewAllowedClass(IntPtr cls)
    {
        var type = GameplayTypeName(cls);
        return type is "UnityEngine.Resources" or "UnityEngine.Object" or "UnityEngine.SceneManagement.Scene"
            or "NormalModeInLevelController" or "UnityEngine.GameObject" or "UnityEngine.Object[]" or "InLevel.NoteInformation[]"
            || type is "System.Collections.Generic.List<InLevel.Line>" or "System.Collections.Generic.List<InLevel.Note>"
                or "System.Collections.Generic.List<InLevel.LinePoint>" or "System.Collections.Generic.List<System.Single>";
    }
    private IntPtr NoteViewMethods(IntPtr cls, IntPtr iterator)
    {
        ++NoteViewMethodResolutions;
        if (!NoteViewAllowedClass(cls)) NoteViewRejectDispatch("resolve " + GameplayTypeName(cls) + ".*");
        return GameplayMethods(cls, iterator);
    }
    private IntPtr NoteViewNamedMethod(IntPtr cls, string name, int count)
    {
        ++NoteViewMethodResolutions;
        if (!NoteViewAllowedClass(cls)) NoteViewRejectDispatch("resolve " + GameplayTypeName(cls) + "." + name);
        if (cls == ClassOf(_referenceArrayType))
            return name == "get_Rank" && count == 0 ? _arrayRank : name == "get_Length" && count == 0 ? _arrayLength
                : name == "GetValue" && count == 1 ? _arrayGet : IntPtr.Zero;
        return _gameplayMethods.FirstOrDefault(row => row.Value.Class == cls && row.Value.Name == name
            && row.Value.Parameters.Length == count).Key;
    }
    private IntPtr NoteViewInvoke(IntPtr method, IntPtr instance, IntPtr arguments, IntPtr exception)
    {
        ++NoteViewInvocations;
        string type, name;
        int? index = null;
        if (IsArrayMethod(method))
        {
            if (!_referenceArrays.ContainsKey(instance)) NoteViewRejectDispatch("invoke array on unowned receiver");
            type = GameplayTypeName(ObjectClass(instance));
            name = method == _arrayRank ? "get_Rank" : method == _arrayLength ? "get_Length" : "GetValue";
            if (method == _arrayGet) index = Marshal.ReadInt32(Marshal.ReadIntPtr(arguments));
        }
        else
        {
            if (!_gameplayMethods.TryGetValue(method, out var row)) NoteViewRejectDispatch("invoke unknown method " + method);
            type = GameplayTypeName(row!.Class);
            name = row.Name;
            var allowed = (type, name) switch
            {
                ("UnityEngine.Resources", "FindObjectsOfTypeAll") or ("UnityEngine.Object", "op_Implicit")
                    or ("UnityEngine.SceneManagement.Scene", "IsValidInternal" or "GetIsLoadedInternal") => instance == IntPtr.Zero,
                ("NormalModeInLevelController", "get_gameObject" or "get_isActiveAndEnabled") => instance == GameplayController,
                ("UnityEngine.GameObject", "get_scene") => instance == _seekGo,
                _ => type is "System.Collections.Generic.List<InLevel.Line>" or "System.Collections.Generic.List<InLevel.Note>"
                        or "System.Collections.Generic.List<InLevel.LinePoint>" or "System.Collections.Generic.List<System.Single>"
                    && name is "get_Count" or "get_Item" && _seekLists.ContainsKey(instance) && ObjectClass(instance) == row.Class
            };
            if (!allowed) NoteViewRejectDispatch("invoke " + type + "." + name + " receiver=" + instance);
            if (name == "get_Item") index = Marshal.ReadInt32(Marshal.ReadIntPtr(arguments));
        }
        var call = new NoteViewCall(type, name, instance, index);
        var result = SeekInvoke(method, instance, arguments, exception);
        AfterNoteViewInvocation?.Invoke(call);
        return result;
    }
    private void NoteViewReadField(IntPtr owner, IntPtr field, IntPtr output)
    {
        var name = _fieldMetadata[field].Name;
        GameplayReadField(owner, field, output);
        ObserveFlagGraphRead(name, owner);
        AfterNoteViewFieldRead?.Invoke(name, owner);
    }
    private IntPtr NoteViewObjectClass(IntPtr value)
    {
        ++NoteViewObjectClassCalls;
        var result = ObjectClass(value);
        AfterNoteViewObjectClass?.Invoke(value);
        return result;
    }
    private IntPtr NoteViewFields(IntPtr cls, IntPtr iterator)
    {
        ++NoteViewFieldMetadataEnumerations;
        return ClassFields(cls, iterator);
    }
    private uint NoteViewRoot(IntPtr target, bool pinned)
    {
        var handle = NewHandle(target, pinned);
        NoteViewRootLog.Add(new(handle, target, pinned));
        return handle;
    }
    private void NoteViewFree(uint handle)
    {
        var target = _handles.TryGetValue(handle, out var value) ? value : IntPtr.Zero;
        NoteViewFreeLog.Add(new(handle, target, handle == FailFreeHandle));
        FreeHandle(handle);
    }
}
