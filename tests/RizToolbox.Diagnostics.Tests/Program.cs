using System.Text.Json;
using RizToolbox.Core.Diagnostics;
using RizToolbox.Sdk;

var suite = new (string Name, Action Body)[]
{
    ("structured identity and scoped correlation", Context),
    ("rotation retains bounded valid JSONL files", Rotation),
    ("repeated flood is suppressed and counted", RepeatFlood),
    ("unique flood has global rate and key limits", UniqueFlood),
    ("parallel writers preserve sequence and counters", ParallelWrites),
    ("oversized entries fit the configured file", Oversized),
    ("disk failure cannot escape or recurse", DiskFailure),
    ("paired text sink keeps legacy syntax and rotates", TextSink),
    ("existing session is never overwritten", ExistingSession),
    ("cross-session retention respects ownership", SessionRetention),
    ("active session cannot be pruned", ActiveSession),
    ("retention does not delete foreign files", ForeignRetention),
    ("structured disk failure preserves independent text sink", IndependentSink),
    ("filtering and disposal have defined counters", Filtering),
    ("invalid paths fail closed", BadPath),
    ("invalid session and limits are rejected", InvalidOptions),
    ("blocked storage preserves bounded immutable queue", BlockedQueue),
    ("flush honors its cutoff and reports timeout and storage failure", FlushCutoff),
    ("disposal timeout preserves consumer storage ownership", DisposalTimeout),
    ("pending explicit cutoff ignores later asynchronous requests", PendingFlushCutoff),
    ("failed flush keeps its outcome after a later success", FlushAttemptOutcome),
    ("distinct flush cutoffs complete in bounded order", BoundedFlushOrder)
};
var failed = 0;
foreach (var (name, body) in suite)
{
    try { body(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failed++; Console.Error.WriteLine($"FAIL {name}: {ex}"); }
}
Console.WriteLine($"Diagnostics: {suite.Length - failed}/{suite.Length} passed.");
return failed == 0 ? 0 : 1;

static void Context()
{
    using var area = new Area(Close);
    using var log = area.CreateLogger(new DiagnosticOptions(area.Path) { SessionId = "context" });
    log.ForContext(new("example.probe", "riz.runs", "scene/menu", "run-1", 5, "request-2"))
        .Log(LogLevel.Warning, "Waiting for controller.");
    log.Write(LogLevel.Error, "probe.failed", "No target.", fields: new Dictionary<string, string?> { ["reason"] = "missing" });
    Drain(log);
    var events = Events(area.Path, log.SessionId);
    Check(events.Count == 2, "two structured records");
    var first = events[0];
    Check(first.GetProperty("sessionId").GetString() == "context", "session id");
    var context = first.GetProperty("context");
    Check(context.GetProperty("pluginId").GetString() == "example.probe", "plugin context");
    Check(context.GetProperty("runId").GetString() == "run-1" && context.GetProperty("sceneGeneration").GetInt64() == 5, "run and scene");
    Check(context.GetProperty("correlationId").GetString() == "request-2", "correlation");
    var snapshot = log.GetSnapshot();
    Check(snapshot.Written == 2 && snapshot.Levels["Warning"] == 1 && snapshot.Levels["Error"] == 1, "level counters");
    Check(File.Exists(System.IO.Path.Combine(area.Path, "context", "snapshot.json")), "persisted snapshot");
}

static void Rotation()
{
    using var area = new Area(Close);
    using var log = area.CreateLogger(new DiagnosticOptions(area.Path)
    { SessionId = "rotation", MaxFileBytes = 1024, MaxFiles = 3, MaxEventsPerWindow = 1000 });
    for (var index = 0; index < 100; index++) log.Log(LogLevel.Info, $"event {index}: " + new string('x', 200));
    Drain(log);
    var files = Directory.GetFiles(System.IO.Path.Combine(area.Path, "rotation"), "events-*.jsonl");
    Check(files.Length == 3, "exact retained file count");
    Check(files.All(path => new FileInfo(path).Length <= 1024), "file bound");
    var events = Events(area.Path, "rotation");
    Check(events.Count > 0 && events[^1].GetProperty("sequence").GetInt64() == 100, "newest record retained");
    var snapshot = log.GetSnapshot();
    Check(snapshot.Written == 100 && snapshot.RetainedFiles == 3, "rotation counters");
    Check(snapshot.RetainedBytes == files.Sum(path => new FileInfo(path).Length), "retained bytes exact");
}

static void RepeatFlood()
{
    using var area = new Area(Close);
    using var log = area.CreateLogger(new DiagnosticOptions(area.Path)
    { SessionId = "repeat", RepeatBurst = 3, RepeatWindow = TimeSpan.FromDays(1), MaxEventsPerWindow = 1000 });
    for (var index = 0; index < 10000; index++) log.Log(LogLevel.Warning, "One missing controller.");
    Drain(log);
    var snapshot = log.GetSnapshot();
    Check(snapshot.Attempted == 10000 && snapshot.Written == 3 && snapshot.RepeatSuppressed == 9997, "exact repeated flood counters");
    Check(Events(area.Path, "repeat").Count == 3 && snapshot.TrackedRepeatKeys == 1, "bounded repetition");
    Close(log);
    using var scoped = area.CreateLogger(new DiagnosticOptions(area.Path)
    { SessionId = "repeat-context", RepeatBurst = 1, RepeatWindow = TimeSpan.FromDays(1) });
    scoped.Write(LogLevel.Info, "same", "same", new DiagnosticContext(PluginId: "same-plugin"),
        fields: new Dictionary<string, string?> { ["value"] = "first" });
    scoped.Write(LogLevel.Info, "same", "same", new DiagnosticContext(PluginId: "same-plugin"),
        fields: new Dictionary<string, string?> { ["value"] = "second" });
    Drain(scoped);
    Check(scoped.GetSnapshot().Written == 1 && scoped.GetSnapshot().RepeatSuppressed == 1 &&
        scoped.GetSnapshot().TrackedRepeatKeys == 1, "structural repeat identity uses copied context values and excludes fields");
}

static void UniqueFlood()
{
    using var area = new Area(Close);
    using var log = area.CreateLogger(new DiagnosticOptions(area.Path)
    { SessionId = "unique", MaxRepeatKeys = 8, MaxEventsPerWindow = 20, RateWindow = TimeSpan.FromDays(1) });
    for (var index = 0; index < 10000; index++) log.Log(LogLevel.Info, $"Object {index}");
    Drain(log);
    var snapshot = log.GetSnapshot();
    Check(snapshot.Written == 20 && snapshot.RateSuppressed == 9980, "global flood counters");
    Check(snapshot.TrackedRepeatKeys == 8, "bounded unique repeat keys");
}

static void ParallelWrites()
{
    using var area = new Area(Close);
    using var log = area.CreateLogger(new DiagnosticOptions(area.Path)
    { SessionId = "parallel", MaxEventsPerWindow = 10000, MaxFileBytes = 1_048_576, RateWindow = TimeSpan.FromDays(1) });
    // Keep all 1,000 concurrent writes and the final sequence assertion. The finite
    // drain deadline checks consumer progress per batch, not 1,000 synchronous disk flushes.
    for (var batch = 0; batch < 4; batch++)
    {
        var start = batch * 250;
        Parallel.For(start, start + 250, index => log.Log(LogLevel.Info, $"Parallel {index}"));
        Drain(log);
    }
    var events = Events(area.Path, "parallel");
    Check(events.Count == 1000 && log.GetSnapshot().Attempted == 1000, "all parallel writes counted");
    Check(events.Select(record => record.GetProperty("sequence").GetInt64()).SequenceEqual(Enumerable.Range(1, 1000).Select(value => (long)value)), "strict sequence");
}

static void Oversized()
{
    using var area = new Area(Close);
    using var log = area.CreateLogger(new DiagnosticOptions(area.Path)
    { SessionId = "oversize", MaxFileBytes = 1024 });
    log.Write(LogLevel.Error, "oversize", new string('字', 5000),
        new(new string('p', 1000), new string('c', 1000)), new Exception(new string('e', 10000)),
        Enumerable.Range(0, 100).ToDictionary(index => "key" + index, _ => (string?)new string('z', 1000)));
    Drain(log);
    Check(log.GetSnapshot().Written == 1 && log.GetSnapshot().Oversized == 1, "oversize fallback counted");
    Check(Directory.GetFiles(System.IO.Path.Combine(area.Path, "oversize"), "events-*.jsonl").All(path => new FileInfo(path).Length <= 1024), "oversize file bounded");
    Check(Events(area.Path, "oversize").Single().GetProperty("message").GetString()!.Contains("exceeded"), "explicit oversize marker");
}

static void DiskFailure()
{
    using var area = new Area(Close);
    var blocked = System.IO.Path.Combine(area.Path, "blocked");
    File.WriteAllText(blocked, "A file cannot be a log directory.");
    using var log = area.CreateLogger(new DiagnosticOptions(blocked)
    { SessionId = "disk", RepeatBurst = 10000, MaxEventsPerWindow = 10000, DiskRetryInterval = TimeSpan.FromDays(1) });
    for (var index = 0; index < 1000; index++) log.Log(LogLevel.Error, "Disk is unavailable.");
    Drain(log, snapshotWritten: false);
    var snapshot = log.GetSnapshot();
    Check(snapshot.Attempted == 1000 && snapshot.Written == 0 && snapshot.DiskFailures == 1, "disk failure is throttled");
    Check(snapshot.DiskSkipped == 1000 && snapshot.LastError is not null, "skipped writes observable");
    Check(File.ReadAllText(blocked) == "A file cannot be a log directory.", "unrelated file preserved");
}

static void TextSink()
{
    using var area = new Area(Close);
    var text = System.IO.Path.Combine(area.Path, "toolbox.log");
    using var log = area.CreateLogger(new DiagnosticOptions(System.IO.Path.Combine(area.Path, "structured"))
    { SessionId = "text", TextLogPath = text, MaxFileBytes = 1024, MaxFiles = 2, MaxEventsPerWindow = 1000 });
    for (var index = 0; index < 100; index++) log.Log(LogLevel.Info, $"Legacy {index}: " + new string('x', 100));
    Drain(log);
    var files = Directory.GetFiles(area.Path, "toolbox.log*");
    Check(files.Length == 2 && files.All(path => new FileInfo(path).Length <= 1024), "text rotation bound");
    Check(File.ReadAllText(text).Contains("[Info] Legacy 99:"), "legacy text syntax");
    Check(log.GetSnapshot().TextWritten == 100, "text write counter");
}

static void ExistingSession()
{
    using var area = new Area(Close);
    using (var first = area.CreateLogger(new DiagnosticOptions(area.Path) { SessionId = "same" }))
    {
        first.Log(LogLevel.Info, "Original event.");
        Drain(first);
        Close(first);
    }
    var before = Events(area.Path, "same");
    using var second = area.CreateLogger(new DiagnosticOptions(area.Path) { SessionId = "same" });
    second.Log(LogLevel.Error, "Must not append.");
    Drain(second, snapshotWritten: false);
    Check(second.GetSnapshot().DiskFailures == 1 && second.GetSnapshot().Written == 0, "collision diagnostic");
    Check(before.Count == 1 && Events(area.Path, "same").Single().GetProperty("message").GetString() == "Original event.", "existing session unchanged");
}

static void Filtering()
{
    using var area = new Area(Close);
    var log = area.CreateLogger(new DiagnosticOptions(area.Path) { SessionId = "filter", MinimumLevel = LogLevel.Warning });
    var exception = new FormattingProbeException();
    var filteredFields = new PrefixCollisionFields();
    log.Write(LogLevel.Debug, "filtered", "debug", exception: exception, fields: filteredFields);
    log.Log(LogLevel.Info, "info"); log.Log(LogLevel.Warning, "warning");
    Drain(log);
    log.Dispose(); log.Dispose(); log.Log(LogLevel.Error, "after disposal");
    Check(log.Completion.Wait(TimeSpan.FromSeconds(5)), "filtered logger consumer closes");
    var snapshot = log.GetSnapshot();
    Check(snapshot.Attempted == 3 && snapshot.Filtered == 2 && snapshot.Written == 1, "filter and idempotent disposal");
    Check(exception.Formatted == 0 && filteredFields.Inspected == 0,
        "filtered writes do not format caller exceptions or enumerate fields");
}

static void ActiveSession()
{
    using var area = new Area(Close);
    using var first = area.CreateLogger(new DiagnosticOptions(area.Path) { SessionId = "active", MaxSessions = 1 });
    first.Log(LogLevel.Info, "active event");
    Drain(first);
    using var second = area.CreateLogger(new DiagnosticOptions(area.Path) { SessionId = "second", MaxSessions = 1 });
    second.Log(LogLevel.Info, "must not exceed budget");
    Drain(second, snapshotWritten: false);
    Check(second.GetSnapshot().Written == 0 && second.GetSnapshot().DiskFailures == 1, "active session budget fails safely");
    Check(Events(area.Path, "active").Count == 1 && !Directory.Exists(System.IO.Path.Combine(area.Path, "second")), "active session retained");
}

static void ForeignRetention()
{
    using var area = new Area(Close);
    using (var log = area.CreateLogger(new DiagnosticOptions(area.Path) { SessionId = "old", MaxSessions = 1 }))
    {
        log.Log(LogLevel.Info, "original event");
        Drain(log);
        Close(log);
    }
    var foreign = System.IO.Path.Combine(area.Path, "old", "save.json");
    File.WriteAllText(foreign, "never delete");
    using var second = area.CreateLogger(new DiagnosticOptions(area.Path) { SessionId = "new", MaxSessions = 1 });
    second.Log(LogLevel.Info, "retention refused");
    Drain(second, snapshotWritten: false);
    Check(second.GetSnapshot().Written == 0 && second.GetSnapshot().DiskFailures == 1, "foreign session blocks retention");
    Check(File.ReadAllText(foreign) == "never delete" && Events(area.Path, "old").Count == 1, "foreign and diagnostic files untouched");
}

static void IndependentSink()
{
    using var area = new Area(Close);
    var blocked = System.IO.Path.Combine(area.Path, "blocked");
    File.WriteAllText(blocked, "keep");
    var text = System.IO.Path.Combine(area.Path, "toolbox.log");
    using var log = area.CreateLogger(new DiagnosticOptions(blocked) { SessionId = "text-fallback", TextLogPath = text });
    log.Log(LogLevel.Error, "still observable");
    Drain(log, snapshotWritten: false);
    Check(log.GetSnapshot().Written == 0 && log.GetSnapshot().TextWritten == 1, "text fallback independent");
    Check(File.ReadAllText(text).Contains("[Error] still observable"), "text fallback content");
}

static void SessionRetention()
{
    using var area = new Area(Close);
    var unrelated = System.IO.Path.Combine(area.Path, "unrelated");
    Directory.CreateDirectory(unrelated);
    File.WriteAllText(System.IO.Path.Combine(unrelated, "private.txt"), "keep");
    for (var index = 0; index < 5; index++)
    {
        using var log = area.CreateLogger(new DiagnosticOptions(area.Path) { SessionId = "retained-" + index, MaxSessions = 2 });
        log.Log(LogLevel.Info, "Session " + index);
        Drain(log);
        Check(log.GetSnapshot().Written == 1, "retained session writable");
        Close(log);
    }
    Check(Directory.GetDirectories(area.Path, "retained-*").Length == 2, "bounded retained sessions");
    Check(Directory.Exists(System.IO.Path.Combine(area.Path, "retained-4")), "latest session retained");
    Check(File.ReadAllText(System.IO.Path.Combine(unrelated, "private.txt")) == "keep", "unowned files preserved");
}

static void BadPath()
{
    using var area = new Area(Close);
    using var log = area.CreateLogger(new DiagnosticOptions("invalid\0path") { SessionId = "bad" });
    log.Log(LogLevel.Error, "Cannot write.");
    Drain(log, snapshotWritten: false);
    Check(log.GetSnapshot().DiskFailures == 1 && log.GetSnapshot().Written == 0, "invalid path is contained");
}

static void InvalidOptions()
{
    using var area = new Area(Close);
    Throws<ArgumentException>(() => area.CreateLogger(new DiagnosticOptions(area.Path) { SessionId = "../escape" }));
    Throws<ArgumentOutOfRangeException>(() => area.CreateLogger(new DiagnosticOptions(area.Path) { MaxFiles = 0 }));
    Throws<ArgumentOutOfRangeException>(() => area.CreateLogger(new DiagnosticOptions(area.Path) { MaxQueuedEvents = 0 }));
    Throws<ArgumentOutOfRangeException>(() => area.CreateLogger(new DiagnosticOptions(area.Path) { MaxQueuedBytes = 1023 }));
    Throws<ArgumentOutOfRangeException>(() => area.CreateLogger(new DiagnosticOptions(area.Path) { DisposeTimeout = Timeout.InfiniteTimeSpan }));
}

static void BlockedQueue()
{
    using var area = new Area(Close);
    Exercise("event-capacity", maxEvents: 3, maxBytes: 8192, expectedAccepted: 3);
    Exercise("byte-capacity", maxEvents: 8, maxBytes: 1800, expectedAccepted: 2);

    void Exercise(string session, int maxEvents, long maxBytes, int expectedAccepted)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var log = WithStorageBoundary(area, new DiagnosticOptions(area.Path)
        {
            SessionId = session, MaxQueuedEvents = maxEvents, MaxQueuedBytes = maxBytes,
            MaxFields = 2, MaxEventsPerWindow = 1000, RateWindow = TimeSpan.FromDays(1)
        }, (operation, sequence) =>
        {
            if (operation == "structured-write" && sequence == 1)
            { entered.Set(); release.Wait(); }
        });
        try
        {
            log.Log(LogLevel.Info, "First event.");
            Check(entered.Wait(TimeSpan.FromSeconds(5)), "consumer reached actual write boundary");
            var beforeLaterWrites = log.GetSnapshot();
            // This event stays queued behind the blocked write, so mutation precedes consumer encoding.
            // Truncating both keys to the same value must still inspect at most MaxFields inputs.
            var fields = new PrefixCollisionFields();
            var context = new DiagnosticContext(PluginId: "owned-plugin", RunId: "owned-run", SceneGeneration: 7);
            var producing = Task.Run(() =>
            {
                log.Write(LogLevel.Info, "copied", "Copied event.", context, fields: fields);
                Check(fields.Inspected == 2, "field input inspection stays bounded despite key collision");
                fields.Value = "mutated";
                for (var index = 0; index < 5; index++) log.Log(LogLevel.Info, "Later event " + index);
                return log.GetSnapshot();
            });
            Check(producing.Wait(TimeSpan.FromSeconds(5)), "write and snapshot return during blocked storage");
            var blocked = producing.Result;
            Check(blocked.Attempted == 7 && blocked.Enqueued == expectedAccepted && blocked.Processed == 0 &&
                blocked.Written == 0 && blocked.QueueDropped == 7 - expectedAccepted, "exact overflow without fabricated writes");
            Check(blocked.PendingEvents == expectedAccepted && blocked.PendingBytes <= maxBytes &&
                blocked.PeakPendingEvents == expectedAccepted && blocked.PeakPendingBytes <= maxBytes,
                "event and byte capacity include in-flight event");
            Check(blocked.Enqueued == blocked.Processed + blocked.PendingEvents + blocked.QueueAbandoned,
                "pending conservation while disk blocked");
            Throws<NotSupportedException>(() => ((IDictionary<string, long>)blocked.Levels)["Info"] = 0);
            release.Set();
            var result = log.Flush(TimeSpan.FromSeconds(5));
            Check(result.Completed && result.SnapshotWritten, "accepted bounded queue drains");
            var drained = log.GetSnapshot();
            Check(drained.Written == expectedAccepted && drained.Processed == expectedAccepted &&
                drained.PendingEvents == 0 && drained.PendingBytes == 0 && drained.QueueAbandoned == 0,
                "exact completed queue counters");
            var records = Events(area.Path, session);
            Check(records.Select(record => record.GetProperty("sequence").GetInt64())
                .SequenceEqual(Enumerable.Range(1, expectedAccepted).Select(index => (long)index)), "accepted order retained");
            Check(records[1].GetProperty("fields").GetProperty(new string('k', 64)).GetString() == "original" &&
                records[1].GetProperty("context").GetProperty("runId").GetString() == "owned-run",
                "consumer sees copied field and context values");
            Check(beforeLaterWrites.Levels["Info"] == 1 && blocked.Levels["Info"] == 7,
                "earlier snapshot remains an independent read-only copy");
        }
        finally { release.Set(); Close(log); }
    }
}

