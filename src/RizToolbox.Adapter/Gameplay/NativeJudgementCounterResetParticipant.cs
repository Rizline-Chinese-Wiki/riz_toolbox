using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>A private, independently retained accessor for exactly seven controller
/// Int32 fields. RequestDispose must close its owned NoteView immediately, including
/// during an in-flight verification; Dispose releases the accessor's own roots later.
/// The write callback runs once inside the final managed guard, before setter entry.</summary>
internal interface INativeJudgementCounterAccess : IDisposable
{
    void BindLifetimeGuard(Action guard);
    void VerifyOwnerThread();
    void VerifyLocal();
    GameplayJudgementCounters Read();
    void Write(int slot, int value, int expectedCurrent, Action beforeDispatch);
    void RequestDispose();
}

/// <summary>Clears only Bad/Miss/Perfect/Early/Late/MaxPerfect/ChallengeTimeMiss.
/// All Int32 baselines are valid. This has no target-time, score, combo, life,
/// history, Judge-list, coroutine or public Seek semantics. Sampling cannot rule
/// out an unobserved external writer or double ABA between observation and store.</summary>
internal sealed class NativeJudgementCounterResetParticipant : IGameplayValueParticipant
{
    private static readonly GameplayJudgementCounters Zero = new(0, 0, 0, 0, 0, 0, 0);
    private readonly INativeJudgementCounterAccess _access;
    private GameplayJudgementCounters _baseline = Zero;
    private readonly List<string> _operationErrors = new(), _restoreErrors = new();
    private int _attempted;
    private bool _busy, _entered, _committed, _confirmed, _restoring, _restored;
    private bool _disposeRequested, _notifyingDisposal, _cleanupBusy, _released;
    internal int AttemptedWrites => _attempted;
    internal bool SideEffectsUnknown { get; private set; }
    internal string? Failure { get; private set; }
    internal string? CleanupFailure { get; private set; }
    public string Id => "native.judgement-controller.seven-counter-reset";

