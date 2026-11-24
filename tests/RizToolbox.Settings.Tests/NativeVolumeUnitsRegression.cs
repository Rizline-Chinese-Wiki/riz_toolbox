using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using RizToolbox.Adapter.Settings;
using RizToolbox.Core;
using RizToolbox.Sdk;

internal static class NativeVolumeUnitsRegression
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        foreach (var property in new[] { "MasterVolume", "HitFXVolume", "UISEVolume", "MusicVolume" })
        {
            var captured = property;
            yield return ($"native {property} maps percent slider to scalar setter units and restores exact original", () => ScalarUnits(captured));
        }
        yield return ("native volume ranges remain distinct across four sliders and withdraw with the UI", IndependentRanges);
    }

    private static void Require(bool success, string message) { if (!success) throw new InvalidOperationException(message); }
    private static void Exact(float expected, float actual)
        => Require(BitConverter.SingleToInt32Bits(expected) == BitConverter.SingleToInt32Bits(actual), $"Expected exact {expected:R}, got {actual:R}.");

    private static void ScalarUnits(string property)
    {
        using var main = new MainThreadDispatcher(); using var native = new SettingsNativeFixture();
        using var backend = native.Backend(main); using var service = new GameSettingsService(main, new TestLogger(), backend);
        Require(service.Refresh().Success, "Actual native backend/service refresh failed.");
        using var host = new PluginHost(new(new("test", "test", "test", "test")), main, new EventHub(new TestLogger()), new TestLogger());
        SettingViews? low = null, high = null;
        Require(host.Register(new Probe("native.low", context => low = new(service.ReadForPlugin(context), service.OverrideForPlugin(context)))).Success, "Low registration failed.");
        Require(host.Register(new Probe("native.high", context => high = new(service.ReadForPlugin(context), service.OverrideForPlugin(context)))).Success, "High registration failed.");
        Require(host.Enable("native.low").Success && host.Enable("native.high").Success, "Ordinary plugin enable failed.");
        string id = NativeGameSettingsBackend.Definitions.Single(definition => definition.Property == property).Id;
        float original = native.Values[property];
        var before = low!.Read.Read(id); Require(before.Success, "Initial native read failed."); Exact(original, before.Value!.Value);
        var descriptor = before.Value.Descriptor;
        Require(descriptor.Minimum == 0 && descriptor.Maximum == 1 && !descriptor.WholeNumbers,
            $"{property} exposed slider units as setter units: min={descriptor.Minimum}, max={descriptor.Maximum}, whole={descriptor.WholeNumbers}.");
        Require(descriptor.Unit.Contains("scalar", StringComparison.OrdinalIgnoreCase)
            && descriptor.Unit.Contains("continuous", StringComparison.OrdinalIgnoreCase), "Descriptor omits scalar units or continuous setter semantics.");
        Require(native.Writes.Count == 0, "Reading native descriptors changed settings.");
        int writes = native.Writes.Count;
        Require(low.Write.Override(new(id, 99)).Error?.Code == ErrorCode.InvalidArgument && native.Writes.Count == writes,
            "99 percent was accepted as a normalized scalar or reached the setter.");
        var a = low.Write.Override(new(id, .995f, 10)); Require(a.Success, "Continuous scalar .995 rejected: " + a.Error?.Message);
        Exact(.995f, native.Values[property]); Exact(.995f, low.Read.Read(id).Value!.Value);
        float upper = MathF.BitDecrement(.995f);
        var b = high!.Write.Override(new(id, upper, 20)); Require(b.Success, "Upper scalar layer rejected: " + b.Error?.Message);
        Exact(upper, native.Values[property]); Exact(upper, high.Read.Read(id).Value!.Value);
        float updated = MathF.BitDecrement(upper);
        var changed = b.Value!.SetValue(updated); Require(changed.Success, "Setting lease update rejected: " + changed.Error?.Message);
        Exact(updated, native.Values[property]); Exact(updated, high.Read.Read(id).Value!.Value);
        Require(native.Writes.All(write => write.Property == property), "A neighbor volume was written.");
        Require(host.Disable("native.high").Success, "Upper plugin stop failed.");
        Exact(.995f, native.Values[property]); Exact(.995f, low.Read.Read(id).Value!.Value);
        Require(host.Disable("native.low").Success, "Lower plugin stop failed.");
        Exact(original, native.Values[property]);
        // Read back through a fresh ordinary reader after both setting owners stopped.
        IGameSettingsReadService? reader = null;
        Require(host.Register(new Probe("native.reader", context => reader = service.ReadForPlugin(context))).Success
            && host.Enable("native.reader").Success, "Restoration reader failed.");
        Exact(original, reader!.Read(id).Value!.Value);
        Require(native.Writes.Count == 5 && native.ActiveRoots == 0, "Expected two applies, one update, two releases and no surviving native roots.");
    }

    private static void IndependentRanges()
    {
        using var main = new MainThreadDispatcher(); using var native = new SettingsNativeFixture();
        native.Sliders["MasterVolume"] = (10, 90, true);
        native.Sliders["HitFXVolume"] = (20, 80, false);
        native.Sliders["UISEVolume"] = (30, 70, true);
        native.Sliders["MusicVolume"] = (40, 60, false);
        using var backend = native.Backend(main);
        var ranges = backend.GetWriteRanges(); Require(ranges.Success, "Actual native range probe failed: " + ranges.Error?.Message);
        foreach (var definition in NativeGameSettingsBackend.Definitions)
        {
            var expected = native.Sliders[definition.Property]; var actual = ranges.Value![definition.Id];
            Exact(expected.Minimum / 100f, actual.Minimum); Exact(expected.Maximum / 100f, actual.Maximum);
            Require(!actual.WholeNumbers, "UI display quantization became a restriction on the continuous setter.");
        }
        native.UiActive = false;
        Require(backend.GetWriteRanges().Error?.Code == ErrorCode.NotReady && native.ActiveRoots == 0
            && native.Writes.Count == 0, "Closed UI retained write ranges, leaked roots or changed a setting.");
    }
}

