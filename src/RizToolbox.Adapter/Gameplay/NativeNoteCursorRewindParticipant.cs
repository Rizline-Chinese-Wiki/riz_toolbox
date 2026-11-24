using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

internal readonly record struct NativeNoteCursorState(bool HasNote, int StartIndex, bool ActiveHasNote, int ActiveStartIndex)
{
    internal static NativeNoteCursorState Rewound => new(false, 0, false, 0);
    // Write order clears both scan flags before rewinding either array cursor.
    internal int At(int slot) => slot switch { 0 => HasNote ? 1 : 0, 1 => ActiveHasNote ? 1 : 0,
        2 => StartIndex, 3 => ActiveStartIndex, _ => throw new ArgumentOutOfRangeException(nameof(slot)) };
    internal NativeNoteCursorState With(int slot, int value) => slot switch
    {
        0 => this with { HasNote = value != 0 }, 1 => this with { ActiveHasNote = value != 0 },
        2 => this with { StartIndex = value }, 3 => this with { ActiveStartIndex = value },
        _ => throw new ArgumentOutOfRangeException(nameof(slot))
    };
}

/// <summary>An actual retained field accessor, not a seek permit. Reads verify its run,
/// full chart source, pause state and both unchanged native arrays. Local verification
/// must perform no native getter or invocation.</summary>
internal interface INativeNoteCursorAccess : IDisposable
{
    void BindLifetimeGuard(Action guard);
    void VerifyOwnerThread();
    void VerifyLocal();
    NativeNoteCursorState Read();
    void Write(int slot, int value);
    void ValidateBaseline(NativeNoteCursorState state);
}

/// <summary>Rewinds four NoteControl scan fields only. It never calls the native scan,
/// renderer or pool methods, uses no target-time index and grants no public Seek admission.
/// Equal samples do not prove atomicity or absence of unobserved ABA writers.</summary>
internal sealed class NativeNoteCursorRewindParticipant : IGameplayValueParticipant
{
    private readonly INativeNoteCursorAccess _access;
    private NativeNoteCursorState _baseline;
    private int _attempted;
    private bool _busy, _entered, _committed, _restoring, _confirmed, _restored;
    private bool _disposeRequested, _cleanupBusy, _released;
    private readonly List<string> _restoreErrors = new();
    private readonly List<string> _operationErrors = new();
    internal int AttemptedWrites => _attempted;
    internal bool SideEffectsUnknown { get; private set; }
    internal string? Failure { get; private set; }
    internal string? CleanupFailure { get; private set; }
    public string Id => "native.note-control.cursor-rewind";

