namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekHoldStateParticipant
{
    private static readonly object ResponsibilityGate = new();
    private static readonly HashSet<NativeSeekHoldStateParticipant> Responsibilities = new();

    internal static NativeSeekHoldStateParticipant Prepare(INativeSeekHoldStateAccess access, SeekHoldStateTargets targets)
    {
        ArgumentNullException.ThrowIfNull(access); access.VerifyOwnerThread();
        var value = new NativeSeekHoldStateParticipant(access); value.ReserveOwner();
        return PrepareOwned(value, targets);
    }
    internal static NativeSeekHoldStateParticipant Prepare(Func<INativeSeekHoldStateAccess> createAccess, SeekHoldStateTargets targets)
    {
        ArgumentNullException.ThrowIfNull(createAccess);
        var value = new NativeSeekHoldStateParticipant(null); value.ReserveOwner();
        try { value._access = createAccess() ?? throw new InvalidOperationException("The independent actual Hold access was not created."); }
        catch (Exception error)
        {
            value._disposeRequested = true; value.Failure = error.Message;
            if (error.Data.Contains("SeekHoldStateCleanupFailure") || error.Data.Contains("SeekRuntimeNoteCleanupFailure"))
            {
                // Preserve the exact constructor exception and its source-owned
                // wrappers before replacing the public cleanup-owner key.
                value._constructionFailure = error; value.CleanupFailure = error.Message; value._retained = true;
                error.Data["SeekHoldStateSourceCleanupOwner"] = error.Data["SeekHoldStateCleanupOwner"];
                error.Data["SeekHoldStateCleanupOwner"] = value;
            }
            else value.ReleaseOwner();
            throw;
        }
        return PrepareOwned(value, targets);
    }
    private static NativeSeekHoldStateParticipant PrepareOwned(NativeSeekHoldStateParticipant value, SeekHoldStateTargets targets)
    {
        try
        {
            value._access.VerifyOwnerThread(); value._access.BindLifetimeGuard(value.Lifetime);
            value.Operation(() =>
            {
                ArgumentNullException.ThrowIfNull(targets);
                value.VerifySource(); value.CaptureReferences();
                value._baseline = value.ReadSnapshot();
                value._knownBackings.Add(value._baseline.Identity.Notes.RenderNotes);
                value._knownBackings.Add(value._baseline.Identity.Notes.AllNotes);
                value._expectedLists = value._baseline.Lists; value._expectedControl = value._baseline.Control;
                value._expectedHolds = value._baseline.Notes.Select(row => row with { }).ToArray();
                value.CopyTargets(targets); value.ValidateListsAndBudget();
                foreach (var slot in value._representatives)
                {
                    var array = SeekHoldState.Array(SeekHoldState.At(value._baseline.Lists, slot));
                    value._baselineArrays[array.Reference] = array; value._knownBackings.Add(array.Reference);
                }
                value._expectedArrays = new(value._baselineArrays);
                value.VerifySource(); value.RequireAllOwned(); return 0;
            });
            return value;
        }
        catch (Exception error)
        {
            try { value.Dispose(); }
            catch (Exception cleanup) { error.Data["SeekHoldStateCleanupOwner"] = value; error.Data["SeekHoldStateCleanupFailure"] = cleanup; }
            throw;
        }
    }
    private void CaptureReferences()
    {
        Local(); var notes = _access.RuntimeHolds; Local();
        if (notes is null) throw new InvalidDataException("The independent actual type-2 Hold subset is absent.");
        Local(); var count = notes.Count; Local();
        if (count is < 0 or > MaximumCapacity) throw new InvalidDataException("The actual Hold subset exceeds its bound.");
        var references = new long[count]; var seen = new HashSet<long>();
        for (var ordinal = 0; ordinal < count; ++ordinal)
        {
            Local(); var note = notes[ordinal]; Local();
            if (note is null || note.Reference == 0 || !seen.Add(note.Reference))
                throw new InvalidDataException("The actual Hold subset contains an absent or duplicate instance identity.");
            references[ordinal] = note.Reference;
        }
        Local(); if (notes.Count != count) throw new InvalidOperationException("The actual Hold subset changed while being copied."); Local();
        _references = references;
    }
    private void CopyTargets(SeekHoldStateTargets targets)
    {
        _listTargets = new[] { SeekHoldState.Copy(targets.HoldNotes, Local), SeekHoldState.Copy(targets.RunningHoldNotes, Local) };
        var copied = SeekHoldState.Copy(targets.Notes, Local, true);
        if (copied.Length != _references.Length)
            throw new InvalidDataException("Explicit Hold scalar targets must cover every actual Hold instance exactly once.");
        var byReference = copied.ToDictionary(row => row.Reference);
        _holdTargets = _references.Select(reference => byReference.TryGetValue(reference, out var row) ? row
            : throw new InvalidDataException("An actual Hold instance has no explicit scalar target.")).ToArray();
        _controlTarget = SeekHoldState.Copy(targets.Control, Local, true);
    }
    private void ValidateListsAndBudget()
    {
        var actual = new HashSet<long>(_references); var representatives = new List<int>();
        var arrays = new HashSet<long>(); long total = 0;
        for (var slot = 0; slot < 2; ++slot)
        {
            var list = SeekHoldState.At(_baseline.Lists, slot);
            if (list.CapacitySlots.Take(list.Size).Concat(_listTargets[slot]).Any(reference => reference == 0 || !actual.Contains(reference)))
                throw new InvalidDataException("Every active Hold List occurrence and explicit target must refer to an actual type-2 Hold instance.");
            var alias = representatives.FindIndex(previous => SeekHoldState.At(_baseline.Lists, previous).Reference == list.Reference);
            if (alias >= 0)
            {
                if (!_listTargets[representatives[alias]].SequenceEqual(_listTargets[slot]))
                    throw new InvalidDataException("The same actual Hold List requires identical explicit occurrence targets.");
                continue;
            }
            foreach (var previous in representatives)
            {
                var other = SeekHoldState.At(_baseline.Lists, previous);
                if (other.Backing == list.Backing && list.CapacitySlots.Count != 0)
                    throw new NotSupportedException("Distinct Hold Lists sharing a nonempty backing are not supported by this writer.");
            }
            long capacity = list.CapacitySlots.Count;
            if (arrays.Add(list.Backing)) total += capacity;
            while (capacity < _listTargets[slot].Length)
            {
                capacity = capacity == 0 ? 4 : capacity * 2;
                if (capacity > MaximumCapacity)
                    throw new NotSupportedException("The explicit Hold target would grow official List capacity beyond its complete retained bound.");
                total += capacity;
            }
            if (total > MaximumTotalRetainedCapacity)
                throw new NotSupportedException("The explicit Hold targets exceed the complete original and historical backing budget.");
            representatives.Add(slot);
        }
        _representatives = representatives.ToArray();
    }
    private void Lifetime()
    {
        _access.VerifyOwnerThread();
        if (_disposeRequested) throw new ObjectDisposedException(nameof(NativeSeekHoldStateParticipant));
    }
    private void Local() { Lifetime(); _access.VerifyLocal(); }
    private void VerifySource() { Local(); _access.VerifySource(); Local(); }
    private T Operation<T>(Func<T> operation, Action? onFailure = null, Action? onEntering = null)
    {
        _access.VerifyOwnerThread(); Lifetime();
        if (_busy || _cleanupBusy) throw new InvalidOperationException("A Hold-state operation cannot reenter itself.");
        _busy = true; Exception? failure = null;
        try { onEntering?.Invoke(); Local(); var result = operation(); Local(); return result; }
        catch (Exception error)
        {
            failure = error;
            onFailure?.Invoke();
            if (!_operationErrors.Contains(error.Message)) _operationErrors.Add(error.Message);
            Failure = string.Join("; ", _operationErrors);
            if (_hasDispatched && !_restored) { SideEffectsUnknown = true; error.Data["SeekHoldStateCleanupOwner"] = this; }
            throw;
        }
        finally
        {
            _busy = false;
            if (_disposeRequested)
                try { Release(); }
                catch (Exception cleanup)
                {
                    if (failure is null) throw;
                    failure.Data["SeekHoldStateCleanupOwner"] = this; failure.Data["SeekHoldStateCleanupFailure"] = cleanup;
                }
        }
    }
    private void ReserveOwner()
    {
        lock (ResponsibilityGate)
        {
            if (Responsibilities.Count >= MaximumUnclosedOwners)
                throw new NotSupportedException("The bounded Hold-state owner capacity is exhausted before creating a new native source.");
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
        _retained = true; var error = new InvalidOperationException(message);
        error.Data["SeekHoldStateCleanupOwner"] = this; return error;
    }
    public void Dispose()
    {
        if (_access is null)
        {
            if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("The retained Hold source construction responsibility belongs to its owner thread.");
            _disposeRequested = true;
            if (_constructionFailure is not null) throw ResponsibilityError("Hold source construction retains its original owner and unknown cleanup failure.");
            ReleaseOwner(); return;
        }
        _access.VerifyOwnerThread();
        if (_cleanupBusy || _notifyingDisposal) return;
        _disposeRequested = true;
        if (_hasDispatched && !_confirmed && !_restored) SideEffectsUnknown = true;
        if (!_disposalNotified)
        {
            _disposalNotified = true; _notifyingDisposal = true;
            try { _access.RequestDispose(); }
            catch (Exception error) { CleanupFailure ??= error.Message; _retained = true; }
            finally { _notifyingDisposal = false; }
        }
        if (!_busy) Release();
    }
    private void Release()
    {
        if (_released || _cleanupBusy) return;
        if (_pending is not null || _unrecognized || _hasDispatched && !_confirmed && !_restored)
            throw ResponsibilityError("Hold state retains an unresolved dispatch or unconfirmed inverse responsibility; logical disposal keeps its independent owner and roots.");
        if (CleanupFailure is not null || _cleanupAttempted)
            throw ResponsibilityError("Hold source cleanup retains an unknown responsibility and is not retried: " + CleanupFailure);
        _cleanupBusy = true; _cleanupAttempted = true;
        try { _access.Dispose(); _released = true; }
        catch (Exception error) { CleanupFailure ??= error.Message; _retained = true; error.Data["SeekHoldStateCleanupOwner"] = this; throw; }
        finally { _cleanupBusy = false; }
        _retained = false; ReleaseOwner();
    }
}
