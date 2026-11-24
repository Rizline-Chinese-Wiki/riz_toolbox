using System.Runtime.InteropServices;

namespace RizToolbox.Judgements.Tests.NoteFlags;

// Reuses the real target-state, array and Hold-owner export fixtures. Only
// original factory/scheduler behavior is supplied here; no game DLL is loaded.
sealed partial class FakeNative
{
    internal IntPtr SeekHoldFactory, SeekHoldStart, SeekHoldStop;
    private IntPtr _seekHoldEnumerator, _seekHoldFingerArray;
    internal readonly List<(IntPtr Owner, string Field, int Bits)> SeekHoldWrites = new();
    internal int SeekHoldFactories, SeekHoldStarts, SeekHoldStops;
    internal Action? BeforeSeekHoldFactoryMetadata;
    internal Action? BeforeSeekHoldStartMetadata;
    internal Action<IntPtr>? AfterSeekHoldFactory;
    internal Action<string, IntPtr>? AfterSeekHoldStore;
    internal Func<IntPtr, IntPtr>? SeekHoldStartOriginal;
    internal Action? SeekHoldStopOriginal;

    internal void ConfigureSeekHoldEpoch(bool autoPlay, bool freeHold, bool twoHolds, bool reuseSource = false)
    {
        if (!reuseSource) ConfigureTargetState();
        AddType("UnityEngine.Coroutine", 18, false, IntPtr.Size);
        HoldJudge = JudgeOwner;
        ConfigureHoldNativeOwner(_judgeType);
        _seekHoldEnumerator = SeekType("System.Collections.IEnumerator");
        _seekHoldFingerArray = reuseSource ? _fingerArrayType : AddType("System.Int32[]", 29, false, IntPtr.Size);
        SeekField("seek.epoch.fingers.backing", _targetFingerType, "_items", _seekHoldFingerArray, IntPtr.Zero, IntPtr.Zero, 0x10);
        SeekField("seek.epoch.fingers.size", _targetFingerType, "_size", Int32, IntPtr.Zero, 0, 0x18);
        SeekField("seek.epoch.fingers.version", _targetFingerType, "_version", Int32, IntPtr.Zero, 0, 0x1c);
        var mono = SeekType("UnityEngine.MonoBehaviour");
        SeekHoldFactory = GameplayMethodAdd(_judgeType, "CheckHold", false, _seekHoldEnumerator,
            SeekType("InLevel.NoteInformation"), Boolean);
        SeekHoldStart = GameplayMethodAdd(mono, "StartCoroutine", false, SeekType("UnityEngine.Coroutine"), _seekHoldEnumerator);
        SeekHoldStop = GameplayMethodAdd(mono, "StopAllCoroutines", false, Void);
        HoldDiscoverySlots(GameplayController);
        ConfigureHoldDiscoveryMetadata();
        Add("il2cpp_class_get_rank", "Flags", nameof(SeekHoldArrayRank));
        Add("il2cpp_class_get_element_class", "Ptr1", nameof(SeekHoldArrayElement));
        Add("il2cpp_array_element_size", "ArrayElementSize", nameof(SeekHoldArrayWidth));
        Add("il2cpp_array_length", "ArraySize", nameof(HoldArrayLength));
        Add("il2cpp_array_get_byte_length", "ArraySize", nameof(HoldArrayBytes));
        Add("il2cpp_object_get_size", "ArraySize", nameof(HoldObjectSize));
        Add("il2cpp_array_object_header_size", "ReferenceArrayHeaderSize", nameof(HoldArrayHeaderSize));
        Add("il2cpp_class_is_assignable_from", "Assignable", nameof(SeekHoldAssignable));
        Add("il2cpp_class_get_parent", "Ptr1", nameof(SeekHoldClassParent));
        Add("il2cpp_class_get_name", "Ptr1", nameof(SeekHoldClassName));
        Add("il2cpp_class_get_namespace", "Ptr1", nameof(SeekHoldNamespace));
        Add("il2cpp_class_is_inflated", "Predicate", nameof(SeekHoldInflated));
        Add("il2cpp_method_get_param_count", "Flags", nameof(SeekHoldParameters));
        Add("il2cpp_runtime_invoke", "RuntimeInvoke", nameof(SeekHoldInvoke));
        Add("il2cpp_field_set_value", "FieldSetValue", nameof(SeekHoldSetField));
        Add("il2cpp_field_get_value", "FieldGet", nameof(SeekHoldReadField));
        TargetSet(JudgeOwner, "<AutoPlay>k__BackingField", autoPlay);
        TargetSet(JudgeOwner, "<FreeHold>k__BackingField", freeHold);
        TargetSet(JudgeOwner, "_nowTime", .5f);
        if (twoHolds) { TargetSetChart(0, 2, 0, 6); TargetSetTiming(0, 0, 3); }
        HoldIterator = SeekHoldCreateIterator(2, true);
        HoldHandle = HoldCreateHandle();
        ConfigureHoldSourceGuardExports();
        Add("il2cpp_class_instance_size", "ArraySize", nameof(SeekHoldInstanceSize));
        GameplayApi = new Runtime.NativeRuntime(ExportPointer); GameplayApi.BindCurrentThread();
    }

