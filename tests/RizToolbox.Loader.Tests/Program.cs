using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using System.Text.Json;
using RizToolbox.Adapter;
using RizToolbox.Core;
using RizToolbox.Core.Diagnostics;
using RizToolbox.Core.Localization;
using RizToolbox.Core.Services;
using RizToolbox.Hosting;
using RizToolbox.Loader;
using RizToolbox.Sdk;
using static Helpers;

var executableDirectory = Path.GetDirectoryName(Path.GetFullPath(Environment.ProcessPath!))!;
if (!string.Equals(executableDirectory, Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)),
        StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Run the built HostChecks.exe apphost so Bootstrap.DataRoot stays under this test's bin directory.");
if (args.Length == 1 && args[0] == "--background-play-review-checks")
    return BackgroundPlayWindowChecks.Run();
Directory.CreateDirectory(Path.Combine(executableDirectory, "BepInEx", "RizToolbox"));

var checks = new (string Name, Action Run)[]
{
    ("queued shutdown stops the frame and releases remaining owners after a binding fault", CommandShutdown),
    ("package disk snapshots publish through the dispatcher and stop after shutdown", PackageUpdates),
    ("full command queue retries the same ID after capacity returns", QueueFullCommandRetry),
    ("host drains existing and command generated boundaries before observation without duplicate delivery", CommandBoundary),
    ("a managed boundary or adapter fault leaves command and frame phases active", ManagedPhaseFault),
    ("malformed command does not starve the Developer five-second package scan", MalformedCommandScan),
    ("Standard command worker does not periodically hash changed packages", StandardNoPeriodicScan),
    ("invalid startup keeps host diagnostics without adapter or plugin execution and preserves user files", InvalidStartup),
    ("bundled proof package policy does not classify arbitrary example IDs as validation", BundledProofPolicy),
    ("unsupported installation publishes startup failure and leaves optional writes disabled", UnsupportedInstallation),
    ("startup receipt distinguishes attachment, host construction and the first returned tick", StartupMilestones)
}.Concat(CommandMailboxChecks.All).Concat(LatestDiagnosticWriterChecks.All).Concat(HostStatusDiagnosticChecks.All)
    .Concat(DiagnosticSelectionChecks.All).ToArray();
var failures = 0;
foreach (var check in checks)
{
    try { check.Run(); Console.WriteLine("PASS " + check.Name); }
    catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + check.Name + ": " + ex); }
}
Console.WriteLine($"{checks.Length - failures}/{checks.Length} loader host checks passed.");
return failures == 0 ? 0 : 1;

static void CommandShutdown()
{
    using var h = new HostHarness(package: false, failingBinding: true);
    h.Tick();
    Check(h.Adapter.Ticks == 1 && h.Frames == 1, "The live host baseline must tick and publish one frame.");

    var command = Queue(h, () => Reflect.Call(h.Host, "Execute", "shutdown", null, null), h.Shutdown.Token);
    WaitPending(h.Main, 1);
    var cancelledByToken = Queue(h, () => h.Followups++, h.Shutdown.Token);
    var cancelledByDispose = Queue(h, () => h.Followups++);
    WaitPending(h.Main, 3);

    h.Tick(); // TickCore must return immediately after Pump executes the queued command.
    command.GetAwaiter().GetResult();
    Check(SpinWait.SpinUntil(() => cancelledByToken.IsCompleted && cancelledByDispose.IsCompleted,
        TimeSpan.FromSeconds(5)), "Queued follow-ups did not finish cancellation.");
    Check(cancelledByToken.IsCanceled && cancelledByDispose.IsCanceled && h.Followups == 0,
        "Shutdown must cancel both command-token work and pending dispatcher work.");
    Check(h.Main.GetSnapshot().Cancelled >= 2, "Dispatcher did not record both cancellations.");
    Check(h.Adapter.Ticks == 1 && h.Frames == 1 && h.Adapter.Disposals == 1,
        "The shutdown frame must not tick the adapter or publish FrameUpdate; adapter must release once.");
    Check(h.Binding!.Disposals == 1, "The failing activation binding was not attempted exactly once.");
    var flushes = h.Adapter.Flushes;
    h.Tick();
    Reflect.Call(h.Host, "Shutdown", false, "duplicate");
    Check(h.Adapter.Ticks == 1 && h.Adapter.Flushes == flushes && h.Frames == 1 && h.Adapter.Disposals == 1,
        "Stopped ticks and duplicate shutdown must be inert.");

    using var status = h.Status();
    var root = status.RootElement;
    Check(root.GetProperty("stopped").GetBoolean() && root.GetProperty("stopReason").GetString() == "command",
        "Final status must record command shutdown.");
    var errors = root.GetProperty("shutdownFailures").EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
    Check(errors.Length == 1 && errors[0].Contains("activation binding") && errors[0].Contains("binding sentinel"),
        "Binding failure must be retained in final status while later owners are released.");
    Check(!errors.Any(x => x.Contains("application quit")), "Command shutdown must not call Application.Quit.");
}

