using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.Gameplay;

internal interface ISeekCanvasPositionNativeProfile
{
    IntPtr TransformClass { get; }
    IntPtr GetLocalPositionMethod { get; }
    IntPtr SetLocalPositionMethod { get; }
    IntPtr SetParentMethod { get; }
    void VerifyCurrent();
    void VerifyEndpointCurrent(Action guard);
}

/// <summary>Exact current Transform local-position and SetParent metadata with
/// complete native wrapper evidence. No Transform method is invoked by this profile;
/// resolved Unity internal-call effects and complete Seek remain unverified.</summary>
internal sealed class NativeSeekCanvasPositionNativeProfile : ISeekCanvasPositionNativeProfile
{
    internal const string Fingerprint = GameplayClockNativeProfile.Fingerprint;
    internal const bool RuntimePositionVerified = false;
    internal const bool ResolvedUnityInternalCallsKnown = false;
    internal const bool EngineConsumerClosureKnown = false;
    internal const bool CompleteSeekPermitted = false;
    internal const string Evidence = "Pinned GameAssembly " + Fingerprint
        + "; unchanged complete Transform.get_localPosition, Transform.set_localPosition and"
        + " Transform.SetParent(Transform, Boolean) wrappers, current exact declared MethodInfo/entry"
        + " and fresh Vector3/Single/Boolean/Void ABI metadata."
        + " This profile invokes no Transform method, supplies no source root and depends on no Hold instance."
        + " Resolved Unity internal-call implementations, callbacks and complete Seek are unverified.";

    private readonly NativeRuntime _api;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly IntPtr _module;
    private readonly string _path;
    private readonly NativeMetadataClass _transform;
    private readonly NativeMetadataMethod _getter, _setter, _parent;
    private int _checking, _faulted;
    public IntPtr TransformClass => _transform.Class;
    public IntPtr GetLocalPositionMethod => _getter.MethodInfo;
    public IntPtr SetLocalPositionMethod => _setter.MethodInfo;
    public IntPtr SetParentMethod => _parent.MethodInfo;

