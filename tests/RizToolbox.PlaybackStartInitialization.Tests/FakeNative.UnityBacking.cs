using System.Runtime.InteropServices;
using RizToolbox.Runtime;

// Explicit owned-unity-backing-lifetime-v1 mode. Historical ConfigureRootedStock
// retains its original source+16 pitch / clip+16 payload semantics.
sealed partial class FakeNative
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr UnityCreateClass(uint staticBytes, int firstStatic);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr UnityCreateClip(float length, int instanceId, ulong tag);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int UnityMutation(IntPtr value, uint kind, uint operation, int instanceId);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int UnityStorage(IntPtr value, uint kind);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong UnityRead(uint field);
    private readonly Dictionary<IntPtr, IntPtr> _unityScalarClasses = new();
    private readonly Dictionary<IntPtr, IntPtr> _unityParents = new();
    private readonly List<(IntPtr Pointer, uint Kind)> _unityOwned = new();
    private IntPtr _unityPointerType, _unityOffsetField, _unityCachedField;
    internal IntPtr UnityObjectClass, UnityStaticFields, UnityInitialStart;
    internal int UnityCachedReads, UnityStaticReads;
    internal uint UnityCachedFlags, UnityOffsetFlags = 0x10;
    internal IntPtr UnityCachedParentOverride, UnityOffsetParentOverride;
    internal Action<IntPtr, int>? BeforeUnityCachedRead, AfterUnityCachedRead;
    internal Action<int>? BeforeUnityStaticRead, AfterUnityStaticRead;
    internal bool UnityBackingConfigured { get; private set; }
    internal IntPtr UnityCachedField => _unityCachedField;
    internal IntPtr UnityOffsetField => _unityOffsetField;
    internal int UnitySourceField(int historicalOffset) => checked(historicalOffset + 8);
    internal ulong UnityFixtureRead(uint field) => UnityExport<UnityRead>("StockFixtureUnityRead")(field);
    internal ulong UnityGetTimeOriginalCalls => unchecked((ulong)Marshal.ReadInt64(GameplaySource, checked((int)UnityFixtureRead(12))));
    internal IntPtr UnityLastGetTimeMethod => Marshal.ReadIntPtr(GameplaySource, checked((int)UnityFixtureRead(13)));
    private T UnityExport<T>(string name) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_stockModule, name));
    private IntPtr UnityType(string name) => _types.Single(pair => pair.Value.Name == name).Key;
    private IntPtr UnityClass(IntPtr type) => _unityScalarClasses.GetValueOrDefault(type, ClassOf(type));

    internal IntPtr ConfigureUnityBacking(IntPtr module)
    {
        if (UnityBackingConfigured) throw new InvalidOperationException("A Unity-backed fixture can be configured only once.");
        var marker = NativeLibrary.GetExport(module, "StockUnityBackingProfileMarker");
        if (Marshal.PtrToStringAnsi(marker) != "owned-unity-backing-lifetime-v1")
            throw new InvalidOperationException("This fixture has no explicit Unity wrapper profile.");
        UnityInitialStart = ConfigureInitializer(module);
        ConfigureSharedConstructor(module); ConfigureDirectAudio(module);
        UnityTrack(GameplaySource, 1, registered: true);
        UnityTrack(GameplayPlayer, 4, registered: true);
        UnityTrack(GameplayProgress, 5, registered: true);
        UnityTrack(UnityInitialStart, 6);
        _unityPointerType = AddType("System.IntPtr", 24, true, 8);
        RemapUnityReference("UnityEngine.Object", 4, 8);
        RemapUnityReference("UnityEngine.AudioSource", 0, 0);
        RemapUnityReference("UnityEngine.AudioClip", 0, 0);
        foreach (var type in new[] { Int32, _unityPointerType })
        {
            var cls = UnityExport<UnityCreateClass>("StockFixtureUnityCreateClass")(0, 0);
            if (cls == IntPtr.Zero) throw new OutOfMemoryException("Owned scalar class allocation failed.");
            _unityOwned.Add((cls, 3)); _unityScalarClasses.Add(type, cls); _classes[cls] = type;
        }
        UnityObjectClass = ClassOf(UnityType("UnityEngine.Object"));
        UnityStaticFields = Marshal.ReadIntPtr(UnityObjectClass, 0xb8);
        ConfigureUnityHostAncestry();
        _unityCachedField = UnityAddField(UnityObjectClass, "m_CachedPtr", _unityPointerType, 16);
        _unityOffsetField = UnityAddField(UnityObjectClass, "OffsetOfInstanceIDInCPlusPlusObject", Int32, 0);
        // Existing setup never claims its old source storage was a wrapper.
        // Repair audio payloads using the new profile's explicit native layout.
        Marshal.WriteInt32(GameplaySource, 24, BitConverter.SingleToInt32Bits(1));
        Marshal.WriteInt32(GameplaySource, 28, 0);
        Marshal.WriteInt64(GameplaySource, 32, BitConverter.DoubleToInt64Bits(3));
        Marshal.WriteInt64(GameplaySource, 40, 0);
        ConfigureUnityProgressResource();
        Marshal.WriteIntPtr(UnityInitialStart, 0x38, GameplayClip);
        foreach (var obj in new[] { GameplayController, GameplayInformation, GameplayProgress, GameplayPause, GameplayPlayer, GameplaySource, GameplayClip })
            Marshal.WriteIntPtr(obj, UnityClass(_boxedTypes[obj]));
        foreach (var export in new[] { "GetTime", "Pause", "Replay", "Resume", "ProgressPlay", "PauseLeaf", "SetPitch", "SetPitchAlias",
            "PlayScheduled", "SetTime", "SetClip", "ProgressMusicPlayer", "PlayerConstructor", "StartMoveNext", "AudioPlayHelper", "AudioPlayDelayed",
            "AudioPlay", "AudioStop", "AudioStopBoolean", "AudioSetLoop", "ProgressClip", "ResourceGet" })
        {
            var method = UnityExport<RootedMethod>("StockMethod" + export)();
            if (_gameplayMethods.TryGetValue(method, out var row)) Marshal.WriteIntPtr(method, 32, row.Class);
            else if (export != "StartMoveNext") throw new InvalidOperationException("Missing exact native fixture method metadata: " + export);
        }
        if (!_gameplayImages.Values.Contains("mscorlib.dll")) _gameplayImages[GameplayAllocate(8)] = "mscorlib.dll";
        _gameplayAssemblies = GameplayAllocate(checked(_gameplayImages.Count * 8));
        var index = 0;
        foreach (var image in _gameplayImages.Keys) Marshal.WriteIntPtr(_gameplayAssemblies, index++ * 8, image);
        Add("il2cpp_domain_get_assemblies", "Ptr2", nameof(UnityAssemblies));
        Add("il2cpp_class_from_name", "ClassLookup", nameof(UnityFindClass));
        Add("il2cpp_class_from_type", "Ptr1", nameof(UnityClass));
        Add("il2cpp_object_get_class", "Ptr1", nameof(UnityObjectClassOf));
        Add("il2cpp_class_get_name", "Ptr1", nameof(ProgressResourceClassName));
        Add("il2cpp_class_get_namespace", "Ptr1", nameof(ProgressResourceClassNamespace));
        Add("il2cpp_class_get_parent", "Ptr1", nameof(UnityParent));
        Add("il2cpp_class_get_image", "Ptr1", nameof(UnityClassImage));
        Add("il2cpp_class_get_static_field_data", "Ptr1", nameof(UnityClassStaticData));
        Add("il2cpp_class_instance_size", "ArraySize", nameof(UnityClassInstanceSize));
        Add("il2cpp_class_is_assignable_from", "Assignable", nameof(UnityAssignable));
        Add("il2cpp_class_is_generic", "Predicate", nameof(UnityIsGeneric));
        Add("il2cpp_class_is_inflated", "Predicate", nameof(UnityIsInflated));
        Add("il2cpp_field_get_parent", "Ptr1", nameof(UnityFieldParent));
        Add("il2cpp_field_get_flags", "Flags", nameof(UnityFieldFlags));
        Add("il2cpp_field_get_value", "FieldGet", nameof(UnityReadField));
        Add("il2cpp_field_static_get_value", "StaticGet", nameof(UnityReadStatic));
        Add("il2cpp_runtime_invoke", "RuntimeInvoke", nameof(UnityInvoke));
        GameplayApi = new NativeRuntime(ExportPointer); GameplayApi.BindCurrentThread();
        UnityBackingConfigured = true;
        return UnityInitialStart;
    }

    private void RemapUnityReference(string name, uint staticBytes, int firstStatic)
    {
        var oldType = UnityType(name); var oldClass = ClassOf(oldType);
        var cls = UnityExport<UnityCreateClass>("StockFixtureUnityCreateClass")(staticBytes, firstStatic);
        if (cls == IntPtr.Zero) throw new OutOfMemoryException("Owned Unity class allocation failed.");
        _unityOwned.Add((cls, 3)); var type = new IntPtr(checked(cls.ToInt64() - 1000));
        _types[type] = _types[oldType]; _types.Remove(oldType);
        _classes.Remove(oldClass); _classes[cls] = type;
        foreach (var obj in _boxedTypes.Where(pair => pair.Value == oldType).Select(pair => pair.Key).ToArray()) _boxedTypes[obj] = type;
        foreach (var field in _fieldMetadata.Keys.ToArray())
        {
            var row = _fieldMetadata[field];
            if (row.Type == oldType) _fieldMetadata[field] = (row.Name, type, row.Offset);
        }
        foreach (var field in _gameplayFieldParents.Keys.ToArray())
            if (_gameplayFieldParents[field] == oldClass) _gameplayFieldParents[field] = cls;
        if (_structFields.Remove(oldClass, out var fields)) _structFields[cls] = fields;
        foreach (var key in _gameplayMethods.Keys.ToArray())
        {
            var row = _gameplayMethods[key];
            _gameplayMethods[key] = row with {
                Class = row.Class == oldClass ? cls : row.Class,
                Return = row.Return == oldType ? type : row.Return,
                Parameters = row.Parameters.Select(parameter => parameter == oldType ? type : parameter).ToArray()
            };
        }
    }
    private IntPtr UnityAddField(IntPtr cls, string name, IntPtr type, int offset)
    {
        var field = new IntPtr(20000 + _fieldMetadata.Count);
        _fieldMetadata[field] = (name, type, offset); _gameplayFieldParents[field] = cls;
        if (!_structFields.TryGetValue(cls, out var fields)) _structFields[cls] = fields = new();
        fields.Add(field); return field;
    }
    private void ConfigureUnityHostAncestry()
    {
        // The owned graph's four scene hosts are MonoBehaviour instances.
        // Declare their actual fake metadata chain after Object remapping;
        // unrelated managed objects remain outside Unity Object ancestry.
        var component = ClassOf(AddType("UnityEngine.Component", 18, false, 8));
        var behaviour = ClassOf(AddType("UnityEngine.Behaviour", 18, false, 8));
        var mono = ClassOf(AddType("UnityEngine.MonoBehaviour", 18, false, 8));
        _unityParents[component] = UnityObjectClass;
        _unityParents[behaviour] = component;
        _unityParents[mono] = behaviour;
        foreach (var name in new[] { "NormalModeInLevelController", "InLevel.LevelInformation", "InLevel.ProgressControl", "PauseUIControl" })
            _unityParents[ClassOf(UnityType(name))] = mono;
    }
    private void ConfigureUnityProgressResource()
    {
        var progressType = UnityType("InLevel.ProgressControl"); var clipType = UnityType("UnityEngine.AudioClip");
        _progressResourceType = AddType("IResourceReference<UnityEngine.AudioClip>", 21, false, IntPtr.Size);
        _progressResourceMethod = UnityExport<RootedMethod>("StockMethodProgressClip")();
        _progressResourceGetMethod = UnityExport<RootedMethod>("StockMethodResourceGet")();
        _gameplayMethods[_progressResourceMethod] = new(ClassOf(progressType), "set_Clip", false, Void, new[] { _progressResourceType });
        _gameplayMethods[_progressResourceGetMethod] = new(ClassOf(_progressResourceType), "get_Resource", false, clipType, Array.Empty<IntPtr>());
        ProgressResourceClipA = CreateUnityClip(120, -4041, 0x41);
        ProgressResourceClipB = CreateUnityClip(120, 4042, 0x42);
        GameplayClip = ProgressResourceClipA; Marshal.WriteIntPtr(GameplaySource, 48, GameplayClip);
        // ConfigureInitializer made one real reference. Adopt it and update the
        // actual +0x18 store; subsequent Start reloads the actual +0x30 field.
        ProgressResourceA = Marshal.ReadIntPtr(GameplayProgress, 0x30);
        if (ProgressResourceA == IntPtr.Zero) ProgressResourceA = CreateProgressResourceReference(GameplayClip);
        else { RegisterProgressResourceReference(ProgressResourceA); Marshal.WriteIntPtr(ProgressResourceA, 0x18, GameplayClip); }
        UnityTrack(ProgressResourceA, 7);
        ProgressResourceB = CreateProgressResourceReference(ProgressResourceClipB); UnityTrack(ProgressResourceB, 7);
        Marshal.WriteIntPtr(GameplayProgress, 0x30, ProgressResourceA);
        GameplayField("progress.resource", progressType, "<Clip>k__BackingField", _progressResourceType, GameplayProgress, ProgressResourceA);
        _progressResourceField = _gameplayFields["progress.resource"].Field;
        _fieldMetadata[_progressResourceField] = ("<Clip>k__BackingField", _progressResourceType, 0x30);
    }
    internal IntPtr CreateUnityClip(float length, int instanceId, ulong tag)
    {
        var value = UnityExport<UnityCreateClip>("StockFixtureUnityCreateClip")(length, instanceId, tag);
        if (value == IntPtr.Zero) throw new OutOfMemoryException("Owned Unity clip allocation failed.");
        UnityTrack(value, 2, registered: true); _boxedTypes[value] = UnityType("UnityEngine.AudioClip");
        Marshal.WriteIntPtr(value, ClassOf(_boxedTypes[value])); return value;
    }
    internal bool MutateUnitySource(uint operation, int instanceId)
        => UnityExport<UnityMutation>("StockFixtureUnityMutate")(GameplaySource, 1, operation, instanceId) == 1;
    internal bool MutateUnityClip(uint operation, int instanceId)
        => UnityExport<UnityMutation>("StockFixtureUnityMutate")(GameplayClip, 2, operation, instanceId) == 1;
    internal void SetUnityInstanceIdOffset(int value) => Marshal.WriteInt32(UnityStaticFields, value);
    internal void SetUnityBackingMetadata(bool cached, string? name = null, int? offset = null, IntPtr? type = null)
    {
        var field = cached ? _unityCachedField : _unityOffsetField; var row = _fieldMetadata[field];
        _fieldMetadata[field] = (name ?? row.Name, type ?? row.Type, offset ?? row.Offset);
    }
    internal void RegisterUnityEnumerator(IntPtr value)
    { RegisterProgressResourceEnumerator(value); UnityTrack(value, 6); }
    internal void AdoptUnityInitializedPlayer()
    { AdoptActuallyInitializedPlayer(); UnityTrack(GameplayPlayer, 4); }
    internal void AdoptUnityClip(IntPtr clip)
    {
        if (Marshal.ReadIntPtr(GameplaySource, 48) != clip || !_boxedTypes.TryGetValue(clip, out var type)
            || type != UnityType("UnityEngine.AudioClip") || Marshal.ReadIntPtr(clip) != ClassOf(type))
            throw new InvalidOperationException("Actual Unity set_clip did not install the expected exact wrapper.");
        GameplayClip = clip;
    }
    private void UnityTrack(IntPtr value, uint kind, bool registered = false)
    {
        if (value == IntPtr.Zero || _unityOwned.Any(row => row.Pointer == value)) return;
        if (!registered && UnityExport<UnityStorage>("StockFixtureUnityRegisterStorage")(value, kind) != 1)
            throw new InvalidOperationException("Owned Unity storage registration failed.");
        _unityOwned.Add((value, kind));
    }
    internal void ReleaseUnityBackingStorage()
    {
        if (ActiveRoots != 0) throw new InvalidOperationException("Unity storage must outlive every actual NativeRuntime root.");
        foreach (var row in _unityOwned.Where(row => row.Kind != 3).Reverse().Concat(_unityOwned.Where(row => row.Kind == 3).Reverse()))
            if (UnityExport<UnityStorage>("StockFixtureUnityDestroy")(row.Pointer, row.Kind) != 1)
                throw new InvalidOperationException("Owned Unity storage was not destroyed exactly once.");
        _unityOwned.Clear();
    }
    private IntPtr UnityAssemblies(IntPtr _, IntPtr count)
    { Marshal.WriteIntPtr(count, new IntPtr(_gameplayImages.Count)); return _gameplayAssemblies; }
    private IntPtr UnityFindClass(IntPtr image, string ns, string name)
    {
        var type = _types.FirstOrDefault(pair => pair.Value.Name == (ns.Length == 0 ? name : ns + "." + name)).Key;
        if (type == IntPtr.Zero) return IntPtr.Zero;
        var cls = UnityClass(type); return UnityClassImage(cls) == image ? cls : IntPtr.Zero;
    }
    private IntPtr UnityObjectClassOf(IntPtr value)
    {
        if (!_boxedTypes.TryGetValue(value, out var type)) return ObjectClass(value);
        return _types[type].Name is "UnityEngine.AudioSource" or "UnityEngine.AudioClip"
            ? Marshal.ReadIntPtr(value) : UnityClass(type);
    }
    private IntPtr UnityParent(IntPtr cls)
    {
        if (cls == UnityObjectClass) return ClassOf(Object);
        if (_unityParents.TryGetValue(cls, out var parent)) return parent;
        var name = GameplayTypeName(cls);
        if (name is "UnityEngine.AudioSource" or "UnityEngine.AudioClip") return UnityObjectClass;
        return ClassParent(cls);
    }
    private IntPtr UnityClassImage(IntPtr cls)
    {
        var name = GameplayTypeName(cls);
        var image = name.StartsWith("System.", StringComparison.Ordinal) ? "mscorlib.dll"
            : name is "UnityEngine.AudioSource" or "UnityEngine.AudioClip" or "UnityEngine.AudioSettings" ? "UnityEngine.AudioModule.dll"
            : name.StartsWith("UnityEngine.", StringComparison.Ordinal) ? "UnityEngine.CoreModule.dll" : "Assembly-CSharp.dll";
        return _gameplayImages.FirstOrDefault(pair => pair.Value == image).Key;
    }
    private IntPtr UnityClassStaticData(IntPtr cls)
    {
        if (cls != UnityObjectClass) return IntPtr.Zero;
        ++UnityStaticReads; BeforeUnityStaticRead?.Invoke(UnityStaticReads);
        var value = Marshal.ReadIntPtr(cls, 0xb8);
        AfterUnityStaticRead?.Invoke(UnityStaticReads); return value;
    }
    private uint UnityClassInstanceSize(IntPtr cls)
        => GameplayTypeName(cls) switch {
            "UnityEngine.Object" => 0x18,
            "UnityEngine.AudioSource" => checked((uint)UnityFixtureRead(4)),
            "UnityEngine.AudioClip" => checked((uint)UnityFixtureRead(5)),
            _ => ProgressResourceClassInstanceSize(cls)
        };
    private bool UnityAssignable(IntPtr target, IntPtr source)
    {
        if (target == ClassOf(Object)) return true;
        for (var depth = 0; source != IntPtr.Zero && depth < 32; ++depth, source = UnityParent(source))
            if (source == target) return true;
        return false;
    }
    private bool UnityIsGeneric(IntPtr _) => false;
    private bool UnityIsInflated(IntPtr cls) => cls == ClassOf(_progressResourceType);
    private IntPtr UnityFieldParent(IntPtr field)
        => field == _unityCachedField && UnityCachedParentOverride != IntPtr.Zero ? UnityCachedParentOverride
            : field == _unityOffsetField && UnityOffsetParentOverride != IntPtr.Zero ? UnityOffsetParentOverride : GameplayFieldParent(field);
    private uint UnityFieldFlags(IntPtr field)
        => field == _unityCachedField ? UnityCachedFlags : field == _unityOffsetField ? UnityOffsetFlags : FieldFlags(field);
    private void UnityReadField(IntPtr owner, IntPtr field, IntPtr output)
    {
        if (field == _unityCachedField)
        {
            ++FieldReads; AllGameplayReceiversRooted &= HasRootFor(owner); ++UnityCachedReads;
            BeforeUnityCachedRead?.Invoke(owner, UnityCachedReads);
            Marshal.WriteIntPtr(output, Marshal.ReadIntPtr(owner, 16));
            AfterUnityCachedRead?.Invoke(owner, UnityCachedReads); return;
        }
        ProgressResourceReadField(owner, field, output);
    }
    private void UnityReadStatic(IntPtr field, IntPtr output)
    {
        if (field != _unityOffsetField) { ReadStatic(field, output); return; }
        ++FieldReads; ++UnityStaticReads; BeforeUnityStaticRead?.Invoke(UnityStaticReads);
        // Read through the actual Object class staticFields pointer each time.
        var statics = Marshal.ReadIntPtr(UnityObjectClass, 0xb8);
        Marshal.WriteInt32(output, Marshal.ReadInt32(statics, _fieldMetadata[field].Offset));
        AfterUnityStaticRead?.Invoke(UnityStaticReads);
    }
    private IntPtr UnityInvoke(IntPtr method, IntPtr instance, IntPtr arguments, IntPtr exception)
    {
        if (!_gameplayMethods.TryGetValue(method, out var row)) return DirectAudioInvoke(method, instance, arguments, exception);
        Marshal.WriteIntPtr(exception, IntPtr.Zero); AllGameplayReceiversRooted &= instance == IntPtr.Zero || HasRootFor(instance);
        switch (row.Name)
        {
            case "get_DspTime":
                ++RootedDspReads; var dsp = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(GameplayPlayer, 48));
                DuringRootedDspRead?.Invoke(); return Box(_gameplayDouble, BitConverter.GetBytes(dsp));
            case "get_pitch":
                ++RootedPitchReads; var pitch = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(instance, 24));
                DuringRootedPitchRead?.Invoke(); return Box(Single, BitConverter.GetBytes(WrongRootedPitchReadback ? 1 : pitch));
            case "get_time": return Box(Single, BitConverter.GetBytes((float)BitConverter.Int64BitsToDouble(Marshal.ReadInt64(instance, 32))));
            case "get_clip": return Marshal.ReadIntPtr(instance, 48);
            case "get_isPlaying" when instance == GameplaySource: return Box(Boolean, new[] { Marshal.ReadInt32(instance, 56) != 0 ? (byte)1 : (byte)0 });
            case "get_isActiveAndEnabled" when instance == GameplaySource: return Box(Boolean, new[] { Marshal.ReadInt32(instance, 60) != 0 ? (byte)1 : (byte)0 });
            case "get_length": return Box(Single, BitConverter.GetBytes(BitConverter.Int32BitsToSingle(Marshal.ReadInt32(instance, 24))));
            case "op_Implicit":
                var value = Marshal.ReadIntPtr(arguments);
                var unity = _boxedTypes.TryGetValue(value, out var type) && _types[type].Name is "UnityEngine.AudioSource" or "UnityEngine.AudioClip";
                return Box(Boolean, new[] { value != IntPtr.Zero && (!unity || Marshal.ReadIntPtr(value, 16) != IntPtr.Zero) ? (byte)1 : (byte)0 });
            default: return DirectAudioInvoke(method, instance, arguments, exception);
        }
    }
}
