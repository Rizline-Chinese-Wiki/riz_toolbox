using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using RizToolbox.Adapter;
using RizToolbox.Core;
using RizToolbox.Core.Diagnostics;
using RizToolbox.Sdk;
using static Helpers;

internal static class CommandMailboxChecks
{
    internal static readonly (string Name, Action Run)[] All =
    {
        ("startup mailbox does not replay a stale enable over saved activation choices", StaleActivation),
        ("a fresh command before the first poll executes once and does not replay on restart", FreshCommand),
        ("lifecycle mailbox correlates intent waiting and actual success for repeated commands", LifecycleReceipts),
        ("lifecycle mailbox keeps exact rejections separate from recorded intent", LifecycleRejections)
    };

    private static void StaleActivation()
    {
        using var files = new MailboxFiles();
        var store = new PluginActivationStore(files.Activation);
        Check(store.Save(new[] { new PluginActivationIntent("example.panel", false, true) }).Success,
            "Could not stage the saved disabled intent.");
        var activation = File.ReadAllBytes(files.Activation);
        var settings = File.ReadAllBytes(files.Settings);
        files.WriteCommand("october-enable", "enable", "example.panel");
        File.SetLastWriteTimeUtc(files.Command, new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc));
        var stale = File.ReadAllBytes(files.Command);
        var written = File.GetLastWriteTimeUtc(files.Command);

        using var host = new CommandHost(files.Activation);
        host.PumpFor(TimeSpan.FromSeconds(1.2));
        Check(!host.PluginStatus.DesiredEnabled && host.Probe.Enables == 0 && host.Main.GetSnapshot().Executed == 0,
            "The new host replayed the pre-existing enable command over the restored disabled intent.");
        Check(File.ReadAllBytes(files.Activation).SequenceEqual(activation) &&
            File.ReadAllBytes(files.Settings).SequenceEqual(settings) &&
            File.ReadAllBytes(files.Command).SequenceEqual(stale) && File.GetLastWriteTimeUtc(files.Command) == written,
            "Ignoring the startup mailbox rewrote saved activation, settings, or the pending command.");

