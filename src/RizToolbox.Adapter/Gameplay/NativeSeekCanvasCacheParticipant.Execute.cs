using System.Runtime.ExceptionServices;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekCanvasCacheParticipant
{
    internal void VerifyExpectedValues() => Operation(() =>
    {
        if (!_prepared) throw new InvalidOperationException("Prepare actual Canvas cache storage before checking its complete expected raw bits.");
        Observe();
        if (_unrecognized)
            throw new InvalidOperationException("An unrecognized actual Canvas cache state forbids accepting its complete expected raw bits.");
        VerifySource(); return 0;
    });

    public void VerifyPrepared() => Operation(() =>
    {
        if (!_prepared || _commitEntered || _restorationEntered)
            throw new InvalidOperationException("Actual Canvas cache preparation precedes once-only commit and restoration.");
        if (!Observe().SequenceEqual(_baseline) || _unrecognized)
            throw new InvalidOperationException("The complete prepared actual Canvas cache baseline changed.");
        VerifySource(); return 0;
    });

    public void Commit() => Operation(() =>
    {
        if (!_prepared || _commitEntered || _restorationEntered)
            throw new InvalidOperationException("Prepared actual Canvas cache storage commits once only.");
        _commitEntered = true; RevokeConfirmations();
        if (!Observe().SequenceEqual(_baseline) || _unrecognized)
            throw new InvalidOperationException("The complete actual Canvas cache baseline changed before commit.");
        foreach (var row in _uniqueSlots) Store(row.Ordinal, row.TargetBits, SeekCanvasCacheDirection.Apply);
        var actual = Observe();
        if (_unrecognized || !MatchesTargets(actual))
            throw new InvalidOperationException("The complete actual Canvas target cache raw bits were not observed after commit.");
        VerifySource(); _commitFinished = true; return 0;
    });

    public GameplayControlConfirmation Confirm() => Operation(() =>
    {
        if (!_commitFinished || _restorationEntered)
            throw new InvalidOperationException("Complete actual Canvas cache commit before selected storage confirmation.");
        var actual = Observe(); var verified = !_unrecognized && MatchesTargets(actual);
        VerifySource(); _appliedValuesVerified = verified; UpdateResponsibility();
        return new GameplayControlConfirmation(verified,
            "Complete explicit actual Canvas cache raw-bit readback only; no position-engine effects, consumers, basis or complete Seek.");
    }, SeekCanvasCacheDirection.Apply);

    public void Restore() => Operation(() =>
    {
        if (!_prepared || _restorationEntered)
            throw new InvalidOperationException("Actual Canvas cache restoration is once-only after preparation.");
        _restorationEntered = true; RevokeConfirmations(); Observe(); RequireRecognizedRestoration();
        for (var index = _enteredOrdinals.Count - 1; index >= 0; --index)
        {
            // Include every unattempted and aliased slot in each complete
            // observation. Their outside changes cannot be accepted as rollback.
            Observe(); RequireRecognizedRestoration();
            var ordinal = _enteredOrdinals[index]; var current = ReadSelected(ordinal);
            if (current != _expected[ordinal]) { MarkUnrecognized(); RequireRecognizedRestoration(); }
            if (current == _baseline[ordinal]) continue;
            try { Store(ordinal, _baseline[ordinal], SeekCanvasCacheDirection.Restore); }
            catch (Exception error)
            {
                _failure ??= error.Message;
                if (_cleanupException is not null) throw;
                // A throwing store may expose its exact before or commanded
                // bits. A third state permanently stops every later inverse.
                Local(); Observe(); RequireRecognizedRestoration();
            }
        }
        var restored = Observe(); VerifySource(); RequireRecognizedRestoration();
        _restorationFinished = restored.SequenceEqual(_baseline); return 0;
    });

    public GameplayControlRestoration ConfirmRestored() => Operation(() =>
    {
        if (!_restorationEntered)
            throw new InvalidOperationException("Restore actual Canvas cache storage before its independent baseline confirmation.");
        var actual = Observe(); var verified = !_unrecognized && actual.SequenceEqual(_baseline);
        VerifySource(); _restoredValuesVerified = verified; UpdateResponsibility();
        return new GameplayControlRestoration(verified,
            "Complete independent original actual Canvas cache raw-bit readback only. " + _failure);
    }, SeekCanvasCacheDirection.Restore);

    private void RequireRecognizedRestoration()
    {
        if (_unrecognized)
            throw new InvalidOperationException("An unrecognized actual Canvas cache state permanently forbids conditional restoration.");
    }

    private void Store(int ordinal, int bits, SeekCanvasCacheDirection direction)
    {
        Observe();
        if (_unrecognized) throw new InvalidOperationException("An unrecognized actual Canvas cache state forbids another native store.");
        var before = ReadSelected(ordinal);
        if (before != _expected[ordinal])
        {
            MarkUnrecognized();
            throw new InvalidOperationException("The selected actual Canvas cache raw bits changed before store admission.");
        }
        var row = _slots[ordinal]; var index = _stores.Count;
        _stores.Add(new(index, direction, ordinal, row.Kind, row.Owner, row.Index, before, bits,
            true, false, false, null, false, false, null));
        Exception? failure = null; var entered = false; var pendingConflict = false;
        try
        {
            _access.WriteSlot(ordinal, bits, before, () =>
            {
                Local();
                if (entered || _pendingStore >= 0)
                {
                    if (entered) MarkUnrecognized();
                    throw new InvalidOperationException("The final actual Canvas cache store guard entered more than once.");
                }
                entered = true; _hasDispatched = true; _pendingStore = index; RevokeConfirmations();
                if (direction == SeekCanvasCacheDirection.Apply) { ++_attemptedWrites; _enteredOrdinals.Add(ordinal); }
                else ++_attemptedRestores;
                _stores[index] = _stores[index] with { NativeEntered = true };
            });
            if (entered) _stores[index] = _stores[index] with { NativeReturned = true };
            Local();
            if (!entered)
            {
                _dispatchContractUnknown = true; MarkUnrecognized();
                throw new InvalidOperationException("The exact actual Canvas cache store returned without recording its final native admission.");
            }
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
                    failure ??= new InvalidOperationException("The entered actual Canvas cache store has an unrecognized visible post-state.");
                else if (failure is null && observed != bits)
                    failure = new InvalidOperationException("The actual Canvas cache store returned without its commanded raw bits.");
                Observe();
            }
            catch (Exception observation)
            {
                MarkObservedNativeConflict(observation);
                AdoptCleanupResponsibility(observation); failure ??= observation;
            }
        }
        if (failure is not null)
        {
            _stores[index] = _stores[index] with { Failure = failure.Message };
            _failure ??= failure.Message;
            if (_retained) AttachOwner(failure);
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
