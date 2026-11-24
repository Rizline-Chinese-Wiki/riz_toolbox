using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

internal sealed record SeekNoteFlagTarget(long Reference, bool IsActive, bool Destroyed, bool Missed)
{
    internal bool At(int slot) => slot switch
    {
        0 => IsActive, 1 => Destroyed, 2 => Missed,
        _ => throw new ArgumentOutOfRangeException(nameof(slot))
    };
    internal SeekNoteFlagTarget With(int slot, bool value) => slot switch
    {
        0 => this with { IsActive = value }, 1 => this with { Destroyed = value },
        2 => this with { Missed = value }, _ => throw new ArgumentOutOfRangeException(nameof(slot))
    };
}

/// <summary>Owns a separate complete runtime NoteView and the exact field receivers.
/// Read preserves that view's note order. RequestDispose closes the view immediately;
/// Dispose releases independent accessor roots after the in-flight operation exits.
/// Each write callback runs once in the official setter's final managed guard.</summary>
internal interface INativeSeekNoteFlagsAccess : IDisposable
{
    void BindLifetimeGuard(Action guard);
    void VerifyOwnerThread();
    void VerifyLocal();
    IReadOnlyList<SeekNoteFlagTarget> Read();
    SeekNoteFlagTarget ReadNote(int ordinal);
    void Write(int noteOrdinal, int fieldSlot, bool value, bool expectedCurrent, Action beforeDispatch);
    void RequestDispose();
}

/// <summary>Applies explicit IsActive/Destroyed/Missed targets to every actual runtime
/// note, including partial chart coverage. No native initial values or target-time
/// partition are inferred. Does not synchronize renderers, pools, holds, queues,
/// input, clocks or public Seek. Equal Boolean samples cannot exclude external ABA.</summary>
internal sealed partial class NativeSeekNoteFlagsParticipant : IGameplayValueParticipant
{
    private readonly INativeSeekNoteFlagsAccess _access;
    private SeekNoteFlagTarget[] _baseline = Array.Empty<SeekNoteFlagTarget>();
    private SeekNoteFlagTarget[] _targets = Array.Empty<SeekNoteFlagTarget>();
    private readonly List<string> _operationErrors = new(), _restoreErrors = new();
    private int _attempted;
    private bool _prepared, _busy, _entered, _committed, _confirmed, _restoring, _restored;
    private bool _disposeRequested, _notifyingDisposal, _cleanupBusy, _released;
    internal int AttemptedWrites => _attempted;
    internal bool SideEffectsUnknown { get; private set; }
    internal string? Failure { get; private set; }
    internal string? CleanupFailure { get; private set; }
    public string Id => "native.note-information.explicit-three-flags";

