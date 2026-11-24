using RizToolbox.Sdk;

namespace RizToolbox.Adapter.Gameplay;

internal sealed record SeekFingerIdsTarget(long Reference, IReadOnlyList<int>? FingerIds);
internal sealed record SeekFingerIdsNoteField(long Reference, long ListReference);
internal sealed record SeekFingerIdsFieldSnapshot(SeekRuntimeNoteSourceIdentity Identity, IReadOnlyList<SeekFingerIdsNoteField> Notes);
internal sealed record SeekFingerIdsListStorage(long Reference, long Class, long Backing, long ArrayClass,
    int Size, int Version, IReadOnlyList<int> CapacitySlots);
internal sealed record SeekFingerIdsArrayStorage(long Reference, long Class, IReadOnlyList<int> CapacitySlots);
internal enum SeekFingerIdsRestoreField { Size, Items, Version }

/// <summary>Owns one independent complete actual NoteView and all original/scratch
/// List and array roots. Full graph validation occurs at phase boundaries; selected
/// operations validate their note, field token and complete list/backing storage.
/// Allocation publishes root responsibility before constructor entry. Only guarded
/// field publication makes a scratch List visible to a gameplay note.</summary>
internal interface INativeSeekFingerIdsAccess : IDisposable
{
    void BindLifetimeGuard(Action guard);
    void VerifyOwnerThread();
    void VerifyLocal();
    void VerifySource();
    IReadOnlyList<SeekRuntimeNote> RuntimeNotes { get; }
    SeekFingerIdsFieldSnapshot ReadFields();
    SeekFingerIdsNoteField ReadNote(int ordinal);
    SeekFingerIdsListStorage ReadList(long reference);
    SeekFingerIdsArrayStorage ReadBacking(long reference);
    void AllocateList(int ordinal, Action<long> published);
    void Clear(int ordinal, long expectedField, SeekFingerIdsListStorage expectedList, Action beforeDispatch);
    void Add(int ordinal, long expectedField, SeekFingerIdsListStorage expectedList, int value, Action beforeDispatch);
    void WriteField(int ordinal, long value, long expectedField, SeekFingerIdsListStorage? expectedCurrentList,
        SeekFingerIdsListStorage? expectedTargetList, SeekFingerIdsArrayStorage? expectedTargetOriginalBacking, Action beforeDispatch);
    void RestoreCapacitySlot(int ordinal, long expectedField, SeekFingerIdsListStorage expectedList,
        SeekFingerIdsArrayStorage expectedBacking, int capacitySlot, int value, Action beforeDispatch);
    void RestoreListField(int ordinal, long expectedField, SeekFingerIdsListStorage expectedList,
        SeekFingerIdsRestoreField field, long value, Action beforeDispatch);
    void RequestDispose();
}

internal static class SeekFingerIdsState
{
    internal static bool Same(SeekFingerIdsListStorage first, SeekFingerIdsListStorage second)
        => first.Reference == second.Reference && first.Class == second.Class && first.Backing == second.Backing
            && first.ArrayClass == second.ArrayClass && first.Size == second.Size && first.Version == second.Version
            && first.CapacitySlots.SequenceEqual(second.CapacitySlots);
    internal static bool Same(SeekFingerIdsArrayStorage first, SeekFingerIdsArrayStorage second)
        => first.Reference == second.Reference && first.Class == second.Class
            && first.CapacitySlots.SequenceEqual(second.CapacitySlots);
    internal static SeekFingerIdsArrayStorage Array(SeekFingerIdsListStorage list)
        => new(list.Backing, list.ArrayClass, list.CapacitySlots);
    internal static int[] Copy(IReadOnlyList<int> input, Action guard)
    {
        ArgumentNullException.ThrowIfNull(input);
        guard(); var count = input.Count; guard();
        if (count is < 0 or > NativeSeekFingerIdsParticipant.MaximumCapacity)
            throw new InvalidDataException("A complete fingerIds sequence exceeds its bound.");
        var copy = new int[count];
        for (var index = 0; index < count; ++index) { guard(); copy[index] = input[index]; guard(); }
        guard(); var finalCount = input.Count; guard();
        if (count != finalCount) throw new InvalidOperationException("A complete fingerIds sequence changed while being copied.");
        return copy;
    }
    internal static SeekFingerIdsListStorage Copy(SeekFingerIdsListStorage input, Action guard)
    {
        guard();
        if (input is null || input.Reference == 0 || input.Class == 0 || input.Backing == 0 || input.ArrayClass == 0
            || input.Size is < 0 or > NativeSeekFingerIdsParticipant.MaximumCapacity || input.CapacitySlots is null)
            throw new InvalidDataException("Complete official fingerIds List storage is absent or outside its bound.");
        var slots = Copy(input.CapacitySlots, guard);
        if (input.Size > slots.Length) throw new InvalidDataException("A fingerIds List size exceeds its complete backing capacity.");
        guard(); return input with { CapacitySlots = System.Array.AsReadOnly(slots) };
    }
    internal static SeekFingerIdsArrayStorage Copy(SeekFingerIdsArrayStorage input, Action guard)
    {
        guard();
        if (input is null || input.Reference == 0 || input.Class == 0 || input.CapacitySlots is null)
            throw new InvalidDataException("The complete retained fingerIds backing array is absent.");
        var slots = Copy(input.CapacitySlots, guard); guard();
        return input with { CapacitySlots = System.Array.AsReadOnly(slots) };
    }
}

