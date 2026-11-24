using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>A chart profile alone does not identify renderer command methods.</summary>
internal interface ISeekRendererNativeProfile : ISeekChartNativeProfile
{
    IntPtr NoteClass { get; }
    IntPtr RendererClass { get; }
    IntPtr PoolClass { get; }
    IntPtr ChildClass { get; }
    IntPtr AppearMethod { get; }
    IntPtr DisappearMethod { get; }
}

/// <summary>Exact metadata and complete script/wrapper bodies for the pinned image.
/// Invokes no game methods. Includes allocation and dirty-return branches but does
/// not establish an inverse, engine callback closure, or deferred completion.</summary>
internal sealed class NativeSeekRendererProfile : ISeekRendererNativeProfile
{
    internal const string Fingerprint = GameplayClockNativeProfile.Fingerprint;
    internal const bool RuntimeEffectsVerified = false;
    internal const bool EngineConsumerClosureKnown = false;
    internal const bool ExactInverseKnown = false;
    internal const string Evidence = "Pinned GameAssembly " + Fingerprint
        + "; complete NoteInformation Appear/Disappear/SendRendererEvent, EasyPool and EasyPoolChild bodies;"
        + " exact Transform/Instantiate wrappers and directly used shared List helper bodies."
        + " Source evidence only: resolved engine internal calls, prefab callbacks, UnityEvent listeners,"
        + " coroutine completion and allocation cleanup remain unverified. Dirty returns remain pending.";

    private readonly NativeRuntime _api;
    private readonly ISeekChartNativeProfile _chart;
    private readonly ExactHookBroker.Binding? _coroutineStart;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly IntPtr _module;
    private readonly string _path;
    private readonly List<NativeMetadataMethod> _methods = new();
    private readonly List<NativeExactReadField> _fields = new();
    private int _checking, _faulted;
    public IntPtr ChartClass => _chart.ChartClass;
    public IntPtr ProgressClass => _chart.ProgressClass;
    public IntPtr NoteClass { get; }
    public IntPtr RendererClass { get; }
    public IntPtr PoolClass { get; }
    public IntPtr ChildClass { get; }
    public IntPtr AppearMethod { get; }
    public IntPtr DisappearMethod { get; }
    internal IntPtr SendRendererEventMethod { get; }

