using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using RizToolbox.Adapter.UiInput;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>Exact source and official Transform methods used by target Hold geometry.
/// The profile does not certify that target canvas, camera, line or ring state is rebuilt.</summary>
internal interface ISeekHoldGeometryProfile : ISeekHoldNativeProfile
{
    IntPtr UIClass { get; }
    IntPtr CanvasClass { get; }
    IntPtr CameraClass { get; }
    IntPtr LineClass { get; }
    IntPtr LinePointClass { get; }
    IntPtr GroupClass { get; }
    IntPtr EaseClass { get; }
    IntPtr EaseInfoClass { get; }
    IntPtr TransformClass { get; }
    IntPtr GetTransformMethod { get; }
    IntPtr GetParentMethod { get; }
    IntPtr GetPositionMethod { get; }
    IntPtr GetLocalPositionMethod { get; }
    IntPtr GetLocalRotationMethod { get; }
    IntPtr GetLocalScaleMethod { get; }
    IntPtr SetParentMethod { get; }
    IntPtr SetPositionMethod { get; }
    IntPtr SetLocalPositionMethod { get; }
    IntPtr SetLocalRotationMethod { get; }
    IntPtr SetLocalScaleMethod { get; }
    IntPtr TransformPointMethod { get; }
    IntPtr InverseTransformPointMethod { get; }
}

/// <summary>Composes the frozen Hold/target source profile, exact target-geometry
/// dependency metadata and complete official Transform wrappers. No game method
/// is invoked here. Actual target canvas/line/ring state and resolved Unity
/// internal-call effects remain separate requirements.</summary>
internal sealed class NativeSeekHoldGeometryProfile : ISeekHoldGeometryProfile
{
    internal const string Fingerprint = NativeSeekHoldNativeProfile.Fingerprint;
    internal const bool RuntimeGeometryVerified = false;
    internal const bool TargetEnvironmentRebuiltKnown = false;
    internal const bool EngineConsumerClosureKnown = false;
    internal const bool ExactInverseKnown = false;
    internal const bool ProductionAdmissionKnown = false;
    internal const string Evidence = "Pinned GameAssembly " + Fingerprint
        + "; composes frozen Hold and target source evidence, exact geometry dependency fields,"
        + " complete Canvas/Line/Note producers, table-based easing, vector basis initialization and Transform wrappers."
        + " The current target canvas, camera, line, ring and easing-table identities/data must be independently established."
        + " No floor-position-to-world substitution, engine callback closure, inverse or complete Seek admission is implied.";

    private readonly NativeRuntime _api;
    private readonly ISeekHoldNativeProfile _hold;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly IntPtr _module;
    private readonly string _path;
    private readonly IntPtr _vectorClass;
    private readonly List<NativeMetadataClass> _classes = new();
    private readonly List<NativeExactReadField> _fields = new();
    private readonly List<NativeMetadataMethod> _methods = new();
    private int _checking, _faulted;

    public IntPtr ChartClass => _hold.ChartClass;
    public IntPtr ProgressClass => _hold.ProgressClass;
    public IntPtr InformationClass => _hold.InformationClass;
    public IntPtr NoteClass => _hold.NoteClass;
    public IntPtr JudgeClass => _hold.JudgeClass;
    public IntPtr HoldClass => _hold.HoldClass;
    public IntPtr ChallengeTimeClass => _hold.ChallengeTimeClass;
    public IntPtr NoteListClass => _hold.NoteListClass;
    public IntPtr ClearMethod => _hold.ClearMethod;
    public IntPtr AddMethod => _hold.AddMethod;
    public IntPtr UIClass { get; }
    public IntPtr CanvasClass { get; }
    public IntPtr CameraClass { get; }
    public IntPtr LineClass { get; }
    public IntPtr LinePointClass { get; }
    public IntPtr GroupClass { get; }
    public IntPtr EaseClass { get; }
    public IntPtr EaseInfoClass { get; }
    public IntPtr TransformClass { get; }
    public IntPtr GetTransformMethod { get; }
    public IntPtr GetParentMethod { get; }
    public IntPtr GetPositionMethod { get; }
    public IntPtr GetLocalPositionMethod { get; }
    public IntPtr GetLocalRotationMethod { get; }
    public IntPtr GetLocalScaleMethod { get; }
    public IntPtr SetParentMethod { get; }
    public IntPtr SetPositionMethod { get; }
    public IntPtr SetLocalPositionMethod { get; }
    public IntPtr SetLocalRotationMethod { get; }
    public IntPtr SetLocalScaleMethod { get; }
    public IntPtr TransformPointMethod { get; }
    public IntPtr InverseTransformPointMethod { get; }