/// <summary>Explicit fingerIds targets cover every actual note exactly once.
/// Null is a native null field; empty is a real List. Existing List aliases require
/// identical targets, while each originally-null note owns a separate scratch List.
/// This participant infers no input/Hold idle, target-time policy, renderer state or
/// public Seek admission. Conditional samples do not exclude native races or ABA.</summary>
internal sealed class NativeSeekFingerIdsParticipant : IGameplayValueParticipant
{
    internal const int MaximumCapacity = NativeSeekRuntimeNoteView.MaximumNotes;
    internal const int MaximumTotalCapacity = 1024 * 1024;
    private enum MutationKind { Clear, Add, Field, CapacitySlot, ListField }
    private sealed record PendingMutation(MutationKind Kind, int Ordinal, long ListReference,
        long FieldBefore, long Value, int CapacitySlot, SeekFingerIdsRestoreField RestoreField,
        SeekFingerIdsListStorage? ListBefore, Dictionary<long, SeekFingerIdsArrayStorage> ArraysBefore);
    private readonly INativeSeekFingerIdsAccess _access;
    private SeekRuntimeNoteSourceIdentity _identity = null!;
    private long[] _actualReferences = System.Array.Empty<long>();
    private SeekFingerIdsNoteField[] _baselineFields = System.Array.Empty<SeekFingerIdsNoteField>();
    private long[] _expectedFields = System.Array.Empty<long>(), _scratch = System.Array.Empty<long>();
    private int[]?[] _targets = System.Array.Empty<int[]?>();
    private Dictionary<long, SeekFingerIdsListStorage> _baselineLists = new(), _expectedLists = new();
    private readonly Dictionary<long, int> _originalOrdinals = new();
    private Dictionary<long, SeekFingerIdsArrayStorage> _baselineArrays = new(), _expectedArrays = new();
    private readonly HashSet<long> _knownBackings = new();
    private readonly List<PendingMutation> _unknownScratch = new();
    private readonly List<string> _operationErrors = new(), _restoreErrors = new();
    private PendingMutation? _pending;
    private int _attempted, _allocated;
    private bool _busy, _entered, _committed, _confirmed, _restoring, _restored;
    private bool _disposeRequested, _notifyingDisposal, _cleanupBusy, _released;
    internal int AttemptedWrites => _attempted;
    internal int AllocatedLists => _allocated;
    internal bool SideEffectsUnknown { get; private set; }
    internal string? Failure { get; private set; }
    internal string? CleanupFailure { get; private set; }
    public string Id => "native.note-information.explicit-finger-ids";
    private NativeSeekFingerIdsParticipant(INativeSeekFingerIdsAccess access) => _access = access;
    internal static NativeSeekFingerIdsParticipant Prepare(INativeSeekFingerIdsAccess access,
        IReadOnlyList<SeekFingerIdsTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(access); access.VerifyOwnerThread();
        var value = new NativeSeekFingerIdsParticipant(access);
        try
        {
            access.BindLifetimeGuard(value.Lifetime);
            value.Operation(() =>
            {
                value.VerifySource(); value.CaptureActualReferences();
                value._baselineFields = value.ReadFields();
                value._expectedFields = value._baselineFields.Select(field => field.ListReference).ToArray();
                value._scratch = new long[value._baselineFields.Length];
                value.CopyTargets(targets); value.CaptureOriginalLists(); value.ValidateAliasesAndBudget();
                value.VerifySource(); value.RequireAllOwned();
                for (var ordinal = 0; ordinal < value._baselineFields.Length; ++ordinal)
                {
                    if (value._baselineFields[ordinal].ListReference != 0 || value._targets[ordinal] is null) continue;
                    var selected = ordinal;
                    value.Local();
                    access.AllocateList(ordinal, reference =>
                    {
                        // The access already owns the published native root.
                        // Record its selected allocation before lifecycle checks.
                        if (value._scratch[selected] != 0 || reference == 0
                            || value._baselineLists.ContainsKey(reference) || value._scratch.Contains(reference))
                            throw new InvalidOperationException("A scratch List allocation did not publish a distinct actual object once.");
                        value._scratch[selected] = reference; ++value._allocated; value.Local();
                    });
                    value.Local();
                    var scratch = value.ReadList(value._scratch[ordinal]);
                    if (scratch.Size != 0 || scratch.Version != 0 || scratch.CapacitySlots.Count != 0)
                        throw new InvalidOperationException("The official scratch List constructor did not yield its canonical complete empty state.");
                    value._expectedLists.Add(scratch.Reference, scratch); value._knownBackings.Add(scratch.Backing);
                }
                value.VerifySource(); value.RequireAllOwned(); return 0;
            });
            return value;
        }
        catch (Exception error)
        {
            try { value.Dispose(); }
            catch (Exception cleanup) { error.Data["SeekFingerIdsCleanupOwner"] = value; error.Data["SeekFingerIdsCleanupFailure"] = cleanup; }
            throw;
        }
    }
    private void Lifetime()
    {
        _access.VerifyOwnerThread();
        if (_disposeRequested) throw new ObjectDisposedException(nameof(NativeSeekFingerIdsParticipant));
    }
    private void Local() { Lifetime(); _access.VerifyLocal(); }
    private void VerifySource() { Local(); _access.VerifySource(); Local(); }
    private T Operation<T>(Func<T> operation)
    {
        _access.VerifyOwnerThread();
        if (_disposeRequested) throw new ObjectDisposedException(nameof(NativeSeekFingerIdsParticipant));
        if (_busy || _cleanupBusy) throw new InvalidOperationException("A fingerIds operation cannot reenter itself.");
        _busy = true; Exception? operationFailure = null;
        try { Local(); var result = operation(); Local(); return result; }
        catch (Exception error)
        {
            operationFailure = error;
            if (!_operationErrors.Contains(error.Message)) _operationErrors.Add(error.Message);
            Failure = string.Join("; ", _operationErrors);
            if (_attempted != 0 && !_restored || _unknownScratch.Count != 0) SideEffectsUnknown = true;
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
                    operationFailure.Data["SeekFingerIdsCleanupOwner"] = this;
                    operationFailure.Data["SeekFingerIdsCleanupFailure"] = cleanup;
                }
        }
    }
    private void CaptureActualReferences()
    {
        Local(); var notes = _access.RuntimeNotes; Local();
        if (notes is null) throw new InvalidDataException("The independent complete actual NoteView is absent.");
        Local(); var count = notes.Count; Local();
        if (count is < 0 or > NativeSeekRuntimeNoteView.MaximumNotes) throw new InvalidDataException("The actual NoteView exceeds its complete bound.");
        _actualReferences = new long[count]; var identities = new HashSet<long>();
        for (var ordinal = 0; ordinal < count; ++ordinal)
        {
            Local(); var note = notes[ordinal]; Local();
            if (note is null || note.Reference == 0 || !identities.Add(note.Reference))
                throw new InvalidDataException("The independent actual NoteView has an absent or duplicate identity.");
            _actualReferences[ordinal] = note.Reference;
        }
        Local(); if (notes.Count != count) throw new InvalidOperationException("The actual NoteView changed during copying."); Local();
    }
    private SeekFingerIdsNoteField[] ReadFields()
    {
        Local(); var raw = _access.ReadFields(); Local();
        if (raw is null || raw.Identity is null || raw.Identity.Chart is null || raw.Identity.Chart.Run is null || raw.Notes is null)
            throw new InvalidDataException("The complete actual fingerIds field snapshot is absent.");
        if (_identity is not null && raw.Identity != _identity) throw new InvalidOperationException("The fingerIds snapshot belongs to another independent source.");
        _identity ??= raw.Identity;
        Local(); var count = raw.Notes.Count; Local();
        if (count != _actualReferences.Length) throw new InvalidOperationException("The complete actual fingerIds receiver map changed.");
        var result = new SeekFingerIdsNoteField[count];
        for (var ordinal = 0; ordinal < count; ++ordinal)
        {
            Local(); var row = raw.Notes[ordinal]; Local();
            if (row is null || row.Reference != _actualReferences[ordinal])
                throw new InvalidOperationException("The actual fingerIds receiver identity or order changed.");
            result[ordinal] = row with { };
        }
        Local(); if (raw.Notes.Count != count) throw new InvalidOperationException("The complete fingerIds receiver map changed while being copied."); Local();
        return result;
    }
    private SeekFingerIdsNoteField ReadNote(int ordinal)
    {
        Local(); var row = _access.ReadNote(ordinal); Local();
        if (row is null || (uint)ordinal >= (uint)_actualReferences.Length || row.Reference != _actualReferences[ordinal])
            throw new InvalidOperationException("The selected fingerIds receiver lost its exact actual identity.");
        return row with { };
    }
    private SeekFingerIdsListStorage ReadList(long reference)
    {
        Local(); var raw = _access.ReadList(reference); Local();
        var result = SeekFingerIdsState.Copy(raw, Local);
        if (result.Reference != reference) throw new InvalidOperationException("The retained fingerIds List identity changed.");
        return result;
    }
    private SeekFingerIdsArrayStorage ReadArray(long reference)
    {
        Local(); var raw = _access.ReadBacking(reference); Local();
        var result = SeekFingerIdsState.Copy(raw, Local);
        if (result.Reference != reference) throw new InvalidOperationException("The retained fingerIds backing identity changed.");
        return result;
    }
    private void CopyTargets(IReadOnlyList<SeekFingerIdsTarget> input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Local(); var count = input.Count; Local();
        if (count != _actualReferences.Length) throw new InvalidDataException("Explicit fingerIds targets must cover every actual note exactly once.");
        var byReference = new Dictionary<long, int[]?>(); long aggregate = 0;
        for (var index = 0; index < count; ++index)
        {
            Local(); var row = input[index]; Local();
            if (row is null || row.Reference == 0 || byReference.ContainsKey(row.Reference))
                throw new InvalidDataException("Explicit fingerIds target note identities must be nonzero and unique.");
            int[]? copied = null;
            if (row.FingerIds is not null)
            {
                Local(); var members = row.FingerIds.Count; Local();
                if (members is < 0 or > MaximumCapacity || checked(aggregate += members) > MaximumTotalCapacity)
                    throw new NotSupportedException("The complete explicit fingerIds targets exceed the bounded aggregate member budget.");
                copied = SeekFingerIdsState.Copy(row.FingerIds, Local);
                if (copied.Length != members) throw new InvalidOperationException("An explicit fingerIds target changed during copying.");
            }
            byReference.Add(row.Reference, copied);
        }
        Local(); if (input.Count != count) throw new InvalidOperationException("The complete fingerIds target map changed during copying."); Local();
        _targets = _actualReferences.Select(reference => byReference.TryGetValue(reference, out var target)
            ? target : throw new InvalidDataException("An actual runtime note has no explicit fingerIds target.")).ToArray();
    }
    private void CaptureOriginalLists()
    {
        long aggregate = 0;
        for (var ordinal = 0; ordinal < _baselineFields.Length; ++ordinal)
            if (_baselineFields[ordinal].ListReference != 0) _originalOrdinals.TryAdd(_baselineFields[ordinal].ListReference, ordinal);
        foreach (var reference in _baselineFields.Select(field => field.ListReference).Where(reference => reference != 0).Distinct())
        {
            var list = ReadList(reference); _baselineLists.Add(reference, list);
            if (_baselineArrays.TryGetValue(list.Backing, out var prior))
            {
                if (!SeekFingerIdsState.Same(prior, SeekFingerIdsState.Array(list)))
                    throw new InvalidDataException("Aliased fingerIds backing arrays returned contradictory complete storage.");
                if (list.CapacitySlots.Count != 0)
                    throw new NotSupportedException("Distinct fingerIds Lists sharing a nonempty backing array are not supported.");
            }
            else
            {
                aggregate = checked(aggregate + list.CapacitySlots.Count);
                if (aggregate > MaximumTotalCapacity)
                    throw new NotSupportedException("Original complete fingerIds backing storage exceeds the bounded aggregate capacity budget.");
                _baselineArrays.Add(list.Backing, SeekFingerIdsState.Array(list));
            }
            _knownBackings.Add(list.Backing);
        }
        _expectedLists = new(_baselineLists); _expectedArrays = new(_baselineArrays);
    }
    private static bool SameTarget(int[]? first, int[]? second)
        => first is null ? second is null : second is not null && first.SequenceEqual(second);
    private void ValidateAliasesAndBudget()
    {
        long retainedCapacity = _baselineArrays.Values.Sum(array => (long)array.CapacitySlots.Count);
        var groups = new Dictionary<long, int>();
        for (var ordinal = 0; ordinal < _baselineFields.Length; ++ordinal)
        {
            var reference = _baselineFields[ordinal].ListReference;
            if (reference != 0 && groups.TryGetValue(reference, out var previous))
            {
                if (!SameTarget(_targets[previous], _targets[ordinal]))
                    throw new InvalidDataException("Every note sharing one original fingerIds List must specify identical nullness and occurrence targets.");
                continue;
            }
            if (reference != 0) groups.Add(reference, ordinal);
            if (_targets[ordinal] is not { } target) continue;
            long capacity = reference == 0 ? 0 : _baselineLists[reference].CapacitySlots.Count;
            while (capacity < target.Length)
            {
                capacity = capacity == 0 ? 4 : checked(capacity * 2);
                if (capacity > MaximumCapacity)
                    throw new NotSupportedException("An explicit fingerIds target would grow official List capacity beyond its complete bound.");
                retainedCapacity = checked(retainedCapacity + capacity);
                if (retainedCapacity > MaximumTotalCapacity)
                    throw new NotSupportedException("Original and projected retained fingerIds arrays exceed the bounded aggregate capacity budget.");
            }
        }
    }
    private void RequireAllOwned(bool originalSourceOnly = false)
    {
        if (_pending is not null) throw new InvalidOperationException("An entered fingerIds dispatch still owns unresolved before/after responsibility.");
        var fields = ReadFields();
        for (var ordinal = 0; ordinal < fields.Length; ++ordinal)
            if (fields[ordinal].ListReference != _expectedFields[ordinal])
                throw new InvalidOperationException("An actual fingerIds field reference changed outside this participant; it was not overwritten.");
        var attached = originalSourceOnly ? _expectedFields.Where(reference => reference != 0).ToHashSet() : null;
        foreach (var pair in _expectedLists)
        {
            if (originalSourceOnly && !_baselineLists.ContainsKey(pair.Key) && !attached!.Contains(pair.Key)) continue;
            if (!SeekFingerIdsState.Same(ReadList(pair.Key), pair.Value))
                throw new InvalidOperationException("Complete retained fingerIds List storage changed outside its recognized write sequence.");
        }
        foreach (var pair in _expectedArrays)
            if (!SeekFingerIdsState.Same(ReadArray(pair.Key), pair.Value))
                throw new InvalidOperationException("An original complete fingerIds backing changed outside its recognized write sequence.");
    }
    private void RequireSelected(int ordinal, long listReference)
    {
        if (_pending is not null) throw new InvalidOperationException("An entered fingerIds dispatch still awaits independent readback.");
        if (ReadNote(ordinal).ListReference != _expectedFields[ordinal])
            throw new InvalidOperationException("The selected fingerIds field changed outside this participant; it was not overwritten.");
        if (listReference == 0) return;
        if (!_expectedLists.TryGetValue(listReference, out var expected) || !SeekFingerIdsState.Same(ReadList(listReference), expected))
            throw new InvalidOperationException("The selected complete fingerIds List changed before its conditional mutation.");
        if (_baselineLists.TryGetValue(listReference, out var original)
            && !SeekFingerIdsState.Same(ReadArray(original.Backing), _expectedArrays[original.Backing]))
            throw new InvalidOperationException("The selected List's original detached backing changed outside this participant.");
    }
    private void Dispatch(MutationKind kind, int ordinal, long listReference = 0, long value = 0,
        int capacitySlot = 0, SeekFingerIdsRestoreField field = default)
    {
        if (kind == MutationKind.Field && value != 0) listReference = value;
        RequireSelected(ordinal, listReference);
        SeekFingerIdsListStorage? currentFieldList = null;
        if (kind == MutationKind.Field && _expectedFields[ordinal] != 0)
        {
            currentFieldList = _expectedLists[_expectedFields[ordinal]];
            if (!SeekFingerIdsState.Same(ReadList(currentFieldList.Reference), currentFieldList))
                throw new InvalidOperationException("The current fingerIds List changed before conditional field replacement.");
        }
        var before = listReference == 0 ? null : _expectedLists[listReference];
        SeekFingerIdsArrayStorage? targetOriginalBacking = null;
        if (kind == MutationKind.Field && before is not null && _baselineLists.TryGetValue(before.Reference, out var targetOriginal))
        {
            targetOriginalBacking = _expectedArrays[targetOriginal.Backing];
            if (_restoring && (!SeekFingerIdsState.Same(before, targetOriginal)
                || !SeekFingerIdsState.Same(targetOriginalBacking, _baselineArrays[targetOriginal.Backing])))
                throw new InvalidOperationException("An original fingerIds List cannot be reconnected before its complete baseline storage is restored.");
        }
        var arrays = new Dictionary<long, SeekFingerIdsArrayStorage>();
        if (before is not null && _expectedArrays.TryGetValue(before.Backing, out var attached)) arrays.Add(attached.Reference, attached);
        if (_baselineLists.TryGetValue(listReference, out var original)
            && _expectedArrays.TryGetValue(original.Backing, out var detached)) arrays[detached.Reference] = detached;
        var pending = new PendingMutation(kind, ordinal, listReference, _expectedFields[ordinal], value, capacitySlot, field, before, arrays);
        var dispatched = false;
        void Enter()
        {
            Local();
            if (dispatched || _pending is not null) throw new InvalidOperationException("The final fingerIds dispatch guard was entered twice.");
            _pending = pending; dispatched = true;
            if (!_restoring) ++_attempted;
            SideEffectsUnknown = true; Local();
        }
        Local();
        switch (kind)
        {
            case MutationKind.Clear: _access.Clear(ordinal, pending.FieldBefore, before!, Enter); break;
            case MutationKind.Add: _access.Add(ordinal, pending.FieldBefore, before!, checked((int)value), Enter); break;
            case MutationKind.Field: _access.WriteField(ordinal, value, pending.FieldBefore, currentFieldList,
                before, targetOriginalBacking, Enter); break;
            case MutationKind.CapacitySlot:
                _access.RestoreCapacitySlot(ordinal, pending.FieldBefore, before!, _expectedArrays[original!.Backing], capacitySlot, checked((int)value), Enter); break;
            case MutationKind.ListField: _access.RestoreListField(ordinal, pending.FieldBefore, before!, field, value, Enter); break;
        }
        Local();
        if (!dispatched) throw new InvalidOperationException("No final official fingerIds dispatch entry was recorded.");
        ResolvePending(true);
    }
    private bool MatchArrays(IReadOnlyDictionary<long, SeekFingerIdsArrayStorage> arrays)
    {
        foreach (var pair in arrays)
            if (!SeekFingerIdsState.Same(ReadArray(pair.Key), pair.Value)) return false;
        return true;
    }
    private void ResolvePending(bool requireAfter)
    {
        var pending = _pending ?? throw new InvalidOperationException("No entered fingerIds dispatch is awaiting readback.");
        var actualField = ReadNote(pending.Ordinal).ListReference;
        if (pending.Kind == MutationKind.Field)
        {
            if (!requireAfter && actualField == pending.FieldBefore) { _pending = null; return; }
            if (actualField != pending.Value) throw new InvalidOperationException("The entered fingerIds setter has an unrecognized field-reference before/after state.");
            _expectedFields[pending.Ordinal] = pending.Value; _pending = null; return;
        }
        if (actualField != pending.FieldBefore)
            throw new InvalidOperationException("The selected fingerIds field reference changed during its List mutation.");
        var before = pending.ListBefore!; var actual = ReadList(pending.ListReference);
        if (!requireAfter && SeekFingerIdsState.Same(actual, before) && MatchArrays(pending.ArraysBefore)) { _pending = null; return; }
        var arrays = new Dictionary<long, SeekFingerIdsArrayStorage>(pending.ArraysBefore);
        SeekFingerIdsListStorage after;
        if (pending.Kind == MutationKind.CapacitySlot)
        {
            var original = arrays[_baselineLists[pending.ListReference].Backing];
            var slots = original.CapacitySlots.ToArray(); slots[pending.CapacitySlot] = checked((int)pending.Value);
            var changed = original with { CapacitySlots = System.Array.AsReadOnly(slots) }; arrays[original.Reference] = changed;
            after = before.Backing == original.Reference ? before with { CapacitySlots = changed.CapacitySlots } : before;
        }
        else if (pending.Kind == MutationKind.ListField)
        {
            after = pending.RestoreField switch
            {
                SeekFingerIdsRestoreField.Size => before with { Size = checked((int)pending.Value) },
                SeekFingerIdsRestoreField.Version => before with { Version = checked((int)pending.Value) },
                SeekFingerIdsRestoreField.Items => before with { Backing = pending.Value,
                    ArrayClass = _expectedArrays[pending.Value].Class, CapacitySlots = _expectedArrays[pending.Value].CapacitySlots },
                _ => throw new ArgumentOutOfRangeException()
            };
        }
        else if (pending.Kind == MutationKind.Clear)
        {
            var preserved = before with { Size = 0, Version = unchecked(before.Version + 1) };
            var slots = before.CapacitySlots.ToArray(); System.Array.Clear(slots, 0, before.Size);
            var cleared = preserved with { CapacitySlots = System.Array.AsReadOnly(slots) };
            if (SeekFingerIdsState.Same(actual, preserved)) after = preserved;
            else if (SeekFingerIdsState.Same(actual, cleared)) after = cleared;
            else throw new InvalidOperationException("Entered Int32 List.Clear produced neither the complete preserved nor fully cleared active-slot model.");
            if (arrays.ContainsKey(after.Backing)) arrays[after.Backing] = SeekFingerIdsState.Array(after);
        }
        else
        {
            var slots = before.CapacitySlots.ToArray();
            if (before.Size < slots.Length)
            {
                slots[before.Size] = checked((int)pending.Value);
                after = before with { Size = before.Size + 1, Version = unchecked(before.Version + 1), CapacitySlots = System.Array.AsReadOnly(slots) };
            }
            else
            {
                var capacity = slots.Length == 0 ? 4 : slots.Length * 2;
                if (actual.Backing == before.Backing || _knownBackings.Contains(actual.Backing)
                    || actual.ArrayClass != before.ArrayClass || actual.CapacitySlots.Count != capacity || capacity > MaximumCapacity
                    || !SeekFingerIdsState.Same(ReadArray(before.Backing), SeekFingerIdsState.Array(before)))
                    throw new InvalidOperationException("Entered fingerIds Add has no recognized complete official growth state and unchanged old backing.");
                var grown = new int[capacity]; System.Array.Copy(slots, grown, before.Size); grown[before.Size] = checked((int)pending.Value);
                after = before with { Backing = actual.Backing, Size = before.Size + 1, Version = unchecked(before.Version + 1),
                    CapacitySlots = System.Array.AsReadOnly(grown) };
            }
            if (arrays.ContainsKey(after.Backing)) arrays[after.Backing] = SeekFingerIdsState.Array(after);
        }
        if (!SeekFingerIdsState.Same(actual, after) || !MatchArrays(arrays))
            throw new InvalidOperationException("Entered fingerIds dispatch has an unrecognized complete before/after state; its side effects remain unknown.");
        _expectedLists[pending.ListReference] = after;
        foreach (var pair in arrays) _expectedArrays[pair.Key] = pair.Value;
        _knownBackings.Add(after.Backing); _pending = null;
    }
    public void VerifyPrepared() => Operation(() =>
    {
        if (_entered || _restoring) throw new InvalidOperationException("The fingerIds preparation was already entered.");
        VerifySource(); RequireAllOwned(); return 0;
    });
    public void Commit() => Operation(() =>
    {
        if (_entered || _restoring) throw new InvalidOperationException("FingerIds commit is once-only and cannot follow restoration.");
        _entered = true; VerifySource(); RequireAllOwned();
        var rebuilt = new HashSet<long>();
        for (var ordinal = 0; ordinal < _baselineFields.Length; ++ordinal)
        {
            if (_targets[ordinal] is not { } target)
            {
                if (_expectedFields[ordinal] != 0) Dispatch(MutationKind.Field, ordinal, value: 0);
                continue;
            }
            var list = _baselineFields[ordinal].ListReference is var original && original != 0 ? original : _scratch[ordinal];
            if (list == 0) throw new InvalidOperationException("An explicitly nonnull fingerIds target has no independently prepared actual List.");
            if (rebuilt.Add(list))
            {
                Dispatch(MutationKind.Clear, ordinal, list);
                foreach (var member in target) Dispatch(MutationKind.Add, ordinal, list, member);
            }
            if (_expectedFields[ordinal] != list) Dispatch(MutationKind.Field, ordinal, value: list);
        }
        VerifySource(); RequireAllOwned(); _committed = true; return 0;
    });
    public GameplayControlConfirmation Confirm() => Operation(() =>
    {
        if (!_committed || _restoring) throw new InvalidOperationException("Finish the complete fingerIds commit before confirmation.");
        VerifySource(); RequireAllOwned(); var observed = true;
        for (var ordinal = 0; ordinal < _targets.Length; ++ordinal)
        {
            if (_targets[ordinal] is not { } target) observed &= _expectedFields[ordinal] == 0;
            else
            {
                var reference = _expectedFields[ordinal];
                observed &= reference != 0 && _expectedLists.TryGetValue(reference, out var list)
                    && list.Size == target.Length && list.CapacitySlots.Take(list.Size).SequenceEqual(target);
            }
        }
        _confirmed = observed; SideEffectsUnknown = !observed || _unknownScratch.Count != 0;
        return new GameplayControlConfirmation(observed,
            "Independent readback of every actual note's explicit nullable fingerIds sequence, including repeated signed Int32 occurrences. No input/Hold idle, renderer synchronization, clock or public Seek state is confirmed.");
    });
    private bool ResolveRestoreFailure(Exception error)
    {
        _restoreErrors.Add(error.Message); Local();
        if (_pending is null) return true;
        try { ResolvePending(false); return true; }
        catch (Exception observation) { _restoreErrors.Add(observation.Message); SideEffectsUnknown = true; return false; }
    }
    public void Restore() => Operation(() =>
    {
        if (_restoring) throw new InvalidOperationException("FingerIds restoration is once-only.");
        _restoring = true; VerifySource();
        if (_pending is not null)
        {
            try { ResolvePending(false); }
            catch (Exception error)
            {
                _restoreErrors.Add(error.Message); SideEffectsUnknown = true;
                var pending = _pending!;
                if (pending.Kind != MutationKind.Field && !_baselineLists.ContainsKey(pending.ListReference)
                    && _baselineFields[pending.Ordinal].ListReference == 0 && ReadNote(pending.Ordinal).ListReference == 0)
                {
                    // No field publication occurred. Keep the unresolved private
                    // mutation and its independently rooted scratch owner; this
                    // does not assert that allocation/constructor/writes had no
                    // native effects. Other original source restoration can run.
                    _unknownScratch.Add(pending); _pending = null;
                }
                else return 0;
            }
        }
        RequireAllOwned(originalSourceOnly: true);
        foreach (var pair in _baselineLists.Reverse())
        {
            var baseline = pair.Value;
            var ordinal = _originalOrdinals[baseline.Reference];
            var original = _baselineArrays[baseline.Backing];
            for (var slot = original.CapacitySlots.Count - 1; slot >= 0; --slot)
            {
                if (_expectedArrays[original.Reference].CapacitySlots[slot] == original.CapacitySlots[slot]) continue;
                try { Dispatch(MutationKind.CapacitySlot, ordinal, baseline.Reference, original.CapacitySlots[slot], slot); }
                catch (Exception error) { if (!ResolveRestoreFailure(error)) return 0; }
            }
            foreach (var field in new[] { SeekFingerIdsRestoreField.Size, SeekFingerIdsRestoreField.Items, SeekFingerIdsRestoreField.Version })
            {
                var current = _expectedLists[baseline.Reference];
                var wanted = field switch { SeekFingerIdsRestoreField.Size => baseline.Size,
                    SeekFingerIdsRestoreField.Items => baseline.Backing, _ => baseline.Version };
                var sampled = field switch { SeekFingerIdsRestoreField.Size => current.Size,
                    SeekFingerIdsRestoreField.Items => current.Backing, _ => current.Version };
                if (wanted == sampled) continue;
                if (field == SeekFingerIdsRestoreField.Items && current.Size > original.CapacitySlots.Count)
                { _restoreErrors.Add("Original fingerIds backing could not safely be reattached after size restoration failed."); continue; }
                try { Dispatch(MutationKind.ListField, ordinal, baseline.Reference, wanted, field: field); }
                catch (Exception error) { if (!ResolveRestoreFailure(error)) return 0; }
            }
        }
        // All original List storage is restored before any field can reconnect it.
        for (var ordinal = _baselineFields.Length - 1; ordinal >= 0; --ordinal)
        {
            var wanted = _baselineFields[ordinal].ListReference;
            if (_expectedFields[ordinal] == wanted) continue;
            try { Dispatch(MutationKind.Field, ordinal, value: wanted); }
            catch (Exception error) { if (!ResolveRestoreFailure(error)) return 0; }
        }
        VerifySource(); RequireAllOwned(originalSourceOnly: true);
        return 0;
    });
    public GameplayControlRestoration ConfirmRestored() => Operation(() =>
    {
        if (!_restoring) throw new InvalidOperationException("Attempt fingerIds restoration before complete original-source readback.");
        VerifySource(); var fields = ReadFields();
        var restored = _pending is null && fields.SequenceEqual(_baselineFields);
        foreach (var pair in _baselineLists) restored &= SeekFingerIdsState.Same(ReadList(pair.Key), pair.Value);
        foreach (var pair in _baselineArrays) restored &= SeekFingerIdsState.Same(ReadArray(pair.Key), pair.Value);
        _restored = restored; SideEffectsUnknown = !restored || _unknownScratch.Count != 0;
        return new GameplayControlRestoration(restored,
            (restored ? "Every original fingerIds field reference and original List/backing/capacity slot/size/signed version was independently read back. "
                : "The complete original fingerIds source was not restored. ")
            + "Allocated scratch Lists remain independently owned until disposal. "
            + (_unknownScratch.Count == 0 ? "" : "Unpublished scratch mutations remain unknown; source restoration does not cancel or erase those native effects. ")
            + (Failure is null ? "" : "Original operation failure: " + Failure + ". ") + string.Join("; ", _restoreErrors));
    });
    public void Dispose()
    {
        _access.VerifyOwnerThread();
        if (_cleanupBusy || _notifyingDisposal) return;
        _disposeRequested = true;
        if (_attempted != 0 && !_confirmed && !_restored || _unknownScratch.Count != 0) SideEffectsUnknown = true;
        Exception? failure = null; _notifyingDisposal = true;
        try { _access.RequestDispose(); }
        catch (Exception error) { CleanupFailure = error.Message; failure = error; }
        finally { _notifyingDisposal = false; }
        if (!_busy) try { Release(); } catch (Exception error) { failure = error; }
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