static void PackageUpdates()
{
    using var h = new HostHarness(package: true, failingBinding: false);
    var loaded = h.Packages.Packages.Single();
    var plugin = loaded.Plugin;
    var context = AssemblyLoadContext.GetLoadContext(plugin.GetType().Assembly)!;
    var unloads = 0;
    context.Unloading += _ => unloads++;
    h.PublishScan();
    AssertUpdate(h, false);

    var manifest = Path.Combine(h.PackageDirectory!, "plugin.json");
    var original = File.ReadAllText(manifest);
    File.WriteAllText(manifest, original + "\n");
    h.Tick(); // A game frame must not rescan the disk.
    h.WriteStatus();
    AssertUpdate(h, false);
    h.PublishScan();
    AssertUpdate(h, true, "plugin.json");
    Check(File.ReadAllText(manifest) == original + "\n" && ReferenceEquals(plugin, loaded.Plugin),
        "Disk scan changed or reloaded the loaded package.");

    File.WriteAllText(manifest, original);
    h.PublishScan();
    AssertUpdate(h, false);
    var added = Path.Combine(h.PackageDirectory!, "added.txt");
    File.WriteAllText(added, "new file");
    h.PublishScan();
    AssertUpdate(h, true, "added.txt");
    File.Delete(added);
    h.PublishScan();
    AssertUpdate(h, false);
    File.Delete(manifest);
    h.PublishScan();
    AssertUpdate(h, true, "plugin.json");
    File.WriteAllText(manifest, original);
    h.PublishScan();
    AssertUpdate(h, false);

    Reflect.Call(h.Host, "Execute", "shutdown", null, null);
    Check(unloads == 1 && h.Adapter.Disposals == 1, "Package context and adapter must release on shutdown.");
    var rejected = Task.Run(() => Reflect.Call(h.Host, "ScanPackageUpdates")).GetAwaiter().GetResult();
    Check(rejected is null, "Stopped host must reject another package disk scan.");
    using var status = h.Status();
    Check(status.RootElement.GetProperty("stopped").GetBoolean() &&
          !status.RootElement.GetProperty("packageUpdates")[0].GetProperty("restartRequired").GetBoolean(),
        "Final status must preserve the last published, restored package snapshot.");
}

static void QueueFullCommandRetry()
{
    using var h = new HostHarness(package: false, failingBinding: false, maxPending: 1);
    var id = "queue-retry-" + Guid.NewGuid().ToString("N");
    File.WriteAllText(h.CommandFile, JsonSerializer.Serialize(new { id, action = "shutdown" }));
    var placeholder = Queue(h, () => h.Followups++);
    WaitPending(h.Main, 1);
    var loop = h.StartWatchCommands();
    Check(SpinWait.SpinUntil(() => h.Main.GetSnapshot().Rejected >= 1, TimeSpan.FromSeconds(3)),
        "The actual command loop did not encounter the full dispatcher queue.");
    Check(!h.Stopped && h.Followups == 0, "Command ran before the queued placeholder was pumped.");

    h.Tick();
    placeholder.GetAwaiter().GetResult();
    Check(PumpUntil(h, () => h.Stopped, TimeSpan.FromSeconds(5)),
        "The same command ID was not retried after queue capacity returned.");
    Check(SpinWait.SpinUntil(() => loop.IsCompleted, TimeSpan.FromSeconds(2)) && loop.IsCompletedSuccessfully,
        "Command loop did not exit after shutdown.");
    Check(h.Followups == 1 && h.Adapter.Disposals == 1 && h.StopReason == "command" &&
        (string?)Reflect.Get(h.Host, "_lastCommand") == id,
        "Retried command was not executed and acknowledged exactly once.");
}

static void CommandBoundary()
{
    using var h = new HostHarness(package: false, failingBinding: false);
    h.Adapter.EmitBoundary();
    var command = Queue(h, () =>
    {
        Check(h.Adapter.DeliveredBoundaries == 1, "Queued command ran before existing boundary was drained.");
        h.Adapter.EmitBoundary();
    });
    WaitPending(h.Main, 1);
    h.Adapter.OnPumpCommands = () =>
    {
        h.Adapter.OnPumpCommands = null;
        h.Adapter.EmitBoundary();
    };
    h.Tick();
    command.GetAwaiter().GetResult();
    Check(h.Adapter.ObservedBoundary == 3 && h.Adapter.DeliveredBoundaries == 3 && h.Frames == 1 && h.Adapter.HostTickSequence == 1,
        "The command boundary was not observed in the same frame.");
    h.Tick();
    Check(h.Adapter.ObservedBoundary == 3 && h.Adapter.DeliveredBoundaries == 3 && h.Frames == 2 && h.Adapter.HostTickSequence == 2,
        "The next host tick delivered a boundary again.");
}