    internal NativeSeekHoldGeometryProfile(NativeRuntime api, ISeekHoldNativeProfile holdProfile)
    {
        ArgumentNullException.ThrowIfNull(api); ArgumentNullException.ThrowIfNull(holdProfile);
        _api = api; _hold = holdProfile;
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8)
            throw new UnsupportedSignatureException("Hold geometry source evidence requires the pinned Windows x64 image.");
        _module = GetModuleHandle("GameAssembly.dll");
        if (_module == IntPtr.Zero) throw new UnsupportedSignatureException("The actual Hold geometry source image is absent.");
        var path = new StringBuilder(32768);
        var count = GetModuleFileName(_module, path, path.Capacity);
        if (count == 0 || count >= path.Capacity)
            throw new UnsupportedSignatureException("The mapped Hold geometry source image path is unavailable.");
        _path = path.ToString();
        using (var file = File.OpenRead(_path))
        using (var hash = SHA256.Create())
            if (!Convert.ToHexString(hash.ComputeHash(file)).Equals(Fingerprint, StringComparison.OrdinalIgnoreCase))
                throw new UnsupportedSignatureException("The Hold geometry source image differs from its pinned evidence.");

        _hold.VerifyCurrent();
        UIClass = Class("UIControl"); CanvasClass = Class("CanvasControl"); CameraClass = Class("CameraControl");
        LineClass = Class("LineInformation"); LinePointClass = Class("LinePoint"); GroupClass = Class("LinesGroupInformation");
        EaseClass = Class("GetEase"); EaseInfoClass = Class("EaseInfor");
        var lineControl = Class("LineControl"); var noteControl = Class("NoteControl"); var noteData = Class("Note");
        var canvasMove = Class("CanvasMove"); var key = Class("KeyPoint");
        TransformClass = Class("UnityEngine.CoreModule.dll", "UnityEngine", "Transform");
        var component = Class("UnityEngine.CoreModule.dll", "UnityEngine", "Component");
        _vectorClass = Class("UnityEngine.CoreModule.dll", "UnityEngine", "Vector3");
        var extensions = Class("Assembly-CSharp.dll", "", "Extensions");
        CaptureFields(lineControl, noteControl, noteData, canvasMove, key);

