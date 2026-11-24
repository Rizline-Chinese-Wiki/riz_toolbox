using RizToolbox.Runtime;
using A = RizToolbox.Runtime.NativeRuntime.Arg;

namespace RizToolbox.Adapter.Judgements;

internal sealed partial class NativeJudgementBackend
{
    internal sealed partial class SeekHoldEpoch
    {
        internal void StopPredecessors() => Operation(() =>
        {
            VerifyPreparedCore(); StopActual(_original); _stopped = true;
            VerifyPredecessorsStoppedCore(); return 0;
        });
        private void StopActual(IReadOnlyList<Iterator> records)
        {
            Source(); _observer.VerifyDrained(); _profile.VerifyCurrent(); VerifyModes();
            VerifyRecords(records, stopped: false);
            if (Count() != _expectedCount) throw new InvalidOperationException("The Hold count changed before its actual Stop.");
            var inputs = ReadInputs(); var states = records.Select(item => ReadIterator(item.Root)).ToArray();
            _stopObservation = null; _stopProven = false; _stopReturned = false; _callingStop = true;
            try
            {
                _owner._api.InvokeVoidWithPublicationGuarded(_profile.StopMethod, Target(_judge), EnterEffect);
            }
            finally { _callingStop = false; }
            _observer.VerifyDrained(); Source();
            if (_unknown || !_stopReturned || _stopObservation is not { Completed: true, Disposed: true })
                throw new InvalidOperationException("The exact native Hold Stop has no normal returned, physically drained observation.");
            VerifyInputs(inputs);
            if (Count() != _expectedCount) throw new InvalidOperationException("The actual Stop changed the Hold count outside its known empty Dispose path.");
            for (var i = 0; i < records.Count; ++i)
            {
                var actual = ReadIterator(records[i].Root);
                if (actual != states[i]) throw new InvalidOperationException("The actual Stop changed a retained iterator outside its known empty Dispose path.");
                records[i].Current = actual;
            }
            _stopProven = true;
            _owner.HoldLifecycle.MarkSeekStopped(this, records.Select(item => item.Record).ToArray());
            SetCount(_expectedCount, 0); VerifyRecords(records, stopped: true); ConfirmEffect();
        }
        private void VerifyPredecessorsStoppedCore()
        {
            Source(); _observer.VerifyDrained(); VerifyStopCompleted(_owner.HoldLifecycle);
            if (!_stopped || _targetStarted || _targets.Count != 0 || _restored || _unknown || Count() != 0 || _expectedCount != 0)
                throw new InvalidOperationException("The exact predecessor Stop phase is not current.");
            VerifyRecords(_original, stopped: true);
        }
        internal void VerifyPredecessorsStopped() => Operation(() => { VerifyPredecessorsStoppedCore(); return 0; });
        internal void StartTargetHolds() => Operation(() =>
        {
            if (_targetStartAttempted) throw new InvalidOperationException("Target Hold construction is once-only; a failed attempt may only recover.");
            _targetStartAttempted = true;
            VerifyPredecessorsStoppedCore(); _profile.VerifyCurrent(); VerifyModes(); VerifyTargetFingers();
            if (_targetStopped) throw new InvalidOperationException("The target Hold phase was already cancelled.");
            if (!SameTime(JudgeTime(), _originalJudgeTime)) throw new InvalidOperationException("The original Judge time cache changed before target scheduling.");
            Write(_judge, _sourceProfile.JudgeClass, "_nowTime", "System.Single", 0x64, _originalJudgeTime, _target.Coordinates.SyncedSeconds);
            _timeWritten = true;
            foreach (var note in RequiresContinuation)
            {
                Source(); _observer.VerifyDrained(); VerifyTargetFingers();
                var inputs = ReadInputs(); var previousCount = _expectedCount;
                NativeRuntime.RootHandle? iterator = null;
                using var noteArgument = A.Ref(Target(_objects[note]));
                using var direction = A.Value(_directions[note]);
                _owner._api.InvokeReferenceWithPublicationAdmission(_profile.FactoryMethod, Target(_judge),
                    pointer => iterator = Adopt(pointer, _profile.IteratorClass), Local, EnterEffect, noteArgument, direction);
                if (iterator is null) throw new InvalidDataException("The actual CheckHold factory returned no iterator.");
                var initial = ReadIterator(iterator);
                if (initial.State != 0 || initial.Owner != JudgeAddress || initial.Note != note || initial.Left != _directions[note]
                    || initial.Current != 0 || _original.Concat(_targets).Any(item => Target(item.Root) == Target(iterator)))
                    throw new InvalidOperationException("The actual CheckHold factory returned a foreign or already scheduled iterator.");
                VerifyInputs(inputs);
                if (Count() != previousCount) throw new InvalidOperationException("The actual CheckHold factory changed the scheduled Hold count.");
                ConfirmEffect();
                var record = StartActual(iterator, expectedRecord: null);
                var current = ReadIterator(iterator);
                if (current.State is not (2 or 3) || current.Owner != initial.Owner || current.Note != initial.Note
                    || current.Left != initial.Left || current.Current != 0 || Count() != checked(previousCount + 1))
                    throw new InvalidOperationException("The actual target Hold did not reach its recognized paused continuation state and count.");
                _targets.Add(new(record, iterator, current));
                _expectedCount = previousCount + 1;
                VerifyInputs(inputs); VerifyTargetFingers();
                ConfirmEffect();
            }
            _targetStarted = true; VerifyTargetCore(); return 0;
        }, recoverKnownTargetFailure: true);
        private HoldCoroutineLifecycle.Record StartActual(NativeRuntime.RootHandle iterator, HoldCoroutineLifecycle.Record? expectedRecord)
        {
            _observer.VerifyDrained();
            _startObservation = null; _startingEnumerator = Target(iterator).ToInt64(); _callingStart = true;
            var priorMoves = expectedRecord?.MoveCalls ?? 0; var priorYields = expectedRecord?.YieldedMoves ?? 0;
            NativeRuntime.RootHandle? handle = null;
            try
            {
                using var argument = A.Ref(Target(iterator));
                _owner._api.InvokeReferenceWithPublicationAdmission(_profile.StartMethod, Target(_judge),
                    pointer => handle = Adopt(pointer, _owner._holdHandleClass), Local, EnterEffect, argument);
            }
            finally { _callingStart = false; }
            _observer.VerifyDrained(); Source();
            if (_startObservation is not { Scope.Completed: true, Scope.Disposed: true } observation || handle is null)
                throw new InvalidOperationException("The actual Start did not close its original observed scheduling scope.");
            var record = observation.Scope.Entry;
            if (expectedRecord is not null && !ReferenceEquals(expectedRecord, record)
                || record.Identity.Enumerator != _startingEnumerator || record.Handle != Target(handle).ToInt64()
                || record.SeekStopped || record.NaturalCompletion || record.Unknown || record.InFlight != 0
                || !record.StartReturned || record.MoveCalls <= priorMoves || record.YieldedMoves <= priorYields)
                throw new InvalidOperationException("The actual Start did not yield its exact registered Hold identity.");
            record.NativeOwnerBinding!.Source.VerifySource(); return record;
        }
        internal void StopTargetHolds() => Operation(() =>
        {
            Source(); _observer.VerifyDrained();
            if (!_stopped || _unknown || _restored) throw new InvalidOperationException("The original Hold epoch cannot stop an unknown or restored target.");
            if (_targetStopped)
            {
                VerifyStopCompleted(_owner.HoldLifecycle); VerifyRecords(_targets, stopped: true);
                if (Count() != 0 || _expectedCount != 0) throw new InvalidOperationException("The stopped target Hold count changed.");
                return 0;
            }
            if (!_targetStarted)
            {
                if (_targets.Count == 0) VerifyPredecessorsStoppedCore();
                else
                {
                    VerifyKnownPartialTarget(); StopActual(_targets);
                }
            }
            else StopActual(_targets);
            _targetStopped = true; _targetConfirmed = false; return 0;
        });
        // This managed check is also used at the actual progress store boundary.
        // Reuse the Stop proof owned by this epoch and reject later observer faults.
        internal void VerifyTargetStoppedForTimeRecovery()
        {
            Local();
            if (!_targetStopped || _unknown || _owner.HoldLifecycle.Snapshot().Unknown)
                throw new InvalidOperationException("Time restoration requires this epoch's known completed target Hold Stop.");
        }
        private void VerifyKnownPartialTarget()
        {
            Source(); _observer.VerifyDrained(); _profile.VerifyCurrent(); VerifyModes(); VerifyTargetFingers();
            if (!_targetStartAttempted || _targetStarted || !_timeWritten || _unconfirmedEffects != 0
                || _targets.Count > RequiresContinuation.Count || _expectedCount != _targets.Count || Count() != _expectedCount
                || !SameTime(JudgeTime(), _target.Coordinates.SyncedSeconds))
                throw new InvalidOperationException("Partial target scheduling has no complete known actual boundary.");
            VerifyRecords(_targets, stopped: false);
            foreach (var item in _targets)
                if (ReadIterator(item.Root) != item.Current)
                    throw new InvalidOperationException("An actual partial target iterator changed before its controlled Stop.");
        }
        internal void RestorePredecessors() => Operation(() =>
        {
            Source(); _observer.VerifyDrained(); VerifyModes(); VerifyOriginalFingers();
            if (!_stopped || !_targetStopped || _restored || _unknown || Count() != 0 || _expectedCount != 0)
                throw new InvalidOperationException("The actual predecessor cannot restore before the exact target Stop and value recovery.");
            VerifyStopCompleted(_owner.HoldLifecycle); VerifyRecords(_targets, stopped: true);
            var expectedTime = _timeWritten ? _target.Coordinates.SyncedSeconds : _originalJudgeTime;
            if (!SameTime(JudgeTime(), expectedTime)) throw new InvalidOperationException("The owned Judge time cache changed before recovery.");
            if (_timeWritten)
                Write(_judge, _sourceProfile.JudgeClass, "_nowTime", "System.Single", 0x64, expectedTime, _originalJudgeTime);
            // Existing yielded Move states do not increment this field again.
            SetCount(0, _originalCount);
            _restoringCall = true;
            try
            {
                foreach (var item in _original)
                {
                    if (ReadIterator(item.Root) != item.Original)
                        throw new InvalidOperationException("The retained original iterator changed before controlled rescheduling.");
                    var inputs = ReadInputs();
                    using (_owner.HoldLifecycle.BeginSeekRestart(this, item.Record)) StartActual(item.Root, item.Record);
                    VerifyInputs(inputs); RestoreIteratorState(item);
                    if (Count() != _originalCount) throw new InvalidOperationException("The original yielded Hold unexpectedly changed the restored counter.");
                    ConfirmEffect();
                }
            }
            finally { _restoringCall = false; }
            _restored = true; VerifyRestoredCore(); return 0;
        });
        private bool VerifyTargetCore()
        {
            Source(); _observer.VerifyDrained(); VerifyModes(); VerifyTargetFingers();
            if (!_targetStarted || _targetStopped || _restored || _unknown || _targets.Count != RequiresContinuation.Count
                || Count() != _expectedCount || _expectedCount != _targets.Count
                || !SameTime(JudgeTime(), _target.Coordinates.SyncedSeconds))
                throw new InvalidOperationException("The exact target Hold epoch did not independently read back.");
            VerifyRecords(_targets, stopped: false);
            foreach (var item in _targets)
                if (ReadIterator(item.Root) != item.Current) throw new InvalidOperationException("A target Hold iterator changed before confirmation.");
            _targetConfirmed = true; return true;
        }
        internal bool VerifyTarget() => Operation(VerifyTargetCore);
        private bool VerifyRestoredCore()
        {
            Source(); _observer.VerifyDrained(); VerifyModes(); VerifyOriginalFingers();
            if (!_restored || _unknown || Count() != _originalCount || _expectedCount != _originalCount
                || !SameTime(JudgeTime(), _originalJudgeTime))
                throw new InvalidOperationException("The exact original Hold epoch did not independently read back.");
            VerifyRecords(_original, stopped: false);
            foreach (var item in _original)
                if (ReadIterator(item.Root) != item.Original) throw new InvalidOperationException("The original Hold iterator state did not restore.");
            return true;
        }
        internal bool VerifyRestored() => Operation(VerifyRestoredCore);
    }
}