static void ManagedPhaseFault()
{
    using var h = new HostHarness(package: false, failingBinding: false);
    h.Adapter.EmitBoundary();
    h.Adapter.OnFlush = count => { if (count == 1) throw new IOException("first boundary pass"); };
    var commands = 0;
    var command = Queue(h, () => { commands++; h.Adapter.EmitBoundary(); });
    WaitPending(h.Main, 1);
    h.Adapter.OnTick = sequence => { if (sequence == 1) throw new IOException("adapter phase"); };
    h.Tick(); command.GetAwaiter().GetResult();
    Check(commands == 1 && h.Adapter.DeliveredBoundaries == 2 && h.Adapter.Ticks == 1 && h.Frames == 1,
        "A fault skipped command, second boundary, or independent frame dispatch.");
    h.Tick();
    Check(h.Adapter.ObservedBoundary == 2 && h.Adapter.HostTickSequence == 2 && h.Frames == 2,
        "A handled phase fault fenced off a healthy later tick.");
}

static void MalformedCommandScan()
{
    using var h = new HostHarness(package: true, failingBinding: false);
    var manifest = Path.Combine(h.PackageDirectory!, "plugin.json");
    File.AppendAllText(manifest, "\n");
    File.WriteAllText(h.CommandFile, "{ malformed command");
    Reflect.Set(h.Host, "_nextStatus", 0d); // Allow the real TickCore status cadence.
    var loop = h.StartWatchCommands();
    var sawStatus = PumpUntil(h, () =>
    {
        if (!File.Exists(h.StatusFile)) return false;
        try
        {
            using var status = h.Status();
            var updates = status.RootElement.GetProperty("packageUpdates");
            return !status.RootElement.GetProperty("stopped").GetBoolean() && updates.GetArrayLength() == 1 &&
                updates[0].GetProperty("restartRequired").GetBoolean() &&
                updates[0].GetProperty("changedFiles").EnumerateArray().Any(x => x.GetString() == "plugin.json");
        }
        catch (IOException) { return false; }
        catch (JsonException) { return false; }
    }, TimeSpan.FromSeconds(8));
    Check(sawStatus, "Malformed command prevented the scheduled package scan or status publication.");

    var id = "scan-stop-" + Guid.NewGuid().ToString("N");
    File.WriteAllText(h.CommandFile, JsonSerializer.Serialize(new { id, action = "shutdown" }));
    Check(PumpUntil(h, () => h.Stopped, TimeSpan.FromSeconds(3)),
        "Valid shutdown command did not stop the scan loop.");
    Check(SpinWait.SpinUntil(() => loop.IsCompleted, TimeSpan.FromSeconds(2)) && loop.IsCompletedSuccessfully,
        "Package scan loop did not exit after shutdown.");
    using var finalStatus = h.Status();
    Check(finalStatus.RootElement.GetProperty("stopped").GetBoolean() &&
        finalStatus.RootElement.GetProperty("packageUpdates")[0].GetProperty("restartRequired").GetBoolean(),
        "Final status lost the package update found by the actual command worker.");
}

static void AssertUpdate(HostHarness h, bool restart, params string[] files)
{
    using var status = h.Status();
    var updates = status.RootElement.GetProperty("packageUpdates");
    Check(updates.GetArrayLength() == 1 && updates[0].GetProperty("pluginId").GetString() == "package.one",
        "Status must publish the loaded package ID.");
    var update = updates[0];
    Check(update.GetProperty("restartRequired").GetBoolean() == restart &&
        update.GetProperty("changedFiles").EnumerateArray().Select(x => x.GetString()).SequenceEqual(files),
        $"Unexpected package update snapshot; expected restart={restart}, files={string.Join(',', files)}.");
}

