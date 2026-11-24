using RizToolbox.Adapter.UiInput;

namespace RizToolbox.Adapter.Gameplay;

internal sealed partial class NativeSeekCanvasPositionCommands
{
    private static bool Same(NativeVector3 a, NativeVector3 b)
        => BitConverter.SingleToInt32Bits(a.X) == BitConverter.SingleToInt32Bits(b.X)
            && BitConverter.SingleToInt32Bits(a.Y) == BitConverter.SingleToInt32Bits(b.Y)
            && BitConverter.SingleToInt32Bits(a.Z) == BitConverter.SingleToInt32Bits(b.Z);

    private static bool Same(IReadOnlyList<NativeVector3> a, IReadOnlyList<NativeVector3> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; ++i) if (!Same(a[i], b[i])) return false;
        return true;
    }

    private static bool Same(SeekCanvasPositionTarget a, SeekCanvasPositionTarget b)
        => a.Ordinal == b.Ordinal && a.Transform == b.Transform && Same(a.LocalPosition, b.LocalPosition);

    private SeekCanvasPositionTarget[] CopyTargets()
    {
        VerifySource(); var raw = _access.Targets; Local();
        if (raw is null) throw new InvalidDataException("The complete actual Canvas target array is required.");
        var count = raw.Count; Local();
        if (count < 0 || count > MaximumTargets)
            throw new InvalidDataException("The complete actual Canvas targets exceed their retained bound.");
        var copy = new SeekCanvasPositionTarget[count];
        for (var i = 0; i < count; ++i)
        {
            Local(); var row = raw[i]; Local();
            if (row is null || row.Ordinal != i || row.Transform == 0
                || !float.IsFinite(row.LocalPosition.X) || !float.IsFinite(row.LocalPosition.Y)
                || !float.IsFinite(row.LocalPosition.Z))
                throw new InvalidDataException("Actual Canvas targets require complete ordered identities and finite local positions.");
            copy[i] = row with { };
        }
        Local(); var finalCount = raw.Count; Local();
        if (finalCount != count) throw new InvalidOperationException("The complete actual Canvas targets changed during copying.");
        return copy;
    }

    private void BuildUniqueTargets()
    {
        var first = new Dictionary<long, int>(); var unique = new List<SeekCanvasPositionTarget>();
        _firstOrdinals = new int[_targets.Length];
        for (var i = 0; i < _targets.Length; ++i)
        {
            var row = _targets[i];
            if (first.TryGetValue(row.Transform, out var ordinal))
            {
                if (!Same(_targets[ordinal].LocalPosition, row.LocalPosition))
                    throw new InvalidDataException("Shared actual Canvas Transforms have conflicting target raw bits.");
                _firstOrdinals[i] = ordinal;
            }
            else
            {
                first.Add(row.Transform, i); _firstOrdinals[i] = i; unique.Add(row);
            }
        }
        _uniqueTargets = unique.ToArray();
        _commands.Capacity = checked(2 * _uniqueTargets.Length);
        _enteredOrdinals.Capacity = _uniqueTargets.Length;
    }

    private void VerifyTargets()
    {
        var actual = CopyTargets();
        if (actual.Length != _targets.Length)
            throw new InvalidOperationException("The complete actual Canvas target coverage changed.");
        for (var i = 0; i < actual.Length; ++i)
            if (!Same(actual[i], _targets[i]))
                throw new InvalidOperationException("An actual Canvas occurrence, Transform or target raw bits changed.");
    }

    private void VerifySelectedTarget(int ordinal)
    {
        VerifySource(); var raw = _access.Targets; Local();
        if (raw is null) throw new InvalidOperationException("The actual Canvas target array disappeared.");
        var count = raw.Count; Local();
        if (count != _targets.Length || ordinal < 0 || ordinal >= count)
            throw new InvalidOperationException("The selected actual Canvas occurrence is outside complete retained coverage.");
        var row = raw[ordinal]; Local();
        if (row is null || !Same(row, _targets[ordinal]))
            throw new InvalidOperationException("The selected actual Canvas target identity or raw bits changed.");
        var finalCount = raw.Count; Local();
        if (finalCount != count) throw new InvalidOperationException("The actual Canvas target count changed during its selected check.");
    }

    private NativeVector3[] Read()
    {
        VerifyTargets(); VerifySource(); var raw = _access.ReadSnapshot(); Local();
        if (raw is null) throw new InvalidDataException("A complete actual Canvas localPosition snapshot is required.");
        var count = raw.Count; Local();
        if (count < 0 || count > MaximumTargets || count != _targets.Length)
            throw new InvalidDataException("The complete actual Canvas localPosition snapshot has another count.");
        var values = new NativeVector3[count];
        for (var i = 0; i < count; ++i) { Local(); values[i] = raw[i]; Local(); }
        var finalCount = raw.Count; Local();
        if (finalCount != count) throw new InvalidOperationException("The complete actual Canvas snapshot changed during copying.");
        VerifySource();
        for (var i = 0; i < values.Length; ++i)
            if (!Same(values[i], values[_firstOrdinals[i]]))
            {
                if (_prepared) MarkUnrecognized();
                throw new InvalidOperationException("Aliases of the same actual Canvas Transform have different observed raw bits.");
            }
        VerifyTargets();
        return values;
    }

    private NativeVector3 ReadSelected(int ordinal)
    {
        VerifySelectedTarget(ordinal); VerifySource(); var value = _access.ReadSlot(ordinal); Local();
        VerifySelectedTarget(ordinal); VerifySource();
        return value;
    }

    private void MarkUnrecognized()
    {
        _unrecognized = true; _unknown = true; _appliedValuesVerified = false; _restoredValuesVerified = false;
        _failure ??= "An observed actual Canvas localPosition state differs from its complete expected raw bits.";
        if (_externalEffects) _retained = true;
    }

    private void SetExpectedAliases(int ordinal, NativeVector3 value)
    {
        for (var i = 0; i < _expected.Length; ++i)
            if (_firstOrdinals[i] == ordinal) _expected[i] = value;
    }

    private bool RecordPendingObservation(NativeVector3 value)
    {
        if (_pendingCommand < 0) return true;
        var index = _pendingCommand; var row = _commands[index];
        _commands[index] = row with { Readback = value, TargetObserved = Same(value, row.Target),
            OriginalObserved = Same(value, _baseline[row.Ordinal]) };
        if (Same(value, row.Before) || Same(value, row.Target))
        {
            SetExpectedAliases(row.Ordinal, value); _pendingCommand = -1; return true;
        }
        _pendingCommand = -1; MarkUnrecognized();
        _commands[index] = _commands[index] with
        { Failure = _commands[index].Failure ?? "The selected native setter has an unrecognized visible post-state." };
        return false;
    }

    private NativeVector3[] Observe()
    {
        var actual = Read();
        if (_pendingCommand >= 0) RecordPendingObservation(actual[_commands[_pendingCommand].Ordinal]);
        if (!Same(actual, _expected)) MarkUnrecognized();
        return actual;
    }
}
