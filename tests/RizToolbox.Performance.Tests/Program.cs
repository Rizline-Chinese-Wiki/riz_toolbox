using System.Diagnostics;
using System.Runtime.CompilerServices;
using RizToolbox.Core;
using RizToolbox.Core.Diagnostics;
using RizToolbox.Core.Hooks;
using RizToolbox.Sdk;

var tests = new (string Name, Action Run)[]
{
    ("Nearest-rank callback percentiles and allocation totals use recorded units", () => {
        var m = new CallbackPerformanceMonitor(128);
        for (int i = 1; i <= 100; i++) m.Record("probe", "callback", i, i * 2);
        var s = m.GetSnapshot().Callbacks.Single();
        Check(s.TotalSamples == 100 && s.WindowSamples == 100 && s.P50Milliseconds == 50 && s.P95Milliseconds == 95 && s.P99Milliseconds == 99, "Percentile ranks differ.");
        Check(s.TotalAllocatedBytes == 10100 && s.MaximumAllocatedBytes == 200 && s.MaximumMilliseconds == 100, "Allocation or maximum units differ.");
    }),
    ("Ring window retains recent samples and preserves all-time counts", () => {
        var m = new CallbackPerformanceMonitor(16); for (int i = 1; i <= 100; i++) m.Record("p", "e", i);
        var s = m.GetSnapshot().Callbacks.Single(); Check(s.WindowSamples == 16 && s.TotalSamples == 100 && s.P50Milliseconds == 92 && s.P99Milliseconds == 100, "Window did not evict old values.");
    }),
    ("Series and returned snapshots stay bounded and immutable", () => {
        var m = new CallbackPerformanceMonitor(16, 2); m.Record("a", "a", 1); var first = m.GetSnapshot();
        m.Record("b", "b", 2); for (int i = 0; i < 100; i++) m.Record("c", "flood" + i, 3);
        var last = m.GetSnapshot(); Check(first.Callbacks.Count == 1 && last.Callbacks.Count == 2 && last.DroppedSeriesSamples == 100, "Series cap or snapshot isolation failed.");
        Check(first.SessionId == last.SessionId, "A query changed collector identity.");
    }),
    ("Detached capture preserves recent raw order and all-time maxima after ring overwrite", () => {
        var m = new CallbackPerformanceMonitor(16, 2, callbackBudgetMilliseconds: 10, consecutiveLimit: 3);
        m.Record("probe", "callback", 1000, 4096);
        for (int i = 1; i <= 20; i++) m.Record("probe", "callback", i == 12 ? 20 : i == 20 ? 12 : i, i);
        m.Record("probe", "other", 3); for (int i = 0; i < 7; i++) m.Record("excess", "callback", 1);
        var beforeCapture = DateTimeOffset.UtcNow; var copy = m.CaptureWindow(); var afterCapture = DateTimeOffset.UtcNow;
        var window = copy.Callbacks.Single(x => x.Operation == "callback");
        Check(copy.CapturedAtUtc >= beforeCapture && copy.CapturedAtUtc <= afterCapture && copy.CapturedAtUtc.Offset == TimeSpan.Zero,
            "Capture time was not recorded on the collection thread.");
        Check(window.Samples.SequenceEqual(new double[] { 5, 6, 7, 8, 9, 10, 11, 20, 13, 14, 15, 16, 17, 18, 19, 12 }) && window.WindowSamples == 16,
            "Capture sorted samples or copied stale ring values.");
        Check(copy.RetainedSamplesPerSeries == 16 && copy.MaximumSeries == 2 && copy.DroppedSeriesSamples == 7
            && copy.CallbackBudgetMilliseconds == 10 && copy.ConsecutiveLimit == 3, "Capture dropped quota or budget metadata.");
        Check(window.TotalSamples == 21 && window.MaximumMilliseconds == 1000 && window.TotalAllocatedBytes == 4306
            && window.MaximumAllocatedBytes == 4096 && window.OverBudget == 11 && window.ConsecutiveOverBudget == 10,
            "Window eviction lost all-time duration, allocation or budget counters.");
        var snapshot = CallbackPerformanceMonitor.Summarize(copy);
        var row = snapshot.Callbacks.Single(x => x.Operation == "callback");
        Check(row.P50Milliseconds == 12 && row.P95Milliseconds == 20 && row.P99Milliseconds == 20
            && row.MaximumMilliseconds == 1000 && row.TotalAllocatedBytes == 4306 && snapshot.CapturedAtUtc == copy.CapturedAtUtc,
            "Detached summary changed nearest ranks, all-time counters or original capture time.");
        var callbacks = (IList<CallbackPerformanceWindowCopy>)copy.Callbacks;
        var samples = (IList<double>)window.Samples;
        Check(callbacks.IsReadOnly && samples.IsReadOnly, "A detached copy exposed mutable lists.");
        Throws<NotSupportedException>(() => callbacks[0] = callbacks[0]);
        Throws<NotSupportedException>(() => samples[0] = 9000);
        Throws<NotSupportedException>(() => ((IList<CallbackPerformance>)snapshot.Callbacks)[0] = row);
    }),
    ("Worker summaries use only detached copies and preserve ordinal SDK snapshot behavior", () => {
        var m = new CallbackPerformanceMonitor(16, 8);
        m.Record("z", "a", 2); m.Record("a", "a", 3); m.Record("a", "B", 4); m.Record("A", "z", 5);
        var copy = m.CaptureWindow(); var raw = copy.Callbacks.Select(x => x.Samples.ToArray()).ToArray();
        var ownerThread = Environment.CurrentManagedThreadId;
        var worker = Task.Run(() => {
            Check(Environment.CurrentManagedThreadId != ownerThread, "Summary test did not leave the owner thread.");
            var first = CallbackPerformanceMonitor.Summarize(copy);
            var second = CallbackPerformanceMonitor.Summarize(copy);
            Check(first.Callbacks.SequenceEqual(second.Callbacks), "Repeated summary consumed a copy or changed its values.");
            return first;
        }).GetAwaiter().GetResult();
        Check(worker.CapturedAtUtc == copy.CapturedAtUtc && worker.SessionId == copy.SessionId
            && worker.Callbacks.Select(x => (x.Owner, x.Operation)).SequenceEqual(new[] { ("A", "z"), ("a", "B"), ("a", "a"), ("z", "a") }),
            "Worker summary changed capture identity or ordinal series order.");
        for (int i = 0; i < copy.Callbacks.Count; i++) Check(copy.Callbacks[i].Samples.SequenceEqual(raw[i]), "Worker sorting modified the captured raw window.");
        IPerformanceService service = m;
        var compatible = service.GetSnapshot();
        Check(compatible.SessionId == worker.SessionId && compatible.RetainedSamplesPerSeries == worker.RetainedSamplesPerSeries
            && compatible.MaximumSeries == worker.MaximumSeries && compatible.DroppedSeriesSamples == worker.DroppedSeriesSamples
            && compatible.Callbacks.SequenceEqual(worker.Callbacks), "SDK GetSnapshot no longer reuses the same bounded summary semantics.");
        Throws<ArgumentNullException>(() => CallbackPerformanceMonitor.Summarize(null!));
    }),
    ("Slow summary consumers do not block collection or alias old snapshots", () => {
        var m = new CallbackPerformanceMonitor(16, 2); m.Record("old", "callback", 500, 1024);
        for (int i = 1; i <= 16; i++) m.Record("old", "callback", i, 1);
        var copy = m.CaptureWindow(); var old = CallbackPerformanceMonitor.Summarize(copy);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var consumer = Task.Run(() => {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new Exception("Owner did not release the held summary consumer.");
            return CallbackPerformanceMonitor.Summarize(copy);
        });
        PerformanceWindowCopy? latest = null;
        try
        {
            Check(entered.Wait(TimeSpan.FromSeconds(10)), "Summary consumer did not start.");
            m.Record("new", "callback", 1);
            for (int i = 0; i < 1024; i++)
            {
                m.Record("old", "callback", 1000 + i, 2); m.Record("flood", "excess", 3);
                latest = m.CaptureWindow();
            }
            Check(!consumer.IsCompleted && latest is not null && latest.Callbacks.Count == 2
                && latest.Callbacks.All(x => x.WindowSamples <= 16) && latest.DroppedSeriesSamples == 1024,
                "Holding a detached consumer blocked owner progress or expanded retained windows or series.");
        }
        finally { release.Set(); }
        var delayed = consumer.GetAwaiter().GetResult();
        var current = CallbackPerformanceMonitor.Summarize(latest!);
        var currentOld = current.Callbacks.Single(x => x.Owner == "old");
        Check(delayed.CapturedAtUtc == copy.CapturedAtUtc && delayed.Callbacks.SequenceEqual(old.Callbacks)
            && old.Callbacks.Single().P50Milliseconds == 8 && old.Callbacks.Single().MaximumMilliseconds == 500,
            "A slow consumer read later ring contents or replaced an old snapshot's capture time.");
        Check(currentOld.TotalSamples == 1041 && currentOld.WindowSamples == 16 && currentOld.P50Milliseconds == 2015
            && currentOld.P99Milliseconds == 2023 && currentOld.MaximumMilliseconds == 2023 && currentOld.TotalAllocatedBytes == 3088,
            "Collection stopped or lost counters while the previous copy waited for its consumer.");
        Check(copy.Callbacks.Single().Samples.SequenceEqual(Enumerable.Range(1, 16).Select(i => (double)i)),
            "Slow or repeated summaries modified the detached original samples.");
    }),
    ("A retained copy and its summary do not retain the owning collector", () => {
        var detached = CaptureWithoutCollector();
        for (int i = 0; i < 3 && detached.Collector.IsAlive; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        Check(!detached.Collector.IsAlive, "The immutable copy kept the live collector or a deferred query reachable.");
        var row = Task.Run(() => CallbackPerformanceMonitor.Summarize(detached.Copy)).GetAwaiter().GetResult().Callbacks.Single();
        Check(row.TotalSamples == 1 && row.P99Milliseconds == 7 && row.TotalAllocatedBytes == 64,
            "Detached summary needed the collector after collection.");
    }),
    ("Empty windows summarize on a worker while capture remains owner-thread bound", () => {
        var m = new CallbackPerformanceMonitor(16, 1); var empty = m.CaptureWindow();
        var snapshot = Task.Run(() => {
            Throws<InvalidOperationException>(() => m.CaptureWindow());
            Throws<InvalidOperationException>(() => m.GetSnapshot());
            Throws<InvalidOperationException>(() => m.Invoke("worker", "callback", () => throw new Exception("Worker callback ran.")));
            return CallbackPerformanceMonitor.Summarize(empty);
        }).GetAwaiter().GetResult();
        Check(snapshot.Callbacks.Count == 0 && snapshot.CapturedAtUtc == empty.CapturedAtUtc
            && snapshot.RetainedSamplesPerSeries == 16 && snapshot.MaximumSeries == 1 && snapshot.CallbackBudgetMilliseconds is null,
            "Empty summary invented samples or failed to preserve capture metadata.");
        m.Record("owner", "callback", 3); Check(m.CaptureWindow().Callbacks.Single().TotalSamples == 1 && empty.Callbacks.Count == 0,
            "Rejected worker access changed owner state or the empty capture.");
        Throws<ArgumentOutOfRangeException>(() => new CallbackPerformanceMonitor(15));
        Throws<ArgumentOutOfRangeException>(() => new CallbackPerformanceMonitor(16385));
        Throws<ArgumentOutOfRangeException>(() => new CallbackPerformanceMonitor(maximumSeries: 0));
        Throws<ArgumentOutOfRangeException>(() => new CallbackPerformanceMonitor(maximumSeries: 2049));
    }),
    ("Uncalibrated collector never stops callbacks", () => {
        var m = new CallbackPerformanceMonitor(); Check(!m.Record("p", "e", 10000), "Observation-only collector enforced a budget.");
        Check(m.GetSnapshot().CallbackBudgetMilliseconds is null && m.GetSnapshot().Callbacks.Single().OverBudget == 0, "A threshold was invented.");
    }),
    ("Consecutive budget breaches reset after an in-budget sample", () => {
        var m = new CallbackPerformanceMonitor(callbackBudgetMilliseconds: 5, consecutiveLimit: 3);
        Check(!m.Record("p", "e", 6) && !m.Record("p", "e", 6), "Stopped before the configured consecutive limit.");
        Check(!m.Record("p", "e", 2) && !m.Record("p", "e", 6) && !m.Record("p", "e", 6) && m.Record("p", "e", 6), "Budget reset or limit failed.");
        Check(!m.Record("host", "tick", 100, enforceBudget: false), "Host observation inherited plugin enforcement.");
    }),
    ("Actual invocation records real duration and thread allocation", () => {
        var m = new CallbackPerformanceMonitor(); byte[]? retained = null;
        m.Invoke("p", "real", () => { retained = new byte[8192]; var until = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 1000; while (Stopwatch.GetTimestamp() < until) Thread.SpinWait(16); });
        var s = m.GetSnapshot().Callbacks.Single(); Check(s.MaximumMilliseconds > 0 && s.TotalAllocatedBytes >= 8192 && retained!.Length == 8192, "Actual callback was not measured.");
    }),
    ("Callback error identity survives timing and is not replaced by a budget error", () => {
        var m = new CallbackPerformanceMonitor(callbackBudgetMilliseconds: .000001, consecutiveLimit: 1); var original = new ApplicationException("original");
        Exception? observed = null; try { m.Invoke("p", "throw", () => throw original); } catch (Exception ex) { observed = ex; }
        Check(ReferenceEquals(original, observed) && m.GetSnapshot().Callbacks.Single().TotalSamples == 1, "Timing swallowed or replaced callback failure.");
    }),
    ("Formal PluginHost stops a measured slow owner after completion and keeps other subscribers", () => {
        using var f = new Fixture(.01); var effects = 0; var others = 0;
        var slow = new Plugin("slow", c => c.Events.Subscribe<int>(_ => { effects++; var until = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 100; while (Stopwatch.GetTimestamp() < until) Thread.SpinWait(64); }));
        var fast = new Plugin("fast", c => c.Events.Subscribe<int>(_ => others++)); f.Start(slow); f.Start(fast);
        for (int i = 0; i < 4; i++) f.Events.Publish(i);
        Check(effects == 3 && others == 4, "Budget stop preempted a callback or interrupted an unrelated subscriber.");
        Check(f.Host.Statuses.Single(x => x.Id == "slow").State == PluginState.Faulted && f.Host.Statuses.Single(x => x.Id == "fast").State == PluginState.Enabled, "Budget fault ownership is wrong.");
    }),
    ("Exact hook callbacks use the same owner-aware measurement boundary", () => {
        using var f = new Fixture(null); var hooks = new HookDispatcher(f.Main, f.Log) { Performance = f.Monitor };
        var point = new HookPoint<int>("test.hook", new(1, 0)); hooks.Declare(point, "void()", "fixture", "typed fixture");
        f.Capabilities.RegisterScoped<IHookService>(HookCapabilityIds.Observe, c => hooks.ForPlugin(c));
        f.Start(new Plugin("hook-probe", c => c.GetService<IHookService>(HookCapabilityIds.Observe).Service!.Subscribe(point, HookPhase.After, _ => { })));
        hooks.Publish(point, HookPhase.After, 1);
        Check(f.Monitor.GetSnapshot().Callbacks.Any(x => x.Owner == "hook-probe" && x.Operation == "hook:test.hook:After"), "Hook callback bypassed monitor."); hooks.Dispose();
    }),
    ("Twenty lifecycle rounds release all active scope and owned resource roots", () => {
        using var f = new Fixture(null); var releases = 0;
        var p = new Plugin("cycles", c => { var child = ((IScopedPluginContext)c).CreateScope("child"); child.Own(new Cleanup(() => releases++)); c.Events.Subscribe<int>(_ => { }); });
        f.Host.Register(p);
        for (int i = 0; i < 20; i++) { Check(f.Host.Enable("cycles").Success, "Enable failed."); var live = f.Host.GetResourceCounts().Single(); Check(live.Scopes == 2 && live.RetainedOwnedResources >= 2, "Live ownership missing."); f.Host.Disable("cycles"); Check(f.Host.GetResourceCounts().Single().Scopes == 0, "Stopped scope retained active roots."); }
        Check(releases == 20, "Cleanup count drifted across cycles.");
    }),
    ("Wrong thread and invalid samples cannot poison measurements", () => {
        var m = new CallbackPerformanceMonitor(); Exception? cross = null;
        var thread = new Thread(() => { try { m.Record("p", "e", 1); } catch (Exception ex) { cross = ex; } }); thread.Start(); thread.Join();
        Check(cross is InvalidOperationException && m.GetSnapshot().Callbacks.Count == 0, "Worker measurement was accepted.");
        Throws<ArgumentOutOfRangeException>(() => m.Record("p", "e", double.NaN)); Throws<ArgumentOutOfRangeException>(() => m.Record("p", "e", 1, -1));
        Throws<ArgumentException>(() => m.Record("p", "bad\nkey", 1)); Throws<ArgumentOutOfRangeException>(() => new CallbackPerformanceMonitor(callbackBudgetMilliseconds: double.PositiveInfinity));
    })
};
foreach (var test in tests) { test.Run(); Console.WriteLine("PASS " + test.Name); }
Console.WriteLine($"{tests.Length}/{tests.Length} performance checks passed.");
static void Check(bool value, string message) { if (!value) throw new Exception(message); }
static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
[MethodImpl(MethodImplOptions.NoInlining)]
static (PerformanceWindowCopy Copy, WeakReference Collector) CaptureWithoutCollector()
{
    var monitor = new CallbackPerformanceMonitor(16, 1); monitor.Record("detached", "callback", 7, 64);
    return (monitor.CaptureWindow(), new WeakReference(monitor));
}
sealed class Cleanup : IDisposable { private Action? _action; public Cleanup(Action action) => _action = action; public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke(); }
sealed class CaptureLogger : ILogger { public void Log(LogLevel level, string message, Exception? exception = null) { } }
sealed class Plugin : IRizPlugin
{
    private readonly Action<IPluginContext> _enable; public PluginManifest Manifest { get; }
    public Plugin(string id, Action<IPluginContext> enable) { Manifest = new(id, new(1, 0), new(1, 0)); _enable = enable; }
    public void Enable(IPluginContext context) => _enable(context); public void Disable() { }
}
sealed class Fixture : IDisposable
{
    public CaptureLogger Log { get; } = new(); public MainThreadDispatcher Main { get; } = new();
    public CallbackPerformanceMonitor Monitor { get; }
    public CapabilityRegistry Capabilities { get; } = new(new("test", "test", "test", "fixture"));
    public EventHub Events { get; } public PluginHost Host { get; }
    public Fixture(double? budget) { Monitor = new(callbackBudgetMilliseconds: budget); Events = new(Log) { Performance = Monitor }; Host = new(Capabilities, Main, Events, Log); }
    public void Start(IRizPlugin plugin) { if (!Host.Register(plugin).Success || !Host.Enable(plugin.Manifest.Id).Success) throw new Exception("Plugin setup failed."); }
    public void Dispose() { Host.Dispose(); Main.Dispose(); }
}
