using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>Fixed read-only line/ring source metadata. A Canvas profile provides
/// the original chart source; no Hold instance or producer invocation is needed.</summary>
internal interface ISeekLineRingSourceProfile : ISeekChartNativeProfile
{
    IntPtr InformationClass { get; }
    IntPtr LineControlClass { get; }
    IntPtr LineClass { get; }
    IntPtr LinePointClass { get; }
    IntPtr GroupClass { get; }
    IntPtr TransformClass { get; }
    IntPtr UIClass { get; }
    IntPtr CameraClass { get; }
    IntPtr GetTransformMethod { get; }
    IntPtr GetParentMethod { get; }
    IntPtr GetPositionMethod { get; }
    IntPtr GetLocalPositionMethod { get; }
    IntPtr GetLocalRotationMethod { get; }
    IntPtr GetLocalScaleMethod { get; }
    // Test/custom providers retain the base contract. The native implementation
    // additionally carries this guard into the fresh getter ABI inspection.
    void VerifyCurrent(Action guard)
    {
        ArgumentNullException.ThrowIfNull(guard);
        guard(); VerifyCurrent(); guard();
    }
}

/// <summary>Composes the original Canvas profile with exact source descriptors,
/// four complete LineControl body pins and six complete getter wrappers. This
/// profile invokes no getter, producer, initializer or easing method. Actual
/// graph/root capture and resolved Unity effects require separate evidence.</summary>
internal sealed class NativeSeekLineRingSourceProfile : ISeekLineRingSourceProfile
{
    internal const string Fingerprint = GameplayClockNativeProfile.Fingerprint;
    internal const bool RuntimeLineRingSourceVerified = false;
    internal const bool TargetEnvironmentRebuiltKnown = false;
    internal const bool ResolvedUnityInternalCallsKnown = false;
    internal const bool EngineConsumerClosureKnown = false;
    internal const bool ProductionAdmissionKnown = false;
    internal const bool CompleteSeekPermitted = false;
    internal const string Evidence = "Pinned GameAssembly " + Fingerprint
        + "; composes the original Canvas/chart profile, 32 exact source fields, four complete LineControl bodies"
        + " and six complete Component/Transform getter wrappers with fresh reference/Vector3/Quaternion ABI metadata."
        + " No Hold profile or instance is required and no game/Unity method, initializer or field getter is invoked here."
        + " Actual complete arrays, segment/point data, baselines, identities and roots require an independent current capture."
        + " Unpinned external producers/helpers, GetEaseWithIndex, SetPositions, Appear/Disappear and color effects remain open;"
        + " these pins grant no target write, engine-effect closure or complete Seek admission.";

    private readonly NativeRuntime _api;
    private readonly ISeekCanvasNativeProfile _canvas;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly IntPtr _module, _canvasClass;
    private readonly string _path;
    private readonly List<NativeMetadataClass> _classes = new();
    private readonly List<NativeExactReadField> _fields = new();
    private readonly List<NativeMetadataMethod> _methods = new();
    private int _checking, _faulted;

    public IntPtr ChartClass { get; }
    public IntPtr ProgressClass { get; }
    public IntPtr InformationClass { get; }
    public IntPtr LineControlClass { get; }
    public IntPtr LineClass { get; }
    public IntPtr LinePointClass { get; }
    public IntPtr GroupClass { get; }
    public IntPtr TransformClass { get; }
    public IntPtr UIClass { get; }
    public IntPtr CameraClass { get; }
    public IntPtr GetTransformMethod { get; }
    public IntPtr GetParentMethod { get; }
    public IntPtr GetPositionMethod { get; }
    public IntPtr GetLocalPositionMethod { get; }
    public IntPtr GetLocalRotationMethod { get; }
    public IntPtr GetLocalScaleMethod { get; }