static void FlushCutoff()
{
    using var area = new Area(Close);
    using var firstWrite = new ManualResetEventSlim();
    using var releaseFirst = new ManualResetEventSlim();
    using var laterWrite = new ManualResetEventSlim();
    using var releaseLater = new ManualResetEventSlim();
    using var firstSnapshot = new ManualResetEventSlim();
    using var releaseSnapshot = new ManualResetEventSlim();
    var snapshotCalls = 0;
    using var log = WithStorageBoundary(area, new DiagnosticOptions(area.Path)
    {
        SessionId = "cutoff", MaxEventsPerWindow = 1000, RateWindow = TimeSpan.FromDays(1)
    }, (operation, sequence) =>
    {
        if (operation == "structured-write" && sequence == 1) { firstWrite.Set(); releaseFirst.Wait(); }
        if (operation == "structured-write" && sequence == 2) { laterWrite.Set(); releaseLater.Wait(); }
        if (operation == "snapshot-write" && Interlocked.Increment(ref snapshotCalls) == 1)
        { firstSnapshot.Set(); releaseSnapshot.Wait(); }
    });
    try
    {
        log.Log(LogLevel.Info, "Captured event.");
        Check(firstWrite.Wait(TimeSpan.FromSeconds(5)), "first event storage blocked");
        var laterProducer = Task.Run(() =>
        {
            Check(firstSnapshot.Wait(TimeSpan.FromSeconds(5)), "captured cutoff flush reached before later event");
            log.Log(LogLevel.Info, "Later event.");
            for (var index = 0; index < 5; index++) log.RequestFlush();
            Check(log.GetSnapshot().Processed == 1 && log.GetSnapshot().PendingEvents == 1,
                "later event still pending at first cutoff");
            releaseSnapshot.Set();
        });
        releaseFirst.Set();
        var firstResult = log.Flush(TimeSpan.FromSeconds(5));
        Check(firstResult.Completed && firstResult.SnapshotWritten && firstResult.ThroughSequence == 1,
            "explicit flush returns its captured cutoff despite later writes and flush requests");
        Check(laterProducer.Wait(TimeSpan.FromSeconds(5)), "later writes do not block at active flush storage");
        Check(laterWrite.Wait(TimeSpan.FromSeconds(5)), "later storage boundary reached");
        var throughFirst = log.GetSnapshot();
        Check(throughFirst.LastSuccessfulFlushSequence == 1 && throughFirst.Written == 1 && throughFirst.PendingEvents == 1,
            "first flush completes while later disk work remains blocked");
        var timedOut = log.Flush(TimeSpan.Zero);
        Check(!timedOut.Completed && !timedOut.SnapshotWritten && timedOut.TimedOut && timedOut.ThroughSequence == 2,
            "flush timeout keeps captured cutoff and reports failure to wait");
        releaseLater.Set();
        Drain(log);
        Check(log.GetSnapshot().FlushTimeouts == 1, "timeout remains observable after recovery");
    }
    finally { releaseFirst.Set(); releaseSnapshot.Set(); releaseLater.Set(); Close(log); }

    using var failedSnapshot = WithStorageBoundary(area, new DiagnosticOptions(area.Path)
    { SessionId = "snapshot-failure", DiskRetryInterval = TimeSpan.FromDays(1) }, (operation, _) =>
    { if (operation == "snapshot-write") throw new IOException("Snapshot write failed for the test."); });
    failedSnapshot.Log(LogLevel.Info, "Event succeeds before snapshot fails.");
    var failed = failedSnapshot.Flush(TimeSpan.FromSeconds(5));
    var failureState = failedSnapshot.GetSnapshot();
    Check(failed.Completed && !failed.SnapshotWritten && !failed.TimedOut && failed.ThroughSequence == 1,
        "completed failed flush differs from timeout");
    Check(failureState.Written == 1 && failureState.Processed == 1 && failureState.DiskFailures == 1 &&
        failureState.LastSuccessfulFlushSequence == 0, "failed snapshot does not advance success watermark");
}

