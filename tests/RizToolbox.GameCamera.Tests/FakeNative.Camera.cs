using System.Runtime.InteropServices;
using RizToolbox.Adapter.UiInput;
using RizToolbox.Runtime;

// Process-owned metadata, buffers and delegates. No Unity or game DLL is loaded.
sealed partial class FakeNative
{
    internal sealed class CameraMethod
    {
        internal IntPtr Pointer, Class, Return; internal string Name = ""; internal IntPtr[] Parameters = Array.Empty<IntPtr>(); internal bool Static, Generic, Inflated;
    }
    private readonly Dictionary<IntPtr, CameraMethod> _cameraMethods = new();
    private readonly Dictionary<IntPtr, IntPtr> _cameraFieldParents = new(), _cameraParents = new();
    private readonly Dictionary<(IntPtr Owner, IntPtr Field), byte[]> _cameraStorage = new();
    private readonly Dictionary<string, (IntPtr Owner, IntPtr Field)> _cameraFields = new();
    private readonly Dictionary<IntPtr, IntPtr> _cameraTransforms = new(), _cameraGameObjects = new();
    private readonly Dictionary<IntPtr, int> _cameraObjectScenes = new();
    private readonly Dictionary<int, string> _cameraSceneNames = new();
    private readonly HashSet<int> _cameraInvalidScenes = new(), _cameraUnloadedScenes = new();
    private readonly HashSet<IntPtr> _cameraDisabledBehaviours = new();
    private readonly Dictionary<IntPtr, string> _cameraImages = new();
    private IntPtr _cameraAssemblies, _cameraVectorType, _cameraSceneType, _cameraTypeType;
    internal NativeRuntime CameraApi = null!;
    internal IntPtr CameraVectorType => _cameraVectorType;
    internal IntPtr CameraController, CameraOtherController, CameraInformation, CameraChart, CameraProgress, CameraRigControl,
        CameraJudge, CameraObject, CameraRigTransform, CameraViewTransform, CameraOtherTransform, CameraGameObject,
        CameraRigGameObject, CameraViewGameObject, CameraOtherGameObject, CameraControllers;
    internal IntPtr CameraControllerClass, CameraTransformClass;
    internal NativeVector3 CameraPosition = new(.5f, -2.4f, 0);
    internal NativeVector3 CameraScale = new(1, 2, 3);
    internal bool CameraActive = true, CameraLoaded = true, CameraValid = true, CameraDescendant = true, CameraIgnoreWrites,
        CameraFailSetter, CameraMatches = true, AllCameraReceiversRooted = true, AllCameraValuesRooted = true, AllCameraMatcherArgumentsRooted = true;
    internal bool CameraIgnoreScaleWrites, CameraFailScaleSetter;
    internal IntPtr CameraScaleBoxType, CameraLastScaleWriteReceiver;
    internal string CameraScene = "Play";
    internal int CameraActiveSceneHandle = 7, CameraControllerSceneHandle = -12,
        CameraRigSceneHandle = 21, CameraViewSceneHandle = 22;
    internal int CameraGets, CameraSets, CameraScaleGets, CameraScaleSets, CameraMatchCalls;
    internal Action? OnCameraRead, OnCameraScaleRead;

