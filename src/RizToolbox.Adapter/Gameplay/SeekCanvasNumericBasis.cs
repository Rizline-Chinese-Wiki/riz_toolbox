using System.Runtime.CompilerServices;

namespace RizToolbox.Adapter.Gameplay;

/// <summary>A historical, owned copy of the initialized Canvas numeric inputs.
/// It carries the exact original capture/Plan and physical array identities;
/// reading it performs no native work and grants no current-source or Seek proof.</summary>
internal sealed class SeekCanvasNumericBasis
{
    internal NativeSeekCanvasTargetCapture CanvasCapture { get; }
    internal NativeSeekCanvasTargetPlan CanvasPlan { get; }
    internal SeekCanvasSourceIdentity SourceIdentity { get; }
    internal long TransformsArray { get; }
    internal IReadOnlyList<long> TransformReferences { get; }
    internal long EaseArray { get; }
    internal IReadOnlyList<SeekCanvasSourceEase> Eases { get; }
    internal bool CompleteSeekPermitted => false;

    private SeekCanvasNumericBasis(NativeSeekCanvasTargetCapture capture, NativeSeekCanvasTargetPlan plan,
        SeekCanvasSourceIdentity identity, long transformsArray, long[] transforms,
        long easeArray, SeekCanvasSourceEase[] eases)
    {
        CanvasCapture = capture; CanvasPlan = plan; SourceIdentity = identity;
        TransformsArray = transformsArray; TransformReferences = Array.AsReadOnly(transforms);
        EaseArray = easeArray; Eases = Array.AsReadOnly(eases);
    }

    internal static SeekCanvasNumericBasis Copy(NativeSeekCanvasTargetCapture capture,
        NativeSeekCanvasTargetPlan plan, SeekCanvasSourceSnapshot source, Action guard)
    {
        ArgumentNullException.ThrowIfNull(capture); ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(guard);
        guard();
        if (!ReferenceEquals(capture.Plan, plan) || source.Identity is null || source.Identity.Notes is null
            || source.TransformsArray == 0 || source.EaseArray == 0
            || source.Transforms.Count != plan.Rows.Count || source.Moves.Count != plan.Rows.Count
            || source.Eases.Count == 0 || source.Eases.Count > NativeSeekCanvasTargetCapture.MaximumEaseRows)
            throw new InvalidDataException("The original Canvas numeric source or Plan association is incomplete.");
        var transforms = new long[source.Transforms.Count];
        for (var slot = 0; slot < transforms.Length; ++slot)
        {
            guard(); transforms[slot] = source.Transforms[slot];
            if (transforms[slot] == 0 || source.Moves[slot].Reference != plan.Rows[slot].MoveReference
                || plan.Rows[slot].Ordinal != slot)
                throw new InvalidDataException("The original Canvas slot, move and Transform association is inconsistent.");
        }
        var eases = new SeekCanvasSourceEase[source.Eases.Count];
        for (var slot = 0; slot < eases.Length; ++slot)
        {
            guard(); var row = source.Eases[slot];
            if (row is null || row.Reference == 0 || row.Progress == 0 || row.Code != slot
                || row.Samples is null || row.Samples.Count != 101)
                throw new InvalidDataException("The complete initialized Ease row is absent or has another identity.");
            var samples = new float[101];
            for (var index = 0; index < samples.Length; ++index)
            {
                guard(); samples[index] = row.Samples[index];
                if (!float.IsFinite(samples[index]))
                    throw new InvalidDataException("The initialized Ease row contains a nonfinite Single sample.");
            }
            eases[slot] = row with { Samples = Array.AsReadOnly(samples) };
        }
        guard();
        var identity = source.Identity with { Notes = source.Identity.Notes with
        { Chart = source.Identity.Notes.Chart with
        { Run = source.Identity.Notes.Chart.Run with { } } } };
        return new(capture, plan, identity, source.TransformsArray, transforms, source.EaseArray, eases);
    }

    internal void VerifyAssociation(SeekLineRingSourceIdentity line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (!ReferenceEquals(line.CanvasCapture, CanvasCapture)
            || !ReferenceEquals(line.CanvasPlan, CanvasPlan)
            || line.Notes != SourceIdentity.Notes
            || line.Information != SourceIdentity.Information || line.Canvas != SourceIdentity.Canvas)
            throw new InvalidOperationException("The line/ring source has another original Canvas capture, Plan or NoteView identity.");
    }

    internal long TransformAt(int canvasSlot)
    {
        if ((uint)canvasSlot >= (uint)TransformReferences.Count)
            throw new ArgumentOutOfRangeException(nameof(canvasSlot));
        return TransformReferences[canvasSlot];
    }

    /// <summary>GetEaseWithIndex's initialized-table path, with no interpolation
    /// or numerical clamp. A missing type is rejected before indexing.</summary>
    internal float EaseAtIndex(int index, int type)
    {
        if ((uint)type >= (uint)Eases.Count) throw new InvalidDataException("The selected Ease type is not initialized.");
        var samples = Eases[type].Samples;
        return samples[index <= 0 ? 0 : index >= 100 ? 100 : index];
    }

    /// <summary>GetEaseWithProgress's finite, representable Single path over
    /// the same owned samples. Caller-specific Clamp01 remains separate.</summary>
    internal float EaseAtProgress(float progress, int type)
    {
        if ((uint)type >= (uint)Eases.Count) throw new InvalidDataException("The selected Ease type is not initialized.");
        var samples = Eases[type].Samples;
        var scaled = Finite(progress * 100f);
        if ((double)scaled < int.MinValue || (double)scaled > int.MaxValue)
            throw new NotSupportedException("The Ease index is not representable by native Int32 conversion.");
        var index = (int)scaled;
        if (index < 0) return samples[0];
        if (index >= 100) return samples[100];
        var fraction = Finite(scaled - (float)index);
        var difference = Finite(samples[index + 1] - samples[index]);
        var product = Finite(fraction * difference);
        return Finite(product + samples[index]);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static float Finite(float value)
    {
        if (!float.IsFinite(value)) throw new InvalidDataException("The Ease Single intermediate is nonfinite.");
        return value;
    }
}
