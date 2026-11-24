using RizToolbox.Adapter.Gameplay;
using RizToolbox.Runtime;

namespace RizToolbox.Judgements.Tests.CanvasBasis;

internal static partial class NativeSeekCanvasBasisChecks
{
    private static void CacheConflictSampleCoverage()
    {
        var failures = new List<Exception>();
        foreach (var boundary in new[] { "array", "selected", "selected-inner", "logical", "snapshot-prefix", "snapshot-first" })
        foreach (var thirdState in boundary.StartsWith("snapshot", StringComparison.Ordinal) ? new[] { true } : new[] { false, true })
        {
            try
            {
                if (boundary is "array" or "selected" or "selected-inner") CacheArrayConflictSamples(boundary, thirdState);
                else if (boundary == "logical") CacheLogicalConflictSamples(thirdState);
                else CacheNestedConflictSamples(boundary);
            }
            catch (Exception error)
            { failures.Add(new InvalidOperationException("Cache observed-sample boundary: " + boundary + "/third=" + thirdState, error)); }
        }
        if (failures.Count != 0)
            throw new AggregateException("A pending cache transition must not erase another already observed physical slot's third state.", failures);
    }

    private static void CacheArrayConflictSamples(string boundary, bool thirdState)
    {
        using var f = new Fixture(); var n = f.Native; var basis = f.Prepare(); var cache = basis.OriginalCacheOwner;
        var before = cache.GetJournal(); var array = n.CanvasCacheArray(0); var armed = false; var fired = false; var samples = 0;
        n.AfterCanvasCacheObservedStore = row =>
        { if (row.Kind == SeekCanvasCacheKind.FloorPositions && row.Index == 0) armed = true; };
        n.AfterGeometryArrayExport = (name, pointer) =>
        {
            if (!armed || fired || name != "size" || pointer != array || InArrayWriter()
                || !Environment.StackTrace.Contains("ReadRootedGeometrySingleArray", StringComparison.Ordinal)) return;
            if (++samples == 1)
            {
                // This value is in the complete Marshal.Copy sample, beyond
                // the first differing slot of the subsequent raw-bit pass.
                if (thirdState) n.CanvasCacheSetBits(0, 2, Bits(77));
            }
            else if (boundary == "array" && samples == 2)
            {
                n.CanvasCacheSetBits(0, 0, before.Baseline[0]);
                if (thirdState) n.CanvasCacheSetBits(0, 2, before.Baseline[2]);
                fired = true;
            }
            else if (boundary == "selected" && samples == 3)
                n.CanvasCacheSetBits(0, 0, before.Baseline[0]);
            else if (boundary == "selected-inner" && samples == 3)
            {
                if (thirdState) n.CanvasCacheSetBits(0, 2, before.Baseline[2]);
            }
            else if (boundary == "selected-inner" && samples == 4)
            { n.CanvasCacheSetBits(0, 0, before.Baseline[0]); fired = true; }
        };
        n.AfterCanvasSourceRead = (_, _) =>
        {
            // Both complete ReadOne arrays precede ReadSlot's final source
            // sweep. Its callback returns the other slot before the pair throws.
            if (boundary != "selected" || fired || samples != 4
                || !Environment.StackTrace.Contains("CacheAccess.ReadSlot", StringComparison.Ordinal)) return;
            if (thirdState) n.CanvasCacheSetBits(0, 2, before.Baseline[2]);
            fired = true;
        };
        var failure = Reject(basis.Execute); f.Clear();
        Check(Data(failure, NativeCanvasCacheConflict.Key) is NativeCanvasCacheConflict,
            "The raw array reader discarded its typed observed conflict.");
        var conflict = (NativeCanvasCacheConflict)Data(failure, NativeCanvasCacheConflict.Key)!;
        var frozenSamples = conflict.Observations.ToArray(); var journal = cache.GetJournal();
        Check(fired && samples == (boundary == "array" ? 2 : 4) && conflict.MultipleSamples && conflict.Owner == array.ToInt64() && conflict.Index == 0
            && conflict.ExpectedBits == before.Slots[0].TargetBits && conflict.ObservedBits == before.Baseline[0]
            && conflict.Observations.Count == (boundary == "array" ? 4 : boundary == "selected" ? 6 : 7)
            && conflict.Observations[0].Bits == before.Slots[0].TargetBits
            && conflict.Observations.Any(row => row.Index == 0 && row.Bits == before.Baseline[0])
            && conflict.Observations.Any(row => row.Index == 2 && row.Bits == (thirdState ? Bits(77) : before.Baseline[2]))
            && journal.AttemptedWrites == 1 && journal.Stores.Count == 1 && journal.Stores[0].NativeEntered && journal.Stores[0].NativeReturned
            && n.CanvasCacheSnapshot().SequenceEqual(before.Baseline) && journal.UnrecognizedState == thirdState
            && ReferenceEquals(Data(failure, "SeekCanvasCacheCleanupOwner"), cache)
            && ReferenceEquals(journal.SourceCapture, basis.SourceCapture),
            "The array conflict lost its complete first sample, observed prefix, exact pending owner, or third-state classification.");
        CacheConflictOutcome(f, basis, before, thirdState, expectedInverses: 0);
        Check(conflict.Observations.SequenceEqual(frozenSamples), "Later cache recovery mutated a previously published raw-bit sample list.");
    }

