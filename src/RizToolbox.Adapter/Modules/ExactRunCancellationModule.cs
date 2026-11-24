using System.Runtime.InteropServices;
using System.Security.Cryptography;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Modules;

/// <summary>Observes accepted Retry only. Back/scene disappearance are not cancellation evidence.</summary>
internal sealed class ExactRunCancellationModule : IAdapterModule
{
    private const string Fingerprint = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79";
    private readonly NativeRuntime _api;
    private readonly IMainThreadDispatcher _main;
    private readonly ILogger _log;
    private readonly AdapterBuildProfile _profile;
    private readonly GameEventState _state;
    private readonly string _dobby;
    private readonly VerifiedRunCancellationQueue _pending;
    private ExactHookBroker.Binding? _binding;

    internal ExactRunCancellationModule(NativeRuntime api, IMainThreadDispatcher main, ILogger log,
        AdapterBuildProfile profile, GameEventState state, string dobby)
    {
        (_api, _main, _log, _profile, _state, _dobby) = (api, main, log, profile, state, dobby);
        _pending = new(state);
    }
    public double IntervalSeconds => 1;
    public AdapterModuleManifest Manifest { get; } = new("rizline.retry-cancellation", new(1, 0),
        new[] { GameLifecycleCapabilityIds.RetryCancellation }, new[] { GameLifecycleCapabilityIds.Lifecycle },
        Array.Empty<string>(), new[] {
            Method("retry.entry", "Assembly-CSharp.dll", "", "NormalModeInLevelController", "Retry", "System.Void"),
            Field("retry.info", "NormalModeInLevelController", "LevelInformation", "InLevel.LevelInformation"),
            Field("retry.pause", "NormalModeInLevelController", "pauseUIControl", "PauseUIControl"),
            Field("retry.pausing", "PauseUIControl", "_pausing", "System.Boolean"),
            Field("retry.over", "PauseUIControl", "_over", "System.Boolean"),
            Method("retry.info-go", "UnityEngine.CoreModule.dll", "UnityEngine", "Component", "get_gameObject", "UnityEngine.GameObject"),
            Method("retry.active-self", "UnityEngine.CoreModule.dll", "UnityEngine", "GameObject", "get_activeSelf", "System.Boolean") },
        "Pinned Retry entry, retained per-invocation context and matching observed run token. Only successful original return plus the original info GameObject activeSelf true-to-false edge proves accepted cancellation; runtime acceptance remains separate.");

    private static AdapterSymbolRequirement Method(string id, string assembly, string ns, string type, string member, string result)
        => new(id, assembly, ns, type, AdapterSymbolKind.Method, member, false, Array.Empty<string>()) { ExpectedType = result };
    private static AdapterSymbolRequirement Field(string id, string type, string member, string result)
        => new(id, "Assembly-CSharp.dll", "", type, AdapterSymbolKind.Field, member, false) { ExpectedType = result };

