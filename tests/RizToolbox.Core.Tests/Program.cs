using RizToolbox.Core;
using RizToolbox.Sdk;

var tests = new (string Name, Action Run)[]
{
    ("dependency activation and reverse cascade disable", DependencyLifecycle),
    ("duplicate, SDK, dependency, cycle and capability validation", ManifestValidation),
    ("failed enable rollback, reverse cleanup and successful retry", RollbackAndRetry),
    ("deterministic event order, disposal and re-enable", EventOrderAndReenable),
    ("subscriber fault stops owner and dependent without blocking unrelated delivery", SubscriberFault),
    ("scoped queued callback cancelled on disable", ScopedDispatchCancellation),
    ("dispatcher queue bounds, pump thread, cancellation and callback faults", DispatcherBehavior),
    ("typed capability lookup and explicit unsupported reasons", TypedCapabilities),
    ("dependency version bounds and manifest snapshots", DependencyVersionAndSnapshot),
    ("dispatched callback fault and cleanup exception isolation", DispatchFaultAndCleanup),
    ("late capability activates arbitrary plugin and withdrawal stops dependents first", CapabilityLifecycle),
    ("explicit disable survives future capability changes and dependency requests", DisabledIntent),
    ("callback faults are not retried by reconcile or enable all", FaultRetryIntent),
    ("services automatically own handles, namespace panels and reject stale use", AutomaticOwnership),
    ("optional service withdrawal invalidates cached view without stopping plugin", OptionalServiceWithdrawal),
    ("scoped factories cache per enable and clean up on stop", ScopedFactoryLifecycle),
    ("configuration defaults, isolation, atomic replacement and validation", ConfigurationLifecycle),
    ("configuration serialization cannot overwrite a replacement activation", ConfigurationSaveExpiry),
    ("configuration workers return WrongThread while the owner is serializing", ConfigurationWorkerDuringSave),
    ("configuration defaults expire with their activation", ConfigurationDefaultsExpiry),
    ("typed extension ordering, exact version, withdrawal and owner faults", TypedExtensions),
    ("late dependency registration activates desired waiting plugin", LateDependency),
    ("plugin manifests reject unsafe configuration directory IDs", UnsafePluginIds),
    ("capability withdrawal during enable rolls back into waiting", ReentrantCapabilityWithdrawal),
    ("fault attribution during activation stops subscriber and preserves publisher", ActivationFaultAttribution),
    ("withdrawn optional extension view suppresses stale callbacks", ExtensionCapabilityWithdrawal),
    ("scoped factory reentrancy and explicit disposal do not leak or double dispose", FactoryReentrancyAndDisposal)
};
int failures = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {test.Name}: {ex}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} core checks passed.");
return failures == 0 ? 0 : 1;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Equal<T>(IEnumerable<T> expected, IEnumerable<T> actual, string message)
    => Check(expected.SequenceEqual(actual), message + $"; got [{string.Join(", ", actual)}]");

static PluginManifest Manifest(string id, params string[] dependencies)
    => new(id, new(1, 0, 0), new(1, 0, 0), Dependencies: dependencies.Select(x => new PluginDependency(x, new(1, 0, 0))).ToArray());

static void DependencyLifecycle()
{
    using var f = new Fixture();
    var calls = new List<string>();
    f.Host.Register(new Plugin(Manifest("base"), _ => calls.Add("+base"), () => calls.Add("-base")));
    f.Host.Register(new Plugin(Manifest("middle", "base"), _ => calls.Add("+middle"), () => calls.Add("-middle")));
    f.Host.Register(new Plugin(Manifest("top", "middle"), _ => calls.Add("+top"), () => calls.Add("-top")));
    Check(f.Host.Enable("top").Success, "Dependency chain activation failed.");
    Check(f.Host.Enable("top").Success, "Repeated enable should be idempotent.");
    Check(f.Host.Disable("base").Success, "Dependency stop failed.");
    Equal(new[] { "+base", "+middle", "+top", "-top", "-middle", "-base" }, calls, "Wrong lifecycle order");
    Check(f.Host.Statuses.All(x => x.State is PluginState.Disabled or PluginState.Waiting), "An active dependent survived dependency stop.");
}

static void ManifestValidation()
{
    using var f = new Fixture();
    Check(f.Host.Register(new Plugin(Manifest("valid"))).Success, "Registration failed.");
    Check(!f.Host.Register(new Plugin(Manifest("valid"))).Success, "Duplicate ID accepted.");
    Check(!f.Host.Register(new Plugin(Manifest("Bad ID"))).Success, "Invalid ID accepted.");
    f.Host.Register(new Plugin(Manifest("future") with { MinSdk = new(2, 0, 0) }));
    f.Host.Register(new Plugin(Manifest("old") with { MinSdk = new(0, 1, 0), MaxSdkExclusive = new(1, 0, 0) }));
    f.Host.Register(new Plugin(Manifest("missing", "absent")));
    f.Host.Register(new Plugin(Manifest("cycle-a", "cycle-b")));
    f.Host.Register(new Plugin(Manifest("cycle-b", "cycle-a")));
    f.Capabilities.SetUnavailable(CapabilityIds.Practice, "No verified practice hook in this build.");
    f.Host.Register(new Plugin(Manifest("unsupported") with { RequiredCapabilities = new[] { CapabilityIds.Practice } }));
    foreach (var id in new[] { "future", "old", "missing", "cycle-a", "unsupported", "unknown" })
        Check(!f.Host.Enable(id).Success, $"Invalid plugin '{id}' activated.");
    var unavailable=f.Host.Enable("unsupported").Error!;
    Check(unavailable.Code==ErrorCode.CapabilityUnavailable && unavailable.Message.Contains("No verified practice hook"), "Unavailable capability reason was lost.");
    Check(f.Host.Statuses.All(x => x.State is PluginState.Disabled or PluginState.Waiting), "Invalid plugin activated.");
}

