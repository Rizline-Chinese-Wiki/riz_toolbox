using RizToolbox.Core.Mutation;
using RizToolbox.Core.Targets;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Modules;

/// <summary>The shared registry outlives any one discovery provider or scene.
/// Providers remove only their own target generations and reversible channels.</summary>
internal sealed class SemanticTargetsModule : IAdapterModule
{
    private readonly IMainThreadDispatcher _main;
    private readonly ILogger _log;
    private SemanticTargetRegistry? _targets;
    private MutationCoordinator? _mutations;
    internal SemanticTargetsModule(IMainThreadDispatcher main, ILogger log) => (_main, _log) = (main, log);
    public AdapterModuleManifest Manifest { get; } = new("rizline.semantic-targets", new(1, 0),
        new[] { TargetCapabilityIds.Targets, TargetCapabilityIds.Mutations }, Array.Empty<string>(),
        Array.Empty<string>(), Array.Empty<AdapterSymbolRequirement>(),
        "Shared typed targets and property arbitration stay available with an empty registry; each discovery provider owns its own generations.");
    public double IntervalSeconds => 1;
    internal SemanticTargetRegistry Targets => _targets ?? throw new InvalidOperationException("The shared target provider is not active.");
    internal MutationCoordinator Mutations => _mutations ?? throw new InvalidOperationException("The shared mutation provider is not active.");
    public ModuleProbe Probe()
    {
        _main.VerifyAccess();
        _targets ??= new(_main, _log);
        _mutations ??= new(_targets, _main, _log);
        return ModuleProbe.Available;
    }
    public void Register(ModuleRegistration registration)
    {
        registration.RegisterScoped<ITargetService>(TargetCapabilityIds.Targets, context => Targets.ForPlugin(context));
        registration.RegisterScoped<IMutationService>(TargetCapabilityIds.Mutations, context => Mutations.ForPlugin(context));
    }
    public ModuleProbe Tick() { _main.VerifyAccess(); _ = Targets; _ = Mutations; return ModuleProbe.Available; }
    public void Release()
    {
        _main.VerifyAccess(); var mutations = _mutations; var targets = _targets;
        _mutations = null; _targets = null;
        try { mutations?.Dispose(); } finally { targets?.Dispose(); }
    }
}
