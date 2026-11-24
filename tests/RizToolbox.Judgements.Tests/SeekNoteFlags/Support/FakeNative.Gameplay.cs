#pragma warning disable CS0649 // Reused synthetic fixture fault knobs intentionally retain default values.
using System.Runtime.InteropServices;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.NoteFlags;

// Synthetic export/metadata process using the actual NativeRuntime and GameplayNativeBackend.
// No GameAssembly, Unity runtime, detour or game process is loaded by this fixture.
sealed partial class FakeNative
{
    private sealed record GameplayMethod(IntPtr Class, string Name, bool Static, IntPtr Return, IntPtr[] Parameters);
    private readonly Dictionary<IntPtr, GameplayMethod> _gameplayMethods = new();
    private readonly Dictionary<IntPtr, IntPtr> _gameplayFieldParents = new();
    private readonly Dictionary<(IntPtr Owner, IntPtr Field), byte[]> _gameplayStorage = new();
    private readonly Dictionary<string, (IntPtr Owner, IntPtr Field)> _gameplayFields = new();
    private readonly Dictionary<IntPtr, string> _gameplayImages = new();
    private IntPtr _gameplayAssemblies, _gameplayType, _gameplayDouble;
    private IntPtr _gameplayControllerType, _gameplayControllerBaseType, _gameplayControllerParent;
    internal IntPtr GameplayControllerClass => ClassOf(_gameplayControllerType);
    internal IntPtr GameplayControllerBaseClass => ClassOf(_gameplayControllerBaseType);
    internal IntPtr GameplayControllerInformationField => _gameplayFields["controller.info"].Field;
    internal NativeRuntime GameplayApi = null!;
    internal IntPtr GameplayController, GameplayInformation, GameplayProgress, GameplayPause, GameplayControllers,
        GameplayPlayer, GameplaySource, GameplayClip, GameplayGetTimeMethod;
    internal bool GameplayActive = true, GameplayCanDo = true, AllGameplayReceiversRooted = true;
    internal float GameplayPitch = 1, GameplayPosition = 4, GameplayLength = 120;
    internal double GameplayDsp = 100;
    internal long GameplayPitchRevision = 1;
    internal int GameplayPitchWrites, GameplayCommandCalls;
    internal Action? AfterGameplayPitchWrite;
    internal string? MissingGameplayField;
    internal readonly List<float> GameplayPitchAttempts = new();