    internal NativeSeekCanvasPositionNativeProfile(NativeRuntime api)
    {
        ArgumentNullException.ThrowIfNull(api); _api = api;
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8)
            throw new UnsupportedSignatureException("Canvas position evidence requires the pinned Windows x64 image.");
        _module = GetModuleHandle("GameAssembly.dll");
        if (_module == IntPtr.Zero) throw new UnsupportedSignatureException("The actual Canvas position image is absent.");
        var path = new StringBuilder(32768);
        var count = GetModuleFileName(_module, path, path.Capacity);
        if (count == 0 || count >= path.Capacity)
            throw new UnsupportedSignatureException("The mapped Canvas position image path is unavailable.");
        _path = path.ToString();
        using (var file = File.OpenRead(_path))
        using (var hash = SHA256.Create())
            if (!Convert.ToHexString(hash.ComputeHash(file)).Equals(Fingerprint, StringComparison.OrdinalIgnoreCase))
                throw new UnsupportedSignatureException("The Canvas position image differs from its pinned evidence.");
        _transform = _api.InspectLineEndpointTransformClass(
            _api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Transform"), () => { });
        if (_transform.TypeName != "UnityEngine.Transform")
            throw new UnsupportedSignatureException("The Canvas position profile has another exact Transform class.");
        _getter = Bind("get_localPosition", 0x1561640);
        _setter = Bind("set_localPosition", 0x1561cd0);
        _parent = _api.InspectLineEndpointParentMethod(TransformClass, _module, () => { });
        if (_parent.Entry?.Rva != 0x1560d80)
            throw new UnsupportedSignatureException("The exact SetParent(Transform, Boolean) entry differs from pinned evidence.");
        VerifyCurrent();
    }
    private NativeMetadataMethod Bind(string name, int rva)
    {
        var method = _api.InspectLineEndpointPositionMethod(TransformClass, name, _module, () => { });
        if (method.Entry?.Rva != rva || !Bodies.Any(body => body.Rva == rva))
            throw new UnsupportedSignatureException("The Canvas position method entry differs from its complete wrapper evidence: " + name);
        return method;
    }
    public void VerifyCurrent() => VerifyEndpointCurrent(() => { });
    public void VerifyEndpointCurrent(Action guard)
    {
        ArgumentNullException.ThrowIfNull(guard);
        var guardFailed = false;
        void Guard()
        {
            try { guard(); }
            catch { guardFailed = true; throw; }
        }
        Guard();
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Canvas position profile belongs to its owner thread.");
        if (Volatile.Read(ref _faulted) != 0)
            throw new UnsupportedSignatureException("The Canvas position profile has been withdrawn.");
        if (Interlocked.CompareExchange(ref _checking, 1, 0) != 0)
            throw new InvalidOperationException("Canvas position profile verification cannot reenter.");
        try
        {
            VerifyModule(Guard);
            Guard(); var currentClass = _api.InspectLineEndpointTransformClass(TransformClass, Guard); Guard();
            if (currentClass != _transform)
                throw new UnsupportedSignatureException("The Canvas position Transform metadata changed.");
            foreach (var expected in new[] { _getter, _setter, _parent })
            {
                Guard();
                var current = expected.Name == "SetParent"
                    ? _api.InspectLineEndpointParentMethod(TransformClass, _module, Guard)
                    : _api.InspectLineEndpointPositionMethod(TransformClass, expected.Name, _module, Guard);
                Guard();
                if (current.MethodInfo != expected.MethodInfo || current.DeclaringClass != expected.DeclaringClass
                    || current.Name != expected.Name || current.ReturnType != expected.ReturnType
                    || !current.ParameterTypes.SequenceEqual(expected.ParameterTypes) || current.Entry != expected.Entry
                    || current.Flags != expected.Flags || current.ImplementationFlags != expected.ImplementationFlags
                    || current.GenericMethod != expected.GenericMethod || current.InflatedMethod != expected.InflatedMethod)
                    throw new UnsupportedSignatureException("The exact Canvas position MethodInfo changed: " + expected.Name);
            }
            foreach (var body in Bodies)
            {
                var address = IntPtr.Add(_module, body.Rva);
                Guard(); HookTargetValidation.VerifyExecutable(address, _module); Guard();
                HookTargetValidation.VerifyReadable(address, (nuint)body.Length); Guard();
                var bytes = new byte[body.Length]; Marshal.Copy(address, bytes, 0, bytes.Length);
                Guard();
                if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(body.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new UnsupportedSignatureException("A complete Canvas position wrapper changed: " + body.Name);
            }
            VerifyModule(Guard);
        }
        catch { if (!guardFailed) Interlocked.Exchange(ref _faulted, 1); throw; }
        finally { Volatile.Write(ref _checking, 0); }
    }
    private void VerifyModule(Action guard)
    {
        guard(); var byName = GetModuleHandle("GameAssembly.dll"); guard();
        var byPath = GetModuleHandle(_path); guard();
        if (byName != _module || byPath != _module)
            throw new UnsupportedSignatureException("The Canvas position module changed or was unloaded.");
    }
    internal sealed record Body(string Name, int Rva, int Length, string Sha256);
    internal static IReadOnlyList<Body> Bodies { get; } = Array.AsReadOnly(new Body[]
    {
        new("Transform.get_localPosition", 0x1561640, 80, "612e70f7eec18a576df840931de06002cb68726cb1b57e0361fc2458378ba0c2"),
        new("Transform.set_localPosition", 0x1561cd0, 80, "2c7935a8b473251506fc074d13e9b7a0de7a5bc73ea4dc9d6e58fa12038ec09b"),
        new("Transform.SetParent(Transform,Boolean)", 0x1560d80, 96, "bfcbdf4f759720fdc20bd1b1cbe40683ac00a170e56f3484b5a4ed1c63e9d28c"),
    });
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern uint GetModuleFileName(IntPtr module, StringBuilder path, int size);
}
