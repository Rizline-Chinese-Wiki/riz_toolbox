namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekHoldGeometryCommands
{
    private static readonly object ResponsibilityGate = new();
    private static readonly HashSet<NativeSeekHoldGeometryCommands> Responsibilities = new();
    internal static NativeSeekHoldGeometryCommands Prepare(Func<INativeSeekHoldGeometryAccess> createAccess, SeekHoldGeometryRequest request)
    {
        ArgumentNullException.ThrowIfNull(createAccess);
        var value = new NativeSeekHoldGeometryCommands(null); value.ReserveOwner();
        try { value._access = createAccess() ?? throw new InvalidOperationException("The independent actual Hold geometry source was not created."); }
        catch (Exception error)
        {
            value._disposed = true; value._failure = error.Message;
            if (error.Data.Contains("SeekHoldGeometryCleanupFailure") || error.Data.Contains("SeekHoldStateCleanupFailure")
                || error.Data.Contains("SeekRuntimeNoteCleanupFailure") || error.Data.Contains("SeekNoteFlagsCleanupFailure"))
            {
                value._constructionFailure = error; value._cleanupFailure = error.Message; value._retained = true;
                foreach (var prefix in new[] { "SeekHoldGeometry", "SeekHoldState", "SeekRuntimeNote", "SeekNoteFlags" })
                    if (error.Data[prefix + "CleanupOwner"] is object sourceOwner)
                    { error.Data["SeekHoldGeometrySourceCleanupOwner"] = sourceOwner; break; }
                error.Data["SeekHoldGeometryEffectsOwner"] = value;
            }
            else value.ReleaseOwner();
            throw;
        }
        return PrepareOwned(value, request);
    }
    internal static NativeSeekHoldGeometryCommands Prepare(INativeSeekHoldGeometryAccess access, SeekHoldGeometryRequest request)
    {
        ArgumentNullException.ThrowIfNull(access); access.VerifyOwnerThread();
        var value = new NativeSeekHoldGeometryCommands(access); value.ReserveOwner(); return PrepareOwned(value, request);
    }
    private static NativeSeekHoldGeometryCommands PrepareOwned(NativeSeekHoldGeometryCommands value, SeekHoldGeometryRequest request)
    {
        try
        {
            value._access.BindLifetimeGuard(value.Lifetime);
            value.Operation(() =>
            {
                value.VerifySource(); value._baseline = value.Read(); SeekHoldGeometryState.Validate(value._baseline);
                value._current = value._baseline;
                foreach (var note in value._baseline.Notes) value._noteMap.Add(note.Reference, note);
                foreach (var transform in value._baseline.Transforms) value._transforms.Add(transform.Reference, transform);
                value._plan = value.Evaluate(request); value._rows = new SeekHoldGeometryCommandEffects?[value._plan.Steps.Count];
                value._noteTargets = value._baseline.Notes.Select(row => new SeekHoldNoteTarget(row.Reference, row.IsStaticNote, row.LastFrameHoldLineLength)).ToArray();
                value._lastPendingLength = value._baseline.Control.HoldLineLength;
                value.VerifySource();
                if (!SeekHoldGeometryState.Same(value.Read(), value._baseline))
                    throw new InvalidOperationException("The complete actual Hold geometry changed during preparation.");
                value._prepared = true; return 0;
            });
            return value;
        }
        catch (Exception error)
        {
            try { value.Dispose(); }
            catch (Exception cleanup) { error.Data["SeekHoldGeometryEffectsOwner"] = value; error.Data["SeekHoldGeometryCleanupFailure"] = cleanup; }
            throw;
        }
    }
    private void Lifetime()
    {
        VerifyRegisteredOwner();
        _access.VerifyOwnerThread();
        if (_disposed) throw new ObjectDisposedException(nameof(NativeSeekHoldGeometryCommands));
    }
    private void Local() { Lifetime(); _access.VerifyLocal(); }
    private void VerifySource() { Local(); _access.VerifySource(); Local(); }
    private T Operation<T>(Func<T> operation)
    {
        _access.VerifyOwnerThread(); Lifetime();
        if (_busy || _cleanupBusy) throw new InvalidOperationException("A Hold geometry operation cannot reenter itself.");
        _busy = true; Exception? failure = null;
        try { Local(); var value = operation(); Local(); return value; }
        catch (Exception error)
        {
            _transformOwnershipAcknowledged = false;
            failure = error; _failure ??= error.Message;
            if (_attempted != 0) { _unknown = true; _retained = true; error.Data["SeekHoldGeometryEffectsOwner"] = this; }
            throw;
        }
        finally
        {
            _busy = false;
            if (_disposed)
                try { Release(); }
                catch (Exception cleanup)
                {
                    if (failure is null) throw;
                    failure.Data["SeekHoldGeometryEffectsOwner"] = this; failure.Data["SeekHoldGeometryCleanupFailure"] = cleanup;
                }
        }
    }
    private SeekHoldGeometrySnapshot Read(int? selected = null)
    {
        Local(); var raw = _access.ReadSnapshot(selected); Local();
        var copy = selected is null ? SeekHoldGeometryState.Copy(raw, Local) : SeekHoldGeometryState.CopySelected(raw, _current!, Local);
        if (selected is int ordinal && copy.Notes[0].Reference != _baseline!.Notes[ordinal].Reference)
            throw new InvalidOperationException("The selected actual Hold geometry receiver changed.");
        return copy;
    }
    private bool MatchesCurrent(SeekHoldGeometrySnapshot selected)
        => selected.Notes.All(row => _noteMap.TryGetValue(row.Reference, out var expected) && SeekHoldGeometryState.Same(row, expected))
            && selected.Transforms.All(row => _transforms.TryGetValue(row.Reference, out var expected) && SeekHoldGeometryState.Same(row, expected));
    private SeekHoldGeometrySnapshot CurrentSnapshot()
        => _current! with { Transforms = Array.AsReadOnly(_current!.Transforms.Select(row => _transforms[row.Reference]).ToArray()) };
    private void ReserveOwner()
    {
        lock (ResponsibilityGate)
        {
            if (Responsibilities.Count >= MaximumUnclosedOwners)
                throw new NotSupportedException("The bounded Hold geometry effects-owner capacity is exhausted before creating a new native source.");
            Responsibilities.Add(this); _ownerReserved = true;
        }
    }
    private void ReleaseOwner()
    {
        lock (ResponsibilityGate)
        {
            if (!_ownerReserved) return;
            Responsibilities.Remove(this); _ownerReserved = false;
        }
    }
    private Exception ResponsibilityError(string message)
    {
        _retained = true; var error = new InvalidOperationException(message); error.Data["SeekHoldGeometryEffectsOwner"] = this; return error;
    }
    public void Dispose()
    {
        VerifyRegisteredOwner();
        if (_access is null)
        {
            if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("The retained Hold geometry construction responsibility belongs to its owner thread.");
            _disposed = true;
            if (_constructionFailure is not null) throw ResponsibilityError("Hold geometry construction retains its original source and unknown cleanup failure.");
            ReleaseOwner(); return;
        }
        _access.VerifyOwnerThread();
        if (_cleanupBusy || _notifyingDisposal) return;
        _disposed = true;
        if (!_disposalNotified)
        {
            _disposalNotified = true; _notifyingDisposal = true;
            try { _access.RequestDispose(); }
            catch (Exception error) { _cleanupFailure ??= error.Message; _retained = true; }
            finally { _notifyingDisposal = false; }
        }
        if (!_busy) Release();
    }
    private void Release()
    {
        if (_released || _cleanupBusy) return;
        if (_unknown || _externalEffects && !_transformOwnershipAcknowledged)
            throw ResponsibilityError("Hold Transform callbacks or entered effects remain unclosed; logical disposal retains independent roots without an inverse or retry.");
        if (_cleanupFailure is not null || _cleanupAttempted)
            throw ResponsibilityError("Hold geometry cleanup retains an unknown native responsibility and is not retried: " + _cleanupFailure);
        _cleanupBusy = true; _cleanupAttempted = true;
        try { _access.Dispose(); _released = true; }
        catch (Exception error) { _cleanupFailure ??= error.Message; _retained = true; error.Data["SeekHoldGeometryEffectsOwner"] = this; throw; }
        finally { _cleanupBusy = false; }
        _retained = false; ReleaseOwner();
    }
}