static void StandardNoPeriodicScan()
{
    using var h = new HostHarness(package: true, failingBinding: false, configurationJson: "{}");
    File.AppendAllText(Path.Combine(h.PackageDirectory!, "plugin.json"), "\n");
    var loop = h.StartWatchCommands();
    PumpUntil(h, () => false, TimeSpan.FromSeconds(5.8));
    h.WriteStatus();
    using var status = h.Status();
    Check(status.RootElement.GetProperty("packageUpdates").GetArrayLength() == 0,
        "Standard performed a periodic package scan.");
    Check(status.RootElement.GetProperty("runtime").GetProperty("Mode").GetString() == "Standard",
        "Status must report the actual Standard mode.");
    Reflect.Call(h.Host, "Shutdown", false, "standard-check");
    Check(SpinWait.SpinUntil(() => loop.IsCompleted, TimeSpan.FromSeconds(2)), "Standard worker did not stop.");
}

static void InvalidStartup()
{
    var root = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "BepInEx", "RizToolbox");
    var activation = Path.Combine(root, "activation.json");
    var settings = Path.Combine(root, "settings.json");
    var package = Path.Combine(root, "mods", "invalid-mode-check-" + Guid.NewGuid().ToString("N"));
    var marker = Path.Combine(root, "constructor-check-" + Guid.NewGuid().ToString("N") + ".txt");
    var previousMarker = Environment.GetEnvironmentVariable("RIZ_HOSTING_CONSTRUCTOR_PROBE");
    var oldActivation = File.Exists(activation) ? File.ReadAllBytes(activation) : null;
    var oldSettings = File.Exists(settings) ? File.ReadAllBytes(settings) : null;
    var preserved = System.Text.Encoding.UTF8.GetBytes("user-intent-sentinel; do not parse or overwrite");
    object? host = null;
    using var log = new StructuredDiagnosticLogger(new DiagnosticOptions(Path.Combine(root, "invalid-startup-diagnostics")));
    try
    {
        Environment.SetEnvironmentVariable("RIZ_HOSTING_CONSTRUCTOR_PROBE", marker);
        StagePackage(package, "IsolationFixture.DiscoveryConstructorPlugin");
        File.WriteAllBytes(activation, preserved);
        const string userSettings = "{\"runSelfValidation\":true,\"enableExamples\":true}";
        File.WriteAllText(settings, userSettings);
        var profile = new AdapterBuildProfile("offline", new string('0', 64), "offline", "offline", "Tick", "Update",
            new Dictionary<string, TypeBinding>());
        host = Activator.CreateInstance(Reflect.HostType, BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, args: new object?[] { null, profile, log, Reflect.Configuration("{\"mode\":\"invalid\"}") }, culture: null)!;
        var constructed = WaitPublishedStatus(host);
        using (var initial = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "status.json"))))
            Check(initial.RootElement.GetProperty("revision").GetInt64() >= constructed.Revision &&
                !initial.RootElement.GetProperty("runtime").GetProperty("IsValid").GetBoolean(),
                "The real constructor did not persist its initial safe-observation status.");
        Check(Reflect.Get(host, "_adapter")!.GetType().Name == "SafeObservationAdapter",
            "Invalid configuration constructed the game adapter.");
        Check(((PluginLoadResult)Reflect.Get(host, "_packages")!).Packages.Count == 0,
            "Invalid configuration executed plugin code.");
        Reflect.Call(host, "Tick", 1L);
        Reflect.Call(host, "Execute", "enable", "package.one", null);
        Reflect.Call(host, "WriteStatus", false);
        WaitPublishedStatus(host);
        using var status = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "status.json")));
        var runtime = status.RootElement.GetProperty("runtime");
        Check(!runtime.GetProperty("AllowOptionalWrites").GetBoolean() && !runtime.GetProperty("IsValid").GetBoolean(),
            "Invalid configuration was not reported as safe observation.");
        Check(status.RootElement.GetProperty("discoveredPackages").EnumerateArray().Any(item =>
                item.GetProperty("id").GetString() == "package.one" && item.GetProperty("blockedByRuntimeMode").GetBoolean()) &&
            !File.Exists(marker), "The valid installed fixture was not discovered or its constructor executed.");
        Check(File.ReadAllBytes(activation).SequenceEqual(preserved), "Invalid startup rewrote activation intent.");
        Check(File.ReadAllText(settings) == userSettings && File.Exists(Path.Combine(package, "plugin.json")),
            "Invalid startup changed settings or removed the installed package.");
    }
    finally
    {
        try
        {
            if (host is not null)
            {
                Reflect.Call(host, "Shutdown", false, "invalid-startup-check");
                WaitPublishedStatus(host);
            }
        }
        finally
        {
            if (host is not null && Reflect.Get(host, "_statusWriter") is IDisposable writer) writer.Dispose();
            if (oldActivation is null) File.Delete(activation); else File.WriteAllBytes(activation, oldActivation);
            if (oldSettings is null) File.Delete(settings); else File.WriteAllBytes(settings, oldSettings);
            Environment.SetEnvironmentVariable("RIZ_HOSTING_CONSTRUCTOR_PROBE", previousMarker);
            File.Delete(marker);
            if (Directory.Exists(package))
            {
                foreach (var name in new[] { "plugin.json", "IsolationPlugin.dll", "IsolationPrivate.dll" })
                    File.Delete(Path.Combine(package, name));
                Directory.Delete(package);
            }
        }
    }
}