static void RollbackAndRetry()
{
    using var f = new Fixture();
    var cleanup = new List<string>();
    int hits = 0, attempts = 0;
    f.Host.Register(new Plugin(Manifest("base"), context => context.Own(new Cleanup(() => cleanup.Add("base")))));
    f.Host.Register(new Plugin(Manifest("failing", "base"), context =>
    {
        attempts++;
        context.Events.Subscribe<int>(_ => hits++);
        context.Own(new Cleanup(() => cleanup.Add("first")));
        context.Own(new Cleanup(() => cleanup.Add("second")));
        if (attempts == 1) throw new InvalidOperationException("intentional enable failure");
    }));
    Check(!f.Host.Enable("failing").Success, "Failure wasn't surfaced.");
    Equal(new[] { "second", "first", "base" }, cleanup, "Activation rollback cleanup order failed");
    f.Events.Publish(1);
    Check(hits == 0, "Failed activation leaked an event subscription.");
    Check(f.Host.Statuses.Single(x => x.Id == "failing").State == PluginState.Faulted, "Failure state missing.");
    Check(f.Host.Enable("failing").Success, "Faulted plugin cannot retry activation.");
    f.Events.Publish(2);
    Check(hits == 1, "Retry duplicated/leaked event subscriptions.");
}

static void EventOrderAndReenable()
{
    using var f = new Fixture();
    var order = new List<string>();
    IDisposable? disposable = null;
    f.Host.Register(new Plugin(Manifest("ordered"), context =>
    {
        context.Events.Subscribe<int>(_ => order.Add("normal-1"));
        context.Events.Subscribe<int>(_ => order.Add("high"), 10);
        disposable = context.Events.Subscribe<int>(_ => order.Add("normal-2"));
    }));
    f.Host.Enable("ordered");
    f.Events.Publish(1);
    Equal(new[] { "high", "normal-1", "normal-2" }, order, "Event order failed");
    disposable!.Dispose();
    disposable.Dispose();
    order.Clear();
    f.Events.Publish(1);
    Equal(new[] { "high", "normal-1" }, order, "Disposal didn't unsubscribe");
    f.Host.Disable("ordered");
    order.Clear();
    f.Events.Publish(1);
    Check(order.Count == 0, "Disabled plugin still receives events.");
    f.Host.Enable("ordered");
    f.Events.Publish(1);
    Equal(new[] { "high", "normal-1", "normal-2" }, order, "Reenable caused duplicate events");
}

static void SubscriberFault()
{
    using var f = new Fixture();
    int dependentHits = 0, unrelatedHits = 0;
    f.Host.Register(new Plugin(Manifest("broken"), c => c.Events.Subscribe<int>(_ => throw new Exception("callback fault"), 10)));
    f.Host.Register(new Plugin(Manifest("dependent", "broken"), c => c.Events.Subscribe<int>(_ => dependentHits++)));
    f.Host.Register(new Plugin(Manifest("unrelated"), c => c.Events.Subscribe<int>(_ => unrelatedHits++)));
    f.Host.Enable("dependent");
    f.Host.Enable("unrelated");
    f.Events.Publish(1);
    Check(dependentHits == 0 && unrelatedHits == 1, "Fault cleanup interrupted unrelated delivery or kept dependent active.");
    Check(f.Host.Statuses.Single(x => x.Id == "broken").State == PluginState.Faulted, "Owner wasn't faulted.");
    Check(f.Host.Statuses.Single(x => x.Id == "dependent").State == PluginState.Disabled, "Dependent wasn't stopped.");
    Check(f.Log.Errors > 0, "Fault wasn't logged.");
}

static void ScopedDispatchCancellation()
{
    using var f = new Fixture();
    IPluginContext? context = null;
    int count = 0;
    f.Host.Register(new Plugin(Manifest("queued"), c => context = c));
    f.Host.Enable("queued");
    Task? pending = null;
    var worker = new Thread(() => pending = context!.MainThread.InvokeAsync(() => count++));
    worker.Start(); worker.Join();
    Check(f.Dispatcher.PendingCount == 1, "Background callback wasn't queued.");
    f.Host.Disable("queued");
    f.Dispatcher.Pump();
    try { pending!.GetAwaiter().GetResult(); throw new Exception("Expected cancellation."); }
    catch (OperationCanceledException) { }
    Check(count == 0, "Stopped plugin callback still executed.");
}

