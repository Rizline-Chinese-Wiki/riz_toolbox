using System.Runtime.ExceptionServices;
using RizToolbox.Adapter.UiInput;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekCanvasPositionCommands
{
    internal void VerifyExpectedValues() => Operation(() =>
    {
        if (!_prepared) throw new InvalidOperationException("Prepare actual Canvas position commands before checking their complete expected raw bits.");
        Observe();
        if (_unrecognized)
            throw new InvalidOperationException("An unrecognized actual Canvas position state forbids accepting its complete expected raw bits.");
        VerifySource(); return 0;
    });

    internal void VerifyPrepared() => Operation(() =>
    {
        if (!_prepared || _executionEntered || _restorationEntered)
            throw new InvalidOperationException("Actual Canvas preparation must precede its once-only execution and restoration.");
        if (!Same(Observe(), _baseline) || _unrecognized)
            throw new InvalidOperationException("The complete prepared actual Canvas localPosition baseline changed.");
        return 0;
    });

    internal void Execute() => Operation(() =>
    {
        if (!_prepared || _executionEntered || _restorationEntered)
            throw new InvalidOperationException("Prepared actual Canvas position commands execute once only.");
        _executionEntered = true; _appliedValuesVerified = false; _restoredValuesVerified = false;
        if (!Same(Observe(), _baseline) || _unrecognized)
            throw new InvalidOperationException("The complete actual Canvas localPosition baseline changed before execution.");
        foreach (var row in _uniqueTargets) Store(row.Ordinal, row.LocalPosition, SeekCanvasPositionDirection.Apply);
        var actual = Observe();
        if (_unrecognized || !MatchesTargets(actual))
            throw new InvalidOperationException("The complete actual Canvas target positions were not observed after execution.");
        VerifySource(); _executionFinished = true; return 0;
    });

    internal bool VerifyAppliedValues() => Operation(VerifyAppliedValuesCore, SeekCanvasPositionDirection.Apply);

    private bool VerifyAppliedValuesCore()
    {
        if (!_executionFinished || _restorationEntered)
            throw new InvalidOperationException("Complete the actual Canvas command sequence before verifying its selected applied values.");
        var actual = Observe();
        var verified = !_unrecognized && MatchesTargets(actual);
        VerifySource(); _appliedValuesVerified = verified; return verified;
    }

    internal void RestoreSelectedValues()
    {
        RevokeTargetOwnershipAcknowledgement();
        RestoreSelectedValuesCore();
    }
    private void RestoreSelectedValuesCore() => Operation(() =>
    {
        if (!_prepared || _restorationEntered)
            throw new InvalidOperationException("Actual Canvas selected value restoration is once-only after preparation.");
        _restorationEntered = true; _appliedValuesVerified = false; _restoredValuesVerified = false;
        Observe();
        RequireRecognizedRestoration();
        for (var index = _enteredOrdinals.Count - 1; index >= 0; --index)
        {
            // Full coverage includes unattempted and aliased occurrences. Never
            // manufacture a rollback by accepting their external modifications.
            Observe(); RequireRecognizedRestoration();
            var ordinal = _enteredOrdinals[index]; var current = ReadSelected(ordinal);
            if (!Same(current, _expected[ordinal]))
            {
                MarkUnrecognized(); RequireRecognizedRestoration();
            }
            if (Same(current, _baseline[ordinal])) continue;
            try { Store(ordinal, _baseline[ordinal], SeekCanvasPositionDirection.Restore); }
            catch (Exception error)
            {
                _failure ??= error.Message;
                // A native exception can leave either its exact before or target
                // value. A third state is sticky and stops every later inverse.
                Local(); Observe(); RequireRecognizedRestoration();
            }
        }
        var restored = Observe(); VerifySource();
        RequireRecognizedRestoration();
        _restorationFinished = !_unrecognized && Same(restored, _baseline); return 0;
    });

    internal bool VerifyRestoredValues() => Operation(VerifyRestoredValuesCore, SeekCanvasPositionDirection.Restore);

    private bool VerifyRestoredValuesCore()
    {
        if (!_restorationEntered)
            throw new InvalidOperationException("Restore selected actual Canvas values before verifying their independent baseline.");
        var actual = Observe(); var verified = !_unrecognized && Same(actual, _baseline);
        VerifySource(); _restoredValuesVerified = verified; return verified;
    }

    private bool MatchesTargets(IReadOnlyList<NativeVector3> actual)
    {
        if (actual.Count != _targets.Length) return false;
        for (var i = 0; i < actual.Count; ++i)
            if (!Same(actual[i], _targets[i].LocalPosition)) return false;
        return true;
    }

    private void RequireRecognizedRestoration()
    {
        if (_unrecognized)
            throw new InvalidOperationException("An unrecognized actual Canvas state permanently forbids selected value restoration.");
    }

    private void Store(int ordinal, NativeVector3 value, SeekCanvasPositionDirection direction)
    {
        Observe();
        if (_unrecognized) throw new InvalidOperationException("An unrecognized actual Canvas state forbids another setter.");
        var before = ReadSelected(ordinal);
        if (!Same(before, _expected[ordinal]))
        {
            MarkUnrecognized(); throw new InvalidOperationException("The selected actual Canvas raw bits changed before setter admission.");
        }
        var index = _commands.Count;
        _commands.Add(new(index, direction, ordinal, _targets[ordinal].Transform, before, value,
            false, false, null, false, false, false, false, null));
        Exception? failure = null; var entered = false; var pendingConflict = false;
        try
        {
            _access.WriteSlot(ordinal, value, before, () =>
            {
                Local();
                if (entered || _pendingCommand >= 0)
                {
                    if (entered) MarkUnrecognized();
                    throw new InvalidOperationException("The final actual Canvas setter guard entered more than once.");
                }
                _targetOwnershipAcknowledged = false;
                entered = true; _externalEffects = true; _unknown = true; _retained = true; _pendingCommand = index;
                if (direction == SeekCanvasPositionDirection.Apply) { ++_attemptedWrites; _enteredOrdinals.Add(ordinal); }
                else ++_attemptedRestores;
                _commands[index] = _commands[index] with
                { NativeEntered = true, ExternalEffectsUnclosed = true, SideEffectsUnknown = true };
            });
            if (entered) _commands[index] = _commands[index] with { NativeReturned = true };
            Local();
            if (!entered) throw new InvalidOperationException("The exact actual Canvas setter did not record native entry.");
        }
        catch (Exception error)
        {
            failure = error; pendingConflict = MarkObservedNativeConflict(error); AdoptCleanupResponsibility(error);
        }
        if (entered && _cleanupException is null && !pendingConflict)
        {
            try
            {
                var observed = ReadSelected(ordinal);
                if (!RecordPendingObservation(observed))
                    failure ??= new InvalidOperationException("The entered actual Canvas setter has an unrecognized visible post-state.");
                else if (failure is null && !Same(observed, value))
                    failure = new InvalidOperationException("The actual Canvas setter returned without its commanded localPosition raw bits.");
            }
            catch (Exception observation)
            {
                MarkObservedNativeConflict(observation);
                AdoptCleanupResponsibility(observation);
                if (failure is not null && !ReferenceEquals(failure, observation))
                    failure.Data["SeekCanvasPositionObservationFailure"] = observation;
                failure ??= observation;
                _commands[index] = _commands[index] with { Failure = failure.Message };
            }
        }
        if (failure is not null)
        {
            _commands[index] = _commands[index] with { Failure = failure.Message };
            _failure ??= failure.Message;
            if (entered) AttachOwner(failure);
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
