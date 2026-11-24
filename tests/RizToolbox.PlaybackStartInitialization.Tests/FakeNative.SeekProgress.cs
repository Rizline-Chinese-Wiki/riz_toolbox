using System.Reflection;
using System.Runtime.InteropServices;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

// The pinned image supplies code/profile evidence only. Its entries and DllMain
// are never executed. The owned source retains its own actual GetTime metadata;
// a separate metadata accessor resolves the fixed converter profile on the same
// progress/chart classes. This cannot issue a formal source or fixed consumer.
sealed partial class FakeNative
{
    internal IntPtr SeekProgressChart;
    internal NativeRuntime SeekProgressProfileApi = null!;
    internal Action<string>? DuringSeekProgressWriteMetadata, AfterSeekProgressStore;
    internal readonly List<(string Field, long Bits)> SeekProgressStores = new();
    internal readonly List<(string Method, double Input, float Output)> SeekProgressConversions = new();
    internal int SeekProgressForbiddenImageCalls;
    internal Exception? SeekProgressCallbackFailure;
    private readonly Dictionary<IntPtr, IntPtr> _seekProgressBuffers = new();
    private readonly Dictionary<IntPtr, GameplayMethod> _seekProgressProfileMethods = new();
    private readonly Dictionary<IntPtr, IntPtr> _seekProgressMethodNames = new();
    private IntPtr _seekProgressToChart, _seekProgressToReal, _seekProgressPinnedGetTime;

    internal void ConfigureSeekProgress(IntPtr mappedImage)
    {
        if (!UnityBackingConfigured || mappedImage == IntPtr.Zero || _seekProgressBuffers.Count != 0)
            throw new InvalidOperationException("Configure the actual owned Unity graph before its once-only Seek progress fixture.");
        var information = UnityType("InLevel.LevelInformation");
        var progress = UnityType("InLevel.ProgressControl");
        var chart = AddType("InLevel.Chart", 18, false, IntPtr.Size);
        SeekProgressChart = Box(chart, new byte[0x48]);
        Marshal.WriteIntPtr(SeekProgressChart, ClassOf(chart));
        GameplayField("info.chart", information, "chart", chart, GameplayInformation, SeekProgressChart);
        var shifts = AddType("System.Collections.Generic.List<InLevel.KeyPoint>", 21, false, IntPtr.Size);
        GameplayField("chart.bpm", chart, "bPM", Single, SeekProgressChart, 120f);
        GameplayField("chart.shifts", chart, "bpmShifts", shifts, SeekProgressChart, IntPtr.Zero);
        var delay = UnityAddField(ClassOf(chart), "chartDelayMs", Int32, 0x14);
        _gameplayStorage[(SeekProgressChart, delay)] = BitConverter.GetBytes(37000);

        foreach (var (key, offset) in new[]
        {
            ("info.nowTime", 0xb0), ("info.nowRealTime", 0xb4), ("info.nowSyncedTime", 0xb8),
            ("info.progress", 0x28), ("info.chart", 0x88), ("progress.info", 0x20),
            ("progress.preparing", 0x40), ("progress.playing", 0x42), ("progress.offset", 0x48),
            ("progress.now", 0x50), ("progress.ready", 0x65), ("chart.bpm", 0x30), ("chart.shifts", 0x38)
        })
        {
            var field = _gameplayFields[key].Field; var metadata = _fieldMetadata[field];
            _fieldMetadata[field] = (metadata.Name, metadata.Type, offset);
        }

        // Each cache has independent actual unmanaged storage. Both the actual
        // time source and the progress participant read these same buffers.
        foreach (var key in new[] { "progress.now", "info.nowTime", "info.nowRealTime", "info.nowSyncedTime" })
        {
            var row = _gameplayFields[key]; var bytes = _gameplayStorage[(row.Owner, row.Field)];
            var buffer = GameplayAllocate(bytes.Length); Marshal.Copy(bytes, 0, buffer, bytes.Length);
            _seekProgressBuffers.Add(row.Field, buffer);
        }
        _seekProgressToChart = SeekProgressMethod(mappedImage, ClassOf(progress), "RealTimeToChartTime", Single, 0x400c80, _gameplayDouble);
        _seekProgressToReal = SeekProgressMethod(mappedImage, ClassOf(progress), "ChartTimeToRealTime", Single, 0x400400, _gameplayDouble);
        SeekProgressMethod(mappedImage, ClassOf(progress), "Update", Void, 0x400fe0);
        SeekProgressMethod(mappedImage, ClassOf(chart), "get_ChartDelay", Single, 0x3e5670);
        _seekProgressPinnedGetTime = SeekProgressMethod(mappedImage, ClassOf(UnityType("Pigeon.MUGToolkit.Core.MUGMusicPlayer")),
            "GetTime", Single, 0x3fbb10, listedInOwnedApi: false);

        Add("il2cpp_field_get_value", "FieldGet", nameof(SeekProgressReadField));
        Add("il2cpp_field_get_parent", "Ptr1", nameof(SeekProgressFieldParent));
        Add("il2cpp_field_set_value", "FieldSetValue", nameof(SeekProgressWriteField));
        Add("il2cpp_runtime_invoke", "RuntimeInvoke", nameof(SeekProgressInvoke));
        Add("il2cpp_method_is_generic", "Predicate", nameof(SeekProgressGeneric));
        Add("il2cpp_method_is_inflated", "Predicate", nameof(SeekProgressInflated));
        GameplayApi = new NativeRuntime(ExportPointer); GameplayApi.BindCurrentThread();

        foreach (var row in _gameplayMethods.Where(row => row.Value.Name != "GetTime"))
            _seekProgressProfileMethods.Add(row.Key, row.Value);
        var profileExports = new Dictionary<string, Delegate>(_exports);
        foreach (var (name, kind, handler) in new[]
        {
            ("il2cpp_class_get_methods", "Ptr2", nameof(SeekProgressProfileMethodList)),
            ("il2cpp_method_get_name", "Ptr1", nameof(SeekProgressProfileMethodName)),
            ("il2cpp_method_get_class", "Ptr1", nameof(SeekProgressProfileMethodClass)),
            ("il2cpp_method_get_param_count", "Flags", nameof(SeekProgressProfileParameterCount)),
            ("il2cpp_method_get_param", "ParameterType", nameof(SeekProgressProfileParameter)),
            ("il2cpp_method_get_return_type", "Ptr1", nameof(SeekProgressProfileReturn)),
            ("il2cpp_method_get_flags", "MethodFlags", nameof(SeekProgressProfileFlags))
        })
        {
            var type = typeof(NativeRuntime).GetNestedType(kind, BindingFlags.NonPublic)!;
            profileExports[name] = Delegate.CreateDelegate(type, this, GetType().GetMethod(handler, BindingFlags.Instance | BindingFlags.NonPublic)!);
        }
        SeekProgressProfileApi = new NativeRuntime(name => Marshal.GetFunctionPointerForDelegate(profileExports[name]));
        SeekProgressProfileApi.BindCurrentThread();
    }