    internal void ConfigureGameplay()
    {
        _gameplayDouble = AddType("System.Double", 13, true, 8);
        _gameplayType = AddType("System.Type", 18, false, 8);
        var resources = AddType("UnityEngine.Resources", 18, false, 8);
        var unityObject = AddType("UnityEngine.Object", 18, false, 8);
        var audioSettings = AddType("UnityEngine.AudioSettings", 18, false, 8);
        var time = AddType("UnityEngine.Time", 18, false, 8);
        var blocker = AddType("TouchBlocker", 18, false, 8);
        var window = AddType("WindowControl", 18, false, 8);
        var controller = _gameplayControllerType = AddType("NormalModeInLevelController", 18, false, 8);
        _gameplayControllerBaseType = AddType("InLevelController", 18, false, 8);
        _gameplayControllerParent = GameplayControllerBaseClass;
        var information = AddType("InLevel.LevelInformation", 18, false, 8);
        var progress = AddType("InLevel.ProgressControl", 18, false, 8);
        var pause = AddType("PauseUIControl", 18, false, 8);
        var player = AddType("Pigeon.MUGToolkit.Core.MUGMusicPlayer", 18, false, 8);
        var source = AddType("UnityEngine.AudioSource", 18, false, 8);
        var clip = AddType("UnityEngine.AudioClip", 18, false, 8);
        GameplayController = Box(controller, new byte[16]); GameplayInformation = Box(information, new byte[16]);
        GameplayProgress = Box(progress, new byte[16]); GameplayPause = Box(pause, new byte[16]);
        GameplayPlayer = Box(player, new byte[16]); GameplaySource = Box(source, new byte[16]); GameplayClip = Box(clip, new byte[16]);
        SeekField("controller.info", _gameplayControllerBaseType, "LevelInformation", information, GameplayController, GameplayInformation, 0x28);
        GameplayField("controller.pause", controller, "pauseUIControl", pause, GameplayController, GameplayPause);
        GameplayField("info.progress", information, "progressControl", progress, GameplayInformation, GameplayProgress);
        GameplayField("progress.info", progress, "levelInformation", information, GameplayProgress, GameplayInformation);
        GameplayField("info.loaded", information, "loadOver", Boolean, GameplayInformation, true);
        GameplayField("info.over", information, "levelOver", Boolean, GameplayInformation, false);
        GameplayField("info.crashed", information, "crashed", Boolean, GameplayInformation, false);
        GameplayField("progress.ready", progress, "timeReady", Boolean, GameplayProgress, true);
        GameplayField("progress.playing", progress, "isPlaying", Boolean, GameplayProgress, false);
        GameplayField("progress.preparing", progress, "prepareToPlay", Boolean, GameplayProgress, false);
        GameplayField("progress.now", progress, "_nowTime", _gameplayDouble, GameplayProgress, 3.8);
        GameplayField("progress.offset", progress, "_offset", _gameplayDouble, GameplayProgress, .2);
        GameplayField("pause.paused", pause, "_pausing", Boolean, GameplayPause, true);
        GameplayField("pause.over", pause, "_over", Boolean, GameplayPause, false);
        GameplayField("pause.terminated", pause, "_terminated", Boolean, GameplayPause, false);
        GameplayField("pause.time", pause, "_pauseTime", Single, GameplayPause, 0f);
        foreach (var field in new[] { "nowTime", "nowRealTime", "nowSyncedTime" })
            GameplayField("info." + field, information, field, Single, GameplayInformation, field == "nowTime" ? 7.6f : 3.8f);
        GameplayMethodAdd(resources, "FindObjectsOfTypeAll", true, Object, _gameplayType);
        GameplayMethodAdd(unityObject, "op_Implicit", true, Boolean, unityObject);
        GameplayMethodAdd(controller, "get_isActiveAndEnabled", false, Boolean);
        GameplayMethodAdd(controller, "Pause", false, Void); GameplayMethodAdd(controller, "Retry", false, Void); GameplayMethodAdd(controller, "Skip", false, Void);
        GameplayMethodAdd(pause, "Pause", false, Void); GameplayMethodAdd(pause, "Resume", false, Void);
        GameplayMethodAdd(pause, "get_CanDoOperation", false, Boolean); GameplayMethodAdd(pause, "get_isActiveAndEnabled", false, Boolean);
        GameplayMethodAdd(audioSettings, "get_dspTime", true, _gameplayDouble);
        GameplayMethodAdd(time, "get_timeSinceLevelLoad", true, Single);
        GameplayMethodAdd(blocker, "get_Blocked", true, Boolean); GameplayMethodAdd(window, "get_HasActiveWindow", true, Boolean);
        GameplayMethodAdd(source, "get_pitch", false, Single); GameplayMethodAdd(source, "set_pitch", false, Void, Single);
        GameplayMethodAdd(source, "get_time", false, Single); GameplayMethodAdd(source, "get_isPlaying", false, Boolean);
        GameplayMethodAdd(clip, "get_length", false, Single);
        GameplayGetTimeMethod = GameplayMethodAdd(player, "GetTime", false, Single);
        _referenceArrayType = AddType("UnityEngine.Object[]", 29, false, 8);
        var discovery = _gameplayMethods.Single(x => x.Value.Name == "FindObjectsOfTypeAll");
        _gameplayMethods[discovery.Key] = discovery.Value with { Return = _referenceArrayType };
        GameplayControllers = ReferenceArray(1); _referenceArrays[GameplayControllers] = new[] { GameplayController };
        foreach (var name in new[] { "Assembly-CSharp.dll", "UnityEngine.CoreModule.dll", "UnityEngine.AudioModule.dll" })
            _gameplayImages[GameplayAllocate(8)] = name;
        _gameplayAssemblies = GameplayAllocate(24); var imageIndex = 0;
        foreach (var image in _gameplayImages.Keys) Marshal.WriteIntPtr(_gameplayAssemblies, imageIndex++ * 8, image);
        Add("il2cpp_domain_get_assemblies", "Ptr2", nameof(GameplayAssemblies)); Add("il2cpp_assembly_get_image", "Ptr1", nameof(GameplayIdentity));
        Add("il2cpp_image_get_name", "Ptr1", nameof(GameplayImage)); Add("il2cpp_class_from_name", "ClassLookup", nameof(GameplayFindClass));
        Add("il2cpp_class_get_name", "Ptr1", nameof(GameplayClassName)); Add("il2cpp_class_get_namespace", "Ptr1", nameof(GameplayNamespace));
        Add("il2cpp_class_get_image", "Ptr1", nameof(GameplayClassImage));
        Add("il2cpp_class_is_assignable_from", "Assignable", nameof(GameplayAssignable)); Add("il2cpp_type_get_object", "Ptr1", nameof(GameplayTypeObject));
        Add("il2cpp_class_get_methods", "Ptr2", nameof(GameplayMethods)); Add("il2cpp_method_get_name", "Ptr1", nameof(GameplayMethodName));
        Add("il2cpp_method_get_class", "Ptr1", nameof(GameplayMethodClass)); Add("il2cpp_method_get_param_count", "Flags", nameof(GameplayParamCount));
        Add("il2cpp_method_get_param", "ParameterType", nameof(GameplayParam)); Add("il2cpp_method_get_return_type", "Ptr1", nameof(GameplayReturn));
        Add("il2cpp_method_get_flags", "MethodFlags", nameof(GameplayFlags)); Add("il2cpp_class_get_field_from_name", "FieldLookup", nameof(GameplayFindField));
        Add("il2cpp_field_get_parent", "Ptr1", nameof(GameplayFieldParent)); Add("il2cpp_field_get_value", "FieldGet", nameof(GameplayReadField));
        Add("il2cpp_runtime_invoke", "RuntimeInvoke", nameof(GameplayInvoke));
        GameplayApi = new NativeRuntime(ExportPointer); GameplayApi.BindCurrentThread();
    }

