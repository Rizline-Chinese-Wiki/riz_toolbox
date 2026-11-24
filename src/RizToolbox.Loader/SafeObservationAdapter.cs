using RizToolbox.Adapter;
using RizToolbox.Sdk;

namespace RizToolbox.Loader;

/// <summary>Invalid startup configuration permits host diagnostics only. In
/// particular, constructing the normal adapter would already probe optional
/// game modules, even if every plugin were disabled afterward.</summary>
internal sealed class SafeObservationAdapter : IGameAdapter
{
    internal SafeObservationAdapter(GameBuildDescriptor build) => Build = build;
    public GameBuildDescriptor Build { get; }
    public IReadOnlyList<string> SupportedCapabilities => Array.Empty<string>();
    public IReadOnlyList<AdapterModuleStatus> GetModules() => Array.Empty<AdapterModuleStatus>();
    public void FlushBoundaryObservations() { }
    public void PumpCommands() { }
    public void Tick(long hostTickSequence) { }
    public void Dispose() { }
}
