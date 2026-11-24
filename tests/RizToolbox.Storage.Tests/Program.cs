using System.Text;
using System.Text.Json;
using RizToolbox.Core;
using RizToolbox.Core.Localization;
using RizToolbox.Core.Storage;
using RizToolbox.Sdk;

var tests = new (string Name, Action Test)[]
{
    ("data/cache namespaces isolate identical plugin-local names and copy bytes", DataIsolation),
    ("cumulative quota and per-file limits preserve previous files", QuotaLimits),
    ("schema configuration validates, migrates atomically and preserves backups", ConfigurationMigration),
    ("migration failures preserve originals and do not affect other plugins", MigrationIsolation),
    ("migration backup bytes count against quota before any disk change", MigrationQuota),
    ("configuration quota counts the exact bytes committed by one serializer pass", ConfigurationQuotaUsesCommittedBytes),
    ("configuration scope expiry during serialization preserves bytes and revision", ConfigurationSaveExpiry),
    ("configuration scope expiry after migration validation preserves original and backup set", ConfigurationMigrationExpiry),
    ("migration rejects same-document reentry and permits an independent document", ConfigurationMigrationReentry),
    ("a failed final replacement leaves no partial migration backup", ConfigurationMigrationReplaceFailure),
    ("traversal, absolute paths and Windows reserved names are rejected", InvalidNames),
    ("symbolic link escape is rejected for read/write/list/quota", LinkEscape),
    ("storage changes are namespaced, ordered and automatically unsubscribed", StorageNotifications),
    ("reentrant config saves deliver each plugin's revisions in order", ReentrantStorageNotifications),
    ("queued storage changes respect teardown and subscriber faults", QueuedStorageNotificationsRespectTeardown),
    ("storage callback exceptions stop owner while other plugins still receive events", StorageCallbackFault),
    ("storage and localization cached views expire on disable with new views on restart", ExpiredViews),
    ("language namespaces, per-plugin fallback and hot language switch", LocalizationFallback),
    ("language tables copy inputs, reject collisions and release leases automatically", LocalizationOwnership),
    ("language callback faults are isolated and do not interrupt other plugins", LocalizationFault),
    ("wrong-thread storage and language operations return structured errors", WrongThread)
};
int failures = 0;
foreach (var test in tests)
{
    try { test.Test(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + test.Name + ": " + ex); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} storage/localization checks passed.");
return failures == 0 ? 0 : 1;

static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
static string Read(IPluginStorageService service, string name, PluginStorageArea area = PluginStorageArea.Data) => Encoding.UTF8.GetString(service.Read(area, name).Value!);

static void DataIsolation()
{
    using var f = new Fixture(); var a = f.Start("a"); var b = f.Start("b");
    Check(a.Storage.Write(PluginStorageArea.Data, "folder/item.bin", Bytes("a")).Success && b.Storage.Write(PluginStorageArea.Data, "folder/item.bin", Bytes("b")).Success, "Namespaced writes failed.");
    Check(Read(a.Storage, "folder/item.bin") == "a" && Read(b.Storage, "folder/item.bin") == "b", "Cross-plugin storage collision.");
    var copy = a.Storage.Read(PluginStorageArea.Data, "folder/item.bin").Value!; copy[0] = 0;
    Check(Read(a.Storage, "folder/item.bin") == "a", "Returned byte array aliases host storage.");
    Check(a.Storage.Write(PluginStorageArea.Cache, "folder/item.bin", Bytes("cache")).Success && Read(a.Storage, "folder/item.bin", PluginStorageArea.Cache) == "cache", "Data/cache areas collided.");
    Check(a.Storage.List(PluginStorageArea.Data).Value!.Single().Name == "folder/item.bin", "List leaked filesystem paths.");
    Check(a.Storage.Delete(PluginStorageArea.Data, "folder/item.bin").Success && a.Storage.Delete(PluginStorageArea.Data, "folder/item.bin").Success && !a.Storage.Read(PluginStorageArea.Data, "folder/item.bin").Success, "Delete is not idempotent.");
}

static void QuotaLimits()
{
    using var f = new Fixture(1024, 700); var a = f.Start("a");
    Check(a.Storage.Write(PluginStorageArea.Data, "first", new byte[600]).Success, "First quota write failed.");
    var over = a.Storage.Write(PluginStorageArea.Cache, "second", new byte[500]);
    Check(!over.Success && over.Error!.Code == ErrorCode.QuotaExceeded && a.Storage.GetQuota().Value!.UsedBytes == 600, "Cumulative quota not enforced atomically.");
    var limit = a.Storage.Write(PluginStorageArea.Data, "first", new byte[701]);
    Check(!limit.Success && limit.Error!.Code == ErrorCode.QuotaExceeded && a.Storage.Read(PluginStorageArea.Data, "first").Value!.Length == 600, "Per-file limit lost previous content.");
    Check(a.Storage.Write(PluginStorageArea.Data, "first", new byte[200]).Success && a.Storage.GetQuota().Value!.UsedBytes == 200, "Replacement charged full previous plus new bytes.");
    Check(Directory.GetFiles(f.Path, "*.tmp", SearchOption.AllDirectories).Length == 0, "Atomic temporary files leaked.");
}

static void ConfigurationMigration()
{
    using var f = new Fixture(); var a = f.Start("a");
    var missing = a.Storage.LoadConfiguration("preferences", 1, () => new Settings(3));
    Check(missing.Success && missing.Value!.Value.Count == 3 && a.Storage.GetQuota().Value!.UsedBytes == 0, "Defaults should not write an unsolicited file.");
    Check(a.Storage.SaveConfiguration("preferences", 1, new Settings(4)).Success, "Initial configuration save failed.");
    string path = f.Config("a", "preferences"); var original = File.ReadAllText(path);
    var migrated = a.Storage.LoadConfiguration("preferences", 2, () => new Settings(0), new[] { new ConfigurationMigration(1, 2,
        old => JsonSerializer.SerializeToElement(new { count = old.GetProperty("count").GetInt32() + 1 })) }, value => value.Count < 0 ? "Invalid count" : null);
    Check(migrated.Success && migrated.Value is { Migrated: true } && migrated.Value.Value.Count == 5, "Valid configuration migration failed.");
    Check(Directory.GetFiles(System.IO.Path.GetDirectoryName(path)!, "*.bak").Any(file => File.ReadAllText(file) == original), "Original successful-migration backup missing.");
    var invalid = a.Storage.SaveConfiguration("preferences", 2, new Settings(-1), value => value.Count < 0 ? "Invalid count" : null);
    Check(!invalid.Success && invalid.Error!.Code == ErrorCode.ConfigInvalid && a.Storage.LoadConfiguration("preferences", 2, () => new Settings(0)).Value!.Value.Count == 5, "Invalid config save corrupted prior value.");
    Check(!a.Storage.Write(PluginStorageArea.Configuration, "preferences", Bytes("bypass")).Success, "Raw write bypassed schema/validation.");
}

static void MigrationIsolation()
{
    using var f = new Fixture(); var a = f.Start("a"); var b = f.Start("b");
    a.Storage.SaveConfiguration("settings", 1, new Settings(2)); string path = f.Config("a", "settings"); var original = File.ReadAllText(path);
    var broken = a.Storage.LoadConfiguration("settings", 2, () => new Settings(0), new[] { new ConfigurationMigration(1, 2, _ => throw new InvalidOperationException("fixture migration")) });
    Check(!broken.Success && broken.Error!.Code == ErrorCode.MigrationFailed && File.ReadAllText(path) == original, "Failed migration overwrote source.");
    Check(b.Storage.SaveConfiguration("settings", 1, new Settings(9)).Success, "Broken neighbor prevented valid configuration.");
    File.WriteAllText(path, "{ invalid"); var parse = a.Storage.LoadConfiguration("settings", 1, () => new Settings(0));
    Check(!parse.Success && parse.Error!.Code == ErrorCode.ConfigInvalid && File.ReadAllText(path) == "{ invalid", "Corrupt source overwritten.");
}

static void MigrationQuota()
{
    using var f = new Fixture(240, 200); var a = f.Start("a");
    Check(a.Storage.SaveConfiguration("settings", 1, new { text = new string('x', 60) }).Success, "Initial config save failed.");
    Check(a.Storage.Write(PluginStorageArea.Cache, "hold", new byte[100]).Success, "Cache fill failed.");
    string path = f.Config("a", "settings"); var original = File.ReadAllText(path);
    var migration = a.Storage.LoadConfiguration("settings", 2, () => new TextSettings(""), new[] { new ConfigurationMigration(1, 2, json => json) });
    Check(!migration.Success && migration.Error!.Code == ErrorCode.QuotaExceeded && File.ReadAllText(path) == original && Directory.GetFiles(System.IO.Path.GetDirectoryName(path)!, "*.bak").Length == 0,
        "Migration backup quota checked after mutation or wrong error.");
    File.WriteAllText(path, new string(' ', 201));
    Check(a.Storage.LoadConfiguration("settings", 2, () => new TextSettings("")).Error?.Code == ErrorCode.QuotaExceeded,
        "Existing file above the configured size limit lost its quota error.");
}

static void ConfigurationQuotaUsesCommittedBytes()
{
    using var f = new Fixture(240, 200);
    var storage = f.Start("a").Storage;
    Check(storage.Write(PluginStorageArea.Cache, "hold", new byte[100]).Success, "Could not reserve quota.");
    int calls = 0;
    var result = storage.SaveConfiguration("settings", 1, new DynamicTextSettings(() =>
        ++calls == 1 ? "small" : new string('x', 120)));
    Check(result.Success && calls == 1, "Save serialized the plugin value more than once.");
    Check(storage.LoadConfiguration("settings", 1, () => new TextSettings("")).Value?.Value.Text == "small", "Save did not commit the measured payload.");
    Check(storage.GetQuota().Value?.UsedBytes <= 240, "Committed payload exceeded quota.");
}

static void ConfigurationSaveExpiry()
{
    using var f = new Fixture();
    var storage = f.Start("a").Storage;
    var changes = new List<PluginStorageChanged>();
    Check(storage.Subscribe(changes.Add).Success, "Could not subscribe to storage changes.");
    Check(storage.SaveConfiguration("settings", 1, new TextSettings("original")).Success, "Could not save original.");
    var path = f.Config("a", "settings");
    var original = File.ReadAllText(path);
    var result = storage.SaveConfiguration("settings", 1, new DynamicTextSettings(() =>
    {
        Check(f.Host.Disable("a").Success, "Could not disable scope during serialization.");
        return "expired";
    }));
    Check(result.Error?.Code == ErrorCode.ScopeExpired && File.ReadAllText(path) == original, "Expired save changed storage or returned the wrong result.");
    Check(f.Host.Enable("a").Success, "Could not reenable plugin after canceled save.");
    var fresh = f.Latest("a").Storage;
    Check(fresh.Subscribe(changes.Add).Success && fresh.SaveConfiguration("settings", 1, new TextSettings("fresh")).Success, "Could not save from fresh scope.");
    Check(changes.Select(x => x.Revision).SequenceEqual(new long[] { 1, 2 }), "Expired save advanced the storage revision.");
}

static void ConfigurationMigrationExpiry()
{
    using var f = new Fixture();
    var storage = f.Start("a").Storage;
    Check(storage.SaveConfiguration("settings", 1, new Settings(3)).Success, "Could not save original.");
    var path = f.Config("a", "settings");
    var original = File.ReadAllText(path);
    var result = storage.LoadConfiguration("settings", 2, () => new Settings(0),
        new[] { new ConfigurationMigration(1, 2, json => json) }, _ =>
        {
            Check(f.Host.Disable("a").Success, "Could not disable scope during validation.");
            return null;
        });
    Check(result.Error?.Code == ErrorCode.ScopeExpired && File.ReadAllText(path) == original, "Expired migration changed source bytes or returned the wrong result.");
    Check(Directory.GetFiles(Path.GetDirectoryName(path)!).SequenceEqual(new[] { path }), "Expired migration created an extra file.");
}

static void ConfigurationMigrationReentry()
{
    using var f = new Fixture();
    var storage = f.Start("a").Storage;
    Check(storage.SaveConfiguration("settings", 1, new Settings(3)).Success, "Could not save original.");
    var path = f.Config("a", "settings");
    var original = File.ReadAllText(path);
    OperationError? nestedError = null;
    bool independent = false;
    var result = storage.LoadConfiguration("settings", 2, () => new Settings(0),
        new[] { new ConfigurationMigration(1, 2, json =>
        {
            nestedError = storage.SaveConfiguration("settings", 1, new Settings(99)).Error;
            independent = storage.SaveConfiguration("other", 1, new Settings(5)).Success;
            return json;
        }) });
    Check(nestedError?.Code == ErrorCode.Conflict && independent, "Configuration guard did not isolate the in-flight document.");
    Check(result.Value is { Migrated: true, Value.Count: 3 }, "Reentered save displaced the migration baseline.");
    Check(File.ReadAllText(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.bak").Single()) == original, "Migration backup lost the original bytes.");
}

static void ConfigurationMigrationReplaceFailure()
{
    if (!OperatingSystem.IsWindows()) return;
    using var f = new Fixture();
    var storage = f.Start("a").Storage;
    Check(storage.SaveConfiguration("settings", 1, new Settings(3)).Success, "Could not save original.");
    var path = f.Config("a", "settings");
    var original = File.ReadAllText(path);
    using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    var result = storage.LoadConfiguration("settings", 2, () => new Settings(0),
        new[] { new ConfigurationMigration(1, 2, json => json) });
    Check(result.Error?.Code == ErrorCode.MigrationFailed && File.ReadAllText(path) == original, "Locked original changed or replacement did not fail.");
    Check(Directory.GetFiles(Path.GetDirectoryName(path)!).SequenceEqual(new[] { path }), "Failed replacement left a new backup or temporary file.");
}

static void InvalidNames()
{
    using var f = new Fixture(); var a = f.Start("a");
    foreach (var path in new[] { "../outside", "/outside", "C:/outside", "nested/../outside", "a\\b", "CON", "nul.txt", "LPT1.bin", "a.", "a//b", "a:stream", "" })
        Check(a.Storage.Write(PluginStorageArea.Data, path, Bytes("x")).Error?.Code == ErrorCode.InvalidArgument, "Unsafe path accepted: " + path);
    Check(!a.Storage.SaveConfiguration("nested/name", 1, new Settings(1)).Success && a.Storage.List((PluginStorageArea)99).Error?.Code == ErrorCode.InvalidArgument, "Invalid config name or unknown area accepted.");
}

static void LinkEscape()
{
    using var f = new Fixture(); using var outside = new Fixture(); var a = f.Start("a");
    a.Storage.Write(PluginStorageArea.Data, "init", Bytes("safe")); string root = System.IO.Path.Combine(f.Path, "storage", "plugin-a", "data");
    var link = System.IO.Path.Combine(root, "linked");
    try { Directory.CreateSymbolicLink(link, outside.Path); }
    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
    { Console.WriteLine("SKIP unavailable OS symbolic-link privilege: " + ex.Message); return; }
    try
    {
        Check(!a.Storage.Write(PluginStorageArea.Data, "linked/file", Bytes("escape")).Success && !a.Storage.Read(PluginStorageArea.Data, "linked/file").Success &&
            !a.Storage.List(PluginStorageArea.Data).Success && !a.Storage.GetQuota().Success && Directory.GetFiles(outside.Path).Length == 0, "Link escaped namespace or quota accounting.");
    }
    finally { Directory.Delete(link); }
}

static void StorageNotifications()
{
    using var f = new Fixture(); var a = f.Start("a"); var b = f.Start("b"); var seenA = new List<PluginStorageChanged>(); var seenB = new List<PluginStorageChanged>();
    var subscription = a.Storage.Subscribe(seenA.Add); b.Storage.Subscribe(seenB.Add);
    a.Storage.Write(PluginStorageArea.Data, "file", Bytes("v1")); a.Storage.Write(PluginStorageArea.Data, "file", Bytes("v2")); a.Storage.Delete(PluginStorageArea.Data, "file");
    Check(seenA.Select(x => x.Revision).SequenceEqual(new long[] { 1, 2, 3 }) && seenB.Count == 0, "Change namespace/revision incorrect.");
    subscription.Value!.Dispose(); a.Storage.Write(PluginStorageArea.Data, "file", Bytes("v3")); Check(seenA.Count == 3, "Disposed change subscription called.");
    f.Host.Disable("a"); f.Host.Enable("a"); var fresh = f.Latest("a"); fresh.Storage.Write(PluginStorageArea.Data, "file", Bytes("v4")); Check(seenA.Count == 3, "Old change subscription survived re-enable.");
}

static void ReentrantStorageNotifications()
{
    using var f = new Fixture();
    var a = f.Start("a").Storage;
    var b = f.Start("b").Storage;
    var seenA = new List<PluginStorageChanged>();
    var seenB = new List<PluginStorageChanged>();
    Check(a.Subscribe(change =>
    {
        if (change.Revision != 1) return;
        Check(a.SaveConfiguration("settings", 1, new Settings(2)).Success, "Same-document reentrant save failed after the first commit.");
        Check(b.SaveConfiguration("settings", 1, new Settings(3)).Success, "Neighbor configuration save failed.");
    }).Success, "First subscription failed.");
    Check(b.Subscribe(change =>
    {
        if (change.Revision != 1) return;
        Check(b.SaveConfiguration("settings", 1, new Settings(4)).Success, "Neighbor reentrant save failed.");
        Check(a.SaveConfiguration("extra", 1, new Settings(5)).Success, "Save back into the first namespace failed.");
    }).Success, "Neighbor subscription failed.");
    Check(a.Subscribe(seenA.Add).Success && b.Subscribe(seenB.Add).Success, "Recording subscriptions failed.");

    Check(a.SaveConfiguration("settings", 1, new Settings(1)).Success, "First configuration save failed.");
    Check(f.State("a") == PluginState.Enabled && f.State("b") == PluginState.Enabled, "A reentrant publisher faulted.");
    Check(seenA.Select(x => (x.PluginId, x.Name, x.Revision)).SequenceEqual(new[] { ("a", "settings", 1L), ("a", "settings", 2L), ("a", "extra", 3L) }),
        "The first namespace saw unordered revisions or a foreign notification.");
    Check(seenB.Select(x => (x.PluginId, x.Name, x.Revision)).SequenceEqual(new[] { ("b", "settings", 1L), ("b", "settings", 2L) }),
        "The neighbor namespace saw unordered revisions or a foreign notification.");
    Check(a.LoadConfiguration("settings", 1, () => new Settings(0)).Value?.Value.Count == 2 &&
        b.LoadConfiguration("settings", 1, () => new Settings(0)).Value?.Value.Count == 4,
        "A reentrant notification changed the final committed configuration.");
}

static void QueuedStorageNotificationsRespectTeardown()
{
    foreach (bool fault in new[] { false, true })
    {
        using var f = new Fixture();
        var a = f.Start("a").Storage;
        var b = f.Start("b").Storage;
        var stale = new List<long>();
        var restarted = new List<long>();
        var neighbor = new List<long>();
        bool committed = false;
        void Restart()
        {
            Check(f.Host.Enable("a").Success, "Could not restart the storage plugin.");
            var fresh = f.Latest("a").Storage;
            Check(fresh.LoadConfiguration("settings", 1, () => new Settings(0)).Value?.Value.Count == 2, "Teardown lost a committed reentrant save.");
            Check(fresh.Subscribe(change => restarted.Add(change.Revision)).Success, "Replacement subscription failed.");
            Check(fresh.SaveConfiguration("settings", 1, new Settings(3)).Success, "Replacement activation could not publish a new save.");
        }

        Check(a.Subscribe(change =>
        {
            if (change.Revision != 1) return;
            committed = a.SaveConfiguration("settings", 1, new Settings(2)).Success;
            if (fault) throw new InvalidOperationException("fixture storage subscriber fault");
            Check(f.Host.Disable("a").Success, "Could not stop the publishing activation.");
            Restart();
        }).Success, "Teardown subscription failed.");
        Check(a.Subscribe(change => stale.Add(change.Revision)).Success && b.Subscribe(change => neighbor.Add(change.Revision)).Success,
            "Recording subscriptions failed.");
        Check(a.SaveConfiguration("settings", 1, new Settings(1)).Success && committed, "A committed write failed during notification delivery.");
        if (fault)
        {
            Check(f.State("a") == PluginState.Faulted, "Subscriber fault was not attributed to the writer's scope.");
            Restart();
        }

        Check(a.GetQuota().Error?.Code == ErrorCode.ScopeExpired && stale.Count == 0, "An expired activation received a queued revision.");
        Check(f.State("a") == PluginState.Enabled && restarted.SequenceEqual(new[] { 3L }), "A queued revision escaped teardown or blocked the replacement activation.");
        Check(b.SaveConfiguration("settings", 1, new Settings(1)).Success && f.State("b") == PluginState.Enabled && neighbor.SequenceEqual(new[] { 1L }),
            "Teardown blocked the neighboring namespace or delivered foreign revisions.");
    }
}

static void StorageCallbackFault()
{
    using var f = new Fixture(); var a = f.Start("a"); var b = f.Start("b"); int count = 0;
    a.Storage.Subscribe(_ => throw new InvalidOperationException("bad storage subscriber")); b.Storage.Subscribe(_ => count++);
    a.Storage.Write(PluginStorageArea.Data, "file", Bytes("x")); b.Storage.Write(PluginStorageArea.Data, "file", Bytes("x"));
    Check(f.State("a") == PluginState.Faulted && f.State("b") == PluginState.Enabled && count == 1, "Storage subscriber fault attribution/isolation failed.");
}

static void ExpiredViews()
{
    using var f = new Fixture(); var a = f.Start("a");
    a.Localization.Register("en", new Dictionary<string, string> { ["hello"] = "hello" }, true);
    f.Host.Disable("a");
    Check(a.Storage.GetQuota().Error?.Code == ErrorCode.ScopeExpired && a.Storage.Write(PluginStorageArea.Data, "file", Bytes("bad")).Error?.Code == ErrorCode.ScopeExpired &&
        a.Localization.Resolve("hello").Error?.Code == ErrorCode.ScopeExpired, "Cached service remained active after disable.");
    f.Host.Enable("a"); var fresh = f.Latest("a");
    Check(fresh.Storage.GetQuota().Success && fresh.Localization.Register("en", new Dictionary<string, string> { ["hello"] = "new" }, true).Success, "Old owned tables leaked into restarted plugin.");
}

static void LocalizationFallback()
{
    using var f = new Fixture(); var a = f.Start("a"); var b = f.Start("b"); int notifications = 0;
    a.Localization.Register("en", new Dictionary<string, string> { ["hello"] = "Hello", ["fallback"] = "Fallback" }, true);
    a.Localization.Register("zh-CN", new Dictionary<string, string> { ["hello"] = "你好" });
    b.Localization.Register("en", new Dictionary<string, string> { ["hello"] = "Neighbor" }, true);
    a.Localization.Subscribe(_ => notifications++);
    Check(a.Localization.Resolve("hello").Value == "Hello" && b.Localization.Resolve("hello").Value == "Neighbor", "Language keys collided across plugins.");
    f.Localization.SetLanguage("zh-CN");
    Check(a.Localization.Resolve("hello").Value == "你好" && a.Localization.Resolve("fallback").Value == "Fallback" && b.Localization.Resolve("hello").Value == "Neighbor" && notifications == 1, "Live language/fallback incorrect.");
    Check(a.Localization.GetState().Value!.Language == "zh-cn" && a.Localization.Resolve("missing").Error?.TargetId == "a/missing", "Language state or missing-key namespace incorrect.");
    Check(a.Localization.Resolve("b/hello").Error?.Code == ErrorCode.InvalidArgument, "Other-plugin key traversal accepted.");
    f.Localization.SetLanguage("fr"); Check(a.Localization.Resolve("hello").Value == "Hello", "Missing active language did not fall back.");
}

static void LocalizationOwnership()
{
    using var f = new Fixture(); var a = f.Start("a"); var messages = new Dictionary<string, string> { ["key"] = "original" };
    var first = a.Localization.Register("en", messages, true); Check(first.Success, "Language registration failed."); messages["key"] = "changed";
    Check(a.Localization.Resolve("key").Value == "original" && a.Localization.Register("en", messages).Error?.Code == ErrorCode.Conflict &&
        a.Localization.Register("fr", messages, true).Error?.Code == ErrorCode.Conflict, "Table aliasing or duplicate fallback accepted.");
    first.Value!.Dispose(); first.Value.Dispose();
    Check(a.Localization.Register("fr", messages, true).Success && a.Localization.Resolve("key").Value == "changed", "Lease disposal did not release table/fallback.");
    f.Host.Disable("a"); f.Host.Enable("a");
    Check(f.Latest("a").Localization.Register("fr", messages, true).Success, "Disabled plugin's table survived.");
}

static void LocalizationFault()
{
    using var f = new Fixture(); var a = f.Start("a"); var b = f.Start("b"); int calls = 0;
    a.Localization.Subscribe(_ => throw new InvalidOperationException("bad language subscriber")); b.Localization.Subscribe(_ => calls++);
    var result = f.Localization.SetLanguage("fr");
    Check(result.Success && f.State("a") == PluginState.Faulted && f.State("b") == PluginState.Enabled && calls == 1, "Language fault prevented unrelated delivery.");
    f.Localization.SetLanguage("de"); Check(calls == 2, "Subsequent valid language changes broken by fault.");
}

static void WrongThread()
{
    using var f = new Fixture(); var a = f.Start("a");
    var errors = Task.Run(() => new[] { a.Storage.GetQuota().Error?.Code, a.Localization.Resolve("key").Error?.Code, f.Localization.SetLanguage("fr").Error?.Code }).GetAwaiter().GetResult();
    Check(errors.All(x => x == ErrorCode.WrongThread), "Wrong thread did not return structured rejection.");
}

sealed record Settings(int Count);
sealed record TextSettings(string Text);
sealed class DynamicTextSettings
{
    private readonly Func<string> _getText;
    public DynamicTextSettings(Func<string> getText) => _getText = getText;
    public string Text => _getText();
}
sealed record Services(IPluginStorageService Storage, IPluginLocalizationService Localization);
sealed class Probe : IRizPlugin
{
    private readonly Action<IPluginContext> _start; public PluginManifest Manifest { get; }
    public Probe(string id, Action<IPluginContext> start) => (Manifest, _start) = (new(id, new(1, 0, 0), new(1, 0, 0)), start);
    public void Enable(IPluginContext context) => _start(context);
    public void Disable() { }
}
sealed class TestLogger : ILogger { public void Log(LogLevel level, string message, Exception? exception = null) { } }
sealed class Fixture : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "riz-storage-tests-" + Guid.NewGuid().ToString("N"));
    public PluginHost Host { get; } public PluginStorageManager Storage { get; } public PluginLocalizationManager Localization { get; } = new();
    private readonly Dictionary<string, Services> _latest = new(StringComparer.Ordinal);
    public Fixture(long quota = 16 * 1024 * 1024, int maximumFile = 1024 * 1024)
    {
        Directory.CreateDirectory(Path); var logger = new TestLogger(); var capabilities = new CapabilityRegistry(new("test", "test", "test", "test"));
        Host = new(capabilities, new MainThreadDispatcher(), new EventHub(logger), logger, Path); Storage = new(Path, quota, maximumFile);
    }
    public string Config(string id, string name) => System.IO.Path.Combine(Path, "storage", "plugin-" + id, "configuration", name + ".json");
    public Services Start(string id)
    {
        var registered = Host.Register(new Probe(id, context => _latest[id] = new(Storage.ForPlugin(context), Localization.ForPlugin(context))));
        if (!registered.Success || !Host.Enable(id).Success) throw new InvalidOperationException("Cannot start test plugin " + id);
        return Latest(id);
    }
    public Services Latest(string id) => _latest[id];
    public PluginState State(string id) => Host.Statuses.Single(x => x.Id == id).State;
    public void Dispose()
    {
        Host.Dispose(); string full = System.IO.Path.GetFullPath(Path);
        if (!full.StartsWith(System.IO.Path.GetTempPath(), StringComparison.OrdinalIgnoreCase) || !System.IO.Path.GetFileName(full).StartsWith("riz-storage-tests-")) throw new InvalidOperationException("Unsafe cleanup.");
        Directory.Delete(full, true);
    }
}
