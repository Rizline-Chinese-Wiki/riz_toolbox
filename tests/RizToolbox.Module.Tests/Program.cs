using RizToolbox.Adapter.Modules;
using RizToolbox.Adapter;
using RizToolbox.Core;
using RizToolbox.Sdk;

if (args.Length == 1 && args[0] == "--local-admission-dependency-checks")
    return LocalAdmissionDependencyChecks.Run();

var checks = new (string Name, Action Run)[] {
    ("Registration declares unavailable rather than an empty ready service", () => {
        using var f = new Fixture(); f.Add("catalog");
        Check(!f.Registry.GetCapability("catalog.cap").Available);
        Equal(AdapterModuleState.Waiting, f.Status("catalog").State);
    }),
    ("Successful probe registers actual service and build/module diagnostics", () => {
        using var f = new Fixture(); f.Add("a"); f.Manager.Tick(0);
        Check(f.Registry.GetService<IProbeService>("a.cap").Service!.Value() == "a");
        var report = f.Status("a"); Equal("profile-test", report.ProfileId); Equal(new string('a', 64), report.BuildFingerprint);
        Equal(AdapterModuleState.Active, report.State); Equal(AdapterVerificationTier.RuntimeProbed, report.VerificationTier);
        Equal(new Version(1, 0), report.Manifest.Version); Check(report.Symbols.Single().Available);
    }),
    ("Missing optional symbol disables only its owning module", () => {
        using var f = new Fixture(); f.Add("broken"); f.Add("healthy");
        f.Resolver.Missing.Add("broken.symbol"); f.Manager.Tick(0);
        Equal(AdapterModuleState.Unsupported, f.Status("broken").State);
        Check(!f.Registry.GetCapability("broken.cap").Available && f.Registry.GetCapability("healthy.cap").Available);
    }),
    ("Ambiguous signature is a permanent per-module failure", () => {
        using var f = new Fixture(); var a = f.Add("a"); a.OnProbe = () => throw new System.Reflection.AmbiguousMatchException("two methods");
        f.Manager.Tick(0); f.Manager.Tick(100); f.Manager.NotifySceneChanged(); f.Manager.Tick(101);
        Equal(1, a.Probes); Equal(AdapterModuleState.Unsupported, f.Status("a").State);
        Check(!f.Manager.RequestProbe("a"));
    }),
    ("Late objects retry with backoff and become available", () => {
        using var f = new Fixture(); var a = f.Add("a"); var ready = false;
        a.OnProbe = () => ready ? ModuleProbe.Available : ModuleProbe.Waiting("loading");
        f.Manager.Tick(0); f.Manager.Tick(.1); Equal(1, a.Probes); Check(!f.Registry.GetCapability("a.cap").Available);
        ready = true; f.Manager.Tick(1); Equal(2, a.Probes); Check(f.Registry.GetCapability("a.cap").Available);
    }),
    ("An epoch bounds temporary retries and a scene change re-arms it", () => {
        using var f = new Fixture(); var a = f.Add("a"); a.OnProbe = () => ModuleProbe.Waiting("no object");
        for (var i = 0; i < 40; i++) f.Manager.Tick(i * 31);
        Equal(ModuleManager.MaximumAutomaticAttempts, a.Probes); Equal(AdapterModuleState.Suspended, f.Status("a").State);
        f.Manager.NotifySceneChanged(); a.OnProbe = () => ModuleProbe.Available; f.Manager.Tick(1250);
        Check(f.Registry.GetCapability("a.cap").Available);
    }),
    ("Unknown or active module cannot be force-probed", () => {
        using var f = new Fixture(); f.Add("a"); f.Manager.Tick(0);
        Check(!f.Manager.RequestProbe("unknown") && !f.Manager.RequestProbe("a"));
    }),
    ("Flapping activation without a healthy tick exhausts its bounded retry epoch", () => {
        using var f = new Fixture(); var a = f.Add("a"); a.OnTick = () => ModuleProbe.Waiting("fails after construction");
        for (var i = 0; i < 40; i++) f.Manager.Tick(i * 31);
        Equal(AdapterModuleState.Suspended, f.Status("a").State);
        Equal(ModuleManager.MaximumAutomaticAttempts, f.Status("a").ConsecutiveFailures);
        Check(f.Manager.RequestProbe("a")); a.OnTick = () => ModuleProbe.Available; f.Manager.Tick(1250); f.Manager.Tick(1252);
        Equal(0, f.Status("a").ConsecutiveFailures);
    }),
    ("Probe work has a fixed per-tick budget", () => {
        using var f = new Fixture(); for (var i = 0; i < 7; i++) f.Add("m" + i);
        f.Manager.Tick(0); Equal(4, f.Manager.GetModules().Count(x => x.State == AdapterModuleState.Active));
        f.Manager.Tick(.01); Equal(7, f.Manager.GetModules().Count(x => x.State == AdapterModuleState.Active));
    }),
    ("Module dependencies wait without spending probe attempts", () => {
        using var f = new Fixture(); var a = f.Add("a", dependencies: new[] { "upstream" }); f.Manager.Tick(0);
        Equal(0, a.Probes); Check(f.Status("a").Failure!.Contains("upstream"));
        f.Registry.Register<IProbeService>("upstream", new ProbeService("source")); f.Manager.Tick(.1);
        Check(f.Registry.GetCapability("a.cap").Available);
    }),
    ("Dependency withdrawal stops downstream modules before provider cleanup", () => {
        using var f = new Fixture(); var order = new List<string>();
        var source = f.Add("source"); var dependent = f.Add("dependent", dependencies: new[] { "source.cap" });
        source.OnRelease = () => order.Add("source"); dependent.OnRelease = () => order.Add("dependent");
        f.Manager.Tick(0); source.OnTick = () => ModuleProbe.Waiting("gone"); f.Manager.Tick(1);
        Check(!f.Registry.GetCapability("source.cap").Available && !f.Registry.GetCapability("dependent.cap").Available);
        Check(order.SequenceEqual(new[] { "dependent", "source" }));
    }),
    ("Provider replacement invalidates a dependent module generation", () => {
        using var f = new Fixture(); f.Registry.Register<IProbeService>("upstream", new ProbeService("old"));
        f.Add("a", dependencies: new[] { "upstream" }); f.Manager.Tick(0);
        var before = f.Registry.Describe("a.cap").Value!.Generation;
        f.Registry.SetUnavailable("upstream", "replace"); f.Registry.Register<IProbeService>("upstream", new ProbeService("new"));
        f.Manager.Tick(.1); Check(f.Registry.Describe("a.cap").Value!.Generation > before);
    }),
    ("Recovery creates a fresh capability generation", () => {
        using var f = new Fixture(); var a = f.Add("a"); f.Manager.Tick(0);
        var before = f.Registry.Describe("a.cap").Value!.Generation;
        a.OnTick = () => ModuleProbe.Waiting("scene unloaded"); f.Manager.Tick(1);
        Check(!f.Registry.GetCapability("a.cap").Available);
        a.OnTick = () => ModuleProbe.Available; f.Manager.Tick(2);
        Check(f.Registry.Describe("a.cap").Value!.Generation > before);
    }),
    ("Capabilities withdraw before native object release", () => {
        using var f = new Fixture(); var a = f.Add("a"); bool? availableAtRelease = null;
        a.OnRelease = () => availableAtRelease = f.Registry.GetCapability("a.cap").Available;
        f.Manager.Tick(0); a.OnTick = () => ModuleProbe.Waiting("unload"); f.Manager.Tick(1);
        Equal(false, availableAtRelease);
    }),
    ("Partial module registration is rolled back", () => {
        using var f = new Fixture(); var a = f.Add("a", capabilities: new[] { "a.cap", "a.extra" });
        a.OnRegister = r => { r.Register<IProbeService>("a.cap", new ProbeService("partial")); throw new InvalidOperationException("register failed"); };
        f.Manager.Tick(0); Check(!f.Registry.GetCapability("a.cap").Available && !f.Registry.GetCapability("a.extra").Available);
        Equal(AdapterModuleState.Waiting, f.Status("a").State);
    }),
    ("Module cannot register an undeclared capability", () => {
        using var f = new Fixture(); var a = f.Add("a"); a.OnRegister = r => r.Register<IProbeService>("other", new ProbeService("x"));
        f.Manager.Tick(0); Check(!f.Registry.GetCapability("other").Available && !f.Registry.GetCapability("a.cap").Available);
    }),
    ("Incomplete successful registration is rejected", () => {
        using var f = new Fixture(); var a = f.Add("a", capabilities: new[] { "a.cap", "a.extra" });
        a.OnRegister = r => r.Register<IProbeService>("a.cap", new ProbeService("partial"));
        f.Manager.Tick(0); Check(!f.Registry.GetCapability("a.cap").Available);
        Check(f.Status("a").Failure!.Contains("every declared"));
    }),
    ("Fault and cleanup withdraw related capabilities atomically even when a listener and logger fail", () => {
        using var f = new Fixture();
        var failing = f.Add("faulting-source", capabilities: new[] { "faulting-source.cap", "faulting-source.second" });
        var dependent = f.Add("faulting-dependent", dependencies: new[] { "faulting-source.second" });
        var peer = f.Add("maintenance-peer");
        f.Log.FailWrites = true;
        f.Registry.Changed += () => throw new IOException("capability notification");
        var observedWithdrawals = 0;
        var partialWithdrawal = false;
        var checking = false;
        f.Registry.Changed += () =>
        {
            if (!checking) return;
            observedWithdrawals++;
            partialWithdrawal |= f.Registry.GetCapability("faulting-source.cap").Available
                || f.Registry.GetCapability("faulting-source.second").Available
                || f.Registry.GetCapability("faulting-dependent.cap").Available;
        };
        f.Manager.Tick(0);
        Check(f.Registry.GetCapability("faulting-dependent.cap").Available && f.Registry.GetCapability("maintenance-peer.cap").Available);
        checking = true;
        var sequence = new List<string>();
        dependent.OnRelease = () => sequence.Add("dependent");
        failing.OnRelease = () => { sequence.Add("source"); throw new IOException("unconfirmed cleanup"); };
        failing.OnTick = () => throw new IOException("managed tick fault");
        peer.OnTick = () => { sequence.Add("peer"); return ModuleProbe.Available; };
        f.Manager.Tick(1);
        checking = false;
        Check(observedWithdrawals > 0 && !partialWithdrawal);
        Check(sequence.SequenceEqual(new[] { "dependent", "source", "peer" }));
        Equal(AdapterModuleState.Unsupported, f.Status("faulting-source").State);
        Check(f.Status("faulting-source").Failure!.Contains("process restart", StringComparison.OrdinalIgnoreCase));
        Check(!f.Manager.RequestProbe("faulting-source"));
        f.Manager.NotifySceneChanged(); f.Manager.Tick(31);
        Equal(1, failing.Probes); Equal(1, failing.Releases);
        f.Manager.Dispose(); Equal(1, failing.Releases);
    }),
    ("Unconfirmed dependent cleanup retains the required provider and allows independent release", () => {
        using var f = new Fixture();
        var provider = f.Add("retained-provider");
        var child = f.Add("retained-child", dependencies: new[] { "retained-provider.cap" });
        var peer = f.Add("independent-release");
        f.Manager.Tick(0);
        child.OnRelease = () => throw new IOException("opaque dependent release");
        provider.OnTick = () => ModuleProbe.Waiting("provider unavailable");
        f.Manager.Tick(1);
        Equal(0, provider.Releases); Equal(1, child.Releases);
        Check(!f.Registry.GetCapability("retained-provider.cap").Available && !f.Registry.GetCapability("retained-child.cap").Available);
        Check(f.Status("retained-provider").Failure!.Contains("retained-child"));
        f.Manager.Dispose();
        Equal(0, provider.Releases); Equal(1, child.Releases); Equal(1, peer.Releases);
    }),
    ("An out-of-band phase fault retires its active module while independent delivery continues", () => {
        using var f = new Fixture(); var failed = f.Add("boundary-owner"); var peer = f.Add("boundary-peer");
        f.Manager.Tick(0); f.Log.FailWrites = true;
        var phaseOk = AdapterTickPhase.Run(f.Log, "test-boundary", failed, _ => throw new IOException("boundary work"),
            (module, error) => f.Manager.ReportFault(module, error));
        Check(!phaseOk && !f.Registry.GetCapability("boundary-owner.cap").Available);
        Check(f.Registry.GetCapability("boundary-peer.cap").Available); Equal(1, failed.Releases); Equal(0, peer.Releases);
        var delivered = 0;
        var hub = new EventHub(f.Log);
        using var faulty = hub.Subscribe<int>(_ => throw new IOException("subscriber"));
        using var good = hub.Subscribe<int>(_ => delivered++);
        hub.Publish(1); hub.Publish(2);
        Equal(2, delivered);
    }),
    ("Module IDs and capability owners must be unique", () => {
        using var f = new Fixture(); f.Add("a");
        Throws<InvalidOperationException>(() => f.Add("a"));
        Throws<InvalidOperationException>(() => f.Add("b", capabilities: new[] { "a.cap" }));
    }),
    ("Module manifests are frozen against mutable input lists", () => {
        using var f = new Fixture(); var capabilities = new List<string> { "a.cap" }; f.Add("a", capabilities: capabilities);
        capabilities[0] = "changed"; Equal("a.cap", f.Status("a").Manifest.Capabilities.Single());
    }),
    ("Dependency cycles are rejected before changing ownership", () => {
        using var f = new Fixture(); f.Add("a", dependencies: new[] { "b.cap" });
        Throws<InvalidOperationException>(() => f.Add("b", dependencies: new[] { "a.cap" }));
        Equal(1, f.Manager.GetModules().Count);
    }),
    ("An external ready capability cannot be taken over by a module", () => {
        using var f = new Fixture(); f.Registry.Register<IProbeService>("a.cap", new ProbeService("external"));
        Throws<InvalidOperationException>(() => f.Add("a"));
        Equal("external", f.Registry.GetService<IProbeService>("a.cap").Service!.Value());
    }),
    ("Invalid monotonic time and wrong thread are refused", () => {
        using var f = new Fixture(); f.Add("a"); f.Manager.Tick(1);
        Throws<ArgumentOutOfRangeException>(() => f.Manager.Tick(.5));
        Throws<ArgumentOutOfRangeException>(() => f.Manager.Tick(double.NaN));
        Task.Run(() => Throws<InvalidOperationException>(() => f.Manager.GetModules())).GetAwaiter().GetResult();
    }),
    ("Dispose withdraws capabilities and rejects later work", () => {
        var f = new Fixture(); var a = f.Add("a"); f.Manager.Tick(0);
        f.Manager.Dispose(); Equal(AdapterModuleState.Disposed, f.Status("a").State);
        Check(!f.Registry.GetCapability("a.cap").Available); Equal(1, a.Releases);
        f.Manager.Dispose(); Equal(1, a.Releases); Throws<ObjectDisposedException>(() => f.Manager.Tick(1));
    }),
    ("Old plugin service views fail after module withdrawal and recovery", () => {
        using var f = new Fixture(); var a = f.Add("a"); var main = new MainThreadDispatcher();
        using var host = new PluginHost(f.Registry, main, new EventHub(f.Log), f.Log);
        var plugin = new ProbePlugin(); host.Register(plugin); host.Enable("test.consumer");
        f.Manager.Tick(0); var old = plugin.Service!; Equal("a", old.Value());
        a.OnTick = () => ModuleProbe.Waiting("object gone"); f.Manager.Tick(1);
        Throws<ObjectDisposedException>(() => old.Value());
        a.OnTick = () => ModuleProbe.Available; f.Manager.Tick(2);
        Check(plugin.Enabled == 2 && plugin.Service!.Value() == "a"); Throws<ObjectDisposedException>(() => old.Value());
    }),
    ("Scoped module factories receive the real plugin identity and invalidate views on recovery", () => {
        using var f = new Fixture(); var a = f.Add("a");
        a.OnRegister = registration => registration.RegisterScoped<IProbeService>("a.cap", context => new ProbeService(context.PluginId));
        var main = new MainThreadDispatcher(); using var host = new PluginHost(f.Registry, main, new EventHub(f.Log), f.Log);
        var plugin = new ProbePlugin(); host.Register(plugin); host.Enable("test.consumer");
        f.Manager.Tick(0); var old = plugin.Service!; Equal("test.consumer",old.Value());
        a.OnTick = () => ModuleProbe.Waiting("module paused"); f.Manager.Tick(1);
        Throws<ObjectDisposedException>(()=>old.Value()); a.OnTick=()=>ModuleProbe.Available;f.Manager.Tick(2);
        Equal(2,plugin.Enabled);Equal("test.consumer",plugin.Service!.Value());Throws<ObjectDisposedException>(()=>old.Value());
    })
}.Concat(ModuleDemandChecks.Cases).Concat(GameAppearanceModuleChecks.Cases).Concat(GamePostProcessingModuleChecks.Cases).Concat(BuiltinManifestContractChecks.Cases).Concat(LocalAdmissionDependencyChecks.Cases).ToArray();
int failures = 0;
foreach (var test in checks) try { test.Run(); Console.WriteLine("PASS " + test.Name); }
catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + test.Name + ": " + ex.Message); }
Console.WriteLine($"Adapter module checks: {checks.Length - failures}/{checks.Length} passed.");
return failures == 0 ? 0 : 1;