    public ModuleProbe Probe()
    {
        _main.VerifyAccess();
        if (_binding is not null) return ModuleProbe.Available;
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || _profile.GameAssemblySha256 != Fingerprint || _profile.UnityVersion != "2022.3.62f2")
            return ModuleProbe.Unsupported("UnsupportedBuild: Retry cancellation is pinned to the verified Windows x64 build.");
        var file = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "GameAssembly.dll");
        using var stream = File.OpenRead(file); using var hash = SHA256.Create();
        if (Convert.ToHexString(hash.ComputeHash(stream)).ToLowerInvariant() != Fingerprint)
            return ModuleProbe.Unsupported("UnsupportedBuild: Retry cancellation fingerprint differs.");
        var cls = _api.Class("Assembly-CSharp.dll", "", "NormalModeInLevelController");
        var target = _api.ResolveExactHook("framework.lifecycle.retry", GetModuleHandle("GameAssembly.dll"), cls,
            "Retry", Array.Empty<string>(), 0x3C1310, ExactHookAbi.InstanceVoid);
        var broker = new ExactHookBroker(_dobby, _main, _log);
        _binding = broker.BindRetainedObservation(target,
            receiver => _api.ExactHookReceiverMatches(receiver, target.DeclaringClass),
            Capture, observation =>
            {
                if (observation.Token is not { } token || !observation.AcceptedAfterReturn()) return;
                if (_pending.Enqueue(token,
                    "Original NormalModeInLevelController.Retry returned and its rooted current-run info GameObject changed activeSelf true to false."))
                    _log.Log(LogLevel.Info, $"Accepted Retry cancellation queued for observed run {token.RunId}; sceneGeneration={token.SceneGeneration}.");
            });
        _log.Log(LogLevel.Info, "Retry cancellation observer installed at 3C1310; awaiting an actual accepted invocation.");
        return ModuleProbe.Available;
    }

    private RetryObservation Capture(IntPtr receiver)
    {
        var roots = new List<NativeRuntime.RootHandle>();
        try
        {
            NativeRuntime.RootHandle Keep(IntPtr value) { var root = _api.Root(value); roots.Add(root); return root; }
            var controller = Keep(receiver);
            if (!_api.Unbox<bool>(_api.Call(controller.Target, "get_isActiveAndEnabled"))) return new(_api, roots, null, null);
            var info = _api.Read<IntPtr>(controller.Target, "LevelInformation");
            var pause = _api.Read<IntPtr>(controller.Target, "pauseUIControl");
            if (info == IntPtr.Zero || pause == IntPtr.Zero) return new(_api, roots, null, null);
            var infoRoot = Keep(info); var pauseRoot = Keep(pause);
            var token = _state.CaptureCancellationToken(infoRoot.Target.ToInt64());
            if (token is null || !_api.Read<bool>(pauseRoot.Target, "_pausing") || _api.Read<bool>(pauseRoot.Target, "_over"))
                return new(_api, roots, null, null);
            var go = _api.Call(infoRoot.Target, "get_gameObject");
            if (go == IntPtr.Zero) return new(_api, roots, null, null);
            var gameObject = Keep(go);
            if (!_api.Unbox<bool>(_api.Call(gameObject.Target, "get_activeSelf"))) return new(_api, roots, null, null);
            return new(_api, roots, token, gameObject);
        }
        catch { foreach (var root in roots.AsEnumerable().Reverse()) try { root.Dispose(); } catch { } throw; }
    }
    private sealed class RetryObservation : IDisposable
    {
        private readonly NativeRuntime _api;
        private readonly List<NativeRuntime.RootHandle> _roots;
        private readonly NativeRuntime.RootHandle? _gameObject;
        internal ObservedRunCancellationToken? Token { get; }
        internal RetryObservation(NativeRuntime api, List<NativeRuntime.RootHandle> roots,
            ObservedRunCancellationToken? token, NativeRuntime.RootHandle? gameObject)
            => (_api, _roots, Token, _gameObject) = (api, roots, token, gameObject);
        internal bool AcceptedAfterReturn() => Token is not null && _gameObject is not null &&
            !_api.Unbox<bool>(_api.Call(_gameObject.Target, "get_activeSelf"));
        public void Dispose()
        {
            Exception? failure = null;
            foreach (var root in _roots.AsEnumerable().Reverse()) try { root.Dispose(); } catch (Exception ex) { failure ??= ex; }
            _roots.Clear(); if (failure is not null) throw failure;
        }
    }
    public void Register(ModuleRegistration registration)
        => registration.RegisterScoped<IGameLifecycleService>(GameLifecycleCapabilityIds.RetryCancellation, _state.ForPlugin);
    public ModuleProbe Tick() => _binding is not null ? ModuleProbe.Available : ModuleProbe.Waiting("Retry observer is detached.");
    internal void FlushAccepted() { _main.VerifyAccess(); if (_binding is not null) _pending.Drain(); }
    public void Release() { _main.VerifyAccess(); var binding = _binding; _binding = null; _pending.Clear(); binding?.Dispose(); }
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
}
