using RizToolbox.Adapter.Gameplay;

namespace RizToolbox.Judgements.Tests.LineRingSource;

internal static partial class NativeSeekLineRingSourceChecks
{
    private static void NumericBasisOriginalCopy()
    {
        IntPtr injectedProgress = IntPtr.Zero;
        using var f = new Fixture(n =>
        {
            var samples = Enumerable.Repeat(.75f, 101).ToArray();
            samples[0] = .25f; samples[1] = 1.25f;
            samples[50] = .375f; samples[51] = .875f;
            injectedProgress = n.CanvasSourceSampleArray(samples);
            n.CanvasSourceSet(n.CanvasSourceSlots(n.CanvasSourceEaseArray)[1], "progress", injectedProgress);
        });
        var n = f.Native; var aggregate = f.Prepare();
        var plan = aggregate.SourceCapture.Plan; var ranges = n.CanvasSourceRangeAttempts;
        var source = f.Capture(aggregate); var copy = source.ReadNumericBasis(); var original = source.Snapshot.Identity;
        var capturedCanvas = (SeekCanvasSourceSnapshot)Field(copy.CanvasCapture, "_baseline");
        var transformSlots = n.CanvasSourceSlots(n.GeometryCanvasArray).Select(x => x.ToInt64()).ToArray();
        var easeSlots = n.CanvasSourceSlots(n.CanvasSourceEaseArray).Select(x => x.ToInt64()).ToArray();
        Check(ReferenceEquals(copy.CanvasCapture, aggregate.SourceCapture)
            && ReferenceEquals(copy.CanvasPlan, plan) && copy.SourceIdentity.Notes == original.Notes
            && copy.SourceIdentity.Information == original.Information && copy.SourceIdentity.Canvas == original.Canvas
            && copy.TransformsArray == n.GeometryCanvasArray.ToInt64()
            && copy.EaseArray == n.CanvasSourceEaseArray.ToInt64()
            && copy.TransformReferences.SequenceEqual(transformSlots)
            && copy.Eases.Select(x => x.Reference).SequenceEqual(easeSlots)
            && copy.Eases.Select(x => x.Progress).SequenceEqual(new[] { n.CanvasSourceSamples[0].ToInt64(), injectedProgress.ToInt64() })
            && copy.Eases.Count == 2 && copy.Eases.All(x => x.Samples.Count == 101)
            && !ReferenceEquals(copy.TransformReferences, capturedCanvas.Transforms)
            && !ReferenceEquals(copy.Eases[1].Samples, capturedCanvas.Eases[1].Samples)
            && n.CanvasSourceRangeAttempts == ranges && !copy.CompleteSeekPermitted,
            "The numeric basis recaptured a source or lost an exact original Canvas/NoteView/array identity.");
        for (var slot = 0; slot < transformSlots.Length; ++slot)
            Check(copy.TransformAt(slot) == transformSlots[slot], "Canvas ordinal was inferred from a move or chart index.");
        NoNative(n, () =>
        {
            Check(Bits(copy.EaseAtIndex(-50, 1)) == Bits(.25f)
                && Bits(copy.EaseAtIndex(50, 1)) == Bits(.375f)
                && Bits(copy.EaseAtIndex(51, 1)) == Bits(.875f)
                && Bits(copy.EaseAtIndex(1000, 1)) == Bits(.75f),
                "GetEaseWithIndex was replaced by interpolation or Clamp01.");
            Check(Bits(copy.EaseAtProgress(-.005f, 1)) == Bits(-.25f)
                && Bits(copy.EaseAtProgress(.505f, 1)) == Bits(.625f)
                && Bits(copy.EaseAtProgress(1f, 1)) == Bits(.75f),
                "Owned progress interpolation lost native truncation, Single rounding, or the last sample.");
            Check(Reject(() => copy.EaseAtProgress(float.NaN, 1)) is InvalidDataException
                && Reject(() => copy.EaseAtProgress(float.MaxValue, 1)) is InvalidDataException
                && Reject(() => copy.EaseAtProgress(30_000_000f, 1)) is NotSupportedException
                && Reject(() => copy.EaseAtProgress(0f, -1)) is InvalidDataException
                && Reject(() => copy.EaseAtProgress(0f, 2)) is InvalidDataException,
                "Progress easing accepted a nonfinite, overflowing, unrepresentable, or absent Ease type.");
            Readonly(copy.TransformReferences, "Owned Canvas Transform slots are writable.");
            Readonly(copy.Eases, "Owned Ease rows are writable.");
            foreach (var row in copy.Eases) Readonly(row.Samples, "Owned Ease samples are writable.");
            Reject(() => copy.TransformAt(-1)); Reject(() => copy.EaseAtIndex(0, 2));
            Reject(() => copy.VerifyAssociation(original with { Notes = original.Notes with { NoteControl = -1 } }));
            Reject(() => copy.VerifyAssociation(original with { Canvas = -1 }));
        });
        NoWrites(n, "Owned numeric basis dispatched an initializer, producer or setter.");
    }

