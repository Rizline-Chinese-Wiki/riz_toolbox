using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using RizToolbox.Adapter;
using RizToolbox.Core;
using RizToolbox.Core.Diagnostics;
using RizToolbox.Sdk;
using static Helpers;

internal static class HostStatusDiagnosticChecks
{
    internal static readonly (string Name, Action Run)[] All =
    {
        ("real host writes fresh scene observations at status cadence and omits them after shutdown", ObservedScenes),
        ("real host status capture faults preserve frames and recover with bounded failure evidence", CaptureFailure),
        ("real host copies remain detached during concurrent reads and terminal shutdown", DetachedDuringShutdown),
        ("real host Tick records phase durations and allocations through faults and status cadence", TickTiming),
        ("real host persistence faults preserve frames and recover to actual status files", PersistenceFailure),
        ("status budgets bound exhausted text and nested rows and account for the final capture error", CaptureBudget)
    };

    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static JsonElement Document(object copy) => JsonSerializer.SerializeToElement(copy, copy.GetType());
    private static string Encoded(object copy) => JsonSerializer.Serialize(copy, copy.GetType());

    // Close a test bridge over the real internal DTO type. Publication and
    // capture still run through ToolboxHost; only its persistence sink changes.
    internal static void InjectWriter(object host, Func<object, Task> persist)
    {
        Check(Reflect.Get(host, "_statusWriter") is null, "Inject the sink before real host capture creates a writer.");
        var copyType = Reflect.HostType.GetField("_statusWriter", Members)!.FieldType.GetGenericArguments()[0];
        var writer = typeof(HostStatusDiagnosticChecks).GetMethod(nameof(CreateWriter), Statics)!
            .MakeGenericMethod(copyType).Invoke(null, new object[] { persist })!;
        Reflect.Set(host, "_statusWriter", writer);
    }

    private static object CreateWriter<T>(Func<object, Task> persist) where T : class
        => new LatestDiagnosticWriter<T>(copy => persist(copy));

    internal static Task Persist(object copy)
        => (Task)Reflect.HostType.Assembly.GetType("RizToolbox.Loader.HostStatusPersistence", true)!
            .GetMethod("Write", Statics)!.Invoke(null, new[] { copy })!;

    private static LatestDiagnosticWaitResult WaitAttempt(object host, long revision)
    {
        var writer = Reflect.Get(host, "_statusWriter")!;
        var result = (LatestDiagnosticWaitResult)writer.GetType().GetMethod("WaitThrough")!.Invoke(writer,
            new object[] { revision, TimeSpan.FromSeconds(5) })!;
        Check(result.Completed, $"Published status revision {revision} did not finish its actual attempt within five seconds.");
        return result;
    }

    private static void WaitExit(object host)
        => Check(SpinWait.SpinUntil(() => StatusWriterSnapshot(host).Completed, TimeSpan.FromSeconds(5)),
            "The terminal host writer did not drain and exit after the actual persistence operation returned.");

    private static void RequestTickStatus(HostHarness h)
    {
        Reflect.Set(h.Host, "_statusRequested", true);
        h.Tick();
    }

