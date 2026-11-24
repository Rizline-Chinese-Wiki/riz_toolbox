using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

internal sealed record SeekJudgeQueueTargets(IReadOnlyList<long> Notes,
    IReadOnlyList<long> TapWaiting, IReadOnlyList<long> DragWaiting);
internal sealed record SeekJudgeArrayStorage(long Reference, long Class, IReadOnlyList<long> CapacitySlots);
internal enum SeekJudgeQueueRestoreField { Size, Items, Version }

/// <summary>Owns an independent actual NoteView, original capacity references and
/// exact Judge/list receivers. Every callback runs in the final managed dispatch
/// guard. Conditional samples are not an atomic native compare-and-swap.</summary>
internal interface INativeSeekJudgeQueuesAccess : IDisposable
{
    void BindLifetimeGuard(Action guard);
    void VerifyOwnerThread();
    void VerifyLocal();
    void VerifySource();
    IReadOnlyList<SeekRuntimeNote> RuntimeNotes { get; }
    SeekJudgeRawState Read();
    SeekJudgeArrayStorage ReadBacking(long reference);
    void Clear(int listSlot, SeekJudgeRawState expectedCurrent, Action beforeDispatch);
    void Add(int listSlot, long reference, SeekJudgeRawState expectedCurrent, Action beforeDispatch);
    void RestoreCapacitySlot(int listSlot, int capacitySlot, long value,
        SeekJudgeRawState expectedCurrent, SeekJudgeArrayStorage expectedBacking, Action beforeDispatch);
    void RestoreListField(int listSlot, SeekJudgeQueueRestoreField field, long value,
        SeekJudgeRawState expectedCurrent, Action beforeDispatch);
    void RequestDispose();
}

internal static class SeekJudgeQueueState
{
    internal static SeekJudgeListStorage At(SeekJudgeRawState value, int slot) => slot switch
    {
        0 => value.Notes, 1 => value.TapWaiting, 2 => value.DragWaiting,
        _ => throw new ArgumentOutOfRangeException(nameof(slot))
    };
    internal static SeekJudgeRawState With(SeekJudgeRawState value, SeekJudgeListStorage replacement)
        => value with
        {
            Notes = value.Notes.Reference == replacement.Reference ? replacement : value.Notes,
            TapWaiting = value.TapWaiting.Reference == replacement.Reference ? replacement : value.TapWaiting,
            DragWaiting = value.DragWaiting.Reference == replacement.Reference ? replacement : value.DragWaiting
        };
    internal static bool Same(SeekJudgeArrayStorage first, SeekJudgeArrayStorage second)
        => first.Reference == second.Reference && first.Class == second.Class
            && first.CapacitySlots.SequenceEqual(second.CapacitySlots);
    internal static bool Same(SeekJudgeListStorage first, SeekJudgeListStorage second)
        => first.Reference == second.Reference && first.Class == second.Class && first.Backing == second.Backing
            && first.ArrayClass == second.ArrayClass && first.Size == second.Size && first.Version == second.Version
            && first.CapacitySlots.SequenceEqual(second.CapacitySlots);
    internal static bool Same(SeekJudgeRawState first, SeekJudgeRawState second)
        => first.Identity == second.Identity && first.HoldOnJudgeCount == second.HoldOnJudgeCount
            && Same(first.Notes, second.Notes) && Same(first.TapWaiting, second.TapWaiting)
            && Same(first.DragWaiting, second.DragWaiting);
    internal static SeekJudgeArrayStorage Array(SeekJudgeListStorage list)
        => new(list.Backing, list.ArrayClass, list.CapacitySlots);
    internal static long[] Copy(IReadOnlyList<long> values, Action guard)
    {
        ArgumentNullException.ThrowIfNull(values);
        guard(); var count = values.Count; guard();
        if (count is < 0 or > NativeSeekJudgeStateView.MaximumCapacity)
            throw new InvalidDataException("A complete Judge sequence exceeds its bound.");
        var result = new long[count];
        for (var index = 0; index < count; ++index) { guard(); result[index] = values[index]; guard(); }
        guard(); var finalCount = values.Count; guard();
        if (count != finalCount) throw new InvalidOperationException("A complete Judge sequence changed while being copied.");
        return result;
    }
    internal static SeekJudgeArrayStorage Copy(SeekJudgeArrayStorage value, Action guard)
    {
        guard();
        if (value is null || value.Reference == 0 || value.Class == 0 || value.CapacitySlots is null)
            throw new InvalidDataException("The complete Judge backing storage is absent.");
        var slots = Copy(value.CapacitySlots, guard); guard();
        return value with { CapacitySlots = System.Array.AsReadOnly(slots) };
    }
    internal static SeekJudgeRawState Copy(SeekJudgeRawState value, Action guard)
    {
        guard();
        if (value is null || value.Identity is null || value.Identity.Notes is null
            || value.Identity.Notes.Chart is null || value.Identity.Notes.Chart.Run is null
            || value.Identity.NoteJudgeControl == 0)
            throw new InvalidDataException("The exact Judge source identity is absent.");
        SeekJudgeListStorage CopyList(SeekJudgeListStorage list)
        {
            guard();
            if (list is null || list.Reference == 0 || list.Class == 0 || list.Backing == 0 || list.ArrayClass == 0
                || list.Size is < 0 or > NativeSeekJudgeStateView.MaximumCapacity)
                throw new InvalidDataException("A complete official Judge list is absent or outside its bound.");
            var slots = Copy(list.CapacitySlots, guard);
            if (list.Size > slots.Length) throw new InvalidDataException("A Judge list size exceeds its complete backing capacity.");
            return list with { CapacitySlots = System.Array.AsReadOnly(slots) };
        }
        var result = value with { Notes = CopyList(value.Notes), TapWaiting = CopyList(value.TapWaiting), DragWaiting = CopyList(value.DragWaiting) };
        for (var slot = 0; slot < 3; ++slot)
            for (var previous = 0; previous < slot; ++previous)
            {
                var first = At(result, previous); var second = At(result, slot);
                if (first.Reference == second.Reference && !Same(first, second)
                    || first.Backing == second.Backing && !Same(Array(first), Array(second)))
                    throw new InvalidDataException("Aliased Judge storage returned contradictory complete state.");
            }
        guard(); return result;
    }
}

