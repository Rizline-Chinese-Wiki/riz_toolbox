using RizToolbox.Adapter.AudioContent;
using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;
using RizToolbox.Runtime.Hooks;
using RizToolbox.Sdk;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    private SeekHoldEpoch? _seekHoldEpoch;
    internal SeekHoldEpoch PrepareSeekHoldEpoch(JudgementModule.SeekBackendAccess observer, JudgementRunCapture run,
        NativeSeekTargetStatePlan target, ISeekTargetNativeProfile profile)
    {
        HoldLocal(); observer.VerifyDrained(); target.VerifySource();
        if (!ReferenceEquals(observer.Backend, this) || run is not RunValue actual || _seekHoldEpoch is not null)
            throw new InvalidOperationException("Hold Seek requires the original installed backend, its actual run, and one registered epoch.");
        actual.VerifySeekEpochOwner(this);
        if (!actual.Run.SameGeneration(target.Run)) throw new InvalidOperationException("The Hold target belongs to another run.");
        var owned = CaptureRun() as RunValue ?? throw new InvalidOperationException("No independent actual Hold run could be retained.");
        SeekHoldEpoch? epoch = null;
        try
        {
            epoch = new(this, observer, owned, target, profile); _seekHoldEpoch = epoch;
            epoch.Prepare(); return epoch;
        }
        catch (Exception error)
        {
            try { if (epoch is null) owned.Dispose(); else epoch.Dispose(); }
            catch (Exception cleanup) { error.Data["SeekHoldEpochOwner"] = epoch; error.Data["SeekHoldEpochCleanupFailure"] = cleanup; }
            throw;
        }
    }
    private sealed partial class RunValue
    {
        internal void VerifySeekEpochOwner(NativeJudgementBackend owner)
        {
            Controller.VerifyOwnerThread();
            if (_disposed || !ReferenceEquals(_owner, owner) || owner._seekDisposed)
                throw new ObjectDisposedException(nameof(JudgementRunCapture));
        }
    }

    internal sealed partial class SeekHoldEpoch : IDisposable
    {
        private static readonly HashSet<SeekHoldEpoch> Responsibilities = new();
        private static readonly object ResponsibilityGate = new();
        private readonly SeekHoldEpoch _registered;
        private readonly NativeJudgementBackend _owner;
        private readonly JudgementModule.SeekBackendAccess _observer;
        private readonly RunValue _run;
        private readonly NativeSeekTargetStatePlan _target;
        private readonly ISeekTargetNativeProfile _sourceProfile;
        private readonly Func<NativeRuntime, ISeekTargetNativeProfile, JudgementModule.SeekBackendAccess, ISeekHoldEpochProfile> _prepareProfile;
        private ISeekHoldEpochProfile _profile = null!;
        private readonly HoldRoots _roots;
        private NativeSeekNoteFlagsAccess? _fingers;
        private NativeRuntime.RootHandle _judge = null!;
        private readonly Dictionary<long, NativeRuntime.RootHandle> _objects = new();
        private readonly Dictionary<long, SeekFingerIdsListStorage> _fingerBaseline = new();
        private SeekFingerIdsFieldSnapshot _fingerFields = null!;
        private readonly List<Iterator> _original = new(), _targets = new();
        private readonly Dictionary<long, bool> _directions = new();
        private readonly List<Exception> _cleanup = new();
        private HoldStopObservation? _stopObservation;
        private HoldStartObservation? _startObservation;
        private long _startingEnumerator;
        private bool _callingStop, _callingStart, _stopProven, _restoringCall;
        private bool _prepared, _stopped, _targetStarted, _targetStopped, _restored, _unknown, _disposed, _busy, _released;
        private bool _effectsEntered, _targetConfirmed, _timeWritten, _stopReturned;
        private bool _targetStartAttempted;
        private int _unconfirmedEffects;
        private int _originalCount, _expectedCount;
        private float _originalJudgeTime;
        private bool _autoPlay, _freeHold;
        private string? _failure;
        internal ObservedJudgementRun Run => _run.Run;
        internal long JudgeAddress => JudgeIdentity.NoteJudgeControl;
        internal SeekTargetSourceIdentity JudgeIdentity { get; }
        internal IReadOnlyList<SeekFingerIdsTarget> FingerTargets { get; private set; } = Array.Empty<SeekFingerIdsTarget>();
        internal IReadOnlyList<long> RequiresContinuation { get; private set; } = Array.Empty<long>();
        internal bool SideEffectsUnknown => _unknown;
        internal string? Failure => _failure;
        // This object proves controlled API invocation and physical observer drain,
        // not hidden engine Detach completion or historical input equivalence.
        internal bool EngineCancellationProven => false;
        private sealed record IteratorState(int State, long Current, long Owner, long Note, bool Left,
            bool NormalPerfect, int StartBits, int NextParticleBits);
        private sealed class Iterator
        {
            internal readonly HoldCoroutineLifecycle.Record Record;
            internal readonly NativeRuntime.RootHandle Root;
            internal readonly IteratorState Original;
            internal IteratorState Current;
            internal Iterator(HoldCoroutineLifecycle.Record record, NativeRuntime.RootHandle root, IteratorState value)
                => (Record, Root, Original, Current) = (record, root, value, value);
        }
        internal SeekHoldEpoch(NativeJudgementBackend owner, JudgementModule.SeekBackendAccess observer, JudgementRunCapture run,
            NativeSeekTargetStatePlan target, ISeekTargetNativeProfile profile,
            Func<NativeRuntime, ISeekTargetNativeProfile, JudgementModule.SeekBackendAccess, ISeekHoldEpochProfile>? prepareProfile = null)
        {
            (_registered, _owner, _observer, _run, _target, _sourceProfile, JudgeIdentity)
                = (this, owner, observer, run as RunValue ?? throw new InvalidOperationException("The actual native Hold run is required."), target, profile, target.Identity);
            _prepareProfile = prepareProfile ?? ((api, source, access) => new NativeSeekHoldEpochProfile(api, source, access));
            _roots = new(owner._holdMain!, checked(8 + 6 * NativeSeekRuntimeNoteView.MaximumNotes));
        }
        private void Local()
        {
            _owner.HoldLocal();
            if (_disposed || !ReferenceEquals(_registered, this) || !ReferenceEquals(_owner._seekHoldEpoch, this))
                throw new InvalidOperationException("The original Hold Seek epoch is no longer registered.");
            _run.VerifySeekEpochOwner(_owner); _roots.VerifyOpen();
            if (!_owner._state.MatchesCurrentGameplayRun(Run.Information, Run.Controller, Run.RunId, Run.SceneGeneration))
                throw new InvalidOperationException("The original Hold Seek run expired.");
        }
        private void Source()
        {
            Local(); _observer.VerifyCurrent(); _fingers?.VerifySource();
            if (!ReferenceEquals(_observer.Backend, _owner) || _owner.HoldLifecycle.Snapshot().Unknown)
                throw new InvalidOperationException("The original Hold observer has unknown responsibility.");
            if (_judge is not null && (Target(_judge).ToInt64() != JudgeAddress
                || Reference(_judge, _sourceProfile.JudgeClass, "levelInformation", "InLevel.LevelInformation", 0x20) != Run.Information))
                throw new InvalidOperationException("The actual Hold judgement owner changed its run association.");
            Local();
        }
        private T Operation<T>(Func<T> operation, bool recoverKnownTargetFailure = false)
        {
            Local(); if (_busy) throw new InvalidOperationException("The original Hold Seek epoch cannot reenter.");
            _busy = true;
            try { var result = operation(); Local(); return result; }
            catch (Exception error)
            {
                _failure ??= error.Message;
                if (_effectsEntered || _stopped || _callingStop || _callingStart || _targets.Count != 0)
                {
                    // Only target construction may fail at a known boundary
                    // after prior successful Stop/Start phases. A dispatched
                    // effect stays pending until its exact complete readback.
                    // Physical faults and unknown temporary cleanup never take
                    // this path, and recovery still independently checks every
                    // retained record, input, counter and source before Stop.
                    if (!recoverKnownTargetFailure || _unconfirmedEffects != 0 || _unknown
                        || _targetStarted && !_targetConfirmed
                        || error.Data.Contains("NativePublicationCleanupOwner")) _unknown = true;
                    Retain(); error.Data["SeekHoldEpochOwner"] = this;
                }
                throw;
            }
            finally { _busy = false; if (_disposed) Release(); }
        }
        private void Retain() { lock (ResponsibilityGate) Responsibilities.Add(this); }
        private void EnterEffect()
        {
            Local(); _unconfirmedEffects = checked(_unconfirmedEffects + 1); _effectsEntered = true;
        }
        private void ConfirmEffect()
        {
            Local();
            if (_unconfirmedEffects <= 0) throw new InvalidOperationException("No actual Hold effect is awaiting its exact readback.");
            --_unconfirmedEffects;
        }
        private IntPtr Target(NativeRuntime.RootHandle root) { Local(); var value = _roots.Target(root); Local(); return value; }
        private NativeRuntime.RootHandle Adopt(IntPtr address, IntPtr? cls = null)
        {
            Local(); var root = _roots.Adopt(_owner._api, address); _objects[address.ToInt64()] = root; Local();
            if (cls is IntPtr expected && _owner._api.ObjectClass(Target(root)) != expected)
                throw new UnsupportedSignatureException("A retained Hold epoch receiver has another exact class.");
            return root;
        }
        private T Value<T>(NativeRuntime.RootHandle root, IntPtr cls, string name, string type, int offset) where T : struct
        {
            Local(); var field = _owner._api.InspectExactDeclaredReadField(cls, name, type, false, offset); Local();
            var result = _owner._api.ReadExactField<T>(root, field); Local(); return result;
        }
        private void Write<T>(NativeRuntime.RootHandle root, IntPtr cls, string name, string type, int offset, T expected, T value) where T : struct
        {
            if (!EqualityComparer<T>.Default.Equals(Value<T>(root, cls, name, type, offset), expected))
                throw new InvalidOperationException("A selected Hold epoch field changed before its conditional write: " + name);
            using var argument = A.Value(value);
            _owner._api.WriteExactGuarded(root, cls, name, type, argument, EnterEffect);
            if (!EqualityComparer<T>.Default.Equals(Value<T>(root, cls, name, type, offset), value))
                throw new InvalidOperationException("A selected Hold epoch write did not read back: " + name);
            ConfirmEffect();
        }
        private int Count() => Value<int>(_judge, _sourceProfile.JudgeClass, "holdOnJudgeCount", "System.Int32", 0x50);
        private void SetCount(int expected, int next)
        {
            Write(_judge, _sourceProfile.JudgeClass, "holdOnJudgeCount", "System.Int32", 0x50, expected, next);
            _expectedCount = next;
        }
        internal int ReadOwnedJudgeCount()
        {
            Source(); var count = Count();
            if (!_prepared || count != _expectedCount) throw new InvalidOperationException("The actual Hold counter differs from this private epoch phase.");
            return count;
        }
        internal void VerifyLifecycle(HoldCoroutineLifecycle lifecycle)
        { Local(); if (!ReferenceEquals(_owner.HoldLifecycle, lifecycle)) throw new InvalidOperationException("Foreign Hold lifecycle."); }
        internal void VerifyStopCompleted(HoldCoroutineLifecycle lifecycle)
        {
            VerifyLifecycle(lifecycle);
            if (!_stopProven || _stopObservation is not { Completed: true, Disposed: true })
                throw new InvalidOperationException("No actual completed Stop observation belongs to this epoch.");
        }
        internal void VerifyRestart(HoldCoroutineLifecycle lifecycle, HoldCoroutineLifecycle.Record record)
        {
            VerifyStopCompleted(lifecycle);
            if (!_restoringCall || !_original.Any(item => ReferenceEquals(item.Record, record)))
                throw new InvalidOperationException("Only this original stopped record may be restored by its current private call.");
        }
        public void Dispose()
        {
            _owner._holdMain!.VerifyAccess();
            if (!ReferenceEquals(_registered, this)) throw new InvalidOperationException("A copied Hold epoch cannot release the original roots.");
            _disposed = true; if (!_busy) Release();
        }
        private void Release()
        {
            if (_released) return;
            if (_unknown || _effectsEntered && !_restored && !(_targetConfirmed && !_targetStopped))
            { Retain(); throw new InvalidOperationException("The original Hold epoch retains unresolved Stop/Start responsibility."); }
            _released = true;
            try { _fingers?.Dispose(); } catch (Exception error) { _cleanup.Add(error); }
            try { _roots.Dispose(); } catch (Exception error) { _cleanup.Add(error); }
            try { _run.Dispose(); } catch (Exception error) { _cleanup.Add(error); }
            if (_cleanup.Count != 0) { Retain(); throw new AggregateException("Hold epoch native-root cleanup is unknown without retry.", _cleanup); }
            if (ReferenceEquals(_owner._seekHoldEpoch, this)) _owner._seekHoldEpoch = null;
            lock (ResponsibilityGate) Responsibilities.Remove(this);
        }
    }
}
