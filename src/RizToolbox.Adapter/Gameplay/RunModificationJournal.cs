using System.Diagnostics;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>Adapter-only ordinary DTO, never a public plugin write API. Producers call only after actual dispatch/application.</summary>
internal sealed record RunModificationInput(string Owner, string Source, string Operation, string TargetId,
    RunModificationCategory Category, RunModificationOutcome Outcome, string Detail,
    string? ObservedRunId = null, string? ObservedSessionId = null, long? ObservedLifecycleRevision = null,
    bool LocalResultBehaviorVerified = false, bool SubmissionBehaviorVerified = false);
internal sealed record RunModificationJournalLimits(int MaximumRuns = 64, int MaximumEntriesPerRun = 256,
    int MaximumUnassociatedEntries = 256, int MaximumActiveEffects = 128, int MaximumQueryEntries = 512);

/// <summary>One real lifecycle session. Record callbacks never propagate logging/audit faults into a native operation.</summary>
internal sealed class RunModificationJournal : IDisposable
{
    private readonly IMainThreadDispatcher _main;
    private readonly ILogger _log;
    private readonly IGameLifecycleService _lifecycle;
    private readonly RunModificationJournalLimits _limits;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<string, RunState> _runs = new(StringComparer.Ordinal);
    private readonly Queue<string> _runOrder = new();
    private readonly Queue<RunModificationRecord> _unassociated = new();
    private readonly Dictionary<string, Effect> _effects = new(StringComparer.Ordinal);
    private readonly Queue<Effect> _retiredEffects = new();
    private readonly Queue<string> _deferredRunEffectReleases = new();
    private IGameLifecycleSubscription? _subscription;
    private GameLifecycleSnapshot _snapshot = null!;
    private long _lastEventRevision, _sequence, _revision;
    private bool _disposed, _processing, _associationAvailable = true;
    private long _recorded, _unassociatedTotal, _invalid, _reentrant, _expired, _capacity, _stale, _wrongThread, _evictedRecords, _evictedRuns, _internalFailures;
    private sealed class RunState
    {
        internal string Id = null!; internal string? Chart;
        internal long? Start, End; internal long Last, Evicted; internal RunEndReason? Reason; internal bool Closed;
        internal readonly Queue<RunModificationRecord> Entries = new();
    }
    private sealed record Effect(string Id, RunModificationInput Input, long ActivatedRevision, bool RunScoped,
        long? ReleasedRevision = null);
    internal RunModificationJournal(IMainThreadDispatcher main, ILogger log, IGameLifecycleService lifecycle, RunModificationJournalLimits? limits = null)
    {
        _main = main; _log = log; _lifecycle = lifecycle; _limits = limits ?? new(); _main.VerifyAccess();
        if (_limits.MaximumRuns is < 1 or > 256 || _limits.MaximumEntriesPerRun is < 1 or > 4096 || _limits.MaximumUnassociatedEntries is < 1 or > 4096 ||
            _limits.MaximumActiveEffects is < 1 or > 1024 || _limits.MaximumQueryEntries is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(limits));
        var subscribed = lifecycle.Subscribe(OnLifecycle);
        if (!subscribed.Success || subscribed.Value is null) throw new InvalidOperationException(subscribed.Error?.Message ?? "Lifecycle subscription unavailable.");
        _subscription = subscribed.Value;
        try
        {
            if (!ValidSnapshot(_subscription.InitialSnapshot)) throw new InvalidDataException("Initial lifecycle snapshot is invalid.");
            _snapshot = Copy(_subscription.InitialSnapshot); _lastEventRevision = _snapshot.Revision;
            ObserveCurrent(_snapshot); // A baseline run is real, but it is not a newly observed start event.
        }
        catch { _subscription.Dispose(); _subscription = null; throw; }
    }
    internal IRunModificationService ForPlugin(IPluginContext context) => context.Own(new View(this, context));
    internal RunModificationCounters Counters => new(_recorded, _unassociatedTotal, _invalid, _reentrant, _expired, _capacity, _stale,
        Interlocked.Read(ref _wrongThread), _evictedRecords, _evictedRuns, _internalFailures);
    private static bool ValidSnapshot(GameLifecycleSnapshot? snapshot) => snapshot is not null && !string.IsNullOrWhiteSpace(snapshot.SessionId) && snapshot.SessionId.Length <= 128 &&
        snapshot.Revision >= 0 && snapshot.SceneGeneration >= 0 && snapshot.Limitations is not null && (snapshot.RunId is null || snapshot.RunId.Length is > 0 and <= 128);
    private static GameLifecycleSnapshot Copy(GameLifecycleSnapshot snapshot) => snapshot with { Limitations = Array.AsReadOnly(snapshot.Limitations.ToArray()) };
    private static bool Active(GameLifecycleSnapshot snapshot) => snapshot.RunsAvailable && snapshot.RunId is not null && snapshot.Phase is RunLifecyclePhase.Playing or RunLifecyclePhase.Paused or RunLifecyclePhase.AwaitingResult;
    private bool Enter()
    {
        if (!_main.IsMainThread) { Interlocked.Increment(ref _wrongThread); return false; }
        if (_disposed) { _expired++; return false; }
        if (_processing) { _reentrant++; return false; }
        _processing = true; return true;
    }
    private void Refresh()
    {
        var fresh = _lifecycle.GetSnapshot();
        if (!fresh.Success || !ValidSnapshot(fresh.Value) || fresh.Value!.SessionId != _snapshot.SessionId || fresh.Value.Revision < _snapshot.Revision)
        { _associationAvailable = false; return; }
        _associationAvailable = true; _snapshot = Copy(fresh.Value); ObserveCurrent(_snapshot);
    }
    private void ObserveCurrent(GameLifecycleSnapshot snapshot)
    {
        foreach (var run in _runs.Values)
            if (run.Id != snapshot.RunId || snapshot.Phase == RunLifecyclePhase.Finished) run.Closed = true;
        if (Active(snapshot)) { var run = EnsureRun(snapshot.RunId!, snapshot.ChartId, snapshot.Revision); run.Last = Math.Max(run.Last, snapshot.Revision); }
    }
    private RunState EnsureRun(string id, string? chart, long revision)
    {
        if (_runs.TryGetValue(id, out var current)) return current;
        if (_runs.Count >= _limits.MaximumRuns)
        {
            var oldest = _runOrder.Dequeue(); var evicted = _runs[oldest]; _runs.Remove(oldest); _evictedRuns++; _evictedRecords += evicted.Entries.Count;
        }
        var run = new RunState { Id = id, Chart = chart, Last = revision }; _runs.Add(id, run); _runOrder.Enqueue(id); return run;
    }
    internal void Record(RunModificationInput input)
    {
        if (!Enter()) return;
        try
        {
            if (!Valid(input) || input.Outcome is RunModificationOutcome.EffectActivated or RunModificationOutcome.EffectCarriedIntoRun or RunModificationOutcome.EffectReleased) { _invalid++; return; }
            Refresh(); Write(input, null);
        }
        catch (Exception ex) { Failure(ex); }
        finally { Exit(); }
    }
    internal IDisposable BeginEffect(RunModificationInput input)
        => BeginEffect(input, runScoped: false) ?? EmptyEffect.Instance;

