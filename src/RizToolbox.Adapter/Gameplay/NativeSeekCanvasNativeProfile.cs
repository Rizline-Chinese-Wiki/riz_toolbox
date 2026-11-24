using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>Exact current Canvas source metadata and complete native producer
/// bodies. Composes the clock/chart source without depending on any Hold instance.
/// This verifies read-only evidence; it invokes no producer, easing initializer,
/// Unity method, or field getter and grants no target write or Seek admission.</summary>
internal sealed class NativeSeekCanvasNativeProfile : ISeekCanvasNativeProfile
{
    internal const string Fingerprint = GameplayClockNativeProfile.Fingerprint;
    internal const bool RuntimeCanvasSourceVerified = false;
    internal const bool TargetEnvironmentRebuiltKnown = false;
    internal const bool EngineConsumerClosureKnown = false;
    internal const bool ProductionAdmissionKnown = false;
    internal const string Evidence = "Pinned GameAssembly " + Fingerprint
        + "; composes the frozen chart/clock profile and exact CanvasMove, KeyPoint, initialized EaseInfos,"
        + " static readonly down/right field metadata, Canvas producers, range converter, easing consumer and vector initializer."
        + " Actual complete arrays/List storage, initialized easing data, vector values, run/publication identities and roots"
        + " require independent current capture. No game/Unity method, easing initializer or static field getter is invoked here."
        + " Range invocation effects, resolved Unity callbacks, target writes and complete Seek admission remain unverified.";

    private readonly NativeRuntime _api;
    private readonly ISeekChartNativeProfile _chart;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly IntPtr _module;
    private readonly string _path;
    private readonly List<NativeMetadataClass> _classes = new();
    private readonly List<NativeExactReadField> _fields = new();
    private readonly List<NativeMetadataMethod> _methods = new();
    private int _checking, _faulted;

    public IntPtr ChartClass => _chart.ChartClass;
    public IntPtr ProgressClass => _chart.ProgressClass;
    public IntPtr InformationClass { get; }
    public IntPtr CanvasClass { get; }
    public IntPtr CanvasMoveClass { get; }
    public IntPtr KeyPointClass { get; }
    public IntPtr TransformClass { get; }
    public IntPtr VectorClass { get; }
    public IntPtr EaseClass { get; }
    public IntPtr EaseInfoClass { get; }
    public IntPtr ChartTimeRangeMethod { get; }

    internal NativeSeekCanvasNativeProfile(NativeRuntime api, ISeekChartNativeProfile chartProfile)
    {
        ArgumentNullException.ThrowIfNull(api); ArgumentNullException.ThrowIfNull(chartProfile);
        _api = api; _chart = chartProfile;
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8)
            throw new UnsupportedSignatureException("Canvas source evidence requires the pinned Windows x64 image.");
        _module = GetModuleHandle("GameAssembly.dll");
        if (_module == IntPtr.Zero) throw new UnsupportedSignatureException("The actual Canvas source image is absent.");
        var path = new StringBuilder(32768);
        var count = GetModuleFileName(_module, path, path.Capacity);
        if (count == 0 || count >= path.Capacity)
            throw new UnsupportedSignatureException("The mapped Canvas source image path is unavailable.");
        _path = path.ToString();
        using (var file = File.OpenRead(_path))
        using (var hash = SHA256.Create())
            if (!Convert.ToHexString(hash.ComputeHash(file)).Equals(Fingerprint, StringComparison.OrdinalIgnoreCase))
                throw new UnsupportedSignatureException("The Canvas source image differs from its pinned evidence.");