static void DisposalTimeout()
{
    using var area = new Area(Close);
    using var entered = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    using var log = WithStorageBoundary(area, new DiagnosticOptions(area.Path)
    {
        SessionId = "busy", MaxSessions = 1, DisposeTimeout = TimeSpan.FromMilliseconds(25)
    }, (operation, sequence) =>
    {
        if (operation == "structured-write" && sequence == 1) { entered.Set(); release.Wait(); }
    });
    try
    {
        log.Log(LogLevel.Info, "Busy storage.");
        Check(entered.Wait(TimeSpan.FromSeconds(5)), "busy stream and session are owned by consumer");
        var disposing = Task.Run(log.Dispose);
        Check(disposing.Wait(TimeSpan.FromSeconds(5)), "dispose returns with finite deadline");
        log.Log(LogLevel.Error, "ignored after disposal");
        var blocked = log.GetSnapshot();
        Check(!blocked.AcceptingWrites && !blocked.ConsumerCompleted && !log.Completion.IsCompleted &&
            blocked.Attempted == 1 && blocked.PendingEvents == 1 && blocked.Processed == 0 && blocked.FlushTimeouts == 1,
            "timeout closes admission and keeps honest pending state");
        var sessionLock = System.IO.Path.Combine(area.Path, "busy", "session.lock");
        Throws<IOException>(() =>
        { using var forbidden = new FileStream(sessionLock, FileMode.Open, FileAccess.ReadWrite, FileShare.None); });
        using (var second = area.CreateLogger(new DiagnosticOptions(area.Path)
        { SessionId = "blocked-retention", MaxSessions = 1 }))
        {
            second.Log(LogLevel.Info, "active consumer still owns session");
            Drain(second, snapshotWritten: false);
            Check(second.GetSnapshot().DiskFailures == 1 && second.GetSnapshot().Written == 0 &&
                Directory.Exists(System.IO.Path.Combine(area.Path, "busy")), "retention cannot prune timed-out busy consumer");
            Close(second);
        }
        release.Set();
        Check(log.Completion.Wait(TimeSpan.FromSeconds(5)), "busy consumer eventually releases handles");
        var closed = log.GetSnapshot();
        Check(closed.ConsumerCompleted && closed.Processed == 1 && closed.Written == 1 && closed.PendingEvents == 0 &&
            closed.QueueAbandoned == 0, "timed-out dispose does not abandon or steal storage");
        using var third = area.CreateLogger(new DiagnosticOptions(area.Path)
        { SessionId = "after-drain", MaxSessions = 1 });
        third.Log(LogLevel.Info, "closed session can be retained away");
        Drain(third);
        Check(!Directory.Exists(System.IO.Path.Combine(area.Path, "busy")), "retention works after consumer completion");
    }
    finally { release.Set(); Close(log); }
}

