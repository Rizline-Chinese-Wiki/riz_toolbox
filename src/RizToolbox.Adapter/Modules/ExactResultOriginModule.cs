using System.Runtime.InteropServices;
using System.Security.Cryptography;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core.Diagnostics;

namespace RizToolbox.Adapter.Modules;

/// <summary>Records provenance of actual original ControllerOver returns; never constructs or replaces a result.</summary>
internal sealed class ExactResultOriginModule : IAdapterModule
{
    private const string Fingerprint = "853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79";
    private readonly NativeRuntime _api;
    private readonly IMainThreadDispatcher _main;
    private readonly ILogger _log;
    private readonly AdapterBuildProfile _profile;
    private readonly GameEventState _state;
    private readonly string _dobby;
    private ExactHookBroker.Binding? _binding;
    private VerifiedHookTarget? _target;
    private LocalResourceReferenceOwnerProbe? _resourceReferenceOwnerProbe;
    internal ExactResultOriginModule(NativeRuntime api, IMainThreadDispatcher main, ILogger log,
        AdapterBuildProfile profile, GameEventState state, string dobby)
        => (_api, _main, _log, _profile, _state, _dobby) = (api, main, log, profile, state, dobby);

    public double IntervalSeconds => 1;
    public AdapterModuleManifest Manifest { get; } = new("rizline.result-origin", new(1, 0),
        new[] { GameLifecycleCapabilityIds.ResultOrigin }, new[] { GameLifecycleCapabilityIds.Lifecycle },
        Array.Empty<string>(), new[] {
            new AdapterSymbolRequirement("result-origin.entry", "Assembly-CSharp.dll", "", "NormalModeInLevelController",
                AdapterSymbolKind.Method, "ControllerOver", false, Array.Empty<string>()) { ExpectedType = "InsideLevelResult" },
            Field("result-origin.info", "NormalModeInLevelController", "LevelInformation", "InLevel.LevelInformation"),
            Field("result-origin.params", "NormalModeInLevelController", "LevelParams", "InsideLevelParams"),
            Field("result-origin.params-metadata", "InsideLevelParams", "metadata", "ILevelMetadata"),
            Field("result-origin.result-metadata", "InsideLevelResult", "metadata", "ILevelMetadata") },
        "Pinned ControllerOver original-return identity with retained entry run/controller/information. Result association requires that exact rooted result and current observed run; unknown, stale and withdrawn origins remain unassociated.");
    private static AdapterSymbolRequirement Field(string id, string type, string name, string expected)
        => new(id, "Assembly-CSharp.dll", "", type, AdapterSymbolKind.Field, name, false) { ExpectedType = expected };

