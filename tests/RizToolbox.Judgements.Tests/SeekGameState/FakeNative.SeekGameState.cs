using System.Runtime.InteropServices;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

// One graph is constructed once. Every participating production reader sees
// these same source objects, classes, lists, arrays and Transform identities.
// Only official exports and native method behavior are supplied by this shim.
sealed partial class FakeNative
{
    internal readonly List<(IntPtr Owner, string Name, long Bits)> GameStateScalarStores = new();
    internal Action<string>? AfterGameStateScalarStore;
    internal readonly List<(IntPtr Owner, int Bits)> GameStateTimeStores = new();
    internal readonly List<string> GameStateStages = new();
    private IntPtr _gameStateActive, _gameStateSetNotes;
    private readonly HashSet<IntPtr> _gameStateRetiredMethods = new();
    internal ISeekNoteVisibilityNativeProfile GameStateVisibilityProfile = null!;

    internal void ConfigureSeekGameStateGraph(IntPtr image)
    {
        CanvasBasisUseStableBorrowedMetadata();
        ConfigureLineRingSource(preserveHolds: true);
        CanvasSourceNoHold = false;
        TargetSet(GameplayInformation, "holdControl", TargetHold);
        // Canvas-only capture deliberately poisoned these fields. Restore the
        // actual original lists, never a new graph or unrelated capture.
        TargetSet(TargetHold, "holdNotes", TargetHoldLists[0]);
        TargetSet(TargetHold, "runningHoldNotes", TargetHoldLists[1]);
        var thirdCanvas = CanvasSourceCloneObject(CanvasSourceMoves[0]);
        CanvasSourceSlot(CanvasSourceMovesArray, 2, thirdCanvas);
        TargetSet(CanvasSourceMoves[0], "index", 0);
        TargetSet(CanvasSourceMoves[1], "index", 1);
        TargetSet(thirdCanvas, "index", 2);
        ConfigureNoteFlags(reuseSource: true);
        ConfigureFingerIds(reuseSource: true);
        ConfigureRenderers(RendererShape.Inactive, 5, reuseSource: true);
        ConfigureRendererInverse();
        foreach (var name in new[] { "SetParent", "set_localPosition", "set_localRotation", "set_localScale" })
        {
            // Both fixtures describe this one native Transform declaration.
            // Keep the original geometry MethodInfo, not two same-signature methods.
            _gameStateRetiredMethods.Add(_rendererInverseMethods[name]);
            _rendererInverseMethods[name] = _geometryMethods[name];
        }
        ConfigureLineBuffer(Enumerable.Range(0, 107).Select(i => new NativeVector3BufferBits(i, i + 1, i + 2)).ToArray());
        Add("il2cpp_class_get_rank", "Flags", nameof(GameStateArrayRank));
        Add("il2cpp_class_get_element_class", "Ptr1", nameof(GameStateArrayElement));
        Add("il2cpp_array_element_size", "ArrayElementSize", nameof(GameStateArrayWidth));
        Add("il2cpp_array_class_get", "ArrayClassGet", nameof(GameStateArrayClass));
        ConfigureLineRendererConsumer(image);
        // The general read fixture includes deliberate out-of-chart orphan
        // lines. The complete transaction uses all original runtime lines and
        // the additional renderer line, each with its actual adjacent pair.
        var exactLines = LineRingLines.Where(line => line != LineRingOrphanStatic && line != LineRingOrphanDynamic).ToArray();
        TargetSet(LineRendererSecondDynamic, "linesGroup", NoteViewGroups[1]);
        foreach (var points in NoteViewPoints) TargetSet(points[^1], "canvasIndex", 1);
        foreach (var line in exactLines)
        {
            var start = SeekValue<IntPtr>(line, "startLinePoint");
            var end = SeekValue<IntPtr>(line, "endLinePoint");
            var startCanvas = TargetInt(start, "canvasIndex");
            var isStatic = startCanvas == TargetInt(end, "canvasIndex");
            TargetSet(line, "canvasIndex", startCanvas);
            TargetSet(line, "lineIsStatic", isStatic);
            _noteViewStaticLines[line] = isStatic;
        }
        foreach (var line in LineRingLines)
            SeekField("composition.line.visible." + line, _noteViewLineType, "isVisible", Boolean, line, true, 0x51);
        CursorRenderArray = CursorArray(NoteViewRuntimeNotes.Where(note =>
            !TargetBoolean(SeekValue<IntPtr>(note, "lineInformation"), "lineIsStatic")).ToArray());
        SetGameplayField("cursor.render", CursorRenderArray);
        TargetSet(CursorControl, "_startIndex", Math.Min(1, _referenceArrays[CursorRenderArray].Length));
        TargetSet(CursorControl, "_hasNote", _referenceArrays[CursorRenderArray].Length > 0);
        foreach (var group in NoteViewGroups)
            LineRingSetGroupLines(group, exactLines.Where(line => SeekValue<IntPtr>(line, "linesGroup") == group).ToArray());
        LineRingSetGroups(NoteViewGroups.ToArray());
        // The older consumer fixture's first dynamic reference may become
        // static when the complete chart restores its real Canvas pairs.
        // Duplicate one actual dynamic occurrence, never that retired choice.
        var dynamicLines = exactLines.Where(line => !TargetBoolean(line, "lineIsStatic")).ToArray();
        if (dynamicLines.Length == 0)
            throw new InvalidOperationException("The composed chart lacks an actual cross-Canvas line.");
        LineRingSetDynamic(dynamicLines.Append(dynamicLines[0]).ToArray());
        SeekField("composition.control.speed", _cursorType, "_speed", Single, CursorControl, 2f, 0x40);
        SeekField("composition.control.canvas", _cursorType, "_canvasControl", _geometryCanvasType,
            CursorControl, GeometryCanvasControl, 0x38);
        SeekField("composition.information.speed", SeekType("InLevel.LevelInformation"), "speed", Single,
            GameplayInformation, 2f, 0x98);
        // The general read-only fixture deliberately uses a NaN scene-width
        // canary. A complete producer composition needs actual finite scene
        // inputs before its independent dynamic observation can be admitted.
        TargetSet(LineRingControl, "_sceneWidth", 1920f);
        TargetSet(LineRingControl, "_nowTime", 2.5f);
        TargetSet(LineRingControl, "_speed", 2f);
        var counterNames = new[] { "_bad", "_miss", "_perfect", "_early", "_late", "_maxPerfect", "_challengeTimeMiss" };
        for (var slot = 0; slot < counterNames.Length; ++slot)
        {
            var field = _gameplayFields["seek.counter." + counterNames[slot]].Field;
            var metadata = _fieldMetadata[field];
            _fieldMetadata[field] = (metadata.Name, metadata.Type, 0x100 + slot * sizeof(int));
            TargetSet(GameplayController, counterNames[slot], 17 + slot);
        }
        SeekField("composition.player.source", SeekType("Pigeon.MUGToolkit.Core.MUGMusicPlayer"),
            "audioSource", SeekType("UnityEngine.AudioSource"), GameplayPlayer, GameplaySource, 0x20);
        SeekField("composition.progress.player", SeekType("InLevel.ProgressControl"),
            "<MusicPlayer>k__BackingField", SeekType("Pigeon.MUGToolkit.Core.MUGMusicPlayer"),
            GameplayProgress, GameplayPlayer, 0x28);
        SeekField("composition.progress.source", SeekType("InLevel.ProgressControl"),
            "audioSource", SeekType("UnityEngine.AudioSource"), GameplayProgress, GameplaySource, 0x30);
        foreach (var receiver in new[] { GameplayPlayer, GameplaySource, GameplayClip })
            Marshal.WriteIntPtr(receiver, ObjectClass(receiver));
        GameplayMethodAdd(SeekType("UnityEngine.AudioSource"), "get_clip", false, SeekType("UnityEngine.AudioClip"));
        GameplayMethodAdd(SeekType("Pigeon.MUGToolkit.Core.MUGMusicPlayer"), "SetTime", false, Void, Single);
        _gameStateActive = GameplayMethodAdd(_cursorType, "ActiveNotes", false, Void);
        _gameStateSetNotes = GameplayMethodAdd(_cursorType, "SetNotes", false, Void);
        foreach (var note in NoteViewRuntimeNotes)
        {
            var transform = GeometryComponentTransforms.TryGetValue(note, out var existing) ? existing
                : GeometryNewTransform(GeometryUiRoot, new(1, 2, 3), new(0, 0, 0, 1), new(1, 1, 1));
            GeometryComponentTransforms[note] = transform;
            _rendererTransforms[note] = transform;
            if (!_rendererTrs.ContainsKey(transform))
            {
                var original = GeometryTransforms[transform];
                _rendererTrs.Add(transform, new(original.Parent, original.Position, original.Rotation, original.Scale));
            }
        }
        foreach (var component in new[] { GeometryCamera }.Concat(LineRingLines))
            _rendererTransforms[component] = GeometryComponentTransforms[component];
        // Keep the initial paused audio and all four caches mutually consistent.
        TargetSet(GameplayProgress, "_offset", .25d);
        TargetSet(GameplayProgress, "_nowTime", 3.75d);
        TargetSet(GameplayInformation, "nowTime", 7.5f);
        TargetSet(GameplayInformation, "nowRealTime", 3.75f);
        TargetSet(GameplayInformation, "nowSyncedTime", 3.75f);
        GameplayPitch = 1.5f;
        NoteTimeRemember(); InitializationRemember();
        GameStateVisibilityProfile = new GameStateVisibility(this);
        CanvasSourceRemember();
    }