    private static void NumericBasisAfterAggregateDisposal()
    {
        using var f = new Fixture(); var n = f.Native; var aggregate = f.Prepare();
        var source = f.Capture(aggregate); var expectedCapture = aggregate.SourceCapture;
        var ranges = n.CanvasSourceRangeAttempts;
        f.Run.Dispose(); var roots = n.CanvasBasisCurrentRoots(); aggregate.Dispose();
        OriginalRootsRemain(n, roots, "Aggregate/Run disposal consumed the line reader's independent Canvas hold.");
        // First public numeric read happens only after both outer owners were
        // logically disposed. The already owned copy is admitted through the
        // original line reader, not fetched anew from CanvasCapture._baseline.
        var copy = source.ReadNumericBasis();
        Check(ReferenceEquals(copy.CanvasCapture, expectedCapture)
            && ReferenceEquals(copy.CanvasPlan, source.Snapshot.Identity.CanvasPlan)
            && copy.TransformReferences.Count == copy.CanvasPlan.Rows.Count
            && n.CanvasSourceRangeAttempts == ranges && source.GetJournal().SourceVerified
            && !source.GetJournal().BaselineVerified,
            "The independent line hold could not first read its original owned numeric basis after outer disposal.");
        source.Dispose();
        NoNative(n, () => Reject(() => source.ReadNumericBasis()));
        Check(n.ActiveRoots == 0, "Final line hold did not release the exact original graph once.");
    }

    private static void NumericBasisGuards()
    {
        using var f = new Fixture(); var n = f.Native; var source = f.Capture();
        var copy = source.ReadNumericBasis(); var originalSample = Bits(copy.EaseAtIndex(50, 1));
        NoNative(n, () => Task.Run(() => Reject(() => source.ReadNumericBasis())).GetAwaiter().GetResult());
        var fired = false;
        n.AfterCanvasSourceRead = (name, receiver) =>
        {
            if (fired || receiver != n.LineRingControl || name != "_nowTime") return;
            fired = true; NoNative(n, () => Reject(() => source.ReadNumericBasis()));
        };
        Check(ReferenceEquals(source.ReadNumericBasis(), copy) && fired,
            "Numeric basis reentry was not denied inside the existing line source operation.");
        f.Clear();
        n.LineRingSet(n.LineRingControl, "_speed", 99f);
        Check(ReferenceEquals(source.ReadNumericBasis(), copy) && source.GetJournal().SourceVerified
            && !source.GetJournal().BaselineVerified,
            "A baseline scalar change was confused with original source drift or verified as an old baseline.");
        Reject(source.VerifyBaseline);
        var ease = n.CanvasSourceSlots(n.CanvasSourceEaseArray)[1];
        var replacement = n.CanvasSourceSampleArray(Enumerable.Repeat(.125f, 101).ToArray());
        n.CanvasSourceSet(ease, "progress", replacement);
        Reject(() => source.ReadNumericBasis());
        Check(Bits(copy.EaseAtIndex(50, 1)) == originalSample && !source.GetJournal().SourceVerified,
            "Fresh source failure modified an already owned sample or preserved stale source proof.");
        NoNative(n, () => Reject(() => source.ReadNumericBasis()));
    }
}