    internal void ConfigureCamera()
    {
        _cameraVectorType = AddStruct("UnityEngine.Vector3", 12, new("x", "System.Single", 0), new("y", "System.Single", 4), new("z", "System.Single", 8));
        _cameraSceneType = AddStruct("UnityEngine.SceneManagement.Scene", 4, new NativeFieldLayout("m_Handle", "System.Int32", 0));
        foreach (var pair in _structFields) foreach (var field in pair.Value) _cameraFieldParents[field] = pair.Key;
        var unityObject = AddType("UnityEngine.Object", 18, false, 8); var component = AddType("UnityEngine.Component", 18, false, 8);
        var behaviour = AddType("UnityEngine.Behaviour", 18, false, 8);
        var gameObject = AddType("UnityEngine.GameObject", 18, false, 8); var transform = AddType("UnityEngine.Transform", 18, false, 8);
        var sceneManager = AddType("UnityEngine.SceneManagement.SceneManager", 18, false, 8);
        var camera = AddType("UnityEngine.Camera", 18, false, 8); var resources = AddType("UnityEngine.Resources", 18, false, 8);
        _cameraTypeType = AddType("System.Type", 18, false, 8);
        var controllerBase = AddType("InLevelController", 18, false, 8); var controller = AddType("NormalModeInLevelController", 18, false, 8);
        var information = AddType("InLevel.LevelInformation", 18, false, 8); var chart = AddType("InLevel.Chart", 18, false, 8);
        var progress = AddType("InLevel.ProgressControl", 18, false, 8); var rig = AddType("InLevel.CameraControl", 18, false, 8);
        var judge = AddType("InLevel.NoteJudgeControl", 18, false, 8);
        CameraControllerClass = ClassOf(controller); CameraTransformClass = ClassOf(transform);
        _cameraParents[ClassOf(component)] = ClassOf(unityObject); _cameraParents[ClassOf(gameObject)] = ClassOf(unityObject);
        _cameraParents[ClassOf(behaviour)] = ClassOf(component);
        _cameraParents[ClassOf(transform)] = ClassOf(component); _cameraParents[ClassOf(camera)] = ClassOf(behaviour);
        _cameraParents[ClassOf(controller)] = ClassOf(controllerBase); _cameraParents[ClassOf(controllerBase)] = ClassOf(behaviour);
        foreach (var type in new[] { information, progress, judge }) _cameraParents[ClassOf(type)] = ClassOf(component);
        _cameraParents[ClassOf(rig)] = ClassOf(behaviour);
        CameraController = Box(controller, new byte[0xD0]); CameraOtherController = Box(controller, new byte[0xD0]);
        CameraInformation = Box(information, new byte[0xD0]); CameraChart = Box(chart, new byte[0xA0]); CameraProgress = Box(progress, new byte[0xA0]);
        CameraRigControl = Box(rig, new byte[0x80]); CameraJudge = Box(judge, new byte[0x90]); CameraObject = Box(camera, new byte[0x40]);
        CameraRigTransform = Box(transform, new byte[0x40]); CameraViewTransform = Box(transform, new byte[0x40]); CameraOtherTransform = Box(transform, new byte[0x40]);
        CameraGameObject = Box(gameObject, new byte[0x40]); CameraRigGameObject = Box(gameObject, new byte[0x40]);
        CameraViewGameObject = Box(gameObject, new byte[0x40]); CameraOtherGameObject = Box(gameObject, new byte[0x40]);
        _cameraGameObjects[CameraController] = CameraGameObject; _cameraGameObjects[CameraOtherController] = CameraOtherGameObject;
        _cameraGameObjects[CameraRigControl] = CameraRigGameObject; _cameraGameObjects[CameraObject] = CameraViewGameObject;
        _cameraObjectScenes[CameraGameObject] = CameraControllerSceneHandle;
        _cameraObjectScenes[CameraOtherGameObject] = CameraControllerSceneHandle;
        _cameraObjectScenes[CameraRigGameObject] = CameraRigSceneHandle;
        _cameraObjectScenes[CameraViewGameObject] = CameraViewSceneHandle;
        _cameraSceneNames[CameraControllerSceneHandle] = "DontDestroyOnLoad";
        _cameraSceneNames[CameraRigSceneHandle] = "CameraRigHost";
        _cameraSceneNames[CameraViewSceneHandle] = "CameraRenderHost";
        _cameraTransforms[CameraRigControl] = CameraRigTransform; _cameraTransforms[CameraObject] = CameraViewTransform;
        CameraRef("controller.info", ClassOf(controllerBase), "LevelInformation", information, 0x28, CameraController, CameraInformation);
        CameraRef("info.chart", ClassOf(information), "chart", chart, 0x88, CameraInformation, CameraChart);
        CameraRef("info.progress", ClassOf(information), "progressControl", progress, 0x28, CameraInformation, CameraProgress);
        CameraRef("info.rig", ClassOf(information), "cameraControl", rig, 0x78, CameraInformation, CameraRigControl);
        CameraRef("info.judge", ClassOf(information), "noteJudgeControl", judge, 0x58, CameraInformation, CameraJudge);
        CameraRef("rig.info", ClassOf(rig), "levelInformation", information, 0x20, CameraRigControl, CameraInformation);
        CameraRef("judge.info", ClassOf(judge), "levelInformation", information, 0x20, CameraJudge, CameraInformation);
        CameraRef("judge.camera", ClassOf(judge), "_camera", camera, 0x70, CameraJudge, CameraObject);
        CameraFlag("info.loaded", ClassOf(information), "loadOver", 0xBE, CameraInformation, true);
        CameraFlag("info.over", ClassOf(information), "levelOver", 0xC0, CameraInformation, false);
        CameraFlag("info.crashed", ClassOf(information), "crashed", 0xC1, CameraInformation, false);
        CameraFlag("progress.ready", ClassOf(progress), "timeReady", 0x65, CameraProgress, true);
        CameraAddMethod(ClassOf(resources), "FindObjectsOfTypeAll", true, Object, _cameraTypeType);
        CameraAddMethod(ClassOf(component), "get_transform", false, transform);
        CameraAddMethod(ClassOf(component), "get_gameObject", false, gameObject);
        CameraAddMethod(ClassOf(gameObject), "get_scene", false, _cameraSceneType);
        CameraAddMethod(ClassOf(gameObject), "get_activeInHierarchy", false, Boolean);
        CameraAddMethod(ClassOf(behaviour), "get_isActiveAndEnabled", false, Boolean);
        CameraAddMethod(ClassOf(sceneManager), "GetActiveScene", true, _cameraSceneType);
        CameraAddMethod(ClassOf(unityObject), "op_Implicit", true, Boolean, unityObject);
        CameraAddMethod(ClassOf(unityObject), "GetInstanceID", false, Int32);
        CameraAddMethod(ClassOf(_cameraSceneType), "IsValidInternal", true, Boolean, Int32);
        CameraAddMethod(ClassOf(_cameraSceneType), "GetIsLoadedInternal", true, Boolean, Int32);
        CameraAddMethod(ClassOf(_cameraSceneType), "GetNameInternal", true, String, Int32);
        CameraAddMethod(ClassOf(transform), "IsChildOf", false, Boolean, transform);
        CameraAddMethod(ClassOf(transform), "get_localPosition", false, _cameraVectorType);
        CameraAddMethod(ClassOf(transform), "set_localPosition", false, Void, _cameraVectorType);
        CameraAddMethod(ClassOf(transform), "get_localScale", false, _cameraVectorType);
        CameraAddMethod(ClassOf(transform), "set_localScale", false, Void, _cameraVectorType);
        _referenceArrayType = AddType("UnityEngine.Object[]", 29, false, 8);
        CameraMethodByName("FindObjectsOfTypeAll").Return = _referenceArrayType;
        CameraControllers = ReferenceArray(1); SetCameraControllers(CameraController);
        var gameImage = CameraAllocation(8); var unityImage = CameraAllocation(8); var systemImage = CameraAllocation(8);
        _cameraImages[gameImage] = "Assembly-CSharp.dll"; _cameraImages[unityImage] = "UnityEngine.CoreModule.dll"; _cameraImages[systemImage] = "mscorlib.dll";
        _cameraAssemblies = CameraAllocation(24); Marshal.WriteIntPtr(_cameraAssemblies, gameImage); Marshal.WriteIntPtr(_cameraAssemblies, 8, unityImage); Marshal.WriteIntPtr(_cameraAssemblies, 16, systemImage);
        Add("il2cpp_domain_get_assemblies", "Ptr2", nameof(CameraAssemblies)); Add("il2cpp_assembly_get_image", "Ptr1", nameof(CameraIdentity));
        Add("il2cpp_image_get_name", "Ptr1", nameof(CameraImage)); Add("il2cpp_class_from_name", "ClassLookup", nameof(CameraFindClass));
        Add("il2cpp_class_get_name", "Ptr1", nameof(CameraClassName)); Add("il2cpp_class_get_namespace", "Ptr1", nameof(CameraNamespace));
        Add("il2cpp_class_get_parent", "Ptr1", nameof(CameraParent)); Add("il2cpp_class_is_assignable_from", "Assignable", nameof(CameraAssignable));
        Add("il2cpp_type_get_object", "Ptr1", nameof(CameraTypeObject)); Add("il2cpp_class_get_methods", "Ptr2", nameof(CameraMethods));
        Add("il2cpp_method_get_name", "Ptr1", nameof(CameraMethodName)); Add("il2cpp_method_get_class", "Ptr1", nameof(CameraMethodClass));
        Add("il2cpp_method_get_param_count", "Flags", nameof(CameraParamCount)); Add("il2cpp_method_get_param", "ParameterType", nameof(CameraParam));
        Add("il2cpp_method_get_return_type", "Ptr1", nameof(CameraReturn)); Add("il2cpp_method_get_flags", "MethodFlags", nameof(CameraFlags));
        Add("il2cpp_method_is_generic", "Predicate", nameof(CameraMethodGeneric)); Add("il2cpp_method_is_inflated", "Predicate", nameof(CameraMethodInflated));
        Add("il2cpp_class_get_field_from_name", "FieldLookup", nameof(CameraFindField)); Add("il2cpp_field_get_parent", "Ptr1", nameof(CameraFieldParent));
        Add("il2cpp_field_get_value", "FieldGet", nameof(CameraReadField)); Add("il2cpp_class_instance_size", "ArraySize", nameof(CameraInstanceSize));
        Add("il2cpp_runtime_invoke", "RuntimeInvoke", nameof(CameraInvoke)); Add("il2cpp_object_unbox", "Ptr1", nameof(CameraUnbox));
        CameraApi = new NativeRuntime(ExportPointer); CameraApi.BindCurrentThread();
    }
    private IntPtr CameraAllocation(int bytes) { var pointer = Marshal.AllocHGlobal(bytes); _allocations.Add(pointer); return pointer; }
    private IntPtr CameraText(string text) { var pointer = Marshal.StringToHGlobalAnsi(text); _allocations.Add(pointer); return pointer; }
    private void CameraRef(string key, IntPtr cls, string name, IntPtr type, int offset, IntPtr owner, IntPtr target)
    { _cameraFields[key] = (owner, CameraAddField(cls, name, type, offset)); SetCameraReference(key, target); }
    private void CameraFlag(string key, IntPtr cls, string name, int offset, IntPtr owner, bool value)
    { _cameraFields[key] = (owner, CameraAddField(cls, name, Boolean, offset)); SetCameraFlag(key, value); }
    private IntPtr CameraAddField(IntPtr cls, string name, IntPtr type, int offset)
    {
        var field = new IntPtr(10000 + _fieldMetadata.Count); _fieldMetadata[field] = (name, type, offset); _cameraFieldParents[field] = cls;
        if (!_structFields.TryGetValue(cls, out var rows)) _structFields[cls] = rows = new(); rows.Add(field); return field;
    }
    private void CameraAddMethod(IntPtr cls, string name, bool isStatic, IntPtr returns, params IntPtr[] parameters)
    { var pointer = new IntPtr(80000 + _cameraMethods.Count); _cameraMethods[pointer] = new() { Pointer = pointer, Class = cls, Name = name, Static = isStatic, Return = returns, Parameters = parameters }; }
    internal CameraMethod CameraMethodByName(string name) => _cameraMethods.Values.Single(x => x.Name == name);
    internal CameraMethod DuplicateCameraMethod(string name)
    {
        var source = CameraMethodByName(name); var pointer = new IntPtr(80000 + _cameraMethods.Count);
        var duplicate = new CameraMethod { Pointer = pointer, Class = source.Class, Name = source.Name,
            Static = source.Static, Generic = source.Generic, Inflated = source.Inflated, Return = source.Return, Parameters = source.Parameters.ToArray() };
        _cameraMethods.Add(pointer, duplicate); return duplicate;
    }
    internal Dictionary<string, NativeExactReadField> CameraFieldDescriptors() => _cameraFields.ToDictionary(x => x.Key, x => {
        var field = x.Value.Field; var metadata = _fieldMetadata[field];
        return CameraApi.InspectExactDeclaredReadField(_cameraFieldParents[field], metadata.Name, _types[metadata.Type].Name, false, metadata.Offset);
    });
    internal void SetCameraReference(string key, IntPtr pointer) { var field = _cameraFields[key]; _cameraStorage[(field.Owner, field.Field)] = BitConverter.GetBytes(pointer.ToInt64()); }
    internal void SetCameraFlag(string key, bool value) { var field = _cameraFields[key]; _cameraStorage[(field.Owner, field.Field)] = new[] { value ? (byte)1 : (byte)0 }; }
    internal void SetCameraControllers(params IntPtr[] controllers) => _referenceArrays[CameraControllers] = controllers;
    internal void SetCameraObjectScene(IntPtr component, int handle) => _cameraObjectScenes[_cameraGameObjects[component]] = handle;
    internal void SetCameraSceneName(int handle, string name) => _cameraSceneNames[handle] = name;
    internal void SetCameraSceneValid(int handle, bool valid)
    { if (valid) _cameraInvalidScenes.Remove(handle); else _cameraInvalidScenes.Add(handle); }
    internal void SetCameraSceneLoaded(int handle, bool loaded)
    { if (loaded) _cameraUnloadedScenes.Remove(handle); else _cameraUnloadedScenes.Add(handle); }
    internal void SetCameraBehaviourEnabled(IntPtr component, bool enabled)
    { if (enabled) _cameraDisabledBehaviours.Remove(component); else _cameraDisabledBehaviours.Add(component); }
    internal void SetCameraTransform(IntPtr source, IntPtr transform) => _cameraTransforms[source] = transform;
    internal IntPtr CameraReplacement(IntPtr original) => Box(_boxedTypes[original], new byte[0x200]);
    internal bool MatchCameraRun(long information, long controller, string run, long scene)
    {
        ++CameraMatchCalls; AllCameraMatcherArgumentsRooted &= HasRootFor(new(information)) && HasRootFor(new(controller));
        return CameraMatches && information == CameraInformation.ToInt64() && controller == CameraController.ToInt64() && run == "run" && scene == 10;
    }
    private IntPtr CameraAssemblies(IntPtr _, IntPtr count) { Marshal.WriteIntPtr(count, new IntPtr(3)); return _cameraAssemblies; }
    private IntPtr CameraIdentity(IntPtr value) => value;
    private IntPtr CameraImage(IntPtr value) => CameraText(_cameraImages[value]);
    private string CameraTypeName(IntPtr cls) => _types[_classes[cls]].Name;
    private IntPtr CameraClassName(IntPtr cls) => CameraText(CameraTypeName(cls).Split('.').Last());
    private IntPtr CameraNamespace(IntPtr cls) { var text = CameraTypeName(cls); var index = text.LastIndexOf('.'); return CameraText(index < 0 ? "" : text[..index]); }
    private IntPtr CameraFindClass(IntPtr _, string ns, string name) => _classes.Keys.FirstOrDefault(cls => CameraTypeName(cls) == (ns.Length == 0 ? name : ns + "." + name));
    private IntPtr CameraParent(IntPtr cls) => _cameraParents.TryGetValue(cls, out var parent) ? parent : IntPtr.Zero;
    private bool CameraAssignable(IntPtr target, IntPtr source)
    { if (target == ClassOf(Object)) return true; for (var at = source; at != IntPtr.Zero; at = CameraParent(at)) if (target == at) return true; return false; }
    private IntPtr CameraTypeObject(IntPtr _) => Box(_cameraTypeType, new byte[8]);
    private IntPtr CameraMethods(IntPtr cls, IntPtr iterator)
    {
        if (cls == ClassOf(_referenceArrayType)) return ClassMethods(cls, iterator);
        var rows = _cameraMethods.Values.Where(x => x.Class == cls).ToArray(); var index = Marshal.ReadIntPtr(iterator).ToInt32();
        if (index >= rows.Length) return IntPtr.Zero; Marshal.WriteIntPtr(iterator, new IntPtr(index + 1)); return rows[index].Pointer;
    }
    private IntPtr CameraMethodName(IntPtr method) => IsArrayMethod(method) ? MethodName(method) : CameraText(_cameraMethods[method].Name);
    private IntPtr CameraMethodClass(IntPtr method) => IsArrayMethod(method) ? MethodClass(method) : _cameraMethods[method].Class;
    private uint CameraParamCount(IntPtr method) => IsArrayMethod(method) ? ParameterCount(method) : (uint)_cameraMethods[method].Parameters.Length;
    private IntPtr CameraParam(IntPtr method, uint index) => IsArrayMethod(method) ? ParameterType(method, index) : _cameraMethods[method].Parameters[index];
    private IntPtr CameraReturn(IntPtr method) => IsArrayMethod(method) ? GetReturnType(method) : _cameraMethods[method].Return;
    private uint CameraFlags(IntPtr method, out uint implementation) { implementation = 0; return !IsArrayMethod(method) && _cameraMethods[method].Static ? 0x10u : 0; }
    private bool CameraMethodGeneric(IntPtr method) => !IsArrayMethod(method) && _cameraMethods[method].Generic;
    private bool CameraMethodInflated(IntPtr method) => !IsArrayMethod(method) && _cameraMethods[method].Inflated;
    private IntPtr CameraFindField(IntPtr cls, string name)
    { for (var at = cls; at != IntPtr.Zero; at = CameraParent(at)) if (_structFields.TryGetValue(at, out var fields)) { var found = fields.FirstOrDefault(x => _fieldMetadata[x].Name == name); if (found != IntPtr.Zero) return found; } return IntPtr.Zero; }
    private IntPtr CameraFieldParent(IntPtr field) => _cameraFieldParents[field];
    private uint CameraInstanceSize(IntPtr _) => 0x200;
    private void CameraReadField(IntPtr owner, IntPtr field, IntPtr output)
    {
        ++FieldReads; AllCameraReceiversRooted &= HasRootFor(owner);
        var bytes = _cameraStorage[(owner, field)]; if (bytes.Length != _types[_fieldMetadata[field].Type].Size) throw new InvalidOperationException("Controlled camera field size mismatch.");
        Marshal.Copy(bytes, 0, output, bytes.Length);
    }
    private IntPtr CameraUnbox(IntPtr value) { ++Unboxes; AllCameraValuesRooted &= HasRootFor(value); return value; }
    private static byte[] CameraBytes(NativeVector3 value) => new[] { value.X, value.Y, value.Z }.SelectMany(BitConverter.GetBytes).ToArray();
    private IntPtr CameraInvoke(IntPtr method, IntPtr instance, IntPtr arguments, IntPtr exception)
    {
        ++Invocations; Marshal.WriteIntPtr(exception, IntPtr.Zero); AllCameraReceiversRooted &= instance == IntPtr.Zero || HasRootFor(instance);
        if (IsArrayMethod(method)) return InvokeReferenceArray(method, instance, arguments, exception);
        var row = _cameraMethods[method];
        switch (row.Name)
        {
            case "FindObjectsOfTypeAll": return CameraControllers;
            case "get_transform": return _cameraTransforms[instance];
            case "get_gameObject": return _cameraGameObjects[instance];
            case "get_scene": return Box(_cameraSceneType, BitConverter.GetBytes(_cameraObjectScenes[instance]));
            case "GetActiveScene": return Box(_cameraSceneType, BitConverter.GetBytes(CameraActiveSceneHandle));
            case "get_activeInHierarchy": return Box(Boolean, new[] { CameraActive ? (byte)1 : (byte)0 });
            case "get_isActiveAndEnabled": return Box(Boolean, new[] { _cameraDisabledBehaviours.Contains(instance) ? (byte)0 : (byte)1 });
            case "op_Implicit": return Box(Boolean, new byte[] { 1 });
            case "GetInstanceID": return Box(Int32, BitConverter.GetBytes(instance == CameraRigTransform ? 101 : 103));
            case "IsValidInternal": return Box(Boolean, new[] { CameraValid && !_cameraInvalidScenes.Contains(CameraSceneArgument(arguments)) ? (byte)1 : (byte)0 });
            case "GetIsLoadedInternal": return Box(Boolean, new[] { CameraLoaded && !_cameraUnloadedScenes.Contains(CameraSceneArgument(arguments)) ? (byte)1 : (byte)0 });
            case "GetNameInternal":
                var handle = CameraSceneArgument(arguments);
                return MakeString(handle == CameraActiveSceneHandle ? CameraScene : _cameraSceneNames.GetValueOrDefault(handle, "Unknown"));
            case "IsChildOf": return Box(Boolean, new[] { CameraDescendant ? (byte)1 : (byte)0 });
            case "get_localPosition": ++CameraGets; OnCameraRead?.Invoke(); return Box(_cameraVectorType, CameraBytes(CameraPosition));
            case "set_localPosition":
                ++CameraSets; if (!CameraIgnoreWrites) CameraPosition = Marshal.PtrToStructure<NativeVector3>(Marshal.ReadIntPtr(arguments));
                if (CameraFailSetter) { CameraFailSetter = false; Marshal.WriteIntPtr(exception, new IntPtr(99)); } return IntPtr.Zero;
            case "get_localScale":
                ++CameraScaleGets; OnCameraScaleRead?.Invoke();
                return CameraScaleBoxType == IntPtr.Zero ? Box(_cameraVectorType, CameraBytes(CameraScale)) : Box(CameraScaleBoxType, BitConverter.GetBytes(CameraScale.X));
            case "set_localScale":
                ++CameraScaleSets; CameraLastScaleWriteReceiver = instance;
                if (!CameraIgnoreScaleWrites) CameraScale = Marshal.PtrToStructure<NativeVector3>(Marshal.ReadIntPtr(arguments));
                if (CameraFailScaleSetter) { CameraFailScaleSetter = false; Marshal.WriteIntPtr(exception, new IntPtr(99)); } return IntPtr.Zero;
            default: throw new InvalidOperationException("Unexpected camera fixture method " + row.Name);
        }
    }
    private static int CameraSceneArgument(IntPtr arguments) => Marshal.ReadInt32(Marshal.ReadIntPtr(arguments));
}