    // A per-run effect requires an exact current observation at activation. Its
    // completion belongs to that same record even if the lifecycle has moved on.
    internal IDisposable? BeginObservedRunEffect(RunModificationInput input)
        => BeginEffect(input, runScoped: true);

    private IDisposable? BeginEffect(RunModificationInput input, bool runScoped)
    {
        if (!Enter()) return null;
        try
        {
            if (!Valid(input) || input.Outcome != RunModificationOutcome.Applied
                || runScoped && (input.ObservedRunId is null || input.ObservedSessionId is null || input.ObservedLifecycleRevision is null))
            { _invalid++; return null; }
            if (_effects.Count >= _limits.MaximumActiveEffects) { _capacity++; return null; }
            Refresh();
            if (runScoped && (_snapshot.Phase is not (RunLifecyclePhase.Playing or RunLifecyclePhase.Paused)
                || FindCurrentRun(_snapshot, input) is null)) return null;
            var id = Guid.NewGuid().ToString("N");
            var effect = new Effect(id, input, _snapshot.Revision, runScoped);
            _effects.Add(id, effect);
            Write(input with { Outcome = RunModificationOutcome.EffectActivated }, id); return new EffectLease(this, id);
        }
        catch (Exception ex) { Failure(ex); return null; }
        finally { Exit(); }
    }
    private bool ReleaseEffect(string id)
    {
        if (_main.IsMainThread && _processing && _effects.TryGetValue(id, out var pending) && pending.RunScoped)
        {
            _deferredRunEffectReleases.Enqueue(id);
            return true;
        }
        if (!Enter()) return _main.IsMainThread && _disposed;
        try { ReleaseEffectNow(id); return true; }
        catch (Exception ex) { Failure(ex); return true; }
        finally { Exit(); }
    }
    private void ReleaseEffectNow(string id)
    {
        if (!_effects.Remove(id, out var effect)) return;
        Refresh();
        var release = effect.Input with { Outcome = RunModificationOutcome.EffectReleased, ObservedLifecycleRevision = null };
        if (effect.RunScoped)
        {
            if (_runs.TryGetValue(effect.Input.ObservedRunId!, out var owner))
                Write(release, id, endingRun: owner);
            return;
        }
        if (_retiredEffects.Count >= _limits.MaximumActiveEffects * 2) { _retiredEffects.Dequeue(); _capacity++; }
        _retiredEffects.Enqueue(effect with { ReleasedRevision = _snapshot.Revision });
        Write(release with { ObservedRunId = null, ObservedSessionId = null }, id);
    }
    private void Exit()
    {
        try
        {
            while (_deferredRunEffectReleases.TryDequeue(out var id))
                try { ReleaseEffectNow(id); } catch (Exception ex) { Failure(ex); }
        }
        finally { _processing = false; }
    }
    private static bool Valid(RunModificationInput? input) => input is not null && Token(input.Owner, 128) && Token(input.Source, 128) && Token(input.Operation, 128) &&
        Token(input.TargetId, 256) && input.Detail is { Length: <= 2048 } && Enum.IsDefined(typeof(RunModificationCategory), input.Category) && input.Category != RunModificationCategory.Unknown &&
        Enum.IsDefined(typeof(RunModificationOutcome), input.Outcome) && input.Outcome != RunModificationOutcome.Unknown &&
        (input.ObservedRunId is null || Token(input.ObservedRunId, 128)) && (input.ObservedSessionId is null || Token(input.ObservedSessionId, 128)) && input.ObservedLifecycleRevision is null or >= 0;
    private static bool Token(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && !value.Any(char.IsControl);
    private RunState? FindCurrentRun(GameLifecycleSnapshot state, RunModificationInput input)
        => _associationAvailable && Active(state) && _runs.TryGetValue(state.RunId!, out var live) && !live.Closed &&
            (input.ObservedRunId is null || input.ObservedRunId == live.Id) &&
            (input.ObservedSessionId is null || input.ObservedSessionId == state.SessionId) &&
            (input.ObservedLifecycleRevision is null || input.ObservedLifecycleRevision == state.Revision) ? live : null;
    private void Write(RunModificationInput input, string? effectId, GameLifecycleSnapshot? carriedStart = null, RunState? endingRun = null)
    {
        var state = carriedStart ?? _snapshot; RunState? run = null; var association = RunModificationAssociation.Unassociated;
        if (endingRun is not null)
        { run = endingRun; association = RunModificationAssociation.ExplicitObservedRun; }
        else if (carriedStart is not null)
        { run = EnsureRun(carriedStart.RunId!, carriedStart.ChartId, carriedStart.Revision); if (!run.Closed) association = RunModificationAssociation.ActiveEffectAtRunStart; else run = null; }
        else if (FindCurrentRun(state, input) is { } current)
        { run = current; association = input.ObservedRunId is null ? RunModificationAssociation.CurrentObservedRun : RunModificationAssociation.ExplicitObservedRun; }
        var record = new RunModificationRecord(++_sequence, state.SessionId, run?.Id, state.Revision, state.SceneGeneration,
            input.Owner, input.Source, input.Operation, input.TargetId, input.Category, input.Outcome, association,
            input.Detail, _clock.Elapsed.TotalSeconds, effectId, input.ObservedRunId, input.ObservedSessionId, input.ObservedLifecycleRevision,
            input.LocalResultBehaviorVerified, input.SubmissionBehaviorVerified);
        if (run is null)
        { if (_unassociated.Count >= _limits.MaximumUnassociatedEntries) { _unassociated.Dequeue(); _evictedRecords++; } _unassociated.Enqueue(record); _unassociatedTotal++; }
        else
        { if (run.Entries.Count >= _limits.MaximumEntriesPerRun) { run.Entries.Dequeue(); run.Evicted++; _evictedRecords++; } run.Entries.Enqueue(record); }
        _recorded++; _revision++; Log("Run modification observed: " + record.Owner + "/" + record.Source + "/" + record.Operation + "; run=" + (record.RunId ?? "unassociated") + "; outcome=" + record.Outcome);
    }
    private void OnLifecycle(GameLifecycleChange change)
    {
        if (!Enter()) return;
        try
        {
            var state = change?.Snapshot;
            if (!ValidSnapshot(state) || _snapshot is null || state!.SessionId != _snapshot.SessionId || state.Revision <= _lastEventRevision) { _stale++; return; }
            _lastEventRevision = state.Revision;
            // Public event handlers can run before this subscription and record against a fresher actual snapshot.
            if (state.Revision >= _snapshot.Revision) { _snapshot = Copy(state); _associationAvailable = true; ObserveCurrent(_snapshot); }
            if (change!.Kind == GameLifecycleChangeKind.RunStarted && Active(state))
            {
                var run = EnsureRun(state.RunId!, state.ChartId, state.Revision);
                if (run.Start is null)
                {
                    run.Start = state.Revision; run.Last = Math.Max(run.Last, state.Revision);
                    foreach (var effect in _effects.Values.Concat(_retiredEffects).Where(x => !x.RunScoped && x.ActivatedRevision < state.Revision && (x.ReleasedRevision is null || x.ReleasedRevision >= state.Revision)).ToArray())
                        Write(effect.Input with { Outcome = RunModificationOutcome.EffectCarriedIntoRun }, effect.Id, state);
                }
            }
            if (change.Kind == GameLifecycleChangeKind.RunEnded && change.RelatedRunId is { } ended &&
                (change.Result?.SessionId is null || change.Result.SessionId == _snapshot.SessionId) && _runs.TryGetValue(ended, out var known))
            {
                known.Closed = true; known.End ??= state.Revision; known.Reason ??= change.EndReason; known.Last = Math.Max(known.Last, state.Revision);
            }
            _revision++;
        }
        catch (Exception ex) { Failure(ex); }
        finally { Exit(); }
    }
    private Result<RunModificationJournalSnapshot> Read(IPluginContext context, RunModificationQuery? query)
    {
        if (!_main.IsMainThread) return Fail(ErrorCode.WrongThread, "Run modification reads require the verified main thread.");
        try { context.MainThread.VerifyAccess(); }
        catch (ObjectDisposedException ex) { return Fail(ErrorCode.ScopeExpired, ex.Message); }
        if (_disposed || context is IScopedPluginContext scope && scope.Lifetime.IsCancellationRequested) return Fail(ErrorCode.ScopeExpired, "Run modification scope expired.");
        if (_processing) return Fail(ErrorCode.Conflict, "Journal is processing an observation; synchronous read reentry is refused.");
        query ??= new();
        if (query.MaximumEntries < 1 || query.MaximumEntries > _limits.MaximumQueryEntries || (query.RunId is not null && !Token(query.RunId, 128)) ||
            (query.Owner is not null && !Token(query.Owner, 128)) || (query.Source is not null && !Token(query.Source, 128))) return Fail(ErrorCode.InvalidArgument, "Invalid journal filter or bounded query limit.");
        _processing = true;
        try
        {
            Refresh();
            var all = _runs.Values.SelectMany(x => x.Entries).Concat(_unassociated).Where(x => (query.RunId is null || x.RunId == query.RunId) &&
                (query.Owner is null || x.Owner == query.Owner) && (query.Source is null || x.Source == query.Source)).OrderBy(x => x.Sequence).ToArray();
            var records = all.Skip(Math.Max(0, all.Length - query.MaximumEntries)).ToArray();
            var runs = _runOrder.Select(id => _runs[id]).Select(x => new RunModificationRunInfo(_snapshot.SessionId, x.Id, x.Chart, x.Start, x.Last, x.End, x.Reason, x.Closed, x.Entries.Count, x.Evicted)).ToArray();
            var effects = _effects.Values.Select(x => new RunModificationActiveEffect(x.Id, x.Input.Owner, x.Input.Source, x.Input.Operation, x.Input.TargetId, x.Input.Category,
                x.Input.Detail, x.ActivatedRevision, x.Input.LocalResultBehaviorVerified, x.Input.SubmissionBehaviorVerified)).ToArray();
            return Result<RunModificationJournalSnapshot>.Ok(new(_revision, Copy(_snapshot), Array.AsReadOnly(records), Array.AsReadOnly(runs), Array.AsReadOnly(effects), Counters, all.Length > records.Length));
        }
        catch (Exception ex) { Failure(ex); return Fail(ErrorCode.InternalError, "Journal snapshot failed: " + ex.Message); }
        finally { Exit(); }
    }
    private static Result<RunModificationJournalSnapshot> Fail(ErrorCode code, string message) => Result<RunModificationJournalSnapshot>.Fail(new(code, message, RunModificationCapabilityIds.Read));
    private void Log(string message) { try { _log.Log(LogLevel.Debug, message); } catch { } }
    private void Failure(Exception ex) { _internalFailures++; try { _log.Log(LogLevel.Warning, "Run modification journal rejected an internal audit failure: " + ex.Message); } catch { } }
    public void Dispose()
    {
        _main.VerifyAccess(); if (_disposed) return; _disposed = true;
        try { _subscription?.Dispose(); } finally { _subscription = null; _effects.Clear(); _retiredEffects.Clear(); _deferredRunEffectReleases.Clear(); }
    }
    private sealed class EffectLease : IDisposable
    {
        private RunModificationJournal? _owner; private readonly string _id;
        internal EffectLease(RunModificationJournal owner, string id) => (_owner, _id) = (owner, id);
        public void Dispose() { var owner = _owner; if (owner is null) return; if (owner.ReleaseEffect(_id)) _owner = null; }
    }
    private sealed class EmptyEffect : IDisposable { internal static EmptyEffect Instance { get; } = new(); public void Dispose() { } }
    private sealed class View : IRunModificationService, IDisposable
    {
        private readonly RunModificationJournal _owner; private readonly IPluginContext _context; private bool _disposed;
        internal View(RunModificationJournal owner, IPluginContext context) => (_owner, _context) = (owner, context);
        public Result<RunModificationJournalSnapshot> Read(RunModificationQuery? query = null) => _disposed ? Fail(ErrorCode.ScopeExpired, "Run modification view expired.") : _owner.Read(_context, query);
        public void Dispose() { _owner._main.VerifyAccess(); _disposed = true; }
    }
}
