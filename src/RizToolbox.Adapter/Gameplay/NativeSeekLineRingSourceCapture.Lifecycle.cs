using System.Runtime.ExceptionServices;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekLineRingSourceCapture
{
    internal static NativeSeekLineRingSourceCapture Capture(Func<IRetainedSeekLineRingReader> create)
    { ArgumentNullException.ThrowIfNull(create); return Capture(_ => create()); }

    internal static NativeSeekLineRingSourceCapture Capture(Func<Action, IRetainedSeekLineRingReader> create)
    {
        ArgumentNullException.ThrowIfNull(create); var owner = new NativeSeekLineRingSourceCapture();
        lock (OwnerGate)
        {
            if (Owners.Count >= MaximumUnclosedOwners) throw new NotSupportedException("Line/ring capture owner capacity is exhausted before native acquisition.");
            Owners.Add(owner); owner._ownerReserved = true;
        }
        try
        {
            owner._reader = create(owner.Lifetime) ?? throw new InvalidOperationException("An actual retained line/ring reader is required.");
            owner._canvasCapture = owner._reader.SourceCapture;
            owner.Operation(() =>
            {
                owner._reader.BindLifetimeGuard(owner.Lifetime);
                owner._reader.VerifySource();
                // Copy from the original Canvas capture while this line reader's
                // independent managed hold and shared operation are both active.
                // Later reads never borrow the capture's private baseline.
                var numericBasis = owner._canvasCapture.CopyNumericBasis();
                var first = owner.Read(); var second = owner.Read();
                if (!SeekLineRingSourceState.Same(first, second, baseline: true))
                    throw new InvalidOperationException("The complete actual line/ring source or baseline changed during capture.");
                numericBasis.VerifyAssociation(second.Identity);
                owner._snapshot = second; owner._coverage = SeekLineRingSourceState.Coverage(second);
                owner._reader.VerifySource(); owner.Local(); owner._reader.CompleteCapture(); owner.Local();
                owner._numericBasis = numericBasis; return 0;
            });
            return owner;
        }
        catch (Exception error)
        {
            owner.AdoptCleanup(error); owner._failure ??= error.Message;
            owner._canvasCapture ??= error.Data["SeekCanvasBasisSourceCapture"] as NativeSeekCanvasTargetCapture;
            if (owner._cleanupFailure is not null) owner.Attach(error);
            try { owner.Dispose(); }
            catch (Exception cleanup)
            {
                owner.Attach(error);
                error.Data["SeekLineRingSourceConstructionCleanup"] = cleanup;
            }
            throw;
        }
    }

    private void VerifyThread()
    {
        if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("The line/ring source belongs to its original owner thread.");
    }
    private void DiagnosticEntry()
    {
        VerifyThread();
        if (_busy || _releasing || _notifyingDisposal) throw new InvalidOperationException("Line/ring diagnostics cannot reenter an active operation.");
    }
    private void Lifetime()
    {
        VerifyThread();
        if (!_endpointWriterReleasing) _endpointWriterOwner?.VerifySourceLifetime();
        if (_disposed) throw new ObjectDisposedException(nameof(NativeSeekLineRingSourceCapture));
        if (_cleanupFailure is not null) throw Responsibility("Unknown original line/ring cleanup rejects further native access or retry.");
        if (_sourceExpired) throw new InvalidOperationException("The retained actual line/ring source was already observed expired.");
    }
    private void Local() { Lifetime(); _reader!.VerifyLocal(); Lifetime(); }
    private SeekLineRingSourceSnapshot Read()
    { Local(); var value = _reader!.ReadSnapshot(); Local(); return SeekLineRingSourceState.Copy(value, Local); }

    private T Operation<T>(Func<T> action)
    {
        VerifyThread();
        // Proof revocation precedes every failing lifetime/scope admission.
        _sourceVerified = false; _baselineVerified = false;
        if (_busy || _releasing || _notifyingDisposal) throw new InvalidOperationException("Line/ring operations cannot synchronously reenter.");
        _busy = true; IDisposable? scope = null; Exception? failure = null;
        try
        {
            Lifetime(); scope = _reader!.EnterOperation(); Local(); var result = action(); Local(); return result;
        }
        catch (Exception error)
        {
            failure = error; _failure ??= error.Message;
            if (error.Data["SeekLineRingSourceExpired"] is true && !SeekCanvasBasisCallerWithdrawal.Is(error)) _sourceExpired = true;
            AdoptCleanup(error); throw;
        }
        finally
        {
            Exception? scopeFailure = null, releaseFailure = null;
            try { scope?.Dispose(); }
            catch (Exception error) { scopeFailure = error; AdoptCleanup(error); }
            _busy = false;
            if (_disposed)
                try { Release(); }
                catch (Exception error) { releaseFailure = error; }
            if (scopeFailure is not null || releaseFailure is not null)
            {
                var primary = failure ?? scopeFailure ?? releaseFailure!;
                primary.Data["SeekLineRingSourceOperationCleanupFailures"] = new[] { scopeFailure, releaseFailure }.Where(item => item is not null).ToArray();
                Attach(primary);
                if (failure is null) ExceptionDispatchInfo.Capture(primary).Throw();
            }
        }
    }

    internal void VerifySource() => Verify(baseline: false);
    internal void VerifyBaseline() => Verify(baseline: true);
    private void Verify(bool baseline) => Operation(() => { VerifyInOperation(baseline); return 0; });

    // Call only while this owner's existing shared operation is held. Target
    // projections use the same complete observation contract without nesting a
    // public VerifySource operation or borrowing mutable Canvas baselines.
    private SeekLineRingSourceSnapshot VerifyInOperation(bool baseline)
    {
        if (_snapshot is null) throw new InvalidOperationException("Capture the complete line/ring graph before verification.");
        _reader!.VerifySource(); var first = Read(); var second = Read();
        if (!SeekLineRingSourceState.Same(first, second, baseline: false)
            || !SeekLineRingSourceState.Same(_snapshot, second, baseline: false))
        {
            _sourceExpired = true;
            throw new InvalidOperationException("The original actual line/ring arrays, endpoints, points, groups or chart associations changed.");
        }
        if (baseline && (!SeekLineRingSourceState.Same(first, second, baseline: true)
            || !SeekLineRingSourceState.Same(_snapshot, second, baseline: true)))
            throw new InvalidOperationException("The selected original line/ring baselines changed; this does not replace the retained source.");
        _reader.VerifySource(); Local(); _sourceVerified = true; _baselineVerified = baseline; return second;
    }

    private void AdoptCleanup(Exception error)
    {
        var cleanup = error.Data[FailureKey] as Exception ?? error.Data["SeekCanvasBasisSourceCleanupFailure"] as Exception
            ?? error.Data["NativePublicationCleanupFailure"] as Exception;
        if (cleanup is null) return;
        _cleanupFailure ??= cleanup;
        _sourceCleanupOwner ??= error.Data[SourceOwnerKey] ?? error.Data["NativePublicationCleanupOwner"]
            ?? error.Data["SeekCanvasBasisSourceCleanupOwner"] ?? error.Data[OwnerKey];
        _retained = true; _sourceVerified = false; _baselineVerified = false; Attach(error);
    }
    private void Attach(Exception error)
    {
        error.Data[OwnerKey] = this;
        if (_cleanupFailure is not null) error.Data[FailureKey] = _cleanupFailure;
        if (_sourceCleanupOwner is not null) error.Data[SourceOwnerKey] = _sourceCleanupOwner;
        if (_canvasCapture is not null) error.Data["SeekCanvasBasisSourceCapture"] = _canvasCapture;
    }
    private Exception Responsibility(string reason)
    { _retained = true; var error = new InvalidOperationException(reason); Attach(error); return error; }

    public void Dispose()
    {
        VerifyThread();
        if (_endpointWriterOwner is not null && !_endpointWriterReleasing)
            throw new InvalidOperationException("The original line capture was adopted by its endpoint command owner.");
        _sourceVerified = false; _baselineVerified = false;
        if (_released) return;
        _disposed = true;
        if (_releasing || _notifyingDisposal) return;
        if (_cleanupFailure is not null) throw Responsibility("The exact original line/ring cleanup owner remains retained; native cleanup is not retried.");
        _notifyingDisposal = true;
        try { _reader?.RequestDispose(); }
        catch (Exception error) { AdoptCleanup(error); Attach(error); throw; }
        finally { _notifyingDisposal = false; }
        if (!_busy) Release();
    }
    private void Release()
    {
        if (_released || _releasing) return;
        if (_cleanupFailure is not null || _releaseAttempted)
            throw Responsibility("Unknown line/ring reader cleanup cannot be retried.");
        _releaseAttempted = true; _releasing = true;
        try { _reader?.Dispose(); _released = true; }
        catch (Exception error)
        {
            AdoptCleanup(error); _cleanupFailure ??= error; _retained = true; Attach(error); throw;
        }
        finally { _releasing = false; }
        _retained = false;
        lock (OwnerGate) { if (_ownerReserved) { Owners.Remove(this); _ownerReserved = false; } }
    }
}