    private static void ObservedScenes()
    {
        using var h = new HostHarness(package: false, failingBinding: false);
        var state = new GameEventState();
        h.Adapter.GameEvents = state;
        void CheckScene(string observation, string? scene = null, long? generation = null)
        {
            using var status = h.Status();
            var root = status.RootElement;
            var capturedGeneration = root.GetProperty("sceneGeneration");
            Check(!root.GetProperty("stopped").GetBoolean()
                && root.GetProperty("sceneObservation").GetString() == observation
                && root.GetProperty("scene").GetString() == scene
                && (generation is null ? capturedGeneration.ValueKind == JsonValueKind.Null
                    : capturedGeneration.GetInt64() == generation),
                "The status file did not reflect the latest eligible scene observation.");
        }

        h.WriteStatus();
        CheckScene("Unavailable");
        state.SetAvailability(scenes: true);
        RequestTickStatus(h);
        CheckScene("AwaitingSample");

        state.Scene("GameStart");
        RequestTickStatus(h);
        CheckScene("Observed", "GameStart", 1);
        state.Scene("LevelList");
        var sceneReads = h.Adapter.SceneStatusReads;
        Reflect.Set(h.Host, "_nextStatus", double.PositiveInfinity);
        h.Tick();
        Check(h.Adapter.SceneStatusReads == sceneReads, "A tick without status capture read the scene.");
        CheckScene("Observed", "GameStart", 1);
        RequestTickStatus(h);
        CheckScene("Observed", "LevelList", 2);

        state.SetAvailability(scenes: false);
        RequestTickStatus(h);
        CheckScene("Unavailable");
        state.SetAvailability(scenes: true);
        RequestTickStatus(h);
        CheckScene("AwaitingSample");
        Check(state.Scene("LevelList") is null, "A same-name sample fabricated a scene transition.");
        RequestTickStatus(h);
        CheckScene("Observed", "LevelList", 2);

        var expectedNameLength = 4096;
        state.Scene(new string('s', expectedNameLength + 1));
        RequestTickStatus(h);
        CheckScene("Observed", new string('s', expectedNameLength), 3);

        sceneReads = h.Adapter.SceneStatusReads;
        Reflect.Call(h.Host, "Shutdown", false, "scene-status-check");
        WaitExit(h.Host);
        using var terminal = h.Status();
        var stopped = terminal.RootElement;
        Check(stopped.GetProperty("stopped").GetBoolean() && h.Adapter.SceneStatusReads == sceneReads
            && !stopped.TryGetProperty("scene", out _) && !stopped.TryGetProperty("sceneGeneration", out _)
            && !stopped.TryGetProperty("sceneObservation", out _),
            "Terminal status read the disposed scene provider or advertised a current scene.");
    }

    private static void CaptureFailure()
    {
        using var h = new HostHarness(package: false, failingBinding: false);
        h.WriteStatus();
        var baseline = StatusWriterSnapshot(h.Host);
        h.Adapter.OnSupportedCapabilities = () => throw new InvalidOperationException("capture sentinel " + new string('x', 900));
        RequestTickStatus(h);
        var failed = StatusWriterSnapshot(h.Host);
        var error = (string?)Reflect.Get(h.Host, "_statusCaptureError");
        Check(h.Frames == 1 && h.Adapter.Ticks == 1 && !h.Stopped &&
            (long)Reflect.Get(h.Host, "_statusCaptureFailures")! == 1 &&
            error is { Length: > 0 and <= 512 } && error.Contains("capture sentinel", StringComparison.Ordinal),
            "A throwing capture getter escaped Tick, skipped frames, or lost bounded capture failure evidence.");
        Check(failed.Published == baseline.Published && failed.LastPublished == baseline.LastPublished &&
            (long)Reflect.Get(h.Host, "_statusRevision")! > failed.LastPublished,
            "A failed capture falsely advanced the published revision.");
        using (var retained = h.Status())
            Check(retained.RootElement.GetProperty("revision").GetInt64() == baseline.LastPublished,
                "A failed capture replaced the last successfully persisted status.");

        h.Adapter.OnSupportedCapabilities = null;
        RequestTickStatus(h);
        WaitPublishedStatus(h.Host);
        using (var recovered = h.Status())
        {
            var health = recovered.RootElement.GetProperty("statusDiagnostics");
            Check(h.Frames == 2 && h.Adapter.Ticks == 2 && !h.Stopped &&
                recovered.RootElement.GetProperty("revision").GetInt64() > baseline.LastPublished &&
                health.GetProperty("CaptureFailures").GetInt64() == 1 &&
                health.GetProperty("LastCaptureError").GetString() == error,
                "A later healthy capture did not recover or forgot the original capture failure.");
        }
        Reflect.Call(h.Host, "Shutdown", false, "capture-recovery");
        WaitPublishedStatus(h.Host);
        WaitExit(h.Host);
        using var terminal = h.Status();
        Check(terminal.RootElement.GetProperty("stopped").GetBoolean() &&
            terminal.RootElement.GetProperty("statusDiagnostics").GetProperty("CaptureFailures").GetInt64() == 1,
            "Terminal status lost the recovered host's capture failure history.");
    }

