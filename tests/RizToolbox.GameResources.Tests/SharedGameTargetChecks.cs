using RizToolbox.Adapter;
using RizToolbox.Adapter.Modules;
using RizToolbox.Core;
using RizToolbox.Core.Mutation;
using RizToolbox.Core.Targets;
using RizToolbox.Sdk;
using static TestColors;

internal static class SharedGameTargetChecks
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Shared target capability remains available without any UI discovery", EmptyRegistry),
        ("Removing one color provider restores only its layers and preserves a peer provider", IndependentProviders),
        ("Refreshing one discovery provider does not write another provider's channels", IndependentRefresh),
        ("A scoped UI color service refuses a peer provider's shared color generation", OwnColorBoundary),
        ("Shared registry withdrawal invalidates old generations and a fresh provider can recover", RegistryRecovery)
    };
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("Shared target ownership check failed."); }
    private static T Need<T>(OperationResult<T> value) => value.Success ? value.Value! : throw new InvalidOperationException(value.Error?.Message);
    private static void EmptyRegistry()
    {
        using var main = new MainThreadDispatcher(); var module = new SemanticTargetsModule(main, new Logger());
        Check(module.Probe().Ready && module.Tick().Ready && module.Targets.GetTargets().Count == 0);
        Check(module.Manifest.Capabilities.Contains(TargetCapabilityIds.Targets)
            && module.Manifest.Capabilities.Contains(TargetCapabilityIds.Mutations)
            && module.Manifest.RequiredCapabilities.Count == 0 && module.Manifest.RequiredSymbols.Count == 0);
        module.Release(); module.Release(); Check(module.Probe().Ready); module.Release();
    }
    private static void IndependentProviders()
    {
        using var main = new MainThreadDispatcher(); var log = new Logger();
        using var targets = new SemanticTargetRegistry(main, log); using var mutations = new MutationCoordinator(targets, main, log);
        var first = new Image("ui"); var peer = new Image("track");
        var firstBackend = new Backend(); firstBackend.Images.Add(first); var peerBackend = new Backend(); peerBackend.Images.Add(peer);
        using var firstService = new GameResourceService(main, log, firstBackend, targets, mutations);
        using var peerService = new GameResourceService(main, log, peerBackend, targets, mutations);
        Check(firstService.Refresh(true) == 1 && peerService.Refresh(true) == 1 && targets.GetTargets().Count == 2);
        Check(Need(firstService.GetTargets()).All(value => !value.Key.Contains("track", StringComparison.Ordinal)));
        using var firstLayer = Need(firstService.OverrideColor(new(first.GenericAlias, Red)));
        using var peerLayer = Need(peerService.OverrideColor(new(peer.GenericAlias, Blue)));
        firstService.Dispose(); firstService.Dispose();
        Check(first.Color == first.Original && first.Released == 1 && peer.Released == 0 && peer.Color == Blue);
        Check(targets.Resolve<ResourceColor>(new(Key: first.GenericAlias)).Kind == TargetMatchKind.Missing
            && targets.Resolve<ResourceColor>(new(Key: peer.GenericAlias)).Kind == TargetMatchKind.Unique);
        Check(peerService.Refresh(true) == 1); peerLayer.Dispose(); Check(peer.Color == peer.Original);
    }
    private static void IndependentRefresh()
    {
        using var main = new MainThreadDispatcher(); var log = new Logger();
        using var targets = new SemanticTargetRegistry(main, log); using var mutations = new MutationCoordinator(targets, main, log);
        var a = new Backend(); var b = new Backend(); var first = new Image("first"); var peer = new Image("peer");
        a.Images.Add(first); b.Images.Add(peer);
        using var firstService = new GameResourceService(main, log, a, targets, mutations);
        using var peerService = new GameResourceService(main, log, b, targets, mutations);
        firstService.Refresh(true); peerService.Refresh(true);
        using var peerLayer = Need(peerService.OverrideColor(new(peer.GenericAlias, Blue)));
        peer.Color = Green; var writes = peer.Writes; firstService.Refresh(true);
        Check(peer.Color == Green && writes == peer.Writes);
        peerService.Refresh(true); Check(peer.Color == Blue && peer.Writes == writes + 1);
        peerLayer.Dispose(); Check(peer.Color == Green);
    }
    private static void RegistryRecovery()
    {
        using var main = new MainThreadDispatcher(); var module = new SemanticTargetsModule(main, new Logger()); module.Probe();
        var firstRegistry = module.Targets; var value = Need(firstRegistry.Register("test.shared", () => 4, () => true));
        using var lease = Need(firstRegistry.Acquire(value)); module.Release(); Check(!lease.IsValid);
        module.Probe(); Check(!ReferenceEquals(firstRegistry, module.Targets));
        Check(!module.Targets.Acquire(value).Success);
        var fresh = Need(module.Targets.Register("test.shared", () => 5, () => true));
        Check(fresh.Generation != value.Generation && Need(module.Targets.Acquire(fresh)).Read().Value == 5);
        module.Release();
    }
    private static void OwnColorBoundary()
    {
        using var main = new MainThreadDispatcher(); var log = new Logger();
        using var targets = new SemanticTargetRegistry(main, log); using var mutations = new MutationCoordinator(targets, main, log);
        var ui = new Backend(); var peer = new Backend(); var image = new Image("own-ui"); var track = new Image("peer-track");
        ui.Images.Add(image); peer.Images.Add(track);
        using var service = new GameResourceService(main, log, ui, targets, mutations);
        using var peerService = new GameResourceService(main, log, peer, targets, mutations);
        service.Refresh(true); peerService.Refresh(true);
        var caps = new CapabilityRegistry(new("test", "test", "test", "test", true));
        caps.RegisterScoped<IGameResourceService>(GameCapabilityIds.Resources, context => service.ForPlugin(context));
        using var host = new PluginHost(caps, main, new EventHub(log), log); var plugin = new BoundaryConsumer();
        Check(host.Register(plugin).Success && host.Enable(plugin.Manifest.Id).Success);
        var descriptor = targets.GetTargets().Single(value => value.Aliases.Contains(track.GenericAlias));
        foreach (var key in new[] { descriptor.Key, track.GenericAlias })
        {
            var refused = plugin.View!.OverrideColor(new(key, Red));
            Check(!refused.Success && refused.Error is { Code: ErrorCode.TargetMissing, CapabilityId: GameCapabilityIds.Resources }
                && refused.Error.TargetId == key && track.Color == track.Original && track.Writes == 0);
        }
        using var ownLayer = Need(plugin.View!.OverrideColor(new(image.GenericAlias, Green)));
        using var peerLayer = Need(peerService.OverrideColor(new(track.GenericAlias, Blue)));
        Check(image.Color == Green && track.Color == Blue);
    }
    private sealed class BoundaryConsumer : IRizPlugin
    {
        public PluginManifest Manifest => new("test.ui-provider-boundary", new(1, 0), new(1, 0),
            RequiredCapabilities: new[] { GameCapabilityIds.Resources });
        internal IGameResourceService? View;
        public void Enable(IPluginContext context) => View = context.GetService<IGameResourceService>(GameCapabilityIds.Resources).Service;
        public void Disable() { }
    }
}
