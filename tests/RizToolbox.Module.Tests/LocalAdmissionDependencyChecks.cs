using RizToolbox.Adapter;
using RizToolbox.Adapter.AudioContent;
using RizToolbox.Adapter.Judgements;
using RizToolbox.Adapter.Modules;
using RizToolbox.Core;
using RizToolbox.Sdk;

internal static class LocalAdmissionDependencyChecks
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Production LOCAL manifest requires the real native judgement provider", ManifestContract),
        ("Empty mods activate native judgement before LOCAL composition in production registration order", EmptyMods),
        ("A background-only consumer preserves the native judgement prerequisite without playback demand", BackgroundOnly),
        ("An always-on LOCAL owner wakes its native judgement dependency even if the provider is demand managed", DependencyDemand),
        ("A waiting native judgement provider retries before any LOCAL probe or retained responsibility", ProviderWaiting),
        ("An absent native judgement provider leaves LOCAL unprobed without spending retries", ProviderAbsent),
        ("An unsupported native judgement provider cannot admit or initialize LOCAL", ProviderUnsupported),
        ("Judgement withdrawal revokes LOCAL before releasing the native provider", ProviderWithdrawal),
        ("A genuine partial LOCAL composition failure retains strict no-retry cleanup behavior", CompositionFailure)
    };

    internal static int Run()
    {
        var failures = 0;
        foreach (var test in Cases)
            try { test.Run(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception error) { failures++; Console.Error.WriteLine("FAIL " + test.Name + ": " + error.Message); }
        Console.WriteLine($"{Cases.Length - failures}/{Cases.Length} LOCAL admission dependency checks passed.");
        return failures == 0 ? 0 : 1;
    }

    private static void ManifestContract()
    {
        using var f = new AdmissionFixture();
        Check(f.Local.Manifest.RequiredCapabilities.Contains(JudgementCapabilityIds.Observe, StringComparer.Ordinal),
            "LOCAL must declare the existing judgement module whose Probe installs native outer/inner/Hold activity accounting.");
        Check(f.Judge.Manifest.Capabilities.Contains(JudgementCapabilityIds.Observe, StringComparer.Ordinal), "The required provider must be the production judgement manifest.");
        Check(!f.LocalOnDemand && !f.JudgeOnDemand, "Both production owners must remain eligible without plugin demand.");
    }

    private static void EmptyMods()
    {
        using var f = new AdmissionFixture();
        using var plugins = new PluginHost(f.Core.Registry, f.Main, new EventHub(f.Core.Log), f.Core.Log);
        Check(plugins.GetRequiredCapabilityDemand().Count == 0, "Empty mods must submit no consumer capability intent.");
        f.Core.Manager.SetCapabilityDemand(plugins.GetRequiredCapabilityDemand());
        AssertOrderedActivation(f);
    }

    private static void BackgroundOnly()
    {
        using var f = new AdmissionFixture();
        var service = new BackgroundService();
        f.Core.Registry.Register<IBackgroundPlayService>(BackgroundPlayCapability.Id, service);
        using var plugins = new PluginHost(f.Core.Registry, f.Main, new EventHub(f.Core.Log), f.Core.Log);
        var consumer = new BackgroundConsumer();
        Check(plugins.Register(consumer).Success, "The pure SDK background consumer must register.");
        plugins.Enable(consumer.Manifest.Id);
        Check(consumer.Enabled == 1 && service.Requests == 1, "The background scope must activate without adapter capability requests.");
        var demand = plugins.GetRequiredCapabilityDemand();
        Check(demand.SequenceEqual(new[] { BackgroundPlayCapability.Id }), "Only the host background capability may be requested.");
        f.Core.Manager.SetCapabilityDemand(demand);
        AssertOrderedActivation(f);
    }

    private static void DependencyDemand()
    {
        using var f = new AdmissionFixture(judgeOnDemand: true);
        f.Core.Manager.SetCapabilityDemand(Array.Empty<string>());
        AssertOrderedActivation(f);
        Check(f.Judge.Probes == 1, "The always-on LOCAL root must include its real provider in the demand closure.");
    }

    private static void AssertOrderedActivation(AdmissionFixture f)
    {
        f.Core.Manager.Tick(0);
        Check(f.Local.Probes == 0, "LOCAL composed before the native judgement provider was active.");
        Check(f.Judge.Probes == 1 && f.Core.Registry.GetCapability(JudgementCapabilityIds.Observe).Available,
            "The genuine production provider manifest must be eligible without a judgement consumer plugin.");
        Check(f.Core.Status(f.Local.Manifest.Id).ProbeAttempts == 0, "Dependency waiting must spend no LOCAL probe attempt.");
        f.Core.Manager.Tick(.01);
        Check(f.Order.SequenceEqual(new[] { "judge", "local" }), "Provider must finish before LOCAL initializes.");
        Check(f.Core.Registry.GetCapability(LocalRunProtectionModule.Capability).Available, "LOCAL must activate after dependency publication.");
        Check(f.Unrequested.Probes == 0, "Unrequested optional work must remain dormant.");
    }

    private static void ProviderWaiting()
    {
        using var f = new AdmissionFixture();
        var ready = false;
        f.Judge.OnProbe = () => ready ? ModuleProbe.Available : ModuleProbe.Waiting("controlled native provider is loading");
        f.Core.Manager.Tick(0); f.Core.Manager.Tick(.1);
        Check(f.Local.Probes == 0 && f.Local.Releases == 0, "A loading provider must not create or withdraw a partial LOCAL composition.");
        Check(f.Core.Status(f.Local.Manifest.Id).ProbeAttempts == 0 && f.Core.Status(f.Local.Manifest.Id).State == AdapterModuleState.Waiting,
            "Dependency waiting must leave LOCAL retryable and untouched.");
        ready = true; f.Core.Manager.Tick(1); f.Core.Manager.Tick(1.01);
        Check(f.Judge.Probes == 2 && f.Local.Probes == 1 && f.Local.Releases == 0, "Provider retry must allow the first LOCAL composition only after it becomes ready.");
        Check(f.Core.Registry.GetCapability(LocalRunProtectionModule.Capability).Available, "LOCAL must recover through ordinary ticks.");
    }

    private static void ProviderAbsent()
    {
        using var f = new AdmissionFixture(includeJudge: false);
        f.Core.Manager.Tick(0); f.Core.Manager.Tick(100); f.Core.Manager.NotifySceneChanged(); f.Core.Manager.Tick(101);
        Check(f.Local.Probes == 0 && f.Local.Releases == 0, "A missing provider must never initialize LOCAL.");
        var status = f.Core.Status(f.Local.Manifest.Id);
        Check(status.ProbeAttempts == 0 && status.State == AdapterModuleState.Waiting && status.Failure!.Contains(JudgementCapabilityIds.Observe, StringComparison.Ordinal),
            "Missing-provider diagnostics must identify the real prerequisite while preserving LOCAL retries.");
    }

    private static void ProviderUnsupported()
    {
        using var f = new AdmissionFixture();
        f.Judge.OnProbe = () => ModuleProbe.Unsupported("controlled native helper validation failed");
        f.Core.Manager.Tick(0); f.Core.Manager.Tick(100); f.Core.Manager.NotifySceneChanged(); f.Core.Manager.Tick(101);
        Check(f.Judge.Probes == 1 && f.Core.Status(f.Judge.Manifest.Id).State == AdapterModuleState.Unsupported, "Permanent provider validation failure must stay permanent.");
        Check(f.Local.Probes == 0 && f.Local.Releases == 0 && !f.Core.Registry.GetCapability(LocalRunProtectionModule.Capability).Available,
            "An unsupported real provider must not be replaced by fake readiness or partial LOCAL hooks.");
    }

    private static void ProviderWithdrawal()
    {
        using var f = new AdmissionFixture();
        f.Core.Manager.Tick(0); f.Core.Manager.Tick(.01);
        f.Local.OnRelease = () => f.Order.Add("release-local");
        f.Judge.OnRelease = () => f.Order.Add("release-judge");
        f.Judge.OnTick = () => ModuleProbe.Waiting("controlled provider withdrawn");
        f.Core.Manager.Tick(1);
        Check(f.Order.TakeLast(2).SequenceEqual(new[] { "release-local", "release-judge" }), "LOCAL must release its dependency before native judge cleanup.");
        Check(!f.Core.Registry.GetCapability(LocalRunProtectionModule.Capability).Available && !f.Core.Registry.GetCapability(JudgementCapabilityIds.Observe).Available,
            "Both service generations must withdraw before cleanup.");
    }

    private static void CompositionFailure()
    {
        using var f = new AdmissionFixture();
        f.Local.OnProbe = () => throw new IOException("controlled genuine partial composition failure");
        f.Local.OnRelease = () => throw new IOException("controlled retained native responsibility");
        f.Core.Manager.Tick(0); f.Core.Manager.Tick(.01);
        f.Core.Manager.NotifySceneChanged(); f.Core.Manager.Tick(100);
        Check(f.Local.Probes == 1 && f.Local.Releases == 1 && f.Core.Status(f.Local.Manifest.Id).State == AdapterModuleState.Unsupported,
            "A real composition/cleanup failure must retain the original permanent no-retry behavior.");
        Check(!f.Core.Manager.RequestProbe(f.Local.Manifest.Id) && !f.Core.Registry.GetCapability(LocalRunProtectionModule.Capability).Available,
            "No service or manual retry may bypass retained unknown responsibility.");
    }

    // Scheduling projection only: actual production manifests and demand policy, with
    // controlled Probe/Register bodies. Native game validation is never impersonated.
    private sealed class AdmissionFixture : IDisposable
    {
        internal readonly Fixture Core = new();
        internal readonly MainThreadDispatcher Main = new();
        internal readonly List<string> Order = new();
        internal readonly ManifestModule Local, Judge;
        internal readonly Module Unrequested;
        internal readonly bool LocalOnDemand, JudgeOnDemand;
        internal AdmissionFixture(bool includeJudge = true, bool? judgeOnDemand = null)
        {
            var profile = new AdapterBuildProfile("local-admission-fixture", new string('a', 64), "fixture", "fixture", "fixture", "fixture", new Dictionary<string, TypeBinding>());
            var state = new GameEventState();
            var local = new LocalRunProtectionModule(null!, Main, Core.Log, profile, "unused-dobby.dll");
            var judge = new JudgementModule(null!, Main, Core.Log, profile, state, new EventHub(Core.Log), "unused-dobby.dll", "unused-context.dll");
            Local = new(local.Manifest, local.IntervalSeconds, () => Order.Add("local"));
            Judge = new(judge.Manifest, judge.IntervalSeconds, () => Order.Add("judge"));
            LocalOnDemand = RizlineAdapter.IsDemandManaged(local);
            JudgeOnDemand = RizlineAdapter.IsDemandManaged(judge);
            Core.Registry.Register<IGameLifecycleService>(GameLifecycleCapabilityIds.Lifecycle, state);
            // The real adapter registers LOCAL before JudgementModule.
            Core.Manager.Add(Local, LocalOnDemand);
            if (includeJudge) Core.Manager.Add(Judge, judgeOnDemand ?? JudgeOnDemand);
            Unrequested = Core.Add("unrequested.playback", onDemand: true);
        }
        public void Dispose() { Core.Dispose(); Main.Dispose(); }
    }

    private sealed class ManifestModule : IAdapterModule
    {
        public AdapterModuleManifest Manifest { get; }
        public double IntervalSeconds { get; }
        private readonly Action _beforeProbe;
        internal int Probes, Releases;
        internal Func<ModuleProbe> OnProbe = () => ModuleProbe.Available;
        internal Func<ModuleProbe> OnTick = () => ModuleProbe.Available;
        internal Action? OnRelease;
        internal ManifestModule(AdapterModuleManifest manifest, double interval, Action beforeProbe)
            => (Manifest, IntervalSeconds, _beforeProbe) = (manifest, interval, beforeProbe);
        public ModuleProbe Probe() { Probes++; _beforeProbe(); return OnProbe(); }
        public void Register(ModuleRegistration registration)
        {
            foreach (var capability in Manifest.Capabilities)
                registration.Register<IProbeService>(capability, new ProbeService(Manifest.Id));
        }
        public ModuleProbe Tick() => OnTick();
        public void Release() { Releases++; OnRelease?.Invoke(); }
    }

    private sealed class BackgroundService : IBackgroundPlayService
    {
        internal int Requests;
        public Result EnableForScope() { Requests++; return Result.Ok(); }
    }
    private sealed class BackgroundConsumer : IRizPlugin
    {
        public PluginManifest Manifest => new("test.background-only", new(1, 0), new(1, 0), RequiredCapabilities: new[] { BackgroundPlayCapability.Id });
        internal int Enabled;
        public void Enable(IPluginContext context)
        {
            var service = context.GetService<IBackgroundPlayService>(BackgroundPlayCapability.Id);
            Check(service.Available && service.Service!.EnableForScope().Success, "Background service is unavailable.");
            Enabled++;
        }
        public void Disable() { }
    }
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
}
