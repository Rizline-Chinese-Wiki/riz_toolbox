using RizToolbox.Adapter.AudioContent;
using RizToolbox.Core;
using RizToolbox.Sdk;
using static Assert;

internal static partial class LocalCatalogRegression
{
    internal static readonly (string Name, Action Test)[] RunResultCases = {
        ("LOCAL result observes exact capture, ignores official results and survives verified retirement", ResultAfterRetirement),
        ("LOCAL receipts keep distinct request identities across a new revision and a different plugin", ResultAcrossRequests),
        ("A LOCAL host retry cannot rebind the original request's pending receipt", ResultRetryDoesNotRebind),
        ("LOCAL result distinguishes a pending request from return without capture and capture failure", ResultTerminalFailures),
        ("LOCAL result handle and plugin scope expire independently of the protected run", ResultScopeLifetime),
        ("LOCAL result preserves dispatcher refusal and legacy RequestPlay creates no observation", ResultAdmission),
        ("LOCAL result quota is per selector and disposal restores capacity", ResultQuota)
    };

    private static void ResultAfterRetirement()
    {
        using var f = new RunResultFixture();
        using var selected = f.Select("result.reader");
        using var handle = Need(selected.RequestPlayWithResult());
        var run = f.Play.Last;
        CheckPending(handle);

        RunFinished? delivered = null;
        using var subscription = f.Events.Subscribe<RunFinished>(result => delivered = result);
        var official = new RunFinished("official-run", "official-chart", 99999, .91, 9, 9, 0, 0, false, false)
            { AssociatedWithObservedStart = true, EndReason = RunEndReason.Completed };
        f.Events.Publish(official);
        Check(ReferenceEquals(official, delivered));
        f.Play.OriginalResult();
        using var replacement = f.Select("result.reader");
        Check(!selected.IsCurrent && replacement.IsCurrent);
        selected.Dispose();
        CheckPending(handle);
        Equal(0, run.Backend.Invocations);

        run.Backend.BeforeInvoke = () => CheckPending(handle);
        f.Play.Capture(run, 31415);
        var cached = f.Play.Host.Sessions.Read(run.Session).Result!;
        var published = Need(handle.Read());
        Equal(selected.Snapshot.Song.Song, published.Song);
        Equal(selected.Snapshot.Song.Revision, published.SongRevision);
        Equal(selected.Snapshot.Chart.Id, published.ChartId);
        Equal(cached.Session.SessionId, published.LocalSessionId);
        Equal(cached.Session.RunGeneration, published.RunGeneration);
        Equal(cached.Copy, published.Result);
        Equal(new RunFinished(null, null, 31415, .5, 1, 2, 3, 4, false, false)
            { EndReason = RunEndReason.Completed }, published.Result);
        Equal(ErrorCode.WrongThread, Task.Run(() => handle.Read()).GetAwaiter().GetResult().Error!.Code);

        f.Play.Return(run);
        Check(f.Play.Host.Sessions.Read(run.Session).ProtectionRetired);
        Equal(3, run.Backend.Frees.Count);
        Equal(published, Need(handle.Read()));
    }

    private static void ResultAcrossRequests()
    {
        using var f = new RunResultFixture();
        using var firstSelection = f.Select("result.reader");
        using var first = Need(firstSelection.RequestPlayWithResult());
        var firstRun = f.Play.Last;
        f.Play.Capture(firstRun, 111);
        var old = Need(first.Read());
        f.Play.Return(firstRun);

        f.RepublishSong();
        using var nextSelection = f.Select("result.reader");
        Check(!firstSelection.IsCurrent && nextSelection.Snapshot.Song.Revision != old.SongRevision);
        using var next = Need(nextSelection.RequestPlayWithResult());
        var nextRun = f.Play.Last;
        CheckPending(next);
        Equal(old, Need(first.Read()));
        f.Play.Capture(nextRun, 222);
        var current = Need(next.Read());
        Equal(old.Song, current.Song);
        Equal(old.ChartId, current.ChartId);
        Equal(old.LocalSessionId, current.LocalSessionId);
        Check(current.RunGeneration > old.RunGeneration);
        Equal(nextSelection.Snapshot.Song.Revision, current.SongRevision);
        Equal(222L, current.Result.Score);
        f.Play.Return(nextRun);

        using var otherSelection = f.Select("result.other");
        using var other = Need(otherSelection.RequestPlayWithResult());
        CheckPending(other);
        f.Play.Capture(f.Play.Last, 333);
        Equal(333L, Need(other.Read()).Result.Score);
        Equal(current, Need(next.Read()));
        Equal(old, Need(first.Read()));
    }