    private static void CacheLogicalConflictSamples(bool thirdState)
    {
        using var f = new Fixture(); var n = f.Native; var basis = f.Prepare(); var cache = basis.OriginalCacheOwner;
        var before = cache.GetJournal(); var armed = false; var fired = false; var samples = 0;
        n.AfterCanvasCacheScalar = name => { if (name == "nowTime") armed = true; };
        n.AfterCanvasSourceRead = (name, receiver) =>
        {
            if (!armed || fired || name != "nowTime" || receiver != n.GeometryCanvasControl
                || Environment.StackTrace.Contains("WriteCanvasCacheScalar", StringComparison.Ordinal)
                || !Environment.StackTrace.Contains("CacheAccess.ReadSlot", StringComparison.Ordinal)) return;
            if (++samples == 1) n.CanvasCacheSetBits(3, -1, before.Baseline[9]);
            else if (samples == 2) fired = true;
        };
        var pendingFailure = Reject(basis.Execute); f.Clear();
        Check(fired && samples == 2 && cache.GetJournal().AttemptedWrites == 10 && !cache.GetJournal().UnrecognizedState
            && Data(pendingFailure, NativeCanvasCacheConflict.Key) is NativeCanvasCacheConflict
            && n.CanvasCacheBits(3) == before.Baseline[9] && n.GeometryCalls.Count == 0,
            "The allowed scalar before/target pair did not retain its actual pending owner before the full logical observation.");

        var nowSamples = 0; var speedSamples = 0; fired = false;
        n.AfterCanvasSourceRead = (name, receiver) =>
        {
            if (fired || receiver != n.GeometryCanvasControl
                || !Environment.StackTrace.Contains("CacheAccess.Snapshot", StringComparison.Ordinal)) return;
            if (name == "nowTime" && ++nowSamples == 1)
            {
                n.CanvasCacheSetBits(3, -1, before.Slots[9].TargetBits);
                if (thirdState) n.CanvasCacheSetBits(4, -1, Bits(77));
            }
            else if (name == "speed" && ++speedSamples == 2)
            {
                if (thirdState) n.CanvasCacheSetBits(4, -1, before.Baseline[10]);
                fired = true;
            }
        };
        var failure = Reject(cache.VerifyExpectedValues); f.Clear();
        Check(Data(failure, NativeCanvasCacheConflict.Key) is NativeCanvasCacheConflict,
            "The complete logical cache snapshots discarded their typed conflict.");
        var conflict = (NativeCanvasCacheConflict)Data(failure, NativeCanvasCacheConflict.Key)!;
        Check(fired && nowSamples == 2 && speedSamples == 2 && conflict.MultipleSamples
            && conflict.Owner == n.GeometryCanvasControl.ToInt64() && conflict.Index == -1 && conflict.Field == nameof(SeekCanvasCacheKind.NowTime)
            && conflict.ExpectedBits == before.Baseline[9] && conflict.ObservedBits == before.Slots[9].TargetBits
            && conflict.Observations.Count == 22
            && conflict.Observations.Count(row => row.Index == -1 && row.Field == nameof(SeekCanvasCacheKind.Speed)
                && row.Bits == (thirdState ? Bits(77) : before.Baseline[10])) == 2
            && cache.GetJournal().UnrecognizedState == thirdState && n.CanvasCacheBits(4) == before.Baseline[10]
            && n.CanvasCacheBits(3) == before.Slots[9].TargetBits,
            "The first pending scalar difference erased another scalar's already sampled third state or rejected an allowed pair.");
        CacheConflictOutcome(f, basis, before, thirdState, expectedInverses: 10);
    }