static void Check(bool value) { if (!value) throw new InvalidOperationException("Assertion failed."); }
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}."); }
static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }

public interface IProbeService { string Value(); }
sealed record ProbeService(string Id) : IProbeService { public string Value() => Id; }
sealed class ProbePlugin : IRizPlugin
{
    public PluginManifest Manifest => new("test.consumer", new(1, 0), new(1, 0), RequiredCapabilities: new[] { "a.cap" });
    public IProbeService? Service; public int Enabled;
    public void Enable(IPluginContext context) { Service = context.GetService<IProbeService>("a.cap").Service; Enabled++; }
    public void Disable() { }
}
sealed class Logger : ILogger
{
    internal bool FailWrites;
    public void Log(LogLevel level, string message, Exception? exception = null)
    { if (FailWrites) throw new IOException("controlled diagnostic sink failure"); }
}
sealed class Resolver : IModuleSymbolResolver
{
    internal HashSet<string> Missing = new();
    internal List<string> Probed = new();
    public AdapterSymbolProbe Probe(AdapterSymbolRequirement symbol)
    {
        Probed.Add(symbol.Id);
        return new(symbol.Id, !Missing.Contains(symbol.Id), "static Type.Method(System.Int32)", Missing.Contains(symbol.Id) ? "symbol absent" : null);
    }
}
sealed class Module : IAdapterModule
{
    public AdapterModuleManifest Manifest { get; }
    public double IntervalSeconds => 1;
    internal int Probes, Releases;
    internal Func<ModuleProbe> OnProbe = () => ModuleProbe.Available;
    internal Func<ModuleProbe> OnTick = () => ModuleProbe.Available;
    internal Action<ModuleRegistration>? OnRegister;
    internal Action? OnRelease;
    internal Module(string id, IReadOnlyList<string>? capabilities, IReadOnlyList<string>? dependencies, IReadOnlyList<string>? scenes)
        => Manifest = new(id, new(1, 0), capabilities ?? new[] { id + ".cap" }, dependencies ?? Array.Empty<string>(), scenes ?? new[] { "*" },
            new[] { new AdapterSymbolRequirement(id + ".symbol", "test.dll", "", "Type") }, "Offline controlled module lifecycle test.");
    public ModuleProbe Probe() { Probes++; return OnProbe(); }
    public ModuleProbe Tick() => OnTick();
    public void Register(ModuleRegistration registration)
    {
        if (OnRegister is not null) { OnRegister(registration); return; }
        foreach (var id in Manifest.Capabilities) registration.Register<IProbeService>(id, new ProbeService(Manifest.Id));
    }
    public void Release() { Releases++; OnRelease?.Invoke(); }
}
sealed class Fixture : IDisposable
{
    internal CapabilityRegistry Registry;
    internal Resolver Resolver = new(); internal Logger Log = new(); internal ModuleManager Manager;
    internal Fixture()
    {
        Registry = new(new("test", "test", "test", new string('a', 64), true), Log);
        Manager = new(Registry, Resolver, Log, "profile-test", new string('a', 64));
    }
    internal Module Add(string id, IReadOnlyList<string>? capabilities = null, IReadOnlyList<string>? dependencies = null,
        bool onDemand = false, IReadOnlyList<string>? scenes = null)
    { var m = new Module(id, capabilities, dependencies, scenes); Manager.Add(m, onDemand); return m; }
    internal AdapterModuleStatus Status(string id) => Manager.GetModules().Single(x => x.Manifest.Id == id);
    public void Dispose() => Manager.Dispose();
}
