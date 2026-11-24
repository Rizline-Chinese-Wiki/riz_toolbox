using RizToolbox.Adapter.Modules;
using RizToolbox.Core;
using RizToolbox.Sdk;

internal static class ModuleDemandChecks
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Empty demand leaves optional work untouched and starts always-on modules", EmptyDemand),
        ("A waiting consumer's alias demand wakes only its required capability closure", WaitingAliasDemand),
        ("Demand changes revoke stale views and release dependents before providers", LastConsumerWithdrawal),
        ("Named scenes gate demand and an undeclared scene uses existing probe eligibility", SceneDemand),
        ("Full diagnostics are explicit and always-on dependencies survive a return to empty demand", DiagnosticDemand),
        ("Failed demand cleanup withdraws access and requires a restart", FailedCleanup)
    };

    private static void EmptyDemand()
    {
        using var f = new Fixture();
        var observer = f.Add("observer");
        var optional = f.Add("optional", onDemand: true);
        f.Manager.Tick(0);
        f.Manager.NotifySceneChanged();
        f.Manager.Tick(15, "Play");
        Check(observer.Probes == 1 && f.Status("observer").State == AdapterModuleState.Active, "The observer should activate.");
        Check(f.Status("optional").State == AdapterModuleState.Dormant && !f.Manager.RequestProbe("optional"), "The optional module should stay dormant.");
        Check(f.Resolver.Probed.SequenceEqual(new[] { "observer.symbol" }), "Only the observer's symbols should be resolved.");
        Check(!f.Registry.GetService<IProbeService>("optional.cap").Available && optional.Probes == 0, "A lookup should not activate the provider.");
        f.Manager.Dispose();
        Check(optional.Releases == 0, "A dormant module that never probed should not release resources.");
    }

    private static void WaitingAliasDemand()
    {
        using var f = new Fixture();
        f.Add("base", onDemand: true);
        f.Add("middle", dependencies: new[] { "base.cap" }, onDemand: true);
        f.Add("a", new[] { "a.primary", "a.cap" }, new[] { "middle.cap" }, onDemand: true);
        f.Add("unrelated", onDemand: true);
        using var main = new MainThreadDispatcher();
        using var host = new PluginHost(f.Registry, main, new EventHub(f.Log), f.Log);
        var plugin = new ProbePlugin();
        Check(host.Register(plugin).Success, "The consumer should register.");
        host.Enable("test.consumer");
        Check(host.Statuses.Single().State == PluginState.Waiting && plugin.Enabled == 0, "The consumer should wait for the provider.");

        var demand = new List<string> { "a.cap" };
        f.Manager.SetCapabilityDemand(demand);
        demand.Clear();
        Check(f.Resolver.Probed.Count == 0, "Submitting intent should leave all probing on the tick.");
        f.Manager.Tick(0);
        Check(f.Resolver.Probed.SequenceEqual(new[] { "base.symbol", "middle.symbol", "a.symbol" }), "The alias should resolve its transitive owners.");
        Check(plugin.Enabled == 1 && plugin.Service!.Value() == "a", "The waiting consumer should enable when the alias appears.");
        Check(f.Status("unrelated").State == AdapterModuleState.Dormant, "Unrelated modules should stay dormant.");
    }

    private static void LastConsumerWithdrawal()
    {
        using var f = new Fixture();
        var provider = f.Add("base", onDemand: true);
        var consumer = f.Add("a", new[] { "a.cap", "a.alias" }, new[] { "base.cap" }, onDemand: true);
        var releaseOrder = new List<string>();
        bool unavailableAtRelease = false;
        provider.OnRelease = () => releaseOrder.Add("base");
        consumer.OnRelease = () =>
        {
            unavailableAtRelease = !f.Registry.GetCapability("a.cap").Available && !f.Registry.GetCapability("a.alias").Available;
            releaseOrder.Add("a");
        };

        using var main = new MainThreadDispatcher();
        using var host = new PluginHost(f.Registry, main, new EventHub(f.Log), f.Log);
        var plugin = new ProbePlugin();
        host.Register(plugin);
        host.Enable("test.consumer");
        f.Manager.SetCapabilityDemand(new[] { "a.cap", "a.alias" });
        f.Manager.Tick(0);
        var old = plugin.Service!;
        var generation = f.Registry.Describe("a.cap").Value!.Generation;
        f.Manager.SetCapabilityDemand(new[] { "a.alias" });
        f.Manager.Tick(.1);
        Check(consumer.Releases == 0 && f.Registry.Describe("a.cap").Value!.Generation == generation, "One remaining alias consumer keeps the shared owner.");
        f.Manager.SetCapabilityDemand(Array.Empty<string>());
        f.Manager.Tick(.2);
        Check(unavailableAtRelease && releaseOrder.SequenceEqual(new[] { "a", "base" }), "Withdraw both capability IDs before releasing the dependency closure.");
        Check(f.Status("base").State == AdapterModuleState.Dormant && f.Status("a").State == AdapterModuleState.Dormant, "Released modules should return to dormant.");
        Check(Expired(old), "The old service view should expire.");

        f.Manager.SetCapabilityDemand(new[] { "a.cap" });
        f.Manager.Tick(.3);
        Check(plugin.Enabled == 2 && provider.Probes == 2 && consumer.Probes == 2, "Renewed demand should create a fresh generation.");
        Check(plugin.Service!.Value() == "a" && Expired(old), "A new generation should never revive an old view.");
    }

    private static void SceneDemand()
    {
        using var f = new Fixture();
        var dependency = f.Add("resource", onDemand: true);
        var specific = f.Add("specific", dependencies: new[] { "resource.cap" }, onDemand: true, scenes: new[] { "Play" });
        var undeclared = f.Add("undeclared", onDemand: true, scenes: Array.Empty<string>());
        f.Manager.SetCapabilityDemand(new[] { "specific.cap", "undeclared.cap" });
        f.Manager.Tick(0);
        f.Manager.Tick(.1, "Menu");
        Check(specific.Probes == 0 && dependency.Probes == 0 && undeclared.Probes == 1, "A scene-specific branch should wait without blocking the independent unclassified module.");
        f.Manager.Tick(.2, "Play");
        Check(specific.Probes == 1 && dependency.Probes == 1, "The named scene should wake the requested branch.");
        f.Manager.Tick(.3);
        Check(specific.Releases == 1 && dependency.Releases == 1 && undeclared.Releases == 0, "Losing the observed scene should retire only the scene-specific branch.");
        f.Manager.Tick(.4, "Play");
        Check(specific.Probes == 2 && dependency.Probes == 2, "The observed scene should restart the eligible branch.");
    }

    private static void DiagnosticDemand()
    {
        using var f = new Fixture();
        var shared = f.Add("shared", onDemand: true);
        var startup = f.Add("startup", dependencies: new[] { "shared.cap" });
        var optional = f.Add("optional", onDemand: true);
        f.Manager.Tick(0);
        Check(startup.Probes == 1 && shared.Probes == 1 && optional.Probes == 0, "An always-on module keeps its prerequisites eligible.");
        f.Manager.SetCapabilityDemand(null);
        f.Manager.Tick(.1);
        Check(optional.Probes == 1, "Full diagnostics should explicitly admit optional work.");
        f.Manager.SetCapabilityDemand(Array.Empty<string>());
        f.Manager.Tick(.2);
        Check(optional.Releases == 1 && startup.Releases == 0 && shared.Releases == 0, "An empty plan should retain only startup roots and their closure.");
    }

    private static void FailedCleanup()
    {
        using var f = new Fixture();
        var optional = f.Add("optional", onDemand: true);
        f.Add("independent", onDemand: true);
        optional.OnRelease = () => throw new InvalidOperationException("cleanup unconfirmed");
        f.Manager.SetCapabilityDemand(new[] { "optional.cap" });
        f.Manager.Tick(0);
        f.Manager.SetCapabilityDemand(new[] { "independent.cap" });
        f.Manager.Tick(.1);
        Check(f.Status("independent").State == AdapterModuleState.Active, "Independent work should proceed after a release fault.");
        Check(!f.Registry.GetCapability("optional.cap").Available && f.Status("optional").State == AdapterModuleState.Unsupported
            && f.Status("optional").Failure!.Contains("restart is required", StringComparison.Ordinal), "Failed cleanup should withdraw access and report retained responsibility.");
        f.Manager.NotifySceneChanged();
        f.Manager.SetCapabilityDemand(null);
        f.Manager.Tick(10, "Play");
        Check(!f.Manager.RequestProbe("optional") && optional.Probes == 1 && optional.Releases == 1, "A new plan should never recreate a provider with failed retirement.");
    }

    private static bool Expired(IProbeService view)
    {
        try { view.Value(); return false; }
        catch (ObjectDisposedException) { return true; }
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