    private static void CacheNestedConflictSamples(string boundary)
    {
        using var f = new Fixture(); var n = f.Native; var basis = f.Prepare(); var cache = basis.OriginalCacheOwner;
        var before = cache.GetJournal(); var pendingArray = n.CanvasCacheArray(1); var armed = false; var fired = false; var samples = 0;
        n.AfterCanvasCacheObservedStore = row =>
        { if (row.Kind == SeekCanvasCacheKind.FloorSpeeds && row.Index == 0) armed = true; };
        n.AfterGeometryArrayExport = (name, pointer) =>
        {
            if (!armed || fired || name != "size" || pointer != pendingArray || InArrayWriter()
                || !Environment.StackTrace.Contains("ReadRootedGeometrySingleArray", StringComparison.Ordinal)) return;
            if (++samples == 2) { fired = true; n.CanvasCacheSetBits(1, 0, before.Baseline[3]); }
        };
        Reject(basis.Execute); f.Clear();
        Check(fired && samples == 2 && !cache.GetJournal().UnrecognizedState && cache.GetJournal().AttemptedWrites == 4,
            "The nested-snapshot regression did not begin with its original recognized pending array slot.");

        samples = 0; fired = false; var firstComplete = false; var thirdSampled = false;
        if (boundary == "snapshot-first") n.CanvasCacheSetBits(4, -1, Bits(77));
        n.AfterCanvasSourceRead = (name, receiver) =>
        {
            if (boundary != "snapshot-first" || firstComplete || receiver != n.GeometryCanvasControl || name != "speed"
                || !Environment.StackTrace.Contains("CacheAccess.Snapshot", StringComparison.Ordinal)) return;
            firstComplete = true; thirdSampled = true; n.CanvasCacheSetBits(4, -1, before.Baseline[10]);
        };
        n.AfterGeometryArrayExport = (name, pointer) =>
        {
            if (fired || !Environment.StackTrace.Contains("CacheAccess.Snapshot", StringComparison.Ordinal)) return;
            if (boundary == "snapshot-prefix" && !thirdSampled && name == "size" && pointer == n.CanvasCacheArray(0))
            { thirdSampled = true; n.CanvasCacheSetBits(0, 2, Bits(77)); }
            if (name != "size" || pointer != pendingArray || (boundary == "snapshot-first" && !firstComplete)) return;
            if (++samples == 2)
            {
                n.CanvasCacheSetBits(1, 0, before.Slots[3].TargetBits);
                if (boundary == "snapshot-prefix") n.CanvasCacheSetBits(0, 2, before.Slots[2].TargetBits);
                fired = true;
            }
        };
        var failure = Reject(cache.VerifyExpectedValues); f.Clear();
        Check(Data(failure, NativeCanvasCacheConflict.Key) is NativeCanvasCacheConflict,
            "A nested array conflict lost its actual raw-bit provenance.");
        var conflict = (NativeCanvasCacheConflict)Data(failure, NativeCanvasCacheConflict.Key)!;
        Check(fired && thirdSampled && samples == 2 && conflict.Owner == pendingArray.ToInt64() && conflict.Index == 0
            && conflict.Observations.Count == (boundary == "snapshot-prefix" ? 7 : 18)
            && conflict.Observations.Any(row => row.Bits == Bits(77)
                && (boundary == "snapshot-prefix" ? row.Owner == n.CanvasCacheArray(0).ToInt64() && row.Index == 2
                    : row.Owner == n.GeometryCanvasControl.ToInt64() && row.Index == -1 && row.Field == nameof(SeekCanvasCacheKind.Speed)))
            && cache.GetJournal().UnrecognizedState && n.CanvasCacheBits(4) == before.Baseline[10],
            "A throwing inner array read erased a completed logical prefix or the preceding complete snapshot's third state.");
        CacheConflictOutcome(f, basis, before, thirdState: true, expectedInverses: 0);
    }

    private static void CacheConflictOutcome(Fixture fixture, NativeSeekCanvasBasisCommands basis,
        SeekCanvasCacheEffectsJournal before, bool thirdState, int expectedInverses)
    {
        var native = fixture.Native; var cache = basis.OriginalCacheOwner; var positions = basis.OriginalPositionOwner;
        var writes = native.CanvasCacheObservedStores.Count; var scalars = native.CanvasCacheScalarCalls.Count;
        if (thirdState)
        {
            Check(DoesNotConfirm(basis.VerifyAppliedValues), "An observed cache third state regained a combined target proof.");
            Reject(basis.RestoreSelectedValues); Reject(cache.Restore);
            Check(cache.GetJournal().UnrecognizedState && cache.GetJournal().AttemptedRestores == 0
                && native.CanvasCacheObservedStores.Count == writes && native.CanvasCacheScalarCalls.Count == scalars
                && positions.GetJournal().AttemptedWrites == 0 && positions.GetJournal().AttemptedRestores == 0
                && native.GeometryCalls.Count == 0 && !basis.GetJournal().AppliedValuesVerified && !basis.GetJournal().RestoredValuesVerified
                && cache.GetJournal().HasRetainedResponsibility && ReferenceEquals(cache.GetJournal().CleanupOwner, cache),
                "Returning every sampled cache slot to allowed bits erased sticky evidence, entered an inverse, or replaced its original owner.");
        }
        else
        {
            basis.RestoreSelectedValues(); Check(basis.VerifyRestoredValues(), "An allowed exact pending cache transition could not recover selected values.");
            Check(!cache.GetJournal().UnrecognizedState && cache.GetJournal().AttemptedRestores == expectedInverses
                && native.CanvasCacheSnapshot().SequenceEqual(before.Baseline) && native.GeometryCalls.Count == 0
                && positions.GetJournal().AttemptedWrites == 0 && positions.GetJournal().AttemptedRestores == 0
                && !cache.GetJournal().HasRetainedResponsibility,
                "An allowed pending pair became sticky, lost exact original bits, or acquired engine effects.");
        }
    }
}