static void PendingFlushCutoff()
{
    using var area = new Area(Close);
    using var firstSnapshot = new ManualResetEventSlim();
    using var releaseFirst = new ManualResetEventSlim();
    using var thirdWrite = new ManualResetEventSlim();
    using var releaseThird = new ManualResetEventSlim();
    using var log = WithStorageBoundary(area, new DiagnosticOptions(area.Path)
    {
        SessionId = "pending-cutoff", MaxQueuedEvents = 4
    }, (operation, sequence) =>
    {
        if (operation == "snapshot-write" && sequence == 1) { firstSnapshot.Set(); releaseFirst.Wait(); }
        if (operation == "structured-write" && sequence == 3) { thirdWrite.Set(); releaseThird.Wait(); }
    });
    try
    {
        log.Log(LogLevel.Info, "First event.");
        log.RequestFlush();
        Check(firstSnapshot.Wait(TimeSpan.FromSeconds(5)), "earlier active snapshot is blocked");
        log.Log(LogLevel.Info, "Second event.");
        var second = BeginFlush(log);
        WaitForFlushAttempts(log, 2, "explicit second cutoff is registered before later production");
        log.Log(LogLevel.Info, "Third event.");
        for (var index = 0; index < 1000; index++) log.RequestFlush();
        Check(log.GetSnapshot().PendingFlushAttempts == 2, "asynchronous requests remain one merged target");
        releaseFirst.Set();
        Check(thirdWrite.Wait(TimeSpan.FromSeconds(5)), "later event storage is blocked");
        Check(second.Wait(TimeSpan.FromSeconds(5)), "second explicit flush returns before third event storage completes");
        var result = second.Result;
        Check(result.Completed && result.SnapshotWritten && !result.TimedOut && result.ThroughSequence == 2,
            "pending explicit flush retains its own cutoff and attempt outcome");
        var blocked = log.GetSnapshot();
        Check(blocked.Written == 2 && blocked.Processed == 2 && blocked.PendingEvents == 1 &&
            blocked.LastSuccessfulFlushSequence == 2, "second snapshot completes while later event remains pending");
        releaseThird.Set();
        Drain(log);
    }
    finally { releaseFirst.Set(); releaseThird.Set(); Close(log); }
}

