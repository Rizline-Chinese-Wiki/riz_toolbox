using System.Runtime.InteropServices;
using RizToolbox.Runtime;

sealed partial class FakeNative
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr ResourceCreateClip(ulong tag);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr ResourceCreateReference(IntPtr clip);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr ResourceCreateReceiver(uint classIndex, IntPtr source, IntPtr reference);
    private IntPtr _progressResourceType, _progressResourceMethod, _progressResourceGetMethod, _progressResourceField;
    internal IntPtr ProgressResourceA, ProgressResourceB, ProgressResourceClipA, ProgressResourceClipB;
    internal int ProgressResourceRuntimeInvocations;
    internal IntPtr ProgressResourceType => _progressResourceType;
    internal IntPtr ProgressResourceMethod => _progressResourceMethod;
    internal IntPtr ProgressResourceField => _progressResourceField;

    internal void ConfigureProgressResource(IntPtr module)
    {
        if (_stockModule != module) throw new InvalidOperationException("Configure the actual rooted initializer first.");
        var progressType = _types.Single(row => row.Value.Name == "InLevel.ProgressControl").Key;
        var clipType = _types.Single(row => row.Value.Name == "UnityEngine.AudioClip").Key;
        _progressResourceType = AddType("IResourceReference<UnityEngine.AudioClip>", 21, false, IntPtr.Size);
        _progressResourceMethod = Marshal.GetDelegateForFunctionPointer<RootedMethod>(NativeLibrary.GetExport(module, "StockMethodProgressClip"))();
        _progressResourceGetMethod = Marshal.GetDelegateForFunctionPointer<RootedMethod>(NativeLibrary.GetExport(module, "StockMethodResourceGet"))();
        _gameplayMethods[_progressResourceMethod] = new(ClassOf(progressType), "set_Clip", false, Void, new[] { _progressResourceType });
        _gameplayMethods[_progressResourceGetMethod] = new(ClassOf(_progressResourceType), "get_Resource", false, clipType, Array.Empty<IntPtr>());
        Marshal.WriteIntPtr(_progressResourceMethod, 32, ClassOf(progressType));
        Marshal.WriteIntPtr(_progressResourceGetMethod, 32, ClassOf(_progressResourceType));

        ProgressResourceClipA = CreateProgressResourceClip(0x41);
        ProgressResourceClipB = CreateProgressResourceClip(0x42);
        GameplayClip = ProgressResourceClipA;
        Marshal.WriteIntPtr(GameplaySource, 40, ProgressResourceClipA);
        ProgressResourceA = Marshal.ReadIntPtr(GameplayProgress, 0x30);
        if (ProgressResourceA == IntPtr.Zero) ProgressResourceA = CreateProgressResourceReference(ProgressResourceClipA);
        else
        {
            RegisterProgressResourceReference(ProgressResourceA);
            Marshal.WriteIntPtr(ProgressResourceA, 0x18, ProgressResourceClipA);
        }
        ProgressResourceB = CreateProgressResourceReference(ProgressResourceClipB);
        Marshal.WriteIntPtr(GameplayProgress, 0x30, ProgressResourceA);
        GameplayField("progress.resource", progressType, "<Clip>k__BackingField", _progressResourceType,
            GameplayProgress, ProgressResourceA);
        _progressResourceField = _gameplayFields["progress.resource"].Field;
        _fieldMetadata[_progressResourceField] = ("<Clip>k__BackingField", _progressResourceType, 0x30);

        Add("il2cpp_class_get_name", "Ptr1", nameof(ProgressResourceClassName));
        Add("il2cpp_class_get_namespace", "Ptr1", nameof(ProgressResourceClassNamespace));
        Add("il2cpp_class_get_image", "Ptr1", nameof(ProgressResourceClassImage));
        Add("il2cpp_class_is_generic", "Predicate", nameof(ProgressResourceClassIsGeneric));
        Add("il2cpp_class_is_inflated", "Predicate", nameof(ProgressResourceClassIsInflated));
        Add("il2cpp_class_instance_size", "ArraySize", nameof(ProgressResourceClassInstanceSize));
        Add("il2cpp_field_get_value", "FieldGet", nameof(ProgressResourceReadField));
        // DirectAudioInvoke already forwards through SharedConstructorInvoke.
        // The latter handles these exact native metadata rows before fallback.
        GameplayApi = new NativeRuntime(ExportPointer); GameplayApi.BindCurrentThread();
    }
    internal IntPtr CreateProgressResourceClip(ulong tag)
    {
        var clip = Marshal.GetDelegateForFunctionPointer<ResourceCreateClip>(NativeLibrary.GetExport(_stockModule, "StockFixtureCreateAudioClip"))(tag);
        var type = _types.Single(row => row.Value.Name == "UnityEngine.AudioClip").Key;
        _boxedTypes[clip] = type; Marshal.WriteIntPtr(clip, ClassOf(type));
        return clip;
    }
    internal IntPtr CreateProgressResourceReference(IntPtr clip)
    {
        var reference = Marshal.GetDelegateForFunctionPointer<ResourceCreateReference>(NativeLibrary.GetExport(_stockModule, "StockFixtureCreateResourceReference"))(clip);
        RegisterProgressResourceReference(reference);
        return reference;
    }
    internal void RegisterProgressResourceEnumerator(IntPtr value)
    {
        if (value == IntPtr.Zero || Marshal.ReadIntPtr(value, 0x20) != GameplayProgress)
            throw new InvalidOperationException("Actual resource enumerator has no matching native Progress.");
        var type = _types.Single(row => row.Value.Name == "InLevel.ProgressControl.<Start>d__25").Key;
        _boxedTypes[value] = type;
        Marshal.WriteIntPtr(value, ClassOf(type));
    }
    internal void AdoptProgressResourceClip(IntPtr expected)
    {
        var type = _types.Single(row => row.Value.Name == "UnityEngine.AudioClip").Key;
        if (expected == IntPtr.Zero || Marshal.ReadIntPtr(GameplaySource, 40) != expected
            || !_boxedTypes.TryGetValue(expected, out var actualType) || actualType != type
            || Marshal.ReadIntPtr(expected) != ClassOf(type))
            throw new InvalidOperationException("Actual resource consumer did not install the expected registered AudioClip.");
        // The physical set_clip has already changed source+40. Only the fake
        // occurrence/API identity follows that verified native result here.
        GameplayClip = expected;
    }
    private void RegisterProgressResourceReference(IntPtr reference)
    {
        _boxedTypes[reference] = _progressResourceType;
        Marshal.WriteIntPtr(reference, ClassOf(_progressResourceType));
    }
    internal IntPtr CreateProgressResourceReceiver(uint classIndex, IntPtr reference)
    {
        var receiver = Marshal.GetDelegateForFunctionPointer<ResourceCreateReceiver>(NativeLibrary.GetExport(_stockModule,
            "StockFixtureCreateProgressResourceReceiver"))(classIndex, GameplaySource, reference);
        var type = classIndex == 0 ? _boxedTypes[GameplayProgress] : AddType("Fixture.ForeignResourceReceiver" + classIndex, 18, false, 0x70);
        _boxedTypes[receiver] = type; Marshal.WriteIntPtr(receiver, ClassOf(type));
        return receiver;
    }
    private IntPtr ProgressResourceClassName(IntPtr cls)
        => cls == ClassOf(_progressResourceType) ? GameplayText("IResourceReference`1") : GameplayClassName(cls);
    private IntPtr ProgressResourceClassNamespace(IntPtr cls)
        => cls == ClassOf(_progressResourceType) ? GameplayText("") : GameplayNamespace(cls);
    private IntPtr ProgressResourceClassImage(IntPtr cls)
        => _classes.ContainsKey(cls) ? _gameplayImages.Single(row => row.Value == "Assembly-CSharp.dll").Key : IntPtr.Zero;
    private bool ProgressResourceClassIsGeneric(IntPtr _) => false;
    private bool ProgressResourceClassIsInflated(IntPtr cls) => cls == ClassOf(_progressResourceType);
    private uint ProgressResourceClassInstanceSize(IntPtr cls)
    {
        if (cls == ClassOf(_progressResourceType)) return 0x40;
        var type = _classes[cls];
        return _types[type].Name switch {
            "InLevel.ProgressControl" => 0x70,
            "Pigeon.MUGToolkit.Core.MUGMusicPlayer" => 0x80,
            "UnityEngine.AudioSource" => 0xa0,
            "UnityEngine.AudioClip" => 0x18,
            "InLevel.ProgressControl.<Start>d__25" => 0x60,
            _ => checked((uint)Math.Max(0x20, _types[type].Size + 0x10))
        };
    }
    private void ProgressResourceReadField(IntPtr owner, IntPtr field, IntPtr output)
    {
        if (field == _progressResourceField)
        {
            FieldReads++; AllGameplayReceiversRooted &= HasRootFor(owner);
            Marshal.WriteIntPtr(output, Marshal.ReadIntPtr(owner, 0x30)); return;
        }
        RootedStockReadField(owner, field, output);
    }
    private bool TryProgressResourceInvoke(IntPtr method, IntPtr instance, IntPtr arguments, IntPtr exception, out IntPtr result)
    {
        result = IntPtr.Zero;
        if (_progressResourceMethod == IntPtr.Zero || method != _progressResourceMethod) return false;
        Marshal.WriteIntPtr(exception, IntPtr.Zero);
        var reference = Marshal.ReadIntPtr(arguments);
        AllGameplayReceiversRooted &= HasRootFor(instance) && (reference == IntPtr.Zero || HasRootFor(reference));
        ++ProgressResourceRuntimeInvocations;
        var invoke = Marshal.GetDelegateForFunctionPointer<RootedInvoke>(NativeLibrary.GetExport(_stockModule, "StockFixtureInvoke"));
        if (invoke(4, NativeLibrary.GetExport(_stockModule, "StockProgressClip"), instance,
            unchecked((ulong)reference.ToInt64()), method, out var code) != 1)
            throw new InvalidOperationException("Actual resource setter Runtime invocation failed: " + code.ToString("x"));
        return true;
    }
}
