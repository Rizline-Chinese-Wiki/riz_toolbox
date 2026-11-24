namespace RizToolbox.Adapter.Gameplay;

internal sealed record SeekNoteTimeNote(long Reference, bool IsHold);

internal interface INativeSeekNoteTimeAccess : IDisposable
{
    void VerifyOwnerThread();
    void VerifyLocal();
    void BindLifetimeGuard(Action guard);
    IReadOnlyList<SeekNoteTimeNote> Notes { get; }
    IReadOnlyList<int> ReadSnapshot();
    int ReadSlot(int slot);
    void WriteSlot(int slot, int bits, int expectedBits, Action beforeDispatch);
    void RequestDispose();
}

/// <summary>Explicit actual-note Single game-seconds caches only. Ordinary tails
/// are not writable; partial chart coverage does not fabricate missing instances.
/// It does not dispatch consumers, geometry or provide complete Seek admission.</summary>
internal sealed class NativeSeekNoteTimeParticipant : IGameplayValueParticipant
{
    private static readonly object OwnerGate = new();
    private static readonly HashSet<NativeSeekNoteTimeParticipant> Owners = new();
    private INativeSeekNoteTimeAccess _access = null!;
    private int[] _baseline = Array.Empty<int>(), _expected = Array.Empty<int>(), _target = Array.Empty<int>();
    private int _attempted, _pendingSlot = -1, _pendingBits;
    private bool _busy, _entered, _committed, _confirmed, _restoring, _restored, _disposed;
    private bool _cleanupBusy, _cleanupAttempted, _released, _notifyingDisposal, _notified;
    private bool _hasDispatched, _unrecognized, _retained;
    private Exception? _constructionFailure;
    private string? _failure, _cleanupFailure;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    internal int AttemptedWrites => _attempted;
    internal bool SideEffectsUnknown { get; private set; }
    internal bool HasRetainedResponsibility => _retained;
    public string Id => "native.note.explicit-game-seconds-caches";