    private NativeNoteCursorRewindParticipant(INativeNoteCursorAccess access) => _access = access;
    internal static NativeNoteCursorRewindParticipant Prepare(INativeNoteCursorAccess access)
    {
        ArgumentNullException.ThrowIfNull(access);
        var value = new NativeNoteCursorRewindParticipant(access);
        try
        {
            access.BindLifetimeGuard(value.Lifetime);
            value.Operation(() => { value._baseline = access.Read(); access.ValidateBaseline(value._baseline); return 0; });
            return value;
        }
        catch { value.Dispose(); throw; }
    }
    private void Lifetime()
    {
        _access.VerifyOwnerThread();
        if (_disposeRequested) throw new ObjectDisposedException(nameof(NativeNoteCursorRewindParticipant));
    }
    private void Local()
    {
        Lifetime();
        _access.VerifyLocal();
    }
    private T Operation<T>(Func<T> operation)
    {
        _access.VerifyOwnerThread();
        if (_disposeRequested) throw new ObjectDisposedException(nameof(NativeNoteCursorRewindParticipant));
        if (_busy || _cleanupBusy) throw new InvalidOperationException("A cursor operation cannot reenter itself.");
        _busy = true;
        try { Local(); var value = operation(); Local(); return value; }
        catch (Exception error)
        {
            if (!_operationErrors.Contains(error.Message)) _operationErrors.Add(error.Message);
            Failure = string.Join("; ", _operationErrors);
            if (_attempted != 0 && !_restored) SideEffectsUnknown = true;
            throw;
        }
        finally { _busy = false; if (_disposeRequested) Release(); }
    }
    private NativeNoteCursorState Read()
    { Local(); var state = _access.Read(); Local(); return state; }
    public void VerifyPrepared() => Operation(() =>
    {
        if (_entered || _restoring) throw new InvalidOperationException("This cursor preparation was already entered.");
        if (Read() != _baseline) throw new InvalidOperationException("The four prepared cursor fields changed before commit.");
        return 0;
    });
    public void Commit() => Operation(() =>
    {
        if (_entered || _restoring) throw new InvalidOperationException("Cursor commit is once-only and cannot follow restoration.");
        _entered = true;
        var expected = _baseline;
        if (Read() != expected) throw new InvalidOperationException("Cursor baseline changed before its first write.");
        for (var slot = 0; slot < 4; ++slot)
        {
            if (Read() != expected) throw new InvalidOperationException("An external writer changed the cursor transaction.");
            // Includes an official setter which writes and then reports failure.
            _attempted = slot + 1;
            _access.Write(slot, 0); Local();
            expected = expected.With(slot, 0);
            if (Read() != expected) throw new InvalidOperationException("Actual cursor field readback differs after writing.");
        }
        _committed = true;
        return 0;
    });
    public GameplayControlConfirmation Confirm() => Operation(() =>
    {
        if (!_committed || _restoring) throw new InvalidOperationException("Cursor commit must finish before confirmation.");
        var observed = Read() == NativeNoteCursorState.Rewound;
        _confirmed = observed; SideEffectsUnknown = !observed;
        return new GameplayControlConfirmation(observed,
            "Independent native readback of four NoteControl scan fields and retained run/chart/arrays. No note/judge/hold/input/result reset or Seek value is confirmed.");
    });
    public void Restore() => Operation(() =>
    {
        if (_restoring) throw new InvalidOperationException("Cursor restoration is once-only.");
        _restoring = true;
        var protectedSlots = new bool[4];
        var considered = new bool[4];
        // Restore only fields whose setters were attempted. A baseline value is
        // already restored; only our post-value may be overwritten. Third values
        // remain untouched while other independent fields are considered.
        for (var slot = _attempted - 1; slot >= 0; --slot)
        {
            var sampled = Read();
            // These fields were never ours to mutate. A changed unattempted
            // field revokes the partial-transaction baseline before any later
            // restoration store; do not infer authority from one selected slot.
            for (var index = _attempted; index < 4; ++index)
                if (sampled.At(index) != _baseline.At(index))
                {
                    _restoreErrors.Add("Unattempted cursor field changed at slot " + index + "; remaining restoration stopped.");
                    return 0;
                }
            for (var index = 0; index < _attempted; ++index)
            {
                var value = sampled.At(index);
                if (protectedSlots[index]) continue;
                if ((considered[index] && value != _baseline.At(index))
                    || (value != _baseline.At(index) && value != NativeNoteCursorState.Rewound.At(index)))
                {
                    protectedSlots[index] = true;
                    _restoreErrors.Add((considered[index] ? "Restoration baseline not retained" : "External cursor value")
                        + " at slot " + index + "; it was not overwritten.");
                }
            }
            considered[slot] = true;
            if (protectedSlots[slot]) continue;
            var current = sampled.At(slot); var original = _baseline.At(slot);
            if (current == original) continue;
            try { _access.Write(slot, original); Local(); }
            catch (Exception error)
            {
                _restoreErrors.Add("Restore slot " + slot + ": " + error.Message);
                // An expired/disposed source must never reach another setter.
                Local();
            }
        }
        return 0;
    });
    public GameplayControlRestoration ConfirmRestored() => Operation(() =>
    {
        if (!_restoring) throw new InvalidOperationException("Attempt restoration before independent baseline readback.");
        _restored = Read() == _baseline; SideEffectsUnknown = !_restored;
        return new GameplayControlRestoration(_restored,
            (_restored ? "All four original cursor fields were independently read back on the original retained source. "
                : "The original cursor fields were not all restored. ")
                + (Failure is null ? "" : "Original operation failure: " + Failure + ". ")
                + string.Join("; ", _restoreErrors));
    });
    public void Dispose()
    {
        _access.VerifyOwnerThread();
        if (_cleanupBusy) return;
        _disposeRequested = true;
        if (_attempted != 0 && !_confirmed && !_restored) SideEffectsUnknown = true;
        if (!_busy) Release();
    }
    private void Release()
    {
        if (_released || _cleanupBusy) return;
        _cleanupBusy = true;
        try { _access.Dispose(); _released = true; }
        catch (Exception error) { CleanupFailure = error.Message; throw; }
        finally { _cleanupBusy = false; }
    }
}
