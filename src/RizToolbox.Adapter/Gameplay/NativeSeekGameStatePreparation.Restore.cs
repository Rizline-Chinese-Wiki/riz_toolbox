namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekGameStatePreparation
{
    private void Recover(Action action)
    {
        try { Current(); action(); Current(); }
        catch (Exception error) { _restoreErrors.Add(error); }
    }

    // Time is reachable by target Hold callbacks. A reconstruction that was
    // entered must close its actual target scheduling before any inverse time
    // store, even when another independent game-state inverse failed.
    internal void VerifyTimeRestoration()
    {
        Registered();
        if (_disposed) throw new ObjectDisposedException(nameof(NativeSeekGameStatePreparation));
        if (_entered) _holdEpoch.VerifyTargetStoppedForTimeRecovery();
    }

    internal void RevokeAllocationAcknowledgement()
    {
        Registered(); _renderer?.RevokePoolOwnershipAcknowledgement();
        _holdGeometry?.RevokeTransformOwnershipAcknowledgement();
        _canvas?.RevokeTargetOwnershipAcknowledgement();
        _lines?.RevokeTargetOwnershipAcknowledgement();
    }

    public void Restore()
    {
        RevokeAllocationAcknowledgement();
        Operation(() =>
        {
        if (_restoring) throw new InvalidOperationException("Game-state restoration is once-only.");
        _restoring = true;
        // Every following inverse owns state reachable by these callbacks. An
        // unresolved Stop is a failed prerequisite, not an independent stage
        // whose failure permits us to overwrite still-running target state.
        _holdEpoch.StopTargetHolds();
        if (_holdFinalEntered) Recover(() =>
        {
            _holdFinal!.Restore(); Require(_holdFinal.ConfirmRestored().OriginalStateRestored, "Final Hold scalar/list restoration failed.");
        });
        if (_holdGeometry is not null) Recover(() =>
        {
            _holdGeometry.RestoreSelectedValues(); Require(_holdGeometry.VerifyRestoredValues(), "Hold Transform restoration failed.");
            _holdGeometry.AcknowledgeRestoredTransformOwnership();
        });
        if (_visibility is not null) Recover(() =>
        {
            _visibility.RestoreSelectedValues(); Require(_visibility.VerifyRestoredValues(), "Native note visibility value restoration failed.");
        });
        if (_holdResetEntered) Recover(() =>
        {
            _holdReset.Restore(); Require(_holdReset.ConfirmRestored().OriginalStateRestored, "Original Hold state restoration failed.");
        });
        if (_rendererEntered || _flagsEntered) Recover(() =>
        {
            _flags.Restore(); Require(_flags.ConfirmRestored().OriginalStateRestored, "Original note flags did not read back.");
            _renderer.AcceptRestoredNoteFlags(_flags);
        });
        if (_rendererEntered) Recover(() =>
        {
            _renderer.Restore(); Require(_renderer.VerifyRestoredValues(), "The original renderer pools did not read back.");
        });
        if (_dynamicEntered) Recover(() =>
        {
            _lines.RestoreDynamicRendererSavedValues();
            Require(_lines.VerifyRestoredDynamicRendererSavedValues(), "The original line renderer storage did not read back.");
        });
        if (_linesEntered) Recover(() =>
        {
            _lines.RestoreSelectedValues(); Require(_lines.VerifyRestoredValues(), "The original line endpoints did not read back.");
            _lines.AcknowledgeVerifiedRestoredOwnership();
        });
        if (_canvasEntered) Recover(() =>
        {
            _canvas.RestoreSelectedValues(); Require(_canvas.VerifyRestoredValues(), "The original Canvas values did not read back.");
            _canvas.AcknowledgeVerifiedRestoredOwnership();
        });
        for (var i = _attemptedValues - 1; i >= 0; --i)
        {
            var value = _values[i];
            Recover(() => { value.Restore(); Require(value.ConfirmRestored().OriginalStateRestored, value.Id + " did not restore its actual original values."); });
        }
            _restored = _restoreErrors.Count == 0;
            if (!_restored) RevokeAllocationAcknowledgement();
            return 0;
        });
    }

    public GameplayControlRestoration ConfirmRestored() => Operation(() =>
    {
        if (!_entered && !_restoring)
        {
            VerifyPreparation();
            return new GameplayControlRestoration(true, "The unattempted original game-state preparation still has its independently verified baseline.");
        }
        if (!_restoring) throw new InvalidOperationException("Attempt the actual inverse before confirming game-state restoration.");
        if (_restored)
        {
            for (var i = 0; i < _attemptedValues; ++i)
                _restored &= _values[i].ConfirmRestored().OriginalStateRestored;
            if (_rendererEntered || _flagsEntered) _restored &= _flags.ConfirmRestored().OriginalStateRestored;
            if (_holdResetEntered) _restored &= _holdReset.ConfirmRestored().OriginalStateRestored;
            if (_rendererEntered) _restored &= _renderer.VerifyRestoredValues();
            if (_canvasEntered) _restored &= _canvas.VerifyRestoredValues();
            // Geometry and visibility were read back before their original
            // parent basis was restored. Their world snapshots are historical
            // afterwards; verifying those old target-basis coordinates again
            // would reject the legitimate parent inverse.
        }
        if (!_restored) RevokeAllocationAcknowledgement();
        return new GameplayControlRestoration(_restored,
            _restored ? "The original selected game-state values were independently restored in dependency order. External engine effects are separately retained."
                : "The original game state was not fully restored: " + string.Join("; ", _restoreErrors.Select(error => error.Message)));
    });
}