    private NativeSeekNoteFlagsParticipant(INativeSeekNoteFlagsAccess access) => _access = access;
    internal static NativeSeekNoteFlagsParticipant Prepare(INativeSeekNoteFlagsAccess access,
        IReadOnlyList<SeekNoteFlagTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(access);
        access.VerifyOwnerThread();
        var value = new NativeSeekNoteFlagsParticipant(access);
        try
        {
            access.BindLifetimeGuard(value.Lifetime);
            value.Operation(() =>
            {
                value._baseline = value.Read();
                var copied = value.Copy(targets);
                if (copied.Length != value._baseline.Length)
                    throw new InvalidDataException("Explicit targets must cover every actual runtime note exactly once.");
                var byReference = copied.ToDictionary(row => row.Reference);
                value._targets = value._baseline.Select(row => byReference.TryGetValue(row.Reference, out var target)
                    ? target : throw new InvalidDataException("An actual runtime note has no explicit flag target.")).ToArray();
                value._prepared = true;
                if (!value.Read().SequenceEqual(value._baseline))
                    throw new InvalidOperationException("Actual note flags changed during preparation.");
                return 0;
            });
            return value;
        }
        catch (Exception error)
        {
            try { value.Dispose(); }
            catch (Exception cleanup)
            {
                error.Data["SeekNoteFlagsCleanupOwner"] = value;
                error.Data["SeekNoteFlagsCleanupFailure"] = cleanup;
            }
            throw;
        }
    }
    private void Lifetime()
    {
        _access.VerifyOwnerThread();
        if (_disposeRequested) throw new ObjectDisposedException(nameof(NativeSeekNoteFlagsParticipant));
    }
    private void Local() { Lifetime(); _access.VerifyLocal(); }
    private T Operation<T>(Func<T> operation)
    {
        _access.VerifyOwnerThread();
        if (_disposeRequested) throw new ObjectDisposedException(nameof(NativeSeekNoteFlagsParticipant));
        if (_busy || _cleanupBusy) throw new InvalidOperationException("A note-flags operation cannot reenter itself.");
        _busy = true;
        Exception? operationFailure = null;
        try { Local(); var result = operation(); Local(); return result; }
        catch (Exception error)
        {
            operationFailure = error;
            if (!_operationErrors.Contains(error.Message)) _operationErrors.Add(error.Message);
            Failure = string.Join("; ", _operationErrors);
            if (_attempted != 0 && !_restored) SideEffectsUnknown = true;
            throw;
        }
        finally
        {
            _busy = false;
            if (_disposeRequested)
            {
                try { Release(); }
                catch (Exception cleanup)
                {
                    if (operationFailure is null) throw;
                    operationFailure.Data["SeekNoteFlagsCleanupOwner"] = this;
                    operationFailure.Data["SeekNoteFlagsCleanupFailure"] = cleanup;
                }
            }
        }
    }
    private SeekNoteFlagTarget[] Copy(IReadOnlyList<SeekNoteFlagTarget> input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Local(); var count = input.Count; Local();
        if (count is < 0 or > NativeSeekRuntimeNoteView.MaximumNotes)
            throw new InvalidDataException("The complete note-flags collection exceeds its bound.");
        var copied = new SeekNoteFlagTarget[count];
        var references = new HashSet<long>();
        for (var index = 0; index < count; ++index)
        {
            Local(); var row = input[index]; Local();
            if (row is null || row.Reference == 0 || !references.Add(row.Reference))
                throw new InvalidDataException("Note-flags references must be nonzero and unique.");
            copied[index] = row with { };
        }
        Local(); var finalCount = input.Count; Local();
        if (finalCount != count) throw new InvalidOperationException("The complete note-flags input changed during copying.");
        return copied;
    }
    private SeekNoteFlagTarget[] Read()
    {
        Local(); var input = _access.Read(); Local();
        var copied = Copy(input ?? throw new InvalidDataException("The actual note-flags snapshot is absent."));
        if (_prepared && (copied.Length != _baseline.Length
            || copied.Where((row, index) => row.Reference != _baseline[index].Reference).Any()))
            throw new InvalidOperationException("The actual note receiver identities or order changed.");
        return copied;
    }
    private void Write(int index, bool value, bool expectedCurrent, Action attempted)
    {
        Local(); var dispatched = false;
        _access.Write(index / 3, index % 3, value, expectedCurrent, () =>
        {
            Local();
            if (dispatched) throw new InvalidOperationException("The exact note setter guard was entered twice.");
            attempted(); dispatched = true; Local();
        });
        Local();
        if (!dispatched) throw new InvalidOperationException("No exact note setter entry was recorded.");
    }
    private static bool At(SeekNoteFlagTarget[] values, int index) => values[index / 3].At(index % 3);
    private SeekNoteFlagTarget ReadNote(int ordinal)
    {
        Local(); var row = _access.ReadNote(ordinal); Local();
        if (row is null || (uint)ordinal >= (uint)_baseline.Length || row.Reference != _baseline[ordinal].Reference)
            throw new InvalidOperationException("The selected note receiver identity changed.");
        return row with { };
    }
    public void VerifyPrepared() => Operation(() =>
    {
        if (_entered || _restoring) throw new InvalidOperationException("This note-flags preparation was already entered.");
        if (!Read().SequenceEqual(_baseline)) throw new InvalidOperationException("The actual note-flags baseline changed.");
        return 0;
    });
    public void Commit() => Operation(() =>
    {
        if (_entered || _restoring) throw new InvalidOperationException("Note-flags commit is once-only and cannot follow restoration.");
        _entered = true;
        var expected = _baseline.ToArray();
        if (!Read().SequenceEqual(expected)) throw new InvalidOperationException("Prepared note flags changed before the first setter.");
        for (var index = 0; index < expected.Length * 3; ++index)
        {
            if (ReadNote(index / 3) != expected[index / 3])
                throw new InvalidOperationException("An external value changed the selected note flags.");
            var currentIndex = index;
            Write(index, At(_targets, index), At(expected, index), () =>
            {
                if (_attempted != currentIndex) throw new InvalidOperationException("Note setter order differs from its attempted prefix.");
                _attempted = currentIndex + 1; SideEffectsUnknown = true;
            });
            expected[index / 3] = expected[index / 3].With(index % 3, At(_targets, index));
            if (ReadNote(index / 3) != expected[index / 3])
                throw new InvalidOperationException("Actual selected-note readback differs after its setter.");
        }
        if (!Read().SequenceEqual(_targets)) throw new InvalidOperationException("The complete actual flag targets differ after commit.");
        _committed = true;
        return 0;
    });
    public GameplayControlConfirmation Confirm() => Operation(() =>
    {
        if (!_committed || _restoring) throw new InvalidOperationException("Finish the flag commit before confirmation.");
        var observed = Read().SequenceEqual(_targets);
        _confirmed = observed; SideEffectsUnknown = !observed;
        return new GameplayControlConfirmation(observed,
            "Independent readback of three explicit Boolean targets for every actual runtime note. No renderer, pool, hold, queue, target-time or public Seek state is confirmed.");
    });
    public void Restore() => Operation(() =>
    {
        if (_restoring) throw new InvalidOperationException("Note-flags restoration is once-only.");
        _restoring = true;
        var initial = Read();
        var protectedSlots = new bool[_baseline.Length * 3];
        var considered = new bool[protectedSlots.Length];
        for (var slot = _attempted; slot < protectedSlots.Length; ++slot)
            if (At(initial, slot) != At(_baseline, slot))
            {
                _restoreErrors.Add("Unattempted flag changed at slot " + slot + "; restoration stopped.");
                SideEffectsUnknown = true; return 0;
            }
        for (var index = _attempted - 1; index >= 0; --index)
        {
            var ordinal = index / 3;
            var sampled = ReadNote(ordinal);
            for (var slot = ordinal * 3; slot < ordinal * 3 + 3; ++slot)
                if (slot >= _attempted && sampled.At(slot % 3) != At(_baseline, slot))
                {
                    _restoreErrors.Add("Unattempted flag changed at slot " + slot + "; remaining restoration stopped.");
                    SideEffectsUnknown = true; return 0;
                }
            for (var slot = ordinal * 3; slot < Math.Min(ordinal * 3 + 3, _attempted); ++slot)
            {
                if (protectedSlots[slot]) continue;
                var current = sampled.At(slot % 3);
                if ((considered[slot] && current != At(_baseline, slot))
                    || (current != At(_baseline, slot) && current != At(_targets, slot)))
                {
                    protectedSlots[slot] = true;
                    _restoreErrors.Add("External flag value at slot " + slot + "; it was not overwritten.");
                }
            }
            considered[index] = true;
            if (protectedSlots[index] || sampled.At(index % 3) == At(_baseline, index)) continue;
            try { Write(index, At(_baseline, index), At(_targets, index), () => SideEffectsUnknown = true); }
            catch (Exception error)
            {
                _restoreErrors.Add("Restore slot " + index + ": " + error.Message);
                Local();
            }
        }
        return 0;
    });
    public GameplayControlRestoration ConfirmRestored() => Operation(() =>
    {
        if (!_restoring) throw new InvalidOperationException("Attempt restoration before independent baseline readback.");
        _restored = Read().SequenceEqual(_baseline); SideEffectsUnknown = !_restored;
        return new GameplayControlRestoration(_restored,
            (_restored ? "Every original note flag was independently read back. " : "The original note flags were not all restored. ")
                + (Failure is null ? "" : "Original operation failure: " + Failure + ". ") + string.Join("; ", _restoreErrors));
    });
    public void Dispose()
    {
        _access.VerifyOwnerThread();
        if (_cleanupBusy || _notifyingDisposal) return;
        _disposeRequested = true;
        if (_attempted != 0 && !_confirmed && !_restored) SideEffectsUnknown = true;
        Exception? failure = null;
        _notifyingDisposal = true;
        try { _access.RequestDispose(); }
        catch (Exception error) { CleanupFailure = error.Message; failure = error; }
        finally { _notifyingDisposal = false; }
        if (!_busy)
        {
            try { Release(); }
            catch (Exception error) { failure = error; }
        }
        if (failure is not null) throw failure;
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