    internal IntPtr SeekHoldCreateIterator(int noteOrdinal, bool left)
    {
        var iterator = SeekObject(_seekCoroutineType);
        var prefix = "seek.epoch." + iterator.ToInt64();
        SeekField(prefix + ".owner", _seekCoroutineType, "<>4__this", _judgeType, iterator, JudgeOwner, 0x20);
        SeekField(prefix + ".state", _seekCoroutineType, "<>1__state", Int32, iterator, 0, 0x10);
        SeekField(prefix + ".current", _seekCoroutineType, "<>2__current", Object, iterator, IntPtr.Zero, 0x18);
        SeekField(prefix + ".note", _seekCoroutineType, "holdNoteInformation", SeekType("InLevel.NoteInformation"),
            iterator, TargetNote(noteOrdinal), 0x28);
        SeekField(prefix + ".left", _seekCoroutineType, "isPressLeft", Boolean, iterator, left, 0x30);
        SeekField(prefix + ".perfect", _seekCoroutineType, "<isNormalPerfect>5__2", Boolean, iterator, false, 0x31);
        SeekField(prefix + ".start", _seekCoroutineType, "<startTime>5__3", Single, iterator, 0f, 0x34);
        SeekField(prefix + ".particle", _seekCoroutineType, "<nextParticleTime>5__4", Single, iterator, 0f, 0x38);
        return iterator;
    }

    internal void SeekHoldMoveOriginal(IntPtr iterator)
    {
        var state = TargetInt(iterator, "<>1__state");
        if (state == 0)
        {
            TargetSet(iterator, "<isNormalPerfect>5__2", true);
            TargetSet(iterator, "<startTime>5__3", TargetFloat(JudgeOwner, "_nowTime"));
            TargetSet(iterator, "<nextParticleTime>5__4", 0f);
            TargetSet(JudgeOwner, "holdOnJudgeCount", checked(JudgeHoldValue + 1));
            TargetSet(iterator, "<>1__state", 2);
        }
        else if (state is 2 or 3) TargetSet(iterator, "<>1__state", 3);
        else throw new InvalidOperationException("The controlled paused original received an unsupported state.");
    }