        files.WriteCommand("fresh-enable", "enable", "example.panel");
        host.WaitForCommand("fresh-enable");
        Check(host.PluginStatus.DesiredEnabled && host.Probe.Enables == 1 &&
            store.Load().Value!.Single().DesiredEnabled,
            "A fresh intentional enable did not execute and persist its activation choice.");
        host.PumpFor(TimeSpan.FromSeconds(0.6));
        Check(host.Main.GetSnapshot().Executed == 1 && host.Probe.Enables == 1,
            "The consumed command ID executed again in the same host.");
    }

    private static void FreshCommand()
    {
        using var files = new MailboxFiles();
        var store = new PluginActivationStore(files.Activation);
        Check(store.Save(new[] { new PluginActivationIntent("example.panel", true) }).Success,
            "Could not stage the saved enabled intent.");
        var activation = File.ReadAllBytes(files.Activation);
        var settings = File.ReadAllBytes(files.Settings);
        files.WriteCommand("previous-shutdown", "shutdown");
        using (var first = new CommandHost(files.Activation))
        {
            // Publish immediately after construction, before the worker's first 500 ms poll.
            files.WriteCommand("fresh-status", "status");
            first.WaitForCommand("fresh-status");
            Check(!first.Stopped && first.PluginStatus.DesiredEnabled && first.Probe.Enables == 1,
                "Startup ignored the fresh command or lost the saved enabled intent.");
            files.WriteCommand("fresh-shutdown", "shutdown");
            first.WaitForCommand("fresh-shutdown");
            Check(first.Stopped && first.Main.GetSnapshot().Executed == 2,
                "Fresh status/shutdown commands did not execute exactly once.");
        }

        using var restarted = new CommandHost(files.Activation);
        restarted.PumpFor(TimeSpan.FromSeconds(1.2));
        Check(!restarted.Stopped && restarted.Main.GetSnapshot().Executed == 0 &&
            restarted.PluginStatus.DesiredEnabled && restarted.Probe.Enables == 1,
            "A new host replayed the previous host's shutdown or failed to restore enabled intent.");
        Check(File.ReadAllBytes(files.Activation).SequenceEqual(activation) &&
            File.ReadAllBytes(files.Settings).SequenceEqual(settings),
            "Status/shutdown commands or host restart changed saved activation/settings.");
    }

    private static void LifecycleReceipts()
    {
        const string capability = "mailbox.controlled-provider";
        using var files = new MailboxFiles();
        var store = new PluginActivationStore(files.Activation);
        Check(store.Save(new[] { new PluginActivationIntent("example.panel", false, true) }).Success);
        using var host = new CommandHost(files.Activation, requiredCapability: capability);

        files.WriteCommand("wait-enable", "enable", "example.panel");
        host.WaitForCommand("wait-enable");
        Check(host.Probe.Enables == 0 && host.PluginStatus.State == PluginState.Waiting
            && host.PluginStatus.DesiredEnabled && store.Load().Value!.Single().DesiredEnabled,
            "The command must preserve the actual waiting state and recorded intent.");
        var waiting = host.Receipt("wait-enable");
        CheckReceipt(waiting, "enable", "example.panel", "waiting", intentRecorded: true, coreSuccess: false, "Info");
        var waitFields = waiting.GetProperty("fields");
        Equal("Waiting", waitFields.GetProperty("pluginState").GetString());
        Equal("true", waitFields.GetProperty("desiredEnabled").GetString());
        Equal("CapabilityUnavailable", waitFields.GetProperty("errorCode").GetString());
        Equal("true", waitFields.GetProperty("errorRetryable").GetString());
        Equal(capability, waiting.GetProperty("context").GetProperty("capabilityId").GetString());
        Check(waiting.GetProperty("message").GetString()!.StartsWith("Lifecycle command enable example.panel: False OperationError", StringComparison.Ordinal));

        host.PublishCapability(capability);
        Check(host.PluginStatus.State == PluginState.Enabled && host.Probe.Enables == 1,
            "Normal capability reconciliation must activate the already recorded intent.");
        files.WriteCommand("ready-enable", "enable", "example.panel");
        host.WaitForCommand("ready-enable");
        CheckReceipt(host.Receipt("ready-enable"), "enable", "example.panel", "succeeded", true, true, "Info");
        Check(host.Probe.Enables == 1, "A second enable command re-ran the active plugin.");

        files.WriteCommand("ready-disable", "disable", "example.panel");
        host.WaitForCommand("ready-disable");
        CheckReceipt(host.Receipt("ready-disable"), "disable", "example.panel", "succeeded", true, true, "Info");
        Check(host.PluginStatus.State == PluginState.Disabled && !store.Load().Value!.Single().DesiredEnabled);
    }

    private static void LifecycleRejections()
    {
        using var files = new MailboxFiles();
        var store = new PluginActivationStore(files.Activation);
        Check(store.Save(new[] { new PluginActivationIntent("example.panel", false, true) }).Success);
        using var host = new CommandHost(files.Activation, allowWrites: false);

        files.WriteCommand("invalid-mode", "enable", "example.panel");
        host.WaitForCommand("invalid-mode");
        var invalid = host.Receipt("invalid-mode");
        CheckReceipt(invalid, "enable", "example.panel", "rejected", false, false, "Warning");
        Check(invalid.GetProperty("fields").GetProperty("configurationDetail").GetString()!.Contains("Standard or Developer", StringComparison.Ordinal));
        Check(host.Probe.Enables == 0 && !host.PluginStatus.DesiredEnabled);

        host.AllowWrites();
        files.WriteCommand("missing-target", "enable", "missing.plugin");
        host.WaitForCommand("missing-target");
        var missing = host.Receipt("missing-target");
        CheckReceipt(missing, "enable", "missing.plugin", "rejected", false, false, "Warning");
        Equal("TargetMissing", missing.GetProperty("fields").GetProperty("errorCode").GetString());
        Equal("missing.plugin", missing.GetProperty("context").GetProperty("targetId").GetString());
        Check(store.Load().Value!.All(intent => intent.Id != "missing.plugin"));

        host.Probe.FailEnable = true;
        files.WriteCommand("failed-enable", "enable", "example.panel");
        host.WaitForCommand("failed-enable");
        var failed = host.Receipt("failed-enable");
        CheckReceipt(failed, "enable", "example.panel", "rejected", true, false, "Warning");
        var failure = failed.GetProperty("fields");
        Equal("Faulted", failure.GetProperty("pluginState").GetString());
        Equal("InternalError", failure.GetProperty("errorCode").GetString());
        Check(failure.GetProperty("errorMessage").GetString()!.Contains("Controlled plugin activation fault.", StringComparison.Ordinal));
        Check(host.Probe.Enables == 1 && host.PluginStatus.DesiredEnabled && store.Load().Value!.Single().DesiredEnabled);

        host.Probe.FailEnable = false;
        files.WriteCommand("retry-enable", "enable", "example.panel");
        host.WaitForCommand("retry-enable");
        Check(host.PluginStatus.State == PluginState.Enabled);
        host.Probe.FailDisable = true;
        files.WriteCommand("cleanup-disable", "disable", "example.panel");
        host.WaitForCommand("cleanup-disable");
        var disabled = host.Receipt("cleanup-disable");
        CheckReceipt(disabled, "disable", "example.panel", "succeeded", true, true, "Info");
        var state = disabled.GetProperty("fields");
        Equal("Faulted", state.GetProperty("pluginState").GetString());
        Equal("InternalError", state.GetProperty("stateErrorCode").GetString());
        Equal("Controlled plugin cleanup fault.", state.GetProperty("stateErrorMessage").GetString());
        Check(!store.Load().Value!.Single().DesiredEnabled);
    }

    private static void CheckReceipt(JsonElement receipt, string action, string plugin, string outcome,
        bool intentRecorded, bool coreSuccess, string level)
    {
        Equal("log", receipt.GetProperty("eventCode").GetString());
        Equal(level, receipt.GetProperty("level").GetString());
        Equal(plugin, receipt.GetProperty("context").GetProperty("pluginId").GetString());
        var fields = receipt.GetProperty("fields");
        Equal(action, fields.GetProperty("action").GetString());
        Equal(outcome, fields.GetProperty("outcome").GetString());
        Equal(intentRecorded ? "true" : "false", fields.GetProperty("intentRecorded").GetString());
        Equal(coreSuccess ? "true" : "false", fields.GetProperty("coreSuccess").GetString());
    }

    private static void Check(bool condition, string message = "Lifecycle mailbox check failed.")
        => Helpers.Check(condition, message);

    private static void Equal<T>(T expected, T actual)
        => Check(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected '{expected}', got '{actual}'.");

    private sealed class CommandHost : IDisposable
    {
        private readonly StructuredDiagnosticLogger _log;
        private readonly object _host;
        private readonly PluginHost _plugins;
        private long _tick;
        internal MainThreadDispatcher Main { get; }
        internal ProbePlugin Probe { get; }
        internal bool Stopped => (bool)Reflect.Get(_host, "_stopped")!;
        internal PluginStatus PluginStatus => _plugins.Statuses.Single();

        internal CommandHost(string activationPath, string? requiredCapability = null, bool allowWrites = true)
        {
            Probe = new(requiredCapability);
            _log = new StructuredDiagnosticLogger(new DiagnosticOptions(Path.Combine(AppContext.BaseDirectory,
                "case-data", "mailbox-" + Guid.NewGuid().ToString("N"))));
            var profile = new AdapterBuildProfile("offline", new string('0', 64), "offline", "offline", "Tick", "Update",
                new Dictionary<string, TypeBinding>());
            // Run the real constructor/mailbox capture with the safe adapter so no native game API is required.
            _host = Activator.CreateInstance(Reflect.HostType, BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null, args: new object?[] { null, profile, _log, Reflect.Configuration("{\"mode\":\"invalid\"}") }, culture: null)!;
            Main = (MainThreadDispatcher)Reflect.Get(_host, "_main")!;
            _plugins = (PluginHost)Reflect.Get(_host, "_plugins")!;
            Check(_plugins.Register(Probe).Success, "Could not register the mailbox probe.");
            var binding = new PluginActivationStore(activationPath).Bind(_plugins);
            Check(binding.Success, "Could not bind the real saved activation store.");
            Reflect.Set(_host, "_activationBinding", binding.Value);
            // Exercise the real command/lifecycle path while retaining the offline adapter.
            if (allowWrites) AllowWrites();
        }

        internal void AllowWrites() => Reflect.Set(_host, "_configuration", Reflect.Configuration("{}"));

        internal void PublishCapability(string capability)
            => ((CapabilityRegistry)Reflect.Get(_host, "_capabilities")!).Register<object>(capability, new object());

        internal JsonElement Receipt(string id)
        {
            Check(_log.Flush(TimeSpan.FromSeconds(5)).Completed, "Command diagnostics did not flush.");
            var receipts = new List<JsonElement>();
            foreach (var file in Directory.EnumerateFiles(_log.GetSnapshot().DirectoryPath!, "events-*.jsonl"))
            {
                using var stream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                while (reader.ReadLine() is { } line)
                {
                    using var doc = JsonDocument.Parse(line);
                    var row = doc.RootElement;
                    if (row.TryGetProperty("context", out var context) && context.TryGetProperty("correlationId", out var correlation)
                        && correlation.GetString() == id) receipts.Add(row.Clone());
                }
            }
            Check(receipts.Count == 1, $"Expected one correlated mailbox receipt for '{id}', got {receipts.Count}.");
            return receipts.Single();
        }

        internal void PumpFor(TimeSpan duration) => PumpUntil(() => false, duration);
        internal void WaitForCommand(string id)
            => Check(PumpUntil(() => (string?)Reflect.Get(_host, "_lastCommand") == id, TimeSpan.FromSeconds(5)),
                "The current-session command was not executed and acknowledged: " + id);

        private bool PumpUntil(Func<bool> condition, TimeSpan timeout)
        {
            var elapsed = Stopwatch.StartNew();
            while (elapsed.Elapsed < timeout)
            {
                Reflect.Call(_host, "Tick", ++_tick);
                if (condition()) return true;
                Thread.Sleep(10);
            }
            return false;
        }

        public void Dispose()
        {
            try
            {
                if (!Stopped) Reflect.Call(_host, "Shutdown", false, "mailbox-check-cleanup");
                WaitPublishedStatus(_host);
            }
            finally
            {
                if (Reflect.Get(_host, "_statusWriter") is IDisposable writer) writer.Dispose();
                _log.Dispose();
            }
        }
    }

    private sealed class ProbePlugin : IRizPlugin
    {
        public PluginManifest Manifest { get; }
        public int Enables;
        internal bool FailEnable, FailDisable;
        internal ProbePlugin(string? requiredCapability)
            => Manifest = new("example.panel", new(1, 0, 0), new(1, 0, 0),
                RequiredCapabilities: requiredCapability is null ? Array.Empty<string>() : new[] { requiredCapability });
        public void Enable(IPluginContext context)
        {
            Enables++;
            if (FailEnable) throw new InvalidOperationException("Controlled plugin activation fault.");
        }
        public void Disable()
        { if (FailDisable) throw new InvalidOperationException("Controlled plugin cleanup fault."); }
    }

    private sealed class MailboxFiles : IDisposable
    {
        private static readonly string Root = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "BepInEx", "RizToolbox");
        internal string Command => Path.Combine(Root, "command.json");
        internal string Settings => Path.Combine(Root, "settings.json");
        internal string Activation => Path.Combine(Root, "activation.json");
        private readonly Dictionary<string, byte[]?> _originals;

        internal MailboxFiles()
        {
            _originals = new[] { Command, Settings, Activation }.ToDictionary(path => path,
                path => File.Exists(path) ? File.ReadAllBytes(path) : null);
            File.WriteAllText(Settings, "{\"enableExamples\":true,\"language\":\"en\"}");
        }

        internal void WriteCommand(string id, string action, string? pluginId = null)
            => File.WriteAllText(Command, JsonSerializer.Serialize(new { id, action, pluginId }));

        public void Dispose()
        {
            foreach (var pair in _originals)
                if (pair.Value is null) File.Delete(pair.Key); else File.WriteAllBytes(pair.Key, pair.Value);
        }
    }
}
