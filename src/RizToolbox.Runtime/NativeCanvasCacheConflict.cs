namespace RizToolbox.Runtime;

internal readonly record struct NativeCanvasCacheObservation(long Owner, int Index, string Field, int Bits);

/// <summary>An actually observed raw-bit conflict, distinct from a failed read
/// or metadata check. A later return to the expected bits cannot erase it.</summary>
internal sealed record NativeCanvasCacheConflict(long Owner, int Index, string Field,
    int ExpectedBits, int ObservedBits, bool MultipleSamples)
{
    internal const string Key = "NativeCanvasCacheConflict";
    internal IReadOnlyList<NativeCanvasCacheObservation> Observations { get; private init; }
        = Array.Empty<NativeCanvasCacheObservation>();

    internal static Exception Error(long owner, int index, string field, int expected, int observed,
        bool multipleSamples = false, IReadOnlyList<NativeCanvasCacheObservation>? observations = null)
    {
        var error = new InvalidOperationException("An actual Canvas cache raw-bit conflict was observed: " + field);
        observations ??= multipleSamples
            ? new[] { new NativeCanvasCacheObservation(owner, index, field, expected), new NativeCanvasCacheObservation(owner, index, field, observed) }
            : new[] { new NativeCanvasCacheObservation(owner, index, field, observed) };
        error.Data[Key] = new NativeCanvasCacheConflict(owner, index, field, expected, observed, multipleSamples)
        { Observations = Array.AsReadOnly(observations.ToArray()) };
        return error;
    }

    // Preserve completed outer samples when an inner array observation throws.
    // Neither the original conflict nor any previously published list is mutated.
    internal static void PrependObservations(Exception error, IReadOnlyList<NativeCanvasCacheObservation> observations)
    {
        if (observations.Count == 0 || error.Data[Key] is not NativeCanvasCacheConflict conflict) return;
        error.Data[Key] = conflict with
        { Observations = Array.AsReadOnly(observations.Concat(conflict.Observations).ToArray()) };
    }
}
