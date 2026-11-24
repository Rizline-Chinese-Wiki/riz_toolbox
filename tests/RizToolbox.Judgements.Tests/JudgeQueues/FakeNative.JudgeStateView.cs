using System.Runtime.InteropServices;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

// Adds only the real declared Judge fields and exact closed List metadata to
// the nine byte-preserved NoteView fixture inputs. No product setter is allowed.
sealed partial class FakeNative
{
    internal enum JudgeAlias { None, List, Backing }
    internal enum JudgeMetadataFault
    {
        Namespace, Name, Image, Parent, OpenGeneric, NotInflated, ArrayRank,
        ArrayElement, ArrayElementWidth, StorageOverlap, ForeignFieldOwner,
        StaticField, ByRefVersion, WrongVersionWidth, WrongHoldType, HoldOffset
    }
    internal readonly record struct JudgeMetadataCall(string Operation, long Subject);
    internal readonly List<JudgeMetadataCall> JudgeMetadataLog = new();
    internal readonly List<string> JudgeWriteViolations = new();
    internal readonly List<IntPtr> JudgeInspectedCapacityReferences = new();
    internal Action<string, IntPtr>? AfterJudgeMethodResolution;
    internal Action<string, IntPtr>? AfterJudgeMetadata;
    internal Action<uint, IntPtr>? AfterJudgeRoot;
    internal IntPtr JudgeOwner, JudgeForeignTail;
    internal readonly IntPtr[] JudgeLists = new IntPtr[3];
    internal bool JudgePublicationRooted = true;
    private IntPtr _judgeType, _judgeListType, _judgeAssemblies, _judgeMscorlibImage;
    private readonly Dictionary<IntPtr, uint> _judgeFlags = new();
    private readonly Dictionary<IntPtr, IntPtr> _judgeFieldParents = new();
    private JudgeMetadataFault? _judgeMetadataFault;
    private IntPtr[] _judgeInitialLists = Array.Empty<IntPtr>(), _judgeInitialBackings = Array.Empty<IntPtr>();
    private IntPtr[][] _judgeInitialSlots = Array.Empty<IntPtr[]>();
    private int[] _judgeInitialSizes = Array.Empty<int>(), _judgeInitialVersions = Array.Empty<int>();
    internal static readonly string[] JudgeListNames =
        { "notes", "tapNotesWaitingForJudge", "dragNotesWaitingForJudge" };

