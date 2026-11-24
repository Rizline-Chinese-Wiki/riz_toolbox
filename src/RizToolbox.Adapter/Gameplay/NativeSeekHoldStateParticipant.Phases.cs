using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekHoldStateParticipant
{
    public void VerifyPrepared() => Operation(() =>
    {
        if (_entered || _restoring) throw new InvalidOperationException("This Hold-state preparation was already entered.");
        VerifySource(); RequireAllOwned(); return 0;
    });
    public void Commit() => Operation(() =>
    {
        if (_entered || _restoring) throw new InvalidOperationException("Hold-state commit is once-only and cannot follow restoration.");
        _entered = true; VerifySource(); RequireAllOwned();
        foreach (var slot in _representatives)
        {
            DispatchList(MutationKind.Clear, slot);
            foreach (var reference in _listTargets[slot]) DispatchList(MutationKind.Add, slot, value: reference);
        }
        for (var ordinal = 0; ordinal < _holdTargets.Length; ++ordinal)
        {
            DispatchHold(ordinal, SeekHoldNoteField.IsStaticNote, _holdTargets[ordinal]);
            DispatchHold(ordinal, SeekHoldNoteField.LastFrameHoldLineLength, _holdTargets[ordinal]);
        }
        foreach (var field in new[] { SeekHoldControlField.NowTime, SeekHoldControlField.Speed, SeekHoldControlField.HoldLineLength })
            DispatchControl(field, _controlTarget);
        VerifySource(); RequireAllOwned(); _committed = true; return 0;
    });
    private bool ExpectedTargets()
        => Enumerable.Range(0, 2).All(slot =>
        {
            var list = SeekHoldState.At(_expectedLists, slot);
            return list.Size == _listTargets[slot].Length && list.CapacitySlots.Take(list.Size).SequenceEqual(_listTargets[slot]);
        }) && SeekHoldState.Same(_expectedHolds, _holdTargets) && SeekHoldState.Same(_expectedControl, _controlTarget);
    private void WithdrawConfirmation()
    {
        _confirmed = false; _restored = false;
        if (_hasDispatched) { _retained = true; SideEffectsUnknown = true; }
    }
    public GameplayControlConfirmation Confirm()
    {
        var observing = false;
        return Operation(() =>
        {
            VerifySource(); RequireAllOwned(); _confirmed = !_unrecognized && ExpectedTargets();
            SideEffectsUnknown = !_confirmed;
            if (_confirmed) _retained = false;
            return new GameplayControlConfirmation(_confirmed,
                "Independent complete readback of two explicit Hold occurrence sequences and bit-exact scalar targets for every actual Hold and HoldControl. Engine exit, Hold-idle, Transform geometry, renderer events and complete Seek remain unconfirmed.");
        }, () => { if (observing) WithdrawConfirmation(); }, () =>
        {
            if (!_committed || _restoring) throw new InvalidOperationException("Finish the complete Hold-state commit before confirmation.");
            observing = true; WithdrawConfirmation();
        });
    }
    private bool ResolveRestoreFailure(Exception error)
    {
        _restoreErrors.Add(error.Message); Local();
        try
        {
            if (_pending is null) RequireAllOwned();
            else ResolvePending(false);
            return true;
        }
        catch (Exception observation)
        {
            _restoreErrors.Add(observation.Message); _unrecognized = true; _retained = true; SideEffectsUnknown = true; return false;
        }
    }
    public void Restore() => Operation(() =>
    {
        if (_restoring) throw new InvalidOperationException("Hold-state restoration is once-only.");
        _restoring = true; _confirmed = false; VerifySource();
        if (_pending is not null)
        {
            try { ResolvePending(false); }
            catch (Exception error)
            {
                _restoreErrors.Add(error.Message); _unrecognized = true; _retained = true; SideEffectsUnknown = true; return 0;
            }
        }
        RequireAllOwned();
        if (ExpectedBaseline()) return 0;
        foreach (var field in new[] { SeekHoldControlField.HoldLineLength, SeekHoldControlField.Speed, SeekHoldControlField.NowTime })
        {
            if (SeekHoldState.Same(SeekHoldState.With(_expectedControl, field, _baseline.Control), _expectedControl)) continue;
            try { DispatchControl(field, _baseline.Control); }
            catch (Exception error) { if (!ResolveRestoreFailure(error)) return 0; }
        }
        for (var ordinal = _expectedHolds.Length - 1; ordinal >= 0; --ordinal)
        {
            foreach (var field in new[] { SeekHoldNoteField.LastFrameHoldLineLength, SeekHoldNoteField.IsStaticNote })
            {
                if (SeekHoldState.Same(SeekHoldState.With(_expectedHolds[ordinal], field, _baseline.Notes[ordinal]), _expectedHolds[ordinal])) continue;
                try { DispatchHold(ordinal, field, _baseline.Notes[ordinal]); }
                catch (Exception error) { if (!ResolveRestoreFailure(error)) return 0; }
            }
        }
        foreach (var listSlot in _representatives.Reverse())
        {
            var baseline = SeekHoldState.At(_baseline.Lists, listSlot); var original = _baselineArrays[baseline.Backing];
            // The accessor also verifies every active occurrence as an actual
            // Hold. Original tail slots may be null or unrelated notes, so first
            // withdraw the current active prefix before restoring any old slot.
            // A failed pre-store inverse is not blindly retried at this List.
            if (SeekHoldState.At(_expectedLists, listSlot).Size != 0)
            {
                try { DispatchList(MutationKind.ListField, listSlot, value: 0, field: SeekHoldListRestoreField.Size); }
                catch (Exception error) { if (!ResolveRestoreFailure(error)) return 0; }
                if (SeekHoldState.At(_expectedLists, listSlot).Size != 0)
                {
                    _restoreErrors.Add("The current Hold active prefix could not be safely withdrawn before original storage restoration."); continue;
                }
            }
            for (var index = original.CapacitySlots.Count - 1; index >= 0; --index)
            {
                if (_expectedArrays[original.Reference].CapacitySlots[index] == original.CapacitySlots[index]) continue;
                try { DispatchList(MutationKind.CapacitySlot, listSlot, index, original.CapacitySlots[index]); }
                catch (Exception error) { if (!ResolveRestoreFailure(error)) return 0; }
            }
            if (!SeekHoldState.Same(_expectedArrays[original.Reference], original))
            {
                _restoreErrors.Add("The complete original Hold capacity was not restored; its active prefix was not republished."); continue;
            }
            // Reattach the completely restored array while Size is zero, then
            // publish its original active prefix and original signed Version.
            foreach (var field in new[] { SeekHoldListRestoreField.Items, SeekHoldListRestoreField.Size, SeekHoldListRestoreField.Version })
            {
                var current = SeekHoldState.At(_expectedLists, listSlot);
                if (field == SeekHoldListRestoreField.Size && current.Backing != baseline.Backing)
                {
                    _restoreErrors.Add("The original Hold backing was not reattached; its active prefix was not republished."); break;
                }
                var wanted = field switch { SeekHoldListRestoreField.Size => baseline.Size,
                    SeekHoldListRestoreField.Items => baseline.Backing, _ => baseline.Version };
                var sampled = field switch { SeekHoldListRestoreField.Size => current.Size,
                    SeekHoldListRestoreField.Items => current.Backing, _ => current.Version };
                if (wanted == sampled) continue;
                if (field == SeekHoldListRestoreField.Items && current.Size > original.CapacitySlots.Count)
                {
                    _restoreErrors.Add("The original Hold backing could not be safely reattached after failed Size restoration."); continue;
                }
                try { DispatchList(MutationKind.ListField, listSlot, value: wanted, field: field); }
                catch (Exception error) { if (!ResolveRestoreFailure(error)) return 0; }
            }
        }
        VerifySource(); RequireAllOwned(); return 0;
    });
    private bool ExpectedBaseline()
        => SeekHoldState.Same(_expectedLists, _baseline.Lists) && SeekHoldState.Same(_expectedControl, _baseline.Control)
            && SeekHoldState.Same(_expectedHolds, _baseline.Notes)
            && _baselineArrays.All(pair => _expectedArrays.TryGetValue(pair.Key, out var array) && SeekHoldState.Same(array, pair.Value));
    public GameplayControlRestoration ConfirmRestored()
    {
        var observing = false;
        return Operation(() =>
        {
            VerifySource(); var actual = ReadSnapshot();
            var arraysMatch = ArraysMatch(_expectedArrays);
            var matchesExpected = actual.Identity == _baseline.Identity && SeekHoldState.Same(actual.Lists, _expectedLists)
                && SeekHoldState.Same(actual.Control, _expectedControl) && SeekHoldState.Same(actual.Notes, _expectedHolds) && arraysMatch;
            if (!matchesExpected)
            {
                _unrecognized = true; _retained = true;
                _restoreErrors.Add("Independent restoration confirmation observed state outside the recognized inverse sequence; that responsibility remains unknown.");
            }
            _restored = !_unrecognized && _pending is null && matchesExpected && ExpectedBaseline() && actual.Identity == _baseline.Identity
                && SeekHoldState.Same(actual.Lists, _baseline.Lists) && SeekHoldState.Same(actual.Control, _baseline.Control)
                && SeekHoldState.Same(actual.Notes, _baseline.Notes);
            SideEffectsUnknown = !_restored;
            if (_restored) _retained = false;
            else if (_hasDispatched) _retained = true;
            return new GameplayControlRestoration(_restored,
                (_restored ? "Every original Hold List/backing/capacity slot/Size/signed Version and bit-exact scalar baseline was independently read back. "
                    : "The complete original Hold state was not restored through the recognized conditional sequence. ")
                + (Failure is null ? "" : "Original operation failure: " + Failure + ". ") + string.Join("; ", _restoreErrors));
        }, () => { if (observing) WithdrawConfirmation(); }, () =>
        {
            if (!_restoring) throw new InvalidOperationException("Attempt restoration before complete original Hold-state readback.");
            observing = true; WithdrawConfirmation();
        });
    }
}
