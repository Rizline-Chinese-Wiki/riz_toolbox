using System.Runtime.InteropServices;
using System.Security.Cryptography;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Modules;

/// <summary>Observes the original Skip plus its real LevelOver side effect. Does not synthesize a result.</summary>
internal sealed class ExactSkipTerminationModule : IAdapterModule
{
    public const string Capability = GameLifecycleCapabilityIds.SkipTermination;
    private const string Fingerprint = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79";
    private readonly NativeRuntime _api;
    private readonly IMainThreadDispatcher _main;
    private readonly ILogger _log;
    private readonly AdapterBuildProfile _profile;
    private readonly GameEventState _state;
    private readonly string _dobby;
    private readonly VerifiedRunCancellationQueue _accepted;
    private ExactHookBroker.Binding? _binding;

    internal ExactSkipTerminationModule(NativeRuntime api, IMainThreadDispatcher main, ILogger log,
        AdapterBuildProfile profile, GameEventState state, string dobby)
    {
        (_api, _main, _log, _profile, _state, _dobby) = (api, main, log, profile, state, dobby);
        _accepted = new(state);
    }
    public double IntervalSeconds => 1;
    public AdapterModuleManifest Manifest { get; } = new("rizline.skip-termination", new(1, 0),
        new[] { Capability }, new[] { GameLifecycleCapabilityIds.Lifecycle }, Array.Empty<string>(), new[] {
            Method("skip.entry", "NormalModeInLevelController", "Skip"),
            Method("skip.level-over", "NormalModeInLevelController", "LevelOver"),
            Field("skip.info", "NormalModeInLevelController", "LevelInformation", "InLevel.LevelInformation"),
            Field("skip.pause", "NormalModeInLevelController", "pauseUIControl", "PauseUIControl"),
            Field("skip.pause-owner", "PauseUIControl", "normalModeInLevelController", "NormalModeInLevelController"),
            Field("skip.skipped", "NormalModeInLevelController", "_skip", "System.Boolean"),
            Field("skip.controller-crashed", "NormalModeInLevelController", "_crashed", "System.Boolean"),
            Field("skip.terminated", "PauseUIControl", "_terminated", "System.Boolean"),
            Field("skip.info-over", "LevelInformation", "levelOver", "System.Boolean", "InLevel"),
            Field("skip.info-crashed", "LevelInformation", "crashed", "System.Boolean", "InLevel"),
            Behaviour("skip.enabled", "get_enabled"), Behaviour("skip.active", "get_isActiveAndEnabled") },
        "Pinned original Skip sets _skip and dispatches LevelOver, which disables that controller's PauseUI. Before refuses ended/crashed/already-skipped state and requires the owned pause enabled; successful original return must show _skip false-to-true plus pause.enabled true-to-false. Same observed run only, deferred Skipped delivery, no fabricated result or late-result origin claim.");
    private static AdapterSymbolRequirement Method(string id, string type, string name)
        => new(id, "Assembly-CSharp.dll", "", type, AdapterSymbolKind.Method, name, false, Array.Empty<string>()) { ExpectedType = "System.Void" };
    private static AdapterSymbolRequirement Field(string id, string type, string name, string result, string ns = "")
        => new(id, "Assembly-CSharp.dll", ns, type, AdapterSymbolKind.Field, name, false) { ExpectedType = result };
    private static AdapterSymbolRequirement Behaviour(string id, string name)
        => new(id, "UnityEngine.CoreModule.dll", "UnityEngine", "Behaviour", AdapterSymbolKind.Method, name, false, Array.Empty<string>()) { ExpectedType = "System.Boolean" };

