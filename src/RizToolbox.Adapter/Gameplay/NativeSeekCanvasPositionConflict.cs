using RizToolbox.Adapter.UiInput;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>Raw components of one actually observed Vector3. Integer components
/// preserve signed zero and NaN payloads without float equality or arithmetic.</summary>
internal readonly record struct NativeSeekCanvasPositionBits(int X, int Y, int Z)
{
    internal static NativeSeekCanvasPositionBits From(NativeVector3 value)
        => new(BitConverter.SingleToInt32Bits(value.X), BitConverter.SingleToInt32Bits(value.Y),
            BitConverter.SingleToInt32Bits(value.Z));
}

internal readonly record struct NativeSeekCanvasPositionSample(int Ordinal, long Transform,
    NativeSeekCanvasPositionBits Bits);

/// <summary>Observed values, distinct from a failed getter or metadata check.
/// With MultipleSamples both values were read; otherwise FirstBits is the exact
/// conditional expectation and SecondBits is the value actually read. Complete
/// snapshots retain every sampled occurrence, including stable third states.</summary>
internal sealed record NativeSeekCanvasPositionConflict(int Ordinal, long Transform,
    NativeSeekCanvasPositionBits FirstBits, NativeSeekCanvasPositionBits SecondBits, bool MultipleSamples)
{
    internal const string Key = "NativeSeekCanvasPositionConflict";
    internal IReadOnlyList<NativeSeekCanvasPositionSample>? FirstSnapshot { get; init; }
    internal IReadOnlyList<NativeSeekCanvasPositionSample>? SecondSnapshot { get; init; }

    internal static Exception Error(int ordinal, long transform, NativeVector3 first,
        NativeVector3 second, bool multipleSamples = false)
    {
        var conflict = new NativeSeekCanvasPositionConflict(ordinal, transform,
            NativeSeekCanvasPositionBits.From(first), NativeSeekCanvasPositionBits.From(second), multipleSamples);
        return Error(conflict);
    }

    internal static Exception SnapshotError(int ordinal, IReadOnlyList<long> transforms,
        IReadOnlyList<NativeVector3> first, IReadOnlyList<NativeVector3> second)
    {
        var count = transforms.Count;
        if (count < 0 || count > NativeSeekCanvasPositionCommands.MaximumTargets || first.Count != count || second.Count != count
            || (uint)ordinal >= (uint)count)
            throw new InvalidDataException("A complete position-conflict observation requires bounded original occurrence coverage.");
        var before = new NativeSeekCanvasPositionSample[count];
        var after = new NativeSeekCanvasPositionSample[count];
        for (var i = 0; i < count; ++i)
        {
            before[i] = new(i, transforms[i], NativeSeekCanvasPositionBits.From(first[i]));
            after[i] = new(i, transforms[i], NativeSeekCanvasPositionBits.From(second[i]));
        }
        var conflict = new NativeSeekCanvasPositionConflict(ordinal, transforms[ordinal],
            before[ordinal].Bits, after[ordinal].Bits, true)
        { FirstSnapshot = Array.AsReadOnly(before), SecondSnapshot = Array.AsReadOnly(after) };
        return Error(conflict);
    }

    private static Exception Error(NativeSeekCanvasPositionConflict conflict)
    {
        var error = new InvalidOperationException("An actual Canvas localPosition raw-bit conflict was observed.");
        error.Data[Key] = conflict; return error;
    }
}