    private static void ResultRetryDoesNotRebind()
    {
        using var f = new RunResultFixture();
        using var selected = f.Select("result.reader");
        using var requested = Need(selected.RequestPlayWithResult());
        var firstRun = f.Play.Last;
        CheckPending(requested);
        var retry = f.Play.Retry(firstRun);
        Check(!ReferenceEquals(firstRun.Session, retry.Session));
        var original = f.Play.Host.Sessions.Read(firstRun.Session).Identity;
        var repeated = f.Play.Host.Sessions.Read(retry.Session).Identity;
        Equal(original.SongRevision, repeated.SongRevision);
        Check(original.RunGeneration < repeated.RunGeneration);
        f.Play.Return(firstRun);
        Equal(ErrorCode.TargetExpired, requested.Read().Error!.Code);
        f.Play.Capture(retry, 444);
        Equal(ErrorCode.TargetExpired, requested.Read().Error!.Code);
    }

    private static void ResultTerminalFailures()
    {
        using var f = new RunResultFixture();
        using var selected = f.Select("result.reader");
        using var unfinished = Need(selected.RequestPlayWithResult());
        var first = f.Play.Last;
        CheckPending(unfinished);
        f.Play.Host.Sessions.BeginReturn(first.Session);
        var ended = unfinished.Read();
        Equal(ErrorCode.TargetExpired, ended.Error!.Code);
        Check(!ended.Error.Retryable);
        f.Play.Return(first);
        Equal(ErrorCode.TargetExpired, unfinished.Read().Error!.Code);

        using var unavailable = Need(selected.RequestPlayWithResult());
        var second = f.Play.Last;
        CheckPending(unavailable);
        var claim = f.Play.Host.Sessions.TryClaimResult(second.Session);
        Equal(LocalRunResultClaimKind.InvokeOnce, claim.Kind);
        f.Play.Host.Sessions.FailResult(claim.Claim!, "Controlled result capture failed.");
        var failed = unavailable.Read();
        Equal(ErrorCode.ObservationUnavailable, failed.Error!.Code);
        Check(!failed.Error.Retryable);
        f.Play.Return(second);
        Equal(ErrorCode.ObservationUnavailable, unavailable.Read().Error!.Code);
    }

    private static void ResultScopeLifetime()
    {
        const string id = "result.reader";
        using var f = new RunResultFixture();
        using var selected = f.Select(id);
        using var abandoned = Need(selected.RequestPlayWithResult());
        var run = f.Play.Last;
        Check(f.PluginHost.Disable(id).Success);
        Equal(ErrorCode.ScopeExpired, abandoned.Read().Error!.Code);
        Equal(1, f.Play.Host.Read().UnretiredSessions);
        f.Play.Capture(run, 555);
        Equal(ErrorCode.ScopeExpired, abandoned.Read().Error!.Code);
        f.Play.Return(run);

        Check(f.PluginHost.Enable(id).Success);
        using var newSelection = f.Select(id);
        using var fresh = Need(newSelection.RequestPlayWithResult());
        var newRun = f.Play.Last;
        CheckPending(fresh);
        Equal(ErrorCode.ScopeExpired, abandoned.Read().Error!.Code);
        f.Play.Capture(newRun, 666);
        var copy = Need(fresh.Read());
        fresh.Dispose();
        Equal(ErrorCode.ScopeExpired, fresh.Read().Error!.Code);
        Equal(666L, copy.Result.Score);
        Equal(1, f.Play.Host.Read().UnretiredSessions);
    }

