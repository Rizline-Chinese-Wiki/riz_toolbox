namespace RizToolbox.Adapter.Gameplay;

internal interface INativeSeekJudgeInitializationAccess : IDisposable
{
    void VerifyOwnerThread();
    void VerifyLocal();
    void BindLifetimeGuard(Action guard);
    IReadOnlyList<long> References { get; }
    IReadOnlyList<int> ReadSnapshot();
    int ReadSlot(int slot);
    void WriteSlot(int slot, int bits, int expectedBits, Action beforeDispatch);
    void RequestDispose();
}

/// <summary>Explicit pending initialization only. Four Single caches, every actual
/// challenge flag, signed combo and finally ready are independently read back.
/// It does not dispatch Judge.Update or provide a complete Seek transaction.</summary>
internal sealed class NativeSeekJudgeInitializationParticipant : IGameplayValueParticipant
{
    private static readonly object OwnerGate = new();
    private static readonly HashSet<NativeSeekJudgeInitializationParticipant> Owners = new();
    private INativeSeekJudgeInitializationAccess _access = null!;
    private int[] _baseline = Array.Empty<int>(), _expected = Array.Empty<int>(), _target = Array.Empty<int>();
    private int _attempted, _pendingSlot = -1, _pendingBits;
    private bool _busy, _entered, _committed, _confirmed, _restoring, _restored, _disposed;
    private bool _cleanupBusy, _cleanupAttempted, _released, _notifyingDisposal, _notified;
    private bool _hasDispatched, _unrecognized, _retained, _initialized;
    private Exception? _constructionFailure;
    private string? _failure, _cleanupFailure;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    internal int AttemptedWrites => _attempted;
    internal bool SideEffectsUnknown { get; private set; }
    internal bool HasRetainedResponsibility => _retained;
    public string Id => "native.judge.explicit-pending-initialization";