/// <summary>Writes explicit occurrence sequences through official List.Clear/Add.
/// No initial queues, target-time partition, Hold-idle or public Seek permission is
/// inferred. Hold count is read only. Restoration requires the complete original
/// backing identities, capacity slots, sizes and signed versions. Equal samples
/// cannot exclude an external writer or unobserved ABA.</summary>
internal sealed partial class NativeSeekJudgeQueuesParticipant : IGameplayValueParticipant
{
    private enum MutationKind { Clear, Add, CapacitySlot, Field }
    private sealed record PendingMutation(MutationKind Kind, int ListSlot, int CapacitySlot, long Value,
        SeekJudgeQueueRestoreField Field, SeekJudgeRawState Before, Dictionary<long, SeekJudgeArrayStorage> ArraysBefore);
    private readonly INativeSeekJudgeQueuesAccess _access;
    private readonly NativeSeekJudgeQueuesParticipant _registered;
    private SeekJudgeRawState _baseline = null!, _expected = null!;
    private long[][] _targets = System.Array.Empty<long[]>();
    private int[] _representatives = System.Array.Empty<int>();
    private Dictionary<long, SeekJudgeArrayStorage> _baselineArrays = new(), _expectedArrays = new();
    private PendingMutation? _pending;
    private readonly List<string> _operationErrors = new(), _restoreErrors = new();
    private int _attempted;
    private bool _busy, _entered, _committed, _confirmed, _restoring, _restored;
    private bool _disposeRequested, _notifyingDisposal, _cleanupBusy, _released;
    internal int AttemptedWrites => _attempted;
    internal bool SideEffectsUnknown { get; private set; }
    internal string? Failure { get; private set; }
    internal string? CleanupFailure { get; private set; }
    public string Id => "native.note-judge.explicit-three-queues";