    private IntPtr SeekProgressMethod(IntPtr image, IntPtr cls, string name, IntPtr returns, int rva,
        IntPtr argument = default, bool listedInOwnedApi = true)
    {
        var parameters = argument == IntPtr.Zero ? Array.Empty<IntPtr>() : new[] { argument };
        var method = GameplayAllocate(Marshal.SizeOf<HookTargetValidation.MethodInfo2022>());
        var parameterBuffer = parameters.Length == 0 ? IntPtr.Zero : GameplayAllocate(IntPtr.Size);
        if (parameters.Length != 0) Marshal.WriteIntPtr(parameterBuffer, argument);
        var text = GameplayText(name); _seekProgressMethodNames.Add(method, text);
        Marshal.StructureToPtr(new HookTargetValidation.MethodInfo2022
        {
            MethodPointer = IntPtr.Add(image, rva), VirtualMethodPointer = IntPtr.Add(image, rva),
            Name = text, Class = cls, ReturnType = returns, Parameters = parameterBuffer,
            Slot = ushort.MaxValue, ParameterCount = (byte)parameters.Length
        }, method, false);
        var row = new GameplayMethod(cls, name, false, returns, parameters);
        if (listedInOwnedApi) _gameplayMethods.Add(method, row);
        else _seekProgressProfileMethods.Add(method, row);
        return method;
    }
    private bool SeekProgressGeneric(IntPtr method) => _seekProgressMethodNames.ContainsKey(method) ? false : IsGenericMethod(method);
    private bool SeekProgressInflated(IntPtr method) => _seekProgressMethodNames.ContainsKey(method) ? false : IsInflatedMethod(method);
    private IntPtr SeekProgressProfileMethodList(IntPtr cls, IntPtr iterator)
    {
        var methods = _seekProgressProfileMethods.Where(row => row.Value.Class == cls).ToArray();
        var index = Marshal.ReadIntPtr(iterator).ToInt32();
        if (index >= methods.Length) return IntPtr.Zero;
        Marshal.WriteIntPtr(iterator, new IntPtr(index + 1)); return methods[index].Key;
    }
    private IntPtr SeekProgressProfileMethodName(IntPtr method) => _seekProgressMethodNames.TryGetValue(method, out var name)
        ? name : GameplayText(_seekProgressProfileMethods[method].Name);
    private IntPtr SeekProgressProfileMethodClass(IntPtr method) => _seekProgressProfileMethods[method].Class;
    private uint SeekProgressProfileParameterCount(IntPtr method) => (uint)_seekProgressProfileMethods[method].Parameters.Length;
    private IntPtr SeekProgressProfileParameter(IntPtr method, uint index) => _seekProgressProfileMethods[method].Parameters[index];
    private IntPtr SeekProgressProfileReturn(IntPtr method) => _seekProgressProfileMethods[method].Return;
    private uint SeekProgressProfileFlags(IntPtr method, out uint implementation)
    { implementation = 0; return _seekProgressProfileMethods[method].Static ? 0x10u : 0; }