    internal void ConfigureJudgeStateView(NoteViewShape shape = NoteViewShape.Complete,
        JudgeAlias alias = JudgeAlias.None)
    {
        ConfigureNoteView(shape);
        _judgeType = SeekType("InLevel.NoteJudgeControl");
        _judgeListType = AddType("System.Collections.Generic.List<InLevel.NoteInformation>", 21, false, IntPtr.Size);
        JudgeOwner = SeekObject(_judgeType);
        SeekField("judge.info.owner", SeekType("InLevel.LevelInformation"), "noteJudgeControl", _judgeType,
            GameplayInformation, JudgeOwner, 0x58);
        SeekField("judge.owner.info", _judgeType, "levelInformation", SeekType("InLevel.LevelInformation"),
            JudgeOwner, GameplayInformation, 0x20);
        var actual = _referenceArrays[CursorAllArray];
        // This has a valid NoteInformation class and even the same chart data as
        // an owned note. It occurs only in capacity storage unless a test moves it.
        JudgeForeignTail = SeekObject(SeekType("InLevel.NoteInformation"));
        SeekField("judge.foreign.data", SeekType("InLevel.NoteInformation"), "noteData", _seekNoteType,
            JudgeForeignTail, SeekValue<IntPtr>(actual[0], "noteData"), 0x28);
        SeekField("judge.foreign.line", SeekType("InLevel.NoteInformation"), "lineInformation", _noteViewLineType,
            JudgeForeignTail, SeekValue<IntPtr>(actual[0], "lineInformation"), 0x20);
        JudgeLists[0] = JudgeNewList(3, -17,
            actual[^1], actual[0], actual[^1], IntPtr.Zero, JudgeForeignTail, actual[2]);
        JudgeLists[1] = alias == JudgeAlias.List ? JudgeLists[0]
            : JudgeNewList(2, int.MinValue, actual[2], actual[0], IntPtr.Zero, actual[2]);
        JudgeLists[2] = JudgeNewList(2, int.MaxValue, actual[1], actual[^1], actual[0], IntPtr.Zero);
        if (alias == JudgeAlias.Backing) JudgeSetList(1, "_items", JudgeBacking(0));
        for (var slot = 0; slot < JudgeLists.Length; ++slot)
            SeekField("judge.list." + slot, _judgeType, JudgeListNames[slot], _judgeListType,
                JudgeOwner, JudgeLists[slot], 0x38 + slot * IntPtr.Size);
        SeekField("judge.hold", _judgeType, "holdOnJudgeCount", Int32, JudgeOwner, int.MinValue, 0x50);
        _judgeMscorlibImage = GameplayAllocate(8);
        _gameplayImages.Add(_judgeMscorlibImage, "mscorlib.dll");
        _judgeAssemblies = GameplayAllocate(_gameplayImages.Count * IntPtr.Size);
        var ordinal = 0;
        foreach (var image in _gameplayImages.Keys)
            Marshal.WriteIntPtr(_judgeAssemblies, ordinal++ * IntPtr.Size, image);

        Add("il2cpp_domain_get_assemblies", "Ptr2", nameof(JudgeAssemblies));
        Add("il2cpp_class_get_name", "Ptr1", nameof(JudgeClassName));
        Add("il2cpp_class_get_namespace", "Ptr1", nameof(JudgeNamespace));
        Add("il2cpp_class_get_image", "Ptr1", nameof(JudgeClassImage));
        Add("il2cpp_class_get_parent", "Ptr1", nameof(JudgeClassParent));
        Add("il2cpp_class_is_generic", "Predicate", nameof(JudgeClassGeneric));
        Add("il2cpp_class_is_inflated", "Predicate", nameof(JudgeClassInflated));
        Add("il2cpp_class_get_rank", "Flags", nameof(JudgeArrayRank));
        Add("il2cpp_class_get_element_class", "Ptr1", nameof(JudgeArrayElement));
        Add("il2cpp_array_element_size", "ArrayElementSize", nameof(JudgeArrayElementSize));
        Add("il2cpp_field_get_flags", "Flags", nameof(JudgeFieldFlags));
        Add("il2cpp_field_get_parent", "Ptr1", nameof(JudgeFieldParent));
        Add("il2cpp_object_get_class", "Ptr1", nameof(JudgeObjectClass));
        Add("il2cpp_class_get_methods", "Ptr2", nameof(JudgeMethods));
        Add("il2cpp_class_get_method_from_name", "MethodLookup", nameof(JudgeNamedMethod));
        Add("il2cpp_field_set_value", "FieldSetValue", nameof(JudgeRejectValueWrite));
        Add("il2cpp_field_set_value_object", "FieldSetObject", nameof(JudgeRejectObjectWrite));
        Add("il2cpp_gchandle_new", "NewHandle", nameof(JudgeRoot));
        GameplayApi = new NativeRuntime(ExportPointer);
        GameplayApi.BindCurrentThread();
        _judgeInitialLists = JudgeLists.ToArray();
        _judgeInitialBackings = Enumerable.Range(0, 3).Select(JudgeBacking).ToArray();
        _judgeInitialSlots = Enumerable.Range(0, 3).Select(JudgeCapacity).ToArray();
        _judgeInitialSizes = Enumerable.Range(0, 3).Select(index => JudgeScalar(index, "_size")).ToArray();
        _judgeInitialVersions = Enumerable.Range(0, 3).Select(index => JudgeScalar(index, "_version")).ToArray();
    }

