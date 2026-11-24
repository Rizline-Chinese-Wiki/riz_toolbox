namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekCanvasBasisCommands
{
    private void VerifyProvenance()
    {
        Local();
        if (_cache is null || _positions is null || _sourceCapture is null || _plan is null
            || !ReferenceEquals(_basis!.SourceCapture, _sourceCapture)
            || !ReferenceEquals(_cache.SourceCapture, _sourceCapture)
            || !ReferenceEquals(_positions.SourceCapture, _sourceCapture)
            || !ReferenceEquals(_sourceCapture.Plan, _plan))
            throw new InvalidOperationException("Both original Canvas owners must consume exactly the same retained capture and Plan.");
    }

    private void VerifyPreparation()
    {
        VerifyProvenance();
        _cache!.VerifyPrepared(); _positions!.VerifyPrepared(); CommonSource();
        _cache.VerifyPrepared(); _positions.VerifyPrepared(); Local();
    }

    internal void VerifyPrepared()
    {
        Operation("VerifyPrepared", () =>
        {
            if (!_prepared || _executionEntered || _restorationEntered)
                throw new InvalidOperationException("Canvas basis preparation must precede once-only execution/restoration.");
            VerifyPreparation(); return 0;
        });
    }

    internal void Execute()
    {
        Operation("Execute", () =>
        {
            if (!_prepared || _executionEntered || _restorationEntered)
                throw new InvalidOperationException("Prepared Canvas basis commands execute once only.");
            _executionEntered = true; RevokeAllConfirmations(); VerifyPreparation();
            _phase = "CommitCaches"; _cache!.Commit();
            Require(_cache.Confirm().Observed == true, "The complete Canvas cache targets must be observed before any position setter.");
            _phase = "BeforePositions"; _positions!.VerifyPrepared(); CommonSource();
            _phase = "ExecutePositions"; _positions.Execute();
            _phase = "ObserveApplied"; ObserveApplied(); Local();
            _executionFinished = true; RecordProof(restored: false); return 0;
        });
    }

    internal bool VerifyAppliedValues()
    {
        return Operation("VerifyAppliedValues", () =>
        {
            if (!_executionFinished || _restorationEntered)
                throw new InvalidOperationException("Finish Canvas basis execution before observing its selected applied values.");
            RevokeAllConfirmations(); ObserveApplied(); Local(); RecordProof(restored: false); return true;
        });
    }

    private void ObserveApplied()
    {
        VerifyProvenance();
        // Ordered complete samples, not an atomic snapshot or an engine/consumer
        // publication certificate. A second round catches cross-child callbacks.
        for (var pass = 0; pass < 2; ++pass)
        {
            CommonSource();
            Require(_cache!.Confirm().Observed == true, "The complete Canvas cache target raw bits were not observed.");
            Require(_positions!.VerifyAppliedValues(), "The complete Canvas position target raw bits were not observed.");
        }
    }

    internal void RestoreSelectedValues()
    {
        RevokeTargetOwnershipAcknowledgement();
        Operation("RestoreSelectedValues", () =>
        {
            if (!_prepared || _restorationEntered)
                throw new InvalidOperationException("Selected Canvas basis value recovery is once-only after preparation.");
            _restorationEntered = true; RevokeAllConfirmations(); VerifyProvenance();
            CommonSource(); _cache!.VerifyExpectedValues(); _positions!.VerifyExpectedValues();
            _phase = "RestorePositions"; _positions.RestoreSelectedValues();
            Require(_positions.VerifyRestoredValues(), "Selected Canvas positions were not restored; cache inverse is not admitted.");
            _phase = "BeforeCacheRestoration"; CommonSource(); _cache.VerifyExpectedValues();
            _phase = "RestoreCaches"; _cache.Restore();
            Require(_cache.ConfirmRestored().OriginalStateRestored, "The complete original Canvas cache raw bits were not observed.");
            _phase = "ObserveRestored"; ObserveRestored(); Local();
            _restorationFinished = true; RecordProof(restored: true); return 0;
        });
    }

    internal bool VerifyRestoredValues()
    {
        return Operation("VerifyRestoredValues", () =>
        {
            if (!_restorationFinished)
                throw new InvalidOperationException("Finish selected Canvas basis value recovery before observing its original values.");
            RevokeAllConfirmations(); ObserveRestored(); Local(); RecordProof(restored: true); return true;
        });
    }

    private void ObserveRestored()
    {
        VerifyProvenance();
        for (var pass = 0; pass < 2; ++pass)
        {
            CommonSource();
            Require(_cache!.ConfirmRestored().OriginalStateRestored, "The complete original Canvas cache raw bits were not observed.");
            Require(_positions!.VerifyRestoredValues(), "The complete original Canvas position raw bits were not observed.");
        }
    }

    private static void Require(bool observed, string reason)
    {
        if (!observed) throw new InvalidOperationException(reason);
    }
}
