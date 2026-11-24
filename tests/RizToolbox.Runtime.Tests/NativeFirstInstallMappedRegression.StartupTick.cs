using System.Reflection;
using System.Runtime.InteropServices;
using RizToolbox.Runtime;

internal static partial class NativeFirstInstallMappedRegression
{
    private const string StartupAssembly = "Assembly-CSharp.dll";

    private static RuntimeBridge CreateStartupConsumer(FakeNative native)
    {
        var consumer = new RuntimeBridge(StartupAssembly, "", "GameManager", "Update", (_, _) => { }, new Logger());
        typeof(RuntimeBridge).GetField("_api", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(consumer, native.CreateStartupTickRuntime());
        return consumer;
    }

    private static void TickStartupProfileBinding()
    {
        using var native = new FakeNative();
        var consumer = CreateStartupConsumer(native);
        var h = new TickAdmissionHarness(matchesTick: consumer.MatchesTick);
        foreach (var impostor in new[] { FakeNative.StartupOtherImage, FakeNative.StartupOverload, FakeNative.StartupOtherMetadata })
        {
            var frame = TickAdmissionHarness.Frame(impostor);
            frame.OsThreadId++;
            h.Admission.Observe(frame, frame.OsThreadId);
            TickCheck(h.Dispatches == 0 && h.BoundTick == IntPtr.Zero && h.Faults == 0 && !h.Admission.Unknown,
                $"Startup accepted or faulted on unrelated Update metadata {impostor}.");
        }

        var actual = TickAdmissionHarness.Frame(native.Method);
        h.Admission.Observe(actual, actual.OsThreadId);
        h.Admission.Observe(TickAdmissionHarness.Frame(FakeNative.StartupOtherImage), actual.OsThreadId);
        h.Admission.Observe(actual, actual.OsThreadId);
        TickCheck(h.BoundTick == native.Method && h.Filters == 4 && h.Faults == 0 && !h.Admission.Unknown
            && h.Sequences.SequenceEqual(new long[] { 1, 2 }));
    }

    private static void TickStartupSignature()
    {
        using var native = new FakeNative();
        var consumer = CreateStartupConsumer(native);
        TickCheck(consumer.MatchesTick(native.Method));
        native.IsStatic = true;
        TickCheck(!consumer.MatchesTick(native.Method), "A static Update reached startup admission.");
        native.IsStatic = false;
        native.ReturnType = native.Single;
        TickCheck(!consumer.MatchesTick(native.Method), "A non-void Update reached startup admission.");
        native.ReturnType = native.Void;
        native.GenericMethod = true;
        TickCheck(!consumer.MatchesTick(native.Method), "A generic Update reached startup admission.");
        native.GenericMethod = false;
        native.InflatedMethod = true;
        TickCheck(!consumer.MatchesTick(native.Method), "An inflated Update reached startup admission.");
        native.InflatedMethod = false;
        native.StartupMissingNamespace = true;
        TickCheck(!consumer.MatchesTick(native.Method), "Missing namespace metadata was treated as the global namespace.");
        native.StartupMissingNamespace = false;
        TickCheck(consumer.MatchesTick(native.Method));
    }
}

sealed partial class FakeNative
{
    internal static readonly IntPtr StartupOtherImage = new(0x411), StartupOverload = new(0x412), StartupOtherMetadata = new(0x413);
    private static readonly IntPtr StartupClass = new(0x421), StartupOtherClass = new(0x422);
    private static readonly IntPtr StartupImage = new(0x431), StartupOtherImageMetadata = new(0x432);
    private IntPtr _startupMethodName, _startupClassName, _startupNamespace, _startupImageName, _startupOtherImageName;
    internal bool StartupMissingNamespace;

    internal NativeRuntime CreateStartupTickRuntime()
    {
        _startupMethodName = StartupText("Update");
        _startupClassName = StartupText("GameManager");
        _startupNamespace = StartupText("");
        _startupImageName = StartupText("Assembly-CSharp.dll");
        _startupOtherImageName = StartupText("Other.dll");
        Add("il2cpp_method_get_name", "Ptr1", nameof(StartupMethodName));
        Add("il2cpp_method_get_class", "Ptr1", nameof(StartupDeclaringClass));
        Add("il2cpp_class_get_name", "Ptr1", nameof(StartupClassName));
        Add("il2cpp_class_get_namespace", "Ptr1", nameof(StartupNamespace));
        Add("il2cpp_class_get_image", "Ptr1", nameof(StartupDeclaringImage));
        Add("il2cpp_image_get_name", "Ptr1", nameof(StartupImageName));
        Add("il2cpp_method_get_param_count", "Flags", nameof(StartupParameterCount));
        Add("il2cpp_class_from_name", "ClassLookup", nameof(StartupFindClass));
        Add("il2cpp_class_get_method_from_name", "MethodLookup", nameof(StartupFindMethod));
        return new NativeRuntime(ExportPointer);
    }

    private IntPtr StartupText(string value)
    {
        var pointer = Marshal.StringToHGlobalAnsi(value);
        _allocations.Add(pointer);
        return pointer;
    }

    private IntPtr StartupMethodName(IntPtr method) => _startupMethodName;
    private IntPtr StartupDeclaringClass(IntPtr method) => method == StartupOtherImage ? StartupOtherClass : StartupClass;
    private IntPtr StartupClassName(IntPtr cls) => _startupClassName;
    private IntPtr StartupNamespace(IntPtr cls) => StartupMissingNamespace ? IntPtr.Zero : _startupNamespace;
    private IntPtr StartupDeclaringImage(IntPtr cls) => cls == StartupClass ? StartupImage : StartupOtherImageMetadata;
    private IntPtr StartupImageName(IntPtr image) => image == StartupImage ? _startupImageName : _startupOtherImageName;
    private uint StartupParameterCount(IntPtr method) => method == StartupOverload ? 1u : 0u;
    private IntPtr StartupFindClass(IntPtr image, string ns, string name) => ns == "" && name == "GameManager"
        ? image == StartupImage ? StartupClass : StartupOtherClass
        : IntPtr.Zero;
    private IntPtr StartupFindMethod(IntPtr cls, string name, int count) => name == "Update" && count == 0
        ? cls == StartupClass ? Method : StartupOtherImage
        : IntPtr.Zero;
}