    public ModuleProbe Probe()
    {
        _main.VerifyAccess(); if (_binding is not null) return ModuleProbe.Available;
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || _profile.GameAssemblySha256 != Fingerprint || _profile.UnityVersion != "2022.3.62f2")
            return ModuleProbe.Unsupported("UnsupportedBuild: result origin requires the verified Windows x64 build.");
        var imagePath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "GameAssembly.dll");
        using var stream = File.OpenRead(imagePath); using var hash = SHA256.Create();
        if (Convert.ToHexString(hash.ComputeHash(stream)).ToLowerInvariant() != Fingerprint)
            return ModuleProbe.Unsupported("UnsupportedBuild: result-origin game image fingerprint differs.");
        var cls = _api.Class("Assembly-CSharp.dll", "", "NormalModeInLevelController");
        _api.RequireInstanceFieldLayout(cls, "LevelInformation", "InLevel.LevelInformation", 0x28);
        _api.RequireInstanceFieldLayout(cls, "LevelParams", "InsideLevelParams", 0x20);
        var target = _api.ResolveExactHook("framework.lifecycle.result-origin", GetModuleHandle("GameAssembly.dll"),
            cls, "ControllerOver", Array.Empty<string>(), 0x3BEA60, ExactHookAbi.InstanceReferenceReturn0);
        if (target.ReturnClass != _api.Class("Assembly-CSharp.dll", "", "InsideLevelResult"))
            return ModuleProbe.Unsupported("Result-origin return class does not match InsideLevelResult.");
        var referenceMarker = Path.Combine(Path.GetDirectoryName(imagePath)!, "BepInEx", "RizToolbox", "probes", "local-resource-reference.enabled");
        _resourceReferenceOwnerProbe ??= new(_main, _log, File.Exists(referenceMarker),
            owner => new LocalResourceReferenceDiagnostics(_api, _profile).CaptureExistingOwner(
                GetModuleHandle("GameAssembly.dll"), owner, "InsideLevelParams"));
        try
        {
            _target = target;
            _binding = new ExactHookBroker(_dobby, _main, _log).BindRetainedReferenceObservation(target,
                receiver => _api.ExactHookReceiverMatches(receiver, target.DeclaringClass), Capture,
                result => _api.RetainExactHookResult(target, result), After);
            _state.ResultOrigins.Activate(); _state.ResultOriginAvailabilityChanged();
            _log.Log(LogLevel.Info, $"Result-origin observer installed at 3BEA60; generation={_state.ResultOrigins.Generation}; awaiting original ControllerOver returns.");
            return ModuleProbe.Available;
        }
        catch { Release(); throw; }
    }

    private Observation Capture(IntPtr receiver)
    {
        var observation = new Observation(_api);
        try
        {
            if (_binding is null || !_state.ResultOrigins.Active) return observation;
            observation.Controller = observation.Keep(receiver);
            var information = _api.Read<IntPtr>(observation.Controller.Target, "LevelInformation");
            var parameters = _api.Read<IntPtr>(observation.Controller.Target, "LevelParams");
            if (information == IntPtr.Zero || parameters == IntPtr.Zero) return observation;
            observation.Information = observation.Keep(information);
            observation.Parameters = observation.Keep(parameters);
            // This owner already belongs to the verified original receiver graph. The
            // optional once-only diagnostic borrows it synchronously and contains faults.
            _resourceReferenceOwnerProbe?.TryCapture(observation.Parameters);
            observation.Origin = _state.CaptureResultOrigin(observation.Information.Target.ToInt64(), observation.Controller.Target.ToInt64());
            return observation;
        }
        catch { observation.Dispose(); throw; }
    }

    private void After(Observation observation, IntPtr result)
    {
        if (_binding is null || _target is null || observation.Origin is not { } origin ||
            observation.Controller is null || observation.Information is null || observation.Parameters is null) return;
        if (!_state.ResultOrigins.Active || origin.SourceGeneration != _state.ResultOrigins.Generation) return;
        if (_api.Read<IntPtr>(observation.Controller.Target, "LevelInformation") != observation.Information.Target ||
            _api.Read<IntPtr>(observation.Controller.Target, "LevelParams") != observation.Parameters.Target ||
            _api.Read<IntPtr>(result, "metadata") != _api.Read<IntPtr>(observation.Parameters.Target, "metadata"))
        {
            _log.Log(LogLevel.Warning, "Result-origin capture refused: original receiver graph or result metadata differs from entry.");
            return;
        }
        var retention = new Retention(_api);
        try
        {
            retention.ResultRoot = retention.KeepValidatedResult(_target, result);
            retention.InformationRoot = retention.Keep(observation.Information.Target);
            retention.ControllerRoot = retention.Keep(observation.Controller.Target);
            retention.TerminalSnapshot = CopyTerminalSnapshot(retention.ResultRoot, retention.InformationRoot,
                retention.ControllerRoot, observation.Parameters);
        }
        catch { retention.Dispose(); throw; }
        var accepted = _state.ResultOrigins.Register(origin, retention, out var reason); // registry owns every path
        _log.Log(accepted ? LogLevel.Info : LogLevel.Warning,
            $"Result-origin return: accepted={accepted}; run={origin.RunId}; capturedRevision={origin.Revision}; generation={origin.SourceGeneration}; pending={_state.ResultOrigins.PendingCount}; {reason}");
    }

    internal RunFinished? CopyTerminalSnapshot(NativeRuntime.RootHandle resultRoot, NativeRuntime.RootHandle informationRoot,
        NativeRuntime.RootHandle controllerRoot, NativeRuntime.RootHandle parameters)
    {
        try
        {
            var result = resultRoot.Target;
            var information = informationRoot.Target;
            var controller = controllerRoot.Target;
            var parameter = parameters.Target;
            var metadata = _api.Read<IntPtr>(parameter, "metadata");
            // ControllerOver constructs the actual result before returning. Verify
            // that the completed graph and result stay current across the copy.
            if (metadata == IntPtr.Zero || !TerminalGraphMatches()) return null;
            var copy = GameEventObserver.CopyActualResult(_api, resultRoot);
            if (!double.IsFinite(copy.RawCompletion) || copy.Perfect < 0 || copy.MaxPerfect < 0 || copy.Bad < 0 || copy.Miss < 0
                || !TerminalGraphMatches() || GameEventObserver.CopyActualResult(_api, resultRoot) != copy
                || !TerminalGraphMatches()) return null;
            return copy;

            bool TerminalGraphMatches() => resultRoot.Target == result && informationRoot.Target == information
                && controllerRoot.Target == controller && parameters.Target == parameter
                && _api.Read<bool>(information, "levelOver")
                && _api.Read<IntPtr>(controller, "LevelInformation") == information
                && _api.Read<IntPtr>(controller, "LevelParams") == parameter
                && _api.Read<IntPtr>(parameter, "metadata") == metadata
                && _api.Read<IntPtr>(result, "metadata") == metadata;
        }
        catch (Exception error)
        {
            _log.TryLog(LogLevel.Warning, "Could not copy the actual terminal result from its original return.", error);
            return null;
        }
    }

    private class Roots : IDisposable
    {
        protected readonly NativeRuntime Api;
        private readonly List<NativeRuntime.RootHandle> _roots = new();
        internal Roots(NativeRuntime api) => Api = api;
        internal NativeRuntime.RootHandle Keep(IntPtr pointer) => Adopt(Api.Root(pointer));
        protected NativeRuntime.RootHandle Adopt(NativeRuntime.RootHandle root)
        { try { _roots.Add(root); return root; } catch { root.Dispose(); throw; } }
        public void Dispose()
        {
            Exception? failure = null;
            while (_roots.Count > 0)
            {
                var index = _roots.Count - 1; var root = _roots[index]; _roots.RemoveAt(index);
                try { root.Dispose(); } catch (Exception error) { failure ??= error; }
            }
            if (failure is not null) throw failure;
        }
    }
    private sealed class Observation : Roots
    {
        internal NativeRuntime.RootHandle? Controller, Information, Parameters;
        internal ObservedResultOrigin? Origin;
        internal Observation(NativeRuntime api) : base(api) { }
    }
    private sealed class Retention : Roots, IResultOriginRetention
    {
        internal NativeRuntime.RootHandle? ResultRoot, InformationRoot, ControllerRoot;
        public RunFinished? TerminalSnapshot { get; set; }
        internal Retention(NativeRuntime api) : base(api) { }
        internal NativeRuntime.RootHandle KeepValidatedResult(VerifiedHookTarget target, IntPtr result)
            => Adopt(Api.RetainExactHookResult(target, result));
        public long Result => ResultRoot?.Target.ToInt64() ?? 0;
        public long Information => InformationRoot?.Target.ToInt64() ?? 0;
        public long Controller => ControllerRoot?.Target.ToInt64() ?? 0;
    }

    public void Register(ModuleRegistration registration)
        => registration.RegisterScoped<IGameLifecycleService>(GameLifecycleCapabilityIds.ResultOrigin, _state.ForPlugin);
    public ModuleProbe Tick()
    {
        _main.VerifyAccess();
        return _binding is not null && _state.ResultOrigins.Active ? ModuleProbe.Available
            : ModuleProbe.Waiting("Original-result observer is detached.");
    }
    public void Release()
    {
        _main.VerifyAccess(); var binding = _binding; _binding = null; _target = null;
        try { binding?.Dispose(); }
        finally
        {
            _state.ResultOrigins.Withdraw("Original-result observer was withdrawn; new results remain unassociated.");
            _state.ResultOriginAvailabilityChanged();
            if (_state.ResultOrigins.ReleaseFailures != 0)
                _log.Log(LogLevel.Error, $"Result-origin root cleanup failures={_state.ResultOrigins.ReleaseFailures}.");
        }
    }
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
}