// Controlled IL2CPP export/metadata buffers exercise the actual NativeGameSettingsBackend,
// NativeRuntime ABI checks, GameSettingsService and plugin scope cleanup. No game image is loaded.
// Reflection supplies the existing internal export seam without expanding Runtime's friend list.
internal sealed class SettingsNativeFixture : IDisposable
{
    private sealed record NativeType(string Name, int Code, bool IsValue, int Size);
    private sealed record NativeMethod(IntPtr Owner, string Name, IntPtr Return, bool Static, IntPtr[] Parameters);
    private sealed record NativeField(IntPtr Owner, string Name, IntPtr Type, bool Static, int Offset = 0);
    private static readonly Type RuntimeType = Assembly.Load("RizToolbox.Runtime").GetType("RizToolbox.Runtime.NativeRuntime", true)!;
    private readonly Dictionary<IntPtr, NativeType> _types = new();
    private readonly Dictionary<IntPtr, NativeMethod> _methods = new();
    private readonly Dictionary<IntPtr, NativeField> _fields = new();
    private readonly Dictionary<IntPtr, IntPtr> _objectTypes = new();
    private readonly Dictionary<IntPtr, string> _images = new();
    private readonly Dictionary<uint, IntPtr> _handles = new();
    private readonly Dictionary<string, Delegate> _exports = new();
    private readonly List<IntPtr> _allocations = new();
    private readonly Dictionary<IntPtr, string> _sliderProperties = new();
    private readonly IntPtr _void, _bool, _int, _single, _object, _systemType, _settings, _manager, _ui, _resources, _scene, _unity, _slider, _gameObject, _array;
    private readonly IntPtr _uiObject, _managerObject, _gameObjectValue, _arrayObject, _typeObject, _assemblies;
    private readonly object _api;
    private uint _nextHandle;
    internal bool UiActive = true;
    internal int ActiveRoots => _handles.Count;
    internal Dictionary<string, float> Values { get; } = new(StringComparer.Ordinal)
    { ["MasterVolume"] = .8765432f, ["HitFXVolume"] = .7654321f, ["UISEVolume"] = .6543219f, ["MusicVolume"] = .9876543f };
    internal Dictionary<string, (float Minimum, float Maximum, bool Whole)> Sliders { get; } = new(StringComparer.Ordinal);
    internal List<(string Property, float Value)> Writes { get; } = new();

