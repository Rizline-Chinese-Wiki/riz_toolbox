using System.Runtime.ExceptionServices;
using RizToolbox.Runtime;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekCanvasCacheParticipant
{
    internal static NativeSeekCanvasCacheParticipant Prepare(Func<INativeSeekCanvasCacheAccess> createAccess)
    {
        ArgumentNullException.ThrowIfNull(createAccess);
        var value = Reserve(); value.PrepareReserved(createAccess); return value;
    }

    internal static NativeSeekCanvasCacheParticipant Reserve()
    {
        var value = new NativeSeekCanvasCacheParticipant(); value.ReserveOwner(); return value;
    }

    internal void PrepareReserved(Func<INativeSeekCanvasCacheAccess> createAccess)
    {
        ArgumentNullException.ThrowIfNull(createAccess); CheckThread();
        if (!_ownerReserved || _disposed || _preparationEntered || _busy || _cleanupBusy || _notifyingDisposal)
            throw new InvalidOperationException("An original reserved actual Canvas cache owner can attach one access only.");
        _preparationEntered = true;
        try { _access = createAccess() ?? throw new InvalidOperationException("No independent actual Canvas cache access was created."); }
        catch (Exception error)
        {
            _disposed = true; _failure = error.Message;
            AdoptCleanupResponsibility(error);
            if (_cleanupException is not null)
            {
                _constructionFailure = error; UpdateResponsibility(); AttachOwner(error);
            }
            else { _released = true; ReleaseOwner(); }
            throw;
        }
        try
        {
            _sourceCapture = _access.SourceCapture
                ?? throw new InvalidOperationException("The actual Canvas cache access has no original source capture.");
            Operation(() =>
            {
                _access.BindLifetimeGuard(Lifetime); Local();
                _slots = CopySlots(); BuildUniqueSlots();
                _baseline = Read(); _expected = _baseline.ToArray();
                if (!Read().SequenceEqual(_baseline))
                    throw new InvalidOperationException("The complete actual Canvas cache raw-bit baseline changed during preparation.");
                VerifySlots(); VerifySource(); _prepared = true; return 0;
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
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Actual Canvas cache storage belongs to its original owner thread.");
    }

    private void DiagnosticEntry()
    {
        CheckThread();
        if (_busy || _cleanupBusy || _notifyingDisposal)
            throw new InvalidOperationException("Actual Canvas cache diagnostics cannot reenter an active operation.");
    }

    private void Lifetime()
    {
        CheckThread();
        if (_disposed) throw new ObjectDisposedException(nameof(NativeSeekCanvasCacheParticipant));
        if (_cleanupException is not null)
            throw Responsibility("Actual Canvas cache access retains its original unknown native cleanup; no further access or cleanup retry is admitted.");
        _access.VerifyOwnerThread();
        if (_disposed) throw new ObjectDisposedException(nameof(NativeSeekCanvasCacheParticipant));
        if (_cleanupException is not null)
            throw Responsibility("Actual Canvas cache access retains its original unknown native cleanup; no further access or cleanup retry is admitted.");
    }

    private void Local() { Lifetime(); _access.VerifyLocal(); Lifetime(); }
    private void VerifySource() { Local(); _access.VerifySource(); Local(); }

    private void AdvanceObservationRevision()
    {
        if (_observationRevision != long.MaxValue) ++_observationRevision;
        if (_observationRevision != long.MaxValue) return;
        RevokeConfirmations();
        const string reason = "The bounded actual Canvas cache observation revision is exhausted; its original managed owner remains retained without a native cleanup claim.";
        _failure ??= reason;
        var error = new NotSupportedException(reason);
        if (!_released) { _retained = true; AttachOwner(error); }
        throw error;
    }

    internal void RevokeBasisConfirmations()
    {
        CheckThread(); AdvanceObservationRevision(); RevokeConfirmations();
    }

    private T Operation<T>(Func<T> operation, SeekCanvasCacheDirection? confirmation = null)
    {
        CheckThread(); AdvanceObservationRevision();
        // Revoke before reentry, lifetime, or shared-source admission can fail.
        if (confirmation is not null) RevokeConfirmations();
        if (_busy || _cleanupBusy || _notifyingDisposal)
            throw new InvalidOperationException("An actual Canvas cache operation cannot reenter itself.");
        _busy = true; Exception? failure = null; IDisposable? scope = null;
        try
        {
            if (_access is null) throw new InvalidOperationException("Prepare the reserved actual Canvas cache owner before using it.");
            scope = _access.EnterOperation();
            Local(); var result = operation(); Local(); UpdateResponsibility(); return result;
        }
        catch (Exception error)
        {
            failure = error; RecordOperationFailure(error); throw;
        }
        finally
        {
            Exception? scopeFailure = null, releaseFailure = null;
            // The shared managed scope must end before the original child is
            // allowed to surrender its physical-source lease.
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
        _failure ??= error.Message; MarkObservedNativeConflict(error);
        AdoptCleanupResponsibility(error); RevokeConfirmations();
        if (_retained) AttachOwner(error);
    }

    private void AttachOperationCleanup(Exception primary, Exception? scopeFailure, Exception? releaseFailure)
    {
        AttachOwner(primary);
        const string key = "SeekCanvasCacheOperationCleanupFailures";
        var failures = new[] { scopeFailure, releaseFailure }.Where(error => error is not null).Cast<Exception>();
        if (primary.Data[key] is IEnumerable<Exception> previous) failures = previous.Concat(failures);
        primary.Data[key] = failures.ToArray();
        // Refusing to surrender an unconfirmed storage owner is a managed
        // responsibility failure, not evidence that a native free was attempted.
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
                throw new NotSupportedException("The bounded Canvas cache owner capacity is exhausted before creating a native source.");
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
        if (_sourceCleanupOwner is not null) error.Data[SourceCleanupOwnerKey] = _sourceCleanupOwner;
        error.Data[CleanupOwnerKey] = this;
        if (_cleanupException is not null) error.Data[CleanupFailureKey] = _cleanupException;
    }

    private bool MarkObservedNativeConflict(Exception error)
    {
        // A confirmed native observation remains a third state even if an
        // outside writer returns the storage to expected bits before our readback.
        if (error.Data[NativeCanvasCacheConflict.Key] is not NativeCanvasCacheConflict conflict) return false;
        if (_expected.Length != _slots.Length || conflict.Observations.Count == 0)
        {
            MarkUnrecognized(); return false;
        }
        var expectedByPhysical = new Dictionary<(long Owner, int Index, SeekCanvasCacheKind? Scalar), int>();
        foreach (var slot in _uniqueSlots)
            expectedByPhysical.Add((slot.Owner, slot.Index, slot.Index < 0 ? slot.Kind : (SeekCanvasCacheKind?)null), slot.Ordinal);
        var pending = _pendingStore >= 0 ? _stores[_pendingStore] : null;
        var pendingKey = pending is null ? default : (pending.Owner, pending.Index,
            pending.Index < 0 ? pending.Kind : (SeekCanvasCacheKind?)null);
        foreach (var sample in conflict.Observations)
        {
            if (!TryConflictPhysicalKey(sample.Owner, sample.Index, sample.Field, out var key)
                || !expectedByPhysical.TryGetValue(key, out var ordinal)
                || (pending is not null && key == pendingKey
                    ? sample.Bits != pending.BeforeBits && sample.Bits != pending.TargetBits
                    : sample.Bits != _expected[ordinal]))
            {
                MarkUnrecognized(); return false;
            }
        }
        if (pending is not null && conflict.MultipleSamples
            && TryConflictPhysicalKey(conflict.Owner, conflict.Index, conflict.Field, out var conflictKey) && conflictKey == pendingKey
            && (conflict.ExpectedBits == pending.BeforeBits || conflict.ExpectedBits == pending.TargetBits)
            && (conflict.ObservedBits == pending.BeforeBits || conflict.ObservedBits == pending.TargetBits))
            return true; // Only this exact pending physical slot may transition.
        MarkUnrecognized(); return false;
    }

    private static bool TryConflictPhysicalKey(long owner, int index, string field,
        out (long Owner, int Index, SeekCanvasCacheKind? Scalar) key)
    {
        key = default;
        if (owner == 0 || index < -1) return false;
        if (index >= 0) { key = (owner, index, null); return true; }
        var scalar = field switch
        {
            "nowTime" or nameof(SeekCanvasCacheKind.NowTime) => SeekCanvasCacheKind.NowTime,
            "speed" or nameof(SeekCanvasCacheKind.Speed) => SeekCanvasCacheKind.Speed,
            _ => (SeekCanvasCacheKind?)null
        };
        if (scalar is null) return false;
        key = (owner, -1, scalar); return true;
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
        _cleanupFailure ??= _cleanupException.Message; RevokeConfirmations(); AttachOwner(error);
    }

    private void AdoptSourceCapture(Exception error)
    {
        _sourceCapture ??= error.Data["SeekCanvasBasisSourceCapture"] as NativeSeekCanvasTargetCapture
            ?? error.Data["SeekCanvasSourceCleanupOwner"] as NativeSeekCanvasTargetCapture
            ?? error.Data[SourceCleanupOwnerKey] as NativeSeekCanvasTargetCapture;
    }

    private Exception Responsibility(string reason)
    {
        _retained = true; var error = new InvalidOperationException(reason); AttachOwner(error); return error;
    }

    public void Dispose()
    {
        CheckThread(); AdvanceObservationRevision();
        if (_released) return;
        if (_cleanupBusy || _notifyingDisposal) { _disposed = true; return; }
        // Unknown temporary frees preclude even owner/source/native verification.
        if (_cleanupException is not null || _constructionFailure is not null)
        {
            _disposed = true; RevokeConfirmations();
            throw Responsibility("Actual Canvas cache cleanup retains its exact original source owner and unknown native outcome; cleanup is not retried.");
        }
        if (_access is null) { _disposed = true; _released = true; ReleaseOwner(); return; }

        Exception? failure = null;
        if (!_busy && !_disposed && _hasDispatched && CacheResponsibilityClosed)
        {
            try
            {
                Operation(() =>
                {
                    var applied = _appliedValuesVerified; var restored = _restoredValuesVerified;
                    var actual = Observe(); VerifySource();
                    if (_unrecognized || applied && !MatchesTargets(actual) || restored && !actual.SequenceEqual(_baseline))
                        throw new InvalidOperationException("The latest complete actual Canvas cache readback no longer closes its storage responsibility.");
                    return 0;
                });
            }
            catch (Exception error) { failure = error; }
        }
        _disposed = true;
        if (_busy) RevokeConfirmations();
        if (_cleanupException is null && !_disposalNotified)
        {
            _disposalNotified = true; _notifyingDisposal = true;
            try { _access.RequestDispose(); }
            catch (Exception error)
            {
                AdoptCleanupResponsibility(error);
                _cleanupException ??= error; _cleanupFailure ??= _cleanupException.Message;
                RevokeConfirmations(); failure ??= error;
            }
            finally { _notifyingDisposal = false; }
        }
        if (!_busy)
        {
            try { Release(); }
            catch (Exception)
            {
                if (failure is null) throw;
                AttachOwner(failure);
                if (_cleanupException is not null) failure.Data[CleanupFailureKey] = _cleanupException;
            }
        }
        if (failure is not null) { AttachOwner(failure); ExceptionDispatchInfo.Capture(failure).Throw(); }
    }

    private void Release()
    {
        if (_released || _cleanupBusy) return;
        if (_cleanupException is not null || _cleanupFailure is not null || _cleanupAttempted)
            throw Responsibility("Unknown actual Canvas cache native cleanup is not retried: " + _cleanupFailure);
        if (_dispatchContractUnknown || _hasDispatched && !CacheResponsibilityClosed)
            throw Responsibility("Entered actual Canvas cache storage remains unconfirmed; retain its original independent roots.");
        _cleanupAttempted = true; _cleanupBusy = true;
        try { _access.Dispose(); _released = true; }
        catch (Exception error)
        {
            AdoptCleanupResponsibility(error);
            _cleanupException ??= error; _cleanupFailure ??= _cleanupException.Message;
            RevokeConfirmations(); AttachOwner(error); throw;
        }
        finally { _cleanupBusy = false; }
        _retained = false; ReleaseOwner();
    }
}