    public ModuleProbe Probe()
    {
        _main.VerifyAccess(); if (_binding is not null) return ModuleProbe.Available;
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || _profile.GameAssemblySha256 != Fingerprint || _profile.UnityVersion != "2022.3.62f2")
            return ModuleProbe.Unsupported("UnsupportedBuild: Skip termination is pinned to the verified Windows x64 build.");
        var file = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "GameAssembly.dll");
        using var stream = File.OpenRead(file); using var hash = SHA256.Create();
        if (Convert.ToHexString(hash.ComputeHash(stream)).ToLowerInvariant() != Fingerprint)
            return ModuleProbe.Unsupported("UnsupportedBuild: Skip termination fingerprint differs.");
        var cls = _api.Class("Assembly-CSharp.dll", "", "NormalModeInLevelController"); var module = GetModuleHandle("GameAssembly.dll");
        var skip = _api.ResolveExactHook("framework.lifecycle.skip-terminal", module, cls, "Skip", Array.Empty<string>(), 0x3C17D0, ExactHookAbi.InstanceVoid);
        // Verify the static callee whose enabled transition is used as direct after evidence; do not patch it.
        _api.ResolveExactHook("framework.lifecycle.skip-level-over-proof", module, cls, "LevelOver", Array.Empty<string>(), 0x3C0C00, ExactHookAbi.InstanceVoid);
        var broker = new ExactHookBroker(_dobby, _main, _log);
        try
        {
            _binding = broker.BindRetainedObservation(skip, receiver => _api.ExactHookReceiverMatches(receiver, skip.DeclaringClass), Capture, After);
            _log.Log(LogLevel.Info, "Skip termination observer installed at 3C17D0; awaiting an original Skip and owned PauseUI disable edge.");
            return ModuleProbe.Available;
        }
        catch { Release(); throw; }
    }
    private SkipObservation Capture(IntPtr receiver)
    {
        var roots = new RootSet();
        try
        {
            if (_binding is null) return new(_api, roots);
            var controller = roots.Keep(_api, receiver);
            if (!_api.Unbox<bool>(_api.Call(controller.Target, "get_isActiveAndEnabled"))
                || _api.Read<bool>(controller.Target, "_skip") || _api.Read<bool>(controller.Target, "_crashed")) return new(_api, roots);
            var pointer = _api.Read<IntPtr>(controller.Target, "LevelInformation");
            if (pointer == IntPtr.Zero) return new(_api, roots);
            var information = roots.Keep(_api, pointer);
            if (!_api.Unbox<bool>(_api.Call(information.Target, "get_isActiveAndEnabled"))
                || _api.Read<bool>(information.Target, "levelOver") || _api.Read<bool>(information.Target, "crashed")) return new(_api, roots);
            var token = _state.CaptureCancellationToken(information.Target.ToInt64());
            if (token is null) return new(_api, roots);
            pointer = _api.Read<IntPtr>(controller.Target, "pauseUIControl");
            if (pointer == IntPtr.Zero) return new(_api, roots);
            var pause = roots.Keep(_api, pointer);
            if (_api.Read<IntPtr>(pause.Target, "normalModeInLevelController") != controller.Target || _api.Read<bool>(pause.Target, "_terminated")
                || !_api.Unbox<bool>(_api.Call(pause.Target, "get_isActiveAndEnabled")) || !_api.Unbox<bool>(_api.Call(pause.Target, "get_enabled"))) return new(_api, roots);
            return new(_api, roots, token, controller, information, pause);
        }
        catch { roots.DisposeSafely(_log); throw; }
    }
    private void After(SkipObservation observation)
    {
        if (_binding is null || observation.Token is not { } before || !observation.AcceptedAfterReturn()) return;
        var current = _state.CaptureCancellationToken(before.Information);
        if (!SameRun(before, current)) return;
        if (_accepted.Enqueue(current!,
            "Original NormalModeInLevelController.Skip returned across current controller._skip false to true and its same rooted PauseUI.enabled true to false, the verified LevelOver side effect.",
            RunEndReason.Skipped))
            _log.Log(LogLevel.Info, $"Accepted Skip termination queued for observed run {current!.RunId}; sceneGeneration={current.SceneGeneration}; revision={current.Revision}.");
    }
    private static bool SameRun(ObservedRunCancellationToken before, ObservedRunCancellationToken? current)
        => current is not null && before.SessionId == current.SessionId && before.RunId == current.RunId
            && before.SceneGeneration == current.SceneGeneration && before.Information == current.Information;

    private sealed class SkipObservation : IDisposable
    {
        private readonly NativeRuntime _api;
        private readonly RootSet _roots;
        private readonly NativeRuntime.RootHandle? _controller, _information, _pause;
        internal ObservedRunCancellationToken? Token { get; }
        internal SkipObservation(NativeRuntime api, RootSet roots, ObservedRunCancellationToken? token = null,
            NativeRuntime.RootHandle? controller = null, NativeRuntime.RootHandle? information = null, NativeRuntime.RootHandle? pause = null)
            => (_api, _roots, Token, _controller, _information, _pause) = (api, roots, token, controller, information, pause);
        internal bool AcceptedAfterReturn() => _controller is not null && _information is not null && _pause is not null
            && _api.Read<IntPtr>(_controller.Target, "LevelInformation") == _information.Target
            && _api.Read<IntPtr>(_controller.Target, "pauseUIControl") == _pause.Target
            && _api.Read<IntPtr>(_pause.Target, "normalModeInLevelController") == _controller.Target
            && !_api.Read<bool>(_controller.Target, "_crashed") && !_api.Read<bool>(_information.Target, "crashed")
            && _api.Read<bool>(_controller.Target, "_skip") && !_api.Unbox<bool>(_api.Call(_pause.Target, "get_enabled"));
        public void Dispose() => _roots.Dispose();
    }
    private sealed class RootSet : IDisposable
    {
        private readonly List<NativeRuntime.RootHandle> _roots = new();
        internal NativeRuntime.RootHandle Keep(NativeRuntime api, IntPtr pointer)
        {
            var root = api.Root(pointer);
            try { _roots.Add(root); return root; } catch { try { root.Dispose(); } catch { } throw; }
        }
        internal void DisposeSafely(ILogger log) { try { Dispose(); } catch (Exception ex) { try { log.Log(LogLevel.Warning, "Skip capture-root cleanup failed.", ex); } catch { } } }
        public void Dispose()
        {
            Exception? failure = null;
            while (_roots.Count > 0)
            {
                var index = _roots.Count - 1; var root = _roots[index]; _roots.RemoveAt(index);
                try { root.Dispose(); } catch (Exception ex) { failure ??= ex; }
            }
            if (failure is not null) throw failure;
        }
    }
    public void Register(ModuleRegistration registration) => registration.RegisterScoped<IGameLifecycleService>(Capability, _state.ForPlugin);
    public ModuleProbe Tick() { _main.VerifyAccess(); return _binding is not null ? ModuleProbe.Available : ModuleProbe.Waiting("Skip observer is detached."); }
    internal void FlushAccepted() { _main.VerifyAccess(); if (_binding is not null) _accepted.Drain(); }
    public void Release()
    {
        _main.VerifyAccess(); var binding = _binding; _binding = null;
        try { binding?.Dispose(); } finally { _accepted.Clear(); }
    }
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
}
