using System.Runtime.InteropServices;
using System.Security.Cryptography;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Modules;

/// <summary>Separate accepted-Back request and verified terminal Update edge; never infers cancellation from disappearance.</summary>
internal sealed class ExactBackCancellationModule : IAdapterModule
{
    public const string Capability = GameLifecycleCapabilityIds.BackCancellation;
    private const string Fingerprint = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79";
    private readonly NativeRuntime _api;
    private readonly IMainThreadDispatcher _main;
    private readonly ILogger _log;
    private readonly AdapterBuildProfile _profile;
    private readonly GameEventState _state;
    private readonly string _dobby;
    private readonly BackCancellationTracker _requests;
    private readonly VerifiedRunCancellationQueue _accepted;
    private ExactHookBroker.Binding? _back, _update;
    private IGameLifecycleSubscription? _lifecycle;
    private IntPtr _controllerClass;
    private bool Installed => _back is not null && _update is not null;

    internal ExactBackCancellationModule(NativeRuntime api, IMainThreadDispatcher main, ILogger log,
        AdapterBuildProfile profile, GameEventState state, string dobby)
    {
        (_api, _main, _log, _profile, _state, _dobby) = (api, main, log, profile, state, dobby);
        _requests = new(state.CaptureCancellationToken, ex => log.Log(LogLevel.Warning, "Back cancellation pending-root cleanup failed.", ex));
        _accepted = new(state);
    }
    public double IntervalSeconds => 1;
    public AdapterModuleManifest Manifest { get; } = new("rizline.back-cancellation", new(1, 0),
        new[] { Capability }, new[] { GameLifecycleCapabilityIds.Lifecycle }, Array.Empty<string>(), new[] {
            Method("back.entry", "PauseUIControl", "Back"), Method("back.update", "PauseUIControl", "Update"),
            Field("back.controller", "PauseUIControl", "normalModeInLevelController", "NormalModeInLevelController"),
            Field("back.info", "NormalModeInLevelController", "LevelInformation", "InLevel.LevelInformation"),
            Field("back.pause", "NormalModeInLevelController", "pauseUIControl", "PauseUIControl"),
            Field("back.pausing", "PauseUIControl", "_pausing", "System.Boolean"),
            Field("back.over", "PauseUIControl", "_over", "System.Boolean"),
            Field("back.terminated", "PauseUIControl", "_terminated", "System.Boolean"),
            Field("back.skipped", "NormalModeInLevelController", "_skip", "System.Boolean"),
            Field("back.controller-crashed", "NormalModeInLevelController", "_crashed", "System.Boolean"),
            Field("back.info-over", "LevelInformation", "levelOver", "System.Boolean", "InLevel"),
            Field("back.info-crashed", "LevelInformation", "crashed", "System.Boolean", "InLevel"),
            new AdapterSymbolRequirement("back.active", "UnityEngine.CoreModule.dll", "UnityEngine", "Behaviour", AdapterSymbolKind.Method,
                "get_isActiveAndEnabled", false, Array.Empty<string>()) { ExpectedType = "System.Boolean" } },
        "Pinned Back accepts a request only; the matching original Update must return across _terminated and controller._skip false-to-true. Native ended/crashed/already-skipped states refuse capture independently of sampled state. One rooted pending request per observed run, fresh terminal revision, deferred Aborted delivery. Late-result origin is not established by this capability; runtime acceptance remains separate.");
    private static AdapterSymbolRequirement Method(string id, string type, string name)
        => new(id, "Assembly-CSharp.dll", "", type, AdapterSymbolKind.Method, name, false, Array.Empty<string>()) { ExpectedType = "System.Void" };
    private static AdapterSymbolRequirement Field(string id, string type, string name, string result, string ns = "")
        => new(id, "Assembly-CSharp.dll", ns, type, AdapterSymbolKind.Field, name, false) { ExpectedType = result };

