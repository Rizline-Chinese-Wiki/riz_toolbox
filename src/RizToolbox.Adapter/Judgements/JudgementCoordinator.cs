using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Judgements;

/// <summary>One run registry and synchronous caller scope per adapter generation.
/// Native addresses remain internal and only rooted immutable identities reach observers.</summary>
internal sealed class JudgementCoordinator : IDisposable
{
    private readonly IJudgementBackend _backend;
    private readonly IMainThreadDispatcher _main;
    private readonly ILogger _log;
    private readonly Func<int> _depth;
    private readonly Func<string?> _observedRunId;
    private readonly Action<GameplayJudgementObservation> _publish;
    private readonly ulong _module;
    private readonly Dictionary<string,long> _sources = JudgementSourceResolver.Sites.ToDictionary(x => x.Id,_ => 0L);
    private RuntimeNoteRegistry? _registry;
    private JudgementRunCapture? _run;
    private OuterScope? _current;
    private string? _blockedRunId, _reason;
    private bool _disposed;
    private long _observations, _unassociated;
    internal JudgementCoordinator(IJudgementBackend backend,IMainThreadDispatcher main,ILogger log,
        ulong module,Func<int> depth,Func<string?> observedRunId,Action<GameplayJudgementObservation> publish)
        => (_backend,_main,_log,_module,_depth,_observedRunId,_publish) = (backend,main,log,module,depth,observedRunId,publish);
    internal JudgementObserverStatus Status()
    {
        _main.VerifyAccess();
        return new(!_disposed,_registry is { IsRetired:false },_run?.Run.RunId,_observations,_unassociated,
            Array.AsReadOnly(JudgementSourceResolver.Sites.Select(x => new JudgementSourceStatus(x.Id,x.DeltaKind,_sources[x.Id])).ToArray()),_reason);
    }
    internal void Tick()
    {
        _main.VerifyAccess(); if (_disposed) return;
        if (_run is not null)
        {
            var current = _backend.CurrentRun(_run);
            if (current is null || !_run.Run.SameGeneration(current)) Retire("Observed run or chart identity changed.");
        }
        if (_observedRunId() != _blockedRunId) _blockedRunId = null;
    }
    private ObservedJudgementRun? EnsureRun()
    {
        Tick();
        if (_blockedRunId is not null && _blockedRunId == _observedRunId()) return null;
        if (_run is null)
        {
            var run = _backend.CaptureRun();
            if (run is null) { _reason = "No matching observed normal-mode run is available."; return null; }
            _registry = new(_main,run.Run,run,run.Graph,RuntimeNoteRegistry.MaximumNotes,error => Log("Judgement root cleanup failed.",error));
            _run = run; _reason = null;
        }
        return _backend.CurrentRun(_run);
    }
    internal OuterScope? CaptureOuter(NativeJudgeFrame frame)
    {
        _main.VerifyAccess(); if (_disposed) return null;
        JudgementNoteCapture? source = null; RuntimeNoteRegistry.InvocationLease? lease = null;
        try
        {
            var run = EnsureRun();
            if (run is null) { _unassociated++; return null; }
            var site = JudgementSourceResolver.Find(frame.ReturnAddress,_module);
            if (site is null) throw new InvalidOperationException("A judgement caller is outside the pinned twelve-source profile.");
            source = _backend.CaptureNote(_run!,site,frame);
            var acquired = _registry!.Acquire(run,source.Note,source.Data,() => _backend.RetainNote(source.Note));
            if (!acquired.Success) throw new InvalidOperationException(acquired.Error?.Message ?? "Runtime note identity is unavailable.");
            lease = acquired.Value!;
            var scope = new OuterScope(this,_current,_run!,lease,source,site,_depth());
            _current = scope; source = null; lease = null; _sources[site.Id]++;
            return scope;
        }
        catch (Exception error) { _unassociated++; WithdrawRun(error); return null; }
        finally { try { source?.Dispose(); } finally { lease?.Dispose(); } }
    }
    internal InnerObservation CaptureInner(IntPtr receiver,int judge,float delta)
    {
        _main.VerifyAccess();
        var scope = _current;
        if (_disposed || scope is null || scope.Depth != _depth()) { _unassociated++; return new(); }
        JudgementInnerCapture? capture = null;
        try
        {
            // Scope depth is checked before native metadata. In particular, an
            // unrecognized/suppressed nested outer call cannot borrow this note.
            capture = _backend.CaptureInner(scope.Run,receiver);
            var before = capture.ReadCounters();
            var result = new InnerObservation(scope,capture,judge,delta,before);
            capture = null; return result;
        }
        catch (Exception error) { _unassociated++; WithdrawRun(error); return new(); }
        finally { capture?.Dispose(); }
    }
    internal void CompleteInner(InnerObservation observation,long invocation)
    {
        _main.VerifyAccess();
        if (_disposed || observation.Scope is not { } scope || observation.Capture is not { } capture) return;
        GameplayJudgementCounters after;
        try { after = capture.ReadCounters(); }
        catch (Exception error) { _unassociated++; WithdrawRun(error); return; }
        // The original may close the run or withdraw the registry. Its retained
        // entry origin remains authoritative for this final invocation.
        var identity = scope.Lease.Identity; var origin = scope.Lease.CapturedRun; var location = scope.Lease.ChartLocation;
        var dto = new GameplayJudgementObservation(invocation,observation.Judge,observation.Delta,
            $"{identity.SessionId}/{identity.RunId}/{identity.Sequence}", "retained-runtime-note",GameplayClockUnit.RawGameUnits,
            "Exact caller source and rooted note/graph; actual NormalMode original returned. Measured sources retain raw units pending runtime time calibration; counter snapshots do not assert score acceptance.")
        {
            RuntimeNote = new(identity.SessionId,identity.RunId,identity.Sequence),
            ChartNote = location is null ? null : new(location.LineOrdinal,location.NoteOrdinal),
            ChartAssociation = (JudgementNoteAssociation)scope.Lease.GraphAssociation,
            DeltaKind = scope.Site.DeltaKind, SourceId = scope.Site.Id,
            SceneGeneration = origin.SceneGeneration, LifecycleRevisionAtEntry = origin.Revision,
            OriginalReturned = true, CountersBefore = observation.Before, CountersAfter = after
        };
        _observations++; _publish(dto);
    }
    private void WithdrawRun(Exception error)
    {
        var key = _run?.Run.RunId ?? _observedRunId();
        var changed = _blockedRunId != key || _reason != error.Message;
        _blockedRunId = key; _reason = error.Message;
        Retire(_reason);
        if (changed) Log("Judgement identity coverage withdrawn for this run: " + _reason,error);
    }
    private void Retire(string reason)
    {
        var registry = _registry; _registry = null; _run = null;
        registry?.Retire(reason);
    }
    private void Log(string message,Exception error) { try { _log.Log(LogLevel.Warning,message,error); } catch { } }
    public void Dispose() { _main.VerifyAccess(); if (_disposed) return; _disposed = true; Retire("Judgement provider was withdrawn."); }