    internal SettingsNativeFixture()
    {
        _void = Type("System.Void", 1, true, 0); _bool = Type("System.Boolean", 2, true, 1);
        _int = Type("System.Int32", 8, true, 4); _single = Type("System.Single", 12, true, 4);
        _object = Type("System.Object"); _systemType = Type("System.Type");
        _settings = Type("SettingsControl"); _manager = Type("GameManager"); _ui = Type("LevelListSettingsControl");
        _resources = Type("UnityEngine.Resources"); _unity = Type("UnityEngine.Object"); _slider = Type("UnityEngine.UI.Slider");
        _scene = Type("UnityEngine.SceneManagement.Scene", 17, true, 4); _gameObject = Type("UnityEngine.GameObject");
        _array = Type("LevelListSettingsControl[]", 29);
        _uiObject = Object(_ui); _managerObject = Object(_manager); _gameObjectValue = Object(_gameObject);
        _arrayObject = Object(_array); _typeObject = Object(_systemType);
        Field(_scene, "m_Handle", _int, false, 16); Field(_manager, "Instance", _manager, true);
        foreach (var definition in NativeGameSettingsBackend.Definitions)
        {
            Method(_settings, "get_" + definition.Property, _single, true);
            Method(_settings, "set_" + definition.Property, _void, true, _single);
            Field(_ui, definition.SliderField, _slider);
            _sliderProperties.Add(Object(_slider), definition.Property);
            Sliders.Add(definition.Property, (0, 100, true));
        }
        Method(_resources, "FindObjectsOfTypeAll", _array, true, _systemType);
        Method(_ui, "get_isActiveAndEnabled", _bool); Method(_ui, "get_gameObject", _gameObject);
        Method(_gameObject, "get_scene", _scene); Method(_scene, "IsValidInternal", _bool, true, _int);
        Method(_scene, "GetIsLoadedInternal", _bool, true, _int); Method(_unity, "op_Implicit", _bool, true, _unity);
        Method(_slider, "get_minValue", _single); Method(_slider, "get_maxValue", _single); Method(_slider, "get_wholeNumbers", _bool);
        Method(_array, "get_Rank", _int); Method(_array, "get_Length", _int); Method(_array, "GetValue", _object, false, _int);
        _images.Add(new(11), "Assembly-CSharp.dll"); _images.Add(new(12), "UnityEngine.CoreModule.dll"); _images.Add(new(13), "UnityEngine.UI.dll");
        _assemblies = Allocate(IntPtr.Size * _images.Count); int at = 0;
        foreach (var image in _images.Keys) Marshal.WriteIntPtr(_assemblies, at++ * IntPtr.Size, image);
        Export("il2cpp_domain_get", "Ptr0", nameof(Domain)); Export("il2cpp_domain_get_assemblies", "Ptr2", nameof(Assemblies));
        Export("il2cpp_assembly_get_image", "Ptr1", nameof(Identity)); Export("il2cpp_image_get_name", "Ptr1", nameof(ImageName));
        Export("il2cpp_class_from_name", "ClassLookup", nameof(FindClass)); Export("il2cpp_class_get_method_from_name", "MethodLookup");
        Export("il2cpp_object_get_class", "Ptr1", nameof(ObjectClass)); Export("il2cpp_class_get_parent", "Ptr1");
        Export("il2cpp_class_get_name", "Ptr1", nameof(ClassName)); Export("il2cpp_class_get_namespace", "Ptr1", nameof(ClassNamespace));
        Export("il2cpp_method_get_name", "Ptr1", nameof(MethodName)); Export("il2cpp_method_get_class", "Ptr1", nameof(MethodClass));
        Export("il2cpp_object_unbox", "Ptr1", nameof(Identity)); Export("il2cpp_object_new", "Ptr1");
        Export("il2cpp_class_get_type", "Ptr1", nameof(Identity)); Export("il2cpp_type_get_object", "Ptr1", nameof(TypeObject));
        Export("il2cpp_class_get_field_from_name", "FieldLookup", nameof(FindField)); Export("il2cpp_field_get_type", "Ptr1", nameof(FieldType));
        Export("il2cpp_field_get_parent", "Ptr1", nameof(FieldParent)); Export("il2cpp_field_get_flags", "Flags", nameof(FieldFlags));
        Export("il2cpp_field_get_value", "FieldGet", nameof(ReadField)); Export("il2cpp_field_static_get_value", "StaticGet", nameof(ReadStatic));
        Export("il2cpp_class_get_methods", "Ptr2", nameof(Methods)); Export("il2cpp_method_get_param_count", "Flags", nameof(ParameterCount));
        Export("il2cpp_method_get_flags", "MethodFlags", nameof(MethodFlags)); Export("il2cpp_method_get_param", "ParameterType", nameof(Parameter));
        Export("il2cpp_method_get_return_type", "Ptr1", nameof(Return)); Export("il2cpp_type_get_name", "Ptr1", nameof(TypeName));
        Export("il2cpp_free", "FreePointer", nameof(Free)); Export("il2cpp_gchandle_new", "NewHandle", nameof(NewHandle));
        Export("il2cpp_gchandle_free", "FreeHandle", nameof(FreeHandle)); Export("il2cpp_gchandle_get_target", "HandleTarget", nameof(HandleTarget));
        Export("il2cpp_string_new", "StringNew"); Export("il2cpp_string_length", "StringLength"); Export("il2cpp_string_chars", "Ptr1");
        Export("il2cpp_runtime_invoke", "RuntimeInvoke", nameof(Invoke)); Export("il2cpp_class_from_type", "Ptr1", nameof(Identity));
        Export("il2cpp_class_enum_basetype", "Ptr1"); Export("il2cpp_type_is_byref", "Predicate");
        Export("il2cpp_class_is_valuetype", "Predicate", nameof(IsValue)); Export("il2cpp_class_is_enum", "Predicate");
        Export("il2cpp_class_is_assignable_from", "Assignable", nameof(Assignable)); Export("il2cpp_class_value_size", "ValueSize", nameof(ValueSize));
        Export("il2cpp_type_get_type", "TypeCode", nameof(TypeCode)); Export("il2cpp_class_get_fields", "Ptr2", nameof(Fields));
        Export("il2cpp_field_get_name", "Ptr1", nameof(FieldName)); Export("il2cpp_field_get_offset", "FieldOffset", nameof(FieldOffset));
        Export("il2cpp_method_is_generic", "Predicate"); Export("il2cpp_method_is_inflated", "Predicate");
        Func<string, IntPtr> exports = name => Marshal.GetFunctionPointerForDelegate(_exports[name]);
        _api = Activator.CreateInstance(RuntimeType, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { exports }, null)!;
        RuntimeType.GetMethod("BindCurrentThread", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_api, null);
    }

