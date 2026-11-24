using System.Diagnostics;
using RizToolbox.Core;
using RizToolbox.Core.Diagnostics;
using RizToolbox.Core.Hooks;
using RizToolbox.Runtime;
using RizToolbox.Sdk;
using RizToolbox.Adapter.Modules;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.Settings;
using RizToolbox.Adapter.Visual;
using RizToolbox.Adapter.UiInput;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Adapter.Judgements;

namespace RizToolbox.Adapter;

public interface IGameAdapter : IDisposable, IAdapterModuleStatusProvider
{
    GameBuildDescriptor Build { get; }
    IReadOnlyList<string> SupportedCapabilities { get; }
    /// <summary>Schedules capability demand for the next tick. Null selects explicit full diagnostics.</summary>
    void SetCapabilityDemand(IReadOnlyCollection<string>? capabilityIds) { }
    void FlushBoundaryObservations();
    void PumpCommands();
    void Tick(long hostTickSequence);
}

internal static class AdapterFactory
{
    internal static IGameAdapter Create(NativeRuntime api, AdapterBuildProfile profile, MainThreadDispatcher main,
        CapabilityRegistry capabilities, EventHub events, ExtensionPointRegistry extensions, ILogger log,
        Action<string, Exception>? onPluginFault = null,
        Func<string, string?>? packageDirectoryByPluginId = null, Func<string, string?>? dataDirectoryByPluginId = null,
        bool developerDiagnostics = false)
        => new RizlineAdapter(api, profile, main, capabilities, events, extensions, log, onPluginFault,
            packageDirectoryByPluginId, dataDirectoryByPluginId, developerDiagnostics);
}

internal sealed class RizlineAdapter : IGameAdapter
{
    private readonly ModuleManager _modules;
    private readonly CapabilityRegistry _capabilities;
    private readonly ILogger _log;
    private readonly GameEventState _state = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly HookDispatcher _hooks;
    private readonly RunModificationJournal _modifications;
    private readonly ExactRunCancellationModule _runCancellation;
    private readonly ExactBackCancellationModule _backCancellation;
    private readonly ExactSkipTerminationModule _skipTermination;
    private readonly GameplayModule _gameplay;
    private readonly NativePlaybackStockRateHost _playback;
    private readonly AudioContentModules _audioContent;
    private bool _disposed;
    public GameBuildDescriptor Build { get; }
    public IReadOnlyList<string> SupportedCapabilities => Array.AsReadOnly(CapabilityList.Concat(
        _modules.GetModules().SelectMany(x => x.Manifest.Capabilities)).Distinct(StringComparer.Ordinal).ToArray());
    internal static readonly string[] CapabilityList = {
        CapabilityIds.Catalog, CapabilityIds.Scores, CapabilityIds.Ui, CapabilityIds.Resources, CapabilityIds.Practice,
        GameCapabilityIds.Resources, GameCapabilityIds.Scenes, GameCapabilityIds.Runs, CapabilityIds.Extensions, CapabilityIds.Metadata, DataCapabilityIds.Read,
        TargetCapabilityIds.Targets,TargetCapabilityIds.Mutations
    };

