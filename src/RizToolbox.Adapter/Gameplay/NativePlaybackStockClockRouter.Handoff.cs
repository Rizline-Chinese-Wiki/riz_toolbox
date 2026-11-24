using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativePlaybackStockClockRouter
{
    private Handoff? _handoff;
    private NativePlaybackSingleReturnBridge.LogicalHandoff? _admitted;
    internal Handoff BeginHandoff(Session predecessor)
    {
        Open(); _group!.VerifyCurrent();
        if (!ReferenceEquals(_session, predecessor) || _handoff is not null || InitializationPending)
            throw new GameplayStateException(ErrorCode.TargetExpired, "Only the registered exact session can begin this physical group's logical handoff.");
        var native = predecessor.BeginOwnHandoff();
        predecessor.Withdraw("The exact logical session entered native joint handoff.");
        _handoff = Handoff.Issue(this, predecessor, native); return _handoff;
    }

    internal sealed partial class Session
    {
        internal string HandoffRunId => _initial.Binding.RunId;
        internal NativePlaybackSingleReturnBridge.LogicalHandoff BeginOwnHandoff()
        {
            _owner.Open();
            // A shallow copy retains the real publication object but does not
            // own this exact route registration. A legitimately withdrawn run
            // may still transfer its exact inactive retained publication, so
            // this gate deliberately does not require !_withdrawn.
            if (!ReferenceEquals(_owner._session, this) || !_armed || _finalClock is null
                || _publication is null || _owner._handoff is not null || _owner.InitializationPending)
                throw new GameplayStateException(ErrorCode.TargetExpired, "Only this registered formerly armed session owns its retained publication handoff.");
            _owner._group!.VerifyCurrent();
            return _publication.BeginHandoff();
        }
    }

    internal sealed partial class Handoff
    {
        private readonly NativePlaybackStockClockRouter _owner;
        private readonly Session _predecessor;
        private readonly NativePlaybackSingleReturnBridge.LogicalHandoff _native;
        private bool _consumed;
        private NativePlaybackStockRootedSource? _successor;
        private NativePlaybackBinding? _admittedGraph;
        private GameEventState.PlaybackRunAssociation? _admittedRun;
        private long _admittedPause;
        private Handoff(NativePlaybackStockClockRouter owner, Session predecessor, NativePlaybackSingleReturnBridge.LogicalHandoff native)
            => (_owner, _predecessor, _native) = (owner, predecessor, native);
        internal static Handoff Issue(NativePlaybackStockClockRouter owner, Session predecessor, NativePlaybackSingleReturnBridge.LogicalHandoff native)
            => new(owner, predecessor, native);
        private void Registered()
        {
            _owner.Open();
            if (!ReferenceEquals(_owner._handoff, this) || _consumed
                || (_successor is null ? !ReferenceEquals(_owner._session, _predecessor)
                    : !ReferenceEquals(_owner._source, _successor) || _owner._session is not null))
                throw new GameplayStateException(ErrorCode.TargetExpired, "A copied, consumed, or superseded route handoff has no session authority.");
            _owner._group!.VerifyCurrent();
        }
        internal NativePlaybackSingleReturnBridge.HandoffInformation Read()
        { Registered(); return _native.Read(); }
        internal bool TryAdmit(GameEventState.PlaybackRunAssociation run, NativePlaybackBinding graph, long pause)
        {
            Registered(); run.VerifyCurrent(graph);
            if (graph.RunId == _predecessor.HandoffRunId || pause == 0)
                throw new GameplayStateException(ErrorCode.InvalidArgument, "A successor requires a different host-issued run and an independent pause root.");
            if (_successor is not null && (!ReferenceEquals(_admittedRun, run) || _admittedGraph != graph || _admittedPause != pause))
                throw new GameplayStateException(ErrorCode.TargetExpired, "A pending source-open cannot change its registered successor graph.");
            if (_successor is null && _native.Read().Phase == 1 && !_native.TryAdmit()) return false;
            try
            {
                if (_successor is null)
                {
                    // This cut drains all pre-withdrawal policy/observer borrowers.
                    // The source gate stays closed throughout graph construction and
                    // predecessor retirement; inactive GetTime forwards only raw stock.
                    var info = _native.Read(2);
                    if (info.NewEpoch > long.MaxValue) throw new InvalidOperationException("Logical source generation overflow.");
                    var binding = graph with { SourceGeneration = (long)info.NewEpoch };
                    run.VerifyCurrent(binding);
                    var origin = new NativePlaybackRateOrigin(binding.RunId, binding.ControllerGeneration,
                        binding.Controller, binding.Information, binding.Progress, pause);
                    var route = new object();
                    _successor = _owner.CreateSource(binding, origin, run, route);
                    _admittedGraph = graph; _admittedRun = run; _admittedPause = pause;
                    _owner.RetireFreshPredecessor(_native);
                    _owner._source!.Dispose(); _owner._source.PumpCompleted();
                    if (!_owner._source.RootsReleased && !_owner._retainedSources.Contains(_owner._source))
                        _owner._retainedSources.Add(_owner._source);
                    _owner._logicalObservations.Clear();
                    _owner._session = null; _owner._binding = binding; _owner._origin = origin; _owner._run = run;
                    _owner._admitted = _native; _owner._callProfile = null;
                    _owner._source = _successor; _owner._sourceRoute = route;
                }
                // A newly entered raw original may make this conditional open Busy.
                // The same registered graph may retry; no baseline or Clock exists yet.
                if (!_native.TryOpenSource()) return false;
                _successor.VerifyPlayerWatchActivated();
                _owner.RegisterFormalSuccessor(_native, _successor);
                _consumed = true; _owner._handoff = null; return true;
            }
            catch (Exception error)
            {
                // A receipt that consumed native admission can never restart the old
                // logical source or infer a fresh grant after partial construction.
                _consumed = true; _owner._sourceRoute = null;
                _owner._transferFailure = "The admitted logical successor failed closed: " + error.Message;
                if (_successor is not null && !ReferenceEquals(_owner._source, _successor)) _owner._retainedSources.Add(_successor);
                throw;
            }
        }
    }
}
