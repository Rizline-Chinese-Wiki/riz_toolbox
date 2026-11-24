using RizToolbox.Adapter.Gameplay;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Modules;

/// <summary>Read-only audit availability does not imply availability of a native control or a current run.</summary>
internal sealed class RunModificationModule : IAdapterModule
{
    private readonly RunModificationJournal _journal;
    internal RunModificationModule(RunModificationJournal journal) => _journal = journal;
    public AdapterModuleManifest Manifest { get; } = new("rizline.run-modifications", new(1,0),
        new[] { RunModificationCapabilityIds.Read }, Array.Empty<string>(), new[] { "*" }, Array.Empty<AdapterSymbolRequirement>(),
        "Bounded read-only audit with real lifecycle association; unassociated operations retain null RunId and unverified result/submission semantics.");
    public double IntervalSeconds => 2;
    public ModuleProbe Probe() => ModuleProbe.Available;
    public void Register(ModuleRegistration registration) => registration.RegisterScoped<IRunModificationService>(RunModificationCapabilityIds.Read, _journal.ForPlugin);
    public ModuleProbe Tick() => ModuleProbe.Available;
    // Adapter owns the journal so native/active-effect producers are released before its subscription.
    public void Release() { }
}
