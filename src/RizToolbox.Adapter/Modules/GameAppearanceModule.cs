using RizToolbox.Adapter.GameAppearance;
using RizToolbox.Core.Mutation;
using RizToolbox.Core.Targets;
using RizToolbox.Runtime;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Modules;

/// <summary>Game appearance discovery is independent of menu/UI discovery.
/// Empty scenes keep the query service available without inventing a target.</summary>
internal sealed class GameAppearanceModule : IAdapterModule
{
    private readonly IMainThreadDispatcher _main;
    private readonly SemanticTargetsModule _shared;
    private readonly Func<SemanticTargetRegistry, MutationCoordinator, GameAppearanceService> _create;
    private readonly string _fingerprint;
    private GameAppearanceService? _service;
    internal GameAppearanceModule(NativeRuntime api, IMainThreadDispatcher main, ILogger log,
        SemanticTargetsModule shared, string fingerprint, Func<GameLifecycleSnapshot?> lifecycle,
        Func<long, long, string, long, bool> matchesRun)
        : this(main, shared, (targets, mutations) => new(api, main, log, targets, mutations, lifecycle,matchesRun), fingerprint) { }
    internal GameAppearanceModule(IMainThreadDispatcher main, SemanticTargetsModule shared,
        Func<SemanticTargetRegistry, MutationCoordinator, GameAppearanceService> create, string fingerprint)
        => (_main, _shared, _create, _fingerprint) = (main, shared, create, fingerprint);
    public AdapterModuleManifest Manifest { get; } = new("rizline.game-appearance", new(1, 0),
        new[] { GameAppearanceCapabilityIds.Appearance },
        new[] { TargetCapabilityIds.Targets, TargetCapabilityIds.Mutations, GameLifecycleCapabilityIds.Lifecycle },
        Array.Empty<string>(), NativeGameAppearanceBackend.Requirements, NativeGameAppearanceBackend.ValidationEvidence);
    public double IntervalSeconds => .25;
    public ModuleProbe Probe()
    {
        _main.VerifyAccess();
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || _fingerprint != NativeGameAppearanceBackend.Fingerprint)
            return ModuleProbe.Unsupported("Game appearance requires its pinned Windows x64 game build.");
        _service ??= _create(_shared.Targets, _shared.Mutations);
        _service.Refresh(); return ModuleProbe.Available;
    }
    public void Register(ModuleRegistration registration)
        => registration.RegisterScoped<IGameAppearanceService>(GameAppearanceCapabilityIds.Appearance,
            context => (_service ?? throw new InvalidOperationException("The game appearance provider is not active.")).ForPlugin(context));
    public ModuleProbe Tick()
    {
        _main.VerifyAccess();
        if (_service is null) return ModuleProbe.Waiting("The game appearance service has been released.");
        _service.Refresh(); return ModuleProbe.Available;
    }
    public void Release()
    {
        _main.VerifyAccess();
        _service?.Dispose();
        _service = null;
    }
}