static void DispatcherBehavior()
{
    using var dispatcher = new MainThreadDispatcher(2);
    int thread = 0;
    Task? first = null, second = null, overflow = null;
    Exception? wrongPump = null;
    using var cancellation = new CancellationTokenSource();
    var worker = new Thread(() =>
    {
        first = dispatcher.InvokeAsync(() => thread = Environment.CurrentManagedThreadId);
        second = dispatcher.InvokeAsync(() => throw new Exception("Should be cancelled"), cancellation.Token);
        overflow = dispatcher.InvokeAsync(() => { });
        try { dispatcher.Pump(); } catch (Exception ex) { wrongPump = ex; }
    });
    worker.Start(); worker.Join();
    Check(wrongPump is InvalidOperationException, "Background pump accepted.");
    Check(overflow!.IsFaulted, "Queue bound wasn't enforced.");
    cancellation.Cancel();
    Check(dispatcher.Pump(1) == 1 && dispatcher.PendingCount == 1, "Pump batch bound failed.");
    first!.GetAwaiter().GetResult();
    Check(thread == Environment.CurrentManagedThreadId, "Callback ran on wrong thread.");
    dispatcher.Pump();
    Check(second!.IsCanceled && dispatcher.PendingCount == 0, "Cancellation or pending count failed.");
    Check(dispatcher.InvokeAsync(() => throw new Exception("fault")).IsFaulted, "Callback exception escaped task.");
    dispatcher.Dispose();
    Check(dispatcher.InvokeAsync(() => { }).IsFaulted, "Disposed queue accepted work.");
}

static void TypedCapabilities()
{
    using var f = new Fixture();
    f.Capabilities.Register<IScoreService>(CapabilityIds.Scores, new Scores());
    var lookup = f.Capabilities.GetService<IScoreService>(CapabilityIds.Scores);
    Check(lookup.Available && lookup.Service!.GetBestScoresSnapshot().Success, "Typed service failed.");
    Check(!f.Capabilities.GetService<Scores>(CapabilityIds.Scores).Available, "Concrete game adapter leaked through declared contract.");
    var unsupported = f.Capabilities.GetService<IResourceService>(CapabilityIds.Resources);
    Check(!unsupported.Available && !string.IsNullOrWhiteSpace(unsupported.Error?.Message), "Missing capability didn't report reason.");
    bool duplicate = false;
    try { f.Capabilities.Register<IScoreService>(CapabilityIds.Scores, new Scores()); }
    catch (InvalidOperationException) { duplicate = true; }
    Check(duplicate, "Duplicate capability replaced running service.");
}

static void DependencyVersionAndSnapshot()
{
    using var f = new Fixture();
    f.Host.Register(new Plugin(Manifest("base")));
    var dependencies = new List<PluginDependency> { new("base", new(1, 0, 0), new(2, 0, 0)) };
    f.Host.Register(new Plugin(Manifest("valid") with { Dependencies = dependencies }));
    dependencies.Clear();
    dependencies.Add(new("missing", new(1, 0, 0)));
    Check(f.Host.Enable("valid").Success, "Mutable manifest changed registered graph.");
    f.Host.Register(new Plugin(Manifest("too-new") with { Dependencies = new[] { new PluginDependency("base", new(2, 0, 0)) } }));
    f.Host.Register(new Plugin(Manifest("too-old") with { Dependencies = new[] { new PluginDependency("base", new(0, 1, 0), new(1, 0, 0)) } }));
    Check(!f.Host.Enable("too-new").Success && !f.Host.Enable("too-old").Success, "Dependency version bounds ignored.");
}

static void DispatchFaultAndCleanup()
{
    using var f = new Fixture();
    IPluginContext? context = null;
    bool cleaned = false;
    f.Host.Register(new Plugin(Manifest("fault"), c =>
    {
        context = c;
        c.Own(new Cleanup(() => cleaned = true));
        c.Own(new Cleanup(() => throw new Exception("cleanup failure")));
    }, () => throw new Exception("disable failure")));
    f.Host.Enable("fault");
    var callback = context!.MainThread.InvokeAsync(() => throw new Exception("dispatch failure"));
    try { callback.GetAwaiter().GetResult(); } catch (Exception) { }
    Check(callback.IsFaulted && cleaned, "Dispatch fault cleanup failed.");
    Check(f.Host.Statuses.Single().State == PluginState.Faulted && f.Log.Errors >= 2, "Fault state/logging missing.");
}