    private void SeekProgressReadField(IntPtr owner, IntPtr field, IntPtr output)
    {
        if (!_seekProgressBuffers.TryGetValue(field, out var buffer)) { UnityReadField(owner, field, output); return; }
        FieldReads++; AllGameplayReceiversRooted &= HasRootFor(owner);
        var bytes = new byte[_types[_fieldMetadata[field].Type].Size];
        Marshal.Copy(buffer, bytes, 0, bytes.Length); Marshal.Copy(bytes, 0, output, bytes.Length);
    }
    private IntPtr SeekProgressFieldParent(IntPtr field)
    {
        var parent = UnityFieldParent(field);
        if (_seekProgressBuffers.ContainsKey(field))
        {
            try { DuringSeekProgressWriteMetadata?.Invoke(_fieldMetadata[field].Name); }
            catch (Exception error) { SeekProgressCallbackFailure ??= error; }
        }
        return parent;
    }
    private void SeekProgressWriteField(IntPtr owner, IntPtr field, IntPtr input)
    {
        ValueFieldWrites++; AllFieldOwnersRooted &= HasRootFor(owner); AllGameplayReceiversRooted &= HasRootFor(owner);
        if (!_seekProgressBuffers.TryGetValue(field, out var buffer))
        { SeekProgressCallbackFailure ??= new InvalidOperationException("Unexpected field outside the four actual time caches."); return; }
        var bytes = new byte[_types[_fieldMetadata[field].Type].Size];
        Marshal.Copy(input, bytes, 0, bytes.Length); Marshal.Copy(bytes, 0, buffer, bytes.Length);
        SeekProgressStores.Add((_fieldMetadata[field].Name, bytes.Length == 8 ? BitConverter.ToInt64(bytes) : BitConverter.ToInt32(bytes)));
        try { AfterSeekProgressStore?.Invoke(_fieldMetadata[field].Name); }
        catch (Exception error) { SeekProgressCallbackFailure ??= error; }
    }
    private IntPtr SeekProgressInvoke(IntPtr method, IntPtr instance, IntPtr arguments, IntPtr exception)
    {
        if (method != _seekProgressToChart && method != _seekProgressToReal)
        {
            if (method == _seekProgressPinnedGetTime || _seekProgressMethodNames.ContainsKey(method))
            {
                ++SeekProgressForbiddenImageCalls;
                Marshal.WriteIntPtr(exception, MakeString("The offline mapped image must never execute.")); return IntPtr.Zero;
            }
            return SeekTimeInvoke(method, instance, arguments, exception);
        }
        Invocations++; AllGameplayReceiversRooted &= instance == GameplayProgress && HasRootFor(instance);
        Marshal.WriteIntPtr(exception, IntPtr.Zero);
        var input = Marshal.PtrToStructure<double>(Marshal.ReadIntPtr(arguments));
        // Controlled BPM=120 with a multiplier .5 from (real=4, chart=8).
        // This IL2CPP conversion shim is explicitly separate from the pinned
        // body evidence. A large chart delay is present and is never called.
        var result = (float)(method == _seekProgressToChart
            ? input <= 4 ? input * 2 : input - 4 + 8
            : input <= 8 ? input / 2 : input - 8 + 4);
        SeekProgressConversions.Add((_gameplayMethods[method].Name, input, result));
        return Box(Single, BitConverter.GetBytes(result));
    }
    // A later writer changes the same unmanaged storage read by the actual
    // participant. This injection does not dispatch a production inverse store.
    internal void InjectSeekProgressAuxiliaryCache(string name, float value)
    {
        if (name is not ("nowRealTime" or "nowSyncedTime") || !float.IsFinite(value))
            throw new ArgumentException("Choose an actual finite auxiliary progress cache.");
        Marshal.WriteInt32(_seekProgressBuffers[_gameplayFields["info." + name].Field], BitConverter.SingleToInt32Bits(value));
    }

    internal (double Progress, float Chart, float Real, float Synced) ReadSeekProgressBuffers()
    {
        double ReadDouble(string key) => Marshal.PtrToStructure<double>(_seekProgressBuffers[_gameplayFields[key].Field]);
        float ReadSingle(string key) => Marshal.PtrToStructure<float>(_seekProgressBuffers[_gameplayFields[key].Field]);
        return (ReadDouble("progress.now"), ReadSingle("info.nowTime"), ReadSingle("info.nowRealTime"), ReadSingle("info.nowSyncedTime"));
    }
}