    internal sealed class OuterScope : IDisposable
    {
        private JudgementCoordinator? _owner;
        private readonly OuterScope? _previous;
        private readonly JudgementNoteCapture _source;
        internal JudgementRunCapture Run { get; }
        internal RuntimeNoteRegistry.InvocationLease Lease { get; }
        internal JudgementCallSite Site { get; }
        internal int Depth { get; }
        internal OuterScope(JudgementCoordinator owner,OuterScope? previous,JudgementRunCapture run,
            RuntimeNoteRegistry.InvocationLease lease,JudgementNoteCapture source,JudgementCallSite site,int depth)
            => (_owner,_previous,Run,Lease,_source,Site,Depth) = (owner,previous,run,lease,source,site,depth);
        public void Dispose()
        {
            var owner = _owner; if (owner is null) return; owner._main.VerifyAccess(); _owner = null;
            if (ReferenceEquals(owner._current,this)) owner._current = _previous;
            try { _source.Dispose(); } finally { Lease.Dispose(); }
        }
    }
    internal sealed class InnerObservation : IDisposable
    {
        internal OuterScope? Scope { get; }
        internal JudgementInnerCapture? Capture { get; private set; }
        internal int Judge { get; }
        internal float Delta { get; }
        internal GameplayJudgementCounters? Before { get; }
        internal InnerObservation() { }
        internal InnerObservation(OuterScope scope,JudgementInnerCapture capture,int judge,float delta,GameplayJudgementCounters before)
            => (Scope,Capture,Judge,Delta,Before) = (scope,capture,judge,delta,before);
        public void Dispose() { var capture = Capture; Capture = null; capture?.Dispose(); }
    }
}