static void CapabilityLifecycle()
{
    using var f = new Fixture();
    var calls = new List<string>();
    f.Host.Register(new Plugin(Manifest("late-base") with { RequiredCapabilities = new[] { CapabilityIds.Scores } },
        _ => calls.Add("+base"), () => calls.Add("-base")));
    f.Host.Register(new Plugin(Manifest("third-party", "late-base"), _ => calls.Add("+top"), () => calls.Add("-top")));
    Check(!f.Host.Enable("third-party").Success, "Missing capability unexpectedly activated plugin.");
    Check(f.Host.Statuses.Single(x => x.Id == "third-party").DesiredEnabled, "Enable intent was discarded.");
    f.Capabilities.Register<IScoreService>(CapabilityIds.Scores, new Scores());
    Equal(new[] { "+base", "+top" }, calls, "Capability readiness did not activate arbitrary plugins");
    f.Capabilities.SetUnavailable(CapabilityIds.Scores, "Scene unloading");
    Equal(new[] { "+base", "+top", "-top", "-base" }, calls, "Withdrawal did not stop dependents first");
    Check(f.Host.Statuses.All(x => x.State == PluginState.Waiting), "Waiting states missing after withdrawal.");
    f.Capabilities.Register<IScoreService>(CapabilityIds.Scores, new Scores());
    Equal(new[] { "+base", "+top", "-top", "-base", "+base", "+top" }, calls, "Capability restoration failed");
}

static void DisabledIntent()
{
    using var f = new Fixture();
    int waitingStarts = 0;
    f.Host.Register(new Plugin(Manifest("waiter") with { RequiredCapabilities = new[] { CapabilityIds.Scores } }, _ => waitingStarts++));
    f.Host.Enable("waiter");
    f.Host.Disable("waiter");
    f.Capabilities.Register<IScoreService>(CapabilityIds.Scores, new Scores());
    Check(waitingStarts == 0 && !f.Host.Statuses.Single().DesiredEnabled, "Explicitly disabled waiter restarted.");
    int baseStarts = 0, topStarts = 0;
    f.Host.Register(new Plugin(Manifest("base"), _ => baseStarts++));
    f.Host.Register(new Plugin(Manifest("top", "base"), _ => topStarts++));
    f.Host.Enable("top");
    f.Host.Disable("base");
    f.Host.Reconcile();
    Check(!f.Host.Enable("top").Success && baseStarts == 1, "Dependent overrode explicitly disabled dependency.");
    Check(f.Host.Enable("base").Success && baseStarts == 2 && topStarts == 2, "Explicit dependency enable did not release waiters.");
    f.Host.Disable("top");
    Check(f.Host.Statuses.Single(x => x.Id == "base").State == PluginState.Enabled, "Stopping dependent stopped explicitly desired dependency.");
}

static void FaultRetryIntent()
{
    using var f = new Fixture();
    int starts = 0;
    f.Host.Register(new Plugin(Manifest("broken"), c => { starts++; c.Events.Subscribe<int>(_ => throw new Exception("fault")); }));
    f.Host.Enable("broken");
    f.Events.Publish(1);
    f.Host.Reconcile();
    f.Capabilities.SetUnavailable("unrelated", "probe");
    f.Host.EnableAll();
    Check(starts == 1 && f.Host.Statuses.Single().State == PluginState.Faulted, "Faulted plugin retried implicitly.");
    Check(f.Host.Enable("broken").Success && starts == 2, "Explicit retry did not start fresh scope.");
}

static void AutomaticOwnership()
{
    using var f = new Fixture();
    var ui = new FakeUi(); var resources = new FakeResources(); var gameResources = new FakeGameResources();
    f.Capabilities.Register<IUiService>(CapabilityIds.Ui, ui);
    f.Capabilities.Register<IResourceService>(CapabilityIds.Resources, resources);
    f.Capabilities.Register<IGameResourceService>(GameCapabilityIds.Resources, gameResources);
    IUiService? cached = null; IPanelHandle? panel = null;
    f.Host.Register(new Plugin(Manifest("first"), c =>
    {
        cached = c.GetService<IUiService>(CapabilityIds.Ui).Service;
        panel = cached!.CreatePanel(new("shared", "First", "text")).Value;
        Check(c.GetService<IResourceService>(CapabilityIds.Resources).Service!.OverrideTexture(new("target", new byte[] { 1 })).Success, "Override failed.");
        Check(c.GetService<IGameResourceService>(GameCapabilityIds.Resources).Service!.OverrideColor(new("target", new(1, 0, 0))).Success, "Color override failed.");
    }));
    f.Host.Register(new Plugin(Manifest("second"), c => c.GetService<IUiService>(CapabilityIds.Ui).Service!.CreatePanel(new("shared", "Second", "text"))));
    f.Host.EnableAll();
    Equal(new[] { "first/shared", "second/shared" }, ui.Created, "Panel names collided");
    Check(panel!.Id == "first/shared", "Panel returned unqualified ID.");
    f.Host.Disable("first");
    Check(ui.Disposed == 1 && resources.Disposed == 1 && gameResources.Disposed == 1, "Resources escaped cleanup when plugin omitted Own.");
    Check(!cached!.CreatePanel(new("late", "Late", "text")).Success && !panel.SetText("stale").Success, "Cached service/handle worked after stop.");
    panel.Dispose(); panel.Dispose();
    Check(ui.Disposed == 1, "Old panel disposal was not idempotent.");
}