    internal void ConfigureSeekGameStateExports()
    {
        Add("il2cpp_class_from_name", "ClassLookup", nameof(GameStateFindClass));
        Add("il2cpp_class_get_name", "Ptr1", nameof(GameStateClassName));
        Add("il2cpp_class_get_namespace", "Ptr1", nameof(GameStateNamespace));
        Add("il2cpp_class_get_image", "Ptr1", nameof(GameStateClassImage));
        Add("il2cpp_class_get_parent", "Ptr1", nameof(GameStateClassParent));
        Add("il2cpp_class_is_assignable_from", "Assignable", nameof(GameStateAssignable));
        Add("il2cpp_class_is_inflated", "Predicate", nameof(GameStateInflated));
        Add("il2cpp_class_get_methods", "Ptr2", nameof(GameStateMethods));
        Add("il2cpp_class_get_method_from_name", "MethodLookup", nameof(GameStateNamedMethod));
        Add("il2cpp_class_get_nested_types", "Ptr2", nameof(GameStateNested));
        Add("il2cpp_method_get_name", "Ptr1", nameof(LineRendererMethodName));
        Add("il2cpp_method_get_class", "Ptr1", nameof(LineRendererMethodClass));
        Add("il2cpp_method_get_param_count", "Flags", nameof(GameStateParameterCount));
        Add("il2cpp_method_get_param", "ParameterType", nameof(LineRendererParameter));
        Add("il2cpp_method_get_return_type", "Ptr1", nameof(LineRendererReturn));
        Add("il2cpp_method_get_flags", "MethodFlags", nameof(LineRendererMethodFlags));
        Add("il2cpp_field_get_value", "FieldGet", nameof(GameStateReadField));
        Add("il2cpp_field_set_value", "FieldSetValue", nameof(GameStateSetValue));
        Add("il2cpp_field_set_value_object", "FieldSetObject", nameof(GameStateSetObject));
        Add("il2cpp_field_static_get_value", "StaticGet", nameof(GeometryStaticRead));
        Add("il2cpp_field_get_flags", "Flags", nameof(GameStateFieldFlags));
        Add("il2cpp_object_get_class", "Ptr1", nameof(GameStateObjectClass));
        Add("il2cpp_class_get_rank", "Flags", nameof(GameStateArrayRank));
        Add("il2cpp_class_get_element_class", "Ptr1", nameof(GameStateArrayElement));
        Add("il2cpp_array_element_size", "ArrayElementSize", nameof(GameStateArrayWidth));
        Add("il2cpp_array_class_get", "ArrayClassGet", nameof(GameStateArrayClass));
        Add("il2cpp_array_length", "ArraySize", nameof(GameStateArrayLength));
        Add("il2cpp_array_get_byte_length", "ArraySize", nameof(GameStateArrayBytes));
        Add("il2cpp_object_get_size", "ArraySize", nameof(GameStateObjectSize));
        Add("il2cpp_class_instance_size", "ArraySize", nameof(GameStateInstanceSize));
        Add("il2cpp_gchandle_new", "NewHandle", nameof(GameStateRoot));
        Add("il2cpp_runtime_invoke", "RuntimeInvoke", nameof(GameStateInvoke));
        GameplayApi = new NativeRuntime(ExportPointer); GameplayApi.BindCurrentThread();
    }

