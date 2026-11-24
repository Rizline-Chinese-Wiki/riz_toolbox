using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class JudgementModule
{
    private readonly object _seekBackendIssuer = new();
    internal SeekBackendAccess CaptureSeekBackend(GameEventState.PlaybackRunAssociation run)
    {
        _main.VerifyAccess(); ArgumentNullException.ThrowIfNull(run); run.VerifyHostCurrent();
        if (!ReferenceEquals(_state.CaptureCurrentPlaybackRun(), run) || _backend is not { } backend
            || _coordinator is null || _hold is not { } move || _holdStart is not { } start)
            throw new GameplayStateException(ErrorCode.NotReady, "Seek requires the installed judgement and Hold observers for this song.");
        if (_holdStops.Any(binding => binding is null))
            throw new GameplayStateException(ErrorCode.NotReady, "Seek requires all six installed Hold Stop observers.");
        var result = SeekBackendAccess.Issue(this, backend, run, _generation, move, start,
            _holdStops.Select(binding => binding!).ToArray(), _seekBackendIssuer);
        result.VerifyCurrent(); return result;
    }

    /// <summary>Borrow the actual installed observer backend. This is not a new
    /// lifecycle, a historical coverage claim, or ownership of the shared backend.</summary>
    internal sealed class SeekBackendAccess
    {
        private readonly JudgementModule _owner;
        private readonly NativeJudgementBackend _backend;
        private readonly GameEventState.PlaybackRunAssociation _run;
        private readonly long _generation;
        private readonly ExactHookBroker.Binding _move, _start;
        private readonly ExactHookBroker.Binding[] _stops;
        private readonly SeekBackendAccess _registered;
        private SeekBackendAccess(JudgementModule owner, NativeJudgementBackend backend,
            GameEventState.PlaybackRunAssociation run, long generation, ExactHookBroker.Binding move, ExactHookBroker.Binding start,
            ExactHookBroker.Binding[] stops)
            => (_owner, _backend, _run, _generation, _move, _start, _stops, _registered)
                = (owner, backend, run, generation, move, start, stops, this);
        internal static SeekBackendAccess Issue(JudgementModule owner, NativeJudgementBackend backend,
            GameEventState.PlaybackRunAssociation run, long generation, ExactHookBroker.Binding move, ExactHookBroker.Binding start,
            ExactHookBroker.Binding[] stops, object issuer)
        {
            if (!ReferenceEquals(owner._seekBackendIssuer, issuer))
                throw new GameplayStateException(ErrorCode.TargetExpired, "Only the installed judgement module can issue its Seek observer access.");
            return new(owner, backend, run, generation, move, start, stops);
        }
        internal NativeJudgementBackend Backend { get { VerifyCurrent(); return _backend; } }
        internal ExactHookBroker.Binding StartCoroutineBinding { get { VerifyCurrent(); return _start; } }
        internal ExactHookBroker.Binding HoldMoveBinding { get { VerifyCurrent(); return _move; } }
        internal ExactHookBroker.Binding StopAllBinding { get { VerifyCurrent(); return _stops[(int)CoroutineStopOperation.All]; } }
        internal void VerifyDrained()
        {
            VerifyCurrent();
            foreach (var binding in _stops.Concat(new[] { _move, _start }))
            {
                var activity = binding.ReadActivity();
                if (!activity.Known || activity.InFlight != 0)
                    throw new GameplayStateException(ErrorCode.NotReady, "The original Hold Start/Move/Stop physical callbacks have not drained.");
            }
        }
        internal void VerifyCurrent()
        {
            _owner._main.VerifyAccess();
            if (!ReferenceEquals(_registered, this) || _owner._generation != _generation || _owner._coordinator is null
                || !ReferenceEquals(_owner._backend, _backend) || !ReferenceEquals(_owner._hold, _move)
                || !ReferenceEquals(_owner._holdStart, _start) || _move.IsDisposed || _start.IsDisposed)
                throw new GameplayStateException(ErrorCode.TargetExpired, "The original installed judgement/Hold observer was detached or replaced.");
            var stops = _backend.ReadHoldStops();
            if (!stops.Installed || stops.Failure is not null || _stops.Length != 6
                || _stops.Where((binding, index) => !ReferenceEquals(_owner._holdStops[index], binding)
                    || binding.IsDisposed || !binding.ReadActivity().Known).Any())
                throw new GameplayStateException(ErrorCode.NotReady, "The original complete Hold Stop observation is unavailable: " + stops.Failure);
            _run.VerifyHostCurrent();
            if (!ReferenceEquals(_owner._state.CaptureCurrentPlaybackRun(), _run)
                || !_move.ReadActivity().Known || !_start.ReadActivity().Known || !_backend.HoldLifecycle.Accepting)
                throw new GameplayStateException(ErrorCode.NotReady, "The current song's original Hold observer is unavailable.");
        }
    }
}