    private NativeSeekJudgeQueuesParticipant(INativeSeekJudgeQueuesAccess access) => (_access, _registered) = (access, this);
    internal static NativeSeekJudgeQueuesParticipant Prepare(INativeSeekJudgeQueuesAccess access, SeekJudgeQueueTargets targets)
    {
        ArgumentNullException.ThrowIfNull(access); access.VerifyOwnerThread();
        var value = new NativeSeekJudgeQueuesParticipant(access);
        try
        {
            access.BindLifetimeGuard(value.Lifetime);
            value.Operation(() =>
            {
                ArgumentNullException.ThrowIfNull(targets);
                value.VerifySource();
                value._baseline = value.Read(); value._expected = value._baseline;
                value._targets = new[] { SeekJudgeQueueState.Copy(targets.Notes, value.Local),
                    SeekJudgeQueueState.Copy(targets.TapWaiting, value.Local), SeekJudgeQueueState.Copy(targets.DragWaiting, value.Local) };
                value.ValidateTargets();
                value._baselineArrays = Enumerable.Range(0, 3).Select(slot => SeekJudgeQueueState.Array(SeekJudgeQueueState.At(value._baseline, slot)))
                    .GroupBy(array => array.Reference).ToDictionary(group => group.Key, group => group.First());
                value._expectedArrays = new(value._baselineArrays);
                if (!SeekJudgeQueueState.Same(value.Read(), value._baseline) || !value.ArraysMatch(value._expectedArrays))
                    throw new InvalidOperationException("Complete actual Judge storage changed during preparation.");
                value.VerifySource(); return 0;
            });
            return value;
        }
        catch (Exception error)
        {
            try { value.Dispose(); }
            catch (Exception cleanup) { error.Data["SeekJudgeQueuesCleanupOwner"] = value; error.Data["SeekJudgeQueuesCleanupFailure"] = cleanup; }
            throw;
        }
    }
    private void Lifetime()
    {
        _access.VerifyOwnerThread();
        if (!ReferenceEquals(_registered, this))
            throw new InvalidOperationException("A copied Judge queue participant cannot use the original retained graph.");
        if (_disposeRequested) throw new ObjectDisposedException(nameof(NativeSeekJudgeQueuesParticipant));
    }
    private void Local() { Lifetime(); _access.VerifyLocal(); }
    private void VerifySource() { Local(); _access.VerifySource(); Local(); }
    private T Operation<T>(Func<T> operation)
    {
        Lifetime();
        if (_busy || _cleanupBusy) throw new InvalidOperationException("A Judge-queues operation cannot reenter itself.");
        _busy = true; Exception? operationFailure = null;
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
                try { Release(); }
                catch (Exception cleanup)
                {
                    if (operationFailure is null) throw;
                    operationFailure.Data["SeekJudgeQueuesCleanupOwner"] = this;
                    operationFailure.Data["SeekJudgeQueuesCleanupFailure"] = cleanup;
                }
        }
    }
    private SeekJudgeRawState Read()
    {
        Local(); var raw = _access.Read(); Local();
        var value = SeekJudgeQueueState.Copy(raw, Local);
        ObserveHoldEpochCount(value);
        return value;
    }
    private bool ArraysMatch(IReadOnlyDictionary<long, SeekJudgeArrayStorage> expected)
    {
        foreach (var pair in expected)
        {
            if (!SeekJudgeQueueState.Same(ReadArray(pair.Key), pair.Value)) return false;
        }
        return true;
    }
    private SeekJudgeArrayStorage ReadArray(long reference)
    {
        Local(); var raw = _access.ReadBacking(reference); Local();
        return SeekJudgeQueueState.Copy(raw, Local);
    }
    private void ValidateTargets()
    {
        Local(); var notes = _access.RuntimeNotes; Local();
        if (notes is null) throw new InvalidDataException("The independent actual NoteView is absent.");
        Local(); var count = notes.Count; Local();
        if (count is < 0 or > NativeSeekRuntimeNoteView.MaximumNotes) throw new InvalidDataException("The actual runtime-note map exceeds its bound.");
        var references = new HashSet<long>();
        for (var index = 0; index < count; ++index)
        {
            Local(); var note = notes[index]; Local();
            if (note is null || note.Reference == 0 || !references.Add(note.Reference))
                throw new InvalidDataException("The independently owned runtime-note map contains an absent or duplicate identity.");
        }
        Local(); if (notes.Count != count) throw new InvalidOperationException("The independent runtime-note map changed during copying."); Local();
        var representatives = new List<int>();
        for (var slot = 0; slot < 3; ++slot)
        {
            var list = SeekJudgeQueueState.At(_baseline, slot);
            if (list.CapacitySlots.Take(list.Size).Concat(_targets[slot]).Any(reference => reference == 0 || !references.Contains(reference)))
                throw new InvalidDataException("Every active occurrence and explicit target must refer to the independent actual NoteView.");
            var alias = representatives.FindIndex(previous => SeekJudgeQueueState.At(_baseline, previous).Reference == list.Reference);
            if (alias >= 0)
            {
                if (!_targets[representatives[alias]].SequenceEqual(_targets[slot]))
                    throw new InvalidDataException("The same actual Judge List requires identical explicit occurrence sequences.");
                continue;
            }
            foreach (var previous in representatives)
            {
                var other = SeekJudgeQueueState.At(_baseline, previous);
                if (other.Backing == list.Backing && list.CapacitySlots.Count != 0)
                    throw new NotSupportedException("Distinct Judge Lists sharing a nonempty backing array are not supported by this writer.");
            }
            // Reject before Clear if official doubling growth would exceed the
            // reader's complete storage bound and lose restoration authority.
            long capacity = list.CapacitySlots.Count;
            while (capacity < _targets[slot].Length)
            {
                capacity = capacity == 0 ? 4 : capacity * 2;
                if (capacity > NativeSeekJudgeStateView.MaximumCapacity)
                    throw new NotSupportedException("The explicit Judge target would grow official List capacity beyond the retained complete-storage bound.");
            }
            representatives.Add(slot);
        }
        _representatives = representatives.ToArray();
    }
    private void RequireOwned()
    {
        if (_pending is not null) throw new InvalidOperationException("An unresolved Judge dispatch still owns its before/after responsibility.");
        if (!SeekJudgeQueueState.Same(Read(), _expected) || !ArraysMatch(_expectedArrays))
            throw new InvalidOperationException("Complete Judge storage changed outside the recognized write sequence; it was not overwritten.");
    }
    private void Dispatch(MutationKind kind, int listSlot, int capacitySlot = 0, long value = 0,
        SeekJudgeQueueRestoreField field = default)
    {
        RequireOwned();
        var pending = new PendingMutation(kind, listSlot, capacitySlot, value, field, _expected, new(_expectedArrays));
        var dispatched = false;
        void Enter()
        {
            Local();
            if (dispatched || _pending is not null) throw new InvalidOperationException("The final Judge dispatch guard was entered twice.");
            _pending = pending; dispatched = true;
            if (kind is MutationKind.Clear or MutationKind.Add) ++_attempted;
            SideEffectsUnknown = true; Local();
        }
        Local();
        switch (kind)
        {
            case MutationKind.Clear: _access.Clear(listSlot, _expected, Enter); break;
            case MutationKind.Add: _access.Add(listSlot, value, _expected, Enter); break;
            case MutationKind.CapacitySlot:
                _access.RestoreCapacitySlot(listSlot, capacitySlot, value, _expected,
                    _expectedArrays[SeekJudgeQueueState.At(_baseline, listSlot).Backing], Enter); break;
            case MutationKind.Field: _access.RestoreListField(listSlot, field, value, _expected, Enter); break;
        }
        Local();
        if (!dispatched) throw new InvalidOperationException("No final official Judge dispatch entry was recorded.");
        ResolvePending(true);
    }
    private void ResolvePending(bool requireAfter)
    {
        var pending = _pending ?? throw new InvalidOperationException("No entered Judge dispatch is awaiting readback.");
        var actual = Read();
        if (!requireAfter && SeekJudgeQueueState.Same(actual, pending.Before) && ArraysMatch(pending.ArraysBefore))
        {
            _expected = pending.Before; _expectedArrays = pending.ArraysBefore; _pending = null; return;
        }
        var list = SeekJudgeQueueState.At(pending.Before, pending.ListSlot);
        var arrays = new Dictionary<long, SeekJudgeArrayStorage>(pending.ArraysBefore);
        SeekJudgeRawState after;
        if (pending.Kind == MutationKind.CapacitySlot)
        {
            var original = arrays[SeekJudgeQueueState.At(_baseline, pending.ListSlot).Backing];
            var slots = original.CapacitySlots.ToArray(); slots[pending.CapacitySlot] = pending.Value;
            var replaced = original with { CapacitySlots = System.Array.AsReadOnly(slots) }; arrays[original.Reference] = replaced;
            after = pending.Before;
            for (var slot = 0; slot < 3; ++slot)
            {
                var attached = SeekJudgeQueueState.At(after, slot);
                if (attached.Backing == original.Reference)
                    after = SeekJudgeQueueState.With(after, attached with { CapacitySlots = replaced.CapacitySlots });
            }
        }
        else if (pending.Kind == MutationKind.Field)
        {
            list = pending.Field switch
            {
                SeekJudgeQueueRestoreField.Size => list with { Size = checked((int)pending.Value) },
                SeekJudgeQueueRestoreField.Version => list with { Version = checked((int)pending.Value) },
                SeekJudgeQueueRestoreField.Items => list with { Backing = pending.Value,
                    ArrayClass = arrays[pending.Value].Class, CapacitySlots = arrays[pending.Value].CapacitySlots },
                _ => throw new ArgumentOutOfRangeException()
            };
            after = SeekJudgeQueueState.With(pending.Before, list);
        }
        else
        {
            var slots = list.CapacitySlots.ToArray();
            if (pending.Kind == MutationKind.Clear)
            {
                System.Array.Clear(slots, 0, list.Size);
                list = list with { Size = 0, Version = unchecked(list.Version + 1), CapacitySlots = System.Array.AsReadOnly(slots) };
            }
            else if (list.Size < slots.Length)
            {
                slots[list.Size] = pending.Value;
                list = list with { Size = list.Size + 1, Version = unchecked(list.Version + 1), CapacitySlots = System.Array.AsReadOnly(slots) };
            }
            else
            {
                var grown = SeekJudgeQueueState.At(actual, pending.ListSlot);
                var expectedCapacity = slots.Length == 0 ? 4 : slots.Length * 2;
                if (grown.Backing == list.Backing || grown.ArrayClass != list.ArrayClass
                    || grown.CapacitySlots.Count != expectedCapacity || grown.CapacitySlots.Count > NativeSeekJudgeStateView.MaximumCapacity
                    || arrays.ContainsKey(grown.Backing)
                    || Enumerable.Range(0, 3).Any(slot => SeekJudgeQueueState.At(pending.Before, slot).Backing == grown.Backing)
                    || !SeekJudgeQueueState.Same(ReadArray(list.Backing), SeekJudgeQueueState.Array(list)))
                    throw new InvalidOperationException("Entered Judge Add has no recognized complete official growth state.");
                var grownSlots = new long[grown.CapacitySlots.Count];
                System.Array.Copy(slots, grownSlots, list.Size); grownSlots[list.Size] = pending.Value;
                list = list with { Backing = grown.Backing, Size = list.Size + 1, Version = unchecked(list.Version + 1),
                    CapacitySlots = System.Array.AsReadOnly(grownSlots) };
            }
            after = SeekJudgeQueueState.With(pending.Before, list);
            if (arrays.ContainsKey(list.Backing)) arrays[list.Backing] = SeekJudgeQueueState.Array(list);
        }
        if (!SeekJudgeQueueState.Same(actual, after) || !ArraysMatch(arrays))
            throw new InvalidOperationException("Entered Judge dispatch has an unrecognized complete before/after state; its side effects remain unknown.");
        _expected = after; _expectedArrays = arrays; _pending = null;
    }
    public void VerifyPrepared() => Operation(() =>
    {
        if (_entered || _restoring) throw new InvalidOperationException("The Judge-queues preparation was already entered.");
        VerifySource(); RequireOwned(); return 0;
    });
    public void Commit() => Operation(() =>
    {
        if (_entered || _restoring) throw new InvalidOperationException("Judge-queues commit is once-only and cannot follow restoration.");
        _entered = true; VerifySource(); RequireOwned();
        foreach (var slot in _representatives)
        {
            Dispatch(MutationKind.Clear, slot);
            foreach (var reference in _targets[slot]) Dispatch(MutationKind.Add, slot, value: reference);
        }
        VerifySource(); RequireOwned(); _committed = true; return 0;
    });
    public GameplayControlConfirmation Confirm() => Operation(() =>
    {
        if (!_committed || _restoring) throw new InvalidOperationException("Finish the complete Judge-queues commit before confirmation.");
        VerifySource(); RequireOwned();
        _confirmed = Enumerable.Range(0, 3).All(slot =>
        {
            var list = SeekJudgeQueueState.At(_expected, slot);
            return list.Size == _targets[slot].Length && list.CapacitySlots.Take(list.Size).SequenceEqual(_targets[slot]);
        });
        SideEffectsUnknown = !_confirmed;
        return new GameplayControlConfirmation(_confirmed,
            "Independent complete readback of three explicit Judge occurrence sequences. Hold count was observed only; Hold-idle, clocks, renderers and public Seek are not confirmed.");
    });
    private bool ResolveRestoreFailure(Exception error)
    {
        _restoreErrors.Add(error.Message); Local();
        if (_pending is null) { RequireOwned(); return true; }
        try { ResolvePending(false); return true; }
        catch (Exception observation) { _restoreErrors.Add(observation.Message); SideEffectsUnknown = true; return false; }
    }
    public void Restore() => Operation(() =>
    {
        if (_restoring) throw new InvalidOperationException("Judge-queues restoration is once-only.");
        _restoring = true; VerifySource();
        if (_pending is not null)
            try { ResolvePending(false); }
            catch (Exception error) { _restoreErrors.Add(error.Message); SideEffectsUnknown = true; return 0; }
        RequireOwned();
        foreach (var listSlot in _representatives.Reverse())
        {
            var baseline = SeekJudgeQueueState.At(_baseline, listSlot);
            var original = _baselineArrays[baseline.Backing];
            for (var index = original.CapacitySlots.Count - 1; index >= 0; --index)
            {
                if (_expectedArrays[original.Reference].CapacitySlots[index] == original.CapacitySlots[index]) continue;
                try { Dispatch(MutationKind.CapacitySlot, listSlot, index, original.CapacitySlots[index]); }
                catch (Exception error) { if (!ResolveRestoreFailure(error)) return 0; }
            }
            // A grown list can have Size greater than the original capacity.
            // Restore Size before reattaching _items so all intermediate states
            // remain completely readable through official storage access.
            foreach (var field in new[] { SeekJudgeQueueRestoreField.Size, SeekJudgeQueueRestoreField.Items, SeekJudgeQueueRestoreField.Version })
            {
                var current = SeekJudgeQueueState.At(_expected, listSlot);
                var wanted = field switch { SeekJudgeQueueRestoreField.Size => baseline.Size,
                    SeekJudgeQueueRestoreField.Items => baseline.Backing, _ => baseline.Version };
                var sampled = field switch { SeekJudgeQueueRestoreField.Size => current.Size,
                    SeekJudgeQueueRestoreField.Items => current.Backing, _ => current.Version };
                if (sampled == wanted) continue;
                if (field == SeekJudgeQueueRestoreField.Items && current.Size > original.CapacitySlots.Count)
                { _restoreErrors.Add("Original backing could not be safely reattached after failed size restoration."); continue; }
                try { Dispatch(MutationKind.Field, listSlot, value: wanted, field: field); }
                catch (Exception error) { if (!ResolveRestoreFailure(error)) return 0; }
            }
        }
        VerifySource(); RequireOwned(); return 0;
    });
    public GameplayControlRestoration ConfirmRestored() => Operation(() =>
    {
        if (!_restoring) throw new InvalidOperationException("Attempt restoration before complete baseline readback.");
        VerifySource();
        _restored = _pending is null && SeekJudgeQueueState.Same(Read(), _baseline) && ArraysMatch(_baselineArrays);
        SideEffectsUnknown = !_restored;
        return new GameplayControlRestoration(_restored,
            (_restored ? "Every original Judge List, backing identity, capacity slot, size and signed version was independently read back. "
                : "The complete original Judge storage was not restored. ")
            + (Failure is null ? "" : "Original operation failure: " + Failure + ". ") + string.Join("; ", _restoreErrors));
    });
    public void Dispose()
    {
        _access.VerifyOwnerThread();
        if (!ReferenceEquals(_registered, this))
            throw new InvalidOperationException("A copied Judge queue participant cannot release the original retained graph.");
        if (_cleanupBusy || _notifyingDisposal) return;
        _disposeRequested = true;
        if (_attempted != 0 && !_confirmed && !_restored) SideEffectsUnknown = true;
        Exception? failure = null; _notifyingDisposal = true;
        try { _access.RequestDispose(); }
        catch (Exception error) { CleanupFailure = error.Message; failure = error; }
        finally { _notifyingDisposal = false; }
        if (!_busy)
            try { Release(); } catch (Exception error) { failure = error; }
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