    internal RizlineAdapter(NativeRuntime api, AdapterBuildProfile profile, MainThreadDispatcher main,
        CapabilityRegistry capabilities, EventHub events, ExtensionPointRegistry extensions, ILogger log,
        Action<string, Exception>? onPluginFault = null,
        Func<string, string?>? packageDirectoryByPluginId = null, Func<string, string?>? dataDirectoryByPluginId = null,
        bool developerDiagnostics = false)
    {
        _capabilities = capabilities;
        _log = log;
        Build = capabilities.GameBuild;
        AdapterCapabilityMetadata.Declare(capabilities);
        _modifications = new(main, log, _state);
        _hooks = new HookDispatcher(main, log) { Performance = events.Performance };
        if (onPluginFault is not null) _hooks.ProviderFaulted += onPluginFault;
        capabilities.DeclareMetadata(HookCapabilityIds.Observe, new(1, 0), CapabilityStability.Experimental, new[] {
            new CapabilityOperationInfo("observe-exact",OperationThread.MainThread,ObservationMode.DirectCallback,CleanupResponsibility.EnableScope,false,Array.Empty<string>(),
                "Build-verified exact GetJudge/Pause/Resume machine entries, read-only before/after delivery. Unsupported mutation policies are rejected.") });
        capabilities.DeclareMetadata(GameLifecycleCapabilityIds.Lifecycle,new(1,0),CapabilityStability.Experimental,new[] {
            new CapabilityOperationInfo("snapshot-subscribe",OperationThread.MainThread,ObservationMode.Sampled,CleanupResponsibility.EnableScope,false,new[]{"*"},
                "Shared session/revision/scene generation; atomic initial snapshot and subsequent observed state. Pause requires the live pause flag; unknown result association stays null.",.25) });
        capabilities.DeclareMetadata(GameLifecycleCapabilityIds.RetryCancellation,new(1,0),CapabilityStability.Experimental,new[] {
            new CapabilityOperationInfo("observe-accepted-retry",OperationThread.MainThread,ObservationMode.DirectCallback,CleanupResponsibility.EnableScope,false,new[]{"*"},
                "Exact Retry after-return observation with retained original info activeSelf edge and observed-run token. Other cancellation paths and late results remain separately limited.") });
        capabilities.DeclareMetadata(GameLifecycleCapabilityIds.BackCancellation,new(1,0),CapabilityStability.Experimental,new[] {
            new CapabilityOperationInfo("observe-accepted-back",OperationThread.MainThread,ObservationMode.DirectCallback,CleanupResponsibility.EnableScope,false,new[]{"*"},
                "Exact Back request and matching original Update terminal edge with retained controller/pause identity. The request alone never ends a run; late-result origin remains separately limited.") });
        capabilities.DeclareMetadata(GameLifecycleCapabilityIds.SkipTermination,new(1,0),CapabilityStability.Experimental,new[] {
            new CapabilityOperationInfo("observe-accepted-skip",OperationThread.MainThread,ObservationMode.DirectCallback,CleanupResponsibility.EnableScope,false,new[]{"*"},
                "Exact original Skip returning across the controller skip flag and owned PauseUI disable edge. Ends the current observed run as Skipped without inventing an actual result.") });
        capabilities.DeclareMetadata(GameLifecycleCapabilityIds.ResultOrigin,new(1,0),CapabilityStability.Experimental,new[] {
            new CapabilityOperationInfo("observe-original-result-origin",OperationThread.MainThread,ObservationMode.DirectCallback,CleanupResponsibility.EnableScope,false,new[]{"*"},
                "Original ControllerOver result objects retain their entry run origin. Sampled results close only the matching observed run; missing or withdrawn provenance remains unassociated.") });
        capabilities.DeclareMetadata(TargetCapabilityIds.Targets,new(1,0),CapabilityStability.Experimental,new[] {
            new CapabilityOperationInfo("resolve",OperationThread.MainThread,ObservationMode.Snapshot,CleanupResponsibility.EnableScope,false,Array.Empty<string>(),
                "Shared typed registry for independent UI and current-run game appearance providers: canonical generations, aliases and missing/ambiguous matches. The registry stays available when empty; each bounded provider owns discovery, refresh and cleanup of its own targets.") });
        capabilities.DeclareMetadata(TargetCapabilityIds.Mutations,new(1,0),CapabilityStability.Experimental,new[] {
            new CapabilityOperationInfo("override-color",OperationThread.MainThread,ObservationMode.NotApplicable,CleanupResponsibility.EnableScope,false,Array.Empty<string>(),
                "Registered ResourceColor/color priority channels for UI and concrete game appearance properties; adopts external baselines and verifies read-back. Provider withdrawal releases only its own channels. No transforms/exclusive strategy is declared for color.") });
        capabilities.DeclareMetadata(DataCapabilityIds.Read,new(1,0),CapabilityStability.Experimental,new[] {
            new CapabilityOperationInfo("refresh",OperationThread.MainThread,ObservationMode.Snapshot,CleanupResponsibility.None,false,new[]{"*"},
                "Single synchronous chart/score traversal; immutable bounded revisions. Module refreshes every 2 s; callers may request a fresh capture."),
            new CapabilityOperationInfo("read-pages",OperationThread.MainThread,ObservationMode.Snapshot,CleanupResponsibility.None,false,new[]{"*"},
                "Session/revision-bound chart, score, actual-result and verified-setting snapshots; optional sources follow independently probed modules and may be unavailable.") });
        var binding = profile.Types[profile.TickType];
        var manager = api.Class(binding.Assembly, binding.Namespace, binding.Name);
        api.Field(manager, "Instance"); api.Method(manager, profile.TickMethod, 0);
        capabilities.SetUnavailable(CapabilityIds.Practice, "No verified practice adapter in this profile.");
        _modules = new(capabilities, new NativeModuleSymbolResolver(api), log, profile.Id, profile.GameAssemblySha256);
        RegisterModule(new ObservationModule(api, profile, main, events, extensions, log, _state, true));
        var runs = new ObservationModule(api, profile, main, events, extensions, log, _state, false);
        RegisterModule(runs);
        RegisterModule(new ExactResultOriginModule(api, main, log, profile, _state,
            Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "BepInEx", "core", "dobby.dll")));
        RegisterModule(new RunModificationModule(_modifications));
        var settings = new SettingsModule(api, main, log);
        RegisterModule(settings);
        RegisterModule(new SettingsOverrideModule(settings));
        _playback = new NativePlaybackStockRateHost(api, main, log, _state, profile,
            Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "BepInEx", "core", "dobby.dll"));
        _gameplay = new GameplayModule(api, main, _state.LifecycleSnapshot, _modifications.Record,
            _state.MatchesCurrentGameplayRun, _playback, pumpAfterObservation: true,
            controlBoundary: _state.GameplayObservationBoundary,
            observedController: runs.AcquireObservedGameplayController, performance: events.Performance, log: log,
            playbackRequested: () => _modules.HasPlannedCapability(GameplayCapabilityIds.Controls));
        RegisterModule(_gameplay);
        RegisterModule(new DataModule(api, main, log, manager, runs.ReadCurrentResult, settings.CaptureDataSettings));
        RegisterModule(new UiModule(api, main, log));
        var semanticTargets = new SemanticTargetsModule(main, log);
        RegisterModule(semanticTargets);
        RegisterModule(new MenuResourceModule(api, main, log, manager,semanticTargets));
        RegisterModule(new GameAppearanceModule(api, main, log, semanticTargets,profile.GameAssemblySha256,
            () => _state.LifecycleSnapshot(),_state.MatchesCurrentGameplayRun));
        RegisterModule(new GameCameraModule(api, main, log, semanticTargets, profile.GameAssemblySha256,
            () => _state.LifecycleSnapshot(), _state.MatchesCurrentGameplayRun));
        capabilities.DeclareMetadata(GamePostProcessingCapabilityIds.PostProcessing, new(1, 0), CapabilityStability.Experimental, new[] {
            new CapabilityOperationInfo("read-target", OperationThread.MainThread, ObservationMode.Sampled, CleanupResponsibility.EnableScope, false,
                Array.Empty<string>(), "Current menu main-camera blur target and its integer range."),
            new CapabilityOperationInfo("override-downsample", OperationThread.MainThread, ObservationMode.NotApplicable, CleanupResponsibility.EnableScope, false,
                Array.Empty<string>(), "Scoped integer downsampling through the shared mutation coordinator. External changes end the leases and remain in place."),
            new CapabilityOperationInfo("bind-downsample", OperationThread.MainThread, ObservationMode.Sampled, CleanupResponsibility.EnableScope, false,
                Array.Empty<string>(), "Scope-owned blur override that follows replacement targets in the explicitly selected scene.") });
        RegisterModule(new GamePostProcessingModule(api, main, log, semanticTargets, profile.GameAssemblySha256));
        var visualBudget = VisualResourceBudget.ProcessShared;
        var visualRetirement = new VisualResourceRetirement(main, log);
        foreach (var kind in new[] { VisualTargetKind.Sprite, VisualTargetKind.Font, VisualTargetKind.Material })
        {
            var capability = VisualModule.Capability(kind);
            capabilities.DeclareMetadata(capability, new(1, 0), CapabilityStability.Experimental, new[] {
                new CapabilityOperationInfo("enumerate", OperationThread.MainThread, ObservationMode.Sampled, CleanupResponsibility.EnableScope, false,
                    Array.Empty<string>(), "Bounded live game UI targets; independent provider generation and invalidation."),
                new CapabilityOperationInfo("create-override", OperationThread.MainThread, ObservationMode.NotApplicable, CleanupResponsibility.EnableScope, false,
                    Array.Empty<string>(), "Scope-owned assets and priority overrides; approved files and shared bounded budget. Material Color/Float parameters require a pinned asset profile and exact reference shader identity; unknown schemas are refused.") });
            RegisterModule(new VisualModule(api, main, log, kind, packageDirectoryByPluginId ?? (_ => null),
                dataDirectoryByPluginId ?? (_ => null), visualBudget, visualRetirement));
        }
        foreach (var operation in new[] {
            (UiInputCapabilityIds.Windows, "create", "Scope-owned overlay/menu UGUI windows and controls. Every observed scene boundary retires owned windows."),
            (UiInputCapabilityIds.Actions, "register-observe", "Registered keyboard/mouse and declared gamepad button edges captured after verified original input updates; bounded copies dispatch on host FrameUpdate with capture and current focus/context checks. Device presence and mappings are probed independently. Overlapping framework bindings conflict. Game input suppression and analog axes are unsupported; gamepad hardware acceptance remains separate."),
            (UiInputCapabilityIds.Navigation, "activate-selected", "Guarded native MainMenuControl.EnterCurrentButton only; no arbitrary scene-name navigation or asynchronous destination guarantee.") })
            capabilities.DeclareMetadata(operation.Item1, new(0, 1), CapabilityStability.Experimental, new[] {
                new CapabilityOperationInfo(operation.Item2, OperationThread.MainThread, ObservationMode.Sampled,
                    CleanupResponsibility.EnableScope, false, new[] { "*" }, operation.Item3) });
        var uiInput = new UiInputModule(api, main, log, events, developerDiagnostics);
        RegisterModule(uiInput);
        foreach (var operation in new[] {
            (AudioContentCapabilityIds.Playback, "load-play", "Bounded synchronous cancellable PCM16 WAV upload and owned AudioSource playback; no streaming or latency promise."),
            (AudioContentCapabilityIds.Replacement, "replace", "Priority clip replacement on eligible live game AudioSources; no restart, sample alignment or unchanged-results promise."),
            (AudioContentCapabilityIds.Mix, "override", "Scoped per-source volume/pitch priority overrides; no mixer bus or global mixer control."),
            (AudioContentCapabilityIds.Clock, "read", "Independent Unity DSP and host-monotonic seconds; no verified chart-time mapping."),
            (AudioContentCapabilityIds.LocalContent, "register-list", "Atomic host-side validated content registration. Registration alone does not make content game-resolvable; formats and actual game binding are separate operations."),
            (AudioContentCapabilityIds.LocalContentBatch, "register-batch", "Atomic publication of a bounded group of validated CPU content with versioned dependencies, including forward references within the group. One owned registration withdraws the whole group while existing consumers retain their dependencies. This does not add songs to the game catalog or adapt music/cover resolution."),
            (AudioContentCapabilityIds.LocalCatalog, "register-read-song", "Bounded LOCAL IDs and exact validated resource revisions with optional provider-authored playback metadata. Withdrawal removes future listing while retained runs keep their resources. Publication alone does not grant native play permission."),
            (AudioContentCapabilityIds.LocalCatalogSelection, "select-request-play", "Per-scope exact revisions dispatch through the formal LOCAL host composition, verified native sources and independent production startup admission. Stale/cancelled requests are rejected; admission unavailable returns CapabilityUnavailable. Preview is unsupported."),
            (AudioContentCapabilityIds.WorkshopChartReplacement, "bind-existing-workshop-chart", "Experimental exclusive local fileVersion0 chart replacement at a verified WorkshopLevel.LoadChart entry. Only existing workshop IDs; no new library or original-loading-failure bypass. Runtime gameplay/result behavior remains unverified.") })
            capabilities.DeclareMetadata(operation.Item1, new(1, 0), CapabilityStability.Experimental, new[] {
                new CapabilityOperationInfo(operation.Item2, OperationThread.MainThread, ObservationMode.Snapshot,
                    CleanupResponsibility.EnableScope, operation.Item1 is AudioContentCapabilityIds.Replacement or AudioContentCapabilityIds.Mix or AudioContentCapabilityIds.WorkshopChartReplacement,
                    Array.Empty<string>(), operation.Item3) });
        capabilities.DeclareMetadata(LocalRunProtectionModule.Capability, new(0, 1), CapabilityStability.Experimental, new[] {
            new CapabilityOperationInfo("read-isolation-status", OperationThread.MainThread, ObservationMode.Snapshot,
                CleanupResponsibility.Process, false, Array.Empty<string>(),
                "Internal host prerequisite: copied exact-gate counters and admission state. No LOCAL play, unbound result classification, or stock-save isolation acceptance is declared.") });
        _audioContent = AudioContentModules.Create(api, main, log, packageDirectoryByPluginId ?? (_ => null),
            dataDirectoryByPluginId ?? (_ => null), profile,
            Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "BepInEx", "core", "dobby.dll"), _state,
            new RunEffectRecorder(_modifications.Record, _modifications.BeginEffect, _modifications.BeginObservedRunEffect), uiInput.GetHostUi);
        foreach (var module in _audioContent.Modules) RegisterModule(module);
        RegisterModule(new ExactGameplayHookModule(api, main, log, profile, _hooks,
            Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "BepInEx", "core", "dobby.dll")));
        capabilities.DeclareMetadata(JudgementCapabilityIds.Observe,new(0,1),CapabilityStability.Experimental,new[] {
            new CapabilityOperationInfo("subscribe",OperationThread.MainThread,ObservationMode.DirectCallback,CleanupResponsibility.EnableScope,false,
                Array.Empty<string>(),"Pinned caller note identity, retained observed-run origin and actual inner original-return counter snapshots. Raw timing units and runtime source coverage remain explicit.") });
        var judgements = new JudgementModule(api,main,log,profile,_state,events,
            Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!,"BepInEx","core","dobby.dll"),
            Path.Combine(Path.GetDirectoryName(typeof(RizlineAdapter).Assembly.Location)!,"RizToolbox.JudgeContext.dll"));
        _playback.AttachJudgementModule(judgements);
        RegisterModule(judgements);
        _runCancellation = new ExactRunCancellationModule(api, main, log, profile, _state,
            Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "BepInEx", "core", "dobby.dll"));
        RegisterModule(_runCancellation);
        _backCancellation = new ExactBackCancellationModule(api, main, log, profile, _state,
            Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "BepInEx", "core", "dobby.dll"));
        RegisterModule(_backCancellation);
        _skipTermination = new ExactSkipTerminationModule(api, main, log, profile, _state,
            Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "BepInEx", "core", "dobby.dll"));
        RegisterModule(_skipTermination);
        foreach (var alias in new[] {
            (AdapterOperationCapabilityIds.CatalogRead, CapabilityIds.Catalog), (AdapterOperationCapabilityIds.ScoresRead, CapabilityIds.Scores),
            (AdapterOperationCapabilityIds.UiCreate, CapabilityIds.Ui), (AdapterOperationCapabilityIds.ToolTextureOverride, CapabilityIds.Resources),
            (AdapterOperationCapabilityIds.GameResourceRead, GameCapabilityIds.Resources), (AdapterOperationCapabilityIds.GameResourceOverride, GameCapabilityIds.Resources),
            (AdapterOperationCapabilityIds.SceneRead, GameCapabilityIds.Scenes), (AdapterOperationCapabilityIds.SceneObserve, GameCapabilityIds.Scenes),
            (AdapterOperationCapabilityIds.RunRead, GameCapabilityIds.Runs), (AdapterOperationCapabilityIds.RunObserve, GameCapabilityIds.Runs) })
        {
            var source = capabilities.Describe(alias.Item2).Value!;
            var observe = alias.Item1.EndsWith(".observe", StringComparison.Ordinal);
            var read = alias.Item1.EndsWith(".read", StringComparison.Ordinal);
            var operations = source.Operations.Where(x => observe ? x.Id == "observe" : read ? x.Id is "read" or "snapshot" or "enumerate" : x.Id != "enumerate").ToArray();
            capabilities.DeclareMetadata(alias.Item1, source.Version, source.Stability, operations);
        }
        _modules.Tick(0);
        RunModulePhase("playback-startup", _gameplay, static module => module.MaintainPlayback());
        log.TryLog(LogLevel.Info, $"Game adapter initialized: {profile.Id}.");
    }

    /// <summary>Adapter extensions register here; Loader never contains per-feature lifecycle branches.</summary>
    internal void RegisterModule(IAdapterModule module) => _modules.Add(module, IsDemandManaged(module));
    // Process-owned transport and minimal observers remain separate from the
    // optional services whose publication follows the demand plan.
    internal static bool IsDemandManaged(IAdapterModule module)
        => module is UiModule or MenuResourceModule or GameAppearanceModule or GameCameraModule or GamePostProcessingModule or VisualModule
            or UiInputModule or GameplayModule or LocalCatalogModule or LocalCatalogSelectionModule
            || AudioContentModules.OwnsAudioCapability(module);

    public IReadOnlyList<AdapterModuleStatus> GetModules() => _modules.GetModules();
    public void SetCapabilityDemand(IReadOnlyCollection<string>? capabilityIds) => _modules.SetCapabilityDemand(capabilityIds);

    public void Tick(long hostTickSequence)
    {
        if (_disposed) return;
        AdapterTickPhase.Run(_log, "visual-retirement", Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency,
            static now => VisualResourceRetirement.TickPending(now));
        AdapterTickPhase.Run(_log, "local-run-maintenance", this, static _ => LocalRunProtectionHost.TickProcessOwners());
        var now = _clock.Elapsed.TotalSeconds;
        var updated = AdapterTickPhase.Run(_log, "modules", (Adapter: this, Now: now),
            static phase => phase.Adapter.TickModules(phase.Now));
        AdapterTickPhase.Run(_log, "audio-maintenance", (Adapter: this, Now: now),
            static phase => phase.Adapter._audioContent.Maintain(phase.Now, phase.Adapter._modules));
        AdapterTickPhase.Run(_log, "playback-retirement", this, static _ => NativePlaybackStockRateHost.PumpRetired());
        // Gameplay reads the committed module and playback state. Retirement
        // above continues even when either of these phases fails.
        if (!updated)
        {
            AdapterTickPhase.Run(_log, "playback-installation-and-drain", _playback, static owner => owner.PumpInstallationAndRetirement());
            return;
        }
        if (!RunModulePhase("playback-maintenance", _gameplay, static module => module.MaintainPlayback())) return;
        AdapterTickPhase.Run(_log, "gameplay-observation", (Adapter: this, Sequence: hostTickSequence),
            static phase => phase.Adapter.ObserveGameplay(phase.Sequence),
            static (phase, error) => phase.Adapter._modules.ReportFault(phase.Adapter._gameplay, error));
    }

    private void TickModules(double now)
    {
        var scene = _state.Snapshot().Scene;
        var observedScene = _capabilities.GetCapability(GameCapabilityIds.Scenes).Available ? scene : null;
        _modules.SetRetainedCapability(UiInputCapabilityIds.Windows, _audioContent.RetainsResultUi);
        _modules.Tick(now, observedScene);
        if (_state.Snapshot().Scene != scene) _modules.NotifySceneChanged();
    }

    private void ObserveGameplay(long hostTickSequence) => _gameplay.ObserveControls(hostTickSequence, _clock.Elapsed.TotalSeconds,
        _modules.HasPlannedCapability(GameplayCapabilityIds.Controls));

    private bool RunModulePhase<T>(string phase, T module, Action<T> work) where T : IAdapterModule
        => AdapterTickPhase.Run(_log, phase, (Manager: _modules, Module: module, Work: work),
            static state => state.Work(state.Module), static (state, error) => state.Manager.ReportFault(state.Module, error));

    public void FlushBoundaryObservations()
    {
        if (_disposed) return;
        RunModulePhase("retry-boundary", _runCancellation, static module => module.FlushAccepted());
        RunModulePhase("back-boundary", _backCancellation, static module => module.FlushAccepted());
        RunModulePhase("skip-boundary", _skipTermination, static module => module.FlushAccepted());
    }

    public void PumpCommands()
    {
        if (!_disposed) RunModulePhase("gameplay-command", _gameplay, static module => module.PumpControlCommand());
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        try { _modules.Dispose(); }
        finally
        {
            try { _playback.Dispose(); }
            finally { try { _modifications.Dispose(); } finally { _hooks.Dispose(); } }
        }
    }
}
