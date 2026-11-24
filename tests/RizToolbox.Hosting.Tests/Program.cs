using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using RizToolbox.Hosting;
using RizToolbox.Sdk;

var checks = new (string Name, Action Test)[]
{
    ("same-named entry and private libraries coexist in independent contexts with shared SDK", Isolation),
    ("directory order is deterministic and entry constructors run once", Determinism),
    ("metadata discovery runs no plugin code and deferred load consumes the same candidates once", MetadataDiscovery),
    ("deferred load rejects changed metadata, assemblies and inventory before running constructors", DiscoverySnapshotIntegrity),
    ("discovery diagnostics survive deferred load without being logged twice", DiscoveryDiagnostics),
    ("host selection can refuse all or selected constructors while retaining discovery diagnostics", DiscoverySelection),
    ("malformed and duplicate JSON properties are rejected before loading code", InvalidJson),
    ("missing entry, absolute paths and both traversal separators are rejected", InvalidPaths),
    ("invalid entry types are isolated from valid neighbors", InvalidTypes),
    ("ID, version, dependency and capability code metadata must match", MetadataMismatch),
    ("SDK ranges, dependency ranges and duplicate plugin IDs have independent diagnostics", MetadataValidation),
    ("missing and mismatched private dependency rejected before constructor", PrivateDependencyValidation),
    ("package SDK and renamed host assemblies are forbidden", ProtectedAssemblies),
    ("framework assembly replacement and duplicate private assembly rejected", FrameworkAndDuplicates),
    ("private dynamic dependency cannot fall back into default context", NoDefaultFallback),
    ("constructor fault and failing logger do not stop other packages", FaultIsolation),
    ("flat legacy DLLs are explicitly diagnosed without implicit loading", LegacyDiagnostic),
    ("reparse point packages are rejected when supported by the filesystem", ReparsePoints),
    ("missing roots and idempotent result disposal", ResultLifetime),
    ("CoreCLR platform catalogue works without TPA and rejects wrong/private identities", PlatformWithoutTpa),
    ("missing required plugin package stays loaded for host waiting and late readiness", WaitingPackage),
    ("extended package metadata round trips optional dependencies, conflicts and order", ExtendedManifest),
    ("extended metadata rejects cross-category duplicates, self references and bad ranges", InvalidExtendedManifest),
    ("package disk changes require restart and never overwrite updated user files", PackageUpdateDiagnostics)
};
int failures = 0;
foreach (var check in checks)
{
    try { check.Test(); Console.WriteLine($"PASS {check.Name}"); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {check.Name}: {ex}"); }
}
Console.WriteLine($"{checks.Length - failures}/{checks.Length} hosting checks passed.");
return failures == 0 ? 0 : 1;

static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static string Proof(IRizPlugin plugin) => (string)plugin.GetType().GetProperty("Proof")!.GetValue(plugin)!;
static void Error(PluginLoadResult result, string code, int count = 1)
    => Check(result.Diagnostics.Count(x => x.Code == code) == count,
        $"Expected {count} {code}; actual: {string.Join("; ", result.Diagnostics.Select(x => x.Code + ":" + x.Message))}");

static void Isolation()
{
    using var root = new TempRoot();
    root.Package("a", 1); root.Package("b", 2);
    using var result = root.Load();
    Check(result.Diagnostics.Count == 0 && result.Plugins.Count == 2, "Both isolated packages must load.");
    Check(Proof(result.Plugins[0]) == "private-v1" && Proof(result.Plugins[1]) == "private-v2", "Private library identities leaked between packages.");
    var one = AssemblyLoadContext.GetLoadContext(result.Plugins[0].GetType().Assembly)!;
    var two = AssemblyLoadContext.GetLoadContext(result.Plugins[1].GetType().Assembly)!;
    Check(one != two && one != AssemblyLoadContext.Default && two != AssemblyLoadContext.Default, "Packages share a context.");
    Check(one.Assemblies.Single(x => x.GetName().Name == "IsolationPrivate").GetName().Version!.Major == 1 &&
        two.Assemblies.Single(x => x.GetName().Name == "IsolationPrivate").GetName().Version!.Major == 2, "Wrong private dependency version.");
    Check(result.Plugins.All(p => p.GetType().GetInterfaces().Contains(typeof(IRizPlugin))), "SDK identity was not shared.");
    Check(one.Assemblies.All(x => x != typeof(IRizPlugin).Assembly), "SDK was copied into package context.");
}

static void Determinism()
{
    using var root = new TempRoot(); root.Package("z", 2); root.Package("a", 1);
    using var result = root.Load();
    Check(result.Plugins.Select(x => x.Manifest.Id).SequenceEqual(new[] { "package.one", "package.two" }), "Unexpected package order.");
    Check(result.Plugins.All(x => (int)x.GetType().GetField("Constructions")!.GetValue(null)! == 1), "Entry constructed more than once.");
}

static void MetadataDiscovery()
{
    using var root = new TempRoot();
    var folder = root.Package("one", 1, entryType: "IsolationFixture.DiscoveryConstructorPlugin");
    var marker = Path.Combine(root.Path, "constructor-count.txt");
    var previous = Environment.GetEnvironmentVariable("RIZ_HOSTING_CONSTRUCTOR_PROBE");
    try
    {
        Environment.SetEnvironmentVariable("RIZ_HOSTING_CONSTRUCTOR_PROBE", marker);
        var discovery = PluginPackageLoader.DiscoverDirectory(root.Path, new SilentLogger());
        Check(discovery.Diagnostics.Count == 0 && discovery.Packages.Count == 1, "Valid metadata was not discovered.");
        var package = discovery.Packages.Single();
        Check(package.DirectoryPath == folder && package.Metadata.Manifest.Id == "package.one", "Discovery lost package metadata.");
        Check(package.Fingerprints.Keys.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(new[] { "IsolationPlugin.dll", "IsolationPrivate.dll", "plugin.json" }), "Discovery did not retain the validated file snapshot.");
        Check(!File.Exists(marker), "Discovery executed the entry constructor.");
        Check(!AssemblyLoadContext.All.SelectMany(x => x.Assemblies).Any(x => x.Location == Path.Combine(folder, "IsolationPlugin.dll")), "Discovery loaded the entry assembly.");
        var dictionary = (IDictionary<string, string>)package.Fingerprints;
        Check(dictionary.IsReadOnly, "Callers can alter the approved fingerprint snapshot.");

        // A later directory must not silently become part of this planned batch.
        root.Package("later", 2);
        using var result = PluginPackageLoader.LoadDiscovered(discovery, new SilentLogger());
        Check(result.Diagnostics.Count == 0 && result.Plugins.Count == 1 && result.Plugins[0].Manifest.Id == "package.one", "Deferred load rediscovered candidates instead of loading the planned batch.");
        Check(File.ReadAllLines(marker).Length == 1 && (int)result.Plugins[0].GetType().GetField("Constructions", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)!.GetValue(null)! == 1, "Deferred load did not execute exactly one entry constructor.");
        Check(Proof(result.Plugins[0]) == "private-v1", "Deferred load lost the inspected private dependency identity.");
    }
    finally { Environment.SetEnvironmentVariable("RIZ_HOSTING_CONSTRUCTOR_PROBE", previous); }
}

static void DiscoverySnapshotIntegrity()
{
    Action<string>[] mutations =
    {
        folder => File.AppendAllText(Path.Combine(folder, "plugin.json"), "\n"),
        folder => File.Copy(TempRoot.Fixture(2, "IsolationPlugin.dll"), Path.Combine(folder, "IsolationPlugin.dll"), true),
        folder => File.Copy(TempRoot.Fixture(2, "IsolationPrivate.dll"), Path.Combine(folder, "IsolationPrivate.dll"), true),
        folder => File.WriteAllText(Path.Combine(folder, "added.txt"), "new file"),
        folder => File.Delete(Path.Combine(folder, "IsolationPrivate.dll"))
    };
    var previous = Environment.GetEnvironmentVariable("RIZ_HOSTING_CONSTRUCTOR_PROBE");
    try
    {
        foreach (var mutate in mutations)
        {
            using var root = new TempRoot();
            var folder = root.Package("one", 1, entryType: "IsolationFixture.DiscoveryConstructorPlugin");
            root.Package("two", 2);
            var marker = Path.Combine(root.Path, "constructor-count.txt");
            Environment.SetEnvironmentVariable("RIZ_HOSTING_CONSTRUCTOR_PROBE", marker);
            var discovery = PluginPackageLoader.DiscoverDirectory(root.Path, new SilentLogger());
            Check(discovery.Diagnostics.Count == 0 && discovery.Packages.Count == 2, "Snapshot fixture discovery failed.");
            mutate(folder);
            using var result = PluginPackageLoader.LoadDiscovered(discovery, new SilentLogger());
            Error(result, "package-changed");
            Check(result.Plugins.Count == 1 && result.Plugins[0].Manifest.Id == "package.two", "Changed package loaded or blocked an unchanged neighbor.");
            Check(!File.Exists(marker), "Changed package executed its constructor before rejection.");
        }
    }
    finally { Environment.SetEnvironmentVariable("RIZ_HOSTING_CONSTRUCTOR_PROBE", previous); }
}

static void DiscoveryDiagnostics()
{
    using var root = new TempRoot();
    root.Package("one", 1); root.Package("duplicate", 1);
    var logger = new CountingLogger();
    var discovery = PluginPackageLoader.DiscoverDirectory(root.Path, logger);
    Check(discovery.Diagnostics.Count(x => x.Code == "duplicate-id") == 1 && logger.Errors == 1, "Discovery did not report its own validation error.");
    using var result = PluginPackageLoader.LoadDiscovered(discovery, logger);
    Error(result, "duplicate-id");
    Check(result.Plugins.Count == 1 && logger.Errors == 1, "Deferred load dropped or logged the discovery error again.");
}

static void DiscoverySelection()
{
    using var root = new TempRoot();
    root.Package("one", 1, entryType: "IsolationFixture.DiscoveryConstructorPlugin");
    root.Package("two", 2);
    var invalid = root.Package("invalid", 1); File.WriteAllText(Path.Combine(invalid, "plugin.json"), "{ invalid");
    var marker = Path.Combine(root.Path, "constructor-count.txt");
    var previous = Environment.GetEnvironmentVariable("RIZ_HOSTING_CONSTRUCTOR_PROBE");
    try
    {
        Environment.SetEnvironmentVariable("RIZ_HOSTING_CONSTRUCTOR_PROBE", marker);
        var discovery = PluginPackageLoader.DiscoverDirectory(root.Path, new SilentLogger());
        using var refused = PluginPackageLoader.LoadDiscovered(discovery, new SilentLogger(), _ => false);
        Error(refused, "manifest-invalid");
        Check(refused.Plugins.Count == 0 && !File.Exists(marker), "Refused discovery executed a constructor or lost diagnostics.");
        using var selected = PluginPackageLoader.LoadDiscovered(discovery, new SilentLogger(), package => package.Metadata.Manifest.Id == "package.two");
        Error(selected, "manifest-invalid");
        Check(selected.Plugins.Count == 1 && selected.Plugins[0].Manifest.Id == "package.two" && !File.Exists(marker), "Package selection executed an excluded constructor.");
    }
    finally { Environment.SetEnvironmentVariable("RIZ_HOSTING_CONSTRUCTOR_PROBE", previous); }
}

static void InvalidJson()
{
    using var root = new TempRoot();
    var a = root.Package("bad-json", 1); File.WriteAllText(Path.Combine(a, "plugin.json"), "{ nope");
    var b = root.Package("duplicate-field", 1); File.WriteAllText(Path.Combine(b, "plugin.json"), "{\"id\":\"a\",\"id\":\"b\"}");
    root.Package("valid", 2);
    using var result = root.Load(); Error(result, "manifest-invalid", 2); Check(result.Plugins.Count == 1, "Malformed package blocked valid neighbor.");
}

static void InvalidPaths()
{
    using var root = new TempRoot();
    var paths = new[] { "../IsolationPlugin.dll", "..\\IsolationPlugin.dll", "C:/outside.dll", "/outside.dll", "nested/../IsolationPlugin.dll", "missing.dll", "./IsolationPlugin.dll", "file.dll:stream" };
    for (int i = 0; i < paths.Length; i++) root.Package(i.ToString(), 1, entryAssembly: paths[i]);
    using var result = root.Load(); Error(result, "path-invalid", paths.Length); Check(result.Plugins.Count == 0, "Bad paths loaded.");
}

static void InvalidTypes()
{
    using var root = new TempRoot();
    string[] types = { "Absent", "WrongType", "AbstractPlugin", "InternalPlugin", "GenericPlugin`1", "NoDefaultConstructor" };
    // Distinct IDs would intentionally mismatch code if type checks accidentally reached construction.
    for (int i = 0; i < types.Length; i++) root.Package(i.ToString(), 1, entryType: "IsolationFixture." + types[i], id: "bad." + i);
    root.Package("valid", 2);
    using var result = root.Load(); Error(result, "entry-type", types.Length); Check(result.Plugins.Count == 1, "Invalid type blocked good package.");
}

static void MetadataMismatch()
{
    foreach (var type in new[] { "ManifestMismatch", "VersionMismatch", "DependencyMismatch", "CapabilityMismatch" })
    {
        using var root = new TempRoot(); root.Package("bad", 1, entryType: "IsolationFixture." + type); root.Package("valid", 2);
        using var result = root.Load(); Error(result, "manifest-mismatch"); Check(result.Plugins.Count == 1, "Mismatch blocked other package.");
    }
}

static void MetadataValidation()
{
    using var root = new TempRoot();
    root.Package("01-valid", 1); root.Package("02-duplicate", 1);
    root.Package("03-sdk", 2, minSdk: "2.0.0");
    root.Package("04-plugin-dependency", 2, dependencies: new[] { new { id = "absent.plugin", minVersion = "1.0.0" } });
    root.Package("05-range", 2, dependencies: new[] { new { id = "package.one", minVersion = "3.0.0", maxVersionExclusive = "2.0.0" } });
    using var result = root.Load();
    Error(result, "duplicate-id"); Error(result, "sdk-incompatible"); Error(result, "plugin-dependency"); Error(result, "manifest-invalid");
    Check(result.Plugins.Count == 1, "Unexpected metadata validation result.");
}

static void PrivateDependencyValidation()
{
    using var root = new TempRoot();
    var missing = root.Package("a-missing", 1); File.Delete(Path.Combine(missing, "IsolationPrivate.dll"));
    var wrong = root.Package("b-wrong", 2); File.Copy(TempRoot.Fixture(1, "IsolationPrivate.dll"), Path.Combine(wrong, "IsolationPrivate.dll"), true);
    using var result = root.Load(); Error(result, "dependency-missing"); Error(result, "dependency-version"); Check(result.Plugins.Count == 0, "Bad dependency accepted.");
}

static void ProtectedAssemblies()
{
    using var root = new TempRoot();
    var a = root.Package("sdk", 1); File.Copy(typeof(IRizPlugin).Assembly.Location, Path.Combine(a, "RizToolbox.Sdk.dll"));
    var b = root.Package("renamed-host", 2); File.Copy(typeof(PluginPackageLoader).Assembly.Location, Path.Combine(b, "innocent.dll"));
    using var result = root.Load(); Error(result, "host-replacement", 2); Check(result.Plugins.Count == 0, "Host assembly replacement accepted.");
}

static void FrameworkAndDuplicates()
{
    using var root = new TempRoot();
    var a = root.Package("framework", 1); File.Copy(typeof(object).Assembly.Location, Path.Combine(a, "innocent.dll"));
    var b = root.Package("duplicate", 2); File.Copy(Path.Combine(b, "IsolationPrivate.dll"), Path.Combine(b, "copy.dll"));
    using var result = root.Load(); Error(result, "host-replacement"); Error(result, "assembly-duplicate");
}

static void NoDefaultFallback()
{
    var defaultAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(AppContext.BaseDirectory, "Fixtures", "default", "HostPrivateOnly.dll"));
    Check(defaultAssembly.GetName().Name == "HostPrivateOnly", "Default fixture not loaded.");
    using var root = new TempRoot(); root.Package("dynamic", 1, entryType: "IsolationFixture.DynamicDefaultDependency"); root.Package("valid", 2);
    using var result = root.Load(); Error(result, "package-load");
    Check(result.Plugins.Count == 1 && result.Diagnostics[0].Message.Contains("HostPrivateOnly"), "Private import fell back to Default.");
}