        Bind(CanvasClass, "GetInformations", "System.Void", 0x3e3620);
        Bind(CanvasClass, "SetPosition", "System.Void", 0x3e3aa0);
        Bind(CanvasClass, "Start", "System.Void", 0x3e3c30);
        Bind(CanvasClass, "Update", "System.Void", 0x3e3cf0);
        Bind(lineControl, "SetDynamicLines", "System.Void", 0x3ecf40);
        Bind(lineControl, "SetRing", "System.Void", 0x3ee5b0);
        Bind(lineControl, "Start", "System.Void", 0x3ef530);
        Bind(lineControl, "Update", "System.Void", 0x3ef570);
        Bind(noteControl, "SetNotes", "System.Void", 0x3fc620);
        Bind(noteControl, "Update", "System.Void", 0x3fcd60);
        Bind(ProgressClass, "ChartTimeRageToRealTime", "System.Single", 0x3fff90, "System.Double", "System.Double");
        BindStatic(EaseClass, "GetEaseWithProgress", "System.Single", 0x3e76c0, "System.Single", "System.Int32");
        BindStatic(extensions, "With", "UnityEngine.Vector3", 0x38ab80, "UnityEngine.Vector3",
            "System.Nullable<System.Single>", "System.Nullable<System.Single>", "System.Nullable<System.Single>");
        BindStatic(_vectorClass, ".cctor", "System.Void", 0x1548ac0);
        GetTransformMethod = Bind(component, "get_transform", "UnityEngine.Transform", 0x1536440);
        GetParentMethod = Bind(TransformClass, "get_parent", "UnityEngine.Transform", 0x1560470);
        GetPositionMethod = Bind(TransformClass, "get_position", "UnityEngine.Vector3", 0x1561970);
        GetLocalPositionMethod = Bind(TransformClass, "get_localPosition", "UnityEngine.Vector3", 0x1561640);
        GetLocalRotationMethod = Bind(TransformClass, "get_localRotation", "UnityEngine.Quaternion", 0x15616e0);
        GetLocalScaleMethod = Bind(TransformClass, "get_localScale", "UnityEngine.Vector3", 0x1561780);
        Bind(TransformClass, "SetParent", "System.Void", 0x1560d30, "UnityEngine.Transform");
        SetParentMethod = Bind(TransformClass, "SetParent", "System.Void", 0x1560d80, "UnityEngine.Transform", "System.Boolean");
        SetPositionMethod = Bind(TransformClass, "set_position", "System.Void", 0x1561f70, "UnityEngine.Vector3");
        SetLocalPositionMethod = Bind(TransformClass, "set_localPosition", "System.Void", 0x1561cd0, "UnityEngine.Vector3");
        SetLocalRotationMethod = Bind(TransformClass, "set_localRotation", "System.Void", 0x1561d70, "UnityEngine.Quaternion");
        SetLocalScaleMethod = Bind(TransformClass, "set_localScale", "System.Void", 0x1561e10, "UnityEngine.Vector3");
        TransformPointMethod = Bind(TransformClass, "TransformPoint", "UnityEngine.Vector3", 0x1560f40, "UnityEngine.Vector3");
        InverseTransformPointMethod = Bind(TransformClass, "InverseTransformPoint", "UnityEngine.Vector3", 0x1560590, "UnityEngine.Vector3");
        VerifyCurrent();
    }

    private void CaptureFields(IntPtr lineControl, IntPtr noteControl, IntPtr noteData, IntPtr canvasMove, IntPtr key)
    {
        Field(InformationClass, "lineControl", "InLevel.LineControl", 0x38);
        Field(InformationClass, "uIControl", "InLevel.UIControl", 0x68);
        Field(InformationClass, "canvasControl", "InLevel.CanvasControl", 0x70);
        Field(InformationClass, "cameraControl", "InLevel.CameraControl", 0x78);
        Field(InformationClass, "speed", "System.Single", 0x98);
        Field(InformationClass, "nowTime", "System.Single", 0xb0);
        Field(InformationClass, "sceneWidth", "System.Single", 0xc4);
        Field(UIClass, "gameSceneCanvas", "UnityEngine.Transform", 0x20);
        Field(CanvasClass, "levelInformation", "InLevel.LevelInformation", 0x20);
        Field(CanvasClass, "canvasMoveData", "InLevel.CanvasMove[]", 0x28);
        Field(CanvasClass, "canvasTransforms", "UnityEngine.Transform[]", 0x30);
        Field(CanvasClass, "floorPositions", "System.Single[]", 0x38);
        Field(CanvasClass, "floorSpeeds", "System.Single[]", 0x40);
        Field(CanvasClass, "xPositions", "System.Single[]", 0x48);
        Field(CanvasClass, "nowTime", "System.Single", 0x50);
        Field(CanvasClass, "speed", "System.Single", 0x54);
        Field(CanvasClass, "screenWidth", "System.Single", 0x58);
        Field(CameraClass, "levelInformation", "InLevel.LevelInformation", 0x20);
        Field(CameraClass, "xPosition", "System.Single", 0x30);
        Field(CameraClass, "scale", "System.Single", 0x34);
        Field(lineControl, "levelInformation", "InLevel.LevelInformation", 0x20);
        Field(lineControl, "dynamicLines", "InLevel.LineInformation[]", 0x28);
        Field(lineControl, "lineGroups", "InLevel.LinesGroupInformation[]", 0x30);
        Field(lineControl, "_canvasControl", "InLevel.CanvasControl", 0x38);
        Field(lineControl, "_nowTime", "System.Single", 0x40);
        Field(lineControl, "_sceneWidth", "System.Single", 0x44);
        Field(lineControl, "_speed", "System.Single", 0x48);
        Field(lineControl, "_gameSceneScale", "System.Single", 0x4c);
        Field(LineClass, "lineIsStatic", "System.Boolean", 0x20);
        Field(LineClass, "canvasIndex", "System.Int32", 0x24);
        Field(LineClass, "startLinePoint", "InLevel.LinePoint", 0x30);
        Field(LineClass, "endLinePoint", "InLevel.LinePoint", 0x38);
        Field(LineClass, "endTransform", "UnityEngine.Transform", 0x40);
        Field(LineClass, "endLineInformation", "InLevel.LineInformation", 0x58);
        Field(LineClass, "linesGroup", "InLevel.LinesGroupInformation", 0x60);
        Field(LinePointClass, "time", "System.Single", 0x10);
        Field(LinePointClass, "xPosition", "System.Single", 0x14);
        Field(LinePointClass, "easeType", "System.Int32", 0x1c);
        Field(LinePointClass, "canvasIndex", "System.Int32", 0x20);
        Field(LinePointClass, "floorPosition", "System.Single", 0x24);
        Field(GroupClass, "judgeRingTransform", "UnityEngine.Transform", 0x20);
        Field(GroupClass, "ringActive", "System.Boolean", 0x29);
        Field(GroupClass, "lines", "InLevel.LineInformation[]", 0x68);
        Field(GroupClass, "nowLineIndex", "System.Int32", 0x70);
        Field(noteControl, "_canvasControl", "InLevel.CanvasControl", 0x38);
        Field(noteControl, "_speed", "System.Single", 0x40);
        Field(noteData, "floorPosition", "System.Single", 0x18);
        Field(noteData, "otherInformations", "System.Collections.Generic.List<System.Single>", 0x20);
        Field(NoteClass, "holdEndFlag", "UnityEngine.Transform", 0x40);
        Field(NoteClass, "holdLine", "UnityEngine.Transform", 0x48);
        Field(NoteClass, "lastFrameHoldLineLength", "System.Single", 0x50);
        Field(NoteClass, "isStaticNote", "System.Boolean", 0x54);
        Field(canvasMove, "index", "System.Int32", 0x10);
        Field(canvasMove, "xPositionKeyPoints", "System.Collections.Generic.List<InLevel.KeyPoint>", 0x18);
        Field(canvasMove, "speedKeyPoints", "System.Collections.Generic.List<InLevel.KeyPoint>", 0x20);
        Field(key, "time", "System.Single", 0x10);
        Field(key, "value", "System.Single", 0x14);
        Field(key, "easeType", "System.Int32", 0x18);
        Field(key, "floorPosition", "System.Single", 0x1c);
        StaticField(EaseClass, "EaseInfos", "InLevel.EaseInfor[]", 0);
        Field(EaseInfoClass, "code", "System.Int32", 0x10);
        Field(EaseInfoClass, "progress", "System.Single[]", 0x18);
        StaticField(_vectorClass, "upVector", "UnityEngine.Vector3", 0x18);
        StaticField(_vectorClass, "rightVector", "UnityEngine.Vector3", 0x3c);
        StaticField(_vectorClass, "forwardVector", "UnityEngine.Vector3", 0x48);
    }

    private IntPtr Class(string name) => Class("Assembly-CSharp.dll", "InLevel", name);
    private IntPtr Class(string assembly, string ns, string name)
    {
        var cls = _api.Class(assembly, ns, name);
        var description = _api.InspectMetadataClass(cls);
        if (description.TypeName != (ns.Length == 0 ? name : ns + "." + name))
            throw new UnsupportedSignatureException("Hold geometry metadata names another exact class: " + name);
        _classes.Add(description); return cls;
    }
    private void Field(IntPtr cls, string name, string type, int offset)
        => _fields.Add(_api.InspectExactDeclaredReadField(cls, name, type, false, offset));
    private void StaticField(IntPtr cls, string name, string type, int offset)
        => _fields.Add(_api.InspectExactDeclaredReadField(cls, name, type, true, offset));
    private IntPtr Bind(IntPtr cls, string name, string returns, int rva, params string[] parameters)
        => Save(_api.InspectDeclaredMethod(cls, name, parameters, returns, _module), rva, false);
    private IntPtr BindStatic(IntPtr cls, string name, string returns, int rva, params string[] parameters)
        => Save(_api.InspectDeclaredStaticMethod(cls, name, parameters, returns, _module), rva, true);
    private IntPtr Save(NativeMetadataMethod method, int rva, bool isStatic)
    {
        if (method.Entry?.Rva != rva || method.GenericMethod || method.InflatedMethod
            || ((method.Flags & 0x10u) != 0) != isStatic || (method.Flags & 0x400u) != 0
            || !Bodies.Any(body => body.Rva == rva))
            throw new UnsupportedSignatureException("Hold geometry method metadata/entry differs: " + method.Name);
        _methods.Add(method); return method.MethodInfo;
    }

    public void VerifyCurrent()
    {
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Hold geometry profile must be verified on its owner thread.");
        if (Volatile.Read(ref _faulted) != 0)
            throw new UnsupportedSignatureException("The Hold geometry source profile has been withdrawn.");
        if (Interlocked.CompareExchange(ref _checking, 1, 0) != 0)
            throw new InvalidOperationException("Hold geometry source verification cannot reenter.");
        try
        {
            _hold.VerifyCurrent(); VerifyModule();
            foreach (var expected in _classes)
                if (_api.InspectMetadataClass(expected.Class) != expected)
                    throw new UnsupportedSignatureException("Hold geometry class metadata changed: " + expected.TypeName);
            foreach (var expected in _fields)
                if (_api.InspectExactDeclaredReadField(expected.DeclaringClass, expected.Name, expected.TypeName,
                    expected.IsStatic, expected.Offset) != expected)
                    throw new UnsupportedSignatureException("Hold geometry field metadata changed: " + expected.Name);
            foreach (var expected in _methods)
            {
                var current = (expected.Flags & 0x10u) != 0
                    ? _api.InspectDeclaredStaticMethod(expected.DeclaringClass.Class, expected.Name, expected.ParameterTypes.ToArray(), expected.ReturnType, _module)
                    : _api.InspectDeclaredMethod(expected.DeclaringClass.Class, expected.Name, expected.ParameterTypes.ToArray(), expected.ReturnType, _module);
                if (current.MethodInfo != expected.MethodInfo || current.DeclaringClass != expected.DeclaringClass
                    || current.Entry != expected.Entry || current.Flags != expected.Flags
                    || current.ImplementationFlags != expected.ImplementationFlags || current.GenericMethod || current.InflatedMethod)
                    throw new UnsupportedSignatureException("Hold geometry MethodInfo changed: " + expected.Name);
            }
            RequireBasis("upVector", new(0, 1, 0)); RequireBasis("rightVector", new(1, 0, 0)); RequireBasis("forwardVector", new(0, 0, 1));
            foreach (var body in Bodies)
            {
                var address = IntPtr.Add(_module, body.Rva);
                HookTargetValidation.VerifyExecutable(address, _module);
                HookTargetValidation.VerifyReadable(address, (nuint)body.Length);
                var bytes = new byte[body.Length]; Marshal.Copy(address, bytes, 0, bytes.Length);
                if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(body.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new UnsupportedSignatureException("A complete Hold geometry source body changed: " + body.Name);
            }
            foreach (var constant in Constants)
            {
                var expected = Convert.FromHexString(constant.HexBytes); var address = IntPtr.Add(_module, constant.Rva);
                HookTargetValidation.VerifyReadable(address, (nuint)expected.Length);
                var actual = new byte[expected.Length]; Marshal.Copy(address, actual, 0, actual.Length);
                if (!actual.AsSpan().SequenceEqual(expected))
                    throw new UnsupportedSignatureException("A Hold geometry source constant changed: " + constant.Name);
            }
            VerifyModule(); _hold.VerifyCurrent();
        }
        catch { Interlocked.Exchange(ref _faulted, 1); throw; }
        finally { Volatile.Write(ref _checking, 0); }
    }
    private void RequireBasis(string name, NativeVector3 expected)
    {
        var actual = _api.ReadStatic<NativeVector3>(_vectorClass, name);
        if (BitConverter.SingleToInt32Bits(actual.X) != BitConverter.SingleToInt32Bits(expected.X)
            || BitConverter.SingleToInt32Bits(actual.Y) != BitConverter.SingleToInt32Bits(expected.Y)
            || BitConverter.SingleToInt32Bits(actual.Z) != BitConverter.SingleToInt32Bits(expected.Z))
            throw new UnsupportedSignatureException("The current Unity vector basis differs from its pinned initialization: " + name);
    }
    private void VerifyModule()
    {
        if (GetModuleHandle("GameAssembly.dll") != _module || GetModuleHandle(_path) != _module)
            throw new UnsupportedSignatureException("The Hold geometry source module changed or was unloaded.");
    }

    internal sealed record Body(string Name, int Rva, int Length, string Sha256);
    internal sealed record DataConstant(string Name, int Rva, string HexBytes);
    internal static IReadOnlyList<DataConstant> Constants { get; } = Array.AsReadOnly(new DataConstant[]
    {
        new("NoteDepth.TypeStep", 0x17c4bb0, "cdcc4c3d"),
        new("NoteDepth.Offset", 0x17c4bcc, "0000003f"),
        new("Geometry.One", 0x17c4bf4, "0000803f"),
        new("Geometry.PastRange", 0x17c4c30, "00004040"),
        new("Geometry.FutureRange", 0x17c4e30, "00000041"),
        new("Ease.SampleScale", 0x17c4c44, "0000c842"),
    });
    // Reuse the earlier renderer profile's exact wrapper pins without invoking
    // renderer commands or asserting resolved internal-call/callback closure.
    internal static IReadOnlyList<string> ReusedWrapperNames { get; } = Array.AsReadOnly(new[]
    {
        "Component.get_transform", "Transform.get_parent", "Transform.get_localPosition",
        "Transform.get_localRotation", "Transform.get_localScale", "Transform.SetParent(Transform)",
        "Transform.SetParent(Transform,Boolean)", "Transform.set_localPosition",
        "Transform.set_localRotation", "Transform.set_localScale",
    });
    internal static IReadOnlyList<Body> NewBodies { get; } = Array.AsReadOnly(new Body[]
    {
        new("CanvasControl.GetInformations", 0x3e3620, 1152, "7e2930d7f43eecb08734452b33a297ddc59f88a3f056ac98289da5da62e45e69"),
        new("CanvasControl.SetPosition", 0x3e3aa0, 400, "a0126b0876263480c1275818b6922419ba20f4769747113d5c8ff723b57c0c0e"),
        new("CanvasControl.Start", 0x3e3c30, 192, "6e5d6401a34cef4ce3194dc2059bd0ee653f7caf118079e23254e1cbdb39508b"),
        new("CanvasControl.Update", 0x3e3cf0, 480, "db91cf6bf18ae8635b345836b282c4adb7bdd68b2baa79149d6cb32fd31b601d"),
        new("LineControl.SetDynamicLines", 0x3ecf40, 1520, "d43eaaada53e04957a151cdea0b27ef98503bb7beb8e3f4757ee12aee5358e0d"),
        new("LineControl.SetRing", 0x3ee5b0, 3968, "327f75bc1b10dae128a7198af2e7acfc3b9e9c854f27e31fcf6896f50271631a"),
        new("LineControl.Start", 0x3ef530, 64, "352fcb5e78d297aa4779173c178ea3db39bd908ba61c6077d75ba04cfd252b2d"),
        new("LineControl.Update", 0x3ef570, 176, "355be275d5c75c3762d345a94c97db0722af3b042fd04d69cf0a361f90dc4042"),
        new("NoteControl.SetNotes", 0x3fc620, 1808, "3d38a7758480fd5c67db79cf56cdb7389819b206fd7665bec48b40f68a00a279"),
        new("NoteControl.Update", 0x3fcd60, 96, "5417f540d23b19f5f06ce1e38a5cbeaf16d8ad84077808b7a027b8ded3d471d0"),
        new("ProgressControl.ChartTimeRageToRealTime", 0x3fff90, 1136, "127814643f82f15c4711bd86f97c2ffd603bba997cc24122c07ceb8e1298e3ef"),
        new("GetEase.GetEaseWithProgress", 0x3e76c0, 416, "975d0e2668b981ecdec69c3e6f875e339b1b6f4d3b1483b176dc7e1a7cd51aa5"),
        new("Extensions.With(Vector3)", 0x38ab80, 160, "d883eb82a488cf3e715c7f30c01d6dd6c711ac37e71abfeaeffbd701ec132bca"),
        new("Vector3..cctor", 0x1548ac0, 320, "94c90dcefdd5c55b567682f14da7e6481d912a1388064f209e31272c7729e66e"),
        new("Transform.get_position", 0x1561970, 80, "1a849a4c9e4f4eff86f864cee5e0f83dee81938f13bf1236432b6258647cb987"),
        new("Transform.set_position", 0x1561f70, 80, "5f76c852033f4f66bdb000c08e76c0cff299f95b40e01d8cc9b03dcf135f77d0"),
        new("Transform.TransformPoint(Vector3)", 0x1560f40, 96, "cabb8a108415a6baeed3570a74c047e2ccab718e1481ac3bffe3f3d630182c4d"),
        new("Transform.InverseTransformPoint(Vector3)", 0x1560590, 96, "19dc43fb26d8ab3b806f1aa8ba0692058722e61a98ef4c459f89fbfd022bd2b5"),
    });
    internal static IReadOnlyList<Body> Bodies { get; } = Array.AsReadOnly(NewBodies.Concat(
        ReusedWrapperNames.Select(name => NativeSeekRendererProfile.Bodies.Single(body => body.Name == name))
            .Select(body => new Body(body.Name, body.Rva, body.Length, body.Sha256))).ToArray());
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern uint GetModuleFileName(IntPtr module, StringBuilder path, int size);
}
