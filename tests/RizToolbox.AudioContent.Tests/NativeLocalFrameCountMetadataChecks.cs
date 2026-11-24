using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using RizToolbox.Adapter;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

internal static class NativeLocalFrameCountMetadataChecks
{
    internal static readonly (string Name, Action Test)[] Cases = {
        ("LOCAL static frame getter reaches the unchanged native entry/RVA guard", StaticGetter),
        ("LOCAL frame declaration is enumerated as static without invoking a game method", StaticEnumeration),
        ("LOCAL static frame lookup refuses an instance getter", InstanceGetter),
        ("LOCAL static frame lookup preserves exact parameter and return types", Signature),
        ("LOCAL static frame lookup refuses another declaring class", DeclaringClass),
        ("LOCAL static frame lookup refuses generic and inflated declarations", Generic),
        ("LOCAL frame lookup rejects a foreign thread before metadata enumeration", Thread)
    };
    internal static int Run()
    {
        var failures = 0;
        foreach (var item in Cases)
        {
            try { item.Test(); Console.WriteLine("PASS " + item.Name); }
            catch (Exception error) { ++failures; Console.Error.WriteLine("FAIL " + item.Name + ": " + error); }
        }
        Console.WriteLine($"{Cases.Length - failures}/{Cases.Length} LOCAL frame metadata checks passed.");
        return failures == 0 ? 0 : 1;
    }
    private static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private static T Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T error) { return error; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static void StaticGetter()
    {
        using var fixture = new Fixture();
        var error = Throws<UnsupportedSignatureException>(fixture.Resolve);
        Check(error.Message.Contains("exact Unity frame-count static getter metadata/RVA changed", StringComparison.Ordinal),
            "The real LOCAL resolver did not reach its unchanged executable-image/RVA guard.");
        fixture.UnusedNativeState();
    }
    private static void StaticEnumeration()
    {
        using var fixture = new Fixture();
        var declaration = fixture.Native.Api.InspectDeclaredStaticMethod(fixture.Native.LocalFrameClass,
            "get_frameCount", Array.Empty<string>(), "System.Int32", IntPtr.Zero);
        Check(declaration.DeclaringClass.TypeName == "UnityEngine.Time" && declaration.Name == "get_frameCount"
            && declaration.ParameterTypes.Count == 0 && declaration.ReturnType == "System.Int32" && (declaration.Flags & 0x10) != 0
            && declaration.Entry is null && declaration.EntryStatus.StartsWith("Unverified", StringComparison.Ordinal),
            "Synthetic declaration inspection invented a pinned executable entry or changed the static signature.");
        var missing = Throws<MissingMethodException>(() => fixture.Native.Api.InspectDeclaredMethod(fixture.Native.LocalFrameClass,
            "get_frameCount", Array.Empty<string>(), "System.Int32", IntPtr.Zero));
        Check(missing.Message == "Time.get_frameCount/0", "The former instance lookup did not reproduce the actual startup error.");
        fixture.UnusedNativeState();
    }
    private static void InstanceGetter()
    { using var fixture = new Fixture(); fixture.Native.LocalFrameStatic = false; Throws<MissingMethodException>(fixture.Resolve); fixture.UnusedNativeState(); }
    private static void Signature()
    {
        using (var fixture = new Fixture()) { fixture.Native.LocalFrameParameterCount = 1; Throws<MissingMethodException>(fixture.Resolve); fixture.UnusedNativeState(); }
        using (var fixture = new Fixture()) { fixture.Native.LocalFrameReturnType = fixture.Native.Single; Throws<UnsupportedSignatureException>(fixture.Resolve); fixture.UnusedNativeState(); }
    }
    private static void DeclaringClass()
    { using var fixture = new Fixture(); fixture.Native.LocalFrameForeignClass = true; Throws<UnsupportedSignatureException>(fixture.Resolve); fixture.UnusedNativeState(); }
    private static void Generic()
    {
        using (var fixture = new Fixture()) { fixture.Native.GenericMethod = true; Throws<UnsupportedSignatureException>(fixture.Resolve); fixture.UnusedNativeState(); }
        using (var fixture = new Fixture()) { fixture.Native.InflatedMethod = true; Throws<UnsupportedSignatureException>(fixture.Resolve); fixture.UnusedNativeState(); }
    }
    private static void Thread()
    {
        using var fixture = new Fixture();
        Task.Run(() => Throws<InvalidOperationException>(fixture.Resolve)).GetAwaiter().GetResult();
        Check(fixture.Native.LocalFrameEnumerations == 0, "Foreign-thread lookup consulted frame metadata."); fixture.UnusedNativeState();
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly FakeNative Native = new();
        private readonly MainThreadDispatcher _main = new();
        private readonly NativeLocalRunTerminationBoundaryBackend _backend;
        internal Fixture()
        {
            Native.ConfigureLocalFrameMetadata();
            var profile = new AdapterBuildProfile("metadata-only", new string('0', 64), "offline", "2022.3.62f2", "manager", "Update", new Dictionary<string, TypeBinding>());
            _backend = new(Native.Api, _main, profile, IntPtr.Zero, new ExactHookBroker(new NoInstall(), _main, new Logger()));
        }
        internal void Resolve()
        {
            try { typeof(NativeLocalRunTerminationBoundaryBackend).GetMethod("ResolveLayout", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_backend, null); }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        internal void UnusedNativeState() => Check(Native.Invocations == 0 && Native.NewRoots == 0 && Native.FieldReads == 0,
            "Metadata-only resolution invoked game code, read instance storage or acquired native roots.");
        public void Dispose() { _main.Dispose(); Native.Dispose(); }
    }
    private sealed class NoInstall : INativeDetourEngine
    { public Delegate Install(VerifiedHookTarget target, Delegate replacement) => throw new InvalidOperationException("No native binding belongs to metadata tests."); }
    private sealed class Logger : ILogger { public void Log(LogLevel level, string message, Exception? exception = null) { } }
}

// Synthetic IL2CPP exports for the real LOCAL ResolveLayout path. Methods are
// enumerated through NativeRuntime itself; no target module or binary is loaded.
sealed partial class FakeNative
{
    private readonly Dictionary<(IntPtr Class, string Name), IntPtr> _localFrameFields = new();
    private readonly Dictionary<IntPtr, string> _localFrameNames = new();
    private IntPtr _localFrameObjectClass, _localFrameObjectType;
    private readonly IntPtr _localFrameGetter = new(73001), _localFrameAlive = new(73002);
    internal IntPtr LocalFrameClass, LocalFrameReturnType;
    internal bool LocalFrameStatic = true, LocalFrameForeignClass;
    internal uint LocalFrameParameterCount;
    internal int LocalFrameEnumerations;
    internal void ConfigureLocalFrameMetadata()
    {
        var cached = (Dictionary<(string, string, string), IntPtr>)typeof(NativeRuntime).GetField("_classes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Api)!;
        IntPtr Class(string assembly, string ns, string name)
        {
            var type = AddType(string.IsNullOrEmpty(ns) ? name : ns + "." + name, 18, false, IntPtr.Size);
            var cls = ClassOf(type); cached.Add((assembly, ns, name), cls); _localFrameNames.Add(cls, name); return cls;
        }
        var normal = Class("Assembly-CSharp.dll", "", "NormalModeInLevelController");
        var controller = Class("Assembly-CSharp.dll", "", "InLevelController");
        var information = Class("Assembly-CSharp.dll", "InLevel", "LevelInformation");
        var progress = Class("Assembly-CSharp.dll", "InLevel", "ProgressControl");
        var parameters = Class("Assembly-CSharp.dll", "", "InsideLevelParams");
        var level = Class("Assembly-CSharp.dll", "InLevel", "LevelControl");
        _localFrameObjectClass = Class("UnityEngine.CoreModule.dll", "UnityEngine", "Object");
        _localFrameObjectType = _classes[_localFrameObjectClass];
        LocalFrameClass = Class("UnityEngine.CoreModule.dll", "UnityEngine", "Time"); LocalFrameReturnType = Int32;
        var metadata = AddType("ILevelMetadata", 18, false, IntPtr.Size);
        void Field(IntPtr owner, string name, IntPtr type, int offset)
        {
            var field = new IntPtr(74000 + _localFrameFields.Count); _localFrameFields.Add((owner, name), field); _fieldMetadata.Add(field, (name, type, offset));
        }
        Field(level, "levelInformation", _classes[information], 0x20);
        Field(controller, "LevelInformation", _classes[information], 0x28);
        Field(controller, "LevelParams", _classes[parameters], 0x20);
        Field(information, "progressControl", _classes[progress], 0x28);
        Field(information, "loadOver", Boolean, 0xbe);
        Field(progress, "levelInformation", _classes[information], 0x20);
        Field(parameters, "metadata", metadata, 0x10);
        void Bind(string fieldName, string methodName)
        {
            var field = typeof(NativeRuntime).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!;
            field.SetValue(Api, Delegate.CreateDelegate(field.FieldType, this, GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!));
        }
        Bind("_findField", nameof(LocalFrameFindField)); Bind("_className", nameof(LocalFrameClassName));
        Bind("_methods", nameof(LocalFrameMethods)); Bind("_methodName", nameof(LocalFrameMethodName));
        Bind("_methodClass", nameof(LocalFrameMethodClass)); Bind("_parameterCount", nameof(LocalFrameParameters));
        Bind("_parameterType", nameof(LocalFrameParameter)); Bind("_methodFlags", nameof(LocalFrameFlags)); Bind("_returnType", nameof(LocalFrameReturns));
    }
    private IntPtr LocalFrameFindField(IntPtr cls, string name) => _localFrameFields.TryGetValue((cls, name), out var field) ? field : IntPtr.Zero;
    private IntPtr LocalFrameText(string value) { var text = Marshal.StringToHGlobalAnsi(value); _allocations.Add(text); return text; }
    private IntPtr LocalFrameClassName(IntPtr cls) => LocalFrameText(_localFrameNames[cls]);
    private IntPtr LocalFrameMethods(IntPtr cls, IntPtr iterator)
    {
        if (cls == LocalFrameClass) ++LocalFrameEnumerations;
        if (Marshal.ReadIntPtr(iterator) != IntPtr.Zero) return IntPtr.Zero;
        Marshal.WriteIntPtr(iterator, new IntPtr(1));
        return cls == LocalFrameClass ? _localFrameGetter : cls == _localFrameObjectClass ? _localFrameAlive : IntPtr.Zero;
    }
    private IntPtr LocalFrameMethodName(IntPtr method) => LocalFrameText(method == _localFrameGetter ? "get_frameCount" : "op_Implicit");
    private IntPtr LocalFrameMethodClass(IntPtr method) => method == _localFrameGetter
        ? LocalFrameForeignClass ? _localFrameObjectClass : LocalFrameClass : _localFrameObjectClass;
    private uint LocalFrameParameters(IntPtr method) => method == _localFrameGetter ? LocalFrameParameterCount : 1;
    private IntPtr LocalFrameParameter(IntPtr method, uint index) => method == _localFrameGetter ? Int32 : _localFrameObjectType;
    private uint LocalFrameFlags(IntPtr method, out uint implementation) { implementation = 0; return method == _localFrameGetter && !LocalFrameStatic ? 0u : 0x10u; }
    private IntPtr LocalFrameReturns(IntPtr method) => method == _localFrameGetter ? LocalFrameReturnType : Boolean;
}