static void FaultIsolation()
{
    using var root = new TempRoot(); root.Package("bad", 1, entryType: "IsolationFixture.Throwing"); root.Package("valid", 2);
    using var result = PluginPackageLoader.LoadDirectory(root.Path, new ThrowingLogger());
    Error(result, "package-load"); Check(result.Plugins.Count == 1, "Constructor or logger fault broke independent load.");
}

static void LegacyDiagnostic()
{
    using var root = new TempRoot(); File.Copy(TempRoot.Fixture(1, "IsolationPlugin.dll"), Path.Combine(root.Path, "legacy.dll")); root.Package("valid", 2);
    using var result = root.Load(); Error(result, "legacy-flat-unsupported"); Check(result.Plugins.Count == 1, "Legacy file affected good package.");
}

static void ReparsePoints()
{
    using var root = new TempRoot(); using var target = new TempRoot(); var directory = target.Package("real", 1);
    var link = Path.Combine(root.Path, "linked");
    try { Directory.CreateSymbolicLink(link, directory); }
    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
    { Console.WriteLine("SKIP symbolic link creation requires unavailable OS privilege: " + ex.Message); return; }
    try { using var result = root.Load(); Error(result, "path-invalid"); Check(result.Plugins.Count == 0, "Reparse package loaded."); }
    finally { Directory.Delete(link); }
}