    private IntPtr GameStateFindClass(IntPtr image, string ns, string name)
        => ns == "System" && name == "IntPtr" ? ClassOf(_holdIntPtrType)
            : ns == "System" && name == "Array" ? ClassOf(_fingerSystemArrayType) : FlagFindClass(image, ns, name);
    private IntPtr GameStateClassName(IntPtr cls) => cls == ClassOf(_fingerListType) ? GameplayText("List`1")
        : cls == ClassOf(_seekKeyListType) ? GameplayText("List`1")
        : cls == ClassOf(_judgeListType) ? JudgeClassName(cls) : RendererClassName(cls);
    private IntPtr GameStateNamespace(IntPtr cls) => cls == ClassOf(_fingerListType) || cls == ClassOf(_seekKeyListType)
        ? GameplayText("System.Collections.Generic")
        : cls == ClassOf(_judgeListType) ? JudgeNamespace(cls) : RendererNamespace(cls);
    private IntPtr GameStateClassImage(IntPtr cls) => _holdSourceClassImages.TryGetValue(cls, out var image) ? image
        : cls == ClassOf(_fingerListType) || cls == ClassOf(_fingerArrayType) || cls == ClassOf(_fingerSystemArrayType)
            ? _judgeMscorlibImage : GeometryClassImage(cls);
    private IntPtr GameStateClassParent(IntPtr cls)
    {
        if (cls == _holdJudgeClass || cls == _holdMonoBehaviourClass || cls == _holdBehaviourClass
            || cls == _holdComponentClass || cls == _holdUnityObjectClass || cls == GameplayControllerClass
            || cls == GameplayControllerBaseClass) return HoldClassParent(cls);
        if (_rendererInverseComponentClasses.Contains(cls)) return RendererClassParent(cls);
        if (cls == ClassOf(_fingerListType) || cls == ClassOf(_seekKeyListType) || cls == ClassOf(_fingerSystemArrayType)) return ClassOf(Object);
        if (cls == ClassOf(_judgeListType)) return JudgeClassParent(cls);
        if (cls == ClassOf(_rendererListType) || cls == ClassOf(_rendererSystemArrayType)
            || cls == ClassOf(_rendererGameObjectType) || cls == ClassOf(_rendererPoolType)
            || cls == ClassOf(_rendererChildType) || cls == ClassOf(_rendererSpriteType)) return RendererClassParent(cls);
        if (cls == ClassOf(_fingerArrayType)) return ClassOf(_fingerSystemArrayType);
        if (cls == ClassOf(_lineRendererUnityType)) return ClassOf(_geometryComponentType);
        if (RendererArrayClass(cls)) return ClassOf(_referenceArrayType);
        return GeometryClassParent(cls);
    }
    private bool GameStateAssignable(IntPtr target, IntPtr source)
    {
        if (target == ClassOf(_seekHoldEnumerator) && source == HoldIteratorClass) return true;
        for (var current = source; current != IntPtr.Zero; current = GameStateClassParent(current))
            if (current == target) return true;
        return GameplayAssignable(target, source);
    }
    private bool GameStateInflated(IntPtr cls) => cls == ClassOf(_fingerListType) || cls == ClassOf(_seekKeyListType)
        || JudgeClassInflated(cls) || RendererInflated(cls);
    private IntPtr GameStateMethods(IntPtr cls, IntPtr iterator)
    {
        if (cls == ClassOf(_referenceArrayType)) return ClassMethods(cls, iterator);
        if (cls == ClassOf(_noteViewLineType)) return LineRendererMethods(cls, iterator);
        var rows = _gameplayMethods.Where(row => row.Value.Class == cls && !_gameStateRetiredMethods.Contains(row.Key)).ToArray();
        var index = Marshal.ReadIntPtr(iterator).ToInt32();
        if (index >= rows.Length) return IntPtr.Zero;
        Marshal.WriteIntPtr(iterator, new IntPtr(index + 1)); return rows[index].Key;
    }
    private IntPtr GameStateNamedMethod(IntPtr cls, string name, int count)
        => cls == ClassOf(_noteViewLineType) && name == "SetPositions" && count == 2 ? _lineRendererMethod
            : _gameplayMethods.FirstOrDefault(row => row.Value.Class == cls && row.Value.Name == name
                && row.Value.Parameters.Length == count && !_gameStateRetiredMethods.Contains(row.Key)).Key;
    private IntPtr GameStateNested(IntPtr cls, IntPtr iterator)
        => cls == ClassOf(SeekType("InLevel.NoteInformation")) ? RendererNested(cls, iterator)
            : cls == ClassOf(_noteViewLineType) ? LineRendererNested(cls, iterator) : SeekNested(cls, iterator);
    private uint GameStateParameterCount(IntPtr method)
    {
        if (method == SeekHoldFactory) BeforeSeekHoldFactoryMetadata?.Invoke();
        if (method == SeekHoldStart) BeforeSeekHoldStartMetadata?.Invoke();
        return LineRendererParameterCount(method);
    }
    private void GameStateReadField(IntPtr owner, IntPtr field, IntPtr output)
    {
        if (LineRingLines.Contains(owner) && _fieldMetadata[field].Name is "_positionsPlan" or "_positionCountPlan")
            LineRendererReadField(owner, field, output);
        else if (owner == LineRingControl && _fieldMetadata[field].Name is "startPosition" or "endPosition" or "newLinePosition" or "easeType" or "linePointCount")
            ProducerReadControlField(owner, field, output);
        else HoldReadField(owner, field, output);
        AfterTargetFieldRead?.Invoke(_fieldMetadata[field].Name, owner);
    }
    private uint GameStateFieldFlags(IntPtr field) => _flagFields.Contains(field) ? FlagFieldFlags(field)
        : field == _fingerField ? FingerFieldFlags(field) : JudgeFieldFlags(field);
    private IntPtr GameStateObjectClass(IntPtr owner)
        => _rendererArrays.ContainsKey(owner) ? RendererObjectClass(owner) : GeometryObjectClass(owner);
    private uint GameStateArrayRank(IntPtr cls) => cls == ClassOf(_fingerArrayType) ? 1u
        : cls == ClassOf(_referenceArrayType) ? HoldArrayRank(cls)
        : RendererArrayClass(cls) ? RendererArrayRank(cls) : GeometryArrayRank(cls);
    private IntPtr GameStateArrayClass(IntPtr cls, uint rank) => cls == ClassOf(_geometryVectorType) && rank == 1
        ? ClassOf(_lineBufferArrayType) : LineRingArrayClass(cls, rank);
    private IntPtr GameStateArrayElement(IntPtr cls) => cls == ClassOf(_fingerArrayType) ? ClassOf(Int32)
        : RendererArrayClass(cls) ? RendererArrayElement(cls)
        : cls == ClassOf(_referenceArrayType) ? ClassOf(SeekType("UnityEngine.Object")) : GeometryArrayElement(cls);
    private int GameStateArrayWidth(IntPtr cls) => cls == ClassOf(_fingerArrayType) ? 4
        : cls == ClassOf(_referenceArrayType) ? IntPtr.Size
        : cls == ClassOf(_lineBufferArrayType) ? 12 : RendererArrayClass(cls) ? IntPtr.Size : GeometryArrayWidth(cls);
    private uint GameStateArrayLength(IntPtr value) => _fingerArrays.TryGetValue(value, out var integers) ? checked((uint)integers.Length)
        : _rendererArrays.TryGetValue(value, out var refs) ? checked((uint)refs.Length)
            : _lineBufferLengths.TryGetValue(value, out var count) ? checked((uint)count) : GeometryArrayLength(value);
    private uint GameStateArrayBytes(IntPtr value) => _fingerArrays.ContainsKey(value) ? GameStateArrayLength(value) * 4
        : _rendererArrays.ContainsKey(value) ? GameStateArrayLength(value) * (uint)IntPtr.Size
            : _lineBufferLengths.ContainsKey(value) ? GameStateArrayLength(value) * 12 : GeometryArrayBytes(value);
    private uint GameStateObjectSize(IntPtr value) => value == LineRingControl ? ProducerControlObjectSize(value)
        : _fingerArrays.ContainsKey(value) || _rendererArrays.ContainsKey(value) || _lineBufferLengths.ContainsKey(value)
            ? checked((uint)ArrayHeaderBytes + GameStateArrayBytes(value)) : LineRendererObjectSize(value);
    private uint GameStateInstanceSize(IntPtr cls)
        => Math.Max(0x400u, HoldSourceInstanceSize(cls));
    private uint GameStateRoot(IntPtr value, bool pinned) => GeometryRoot(value, pinned);