static void FlushAttemptOutcome()
{
    using var area = new Area(Close);
    using var firstSnapshot = new ManualResetEventSlim();
    using var releaseFailure = new ManualResetEventSlim();
    using var thirdBoundary = new ManualResetEventSlim();
    using var releaseThird = new ManualResetEventSlim();
    object? consumerGate = null;
    StructuredDiagnosticLogger? callbackLog = null;
    var gateHeld = false;
    using var log = WithStorageBoundary(area, new DiagnosticOptions(area.Path)
    {
        SessionId = "attempt-outcome", DiskRetryInterval = TimeSpan.Zero
    }, (operation, sequence) =>
    {
        if (operation == "snapshot-write" && sequence == 1)
        {
            firstSnapshot.Set();
            releaseFailure.Wait();
            // Both callers have already registered. Keep this consumer's reentrant gate
            // through the next publication so the first caller observes a later success.
            Monitor.Enter(consumerGate!);
            gateHeld = true;
            throw new IOException("First snapshot fails before the second succeeds.");
        }
        if (operation == "snapshot-write" && sequence == 2) callbackLog!.RequestFlush();
        if (sequence == 3 && gateHeld)
        {
            // The third event is queued before retaining the gate, so there is no await
            // or wait for another gate owner between acquisition and this release.
            // A third snapshot request also guarantees this boundary if its write fails.
            Monitor.Exit(consumerGate!);
            gateHeld = false;
            thirdBoundary.Set();
            releaseThird.Wait();
        }
    });
    callbackLog = log;
    var gateField = typeof(StructuredDiagnosticLogger).GetField("_gate",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    Check(gateField is not null, "consumer scheduling gate is available to the storage seam");
    consumerGate = gateField!.GetValue(log)!;
    try
    {
        log.Log(LogLevel.Info, "First event.");
        var first = BeginFlush(log);
        Check(firstSnapshot.Wait(TimeSpan.FromSeconds(5)), "first explicit attempt reached snapshot storage");
        log.Log(LogLevel.Info, "Second event.");
        var second = BeginFlush(log);
        WaitForFlushAttempts(log, 2, "second explicit attempt is registered before the first fails");
        log.Log(LogLevel.Info, "Third event.");
        releaseFailure.Set();
        Check(thirdBoundary.Wait(TimeSpan.FromSeconds(5)), "second attempt publishes before earlier waiter can resume");
        Check(first.Wait(TimeSpan.FromSeconds(5)) && second.Wait(TimeSpan.FromSeconds(5)), "both explicit attempt results return");
        Check(first.Result.Completed && !first.Result.SnapshotWritten && !first.Result.TimedOut && first.Result.ThroughSequence == 1,
            "later successful attempt cannot mask the first snapshot failure");
        Check(second.Result.Completed && second.Result.SnapshotWritten && second.Result.ThroughSequence == 2,
            "second caller observes its successful atomic snapshot attempt");
        var state = log.GetSnapshot();
        Check(state.DiskFailures == 1 && state.LastSuccessfulFlushSequence == 2 && state.Processed == 2 &&
            state.PendingEvents == 1, "success coverage advances independently of failed attempt result");
        releaseThird.Set();
        Drain(log);
    }
    finally { releaseFailure.Set(); releaseThird.Set(); Close(log); }
}

static void BoundedFlushOrder()
{
    using var area = new Area(Close);
    using var initialSnapshot = new ManualResetEventSlim();
    using var releaseInitial = new ManualResetEventSlim();
    using var secondWrite = new ManualResetEventSlim();
    using var releaseSecond = new ManualResetEventSlim();
    using var thirdWrite = new ManualResetEventSlim();
    using var releaseThird = new ManualResetEventSlim();
    const int capacity = 3;
    var cutoffs = new List<long>();
    using var log = WithStorageBoundary(area, new DiagnosticOptions(area.Path)
    {
        SessionId = "flush-order", MaxQueuedEvents = capacity, DisposeTimeout = TimeSpan.FromMilliseconds(25)
    }, (operation, sequence) =>
    {
        if (operation == "snapshot-write")
        {
            cutoffs.Add(sequence);
            if (sequence == 0) { initialSnapshot.Set(); releaseInitial.Wait(); }
        }
        if (operation == "structured-write" && sequence == 2) { secondWrite.Set(); releaseSecond.Wait(); }
        if (operation == "structured-write" && sequence == 3) { thirdWrite.Set(); releaseThird.Wait(); }
    });
    try
    {
        log.RequestFlush();
        Check(initialSnapshot.Wait(TimeSpan.FromSeconds(5)), "zero-cutoff active snapshot is blocked");
        log.Log(LogLevel.Info, "First event.");
        var first = BeginFlush(log);
        WaitForFlushAttempts(log, 2, "first pending explicit cutoff is registered");
        log.Log(LogLevel.Info, "Second event.");
        var second = BeginFlush(log);
        WaitForFlushAttempts(log, 3, "second pending explicit cutoff is registered");
        log.Log(LogLevel.Info, "Third event.");
        var third = BeginFlush(log);
        WaitForFlushAttempts(log, 4, "third pending explicit cutoff is registered");
        for (var index = 0; index < 32; index++)
        {
            var duplicate = log.Flush(TimeSpan.Zero);
            Check(duplicate.TimedOut && duplicate.ThroughSequence == 3, "duplicate callers capture the same pending cutoff");
            log.RequestFlush();
        }
        Check(log.GetSnapshot().PendingFlushAttempts == capacity + 1,
            "duplicate explicit callers and asynchronous traffic add no concrete attempts");
        var disposing = Task.Run(log.Dispose);
        Check(disposing.Wait(TimeSpan.FromSeconds(5)), "disposal returns while control ring and storage are busy");
        var pendingFinal = log.GetSnapshot();
        Check(pendingFinal.PendingFlushAttempts == capacity + 2 && pendingFinal.PeakPendingFlushAttempts == capacity + 2 &&
            !pendingFinal.AcceptingWrites && !pendingFinal.ConsumerCompleted, "single final disposal attempt fits the hard control capacity");
        for (var index = 0; index < 32; index++)
        {
            log.RequestFlush();
            Check(log.Flush(TimeSpan.Zero).TimedOut, "closed admission callers share the pending final attempt");
        }
        Check(log.GetSnapshot().PendingFlushAttempts == capacity + 2, "closed admission cannot grow flush control state");
        releaseInitial.Set();
        Check(secondWrite.Wait(TimeSpan.FromSeconds(5)), "second event is blocked after first cutoff flush");
        Check(first.Wait(TimeSpan.FromSeconds(5)) && first.Result.Completed && first.Result.SnapshotWritten &&
            first.Result.ThroughSequence == 1 && !second.IsCompleted && !third.IsCompleted,
            "first explicit cutoff completes independently of later pending cutoffs");
        Check(log.GetSnapshot().LastSuccessfulFlushSequence == 1, "first success watermark is exact");
        releaseSecond.Set();
        Check(thirdWrite.Wait(TimeSpan.FromSeconds(5)), "third event is blocked after second cutoff flush");
        Check(second.Wait(TimeSpan.FromSeconds(5)) && second.Result.Completed && second.Result.SnapshotWritten &&
            second.Result.ThroughSequence == 2 && !third.IsCompleted, "second explicit cutoff completes in order");
        Check(log.GetSnapshot().LastSuccessfulFlushSequence == 2, "second success watermark is exact");
        releaseThird.Set();
        Check(log.Completion.Wait(TimeSpan.FromSeconds(5)), "ordered final attempts drain and release consumer resources");
        Check(third.Wait(TimeSpan.FromSeconds(5)) && third.Result.Completed && third.Result.SnapshotWritten &&
            third.Result.ThroughSequence == 3, "third explicit cutoff has its own successful result");
        Check(cutoffs.SequenceEqual(new long[] { 0, 1, 2, 3, 3 }), "snapshot attempts preserve every explicit cutoff and one final retry");
        var closed = log.GetSnapshot();
        Check(closed.Written == capacity && closed.Processed == capacity && closed.PendingEvents == 0 &&
            closed.PendingFlushAttempts == 0 && closed.PeakPendingFlushAttempts == capacity + 2 &&
            closed.QueueDropped == 0 && closed.QueueAbandoned == 0, "all queue and control state drains within the hard bounds");
        var final = log.Flush(TimeSpan.Zero);
        Check(final.Completed && final.SnapshotWritten && !final.TimedOut && final.ThroughSequence == 3,
            "closed consumer reports its actual final attempt result");
    }
    finally { releaseInitial.Set(); releaseSecond.Set(); releaseThird.Set(); Close(log); }
}

static void WaitForFlushAttempts(StructuredDiagnosticLogger log, int expected, string message) =>
    Check(SpinWait.SpinUntil(() => log.GetSnapshot().PendingFlushAttempts == expected, TimeSpan.FromSeconds(5)), message);

static Task<DiagnosticFlushResult> BeginFlush(StructuredDiagnosticLogger log) =>
    Task.Factory.StartNew(() => log.Flush(TimeSpan.FromSeconds(5)), CancellationToken.None,
        TaskCreationOptions.LongRunning, TaskScheduler.Default);

static StructuredDiagnosticLogger WithStorageBoundary(Area area, DiagnosticOptions options, Action<string, long> beforeStorage)
{
    var constructor = typeof(StructuredDiagnosticLogger).GetConstructor(
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null,
        new[] { typeof(DiagnosticOptions), typeof(Action<string, long>) }, null);
    Check(constructor is not null, "internal consumer boundary seam available");
    return area.Own((StructuredDiagnosticLogger)constructor!.Invoke(new object[] { options, beforeStorage }));
}

static DiagnosticFlushResult Drain(StructuredDiagnosticLogger log, bool snapshotWritten = true)
{
    var result = log.Flush(TimeSpan.FromSeconds(5));
    var snapshot = log.GetSnapshot();
    Check(result.Completed && !result.TimedOut,
        $"consumer completed captured flush cutoff (through={result.ThroughSequence}, timedOut={result.TimedOut}, " +
        $"enqueued={snapshot.Enqueued}, processed={snapshot.Processed}, written={snapshot.Written}, pending={snapshot.PendingEvents}, " +
        $"accepting={snapshot.AcceptingWrites}, completed={snapshot.ConsumerCompleted}, internalFailures={snapshot.InternalFailures})");
    Check(result.SnapshotWritten == snapshotWritten, "flush reports actual atomic snapshot outcome");
    Check(snapshot.Enqueued == snapshot.Processed && snapshot.PendingEvents == 0 && snapshot.PendingBytes == 0 &&
        snapshot.QueueDropped == 0 && snapshot.QueueAbandoned == 0, "accepted events drained without loss");
    return result;
}

static void Close(StructuredDiagnosticLogger log)
{
    log.Dispose();
    Check(log.Completion.Wait(TimeSpan.FromSeconds(5)), "consumer releases storage handles");
    var snapshot = log.GetSnapshot();
    Check(snapshot.ConsumerCompleted && !snapshot.AcceptingWrites, "closed consumer state");
}

static List<JsonElement> Events(string root, string session)
{
    var list = new List<JsonElement>();
    foreach (var path in Directory.GetFiles(System.IO.Path.Combine(root, session), "events-*.jsonl").Order(StringComparer.Ordinal))
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        { using var json = JsonDocument.Parse(line); list.Add(json.RootElement.Clone()); }
    }
    return list;
}
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static void Throws<T>(Action action) where T : Exception
{ try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }

