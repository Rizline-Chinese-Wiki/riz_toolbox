using System.Runtime.InteropServices;
using RizToolbox.Adapter;
using RizToolbox.Runtime;

// Reuses the repository's self-owned native-export fixture. Every buffer/delegate
// belongs to this process; no game file, native patch library or GUI is loaded.
sealed partial class FakeNative
{
    internal sealed class AppearanceMethod
    {
        internal IntPtr Pointer, Class, Return; internal string Name = ""; internal IntPtr[] Parameters = Array.Empty<IntPtr>();
        internal bool Static;
    }
    private readonly Dictionary<IntPtr, AppearanceMethod> _appearanceMethods = new();
    private readonly Dictionary<IntPtr, IntPtr> _appearanceFieldParents = new();
    private readonly Dictionary<(IntPtr Owner, IntPtr Field), byte[]> _appearanceStorage = new();
    private readonly Dictionary<IntPtr, string> _appearanceImages = new();
    private IntPtr _appearanceAssemblies;
    internal NativeRuntime AppearanceApi = null!;
    internal IntPtr AppearanceLineClass, AppearanceRendererClass, AppearanceSpriteClass, AppearanceColorType;
    internal IntPtr AppearanceLine, AppearanceRenderer, AppearanceSprite, AppearanceCacheField;
    internal Rgba AppearanceRendered = new(.2f, .4f, .6f, 1), AppearanceCached = new(.1f, .1f, .1f, 1);
    internal Rgba AppearanceEndRendered = new(.2f, .4f, .6f, 1);
    internal Rgba AppearanceSpriteColor = new(.5f, .6f, .7f, 1);
    internal IntPtr AppearanceGroupClass, AppearanceGroup, AppearanceRingRenderer, AppearanceRingCacheField;
    internal Rgba AppearanceRingCached = new(.3f, .4f, .5f, 1), AppearanceRingRendered = new(.3f, .4f, .5f, 1);
    internal bool AppearanceRingIgnoreCacheWrites, AppearanceRingIgnoreRendererWrites;
    internal bool AllAppearanceReceiversRooted = true, AppearanceIgnoreWrites;
    internal int AppearanceGetters, AppearanceSetters;
    internal IntPtr AppearanceInformation, AppearanceChart, AppearanceProgress, AppearanceControllers;
    internal IntPtr AppearanceGameObject, AppearanceUnityObjectClass, AppearanceSceneClass, AppearanceSceneManagerClass;
    internal IntPtr AppearanceResourcesClass, AppearanceRunField;
    internal bool AppearanceControllerActive = true, AppearanceControllerEnabled = true;
    internal bool AppearanceSceneLoaded = true, AppearanceSceneValid = true;
    internal bool AppearanceActiveSceneLoaded = true, AppearanceActiveSceneValid = true;
    internal int AppearanceOwnSceneHandle = -12, AppearanceActiveSceneHandle = 7;
    internal string AppearanceOwnSceneName = "DontDestroyOnLoad", AppearanceActiveSceneName = "Play";
    internal bool AllAppearanceSceneValuesRooted = true;
    private IntPtr _appearanceSceneValue;