    private IntPtr GameplayAllocate(int bytes) { var result = Marshal.AllocHGlobal(bytes); _allocations.Add(result); return result; }
    private Dictionary<string, IntPtr>? _stableBorrowedMetadataText;
    private IntPtr GameplayText(string value)
    {
        if (_stableBorrowedMetadataText is not null && _stableBorrowedMetadataText.TryGetValue(value, out var existing)) return existing;
        var result = Marshal.StringToHGlobalAnsi(value); _allocations.Add(result);
        _stableBorrowedMetadataText?.Add(value, result); return result;
    }
    private void GameplayField(string key, IntPtr type, string name, IntPtr fieldType, IntPtr owner, object value)
    {
        var field = new IntPtr(20000 + _fieldMetadata.Count); _fieldMetadata[field] = (name, fieldType, 16);
        var cls = ClassOf(type); _gameplayFieldParents[field] = cls;
        if (!_structFields.TryGetValue(cls, out var fields)) _structFields[cls] = fields = new(); fields.Add(field);
        _gameplayFields[key] = (owner, field); SetGameplayField(key, value);
    }
    internal void SetGameplayField(string key, object value)
    {
        var row = _gameplayFields[key];
        _gameplayStorage[(row.Owner, row.Field)] = value switch {
            IntPtr pointer => BitConverter.GetBytes(pointer.ToInt64()), bool flag => new[] { flag ? (byte)1 : (byte)0 },
            int integer => BitConverter.GetBytes(integer),
            float single => BitConverter.GetBytes(single), double number => BitConverter.GetBytes(number),
            _ => throw new ArgumentException("Unsupported controlled field value.") };
    }
    internal void ForeignGameplayPitch(float pitch) { GameplayPitchRevision++; GameplayPitch = pitch; }
    private IntPtr GameplayMethodAdd(IntPtr type, string name, bool isStatic, IntPtr returns, params IntPtr[] parameters)
    { var id = new IntPtr(80000 + _gameplayMethods.Count); _gameplayMethods[id] = new(ClassOf(type), name, isStatic, returns, parameters); return id; }
    private IntPtr GameplayAssemblies(IntPtr _, IntPtr count) { Marshal.WriteIntPtr(count, new IntPtr(3)); return _gameplayAssemblies; }
    private IntPtr GameplayIdentity(IntPtr value) => value;
    private IntPtr GameplayImage(IntPtr image) => GameplayText(_gameplayImages[image]);
    private string GameplayTypeName(IntPtr cls) => _types[_classes[cls]].Name;
    private IntPtr GameplayClassName(IntPtr cls) => GameplayText(GameplayTypeName(cls).Split('.').Last());
    private IntPtr GameplayClassImage(IntPtr cls)
        => cls == GameplayControllerClass || cls == GameplayControllerBaseClass
            || GameplayTypeName(cls) == "InLevel.LevelInformation"
            ? _gameplayImages.Single(row => row.Value == "Assembly-CSharp.dll").Key : IntPtr.Zero;
    private IntPtr GameplayNamespace(IntPtr cls) { var name = GameplayTypeName(cls); var end = name.LastIndexOf('.'); return GameplayText(end < 0 ? "" : name[..end]); }
    private IntPtr GameplayFindClass(IntPtr _, string ns, string name)
        => _classes.Keys.FirstOrDefault(cls => GameplayTypeName(cls) == (ns.Length == 0 ? name : ns + "." + name));
    private bool GameplayAssignable(IntPtr target, IntPtr source)
        => target == source || target == ClassOf(Object) || GameplayTypeName(target) == "UnityEngine.Object";
    private IntPtr GameplayTypeObject(IntPtr _) => Box(_gameplayType, new byte[8]);
    private IntPtr GameplayMethods(IntPtr cls, IntPtr iterator)
    {
        if (cls == ClassOf(_referenceArrayType)) return ClassMethods(cls, iterator);
        var rows = _gameplayMethods.Where(x => x.Value.Class == cls).ToArray(); var index = Marshal.ReadIntPtr(iterator).ToInt32();
        if (index >= rows.Length) return IntPtr.Zero; Marshal.WriteIntPtr(iterator, new IntPtr(index + 1)); return rows[index].Key;
    }
    private IntPtr GameplayMethodName(IntPtr method) => IsArrayMethod(method) ? MethodName(method) : GameplayText(_gameplayMethods[method].Name);
    private IntPtr GameplayMethodClass(IntPtr method) => IsArrayMethod(method) ? MethodClass(method) : _gameplayMethods[method].Class;
    private uint GameplayParamCount(IntPtr method) => IsArrayMethod(method) ? ParameterCount(method) : (uint)_gameplayMethods[method].Parameters.Length;
    private IntPtr GameplayParam(IntPtr method, uint index) => IsArrayMethod(method) ? ParameterType(method, index) : _gameplayMethods[method].Parameters[index];
    private IntPtr GameplayReturn(IntPtr method) => IsArrayMethod(method) ? GetReturnType(method) : _gameplayMethods[method].Return;
    private uint GameplayFlags(IntPtr method, out uint implementation) { implementation = 0; return !IsArrayMethod(method) && _gameplayMethods[method].Static ? 0x10u : 0; }
    private IntPtr GameplayFindField(IntPtr cls, string name)
    {
        if (name == MissingGameplayField) return IntPtr.Zero;
        var field = _structFields.TryGetValue(cls, out var fields)
            ? fields.FirstOrDefault(field => _fieldMetadata[field].Name == name) : IntPtr.Zero;
        return field == IntPtr.Zero && cls == GameplayControllerClass && name == "LevelInformation"
            ? GameplayFindField(_gameplayControllerParent, name) : field;
    }
    private IntPtr GameplayFieldParent(IntPtr field) => _gameplayFieldParents[field];
    private void GameplayReadField(IntPtr owner, IntPtr field, IntPtr output)
    {
        FieldReads++; AllGameplayReceiversRooted &= HasRootFor(owner);
        var bytes = _gameplayStorage[(owner, field)]; Marshal.Copy(bytes, 0, output, bytes.Length);
    }
    private IntPtr GameplayInvoke(IntPtr method, IntPtr instance, IntPtr arguments, IntPtr exception)
    {
        Invocations++; Marshal.WriteIntPtr(exception, IntPtr.Zero); AllGameplayReceiversRooted &= instance == IntPtr.Zero || HasRootFor(instance);
        if (IsArrayMethod(method)) return InvokeReferenceArray(method, instance, arguments, exception);
        var row = _gameplayMethods[method];
        switch (row.Name)
        {
            case "FindObjectsOfTypeAll": return GameplayControllers;
            case "op_Implicit": return Box(Boolean, new byte[] { 1 });
            case "get_isActiveAndEnabled": return Box(Boolean, new[] { GameplayActive ? (byte)1 : (byte)0 });
            case "get_CanDoOperation": return Box(Boolean, new[] { GameplayCanDo ? (byte)1 : (byte)0 });
            case "get_dspTime": return Box(_gameplayDouble, BitConverter.GetBytes(GameplayDsp));
            case "get_timeSinceLevelLoad": return Box(Single, BitConverter.GetBytes(100f));
            case "get_Blocked": case "get_HasActiveWindow": case "get_isPlaying": return Box(Boolean, new byte[] { 0 });
            case "get_pitch": return Box(Single, BitConverter.GetBytes(GameplayPitch));
            case "get_time": case "GetTime": return Box(Single, BitConverter.GetBytes(GameplayPosition));
            case "get_length": return Box(Single, BitConverter.GetBytes(GameplayLength));
            case "set_pitch":
                GameplayPitchRevision++; GameplayPitchWrites++; GameplayPitch = Marshal.PtrToStructure<float>(Marshal.ReadIntPtr(arguments));
                GameplayPitchAttempts.Add(GameplayPitch); AfterGameplayPitchWrite?.Invoke(); return IntPtr.Zero;
            case "Pause":
                GameplayCommandCalls++; SetGameplayField("pause.paused", true); SetGameplayField("progress.playing", false); return IntPtr.Zero;
            case "Resume":
                GameplayCommandCalls++; SetGameplayField("pause.paused", false); SetGameplayField("progress.playing", true); return IntPtr.Zero;
            case "Retry": case "Skip": GameplayCommandCalls++; return IntPtr.Zero;
            default: throw new InvalidOperationException("Unexpected gameplay fixture method " + row.Name);
        }
    }
}