    private static object PackageUpdate(IReadOnlyList<string> files, string message)
    {
        var type = Reflect.HostType.GetNestedType("PackageUpdateView", BindingFlags.NonPublic)!;
        return Activator.CreateInstance(type, Members, binder: null,
            args: new object[] { "package.one", true, files, message }, culture: null)!;
    }

    private static IList PackageUpdates(object host, IReadOnlyList<string> files, string message)
    {
        var type = Reflect.HostType.GetNestedType("PackageUpdateView", BindingFlags.NonPublic)!;
        var rows = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(type))!;
        rows.Add(PackageUpdate(files, message));
        Reflect.Set(host, "_packageUpdates", rows);
        return rows;
    }

    private static void DetachedDuringShutdown()
    {
        using var h = new HostHarness(package: true, failingBinding: false);
        var plugins = (PluginHost)Reflect.Get(h.Host, "_plugins")!;
        var capabilities = (CapabilityRegistry)Reflect.Get(h.Host, "_capabilities")!;
        Check(plugins.Register(h.Packages.Packages.Single().Plugin).Success &&
            plugins.SetDesiredEnabled("package.one", true).Success, "The real plugin fixture did not enable.");
        capabilities.Register<object>("fixture.snapshot", new object());
        var advertised = new List<string> { "fixture.snapshot" };
        var scenes = new List<string> { "scene-before" };
        var parameters = new List<string> { "System.Int32" };
        var required = new List<string> { "fixture.required-before" };
        var symbols = new List<AdapterSymbolRequirement>
        {
            new("symbol-before", "Offline.dll", "Offline", "Target", AdapterSymbolKind.Method,
                "Read", ParameterTypes: parameters)
        };
        var probes = new List<AdapterSymbolProbe> { new("symbol-before", true, "before signature", null) };
        var manifest = new AdapterModuleManifest("module-before", new Version(1, 0), advertised,
            required, scenes, symbols, "before evidence");
        var modules = new List<AdapterModuleStatus>
        {
            new(manifest, "offline-before", "before fingerprint", AdapterModuleState.Active,
                AdapterVerificationTier.Declared, 1, null, null, probes)
        };
        h.Adapter.OnSupportedCapabilities = () => advertised;
        h.Adapter.OnGetModules = () => modules;
        var files = new List<string> { "before.dll" };
        var updates = PackageUpdates(h.Host, files, "before update");
        var demand = new HashSet<string>(StringComparer.Ordinal) { "demand-before" };
        Reflect.Set(h.Host, "_lastCapabilityDemand", demand);
        Check(plugins.Extensions.Register(new ExtensionPoint<string>("fixture.extension.before", new Version(1, 0))).Success,
            "The extension fixture did not register.");
        h.Adapter.OnDispose = () => capabilities.SetUnavailable("fixture.snapshot", "disposed provider");

        var first = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopReading = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reading = new ManualResetEventSlim();
        var written = new ConcurrentQueue<object>();
        Task? reader = null;
        InjectWriter(h.Host, async copy =>
        {
            written.Enqueue(copy);
            if (first.TrySetResult(copy)) await release.Task.ConfigureAwait(false);
            await Persist(copy).ConfigureAwait(false);
        });
        try
        {
            Reflect.Call(h.Host, "WriteStatus", false);
            Check(first.Task.Wait(TimeSpan.FromSeconds(5)), "The actual host's first captured copy did not reach persistence.");
            var oldCopy = first.Task.GetAwaiter().GetResult();
            var before = Document(oldCopy);
            var oldEncoding = Encoded(oldCopy);
            Check(before.GetProperty("Plugins")[0].GetProperty("State").GetInt32() == (int)PluginState.Enabled &&
                before.GetProperty("Capabilities")[0].GetProperty("Available").GetBoolean() &&
                before.GetProperty("AdapterModules")[0].GetProperty("Manifest").GetProperty("RequiredSymbols")[0]
                    .GetProperty("ParameterTypes")[0].GetString() == "System.Int32" &&
                before.GetProperty("CapabilityDemand")[0].GetString() == "demand-before",
                "The detached baseline did not include live plugin, capability, nested module, and demand state.");
            reader = Task.Run(() =>
            {
                reading.Set();
                var elapsed = Stopwatch.StartNew();
                while (!stopReading.Task.IsCompleted && elapsed.Elapsed < TimeSpan.FromSeconds(5))
                {
                    Check(Encoded(oldCopy) == oldEncoding, "A previously captured DTO changed during concurrent owner mutation.");
                    Thread.Sleep(1);
                }
                Check(Encoded(oldCopy) == oldEncoding, "The detached DTO changed at the end of concurrent reading.");
            });
            Check(reading.Wait(TimeSpan.FromSeconds(5)), "The detached status reader did not start.");

            advertised[0] = "fixture.after";
            required[0] = "fixture.required-after";
            scenes[0] = "scene-after";
            parameters[0] = "System.String";
            symbols.Clear(); probes.Clear(); modules.Clear();
            files[0] = "after.dll";
            updates.Clear(); updates.Add(PackageUpdate(files, "after update"));
            demand.Clear(); demand.Add("demand-after");
            Check(plugins.SetDesiredEnabled("package.one", false).Success, "The live plugin fixture did not disable.");
            capabilities.SetUnavailable("fixture.snapshot", "availability-after");
            Check(plugins.Extensions.Register(new ExtensionPoint<string>("fixture.extension.after", new Version(1, 0))).Success,
                "The later extension fixture did not register.");
            ((StructuredDiagnosticLogger)Reflect.Get(h.Host, "_log")!).Log(LogLevel.Info, "owner changed after capture");
            h.Tick();
            Reflect.Call(h.Host, "WriteStatus", false);
            var reads = (h.Adapter.CapabilityReads, h.Adapter.ModuleReads, h.Adapter.BuildReads);
            var shuttingDown = Stopwatch.StartNew();
            Reflect.Call(h.Host, "Shutdown", false, "detached-check");
            Check(shuttingDown.Elapsed < TimeSpan.FromSeconds(2) && h.Stopped && h.Adapter.Disposals == 1 &&
                h.Frames == 1 && h.Adapter.Ticks == 1,
                "Real Shutdown waited for the blocked persistence sink or failed to release the live adapter.");
            var held = StatusWriterSnapshot(h.Host);
            Check(held.Closed && !held.Completed && held.Running == 1 && held.Queued == 1 && held.Coalesced >= 1 &&
                reads == (h.Adapter.CapabilityReads, h.Adapter.ModuleReads, h.Adapter.BuildReads) &&
                (long)Reflect.Get(h.Host, "_statusCaptureFailures")! == 0,
                "Terminal capture queried a disposed adapter or failed to replace the pending live copy.");
            Check(Encoded(oldCopy) == oldEncoding, "Shutdown mutated an already captured live status copy.");
            stopReading.TrySetResult(true);
            Check(reader.Wait(TimeSpan.FromSeconds(5)), "The concurrent detached-copy reader did not return.");
            reader.GetAwaiter().GetResult();
            release.TrySetResult(true);
            WaitPublishedStatus(h.Host);
            WaitExit(h.Host);
            var copies = written.ToArray();
            Check(copies.Length == 2 && Document(copies[1]).GetProperty("Stopped").GetBoolean() &&
                Document(copies[1]).GetProperty("AdapterModules").GetArrayLength() == 0 &&
                Document(copies[1]).GetProperty("Performance").ValueKind == JsonValueKind.Null &&
                reads == (h.Adapter.CapabilityReads, h.Adapter.ModuleReads, h.Adapter.BuildReads),
                "The writer retained an intermediate live copy or touched provider state during terminal persistence.");
            using var terminal = h.Status();
            // Bounded diagnostics preserve registry traversal order, rather than
            // sorted SDK catalog order. Check the fixture capability by identity.
            var terminalCapability = terminal.RootElement.GetProperty("capabilityDetails").EnumerateArray()
                .Single(row => row.GetProperty("Id").GetString() == "fixture.snapshot");
            Check(terminal.RootElement.GetProperty("stopped").GetBoolean() &&
                terminal.RootElement.GetProperty("stopReason").GetString() == "detached-check" &&
                terminal.RootElement.GetProperty("packageUpdates")[0].GetProperty("changedFiles")[0].GetString() == "after.dll" &&
                !terminalCapability.GetProperty("Available").GetBoolean() &&
                terminalCapability.GetProperty("UnavailableReason").GetString() == "disposed provider",
                "The actual terminal file did not preserve the last owner state after blocked persistence resumed.");
        }
        finally
        {
            stopReading.TrySetResult(true);
            release.TrySetResult(true);
            if (reader is not null) Check(reader.Wait(TimeSpan.FromSeconds(5)), "The detached reader failed to leave during cleanup.");
        }
    }

    private static readonly string[] Phases =
    {
        "before-command-boundaries", "main-dispatch", "validation-commands", "adapter-commands",
        "after-command-boundaries", "capability-demand", "adapter-tick", "frame-events", "status-capture"
    };

    private static CallbackPerformance Phase(PerformanceSnapshot snapshot, string operation)
        => snapshot.Callbacks.Single(row => row.Owner == "host-phase" && row.Operation == operation);

    private static void TickTiming()
    {
        using var h = new HostHarness(package: false, failingBinding: false);
        var performance = new CallbackPerformanceMonitor(callbackBudgetMilliseconds: 1e-9, consecutiveLimit: 1);
        Reflect.Set(h.Host, "_performance", performance);
        var events = (EventHub)Reflect.Get(h.Host, "_events")!;
        events.Performance = performance;
        using var badFrame = events.Subscribe<FrameUpdate>(_ => throw new IOException("frame timing sentinel"));
        byte[]? allocation = null;
        h.Adapter.OnFlush = count => { if (count == 1) throw new IOException("boundary timing sentinel"); };
        h.Adapter.OnPumpCommands = () => throw new IOException("command timing sentinel");
        h.Adapter.OnTick = sequence =>
        {
            allocation = new byte[8192];
            if (sequence == 1) throw new IOException("adapter timing sentinel");
        };
        h.Adapter.OnSupportedCapabilities = () => throw new IOException("status timing sentinel");
        Reflect.Set(h.Host, "_nextStatus", 0d);
        h.Tick();
        var first = performance.GetSnapshot();
        Check(h.Frames == 1 && h.Adapter.Ticks == 1 && !h.Stopped &&
            Phases.All(name => Phase(first, name).TotalSamples == 1) &&
            Phase(first, "adapter-tick").TotalAllocatedBytes >= 8192 && allocation is { Length: 8192 } &&
            first.Callbacks.All(row => row.OverBudget == 0 && row.ConsecutiveOverBudget == 0),
            "The real Tick omitted failed phases, failed to record allocations, or enforced a host phase budget.");

        h.Adapter.OnFlush = null; h.Adapter.OnPumpCommands = null; h.Adapter.OnSupportedCapabilities = null;
        Reflect.Set(h.Host, "_nextStatus", double.PositiveInfinity);
        h.Tick();
        var second = performance.GetSnapshot();
        Check(Phases.Take(Phases.Length - 1).All(name => Phase(second, name).TotalSamples == 2) &&
            Phase(second, "status-capture").TotalSamples == 1 && h.Frames == 2 && h.Adapter.Ticks == 2 &&
            second.Callbacks.Single(row => row.Owner == "host" && row.Operation == "managed-update").TotalSamples == 2 &&
            second.Callbacks.Single(row => row.Owner == "host" && row.Operation == "update-arrival-interval").TotalSamples == 1,
            "Tick phase counts did not distinguish ordinary frames, outer host work, and arrival intervals.");
        RequestTickStatus(h);
        WaitPublishedStatus(h.Host);
        var third = performance.GetSnapshot();
        Check(Phases.Take(Phases.Length - 1).All(name => Phase(third, name).TotalSamples == 3) &&
            Phase(third, "status-capture").TotalSamples == 2 && h.Frames == 3 && h.Adapter.Ticks == 3 &&
            third.Callbacks.All(row => double.IsFinite(row.P99Milliseconds) && row.P99Milliseconds >= 0 &&
                row.OverBudget == 0) && !third.Callbacks.Any(row => row.Owner == "runtime"),
            "An explicit requested Tick status did not record its actual phase or invented runtime entry timing.");
        using (var status = h.Status())
        {
            var rows = status.RootElement.GetProperty("performance").GetProperty("Callbacks").EnumerateArray().ToArray();
            Check(Phases.All(name => rows.Any(row => row.GetProperty("Owner").GetString() == "host-phase" &&
                row.GetProperty("Operation").GetString() == name)) &&
                rows.Single(row => row.GetProperty("Operation").GetString() == "adapter-tick").GetProperty("TotalSamples").GetInt64() == 3,
                "The real persisted status did not contain actual measured Tick phases.");
        }
        Reflect.Call(h.Host, "Shutdown", false, "timing-check");
        WaitPublishedStatus(h.Host);
        h.Tick();
        Check(Phase(performance.GetSnapshot(), "adapter-tick").TotalSamples == 3,
            "A stopped Tick added adapter phase samples.");
    }

    private static void PersistenceFailure()
    {
        using var h = new HostHarness(package: false, failingBinding: false);
        var attempts = 0;
        InjectWriter(h.Host, copy =>
        {
            var attempt = Interlocked.Increment(ref attempts);
            if (attempt == 1) throw new IOException("sync persistence sentinel " + new string('x', 900));
            if (attempt == 2) return Task.FromException(new IOException("async persistence sentinel"));
            return Persist(copy);
        });
        RequestTickStatus(h);
        var first = WaitAttempt(h.Host, StatusWriterSnapshot(h.Host).LastPublished);
        Check(!first.Persisted && first.LastWritten == 0 && first.LastError is { Length: <= 512 } &&
            first.LastError.Contains("sync persistence sentinel", StringComparison.Ordinal) &&
            h.Frames == 1 && h.Adapter.Ticks == 1 && !h.Stopped,
            "A synchronous sink failure escaped Tick or falsely claimed persisted status.");
        RequestTickStatus(h);
        var second = WaitAttempt(h.Host, StatusWriterSnapshot(h.Host).LastPublished);
        var failed = StatusWriterSnapshot(h.Host);
        Check(!second.Persisted && failed.Failures == 2 && failed.LastWritten == 0 &&
            failed.LastError?.Contains("async persistence sentinel", StringComparison.Ordinal) == true &&
            (long)Reflect.Get(h.Host, "_statusCaptureFailures")! == 0 &&
            h.Frames == 2 && h.Adapter.Ticks == 2 && !h.Stopped,
            "An asynchronous sink failure stopped frames or was misclassified as a capture failure.");
        Reflect.Call(h.Host, "WriteStatus", false);
        var recovered = WaitPublishedStatus(h.Host);
        using (var status = h.Status())
        {
            var persistence = status.RootElement.GetProperty("statusDiagnostics").GetProperty("Persistence");
            Check(persistence.GetProperty("Failures").GetInt64() == 2 &&
                persistence.GetProperty("LastWritten").GetInt64() == 0 &&
                persistence.GetProperty("LastError").GetString()!.Contains("async persistence sentinel", StringComparison.Ordinal) &&
                (long)Reflect.Get(h.Host, "_reportedStatusWriteFailures")! == 2,
                "The recovered actual file lost failed-attempt health or claimed prior successful persistence.");
        }
        h.WriteStatus();
        using (var status = h.Status())
            Check(status.RootElement.GetProperty("statusDiagnostics").GetProperty("Persistence")
                .GetProperty("LastWritten").GetInt64() == recovered.Revision,
                "The next real capture did not report the successful recovery revision.");
        h.Tick();
        Check(h.Frames == 3 && h.Adapter.Ticks == 3 && !h.Stopped, "A recovered sink left later host frames inactive.");
        Reflect.Call(h.Host, "Shutdown", false, "persistence-recovery");
        WaitPublishedStatus(h.Host);
        WaitExit(h.Host);
        using var terminal = h.Status();
        Check(terminal.RootElement.GetProperty("stopped").GetBoolean() && StatusWriterSnapshot(h.Host).Failures == 2,
            "Terminal persistence lost failure history after recovery.");
    }

    private static void CaptureBudget()
    {
        var type = Reflect.HostType.Assembly.GetType("RizToolbox.Loader.StatusCopyBudget", true)!;
        int Constant(string name) => (int)type.GetField(name, Statics)!.GetRawConstantValue()!;
        object NewBudget() => Activator.CreateInstance(type, nonPublic: true)!;
        long Truncated(object budget)
        {
            var limits = type.GetProperty("Limits", Members)!.GetValue(budget)!;
            return (long)limits.GetType().GetProperty("TruncatedValues")!.GetValue(limits)!;
        }
        var maximumCharacters = Constant("MaximumCharacters");
        var maximumTotalCharacters = Constant("MaximumTotalCharacters");
        var maximumRows = Constant("MaximumRows");
        var maximumTotalRows = Constant("MaximumTotalRows");
        var text = type.GetMethod("Text", Members)!;
        var budget = NewBudget();
        var largeText = new string('x', maximumCharacters);
        long characters = 0;
        for (var remaining = maximumTotalCharacters; remaining > 0; remaining -= maximumCharacters)
        {
            var copied = (string)text.Invoke(budget, new object[] { largeText })!;
            characters += copied.Length;
        }
        Check(characters == maximumTotalCharacters, "The text budget did not admit its exact aggregate allowance.");
        var beforeExhaustion = Truncated(budget);
        for (var index = 0; index < 64; index++) characters += ((string)text.Invoke(budget, new object[] { largeText })!).Length;
        Check(characters <= maximumTotalCharacters && Truncated(budget) >= beforeExhaustion + 64,
            "Exhausted text added uncharged placeholder characters or omitted truncation evidence.");
        var rowsBudget = NewBudget();
        var rows = type.GetMethod("Rows", Members)!.MakeGenericMethod(typeof(string), typeof(string));
        var overfull = Enumerable.Repeat("row", maximumRows + 1).ToArray();
        var totalRows = 0;
        for (var index = 0; index < maximumTotalRows / maximumRows; index++)
        {
            var previous = Truncated(rowsBudget);
            var copied = (IReadOnlyList<string>)rows.Invoke(rowsBudget, new object[] { overfull, new Func<string, string>(value => value) })!;
            Check(copied.Count <= maximumRows && Truncated(rowsBudget) > previous,
                "A collection overflow ignored its row cap or omitted truncation accounting.");
            totalRows += copied.Count;
        }
        var beforeRowsExhaustion = Truncated(rowsBudget);
        var exhausted = (IReadOnlyList<string>)rows.Invoke(rowsBudget,
            new object[] { new[] { "after exhaustion" }, new Func<string, string>(value => value) })!;
        Check(totalRows == maximumTotalRows && exhausted.Count == 0 && Truncated(rowsBudget) > beforeRowsExhaustion,
            "An exhausted nested-row allowance retained extra rows or forgot its truncation evidence.");

        using var h = new HostHarness(package: false, failingBinding: false);
        var repeatedText = Enumerable.Repeat(largeText, maximumRows).ToArray();
        var manifest = new AdapterModuleManifest(largeText, new Version(1, 0), repeatedText, repeatedText,
            repeatedText, Array.Empty<AdapterSymbolRequirement>(), largeText);
        var module = new AdapterModuleStatus(manifest, largeText, largeText, AdapterModuleState.Active,
            AdapterVerificationTier.Declared, 1, null, null, Array.Empty<AdapterSymbolProbe>());
        h.Adapter.OnGetModules = () => Enumerable.Repeat(module, maximumRows).ToArray();
        var captures = new ConcurrentQueue<object>();
        InjectWriter(h.Host, copy => { captures.Enqueue(copy); return Task.CompletedTask; });
        Reflect.Call(h.Host, "WriteStatus", false);
        WaitPublishedStatus(h.Host);
        var initial = Document(captures.ToArray().Single());
        var initialTruncation = initial.GetProperty("CaptureLimits").GetProperty("TruncatedValues").GetInt64();
        Reflect.Set(h.Host, "_statusCaptureError", "late capture error after the aggregate text budget");
        Reflect.Call(h.Host, "WriteStatus", false);
        WaitPublishedStatus(h.Host);
        var withError = Document(captures.ToArray().Last());
        Check(initialTruncation > 0 &&
            withError.GetProperty("StatusHealth").GetProperty("LastCaptureError").GetString()!.Length == 0 &&
            withError.GetProperty("CaptureLimits").GetProperty("TruncatedValues").GetInt64() == initialTruncation + 1,
            "The real host captured Limits before accounting for the final truncated LastCaptureError value.");
        Reflect.Call(h.Host, "Shutdown", false, "budget-check");
        WaitPublishedStatus(h.Host);
        WaitExit(h.Host);
    }
}