    internal void ConfigureAppearance()
    {
        AppearanceColorType = AddStruct("UnityEngine.Color", 16,
            new("r", "System.Single", 0), new("g", "System.Single", 4), new("b", "System.Single", 8), new("a", "System.Single", 12));
        var lineType = AddType("InLevel.LineInformation", 18, false, 8);
        var rendererType = AddType("UnityEngine.LineRenderer", 18, false, 8);
        var spriteType = AddType("UnityEngine.SpriteRenderer", 18, false, 8);
        AppearanceLineClass = ClassOf(lineType); AppearanceRendererClass = ClassOf(rendererType); AppearanceSpriteClass = ClassOf(spriteType);
        AppearanceLine = Box(lineType, new byte[0xD0]); AppearanceRenderer = Box(rendererType, new byte[0x40]); AppearanceSprite = Box(spriteType, new byte[0x40]);
        AppearanceCacheField = AddAppearanceField(AppearanceLineClass, "_startColorPlan", AppearanceColorType, 0x68);
        AddAppearanceField(AppearanceLineClass, "_endColorPlan", AppearanceColorType, 0x78);
        foreach (var field in _fieldMetadata.Keys) _appearanceFieldParents.TryAdd(field, ClassOf(AppearanceColorType));
        AddAppearanceMethod(AppearanceRendererClass, "get_startColor", AppearanceColorType);
        AddAppearanceMethod(AppearanceRendererClass, "get_endColor", AppearanceColorType);
        AddAppearanceMethod(AppearanceLineClass, "set_startColor", Void, AppearanceColorType);
        AddAppearanceMethod(AppearanceLineClass, "set_endColor", Void, AppearanceColorType);
        AddAppearanceMethod(AppearanceSpriteClass, "get_color", AppearanceColorType);
        AddAppearanceMethod(AppearanceSpriteClass, "set_color", Void, AppearanceColorType);
        var game = AllocateAppearance(8); var unity = AllocateAppearance(8); var system = AllocateAppearance(8);
        _appearanceImages.Add(game, "Assembly-CSharp.dll"); _appearanceImages.Add(unity, "UnityEngine.CoreModule.dll"); _appearanceImages.Add(system, "mscorlib.dll");
        _appearanceAssemblies = AllocateAppearance(24); Marshal.WriteIntPtr(_appearanceAssemblies, game);
        Marshal.WriteIntPtr(_appearanceAssemblies, 8, unity); Marshal.WriteIntPtr(_appearanceAssemblies, 16, system);
        Add("il2cpp_domain_get_assemblies", "Ptr2", nameof(AppearanceAssemblies));
        Add("il2cpp_assembly_get_image", "Ptr1", nameof(AppearanceIdentity)); Add("il2cpp_image_get_name", "Ptr1", nameof(AppearanceImageName));
        Add("il2cpp_class_from_name", "ClassLookup", nameof(AppearanceFindClass));
        Add("il2cpp_class_get_name", "Ptr1", nameof(AppearanceClassName)); Add("il2cpp_class_get_namespace", "Ptr1", nameof(AppearanceNamespace));
        Add("il2cpp_class_get_methods", "Ptr2", nameof(AppearanceMethods)); Add("il2cpp_method_get_name", "Ptr1", nameof(AppearanceMethodName));
        Add("il2cpp_method_get_class", "Ptr1", nameof(AppearanceMethodClass)); Add("il2cpp_method_get_param_count", "Flags", nameof(AppearanceParameterCount));
        Add("il2cpp_method_get_param", "ParameterType", nameof(AppearanceParameter)); Add("il2cpp_method_get_return_type", "Ptr1", nameof(AppearanceReturn));
        Add("il2cpp_class_get_field_from_name", "FieldLookup", nameof(AppearanceFindField));
        Add("il2cpp_field_get_parent", "Ptr1", nameof(AppearanceFieldParent)); Add("il2cpp_field_get_type", "Ptr1", nameof(AppearanceFieldType));
        Add("il2cpp_field_get_value", "FieldGet", nameof(AppearanceReadField));
        Add("il2cpp_class_instance_size", "ArraySize", nameof(AppearanceInstanceSize));
        Add("il2cpp_runtime_invoke", "RuntimeInvoke", nameof(AppearanceInvoke));
        AppearanceApi = new NativeRuntime(ExportPointer); AppearanceApi.BindCurrentThread();
    }
    internal void ConfigureAppearanceRing()
    {
        var groupType = AddType("InLevel.LinesGroupInformation", 18, false, 8);
        AppearanceGroupClass = ClassOf(groupType); AppearanceGroup = Box(groupType, new byte[0xA0]);
        AppearanceRingRenderer = Box(_classes[AppearanceSpriteClass], new byte[0x40]);
        AppearanceRingCacheField = AddAppearanceField(AppearanceGroupClass, "_startColorPlan", AppearanceColorType, 0x88);
        AddAppearanceMethod(AppearanceGroupClass, "set_color", Void, AppearanceColorType);
    }
    private IntPtr AllocateAppearance(int count) { var value = Marshal.AllocHGlobal(count); _allocations.Add(value); return value; }
    private IntPtr AppearanceText(string text) { var pointer = Marshal.StringToHGlobalAnsi(text); _allocations.Add(pointer); return pointer; }
    private IntPtr AddAppearanceField(IntPtr cls, string name, IntPtr type, int offset)
    {
        var pointer = new IntPtr(10000 + _fieldMetadata.Count); _fieldMetadata.Add(pointer, (name, type, offset));
        if (!_structFields.TryGetValue(cls, out var list)) _structFields.Add(cls, list = new()); list.Add(pointer);
        _appearanceFieldParents.Add(pointer, cls); return pointer;
    }
    private void AddAppearanceMethod(IntPtr cls, string name, IntPtr returns, params IntPtr[] parameters)
    {
        var pointer = new IntPtr(70000 + _appearanceMethods.Count);
        _appearanceMethods.Add(pointer, new() { Pointer = pointer, Class = cls, Name = name, Return = returns, Parameters = parameters });
    }
    internal AppearanceMethod AppearanceMethodByName(string name) => _appearanceMethods.Values.Single(x => x.Name == name);
    internal void ConfigureAppearanceGraph()
    {
        // The graph tests construct only process-owned objects. Production's PE gate
        // remains mandatory and is never invoked or replaced by a test switch.
        var gameObject = AddType("UnityEngine.GameObject", 18, false, 8);
        var unityObject = AddType("UnityEngine.Object", 18, false, 8);
        var scene = AddStruct("UnityEngine.SceneManagement.Scene", 4,
            new NativeFieldLayout("m_Handle", "System.Int32", 0));
        var sceneManager = AddType("UnityEngine.SceneManagement.SceneManager", 18, false, 8);
        var resources = AddType("UnityEngine.Resources", 18, false, 8);
        var systemType = AddType("System.Type", 18, false, 8);
        AppearanceUnityObjectClass = ClassOf(unityObject); AppearanceSceneClass = ClassOf(scene);
        AppearanceSceneManagerClass = ClassOf(sceneManager); AppearanceResourcesClass = ClassOf(resources);
        foreach (var field in _structFields[AppearanceSceneClass]) _appearanceFieldParents[field] = AppearanceSceneClass;
        AppearanceInformation = Box(_classes[AppearanceLineClass], new byte[0xD0]);
        AppearanceChart = Box(_classes[AppearanceLineClass], new byte[0xD0]);
        AppearanceProgress = Box(_classes[AppearanceLineClass], new byte[0xD0]);
        AppearanceGameObject = Box(gameObject, new byte[0x40]);
        AppearanceRunField = AddAppearanceField(AppearanceLineClass, "LevelInformation", _classes[AppearanceLineClass], 0x28);
        SetAppearanceInformation(AppearanceInformation);
        SetAppearanceGraphReference("chart", AppearanceChart, 0x88);
        SetAppearanceGraphReference("progressControl", AppearanceProgress, 0x28);
        SetAppearanceGraphFlag("loadOver", AppearanceInformation, 0xBE, true);
        SetAppearanceGraphFlag("levelOver", AppearanceInformation, 0xC0, false);
        SetAppearanceGraphFlag("crashed", AppearanceInformation, 0xC1, false);
        SetAppearanceGraphFlag("timeReady", AppearanceProgress, 0x65, true);
        _referenceArrayType = AddType("UnityEngine.Object[]", 29, false, IntPtr.Size);
        AppearanceControllers = ReferenceArray(1); _referenceArrays[AppearanceControllers] = new[] { AppearanceLine };
        AddAppearanceStatic(AppearanceResourcesClass, "FindObjectsOfTypeAll", _referenceArrayType, systemType);
        AddAppearanceMethod(AppearanceLineClass, "get_gameObject", gameObject);
        AddAppearanceMethod(AppearanceLineClass, "get_isActiveAndEnabled", Boolean);
        AddAppearanceMethod(ClassOf(gameObject), "get_activeInHierarchy", Boolean);
        AddAppearanceMethod(ClassOf(gameObject), "get_scene", scene);
        AddAppearanceStatic(AppearanceSceneManagerClass, "GetActiveScene", scene);
        AddAppearanceStatic(AppearanceUnityObjectClass, "op_Implicit", Boolean, Object);
        AddAppearanceStatic(AppearanceSceneClass, "IsValidInternal", Boolean, Int32);
        AddAppearanceStatic(AppearanceSceneClass, "GetIsLoadedInternal", Boolean, Int32);
        AddAppearanceStatic(AppearanceSceneClass, "GetNameInternal", String, Int32);
        Add("il2cpp_method_get_flags", "MethodFlags", nameof(AppearanceFlags));
        Add("il2cpp_type_get_object", "Ptr1", nameof(AppearanceTypeObject));
        // NativeRuntime caches the actual export delegates during construction.
        AppearanceApi = new NativeRuntime(ExportPointer); AppearanceApi.BindCurrentThread();
    }
    internal void SetAppearanceInformation(IntPtr information)
        => _appearanceStorage[(AppearanceLine, AppearanceRunField)] = BitConverter.GetBytes(information.ToInt64());
    private void SetAppearanceGraphReference(string name, IntPtr target, int offset)
    {
        var field = AddAppearanceField(AppearanceLineClass, name, _classes[AppearanceLineClass], offset);
        _appearanceStorage[(AppearanceInformation, field)] = BitConverter.GetBytes(target.ToInt64());
    }
    private void SetAppearanceGraphFlag(string name, IntPtr owner, int offset, bool value)
    {
        var field = AddAppearanceField(AppearanceLineClass, name, Boolean, offset);
        _appearanceStorage[(owner, field)] = new[] { value ? (byte)1 : (byte)0 };
    }
    private void AddAppearanceStatic(IntPtr cls, string name, IntPtr returns, params IntPtr[] parameters)
    { AddAppearanceMethod(cls, name, returns, parameters); AppearanceMethodByName(name).Static = true; }
    private uint AppearanceFlags(IntPtr method, out uint implementation)
    { implementation = 0; return IsArrayMethod(method) ? 0 : _appearanceMethods[method].Static ? 0x10u : 0; }
    internal void ChangeAppearanceCacheOffset(int offset)
    { var old = _fieldMetadata[AppearanceCacheField]; _fieldMetadata[AppearanceCacheField] = (old.Name, old.Type, offset); }
    private IntPtr AppearanceAssemblies(IntPtr _, IntPtr count) { Marshal.WriteIntPtr(count, new IntPtr(3)); return _appearanceAssemblies; }
    private IntPtr AppearanceIdentity(IntPtr value) => value;
    private IntPtr AppearanceImageName(IntPtr image) => AppearanceText(_appearanceImages[image]);
    private string AppearanceTypeName(IntPtr cls) => _types[_classes[cls]].Name;
    private IntPtr AppearanceClassName(IntPtr cls) => AppearanceText(AppearanceTypeName(cls).Split('.').Last());
    private IntPtr AppearanceNamespace(IntPtr cls)
    { var full = AppearanceTypeName(cls); var split = full.LastIndexOf('.'); return AppearanceText(split < 0 ? "" : full[..split]); }
    private IntPtr AppearanceFindClass(IntPtr image, string ns, string name)
        => _classes.Keys.FirstOrDefault(cls => AppearanceTypeName(cls) == (ns.Length == 0 ? name : ns + "." + name));
    private IntPtr AppearanceMethods(IntPtr cls, IntPtr iterator)
    {
        if (cls == ClassOf(_referenceArrayType)) return ClassMethods(cls, iterator);
        var rows = _appearanceMethods.Values.Where(x => x.Class == cls).ToArray(); var index = Marshal.ReadIntPtr(iterator).ToInt32();
        if (index >= rows.Length) return IntPtr.Zero; Marshal.WriteIntPtr(iterator, new IntPtr(index + 1)); return rows[index].Pointer;
    }
    private IntPtr AppearanceMethodName(IntPtr method) => IsArrayMethod(method) ? MethodName(method) : AppearanceText(_appearanceMethods[method].Name);
    private IntPtr AppearanceMethodClass(IntPtr method) => IsArrayMethod(method) ? MethodClass(method) : _appearanceMethods[method].Class;
    private uint AppearanceParameterCount(IntPtr method) => IsArrayMethod(method) ? ParameterCount(method) : (uint)_appearanceMethods[method].Parameters.Length;
    private IntPtr AppearanceParameter(IntPtr method, uint index) => IsArrayMethod(method) ? ParameterType(method, index) : _appearanceMethods[method].Parameters[index];
    private IntPtr AppearanceReturn(IntPtr method) => IsArrayMethod(method) ? GetReturnType(method) : _appearanceMethods[method].Return;
    private IntPtr AppearanceTypeObject(IntPtr _) => Box(_types.Keys.Single(type => _types[type].Name == "System.Type"), new byte[8]);
    private IntPtr AppearanceFindField(IntPtr cls, string name)
        => _structFields.TryGetValue(cls, out var fields) ? fields.FirstOrDefault(x => _fieldMetadata[x].Name == name) : IntPtr.Zero;
    private IntPtr AppearanceFieldParent(IntPtr field) => _appearanceFieldParents[field];
    private IntPtr AppearanceFieldType(IntPtr field) => _fieldMetadata[field].Type;
    private uint AppearanceInstanceSize(IntPtr _) => 0xD0;
    private static byte[] AppearanceBytes(Rgba value)
        => new[] { value.R, value.G, value.B, value.Alpha }.SelectMany(BitConverter.GetBytes).ToArray();
    private void AppearanceReadField(IntPtr owner, IntPtr field, IntPtr output)
    {
        ++FieldReads; AllAppearanceReceiversRooted &= _handles.Values.Contains(owner);
        var bytes = owner == AppearanceGroup && field == AppearanceRingCacheField ? AppearanceBytes(AppearanceRingCached)
            : _appearanceStorage.TryGetValue((owner, field), out var stored) ? stored : AppearanceBytes(AppearanceCached);
        if (bytes.Length != _types[_fieldMetadata[field].Type].Size)
            throw new InvalidOperationException("Controlled appearance field storage size differs from official metadata.");
        Marshal.Copy(bytes, 0, output, bytes.Length);
    }
    private IntPtr AppearanceInvoke(IntPtr method, IntPtr instance, IntPtr arguments, IntPtr exception)
    {
        ++Invocations; AllAppearanceReceiversRooted &= instance == IntPtr.Zero || _handles.Values.Contains(instance); Marshal.WriteIntPtr(exception, IntPtr.Zero);
        if (IsArrayMethod(method)) return InvokeReferenceArray(method, instance, arguments, exception);
        var row = _appearanceMethods[method];
        switch (row.Name)
        {
            case "FindObjectsOfTypeAll": return AppearanceControllers;
            case "op_Implicit": return Box(Boolean, new byte[] { 1 });
            case "get_gameObject": return AppearanceGameObject;
            case "get_activeInHierarchy": return Box(Boolean, new[] { AppearanceControllerActive ? (byte)1 : (byte)0 });
            case "get_isActiveAndEnabled": return Box(Boolean, new[] { AppearanceControllerEnabled ? (byte)1 : (byte)0 });
            case "get_scene": return _appearanceSceneValue = Box(_classes[AppearanceSceneClass], BitConverter.GetBytes(AppearanceOwnSceneHandle));
            case "GetActiveScene": return _appearanceSceneValue = Box(_classes[AppearanceSceneClass], BitConverter.GetBytes(AppearanceActiveSceneHandle));
            case "IsValidInternal":
                AllAppearanceSceneValuesRooted &= _handles.Values.Contains(_appearanceSceneValue);
                return Box(Boolean, new[] { AppearanceHandle(arguments) == AppearanceActiveSceneHandle
                    ? AppearanceActiveSceneValid ? (byte)1 : (byte)0 : AppearanceSceneValid ? (byte)1 : (byte)0 });
            case "GetIsLoadedInternal":
                AllAppearanceSceneValuesRooted &= _handles.Values.Contains(_appearanceSceneValue);
                return Box(Boolean, new[] { AppearanceHandle(arguments) == AppearanceActiveSceneHandle
                    ? AppearanceActiveSceneLoaded ? (byte)1 : (byte)0 : AppearanceSceneLoaded ? (byte)1 : (byte)0 });
            case "GetNameInternal":
                AllAppearanceSceneValuesRooted &= _handles.Values.Contains(_appearanceSceneValue);
                return MakeString(AppearanceHandle(arguments) == AppearanceActiveSceneHandle ? AppearanceActiveSceneName : AppearanceOwnSceneName);
        }
        if (row.Name.StartsWith("get_", StringComparison.Ordinal))
        { ++AppearanceGetters; return Box(AppearanceColorType, AppearanceBytes(instance == AppearanceRingRenderer ? AppearanceRingRendered
            : instance == AppearanceSprite ? AppearanceSpriteColor
            : row.Name == "get_endColor" ? AppearanceEndRendered : AppearanceRendered)); }
        ++AppearanceSetters;
        var value = Marshal.PtrToStructure<Rgba>(Marshal.ReadIntPtr(arguments));
        if (!AppearanceIgnoreWrites)
        {
            if (instance == AppearanceGroup)
            {
                if (!AppearanceRingIgnoreCacheWrites) AppearanceRingCached = value;
                if (!AppearanceRingIgnoreRendererWrites) AppearanceRingRendered = value;
            }
            else if (instance == AppearanceSprite) AppearanceSpriteColor = value;
            else
            {
                if (row.Name == "set_endColor") AppearanceEndRendered = value;
                else AppearanceCached = AppearanceRendered = value;
                var cache = row.Name == "set_endColor" ? "_endColorPlan" : "_startColorPlan";
                foreach (var field in _structFields[AppearanceLineClass].Where(field => _fieldMetadata[field].Name == cache))
                    _appearanceStorage[(instance, field)] = AppearanceBytes(value);
            }
        }
        return IntPtr.Zero;
    }
    private static int AppearanceHandle(IntPtr arguments) => Marshal.ReadInt32(Marshal.ReadIntPtr(arguments));
}