    internal NativeSeekLineRingSourceProfile(NativeRuntime api, ISeekCanvasNativeProfile canvasProfile)
    {
        ArgumentNullException.ThrowIfNull(api); ArgumentNullException.ThrowIfNull(canvasProfile);
        _api = api; _canvas = canvasProfile;
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8)
            throw new UnsupportedSignatureException("Line/ring source evidence requires the pinned Windows x64 image.");
        _module = GetModuleHandle("GameAssembly.dll");
        if (_module == IntPtr.Zero) throw new UnsupportedSignatureException("The actual line/ring source image is absent.");
        var path = new StringBuilder(32768);
        var count = GetModuleFileName(_module, path, path.Capacity);
        if (count == 0 || count >= path.Capacity)
            throw new UnsupportedSignatureException("The mapped line/ring source image path is unavailable.");
        _path = path.ToString();
        using (var file = File.OpenRead(_path))
        using (var hash = SHA256.Create())
            if (!Convert.ToHexString(hash.ComputeHash(file)).Equals(Fingerprint, StringComparison.OrdinalIgnoreCase))
                throw new UnsupportedSignatureException("The line/ring source image differs from its pinned evidence.");

        _canvas.VerifyCurrent();
        ChartClass = Class("Chart"); ProgressClass = Class("ProgressControl");
        InformationClass = Class("LevelInformation"); _canvasClass = Class("CanvasControl");
        LineControlClass = Class("LineControl"); LineClass = Class("LineInformation");
        LinePointClass = Class("LinePoint"); GroupClass = Class("LinesGroupInformation");
        TransformClass = Class("UnityEngine.CoreModule.dll", "UnityEngine", "Transform");
        UIClass = Class("UIControl"); CameraClass = Class("CameraControl");
        var component = Class("UnityEngine.CoreModule.dll", "UnityEngine", "Component");
        VerifyCanvasIdentity();
        CaptureFields();
        Bind(LineControlClass, "SetDynamicLines", "System.Void", 0x3ecf40);
        Bind(LineControlClass, "SetRing", "System.Void", 0x3ee5b0);
        Bind(LineControlClass, "Start", "System.Void", 0x3ef530);
        Bind(LineControlClass, "Update", "System.Void", 0x3ef570);
        GetTransformMethod = Bind(component, "get_transform", "UnityEngine.Transform", 0x1536440);
        GetParentMethod = Bind(TransformClass, "get_parent", "UnityEngine.Transform", 0x1560470);
        GetPositionMethod = Bind(TransformClass, "get_position", "UnityEngine.Vector3", 0x1561970);
        GetLocalPositionMethod = Bind(TransformClass, "get_localPosition", "UnityEngine.Vector3", 0x1561640);
        GetLocalRotationMethod = Bind(TransformClass, "get_localRotation", "UnityEngine.Quaternion", 0x15616e0);
        GetLocalScaleMethod = Bind(TransformClass, "get_localScale", "UnityEngine.Vector3", 0x1561780);
        VerifyCurrent();
    }

    private void CaptureFields()
    {
        Field(InformationClass, "lineControl", "InLevel.LineControl", 0x38);
        Field(InformationClass, "uIControl", "InLevel.UIControl", 0x68);
        Field(InformationClass, "cameraControl", "InLevel.CameraControl", 0x78);
        Field(InformationClass, "sceneWidth", "System.Single", 0xc4);
        Field(UIClass, "gameSceneCanvas", "UnityEngine.Transform", 0x20);
        Field(CameraClass, "levelInformation", "InLevel.LevelInformation", 0x20);
        Field(CameraClass, "xPosition", "System.Single", 0x30);
        Field(CameraClass, "scale", "System.Single", 0x34);
        Field(LineControlClass, "levelInformation", "InLevel.LevelInformation", 0x20);
        Field(LineControlClass, "dynamicLines", "InLevel.LineInformation[]", 0x28);
        Field(LineControlClass, "lineGroups", "InLevel.LinesGroupInformation[]", 0x30);
        Field(LineControlClass, "_canvasControl", "InLevel.CanvasControl", 0x38);
        Field(LineControlClass, "_nowTime", "System.Single", 0x40);
        Field(LineControlClass, "_sceneWidth", "System.Single", 0x44);
        Field(LineControlClass, "_speed", "System.Single", 0x48);
        Field(LineControlClass, "_gameSceneScale", "System.Single", 0x4c);
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
    }

    private IntPtr Class(string name) => Class("Assembly-CSharp.dll", "InLevel", name);
    private IntPtr Class(string assembly, string ns, string name)
    {
        var cls = _api.Class(assembly, ns, name);
        var description = _api.InspectMetadataClass(cls);
        if (description.TypeName != ns + "." + name)
            throw new UnsupportedSignatureException("Line/ring metadata names another exact class: " + name);
        _classes.Add(description); return cls;
    }
    private void Field(IntPtr cls, string name, string type, int offset)
        => _fields.Add(_api.InspectLineRingSourceField(cls, name, type, offset, VerifyOwnerThread));
    private static bool IsGetter(string name) => name is "get_transform" or "get_parent" or "get_position"
        or "get_localPosition" or "get_localRotation" or "get_localScale";
    private NativeMetadataMethod InspectMethod(IntPtr cls, string name, string returns, Action? guard = null)
    {
        guard ??= VerifyOwnerThread;
        return IsGetter(name) ? _api.InspectLineRingSourceGetter(cls, name, _module, guard)
            : _api.InspectDeclaredMethod(cls, name, Array.Empty<string>(), returns, _module);
    }
    private IntPtr Bind(IntPtr cls, string name, string returns, int rva)
    {
        var method = InspectMethod(cls, name, returns);
        if (method.MethodInfo == IntPtr.Zero || method.DeclaringClass.Class != cls || method.Name != name
            || method.ReturnType != returns || method.ParameterTypes.Count != 0 || method.Entry?.Rva != rva
            || method.GenericMethod || method.InflatedMethod || (method.Flags & (0x10u | 0x400u)) != 0
            || !Bodies.Any(body => body.Rva == rva))
            throw new UnsupportedSignatureException("Line/ring source method metadata/entry differs: " + name);
        _methods.Add(method); return method.MethodInfo;
    }

    private void VerifyOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Line/ring profile belongs to its owner thread.");
    }
    public void VerifyCurrent() => VerifyCurrentCore(VerifyOwnerThread);
    public void VerifyCurrent(Action guard)
    {
        ArgumentNullException.ThrowIfNull(guard); VerifyCurrentCore(guard);
    }
    private void VerifyCurrentCore(Action? guard)
    {
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Line/ring source profile must be verified on its owner thread.");
        if (Volatile.Read(ref _faulted) != 0)
            throw new UnsupportedSignatureException("The line/ring source profile has been withdrawn.");
        if (Interlocked.CompareExchange(ref _checking, 1, 0) != 0)
            throw new InvalidOperationException("Line/ring source profile verification cannot reenter.");
        var guardFailed = false;
        void Guard()
        {
            try { guard?.Invoke(); }
            catch { guardFailed = true; throw; }
        }
        T Observe<T>(Func<T> read)
        {
            Guard(); var value = read(); Guard(); return value;
        }
        void ObserveAction(Action read)
        {
            Guard(); read(); Guard();
        }
        try
        {
            VerifyModule(Guard); ObserveAction(_canvas.VerifyCurrent); ObserveAction(VerifyCanvasIdentity);
            // Existing class/producer inspectors and the composed Canvas
            // profile expose API-boundary guards only. Their internal export
            // sequences are not certified as individually caller-guarded here.
            foreach (var expected in _classes)
                if (Observe(() => _api.InspectMetadataClass(expected.Class)) != expected)
                    throw new UnsupportedSignatureException("Line/ring class metadata changed: " + expected.TypeName);
            foreach (var expected in _fields)
                if (Observe(() => _api.InspectLineRingSourceField(expected.DeclaringClass, expected.Name, expected.TypeName,
                    expected.Offset, Guard)) != expected)
                    throw new UnsupportedSignatureException("Line/ring field metadata changed: " + expected.Name);
            foreach (var expected in _methods)
            {
                var current = Observe(() => InspectMethod(expected.DeclaringClass.Class, expected.Name, expected.ReturnType, Guard));
                if (current.MethodInfo != expected.MethodInfo || current.DeclaringClass != expected.DeclaringClass
                    || current.Name != expected.Name || current.ReturnType != expected.ReturnType
                    || !current.ParameterTypes.SequenceEqual(expected.ParameterTypes) || current.Entry != expected.Entry
                    || current.Flags != expected.Flags || current.ImplementationFlags != expected.ImplementationFlags
                    || current.GenericMethod != expected.GenericMethod || current.InflatedMethod != expected.InflatedMethod)
                    throw new UnsupportedSignatureException("The exact line/ring source MethodInfo changed: " + expected.Name);
            }
            foreach (var body in Bodies)
            {
                var address = IntPtr.Add(_module, body.Rva);
                ObserveAction(() => HookTargetValidation.VerifyExecutable(address, _module));
                ObserveAction(() => HookTargetValidation.VerifyReadable(address, (nuint)body.Length));
                var bytes = new byte[body.Length]; ObserveAction(() => Marshal.Copy(address, bytes, 0, bytes.Length));
                if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(body.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new UnsupportedSignatureException("A complete line/ring source body changed: " + body.Name);
            }
            VerifyModule(Guard); ObserveAction(_canvas.VerifyCurrent); ObserveAction(VerifyCanvasIdentity);
        }
        catch { if (!guardFailed) Interlocked.Exchange(ref _faulted, 1); throw; }
        finally { Volatile.Write(ref _checking, 0); }
    }
    private void VerifyCanvasIdentity()
    {
        if (_canvas.ChartClass != ChartClass || _canvas.ProgressClass != ProgressClass
            || _canvas.InformationClass != InformationClass || _canvas.CanvasClass != _canvasClass
            || _canvas.TransformClass != TransformClass)
            throw new UnsupportedSignatureException("The line/ring profile differs from the original exact Canvas source classes.");
    }
    private void VerifyModule(Action? guard = null)
    {
        guard?.Invoke(); var named = GetModuleHandle("GameAssembly.dll"); guard?.Invoke();
        var located = GetModuleHandle(_path); guard?.Invoke();
        if (named != _module || located != _module)
            throw new UnsupportedSignatureException("The line/ring source module changed or was unloaded.");
    }

    internal sealed record Body(string Name, int Rva, int Length, string Sha256);
    // All pins are unchanged complete spans in the frozen geometry evidence.
    // Pins of the four producers identify code only; none is invoked and their
    // externalBodyEdges do not establish the unpinned callees or Unity effects.
    internal static IReadOnlyList<Body> Bodies { get; } = Array.AsReadOnly(new Body[]
    {
        new("LineControl.SetDynamicLines", 0x3ecf40, 1520, "d43eaaada53e04957a151cdea0b27ef98503bb7beb8e3f4757ee12aee5358e0d"),
        new("LineControl.SetRing", 0x3ee5b0, 3968, "327f75bc1b10dae128a7198af2e7acfc3b9e9c854f27e31fcf6896f50271631a"),
        new("LineControl.Start", 0x3ef530, 64, "352fcb5e78d297aa4779173c178ea3db39bd908ba61c6077d75ba04cfd252b2d"),
        new("LineControl.Update", 0x3ef570, 176, "355be275d5c75c3762d345a94c97db0722af3b042fd04d69cf0a361f90dc4042"),
        new("Component.get_transform", 0x1536440, 64, "140cdc11d450d35fe4483722d36501b7cc517fbe4f25532967c205125a0de1da"),
        new("Transform.get_parent", 0x1560470, 64, "2518545c8a00396c913446b0ecb31843d6778cdf31988a80c0e58a4cb81c85fa"),
        new("Transform.get_position", 0x1561970, 80, "1a849a4c9e4f4eff86f864cee5e0f83dee81938f13bf1236432b6258647cb987"),
        new("Transform.get_localPosition", 0x1561640, 80, "612e70f7eec18a576df840931de06002cb68726cb1b57e0361fc2458378ba0c2"),
        new("Transform.get_localRotation", 0x15616e0, 80, "137baac237d0a1753ef7fb200f02cf00b19dfe2984c05029ab36f51d40614605"),
        new("Transform.get_localScale", 0x1561780, 80, "67f3737384dcb03edc6dd36f10e77ac7fd9130187c694af8e73c1b46f45389cb"),
    });
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern uint GetModuleFileName(IntPtr module, StringBuilder path, int size);
}