        _chart.VerifyCurrent();
        RequireClass(ChartClass, "InLevel.Chart"); RequireClass(ProgressClass, "InLevel.ProgressControl");
        InformationClass = Class("LevelInformation"); CanvasClass = Class("CanvasControl");
        CanvasMoveClass = Class("CanvasMove"); KeyPointClass = Class("KeyPoint");
        EaseClass = Class("GetEase"); EaseInfoClass = Class("EaseInfor");
        TransformClass = Class("UnityEngine.CoreModule.dll", "UnityEngine", "Transform");
        VectorClass = Class("UnityEngine.CoreModule.dll", "UnityEngine", "Vector3");
        CaptureFields();
        Bind(CanvasClass, "GetInformations", "System.Void", 0x3e3620);
        Bind(CanvasClass, "SetPosition", "System.Void", 0x3e3aa0);
        Bind(CanvasClass, "Start", "System.Void", 0x3e3c30);
        Bind(CanvasClass, "Update", "System.Void", 0x3e3cf0);
        ChartTimeRangeMethod = Bind(ProgressClass, "ChartTimeRageToRealTime", "System.Single", 0x3fff90,
            "System.Double", "System.Double");
        BindStatic(EaseClass, "GetEaseWithProgress", "System.Single", 0x3e76c0, "System.Single", "System.Int32");
        BindStatic(VectorClass, ".cctor", "System.Void", 0x1548ac0);
        VerifyCurrent();
    }

    private void CaptureFields()
    {
        Field(InformationClass, "progressControl", "InLevel.ProgressControl", 0x28);
        Field(InformationClass, "canvasControl", "InLevel.CanvasControl", 0x70);
        Field(InformationClass, "chart", "InLevel.Chart", 0x88);
        Field(InformationClass, "speed", "System.Single", 0x98);
        Field(InformationClass, "nowTime", "System.Single", 0xb0);
        Field(InformationClass, "sceneWidth", "System.Single", 0xc4);
        Field(ProgressClass, "levelInformation", "InLevel.LevelInformation", 0x20);
        Field(ProgressClass, "isPlaying", "System.Boolean", 0x42);
        Field(ChartClass, "bPM", "System.Single", 0x30);
        Field(ChartClass, "bpmShifts", "System.Collections.Generic.List<InLevel.KeyPoint>", 0x38);
        Field(CanvasClass, "levelInformation", "InLevel.LevelInformation", 0x20);
        Field(CanvasClass, "canvasMoveData", "InLevel.CanvasMove[]", 0x28);
        Field(CanvasClass, "canvasTransforms", "UnityEngine.Transform[]", 0x30);
        Field(CanvasClass, "floorPositions", "System.Single[]", 0x38);
        Field(CanvasClass, "floorSpeeds", "System.Single[]", 0x40);
        Field(CanvasClass, "xPositions", "System.Single[]", 0x48);
        Field(CanvasClass, "nowTime", "System.Single", 0x50);
        Field(CanvasClass, "speed", "System.Single", 0x54);
        Field(CanvasClass, "screenWidth", "System.Single", 0x58);
        Field(CanvasMoveClass, "index", "System.Int32", 0x10);
        Field(CanvasMoveClass, "xPositionKeyPoints", "System.Collections.Generic.List<InLevel.KeyPoint>", 0x18);
        Field(CanvasMoveClass, "speedKeyPoints", "System.Collections.Generic.List<InLevel.KeyPoint>", 0x20);
        Field(KeyPointClass, "time", "System.Single", 0x10);
        Field(KeyPointClass, "value", "System.Single", 0x14);
        Field(KeyPointClass, "easeType", "System.Int32", 0x18);
        Field(KeyPointClass, "floorPosition", "System.Single", 0x1c);
        StaticField(EaseClass, "EaseInfos", "InLevel.EaseInfor[]", 0);
        Field(EaseInfoClass, "code", "System.Int32", 0x10);
        Field(EaseInfoClass, "progress", "System.Single[]", 0x18);
        ReadonlyStaticField(VectorClass, "downVector", "UnityEngine.Vector3", 0x24);
        ReadonlyStaticField(VectorClass, "rightVector", "UnityEngine.Vector3", 0x3c);
    }

    private IntPtr Class(string name) => Class("Assembly-CSharp.dll", "InLevel", name);
    private IntPtr Class(string assembly, string ns, string name)
    {
        var cls = _api.Class(assembly, ns, name);
        RequireClass(cls, ns.Length == 0 ? name : ns + "." + name); return cls;
    }
    private void RequireClass(IntPtr cls, string typeName)
    {
        var description = _api.InspectMetadataClass(cls);
        if (description.TypeName != typeName)
            throw new UnsupportedSignatureException("Canvas metadata names another exact class: " + typeName);
        _classes.Add(description);
    }
    private void Field(IntPtr cls, string name, string type, int offset)
        => _fields.Add(_api.InspectExactDeclaredReadField(cls, name, type, false, offset));
    private void StaticField(IntPtr cls, string name, string type, int offset)
        => _fields.Add(_api.InspectExactDeclaredReadField(cls, name, type, true, offset));
    private void ReadonlyStaticField(IntPtr cls, string name, string type, int offset)
    {
        var field = _api.InspectExactDeclaredReadField(cls, name, type, true, offset);
        if ((field.Flags & 0x20u) == 0)
            throw new UnsupportedSignatureException("Canvas vector basis field is not static readonly: " + name);
        _fields.Add(field);
    }
    private IntPtr Bind(IntPtr cls, string name, string returns, int rva, params string[] parameters)
        => Save(name == "ChartTimeRageToRealTime"
            ? _api.InspectCanvasSourceMethod(cls, name, parameters, returns, _module)
            : _api.InspectDeclaredMethod(cls, name, parameters, returns, _module), rva, false);
    private IntPtr BindStatic(IntPtr cls, string name, string returns, int rva, params string[] parameters)
        => Save(_api.InspectDeclaredStaticMethod(cls, name, parameters, returns, _module), rva, true);
    private IntPtr Save(NativeMetadataMethod method, int rva, bool isStatic)
    {
        if (method.Entry?.Rva != rva || method.GenericMethod || method.InflatedMethod
            || ((method.Flags & 0x10u) != 0) != isStatic || (method.Flags & 0x400u) != 0
            || !Bodies.Any(body => body.Rva == rva))
            throw new UnsupportedSignatureException("Canvas source method metadata/entry differs: " + method.Name);
        _methods.Add(method); return method.MethodInfo;
    }

    public void VerifyCurrent()
    {
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Canvas source profile must be verified on its owner thread.");
        if (Volatile.Read(ref _faulted) != 0)
            throw new UnsupportedSignatureException("The Canvas source profile has been withdrawn.");
        if (Interlocked.CompareExchange(ref _checking, 1, 0) != 0)
            throw new InvalidOperationException("Canvas source profile verification cannot reenter.");
        try
        {
            _chart.VerifyCurrent(); VerifyModule();
            foreach (var expected in _classes)
                if (_api.InspectMetadataClass(expected.Class) != expected)
                    throw new UnsupportedSignatureException("Canvas class metadata changed: " + expected.TypeName);
            foreach (var expected in _fields)
                if (_api.InspectExactDeclaredReadField(expected.DeclaringClass, expected.Name, expected.TypeName,
                    expected.IsStatic, expected.Offset) != expected)
                    throw new UnsupportedSignatureException("Canvas field metadata changed: " + expected.Name);
            foreach (var expected in _methods)
            {
                var current = expected.Name == "ChartTimeRageToRealTime"
                    ? _api.InspectCanvasSourceMethod(expected.DeclaringClass.Class, expected.Name,
                        expected.ParameterTypes.ToArray(), expected.ReturnType, _module)
                    : (expected.Flags & 0x10u) != 0
                    ? _api.InspectDeclaredStaticMethod(expected.DeclaringClass.Class, expected.Name, expected.ParameterTypes.ToArray(), expected.ReturnType, _module)
                    : _api.InspectDeclaredMethod(expected.DeclaringClass.Class, expected.Name, expected.ParameterTypes.ToArray(), expected.ReturnType, _module);
                if (current.MethodInfo != expected.MethodInfo || current.DeclaringClass != expected.DeclaringClass
                    || current.Entry != expected.Entry || current.Flags != expected.Flags
                    || current.ImplementationFlags != expected.ImplementationFlags || current.GenericMethod || current.InflatedMethod)
                    throw new UnsupportedSignatureException("Canvas source MethodInfo changed: " + expected.Name);
            }
            foreach (var body in Bodies)
            {
                var address = IntPtr.Add(_module, body.Rva);
                HookTargetValidation.VerifyExecutable(address, _module);
                HookTargetValidation.VerifyReadable(address, (nuint)body.Length);
                var bytes = new byte[body.Length]; Marshal.Copy(address, bytes, 0, bytes.Length);
                if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(body.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new UnsupportedSignatureException("A complete Canvas source body changed: " + body.Name);
            }
            foreach (var constant in Constants)
            {
                var expected = Convert.FromHexString(constant.HexBytes); var address = IntPtr.Add(_module, constant.Rva);
                HookTargetValidation.VerifyReadable(address, (nuint)expected.Length);
                var actual = new byte[expected.Length]; Marshal.Copy(address, actual, 0, actual.Length);
                if (!actual.AsSpan().SequenceEqual(expected))
                    throw new UnsupportedSignatureException("A Canvas source constant changed: " + constant.Name);
            }
            VerifyModule(); _chart.VerifyCurrent();
        }
        catch { Interlocked.Exchange(ref _faulted, 1); throw; }
        finally { Volatile.Write(ref _checking, 0); }
    }
    private void VerifyModule()
    {
        if (GetModuleHandle("GameAssembly.dll") != _module || GetModuleHandle(_path) != _module)
            throw new UnsupportedSignatureException("The Canvas source module changed or was unloaded.");
    }

    internal sealed record Body(string Name, int Rva, int Length, string Sha256);
    internal sealed record DataConstant(string Name, int Rva, string HexBytes);
    internal static IReadOnlyList<DataConstant> Constants { get; } = Array.AsReadOnly(new DataConstant[]
    {
        new("Canvas.DoubleEpsilon", 0x17c4bc0, "8dedb5a0f7c6b03e"),
        new("Canvas.FloatAbsMask", 0x17c4c70, "ffffff7fffffff7fffffff7fffffff7f"),
        new("Canvas.One", 0x17c4bf4, "0000803f"),
        new("Ease.SampleScale", 0x17c4c44, "0000c842"),
        new("Range.SecondsPerMinute", 0x17c4f78, "0000000000004e40"),
    });
    // Full next-distinct-metadata spans are reused unchanged from the frozen
    // Hold geometry evidence. Static data reuse introduces no Hold dependency.
    internal static IReadOnlyList<Body> Bodies { get; } = Array.AsReadOnly(new Body[]
    {
        new("CanvasControl.GetInformations", 0x3e3620, 1152, "7e2930d7f43eecb08734452b33a297ddc59f88a3f056ac98289da5da62e45e69"),
        new("CanvasControl.SetPosition", 0x3e3aa0, 400, "a0126b0876263480c1275818b6922419ba20f4769747113d5c8ff723b57c0c0e"),
        new("CanvasControl.Start", 0x3e3c30, 192, "6e5d6401a34cef4ce3194dc2059bd0ee653f7caf118079e23254e1cbdb39508b"),
        new("CanvasControl.Update", 0x3e3cf0, 480, "db91cf6bf18ae8635b345836b282c4adb7bdd68b2baa79149d6cb32fd31b601d"),
        new("ProgressControl.ChartTimeRageToRealTime", 0x3fff90, 1136, "127814643f82f15c4711bd86f97c2ffd603bba997cc24122c07ceb8e1298e3ef"),
        new("GetEase.GetEaseWithProgress", 0x3e76c0, 416, "975d0e2668b981ecdec69c3e6f875e339b1b6f4d3b1483b176dc7e1a7cd51aa5"),
        new("Vector3..cctor", 0x1548ac0, 320, "94c90dcefdd5c55b567682f14da7e6481d912a1388064f209e31272c7729e66e"),
    });
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern uint GetModuleFileName(IntPtr module, StringBuilder path, int size);
}