static void ResultLifetime()
{
    PluginLoadResult.Empty.Dispose(); PluginLoadResult.Empty.Dispose();
    Check(PluginLoadResult.Empty.Plugins.Count == 0 && PluginLoadResult.Empty.Packages.Count == 0 && PluginLoadResult.Empty.Diagnostics.Count == 0, "Shared empty result retained mutable lifetime state.");
    using var root = new TempRoot(); using var empty = PluginPackageLoader.LoadDirectory(Path.Combine(root.Path, "missing"), new SilentLogger());
    Check(empty.Plugins.Count == 0 && empty.Diagnostics.Count == 0, "Absent mods directory should be empty.");
    root.Package("valid", 1); var result = root.Load(); bool unloading = false;
    AssemblyLoadContext.GetLoadContext(result.Plugins[0].GetType().Assembly)!.Unloading += _ => unloading = true;
    result.Dispose(); result.Dispose(); Check(unloading, "Disposal did not request context unloading.");
}

static void PlatformWithoutTpa()
{
    var previous = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
    try
    {
        AppContext.SetData("TRUSTED_PLATFORM_ASSEMBLIES", null);
        var platform = FrameworkAssemblyCatalog.Create(typeof(object).Assembly.Location);
        var reference = new AssemblyName("System.Runtime, Version=6.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a");
        Check(platform.Contains("System.Runtime") && platform.IsCompatible(reference), "Missing-TPA runtime catalogue did not include actual System.Runtime.");
        var loaded = platform.Load(reference);
        Check(System.IO.Path.GetDirectoryName(loaded.Location) == System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location), "Framework import escaped the actual runtime directory.");
        Check(!platform.Contains("HostPrivateOnly") && !platform.Contains("RizToolbox.Hosting") && !platform.Contains("System.Fake.PrivateLibrary"), "Application/private DLL was promoted to framework import.");
        Check(!platform.IsCompatible(new AssemblyName("System.Runtime, Version=99.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")), "Unsupported future framework version accepted.");
        Check(!platform.IsCompatible(new AssemblyName("System.Runtime, Version=6.0.0.0, Culture=neutral, PublicKeyToken=0011223344556677")), "Wrong framework public key accepted.");
        using var root = new TempRoot(); root.Package("a", 1); root.Package("b", 2);
        using var result = root.Load();
        Check(result.Plugins.Count == 2 && result.Diagnostics.Count == 0 && Proof(result.Plugins[1]) == "private-v2", "Missing-TPA package discovery failed.");
    }
    finally { AppContext.SetData("TRUSTED_PLATFORM_ASSEMBLIES", previous); }
}