    private static void ResultAdmission()
    {
        using var f = new RunResultFixture();
        using var selected = f.Select("result.reader");
        var error = new OperationError(ErrorCode.CapabilityUnavailable, "Controlled admission is unavailable.");
        f.Play.DispatchError = error;
        var denied = selected.RequestPlayWithResult();
        Check(!denied.Success && denied.Value is null && ReferenceEquals(error, denied.Error));
        Equal(0, f.Play.Host.Read().UnretiredSessions);
        f.Play.DispatchError = null;

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Equal(ErrorCode.Cancelled, selected.RequestPlayWithResult(cancelled.Token).Error!.Code);
        Equal(0, f.Play.Host.Read().UnretiredSessions);

        Need(selected.RequestPlay());
        var legacy = f.Play.Last;
        Check(legacy.Prepared.ResultObservation is null);
        ILocalCatalogSelection olderImplementation = new LegacySelection(selected.Snapshot);
        Equal(ErrorCode.OperationUnsupported, olderImplementation.RequestPlayWithResult().Error!.Code);
        f.Play.Capture(legacy, 777);
        Equal(777L, f.Play.Host.Sessions.Read(legacy.Session).Result!.Copy.Score);
    }

    private static void ResultQuota()
    {
        using var f = new RunResultFixture();
        using var selected = f.Select("result.reader");
        var held = new List<ILocalRunResultHandle>();
        try
        {
            for (var index = 0; index < 32; index++)
            {
                held.Add(Need(selected.RequestPlayWithResult()));
                f.Play.Return(f.Play.Last);
            }
            var last = f.Play.Last;
            Equal(ErrorCode.QuotaExceeded, selected.RequestPlayWithResult().Error!.Code);
            Check(ReferenceEquals(last, f.Play.Last));
            using var unrelated = f.Select("result.other");
            using var other = Need(unrelated.RequestPlayWithResult());
            CheckPending(other);
            f.Play.Return(f.Play.Last);
            held[0].Dispose();
            using var replacement = Need(selected.RequestPlayWithResult());
            CheckPending(replacement);
        }
        finally { foreach (var handle in held) handle.Dispose(); }
    }

    private sealed class LegacySelection : ILocalCatalogSelection
    {
        internal LegacySelection(LocalSongSelectionSnapshot snapshot) => Snapshot = snapshot;
        public LocalSongSelectionSnapshot Snapshot { get; }
        public bool IsCurrent => true;
        public Result RequestPlay(CancellationToken cancellationToken = default) => Result.Ok();
        public void Dispose() { }
    }

    private static void CheckPending(ILocalRunResultHandle handle)
    {
        var pending = handle.Read();
        Check(!pending.Success && pending.Value is null && pending.Error is { Code: ErrorCode.NotReady, Retryable: true });
    }

    private sealed class RunResultFixture : IDisposable
    {
        private readonly CatalogFixture _catalog;
        private readonly Context _provider;
        private readonly LocalSongRequest _request;
        private readonly Dictionary<string, IPluginContext> _contexts = new(StringComparer.Ordinal);
        private ILocalSongRegistration _song;
        internal EventHub Events { get; }
        internal PluginHost PluginHost { get; }
        internal RunResultDispatcher Play { get; }

        internal RunResultFixture()
        {
            var target = new PlayTarget();
            _catalog = new CatalogFixture(play: target, chartJson: ChartWithLines((true, true)));
            Play = new RunResultDispatcher(_catalog.Main);
            target.Execute = Play.Dispatch;
            _provider = _catalog.Context();
            _request = _catalog.Resources(_provider);
            _song = RegisterAuthoredChart(_catalog.Catalog.ForPlugin(_provider), _request);
            var log = new Logger();
            var capabilities = new CapabilityRegistry(new("test", "test", "test", "test"));
            capabilities.RegisterScoped<ILocalCatalogSelectionService>(AudioContentCapabilityIds.LocalCatalogSelection, _catalog.Catalog.SelectionForPlugin);
            Events = new EventHub(log);
            PluginHost = new PluginHost(capabilities, _catalog.Main, Events, log);
        }

        internal ILocalCatalogSelection Select(string pluginId)
        {
            if (!_contexts.ContainsKey(pluginId))
            {
                PluginHost.Register(new CatalogPlugin(context => _contexts[pluginId] = context, pluginId));
                Check(PluginHost.Enable(pluginId).Success);
            }
            var lookup = _contexts[pluginId].GetService<ILocalCatalogSelectionService>(AudioContentCapabilityIds.LocalCatalogSelection);
            Check(lookup.Available);
            return Need(lookup.Service!.SelectRevision(_song.Snapshot, "easy"));
        }

        internal void RepublishSong()
        {
            _song.Dispose();
            _song = RegisterAuthoredChart(_catalog.Catalog.ForPlugin(_provider), _request);
        }

