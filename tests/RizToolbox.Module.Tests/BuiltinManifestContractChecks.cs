using RizToolbox.Adapter;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Adapter.GameCamera;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Adapter.Judgements;
using RizToolbox.Adapter.Modules;
using RizToolbox.Adapter.Settings;
using RizToolbox.Adapter.UiInput;
using RizToolbox.Adapter.Visual;
using RizToolbox.Core;
using RizToolbox.Core.Hooks;
using RizToolbox.Sdk;

internal static class BuiltinManifestContractChecks
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Every production built-in module registers together before native probing", RegisterAllBuiltins),
        ("Camera module uses a positive normal interval and reapplies on every advancing host tick", CameraEveryHostTick)
    };

    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    private static void RegisterAllBuiltins()
    {
        // Use the shipped profile and actual module constructors/manifest factories.
        // Null native APIs are intentional: registration must not probe the game.
        var profile = BuildProfiles.Select(Path.Combine(AppContext.BaseDirectory, "profiles"), NativeGameCameraBackend.Fingerprint);
        using var main = new MainThreadDispatcher(); var log = new Logger();
        var state = new GameEventState(); var events = new EventHub(log); var extensions = new ExtensionPointRegistry(log);
        using var journal = new RunModificationJournal(main, log, state);
        using var hooks = new HookDispatcher(main, log);
        var capabilities = new CapabilityRegistry(new(profile.Id, profile.GameVersion, profile.UnityVersion, profile.GameAssemblySha256, true));
        capabilities.Register<IExtensionService>(CapabilityIds.Extensions, extensions);
        var resolver = new NoProbeResolver();
        using var manager = new ModuleManager(capabilities, resolver, log, profile.Id, profile.GameAssemblySha256);
        var runs = new ObservationModule(null!, profile, main, events, extensions, log, state, false);
        var settings = new SettingsModule(api: null!, main, log);
        var shared = new SemanticTargetsModule(main, log);
        var uiInput = new UiInputModule(api: null!, main, log, events);
        var modules = new List<IAdapterModule> {
            new ObservationModule(null!, profile, main, events, extensions, log, state, true), runs,
            new ExactResultOriginModule(null!, main, log, profile, state, "unused-dobby.dll"),
            new RunModificationModule(journal), settings, new SettingsOverrideModule(settings),
            new GameplayModule(null!, main, state.LifecycleSnapshot, journal.Record, state.MatchesCurrentGameplayRun),
            new DataModule(null!, main, log, IntPtr.Zero, runs.ReadCurrentResult, settings.CaptureDataSettings),
            new UiModule(null!, main, log), shared, new MenuResourceModule(null!, main, log, IntPtr.Zero, shared),
            new GameAppearanceModule(null!, main, log, shared, profile.GameAssemblySha256, state.LifecycleSnapshot, state.MatchesCurrentGameplayRun),
            new GameCameraModule(null!, main, log, shared, profile.GameAssemblySha256, state.LifecycleSnapshot, state.MatchesCurrentGameplayRun),
            new GamePostProcessingModule(null!, main, log, shared, profile.GameAssemblySha256)
        };
        var visualBudget = new VisualResourceBudget(); var retirement = new VisualResourceRetirement(main, log);
        foreach (var kind in new[] { VisualTargetKind.Sprite, VisualTargetKind.Font, VisualTargetKind.Material })
            modules.Add(new VisualModule(null!, main, log, kind, _ => null, _ => null, visualBudget, retirement));
        modules.Add(uiInput);
        modules.AddRange(AudioContentModules.Create(null!, main, log, _ => null, _ => null, profile, "unused-dobby.dll", state,
            new RunEffectRecorder(journal.Record, journal.BeginEffect), uiInput.GetHostUi).Modules);
        modules.AddRange(new IAdapterModule[] {
            new ExactGameplayHookModule(null!, main, log, profile, hooks, "unused-dobby.dll"),
            new JudgementModule(null!, main, log, profile, state, events, "unused-dobby.dll", "unused-judge-context.dll"),
            new ExactRunCancellationModule(null!, main, log, profile, state, "unused-dobby.dll"),
            new ExactBackCancellationModule(null!, main, log, profile, state, "unused-dobby.dll"),
            new ExactSkipTerminationModule(null!, main, log, profile, state, "unused-dobby.dll")
        });
        Check(modules.Count == 32, "The production registration inventory must contain all 32 built-in instances.");
        var implementationTypes = typeof(IAdapterModule).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(IAdapterModule).IsAssignableFrom(type)).ToHashSet();
        Check(implementationTypes.SetEquals(modules.Select(module => module.GetType())),
            "A production module implementation is missing from the built-in registration contract.");
        var demandManaged = new HashSet<string>(StringComparer.Ordinal) {
            "rizline.tool-ui", "rizline.menu-resources", "rizline.game-appearance", "rizline.game-camera", "rizline.game-post-processing",
            "rizline.visual.sprite", "rizline.visual.font", "rizline.visual.material", "rizline.audio.clock",
            "rizline.audio.playback", "rizline.audio.replace", "rizline.audio.mix", "rizline.ui-input",
            "rizline.gameplay-primitives", "rizline.content.local-catalog", "rizline.content.local-catalog-selection"
        };
        foreach (var module in modules)
        {
            var onDemand = RizlineAdapter.IsDemandManaged(module);
            Check(onDemand == demandManaged.Contains(module.Manifest.Id), $"Unexpected demand policy for '{module.Manifest.Id}'.");
            try { manager.Add(module, onDemand); }
            catch (Exception error) { throw new InvalidOperationException($"Built-in '{module.Manifest.Id}' failed production registration.", error); }
        }
        var registered = manager.GetModules();
        Check(registered.Select(item => item.Manifest.Id).SequenceEqual(modules.Select(module => module.Manifest.Id)),
            "All built-ins must survive registration in production order.");
        Check(registered.All(item => item.ProbeAttempts == 0 && item.State ==
                (demandManaged.Contains(item.Manifest.Id) ? AdapterModuleState.Dormant : AdapterModuleState.Waiting)),
            "Manifest registration must defer native probing and hold audited modules dormant.");
        Check(resolver.Calls == 0, "Manifest registration called the native symbol resolver.");
        var owned = modules.SelectMany(module => module.Manifest.Capabilities).ToHashSet(StringComparer.Ordinal);
        Check(modules.SelectMany(module => module.Manifest.RequiredCapabilities).All(id => owned.Contains(id) || id == CapabilityIds.Extensions),
            "A built-in dependency has no production provider.");
        Check(owned.All(id => !capabilities.GetCapability(id).Available), "Manifest registration published a capability before probing.");
    }

    private static void CameraEveryHostTick()
    {
        using var main = new MainThreadDispatcher(); var log = new Logger(); var state = new GameEventState();
        var capabilities = new CapabilityRegistry(new("camera-tick", "test", "test", NativeGameCameraBackend.Fingerprint, true));
        capabilities.Register<IGameLifecycleService>(GameLifecycleCapabilityIds.Lifecycle, state);
        var shared = new SemanticTargetsModule(main, log); var backend = new TickCameraBackend();
        double now = 0;
        var snapshot = new GameLifecycleSnapshot("session", 1, 10, "Play", "run", "chart", RunLifecyclePhase.Playing,
            false, true, true, true, Array.Empty<OperationError>());
        using var manager = new ModuleManager(capabilities, new Resolver(), log, "camera-tick", NativeGameCameraBackend.Fingerprint);
        manager.Add(shared);
        var camera = new GameCameraModule(main, shared, (targets, mutations) =>
            new GameCameraService(main, log, targets, mutations, backend, () => snapshot, () => now), NativeGameCameraBackend.Fingerprint);
        Check(camera.TickCadence == AdapterModuleTickCadence.EveryHostTick, "Camera maintenance must run on each admitted host tick.");
        manager.Add(camera, onDemand: true);
        using var host = new PluginHost(capabilities, main, new EventHub(log), log); var plugin = new CameraConsumer();
        manager.Tick(0);
        Check(backend.Discoveries == 0, "A camera without demand scanned the game.");
        Check(host.Register(plugin).Success, "Camera consumer did not register.");
        host.Enable(plugin.Manifest.Id);
        manager.SetCapabilityDemand(host.GetRequiredCapabilityDemand());
        manager.Tick(0);
        Check(plugin.Camera is not null, "Actual camera module did not activate through ModuleManager.");
        Check(backend.Discoveries == 0, "Camera probing ran the gameplay discovery phase.");
        manager.Tick(0);
        using var lease = plugin.Camera!.OverrideScalar(new(new(Role: GameCameraRoles.RigLocalPositionX), "run", 2)).Value
            ?? throw new InvalidOperationException("Camera scalar override did not activate.");
        foreach (var elapsed in new[] { 0d, 0d, 1e-12, 2e-12, 1d / 1000, 1d / 240, 1d / 60, 10d })
        {
            backend.Value = 17; var writes = backend.Writes;
            now = elapsed;
            manager.Tick(elapsed);
            Check(backend.Value == 2 && backend.Writes == writes + 1,
                $"Camera must reapply exactly once on advancing host tick {elapsed:R}.");
        }
        Check(backend.Discoveries == 2, "Per-tick maintenance bypassed the discovery interval.");
        Check(manager.GetModules().Single(item => item.Manifest.Id == "rizline.game-camera").State == AdapterModuleState.Active,
            "Camera registration or reconciliation suspended the actual module.");
    }

    private sealed class NoProbeResolver : IModuleSymbolResolver
    {
        internal int Calls;
        public AdapterSymbolProbe Probe(AdapterSymbolRequirement symbol)
        { Calls++; throw new InvalidOperationException("Built-in manifest registration must not probe native symbols."); }
    }
    private sealed class CameraConsumer : IRizPlugin
    {
        public PluginManifest Manifest => new("test.camera-module-ticks", new(1, 0), new(1, 0), RequiredCapabilities: new[] { GameCameraCapabilityIds.Camera });
        internal IGameCameraService? Camera;
        public void Enable(IPluginContext context) => Camera = context.GetService<IGameCameraService>(GameCameraCapabilityIds.Camera).Service;
        public void Disable() { }
    }
    private sealed class TickCameraBackend : IGameCameraBackend
    {
        internal float Value;
        internal int Writes, Discoveries;
        public IReadOnlyList<GameCameraScalarProperty> Properties => new[] { NativeGameCameraBackend.LocalX };
        public IEnumerable<GameCameraCandidate> Discover(GameCameraRun run, int maximumScanned)
        {
            Discoveries++;
            yield return new("tick-camera", "game.camera/test-tick/local-position-x", "tick-camera-x", run,
                new(Scene: "Play", Role: GameCameraRoles.RigLocalPositionX, ContentId: "chart"), NativeGameCameraBackend.LocalX,
                Array.Empty<string>(), () => Value, value => { Value = value; Writes++; }, () => true, () => true, () => new Lifetime());
        }
    }
    private sealed class Lifetime : IDisposable { public void Dispose() { } }
}