    internal static NativeSeekNoteTimeParticipant Prepare(Func<INativeSeekNoteTimeAccess> create,
        IReadOnlyList<SeekRuntimeTimeTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(create); ArgumentNullException.ThrowIfNull(targets);
        var value = new NativeSeekNoteTimeParticipant();
        lock (OwnerGate)
        {
            if (Owners.Count >= 256) throw new NotSupportedException("The bounded note-time owner capacity is exhausted.");
            Owners.Add(value);
        }
        try
        {
            value._access = create() ?? throw new InvalidOperationException("No independent note-time access was created.");
            value._access.BindLifetimeGuard(value.Lifetime);
            value.Operation(() =>
            {
                value._baseline = value.Read(); value._expected = value._baseline.ToArray();
                value._target = value.BuildTargets(targets);
                return 0;
            });
            return value;
        }
        catch (Exception error)
        {
            if (value._access is null && error.Data["SeekNoteTimeCleanupFailure"] is Exception)
            {
                value._constructionFailure = error; value._retained = true; value._disposed = true;
                error.Data["SeekNoteTimeSourceCleanupOwner"] = error.Data["SeekNoteTimeCleanupOwner"];
                error.Data["SeekNoteTimeCleanupOwner"] = value;
            }
            else
            {
                try { value.Dispose(); }
                catch (Exception cleanup)
                { error.Data["SeekNoteTimeCleanupOwner"] = value; error.Data["SeekNoteTimeCleanupFailure"] = cleanup; }
            }
            throw;
        }
    }
    private int[] BuildTargets(IReadOnlyList<SeekRuntimeTimeTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var notes = _access.Notes.ToArray(); Local();
        if (targets.Count != notes.Length || notes.Length > NativeSeekRuntimeNoteView.MaximumNotes)
            throw new InvalidDataException("Timing targets must cover every actual note once.");
        var byReference = new Dictionary<long, SeekRuntimeTimeTarget>();
        foreach (var row in targets)
        {
            Local();
            if (byReference.Count >= notes.Length || row is null || row.Reference == 0 || !byReference.TryAdd(row.Reference, row with { }))
                throw new InvalidDataException("Timing targets have missing duplicate or extra references.");
        }
        if (targets.Count != notes.Length || byReference.Count != notes.Length)
            throw new InvalidDataException("Timing target enumeration differs from its complete declared count.");
        var values = new List<int>(notes.Sum(note => note.IsHold ? 2 : 1));
        foreach (var note in notes)
        {
            if (!byReference.TryGetValue(note.Reference, out var target) || !float.IsFinite(target.RealTime)
                || note.IsHold != target.HoldEndRealTime.HasValue || target.HoldEndRealTime is float tail && !float.IsFinite(tail))
                throw new InvalidDataException("Timing targets are foreign nonfinite or have a type-inconsistent tail.");
            values.Add(BitConverter.SingleToInt32Bits(target.RealTime));
            if (note.IsHold) values.Add(BitConverter.SingleToInt32Bits(target.HoldEndRealTime!.Value));
        }
        return values.ToArray();
    }
    private void Lifetime()
    { _access.VerifyOwnerThread(); if (_disposed) throw new ObjectDisposedException(nameof(NativeSeekNoteTimeParticipant)); }
    private void Local() { Lifetime(); _access.VerifyLocal(); }
    private T Operation<T>(Func<T> operation)
    {
        _access.VerifyOwnerThread(); Lifetime();
        if (_busy || _cleanupBusy) throw new InvalidOperationException("Note-time operations cannot reenter themselves.");
        _busy = true; Exception? failure = null;
        try { Local(); var result = operation(); Local(); return result; }
        catch (Exception error)
        {
            failure = error; _failure ??= error.Message; _confirmed = false; _restored = false;
            if (_hasDispatched) { SideEffectsUnknown = true; error.Data["SeekNoteTimeCleanupOwner"] = this; }
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
                    failure.Data["SeekNoteTimeCleanupOwner"] = this;
                    failure.Data["SeekNoteTimeCleanupFailure"] = cleanup;
                }
        }
    }
    private int[] Read()
    {
        Local(); var result = _access.ReadSnapshot().ToArray(); Local();
        if (result.Length != _access.Notes.Sum(note => note.IsHold ? 2 : 1)) throw new InvalidDataException("Incomplete actual note-time storage.");
        return result;
    }
    private int[] Observe()
    {
        var actual = Read();
        if (_pendingSlot >= 0)
        {
            var slot = _pendingSlot;
            if (actual[slot] == _pendingBits || actual[slot] == _expected[slot]) _expected[slot] = actual[slot];
        }
        if (!actual.SequenceEqual(_expected)) { _unrecognized = true; SideEffectsUnknown = true; }
        _pendingSlot = -1; return actual;
    }
    private void Store(int slot, int bits, bool forward)
    {
        Local(); var entered = false;
        _access.WriteSlot(slot, bits, _expected[slot], () =>
        {
            Local();
            if (entered) throw new InvalidOperationException("Note-time setter guard entered twice.");
            if (forward && _attempted != slot) throw new InvalidOperationException("Note-time setter order differs from its prefix.");
            entered = true; _hasDispatched = true; _pendingSlot = slot; _pendingBits = bits; SideEffectsUnknown = true;
            if (forward) ++_attempted;
        });
        Local();
        if (!entered) throw new InvalidOperationException("No note-time setter entry was recorded.");
        _expected[slot] = bits; _pendingSlot = -1;
        var observed = _access.ReadSlot(slot); Local();
        if (observed != bits) { _unrecognized = true; throw new InvalidOperationException("Note-time setter readback differs."); }
    }
    public void VerifyPrepared() => Operation(() =>
    {
        if (_entered || _restoring) throw new InvalidOperationException("Preparation was already entered.");
        if (!Observe().SequenceEqual(_baseline)) throw new InvalidOperationException("Note-time baseline changed.");
        return 0;
    });
    public void Commit() => Operation(() =>
    {
        if (_entered || _restoring) throw new InvalidOperationException("Note-time is once-only.");
        _entered = true;
        if (!Observe().SequenceEqual(_baseline)) throw new InvalidOperationException("Note-time storage changed before commit.");
        for (var slot = 0; slot < _target.Length; ++slot) Store(slot, _target[slot], true);
        if (!Observe().SequenceEqual(_target) || _unrecognized) throw new InvalidOperationException("Note-time has an unrecognized final state.");
        _committed = true; return 0;
    });
    private void RevokeConfirmation(bool restoration)
    {
        _access.VerifyOwnerThread();
        if (_busy || _cleanupBusy) throw new InvalidOperationException("Note-time confirmation cannot reenter itself.");
        if (restoration) _restored = false; else _confirmed = false;
    }
    public GameplayControlConfirmation Confirm()
    {
        RevokeConfirmation(false);
        return Operation(() =>
        {
            if (!_committed || _restoring) throw new InvalidOperationException("Commit before confirmation.");
            _confirmed = Observe().SequenceEqual(_target) && !_unrecognized;
            SideEffectsUnknown = !_confirmed;
            return new GameplayControlConfirmation(_confirmed, "Exact selected note game-seconds cache readback only; no geometry, consumers or full Seek.");
        });
    }
    public void Restore() => Operation(() =>
    {
        if (_restoring) throw new InvalidOperationException("Note-time restoration is once-only.");
        _restoring = true; _confirmed = false;
        Observe();
        // Do not overwrite a third state or an unattempted field to manufacture rollback.
        if (_unrecognized) return 0;
        for (var slot = _attempted - 1; slot >= 0; --slot)
        {
            Local(); var current = _access.ReadSlot(slot); Local();
            if (current != _expected[slot]) { _unrecognized = true; SideEffectsUnknown = true; return 0; }
            if (current == _baseline[slot]) continue;
            try { Store(slot, _baseline[slot], false); }
            catch (Exception error)
            {
                _failure ??= error.Message; Local(); Observe();
                if (_unrecognized) return 0;
            }
        }
        Observe(); return 0;
    });
    public GameplayControlRestoration ConfirmRestored()
    {
        RevokeConfirmation(true);
        return Operation(() =>
        {
            if (!_restoring) throw new InvalidOperationException("Restore before baseline confirmation.");
            _restored = Observe().SequenceEqual(_baseline) && !_unrecognized; SideEffectsUnknown = !_restored;
            return new GameplayControlRestoration(_restored, "Independent original selected note-time raw bits readback. " + _failure);
        });
    }
    public void Dispose()
    {
        if (_access is null)
        {
            if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Construction cleanup belongs to its owner thread.");
            _disposed = true;
            if (_constructionFailure is not null) throw Responsibility("Original note-time construction cleanup remains unknown.");
            lock (OwnerGate) Owners.Remove(this); return;
        }
        _access.VerifyOwnerThread();
        if (_cleanupBusy || _notifyingDisposal) return;
        _disposed = true;
        if (!_notified)
        {
            _notified = true; _notifyingDisposal = true;
            try { _access.RequestDispose(); }
            catch (Exception error) { _cleanupFailure ??= error.Message; _retained = true; }
            finally { _notifyingDisposal = false; }
        }
        if (!_busy) Release();
    }
    private Exception Responsibility(string reason)
    { _retained = true; var error = new InvalidOperationException(reason); error.Data["SeekNoteTimeCleanupOwner"] = this; return error; }
    private void Release()
    {
        if (_released || _cleanupBusy) return;
        if (_unrecognized || _hasDispatched && !_confirmed && !_restored)
            throw Responsibility("Entered note-time storage remains unconfirmed; retain its original independent owner.");
        if (_cleanupAttempted || _cleanupFailure is not null) throw Responsibility("Unknown note-time native cleanup is not retried: " + _cleanupFailure);
        _cleanupAttempted = true; _cleanupBusy = true;
        try { _access.Dispose(); _released = true; }
        catch (Exception error)
        {
            _cleanupFailure ??= error.Message; _retained = true;
            var failure = new AggregateException("Note-time source cleanup retains an unknown outcome.", error);
            failure.Data["SeekNoteTimeCleanupOwner"] = this; failure.Data["SeekNoteTimeCleanupFailure"] = error; throw failure;
        }
        finally { _cleanupBusy = false; }
        _retained = false; lock (OwnerGate) Owners.Remove(this);
    }
}