static void OptionalServiceWithdrawal()
{
    using var f = new Fixture();
    var originalScores=new Scores();f.Capabilities.Register<IScoreService>(CapabilityIds.Scores, originalScores);
    IScoreService? cached = null; IPluginContext? context = null;
    f.Host.Register(new Plugin(Manifest("optional"), c => { context = c; cached = c.GetService<IScoreService>(CapabilityIds.Scores).Service; }));
    f.Host.Enable("optional");
    Check(cached!.GetBestScoresSnapshot().Success, "Readonly proxy failed.");
    var wrongThread=Task.Run(()=>cached.GetBestScoresSnapshot()).GetAwaiter().GetResult();
    Check(!wrongThread.Success && wrongThread.Error is {Code:ErrorCode.WrongThread,CapabilityId:CapabilityIds.Scores}
        && originalScores.Calls==1, "Readonly service lost main thread guard or invoked its provider on a worker.");
    f.Capabilities.SetUnavailable(CapabilityIds.Scores, "optional off");
    Check(f.Host.Statuses.Single().State == PluginState.Enabled, "Optional capability withdrawal stopped owner.");
    var stale=cached.GetBestScoresSnapshot();
    Check(!stale.Success && stale.Error?.Code==ErrorCode.ScopeExpired && originalScores.Calls==1, "Withdrawn service remained usable.");
    f.Capabilities.Register<IScoreService>(CapabilityIds.Scores, new Scores());
    Check(context!.GetService<IScoreService>(CapabilityIds.Scores).Service!.GetBestScoresSnapshot().Success, "New service generation unavailable.");
    stale=cached.GetBestScoresSnapshot();
    Check(!stale.Success && stale.Error?.Code==ErrorCode.ScopeExpired && originalScores.Calls==1, "Old generation revived after restore.");
}

static void ScopedFactoryLifecycle()
{
    using var f = new Fixture();
    int created = 0, disposed = 0;
    f.Capabilities.RegisterScoped<ICounterService>("counter", c => { created++; return new CounterService(c.PluginId, () => disposed++); });
    ICounterService? cached = null;
    f.Host.Register(new Plugin(Manifest("counter-user"), c =>
    {
        cached = c.GetService<ICounterService>("counter").Service;
        Check(ReferenceEquals(cached, c.GetService<ICounterService>("counter").Service), "Factory not cached per scope.");
        Check(cached!.Name() == "counter-user", "Scoped factory got wrong owner.");
    }));
    Check(!f.Capabilities.GetService<ICounterService>("counter").Available, "Host leaked scoped factory without owner.");
    f.Host.Enable("counter-user"); f.Host.Disable("counter-user");
    Check(created == 1 && disposed == 1, "Scoped factory lifecycle cleanup failed.");
    bool stale = false;
    try { cached!.Name(); } catch (ObjectDisposedException) { stale = true; }
    Check(stale, "Custom cached service survived stop.");
    f.Host.Enable("counter-user");
    Check(created == 2, "Reenable reused old service instance.");
}

