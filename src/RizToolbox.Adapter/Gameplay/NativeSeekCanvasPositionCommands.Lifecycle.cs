using System.Runtime.ExceptionServices;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekCanvasPositionCommands
{
    internal static NativeSeekCanvasPositionCommands Prepare(Func<INativeSeekCanvasPositionAccess> createAccess)
    {
        ArgumentNullException.ThrowIfNull(createAccess);
        var value = Reserve(); value.PrepareReserved(createAccess); return value;
    }

    internal static NativeSeekCanvasPositionCommands Reserve()
    {
        var value = new NativeSeekCanvasPositionCommands(); value.ReserveOwner(); return value;
    }

    internal void PrepareReserved(Func<INativeSeekCanvasPositionAccess> createAccess)
    {
        ArgumentNullException.ThrowIfNull(createAccess); CheckThread();
        if (!_ownerReserved || _disposed || _preparationEntered || _busy || _cleanupBusy || _notifyingDisposal)
            throw new InvalidOperationException("An original reserved actual Canvas position owner can attach one access only.");
        _preparationEntered = true;
        try { _access = createAccess() ?? throw new InvalidOperationException("No independent actual Canvas position access was created."); }
        catch (Exception error)
        {
            _disposed = true; _failure = error.Message; AdoptCleanupResponsibility(error);
            if (_cleanupException is not null)
            {
                _constructionFailure = error; _retained = true; AttachOwner(error);
            }
            else { _released = true; ReleaseOwner(); }
            throw;
        }
        try
        {
            _sourceCapture = _access.SourceCapture
                ?? throw new InvalidOperationException("The actual Canvas position access has no original source capture.");
            Operation(() =>
            {
                _access.BindLifetimeGuard(Lifetime); Local();
                _targets = CopyTargets(); BuildUniqueTargets();
                _baseline = Read(); _expected = _baseline.ToArray();
                if (!Same(Read(), _baseline))
                    throw new InvalidOperationException("The complete actual Canvas localPosition baseline changed during preparation.");
                VerifyTargets(); VerifySource(); _prepared = true; return 0;
            });
        }
        catch (Exception error)
        {
            AdoptSourceCapture(error);
            try { Dispose(); }
            catch (Exception cleanup)
            {
                AttachOperationCleanup(error, cleanup, null);
            }
            throw;
        }
    }

    private void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != _thread || !ReferenceEquals(_registered, this))
            throw new InvalidOperationException("Actual Canvas position commands belong to their original owner thread.");
    }

    private void DiagnosticEntry()
    {
        CheckThread();
        if (_busy || _cleanupBusy || _notifyingDisposal)
            throw new InvalidOperationException("Actual Canvas position diagnostics cannot reenter an active operation.");
    }

    private void Lifetime()
    {
        CheckThread();
        if (_disposed) throw new ObjectDisposedException(nameof(NativeSeekCanvasPositionCommands));
        if (_cleanupException is not null)
            throw Responsibility("Actual Canvas access retains its original temporary native cleanup owner; no further access or cleanup retry is admitted.");
        _access.VerifyOwnerThread();
    }

    private void Local() { Lifetime(); _access.VerifyLocal(); Lifetime(); }
    private void VerifySource() { Local(); _access.VerifySource(); Local(); }

    private void RevokeConfirmations()
    { _appliedValuesVerified = false; _restoredValuesVerified = false; }

    private void AdvanceObservationRevision()
    {
        if (_observationRevision != long.MaxValue) ++_observationRevision;
        if (_observationRevision != long.MaxValue) return;
        RevokeConfirmations();
        const string reason = "The bounded actual Canvas position observation revision is exhausted; its original managed owner remains retained without a native cleanup claim.";
        _failure ??= reason;
        var error = new NotSupportedException(reason);
        if (!_released) { _retained = true; AttachOwner(error); }
        throw error;
    }

    internal void RevokeBasisConfirmations()
    {
        CheckThread(); AdvanceObservationRevision(); RevokeConfirmations();
    }

    private T Operation<T>(Func<T> operation, SeekCanvasPositionDirection? confirmation = null)
    {
        CheckThread(); AdvanceObservationRevision();
        // Revoke before reentry, lifetime, or shared-source admission can fail.
        if (confirmation is not null) RevokeConfirmations();
        if (_busy || _cleanupBusy || _notifyingDisposal)
            throw new InvalidOperationException("An actual Canvas position operation cannot reenter itself.");
        _busy = true; Exception? failure = null; IDisposable? scope = null;
        try
        {
            if (_access is null) throw new InvalidOperationException("Prepare the reserved actual Canvas position owner before using it.");
            scope = _access.EnterOperation();
            Local(); var result = operation(); Local(); return result;
        }
        catch (Exception error)
        {
            failure = error; RecordOperationFailure(error); throw;
        }
        finally
        {
            Exception? scopeFailure = null, releaseFailure = null;
            // Exit the holder's managed scope before the original owner can
            // release its own child lease; sibling holds remain independent.
            try { scope?.Dispose(); }
            catch (Exception cleanup) { scopeFailure = cleanup; RecordOperationFailure(cleanup); }
            _busy = false;
            if (_disposed)
                try { Release(); }
                catch (Exception cleanup) { releaseFailure = cleanup; }
            if (scopeFailure is not null || releaseFailure is not null)
            {
                var primary = failure ?? scopeFailure ?? releaseFailure!;
                AttachOperationCleanup(primary, scopeFailure, releaseFailure);
                if (failure is null) ExceptionDispatchInfo.Capture(primary).Throw();
            }
        }
    }

    private void RecordOperationFailure(Exception error)
    {
        _targetOwnershipAcknowledged = false;
        _failure ??= error.Message; MarkObservedNativeConflict(error); RevokeConfirmations(); AdoptCleanupResponsibility(error);
        if (_externalEffects) { _unknown = true; _retained = true; AttachOwner(error); }
    }

    private void AttachOperationCleanup(Exception primary, Exception? scopeFailure, Exception? releaseFailure)
    {
        AttachOwner(primary);
        const string key = "SeekCanvasPositionOperationCleanupFailures";
        var failures = new[] { scopeFailure, releaseFailure }.Where(error => error is not null).Cast<Exception>();
        if (primary.Data[key] is IEnumerable<Exception> previous) failures = previous.Concat(failures);
        primary.Data[key] = failures.ToArray();
        // Entered Unity effects retain their owner even without a native cleanup
        // attempt. Preserve that error without classifying a known hold as an
        // unknown native free in the aggregate's cleanup chain.
        if (_cleanupException is not null) primary.Data[CleanupFailureKey] = _cleanupException;
        else if (scopeFailure?.Data[CleanupFailureKey] is Exception scopeCleanup)
            primary.Data[CleanupFailureKey] = scopeCleanup;
        else if (releaseFailure?.Data[CleanupFailureKey] is Exception releaseCleanup)
            primary.Data[CleanupFailureKey] = releaseCleanup;
    }

    private void ReserveOwner()
    {
        lock (OwnerGate)
        {
            if (Owners.Count >= MaximumUnclosedOwners)
                throw new NotSupportedException("The bounded Canvas position owner capacity is exhausted before creating a native source.");
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

    private void AttachOwner(Exception error)
    {
        if (_sourceCapture is not null) error.Data["SeekCanvasBasisSourceCapture"] = _sourceCapture;
        if (_sourceCleanupOwner is not null)
            error.Data[SourceCleanupOwnerKey] = _sourceCleanupOwner;
        error.Data[CleanupOwnerKey] = this;
        if (_externalEffects || _constructionFailure is not null) error.Data[EffectsOwnerKey] = this;
        if (_cleanupException is not null) error.Data[CleanupFailureKey] = _cleanupException;
    }

    private void AdoptCleanupResponsibility(Exception error)
    {
        AdoptSourceCapture(error);
        if (!error.Data.Contains(CleanupFailureKey)) return;
        if (error.Data[SourceCleanupOwnerKey] is object originalOwner && !ReferenceEquals(originalOwner, this))
            _sourceCleanupOwner ??= originalOwner;
        else if (error.Data[CleanupOwnerKey] is object sourceOwner && !ReferenceEquals(sourceOwner, this))
            _sourceCleanupOwner ??= sourceOwner;
        _cleanupException ??= error.Data[CleanupFailureKey] as Exception ?? error;
        _cleanupFailure ??= _cleanupException.Message;
        _retained = true; AttachOwner(error);
    }

    private void AdoptSourceCapture(Exception error)
    {
        _sourceCapture ??= error.Data["SeekCanvasBasisSourceCapture"] as NativeSeekCanvasTargetCapture
            ?? error.Data["SeekCanvasSourceCleanupOwner"] as NativeSeekCanvasTargetCapture
            ?? error.Data[SourceCleanupOwnerKey] as NativeSeekCanvasTargetCapture;
    }

    private bool MarkObservedNativeConflict(Exception error)
    {
        if (error.Data[NativeSeekCanvasPositionConflict.Key] is not NativeSeekCanvasPositionConflict conflict) return false;
        var pending = _pendingCommand >= 0 ? _commands[_pendingCommand] : null;
        bool RetainedOccurrence(int ordinal, long transform)
            => (uint)ordinal < (uint)_targets.Length && _targets[ordinal].Transform == transform;
        bool PendingBits(NativeSeekCanvasPositionBits bits)
            => pending is not null && (bits == NativeSeekCanvasPositionBits.From(pending.Before)
                || bits == NativeSeekCanvasPositionBits.From(pending.Target));
        var pendingTransition = conflict.MultipleSamples && pending is not null
            && conflict.Transform == pending.Transform && RetainedOccurrence(conflict.Ordinal, conflict.Transform)
            && PendingBits(conflict.FirstBits) && PendingBits(conflict.SecondBits);
        if (!pendingTransition) { MarkUnrecognized(); return false; }

        bool Recognized(NativeSeekCanvasPositionSample sample, int ordinal)
        {
            if (sample.Ordinal != ordinal || !RetainedOccurrence(ordinal, sample.Transform)) return false;
            return sample.Transform == pending!.Transform ? PendingBits(sample.Bits)
                : sample.Bits == NativeSeekCanvasPositionBits.From(_expected[ordinal]);
        }
        if (conflict.FirstSnapshot is not null || conflict.SecondSnapshot is not null)
        {
            var first = conflict.FirstSnapshot; var second = conflict.SecondSnapshot;
            if (first is null || second is null || first.Count != _targets.Length || second.Count != _targets.Length
                || _expected.Length != _targets.Length)
            { MarkUnrecognized(); return false; }
            for (var ordinal = 0; ordinal < _targets.Length; ++ordinal)
                if (!Recognized(first[ordinal], ordinal) || !Recognized(second[ordinal], ordinal))
                { MarkUnrecognized(); return false; }
        }
        // Retain the exact admitted before/target transition as pending. No
        // later observation may hide another sampled occurrence's third state.
        return true;
    }

    private Exception Responsibility(string reason)
    {
        _retained = true; var error = new InvalidOperationException(reason); AttachOwner(error); return error;
    }

    public void Dispose()
    {
        CheckThread(); AdvanceObservationRevision(); RevokeConfirmations();
        if (_released) return;
        if (_access is null)
        {
            _disposed = true;
            if (_constructionFailure is not null)
                throw Responsibility("Actual Canvas position construction retains its exact source owner and unknown cleanup failure.");
            _released = true; ReleaseOwner(); return;
        }
        _access.VerifyOwnerThread();
        _disposed = true; _appliedValuesVerified = false; _restoredValuesVerified = false;
        if (_cleanupBusy || _notifyingDisposal) return;
        if (!_disposalNotified)
        {
            _disposalNotified = true; _notifyingDisposal = true;
            try { _access.RequestDispose(); }
            catch (Exception error)
            {
                AdoptCleanupResponsibility(error);
                _cleanupException ??= error; _cleanupFailure ??= error.Message; _retained = true;
            }
            finally { _notifyingDisposal = false; }
        }
        if (!_busy) Release();
    }

    private void Release()
    {
        if (_released || _cleanupBusy) return;
        if (_externalEffects && !_targetOwnershipAcknowledged)
            throw Responsibility("Entered Unity Canvas position setters retain engine effects and independent roots after selected value readback or restoration.");
        if (_cleanupFailure is not null || _cleanupAttempted)
            throw Responsibility("Unknown actual Canvas native cleanup is not retried: " + _cleanupFailure);
        _cleanupAttempted = true; _cleanupBusy = true;
        try { _access.Dispose(); _released = true; }
        catch (Exception error)
        {
            AdoptCleanupResponsibility(error);
            _cleanupException ??= error; _cleanupFailure ??= error.Message; _retained = true;
            AttachOwner(error); throw;
        }
        finally { _cleanupBusy = false; }
        _retained = false; ReleaseOwner();
    }
}