static void WaitingPackage()
{
    using var root = new TempRoot();
    root.Package("waiting", 1, entryType: "IsolationFixture.WaitingRequiredPlugin", dependencies: new[] { new { id = "absent.plugin", minVersion = "1.0.0" } });
    using var result = root.Load(); Error(result, "plugin-dependency");
    Check(result.Plugins.Count == 1 && result.Plugins[0].Manifest.Dependencies!.Single().Id == "absent.plugin", "Valid waiting package was discarded before PluginHost could register it.");
}

static void ExtendedManifest()
{
    using var root = new TempRoot();
    var folder = root.Package("extended", 1, entryType: "IsolationFixture.ExtendedManifestPlugin"); root.Package("valid", 2);
    AddManifestProperty(folder, "optionalDependencies", new[] { new { id = "package.two", minVersion = "2.0.0", maxVersionExclusive = "3.0.0" } });
    AddManifestProperty(folder, "conflicts", new[] { new { id = "mutually.exclusive", minVersion = "1.0.0", maxVersionExclusive = "2.0.0" } });
    AddManifestProperty(folder, "loadAfter", new[] { "package.two" });
    using var result = root.Load();
    Check(result.Diagnostics.Count == 0 && result.Plugins.Count == 2 && result.Plugins[0].Manifest.OptionalDependencies!.Count == 1 && result.Plugins[0].Manifest.Conflicts!.Count == 1, "Extended manifest did not load/match code.");
}