static void ConfigurationLifecycle()
{
    using var f = new Fixture();
    IPluginConfiguration? first = null, second = null;
    f.Host.Register(new Plugin(Manifest("config-a"), c => first = c.Configuration));
    f.Host.Register(new Plugin(Manifest("config-b"), c => second = c.Configuration));
    f.Host.EnableAll();
    Check(first!.Load("main", () => new Settings(7)).Value!.Value == 7, "Missing document defaults failed.");
    Check(first.Save("main", new Settings(8)).Success && first.Load("main", () => new Settings(0)).Value!.Value == 8, "Config roundtrip failed.");
    Check(first.Save("main", new Settings(9)).Success && first.Load("main", () => new Settings(0)).Value!.Value == 9, "Atomic replacement failed.");
    Check(second!.Load("main", () => new Settings(3)).Value!.Value == 3, "Config crossed plugin boundary.");
    foreach (var invalid in new[] { "../other", "x/y", "x\\y", "C:main", "", ".", "a.json", "x:y" })
        Check(!first.Save(invalid, new Settings(0)).Success && !first.Load(invalid, () => new Settings(0)).Success, "Invalid config name accepted: " + invalid);
    var path = Path.Combine(f.DataRoot, "config", "plugin-config-a", "config-main.json");
    File.WriteAllText(path, "{broken");
    Check(!first.Load("main", () => new Settings(0)).Success, "Broken JSON silently replaced by defaults.");
    Check(first.Save("main", new Settings(10)).Success, "Could not repair JSON by explicit save.");
    Check(!Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp").Any(), "Atomic write left temporary files.");
    f.Host.Disable("config-a");
    Check(!first.Save("main", new Settings(11)).Success, "Stale configuration changed files.");
}

static void ConfigurationWorkerDuringSave()
{
    using var f = new Fixture();
    IPluginConfiguration? configuration = null;
    f.Host.Register(new Plugin(Manifest("config"), c => configuration = c.Configuration));
    Check(f.Host.Enable("config").Success, "Could not enable configuration fixture.");
    var view = configuration!;
    using var ready = new ManualResetEventSlim();
    using var serializing = new ManualResetEventSlim();
    var worker = Task.Factory.StartNew(() =>
    {
        ready.Set();
        if (!serializing.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Serializer never started.");
        var load = view.Load("same", () => new Settings(42));
        var save = view.Save("same", new Settings(42));
        return (Load: load.Error?.Code, Save: save.Error?.Code);
    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    Check(ready.Wait(TimeSpan.FromSeconds(10)), "Configuration worker did not start.");
    var returnedInsideSave = false;
    var outer = view.Save("same", new ConfigurationValue(() =>
    {
        serializing.Set();
        returnedInsideSave = worker.Wait(TimeSpan.FromSeconds(3));
        return 7;
    }));
    var denied = worker.GetAwaiter().GetResult();
    Check(outer.Success && returnedInsideSave, "A rejected configuration call waited for the owner's serialization lock.");
    Check(denied == (ErrorCode.WrongThread, ErrorCode.WrongThread), "Both off-thread operations must return WrongThread.");
    Check(view.Load("same", () => new Settings(0)).Value?.Value == 7, "The worker changed the owner's document.");
}

static void ConfigurationSaveExpiry()
{
    using var f = new Fixture();
    IPluginConfiguration? configuration = null;
    f.Host.Register(new Plugin(Manifest("config"), c => configuration = c.Configuration));
    Check(f.Host.Enable("config").Success, "Could not enable configuration fixture.");
    var expired = configuration!;
    Check(expired.Save("main", new Settings(1)).Success, "Could not save original configuration.");
    var save = expired.Save("main", new ConfigurationValue(() =>
    {
        Check(f.Host.Disable("config").Success && f.Host.Enable("config").Success, "Could not replace activation.");
        Check(configuration!.Save("main", new Settings(2)).Success, "Replacement activation could not save configuration.");
        return 3;
    }));
    Check(save.Error?.Code == ErrorCode.ScopeExpired, "Expired configuration save was not rejected.");
    Check(configuration!.Load("main", () => new Settings(0)).Value?.Value == 2, "Expired save overwrote replacement data.");
}

static void ConfigurationDefaultsExpiry()
{
    using var f = new Fixture();
    IPluginConfiguration? configuration = null;
    f.Host.Register(new Plugin(Manifest("config"), c => configuration = c.Configuration));
    Check(f.Host.Enable("config").Success, "Could not enable configuration fixture.");
    var load = configuration!.Load("missing", () =>
    {
        Check(f.Host.Disable("config").Success, "Could not disable activation.");
        return new Settings(1);
    });
    Check(load.Error?.Code == ErrorCode.ScopeExpired, "Defaults escaped an expired scope.");
    Check(!Directory.Exists(f.DataRoot), "Loading defaults created a file.");
}

static void TypedExtensions()
{
    using var f = new Fixture();
    var point = new ExtensionPoint<string>("game.target.after", new(1, 0, 0));
    Check(f.Host.Extensions.Register(point, ExtensionPointPhase.After, "test target").Success, "Declaration failed.");
    Check(!f.Host.Extensions.Register(point).Success, "Duplicate declaration accepted.");
    var delivered = new List<string>();
    IExtensionService? cached = null;
    f.Host.Register(new Plugin(Manifest("first"), c =>
    {
        cached = c.GetService<IExtensionService>(CapabilityIds.Extensions).Service;
        Check(cached!.Describe(point.Id).Value!.Phase == ExtensionPointPhase.After && cached.GetAll().Count == 1, "Descriptor unavailable.");
        Check(!cached.Subscribe(new ExtensionPoint<int>(point.Id, point.Version), _ => { }).Success, "Wrong payload accepted.");
        Check(!cached.Subscribe(point with { Version = new(2, 0, 0) }, _ => { }).Success, "Wrong version accepted.");
        Check(!cached.Subscribe(point with { Id = "missing" }, _ => { }).Success, "Unknown target accepted.");
        cached.Subscribe(point, _ => delivered.Add("a"));
        cached.Subscribe(point, _ => delivered.Add("high"), 10);
    }));
    f.Host.Register(new Plugin(Manifest("second"), c => c.GetService<IExtensionService>(CapabilityIds.Extensions).Service!.Subscribe(point, _ => delivered.Add("b"))));
    f.Host.EnableAll();
    f.Host.Extensions.Publish(point, "first");
    Equal(new[] { "high", "a", "b" }, delivered, "Extension dispatch order failed");
    f.Host.Disable("first"); delivered.Clear(); f.Host.Extensions.Publish(point, "second");
    Equal(new[] { "b" }, delivered, "Removal affected other owner or leaked subscription");
    Check(!cached!.Subscribe(point, _ => { }).Success, "Stale extension service accepted subscription.");
    f.Host.Register(new Plugin(Manifest("fault-owner"), c => c.GetService<IExtensionService>(CapabilityIds.Extensions).Service!.Subscribe(point, _ => throw new Exception("extension fault"), 20)));
    f.Host.Enable("fault-owner"); delivered.Clear(); f.Host.Extensions.Publish(point, "third");
    Equal(new[] { "b" }, delivered, "Fault interrupted unrelated extension delivery");
    Check(f.Host.Statuses.Single(x => x.Id == "fault-owner").State == PluginState.Faulted, "Extension exception was not attributed to owner.");
    f.Host.Reconcile(); delivered.Clear(); f.Host.Extensions.Publish(point, "fourth");
    Equal(new[] { "b" }, delivered, "Faulted extension leaked or retried");
}

static void LateDependency()
{
    using var f = new Fixture();
    int starts = 0;
    f.Host.Register(new Plugin(Manifest("waiting", "late"), _ => starts++));
    Check(!f.Host.Enable("waiting").Success, "Missing dependency accepted.");
    f.Host.Register(new Plugin(Manifest("late")));
    Check(starts == 1 && f.Host.Statuses.All(x => x.State == PluginState.Enabled), "New dependency did not reconcile waiting owner.");
}

static void UnsafePluginIds()
{
    using var f = new Fixture();
    foreach (var id in new[] { "../bad", "ends.", "a/b", new string('x', 129) })
        Check(!f.Host.Register(new Plugin(Manifest(id))).Success, "Unsafe manifest ID accepted.");
}

static void ReentrantCapabilityWithdrawal()
{
    using var f = new Fixture();
    f.Capabilities.Register<IScoreService>(CapabilityIds.Scores, new Scores());
    var calls = new List<string>();
    int attempts = 0;
    f.Host.Register(new Plugin(Manifest("base") with { RequiredCapabilities = new[] { CapabilityIds.Scores } }, _ =>
    {
        calls.Add("+base");
        if (++attempts == 1) f.Capabilities.SetUnavailable(CapabilityIds.Scores, "withdrawn during activation");
    }, () => calls.Add("-base")));
    f.Host.Register(new Plugin(Manifest("top", "base"), _ => calls.Add("+top")));
    Check(!f.Host.Enable("top").Success, "Plugin enabled after its capability was withdrawn during activation.");
    Equal(new[] { "+base", "-base" }, calls, "Dependent briefly activated after dependency lost capability");
    Check(f.Host.Statuses.All(x => x.State == PluginState.Waiting), "Reentrant withdrawal incorrectly faulted plugin.");
    f.Capabilities.Register<IScoreService>(CapabilityIds.Scores, new Scores());
    Equal(new[] { "+base", "-base", "+base", "+top" }, calls, "Reentrant rollback did not restore normally");
}

static void ActivationFaultAttribution()
{
    using var f = new Fixture();
    f.Host.Register(new Plugin(Manifest("broken-listener"), c => c.Events.Subscribe<string>(_ => throw new Exception("listener fault"))));
    f.Host.Enable("broken-listener");
    f.Host.Register(new Plugin(Manifest("publisher"), c => c.Events.Publish("activation message")));
    Check(f.Host.Enable("publisher").Success, "Unrelated listener fault was attributed to activation publisher.");
    Check(f.Host.Statuses.Single(x => x.Id == "broken-listener").State == PluginState.Faulted, "Listener was not faulted.");
    Check(f.Host.Statuses.Single(x => x.Id == "publisher").State == PluginState.Enabled, "Publisher was stopped.");
    int ownCleanup = 0;
    f.Host.Register(new Plugin(Manifest("own-fault"), c =>
    {
        c.Own(new Cleanup(() => ownCleanup++));
        c.Events.Subscribe<int>(_ => throw new Exception("own activation callback fault"));
        c.Events.Publish(1);
    }));
    Check(!f.Host.Enable("own-fault").Success && ownCleanup == 1, "Activating owner's own callback fault leaked its enable scope.");
    Check(f.Host.Statuses.Single(x => x.Id == "own-fault").State == PluginState.Faulted, "Own activation fault state missing.");
}

static void ExtensionCapabilityWithdrawal()
{
    using var f = new Fixture();
    var point = new ExtensionPoint<int>("optional.point", new(1, 0, 0));
    f.Host.Extensions.Register(point);
    int calls = 0;
    f.Host.Register(new Plugin(Manifest("optional-extension"), c => c.GetService<IExtensionService>(CapabilityIds.Extensions).Service!.Subscribe(point, _ => calls++)));
    f.Host.Enable("optional-extension");
    f.Host.Extensions.Publish(point, 1);
    f.Capabilities.SetUnavailable(CapabilityIds.Extensions, "adapter detached");
    f.Host.Extensions.Publish(point, 2);
    Check(calls == 1 && f.Host.Statuses.Single().State == PluginState.Enabled, "Revoked optional extension delivered stale callback or faulted its owner.");
}

static void FactoryReentrancyAndDisposal()
{
    using var f = new Fixture();
    int disposed = 0;
    f.Capabilities.RegisterScoped<ICounterService>("counter", c =>
    {
        f.Capabilities.SetUnavailable("counter", "withdrawn inside factory");
        return new CounterService(c.PluginId, () => disposed++);
    });
    IPluginContext? context = null;
    f.Host.Register(new Plugin(Manifest("factory-user"), c => context = c));
    f.Host.Enable("factory-user");
    Check(!context!.GetService<ICounterService>("counter").Available && disposed == 1, "Reentrant withdrawal leaked newly-created scoped service.");
    f.Capabilities.RegisterScoped<ICounterService>("counter", c => new CounterService(c.PluginId, () => disposed++));
    var counter = context.GetService<ICounterService>("counter").Service!;
    counter.Dispose(); counter.Dispose();
    bool stale = false;
    try { counter.Name(); } catch (ObjectDisposedException) { stale = true; }
    Check(stale && disposed == 2, "Explicit factory view disposal was not idempotent or view remained usable.");
    f.Host.Disable("factory-user"); counter.Dispose();
    Check(disposed == 2, "Scope stop double-disposed manually-disposed scoped factory.");
}

sealed record Settings(int Value);

public interface ICounterService : IDisposable { string Name(); }
sealed class CounterService : ICounterService, IDisposable
{
    private readonly string _name; private Action? _dispose;
    public CounterService(string name, Action dispose) { _name = name; _dispose = dispose; }
    public string Name() => _name;
    public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
}

sealed class FakeUi : IUiService
{
    public List<string> Created = new(); public int Disposed;
    public OperationResult<IPanelHandle> CreatePanel(PanelDefinition definition)
    { Created.Add(definition.Id); return OperationResult<IPanelHandle>.Ok(new FakePanel(definition.Id, () => Disposed++)); }
}
sealed class FakePanel : IPanelHandle
{
    private Action? _dispose;
    public FakePanel(string id, Action dispose) { Id = id; _dispose = dispose; }
    public string Id { get; }
    public OperationResult SetText(string text) => _dispose is null ? OperationResult.Fail(ErrorCode.ScopeExpired,"Panel was disposed.") : OperationResult.Ok();
    public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
}
sealed class FakeOverride : IResourceOverride
{
    private Action? _dispose;
    public FakeOverride(string key, Action dispose) { ResourceKey = key; _dispose = dispose; }
    public string ResourceKey { get; }
    public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
}
sealed class FakeResources : IResourceService
{
    public int Disposed;
    public OperationResult<IResourceOverride> OverrideTexture(TextureOverrideRequest request)
        => OperationResult<IResourceOverride>.Ok(new FakeOverride(request.ResourceKey, () => Disposed++));
}
sealed class FakeGameResources : IGameResourceService
{
    public int Disposed;
    public OperationResult<IReadOnlyList<GameResourceTarget>> GetTargets() => OperationResult<IReadOnlyList<GameResourceTarget>>.Ok(Array.Empty<GameResourceTarget>());
    public OperationResult<IResourceOverride> OverrideColor(GameColorOverrideRequest request)
        => OperationResult<IResourceOverride>.Ok(new FakeOverride(request.ResourceKey, () => Disposed++));
}

sealed class Plugin : IRizPlugin
{
    private readonly Action<IPluginContext> _enable;
    private readonly Action _disable;
    public Plugin(PluginManifest manifest, Action<IPluginContext>? enable = null, Action? disable = null)
    { Manifest = manifest; _enable = enable ?? (_ => { }); _disable = disable ?? (() => { }); }
    public PluginManifest Manifest { get; }
    public void Enable(IPluginContext context) => _enable(context);
    public void Disable() => _disable();
}

sealed class Cleanup : IDisposable
{
    private Action? _action;
    public Cleanup(Action action) => _action = action;
    public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
}

sealed class CaptureLogger : ILogger
{
    public int Errors;
    public void Log(LogLevel level, string message, Exception? exception = null) { if (level == LogLevel.Error) Errors++; }
}

sealed class Scores : IScoreService
{
    internal int Calls;
    public OperationResult<IReadOnlyList<ScoreRecord>> GetBestScoresSnapshot()
    { ++Calls; return OperationResult<IReadOnlyList<ScoreRecord>>.Ok(Array.Empty<ScoreRecord>()); }
}

sealed class ConfigurationValue
{
    private readonly Func<int> _getValue;
    public ConfigurationValue(Func<int> getValue) => _getValue = getValue;
    public int Value => _getValue();
}

sealed class Fixture : IDisposable
{
    public string DataRoot { get; } = Path.Combine(Path.GetTempPath(), "riz-core-tests-" + Guid.NewGuid().ToString("N"));
    public CaptureLogger Log { get; } = new();
    public MainThreadDispatcher Dispatcher { get; } = new();
    public CapabilityRegistry Capabilities { get; } = new(new("test", "test", "test", "test-only"));
    public EventHub Events { get; }
    public PluginHost Host { get; }
    public Fixture() { Events = new(Log); Host = new(Capabilities, Dispatcher, Events, Log, DataRoot); }
    public void Dispose()
    {
        Host.Dispose(); Dispatcher.Dispose();
        // This path is generated by the fixture and never comes from plugin input.
        if (Directory.Exists(DataRoot)) Directory.Delete(DataRoot, true);
    }
}