    internal static NativeSeekJudgeInitializationParticipant Prepare(Func<INativeSeekJudgeInitializationAccess> create,
        SeekJudgeInitializationTargets targets)
    {
        ArgumentNullException.ThrowIfNull(create); ArgumentNullException.ThrowIfNull(targets);
        var value = new NativeSeekJudgeInitializationParticipant();
        lock (OwnerGate)
        {
            if (Owners.Count >= 256) throw new NotSupportedException("The bounded initialization owner capacity is exhausted.");
            Owners.Add(value);
        }
        try
        {
            value._access = create() ?? throw new InvalidOperationException("No independent initialization access was created.");
            value._access.BindLifetimeGuard(value.Lifetime);
            value.Operation(() =>
            {
                value._baseline = value.Read(); value._expected = value._baseline.ToArray();
                value._target = value.BuildTargets(targets);
                value._initialized = value._baseline[^1] != 0;
                if (targets.RequiresInitialization == value._initialized)
                    throw new InvalidDataException("The explicit initialization mode differs from the actual ready flag.");
                if (value._initialized && !value._target.SequenceEqual(value._baseline))
                    throw new InvalidDataException("Already initialized storage cannot be overwritten as a fresh initialization.");
                return 0;
            });
            return value;
        }
        catch (Exception error)
        {
            if (value._access is null && error.Data["SeekJudgeInitializationCleanupFailure"] is Exception)
            {
                value._constructionFailure = error; value._retained = true; value._disposed = true;
                error.Data["SeekJudgeInitializationSourceCleanupOwner"] = error.Data["SeekJudgeInitializationCleanupOwner"];
                error.Data["SeekJudgeInitializationCleanupOwner"] = value;
            }
            else
            {
                try { value.Dispose(); }
                catch (Exception cleanup)
                { error.Data["SeekJudgeInitializationCleanupOwner"] = value; error.Data["SeekJudgeInitializationCleanupFailure"] = cleanup; }
            }
            throw;
        }
    }
    private int[] BuildTargets(SeekJudgeInitializationTargets targets)
    {
        ArgumentNullException.ThrowIfNull(targets.Windows); ArgumentNullException.ThrowIfNull(targets.Notes);
        var references = _access.References.ToArray(); Local();
        if (targets.Notes.Count != references.Length || references.Length > NativeSeekRuntimeNoteView.MaximumNotes)
            throw new InvalidDataException("Initialization targets must cover every actual note once.");
        var byReference = new Dictionary<long, bool>();
        foreach (var row in targets.Notes)
        {
            Local();
            if (byReference.Count >= references.Length || row is null || row.Reference == 0 || !byReference.TryAdd(row.Reference, row.InChallengeTime))
                throw new InvalidDataException("Initialization targets have absent or duplicate references.");
        }
        if (byReference.Count != references.Length || targets.Notes.Count != references.Length)
            throw new InvalidDataException("Initialization target enumeration differs from its declared complete count.");
        var windows = new[] { targets.Windows.JudgeTime, targets.Windows.MaxPerfectChallengeTime,
            targets.Windows.NormalPerfectChallengeTime, targets.Windows.BadTime };
        if (windows.Any(x => !float.IsFinite(x))) throw new InvalidDataException("Explicit initialization windows must be finite.");
        var result = new int[references.Length + 6];
        for (var i = 0; i < 4; ++i) result[i] = BitConverter.SingleToInt32Bits(windows[i]);
        for (var i = 0; i < references.Length; ++i)
        {
            if (!byReference.TryGetValue(references[i], out var flag)) throw new InvalidDataException("An initialization target is foreign or missing.");
            result[4 + i] = flag ? 1 : 0;
        }
        result[^2] = targets.ChallengeComboCount; result[^1] = 1;
        return result;
    }
    private void Lifetime()
    { _access.VerifyOwnerThread(); if (_disposed) throw new ObjectDisposedException(nameof(NativeSeekJudgeInitializationParticipant)); }
    private void Local() { Lifetime(); _access.VerifyLocal(); }
    private T Operation<T>(Func<T> operation)
    {
        _access.VerifyOwnerThread(); Lifetime();
        if (_busy || _cleanupBusy) throw new InvalidOperationException("Initialization operations cannot reenter themselves.");
        _busy = true; Exception? failure = null;
        try { Local(); var result = operation(); Local(); return result; }
        catch (Exception error)
        {
            failure = error; _failure ??= error.Message; _confirmed = false; _restored = false;
            if (_hasDispatched) { SideEffectsUnknown = true; error.Data["SeekJudgeInitializationCleanupOwner"] = this; }
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
                    failure.Data["SeekJudgeInitializationCleanupOwner"] = this;
                    failure.Data["SeekJudgeInitializationCleanupFailure"] = cleanup;
                }
        }
    }
    private int[] Read()
    {
        Local(); var result = _access.ReadSnapshot().ToArray(); Local();
        if (result.Length != _access.References.Count + 6) throw new InvalidDataException("Incomplete actual initialization storage.");
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
            if (entered) throw new InvalidOperationException("Initialization setter guard entered twice.");
            if (forward && _attempted != slot) throw new InvalidOperationException("Initialization setter order differs from its prefix.");
            entered = true; _hasDispatched = true; _pendingSlot = slot; _pendingBits = bits; SideEffectsUnknown = true;
            if (forward) ++_attempted;
        });
        Local();
        if (!entered) throw new InvalidOperationException("No initialization setter entry was recorded.");
        _expected[slot] = bits; _pendingSlot = -1;
        var observed = _access.ReadSlot(slot); Local();
        if (observed != bits) { _unrecognized = true; throw new InvalidOperationException("Initialization setter readback differs."); }
    }
    public void VerifyPrepared() => Operation(() =>
    {
        if (_entered || _restoring) throw new InvalidOperationException("Preparation was already entered.");
        if (!Observe().SequenceEqual(_baseline)) throw new InvalidOperationException("Initialization baseline changed.");
        return 0;
    });
    public void Commit() => Operation(() =>
    {
        if (_entered || _restoring) throw new InvalidOperationException("Initialization is once-only.");
        _entered = true;
        if (!Observe().SequenceEqual(_baseline)) throw new InvalidOperationException("Initialization storage changed before commit.");
        if (!_initialized)
            for (var slot = 0; slot < _target.Length; ++slot) Store(slot, _target[slot], true);
        if (!Observe().SequenceEqual(_target) || _unrecognized) throw new InvalidOperationException("Initialization has an unrecognized final state.");
        _committed = true; return 0;
    });
    private void RevokeConfirmation(bool restoration)
    {
        _access.VerifyOwnerThread();
        if (_busy || _cleanupBusy) throw new InvalidOperationException("Initialization confirmation cannot reenter itself.");
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
            return new GameplayControlConfirmation(_confirmed, "Exact explicit Judge initialization cache/flag/count/ready readback only; no full Seek or consumer closure.");
        });
    }
    public void Restore() => Operation(() =>
    {
        if (_restoring) throw new InvalidOperationException("Initialization restoration is once-only.");
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
                if (_unrecognized || slot == _baseline.Length - 1 && _expected[slot] != _baseline[slot]) return 0;
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
            return new GameplayControlRestoration(_restored, "Independent original cache/flag/count/ready readback. " + _failure);
        });
    }
    public void Dispose()
    {
        if (_access is null)
        {
            if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Construction cleanup belongs to its owner thread.");
            _disposed = true;
            if (_constructionFailure is not null) throw Responsibility("Original initialization construction cleanup remains unknown.");
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
    { _retained = true; var error = new InvalidOperationException(reason); error.Data["SeekJudgeInitializationCleanupOwner"] = this; return error; }
    private void Release()
    {
        if (_released || _cleanupBusy) return;
        if (_unrecognized || _hasDispatched && !_confirmed && !_restored)
            throw Responsibility("Entered initialization storage remains unconfirmed; retain its original independent owner.");
        if (_cleanupAttempted || _cleanupFailure is not null) throw Responsibility("Unknown initialization native cleanup is not retried: " + _cleanupFailure);
        _cleanupAttempted = true; _cleanupBusy = true;
        try { _access.Dispose(); _released = true; }
        catch (Exception error)
        { _cleanupFailure ??= error.Message; _retained = true; error.Data["SeekJudgeInitializationCleanupOwner"] = this; throw; }
        finally { _cleanupBusy = false; }
        _retained = false; lock (OwnerGate) Owners.Remove(this);
    }
}