    private void GameStateSetValue(IntPtr owner, IntPtr field, IntPtr input)
    {
        var name = _fieldMetadata[field].Name;
        if (_rendererStorageFields.Contains(field)) { RendererInverseField(owner, field, input); return; }
        if (LineRingLines.Contains(owner) && name == "_positionCountPlan") { LineRendererSetValue(owner, field, input); return; }
        if (_noteTimeSelected.ContainsKey((owner, field))) { NoteTimeSetValue(owner, field, input); return; }
        if (FlagNotes.Contains(owner) && Array.IndexOf(_flagFields, field) >= 0) { FlagSetValue(owner, field, input); return; }
        if (_fingerLists.Contains(owner)) { FingerSetFieldValue(owner, field, input); return; }
        if (TargetHoldLists.Contains(owner) || owner == TargetHold
            || HoldStateNotes.Contains(owner) && name is "isStaticNote" or "lastFrameHoldLineLength")
        { HoldStateSetValue(owner, field, input); return; }
        if (JudgeLists.Contains(owner)) { JudgeQueueSetFieldValue(owner, field, input); return; }
        if (owner == JudgeOwner && name is "holdOnJudgeCount" or "_nowTime"
            || ObjectClass(owner) == HoldIteratorClass && name == "<>1__state")
        { SeekHoldSetField(owner, field, input); return; }
        if (InitializationAllowed(owner, field)) { InitializationSetValue(owner, field, input); return; }
        if (owner == GeometryCanvasControl && name is "nowTime" or "speed") { CanvasCacheWriteScalar(owner, field, input); return; }
        var selected = owner == GameplayProgress && name == "_nowTime"
            || owner == GameplayInformation && name is "nowTime" or "nowRealTime" or "nowSyncedTime"
            || owner == GameplayController && name is "_bad" or "_miss" or "_perfect" or "_early" or "_late" or "_maxPerfect" or "_challengeTimeMiss"
            || owner == CursorControl && name is "_speed" or "_hasNote" or "_activeHasNote" or "_startIndex" or "_activeStartIndex";
        if (!selected || !_types[_fieldMetadata[field].Type].ValueType)
            throw new InvalidOperationException("Combined fixture rejected unowned scalar " + owner + "/" + name);
        var width = _types[_fieldMetadata[field].Type].Size;
        var bytes = new byte[width]; Marshal.Copy(input, bytes, 0, width);
        AllGameplayReceiversRooted &= HasRootFor(owner); ++ValueFieldWrites;
        _gameplayStorage[(owner, field)] = bytes;
        GameStateScalarStores.Add((owner, name, width == 1 ? bytes[0] : width == 8 ? BitConverter.ToInt64(bytes) : BitConverter.ToInt32(bytes)));
        GameStateStages.Add(name); AfterGameStateScalarStore?.Invoke(name);
    }
    private void GameStateSetObject(IntPtr owner, IntPtr field, IntPtr value)
    {
        if (_rendererStorageFields.Contains(field)) { RendererInverseField(owner, field, value); return; }
        if (LineRingLines.Contains(owner) && _fieldMetadata[field].Name == "_positionsPlan") { LineRendererSetObject(owner, field, value); return; }
        if (TargetHoldLists.Contains(owner)) { HoldStateSetObject(owner, field, value); return; }
        if (JudgeLists.Contains(owner)) { JudgeQueueSetFieldObject(owner, field, value); return; }
        if (_fingerLists.Contains(owner) || FingerNotes.Contains(owner) && field == _fingerField)
        { FingerSetFieldObject(owner, field, value); return; }
        throw new InvalidOperationException("Combined fixture rejected an unowned reference store.");
    }