static void BundledProofPolicy()
{
    var manifest = new PluginManifest("example.panel", new Version(1, 0), new Version(1, 0));
    bool IsProof(string assembly) => (bool)Reflect.HostType.GetMethod("IsBundledValidationPackage", BindingFlags.Static | BindingFlags.NonPublic)!
        .Invoke(null, new object[] { new PluginPackageMetadata(manifest, assembly, "Example") })!;
    Check(IsProof("RizToolbox.Examples.dll"), "Bundled validation package was not gated.");
    Check(IsProof("riztoolbox.examples.dll"), "Windows filename casing bypassed bundled proof gating.");
    Check(!IsProof("UserMod.dll"), "An arbitrary user package was blocked solely for its ID.");
}

static void UnsupportedInstallation()
{
    var executableDirectory = Path.GetDirectoryName(Environment.ProcessPath!)!;
    Check(!File.Exists(Path.Combine(executableDirectory, "GameAssembly.dll")), "The standalone test directory must have no game assembly.");
    var root = Path.Combine(executableDirectory, "BepInEx", "RizToolbox");
    Bootstrap.Start();
    using var startup = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "runtime-startup.json")));
    var status = startup.RootElement;
    Check(status.GetProperty("processId").GetInt32() == Environment.ProcessId &&
        status.GetProperty("installationState").GetString() == "Unavailable" &&
        status.GetProperty("hostState").GetString() == "AwaitingTransport" &&
        status.GetProperty("firstHostTickSequence").ValueKind == JsonValueKind.Null &&
        !status.GetProperty("optionalWritesAllowedByStartup").GetBoolean() &&
        status.GetProperty("error").GetProperty("Code").GetInt32() == (int)ErrorCode.CapabilityUnavailable,
        "The startup failure did not report this process, the installation error, and denied writes.");
}

static void StartupMilestones()
{
    var type = typeof(Bootstrap).Assembly.GetType("RizToolbox.Loader.RuntimeStartupStatus", throwOnError: true)!;
    const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
    foreach (var mode in new[] { "success", "early-tick", "admission-fault", "host-fault", "tick-fault", "invalid-config" })
    {
        var root = Path.Combine(AppContext.BaseDirectory, "case-data", "startup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var log = new StructuredDiagnosticLogger(new DiagnosticOptions(Path.Combine(root, "diagnostics")));
        var configuration = Reflect.Configuration(mode == "invalid-config" ? "{\"mode\":\"invalid\"}" : "{\"mode\":\"Standard\"}");
        var reporter = Activator.CreateInstance(type, members, binder: null, args: new[] { root, configuration, log }, culture: null)!;
        var path = Path.Combine(root, "runtime-startup.json");
        var failure = new InvalidOperationException("startup verification sentinel");

        void Call(string method, params object[] values)
        {
            try { type.GetMethod(method, members)!.Invoke(reporter, values); }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); }
        }
        void CheckStatus(string installation, string host, long? sequence, bool writes = false, bool failed = false)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var status = document.RootElement;
            var actualSequence = status.GetProperty("firstHostTickSequence");
            Check(status.GetProperty("installationState").GetString() == installation && status.GetProperty("hostState").GetString() == host
                && (sequence is null ? actualSequence.ValueKind == JsonValueKind.Null : actualSequence.GetInt64() == sequence)
                && status.GetProperty("optionalWritesAllowedByStartup").GetBoolean() == writes
                && !status.GetProperty("grantsOperationPermission").GetBoolean(), "Wrong startup state for " + mode + " / " + host);
            Check(failed ? status.GetProperty("detail").GetString() == failure.Message : status.GetProperty("error").ValueKind == JsonValueKind.Null,
                "Unexpected startup error detail for " + mode);
        }

        CheckStatus("Pending", "AwaitingTransport", null);
        if (mode == "admission-fault")
        {
            Call("AdmissionFailed", failure);
            Call("TransportAttached");
            var unexpected = (Action)(() => throw new InvalidOperationException("Rejected admission reached managed host work."));
            Call("RunFirstTick", 1L, unexpected, unexpected);
            CheckStatus("Ready", "AdmissionFailed", null, failed: true);
            continue;
        }

        var installation = mode == "early-tick" ? "Pending" : "Ready";
        if (installation == "Ready")
        {
            Call("TransportAttached");
            CheckStatus("Ready", "AwaitingUnity", null);
        }
        var steps = new List<string>();
        Action initialize = () =>
        {
            CheckStatus(installation, "InitializingHost", 1);
            steps.Add("initialize");
            if (mode == "host-fault") throw failure;
        };
        Action tick = () =>
        {
            CheckStatus(installation, "RunningFirstTick", 1);
            steps.Add("tick");
            if (mode == "tick-fault") throw failure;
        };
        var expectedFailure = mode is "host-fault" or "tick-fault";
        var failed = false;
        try { Call("RunFirstTick", 1L, initialize, tick); }
        catch (InvalidOperationException error) when (ReferenceEquals(error, failure)) { failed = true; }
        Check(failed == expectedFailure && steps.SequenceEqual(mode == "host-fault" ? new[] { "initialize" } : new[] { "initialize", "tick" }),
            "The startup reporter changed the host invocation or hid its exception.");
        var host = mode == "host-fault" ? "HostInitializationFailed" : mode == "tick-fault" ? "FirstTickFailed" : "FirstTickReturned";
        var writes = installation == "Ready" && !expectedFailure && mode != "invalid-config";
        CheckStatus(installation, host, 1, writes, expectedFailure);
        if (mode == "early-tick")
        {
            Call("TransportAttached");
            CheckStatus("Ready", "FirstTickReturned", 1, writes: true);
        }
        var receipt = File.ReadAllBytes(path);
        Call("RunFirstTick", 2L, initialize, tick);
        Call("AdmissionFailed", failure);
        Check(File.ReadAllBytes(path).SequenceEqual(receipt), "Later callbacks rewrote the first-tick receipt.");
    }
}