        public void Dispose()
        {
            try { PluginHost.Dispose(); }
            finally
            {
                try { Play.Dispose(); }
                finally { _provider.Dispose(); _catalog.Dispose(); }
            }
        }
    }

    private sealed class RunResultDispatcher : ILocalRunBoundaryHandler, IDisposable
    {
        private readonly MainThreadDispatcher _main;
        private readonly Action<LocalBoundaryKind, long> _invoke;
        private readonly List<FixtureRun> _runs = new();
        internal LocalRunProtectionHost Host { get; }
        internal FixtureRun Last => _runs[^1];
        internal OperationError? DispatchError;
        public bool HasPendingWork => false;

        internal RunResultDispatcher(MainThreadDispatcher main)
        {
            _main = main;
            (Host, _invoke) = LocalRunProtectionHostRegression.CreateControlledHost(main);
            Host.InstallHandler(this);
        }

        internal Result Dispatch(LocalPlayRequest request)
        {
            if (DispatchError is { } error) return Result.Fail(error);
            var prepared = request.Retain();
            var backend = new NativeLocalResultCaptureRegression.Backend(_main);
            var selection = prepared.Selection;
            LocalRunSessionRegistry.SessionHandle session;
            try
            {
                session = Host.RegisterPrepared(new(selection.Song.Song, selection.Song.Revision, selection.Chart.Id),
                    backend.Source.Metadata, prepared, prepared.ResultObservation);
            }
            catch { prepared.Dispose(); throw; }
            var run = new FixtureRun(prepared, backend, session);
            _runs.Add(run);
            var commit = Host.Sessions.BeginCommit(session);
            Host.Sessions.BindPendingController(commit, backend.Source.Controller, new FixtureLease());
            Host.Sessions.Commit(commit);
            return Result.Ok();
        }

        public void ValidateHost(LocalRunSessionRegistry registry) => Check(ReferenceEquals(registry, Host.Sessions));

        public void Handle(LocalBoundaryKind boundary, LocalRunSessionRegistry.SessionHandle session, long receiver)
        {
            var run = _runs.Single(entry => ReferenceEquals(entry.Session, session));
            Equal(run.Backend.Source.Controller, receiver);
            if (boundary == LocalBoundaryKind.ControllerRetry)
            {
                Need(Dispatch(run.Prepared));
                return;
            }
            Equal(LocalBoundaryKind.ControllerLevelOver, boundary);
            run.Capture ??= NativeLocalResultCapture.CreateBackend(_main, run.Backend, Host.Sessions, session);
            Check(run.Capture.Capture() is not null);
        }

        public void Tick() { }

        internal void OriginalResult()
        {
            var boundary = LocalBoundaryKind.ControllerLevelOver;
            var before = Host.Read().Boundaries.Single(row => row.Boundary == boundary);
            _invoke(boundary, 0x1234);
            var after = Host.Read().Boundaries.Single(row => row.Boundary == boundary);
            Equal(before.OriginalCalls + 1, after.OriginalCalls);
            Equal(before.Claimed, after.Claimed);
        }

        internal void Capture(FixtureRun run, long score)
        {
            run.Backend.Values = run.Backend.Values with { Score = score };
            _invoke(LocalBoundaryKind.ControllerLevelOver, run.Backend.Source.Controller);
            Equal(1, run.Backend.Invocations);
            Equal(score, Host.Sessions.Read(run.Session).Result!.Copy.Score);
        }

        internal FixtureRun Retry(FixtureRun run)
        {
            var count = _runs.Count;
            _invoke(LocalBoundaryKind.ControllerRetry, run.Backend.Source.Controller);
            Equal(count + 1, _runs.Count);
            return Last;
        }

        internal void Return(FixtureRun run)
        {
            LocalRunProtectionHostRegression.RetireControlledRun(Host.Sessions, run.Session);
            run.Capture?.Dispose();
        }

        public void Dispose()
        {
            try { foreach (var run in _runs) Return(run); }
            finally { Host.RequestStop(); }
        }
    }

    private sealed record FixtureRun(LocalPlayRequest Prepared, NativeLocalResultCaptureRegression.Backend Backend,
        LocalRunSessionRegistry.SessionHandle Session)
    {
        internal NativeLocalResultCapture? Capture;
    }

    private sealed class FixtureLease : IDisposable
    {
        public void Dispose() { }
    }
}