    private IntPtr GameStateInvoke(IntPtr method, IntPtr receiver, IntPtr arguments, IntPtr exception)
    {
        if (method == SeekHoldFactory || method == SeekHoldStart || method == SeekHoldStop)
            return SeekHoldInvoke(method, receiver, arguments, exception);
        if (method == _lineRendererMethod) return LineRendererInvoke(method, receiver, arguments, exception);
        if (method == _gameStateActive || method == _gameStateSetNotes)
        {
            ++Invocations; AllGameplayReceiversRooted &= receiver == CursorControl && HasRootFor(receiver);
            Marshal.WriteIntPtr(exception, IntPtr.Zero); GameStateStages.Add(method == _gameStateActive ? "ActiveNotes" : "SetNotes");
            // Controlled native bodies leave flags, heads and pools unchanged,
            // but execute the real scan cursor outcome over this actual graph.
            // Production still independently reads and validates every value.
            if (method == _gameStateActive)
            {
                var order = _referenceArrays[CursorAllArray];
                var cursor = order.TakeWhile(note => TargetBoolean(note, "destroyed")).Count();
                TargetSet(CursorControl, "_activeStartIndex", cursor);
                TargetSet(CursorControl, "_activeHasNote", cursor < order.Length);
            }
            else
            {
                var order = _referenceArrays[CursorRenderArray];
                var cursor = order.TakeWhile(note => TargetBoolean(note, "isStaticNote")).Count();
                TargetSet(CursorControl, "_startIndex", cursor);
                TargetSet(CursorControl, "_hasNote", cursor < order.Length);
            }
            return IntPtr.Zero;
        }
        if (method == _rendererAppear || method == _rendererDisappear
            || _rendererInverseMethods.Values.Contains(method) && !_geometryMethods.Values.Contains(method))
            return RendererInvoke(method, receiver, arguments, exception);
        if (_geometryMethods.Values.Contains(method))
        {
            if (_rendererTrs.ContainsKey(receiver) && !GeometryTransforms.ContainsKey(receiver)
                || receiver != IntPtr.Zero && _rendererTransforms.ContainsKey(receiver) && !GeometryComponentTransforms.ContainsKey(receiver))
                return RendererInvoke(method, receiver, arguments, exception);
            var result = GeometryInvoke(method, receiver, arguments, exception);
            if (GeometryTransforms.TryGetValue(receiver, out var trs) && _rendererTrs.ContainsKey(receiver))
                _rendererTrs[receiver] = new(trs.Parent, trs.Position, trs.Rotation, trs.Scale);
            return result;
        }
        if (method == _fingerSet && !_fingerArrays.ContainsKey(receiver))
        {
            if (_rendererArrays.ContainsKey(receiver)) return RendererInvoke(_rendererInverseMethods["SetValue"], receiver, arguments, exception);
            return JudgeQueueInvoke(_judgeQueueSetValue, receiver, arguments, exception);
        }
        if (_gameplayMethods.TryGetValue(method, out var metadata) && metadata.Class == ClassOf(_fingerListType)
            || method == _fingerRank || method == _fingerLength || method == _fingerGet || method == _fingerSet)
            return FingerInvoke(method, receiver, arguments, exception);
        if (method == _judgeQueueClear || method == _judgeQueueAdd || method == _judgeQueueSetValue)
            return TargetHoldLists.Contains(receiver) || _holdStateArrays.ContainsKey(receiver)
                ? HoldStateInvoke(method, receiver, arguments, exception) : JudgeQueueInvoke(method, receiver, arguments, exception);
        if (_gameplayMethods.TryGetValue(method, out metadata))
        {
            if (metadata.Class == ClassOf(SeekType("UnityEngine.AudioClip")) && metadata.Name == "get_length"
                || metadata.Class == ClassOf(SeekType("UnityEngine.AudioSource")) && metadata.Name is "get_time" or "get_pitch" or "get_isPlaying"
                || metadata.Class == ClassOf(SeekType("Pigeon.MUGToolkit.Core.MUGMusicPlayer")) && metadata.Name == "GetTime")
                return GameplayInvoke(method, receiver, arguments, exception);
            if (metadata.Name == "get_clip") { ++Invocations; Marshal.WriteIntPtr(exception, IntPtr.Zero); return GameplayClip; }
            if (metadata.Name == "SetTime")
            {
                ++Invocations; AllGameplayReceiversRooted &= receiver == GameplayPlayer && HasRootFor(receiver);
                GameplayPosition = Marshal.PtrToStructure<float>(Marshal.ReadIntPtr(arguments));
                GameStateTimeStores.Add((receiver, BitConverter.SingleToInt32Bits(GameplayPosition)));
                GameStateStages.Add("SetTime"); Marshal.WriteIntPtr(exception, IntPtr.Zero); return IntPtr.Zero;
            }
        }
        return LineRingSourceInvoke(method, receiver, arguments, exception);
    }

    private sealed class GameStateVisibility : ISeekNoteVisibilityNativeProfile
    {
        private readonly FakeNative _n;
        internal GameStateVisibility(FakeNative native) => _n = native;
        public ISeekRendererNativeProfile Renderer => _n.RendererProfile;
        public ISeekHoldGeometryProfile Geometry => _n.GeometryProfile;
        public IntPtr NoteControlClass => _n.ClassOf(_n._cursorType);
        public IntPtr ActiveNotesMethod => _n._gameStateActive;
        public IntPtr SetNotesMethod => _n._gameStateSetNotes;
        public void VerifyCurrent() { Renderer.VerifyCurrent(); Geometry.VerifyCurrent(); }
    }
}