    internal NativeSeekRendererProfile(NativeRuntime api, ISeekChartNativeProfile chartProfile,
        ExactHookBroker.Binding? coroutineStart = null)
    {
        ArgumentNullException.ThrowIfNull(api); ArgumentNullException.ThrowIfNull(chartProfile);
        _api = api; _chart = chartProfile; _coroutineStart = coroutineStart;
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8)
            throw new UnsupportedSignatureException("Renderer source evidence requires the pinned Windows x64 image.");
        _module = GetModuleHandle("GameAssembly.dll");
        if (_module == IntPtr.Zero) throw new UnsupportedSignatureException("The actual renderer image is absent.");
        var path = new StringBuilder(32768);
        var count = GetModuleFileName(_module, path, path.Capacity);
        if (count == 0 || count >= path.Capacity)
            throw new UnsupportedSignatureException("The mapped renderer image path is unavailable.");
        _path = path.ToString();
        using (var file = File.OpenRead(_path))
        using (var hash = SHA256.Create())
            if (!Convert.ToHexString(hash.ComputeHash(file)).Equals(Fingerprint, StringComparison.OrdinalIgnoreCase))
                throw new UnsupportedSignatureException("The renderer image differs from the pinned source evidence.");
        _chart.VerifyCurrent();
        NoteClass = api.Class("Assembly-CSharp.dll", "InLevel", "NoteInformation");
        RendererClass = api.NestedClass(NoteClass, "NoteRenderer");
        PoolClass = api.Class("Assembly-CSharp.dll", "InLevel", "EasyPool");
        ChildClass = api.Class("Assembly-CSharp.dll", "InLevel", "EasyPoolChild");
        var color = api.NestedClass(ChildClass, "ColorTarget");
        var trigger = api.NestedClass(ChildClass, "Trigger");
        var offset = api.NestedClass(ChildClass, "<OffsetTrigger>d__9");
        RequireClass(NoteClass, "InLevel.NoteInformation");
        RequireClass(RendererClass, "InLevel.NoteInformation.NoteRenderer");
        RequireClass(PoolClass, "InLevel.EasyPool");
        RequireClass(ChildClass, "InLevel.EasyPoolChild");
        RequireClass(color, "InLevel.EasyPoolChild.ColorTarget");
        RequireClass(trigger, "InLevel.EasyPoolChild.Trigger");
        RequireClass(offset, "InLevel.EasyPoolChild.<OffsetTrigger>d__9");
        Field(NoteClass, "noteRenderers", "InLevel.NoteInformation.NoteRenderer[]", 0x60);
        Field(NoteClass, "_index", "System.Int32", 0x68);
        Field(RendererClass, "poolIndex", "System.Int32", 0x10);
        Field(RendererClass, "pool", "InLevel.EasyPool", 0x18);
        Field(RendererClass, "poolChild", "InLevel.EasyPoolChild", 0x20);
        Field(RendererClass, "poolChildTransformPlan", "UnityEngine.Transform", 0x28);
        Field(RendererClass, "active", "System.Boolean", 0x30);
        Field(PoolClass, "easyPoolChild", "InLevel.EasyPoolChild", 0x20);
        Field(PoolClass, "poolSize", "System.Int32", 0x28);
        Field(PoolClass, "_childCount", "System.Int32", 0x2c);
        Field(PoolClass, "_childList", "System.Collections.Generic.List<InLevel.EasyPoolChild>", 0x30);
        Field(PoolClass, "_dirtyChildList", "System.Collections.Generic.List<InLevel.EasyPoolChild>", 0x38);
        Field(ChildClass, "targetComponent", "UnityEngine.Component", 0x20);
        Field(ChildClass, "parentPool", "InLevel.EasyPool", 0x28);
        Field(ChildClass, "triggers", "System.Collections.Generic.List<InLevel.EasyPoolChild.Trigger>", 0x30);
        Field(ChildClass, "dirty", "System.Boolean", 0x38);
        Field(ChildClass, "colorTargets", "System.Collections.Generic.List<InLevel.EasyPoolChild.ColorTarget>", 0x40);
        Field(color, "sprite", "UnityEngine.SpriteRenderer", 0x10);
        Field(color, "color", "UnityEngine.Color", 0x18);
        Field(trigger, "events", "UnityEngine.Events.UnityEvent", 0x10);
        Field(trigger, "offset", "System.Single", 0x18);
        Field(offset, "<>1__state", "System.Int32", 0x10);
        Field(offset, "<>2__current", "System.Object", 0x18);
        Field(offset, "<>4__this", "InLevel.EasyPoolChild", 0x20);
        Field(offset, "index", "System.Int32", 0x28);
        AppearMethod = Bind(NoteClass, "Appear", "System.Void", 0x3fcdc0);
        DisappearMethod = Bind(NoteClass, "Disappear", "System.Void", 0x3fceb0);
        SendRendererEventMethod = Bind(NoteClass, "SendRendererEvent", "System.Void", 0x3fcf70, "System.Int32");
        Bind(PoolClass, "GetItem", "InLevel.EasyPoolChild", 0x3e57a0, "UnityEngine.Transform");
        Bind(PoolClass, "ReturnToPool", "System.Void", 0x3e5d20, "InLevel.EasyPoolChild");
        Bind(PoolClass, "Start", "System.Void", 0x3e5f10);
        Bind(PoolClass, "LateUpdate", "System.Void", 0x3e5aa0);
        Bind(ChildClass, "Reset", "System.Void", 0x3f7c00);
        Bind(ChildClass, "StartTrigger", "System.Void", 0x3f7d20, "System.Int32");
        Bind(ChildClass, "ReturnToPool", "System.Void", 0x3f7ce0);
        Bind(ChildClass, "SetDirty", "System.Void", 0x3f7d10);
        Bind(ChildClass, "OffsetTrigger", "System.Collections.IEnumerator", 0x3f7b80, "System.Int32");
        Bind(offset, "MoveNext", "System.Boolean", 0x403d40);
        var component = api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Component");
        var transform = api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Transform");
        var sprite = api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "SpriteRenderer");
        var unityEvent = api.Class("UnityEngine.CoreModule.dll", "UnityEngine.Events", "UnityEvent");
        var mono = api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "MonoBehaviour");
        var unityObject = api.Class("UnityEngine.CoreModule.dll", "UnityEngine", "Object");
        Bind(component, "get_transform", "UnityEngine.Transform", 0x1536440);
        Bind(transform, "set_parent", "System.Void", 0x1561e60, "UnityEngine.Transform");
        Bind(transform, "SetParent", "System.Void", 0x1560d30, "UnityEngine.Transform");
        Bind(transform, "SetParent", "System.Void", 0x1560d80, "UnityEngine.Transform", "System.Boolean");
        Bind(transform, "set_localPosition", "System.Void", 0x1561cd0, "UnityEngine.Vector3");
        Bind(transform, "set_localRotation", "System.Void", 0x1561d70, "UnityEngine.Quaternion");
        Bind(transform, "set_localScale", "System.Void", 0x1561e10, "UnityEngine.Vector3");
        Bind(transform, "get_parent", "UnityEngine.Transform", 0x1560470);
        Bind(transform, "get_localPosition", "UnityEngine.Vector3", 0x1561640);
        Bind(transform, "get_localRotation", "UnityEngine.Quaternion", 0x15616e0);
        Bind(transform, "get_localScale", "UnityEngine.Vector3", 0x1561780);
        Bind(sprite, "set_color", "System.Void", 0x155c010, "UnityEngine.Color");
        Bind(unityEvent, "Invoke", "System.Void", 0x15642e0);
        Bind(mono, "StartCoroutine", "UnityEngine.Coroutine", 0x153d380, "System.Collections.IEnumerator");
        BindStatic(unityObject, "Instantiate", "UnityEngine.Object", 0x153faa0, "UnityEngine.Object", "UnityEngine.Transform", "System.Boolean");
        VerifyCurrent();
    }

    private void RequireClass(IntPtr cls, string name)
    {
        if (_api.InspectMetadataClass(cls).TypeName != name)
            throw new UnsupportedSignatureException("Renderer metadata has another exact class: " + name);
    }
    private void Field(IntPtr cls, string name, string type, int offset)
        => _fields.Add(_api.InspectExactDeclaredReadField(cls, name, type, false, offset));
    private IntPtr Bind(IntPtr cls, string name, string returns, int rva, params string[] parameters)
        => Save(_api.InspectDeclaredMethod(cls, name, parameters, returns, _module), rva, false);
    private IntPtr BindStatic(IntPtr cls, string name, string returns, int rva, params string[] parameters)
        => Save(_api.InspectDeclaredStaticMethod(cls, name, parameters, returns, _module), rva, true);
    private IntPtr Save(NativeMetadataMethod method, int rva, bool isStatic)
    {
        if (method.Entry?.Rva != rva || method.GenericMethod || method.InflatedMethod
            || ((method.Flags & 0x10) != 0) != isStatic || !Bodies.Any(body => body.Rva == rva))
            throw new UnsupportedSignatureException("Renderer method metadata/entry differs: " + method.Name);
        _methods.Add(method);
        return method.MethodInfo;
    }
    public void VerifyCurrent()
    {
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Renderer profile must be verified on its owner thread.");
        if (Volatile.Read(ref _faulted) != 0)
            throw new UnsupportedSignatureException("The renderer source profile has been withdrawn.");
        if (Interlocked.CompareExchange(ref _checking, 1, 0) != 0)
            throw new InvalidOperationException("Renderer source verification cannot reenter.");
        try
        {
            _chart.VerifyCurrent(); VerifyModule();
            foreach (var field in _fields)
                if (_api.InspectExactDeclaredReadField(field.DeclaringClass, field.Name, field.TypeName, false, field.Offset) != field)
                    throw new UnsupportedSignatureException("Renderer field metadata changed: " + field.Name);
            foreach (var expected in _methods)
            {
                var current = (expected.Flags & 0x10) != 0
                    ? _api.InspectDeclaredStaticMethod(expected.DeclaringClass.Class, expected.Name, expected.ParameterTypes.ToArray(), expected.ReturnType, _module)
                    : _api.InspectDeclaredMethod(expected.DeclaringClass.Class, expected.Name, expected.ParameterTypes.ToArray(), expected.ReturnType, _module);
                if (current.MethodInfo != expected.MethodInfo || current.DeclaringClass != expected.DeclaringClass
                    || current.Entry != expected.Entry || current.Flags != expected.Flags
                    || current.ImplementationFlags != expected.ImplementationFlags || current.GenericMethod || current.InflatedMethod)
                    throw new UnsupportedSignatureException("Renderer method metadata changed: " + expected.Name);
            }
            foreach (var body in Bodies)
            {
                var address = IntPtr.Add(_module, body.Rva);
                if (body.Rva == 0x153d380 && _coroutineStart is not null)
                {
                    // The actual installed Start observer proves its exact live
                    // patch and reconstructs the entire pre-install body hash.
                    _coroutineStart.VerifyOriginalBody(_module, address, body.Length, body.Sha256);
                    continue;
                }
                HookTargetValidation.VerifyExecutable(address, _module);
                HookTargetValidation.VerifyReadable(address, (nuint)body.Length);
                var bytes = new byte[body.Length]; Marshal.Copy(address, bytes, 0, bytes.Length);
                if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(body.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new UnsupportedSignatureException("A complete renderer source body changed: " + body.Name);
            }
            VerifyModule(); _chart.VerifyCurrent();
        }
        catch { Interlocked.Exchange(ref _faulted, 1); throw; }
        finally { Volatile.Write(ref _checking, 0); }
    }
    private void VerifyModule()
    {
        if (GetModuleHandle("GameAssembly.dll") != _module || GetModuleHandle(_path) != _module)
            throw new UnsupportedSignatureException("The renderer source module changed or was unloaded.");
    }
    internal sealed record Body(string Name, int Rva, int Length, string Sha256);
    // File-only reproduction: work/seek-target-semantics-astra-20261108/extract-renderer-profile.py.
    // Full next-metadata spans include every image runtime-function instruction.
    // Five anonymous shared helpers use exact PE runtime-function bounds; no
    // generic MethodInfo identity or internal-call target closure is invented.
    internal static IReadOnlyList<Body> Bodies { get; } = Array.AsReadOnly(new Body[]
    {
        new("NoteInformation.Appear", 0x3fcdc0, 240, "2d81789ec15171d3fe889383dc985f9c9ab23cf828da1e8692394ac2ddfa6334"),
        new("NoteInformation.Disappear", 0x3fceb0, 192, "ba8e0a2ed2e06049734477c36523af49be3c77a3d6e9ee0d80a566b70b7e40d1"),
        new("NoteInformation.SendRendererEvent", 0x3fcf70, 160, "a801199c38e7975bc74ab9c30815a74b4d7b1d7febf5ce697c17e9d79be71929"),
        new("EasyPool.GetItem", 0x3e57a0, 768, "d792371fe7cd6ce7995ac1498abc079a8dbd47f29b9ac9451c84d41eeb056958"),
        new("EasyPool.ReturnToPool", 0x3e5d20, 496, "1426b37df64c476786e627d837d01ef937a4963dfe02859fb05926bd973f39f5"),
        new("EasyPool.Start", 0x3e5f10, 320, "ae556d146e76dff53ac549b1a3697f371dd8231f99e1020d5b45116119fa1850"),
        new("EasyPool.LateUpdate", 0x3e5aa0, 640, "31f075f4ebe00544f2c220108e42ffc3acd95714ddaeba550780f91fcfcc4aff"),
        new("EasyPoolChild.Reset", 0x3f7c00, 224, "78021c382e7119117b8d95e2cea8ab22f5cf24303280e0c3acfee3863afd0125"),
        new("EasyPoolChild.StartTrigger", 0x3f7d20, 288, "20d367bdcc748b34ece1636bfc4b910743cef2b99495c4742c4d441e2ef69847"),
        new("EasyPoolChild.ReturnToPool", 0x3f7ce0, 48, "b4a6d042e9b8f9437dd33c5c8a96abfb8c34cec230e8646e23e17433648bc43d"),
        new("EasyPoolChild.SetDirty", 0x3f7d10, 16, "106b55a152383dc6fe499560c29d8dcd11c085e831951cce148f4629e8428c4c"),
        new("EasyPoolChild.OffsetTrigger", 0x3f7b80, 128, "d71ba48bc603a83b8575a4a9662aff6ae92503aca32a4ef44d5fbb3c8ff07457"),
        new("OffsetTrigger.MoveNext", 0x403d40, 288, "575a9b4a85ca6e289f37606fe431e382e013c6ff1d841c6e34617d6ff7efb2d4"),
        new("Component.get_transform", 0x1536440, 64, "140cdc11d450d35fe4483722d36501b7cc517fbe4f25532967c205125a0de1da"),
        new("Transform.set_parent", 0x1561e60, 192, "d0b316c1dcd93eafd1782b729de3291cfc1f87818e2db75f44c82273156382cb"),
        new("Transform.SetParent(Transform)", 0x1560d30, 80, "6276460d175df8aaeb3f8fd40873154efc32288cdec2ee80f812586a410093b2"),
        new("Transform.SetParent(Transform,Boolean)", 0x1560d80, 96, "bfcbdf4f759720fdc20bd1b1cbe40683ac00a170e56f3484b5a4ed1c63e9d28c"),
        new("Transform.set_localPosition", 0x1561cd0, 80, "2c7935a8b473251506fc074d13e9b7a0de7a5bc73ea4dc9d6e58fa12038ec09b"),
        new("Transform.set_localRotation", 0x1561d70, 80, "bed827a378327de73a02900f09220765f88ce81c0516fe196e0c9e2a6471f166"),
        new("Transform.set_localScale", 0x1561e10, 80, "1210fd239927e170d8217a27c4cb538fd6ea748ab1210243681913274ee32cf3"),
        new("Transform.get_parent", 0x1560470, 64, "2518545c8a00396c913446b0ecb31843d6778cdf31988a80c0e58a4cb81c85fa"),
        new("Transform.get_localPosition", 0x1561640, 80, "612e70f7eec18a576df840931de06002cb68726cb1b57e0361fc2458378ba0c2"),
        new("Transform.get_localRotation", 0x15616e0, 80, "137baac237d0a1753ef7fb200f02cf00b19dfe2984c05029ab36f51d40614605"),
        new("Transform.get_localScale", 0x1561780, 80, "67f3737384dcb03edc6dd36f10e77ac7fd9130187c694af8e73c1b46f45389cb"),
        new("SpriteRenderer.set_color", 0x155c010, 80, "f809f08bd003bfeabd74ab9181ce256b8e202aa1a57263029d4651dc7bd2a780"),
        new("UnityEvent.Invoke", 0x15642e0, 624, "625f2c70ab6f2aee5aef7c8aca789fc925873511c71bb14b683ec9e6e2b4e43c"),
        new("MonoBehaviour.StartCoroutine(IEnumerator)", 0x153d380, 272, "e24c0c93ed499166a8e93f9b8cd76b70eb1f7a3f1ad5d9c7f738ba93a3c19be4"),
        new("Object.Instantiate(Object,Transform,Boolean)", 0x153faa0, 992, "aa3dea004bd80b799a4f41891ee09b63ec3541b60bf81bda575b0e5873b148b8"),
        new("GetItem.InstantiateSharedThunk", 0x5a92c0, 169, "c2e9723939d4b39ed8e1bf1d22bfd589bd64a671bd547f07a480b012ffbbda74"),
        new("List.ReferenceAdd", 0x33b0, 97, "a89c2219156971b73bd518871bd60d364dbb1db88ee05808942015db34addbb6"),
        new("List.ReferenceGetItem", 0x9a40b0, 70, "336f92376243f021da75b7c0e64ffe6f56d998f777141e0f7af467455dc53ce0"),
        new("List.ReferenceRemoveAt", 0x9dced0, 140, "0655c2661d535ca81a4e59187f102a6f7d9cfcb655e6db1f5efc2a961c051662"),
        new("List.ReferenceAddWithResize", 0x9d73f0, 213, "747508cf69ce0ae7e90041724baebf0dcae60e3759476f458c7e697d035c6477"),
    });
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern uint GetModuleFileName(IntPtr module, StringBuilder path, int size);
}