sealed class HostHarness : IDisposable
{
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "case-data", "riz-loader-host-" + Guid.NewGuid().ToString("N"));
    private readonly StructuredDiagnosticLogger _log;
    public object Host { get; }
    public MainThreadDispatcher Main { get; }
    public FakeAdapter Adapter { get; }
    public PluginLoadResult Packages { get; }
    public CancellationTokenSource Shutdown { get; } = new();
    public ThrowingBinding? Binding { get; }
    public string? PackageDirectory { get; }
    public int Frames, Followups;
    private string DataRoot => Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "BepInEx", "RizToolbox");
    public string StatusFile => Path.Combine(DataRoot, "status.json");
    public string CommandFile => Path.Combine(DataRoot, "command.json");
    public bool Stopped => (bool)Reflect.Get(Host, "_stopped")!;
    public string? StopReason => (string?)Reflect.Get(Host, "_stopReason");

    public HostHarness(bool package, bool failingBinding, int maxPending = 256,
        string configurationJson = "{\"mode\":\"Developer\"}")
    {
        Main = new MainThreadDispatcher(maxPending);
        File.Delete(CommandFile);
        File.Delete(StatusFile);
        File.Delete(StatusFile + ".tmp");
        Directory.CreateDirectory(_root);
        _log = new StructuredDiagnosticLogger(new DiagnosticOptions(Path.Combine(_root, "diagnostics")));
        var mods = Path.Combine(_root, "mods");
        Directory.CreateDirectory(mods);
        if (package)
        {
            PackageDirectory = Path.Combine(mods, "one");
            StagePackage(PackageDirectory, "IsolationFixture.Plugin");
        }
        Packages = PluginPackageLoader.LoadDirectory(mods, _log);
        Check(Packages.Diagnostics.Count == 0 && Packages.Packages.Count == (package ? 1 : 0),
            "Package fixture did not load: " + string.Join("; ", Packages.Diagnostics.Select(x => x.Message)));

        var build = new GameBuildDescriptor("offline", "offline", "win-x64", "offline", true);
        var capabilities = new CapabilityRegistry(build);
        var events = new EventHub(_log);
        events.Subscribe<FrameUpdate>(_ => Frames++);
        var performance = new CallbackPerformanceMonitor();
        events.Performance = performance;
        var plugins = new PluginHost(capabilities, Main, events, _log, dataRoot: Path.Combine(_root, "data"));
        var services = new InterPluginServiceRegistry(Main, _log);
        Adapter = new FakeAdapter(build);
        if (failingBinding) Binding = new ThrowingBinding();

        Host = RuntimeHelpers.GetUninitializedObject(Reflect.HostType);
        Reflect.Set(Host, "_log", _log);
        Reflect.Set(Host, "_main", Main);
        Reflect.Set(Host, "_capabilities", capabilities);
        Reflect.Set(Host, "_plugins", plugins);
        Reflect.Set(Host, "_events", events);
        Reflect.Set(Host, "_adapter", Adapter);
        Reflect.Set(Host, "_packages", Packages);
        Reflect.Set(Host, "_loadedPackageIds", new HashSet<string>(
            Packages.Packages.Select(item => item.Metadata.Manifest.Id), StringComparer.Ordinal));
        Reflect.Set(Host, "_discovery", PluginPackageLoader.DiscoverDirectory(mods, _log));
        Reflect.Set(Host, "_configuration", Reflect.Configuration(configurationJson));
        Reflect.Set(Host, "_shutdown", Shutdown);
        Reflect.Set(Host, "_clock", Stopwatch.StartNew());
        Reflect.Set(Host, "_activationBinding", Binding);
        Reflect.Set(Host, "_localization", new PluginLocalizationManager());
        Reflect.Set(Host, "_pluginServices", services);
        Reflect.Set(Host, "_performance", performance);
        Reflect.Set(Host, "_nextStatus", double.PositiveInfinity);
        Reflect.Set(Host, "_shutdownFailures", Array.Empty<string>());
        var updateType = Reflect.HostType.GetNestedType("PackageUpdateView", BindingFlags.NonPublic)!;
        Reflect.Set(Host, "_packageUpdates", Array.CreateInstance(updateType, 0));
        var validationType = Reflect.HostType.GetNestedType("ValidationCommand", BindingFlags.NonPublic)!;
        Reflect.Set(Host, "_validation", Activator.CreateInstance(typeof(Queue<>).MakeGenericType(validationType))!);
        var settingsType = Reflect.HostType.GetNestedType("Settings", BindingFlags.NonPublic)!;
        Reflect.Set(Host, "_settings", RuntimeHelpers.GetUninitializedObject(settingsType));
        // _api intentionally remains null: command shutdown(false) must never reach Application.Quit.
    }

    private long _hostTickSequence;
    public void Tick() => Reflect.Call(Host, "Tick", ++_hostTickSequence);
    public void WriteStatus()
    {
        Reflect.Call(Host, "WriteStatus", false);
        WaitPublishedStatus(Host);
    }
    public JsonDocument Status()
    {
        WaitPublishedStatus(Host);
        return JsonDocument.Parse(File.ReadAllText(StatusFile));
    }
    public Task StartWatchCommands() => Task.Run(async () => await (Task)Reflect.Call(Host, "WatchCommands")!);

    public void PublishScan()
    {
        var result = Task.Run(() => Reflect.Call(Host, "ScanPackageUpdates")).GetAwaiter().GetResult();
        Check(result is not null, "Live host rejected its package scan.");
        var published = Queue(this, () => Reflect.Set(Host, "_packageUpdates", result), Shutdown.Token);
        WaitPending(Main, 1);
        Tick();
        published.GetAwaiter().GetResult();
        WriteStatus();
    }

    public void Dispose()
    {
        try
        {
            if (!(bool)Reflect.Get(Host, "_stopped")!) Reflect.Call(Host, "Shutdown", false, "harness-cleanup");
            if (Reflect.Get(Host, "_statusWriter") is not null && StatusWriterSnapshot(Host).LastPublished > 0)
                WaitPublishedStatus(Host);
        }
        finally
        {
            File.Delete(CommandFile);
            Packages.Dispose();
            _log.Dispose();
            if (Reflect.Get(Host, "_statusWriter") is IDisposable writer) writer.Dispose();
            Shutdown.Dispose();
            // ALC unloading is asynchronous while this harness retains plugin/type
            // references. Preserve these owned evidence files until process exit.

        }
    }
}