sealed class Area : IDisposable
{
    private readonly Action<StructuredDiagnosticLogger> _close;
    private readonly List<StructuredDiagnosticLogger> _loggers = new();
    public Area(Action<StructuredDiagnosticLogger> close)
    {
        _close = close;
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "riz-diagnostics-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }
    public string Path { get; }
    public StructuredDiagnosticLogger CreateLogger(DiagnosticOptions options) => Own(new StructuredDiagnosticLogger(options));
    public StructuredDiagnosticLogger Own(StructuredDiagnosticLogger logger)
    {
        _loggers.Add(logger);
        return logger;
    }
    public void Dispose()
    {
        // Product Dispose has a finite deadline. File cleanup must wait for its consumer
        // even when an assertion throws or another using already requested disposal.
        for (var index = _loggers.Count - 1; index >= 0; index--) _close(_loggers[index]);
        if (Directory.Exists(Path)) Directory.Delete(Path, true);
    }
}

sealed class PrefixCollisionFields : IReadOnlyDictionary<string, string?>
{
    public string Value { get; set; } = "original";
    public int Inspected { get; private set; }
    public int Count => 1000;
    public IEnumerable<string> Keys => throw new NotSupportedException();
    public IEnumerable<string?> Values => throw new NotSupportedException();
    public string? this[string key] => throw new NotSupportedException();
    public bool ContainsKey(string key) => throw new NotSupportedException();
    public bool TryGetValue(string key, out string? value) { value = null; return false; }
    public IEnumerator<KeyValuePair<string, string?>> GetEnumerator()
    {
        for (var index = 0; index < Count; index++)
        {
            if (++Inspected > 2) throw new InvalidOperationException("More than MaxFields input entries inspected.");
            yield return new KeyValuePair<string, string?>(new string('k', 64) + index, Value);
        }
    }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

sealed class FormattingProbeException : Exception
{
    public int Formatted { get; private set; }
    public override string ToString() { Formatted++; return "formatted"; }
}
