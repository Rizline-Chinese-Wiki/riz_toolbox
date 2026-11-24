using System.Runtime.ExceptionServices;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekCanvasBasisCommands
{
    internal static NativeSeekCanvasBasisCommands Prepare(Func<INativeSeekCanvasBasisAccess> createBasis,
        ISeekCanvasPositionNativeProfile positionProfile)
    {
        ArgumentNullException.ThrowIfNull(createBasis); ArgumentNullException.ThrowIfNull(positionProfile);
        var value = new NativeSeekCanvasBasisCommands(); value.ReserveOwner();
        try
        {
            // All three public owners exist before any profile/native factory.
            value._cache = NativeSeekCanvasCacheParticipant.Reserve();
            value._positions = NativeSeekCanvasPositionCommands.Reserve();
            value._phase = "CaptureSource";
            value._basis = createBasis() ?? throw new InvalidOperationException("A shared actual Canvas source is required.");
            value._basis.BindTerminalObserver(value.OnSourceTerminal);
            value._sourceCapture = value._basis.SourceCapture;
            value._plan = value._sourceCapture.Plan;
            value._phase = "PrepareCaches";
            value._cache.PrepareReserved(value._basis.CreateCacheAccess);
            value._phase = "PreparePositions";
            value._positions.PrepareReserved(() => value._basis.CreatePositionAccess(positionProfile));
            value._phase = "VerifyPrepared";
            value.Operation("VerifyPrepared", () => { value.VerifyPreparation(); value._prepared = true; return 0; });
            return value;
        }
        catch (Exception error)
        {
            // A source factory may throw before returning its already-published
            // cleanup owner. Adopt that exact holder instead of manufacturing one.
            value._basis ??= error.Data["SeekCanvasBasisSourceOwner"] as INativeSeekCanvasBasisAccess;
            value._sourceCapture ??= error.Data["SeekCanvasBasisSourceCapture"] as NativeSeekCanvasTargetCapture;
            if (value._sourceCapture is not null)
            {
                try { value._plan ??= value._sourceCapture.Plan; }
                catch (InvalidOperationException) { /* Source construction did not finish its Plan. */ }
            }
            value.RememberFailure(error); value.AdoptCleanup(error); value.AttachOwner(error);
            try { value.Dispose(); }
            catch (Exception cleanup)
            {
                value.RememberFailure(cleanup); value.AttachOwner(error);
                if (value._cleanupFailure is not null) error.Data[CleanupFailureKey] = value._cleanupFailure;
            }
            throw;
        }
    }

    private void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != _thread || !ReferenceEquals(_registered, this))
            throw new InvalidOperationException("Canvas basis commands belong to their original owner thread.");
    }

    private void DiagnosticEntry()
    {
        CheckThread();
        if (_busy || _disposing)
            throw new InvalidOperationException("Canvas basis diagnostics cannot reenter an active operation.");
    }

    private void Local()
    {
        CheckThread();
        if (_disposed) throw new ObjectDisposedException(nameof(NativeSeekCanvasBasisCommands));
        if (_cleanupFailure is not null)
            throw Responsibility("The original shared Canvas source retains unknown native cleanup; no retry is admitted.");
        var basis = _basis ?? throw new InvalidOperationException("The shared Canvas source is absent.");
        basis.VerifyOwnerThread(); basis.VerifyLocal();
    }

    private void CommonSource()
    {
        Local(); _basis!.VerifySource(Local); Local();
    }

    private T Operation<T>(string phase, Func<T> operation)
    {
        CheckThread();
        RevokeConfirmations();
        if (_busy || _disposing)
            throw new InvalidOperationException("A Canvas basis operation cannot reenter itself.");
        _phase = phase;
        _busy = true; Exception? failure = null;
        try { Local(); var result = operation(); Local(); return result; }
        catch (Exception error)
        {
            RevokeTargetOwnershipAcknowledgement();
            failure = error; RevokeConfirmations(); RememberFailure(error); AdoptCleanup(error);
            // Keep the primary failure even if an independent child is currently
            // busy, or its bounded revision is exhausted during revocation.
            RevokeChildrenAfterFailure(error);
            try { RefreshResponsibility(); }
            catch (InvalidOperationException) { _retained = true; }
            AttachOwner(error); throw;
        }
        finally
        {
            _busy = false;
            if (_disposed)
            {
                try { FinishDispose(); }
                catch (Exception cleanup)
                {
                    if (failure is null) throw;
                    RememberFailure(cleanup); AttachOwner(failure);
                    if (_cleanupFailure is not null) failure.Data[CleanupFailureKey] = _cleanupFailure;
                }
            }
        }
    }

    private void ReserveOwner()
    {
        lock (OwnerGate)
        {
            if (Owners.Count >= MaximumUnclosedOwners)
                throw new NotSupportedException("The bounded Canvas basis owner capacity is exhausted before native acquisition.");
            Owners.Add(this); _ownerReserved = true;
        }
    }

    private void ReleaseOwner()
    {
        lock (OwnerGate)
        {
            if (!_ownerReserved) return;
            Owners.Remove(this); _ownerReserved = false;
        }
    }

    private void RevokeConfirmations() { _appliedValuesVerified = false; _restoredValuesVerified = false; }

    private void RevokeAllConfirmations()
    {
        RevokeConfirmations();
        _cache?.RevokeBasisConfirmations(); _positions?.RevokeBasisConfirmations();
    }

    private void RevokeChildrenAfterFailure(Exception primary)
    {
        var revocationFailures = new List<Exception>(2);
        try { _cache?.RevokeBasisConfirmations(); }
        catch (Exception error) { revocationFailures.Add(error); }
        try { _positions?.RevokeBasisConfirmations(); }
        catch (Exception error) { revocationFailures.Add(error); }
        if (revocationFailures.Count != 0)
            primary.Data["SeekCanvasBasisProofRevocationFailures"] = Array.AsReadOnly(revocationFailures.ToArray());
    }

    private void InvalidateStaleProofs()
    {
        var cacheRevision = _cache?.ObservationRevision ?? -1;
        var positionRevision = _positions?.ObservationRevision ?? -1;
        if (_appliedValuesVerified && (cacheRevision != _appliedCacheRevision || positionRevision != _appliedPositionRevision))
            _appliedValuesVerified = false;
        if (_restoredValuesVerified && (cacheRevision != _restoredCacheRevision || positionRevision != _restoredPositionRevision))
            _restoredValuesVerified = false;
    }

    private void RecordProof(bool restored)
    {
        // These are pure managed revisions. A later independent child call
        // invalidates this observation even if it subsequently confirms itself.
        var cacheRevision = _cache!.ObservationRevision; var positionRevision = _positions!.ObservationRevision;
        if (restored)
        {
            _restoredCacheRevision = cacheRevision; _restoredPositionRevision = positionRevision;
            _restoredValuesVerified = true;
        }
        else
        {
            _appliedCacheRevision = cacheRevision; _appliedPositionRevision = positionRevision;
            _appliedValuesVerified = true;
        }
    }

    private void RememberFailure(Exception error)
    {
        _failure ??= error.Message;
        // Keep the original commit error in the journal, but only a failure
        // after inverse admission poisons a completed restored ownership proof.
        // This also makes later failed readback/acknowledgement sticky.
        if (_restorationEntered) _restorationFailure ??= error.Message;
        if (_failures.Any(item => item.Phase == _phase && ReferenceEquals(item.Failure, error))) return;
        var childOwner = error.Data["SeekCanvasPositionEffectsOwner"]
            ?? error.Data["SeekCanvasPositionCleanupOwner"] ?? error.Data["SeekCanvasCacheCleanupOwner"];
        // Once-only dispatch bounds the write journal. Diagnostic failures are
        // also bounded; the original thrown error remains with its caller.
        if (_failures.Count < 256) _failures.Add(new(_phase, error.Message, error, childOwner));
    }

    private void AdoptCleanup(Exception error)
    {
        var cleanup = error.Data[CleanupFailureKey] as Exception
            ?? error.Data["SeekCanvasSourceCleanupFailure"] as Exception
            ?? error.Data["SeekCanvasCacheCleanupFailure"] as Exception
            ?? error.Data["SeekCanvasPositionCleanupFailure"] as Exception
            ?? error.Data["NativePublicationCleanupFailure"] as Exception
            ?? _basis?.CleanupFailure;
        if (cleanup is null) return;
        _cleanupFailure ??= cleanup;
        _sourceCleanupOwner ??= error.Data["SeekCanvasCacheSourceCleanupOwner"]
            ?? error.Data["SeekCanvasPositionSourceCleanupOwner"]
            ?? error.Data["SeekCanvasSourceCleanupOwner"] ?? error.Data["NativePublicationCleanupOwner"]
            ?? _basis?.SourceCleanupOwner;
        _retained = true; RevokeConfirmations();
    }

    private void RefreshResponsibility(SeekCanvasCacheEffectsJournal? cache = null,
        SeekCanvasPositionEffectsJournal? positions = null)
    {
        cache ??= _cache?.GetJournal(); positions ??= _positions?.GetJournal();
        _cleanupFailure ??= _basis?.CleanupFailure;
        _sourceCleanupOwner ??= _basis?.SourceCleanupOwner ?? cache?.SourceCleanupOwner ?? positions?.SourceCleanupOwner;
        _retained = _cleanupFailure is not null || _basis?.HasRetainedResponsibility == true
            || cache?.HasRetainedResponsibility == true || positions?.HasRetainedResponsibility == true;
    }

    private void OnSourceTerminal()
    {
        // Invoked inside the last permitted child lease release, before that
        // child's Access.Dispose returns. Never inspect a busy child or reenter
        // disposal here. Terminal means all child leases already passed their
        // original responsibility checks. A successful scene-ownership receipt
        // releases duplicate roots without erasing historical external effects.
        CheckThread();
        _cleanupFailure ??= _basis?.CleanupFailure;
        _sourceCleanupOwner ??= _basis?.SourceCleanupOwner;
        if (!_disposed) return;
        _retained = _cleanupFailure is not null || _basis?.HasRetainedResponsibility == true;
        if (!_retained) ReleaseOwner();
    }

    private void AttachOwner(Exception error)
    {
        error.Data[OwnerKey] = this;
        error.Data[FailuresKey] = Array.AsReadOnly(_failures.Select(item => item with { }).ToArray());
        if (_cleanupFailure is not null) error.Data[CleanupFailureKey] = _cleanupFailure;
        if (_sourceCleanupOwner is not null) error.Data["SeekCanvasBasisSourceCleanupOwner"] = _sourceCleanupOwner;
    }

    private Exception Responsibility(string reason)
    {
        _retained = true; var error = new InvalidOperationException(reason); AttachOwner(error); return error;
    }

    public void Dispose()
    {
        CheckThread(); RevokeConfirmations(); _disposed = true;
        if (_disposing) return;
        if (_busy)
        {
            // Revoke both child callers now; do not drop the bootstrap hold
            // until their managed source scopes have actually unwound.
            _disposing = true;
            try { DisposeChildren(); }
            finally { _disposing = false; }
            return;
        }
        FinishDispose();
    }

    private void DisposeChildren()
    {
        var errors = new List<Exception>(2);
        _phase = "DisposeCaches";
        try { _cache?.Dispose(); }
        catch (Exception error) { RememberFailure(error); AdoptCleanup(error); errors.Add(error); }
        _phase = "DisposePositions";
        try { _positions?.Dispose(); }
        catch (Exception error) { RememberFailure(error); AdoptCleanup(error); errors.Add(error); }
        if (errors.Count == 0) return;
        var failure = new AggregateException("The original Canvas child owners retain their independent cleanup or engine responsibility.", errors);
        AttachOwner(failure); throw failure;
    }

    private void FinishDispose()
    {
        if (_disposing) return;
        _disposing = true; Exception? failure = null;
        try
        {
            try { DisposeChildren(); }
            catch (Exception error) { failure = error; }
            if (!_bootstrapReleaseAttempted)
            {
                _phase = "ReleaseBootstrap"; _bootstrapReleaseAttempted = true;
                try { _basis?.Dispose(); _bootstrapReleased = true; }
                catch (Exception error)
                {
                    RememberFailure(error); AdoptCleanup(error);
                    failure = failure is null ? error : new AggregateException("Canvas child and shared source cleanup remain independently owned.", failure, error);
                }
            }
            if (_basis?.IsOperationActive == true)
            {
                // An independent original child can call aggregate.Dispose
                // from its own callback, with the aggregate itself idle. Its
                // scope/lease will finish later; the terminal observer performs
                // the pure managed registry close after the last known release.
                _retained = true;
                if (failure is not null) { AttachOwner(failure); ExceptionDispatchInfo.Capture(failure).Throw(); }
                return;
            }
            RefreshResponsibility();
            if (!_retained && _bootstrapReleased) ReleaseOwner();
            if (failure is null && _retained)
                failure = Responsibility("The original Canvas basis child/source owners retain unresolved native responsibility; disposal cannot close it.");
            if (failure is not null) { AttachOwner(failure); ExceptionDispatchInfo.Capture(failure).Throw(); }
        }
        finally { _disposing = false; }
    }
}