    // Synthetic-only reset between independent captures. No product write API
    // is called, and no already-faulted view is made valid by this operation.
    internal void JudgeResetStorage()
    {
        Array.Copy(_judgeInitialLists, JudgeLists, 3);
        JudgeSetField(GameplayInformation, "noteJudgeControl", JudgeOwner);
        JudgeSetField(JudgeOwner, "levelInformation", GameplayInformation);
        for (var index = 0; index < 3; ++index)
        {
            JudgeSetField(JudgeOwner, JudgeListNames[index], JudgeLists[index]);
            JudgeSetList(index, "_items", _judgeInitialBackings[index]);
            JudgeSetList(index, "_size", _judgeInitialSizes[index]);
            JudgeSetList(index, "_version", _judgeInitialVersions[index]);
            _referenceArrays[_judgeInitialBackings[index]] = _judgeInitialSlots[index].ToArray();
        }
        JudgeSetHold(int.MinValue);
    }
    internal void JudgeRestoreMetadata()
    {
        _judgeMetadataFault = null;
        _judgeFlags.Clear(); _judgeFieldParents.Clear();
        _fieldMetadata[JudgeField(JudgeLists[0], "_version")] = ("_version", Int32, 0x1c);
        _fieldMetadata[JudgeField(JudgeOwner, "holdOnJudgeCount")] = ("holdOnJudgeCount", Int32, 0x50);
    }
    private uint JudgeRoot(IntPtr target, bool pinned)
    {
        var handle = NoteViewRoot(target, pinned);
        AfterJudgeRoot?.Invoke(handle, target);
        return handle;
    }

    private IntPtr JudgeNewList(int size, int version, params IntPtr[] slots)
    {
        var list = SeekObject(_judgeListType);
        var key = "judge.storage." + _gameplayFields.Count;
        SeekField(key + ".items", _judgeListType, "_items", _cursorArrayType, list, CursorArray(slots), 0x10);
        SeekField(key + ".size", _judgeListType, "_size", Int32, list, size, 0x18);
        SeekField(key + ".version", _judgeListType, "_version", Int32, list, version, 0x1c);
        return list;
    }
    internal IntPtr JudgeBacking(int list) => SeekValue<IntPtr>(JudgeLists[list], "_items");
    internal IntPtr[] JudgeCapacity(int list) => _referenceArrays[JudgeBacking(list)].ToArray();
    internal int JudgeScalar(int list, string name)
        => BitConverter.ToInt32(_gameplayStorage[(JudgeLists[list], JudgeField(JudgeLists[list], name))]);
    private IntPtr JudgeField(IntPtr owner, string name)
        => _structFields[ObjectClass(owner)].Single(field => _fieldMetadata[field].Name == name);
    internal void JudgeSetList(int list, string name, object value)
        => JudgeSetField(JudgeLists[list], name, value);
    private void JudgeSetField(IntPtr owner, string name, object value)
        => _gameplayStorage[(owner, JudgeField(owner, name))] = value switch
        {
            int integer => BitConverter.GetBytes(integer),
            IntPtr pointer => BitConverter.GetBytes(pointer.ToInt64()),
            _ => throw new ArgumentException("Unsupported synthetic Judge storage.")
        };
    internal void JudgeSetHold(int value) => JudgeSetField(JudgeOwner, "holdOnJudgeCount", value);
    internal void JudgeSetSlot(int list, int slot, IntPtr value) => _referenceArrays[JudgeBacking(list)][slot] = value;
    internal void JudgeReplaceBacking(int list, int? capacity = null)
    {
        var old = JudgeCapacity(list);
        var slots = capacity is { } length ? new IntPtr[length] : old.ToArray();
        if (capacity is not null) Array.Copy(old, slots, Math.Min(old.Length, slots.Length));
        JudgeSetList(list, "_items", CursorArray(slots));
    }
    internal void JudgeReplaceList(int list)
    {
        var replacement = JudgeNewList(JudgeScalar(list, "_size"), JudgeScalar(list, "_version"), JudgeCapacity(list));
        JudgeSetField(JudgeOwner, JudgeListNames[list], replacement);
    }
    internal void JudgeChangeOwnerLink(bool informationToOwner)
    {
        if (informationToOwner) JudgeSetField(GameplayInformation, "noteJudgeControl", SeekObject(_judgeType));
        else JudgeSetField(JudgeOwner, "levelInformation", SeekObject(SeekType("InLevel.LevelInformation")));
    }
    internal void JudgeMutateMetadata(JudgeMetadataFault fault)
    {
        _judgeMetadataFault = fault;
        var version = JudgeField(JudgeLists[0], "_version");
        var hold = JudgeField(JudgeOwner, "holdOnJudgeCount");
        if (fault == JudgeMetadataFault.StorageOverlap)
            _fieldMetadata[version] = ("_version", Int32, 0x18);
        if (fault == JudgeMetadataFault.ForeignFieldOwner) _judgeFieldParents[version] = ClassOf(_judgeType);
        if (fault == JudgeMetadataFault.StaticField) _judgeFlags[version] = 0x10;
        if (fault == JudgeMetadataFault.ByRefVersion)
            _fieldMetadata[version] = ("_version", AddType("System.Int32", 8, true, 4, byRef: true), 0x1c);
        if (fault == JudgeMetadataFault.WrongVersionWidth)
            _fieldMetadata[version] = ("_version", AddType("System.Int32", 8, true, 8), 0x1c);
        if (fault == JudgeMetadataFault.WrongHoldType) _fieldMetadata[hold] = ("holdOnJudgeCount", Single, 0x50);
        if (fault == JudgeMetadataFault.HoldOffset) _fieldMetadata[hold] = ("holdOnJudgeCount", Int32, 0x54);
    }
    internal object[] JudgeDeclaredStorage() => JudgeLists.Select((list, index) => new
    {
        index, name = JudgeListNames[index], list = list.ToInt64(), listClass = ObjectClass(list).ToInt64(),
        backing = JudgeBacking(index).ToInt64(), size = JudgeScalar(index, "_size"), version = JudgeScalar(index, "_version"),
        slots = JudgeCapacity(index).Select(value => value.ToInt64()).ToArray()
    }).Cast<object>().ToArray();

