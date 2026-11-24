namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekCanvasCacheParticipant
{
    private static bool Same(SeekCanvasCacheSlot left, SeekCanvasCacheSlot right)
        => left.Ordinal == right.Ordinal && left.Kind == right.Kind && left.Owner == right.Owner
            && left.Index == right.Index && left.TargetBits == right.TargetBits;

    private SeekCanvasCacheSlot[] CopySlots()
    {
        VerifySource(); var raw = _access.Slots; Local();
        if (raw is null) throw new InvalidDataException("The complete actual Canvas cache slots are required.");
        var count = raw.Count; Local();
        if (count < 2 || count > MaximumSlots || (count - 2) % 3 != 0)
            throw new InvalidDataException("Canvas cache coverage requires three equal complete bounded arrays and two scalars.");
        var length = (count - 2) / 3;
        var copy = new SeekCanvasCacheSlot[count];
        var arrayOwners = new long[3]; long canvasOwner = 0;
        for (var ordinal = 0; ordinal < count; ++ordinal)
        {
            Local(); var row = raw[ordinal]; Local();
            var arraySlot = ordinal < 3 * length;
            var group = arraySlot ? ordinal / length : ordinal - 3 * length + 3;
            var index = arraySlot ? ordinal % length : -1;
            if (row is null || row.Ordinal != ordinal || row.Kind != (SeekCanvasCacheKind)group
                || row.Owner == 0 || row.Index != index || !float.IsFinite(BitConverter.Int32BitsToSingle(row.TargetBits)))
                throw new InvalidDataException("Actual Canvas cache slots require complete ordered identities and finite targets.");
            if (arraySlot)
            {
                if (index == 0) arrayOwners[group] = row.Owner;
                else if (arrayOwners[group] != row.Owner)
                    throw new InvalidDataException("Each actual Canvas cache array must retain one complete array identity.");
            }
            else if (group == (int)SeekCanvasCacheKind.NowTime) canvasOwner = row.Owner;
            else if (canvasOwner != row.Owner)
                throw new InvalidDataException("Actual Canvas nowTime and speed must belong to the same CanvasControl.");
            copy[ordinal] = row with { };
        }
        var finalCount = raw.Count; Local();
        if (finalCount != count) throw new InvalidOperationException("Actual Canvas cache coverage changed during copying.");
        return copy;
    }

    private void BuildUniqueSlots()
    {
        // Array aliases ignore the semantic field kind; scalar fields on the same
        // CanvasControl remain distinct physical locations.
        var first = new Dictionary<(long Owner, int Index, SeekCanvasCacheKind? Scalar), int>();
        var last = new Dictionary<int, int>();
        var unique = new List<SeekCanvasCacheSlot>();
        _firstOrdinals = new int[_slots.Length];
        _nextAliases = new int[_slots.Length]; Array.Fill(_nextAliases, -1);
        for (var ordinal = 0; ordinal < _slots.Length; ++ordinal)
        {
            var row = _slots[ordinal];
            var key = (row.Owner, row.Index, row.Index < 0 ? row.Kind : (SeekCanvasCacheKind?)null);
            if (first.TryGetValue(key, out var prior))
            {
                if (_slots[prior].TargetBits != row.TargetBits)
                    throw new InvalidDataException("Shared actual Canvas cache storage has conflicting target raw bits.");
                _firstOrdinals[ordinal] = prior; _nextAliases[last[prior]] = ordinal; last[prior] = ordinal;
            }
            else
            {
                first.Add(key, ordinal); last.Add(ordinal, ordinal);
                _firstOrdinals[ordinal] = ordinal; unique.Add(row);
            }
        }
        _uniqueSlots = unique.ToArray();
        _stores.Capacity = checked(2 * _uniqueSlots.Length);
        _enteredOrdinals.Capacity = _uniqueSlots.Length;
    }

    private void VerifySlots()
    {
        var actual = CopySlots();
        if (actual.Length != _slots.Length)
        {
            if (_prepared) MarkUnrecognized();
            throw new InvalidOperationException("The complete actual Canvas cache coverage changed.");
        }
        for (var ordinal = 0; ordinal < actual.Length; ++ordinal)
            if (!Same(actual[ordinal], _slots[ordinal]))
            {
                if (_prepared) MarkUnrecognized();
                throw new InvalidOperationException("An actual Canvas cache slot identity or target raw bits changed.");
            }
    }

    private void VerifySelectedSlot(int ordinal)
    {
        VerifySource(); var raw = _access.Slots; Local();
        if (raw is null) throw new InvalidOperationException("The actual Canvas cache slot view disappeared.");
        var count = raw.Count; Local();
        if (count != _slots.Length || ordinal < 0 || ordinal >= count)
        {
            MarkUnrecognized();
            throw new InvalidOperationException("The selected actual Canvas cache slot is outside complete retained coverage.");
        }
        var row = raw[ordinal]; Local();
        if (row is null || !Same(row, _slots[ordinal]))
        {
            MarkUnrecognized();
            throw new InvalidOperationException("The selected actual Canvas cache slot identity or target changed.");
        }
        var finalCount = raw.Count; Local();
        if (finalCount != count)
        {
            MarkUnrecognized();
            throw new InvalidOperationException("Actual Canvas cache coverage changed during its selected check.");
        }
    }

    private int[] Read()
    {
        VerifySlots(); VerifySource(); var raw = _access.ReadSnapshot(); Local();
        if (raw is null) throw new InvalidDataException("A complete actual Canvas cache raw-bit snapshot is required.");
        var count = raw.Count; Local();
        if (count < 2 || count > MaximumSlots || count != _slots.Length)
            throw new InvalidDataException("The actual Canvas cache snapshot has incomplete or foreign coverage.");
        var values = new int[count];
        for (var ordinal = 0; ordinal < count; ++ordinal) { Local(); values[ordinal] = raw[ordinal]; Local(); }
        var finalCount = raw.Count; Local();
        if (finalCount != count) throw new InvalidOperationException("The complete actual Canvas cache snapshot changed during copying.");
        VerifySource();
        for (var ordinal = 0; ordinal < count; ++ordinal)
            if (values[ordinal] != values[_firstOrdinals[ordinal]])
            {
                if (_prepared) MarkUnrecognized();
                throw new InvalidOperationException("Aliases of the same actual Canvas cache storage have inconsistent observed raw bits.");
            }
        VerifySlots(); return values;
    }

    private int ReadSelected(int ordinal)
    {
        VerifySelectedSlot(ordinal); VerifySource(); var bits = _access.ReadSlot(ordinal); Local();
        VerifySelectedSlot(ordinal); VerifySource(); return bits;
    }

    private bool MatchesTargets(IReadOnlyList<int> actual)
    {
        if (actual.Count != _slots.Length) return false;
        for (var ordinal = 0; ordinal < actual.Count; ++ordinal)
            if (actual[ordinal] != _slots[ordinal].TargetBits) return false;
        return true;
    }

    private bool CacheResponsibilityClosed => !_unrecognized && !_dispatchContractUnknown
        && (_appliedValuesVerified || _restoredValuesVerified);

    private void UpdateResponsibility()
    {
        if (_released) { _retained = false; return; }
        _retained = _constructionFailure is not null || _cleanupException is not null
            || _dispatchContractUnknown || _hasDispatched && !CacheResponsibilityClosed;
        _unknown = _unrecognized || _cleanupException is not null || _dispatchContractUnknown
            || _hasDispatched && !CacheResponsibilityClosed;
    }

    private void RevokeConfirmations()
    { _appliedValuesVerified = false; _restoredValuesVerified = false; UpdateResponsibility(); }

    private void MarkUnrecognized()
    {
        _unrecognized = true; RevokeConfirmations();
        _failure ??= "An observed actual Canvas cache state differs from its complete expected raw bits.";
    }

    private void SetExpectedAliases(int ordinal, int bits)
    {
        for (var alias = ordinal; alias >= 0; alias = _nextAliases[alias]) _expected[alias] = bits;
    }

    private bool RecordPendingObservation(int bits)
    {
        if (_pendingStore < 0) return true;
        var index = _pendingStore; var row = _stores[index];
        _stores[index] = row with { Readback = bits, TargetObserved = bits == row.TargetBits,
            OriginalObserved = bits == _baseline[row.Ordinal] };
        _pendingStore = -1;
        if (bits == row.BeforeBits || bits == row.TargetBits)
        {
            SetExpectedAliases(row.Ordinal, bits); return true;
        }
        MarkUnrecognized();
        _stores[index] = _stores[index] with
        { Failure = _stores[index].Failure ?? "The selected native cache store has an unrecognized visible post-state." };
        return false;
    }

    private int[] Observe()
    {
        var actual = Read();
        if (_pendingStore >= 0) RecordPendingObservation(actual[_stores[_pendingStore].Ordinal]);
        if (!actual.SequenceEqual(_expected)) MarkUnrecognized();
        if (_appliedValuesVerified && !MatchesTargets(actual)) _appliedValuesVerified = false;
        if (_restoredValuesVerified && !actual.SequenceEqual(_baseline)) _restoredValuesVerified = false;
        UpdateResponsibility(); return actual;
    }
}
