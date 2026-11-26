using System.Diagnostics;
using RizToolbox.Adapter;
using RizToolbox.Adapter.Modules;
using RizToolbox.Core;
using RizToolbox.Sdk;

internal static class AdapterSlowDiagnosticsChecks
{
    internal static readonly (string Name, Action Run)[] Cases = {
        ("Adapter timings ignore fast work and preserve the phase fault boundary", PhaseTiming),
        ("Slow activations identify modules and throttle retries independently of other phases", ActivationTiming)
    };

    private static void PhaseTiming()
    {
        var log = new RecordingLogger();
        var clock = new Clock();
        var diagnostics = new AdapterSlowDiagnostics(log, clock.Read);

        Check(RunFastPhase(log, clock, diagnostics), "The phase should complete.");
        var start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 128; i++) RunFastPhase(log, clock, diagnostics);
        Check(GC.GetAllocatedBytesForCurrentThread() == start, "Warmed-up fast measurements should allocate nothing.");
        Check(log.Entries.Count == 0, "Work below 100 ms should produce no warning.");

        var failure = new IOException("playback failure");
        int faultCallbacks = 0;
        log.FailWrites = true;
        var completed = AdapterTickPhase.Run(log, "playback-maintenance", clock, state =>
        {
            state.Advance(100);
            throw failure;
        }, (_, error) =>
        {
            Check(ReferenceEquals(error, failure), "The fault callback should receive the original exception.");
            faultCallbacks++;
            throw new InvalidOperationException("cleanup failure");
        }, diagnostics, hostTickSequence: 101);

        Check(!completed && faultCallbacks == 1, "The phase must keep its failure result and call cleanup once.");
        Check(log.Entries.Count == 3 && log.Entries[0].Message.Contains("fault handling failed") &&
              ReferenceEquals(log.Entries[1].Error, failure), "Existing cleanup and phase failure reports should both remain.");
        var slow = log.Entries[2];
        Check(slow.Level == LogLevel.Warning && slow.Message.StartsWith("Slow adapter phase 'playback-maintenance' hostTick=101 ") &&
              slow.Message.Contains("elapsedMs=100.0 allocatedBytes=") && slow.Message.Contains("outcome=faulted"),
            "The slow report should identify the phase, threshold, tick, allocation and outcome even if the sink fails.");
        Check(RunFastPhase(log, clock, diagnostics), "A later phase should still proceed.");
    }

    private static bool RunFastPhase(RecordingLogger log, Clock clock, AdapterSlowDiagnostics diagnostics)
        => AdapterTickPhase.Run(log, "playback-maintenance", clock, static state => state.Advance(99),
            slowDiagnostics: diagnostics, hostTickSequence: 100);

    private static void ActivationTiming()
    {
        var log = new RecordingLogger();
        var clock = new Clock();
        var diagnostics = new AdapterSlowDiagnostics(log, clock.Read);
        var capabilities = new CapabilityRegistry(new("test", "test", "test", new string('a', 64), true), log);
        using var manager = new ModuleManager(capabilities, new Resolver(), log, "test", new string('a', 64), diagnostics);

        var modules = Enumerable.Range(1, 8).Select(i => new Module($"probe.{i}", null, null, null)).ToArray();
        foreach (var module in modules)
        {
            module.OnProbe = () =>
            {
                clock.Advance(100);
                return ModuleProbe.Waiting("object pending");
            };
            manager.Add(module);
        }

        manager.Tick(0, "GameStart", 41);
        manager.Tick(.25, "GameStart", 42);
        manager.Tick(1, "GameStart", 43);
        var warnings = SlowWarnings(log);
        Check(warnings.Length == 4 && modules.All(module => module.Probes >= 1) && modules[0].Probes == 2,
            "The window should admit four named activations without affecting later probes or retries.");
        Check(warnings[0].Message.StartsWith("Slow adapter activation 'probe.1' hostTick=41 ") &&
              warnings[0].Message.Contains("attempt=1 state=Waiting"),
            "The first activation warning should identify the actual module and attempt outcome.");

        for (int i = 0; i < 6; i++)
            AdapterTickPhase.Run(log, "playback-maintenance", clock, static state => state.Advance(100),
                slowDiagnostics: diagnostics, hostTickSequence: 51 + i);
        warnings = SlowWarnings(log);
        Check(warnings.Length == 5 && warnings[4].Message.StartsWith("Slow adapter phase 'playback-maintenance' hostTick=51 "),
            "Repeated phase timings should yield one warning independently of activation traffic.");

        clock.Advance(10_000);
        modules[0].OnProbe = () =>
        {
            clock.Advance(100);
            return ModuleProbe.Available;
        };
        manager.Tick(3, "GameStart", 61);
        Check(capabilities.GetCapability("probe.1.cap").Available && SlowWarnings(log).Any(entry =>
              entry.Message.StartsWith("Slow adapter activation 'probe.1' hostTick=61 ") &&
              entry.Message.Contains("attempt=3 state=Active")),
            "A later window may observe the same module recovering with its actual attempt number.");
    }

    private static LogEntry[] SlowWarnings(RecordingLogger log)
        => log.Entries.Where(entry => entry.Message.StartsWith("Slow adapter ", StringComparison.Ordinal)).ToArray();

    private static void Check(bool value, string reason)
    {
        if (!value) throw new InvalidOperationException(reason);
    }

    private sealed class Clock
    {
        private long _ticks;
        internal long Read() => _ticks;
        internal void Advance(int milliseconds) => _ticks += milliseconds * Stopwatch.Frequency / 1000;
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Error);

    private sealed class RecordingLogger : ILogger
    {
        internal readonly List<LogEntry> Entries = new();
        internal bool FailWrites;

        public void Log(LogLevel level, string message, Exception? exception = null)
        {
            Entries.Add(new(level, message, exception));
            if (FailWrites) throw new IOException("controlled sink failure");
        }
    }
}
