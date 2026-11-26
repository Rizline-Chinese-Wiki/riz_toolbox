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
        ("lifecycle mailbox keeps exact rejections separate from recorded intent", LifecycleRejections),
        ("directed commands ignore older sessions and bind a reused ID to the new session", DirectedSessionReceipts),
        ("language receipts reflect selection, validation and a failing callback", LanguageReceipts),
        ("status and shutdown receipts follow the actual status write and never sign a failed stop", StatusWriteOrdering),
        ("a blocked receipt file does not replay an already dispatched command", ReceiptWriteFailure)
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
            var status = first.Result("fresh-status", "status", "succeeded");
            CheckPublishedStatus(files.Status, status, first.SessionId, stopped: false);
            Check(!first.Stopped && first.PluginStatus.DesiredEnabled && first.Probe.Enables == 1,
                "Startup ignored the fresh command or lost the saved enabled intent.");
            files.WriteCommand("fresh-shutdown", "shutdown");
            var shutdown = first.Result("fresh-shutdown", "shutdown", "succeeded");
            CheckPublishedStatus(files.Status, shutdown, first.SessionId, stopped: true);
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
        CheckPluginResult(host.Result("wait-enable", "enable", "waiting", "example.panel"), "Waiting", true, true, "CapabilityUnavailable");
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
        CheckPluginResult(host.Result("ready-enable", "enable", "succeeded", "example.panel"), "Enabled", true, true);
        Check(host.Probe.Enables == 1, "A second enable command re-ran the active plugin.");

        files.WriteCommand("ready-disable", "disable", "example.panel");
        host.WaitForCommand("ready-disable");
        CheckReceipt(host.Receipt("ready-disable"), "disable", "example.panel", "succeeded", true, true, "Info");
        CheckPluginResult(host.Result("ready-disable", "disable", "succeeded", "example.panel"), "Disabled", false, true);
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
        CheckPluginResult(host.Result("invalid-mode", "enable", "rejected", "example.panel"), "Disabled", false, false, "InvalidArgument");
        Check(invalid.GetProperty("fields").GetProperty("configurationDetail").GetString()!.Contains("Standard or Developer", StringComparison.Ordinal));
        Check(host.Probe.Enables == 0 && !host.PluginStatus.DesiredEnabled);

        host.AllowWrites();
        files.WriteCommand("missing-target", "enable", "missing.plugin");
        host.WaitForCommand("missing-target");
        var missing = host.Receipt("missing-target");
        CheckReceipt(missing, "enable", "missing.plugin", "rejected", false, false, "Warning");
        CheckPluginResult(host.Result("missing-target", "enable", "rejected", "missing.plugin"), null, null, false, "TargetMissing");
        Equal("TargetMissing", missing.GetProperty("fields").GetProperty("errorCode").GetString());
        Equal("missing.plugin", missing.GetProperty("context").GetProperty("targetId").GetString());
        Check(store.Load().Value!.All(intent => intent.Id != "missing.plugin"));

        host.Probe.FailEnable = true;
        files.WriteCommand("failed-enable", "enable", "example.panel");
        host.WaitForCommand("failed-enable");
        var failed = host.Receipt("failed-enable");
        CheckReceipt(failed, "enable", "example.panel", "rejected", true, false, "Warning");
        CheckPluginResult(host.Result("failed-enable", "enable", "failed", "example.panel"), "Faulted", true, true, "InternalError");
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
        var fileResult = host.Result("cleanup-disable", "disable", "failed", "example.panel");
        CheckPluginResult(fileResult, "Faulted", false, true, "InternalError");
        Equal("Controlled plugin cleanup fault.", fileResult.GetProperty("errorMessage").GetString());
        Check(!store.Load().Value!.Single().DesiredEnabled);

        host.Probe.FailDisable = false;
        files.WriteCommand("enable-owned-cleanup", "enable", "example.panel");
        host.Result("enable-owned-cleanup", "enable", "succeeded", "example.panel");
        host.Probe.FailOwnedCleanup = true;
        files.WriteCommand("owned-cleanup-disable", "disable", "example.panel");
        var owned = host.Result("owned-cleanup-disable", "disable", "failed", "example.panel");
        CheckPluginResult(owned, "Disabled", false, true, "InternalError");
        Equal("Controlled nested resource cleanup fault.", owned.GetProperty("errorMessage").GetString());
        Check(!store.Load().Value!.Single().DesiredEnabled);
    }

    private static void DirectedSessionReceipts()
    {
        using var files = new MailboxFiles();
        var store = new PluginActivationStore(files.Activation);
        Check(store.Save(new[] { new PluginActivationIntent("example.panel", false, true) }).Success);
        const string reusedId = "same-id-across-sessions";
        string previousSession;
        using (var previous = new CommandHost(files.Activation))
        {
            previousSession = previous.SessionId;
            files.WriteCommand(reusedId, "status", expectedSessionId: previousSession);
            previous.Result(reusedId, "status", "succeeded");
        }
        var stale = File.ReadAllBytes(files.Result);

        using var current = new CommandHost(files.Activation);
        Check(current.SessionId != previousSession, "Both hosts advertised the same diagnostics session.");
        current.PumpFor(TimeSpan.FromSeconds(0.65));
        files.WriteCommand("late-old-session", "enable", "example.panel", expectedSessionId: previousSession);
        current.PumpFor(TimeSpan.FromSeconds(0.65));
        Check(current.Main.GetSnapshot().Executed == 0 && current.Probe.Enables == 0 &&
            stale.SequenceEqual(File.ReadAllBytes(files.Result)), "A directed old-session command dispatched or replaced its receipt.");

        files.WriteCommand(reusedId, "status", expectedSessionId: current.SessionId);
        var fresh = current.Result(reusedId, "status", "succeeded");
        CheckPublishedStatus(files.Status, fresh, current.SessionId, stopped: false);
        Check(current.Main.GetSnapshot().Executed == 1, "The explicitly current ID did not execute exactly once.");
    }

    private static void LanguageReceipts()
    {
        using var files = new MailboxFiles();
        Check(new PluginActivationStore(files.Activation).Save(new[] { new PluginActivationIntent("example.panel", true) }).Success);
        using var host = new CommandHost(files.Activation);
        files.WriteCommand("language-success", "set-language", language: "zh-CN", expectedSessionId: host.SessionId);
        host.Result("language-success", "set-language", "succeeded");
        Equal("zh-cn", host.Probe.Localization!.GetState().Value!.Language);

        files.WriteCommand("language-rejected", "set-language", language: "not a tag");
        var rejected = host.Result("language-rejected", "set-language", "rejected");
        Equal("InvalidArgument", rejected.GetProperty("errorCode").GetString());
        Check(!string.IsNullOrWhiteSpace(rejected.GetProperty("errorMessage").GetString()));
        Equal("zh-cn", host.Probe.Localization.GetState().Value!.Language);

        host.Probe.FailLanguageCallback = true;
        files.WriteCommand("language-fault", "set-language", language: "en");
        var failed = host.Result("language-fault", "set-language", "failed");
        Equal("InternalError", failed.GetProperty("errorCode").GetString());
        Equal("Controlled language callback fault.", failed.GetProperty("errorMessage").GetString());
        Equal(PluginState.Faulted, host.PluginStatus.State);
    }

    private static void StatusWriteOrdering()
    {
        using var files = new MailboxFiles();
        using var harness = new HostHarness(package: false, failingBinding: true, configurationJson: "{}");
        var blockedTerminal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var terminalWriteStarted = new ManualResetEventSlim();
        var failNextWrite = 0;
        HostStatusDiagnosticChecks.InjectWriter(harness.Host, async copy =>
        {
            if (Interlocked.Exchange(ref failNextWrite, 0) == 1)
                throw new IOException("Controlled status persistence fault.");
            if (harness.Stopped)
            {
                terminalWriteStarted.Set();
                await blockedTerminal.Task;
            }
            await HostStatusDiagnosticChecks.Persist(copy);
        });
        harness.WriteStatus();
        var initial = ReadFile(files.Status);
        var session = initial.GetProperty("diagnostics").GetProperty("SessionId").GetString()!;
        Equal(1, initial.GetProperty("commandReceiptVersion").GetInt32());
        var loop = harness.StartWatchCommands();

        JsonElement AwaitResult(string id) => FileResult(files.Result, session, id, () => harness.Tick());

        Interlocked.Exchange(ref failNextWrite, 1);
        files.WriteCommand("status-write-fault", "status", expectedSessionId: session);
        var failed = AwaitResult("status-write-fault");
        CheckResult(failed, session, "status-write-fault", "status", "failed");
        Equal("InternalError", failed.GetProperty("errorCode").GetString());
        Check(failed.GetProperty("errorMessage").GetString()!.Contains("Controlled status persistence fault.", StringComparison.Ordinal));
        Equal(JsonValueKind.Null, failed.GetProperty("statusRevision").ValueKind);
        Equal(initial.GetProperty("revision").GetInt64(), ReadFile(files.Status).GetProperty("revision").GetInt64());

        files.WriteCommand("ordered-stop", "shutdown", expectedSessionId: session);
        try
        {
            Check(PumpUntil(() => harness.Tick(), () => terminalWriteStarted.IsSet, TimeSpan.FromSeconds(5)),
                "The actual terminal status writer was never entered.");
            PumpUntil(() => harness.Tick(), () => false, TimeSpan.FromMilliseconds(150));
            Check(harness.Stopped && harness.Binding!.Disposals == 1 && harness.Adapter.Disposals == 1,
                "The shutdown did not finish releasing the remaining owners.");
            Check(!ReadFile(files.Status).GetProperty("stopped").GetBoolean() &&
                ReadFile(files.Result).GetProperty("commandId").GetString() == "status-write-fault",
                "The shutdown receipt overtook its blocked terminal status write.");
        }
        finally { blockedTerminal.TrySetResult(true); }
        var shutdown = AwaitResult("ordered-stop");
        CheckResult(shutdown, session, "ordered-stop", "shutdown", "failed");
        Equal("InternalError", shutdown.GetProperty("errorCode").GetString());
        Check(shutdown.GetProperty("errorMessage").GetString()!.Contains("binding sentinel", StringComparison.Ordinal));
        CheckPublishedStatus(files.Status, shutdown, session, stopped: true);
        Check(loop.Wait(TimeSpan.FromSeconds(5)), "The real command worker did not exit after its shutdown receipt.");
        files.WriteCommand("after-stop", "status", expectedSessionId: session);
        PumpUntil(() => harness.Tick(), () => false, TimeSpan.FromSeconds(0.65));
        Equal("ordered-stop", ReadFile(files.Result).GetProperty("commandId").GetString());
    }

    private static void ReceiptWriteFailure()
    {
        using var files = new MailboxFiles();
        Check(new PluginActivationStore(files.Activation).Save(new[] { new PluginActivationIntent("example.panel", false, true) }).Success);
        using var host = new CommandHost(files.Activation);
        files.WriteCommand("receipt-baseline", "status");
        host.Result("receipt-baseline", "status", "succeeded");
        using (File.Open(files.Result, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            files.WriteCommand("blocked-receipt", "enable", "example.panel", expectedSessionId: host.SessionId);
            host.WaitForCommand("blocked-receipt");
            host.PumpFor(TimeSpan.FromSeconds(1.1));
            Equal("receipt-baseline", ReadFile(files.Result).GetProperty("commandId").GetString());
            Check(host.DiagnosticMessage("Cannot publish host command receipt."), "No actual receipt write failure was observed.");
        }
        host.PumpFor(TimeSpan.FromSeconds(0.6));
        Check(host.Main.GetSnapshot().Executed == 2 && host.Probe.Enables == 1 &&
            host.PluginStatus.State == PluginState.Enabled, "A receipt write failure reran an already dispatched side effect.");
        Check(!Directory.EnumerateFiles(Path.GetDirectoryName(files.Result)!, "command-result.json.*.tmp").Any(),
            "The failed atomic publication retained its temporary file.");
    }

    private static void CheckPublishedStatus(string path, JsonElement receipt, string session, bool stopped)
    {
        var snapshot = ReadFile(path);
        Equal(session, snapshot.GetProperty("diagnostics").GetProperty("SessionId").GetString());
        Equal(stopped, snapshot.GetProperty("stopped").GetBoolean());
        Check(snapshot.GetProperty("revision").GetInt64() >= receipt.GetProperty("statusRevision").GetInt64(),
            "The receipt signed a status revision that was not written.");
        if (stopped) Check(!snapshot.TryGetProperty("commandReceiptVersion", out _), "Stopped host still advertised command intake.");
        else Equal(1, snapshot.GetProperty("commandReceiptVersion").GetInt32());
    }

    private static void CheckPluginResult(JsonElement result, string? state, bool? desired, bool recorded, string? error = null)
    {
        Equal(state, result.GetProperty("pluginState").GetString());
        var actual = result.GetProperty("desiredEnabled");
        Equal(desired, actual.ValueKind == JsonValueKind.Null ? (bool?)null : actual.GetBoolean());
        Equal(recorded, result.GetProperty("intentRecorded").GetBoolean());
        Equal(error, result.GetProperty("errorCode").GetString());
    }

    private static void CheckResult(JsonElement result, string session, string id, string action, string outcome, string? pluginId = null)
    {
        Equal(1, result.GetProperty("schemaVersion").GetInt32());
        Equal(session, result.GetProperty("sessionId").GetString());
        Equal(id, result.GetProperty("commandId").GetString());
        Equal(action, result.GetProperty("action").GetString());
        Equal(pluginId, result.GetProperty("pluginId").GetString());
        Equal(outcome, result.GetProperty("outcome").GetString());
        if (outcome == "succeeded")
        {
            Equal(JsonValueKind.Null, result.GetProperty("errorCode").ValueKind);
            Equal(JsonValueKind.Null, result.GetProperty("errorMessage").ValueKind);
        }
    }

    private static JsonElement ReadFile(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }

    private static JsonElement FileResult(string path, string session, string id, Action pump)
    {
        JsonElement result = default;
        Check(PumpUntil(pump, () =>
        {
            if (!File.Exists(path)) return false;
            var current = ReadFile(path);
            if (current.GetProperty("sessionId").GetString() != session || current.GetProperty("commandId").GetString() != id)
                return false;
            result = current;
            return true;
        }, TimeSpan.FromSeconds(5)), "The real host did not publish a receipt for " + id);
        return result;
    }

    private static bool PumpUntil(Action pump, Func<bool> condition, TimeSpan timeout)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < timeout)
        {
            pump();
            if (condition()) return true;
            Thread.Sleep(10);
        }
        return false;
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
        internal string SessionId => _log.SessionId;
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

        internal JsonElement Result(string id, string action, string outcome, string? pluginId = null)
        {
            var result = FileResult(MailboxFiles.ResultPath, SessionId, id, Tick);
            CheckResult(result, SessionId, id, action, outcome, pluginId);
            return result;
        }

        internal bool DiagnosticMessage(string text) => DiagnosticRows().Any(row => row.GetProperty("message").GetString() == text);

        internal JsonElement Receipt(string id)
        {
            var receipts = DiagnosticRows().Where(row => row.TryGetProperty("context", out var context)
                && context.TryGetProperty("correlationId", out var correlation) && correlation.GetString() == id).ToArray();
            Check(receipts.Length == 1, $"Expected one correlated mailbox receipt for '{id}', got {receipts.Length}.");
            return receipts.Single();
        }

        private IEnumerable<JsonElement> DiagnosticRows()
        {
            Check(_log.Flush(TimeSpan.FromSeconds(5)).Completed, "Command diagnostics did not flush.");
            foreach (var file in Directory.EnumerateFiles(_log.GetSnapshot().DirectoryPath!, "events-*.jsonl"))
            {
                using var stream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                while (reader.ReadLine() is { } line)
                {
                    using var doc = JsonDocument.Parse(line);
                    yield return doc.RootElement.Clone();
                }
            }
        }

        internal void PumpFor(TimeSpan duration) => PumpUntil(Tick, () => false, duration);
        internal void WaitForCommand(string id)
            => Check(PumpUntil(Tick, () => (string?)Reflect.Get(_host, "_lastCommand") == id, TimeSpan.FromSeconds(5)),
                "The current-session command was not executed and acknowledged: " + id);

        private void Tick() => Reflect.Call(_host, "Tick", ++_tick);

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
        internal bool FailEnable, FailDisable, FailOwnedCleanup, FailLanguageCallback;
        internal IPluginLocalizationService? Localization;
        internal ProbePlugin(string? requiredCapability)
            => Manifest = new("example.panel", new(1, 0, 0), new(1, 0, 0),
                RequiredCapabilities: requiredCapability is null ? Array.Empty<string>() : new[] { requiredCapability });
        public void Enable(IPluginContext context)
        {
            Enables++;
            if (FailEnable) throw new InvalidOperationException("Controlled plugin activation fault.");
            ((IScopedPluginContext)context).CreateScope("cleanup-probe").Own(new Resource(this));
            Localization = context.GetService<IPluginLocalizationService>(StorageCapabilityIds.Localization).Service!;
            Check(Localization.Subscribe(_ =>
            {
                if (FailLanguageCallback) throw new InvalidOperationException("Controlled language callback fault.");
            }).Success);
        }
        public void Disable()
        { if (FailDisable) throw new InvalidOperationException("Controlled plugin cleanup fault."); }

        private sealed class Resource : IDisposable
        {
            private readonly ProbePlugin _owner;
            internal Resource(ProbePlugin owner) => _owner = owner;
            public void Dispose()
            {
                if (_owner.FailOwnedCleanup) throw new InvalidOperationException("Controlled nested resource cleanup fault.");
            }
        }
    }

    private sealed class MailboxFiles : IDisposable
    {
        private static readonly string Root = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "BepInEx", "RizToolbox");
        internal string Command => Path.Combine(Root, "command.json");
        internal string Settings => Path.Combine(Root, "settings.json");
        internal string Activation => Path.Combine(Root, "activation.json");
        internal string Status => Path.Combine(Root, "status.json");
        internal static string ResultPath => Path.Combine(Root, "command-result.json");
        internal string Result => ResultPath;
        private readonly Dictionary<string, byte[]?> _originals;

        internal MailboxFiles()
        {
            _originals = new[] { Command, Settings, Activation, Result }.ToDictionary(path => path,
                path => File.Exists(path) ? File.ReadAllBytes(path) : null);
            File.WriteAllText(Settings, "{\"enableExamples\":true,\"language\":\"en\"}");
        }

        internal void WriteCommand(string id, string action, string? pluginId = null, string? language = null,
            string? expectedSessionId = null)
        {
            var temporary = Command + ".test.tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new { id, action, pluginId, language, expectedSessionId },
                new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }));
            File.Move(temporary, Command, true);
        }

        public void Dispose()
        {
            foreach (var pair in _originals)
                if (pair.Value is null) File.Delete(pair.Key); else File.WriteAllBytes(pair.Key, pair.Value);
        }
    }
}
