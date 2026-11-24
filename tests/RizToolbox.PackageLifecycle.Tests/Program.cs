using System.Text.Json;
using RizToolbox.Core;
using RizToolbox.Sdk;

var tests = new (string Name, Action Test)[]
{
    ("required dependency waits with full path and activates after late registration", RequiredLate),
    ("required incompatible version waits with structured dependency failure", RequiredVersion),
    ("optional missing/incompatible/disabled provider does not block and can be queried", OptionalQuery),
    ("loadAfter and optional ordering are deterministic independently of registration order", StableOrdering),
    ("soft ordering does not demand an unrequested or unavailable provider", SoftOrdering),
    ("required and ordering cycles report complete paths before side effects", CyclePaths),
    ("explicit conflicts are symmetric, versioned and deterministic", ConflictRules),
    ("activation rollback cleans failed plugin and newly activated dependencies", ActivationRollback),
    ("persisted explicit disable survives restart, dependency demand and late capabilities", DurableDisable),
    ("unknown saved package choice applies to later registration", SavedLatePackage),
    ("invalid intent restore validates whole snapshot before touching running plugins", InvalidRestore),
    ("desired waiting package demands its own and compatible hard dependency capabilities", WaitingCapabilityDemand),
    ("activation schema migration preserves source backup and atomic enable intent", ActivationMigration),
    ("migration exceptions/validation gaps/future schemas preserve original bytes", MigrationFailure),
    ("schema store serializes concurrent saves, rejects invalid JSON and enforces limits", StorageFailures)
};
int failures = 0;
foreach (var test in tests)
{
    try { test.Test(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + test.Name + ": " + ex); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} package lifecycle checks passed.");
return failures == 0 ? 0 : 1;

static void Check(bool success, string message) { if (!success) throw new InvalidOperationException(message); }
static PluginManifest Manifest(string id, params string[] dependencies) => new(id, new(1, 0, 0), new(1, 0, 0),
    Dependencies: dependencies.Select(x => new PluginDependency(x, new(1, 0, 0))).ToArray());
static PluginStatus State(Fixture f, string id) => f.Host.Statuses.Single(x => x.Id == id);

static void WaitingCapabilityDemand()
{
    using var f = new Fixture();
    f.Add(Manifest("hard") with { RequiredCapabilities = new[] { "module.hard" } });
    f.Add(Manifest("optional") with { RequiredCapabilities = new[] { "module.optional" } });
    f.Add(Manifest("consumer", "hard") with
    {
        RequiredCapabilities = new[] { "module.consumer" },
        OptionalDependencies = new[] { new PluginDependency("optional", new(1, 0, 0)) },
        LoadAfter = new[] { "optional" }
    });
    f.Add(Manifest("bad-version") with
    {
        Dependencies = new[] { new PluginDependency("hard", new(2, 0, 0)) },
        RequiredCapabilities = new[] { "module.bad-version" }
    });
    Check(f.Host.GetRequiredCapabilityDemand().Count == 0, "An installed but undesired package requested a provider.");
    f.Host.Enable("bad-version");
    f.Host.Enable("consumer");
    var waiting = f.Host.GetRequiredCapabilityDemand();
    Check(State(f, "consumer").State == PluginState.Waiting &&
        waiting.SequenceEqual(new[] { "module.consumer", "module.hard" }), "Waiting lost hard demand or activated a soft/incompatible package.");

    f.Capabilities.Register<object>("module.hard", new());
    f.Capabilities.Register<object>("module.consumer", new());
    Check(State(f, "consumer").State == PluginState.Enabled && State(f, "hard").State == PluginState.Enabled &&
        ReferenceEquals(waiting, f.Host.GetRequiredCapabilityDemand()), "Provider registration did not settle the waiting activation.");
    f.Host.ReportPluginFault("hard", new InvalidOperationException("provider sentinel"));
    Check(State(f, "hard").State == PluginState.Faulted && f.Host.GetRequiredCapabilityDemand().Count == 0,
        "Faulted dependency kept its only consumer's module demand alive.");
    f.Host.Enable("hard");
    Check(f.Host.GetRequiredCapabilityDemand().SequenceEqual(waiting) && State(f, "consumer").State == PluginState.Enabled,
        "Explicit retry did not restore demand and live activation.");
    f.Host.Disable("hard");
    Check(f.Host.GetRequiredCapabilityDemand().Count == 0 && State(f, "consumer").DesiredEnabled,
        "An explicitly disabled hard dependency was demanded or the consumer's saved choice was lost.");
}

static void RequiredLate()
{
    using var f = new Fixture(); var started = new List<string>();
    f.Add(Manifest("top", "middle"), _ => started.Add("top"));
    Check(!f.Host.Enable("top").Success && State(f, "top").State == PluginState.Waiting, "Missing provider did not wait.");
    f.Add(Manifest("middle", "base"), _ => started.Add("middle"));
    Check(State(f, "top").LastError!.Contains("top -> middle -> base"), "Dependency path was truncated.");
    f.Add(Manifest("base"), _ => started.Add("base"));
    Check(started.SequenceEqual(new[] { "base", "middle", "top" }), "Late dependency did not reconcile in dependency order.");
}

static void RequiredVersion()
{
    using var f = new Fixture();
    f.Add(Manifest("base") with { Version = new(2, 0, 0) });
    f.Add(Manifest("top") with { Dependencies = new[] { new PluginDependency("base", new(1, 0, 0), new(2, 0, 0)) } });
    f.Host.Enable("top"); var status = State(f, "top");
    Check(status.State == PluginState.Waiting && status.Failure?.Code == ErrorCode.DependencyUnsatisfied && status.LastError!.Contains("top -> base"), "Version wait/error metadata incorrect.");
}

static void OptionalQuery()
{
    using var f = new Fixture(); IPluginDependencyService? query = null;
    f.Add(Manifest("consumer") with { OptionalDependencies = new[] { new PluginDependency("provider", new(1, 0, 0), new(2, 0, 0)) } },
        c => query = c.GetService<IPluginDependencyService>(PluginLifecycleCapabilityIds.Dependencies).Service);
    Check(f.Host.Enable("consumer").Success && query is not null, "Missing optional dependency blocked consumer or query service absent.");
    Check(query!.Query("provider").Value is { Optional: true, Installed: false }, "Missing optional state wrong.");
    f.Add(Manifest("provider"));
    Check(query.Query("provider").Value is { Installed: true, VersionCompatible: true, Enabled: false }, "Installed optional provider was forced on.");
    f.Host.Enable("provider"); Check(query.Query("provider").Value!.Enabled, "Provider readiness query stale.");
    f.Host.Disable("provider"); Check(State(f, "consumer").State == PluginState.Enabled && !query.Query("provider").Value!.Enabled, "Optional provider stop cascaded.");
    Check(!query.Query("undeclared").Success, "Undeclared dependency query accepted.");
    f.Host.Disable("consumer");
    var expired=query.Query("provider");
    Check(!expired.Success && expired.Error?.Code==ErrorCode.ScopeExpired, "Dependency query view survived activation end.");
    using var incompatible = new Fixture();
    incompatible.Add(Manifest("provider") with { Version = new(2, 0, 0) });
    incompatible.Add(Manifest("consumer") with { OptionalDependencies = new[] { new PluginDependency("provider", new(1, 0, 0), new(2, 0, 0)) } });
    Check(incompatible.Host.Enable("consumer").Success && !incompatible.Host.GetDependencyStatus("consumer", "provider").Value!.VersionCompatible, "Incompatible optional version blocked activation or reported compatible.");
}

static void StableOrdering()
{
    foreach (bool reversed in new[] { false, true })
    {
        using var f = new Fixture(); var calls = new List<string>();
        var manifests = new[] { Manifest("a") with { LoadAfter = new[] { "z" } }, Manifest("m") with { OptionalDependencies = new[] { new PluginDependency("a", new(1, 0, 0)) } }, Manifest("z") };
        foreach (var manifest in reversed ? manifests.Reverse() : manifests) f.Add(manifest, _ => calls.Add(manifest.Id));
        f.Host.EnableAll(); Check(calls.SequenceEqual(new[] { "z", "a", "m" }), "Registration-dependent load order: " + string.Join(",", calls));
    }
}

static void SoftOrdering()
{
    using var f = new Fixture();
    f.Add(Manifest("consumer") with { LoadAfter = new[] { "provider", "absent" } });
    f.Add(Manifest("provider") with { RequiredCapabilities = new[] { "not.ready" } });
    Check(f.Host.Enable("consumer").Success && State(f, "provider").State == PluginState.Disabled, "loadAfter auto-demanded provider.");
    f.Host.Enable("provider"); Check(State(f, "provider").State == PluginState.Waiting && State(f, "consumer").State == PluginState.Enabled, "Unavailable soft predecessor disabled consumer.");
}

static void CyclePaths()
{
    using var f = new Fixture(); int activations = 0;
    f.Add(Manifest("a", "b"), _ => activations++); f.Add(Manifest("b", "c")); f.Add(Manifest("c", "a"));
    f.Host.Enable("a"); Check(activations == 0 && State(f, "a").LastError!.Contains("a -> b -> c -> a"), "Required cycle path incomplete or produced side effects.");
    using var order = new Fixture();
    order.Add(Manifest("a") with { LoadAfter = new[] { "b" } }); order.Add(Manifest("b") with { LoadAfter = new[] { "a" } });
    order.Host.EnableAll(); Check(order.Host.Statuses.All(x => x.State == PluginState.Waiting && x.LastError!.Contains("cycle")), "Ordering cycle activated.");
}

static void ConflictRules()
{
    foreach (bool reversed in new[] { false, true })
    {
        using var f = new Fixture();
        var a = Manifest("a") with { Conflicts = new[] { new PluginConflict("b", new(1, 0, 0), new(2, 0, 0)) } }; var b = Manifest("b");
        foreach (var manifest in reversed ? new[] { b, a } : new[] { a, b }) f.Add(manifest);
        f.Host.EnableAll(); Check(State(f, "a").State == PluginState.Enabled && State(f, "b").Failure?.Code == ErrorCode.Conflict, "Conflict winner depended on registration order.");
        Check(State(f, "b").LastError!.Contains("<->") && State(f, "b").LastError!.Contains("'a'") && State(f, "b").LastError!.Contains("'b'"), "Conflict owners omitted.");
        f.Host.Disable("a"); Check(State(f, "b").State == PluginState.Enabled, "Conflicting waiter did not start after owner stop.");
    }
    using var version = new Fixture(); version.Add(Manifest("a") with { Conflicts = new[] { new PluginConflict("b", new(2, 0, 0)) } }); version.Add(Manifest("b"));
    version.Host.EnableAll(); Check(version.Host.Statuses.All(x => x.State == PluginState.Enabled), "Conflict range applied outside matching version.");
}

static void ActivationRollback()
{
    using var f = new Fixture(); var calls = new List<string>();
    f.Add(Manifest("base"), c => c.Own(new ActionDisposable(() => calls.Add("base-resource"))), () => calls.Add("base-disable"));
    f.Add(Manifest("failing", "base"), c => { c.Own(new ActionDisposable(() => calls.Add("failed-resource"))); throw new InvalidOperationException("fixture-failure"); }, () => calls.Add("failed-disable"));
    f.Host.Enable("failing");
    Check(calls.SequenceEqual(new[] { "failed-disable", "failed-resource", "base-disable", "base-resource" }), "Activation did not reverse every side effect.");
    Check(State(f, "failing").State == PluginState.Faulted && State(f, "base").State != PluginState.Enabled, "Failure leaked active dependency.");
}

static void DurableDisable()
{
    using var directory = new Scratch(); string file = Path.Combine(directory.Path, "activation.json");
    using (var first = new Fixture())
    {
        first.Add(Manifest("provider") with { RequiredCapabilities = new[] { "late" } }); first.Add(Manifest("consumer", "provider"));
        using var binding = new PluginActivationStore(file).Bind(first.Host).Value!;
        first.Host.Enable("consumer"); first.Host.Disable("provider");
    }
    using var next = new Fixture(); next.Add(Manifest("provider") with { RequiredCapabilities = new[] { "late" } }); next.Add(Manifest("consumer", "provider"));
    var restored = new PluginActivationStore(file).Bind(next.Host); Check(restored.Success, "Could not restore intents."); using var nextBinding = restored.Value!;
    next.Capabilities.Register<object>("late", new());
    Check(State(next, "provider").State == PluginState.Disabled && State(next, "consumer").State == PluginState.Waiting && State(next, "consumer").DesiredEnabled, "Disabled provider restarted after new process/capability.");
    next.Host.Enable("provider"); Check(next.Host.Statuses.All(x => x.State == PluginState.Enabled), "Explicit re-enable failed to unblock dependent.");
}

static void SavedLatePackage()
{
    using var f = new Fixture();
    Check(f.Host.RestoreActivationIntents(new[] { new PluginActivationIntent("future", false, true) }, enableUnspecified: true).Success, "Restore failed.");
    f.Add(Manifest("future")); f.Add(Manifest("default"));
    Check(State(f, "future").State == PluginState.Disabled && State(f, "default").State == PluginState.Enabled, "Saved late package intent was lost.");
}

static void InvalidRestore()
{
    using var f = new Fixture(); f.Add(Manifest("active")); f.Host.Enable("active");
    var result = f.Host.RestoreActivationIntents(new[] { new PluginActivationIntent("active", false, true), new PluginActivationIntent("active", true) });
    Check(!result.Success && State(f, "active").State == PluginState.Enabled && State(f, "active").DesiredEnabled, "Invalid restore partially applied intent.");
}

static void ActivationMigration()
{
    using var directory = new Scratch(); string file = Path.Combine(directory.Path, "activation.json"); string original = "{\"enabled\":{\"a\":true,\"b\":false}}";
    File.WriteAllText(file, original); var store = new PluginActivationStore(file); var upgraded = store.Load();
    Check(upgraded.Success && upgraded.Value!.Single(x => x.Id == "b").ExplicitlyDisabled, "Legacy activation did not migrate.");
    Check(Directory.GetFiles(directory.Path, "*.bak").Any(x => File.ReadAllText(x) == original), "Original successful-migration backup missing.");
    Check(store.Load().Success && store.Save(upgraded.Value!).Success, "Migrated activation not reloadable/saveable.");
}

static void MigrationFailure()
{
    using var directory = new Scratch(); string file = Path.Combine(directory.Path, "schema.json"); string original = "{\"schemaVersion\":0,\"data\":{\"value\":1}}";
    File.WriteAllText(file, original); var store = new AtomicSchemaStore(file);
    var thrown = store.Load(1, () => new Settings(0), new[] { new JsonSchemaMigration(0, 1, _ => throw new InvalidOperationException("migration-failed")) });
    Check(!thrown.Success && thrown.Error!.Code == ErrorCode.MigrationFailed && File.ReadAllText(file) == original, "Throwing migration overwrote original.");
    var invalid = store.Load(1, () => new Settings(0), new[] { new JsonSchemaMigration(0, 1, _ => JsonSerializer.SerializeToElement(new { value = -1 })) }, x => x.Value < 0 ? "invalid value" : null);
    Check(!invalid.Success && invalid.Error!.Code == ErrorCode.MigrationFailed && File.ReadAllText(file) == original, "Invalid migration output overwrote original.");
    Check(!store.Load(2, () => new Settings(0)).Success && File.ReadAllText(file) == original, "Missing migration overwrote original.");
    var future = "{\"schemaVersion\":99,\"data\":{\"value\":1}}"; File.WriteAllText(file, future);
    Check(!store.Load(1, () => new Settings(0)).Success && !store.Save(1, new Settings(3)).Success && File.ReadAllText(file) == future, "Future schema silently downgraded.");
}

static void StorageFailures()
{
    using var directory = new Scratch(); string file = Path.Combine(directory.Path, "settings.json");
    var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
    Parallel.For(0, 20, i => { var result = new AtomicSchemaStore(file).Save(1, new Settings(i)); if (!result.Success) failures.Add(result.Error!.Message); });
    Check(failures.Count == 0 && new AtomicSchemaStore(file).Load(1, () => new Settings(-1)).Success, "Concurrent atomic save corrupt or failed.");
    Check(Directory.GetFiles(directory.Path, "*.tmp").Length == 0, "Atomic temporary files leaked.");
    var original = File.ReadAllText(file); Check(!new AtomicSchemaStore(file, 32).Save(1, new { value = new string('x', 100) }).Success && File.ReadAllText(file) == original, "Limit failure overwrote old bytes.");
    File.WriteAllText(file, "{ broken"); Check(!new AtomicSchemaStore(file).Load(1, () => new Settings(0)).Success && File.ReadAllText(file) == "{ broken", "Invalid config overwritten.");
}

sealed record Settings(int Value);
sealed class Plugin : IRizPlugin
{
    public PluginManifest Manifest { get; }
    private readonly Action<IPluginContext>? _enable; private readonly Action? _disable;
    public Plugin(PluginManifest manifest, Action<IPluginContext>? enable, Action? disable) => (Manifest, _enable, _disable) = (manifest, enable, disable);
    public void Enable(IPluginContext context) => _enable?.Invoke(context);
    public void Disable() => _disable?.Invoke();
}
sealed class TestLogger : ILogger { public void Log(LogLevel level, string message, Exception? exception = null) { } }
sealed class ActionDisposable : IDisposable
{
    private Action? _action; public ActionDisposable(Action action) => _action = action;
    public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
}
sealed class Fixture : IDisposable
{
    private readonly Scratch _scratch = new(); public PluginHost Host { get; } public CapabilityRegistry Capabilities { get; }
    public Fixture()
    { var log = new TestLogger(); Capabilities = new(new("test", "test", "test", "test")); Host = new(Capabilities, new MainThreadDispatcher(), new EventHub(log), log, _scratch.Path); }
    public void Add(PluginManifest manifest, Action<IPluginContext>? enable = null, Action? disable = null)
    { var result = Host.Register(new Plugin(manifest, enable, disable)); if (!result.Success) throw new InvalidOperationException(result.Error?.Message); }
    public void Dispose() { Host.Dispose(); _scratch.Dispose(); }
}
sealed class Scratch : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "riz-package-life-" + Guid.NewGuid().ToString("N"));
    public Scratch() => Directory.CreateDirectory(Path);
    public void Dispose()
    { var full = System.IO.Path.GetFullPath(Path); if (!full.StartsWith(System.IO.Path.GetTempPath(), StringComparison.OrdinalIgnoreCase) || !System.IO.Path.GetFileName(full).StartsWith("riz-package-life-")) throw new InvalidOperationException("Unsafe cleanup."); Directory.Delete(full, true); }
}