sealed class FakeAdapter : IGameAdapter, ISceneStatusSource
{
    private readonly GameBuildDescriptor _build;
    public FakeAdapter(GameBuildDescriptor build) => _build = build;
    private void CheckAlive()
    {
        if (Disposals != 0) throw new ObjectDisposedException(nameof(FakeAdapter));
    }
    public GameBuildDescriptor Build { get { BuildReads++; CheckAlive(); return _build; } }
    public IReadOnlyList<string> SupportedCapabilities
    {
        get { CapabilityReads++; CheckAlive(); return OnSupportedCapabilities?.Invoke() ?? Array.Empty<string>(); }
    }
    public int Flushes, Ticks, Disposals;
    public int BuildReads, CapabilityReads, ModuleReads, SceneStatusReads;
    internal GameEventState? GameEvents;
    private int _boundarySequence;
    public int DeliveredBoundaries, ObservedBoundary;
    public long HostTickSequence;
    public Action<int>? OnFlush;
    public Action<long>? OnTick;
    public Action? OnPumpCommands;
    public Func<IReadOnlyList<string>>? OnSupportedCapabilities;
    public Func<IReadOnlyList<AdapterModuleStatus>>? OnGetModules;
    public Action? OnDispose;
    public void EmitBoundary() => _boundarySequence++;
    public void PumpCommands() { CheckAlive(); OnPumpCommands?.Invoke(); }
    public void FlushBoundaryObservations()
    {
        CheckAlive(); Flushes++;
        OnFlush?.Invoke(Flushes);
        DeliveredBoundaries = _boundarySequence;
    }
    public void Tick(long hostTickSequence)
    {
        CheckAlive();
        Ticks++;
        HostTickSequence = hostTickSequence;
        OnTick?.Invoke(hostTickSequence);
        ObservedBoundary = DeliveredBoundaries;
    }
    public IReadOnlyList<AdapterModuleStatus> GetModules()
    { ModuleReads++; CheckAlive(); return OnGetModules?.Invoke() ?? Array.Empty<AdapterModuleStatus>(); }
    SceneStatus ISceneStatusSource.CaptureSceneStatus()
    {
        CheckAlive();
        SceneStatusReads++;
        return GameEvents?.CaptureSceneStatus() ?? default;
    }
    public void Dispose() { Disposals++; OnDispose?.Invoke(); }
}