static void InvalidExtendedManifest()
{
    using var root = new TempRoot();
    var a = root.Package("a", 1, dependencies: new[] { new { id = "provider", minVersion = "1.0.0" } });
    AddManifestProperty(a, "optionalDependencies", new[] { new { id = "provider", minVersion = "1.0.0" } });
    var b = root.Package("b", 1); AddManifestProperty(b, "conflicts", new[] { new { id = "package.one" } });
    var c = root.Package("c", 1); AddManifestProperty(c, "loadAfter", new[] { "package.one" });
    var d = root.Package("d", 1); AddManifestProperty(d, "optionalDependencies", new[] { new { id = "provider", minVersion = "3.0.0", maxVersionExclusive = "2.0.0" } });
    using var result = root.Load(); Error(result, "manifest-invalid", 4); Check(result.Plugins.Count == 0, "Invalid expanded manifest accepted.");
}

static void AddManifestProperty<T>(string folder, string name, T value)
{
    var file = Path.Combine(folder, "plugin.json");
    var properties = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(file))!;
    properties[name] = JsonSerializer.SerializeToElement(value);
    File.WriteAllText(file, JsonSerializer.Serialize(properties));
}

static void PackageUpdateDiagnostics()
{
    using var root = new TempRoot(); var folder = root.Package("one", 1);
    using var result = root.Load(); var package = result.Packages.Single();
    Check(!package.CheckForUpdates().RestartRequired, "Unchanged package requires restart.");
    string metadata = Path.Combine(folder, "plugin.json"); var original = File.ReadAllText(metadata);
    File.WriteAllText(metadata, original + "\n");
    var status = package.CheckForUpdates();
    Check(status.RestartRequired && status.ChangedFiles.SequenceEqual(new[] { "plugin.json" }) && status.Message.Contains("ownership"), "Package changed without explicit restart/ownership guidance.");
    Check(File.ReadAllText(metadata) == original + "\n" && package.Plugin.Manifest.Id == "package.one", "Disk check rewrote/reloaded package.");
    File.WriteAllText(metadata, original); File.WriteAllText(Path.Combine(folder, "added.txt"), "new file");
    Check(package.CheckForUpdates().ChangedFiles.SequenceEqual(new[] { "added.txt" }), "New package file not reported.");
}

