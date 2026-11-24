using RizToolbox.Adapter.GameCamera;
using RizToolbox.Core.Mutation;
using RizToolbox.Core.Targets;
using RizToolbox.Runtime;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Modules;

/// <summary>Game camera discovery is independent of menu/UI discovery.
/// Empty scenes keep the query service available without inventing a target.</summary>
internal sealed class GameCameraModule : IAdapterModule
{
    private readonly IMainThreadDispatcher _main;
    private readonly SemanticTargetsModule _shared;
    private readonly Func<SemanticTargetRegistry, MutationCoordinator, GameCameraService> _create;
    private readonly string _fingerprint;
    private GameCameraService? _service;
    internal GameCameraModule(NativeRuntime api, IMainThreadDispatcher main, ILogger log,
        SemanticTargetsModule shared, string fingerprint, Func<GameLifecycleSnapshot?> lifecycle,
        Func<long, long, string, long, bool> matchesRun)
        : this(main, shared, (targets, mutations) => new(api, main, log, targets, mutations, lifecycle,matchesRun), fingerprint) { }
    internal GameCameraModule(IMainThreadDispatcher main, SemanticTargetsModule shared,
        Func<SemanticTargetRegistry, MutationCoordinator, GameCameraService> create, string fingerprint)
        => (_main, _shared, _create, _fingerprint) = (main, shared, create, fingerprint);
    public AdapterModuleManifest Manifest { get; } = new("rizline.game-camera", new(1, 0),
        new[] { GameCameraCapabilityIds.Camera },
        new[] { TargetCapabilityIds.Targets, TargetCapabilityIds.Mutations, GameLifecycleCapabilityIds.Lifecycle },
        Array.Empty<string>(), NativeGameCameraBackend.Requirements, NativeGameCameraBackend.ValidationEvidence);
    // Discovery is bounded separately by the service. A per-tick refresh is
    // required because CameraControl.Update writes both rig position and scale.
    // Actual ordering relative to that Update and rendering remains unverified.
    public AdapterModuleTickCadence TickCadence => AdapterModuleTickCadence.EveryHostTick;
    public double IntervalSeconds => 1;
    public ModuleProbe Probe()
    {
        _main.VerifyAccess();
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || _fingerprint != NativeGameCameraBackend.Fingerprint)
            return ModuleProbe.Unsupported("Game camera requires its pinned Windows x64 game build.");
        _service ??= _create(_shared.Targets, _shared.Mutations);
        return ModuleProbe.Available;
    }
    public void Register(ModuleRegistration registration)
        => registration.RegisterScoped<IGameCameraService>(GameCameraCapabilityIds.Camera,
            context => (_service ?? throw new InvalidOperationException("The game camera provider is not active.")).ForPlugin(context));
    public ModuleProbe Tick()
    {
        _main.VerifyAccess();
        if (_service is null) return ModuleProbe.Waiting("The game camera service has been released.");
        _service.Refresh(); return ModuleProbe.Available;
    }
    public void Release()
    {
        _main.VerifyAccess();
        _service?.Dispose();
        _service = null;
    }
}
