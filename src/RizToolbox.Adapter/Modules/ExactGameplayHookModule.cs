using System.Runtime.InteropServices;
using RizToolbox.Core.Hooks;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Modules;

/// <summary>Exact machine-entry observations for one explicitly validated Windows game build.</summary>
internal sealed class ExactGameplayHookModule : IAdapterModule
{
    private const string Fingerprint = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79";
    private readonly NativeRuntime _api; private readonly IMainThreadDispatcher _main; private readonly ILogger _log;
    private readonly AdapterBuildProfile _profile; private readonly HookDispatcher _dispatcher; private readonly string _dobby;
    private readonly List<ExactHookBroker.Binding> _bindings = new();
    private bool _installed;
    public AdapterModuleManifest Manifest { get; } = new("rizline.exact-hooks", new(1, 0), new[] { HookCapabilityIds.Observe },
        Array.Empty<string>(), Array.Empty<string>(), new[] {
            Requirement("judge", "GetJudge", "InLevelController.JudgeType", "System.Single"),
            Requirement("pause", "Pause"), Requirement("resume", "Resume") },
        "Exact metadata + known-build RVA + executable-image checks. Native invocation evidence is separate from install success; observation only.");
    public double IntervalSeconds => 1;
    internal ExactGameplayHookModule(NativeRuntime api, IMainThreadDispatcher main, ILogger log,
        AdapterBuildProfile profile, HookDispatcher dispatcher, string dobbyPath)
    { _api = api; _main = main; _log = log; _profile = profile; _dispatcher = dispatcher; _dobby = dobbyPath; }
    private static AdapterSymbolRequirement Requirement(string id, string member, params string[] parameters)
        => new(id, "Assembly-CSharp.dll", "", "NormalModeInLevelController", AdapterSymbolKind.Method, member, false, parameters)
        { ExpectedType = "System.Void" };
    public ModuleProbe Probe()
    {
        _main.VerifyAccess();
        if (_installed) return ModuleProbe.Available;
        if (_profile.GameAssemblySha256 != Fingerprint || _profile.UnityVersion != "2022.3.62f2" || !OperatingSystem.IsWindows() || IntPtr.Size != 8)
            return ModuleProbe.Unsupported("UnsupportedBuild: exact-hook ABI and MethodInfo layout are pinned to the verified Windows x64 build.");
        var file = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "GameAssembly.dll");
        var actual = FileHash.ComputeSha256(file);
        if (actual != Fingerprint) return ModuleProbe.Unsupported("UnsupportedBuild: on-disk GameAssembly fingerprint differs from the pinned exact-hook profile.");
        var module = GetModuleHandle("GameAssembly.dll");
        var controller = _api.Class("Assembly-CSharp.dll", "", "NormalModeInLevelController");
        // The supplied runtime dump records the exported type_get_name spelling verbatim; enum storage is independently checked.
        var judge = _api.ResolveExactHook(GameHookPoints.Judge.Id, module, controller, "GetJudge", new[] { "InLevelController.JudgeType", "System.Single" }, 0x3C0310, ExactHookAbi.InstanceEnum32SingleVoid);
        var pause = _api.ResolveExactHook(GameHookPoints.Pause.Id, module, controller, "Pause", Array.Empty<string>(), 0x3A2410, ExactHookAbi.InstanceVoid);
        var resume = _api.ResolveExactHook(GameHookPoints.Resume.Id, module, controller, "Resume", Array.Empty<string>(), 0x3A2440, ExactHookAbi.InstanceVoid);
        var broker = new ExactHookBroker(_dobby, _main, _log);
        try
        {
            Declare(GameHookPoints.Judge, "void(instance, enum32, float32, MethodInfo*)");
            Declare(GameHookPoints.Pause, "void(instance, MethodInfo*)"); Declare(GameHookPoints.Resume, "void(instance, MethodInfo*)");
            Bind(judge, broker, (phase, invocation, kind, delta) => _dispatcher.Publish(GameHookPoints.Judge, phase, new JudgeHookObservation(invocation, kind, delta)));
            Bind(pause, broker, (phase, invocation, _, _) => _dispatcher.Publish(GameHookPoints.Pause, phase, new ControllerHookObservation(invocation)));
            Bind(resume, broker, (phase, invocation, _, _) => _dispatcher.Publish(GameHookPoints.Resume, phase, new ControllerHookObservation(invocation)));
            _installed = true;
            _log.Log(LogLevel.Info, "Exact hooks installed: GetJudge@3C0310 Pause@3A2410 Resume@3A2440; awaiting actual method calls.");
            return ModuleProbe.Available;
        }
        catch { Release(); throw; }
    }
    private void Declare<T>(HookPoint<T> point, string signature)
    {
        var result = _dispatcher.Declare(point, signature, Fingerprint, "dump.cs exact method + exported metadata + Unity2022 MethodInfo cross-check + executable RVA equality.");
        if (!result.Success) throw new UnsupportedSignatureException(result.Error!.Message);
    }
    private void Bind(VerifiedHookTarget target, ExactHookBroker broker, Action<HookPhase, long, int, float> deliver)
    {
        var binding = broker.Bind(target, instance => _api.ExactHookReceiverMatches(instance, target.DeclaringClass), deliver);
        _bindings.Add(binding); _dispatcher.SetNativeInstalled(target.Id, binding.Describe);
    }
    public void Register(ModuleRegistration registration)
        => registration.RegisterScoped<IHookService>(HookCapabilityIds.Observe, context => _dispatcher.ForPlugin(context));
    public ModuleProbe Tick() => _installed ? ModuleProbe.Available : ModuleProbe.Unsupported("Exact hook module has no logical bindings.");
    public void Release()
    {
        foreach (var binding in _bindings.AsEnumerable().Reverse()) binding.Dispose();
        _bindings.Clear(); _installed = false;
        _dispatcher.SetNativeDetached(GameHookPoints.Judge.Id); _dispatcher.SetNativeDetached(GameHookPoints.Pause.Id); _dispatcher.SetNativeDetached(GameHookPoints.Resume.Id);
        // Native patches and trampolines intentionally remain process-owned. Only subscriptions/bindings are released.
    }
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
}