sealed class SilentLogger : ILogger { public void Log(LogLevel level, string message, Exception? exception = null) { } }
sealed class ThrowingLogger : ILogger { public void Log(LogLevel level, string message, Exception? exception = null) => throw new IOException("logger fixture fault"); }
sealed class CountingLogger : ILogger
{
    public int Errors { get; private set; }
    public void Log(LogLevel level, string message, Exception? exception = null) { if (level == LogLevel.Error) Errors++; }
}

sealed class TempRoot : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "riz-hosting-tests-" + Guid.NewGuid().ToString("N"));
    public TempRoot() => Directory.CreateDirectory(Path);
    public static string Fixture(int version, string file) => System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "v" + version, file);
    public PluginLoadResult Load() => PluginPackageLoader.LoadDirectory(Path, new SilentLogger());
    public string Package(string folder, int version, string? entryAssembly = null, string? entryType = null,
        string? id = null, string minSdk = "1.0.0", object? dependencies = null)
    {
        var directory = System.IO.Path.Combine(Path, folder); Directory.CreateDirectory(directory);
        File.Copy(Fixture(version, "IsolationPlugin.dll"), System.IO.Path.Combine(directory, "IsolationPlugin.dll"));
        File.Copy(Fixture(version, "IsolationPrivate.dll"), System.IO.Path.Combine(directory, "IsolationPrivate.dll"));
        File.WriteAllText(System.IO.Path.Combine(directory, "plugin.json"), JsonSerializer.Serialize(new
        {
            id = id ?? (version == 1 ? "package.one" : "package.two"), version = version + ".0.0",
            entryAssembly = entryAssembly ?? "IsolationPlugin.dll", entryType = entryType ?? "IsolationFixture.Plugin", minSdk,
            dependencies = dependencies ?? Array.Empty<object>(), requiredCapabilities = Array.Empty<string>()
        }));
        return directory;
    }
    public void Dispose()
    {
        var resolved = System.IO.Path.GetFullPath(Path);
        if (!resolved.StartsWith(System.IO.Path.GetTempPath(), StringComparison.OrdinalIgnoreCase) || !System.IO.Path.GetFileName(resolved).StartsWith("riz-hosting-tests-"))
            throw new InvalidOperationException("Refusing unsafe temporary cleanup.");
        try { Directory.Delete(resolved, recursive: true); }
        catch (IOException) { /* Windows may retain collectible assembly file handles until GC; cleanup is best effort. */ }
        catch (UnauthorizedAccessException) { }
    }
}