    internal NativeGameSettingsBackend Backend(IMainThreadDispatcher main)
        => (NativeGameSettingsBackend)Activator.CreateInstance(typeof(NativeGameSettingsBackend), BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { _api, main }, null)!;
    private IntPtr Type(string name, int code = 18, bool value = false, int size = 8)
    { var pointer = new IntPtr(1000 + _types.Count); _types.Add(pointer, new(name, code, value, size)); return pointer; }
    private void Method(IntPtr owner, string name, IntPtr returns, bool isStatic = false, params IntPtr[] parameters)
        => _methods.Add(new(2000 + _methods.Count), new(owner, name, returns, isStatic, parameters));
    private void Field(IntPtr owner, string name, IntPtr type, bool isStatic = false, int offset = 0)
        => _fields.Add(new(3000 + _fields.Count), new(owner, name, type, isStatic, offset));
    private IntPtr Allocate(int bytes) { var memory = Marshal.AllocHGlobal(Math.Max(bytes, 8)); _allocations.Add(memory); return memory; }
    private IntPtr Object(IntPtr type) { var memory = Allocate(8); _objectTypes.Add(memory, type); return memory; }
    private IntPtr Box(IntPtr type, int bits) { var memory = Object(type); Marshal.WriteInt32(memory, bits); return memory; }
    private IntPtr Ansi(string text) { var pointer = Marshal.StringToHGlobalAnsi(text); _allocations.Add(pointer); return pointer; }
    private IntPtr Domain() => new(1);
    private IntPtr Assemblies(IntPtr _, IntPtr count) { Marshal.WriteIntPtr(count, new IntPtr(_images.Count)); return _assemblies; }
    private IntPtr Identity(IntPtr value) => value;
    private IntPtr ImageName(IntPtr image) => Ansi(_images[image]);
    private IntPtr FindClass(IntPtr image, string ns, string name)
        => _types.SingleOrDefault(pair => pair.Value.Name == (ns.Length == 0 ? name : ns + "." + name)).Key;
    private IntPtr ClassName(IntPtr type) => Ansi(_types[type].Name.Split('.').Last());
    private IntPtr ClassNamespace(IntPtr type) => Ansi(_types[type].Name.Contains('.') ? _types[type].Name[.._types[type].Name.LastIndexOf('.')] : "");
    private IntPtr ObjectClass(IntPtr value) => _objectTypes[value];
    private IntPtr TypeObject(IntPtr _) => _typeObject;
    private IntPtr MethodName(IntPtr method) => Ansi(_methods[method].Name);
    private IntPtr MethodClass(IntPtr method) => _methods[method].Owner;
    private uint ParameterCount(IntPtr method) => (uint)_methods[method].Parameters.Length;
    private uint MethodFlags(IntPtr method, out uint implementation) { implementation = 0; return _methods[method].Static ? 0x10u : 0u; }
    private IntPtr Parameter(IntPtr method, uint index) => _methods[method].Parameters[index];
    private IntPtr Return(IntPtr method) => _methods[method].Return;
    private IntPtr TypeName(IntPtr type) => Marshal.StringToCoTaskMemUTF8(_types[type].Name);
    private void Free(IntPtr pointer) => Marshal.FreeCoTaskMem(pointer);
    private bool IsValue(IntPtr type) => _types[type].IsValue;
    private bool Assignable(IntPtr target, IntPtr actual) => target == _object || target == _unity || target == actual;
    private int ValueSize(IntPtr type, out uint alignment) { alignment = 4; return _types[type].Size; }
    private int TypeCode(IntPtr type) => _types[type].Code;
    private IntPtr Methods(IntPtr owner, IntPtr iterator) => Next(_methods.Where(pair => pair.Value.Owner == owner).Select(pair => pair.Key), iterator);
    private IntPtr Fields(IntPtr owner, IntPtr iterator) => Next(_fields.Where(pair => pair.Value.Owner == owner).Select(pair => pair.Key), iterator);
    private static IntPtr Next(IEnumerable<IntPtr> values, IntPtr iterator)
    { int index = Marshal.ReadIntPtr(iterator).ToInt32(); var item = values.ElementAtOrDefault(index); if (item != IntPtr.Zero) Marshal.WriteIntPtr(iterator, new IntPtr(index + 1)); return item; }
    private IntPtr FindField(IntPtr owner, string name) => _fields.SingleOrDefault(pair => pair.Value.Owner == owner && pair.Value.Name == name).Key;
    private IntPtr FieldType(IntPtr field) => _fields[field].Type;
    private IntPtr FieldParent(IntPtr field) => _fields[field].Owner;
    private uint FieldFlags(IntPtr field) => _fields[field].Static ? 0x10u : 0u;
    private IntPtr FieldName(IntPtr field) => Ansi(_fields[field].Name);
    private int FieldOffset(IntPtr field) => _fields[field].Offset;
    private void ReadField(IntPtr owner, IntPtr field, IntPtr output)
    {
        string property = NativeGameSettingsBackend.Definitions.Single(definition => definition.SliderField == _fields[field].Name).Property;
        Marshal.WriteIntPtr(output, _sliderProperties.Single(pair => pair.Value == property).Key);
    }
    private void ReadStatic(IntPtr field, IntPtr output) => Marshal.WriteIntPtr(output, _managerObject);
    private uint NewHandle(IntPtr value, bool pinned) { uint handle = ++_nextHandle; _handles.Add(handle, value); return handle; }
    private void FreeHandle(uint handle) { if (!_handles.Remove(handle)) throw new InvalidOperationException("Double root free."); }
    private IntPtr HandleTarget(uint handle) => _handles[handle];
    private IntPtr Invoke(IntPtr method, IntPtr instance, IntPtr arguments, IntPtr exception)
    {
        Marshal.WriteIntPtr(exception, IntPtr.Zero); var metadata = _methods[method]; string name = metadata.Name;
        if (instance != IntPtr.Zero && !_handles.Values.Contains(instance)) throw new InvalidOperationException("Unrooted receiver.");
        if (metadata.Owner == _settings)
        {
            string property = name[4..];
            if (name.StartsWith("get_")) return Box(_single, BitConverter.SingleToInt32Bits(Values[property]));
            float value = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(Marshal.ReadIntPtr(arguments)));
            Values[property] = value; Writes.Add((property, value)); return IntPtr.Zero;
        }
        if (name == "FindObjectsOfTypeAll") return _arrayObject;
        if (name == "get_Rank" || name == "get_Length") return Box(_int, 1);
        if (name == "GetValue") return _uiObject;
        if (name == "get_gameObject") return _gameObjectValue;
        if (name == "get_scene") return Box(_scene, 7);
        if (name == "get_isActiveAndEnabled") return Box(_bool, UiActive ? 1 : 0);
        if (name is "op_Implicit" or "IsValidInternal" or "GetIsLoadedInternal") return Box(_bool, 1);
        if (metadata.Owner == _slider)
        {
            var slider = Sliders[_sliderProperties[instance]];
            return name == "get_wholeNumbers" ? Box(_bool, slider.Whole ? 1 : 0)
                : Box(_single, BitConverter.SingleToInt32Bits(name == "get_minValue" ? slider.Minimum : slider.Maximum));
        }
        throw new InvalidOperationException("Unexpected native invocation: " + name);
    }
    private void Export(string name, string delegateName, string? handler = null)
    {
        var delegateType = RuntimeType.GetNestedType(delegateName, BindingFlags.NonPublic)!;
        if (handler is not null) _exports[name] = Delegate.CreateDelegate(delegateType, this,
            GetType().GetMethod(handler, BindingFlags.Instance | BindingFlags.NonPublic)!);
        else
        {
            var invoke = delegateType.GetMethod("Invoke")!;
            var parameters = invoke.GetParameters().Select(parameter => Expression.Parameter(parameter.ParameterType)).ToArray();
            var body = new List<Expression>();
            foreach (var parameter in parameters.Where(parameter => parameter.Type.IsByRef)) body.Add(Expression.Assign(parameter, Expression.Default(parameter.Type.GetElementType()!)));
            body.Add(Expression.Default(invoke.ReturnType));
            _exports[name] = Expression.Lambda(delegateType, Expression.Block(body), parameters).Compile();
        }
    }
    public void Dispose()
    {
        if (_handles.Count != 0) throw new InvalidOperationException("Native roots survived the regression.");
        foreach (var pointer in _allocations) Marshal.FreeHGlobal(pointer);
        GC.KeepAlive(_exports);
    }
}