sealed class ThrowingBinding : IDisposable
{
    public int Disposals;
    public void Dispose() { Disposals++; throw new InvalidOperationException("binding sentinel"); }
}

static class Reflect
{
    public static readonly Type HostType = typeof(Bootstrap).Assembly.GetType("RizToolbox.Loader.ToolboxHost", throwOnError: true)!;
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    public static object Configuration(string json)
        => typeof(Bootstrap).Assembly.GetType("RizToolbox.Loader.RuntimeConfiguration", throwOnError: true)!
            .GetMethod("Parse", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object?[] { json, null })!;
    public static void Set(object host, string name, object? value)
        => HostType.GetField(name, Fields)!.SetValue(host, value);
    public static object? Get(object host, string name)
        => HostType.GetField(name, Fields)!.GetValue(host);
    public static object? Call(object host, string name, params object?[] args)
    {
        try { return HostType.GetMethod(name, Fields | BindingFlags.Public)!.Invoke(host, args); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
}

static class Helpers
{
    public static LatestDiagnosticWriterSnapshot StatusWriterSnapshot(object host)
    {
        var writer = Reflect.Get(host, "_statusWriter")
            ?? throw new InvalidOperationException("The real host has not created its diagnostic writer.");
        return (LatestDiagnosticWriterSnapshot)writer.GetType().GetMethod("GetSnapshot")!.Invoke(writer, null)!;
    }

    public static LatestDiagnosticWaitResult WaitPublishedStatus(object host)
    {
        var writer = Reflect.Get(host, "_statusWriter")
            ?? throw new InvalidOperationException("The real host has not created its diagnostic writer.");
        var revision = StatusWriterSnapshot(host).LastPublished;
        Check(revision > 0, "The real host has not published a diagnostic status copy.");
        var result = (LatestDiagnosticWaitResult)writer.GetType().GetMethod("WaitThrough")!.Invoke(writer,
            new object[] { revision, TimeSpan.FromSeconds(5) })!;
        Check(result.Completed && result.Persisted,
            $"Published host status revision {revision} did not persist within five seconds: {result.LastError}");
        return result;
    }

    public static void StagePackage(string directory, string entryType)
    {
        Directory.CreateDirectory(directory);
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "v1");
        foreach (var name in new[] { "IsolationPlugin.dll", "IsolationPrivate.dll" })
            File.Copy(Path.Combine(fixture, name), Path.Combine(directory, name));
        File.WriteAllText(Path.Combine(directory, "plugin.json"), JsonSerializer.Serialize(new
        {
            id = "package.one", version = "1.0.0", entryAssembly = "IsolationPlugin.dll",
            entryType, minSdk = "1.0.0", dependencies = Array.Empty<object>(), requiredCapabilities = Array.Empty<string>()
        }));
    }

    public static Task Queue(HostHarness h, Action action, CancellationToken token = default)
        => Task.Run(async () => await h.Main.InvokeAsync(action, token));

    public static void WaitPending(MainThreadDispatcher main, int count)
        => Check(SpinWait.SpinUntil(() => main.PendingCount == count, TimeSpan.FromSeconds(5)),
            $"Expected {count} queued main-thread actions; found {main.PendingCount}.");

    public static bool PumpUntil(HostHarness h, Func<bool> condition, TimeSpan timeout)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < timeout)
        {
            h.Tick();
            if (condition()) return true;
            Thread.Sleep(20);
        }
        return false;
    }

    public static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
