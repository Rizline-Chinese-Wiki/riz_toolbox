using RizToolbox.Core;
using RizToolbox.Sdk;
using static Helpers;

internal static class DiagnosticSelectionChecks
{
    internal static readonly (string Name, Action Run)[] All =
    {
        ("bounded diagnostic metadata visits preserve complete sorted SDK queries", Metadata),
        ("bounded extension selection stays detached from later registration", Extensions),
        ("diagnostic plugin and scope budgets do not change ownership or full counts", Plugins)
    };

    private static void Metadata()
    {
        var registry = new CapabilityRegistry(new("offline", "offline", "win-x64", "offline", true));
        var operation = new CapabilityOperationInfo("read", OperationThread.MainThread, ObservationMode.Snapshot,
            CleanupResponsibility.None, false, new[] { "*" }, "Diagnostic metadata fixture.");
        foreach (var id in new[] { "z", "a", "m" })
        {
            registry.DeclareMetadata(id, new(1, 0), CapabilityStability.Experimental, new[] { operation });
            registry.Register<object>(id, new object());
        }
        registry.DeclareMetadata("unregistered", new(2, 0), CapabilityStability.Experimental, new[] { operation });
        var complete = registry.GetAll();
        var selected = registry.CaptureDiagnosticMetadata(2);
        Check(selected.Items.Count == 2 && selected.TruncatedValues == 5,
            "Diagnostic selection inspected beyond the entry/metadata limits or hid unvisited inputs.");
        Check(selected.Items.All(item => item.Available && item.Generation > 0 && item.Operations.Count == 1),
            "Provider-free diagnostic rows lost their actual availability or declared operations.");
        var old = selected.Items[0];
        registry.SetUnavailable(old.Id, "withdrawn after diagnostic copy");
        Check(old.Available && registry.CaptureDiagnosticMetadata(2).Items.Single(item => item.Id == old.Id).Available == false,
            "Later withdrawal modified an earlier diagnostic row.");
        Check(complete.Select(item => item.Id).SequenceEqual(new[] { "a", "m", "unregistered", "z" })
            && registry.GetAll().Count == 4 && registry.GetAll().Single(item => item.Id == "unregistered").Operations.Count == 1,
            "Finite diagnostics changed the complete sorted SDK metadata contract.");
        var zero = registry.CaptureDiagnosticMetadata(0);
        Check(zero.Items.Count == 0 && zero.TruncatedValues == 7, "Zero diagnostic budget inspected values or omitted its truncation.");
        var full = registry.CaptureDiagnosticMetadata(32);
        Check(full.Items.Count == 4 && full.TruncatedValues == 0, "A sufficient diagnostic budget lost a unique declared row.");
        Throws<NotSupportedException>(() => ((IList<RizToolbox.Core.Diagnostics.DiagnosticCapabilityRow>)selected.Items)[0] = old);
        Throws<ArgumentOutOfRangeException>(() => registry.CaptureDiagnosticMetadata(4097));
    }

    private static void Extensions()
    {
        var registry = new ExtensionPointRegistry(new SilentLog());
        foreach (var id in new[] { "z", "a", "m" })
            Check(registry.Register(new ExtensionPoint<string>(id, new(1, 0))).Success, "Fixture extension registration failed.");
        var selected = registry.CaptureDiagnosticDescriptors(1);
        Check(selected.Items.Count == 1 && selected.TruncatedValues == 2,
            "Extension capture ignored its finite row limit.");
        Check(registry.Register(new ExtensionPoint<string>("b", new(1, 0))).Success, "Later extension registration failed.");
        Check(selected.Items.Count == 1 && selected.Items[0].PayloadType == typeof(string)
            && registry.GetAll().Select(item => item.Id).SequenceEqual(new[] { "a", "b", "m", "z" }),
            "Later extension registration changed an old copy or the complete SDK query.");
        Throws<NotSupportedException>(() => ((IList<ExtensionPointDescriptor>)selected.Items)[0] = selected.Items[0]);
        Throws<ArgumentOutOfRangeException>(() => registry.CaptureDiagnosticDescriptors(-1));
    }

    private static void Plugins()
    {
        using var main = new MainThreadDispatcher();
        var log = new SilentLog();
        var registry = new CapabilityRegistry(new("offline", "offline", "win-x64", "offline", true));
        using var host = new PluginHost(registry, main, new EventHub(log), log);
        var first = new SelectionPlugin("z");
        var second = new SelectionPlugin("a");
        var third = new SelectionPlugin("m");
        foreach (var plugin in new[] { first, second, third })
        {
            Check(host.Register(plugin).Success && host.Enable(plugin.Manifest.Id).Success, "Selection plugin did not activate.");
        }
        var selected = host.CaptureDiagnosticStatuses(2);
        Check(selected.Items.Count == 2 && selected.TruncatedValues == 1 && host.Statuses.Count == 3,
            "Plugin status budget changed the full catalog.");
        first.Context!.CreateScope("first").CreateScope("nested");
        first.Context.CreateScope("second");
        var complete = host.GetResourceCounts();
        Check(complete.Sum(item => item.Scopes) == 6, "Real nested scopes were not present in the complete query.");
        var bounded = host.CaptureDiagnosticResourceCounts(3, 2);
        Check(bounded.Items.Count == 3 && bounded.Items.Sum(item => item.Scopes) == 2 && bounded.TruncatedValues == 3,
            "Shared scope-node budget traversed beyond its allowance or concealed incomplete counts.");
        var zero = host.CaptureDiagnosticResourceCounts(1, 0);
        Check(zero.Items.Count == 1 && zero.Items[0].Scopes == 0 && zero.TruncatedValues == 3,
            "Zero scope budget read a live subtree or lost omitted plugins.");
        Check(host.GetResourceCounts().SequenceEqual(complete)
            && !first.Context.Lifetime.IsCancellationRequested && !second.Context!.Lifetime.IsCancellationRequested,
            "Diagnostic scope traversal released resources or changed actual ownership.");
        var old = selected.Items.Single(item => item.Id == first.Manifest.Id);
        Check(host.Disable(first.Manifest.Id).Success && old.State == PluginState.Enabled
            && host.Statuses.Single(item => item.Id == first.Manifest.Id).State == PluginState.Disabled,
            "Stopping a plugin changed the earlier selected status copy.");
        Throws<ArgumentOutOfRangeException>(() => host.CaptureDiagnosticResourceCounts(3, -1));
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private sealed class SilentLog : ILogger
    {
        public void Log(LogLevel level, string message, Exception? exception = null) { }
    }
    private sealed class SelectionPlugin : IRizPlugin
    {
        public PluginManifest Manifest { get; }
        public IScopedPluginContext? Context;
        internal SelectionPlugin(string id) => Manifest = new(id, new(1, 0), new(1, 0));
        public void Enable(IPluginContext context) => Context = (IScopedPluginContext)context;
        public void Disable() { }
    }
}
