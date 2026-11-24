using RizToolbox.Adapter;
using RizToolbox.Core;
using RizToolbox.Sdk;

var checks = new (string Name, Action Run)[] {
    ("No empty ready snapshot is fabricated before capture", () => {
        using var f = new Fixture(); Error(ErrorCode.NotReady, f.Service.GetHeader());
    }),
    ("A legitimate empty source becomes a valid empty revision", () => {
        using var f = new Fixture(new(Array.Empty<ChartInfo>(), Array.Empty<ScoreRecord>()));
        var header = Good(f.Service.Refresh()); Equal(0, header.ChartCount);
        var page = Good(f.Service.GetCharts(new(header.Token))); Equal(0, page.Items.Count); Equal(null, page.NextOffset);
    }),
    ("444 chart IDs and score slots survive bounded pagination", () => {
        using var f = new Fixture(Fixture.Input(444)); var header = Good(f.Service.Refresh()); var ids = new List<string>();
        for (int offset = 0; offset < header.ChartCount;)
        {
            var page = Good(f.Service.GetCharts(new(header.Token, offset, 73))); Equal(header.Token, page.Header.Token);
            ids.AddRange(page.Items.Select(x => x.Id)); offset = page.NextOffset ?? header.ChartCount;
        }
        Equal(444, ids.Count); Equal(444, ids.Distinct().Count()); Equal(444, header.BestScoreCount);
    }),
    ("One refresh captures the combined source once and pages perform no new capture", () => {
        using var f = new Fixture(Fixture.Input(444)); var header = Good(f.Service.Refresh()); Equal(1, f.Captures);
        Good(f.Service.GetCharts(new(header.Token, 0, 256))); Good(f.Service.GetCharts(new(header.Token, 256, 256)));
        Good(f.Service.GetBestScores(new(header.Token, 0, 256))); Good(f.Service.GetBestScores(new(header.Token, 256, 256)));
        Equal(1, f.Captures); Good(f.Service.Refresh()); Equal(2, f.Captures);
    }),
    ("Different stable IDs with the same displayed title stay separate", () => {
        using var f = new Fixture(); var header = Good(f.Service.Refresh());
        var page = Good(f.Service.GetCharts(new(header.Token))); Equal(2, page.Items.Count);
        Check(page.Items[0].Title == page.Items[1].Title && page.Items[0].Id != page.Items[1].Id);
    }),
    ("Unchanged and reordered source data keep one revision", () => {
        using var f = new Fixture(); var first = Good(f.Service.Refresh());
        f.Current = f.Current with { Charts = f.Current.Charts.Reverse().ToArray(), BestScores = f.Current.BestScores.Reverse().ToArray() };
        var second = Good(f.Service.Refresh()); Equal(first.Token, second.Token); Check(second.VerifiedAt >= first.VerifiedAt);
    }),
    ("A score edit increments the revision without altering an earlier page", () => {
        using var f = new Fixture(); var first = Good(f.Service.Refresh());
        var originalPage = Good(f.Service.GetBestScores(new(first.Token)));
        f.Current = f.Current with { BestScores = f.Current.BestScores.Select(s => s with { Score = s.Score + 1 }).ToArray() };
        var second = Good(f.Service.Refresh()); Equal(first.Token.Revision + 1, second.Token.Revision);
        Error(ErrorCode.NotReady, f.Service.GetBestScores(new(first.Token)));
        var old = Good(f.Service.GetBestScores(new(first.Token, AllowStale: true)));
        Check(old.Header.IsStale); Equal(originalPage.Items[0].Score, old.Items[0].Score);
    }),
    ("Expired revision and another session token return explicit expiration", () => {
        using var f = new Fixture(); var first = Good(f.Service.Refresh());
        for (var i = 0; i < 2; i++) { f.Current = f.Current with { Charts = f.Current.Charts.Select(c => c with { Rating = i + 5 }).ToArray() }; f.Service.Refresh(); }
        Error(ErrorCode.TargetExpired, f.Service.GetCharts(new(first.Token, AllowStale: true)));
        Error(ErrorCode.TargetExpired, f.Service.GetCharts(new(new("another-session", 1))));
    }),
    ("Provider failure invalidates current data rather than silently serving it as fresh", () => {
        using var f = new Fixture(); var header = Good(f.Service.Refresh());
        f.Failure = new(ErrorCode.NotReady, "asset data is loading", DataCapabilityIds.Read, Retryable: true);
        Error(ErrorCode.NotReady, f.Service.Refresh()); Error(ErrorCode.NotReady, f.Service.GetHeader());
        Error(ErrorCode.NotReady, f.Service.GetCharts(new(header.Token)));
        Check(Good(f.Service.GetHeader(true)).IsStale);
        Check(Good(f.Service.GetCharts(new(header.Token, AllowStale: true))).Header.IsStale);
    }),
    ("Successful same-content refresh recovers freshness without changing revision", () => {
        using var f = new Fixture(); var first = Good(f.Service.Refresh()); f.Service.Invalidate("unloaded");
        var recovered = Good(f.Service.Refresh()); Equal(first.Token, recovered.Token); Check(!recovered.IsStale);
    }),
    ("Mutable source arrays cannot mutate a captured page", () => {
        var charts = Fixture.Input().Charts.ToArray(); using var f = new Fixture(new(charts, Fixture.Input().BestScores));
        var header = Good(f.Service.Refresh()); var oldId = charts[0].Id; charts[0] = charts[0] with { Id = "new" };
        var page = Good(f.Service.GetCharts(new(header.Token))); Equal(oldId, page.Items[0].Id);
        Check(page.Items is not ChartInfo[]);
    }),
    ("Missing play time is preserved as null", () => {
        using var f = new Fixture(); var header = Good(f.Service.Refresh());
        Check(Good(f.Service.GetBestScores(new(header.Token))).Items.All(s => s.PlayedAt is null));
        Check(f.Service.GetFieldDescriptions().Single(x => x.Field == "ScoreRecord.PlayedAt").NullReason!.Contains("Never"));
    }),
    ("Duplicate IDs and score/catalog mismatch reject source content", () => {
        using var f = new Fixture(); f.Current = f.Current with { Charts = new[] { f.Current.Charts[0], f.Current.Charts[0] } };
        Error(ErrorCode.ContentInvalid, f.Service.Refresh());
        f.Current = Fixture.Input() with { BestScores = new[] { Fixture.Input().BestScores[0] with { ChartId = "missing" } } };
        Error(ErrorCode.ContentInvalid, f.Service.Refresh());
    }),
    ("Missing chart fields and nonfinite values reject source content", () => {
        using var f = new Fixture(); f.Current = f.Current with { Charts = new[] { f.Current.Charts[0] with { SongId = "" } } };
        Error(ErrorCode.ContentInvalid, f.Service.Refresh());
        f.Current = Fixture.Input() with { BestScores = Fixture.Input().BestScores.Select(s => s with { RawCompletion = double.NaN }).ToArray() };
        Error(ErrorCode.ContentInvalid, f.Service.Refresh());
    }),
    ("Entry count and actual UTF-8 bytes are bounded", () => {
        using var entryLimit = new Fixture(Fixture.Input(), new(MaximumEntries: 3));
        Error(ErrorCode.QuotaExceeded, entryLimit.Service.Refresh());
        using var byteLimit = new Fixture(Fixture.Input() with { Charts = Fixture.Input().Charts.Select(c => c with { Title = new string('绿', 500) }).ToArray() }, new(MaximumBytes: 1000));
        Error(ErrorCode.QuotaExceeded, byteLimit.Service.Refresh());
    }),
    ("Cancel before capture performs no provider work", () => {
        using var f = new Fixture(); using var stop = new CancellationTokenSource(); stop.Cancel();
        Error(ErrorCode.Cancelled, f.Service.Refresh(stop.Token)); Equal(0, f.Captures);
    }),
    ("Cancel during capture preserves the existing revision", () => {
        using var f = new Fixture(); var header = Good(f.Service.Refresh()); using var stop = new CancellationTokenSource();
        f.AfterCapture = stop.Cancel; Error(ErrorCode.Cancelled, f.Service.Refresh(stop.Token));
        Equal(header.Token, Good(f.Service.GetHeader()).Token);
    }),
    ("Cancelled pages and invalid page bounds fail explicitly", () => {
        using var f = new Fixture(); var header = Good(f.Service.Refresh()); using var stop = new CancellationTokenSource(); stop.Cancel();
        Error(ErrorCode.Cancelled, f.Service.GetCharts(new(header.Token), stop.Token));
        Error(ErrorCode.InvalidArgument, f.Service.GetCharts(new(header.Token, -1)));
        Error(ErrorCode.InvalidArgument, f.Service.GetCharts(new(header.Token, Count: 257)));
        Error(ErrorCode.InvalidArgument, f.Service.GetCharts(new(header.Token, 3)));
        Error(ErrorCode.InvalidArgument, f.Service.GetCharts(new(new("", 0))));
    }),
    ("Wrong thread cannot trigger any native/source reads", () => {
        using var f = new Fixture(); var header = Good(f.Service.Refresh()); var reads = f.Captures;
        Task.Run(() => { Error(ErrorCode.WrongThread, f.Service.Refresh()); Error(ErrorCode.WrongThread, f.Service.GetCharts(new(header.Token))); }).GetAwaiter().GetResult();
        Equal(reads, f.Captures);
    }),
    ("No current-result source reports unsupported rather than inventing a play", () => {
        using var f = new Fixture(); var header = Good(f.Service.Refresh()); Error(ErrorCode.OperationUnsupported, f.Service.GetCurrentResult(header.Token));
        f.Current = f.Current with { SupportsCurrentResult = true }; var available = Good(f.Service.Refresh());
        Error(ErrorCode.NotReady, f.Service.GetCurrentResult(available.Token));
    }),
    ("Actual run counters stay independent of the best-score snapshot", () => {
        using var f = new Fixture(); var result = new RunFinished("run", "chart-0000", 12, .25, 3, 7, 2, 5, true, false);
        f.Current = f.Current with { CurrentResult = result }; var header = Good(f.Service.Refresh());
        var copied = Good(f.Service.GetCurrentResult(header.Token)); Equal(result, copied); Equal(3, copied.Perfect); Equal(7, copied.MaxPerfect);
        Check(Good(f.Service.GetBestScores(new(header.Token))).Items[0].Score != copied.Score);
    }),
    ("Settings require a real source and validate copied JSON", () => {
        using var f = new Fixture(); var first = Good(f.Service.Refresh()); Error(ErrorCode.OperationUnsupported, f.Service.GetSettings(new(first.Token)));
        f.Current = f.Current with { Settings = new[] { new GameSettingSnapshot("notes.speed", "1.5", "verified game field", "multiplier") } };
        var second = Good(f.Service.Refresh()); Equal("1.5", Good(f.Service.GetSettings(new(second.Token))).Items.Single().JsonValue);
        f.Current = f.Current with { Settings = new[] { new GameSettingSnapshot("bad", "{bad", "field") } };
        Error(ErrorCode.ContentInvalid, f.Service.Refresh());
    }),
    ("Provider exceptions are isolated and mark cache stale", () => {
        using var f = new Fixture(); f.Service.Refresh(); f.AfterCapture = () => throw new InvalidOperationException("provider failed");
        Error(ErrorCode.ServiceUnavailable, f.Service.Refresh()); Check(Good(f.Service.GetHeader(true)).IsStale);
    }),
    ("Reentrant refresh leaves the current capture as the only publisher", () => {
        using var f = new Fixture(); var first = Good(f.Service.Refresh());
        Result<DataSnapshotHeader>? nested = null;
        f.AfterCapture = () => {
            f.AfterCapture = null;
            Equal(first.Token, Good(f.Service.GetHeader()).Token);
            nested = f.Service.Refresh();
        };
        f.Current = Fixture.Input(3);
        var next = Good(f.Service.Refresh());
        Error(ErrorCode.Conflict, nested!);
        Equal(2, f.Captures); Equal(first.Token.Revision + 1, next.Token.Revision);
    }),
    ("Invalidation during capture stays in force until a later refresh", () => {
        using var f = new Fixture(); var first = Good(f.Service.Refresh());
        f.AfterCapture = () => f.Service.Invalidate("source changed during capture");
        Error(ErrorCode.NotReady, f.Service.Refresh());
        Equal("source changed during capture", Good(f.Service.GetHeader(true)).StaleReason);
        Error(ErrorCode.NotReady, f.Service.GetCharts(new(first.Token)));
        f.AfterCapture = null; Equal(first.Token, Good(f.Service.Refresh()).Token);
    }),
    ("Provider-reported cancellation preserves the previously verified revision", () => {
        using var f = new Fixture(); var first = Good(f.Service.Refresh());
        using var cancellation = new CancellationTokenSource();
        f.AfterCapture = cancellation.Cancel;
        f.Failure = new(ErrorCode.Cancelled, "native catalog capture cancelled", DataCapabilityIds.Read);
        Error(ErrorCode.Cancelled, f.Service.Refresh(cancellation.Token));
        var current = Good(f.Service.GetHeader());
        Equal(first.Token, current.Token); Equal(first.VerifiedAt, current.VerifiedAt); Check(!current.IsStale);
    }),
    ("Release during capture prevents snapshot publication", () => {
        using var f = new Fixture(); var first = Good(f.Service.Refresh());
        f.AfterCapture = f.Service.Dispose;
        Error(ErrorCode.ScopeExpired, f.Service.Refresh());
        Error(ErrorCode.ScopeExpired, f.Service.GetCharts(new(first.Token, AllowStale: true)));
    }),
    ("A newly observed result expires default reads and preserves explicitly requested old pages", () => {
        var firstResult = new RunFinished("first-run", "chart-0000", 12, .25, 3, 7, 2, 5, true, false);
        var nextResult = firstResult with { RunId = "second-run", Score = 13 };
        (bool Available, RunFinished? Result) observed = (true, firstResult);
        using var f = new Fixture(observeResult: () => observed);
        var first = Good(f.Service.Refresh());
        Equal(firstResult, Good(f.Service.GetCurrentResult(first.Token)));
        observed = (true, nextResult);
        Error(ErrorCode.NotReady, f.Service.GetCurrentResult(first.Token));
        Error(ErrorCode.NotReady, f.Service.GetHeader());
        Error(ErrorCode.NotReady, f.Service.GetCharts(new(first.Token)));
        Equal(firstResult, Good(f.Service.GetCurrentResult(first.Token, allowStale: true)));
        Check(Good(f.Service.GetCharts(new(first.Token, AllowStale: true))).Header.IsStale);
        Equal(1, f.Captures);
        var next = Good(f.Service.Refresh());
        Equal(first.Token.Revision + 1, next.Token.Revision);
        Equal(nextResult, Good(f.Service.GetCurrentResult(next.Token)));
        observed = (false, null);
        Error(ErrorCode.NotReady, f.Service.GetCurrentResult(next.Token));
        var unavailable = Good(f.Service.Refresh());
        Check(!unavailable.SupportsCurrentResult);
        Error(ErrorCode.OperationUnsupported, f.Service.GetCurrentResult(unavailable.Token));
    }),
    ("Refreshing across a result change does not combine different observation cuts", () => {
        var firstResult = new RunFinished("first-run", "chart-0000", 12, .25, 3, 7, 2, 5, true, false);
        var nextResult = firstResult with { RunId = "second-run", Score = 13 };
        var observed = firstResult;
        using var f = new Fixture(observeResult: () => (true, observed));
        var first = Good(f.Service.Refresh());
        f.Current = Fixture.Input(3);
        f.AfterCapture = () => observed = nextResult;
        Error(ErrorCode.NotReady, f.Service.Refresh());
        var stale = Good(f.Service.GetHeader(allowStale: true));
        Check(stale.IsStale); Equal(first.Token, stale.Token); Equal(2, stale.ChartCount);
        Equal(firstResult, Good(f.Service.GetCurrentResult(first.Token, allowStale: true)));
        f.AfterCapture = null;
        var next = Good(f.Service.Refresh());
        Equal(first.Token.Revision + 1, next.Token.Revision); Equal(3, next.ChartCount);
        Equal(nextResult, Good(f.Service.GetCurrentResult(next.Token)));
    }),
    ("Released module clears cache and refuses further reads", () => {
        var f = new Fixture(); var header = Good(f.Service.Refresh()); f.Service.Dispose();
        Error(ErrorCode.ScopeExpired, f.Service.GetHeader()); Error(ErrorCode.ScopeExpired, f.Service.GetCharts(new(header.Token)));
        f.Service.Dispose();
    })
};
int failures = 0;
foreach (var test in checks) try { test.Run(); Console.WriteLine("PASS " + test.Name); }
catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + test.Name + ": " + ex.Message); }
Console.WriteLine($"Revisioned data checks: {checks.Length - failures}/{checks.Length} passed.");
return failures == 0 ? 0 : 1;