    private NativeJudgementCounterResetParticipant(INativeJudgementCounterAccess access) => _access = access;
    internal static NativeJudgementCounterResetParticipant Prepare(INativeJudgementCounterAccess access)
    {
        ArgumentNullException.ThrowIfNull(access);
        access.VerifyOwnerThread();
        var value = new NativeJudgementCounterResetParticipant(access);
        try
        {
            access.BindLifetimeGuard(value.Lifetime);
            value.Operation(() => { value._baseline = value.Read(); return 0; });
            return value;
        }
        catch (Exception error)
        {
            try { value.Dispose(); }
            catch (Exception cleanup)
            {
                error.Data["JudgementCounterCleanupOwner"] = value;
                error.Data["JudgementCounterCleanupFailure"] = cleanup;
            }
            throw;
        }
    }
    private void Lifetime()
    {
        _access.VerifyOwnerThread();
        if (_disposeRequested) throw new ObjectDisposedException(nameof(NativeJudgementCounterResetParticipant));
    }
    private void Local() { Lifetime(); _access.VerifyLocal(); }
    private T Operation<T>(Func<T> operation)
    {
        _access.VerifyOwnerThread();
        if (_disposeRequested) throw new ObjectDisposedException(nameof(NativeJudgementCounterResetParticipant));
        if (_busy || _cleanupBusy) throw new InvalidOperationException("A counter-reset operation cannot reenter itself.");
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
                    operationFailure.Data["JudgementCounterCleanupOwner"] = this;
                    operationFailure.Data["JudgementCounterCleanupFailure"] = cleanup;
                }
            }
        }
    }
    private GameplayJudgementCounters Read()
    {
        Local();
        var value = _access.Read() ?? throw new InvalidDataException("The actual seven-counter snapshot is absent.");
        Local();
        return value;
    }
    private void Write(int slot, int value, int expectedCurrent, Action attempted)
    {
        Local();
        var dispatched = false;
        _access.Write(slot, value, expectedCurrent, () =>
        {
            Local();
            if (dispatched) throw new InvalidOperationException("The exact counter setter guard was entered twice.");
            attempted();
            dispatched = true;
            Local();
        });
        Local();
        if (!dispatched) throw new InvalidOperationException("No exact counter setter entry was recorded.");
    }
    private static int At(GameplayJudgementCounters value, int slot) => slot switch
    {
        0 => value.Bad, 1 => value.Miss, 2 => value.Perfect, 3 => value.Early,
        4 => value.Late, 5 => value.MaxPerfect, 6 => value.ChallengeTimeMiss,
        _ => throw new ArgumentOutOfRangeException(nameof(slot))
    };
    private static GameplayJudgementCounters With(GameplayJudgementCounters value, int slot, int field) => slot switch
    {
        0 => value with { Bad = field }, 1 => value with { Miss = field }, 2 => value with { Perfect = field },
        3 => value with { Early = field }, 4 => value with { Late = field }, 5 => value with { MaxPerfect = field },
        6 => value with { ChallengeTimeMiss = field }, _ => throw new ArgumentOutOfRangeException(nameof(slot))
    };
    public void VerifyPrepared() => Operation(() =>
    {
        if (_entered || _restoring) throw new InvalidOperationException("This counter preparation was already entered.");
        if (Read() != _baseline) throw new InvalidOperationException("The seven-counter baseline changed before commit.");
        return 0;
    });
    public void Commit() => Operation(() =>
    {
        if (_entered || _restoring) throw new InvalidOperationException("Counter reset is once-only and cannot follow restoration.");
        _entered = true;
        var expected = _baseline;
        if (Read() != expected) throw new InvalidOperationException("The prepared counters changed before the first setter.");
        for (var slot = 0; slot < 7; ++slot)
        {
            if (Read() != expected) throw new InvalidOperationException("An external value changed the counter transaction.");
            var currentSlot = slot;
            Write(slot, 0, At(expected, slot), () =>
            {
                if (_attempted != currentSlot) throw new InvalidOperationException("Counter setter order differs from its attempted prefix.");
                _attempted = currentSlot + 1;
                SideEffectsUnknown = true;
            });
            expected = With(expected, slot, 0);
            if (Read() != expected) throw new InvalidOperationException("Actual counter readback differs after its setter.");
        }
        _committed = true;
        return 0;
    });
    public GameplayControlConfirmation Confirm() => Operation(() =>
    {
        if (!_committed || _restoring) throw new InvalidOperationException("Finish counter reset before confirmation.");
        var observed = Read() == Zero;
        _confirmed = observed; SideEffectsUnknown = !observed;
        return new GameplayControlConfirmation(observed,
            "Independent readback of exactly seven controller counters on the retained ready paused run/chart. No score, combo, life, history, Judge lists, hold reconstruction or Seek value is confirmed.");
    });
    public void Restore() => Operation(() =>
    {
        if (_restoring) throw new InvalidOperationException("Counter restoration is once-only.");
        _restoring = true;
        var protectedSlots = new bool[7];
        var considered = new bool[7];
        for (var slot = _attempted - 1; slot >= 0; --slot)
        {
            var sampled = Read();
            // An unattempted field was never ours. Stop all later stores when
            // that baseline changes, including after a partial commit failure.
            for (var index = _attempted; index < 7; ++index)
                if (At(sampled, index) != At(_baseline, index))
                {
                    _restoreErrors.Add("Unattempted counter changed at slot " + index + "; remaining restoration stopped.");
                    SideEffectsUnknown = true;
                    return 0;
                }
            for (var index = 0; index < _attempted; ++index)
            {
                if (protectedSlots[index]) continue;
                var current = At(sampled, index);
                if ((considered[index] && current != At(_baseline, index))
                    || (current != At(_baseline, index) && current != 0))
                {
                    protectedSlots[index] = true;
                    _restoreErrors.Add("External counter value at slot " + index + "; it was not overwritten.");
                }
            }
            considered[slot] = true;
            if (protectedSlots[slot] || At(sampled, slot) == At(_baseline, slot)) continue;
            try { Write(slot, At(_baseline, slot), 0, () => SideEffectsUnknown = true); }
            catch (Exception error)
            {
                _restoreErrors.Add("Restore slot " + slot + ": " + error.Message);
                Local(); // Expired/disposed sources must not enter another setter.
            }
        }
        return 0;
    });
    public GameplayControlRestoration ConfirmRestored() => Operation(() =>
    {
        if (!_restoring) throw new InvalidOperationException("Attempt restoration before independent baseline readback.");
        _restored = Read() == _baseline; SideEffectsUnknown = !_restored;
        return new GameplayControlRestoration(_restored,
            (_restored ? "All seven original counters were independently read back. " : "The original seven counters were not all restored. ")
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