    private void JudgeObserveMetadata(string operation, IntPtr subject)
    {
        JudgeMetadataLog.Add(new(operation, subject.ToInt64()));
        AfterJudgeMetadata?.Invoke(operation, subject);
    }
    private IntPtr JudgeAssemblies(IntPtr _, IntPtr count)
    { Marshal.WriteIntPtr(count, new IntPtr(_gameplayImages.Count)); return _judgeAssemblies; }
    private IntPtr JudgeClassName(IntPtr cls)
    {
        JudgeObserveMetadata("class.name", cls);
        return cls == ClassOf(_judgeListType)
            ? GameplayText(_judgeMetadataFault == JudgeMetadataFault.Name ? "Stack`1" : "List`1")
            : GameplayClassName(cls);
    }
    private IntPtr JudgeNamespace(IntPtr cls)
    {
        JudgeObserveMetadata("class.namespace", cls);
        return cls == ClassOf(_judgeListType)
            ? GameplayText(_judgeMetadataFault == JudgeMetadataFault.Namespace ? "Foreign.Collections" : "System.Collections.Generic")
            : GameplayNamespace(cls);
    }
    private IntPtr JudgeClassImage(IntPtr cls)
    {
        JudgeObserveMetadata("class.image", cls);
        if (cls == ClassOf(_judgeListType) && _judgeMetadataFault == JudgeMetadataFault.Image)
            return _gameplayImages.Single(row => row.Value == "Assembly-CSharp.dll").Key;
        return GameplayTypeName(cls).StartsWith("System.", StringComparison.Ordinal)
            ? _judgeMscorlibImage : _gameplayImages.Single(row => row.Value == "Assembly-CSharp.dll").Key;
    }
    private IntPtr JudgeClassParent(IntPtr cls)
    {
        JudgeObserveMetadata("class.parent", cls);
        return cls == ClassOf(_judgeListType)
            ? _judgeMetadataFault == JudgeMetadataFault.Parent ? ClassOf(String) : ClassOf(Object)
            : CursorClassParent(cls);
    }
    private bool JudgeClassGeneric(IntPtr cls)
    { JudgeObserveMetadata("class.generic", cls); return cls == ClassOf(_judgeListType) && _judgeMetadataFault == JudgeMetadataFault.OpenGeneric; }
    private bool JudgeClassInflated(IntPtr cls)
    { JudgeObserveMetadata("class.inflated", cls); return cls == ClassOf(_judgeListType) && _judgeMetadataFault != JudgeMetadataFault.NotInflated; }
    private uint JudgeArrayRank(IntPtr cls)
    { JudgeObserveMetadata("class.rank", cls); return cls == ClassOf(_cursorArrayType) && _judgeMetadataFault == JudgeMetadataFault.ArrayRank ? 2u : 1u; }
    private IntPtr JudgeArrayElement(IntPtr cls)
    {
        JudgeObserveMetadata("class.element", cls);
        return cls == ClassOf(_cursorArrayType) && _judgeMetadataFault != JudgeMetadataFault.ArrayElement
            ? ClassOf(SeekType("InLevel.NoteInformation")) : ClassOf(Object);
    }
    private int JudgeArrayElementSize(IntPtr cls)
    { JudgeObserveMetadata("class.elementSize", cls); return cls == ClassOf(_cursorArrayType) && _judgeMetadataFault != JudgeMetadataFault.ArrayElementWidth ? IntPtr.Size : ElementSize(cls); }
    private uint JudgeFieldFlags(IntPtr field)
    { JudgeObserveMetadata("field.flags", field); return _judgeFlags.TryGetValue(field, out var flags) ? flags : FieldFlags(field); }
    private IntPtr JudgeFieldParent(IntPtr field)
    { JudgeObserveMetadata("field.parent", field); return _judgeFieldParents.TryGetValue(field, out var parent) ? parent : GameplayFieldParent(field); }
    private IntPtr JudgeObjectClass(IntPtr value)
    {
        if (_boxedTypes.TryGetValue(value, out var type) && _types[type].Name == "InLevel.NoteInformation"
            && JudgeLists.Any(list => list != IntPtr.Zero
            && _referenceArrays[SeekValue<IntPtr>(list, "_items")].Contains(value)))
        {
            JudgePublicationRooted &= HasRootFor(value);
            JudgeInspectedCapacityReferences.Add(value);
        }
        return NoteViewObjectClass(value);
    }
    private IntPtr JudgeMethods(IntPtr cls, IntPtr iterator)
    {
        var result = NoteViewMethods(cls, iterator);
        AfterJudgeMethodResolution?.Invoke("enumerate", cls);
        return result;
    }
    private IntPtr JudgeNamedMethod(IntPtr cls, string name, int count)
    {
        var result = NoteViewNamedMethod(cls, name, count);
        AfterJudgeMethodResolution?.Invoke(name, cls);
        return result;
    }
    private void JudgeRejectValueWrite(IntPtr owner, IntPtr field, IntPtr value)
    {
        ++ValueFieldWrites;
        JudgeWriteViolations.Add("value:" + _fieldMetadata[field].Name);
        throw new InvalidOperationException("JudgeStateView entered a forbidden value setter.");
    }
    private void JudgeRejectObjectWrite(IntPtr owner, IntPtr field, IntPtr value)
    {
        ++ObjectFieldWrites;
        JudgeWriteViolations.Add("reference:" + _fieldMetadata[field].Name);
        throw new InvalidOperationException("JudgeStateView entered a forbidden reference setter.");
    }
}