static void Check(bool value) { if (!value) throw new InvalidOperationException("Assertion failed."); }
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}."); }
static T Good<T>(Result<T> result) => result.Success ? result.Value! : throw new InvalidOperationException($"Unexpected {result.Error!.Code}: {result.Error.Message}");
static void Error<T>(ErrorCode code, Result<T> result) { if (result.Success || result.Error!.Code != code) throw new InvalidOperationException($"Expected {code}, got {(result.Success ? "success" : result.Error!.Code.ToString())}."); }

sealed class Fixture : IDisposable
{
    internal DataSnapshotService Service;
    internal DataSnapshotInput Current;
    internal OperationError? Failure; internal Action? AfterCapture; internal int Captures;
    internal Fixture(DataSnapshotInput? input = null, DataSnapshotLimits? limits = null,
        Func<(bool Available, RunFinished? Result)>? observeResult = null)
    {
        Current = input ?? Input(); var main = new MainThreadDispatcher();
        Service = new(main, token => {
            main.VerifyAccess(); Captures++; AfterCapture?.Invoke();
            return Failure is null ? Result<DataSnapshotInput>.Ok(Current) : Result<DataSnapshotInput>.Fail(Failure);
        }, limits, observeResult);
    }
    internal static DataSnapshotInput Input(int count = 2)
    {
        var charts = Enumerable.Range(0, count).Select(i => new ChartInfo($"chart-{i:D4}", $"song-{i:D4}", "same title", "IN", 12)).ToArray();
        var scores = charts.Select((c, i) => new ScoreRecord(c.Id, 100 + i, .91, 80, 2, 1)).ToArray();
        return new(charts, scores);
    }
    public void Dispose() => Service.Dispose();
}