    public ModuleProbe Probe()
    {
        _main.VerifyAccess();
        if (_back is not null && _update is not null) return ModuleProbe.Available;
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || _profile.GameAssemblySha256 != Fingerprint || _profile.UnityVersion != "2022.3.62f2")
            return ModuleProbe.Unsupported("UnsupportedBuild: Back cancellation is pinned to the verified Windows x64 build.");
        var file = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "GameAssembly.dll");
        using var stream = File.OpenRead(file); using var hash = SHA256.Create();
        if (Convert.ToHexString(hash.ComputeHash(stream)).ToLowerInvariant() != Fingerprint)
            return ModuleProbe.Unsupported("UnsupportedBuild: Back cancellation fingerprint differs.");
        var pause = _api.Class("Assembly-CSharp.dll", "", "PauseUIControl");
        _controllerClass = _api.Class("Assembly-CSharp.dll", "", "NormalModeInLevelController");
        var module = GetModuleHandle("GameAssembly.dll");
        var back = _api.ResolveExactHook("framework.lifecycle.back-request", module, pause, "Back", Array.Empty<string>(), 0x3C3A50, ExactHookAbi.InstanceVoid);
        var update = _api.ResolveExactHook("framework.lifecycle.back-terminal", module, pause, "Update", Array.Empty<string>(), 0x3C3E20, ExactHookAbi.InstanceVoid);
        var broker = new ExactHookBroker(_dobby, _main, _log);
        try
        {
            _back = broker.BindRetainedObservation(back, receiver => _api.ExactHookReceiverMatches(receiver, back.DeclaringClass),
                receiver => Capture(receiver, false), AfterBack);
            _update = broker.BindRetainedObservation(update,
                receiver => _requests.MatchesPause(receiver.ToInt64()) && _api.ExactHookReceiverMatches(receiver, update.DeclaringClass),
                receiver => Capture(receiver, true), AfterUpdate);
            var subscribed = _state.Subscribe(_ => _requests.Prune());
            if (!subscribed.Success || subscribed.Value is null) throw new InvalidOperationException(subscribed.Error?.Message ?? "Lifecycle subscription unavailable.");
            _lifecycle = subscribed.Value;
            _log.Log(LogLevel.Info, "Back cancellation observers installed at 3C3A50/3C3E20; awaiting accepted request and terminal edge.");
            return ModuleProbe.Available;
        }
        catch { Release(); throw; }
    }
    private NativeObservation Capture(IntPtr receiver, bool update)
    {
        var roots = new RootSet();
        try
        {
            if (!Installed) return new(_api, roots);
            var pause = roots.Keep(_api, receiver);
            if (!_api.Unbox<bool>(_api.Call(pause.Target, "get_isActiveAndEnabled"))) return new(_api, roots);
            var pointer = _api.Read<IntPtr>(pause.Target, "normalModeInLevelController");
            if (pointer == IntPtr.Zero || !_api.ExactHookReceiverMatches(pointer, _controllerClass)) return new(_api, roots);
            var controller = roots.Keep(_api, pointer);
            if (!_api.Unbox<bool>(_api.Call(controller.Target, "get_isActiveAndEnabled"))
                || _api.Read<IntPtr>(controller.Target, "pauseUIControl") != pause.Target) return new(_api, roots);
            pointer = _api.Read<IntPtr>(controller.Target, "LevelInformation");
            if (pointer == IntPtr.Zero) return new(_api, roots);
            var information = roots.Keep(_api, pointer);
            var token = _state.CaptureCancellationToken(information.Target.ToInt64());
            // The sampler may be up to 250 ms behind a real end. Do not relabel an
            // already-failed/finished/skipped run merely because its old token is open.
            if (token is null || _api.Read<bool>(pause.Target, "_terminated")
                || _api.Read<bool>(information.Target, "levelOver") || _api.Read<bool>(information.Target, "crashed")
                || _api.Read<bool>(controller.Target, "_crashed") || _api.Read<bool>(controller.Target, "_skip")) return new(_api, roots);
            if (update)
            {
                if (!_api.Read<bool>(pause.Target, "_over")) return new(_api, roots);
                var stamp = _requests.BeginUpdate(controller.Target.ToInt64(), pause.Target.ToInt64(), information.Target.ToInt64(), false);
                return stamp is null ? new(_api, roots) : new(_api, roots, token, controller, pause, information, stamp);
            }
            return !_api.Read<bool>(pause.Target, "_pausing") || _api.Read<bool>(pause.Target, "_over")
                ? new(_api, roots) : new(_api, roots, token, controller, pause, information);
        }
        catch { roots.DisposeSafely(_log); throw; }
    }
    private void AfterBack(NativeObservation observation)
    {
        if (!Installed || observation.Token is not { } token || !observation.RelationshipsMatch()
            || !observation.PauseOver || observation.AlreadyEnding) return;
        var identity = new BackCancellationIdentity(token, observation.Controller, observation.Pause);
        var retained = observation.RetainPending(_log);
        if (_requests.Accept(identity, retained))
            _log.Log(LogLevel.Info, $"Accepted Back request retained for observed run {token.RunId}; awaiting its terminal native Update edge.");
    }
    private void AfterUpdate(NativeObservation observation)
    {
        if (!Installed || observation.Update is not { } update || !observation.RelationshipsMatch()
            || !observation.PauseOver || observation.Crashed) return;
        var token = _requests.CompleteUpdate(update, observation.Terminated, observation.Skipped);
        if (token is not null && _accepted.Enqueue(token,
            "Accepted PauseUIControl.Back request followed by its matching original Update returning across _terminated and controller._skip false to true."))
            _log.Log(LogLevel.Info, $"Accepted Back terminal cancellation queued for observed run {token.RunId}; sceneGeneration={token.SceneGeneration}; revision={token.Revision}.");
    }
    private sealed class NativeObservation : IDisposable
    {
        private readonly NativeRuntime _api;
        private readonly RootSet _roots;
        private readonly NativeRuntime.RootHandle? _controller, _pause, _information;
        internal ObservedRunCancellationToken? Token { get; }
        internal BackCancellationUpdate? Update { get; }
        internal long Controller => _controller?.Target.ToInt64() ?? 0;
        internal long Pause => _pause?.Target.ToInt64() ?? 0;
        internal NativeObservation(NativeRuntime api, RootSet roots, ObservedRunCancellationToken? token = null,
            NativeRuntime.RootHandle? controller = null, NativeRuntime.RootHandle? pause = null,
            NativeRuntime.RootHandle? information = null, BackCancellationUpdate? update = null)
            => (_api, _roots, Token, _controller, _pause, _information, Update) = (api, roots, token, controller, pause, information, update);
        internal bool RelationshipsMatch() => _controller is not null && _pause is not null && _information is not null
            && _api.Read<IntPtr>(_controller.Target, "LevelInformation") == _information.Target
            && _api.Read<IntPtr>(_controller.Target, "pauseUIControl") == _pause.Target
            && _api.Read<IntPtr>(_pause.Target, "normalModeInLevelController") == _controller.Target;
        internal bool PauseOver => _pause is not null && _api.Read<bool>(_pause.Target, "_over");
        internal bool Terminated => _pause is not null && _api.Read<bool>(_pause.Target, "_terminated");
        internal bool Skipped => _controller is not null && _api.Read<bool>(_controller.Target, "_skip");
        internal bool Crashed => _controller is not null && _information is not null
            && (_api.Read<bool>(_controller.Target, "_crashed") || _api.Read<bool>(_information.Target, "crashed"));
        internal bool AlreadyEnding => Crashed || Skipped || _information is not null && _api.Read<bool>(_information.Target, "levelOver");
        internal RootSet RetainPending(ILogger log)
        {
            var result = new RootSet();
            try { result.Keep(_api, _controller!.Target); result.Keep(_api, _pause!.Target); result.Keep(_api, _information!.Target); return result; }
            catch { result.DisposeSafely(log); throw; }
        }
        public void Dispose() => _roots.Dispose();
    }
    private sealed class RootSet : IDisposable
    {
        private readonly List<NativeRuntime.RootHandle> _roots = new();
        internal NativeRuntime.RootHandle Keep(NativeRuntime api, IntPtr pointer)
        {
            var root = api.Root(pointer);
            try { _roots.Add(root); return root; }
            catch { try { root.Dispose(); } catch { } throw; }
        }
        internal void DisposeSafely(ILogger log) { try { Dispose(); } catch (Exception ex) { try { log.Log(LogLevel.Warning, "Back cancellation capture-root cleanup failed.", ex); } catch { } } }
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
    public ModuleProbe Tick() { _main.VerifyAccess(); _requests.Prune(); return Installed ? ModuleProbe.Available : ModuleProbe.Waiting("Back observers are detached."); }
    internal void FlushAccepted() { _main.VerifyAccess(); if (Installed) _accepted.Drain(); }
    public void Release()
    {
        _main.VerifyAccess(); var back = _back; var update = _update; var lifecycle = _lifecycle;
        _back = null; _update = null; _lifecycle = null;
        // Detach logical callbacks before releasing pending roots: cleanup/logging
        // reentry must not create another request after Clear has already run.
        try { back?.Dispose(); }
        finally
        {
            try { update?.Dispose(); }
            finally { try { lifecycle?.Dispose(); } finally { _accepted.Clear(); _requests.Clear(); } }
        }
    }
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
}