    private bool SeekHoldAssignable(IntPtr target, IntPtr source)
        => target == ClassOf(_seekHoldEnumerator) && source == HoldIteratorClass || HoldAssignable(target, source) || CursorAssignable(target, source);
    private IntPtr SeekHoldClassParent(IntPtr cls) => cls == ClassOf(_judgeListType) || cls == ClassOf(_targetFingerType) ? ClassOf(Object)
        : cls == ClassOf(_cursorArrayType) ? CursorClassParent(cls) : HoldClassParent(cls);
    private IntPtr SeekHoldClassName(IntPtr cls) => cls == ClassOf(_targetFingerType) ? GameplayText("List`1") : JudgeClassName(cls);
    private IntPtr SeekHoldNamespace(IntPtr cls) => cls == ClassOf(_targetFingerType) ? GameplayText("System.Collections.Generic") : JudgeNamespace(cls);
    private bool SeekHoldInflated(IntPtr cls) => cls == ClassOf(_targetFingerType) || JudgeClassInflated(cls);
    private uint SeekHoldArrayRank(IntPtr cls) => cls == ClassOf(_referenceArrayType) ? 1u : JudgeArrayRank(cls);
    private IntPtr SeekHoldArrayElement(IntPtr cls) => cls == ClassOf(_seekHoldFingerArray) ? ClassOf(Int32)
        : cls == ClassOf(_referenceArrayType) ? ClassOf(SeekType("UnityEngine.Object")) : JudgeArrayElement(cls);
    private int SeekHoldArrayWidth(IntPtr cls) => cls == ClassOf(_seekHoldFingerArray) ? sizeof(int)
        : cls == ClassOf(_referenceArrayType) ? IntPtr.Size : JudgeArrayElementSize(cls);
    private uint SeekHoldInstanceSize(IntPtr cls)
    {
        var held = HoldSourceInstanceSize(cls);
        return held != 0 ? held : CursorInstanceSize(cls);
    }
    private uint SeekHoldParameters(IntPtr method)
    {
        if (method == SeekHoldFactory) BeforeSeekHoldFactoryMetadata?.Invoke();
        if (method == SeekHoldStart) BeforeSeekHoldStartMetadata?.Invoke();
        return GameplayParamCount(method);
    }
    private void SeekHoldReadField(IntPtr owner, IntPtr field, IntPtr output)
    {
        HoldReadField(owner, field, output);
        AfterTargetFieldRead?.Invoke(_fieldMetadata[field].Name, owner);
    }
    private void SeekHoldSetField(IntPtr owner, IntPtr field, IntPtr input)
    {
        var name = _fieldMetadata[field].Name;
        if (owner == JudgeOwner && name is "holdOnJudgeCount" or "_nowTime"
            || ObjectClass(owner) == HoldIteratorClass && name == "<>1__state")
        {
            AllGameplayReceiversRooted &= HasRootFor(owner);
            var bits = Marshal.ReadInt32(input); ++ValueFieldWrites;
            _gameplayStorage[(owner, field)] = BitConverter.GetBytes(bits);
            SeekHoldWrites.Add((owner, name, bits)); AfterSeekHoldStore?.Invoke(name, owner); return;
        }
        JudgeQueueSetFieldValue(owner, field, input);
    }
    private IntPtr SeekHoldInvoke(IntPtr method, IntPtr receiver, IntPtr arguments, IntPtr exception)
    {
        if (method == SeekHoldFactory || method == SeekHoldStart || method == SeekHoldStop)
        {
            ++Invocations; AllGameplayReceiversRooted &= receiver == JudgeOwner && HasRootFor(receiver);
            Marshal.WriteIntPtr(exception, IntPtr.Zero);
            if (method == SeekHoldFactory)
            {
                ++SeekHoldFactories;
                var note = Marshal.ReadIntPtr(arguments);
                var left = Marshal.ReadByte(Marshal.ReadIntPtr(arguments, IntPtr.Size)) != 0;
                CheckSeekHoldRoot(note);
                var created = SeekHoldCreateIterator(Array.IndexOf(_referenceArrays[CursorAllArray], note), left);
                AfterSeekHoldFactory?.Invoke(created); return created;
            }
            if (method == SeekHoldStart)
            {
                ++SeekHoldStarts; var iterator = Marshal.ReadIntPtr(arguments);
                CheckSeekHoldRoot(iterator);
                try { return SeekHoldStartOriginal!(iterator); }
                catch (IOException) { Marshal.WriteIntPtr(exception, new IntPtr(0x12345)); return IntPtr.Zero; }
            }
            ++SeekHoldStops; SeekHoldStopOriginal!(); return IntPtr.Zero;
        }
        return TargetInvoke(method, receiver, arguments, exception);
    }
    private void CheckSeekHoldRoot(IntPtr value)
    {
        if (value == IntPtr.Zero || !HasRootFor(value)) throw new InvalidOperationException("The controlled Hold call received an unrooted reference.");
    }
}
